using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Extensions.DependencyInjection;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels.Items;
using Mizon.Windows.ViewModels;
using System;
using System.Threading.Tasks;

namespace Mizon.Windows.UserControls;

public sealed partial class MusicLibraryPanel : UserControl
{
    public LocalizationService Localization => LocalizationService.Current;
    public FluentIcons.Common.Symbol ForwardSymbol(bool isPersian) =>
        isPersian ? FluentIcons.Common.Symbol.ArrowLeft : FluentIcons.Common.Symbol.ArrowRight;

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(MusicLibraryViewModel), typeof(MusicLibraryPanel),
        new PropertyMetadata(null, ViewModelChanged));

    private bool _initialized;
    private bool? _wide;
    private MenuFlyout? _playlistMenu;
    private bool _starting;
    private bool _localizationAttached;
    private string? _statusResourceKey;

    public MusicLibraryPanel()
    {
        InitializeComponent();
        _initialized = true;
    }

    public MusicLibraryViewModel? ViewModel
    {
        get => (MusicLibraryViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public event EventHandler? RecentRequested;
    public event EventHandler? RefreshRequested;

    private static void ViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var panel = (MusicLibraryPanel)sender;
        if (panel._initialized) panel.Bindings.Update();
    }

    private void Panel_Loaded(object sender, RoutedEventArgs args)
    {
        if (!IsLoaded) return;
        if (!_localizationAttached)
        {
            _localizationAttached = true;
            Localization.PropertyChanged += LanguageChanged;
        }
        ViewModel?.RefreshLocalization();
        UpdateLayout(ActualWidth);
    }

    private void LanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(LocalizationService.Revision) || !IsLoaded) return;
        ViewModel?.RefreshLocalization();
        if (_statusResourceKey is { } key) ActionStatus.Text = Localization.GetString(key);
    }

    private void Panel_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (args.NewSize.Width != args.PreviousSize.Width) UpdateLayout(args.NewSize.Width);
    }

    private void UpdateLayout(double width)
    {
        if (!_initialized || !double.IsFinite(width) || width <= 0) return;
        var wide = width >= 560;
        if (_wide == wide) return;
        _wide = wide;

        ListColumn.Width = new GridLength(wide ? 1.35 : 1, GridUnitType.Star);
        SummaryColumn.Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        LibraryBody.ColumnSpacing = wide ? 14 : 0;
        LibraryBody.RowSpacing = wide ? 0 : 11;
        Grid.SetColumn(SummaryArea, wide ? 1 : 0);
        Grid.SetRow(PlaylistArea, wide ? 0 : 1);
        ListRow.Height = wide ? new GridLength(0) : GridLength.Auto;
    }

    private NavigationService Navigation => ((App)Application.Current).Services.GetRequiredService<NavigationService>();

    private void More_Click(object sender, RoutedEventArgs args) => Navigation.OpenMusicPlaylists();

    private void Playlist_Click(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: MusicLibraryPlaylistItemViewModel item })
            Navigation.OpenMusicPlaylist(item.PublicId);
    }

    private void Playlist_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: MusicLibraryPlaylistItemViewModel item } anchor) return;
        _playlistMenu?.Hide();
        _playlistMenu = MusicPlaylistMenu.Show(anchor, args, item.PublicId, item.CanPlay,
            () => PlayAsync(item.PublicId),
            () => { Navigation.OpenMusicPlaylist(item.PublicId); return Task.CompletedTask; },
            ShowStatus);
    }

    private async Task PlayAsync(Guid id)
    {
        if (_starting) return;
        _starting = true;
        try
        {
            ShowStatus(Localization.GetString("MusicWidgets/PlaylistPlaying"));
            var player = ((App)Application.Current).Services.GetRequiredService<MusicPlaylistPlaybackService>();
            if (!await player.StartAsync(id)) ShowStatus(Localization.GetString("MusicWidgets/PlaylistUnavailable"));
        }
        finally { _starting = false; }
    }

    private void ShowStatus(string text)
    {
        if (!IsLoaded) return;
        _statusResourceKey = null;
        foreach (var key in new[] { "MusicWidgets/PlaylistPlaying", "MusicWidgets/PlaylistUnavailable",
            "MusicWidgets/PlaylistCopied", "MusicWidgets/PlaylistActionError" })
            if (Localization.GetString(key) == text) { _statusResourceKey = key; break; }
        ActionStatus.Text = text;
        ActionStatus.Visibility = Visibility.Visible;
        AccessibilityAnnouncements.Announce(this, text, "playlist-action");
    }

    private void Panel_Unloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded) return;
        Localization.PropertyChanged -= LanguageChanged;
        _localizationAttached = false;
        _statusResourceKey = null;
        _playlistMenu?.Hide();
        _playlistMenu = null;
        ActionStatus.Text = string.Empty;
        ActionStatus.Visibility = Visibility.Collapsed;
    }

    private void Recent_Click(object sender, RoutedEventArgs args) => RecentRequested?.Invoke(this, EventArgs.Empty);

    private void Refresh_Click(object sender, RoutedEventArgs args) => RefreshRequested?.Invoke(this, EventArgs.Empty);
}
