using System.Net.Http;
using System.Text.Json;
using SteamProtonChecker.CoreModels;

namespace SteamProtonChecker.ApiClients;

/// <summary>
/// Fetches release dates, names and the native-Linux flag in batches through the
/// storefront's IStoreBrowseService/GetItems endpoint. This endpoint is
/// undocumented by Valve; the request fields (include_release, include_platforms)
/// match community API references, and the response parsing here is defensive:
/// anything missing just leaves that field empty.
/// </summary>
public class SteamStoreService
{
    public const int MaxBatchSize = 50;

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _countryCode;

    public SteamStoreService(HttpClient http, string apiKey, string countryCode)
    {
        _http = http;
        _apiKey = apiKey;
        _countryCode = string.IsNullOrWhiteSpace(countryCode) ? "US" : countryCode.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Looks up up to <see cref="MaxBatchSize"/> apps. Every requested ID comes back
    /// exactly once; apps Steam has no store entry for are returned with Found = false.
    /// Throws <see cref="InvalidOperationException"/> if the request itself fails.
    /// </summary>
    public async Task<List<StoreDetails>> GetItemsAsync(IReadOnlyList<int> appIds, CancellationToken ct = default)
    {
        if (appIds.Count == 0)
        {
            return new List<StoreDetails>();
        }

        if (appIds.Count > MaxBatchSize)
        {
            throw new ArgumentException($"At most {MaxBatchSize} apps per request.", nameof(appIds));
        }

        var payload = new
        {
            ids = appIds.Select(id => new { appid = id }).ToArray(),
            context = new { language = "english", country_code = _countryCode },
            data_request = new { include_release = true, include_platforms = true },
        };

        var url = "https://api.steampowered.com/IStoreBrowseService/GetItems/v1/" +
                  $"?key={Uri.EscapeDataString(_apiKey)}" +
                  $"&input_json={Uri.EscapeDataString(JsonSerializer.Serialize(payload))}";

        using var response = await _http.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Steam store lookup failed ({(int)response.StatusCode}). Release dates will be retried next sync.");
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        var found = new Dictionary<int, StoreDetails>();

        if (doc.RootElement.TryGetProperty("response", out var responseEl) &&
            responseEl.TryGetProperty("store_items", out var itemsEl))
        {
            foreach (var item in itemsEl.EnumerateArray())
            {
                var details = ParseItem(item);
                if (details is not null)
                {
                    found[details.AppId] = details;
                }
            }
        }

        // Apps missing from the response have no store page (removed/delisted).
        return appIds
            .Select(id => found.TryGetValue(id, out var d) ? d : new StoreDetails { AppId = id, Found = false })
            .ToList();
    }

    private static StoreDetails? ParseItem(JsonElement item)
    {
        int appId;
        if (item.TryGetProperty("appid", out var appIdEl) && appIdEl.TryGetInt32(out var a))
        {
            appId = a;
        }
        else if (item.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var i))
        {
            appId = i;
        }
        else
        {
            return null;
        }

        // success == 1 means the item resolved; any other value means it didn't.
        if (item.TryGetProperty("success", out var successEl) &&
            successEl.ValueKind == JsonValueKind.Number &&
            successEl.TryGetInt32(out var success) && success != 1)
        {
            return new StoreDetails { AppId = appId, Found = false };
        }

        var details = new StoreDetails
        {
            AppId = appId,
            Name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null,
        };

        if (item.TryGetProperty("release", out var releaseEl) && releaseEl.ValueKind == JsonValueKind.Object)
        {
            // Prefer the original release date (e.g. the first-ever release of a remaster's base game).
            details.ReleaseDate = ReadUnix(releaseEl, "original_release_date") ?? ReadUnix(releaseEl, "steam_release_date");
        }

        if (item.TryGetProperty("platforms", out var platformsEl) &&
            platformsEl.ValueKind == JsonValueKind.Object &&
            platformsEl.TryGetProperty("steamos_linux", out var linuxEl))
        {
            details.HasNativeLinux = linuxEl.ValueKind == JsonValueKind.True;
        }

        return details;
    }

    private static DateTimeOffset? ReadUnix(JsonElement parent, string property)
    {
        if (parent.TryGetProperty(property, out var el) &&
            el.ValueKind == JsonValueKind.Number &&
            el.TryGetInt64(out var unix) && unix > 0)
        {
            return DateTimeOffset.FromUnixTimeSeconds(unix);
        }

        return null;
    }
}
