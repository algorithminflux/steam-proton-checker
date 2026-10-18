using Avalonia.Media;
using SteamProtonChecker.CoreModels;

namespace SteamProtonChecker.ViewModels;

/// <summary>
/// Display wrapper for one game in the grid. Each column shows a text property and
/// sorts by a separate numeric key (the *Sort properties), so dates sort
/// chronologically, hours sort numerically and tiers sort worst-to-best.
/// </summary>
public class GameRow
{
    private static readonly Dictionary<ProtonTier, (IBrush Back, IBrush Fore)> Palette = new()
    {
        [ProtonTier.Borked] = (MakeBrush("#D93B3B"), Brushes.White),
        [ProtonTier.Bronze] = (MakeBrush("#C9702B"), Brushes.White),
        [ProtonTier.Silver] = (MakeBrush("#A9B0BA"), Brushes.Black),
        [ProtonTier.Gold] = (MakeBrush("#E6B422"), Brushes.Black),
        [ProtonTier.Platinum] = (MakeBrush("#7FD6E8"), Brushes.Black),
        [ProtonTier.Native] = (MakeBrush("#3FA66B"), Brushes.White),
        [ProtonTier.Pending] = (MakeBrush("#5B6472"), Brushes.White),
        [ProtonTier.Unrated] = (MakeBrush("#3B4350"), MakeBrush("#C7CDD6")),
    };

    public GameRow(CheckedGame game)
    {
        Game = game;
    }

    public CheckedGame Game { get; }

    public int AppId => Game.AppId;
    public string Name => Game.Name;

    // --- Proton compatibility ---
    public ProtonTier Tier => Game.EffectiveTier;
    public int TierRank => (int)Tier;
    public string TierLabel => Tier.ToString();
    public IBrush TierBrush => Palette[Tier].Back;
    public IBrush TierForeground => Palette[Tier].Fore;

    public string ReportsText => Game.ProtonReportCount is > 0 ? Game.ProtonReportCount.Value.ToString("N0") : "";
    public int ReportsSort => Game.ProtonReportCount ?? 0;

    public string NativeText => Game.HasNativeLinux ? "Yes" : "";
    public int NativeSort => Game.HasNativeLinux ? 1 : 0;

    public string SourceText => Game.SourceLabel;

    // --- Play history (owned games only) ---
    public double HoursPlayed => Game.IsOwned ? Math.Round(Game.PlaytimeMinutes / 60.0, 1) : 0;

    public string HoursText =>
        !Game.IsOwned ? "" :
        Game.PlaytimeMinutes <= 0 ? "Never" :
        HoursPlayed.ToString("0.#");

    public long LastPlayedSort => Game.IsOwned ? Game.LastPlayed?.ToUnixTimeSeconds() ?? 0 : 0;
    public string LastPlayedText => Game.IsOwned ? FormatDate(Game.LastPlayed) : "";

    // --- Dates ---
    public long ReleaseSort => Game.ReleaseDate?.ToUnixTimeSeconds() ?? 0;
    public string ReleaseText => FormatDate(Game.ReleaseDate);

    public long AddedSort => Game.DateAdded?.ToUnixTimeSeconds() ?? 0;
    public string AddedText => FormatDate(Game.DateAdded);

    private static string FormatDate(DateTimeOffset? date) =>
        date is null ? "" : date.Value.ToLocalTime().ToString("yyyy-MM-dd");

    private static IBrush MakeBrush(string hex) => new SolidColorBrush(Color.Parse(hex));
}
