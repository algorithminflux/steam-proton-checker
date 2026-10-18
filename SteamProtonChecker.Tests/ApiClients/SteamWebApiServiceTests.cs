using System.Net;
using SteamProtonChecker.ApiClients;
using SteamProtonChecker.Tests.TestHelpers;

namespace SteamProtonChecker.Tests.ApiClients;

public class SteamWebApiServiceTests
{
    private static SteamWebApiService Service(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler), "test-key");

    [Fact]
    public async Task GetOwnedGames_ParsesPlaytimeAndLastPlayed_AndTreatsZeroAsNeverPlayed()
    {
        var handler = FakeHttpMessageHandler.Returning("""
            {"response":{"game_count":2,"games":[
              {"appid":10,"name":"Alpha","playtime_forever":120,"rtime_last_played":1700000000},
              {"appid":20,"name":"Beta","playtime_forever":0,"rtime_last_played":0}
            ]}}
            """);

        var games = await Service(handler).GetOwnedGamesAsync("76561198000000000");

        Assert.Equal(2, games.Count);
        Assert.Equal("Alpha", games[0].Name);
        Assert.Equal(120, games[0].PlaytimeMinutes);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), games[0].LastPlayed);
        Assert.True(games[0].IsOwned);
        Assert.Null(games[1].LastPlayed);
    }

    [Fact]
    public async Task GetOwnedGames_SendsTheKeyAndSteamId()
    {
        var handler = FakeHttpMessageHandler.Returning("""{"response":{"game_count":0}}""");

        await Service(handler).GetOwnedGamesAsync("76561198000000000");

        var url = Assert.Single(handler.RequestedUrls);
        Assert.Contains("key=test-key", url);
        Assert.Contains("steamid=76561198000000000", url);
    }

    [Fact]
    public async Task GetOwnedGames_UsesAPlaceholderWhenTheNameIsMissing()
    {
        var handler = FakeHttpMessageHandler.Returning("""{"response":{"games":[{"appid":77}]}}""");

        var games = await Service(handler).GetOwnedGamesAsync("76561198000000000");

        Assert.Equal("App 77", Assert.Single(games).Name);
    }

    [Fact]
    public async Task GetOwnedGames_PublicAccountWithNoGames_ReturnsEmpty()
    {
        var handler = FakeHttpMessageHandler.Returning("""{"response":{"game_count":0}}""");

        var games = await Service(handler).GetOwnedGamesAsync("76561198000000000");

        Assert.Empty(games);
    }

    [Fact]
    public async Task GetOwnedGames_PrivateProfile_ExplainsHowToFixIt()
    {
        var handler = FakeHttpMessageHandler.Returning("""{"response":{}}""");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(handler).GetOwnedGamesAsync("76561198000000001"));

        Assert.Contains("76561198000000001", ex.Message);
        Assert.Contains("Public", ex.Message);
    }

    [Fact]
    public async Task GetOwnedGames_HttpFailure_Throws()
    {
        var handler = FakeHttpMessageHandler.Returning("{}", HttpStatusCode.Forbidden);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(handler).GetOwnedGamesAsync("76561198000000000"));

        Assert.Contains("403", ex.Message);
    }

    [Fact]
    public async Task GetPersonaNames_MapsSteamIdsToNames()
    {
        var handler = FakeHttpMessageHandler.Returning("""
            {"response":{"players":[
              {"steamid":"76561198000000001","personaname":"Sam"},
              {"steamid":"76561198000000002","personaname":"Alex"}
            ]}}
            """);

        var names = await Service(handler).GetPersonaNamesAsync(
            new[] { "76561198000000001", "76561198000000002", "76561198000000001" });

        Assert.Equal("Sam", names["76561198000000001"]);
        Assert.Equal("Alex", names["76561198000000002"]);
        Assert.Contains("steamids=76561198000000001,76561198000000002", Assert.Single(handler.RequestedUrls));
    }

    [Fact]
    public async Task GetPersonaNames_IsBestEffort_AndNeverThrowsOnFailure()
    {
        var handler = FakeHttpMessageHandler.Returning("nope", HttpStatusCode.InternalServerError);

        var names = await Service(handler).GetPersonaNamesAsync(new[] { "76561198000000001" });

        Assert.Empty(names);
    }
}
