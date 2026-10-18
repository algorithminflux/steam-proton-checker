using System.Net;
using SteamProtonChecker.ApiClients;
using SteamProtonChecker.Tests.TestHelpers;

namespace SteamProtonChecker.Tests.ApiClients;

public class SteamStoreServiceTests
{
    // Shape of the IStoreBrowseService/GetItems response as understood from community
    // API references. If Steam changes it, these tests (and the parser) are what to update.
    private const string SampleResponse = """
        {"response":{"store_items":[
          {"item_type":0,"id":10,"success":1,"visible":true,"name":"Alpha","appid":10,
           "release":{"steam_release_date":1500000100,"original_release_date":1500000000},
           "platforms":{"windows":true,"mac":false,"steamos_linux":true}},
          {"item_type":0,"id":20,"success":1,"name":"Beta","appid":20,
           "release":{"steam_release_date":1400000000},
           "platforms":{"windows":true,"mac":false,"steamos_linux":false}},
          {"item_type":0,"id":30,"success":2}
        ]}}
        """;

    private static SteamStoreService Service(FakeHttpMessageHandler handler, string country = "ca") =>
        new(new HttpClient(handler), "test-key", country);

    [Fact]
    public async Task GetItems_ParsesReleaseDatesAndNativeLinuxSupport()
    {
        var handler = FakeHttpMessageHandler.Returning(SampleResponse);

        var items = await Service(handler).GetItemsAsync(new[] { 10, 20 });

        Assert.Equal(2, items.Count);

        var alpha = items.Single(i => i.AppId == 10);
        Assert.True(alpha.Found);
        Assert.Equal("Alpha", alpha.Name);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_500_000_000), alpha.ReleaseDate);   // original date preferred
        Assert.True(alpha.HasNativeLinux);

        var beta = items.Single(i => i.AppId == 20);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_400_000_000), beta.ReleaseDate);    // falls back to the Steam date
        Assert.False(beta.HasNativeLinux);
    }

    [Fact]
    public async Task GetItems_ReturnsFoundFalseForUnresolvedAndMissingApps()
    {
        var handler = FakeHttpMessageHandler.Returning(SampleResponse);

        var items = await Service(handler).GetItemsAsync(new[] { 10, 30, 99 });

        Assert.Equal(new[] { 10, 30, 99 }, items.Select(i => i.AppId));
        Assert.True(items[0].Found);
        Assert.False(items[1].Found);   // success != 1
        Assert.False(items[2].Found);   // absent from the response
    }

    [Fact]
    public async Task GetItems_RequestsReleaseAndPlatformData_WithKeyAndUppercaseRegion()
    {
        var handler = FakeHttpMessageHandler.Returning(SampleResponse);

        await Service(handler, "ca").GetItemsAsync(new[] { 10 });

        var url = Assert.Single(handler.RequestedUrls);
        Assert.Contains("key=test-key", url);
        Assert.Contains("include_release", url);
        Assert.Contains("include_platforms", url);
        Assert.Contains("CA", url);
    }

    [Fact]
    public async Task GetItems_WithNoIds_DoesNotCallSteam()
    {
        var handler = FakeHttpMessageHandler.Returning(SampleResponse);

        var items = await Service(handler).GetItemsAsync(Array.Empty<int>());

        Assert.Empty(items);
        Assert.Empty(handler.RequestedUrls);
    }

    [Fact]
    public async Task GetItems_RejectsOversizedBatches()
    {
        var handler = FakeHttpMessageHandler.Returning(SampleResponse);
        var ids = Enumerable.Range(1, SteamStoreService.MaxBatchSize + 1).ToList();

        await Assert.ThrowsAsync<ArgumentException>(() => Service(handler).GetItemsAsync(ids));
    }

    [Fact]
    public async Task GetItems_HttpFailure_Throws()
    {
        var handler = FakeHttpMessageHandler.Returning("{}", HttpStatusCode.TooManyRequests);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(handler).GetItemsAsync(new[] { 10 }));

        Assert.Contains("429", ex.Message);
    }
}
