using System.Diagnostics;
using System.Net.Http;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SteamProtonChecker.CoreModels;
using SteamProtonChecker.Infrastructure;
using SteamProtonChecker.Services;
using SteamProtonChecker.ViewModels;

namespace SteamProtonChecker.Views;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettingsStore.Load();
    private readonly HttpClient _http;
    private readonly LibraryChecker _checker;

    private List<GameRow> _rows = new();
    private DataGridCollectionView? _view;
    private CancellationTokenSource? _cts;

    public MainWindow()
    {
        InitializeComponent();

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SteamProtonChecker/1.0");
        _checker = new LibraryChecker(new GameRepository(), _http);

        // Show the cached library straight away; Sync refreshes it.
        LoadRows(_checker.GetCachedLibrary());

        Opened += OnOpened;
        SearchBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                ApplyFilter();
            }
        };
    }

    // ---------- Startup ----------

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (!_settings.IsConfigured)
        {
            StatusText.Text = "Enter your Steam API key and SteamID64 to get started.";
            await OpenSettingsAsync();
        }
        else if (_rows.Count == 0)
        {
            StatusText.Text = "Click Sync to load your library.";
        }
        else
        {
            StatusText.Text = $"{_rows.Count} games from the last sync. Click Sync to refresh.";
        }
    }

    // ---------- Sync ----------

    private async void SyncButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!_settings.IsConfigured)
        {
            await OpenSettingsAsync();
            if (!_settings.IsConfigured)
            {
                return;
            }
        }

        _cts = new CancellationTokenSource();
        SetBusy(true);
        ShowWarnings(Array.Empty<string>());

        var progress = new Progress<SyncProgress>(p =>
        {
            StatusText.Text = p.Total > 0 ? $"{p.Message} {p.Current}/{p.Total}" : p.Message;
            SyncProgressBar.IsIndeterminate = p.Total == 0;
            SyncProgressBar.Maximum = Math.Max(p.Total, 1);
            SyncProgressBar.Value = p.Current;
        });

        try
        {
            var result = await _checker.SyncAsync(_settings, progress, _cts.Token);
            LoadRows(result.Games);
            ShowWarnings(result.Warnings);
            StatusText.Text = $"Synced {result.Games.Count} games.";
        }
        catch (OperationCanceledException)
        {
            // Whatever finished before the cancel was cached; show it.
            LoadRows(_checker.GetCachedLibrary());
            StatusText.Text = "Sync cancelled. Results gathered so far were kept.";
        }
        catch (Exception ex)
        {
            ShowWarnings(new[] { ex.Message });
            StatusText.Text = "Sync failed.";
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => _cts?.Cancel();

    private void SetBusy(bool busy)
    {
        SyncButton.IsEnabled = !busy;
        CancelButton.IsVisible = busy;
        SyncProgressBar.IsVisible = busy;
    }

    private void ShowWarnings(IReadOnlyCollection<string> warnings)
    {
        WarningText.Text = string.Join(Environment.NewLine, warnings);
        WarningBorder.IsVisible = warnings.Count > 0;
    }

    // ---------- Settings ----------

    private async void SettingsButton_Click(object? sender, RoutedEventArgs e) => await OpenSettingsAsync();

    private async Task OpenSettingsAsync()
    {
        var dialog = new SettingsWindow(_settings);
        var saved = await dialog.ShowDialog<bool>(this);
        if (saved)
        {
            AppSettingsStore.Save(_settings);
            StatusText.Text = "Settings saved. Click Sync to refresh.";
        }
    }

    // ---------- Grid and filters ----------

    private void LoadRows(IEnumerable<CheckedGame> games)
    {
        // Worst tier first, then by name. Clicking a column header re-sorts from here.
        _rows = games
            .Select(g => new GameRow(g))
            .OrderBy(r => r.TierRank)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _view = new DataGridCollectionView(_rows) { Filter = FilterPredicate };
        GamesGrid.ItemsSource = _view;
        UpdateSummary();
    }

    private bool FilterPredicate(object item) => item is GameRow row && RowMatches(row);

    private bool RowMatches(GameRow row)
    {
        var game = row.Game;

        var sourceOk =
            (LibraryCheck.IsChecked == true && game.IsOwned) ||
            (FamilyCheck.IsChecked == true && game.IsFamilyShared) ||
            (WishlistCheck.IsChecked == true && game.IsWishlisted);
        if (!sourceOk)
        {
            return false;
        }

        var query = SearchBox.Text?.Trim();
        if (!string.IsNullOrEmpty(query) && row.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        return TierFilter.SelectedIndex switch
        {
            1 => row.Tier is ProtonTier.Borked or ProtonTier.Bronze,
            2 => row.Tier is ProtonTier.Borked or ProtonTier.Bronze or ProtonTier.Silver,
            3 => row.Tier is ProtonTier.Pending or ProtonTier.Unrated,
            4 => row.Tier is ProtonTier.Gold or ProtonTier.Platinum or ProtonTier.Native,
            _ => true,
        };
    }

    private void ApplyFilter()
    {
        // Filter events fire while the window is still initializing, before any rows exist.
        if (_view is null)
        {
            return;
        }

        _view.Refresh();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var visible = _rows.Where(RowMatches).ToList();

        var parts = new List<string>();
        foreach (var tier in Enum.GetValues<ProtonTier>())
        {
            var count = visible.Count(r => r.Tier == tier);
            if (count > 0)
            {
                parts.Add($"{tier} {count}");
            }
        }

        SummaryText.Text = $"Showing {visible.Count} of {_rows.Count}" +
                           (parts.Count > 0 ? "   |   " + string.Join("  ·  ", parts) : string.Empty);
    }

    private void Filter_Changed(object? sender, RoutedEventArgs e) => ApplyFilter();

    private void TierFilter_Changed(object? sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void GamesGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (GamesGrid.SelectedItem is not GameRow row)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo($"https://www.protondb.com/app/{row.AppId}") { UseShellExecute = true });
        }
        catch (Exception)
        {
            StatusText.Text = $"Couldn't open the browser. ProtonDB page: https://www.protondb.com/app/{row.AppId}";
        }
    }
}
