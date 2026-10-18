namespace SteamProtonChecker.CoreModels;

/// <summary>
/// ProtonDB compatibility tiers, ordered worst to best so the numeric value
/// doubles as a sort key. <see cref="Pending"/> and <see cref="Unrated"/> sort
/// last because they mean "unknown", not "good".
/// </summary>
public enum ProtonTier
{
    Borked = 0,
    Bronze = 1,
    Silver = 2,
    Gold = 3,
    Platinum = 4,

    /// <summary>The game ships a native Linux build, so Proton isn't needed.</summary>
    Native = 5,

    /// <summary>ProtonDB has reports, but not enough to assign a tier yet.</summary>
    Pending = 6,

    /// <summary>ProtonDB has no data for this game.</summary>
    Unrated = 7,
}

public static class ProtonTierParser
{
    /// <summary>Maps ProtonDB's lowercase tier string to <see cref="ProtonTier"/>.</summary>
    public static ProtonTier Parse(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "borked" => ProtonTier.Borked,
        "bronze" => ProtonTier.Bronze,
        "silver" => ProtonTier.Silver,
        "gold" => ProtonTier.Gold,
        "platinum" => ProtonTier.Platinum,
        "native" => ProtonTier.Native,
        "pending" => ProtonTier.Pending,
        _ => ProtonTier.Unrated,
    };
}
