using System.Net;
using System.Net.Http;
using System.Text.Json;
using SteamProtonChecker.CoreModels;

namespace SteamProtonChecker.ApiClients;

public record ProtonDbResult(ProtonTier Tier, int? ReportCount);

/// <summary>
/// Reads ProtonDB's per-game summary (https://www.protondb.com/api/v1/reports/summaries/{appid}.json).
/// This is the same unofficial JSON the ProtonDB site and its browser
/// extensions use; it has no documented contract and could change.
/// </summary>
public class ProtonDbService
{
    private readonly HttpClient _http;

    public ProtonDbService(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Returns the game's tier, or <see cref="ProtonTier.Unrated"/> when ProtonDB has no
    /// reports (HTTP 404). Returns null when the lookup failed for any other reason,
    /// so the caller can retry later instead of recording a wrong "unrated".
    /// </summary>
    public async Task<ProtonDbResult?> GetSummaryAsync(int appId, CancellationToken ct = default)
    {
        var url = $"https://www.protondb.com/api/v1/reports/summaries/{appId}.json";
        using var response = await _http.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new ProtonDbResult(ProtonTier.Unrated, 0);
        }

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var tier = root.TryGetProperty("tier", out var tierEl) && tierEl.ValueKind == JsonValueKind.String
                ? ProtonTierParser.Parse(tierEl.GetString())
                : ProtonTier.Unrated;

            int? total = root.TryGetProperty("total", out var totalEl) && totalEl.TryGetInt32(out var t) ? t : null;

            return new ProtonDbResult(tier, total);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
