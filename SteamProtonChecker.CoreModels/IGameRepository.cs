namespace SteamProtonChecker.CoreModels;

/// <summary>Local cache of every game, its store data and its ProtonDB rating.</summary>
public interface IGameRepository
{
    List<CheckedGame> GetAllGames();

    /// <summary>
    /// Writes the account-side fields (name, source flags, playtime, dates). Store and
    /// ProtonDB columns of existing rows are left alone.
    /// </summary>
    void UpsertGames(IEnumerable<CheckedGame> games);

    /// <summary>Drops cached rows that are no longer owned, wishlisted or family-shared.</summary>
    void DeleteGamesWithoutSource();

    /// <summary>Saves store data. Real names are never overwritten; only placeholder names are filled in.</summary>
    void SaveStoreDetails(StoreDetails details);

    void SaveProtonResult(int appId, ProtonTier tier, int? reportCount);

    List<int> GetAppIdsNeedingStoreRefresh(TimeSpan maxAge);
    List<int> GetAppIdsNeedingProtonRefresh(TimeSpan maxAge);
}
