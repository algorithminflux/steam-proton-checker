using Avalonia.Controls;
using Avalonia.Interactivity;
using SteamProtonChecker.CoreModels;

namespace SteamProtonChecker.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    // Used by Avalonia's XAML loader and previewer only.
    public SettingsWindow() : this(new AppSettings())
    {
    }

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        ApiKeyBox.Text = settings.SteamApiKey;
        SteamIdBox.Text = settings.SteamId64;
        CountryBox.Text = settings.CountryCode;
        FamilyBox.Text = SettingsValidation.FormatFamilyMembers(settings.FamilyMembers);
        TokenBox.Text = settings.SteamWebToken;
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var apiKey = ApiKeyBox.Text?.Trim() ?? string.Empty;
        var steamId = SteamIdBox.Text?.Trim() ?? string.Empty;

        if (apiKey.Length == 0)
        {
            ShowError("Enter your Steam Web API key.");
            return;
        }

        if (!SettingsValidation.IsValidSteamId64(steamId))
        {
            ShowError("Your SteamID64 must be exactly 17 digits (find it at steamid.io).");
            return;
        }

        if (!SettingsValidation.TryParseFamilyMembers(FamilyBox.Text, out var members, out var error))
        {
            ShowError(error);
            return;
        }

        _settings.SteamApiKey = apiKey;
        _settings.SteamId64 = steamId;
        _settings.CountryCode = string.IsNullOrWhiteSpace(CountryBox.Text)
            ? "ca"
            : CountryBox.Text.Trim().ToLowerInvariant();
        _settings.FamilyMembers = members;
        _settings.SteamWebToken = TokenBox.Text?.Trim() ?? string.Empty;

        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
