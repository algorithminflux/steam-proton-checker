using System.IO;
using System.Text.Json;
using SteamProtonChecker.CoreModels;

namespace SteamProtonChecker.Infrastructure;

/// <summary>
/// Persists <see cref="AppSettings"/> as JSON in the OS app-data folder. The web
/// token is marked [JsonIgnore] on the model, so it is never written to disk.
/// </summary>
public static class AppSettingsStore
{
    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SteamProtonChecker",
            "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch
        {
            // Corrupt or unreadable settings file -> fall through to defaults.
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }
}
