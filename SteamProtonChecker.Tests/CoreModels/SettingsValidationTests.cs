using SteamProtonChecker.CoreModels;

namespace SteamProtonChecker.Tests.CoreModels;

public class SettingsValidationTests
{
    [Theory]
    [InlineData("76561198000000000", true)]
    [InlineData(" 76561198000000000 ", true)]
    [InlineData("7656119800000000", false)]
    [InlineData("765611980000000000", false)]
    [InlineData("7656119800000000a", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidSteamId64_RequiresExactly17Digits(string? value, bool expected)
    {
        Assert.Equal(expected, SettingsValidation.IsValidSteamId64(value));
    }

    [Fact]
    public void AppSettings_IsConfigured_NeedsKeyAndSteamId()
    {
        Assert.False(new AppSettings().IsConfigured);
        Assert.False(new AppSettings { SteamApiKey = "k" }.IsConfigured);
        Assert.True(new AppSettings { SteamApiKey = "k", SteamId64 = "76561198000000000" }.IsConfigured);
    }

    [Fact]
    public void TryParseFamilyMembers_ParsesNamesAndIds()
    {
        var ok = SettingsValidation.TryParseFamilyMembers(
            "Sam, 76561198000000001\r\n\r\nAlex = 76561198000000002\n",
            out var members, out var error);

        Assert.True(ok);
        Assert.Equal(string.Empty, error);
        Assert.Collection(members,
            m => { Assert.Equal("Sam", m.Name); Assert.Equal("76561198000000001", m.SteamId64); },
            m => { Assert.Equal("Alex", m.Name); Assert.Equal("76561198000000002", m.SteamId64); });
    }

    [Fact]
    public void TryParseFamilyMembers_AllowsCommasInNames()
    {
        var ok = SettingsValidation.TryParseFamilyMembers(
            "Smith, Pat, 76561198000000001", out var members, out _);

        Assert.True(ok);
        Assert.Equal("Smith, Pat", Assert.Single(members).Name);
    }

    [Fact]
    public void TryParseFamilyMembers_EmptyTextIsFine()
    {
        Assert.True(SettingsValidation.TryParseFamilyMembers("  \n ", out var members, out _));
        Assert.Empty(members);
        Assert.True(SettingsValidation.TryParseFamilyMembers(null, out members, out _));
        Assert.Empty(members);
    }

    [Theory]
    [InlineData("just a name")]
    [InlineData("Sam, 123")]
    [InlineData(", 76561198000000001")]
    public void TryParseFamilyMembers_RejectsMalformedLines(string text)
    {
        var ok = SettingsValidation.TryParseFamilyMembers(text, out var members, out var error);

        Assert.False(ok);
        Assert.Empty(members);
        Assert.Contains("line 1", error);
    }

    [Fact]
    public void TryParseFamilyMembers_RejectsDuplicateIds()
    {
        var ok = SettingsValidation.TryParseFamilyMembers(
            "Sam, 76561198000000001\nSammy, 76561198000000001", out _, out var error);

        Assert.False(ok);
        Assert.Contains("line 2", error);
    }

    [Fact]
    public void FormatFamilyMembers_RoundTrips()
    {
        var members = new List<FamilyMember>
        {
            new() { Name = "Sam", SteamId64 = "76561198000000001" },
            new() { Name = "Alex", SteamId64 = "76561198000000002" },
        };

        var text = SettingsValidation.FormatFamilyMembers(members);
        var ok = SettingsValidation.TryParseFamilyMembers(text, out var parsed, out _);

        Assert.True(ok);
        Assert.Equal(2, parsed.Count);
        Assert.Equal("Alex", parsed[1].Name);
    }
}
