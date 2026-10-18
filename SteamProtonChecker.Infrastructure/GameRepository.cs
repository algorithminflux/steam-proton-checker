using System.IO;
using Microsoft.Data.Sqlite;
using SteamProtonChecker.CoreModels;

namespace SteamProtonChecker.Infrastructure;

/// <summary>
/// Local SQLite cache of every game, its store data and its ProtonDB rating, so
/// re-syncing only fetches what is new or stale.
/// </summary>
public class GameRepository : IGameRepository
{
    private readonly string _connectionString;

    public GameRepository(string? dbPathOverride = null)
    {
        string dbPath;
        if (dbPathOverride is not null)
        {
            dbPath = dbPathOverride;
        }
        else
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SteamProtonChecker");
            Directory.CreateDirectory(dir);
            dbPath = Path.Combine(dir, "library.db");
        }

        _connectionString = $"Data Source={dbPath}";
        Initialize();
    }

    public void UpsertGames(IEnumerable<CheckedGame> games)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        foreach (var game in games)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                              INSERT INTO games (app_id, name, playtime_minutes, last_played_unix, date_added_unix,
                                                 is_owned, is_wishlisted, is_family, family_members)
                              VALUES ($appId, $name, $playtime, $lastPlayed, $dateAdded,
                                      $isOwned, $isWishlisted, $isFamily, $familyMembers)
                              ON CONFLICT(app_id) DO UPDATE SET
                                  name = excluded.name,
                                  playtime_minutes = excluded.playtime_minutes,
                                  last_played_unix = excluded.last_played_unix,
                                  date_added_unix = excluded.date_added_unix,
                                  is_owned = excluded.is_owned,
                                  is_wishlisted = excluded.is_wishlisted,
                                  is_family = excluded.is_family,
                                  family_members = excluded.family_members;
                              """;
            cmd.Parameters.AddWithValue("$appId", game.AppId);
            cmd.Parameters.AddWithValue("$name", game.Name);
            cmd.Parameters.AddWithValue("$playtime", game.PlaytimeMinutes);
            cmd.Parameters.AddWithValue("$lastPlayed", (object?)game.LastPlayed?.ToUnixTimeSeconds() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$dateAdded", (object?)game.DateAdded?.ToUnixTimeSeconds() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$isOwned", game.IsOwned ? 1 : 0);
            cmd.Parameters.AddWithValue("$isWishlisted", game.IsWishlisted ? 1 : 0);
            cmd.Parameters.AddWithValue("$isFamily", game.IsFamilyShared ? 1 : 0);
            cmd.Parameters.AddWithValue("$familyMembers", game.FamilyMembers ?? string.Empty);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public void DeleteGamesWithoutSource()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM games WHERE is_owned = 0 AND is_wishlisted = 0 AND is_family = 0;";
        cmd.ExecuteNonQuery();
    }

    public void SaveStoreDetails(StoreDetails details)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        // Only a placeholder name ("App 123") is replaced; a real name from the library is kept.
        cmd.CommandText = """
                          UPDATE games SET
                              name = CASE WHEN name = ('App ' || app_id) AND $name IS NOT NULL THEN $name ELSE name END,
                              release_unix = $release,
                              has_native_linux = $native,
                              store_fetched_at_unix = $fetchedAt
                          WHERE app_id = $appId;
                          """;
        cmd.Parameters.AddWithValue("$name", (object?)details.Name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$release", (object?)details.ReleaseDate?.ToUnixTimeSeconds() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$native", details.HasNativeLinux ? 1 : 0);
        cmd.Parameters.AddWithValue("$fetchedAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$appId", details.AppId);
        cmd.ExecuteNonQuery();
    }

    public void SaveProtonResult(int appId, ProtonTier tier, int? reportCount)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          UPDATE games SET
                              proton_tier = $tier,
                              proton_reports = $reports,
                              proton_fetched_at_unix = $fetchedAt
                          WHERE app_id = $appId;
                          """;
        cmd.Parameters.AddWithValue("$tier", (int)tier);
        cmd.Parameters.AddWithValue("$reports", (object?)reportCount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fetchedAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$appId", appId);
        cmd.ExecuteNonQuery();
    }

    public List<CheckedGame> GetAllGames()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM games ORDER BY name COLLATE NOCASE;";
        using var reader = cmd.ExecuteReader();

        var games = new List<CheckedGame>();
        while (reader.Read())
        {
            games.Add(ReadGame(reader));
        }

        return games;
    }

    public List<int> GetAppIdsNeedingStoreRefresh(TimeSpan maxAge) =>
        GetAppIdsOlderThan("store_fetched_at_unix", maxAge);

    public List<int> GetAppIdsNeedingProtonRefresh(TimeSpan maxAge) =>
        GetAppIdsOlderThan("proton_fetched_at_unix", maxAge);

    // ---------- Internals ----------

    private List<int> GetAppIdsOlderThan(string column, TimeSpan maxAge)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(maxAge).ToUnixTimeSeconds();
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        // The column name is one of two constants from this class, never user input.
        cmd.CommandText = $"SELECT app_id FROM games WHERE {column} IS NULL OR {column} < $cutoff ORDER BY app_id;";
        cmd.Parameters.AddWithValue("$cutoff", cutoff);
        using var reader = cmd.ExecuteReader();

        var ids = new List<int>();
        while (reader.Read())
        {
            ids.Add(reader.GetInt32(0));
        }

        return ids;
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private void Initialize()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
                           CREATE TABLE IF NOT EXISTS games (
                               app_id INTEGER PRIMARY KEY,
                               name TEXT NOT NULL,
                               playtime_minutes INTEGER NOT NULL DEFAULT 0,
                               last_played_unix INTEGER NULL,
                               date_added_unix INTEGER NULL,
                               release_unix INTEGER NULL,
                               is_owned INTEGER NOT NULL DEFAULT 0,
                               is_wishlisted INTEGER NOT NULL DEFAULT 0,
                               is_family INTEGER NOT NULL DEFAULT 0,
                               family_members TEXT NOT NULL DEFAULT '',
                               has_native_linux INTEGER NOT NULL DEFAULT 0,
                               proton_tier INTEGER NOT NULL DEFAULT {(int)ProtonTier.Unrated},
                               proton_reports INTEGER NULL,
                               store_fetched_at_unix INTEGER NULL,
                               proton_fetched_at_unix INTEGER NULL
                           );
                           """;
        cmd.ExecuteNonQuery();
    }

    private static CheckedGame ReadGame(SqliteDataReader reader)
    {
        return new CheckedGame
        {
            AppId = reader.GetInt32(reader.GetOrdinal("app_id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            PlaytimeMinutes = reader.GetInt32(reader.GetOrdinal("playtime_minutes")),
            LastPlayed = ReadUnix(reader, "last_played_unix"),
            DateAdded = ReadUnix(reader, "date_added_unix"),
            ReleaseDate = ReadUnix(reader, "release_unix"),
            IsOwned = reader.GetInt32(reader.GetOrdinal("is_owned")) == 1,
            IsWishlisted = reader.GetInt32(reader.GetOrdinal("is_wishlisted")) == 1,
            IsFamilyShared = reader.GetInt32(reader.GetOrdinal("is_family")) == 1,
            FamilyMembers = reader.GetString(reader.GetOrdinal("family_members")),
            HasNativeLinux = reader.GetInt32(reader.GetOrdinal("has_native_linux")) == 1,
            ProtonRating = (ProtonTier)reader.GetInt32(reader.GetOrdinal("proton_tier")),
            ProtonReportCount = ReadInt(reader, "proton_reports"),
            StoreFetchedAt = ReadUnix(reader, "store_fetched_at_unix"),
            ProtonFetchedAt = ReadUnix(reader, "proton_fetched_at_unix"),
        };
    }

    private static DateTimeOffset? ReadUnix(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(ordinal));
    }

    private static int? ReadInt(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }
}
