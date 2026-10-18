namespace SteamProtonChecker.CoreModels;

/// <summary>
/// One game from the user's library, a family member's library, or the
/// wishlist, merged with store data (release date, native Linux flag) and its
/// ProtonDB rating. A game can come from more than one source.
/// </summary>
public class CheckedGame
{
    public int AppId { get; set; }
    public string Name { get; set; } = string.Empty;

    // --- Where the game comes from ---
    public bool IsOwned { get; set; }
    public bool IsWishlisted { get; set; }

    /// <summary>Available through Steam Family sharing but not owned by this account.</summary>
    public bool IsFamilyShared { get; set; }

    /// <summary>Comma-separated names of the family members who own it.</summary>
    public string FamilyMembers { get; set; } = string.Empty;

    // --- Account data (only meaningful for owned games) ---
    public int PlaytimeMinutes { get; set; }
    public DateTimeOffset? LastPlayed { get; set; }

    /// <summary>When the game was added to the library. Only known if the optional web token is used.</summary>
    public DateTimeOffset? DateAdded { get; set; }

    // --- Store data ---
    public DateTimeOffset? ReleaseDate { get; set; }
    public bool HasNativeLinux { get; set; }
    public DateTimeOffset? StoreFetchedAt { get; set; }

    // --- ProtonDB data ---
    public ProtonTier ProtonRating { get; set; } = ProtonTier.Unrated;
    public int? ProtonReportCount { get; set; }
    public DateTimeOffset? ProtonFetchedAt { get; set; }

    /// <summary>The wishlist endpoint only returns app IDs, so those games start with this name.</summary>
    public static string PlaceholderName(int appId) => $"App {appId}";

    public bool HasPlaceholderName => Name == PlaceholderName(AppId);

    /// <summary>
    /// The tier to show and sort by. A game with a native Linux build but no
    /// ProtonDB verdict is "Native", not "Unrated" - it won't need Proton.
    /// </summary>
    public ProtonTier EffectiveTier =>
        HasNativeLinux && (ProtonRating == ProtonTier.Unrated || ProtonRating == ProtonTier.Pending)
            ? ProtonTier.Native
            : ProtonRating;

    public string SourceLabel
    {
        get
        {
            var parts = new List<string>();
            if (IsOwned) parts.Add("Library");
            if (IsFamilyShared)
                parts.Add(string.IsNullOrWhiteSpace(FamilyMembers) ? "Family" : $"Family ({FamilyMembers})");
            if (IsWishlisted) parts.Add("Wishlist");
            return string.Join(" + ", parts);
        }
    }

    public bool HasAnySource => IsOwned || IsWishlisted || IsFamilyShared;
}
