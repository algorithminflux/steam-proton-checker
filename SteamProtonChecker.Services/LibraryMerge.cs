using SteamProtonChecker.CoreModels;

namespace SteamProtonChecker.Services;

/// <summary>
/// Pure merge rules for combining the library, wishlist and family sources into
/// one set of games. Each Apply method fully replaces what its source contributes,
/// so call it only after that source was fetched successfully; a failed fetch
/// leaves the cached data for that source untouched.
/// </summary>
public static class LibraryMerge
{
    public static void ApplyOwned(Dictionary<int, CheckedGame> games, IEnumerable<CheckedGame> owned)
    {
        foreach (var game in games.Values)
        {
            game.IsOwned = false;
        }

        foreach (var o in owned)
        {
            var game = GetOrAdd(games, o.AppId, o.Name);
            game.IsOwned = true;
            game.PlaytimeMinutes = o.PlaytimeMinutes;
            game.LastPlayed = o.LastPlayed;

            if (!string.IsNullOrWhiteSpace(o.Name) && o.Name != CheckedGame.PlaceholderName(o.AppId))
            {
                game.Name = o.Name;
            }
        }
    }

    public static void ApplyWishlist(Dictionary<int, CheckedGame> games, IEnumerable<int> appIds)
    {
        foreach (var game in games.Values)
        {
            game.IsWishlisted = false;
        }

        foreach (var appId in appIds)
        {
            GetOrAdd(games, appId, null).IsWishlisted = true;
        }
    }

    /// <summary>
    /// Marks games that family members own. Games the user already owns keep their
    /// "Library" source only; "Family" means available through family sharing but not owned.
    /// </summary>
    public static void ApplyFamily(Dictionary<int, CheckedGame> games, IEnumerable<FamilyGameEntry> entries)
    {
        foreach (var game in games.Values)
        {
            game.IsFamilyShared = false;
            game.FamilyMembers = string.Empty;
        }

        foreach (var entry in entries)
        {
            var game = GetOrAdd(games, entry.AppId, entry.Name);

            if (game.HasPlaceholderName && !string.IsNullOrWhiteSpace(entry.Name) &&
                entry.Name != CheckedGame.PlaceholderName(entry.AppId))
            {
                game.Name = entry.Name;
            }

            if (game.IsOwned)
            {
                continue;
            }

            game.IsFamilyShared = true;
            game.FamilyMembers = string.Join(", ", entry.MemberNames.Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        }
    }

    /// <summary>Sets the date a game was added to the library, for owned games only.</summary>
    public static void ApplyDateAdded(Dictionary<int, CheckedGame> games, IReadOnlyDictionary<int, DateTimeOffset> dates)
    {
        foreach (var (appId, date) in dates)
        {
            if (games.TryGetValue(appId, out var game) && game.IsOwned)
            {
                game.DateAdded = date;
            }
        }
    }

    /// <summary>Removes games that no source claims any more and returns how many were dropped.</summary>
    public static int RemoveUnsourced(Dictionary<int, CheckedGame> games)
    {
        var gone = games.Where(kv => !kv.Value.HasAnySource).Select(kv => kv.Key).ToList();
        foreach (var id in gone)
        {
            games.Remove(id);
        }

        return gone.Count;
    }

    private static CheckedGame GetOrAdd(Dictionary<int, CheckedGame> games, int appId, string? name)
    {
        if (!games.TryGetValue(appId, out var game))
        {
            game = new CheckedGame
            {
                AppId = appId,
                Name = string.IsNullOrWhiteSpace(name) ? CheckedGame.PlaceholderName(appId) : name,
            };
            games[appId] = game;
        }

        return game;
    }
}
