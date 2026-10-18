using System.Net;
using SteamProtonChecker.ApiClients;
using SteamProtonChecker.CoreModels;
using SteamProtonChecker.Tests.TestHelpers;

namespace SteamProtonChecker.Tests.ApiClients;

public class ProtonDbServiceTests
{
    private static ProtonDbService Service(FakeHttpMessageHandler handler) => new(new HttpClient(handler));

    [Fact]
    public async Task GetSummary_ParsesTierAndReportCount()
    {
        var handler = FakeHttpMessageHandler.Returning("""
            {"bestReportedTier":"platinum","confidence":"strong","score":0.77,"tier":"gold","total":1234,"trendingTier":"gold"}
            """);

        var result = await Service(handler).GetSummaryAsync(620);

        Assert.NotNull(result);
        Assert.Equal(ProtonTier.Gold, result.Tier);
        Assert.Equal(1234, result.ReportCount);
        Assert.EndsWith("/api/v1/reports/summaries/620.json", Assert.Single(handler.RequestedUrls));
    }

    [Fact]
    public async Task GetSummary_NotFound_MeansNoReports()
    {
        var handler = FakeHttpMessageHandler.Returning("", HttpStatusCode.NotFound);

        var result = await Service(handler).GetSummaryAsync(620);

        Assert.NotNull(result);
        Assert.Equal(ProtonTier.Unrated, result.Tier);
        Assert.Equal(0, result.ReportCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetSummary_OtherFailures_ReturnNullSoTheyCanBeRetried(HttpStatusCode status)
    {
        var handler = FakeHttpMessageHandler.Returning("", status);

        Assert.Null(await Service(handler).GetSummaryAsync(620));
    }

    [Fact]
    public async Task GetSummary_MalformedJson_ReturnsNull()
    {
        var handler = FakeHttpMessageHandler.Returning("<html>not json</html>");

        Assert.Null(await Service(handler).GetSummaryAsync(620));
    }

    [Fact]
    public async Task GetSummary_UnknownTierName_IsUnrated()
    {
        var handler = FakeHttpMessageHandler.Returning("""{"tier":"diamond","total":5}""");

        var result = await Service(handler).GetSummaryAsync(620);

        Assert.NotNull(result);
        Assert.Equal(ProtonTier.Unrated, result.Tier);
        Assert.Equal(5, result.ReportCount);
    }

    [Fact]
    public async Task GetSummary_MissingFields_DefaultToUnratedWithNoCount()
    {
        var handler = FakeHttpMessageHandler.Returning("{}");

        var result = await Service(handler).GetSummaryAsync(620);

        Assert.NotNull(result);
        Assert.Equal(ProtonTier.Unrated, result.Tier);
        Assert.Null(result.ReportCount);
    }
}
