using System.Net;
using SteamProtonChecker.ApiClients;
using SteamProtonChecker.Tests.TestHelpers;

namespace SteamProtonChecker.Tests.ApiClients;

public class SteamWishlistServiceTests
{
    [Fact]
    public async Task GetWishlistAppIds_ReturnsDistinctIds()
    {
        var handler = FakeHttpMessageHandler.Returning("""
            {"response":{"items":[{"appid":40,"priority":1},{"appid":50},{"appid":40}]}}
            """);

        var ids = await new SteamWishlistService(new HttpClient(handler)).GetWishlistAppIdsAsync("76561198000000000");

        Assert.Equal(new[] { 40, 50 }, ids);
        Assert.Contains("steamid=76561198000000000", Assert.Single(handler.RequestedUrls));
    }

    [Fact]
    public async Task GetWishlistAppIds_EmptyWishlist_ReturnsEmpty()
    {
        var handler = FakeHttpMessageHandler.Returning("""{"response":{}}""");

        var ids = await new SteamWishlistService(new HttpClient(handler)).GetWishlistAppIdsAsync("76561198000000000");

        Assert.Empty(ids);
    }

    [Fact]
    public async Task GetWishlistAppIds_HttpFailure_ExplainsPrivacySetting()
    {
        var handler = FakeHttpMessageHandler.Returning("{}", HttpStatusCode.Forbidden);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SteamWishlistService(new HttpClient(handler)).GetWishlistAppIdsAsync("76561198000000000"));

        Assert.Contains("Public", ex.Message);
    }
}
