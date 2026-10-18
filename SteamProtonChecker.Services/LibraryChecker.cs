using System.Net.Http;
using SteamProtonChecker.ApiClients;
using SteamProtonChecker.CoreModels;

namespace SteamProtonChecker.Services;

public class SyncResult
{
    public List<CheckedGame> Games { get; init; } = new();

    /// <summary>Non-fatal problems (a private family profile, an expired token, ...) to show the user.</summary>
    public List<string> Warnings { get; init; } = new();
}

/// <summary>
/// Top-level orchestration: pull the library, wishlist and family games, merge
/// them, fill in release dates and Linux support from the store, look up each
/// game on ProtonDB, and cache everything locally.
/// </summary>
public class LibraryChecker
{
    private static readonly TimeSpan StoreMaxAge = TimeSpan.FromDays(14);
    private static readonly TimeSpan ProtonMaxAge = TimeSpan.FromDays(7);
    private const int ProtonParallelism = 4;

    private readonly IGameRepository _repo;
    private readonly HttpClient _http;

    public LibraryChecker(IGameRepository repo, HttpClient http)
    {
        _repo = repo;
        _http = http;
    }

    public List<CheckedGame> GetCachedLibrary() => _repo.GetAllGames();

    public async Task<SyncResult> SyncAsync(
        AppSettings settings,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default)
    {
        var warnings = new List<string>();
        var games = _repo.GetAllGames().ToDictionary(g => g.AppId);
        var webApi = new SteamWebApiService(_http, settings.SteamApiKey);

        // 1. Your library.
        progress?.Report(new("Fetching your library...", 0, 0));
        try
        {
            LibraryMerge.ApplyOwned(games, await webApi.GetOwnedGamesAsync(settings.SteamId64, ct));
        }
        catch (Exception ex) when (IsRecoverable(ex, ct))
        {
            warnings.Add($"Library: {ex.Message}");
        }

        // 2. Your wishlist (optional - a private wishlist shouldn't stop the sync).
        progress?.Report(new("Fetching your wishlist...", 0, 0));
        try
        {
            var wishlistIds = await new SteamWishlistService(_http).GetWishlistAppIdsAsync(settings.SteamId64, ct);
            LibraryMerge.ApplyWishlist(games, wishlistIds);
        }
        catch (Exception ex) when (IsRecoverable(ex, ct))
        {
            warnings.Add($"Wishlist: {ex.Message}");
        }

        // 3. Family games: the real shared library if a web token was given, else each member's public library.
        await SyncFamilyAsync(settings, webApi, games, warnings, progress, ct);

        LibraryMerge.RemoveUnsourced(games);
        _repo.UpsertGames(games.Values);
        _repo.DeleteGamesWithoutSource();

        // 4. Release dates and native Linux flags, 50 games per request.
        await RefreshStoreDataAsync(settings, warnings, progress, ct);

        // 5. ProtonDB tiers.
        await RefreshProtonAsync(warnings, progress, ct);

        progress?.Report(new("Done", 0, 0));
        return new SyncResult { Games = _repo.GetAllGames(), Warnings = warnings };
    }

    private async Task SyncFamilyAsync(
        AppSettings settings,
        SteamWebApiService webApi,
        Dictionary<int, CheckedGame> games,
        List<string> warnings,
        IProgress<SyncProgress>? progress,
        CancellationToken ct)
    {
        var namesById = settings.FamilyMembers
            .Where(m => !string.IsNullOrWhiteSpace(m.SteamId64))
            .ToDictionary(m => m.SteamId64, m => m.Name, StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(settings.SteamWebToken))
        {
            progress?.Report(new("Fetching the Steam Family shared library...", 0, 0));
            try
            {
                var shared = await new SteamFamilyService(_http)
                    .GetSharedLibraryAsync(settings.SteamWebToken, settings.SteamId64, ct);

                var otherOwners = shared
                    .SelectMany(a => a.OwnerSteamIds)
                    .Where(id => id != settings.SteamId64 && !namesById.ContainsKey(id));
                var persona = await webApi.GetPersonaNamesAsync(otherOwners, ct);

                string NameOf(string id) =>
                    namesById.TryGetValue(id, out var n) ? n :
                    persona.TryGetValue(id, out var p) ? p : id;

                var entries = shared
                    .Select(a => new FamilyGameEntry(
                        a.AppId,
                        a.Name,
                        a.OwnerSteamIds.Where(id => id != settings.SteamId64).Select(NameOf).ToList()))
                    .Where(e => e.MemberNames.Count > 0)
                    .ToList();

                LibraryMerge.ApplyFamily(games, entries);

                var dates = shared
                    .Where(a => a.AcquiredAt is not null && a.OwnerSteamIds.Contains(settings.SteamId64))
                    .ToDictionary(a => a.AppId, a => a.AcquiredAt!.Value);
                LibraryMerge.ApplyDateAdded(games, dates);

                if (dates.Count == 0)
                {
                    warnings.Add("Date added: Steam's family response included no acquisition dates, so that column stays empty.");
                }

                return;
            }
            catch (Exception ex) when (IsRecoverable(ex, ct))
            {
                warnings.Add($"Family shared library: {ex.Message}" +
                             (settings.FamilyMembers.Count > 0 ? " Falling back to the listed family members' public libraries." : string.Empty));
            }
        }

        if (settings.FamilyMembers.Count == 0)
        {
            return;
        }

        var byApp = new Dictionary<int, (string Name, List<string> Members)>();
        var failures = 0;

        foreach (var member in settings.FamilyMembers.Where(m => !string.IsNullOrWhiteSpace(m.SteamId64)))
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new($"Fetching {member.Name}'s library...", 0, 0));

            try
            {
                foreach (var g in await webApi.GetOwnedGamesAsync(member.SteamId64, ct))
                {
                    if (!byApp.TryGetValue(g.AppId, out var entry))
                    {
                        entry = (g.Name, new List<string>());
                        byApp[g.AppId] = entry;
                    }

                    entry.Members.Add(member.Name);
                }
            }
            catch (Exception ex) when (IsRecoverable(ex, ct))
            {
                failures++;
                warnings.Add($"{member.Name}: {ex.Message}");
            }
        }

        if (failures == 0)
        {
            LibraryMerge.ApplyFamily(
                games,
                byApp.Select(kv => new FamilyGameEntry(kv.Key, kv.Value.Name, kv.Value.Members)));
        }
        else
        {
            warnings.Add("Family games were not updated, because at least one member's library couldn't be read. Cached family data was kept.");
        }
    }

    private async Task RefreshStoreDataAsync(
        AppSettings settings,
        List<string> warnings,
        IProgress<SyncProgress>? progress,
        CancellationToken ct)
    {
        var ids = _repo.GetAppIdsNeedingStoreRefresh(StoreMaxAge);
        if (ids.Count == 0)
        {
            return;
        }

        var store = new SteamStoreService(_http, settings.SteamApiKey, settings.CountryCode);

        for (var i = 0; i < ids.Count; i += SteamStoreService.MaxBatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = ids.Skip(i).Take(SteamStoreService.MaxBatchSize).ToList();
            progress?.Report(new("Fetching release dates...", Math.Min(i + batch.Count, ids.Count), ids.Count));

            try
            {
                foreach (var details in await store.GetItemsAsync(batch, ct))
                {
                    _repo.SaveStoreDetails(details);
                }
            }
            catch (Exception ex) when (IsRecoverable(ex, ct))
            {
                warnings.Add($"Release dates: {ex.Message}");
                return;
            }
        }
    }

    private async Task RefreshProtonAsync(
        List<string> warnings,
        IProgress<SyncProgress>? progress,
        CancellationToken ct)
    {
        var ids = _repo.GetAppIdsNeedingProtonRefresh(ProtonMaxAge);
        if (ids.Count == 0)
        {
            return;
        }

        var proton = new ProtonDbService(_http);
        using var gate = new SemaphoreSlim(ProtonParallelism);
        var dbLock = new object();
        var done = 0;
        var failed = 0;

        var tasks = ids.Select(async id =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var result = await proton.GetSummaryAsync(id, ct);
                if (result is null)
                {
                    Interlocked.Increment(ref failed);
                }
                else
                {
                    lock (dbLock)
                    {
                        _repo.SaveProtonResult(id, result.Tier, result.ReportCount);
                    }
                }
            }
            catch (Exception ex) when (IsRecoverable(ex, ct))
            {
                Interlocked.Increment(ref failed);
            }
            finally
            {
                gate.Release();
                var n = Interlocked.Increment(ref done);
                progress?.Report(new("Checking ProtonDB...", n, ids.Count));
            }
        }).ToList();

        await Task.WhenAll(tasks);

        if (failed > 0)
        {
            warnings.Add($"ProtonDB: {failed} game(s) couldn't be checked and will be retried on the next sync.");
        }
    }

    /// <summary>
    /// Network and parsing problems are reported as warnings so one bad source doesn't
    /// abort the whole sync. A user cancellation is never swallowed.
    /// </summary>
    private static bool IsRecoverable(Exception ex, CancellationToken ct) =>
        !ct.IsCancellationRequested &&
        (ex is InvalidOperationException or HttpRequestException or System.Text.Json.JsonException or TaskCanceledException);
}
