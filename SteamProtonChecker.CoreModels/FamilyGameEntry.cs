namespace SteamProtonChecker.CoreModels;

/// <summary>A game that one or more family members own, used to merge family libraries into the main list.</summary>
public record FamilyGameEntry(int AppId, string Name, IReadOnlyList<string> MemberNames);
