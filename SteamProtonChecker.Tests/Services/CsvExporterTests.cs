using SteamProtonChecker.CoreModels;
using SteamProtonChecker.Services;

namespace SteamProtonChecker.Tests.Services;

public class CsvExporterTests
{
    private static string[] Lines(string csv) =>
        csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Export_EmptyList_WritesOnlyHeader()
    {
        var lines = Lines(CsvExporter.Export(Array.Empty<CheckedGame>()));

        Assert.Single(lines);
        Assert.Equal(string.Join(",", CsvExporter.Header), lines[0]);
    }

    [Fact]
    public void Export_OwnedGame_WritesAllColumns()
    {
        var game = new CheckedGame
        {
            AppId = 620,
            Name = "Portal 2",
            IsOwned = true,
            PlaytimeMinutes = 90,
            LastPlayed = new DateTimeOffset(2025, 3, 4, 12, 0, 0, TimeSpan.Zero),
            ReleaseDate = new DateTimeOffset(2011, 4, 19, 12, 0, 0, TimeSpan.Zero),
            ProtonRating = ProtonTier.Platinum,
            ProtonReportCount = 1234,
        };

        var row = Lines(CsvExporter.Export(new[] { game }))[1];

        Assert.Equal("620,Portal 2,Platinum,1234,No,Library,,1.5,2025-03-04,2011-04-19,", row);
    }

    [Fact]
    public void Export_WishlistGame_LeavesPlayHistoryBlank()
    {
        var game = new CheckedGame
        {
            AppId = 1,
            Name = "Wished",
            IsWishlisted = true,
            PlaytimeMinutes = 500,
            LastPlayed = DateTimeOffset.UtcNow,
        };

        var cells = Lines(CsvExporter.Export(new[] { game }))[1].Split(',');

        Assert.Equal("", cells[7]);
        Assert.Equal("", cells[8]);
    }

    [Fact]
    public void Export_NativeLinuxWithoutVerdict_ReportsNativeTier()
    {
        var game = new CheckedGame { AppId = 2, Name = "N", IsOwned = true, HasNativeLinux = true };

        var cells = Lines(CsvExporter.Export(new[] { game }))[1].Split(',');

        Assert.Equal("Native", cells[2]);
        Assert.Equal("Yes", cells[4]);
    }

    [Fact]
    public void Export_QuotesCommasQuotesAndNewlines()
    {
        var game = new CheckedGame { AppId = 3, Name = "A, \"B\"\nC", IsOwned = true };

        var csv = CsvExporter.Export(new[] { game });

        Assert.Contains("\"A, \"\"B\"\"\nC\"", csv);
    }

    [Theory]
    [InlineData("=SUM(A1)")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("@cmd")]
    public void Export_NeutralisesFormulaNames(string name)
    {
        var game = new CheckedGame { AppId = 4, Name = name, IsOwned = true };

        var cells = Lines(CsvExporter.Export(new[] { game }))[1].Split(',');

        Assert.StartsWith("'", cells[1]);
    }

    [Fact]
    public void Export_FamilyGame_ListsMembersAndSource()
    {
        var game = new CheckedGame { AppId = 5, Name = "F", IsFamilyShared = true, FamilyMembers = "Ann, Bob" };

        var csv = CsvExporter.Export(new[] { game });

        Assert.Contains("\"Family (Ann, Bob)\",\"Ann, Bob\"", csv);
    }
}
