using SteamProtonChecker.CoreModels;

namespace SteamProtonChecker.Tests.CoreModels;

public class CheckedGameTests
{
    [Theory]
    [InlineData("borked", ProtonTier.Borked)]
    [InlineData("bronze", ProtonTier.Bronze)]
    [InlineData("silver", ProtonTier.Silver)]
    [InlineData("gold", ProtonTier.Gold)]
    [InlineData("platinum", ProtonTier.Platinum)]
    [InlineData("native", ProtonTier.Native)]
    [InlineData("pending", ProtonTier.Pending)]
    [InlineData(" Gold ", ProtonTier.Gold)]
    [InlineData("PLATINUM", ProtonTier.Platinum)]
    [InlineData("something-new", ProtonTier.Unrated)]
    [InlineData("", ProtonTier.Unrated)]
    [InlineData(null, ProtonTier.Unrated)]
    public void Parse_MapsProtonDbTierStrings(string? raw, ProtonTier expected)
    {
        Assert.Equal(expected, ProtonTierParser.Parse(raw));
    }

    [Fact]
    public void Tiers_AreOrderedWorstToBest()
    {
        Assert.True(ProtonTier.Borked < ProtonTier.Bronze);
        Assert.True(ProtonTier.Bronze < ProtonTier.Silver);
        Assert.True(ProtonTier.Silver < ProtonTier.Gold);
        Assert.True(ProtonTier.Gold < ProtonTier.Platinum);
        Assert.True(ProtonTier.Platinum < ProtonTier.Native);
        Assert.True(ProtonTier.Native < ProtonTier.Pending);
        Assert.True(ProtonTier.Pending < ProtonTier.Unrated);
    }

    [Theory]
    [InlineData(ProtonTier.Unrated, true, ProtonTier.Native)]
    [InlineData(ProtonTier.Pending, true, ProtonTier.Native)]
    [InlineData(ProtonTier.Borked, true, ProtonTier.Borked)]
    [InlineData(ProtonTier.Gold, true, ProtonTier.Gold)]
    [InlineData(ProtonTier.Unrated, false, ProtonTier.Unrated)]
    [InlineData(ProtonTier.Silver, false, ProtonTier.Silver)]
    public void EffectiveTier_TreatsNativeLinuxBuildsWithNoVerdictAsNative(
        ProtonTier rating, bool hasNativeLinux, ProtonTier expected)
    {
        var game = new CheckedGame { AppId = 1, ProtonRating = rating, HasNativeLinux = hasNativeLinux };

        Assert.Equal(expected, game.EffectiveTier);
    }

    [Fact]
    public void SourceLabel_CombinesSources()
    {
        var game = new CheckedGame { IsOwned = true, IsWishlisted = true };

        Assert.Equal("Library + Wishlist", game.SourceLabel);
    }

    [Fact]
    public void SourceLabel_NamesFamilyMembers()
    {
        var game = new CheckedGame { IsFamilyShared = true, FamilyMembers = "Sam, Alex" };

        Assert.Equal("Family (Sam, Alex)", game.SourceLabel);
    }

    [Fact]
    public void SourceLabel_FamilyWithoutNames()
    {
        var game = new CheckedGame { IsFamilyShared = true };

        Assert.Equal("Family", game.SourceLabel);
    }

    [Fact]
    public void HasAnySource_IsFalseWhenNoSourceClaimsTheGame()
    {
        Assert.False(new CheckedGame().HasAnySource);
        Assert.True(new CheckedGame { IsWishlisted = true }.HasAnySource);
    }

    [Fact]
    public void HasPlaceholderName_DetectsWishlistPlaceholders()
    {
        var placeholder = new CheckedGame { AppId = 619820, Name = "App 619820" };
        var real = new CheckedGame { AppId = 619820, Name = "Some Game" };

        Assert.True(placeholder.HasPlaceholderName);
        Assert.False(real.HasPlaceholderName);
    }
}
