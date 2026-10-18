namespace SteamProtonChecker.CoreModels;

/// <summary>Progress report for the UI. <paramref name="Total"/> is 0 when the step has no countable length.</summary>
public record SyncProgress(string Message, int Current, int Total);
