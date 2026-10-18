using System.Net;
using SteamProtonChecker.ApiClients;
using SteamProtonChecker.Tests.TestHelpers;

namespace SteamProtonChecker.Tests.ApiClients;

public class SteamFamilyServiceTests
{
    private const string MyId = "76561198000000000";

    [Theory]
    [InlineData("abc123", "abc123")]
    [InlineData("  abc123  ", "abc123")]
    [InlineData("\"abc123\"", "abc123")]
    [InlineData("""{"data":{"webapi_token":"abc123","other":1}}""", "abc123")]
    [InlineData("""{"webapi_token":"abc123"}""", "abc123")]
    [InlineData("""{"data":{}}""", "")]
    [InlineData("{not json", "")]
    public void NormalizeToken_AcceptsTheBareTokenOrTheFullJson(string input, string expected)
    {
        Assert.Equal(expected, SteamFamilyService.NormalizeToken(input));
    }

    private static FakeHttpMessageHandler FamilyHandler(string groupBody, string appsBody) =>
        new(req =>
        {
            var url = req.RequestUri!.AbsoluteUri;
            return url.Contains("GetFamilyGroupForUser")
                ? FakeHttpMessageHandler.Json(groupBody)
                : FakeHttpMessageHandler.Json(appsBody);
        });

    [Fact]
    public async Task GetSharedLibrary_ReadsAppsOwnersAndAcquisitionDates()
    {
        var handler = FamilyHandler(
            """{"response":{"family_groupid":"123456789012"}}""",
            """
            {"response":{"apps":[
              {"appid":10,"name":"Alpha","owner_steamids":["76561198000000000"],"rt_time_acquired":1600000000},
              {"appid":30,"name":"Gamma","owner_steamids":[76561198000000001,"76561198000000002"]},
              {"appid":40,"owner_steamids":[]}
            ]}}
            """);

        var apps = await new SteamFamilyService(new HttpClient(handler)).GetSharedLibraryAsync("tok123", MyId);

        Assert.Equal(3, apps.Count);

        Assert.Equal("Alpha", apps[0].Name);
        Assert.Equal(new[] { MyId }, apps[0].OwnerSteamIds);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_600_000_000), apps[0].AcquiredAt);

        // Owner IDs may be JSON numbers or strings.
        Assert.Equal(new[] { "76561198000000001", "76561198000000002" }, apps[1].OwnerSteamIds);
        Assert.Null(apps[1].AcquiredAt);

        Assert.Equal("App 40", apps[2].Name);
    }

    [Fact]
    public async Task GetSharedLibrary_SendsTheTokenAndFamilyGroup_AndNeverTheApiKey()
    {
        var handler = FamilyHandler(
            """{"response":{"family_groupid":"555"}}""",
            """{"response":{"apps":[]}}""");

        await new SteamFamilyService(new HttpClient(handler)).GetSharedLibraryAsync(
            """{"data":{"webapi_token":"tok123"}}""", MyId);

        Assert.Equal(2, handler.RequestedUrls.Count);
        Assert.Contains("access_token=tok123", handler.RequestedUrls[0]);
        Assert.Contains($"steamid={MyId}", handler.RequestedUrls[0]);
        Assert.Contains("access_token=tok123", handler.RequestedUrls[1]);
        Assert.Contains("family_groupid=555", handler.RequestedUrls[1]);
        Assert.Contains("include_own=true", handler.RequestedUrls[1]);
        Assert.DoesNotContain("key=", handler.RequestedUrls[1]);
    }

    [Fact]
    public async Task GetSharedLibrary_NumericGroupIdIsAccepted()
    {
        var handler = FamilyHandler(
            """{"response":{"family_groupid":123456789012}}""",
            """{"response":{"apps":[]}}""");

        await new SteamFamilyService(new HttpClient(handler)).GetSharedLibraryAsync("tok", MyId);

        Assert.Contains("family_groupid=123456789012", handler.RequestedUrls[1]);
    }

    [Fact]
    public async Task GetSharedLibrary_NoFamilyGroup_ExplainsWhy()
    {
        var handler = FamilyHandler("""{"response":{}}""", "{}");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SteamFamilyService(new HttpClient(handler)).GetSharedLibraryAsync("tok", MyId));

        Assert.Contains("Family group", ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetSharedLibrary_RejectedToken_SaysItExpired(HttpStatusCode status)
    {
        var handler = FakeHttpMessageHandler.Returning("{}", status);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SteamFamilyService(new HttpClient(handler)).GetSharedLibraryAsync("tok", MyId));

        Assert.Contains("token", ex.Message);
    }

    [Fact]
    public async Task GetSharedLibrary_UnreadableToken_ThrowsBeforeAnyRequest()
    {
        var handler = FakeHttpMessageHandler.Returning("{}");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SteamFamilyService(new HttpClient(handler)).GetSharedLibraryAsync("{not json", MyId));

        Assert.Empty(handler.RequestedUrls);
    }
}
