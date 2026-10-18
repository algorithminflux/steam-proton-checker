namespace SteamProtonChecker.CoreModels;

/// <summary>Store metadata for one app, as returned by the storefront.</summary>
public class StoreDetails
{
    public int AppId { get; set; }

    /// <summary>False when Steam answered but has no store entry (removed or delisted app).</summary>
    public bool Found { get; set; } = true;

    public string? Name { get; set; }
    public DateTimeOffset? ReleaseDate { get; set; }
    public bool HasNativeLinux { get; set; }
}
