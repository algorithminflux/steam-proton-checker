using System.Net.Http;
using System.Text.Json;

namespace SteamProtonChecker.ApiClients;

/// <summary>
/// Fetches the app IDs on a user's wishlist through the same
/// IWishlistService/GetWishlist endpoint Steam's own store front-end uses.
/// It needs no API key, works for any public SteamID64, and returns app IDs
/// only (names are filled in later from store data).
/// </summary>
public class SteamWishlistService
{
    private readonly HttpClient _http;

    public SteamWishlistService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<int>> GetWishlistAppIdsAsync(string steamId64, CancellationToken ct = default)
    {
        var url = "https://api.steampowered.com/IWishlistService/GetWishlist/v1/" +
                  $"?steamid={Uri.EscapeDataString(steamId64)}";

        using var response = await _http.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                "Could not read the wishlist. Make sure the SteamID64 is correct and the " +
                "profile's 'Game details' privacy setting is Public.");
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        var ids = new List<int>();

        if (!doc.RootElement.TryGetProperty("response", out var responseEl) ||
            !responseEl.TryGetProperty("items", out var itemsEl))
        {
            return ids;
        }

        foreach (var item in itemsEl.EnumerateArray())
        {
            if (item.TryGetProperty("appid", out var appIdEl) && appIdEl.TryGetInt32(out var appId))
            {
                ids.Add(appId);
            }
        }

        return ids.Distinct().ToList();
    }
}
