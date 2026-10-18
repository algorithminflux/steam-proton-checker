using System.Text.Json.Serialization;

namespace SteamProtonChecker.CoreModels;

public class FamilyMember
{
    public string Name { get; set; } = string.Empty;
    public string SteamId64 { get; set; } = string.Empty;
}

public class AppSettings
{
    public string SteamApiKey { get; set; } = string.Empty;
    public string SteamId64 { get; set; } = string.Empty;

    /// <summary>Steam "cc" country code used for store requests. "ca" = Canada.</summary>
    public string CountryCode { get; set; } = "ca";

    /// <summary>
    /// Family members whose libraries are read via their public profiles.
    /// Used when no web token is supplied (see <see cref="SteamWebToken"/>).
    /// </summary>
    public List<FamilyMember> FamilyMembers { get; set; } = new();

    /// <summary>
    /// Optional short-lived token from a signed-in Steam store session. It lets the
    /// app read the real Steam Family shared library and, when Steam provides it,
    /// the date each game was added. Never written to disk.
    /// </summary>
    [JsonIgnore]
    public string SteamWebToken { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SteamApiKey) && !string.IsNullOrWhiteSpace(SteamId64);
}
