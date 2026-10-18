using System.Text.RegularExpressions;

namespace SteamProtonChecker.CoreModels;

/// <summary>Validation and parsing for the values typed into the Settings window.</summary>
public static class SettingsValidation
{
    private static readonly Regex SteamId64Pattern = new(@"^\d{17}$", RegexOptions.Compiled);

    public static bool IsValidSteamId64(string? value) =>
        !string.IsNullOrWhiteSpace(value) && SteamId64Pattern.IsMatch(value.Trim());

    /// <summary>
    /// Parses one family member per line as "Name, SteamID64" (a "=" also works as the
    /// separator). Blank lines are ignored. Returns false with a readable error otherwise.
    /// </summary>
    public static bool TryParseFamilyMembers(string? text, out List<FamilyMember> members, out string error)
    {
        members = new List<FamilyMember>();
        error = string.Empty;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lines = (text ?? string.Empty).Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.LastIndexOfAny(new[] { ',', '=' });
            if (separator <= 0)
            {
                members = new List<FamilyMember>();
                error = $"Family line {i + 1}: use the form \"Name, SteamID64\".";
                return false;
            }

            var name = line[..separator].Trim();
            var id = line[(separator + 1)..].Trim();

            if (name.Length == 0 || !IsValidSteamId64(id))
            {
                members = new List<FamilyMember>();
                error = $"Family line {i + 1}: needs a name and a 17-digit SteamID64.";
                return false;
            }

            if (!seen.Add(id))
            {
                members = new List<FamilyMember>();
                error = $"Family line {i + 1}: that SteamID64 is listed twice.";
                return false;
            }

            members.Add(new FamilyMember { Name = name, SteamId64 = id });
        }

        return true;
    }

    public static string FormatFamilyMembers(IEnumerable<FamilyMember> members) =>
        string.Join(Environment.NewLine, members.Select(m => $"{m.Name}, {m.SteamId64}"));
}
