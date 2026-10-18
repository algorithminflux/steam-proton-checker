using System.Net.Http;
using System.Text.Json;

namespace SteamProtonChecker.ApiClients;

/// <summary>One app in the family's shared library.</summary>
public record SharedLibraryApp(
    int AppId,
    string Name,
    IReadOnlyList<string> OwnerSteamIds,
    DateTimeOffset? AcquiredAt);

/// <summary>
/// Reads the Steam Family shared library through IFamilyGroupsService. Unlike the
/// rest of the Web API, these methods reject a developer API key; they need the
/// short-lived "webapi_token" of a signed-in Steam store session. The calls are
/// unofficial and undocumented, so everything here is parsed defensively and
/// failures surface as <see cref="InvalidOperationException"/> with a clear message.
/// </summary>
public class SteamFamilyService
{
    private readonly HttpClient _http;

    public SteamFamilyService(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Accepts the bare token or the full JSON from
    /// store.steampowered.com/pointssummary/ajaxgetasyncconfig and returns just the token.
    /// </summary>
    public static string NormalizeToken(string input)
    {
        var text = input.Trim();
        if (!text.StartsWith('{'))
        {
            return text.Trim('"');
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("data", out var dataEl) &&
                dataEl.TryGetProperty("webapi_token", out var tokenEl) &&
                tokenEl.ValueKind == JsonValueKind.String)
            {
                return tokenEl.GetString() ?? string.Empty;
            }

            if (doc.RootElement.TryGetProperty("webapi_token", out var directEl) &&
                directEl.ValueKind == JsonValueKind.String)
            {
                return directEl.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
        }

        return string.Empty;
    }

    public async Task<List<SharedLibraryApp>> GetSharedLibraryAsync(
        string webToken, string steamId64, CancellationToken ct = default)
    {
        var token = NormalizeToken(webToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("The Steam web token couldn't be read. Paste the webapi_token value or the full page text.");
        }

        var escapedToken = Uri.EscapeDataString(token);
        var groupId = await GetFamilyGroupIdAsync(escapedToken, steamId64, ct);

        var url = "https://api.steampowered.com/IFamilyGroupsService/GetSharedLibraryApps/v1/" +
                  $"?access_token={escapedToken}&family_groupid={Uri.EscapeDataString(groupId)}" +
                  $"&steamid={Uri.EscapeDataString(steamId64)}" +
                  "&include_own=true&include_free=true&include_non_games=false&language=english";

        using var doc = await GetJsonAsync(url, ct);

        var apps = new List<SharedLibraryApp>();

        if (!doc.RootElement.TryGetProperty("response", out var responseEl) ||
            !responseEl.TryGetProperty("apps", out var appsEl))
        {
            return apps;
        }

        foreach (var app in appsEl.EnumerateArray())
        {
            if (!app.TryGetProperty("appid", out var appIdEl) || !appIdEl.TryGetInt32(out var appId))
            {
                continue;
            }

            var name = app.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;

            var owners = new List<string>();
            if (app.TryGetProperty("owner_steamids", out var ownersEl) && ownersEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var o in ownersEl.EnumerateArray())
                {
                    var id = ReadId(o);
                    if (!string.IsNullOrEmpty(id))
                    {
                        owners.Add(id);
                    }
                }
            }

            // Not guaranteed to be present; the date-added column simply stays empty if it isn't.
            DateTimeOffset? acquired = null;
            if (app.TryGetProperty("rt_time_acquired", out var acqEl) &&
                acqEl.ValueKind == JsonValueKind.Number &&
                acqEl.TryGetInt64(out var unix) && unix > 0)
            {
                acquired = DateTimeOffset.FromUnixTimeSeconds(unix);
            }

            apps.Add(new SharedLibraryApp(
                appId,
                string.IsNullOrWhiteSpace(name) ? $"App {appId}" : name,
                owners,
                acquired));
        }

        return apps;
    }

    private async Task<string> GetFamilyGroupIdAsync(string escapedToken, string steamId64, CancellationToken ct)
    {
        var url = "https://api.steampowered.com/IFamilyGroupsService/GetFamilyGroupForUser/v1/" +
                  $"?access_token={escapedToken}&steamid={Uri.EscapeDataString(steamId64)}";

        using var doc = await GetJsonAsync(url, ct);

        if (doc.RootElement.TryGetProperty("response", out var responseEl) &&
            responseEl.TryGetProperty("family_groupid", out var idEl))
        {
            var id = ReadId(idEl);
            if (!string.IsNullOrEmpty(id) && id != "0")
            {
                return id;
            }
        }

        throw new InvalidOperationException("This account isn't in a Steam Family group, so there's no shared library to read.");
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct);

        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                "Steam rejected the web token. It expires after roughly a day; get a fresh one and try again.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Steam Family request failed ({(int)response.StatusCode}).");
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(body);
    }

    /// <summary>64-bit IDs arrive as numbers or as strings depending on the serializer; accept both.</summary>
    private static string? ReadId(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.GetRawText(),
        _ => null,
    };
}
