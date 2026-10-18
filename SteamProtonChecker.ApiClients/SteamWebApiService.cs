using System.Net.Http;
using System.Text.Json;
using SteamProtonChecker.CoreModels;

namespace SteamProtonChecker.ApiClients;

/// <summary>
/// Talks to the documented Steam Web API (api.steampowered.com) with the user's
/// API key: owned games with playtime and last-played, and persona names.
/// </summary>
public class SteamWebApiService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public SteamWebApiService(HttpClient http, string apiKey)
    {
        _http = http;
        _apiKey = apiKey;
    }

    /// <summary>
    /// Owned games for any account whose "Game details" privacy is Public.
    /// Throws <see cref="InvalidOperationException"/> with a readable message
    /// when the request fails or the profile is private.
    /// </summary>
    public async Task<List<CheckedGame>> GetOwnedGamesAsync(string steamId64, CancellationToken ct = default)
    {
        var url = "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/" +
                  $"?key={Uri.EscapeDataString(_apiKey)}&steamid={Uri.EscapeDataString(steamId64)}" +
                  "&include_appinfo=1&include_played_free_games=1&format=json";

        using var response = await _http.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Steam Web API request failed ({(int)response.StatusCode}) for {steamId64}. " +
                "Double-check the API key and the SteamID64.");
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        if (!doc.RootElement.TryGetProperty("response", out var responseEl) ||
            responseEl.ValueKind != JsonValueKind.Object)
        {
            throw PrivateProfile(steamId64);
        }

        if (!responseEl.TryGetProperty("games", out var gamesEl))
        {
            // A public account with no games still reports game_count (0); a private one returns an empty object.
            if (responseEl.TryGetProperty("game_count", out _))
            {
                return new List<CheckedGame>();
            }

            throw PrivateProfile(steamId64);
        }

        var games = new List<CheckedGame>();
        foreach (var g in gamesEl.EnumerateArray())
        {
            var appId = g.GetProperty("appid").GetInt32();
            var name = g.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            var playtime = g.TryGetProperty("playtime_forever", out var ptEl) ? ptEl.GetInt32() : 0;

            DateTimeOffset? lastPlayed = null;
            if (g.TryGetProperty("rtime_last_played", out var lpEl) && lpEl.TryGetInt64(out var unix) && unix > 0)
            {
                // 0 means "never played" in this API.
                lastPlayed = DateTimeOffset.FromUnixTimeSeconds(unix);
            }

            games.Add(new CheckedGame
            {
                AppId = appId,
                Name = string.IsNullOrWhiteSpace(name) ? CheckedGame.PlaceholderName(appId) : name,
                PlaytimeMinutes = playtime,
                LastPlayed = lastPlayed,
                IsOwned = true,
            });
        }

        return games;
    }

    /// <summary>
    /// Display names for SteamID64s, used to label family members. Best effort:
    /// returns whatever it could resolve and never throws on a network problem.
    /// </summary>
    public async Task<Dictionary<string, string>> GetPersonaNamesAsync(
        IEnumerable<string> steamIds, CancellationToken ct = default)
    {
        var names = new Dictionary<string, string>();
        var ids = steamIds.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct().Take(100).ToList();
        if (ids.Count == 0)
        {
            return names;
        }

        var url = "https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/" +
                  $"?key={Uri.EscapeDataString(_apiKey)}&steamids={string.Join(",", ids)}";

        try
        {
            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                return names;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("response", out var responseEl) &&
                responseEl.TryGetProperty("players", out var playersEl))
            {
                foreach (var p in playersEl.EnumerateArray())
                {
                    var id = p.TryGetProperty("steamid", out var idEl) ? idEl.GetString() : null;
                    var persona = p.TryGetProperty("personaname", out var nameEl) ? nameEl.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(persona))
                    {
                        names[id] = persona;
                    }
                }
            }
        }
        catch (HttpRequestException)
        {
            // Names are cosmetic; fall back to SteamIDs.
        }
        catch (JsonException)
        {
        }

        return names;
    }

    private static InvalidOperationException PrivateProfile(string steamId64) =>
        new($"No game list came back for {steamId64}. Set that profile's 'Game details' privacy to Public.");
}
