using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Mizon.API.Client.Operations.Media;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Pages;

public sealed partial class SettingsPage : Page
{
    private CancellationTokenSource? _pageCancellation;
    private CancellationTokenSource? _sectionCancellation;
    private bool _selectingNavigationItem;
    private int? _pendingSection;
    private bool _released;
    private bool _loaded;
    private bool? _wideLayout;
    private bool? _compactLayout;
    private SettingsSectionTransition? _sectionTransition;
    public SettingsPageViewModel ViewModel { get; private set; }
    public LocalizationService Localization => LocalizationService.Current;
    public AppearanceSettingsService Appearance => AppearanceSettingsService.Current;
    internal string SelectedSectionSlug => GetSectionSlug(_pendingSection ?? ViewModel.SelectedSection);

    public SettingsPage()
    {
        ViewModel = CreateViewModel();
        InitializeComponent();
        SettingsNavigation.SelectedItem = AccountNavigation;
    }

    internal static int GetSectionIndex(string? section) => section?.ToLowerInvariant() switch
    {
        null or "account" => 0,
        "security" => 1,
        "language" => 2,
        "appearance" => 3,
        "notifications" => 4,
        "storage" => 5,
        "updates" => 6,
        "platforms" => 7,
        "ai-connections" => 8,
        _ => throw new ArgumentException("Unknown settings section.", nameof(section))
    };

    internal static string GetSectionSlug(int index) => index switch
    {
        0 => "account", 1 => "security", 2 => "language", 3 => "appearance",
        4 => "notifications", 5 => "storage", 6 => "updates", 7 => "platforms",
        8 => "ai-connections",
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        if (args.Parameter is not Dictionary<string, object> parameters
            || !parameters.TryGetValue(PageConstants.SETTINGS_SECTION, out var value)
            || value is not string section) return;
        var index = GetSectionIndex(section);
        SynchronizeNavigationItem(index);
        if (_released) _pendingSection = index;
        else ApplySection(index);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs args)
    {
        _sectionCancellation?.Cancel();
        base.OnNavigatedFrom(args);
    }

    internal Task OpenSectionAsync(string? section = null, CancellationToken cancellationToken = default) =>
        SelectSectionAsync(GetSectionIndex(section), cancellationToken);

    private static SettingsPageViewModel CreateViewModel()
    {
        var services = ((App)Application.Current).Services;
        return new(services.GetRequiredService<HomePageViewModel>(), services.GetRequiredService<MediaDownloader>());
    }

    public string Text(string key, int revision) => Localization.GetString("Settings/" + key, revision);
    public bool Not(bool value) => !value;

    public string AppearanceStatus(AppearanceState state, int revision)
    {
        if (!state.IsPreferenceSaved) return Text("AppearanceSaveError", revision);
        var key = state.FallbackReason switch
        {
            AppearanceFallbackReason.HighContrast => "BackdropHighContrast",
            AppearanceFallbackReason.TransparencyDisabled => "BackdropTransparencyDisabled",
            AppearanceFallbackReason.Unsupported => "BackdropUnsupported",
            AppearanceFallbackReason.Unavailable => "BackdropUnavailable",
            _ => null
        };
        return key is null ? string.Empty : Text(key, revision);
    }

    public Visibility AppearanceStatusVisibility(AppearanceState state) =>
        !state.IsPreferenceSaved || state.FallbackReason is not (AppearanceFallbackReason.None or AppearanceFallbackReason.WindowUnavailable)
            ? Visibility.Visible : Visibility.Collapsed;

    private async void Page_Loaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        if (_released)
        {
            var selected = _pendingSection ?? ViewModel.SelectedSection;
            ViewModel = CreateViewModel();
            ViewModel.SelectedSection = selected;
            _pendingSection = null;
            _released = false;
        }
        _pageCancellation = new CancellationTokenSource();
        _sectionTransition = new SettingsSectionTransition(SectionHost);
        Bindings.Update();
        UpdateLayoutMode(ActualWidth);
        if (ViewModel.IsStorage) await ViewModel.RefreshStorageAsync(_pageCancellation.Token);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded || _released) return;
        _loaded = false;
        _released = true;
        _sectionCancellation?.Cancel();
        _sectionTransition?.Dispose();
        _sectionTransition = null;
        _pageCancellation?.Cancel();
        _pageCancellation?.Dispose();
        _pageCancellation = null;
        Bindings.StopTracking();
        ViewModel.Dispose();
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs args) => UpdateLayoutMode(args.NewSize.Width);

    private void UpdateLayoutMode(double width)
    {
        if (SettingsNavigation is null || width <= 0) return;
        var wide = width >= 1040;
        var compact = width < 640;
        // Resize changes only these layout properties when crossing a breakpoint.
        if (_wideLayout == wide && _compactLayout == compact) return;
        _wideLayout = wide;
        _compactLayout = compact;
        SettingsNavigation.PaneDisplayMode = wide ? NavigationViewPaneDisplayMode.Left : NavigationViewPaneDisplayMode.Top;
        SettingsNavigation.IsPaneOpen = wide;
        SidebarProfile.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        SidebarFooter.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        HeaderStatus.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        var inset = compact ? 12 : wide ? 24 : 18;
        PageLayout.Padding = new Thickness(inset, compact ? 12 : 18, inset, 12);
        SectionHost.Margin = wide ? new Thickness(20, 0, 4, 24) : new Thickness(4, 14, 4, 20);
    }

    private async void SettingsNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_selectingNavigationItem || args.SelectedItem is not NavigationViewItem { Tag: string tag }
            || !int.TryParse(tag, NumberStyles.None, CultureInfo.InvariantCulture, out var index)) return;
        try { await SelectSectionAsync(index); }
        catch (OperationCanceledException) { }
        catch (Exception error) { AppLog.Error("Settings section could not be opened.", error); }
    }

    private void SynchronizeNavigationItem(int index)
    {
        _selectingNavigationItem = true;
        try
        {
            foreach (var entry in SettingsNavigation.MenuItems)
            {
                if (entry is NavigationViewItem { Tag: string tag } item
                    && int.TryParse(tag, NumberStyles.None, CultureInfo.InvariantCulture, out var itemIndex)
                    && itemIndex == index)
                {
                    SettingsNavigation.SelectedItem = item;
                    break;
                }
            }
        }
        finally { _selectingNavigationItem = false; }
    }

    private void ApplySection(int index)
    {
        ViewModel.SelectedSection = index;
        ViewModel.ShowStatus(string.Empty);
        DetailsScroll?.ChangeView(null, 0, null, true);
    }

    private async Task SelectSectionAsync(int index, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (index is < 0 or > 8) throw new ArgumentOutOfRangeException(nameof(index));
        using var request = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _pageCancellation?.Token ?? CancellationToken.None);
        var previous = _sectionCancellation;
        _sectionCancellation = request;
        previous?.Cancel();
        var token = request.Token;
        try
        {
            SynchronizeNavigationItem(index);
            if (_released)
            {
                _pendingSection = index;
                return;
            }
            if (index == ViewModel.SelectedSection) return;
            if (!_loaded || _sectionTransition is not { } transition)
            {
                ApplySection(index);
                return;
            }

            await transition.RunAsync(() => ApplySection(index), index >= ViewModel.SelectedSection,
                Localization.IsPersian, token);
            token.ThrowIfCancellationRequested();
            if (_loaded && ReferenceEquals(_sectionCancellation, request) && ViewModel.IsStorage)
                await ViewModel.RefreshStorageAsync(token);
            token.ThrowIfCancellationRequested();
        }
        finally
        {
            if (ReferenceEquals(_sectionCancellation, request))
            {
                _sectionCancellation = null;
                // A canceled link must not leave the navigation item pointing to a
                // section whose content never replaced the previous one.
                if (token.IsCancellationRequested && !_released)
                    SynchronizeNavigationItem(ViewModel.SelectedSection);
            }
        }
    }

    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_loaded || sender is not ComboBox { SelectedIndex: >= 0 and <= 2 } selector) return;
        Appearance.SetThemeMode(selector.SelectedIndex);
    }

    private void BackdropSelector_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_loaded || sender is not ComboBox { SelectedIndex: >= 0 and <= 4 } selector) return;
        Appearance.SetBackdropMode(selector.SelectedIndex);
    }

    private async void RefreshAccount_Click(object sender, RoutedEventArgs args)
    {
        if (_pageCancellation is not { } lifetime) return;
        try { await ViewModel.Account.RefreshProfileAsync(lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    private async void RefreshStorage_Click(object sender, RoutedEventArgs args)
    {
        if (_pageCancellation is { } lifetime) await ViewModel.RefreshStorageAsync(lifetime.Token);
    }

    private async void OpenFolder_Click(object sender, RoutedEventArgs args)
    {
        if (!_loaded || sender is not Button { Tag: string kind }) return;
        var path = kind switch
        {
            "cache" => ViewModel.Storage.CacheDirectory,
            "data" => ViewModel.Storage.PackageDataDirectory,
            "logs" => ViewModel.Storage.LogDirectory,
            _ => null
        };
        if (path is not null) await ViewModel.OpenFolderAsync(path);
    }

    private async void WindowsSettings_Click(object sender, RoutedEventArgs args)
    {
        if (_loaded && sender is Button { Tag: string uri }) await ViewModel.OpenWindowsSettingsAsync(uri);
    }

    private void CopyVersion_Click(object sender, RoutedEventArgs args) => ViewModel.CopyVersionInformation();
}
