# Handoff: Steam Proton Checker

Read this first. It tells you what exists, what has **not** been verified, and what to do next.

## Goal

Marc (the user) is moving from Windows 11 to Linux. He wants one desktop app that lists every
game in his Steam library, his family's libraries and his wishlist with its ProtonDB
compatibility tier, so he can quickly see which games will give him trouble before he switches.

Requirements he stated:
- Sources: his library, family library games, wishlist games.
- Show Proton compatibility (borked, bronze, silver, gold, ...), grouped/sorted worst to best.
- Sortable by: compatibility level, hours played, release date, last played, date added to library.
- Built the same way as his existing app, **steam-shuffle** (https://github.com/algorithminflux/steam-shuffle):
  .NET 8, Avalonia, layered projects, xUnit tests, SQLite cache, GitHub Actions release workflow.

## Status: written, never compiled

The code was authored in an environment with **no .NET SDK** (download hosts were blocked), so
nothing has been built or run. Only static checks were done (XML validity, brace balance, project
references, XAML handler names). Expect some compile errors and a few failing tests.

**Step 1 is always:**
```
dotnet restore
dotnet build
dotnet test
```
Fix compile errors and test failures before anything else. When a test fails, decide whether the
test or the code is wrong; several tests encode assumptions about unverified JSON shapes (below).

## Layout

```
SteamProtonChecker.sln
SteamProtonChecker/                 Avalonia UI (WinExe). MainWindow, SettingsWindow, GameRow
SteamProtonChecker.CoreModels/      CheckedGame, ProtonTier, AppSettings, IGameRepository, SettingsValidation
SteamProtonChecker.ApiClients/      SteamWebApiService, SteamWishlistService, SteamStoreService,
                                    SteamFamilyService, ProtonDbService
SteamProtonChecker.Infrastructure/  GameRepository (SQLite), AppSettingsStore (settings.json)
SteamProtonChecker.Services/        LibraryChecker (sync orchestration), LibraryMerge (pure merge rules)
SteamProtonChecker.Tests/           xUnit; HTTP faked via TestHelpers/FakeHttpMessageHandler
.github/workflows/                  ci.yml (build+test), release.yml (win/linux/osx zips on v* tags)
```

Dependency rule: outer to inner only. Services depends on Infrastructure only via `IGameRepository`.

## How a sync works (LibraryChecker.SyncAsync)

1. Load cached games from SQLite into a dictionary.
2. Fetch own library (`GetOwnedGames`) and merge via `LibraryMerge.ApplyOwned`.
3. Fetch wishlist and merge via `ApplyWishlist`.
4. Family: if `AppSettings.SteamWebToken` is set, use `IFamilyGroupsService` (shared library, plus
   acquisition dates if present). Otherwise, or if that fails, read each listed family member's
   *public* library via `GetOwnedGames`.
5. Drop games no source claims, upsert to SQLite.
6. Batch (50) store lookup for release date and native Linux flag, for games not fetched in 14 days.
7. ProtonDB lookup (4 in parallel) for games not fetched in 7 days.

Design decisions worth keeping:
- Each `Apply*` fully **replaces** what its source contributes, and is only called if that fetch
  **succeeded**. A failed fetch (private profile, 403, expired token) leaves the cached data for
  that source alone and adds a warning shown in the UI. Don't regress this.
- "Family" is only set for games the user does **not** own; a game in both libraries shows as Library.
- `CheckedGame.EffectiveTier`: a game with a native Linux build and an Unrated/Pending ProtonDB
  verdict displays as `Native`. A ProtonDB tier (even Borked) is never overridden.
- ProtonDB 404 means "no reports" and is saved as Unrated. Any other failure is **not** saved, so
  it's retried next sync.
- The web token is `[JsonIgnore]`: never written to disk. Keep it that way.
- Grid columns display text but sort by separate numeric keys on `GameRow` (`TierRank`,
  `HoursPlayed`, `LastPlayedSort`, `ReleaseSort`, `AddedSort`), wired through `SortMemberPath`.
- Reflection bindings are used on purpose (`AvaloniaUseCompiledBindingsByDefault=false`).

## Unverified assumptions (verify against live responses, then fix code and tests)

Capture a real response for each, save it as a test fixture, and make the parser match reality.

| # | Assumption | Where | Confidence |
|---|-----------|-------|-----------|
| 1 | ProtonDB: `GET https://www.protondb.com/api/v1/reports/summaries/{appid}.json` returns `{"tier":"gold","total":1234,...}`; 404 means no reports | `ProtonDbService` | From memory; unconfirmed |
| 2 | `GET https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?key=...&input_json={...}` with `data_request` `include_release` and `include_platforms` | `SteamStoreService` | Request field names confirmed in a third-party API reference (xpaw); **response** names are from memory |
| 3 | GetItems response: `response.store_items[]` with `appid`/`id`, `name`, `success` (1 = ok), `release.original_release_date` / `release.steam_release_date` (unix seconds), `platforms.steamos_linux` (bool) | `SteamStoreService.ParseItem` | Unconfirmed |
| 4 | Family: `IFamilyGroupsService/GetFamilyGroupForUser/v1` then `GetSharedLibraryApps/v1` with `access_token=<webapi_token>` (not an API key) | `SteamFamilyService` | Existence and token requirement confirmed by third-party docs and two open-source projects; exact response unconfirmed |
| 5 | Shared library response: `response.apps[]` with `appid`, `name`, `owner_steamids[]`, optionally `rt_time_acquired` | `SteamFamilyService` | `rt_time_acquired` unconfirmed; it's only parsed if present |
| 6 | Token source: sign in at store.steampowered.com, open `/pointssummary/ajaxgetasyncconfig`, take `data.webapi_token`; lasts about a day | Settings help text, `NormalizeToken` | Per the projects below |
| 7 | Avalonia 11.1.3 DataGrid: `SortMemberPath` drives header-click sorting on a `DataGridCollectionView`; `Filter` + `Refresh()` re-filter without losing sort | `MainWindow` | Unverified at compile/run time |

Good reference code for the family endpoints (read their actual params and response handling):
- https://github.com/LethalKebab/steam-achievement-tracker (PR #145, `lib/family.js`)
- https://github.com/Gabriel-Liz2003/Ludex (PR #28; commits `cbe6fc0`, `f14ced8`)
- https://steamapi.xpaw.me/IFamilyGroupsService (method list; response schemas not documented)

## Open question: family library (ask Marc, don't guess)

steam-shuffle's `main` branch (last commit Jul 26, 2026, only branch) has **no** code that reads the
family library: its sync only calls `GetOwnedGames` and the wishlist endpoint. Its README says family
games never appear in `GetOwnedGames`, and offers **+ Add Game** (store search by name, tagged
"Family/Manual") instead. But Marc says steam-shuffle "pulls in the entire family library".

This is unresolved. Possibilities: he runs a newer unpushed local version; `GetOwnedGames` really
returns family games for his account; or the games were added by hand. Ask him which, and ask to see
the local steam-shuffle code if it differs from GitHub. Then:
- If `GetOwnedGames` returns family games, the normal library sync already covers it. Simplify or
  remove the family-member and token paths.
- If steam-shuffle has a working family fetch locally, port that approach instead.
- Either way, consider a manual "+ Add Game" like steam-shuffle's (see its `AddGameWindow`,
  `SteamStoreService.SearchAsync` against `store.steampowered.com/api/storesearch/`) as a safe fallback.

## Next steps, in priority order

1. Build, run tests, fix everything. Commit when green.
2. Run the app against Marc's real account (he supplies his own API key and SteamID64 in Settings;
   never commit or log them). Log the raw JSON from each endpoint once, compare to the table above,
   fix parsers, and turn the real responses into test fixtures.
3. Resolve the family-library question above.
4. "Date added": there is no documented Steam API for it. If the family endpoint doesn't return
   `rt_time_acquired` in practice, either hide the column or find another source. Don't show fake data.
5. UI polish: confirm click-to-sort works on every column (including nulls/blank dates sorting
   sensibly), that the filter keeps the chosen sort, and that large libraries (1,000+ games) stay
   responsive. Sync currently does SQLite writes on the UI thread between awaits; move them off-thread
   if it stutters.
6. Nice-to-haves, only if asked: CSV export, a Steam Deck Verified column, an anti-cheat flag
   (https://areweanticheatyet.com), a "pre-migration checklist" view of just Borked/Bronze games with
   hours played.

## Conventions

- Mirror steam-shuffle: file-scoped namespaces, nullable enabled, no MVVM framework, code-behind views,
  tests mirror the project folders, HTTP faked with `FakeHttpMessageHandler`, no real network in tests.
- Prefer small, defensive JSON parsing (`TryGetProperty`); undocumented Steam/ProtonDB endpoints change.
- Network failures should become warnings, not crashes; a cancellation must never be swallowed.
- Steam throttles: keep store batches at 50 and ProtonDB parallelism low (currently 4).
- Don't add a LICENSE file without asking; steam-shuffle has one and Marc hasn't said which to use.

## Environment notes

- Marc is on Windows 11 today: `dotnet run --project SteamProtonChecker`. Settings and the SQLite cache
  live in `%AppData%\SteamProtonChecker` (`~/.config/SteamProtonChecker` on Linux).
- Needs the .NET 8 SDK. Avalonia packages are pinned to 11.1.3 to match steam-shuffle.
- Not yet pushed anywhere: Marc will create the GitHub repo `steam-proton-checker` himself.
- An earlier standalone `proton_check.py` script exists in the chat history. It is superseded by this
  app and was never run; ignore it.
