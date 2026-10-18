using Microsoft.Data.Sqlite;
using SteamProtonChecker.CoreModels;
using SteamProtonChecker.Infrastructure;

namespace SteamProtonChecker.Tests.Infrastructure;

public class GameRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"spc-test-{Guid.NewGuid():N}.db");

    private GameRepository NewRepo() => new(_dbPath);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public void UpsertGames_RoundTripsAccountFields()
    {
        var repo = NewRepo();
        var played = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var added = DateTimeOffset.FromUnixTimeSeconds(1_600_000_000);

        repo.UpsertGames(new[]
        {
            new CheckedGame
            {
                AppId = 10, Name = "Alpha", IsOwned = true, PlaytimeMinutes = 90,
                LastPlayed = played, DateAdded = added,
            },
            new CheckedGame
            {
                AppId = 30, Name = "Gamma", IsFamilyShared = true, FamilyMembers = "Sam, Alex",
            },
        });

        var games = repo.GetAllGames().ToDictionary(g => g.AppId);

        Assert.Equal(2, games.Count);
        Assert.True(games[10].IsOwned);
        Assert.Equal(90, games[10].PlaytimeMinutes);
        Assert.Equal(played, games[10].LastPlayed);
        Assert.Equal(added, games[10].DateAdded);
        Assert.True(games[30].IsFamilyShared);
        Assert.Equal("Sam, Alex", games[30].FamilyMembers);
        Assert.Null(games[30].LastPlayed);
        Assert.Equal(ProtonTier.Unrated, games[30].ProtonRating);
    }

    [Fact]
    public void UpsertGames_LeavesStoreAndProtonDataAlone()
    {
        var repo = NewRepo();
        repo.UpsertGames(new[] { new CheckedGame { AppId = 10, Name = "Alpha", IsOwned = true, PlaytimeMinutes = 10 } });

        var release = DateTimeOffset.FromUnixTimeSeconds(1_500_000_000);
        repo.SaveStoreDetails(new StoreDetails { AppId = 10, Name = "Alpha", ReleaseDate = release, HasNativeLinux = true });
        repo.SaveProtonResult(10, ProtonTier.Gold, 250);

        repo.UpsertGames(new[] { new CheckedGame { AppId = 10, Name = "Alpha", IsOwned = true, PlaytimeMinutes = 500 } });

        var game = Assert.Single(repo.GetAllGames());
        Assert.Equal(500, game.PlaytimeMinutes);
        Assert.Equal(release, game.ReleaseDate);
        Assert.True(game.HasNativeLinux);
        Assert.Equal(ProtonTier.Gold, game.ProtonRating);
        Assert.Equal(250, game.ProtonReportCount);
        Assert.NotNull(game.StoreFetchedAt);
        Assert.NotNull(game.ProtonFetchedAt);
    }

    [Fact]
    public void SaveStoreDetails_FillsPlaceholderNames_ButKeepsRealOnes()
    {
        var repo = NewRepo();
        repo.UpsertGames(new[]
        {
            new CheckedGame { AppId = 10, Name = "Alpha", IsOwned = true },
            new CheckedGame { AppId = 40, Name = "App 40", IsWishlisted = true },
        });

        repo.SaveStoreDetails(new StoreDetails { AppId = 10, Name = "Alpha (Store Title)" });
        repo.SaveStoreDetails(new StoreDetails { AppId = 40, Name = "Delta" });

        var games = repo.GetAllGames().ToDictionary(g => g.AppId);
        Assert.Equal("Alpha", games[10].Name);
        Assert.Equal("Delta", games[40].Name);
    }

    [Fact]
    public void SaveStoreDetails_ForAnUnknownStoreEntry_StillMarksItFetched()
    {
        var repo = NewRepo();
        repo.UpsertGames(new[] { new CheckedGame { AppId = 40, Name = "App 40", IsWishlisted = true } });

        repo.SaveStoreDetails(new StoreDetails { AppId = 40, Found = false });

        var game = Assert.Single(repo.GetAllGames());
        Assert.Equal("App 40", game.Name);
        Assert.Null(game.ReleaseDate);
        Assert.NotNull(game.StoreFetchedAt);
        Assert.Empty(repo.GetAppIdsNeedingStoreRefresh(TimeSpan.FromDays(14)));
    }

    [Fact]
    public void DeleteGamesWithoutSource_RemovesOnlyUnclaimedGames()
    {
        var repo = NewRepo();
        repo.UpsertGames(new[]
        {
            new CheckedGame { AppId = 10, Name = "Alpha", IsOwned = true },
            new CheckedGame { AppId = 40, Name = "Delta", IsWishlisted = true },
            new CheckedGame { AppId = 30, Name = "Gamma", IsFamilyShared = true },
            new CheckedGame { AppId = 50, Name = "Orphan" },
        });

        repo.DeleteGamesWithoutSource();

        var ids = repo.GetAllGames().Select(g => g.AppId).OrderBy(i => i).ToList();
        Assert.Equal(new[] { 10, 30, 40 }, ids);
    }

    [Fact]
    public void GetAppIdsNeedingRefresh_ReturnsNewAndStaleGamesOnly()
    {
        var repo = NewRepo();
        repo.UpsertGames(new[]
        {
            new CheckedGame { AppId = 10, Name = "Alpha", IsOwned = true },
            new CheckedGame { AppId = 20, Name = "Beta", IsOwned = true },
        });

        // Both are new: nothing has been fetched yet.
        Assert.Equal(new[] { 10, 20 }, repo.GetAppIdsNeedingStoreRefresh(TimeSpan.FromDays(14)));
        Assert.Equal(new[] { 10, 20 }, repo.GetAppIdsNeedingProtonRefresh(TimeSpan.FromDays(7)));

        repo.SaveStoreDetails(new StoreDetails { AppId = 10, Name = "Alpha" });
        repo.SaveProtonResult(10, ProtonTier.Silver, 12);

        Assert.Equal(new[] { 20 }, repo.GetAppIdsNeedingStoreRefresh(TimeSpan.FromDays(14)));
        Assert.Equal(new[] { 20 }, repo.GetAppIdsNeedingProtonRefresh(TimeSpan.FromDays(7)));

        // A negative max age puts the cutoff in the future, so everything counts as stale.
        Assert.Equal(new[] { 10, 20 }, repo.GetAppIdsNeedingStoreRefresh(TimeSpan.FromDays(-1)));
        Assert.Equal(new[] { 10, 20 }, repo.GetAppIdsNeedingProtonRefresh(TimeSpan.FromDays(-1)));
    }

    [Fact]
    public void SaveProtonResult_RoundTripsTierAndReportCount()
    {
        var repo = NewRepo();
        repo.UpsertGames(new[] { new CheckedGame { AppId = 10, Name = "Alpha", IsOwned = true } });

        repo.SaveProtonResult(10, ProtonTier.Borked, null);

        var game = Assert.Single(repo.GetAllGames());
        Assert.Equal(ProtonTier.Borked, game.ProtonRating);
        Assert.Null(game.ProtonReportCount);
    }

    [Fact]
    public void ANewRepositoryOnTheSameFileSeesExistingData()
    {
        NewRepo().UpsertGames(new[] { new CheckedGame { AppId = 10, Name = "Alpha", IsOwned = true } });

        var reopened = NewRepo();

        Assert.Equal("Alpha", Assert.Single(reopened.GetAllGames()).Name);
    }
}
