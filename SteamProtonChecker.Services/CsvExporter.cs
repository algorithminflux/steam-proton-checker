using System.Globalization;
using System.Text;
using SteamProtonChecker.CoreModels;

namespace SteamProtonChecker.Services;

/// <summary>
/// Turns games into CSV text (RFC 4180 quoting, invariant culture, ISO dates). Text
/// cells that start with a formula character are prefixed with an apostrophe so a
/// spreadsheet doesn't evaluate a game name as a formula.
/// </summary>
public static class CsvExporter
{
    public static readonly string[] Header =
    {
        "AppId", "Name", "ProtonTier", "ProtonReports", "NativeLinux", "Source",
        "FamilyMembers", "HoursPlayed", "LastPlayed", "ReleaseDate", "DateAdded",
    };

    public static string Export(IEnumerable<CheckedGame> games)
    {
        var sb = new StringBuilder();
        AppendLine(sb, Header);

        foreach (var g in games)
        {
            AppendLine(sb, new[]
            {
                g.AppId.ToString(CultureInfo.InvariantCulture),
                Safe(g.Name),
                g.EffectiveTier.ToString(),
                g.ProtonReportCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                g.HasNativeLinux ? "Yes" : "No",
                g.SourceLabel,
                Safe(g.FamilyMembers),
                g.IsOwned ? Math.Round(g.PlaytimeMinutes / 60.0, 1).ToString("0.#", CultureInfo.InvariantCulture) : string.Empty,
                g.IsOwned ? FormatDate(g.LastPlayed) : string.Empty,
                FormatDate(g.ReleaseDate),
                FormatDate(g.DateAdded),
            });
        }

        return sb.ToString();
    }

    private static string FormatDate(DateTimeOffset? date) =>
        date is null ? string.Empty : date.Value.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Safe(string text) =>
        text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + text : text;

    private static void AppendLine(StringBuilder sb, IReadOnlyList<string> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(Quote(cells[i]));
        }

        sb.Append("\r\n");
    }

    private static string Quote(string cell) =>
        cell.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
            ? "\"" + cell.Replace("\"", "\"\"") + "\""
            : cell;
}
