using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TTGClient.Models;
using TTGClient.Services;
using TTGClient.Views;

namespace TTGClient;

public partial class MainWindow : Window
{
    private readonly SettingsService _settings = new();
    private readonly ModrinthService _modrinth = new();
    private readonly CurseForgeService _curseForge;
    private readonly ObservableCollection<ModProject> _projects = new();
    private CancellationTokenSource? _searchCancellation;
    private bool _initialSearchStarted;
    private string _selectedSource = "All sources";
    private int _page = 1;
    private bool _modrinthHasNext;
    private bool _curseForgeHasNext;

    public MainWindow()
    {
        InitializeComponent();
        _curseForge = new CurseForgeService(_settings);
        ProjectsList.ItemsSource = _projects;
        UpdateSourceButtonStyles();
        UpdateApiKeyStatus();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialSearchStarted) return;
        _initialSearchStarted = true;
        await SearchAsync(resetPage: true);
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync(resetPage: true);

    private async void QueryTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SearchAsync(resetPage: true);
        }
    }

    private async void AllSources_Click(object sender, RoutedEventArgs e) => await ChangeSourceAsync("All sources");
    private async void Modrinth_Click(object sender, RoutedEventArgs e) => await ChangeSourceAsync("Modrinth");
    private async void CurseForge_Click(object sender, RoutedEventArgs e) => await ChangeSourceAsync("CurseForge");

    private async Task ChangeSourceAsync(string source)
    {
        _selectedSource = source;
        _page = 1;
        UpdateSourceButtonStyles();
        await SearchAsync(resetPage: true);
    }

    private async Task SearchAsync(bool resetPage)
    {
        if (resetPage) _page = 1;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        var token = _searchCancellation.Token;
        var filters = BuildFilters();

        LoadingOverlay.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
        StatusBanner.Visibility = Visibility.Collapsed;
        Mouse.OverrideCursor = Cursors.Wait;

        try
        {
            var includeModrinth = _selectedSource is "All sources" or "Modrinth";
            var includeCurseForge = _selectedSource is "All sources" or "CurseForge";
            Task<(SearchPage? Page, string? Error)> modTask = includeModrinth ? SearchModrinthSafeAsync(filters, token) : Task.FromResult<(SearchPage?, string?)>((null, null));
            Task<(SearchPage? Page, string? Error)> curseTask = includeCurseForge ? SearchCurseForgeSafeAsync(filters, token) : Task.FromResult<(SearchPage?, string?)>((null, null));
            await Task.WhenAll(modTask, curseTask);
            if (token.IsCancellationRequested) return;

            var (modPage, modError) = await modTask;
            var (cursePage, curseError) = await curseTask;
            _modrinthHasNext = modPage?.HasNext ?? false;
            _curseForgeHasNext = cursePage?.HasNext ?? false;

            var results = new List<ModProject>();
            if (modPage is not null) results.AddRange(modPage.Projects);
            if (cursePage is not null) results.AddRange(cursePage.Projects);
            if (_selectedSource == "All sources")
                results = SortCombinedResults(results, filters.Sort);

            _projects.Clear();
            foreach (var project in results) _projects.Add(project);

            var total = (modPage?.TotalCount ?? 0) + (cursePage?.TotalCount ?? 0);
            ResultsCountText.Text = total == 0 ? $"{results.Count} results" : $"{total:N0} matching projects";
            PageText.Text = _selectedSource == "All sources"
                ? $"Page {_page}  ·  up to {filters.PageSize} results per source"
                : $"Page {_page}  ·  {(_selectedSource == "Modrinth" ? modPage?.TotalCount ?? 0 : cursePage?.TotalCount ?? 0):N0} total";
            PreviousPageButton.IsEnabled = _page > 1;
            NextPageButton.IsEnabled = _selectedSource switch
            {
                "Modrinth" => _modrinthHasNext,
                "CurseForge" => _curseForgeHasNext,
                _ => _modrinthHasNext || _curseForgeHasNext
            };

            var messages = new List<string>();
            if (!string.IsNullOrWhiteSpace(modError)) messages.Add(modError);
            if (!string.IsNullOrWhiteSpace(curseError)) messages.Add(curseError);
            if (!IsAny(filters.Environment) && includeCurseForge)
                messages.Add("Environment filtering is supported by Modrinth search, but the CurseForge search endpoint does not expose the same filter.");
            if (!IsAny(filters.Loader) && IsAny(filters.Version) && includeCurseForge)
                messages.Add("CurseForge requires a Minecraft version to apply its mod-loader filter; choose a version to ensure this filter is applied there.");

            if (messages.Count > 0)
            {
                StatusText.Text = string.Join("  •  ", messages);
                StatusBanner.Visibility = Visibility.Visible;
            }

            if (_projects.Count == 0)
            {
                EmptyTitle.Text = messages.Count > 0 ? "No results available" : "No mods found";
                EmptyMessage.Text = messages.Count > 0
                    ? "Check the message above, your API key, or try searching Modrinth first."
                    : "Try another keyword or adjust your filters.";
                EmptyState.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException)
        {
            // A newer search replaced this one.
        }
        catch (Exception ex)
        {
            ShowStatus($"Search failed: {ex.Message}");
            _projects.Clear();
            EmptyTitle.Text = "Couldn't load projects";
            EmptyMessage.Text = "Check your network connection and try again.";
            EmptyState.Visibility = Visibility.Visible;
            ResultsCountText.Text = "Search error";
            NextPageButton.IsEnabled = false;
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                Mouse.OverrideCursor = null;
            }
        }
    }

    private async Task<(SearchPage? Page, string? Error)> SearchModrinthSafeAsync(SearchFilters filters, CancellationToken token)
    {
        try { return (await _modrinth.SearchAsync(filters, token), null); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return (null, $"Modrinth: {ex.Message}"); }
    }

    private async Task<(SearchPage? Page, string? Error)> SearchCurseForgeSafeAsync(SearchFilters filters, CancellationToken token)
    {
        try { return (await _curseForge.SearchAsync(filters, token), null); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return (null, $"CurseForge: {ex.Message}"); }
    }

    private SearchFilters BuildFilters()
    {
        return new SearchFilters(
            QueryTextBox.Text?.Trim() ?? string.Empty,
            GetComboValue(VersionComboBox, "1.20.1"),
            GetComboValue(LoaderComboBox, "Any"),
            GetComboValue(CategoryComboBox, "All categories"),
            GetComboValue(EnvironmentComboBox, "All environments"),
            GetComboValue(SortComboBox, "Relevance"),
            _page,
            20);
    }

    private static string GetComboValue(ComboBox combo, string fallback)
    {
        if (combo.SelectedItem is ComboBoxItem selected && selected.Content is not null)
            return selected.Content.ToString() ?? fallback;
        if (!string.IsNullOrWhiteSpace(combo.Text)) return combo.Text.Trim();
        return fallback;
    }

    private static bool IsAny(string value) => string.IsNullOrWhiteSpace(value) || value == "Any" || value == "All categories" || value == "All environments";

    private static List<ModProject> SortCombinedResults(List<ModProject> results, string sort) => sort switch
    {
        "Downloads" => results.OrderByDescending(p => p.Downloads).ToList(),
        "Recently Updated" or "Newest" => results.OrderByDescending(p => p.ModifiedAt ?? DateTimeOffset.MinValue).ToList(),
        "Name" => results.OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase).ToList(),
        "Followers" => results.OrderByDescending(p => p.Followers).ToList(),
        _ => results
    };

    private async void PreviousPage_Click(object sender, RoutedEventArgs e)
    {
        if (_page <= 1) return;
        _page--;
        await SearchAsync(resetPage: false);
    }

    private async void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if (!NextPageButton.IsEnabled) return;
        _page++;
        await SearchAsync(resetPage: false);
    }

    private void ProjectDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ModProject project })
        {
            var details = new ProjectDetailsWindow(project) { Owner = this };
            details.ShowDialog();
        }
    }

    private void BrowseNav_Click(object sender, RoutedEventArgs e)
    {
        BrowseView.Visibility = Visibility.Visible;
        PlaceholderView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        WorkspaceTitle.Text = "Browse mods";
        WorkspaceSubtitle.Text = "Discover projects from Modrinth and CurseForge.";
        UpdateNavigationStyles(BrowseNavButton);
    }

    private void InstancesNav_Click(object sender, RoutedEventArgs e)
    {
        BrowseView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Visible;
        PlaceholderGlyph.Text = "▦";
        PlaceholderTitle.Text = "Instances";
        PlaceholderDescription.Text = "Create isolated Minecraft profiles with a game version, loader, Java runtime and RAM settings. This is planned for Phase 2.";
        WorkspaceTitle.Text = "Instances";
        WorkspaceSubtitle.Text = "Keep each modpack and Minecraft setup separate.";
        UpdateNavigationStyles(InstancesNavButton);
    }

    private void DownloadsNav_Click(object sender, RoutedEventArgs e)
    {
        BrowseView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Visible;
        PlaceholderGlyph.Text = "↓";
        PlaceholderTitle.Text = "Downloads";
        PlaceholderDescription.Text = "A central download queue with progress, retry and file status is planned for Phase 3. Phase 1 only opens official project pages.";
        WorkspaceTitle.Text = "Downloads";
        WorkspaceSubtitle.Text = "Download management will be added in a later phase.";
        UpdateNavigationStyles(DownloadsNavButton);
    }

    private void SettingsNav_Click(object sender, RoutedEventArgs e)
    {
        BrowseView.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
        WorkspaceTitle.Text = "Settings";
        WorkspaceSubtitle.Text = "Configure your mod library connections.";
        UpdateNavigationStyles(SettingsNavButton);
        UpdateApiKeyStatus();
    }

    private void UpdateNavigationStyles(Button selected)
    {
        foreach (var button in new[] { BrowseNavButton, InstancesNavButton, DownloadsNavButton, SettingsNavButton })
        {
            button.Background = button == selected ? (Brush)FindResource("AccentDarkBrush") : Brushes.Transparent;
            button.BorderBrush = button == selected ? (Brush)FindResource("AccentDarkBrush") : Brushes.Transparent;
        }
    }

    private void UpdateSourceButtonStyles()
    {
        var selectedBrush = (Brush)FindResource("AccentDarkBrush");
        var idleBrush = (Brush)new BrushConverter().ConvertFromString("#252B38")!;
        var buttons = new Dictionary<string, Button>
        {
            ["All sources"] = AllSourcesButton,
            ["Modrinth"] = ModrinthButton,
            ["CurseForge"] = CurseForgeButton
        };
        foreach (var (name, button) in buttons)
        {
            button.Background = name == _selectedSource ? selectedBrush : idleBrush;
            button.BorderBrush = name == _selectedSource ? selectedBrush : (Brush)FindResource("BorderBrushSoft");
        }
    }

    private void SaveApiKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings.SaveCurseForgeApiKey(CurseForgeKeyBox.Password);
            CurseForgeKeyBox.Clear();
            ApiKeyFeedbackText.Text = "Key saved locally for this Windows user.";
            UpdateApiKeyStatus();
        }
        catch (Exception ex)
        {
            ApiKeyFeedbackText.Text = $"Couldn't save the key: {ex.Message}";
        }
    }

    private void ClearApiKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings.SaveCurseForgeApiKey(string.Empty);
            CurseForgeKeyBox.Clear();
            ApiKeyFeedbackText.Text = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY") is { Length: > 0 }
                ? "Saved key cleared. The environment variable still takes priority."
                : "Saved key cleared.";
            UpdateApiKeyStatus();
        }
        catch (Exception ex)
        {
            ApiKeyFeedbackText.Text = $"Couldn't clear the key: {ex.Message}";
        }
    }

    private void UpdateApiKeyStatus()
    {
        var environmentKeyPresent = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY"));
        var configured = _curseForge?.HasApiKey ?? !string.IsNullOrWhiteSpace(_settings.GetCurseForgeApiKey());
        ApiKeyStateText.Text = configured
            ? (environmentKeyPresent ? "Configured via CURSEFORGE_API_KEY environment variable" : "Configured · encrypted for this Windows user")
            : "Not configured · CurseForge search needs a key";
    }

    private void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusBanner.Visibility = Visibility.Visible;
    }
}
