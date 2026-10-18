using System.Net;
using Microsoft.Data.Sqlite;
using SteamProtonChecker.CoreModels;
using SteamProtonChecker.Infrastructure;
using SteamProtonChecker.Services;
using SteamProtonChecker.Tests.TestHelpers;

namespace SteamProtonChecker.Tests.Services;

/// <summary>
/// End-to-end sync against a fake Steam/ProtonDB: library, family and wishlist go in,
/// merged and rated games come out of the SQLite cache.
/// </summary>
public class LibraryCheckerTests : IDisposable
{
    private const string MyId = "76561198000000000";
    private const string SamId = "76561198000000001";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"spc-sync-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private static AppSettings Settings() => new()
    {
        SteamApiKey = "test-key",
        SteamId64 = MyId,
        CountryCode = "ca",
        FamilyMembers = new List<FamilyMember> { new() { Name = "Sam", SteamId64 = SamId } },
    };

    private static HttpResponseMessage Ok(string body) => FakeHttpMessageHandler.Json(body);

    private static FakeHttpMessageHandler HealthySteam() => new(HealthyResponse);

    private static HttpResponseMessage HealthyResponse(HttpRequestMessage req)
    {
        var url = req.RequestUri!.AbsoluteUri;

        if (url.Contains("GetOwnedGames"))
        {
            return url.Contains($"steamid={SamId}")
                ? Ok("""
                     {"response":{"game_count":2,"games":[
                       {"appid":20,"name":"Beta","playtime_forever":5},
                       {"appid":30,"name":"Gamma","playtime_forever":900}
                     ]}}
                     """)
                : Ok("""
                     {"response":{"game_count":2,"games":[
                       {"appid":10,"name":"Alpha","playtime_forever":120,"rtime_last_played":1700000000},
                       {"appid":20,"name":"Beta","playtime_forever":0,"rtime_last_played":0}
                     ]}}
                     """);
        }

        if (url.Contains("GetWishlist"))
        {
            return Ok("""{"response":{"items":[{"appid":40}]}}""");
        }

        if (url.Contains("IStoreBrowseService"))
        {
            return Ok("""
                      {"response":{"store_items":[
                        {"id":10,"appid":10,"success":1,"name":"Alpha","release":{"original_release_date":1500000000},"platforms":{"steamos_linux":true}},
                        {"id":20,"appid":20,"success":1,"name":"Beta","release":{"original_release_date":1400000000},"platforms":{"steamos_linux":false}},
                        {"id":30,"appid":30,"success":1,"name":"Gamma","release":{"original_release_date":1300000000},"platforms":{"steamos_linux":false}},
                        {"id":40,"appid":40,"success":1,"name":"Delta","release":{"original_release_date":1900000000},"platforms":{"steamos_linux":false}}
                      ]}}
                      """);
        }

        if (url.Contains("protondb.com"))
        {
            if (url.EndsWith("/10.json")) return Ok("""{"tier":"gold","total":300}""");
            if (url.EndsWith("/20.json")) return Ok("""{"tier":"borked","total":40}""");
            if (url.EndsWith("/40.json")) return Ok("""{"tier":"silver","total":8}""");
            return FakeHttpMessageHandler.Json("", HttpStatusCode.NotFound);   // 30: no reports
        }

        return FakeHttpMessageHandler.Json("unexpected: " + url, HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Sync_MergesLibraryFamilyAndWishlist_AndRatesEveryGame()
    {
        var checker = new LibraryChecker(new GameRepository(_dbPath), new HttpClient(HealthySteam()));

        var result = await checker.SyncAsync(Settings());

        Assert.Empty(result.Warnings);
        var games = result.Games.ToDictionary(g => g.AppId);
        Assert.Equal(4, games.Count);

        // Library game: playtime, last played, release date, Linux flag and tier all present.
        var alpha = games[10];
        Assert.True(alpha.IsOwned);
        Assert.Equal(120, alpha.PlaytimeMinutes);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), alpha.LastPlayed);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_500_000_000), alpha.ReleaseDate);
        Assert.True(alpha.HasNativeLinux);
        Assert.Equal(ProtonTier.Gold, alpha.ProtonRating);
        Assert.Equal(300, alpha.ProtonReportCount);

        // Owned by both: shown once, as a library game.
        Assert.True(games[20].IsOwned);
        Assert.False(games[20].IsFamilyShared);
        Assert.Equal(ProtonTier.Borked, games[20].ProtonRating);

        // Only Sam owns it: a family game, with no ProtonDB reports.
        Assert.False(games[30].IsOwned);
        Assert.True(games[30].IsFamilyShared);
        Assert.Equal("Sam", games[30].FamilyMembers);
        Assert.Equal(ProtonTier.Unrated, games[30].EffectiveTier);

        // Wishlist game starts as "App 40" and is renamed from store data.
        Assert.True(games[40].IsWishlisted);
        Assert.Equal("Delta", games[40].Name);
        Assert.Equal(ProtonTier.Silver, games[40].ProtonRating);
    }

    [Fact]
    public async Task Sync_ASecondSyncUsesTheCacheInsteadOfRefetchingStoreAndProtonData()
    {
        var first = HealthySteam();
        await new LibraryChecker(new GameRepository(_dbPath), new HttpClient(first)).SyncAsync(Settings());

        var second = HealthySteam();
        await new LibraryChecker(new GameRepository(_dbPath), new HttpClient(second)).SyncAsync(Settings());

        Assert.Contains(first.RequestedUrls, u => u.Contains("protondb.com"));
        Assert.Contains(first.RequestedUrls, u => u.Contains("IStoreBrowseService"));
        Assert.DoesNotContain(second.RequestedUrls, u => u.Contains("protondb.com"));
        Assert.DoesNotContain(second.RequestedUrls, u => u.Contains("IStoreBrowseService"));
    }

    [Fact]
    public async Task Sync_WhenTheLibraryCallFails_WarnsAndKeepsCachedGames()
    {
        await new LibraryChecker(new GameRepository(_dbPath), new HttpClient(HealthySteam())).SyncAsync(Settings());

        var failing = new FakeHttpMessageHandler(req =>
            req.RequestUri!.AbsoluteUri.Contains("GetOwnedGames")
                ? FakeHttpMessageHandler.Json("{}", HttpStatusCode.Forbidden)
                : FakeHttpMessageHandler.Json("""{"response":{}}"""));

        var result = await new LibraryChecker(new GameRepository(_dbPath), new HttpClient(failing)).SyncAsync(Settings());

        Assert.Contains(result.Warnings, w => w.StartsWith("Library:"));
        Assert.True(result.Games.Single(g => g.AppId == 10).IsOwned);   // cached ownership survives a failed fetch
    }

    [Fact]
    public async Task Sync_APrivateFamilyProfile_WarnsAndKeepsCachedFamilyData()
    {
        await new LibraryChecker(new GameRepository(_dbPath), new HttpClient(HealthySteam())).SyncAsync(Settings());

        var samPrivate = new FakeHttpMessageHandler(req =>
        {
            var url = req.RequestUri!.AbsoluteUri;
            if (url.Contains("GetOwnedGames"))
            {
                return url.Contains($"steamid={SamId}")
                    ? Ok("""{"response":{}}""")
                    : Ok("""{"response":{"game_count":1,"games":[{"appid":10,"name":"Alpha"}]}}""");
            }

            return Ok("""{"response":{}}""");
        });

        var result = await new LibraryChecker(new GameRepository(_dbPath), new HttpClient(samPrivate)).SyncAsync(Settings());

        Assert.Contains(result.Warnings, w => w.Contains("76561198000000001"));
        Assert.Contains(result.Warnings, w => w.Contains("Cached family data was kept"));
        Assert.True(result.Games.Single(g => g.AppId == 30).IsFamilyShared);
    }

    [Fact]
    public async Task Sync_WithAWebToken_UsesTheSharedLibraryAndFillsDateAdded()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            var url = req.RequestUri!.AbsoluteUri;

            if (url.Contains("GetFamilyGroupForUser"))
            {
                return Ok("""{"response":{"family_groupid":"777"}}""");
            }

            if (url.Contains("GetSharedLibraryApps"))
            {
                return Ok("""
                          {"response":{"apps":[
                            {"appid":10,"name":"Alpha","owner_steamids":["76561198000000000"],"rt_time_acquired":1600000000},
                            {"appid":30,"name":"Gamma","owner_steamids":["76561198000000001"]}
                          ]}}
                          """);
            }

            if (url.Contains("GetPlayerSummaries"))
            {
                return Ok("""{"response":{"players":[{"steamid":"76561198000000001","personaname":"SamTheMan"}]}}""");
            }

            return HealthyResponse(req);
        });

        var settings = Settings();
        settings.FamilyMembers.Clear();
        settings.SteamWebToken = "tok123";

        var result = await new LibraryChecker(new GameRepository(_dbPath), new HttpClient(handler)).SyncAsync(settings);

        var games = result.Games.ToDictionary(g => g.AppId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_600_000_000), games[10].DateAdded);
        Assert.True(games[30].IsFamilyShared);
        Assert.Equal("SamTheMan", games[30].FamilyMembers);
        // Sam's library is never read directly when the shared library is available.
        Assert.DoesNotContain(handler.RequestedUrls, u => u.Contains("GetOwnedGames") && u.Contains($"steamid={SamId}"));
    }
}
