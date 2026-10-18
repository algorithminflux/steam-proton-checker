using SteamProtonChecker.CoreModels;
using SteamProtonChecker.Services;

namespace SteamProtonChecker.Tests.Services;

public class LibraryMergeTests
{
    private static Dictionary<int, CheckedGame> Games(params CheckedGame[] games) =>
        games.ToDictionary(g => g.AppId);

    private static CheckedGame Owned(int appId, string name, int minutes = 0, DateTimeOffset? lastPlayed = null) =>
        new() { AppId = appId, Name = name, IsOwned = true, PlaytimeMinutes = minutes, LastPlayed = lastPlayed };

    [Fact]
    public void ApplyOwned_AddsNewGamesAndUpdatesPlaytime()
    {
        var games = Games(Owned(10, "Alpha", 60));
        var played = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

        LibraryMerge.ApplyOwned(games, new[] { Owned(10, "Alpha", 300, played), Owned(20, "Beta") });

        Assert.Equal(300, games[10].PlaytimeMinutes);
        Assert.Equal(played, games[10].LastPlayed);
        Assert.True(games[20].IsOwned);
        Assert.Equal("Beta", games[20].Name);
    }

    [Fact]
    public void ApplyOwned_ClearsOwnershipOfGamesNoLongerReturned_ButKeepsTheirStoreData()
    {
        var release = DateTimeOffset.FromUnixTimeSeconds(1_500_000_000);
        var games = Games(
            new CheckedGame { AppId = 10, Name = "Alpha", IsOwned = true, IsWishlisted = true, ReleaseDate = release });

        LibraryMerge.ApplyOwned(games, Array.Empty<CheckedGame>());

        Assert.False(games[10].IsOwned);
        Assert.True(games[10].IsWishlisted);
        Assert.Equal(release, games[10].ReleaseDate);
    }

    [Fact]
    public void ApplyOwned_DoesNotReplaceARealNameWithAPlaceholder()
    {
        var games = Games(Owned(10, "Alpha"));

        LibraryMerge.ApplyOwned(games, new[] { Owned(10, "App 10") });

        Assert.Equal("Alpha", games[10].Name);
    }

    [Fact]
    public void ApplyWishlist_AddsPlaceholderNamedGames_AndKeepsExistingNames()
    {
        var games = Games(Owned(10, "Alpha"));

        LibraryMerge.ApplyWishlist(games, new[] { 10, 40 });

        Assert.True(games[10].IsWishlisted);
        Assert.Equal("Alpha", games[10].Name);
        Assert.True(games[40].IsWishlisted);
        Assert.Equal("App 40", games[40].Name);
    }

    [Fact]
    public void ApplyWishlist_ClearsGamesRemovedFromTheWishlist()
    {
        var games = Games(new CheckedGame { AppId = 40, Name = "Delta", IsWishlisted = true });

        LibraryMerge.ApplyWishlist(games, Array.Empty<int>());

        Assert.False(games[40].IsWishlisted);
    }

    [Fact]
    public void ApplyFamily_FlagsGamesTheUserDoesNotOwn()
    {
        var games = Games(Owned(20, "Beta"));

        LibraryMerge.ApplyFamily(games, new[]
        {
            new FamilyGameEntry(20, "Beta", new[] { "Sam" }),
            new FamilyGameEntry(30, "Gamma", new[] { "Sam", "Alex", "Sam" }),
        });

        Assert.False(games[20].IsFamilyShared);   // already in the user's own library
        Assert.True(games[30].IsFamilyShared);
        Assert.Equal("Alex, Sam", games[30].FamilyMembers);
        Assert.Equal("Gamma", games[30].Name);
    }

    [Fact]
    public void ApplyFamily_ReplacesThePreviousFamilyData()
    {
        var games = Games(new CheckedGame { AppId = 30, Name = "Gamma", IsFamilyShared = true, FamilyMembers = "Sam" });

        LibraryMerge.ApplyFamily(games, Array.Empty<FamilyGameEntry>());

        Assert.False(games[30].IsFamilyShared);
        Assert.Equal(string.Empty, games[30].FamilyMembers);
    }

    [Fact]
    public void ApplyFamily_FillsInAPlaceholderNameFromTheFamilyLibrary()
    {
        var games = Games(new CheckedGame { AppId = 30, Name = "App 30", IsWishlisted = true });

        LibraryMerge.ApplyFamily(games, new[] { new FamilyGameEntry(30, "Gamma", new[] { "Sam" }) });

        Assert.Equal("Gamma", games[30].Name);
    }

    [Fact]
    public void ApplyDateAdded_OnlyAffectsOwnedGames()
    {
        var added = DateTimeOffset.FromUnixTimeSeconds(1_600_000_000);
        var games = Games(Owned(10, "Alpha"), new CheckedGame { AppId = 30, Name = "Gamma", IsFamilyShared = true });

        LibraryMerge.ApplyDateAdded(games, new Dictionary<int, DateTimeOffset> { [10] = added, [30] = added, [99] = added });

        Assert.Equal(added, games[10].DateAdded);
        Assert.Null(games[30].DateAdded);
        Assert.False(games.ContainsKey(99));
    }

    [Fact]
    public void RemoveUnsourced_DropsGamesNoSourceClaims()
    {
        var games = Games(
            Owned(10, "Alpha"),
            new CheckedGame { AppId = 50, Name = "Orphan" });

        var removed = LibraryMerge.RemoveUnsourced(games);

        Assert.Equal(1, removed);
        Assert.True(games.ContainsKey(10));
        Assert.False(games.ContainsKey(50));
    }
}
