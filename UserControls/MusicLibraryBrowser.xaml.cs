using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Mizon.API;
using Mizon.API.Client.Operations.Media;
using Mizon.Windows.Common;
using Mizon.Windows.Pages;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.UserControls;

public sealed partial class MusicLibraryBrowser : UserControl, IDisposable
{
    private readonly List<string?> _cursors = [null];
    private readonly MusicPlaylistPlaybackService _playback;
    private readonly NavigationService _navigation;
    private MenuFlyout? _menu;
    private Guid? _playlistId;
    private int _page, _revision;
    private bool _disposed, _busy, _starting;
    private int _layoutMode = -1;
    private ItemsWrapGrid? _playlistGridPanel;
    private ScalarKeyFrameAnimation? _headerEntrance;
    private static readonly UISettings AccessibilitySettings = new();
    private CancellationTokenSource? _playCancellation;
    private bool _hasPresented;
    private double _savedMusicOffset;
    private MusicCoverTransition? _returnCover;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _returnTimeout;
    private CancellationTokenSource? _restoreCancellation;
    private CancellationTokenSource? _coverWarmupCancellation;
    private Task<ImageSource?>? _coverWarmup;
    private string? _coverWarmupPath;
    private int _coverWarmupPixels;
    public MusicLibraryViewModel Library { get; }
    public MusicPlaylistDetailsViewModel Details { get; }

    public MusicLibraryBrowser()
    {
        var services = ((App)Application.Current).Services;
        var api = services.GetRequiredService<MizonApi>();
        var downloader = services.GetRequiredService<MediaDownloader>();
        Library = new(api, downloader) { CoverDecodeWidth = 400 };
        Details = new(api, downloader);
        _playback = services.GetRequiredService<MusicPlaylistPlaybackService>();
        _navigation = services.GetRequiredService<NavigationService>();
        InitializeComponent();
    }

    public async Task OpenAsync(Guid? playlistId = null)
    {
        if (_disposed) return;
        ClearCoverWarmup();
        _playCancellation?.Cancel();
        _playCancellation?.Dispose();
        _playCancellation = null;
        _starting = false;
        Library.CancelLoading();
        Details.CancelLoading();
        _playlistId = playlistId;
        _page = 0; _cursors.Clear(); _cursors.Add(null);
        var detail = playlistId.HasValue;
        PlaylistList.Visibility = detail ? Visibility.Collapsed : Visibility.Visible;
        MusicList.Visibility = detail ? Visibility.Visible : Visibility.Collapsed;
        PlaylistActions.Visibility = detail ? Visibility.Visible : Visibility.Collapsed;
        LibrarySummary.Visibility = detail ? Visibility.Collapsed : Visibility.Visible;
        LibraryHint.Visibility = detail ? Visibility.Collapsed : Visibility.Visible;
        HeroCover.Visibility = detail ? Visibility.Visible : Visibility.Collapsed;
        SectionHeader.Title = detail ? "آهنگ‌های این پلی‌لیست" : "مجموعه‌های تو";
        Heading.Text = detail ? "پلی‌لیست" : "پلی‌لیست‌های تو";
        LibraryOwnerLabel.Text = detail ? "پلی‌لیست" : "کتابخانهٔ شخصی";
        Subtitle.Text = detail ? "در حال دریافت آهنگ‌ها…" : "مجموعه‌ای با سلیقهٔ خودت";
        _layoutMode = -1;
        ApplyResponsiveLayout();
        // A new destination owns a fresh initial skeleton instead of showing the old list.
        LoadingHost.IsLoading = true;
        LoadingHost.ResetPresentation();
        await LoadPageAsync(null, 0);
    }

    private async Task LoadPageAsync(string? cursor, int page)
    {
        if (_disposed) return;
        var revision = ++_revision;
        _busy = true; Message.IsOpen = false; EmptyState.Visibility = Visibility.Collapsed;
        LoadingHost.IsLoading = true;
        UpdatePaging();
        var success = _playlistId is Guid id
            ? await Details.LoadAsync(id, cursor, page * 20) : await Library.LoadPageAsync(cursor, 20);
        if (_disposed || revision != _revision) return;
        _busy = false; LoadingHost.IsLoading = false;
        if (success)
        {
            _page = page;
            if (_cursors.Count <= page) _cursors.Add(cursor);
            else _cursors[page] = cursor;
            if (_cursors.Count > page + 1) _cursors.RemoveRange(page + 1, _cursors.Count - page - 1);
            if (_playlistId.HasValue)
            {
                Heading.Text = Details.Title;
                LibraryOwnerLabel.Text = Details.IsOwner ? "پلی‌لیست تو" : "پلی‌لیست دوستانت";
                Subtitle.Text = Details.CountText;
                EmptyText.Text = "این پلی‌لیست هنوز آهنگی ندارد.";
                EmptyState.Visibility = Details.EmptyVisibility;
                if (Details.Musics.Count > 0) MusicList.ScrollIntoView(Details.Musics[0]);
            }
            else
            {
                Heading.Text = "پلی‌لیست‌های تو";
                Subtitle.Text = Library.SummaryText;
                EmptyText.Text = "هنوز پلی‌لیستی نداری؛ از صفحهٔ یک آهنگ شروع کن.";
                EmptyState.Visibility = Library.EmptyVisibility;
                if (Library.VisiblePlaylists.Count > 0) PlaylistList.ScrollIntoView(Library.VisiblePlaylists[0]);
            }
            AccessibilityAnnouncements.Announce(this, $"{Heading.Text}، صفحهٔ {_page + 1} دریافت شد", "library-page");
            ToolTipService.SetToolTip(Heading, Heading.Text);
        }
        else ShowStatus(_playlistId.HasValue
            ? "پلی‌لیست در دسترس نیست یا متعلق به حساب دیگری است. برای تلاش دوباره، به‌روزرسانی را بزن."
            : "دریافت پلی‌لیست‌ها ممکن نشد. برای تلاش دوباره، به‌روزرسانی را بزن.", true);
        UpdatePaging();
    }

    private void UpdatePaging()
    {
        PreviousButton.IsEnabled = !_busy && _page > 0;
        NextButton.IsEnabled = !_busy && !(_playlistId.HasValue ? Details.HasError : Library.HasError)
            && (_playlistId.HasValue ? Details.NextCursor : Library.NextCursor) is not null;
        PageNumber.Text = $"صفحهٔ {_page + 1:N0}";
    }

    private async void Next_Click(object sender, RoutedEventArgs args)
    {
        if (!_busy && (_playlistId.HasValue ? Details.NextCursor : Library.NextCursor) is { } cursor)
            await LoadPageAsync(cursor, _page + 1);
    }
    private async void Previous_Click(object sender, RoutedEventArgs args)
    {
        if (!_busy && _page > 0) await LoadPageAsync(_cursors[_page - 1], _page - 1);
    }
    private async void Refresh_Click(object sender, RoutedEventArgs args) => await LoadPageAsync(null, 0);
    private void AllPlaylists_Click(object sender, RoutedEventArgs args) => _navigation.OpenMusicPlaylists();
    private void Playlist_Click(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is MusicLibraryPlaylistItemViewModel item) _navigation.OpenMusicPlaylist(item.PublicId);
    }
    private void Music_Click(object sender, ItemClickEventArgs args)
    {
        if (_disposed || args.ClickedItem is not DiscoveryMusicItemViewModel item) return;
        _navigation.InternalNavigate(typeof(MusicsPage), new() { [PageConstants.MUSIC_ID] = item.PublicId },
            FindTrackCover(MusicList.ContainerFromItem(item)), WarmCover(item));
    }

    private void MusicRow_PointerEntered(object sender, PointerRoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: DiscoveryMusicItemViewModel item }) _ = WarmCover(item);
    }

    private void MusicRow_GotFocus(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: DiscoveryMusicItemViewModel item }) _ = WarmCover(item);
    }

    private Task<ImageSource?>? WarmCover(DiscoveryMusicItemViewModel item)
    {
        if (_disposed || string.IsNullOrWhiteSpace(item.CoverImagePath)) return null;
        var pixels = Math.Clamp((int)Math.Ceiling(280 * (XamlRoot?.RasterizationScale ?? 1)), 280, 1680);
        if (_coverWarmupPath == item.CoverImagePath && _coverWarmupPixels == pixels && _coverWarmup is not null) return _coverWarmup;
        ClearCoverWarmup();
        _coverWarmupPath = item.CoverImagePath;
        _coverWarmupPixels = pixels;
        _coverWarmupCancellation = new();
        return _coverWarmup = WarmCoverAsync(item.CoverImagePath, pixels, _coverWarmupCancellation.Token);
    }

    private static async Task<ImageSource?> WarmCoverAsync(string path, int pixels, CancellationToken token)
    {
        try { return await ImageSourceFactory.LoadNavigationCoverAsync(path, pixels, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return null; }
        catch (Exception error) { AppLog.Error("Unable to prepare playlist cover in advance.", error); return null; }
    }

    private void ClearCoverWarmup()
    {
        _coverWarmupCancellation?.Cancel();
        _coverWarmupCancellation?.Dispose();
        _coverWarmupCancellation = null;
        _coverWarmup = null;
        _coverWarmupPath = null;
    }

    private static Image? FindTrackCover(DependencyObject? root)
    {
        if (root is Image { Name: "TrackCoverImage" } image) return image;
        if (root is null) return null;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindTrackCover(VisualTreeHelper.GetChild(root, index)) is { } cover) return cover;
        return null;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, index)) is { } scroll) return scroll;
        return null;
    }

    internal void SaveMusicPosition()
    {
        _savedMusicOffset = FindScrollViewer(MusicList)?.VerticalOffset ?? _savedMusicOffset;
        ClearReturnAnimation();
        _menu?.Hide(); _menu = null;
    }

    internal async Task RestoreMusicPositionAsync(MusicCoverTransition? transition, Guid musicId)
    {
        ClearReturnAnimation();
        if (_disposed) { transition?.Dispose(); return; }
        _starting = false;
        using var restoration = new CancellationTokenSource();
        _restoreCancellation = restoration;
        _returnCover = transition;
        try
        {
            MusicList.UpdateLayout();
            var scroll = FindScrollViewer(MusicList);
            if (scroll is not null) await RestoreScrollOffsetAsync(scroll, _savedMusicOffset, restoration.Token);
            if (_disposed || restoration.IsCancellationRequested) return;
            MusicList.UpdateLayout();
            if (transition is null) return;
            DiscoveryMusicItemViewModel? item = null;
            foreach (var candidate in Details.Musics)
                if (candidate.PublicId == musicId) { item = candidate; break; }

            var cover = item is null ? null : FindTrackCover(MusicList.ContainerFromItem(item));
            // Never change the saved viewport just to animate an off-screen song
            // (for example after the queue advanced to another track).
            if (cover is null || scroll is null || cover.Source is null)
            {
                ClearReturnAnimation();
                return;
            }
            var bounds = cover.TransformToVisual(scroll).TransformBounds(new global::Windows.Foundation.Rect(0, 0, cover.ActualWidth, cover.ActualHeight));
            if (bounds.Bottom <= 0 || bounds.Top >= scroll.ViewportHeight)
            {
                ClearReturnAnimation();
                return;
            }
            transition.Animation.Completed += ReturnAnimation_Completed;
            _returnTimeout = DispatcherQueue.CreateTimer();
            _returnTimeout.Interval = TimeSpan.FromSeconds(3);
            _returnTimeout.IsRepeating = false;
            _returnTimeout.Tick += ReturnAnimation_Timeout;
            _returnTimeout.Start();
            var started = transition.Animation.TryStart(cover);
            if (!started && ReferenceEquals(_returnCover, transition)) ClearReturnAnimation();
        }
        catch (OperationCanceledException) when (restoration.IsCancellationRequested) { }
        catch (Exception error)
        {
            AppLog.Error("Unable to restore the playlist cover transition.", error);
            if (ReferenceEquals(_returnCover, transition)) ClearReturnAnimation();
        }
        finally
        {
            if (ReferenceEquals(_restoreCancellation, restoration)) _restoreCancellation = null;
        }
    }

    private static async Task RestoreScrollOffsetAsync(ScrollViewer scroll, double offset, CancellationToken token)
    {
        var target = Math.Clamp(offset, 0, scroll.ScrollableHeight);
        if (Math.Abs(scroll.VerticalOffset - target) < .5) return;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void ViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
        {
            if (!args.IsIntermediate && Math.Abs(scroll.VerticalOffset - target) < .5) completion.TrySetResult();
        }
        scroll.ViewChanged += ViewChanged;
        try
        {
            scroll.ChangeView(null, target, null, disableAnimation: true);
            if (Math.Abs(scroll.VerticalOffset - target) < .5) return;
            // Wait for layout/scroll completion, not an arbitrary animation delay.
            await completion.Task.WaitAsync(TimeSpan.FromMilliseconds(350), token);
        }
        catch (TimeoutException) { }
        finally { scroll.ViewChanged -= ViewChanged; }
    }

    private void ReturnAnimation_Completed(ConnectedAnimation sender, object args)
    {
        if (ReferenceEquals(_returnCover?.Animation, sender)) ClearReturnAnimation();
    }
    private void ReturnAnimation_Timeout(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args) => ClearReturnAnimation();
    private void ClearReturnAnimation()
    {
        _restoreCancellation?.Cancel();
        _restoreCancellation = null;
        if (_returnTimeout is not null)
        {
            _returnTimeout.Stop();
            _returnTimeout.Tick -= ReturnAnimation_Timeout;
            _returnTimeout = null;
        }
        if (_returnCover is null) return;
        _returnCover.Animation.Completed -= ReturnAnimation_Completed;
        _returnCover.Dispose();
        _returnCover = null;
    }
    private async void Play_Click(object sender, RoutedEventArgs args)
    {
        if (_playlistId is Guid id) await PlayAsync(id);
    }
    private async Task PlayAsync(Guid id, Guid? musicId = null, Image? coverSource = null)
    {
        if (_starting || _disposed) return;
        _starting = true;
        var revision = _revision;
        _playCancellation?.Cancel();
        _playCancellation?.Dispose();
        var cancellation = _playCancellation = new();
        try
        {
            var started = await _playback.StartAsync(id, cancellation.Token, musicId, coverSource);
            if (_disposed || revision != _revision || cancellation.IsCancellationRequested) return;
            if (!started) ShowStatus("این پلی‌لیست آهنگ قابل پخشی ندارد یا دریافت آن ناموفق بود.", true);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { AppLog.Error("Playlist playback failed.", exception); ShowStatus("پخش پلی‌لیست انجام نشد. دوباره امتحان کن.", true); }
        finally
        {
            if (ReferenceEquals(_playCancellation, cancellation))
            {
                _playCancellation = null;
                cancellation.Dispose();
                _starting = false;
            }
        }
    }


    private void Playlist_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: MusicLibraryPlaylistItemViewModel item } anchor) return;
        _menu?.Hide();
        _menu = MusicPlaylistMenu.Show(anchor, args, item.PublicId, item.CanPlay,
            () => PlayAsync(item.PublicId), () => { _navigation.OpenMusicPlaylist(item.PublicId); return Task.CompletedTask; }, text => ShowStatus(text));
    }
    private void Copy_Click(object sender, RoutedEventArgs args)
    {
        if (_playlistId is not Guid id) return;
        try
        {
            var data = new DataPackage(); data.SetText(MusicPlaylistMenu.Link(id));
            Clipboard.SetContent(data); Clipboard.Flush(); ShowStatus("لینک پلی‌لیست کپی شد.");
        }
        catch (Exception exception) { AppLog.Error("Playlist link could not be copied.", exception); ShowStatus("کپی لینک انجام نشد.", true); }
    }
    private void ShowStatus(string text, bool error = false)
    {
        if (_disposed) return;
        Message.Message = text; Message.Severity = error ? InfoBarSeverity.Warning : InfoBarSeverity.Informational; Message.IsOpen = true;
        AccessibilityAnnouncements.Announce(this, text, "library-action", error);
    }

    private void Browser_Loaded(object sender, RoutedEventArgs args)
    {
        if (_disposed) return;
        ApplyResponsiveLayout();
        ReleaseEntrance();
        if (_hasPresented) return;
        _hasPresented = true;
        if (!AccessibilitySettings.AnimationsEnabled) return;
        var animation = _headerEntrance = CompositionTarget.GetCompositorForCurrentThread().CreateScalarKeyFrameAnimation();
        animation.Target = "Opacity";
        animation.InsertKeyFrame(0, 0);
        animation.InsertKeyFrame(1, 1);
        animation.Duration = TimeSpan.FromMilliseconds(260);
        HeaderRoot.StartAnimation(animation);
    }

    private void Browser_Unloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded) return;
        ClearReturnAnimation();
        ReleaseEntrance();
        _playlistGridPanel = null;
    }

    private void Browser_SizeChanged(object sender, SizeChangedEventArgs args) => ApplyResponsiveLayout();
    private void PlaylistGridPanel_Loaded(object sender, RoutedEventArgs args)
    {
        _playlistGridPanel = (ItemsWrapGrid)sender;
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        if (_disposed || BrowserRoot is null || ActualWidth <= 0) return;
        // Use the control's viewport, not the monitor/window width. A short window
        // also needs a compact header so the virtualized list keeps useful space.
        var compact = ActualWidth < 640 || (ActualHeight > 0 && ActualHeight < 540);
        var narrow = ActualWidth < 420;
        var mode = (compact ? 1 : 0) | (narrow ? 2 : 0) | (ActualWidth < 640 ? 4 : 0);
        if (_layoutMode != mode)
        {
            _layoutMode = mode;
            BrowserRoot.RowSpacing = compact ? 10 : 16;
            HeroBody.Padding = new Thickness(compact ? 14 : 22);
            HeroBody.ColumnSpacing = compact ? 12 : 24;
            HeroBody.RowSpacing = compact ? 10 : 16;
            HeroInfo.Spacing = compact ? 6 : 10;
            LoadingHost.SkeletonHeight = compact ? 90 : 160;
            HeroOverline.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            LibrarySummary.Visibility = !_playlistId.HasValue && !compact ? Visibility.Visible : Visibility.Collapsed;
            HeroArtwork.Width = HeroArtwork.Height = compact ? (narrow ? 64 : 84) : (_playlistId.HasValue ? 166 : 128);
            HeroArtwork.Margin = new Thickness(compact ? 2 : 6);
            HeroNote.FontSize = compact ? 28 : 46;
            Heading.FontSize = compact ? 20 : 28;
            Heading.MaxLines = compact ? 1 : 2;
            AllPlaylistsLabel.Visibility = ActualWidth < 640 ? Visibility.Collapsed : Visibility.Visible;
            PlayLabel.Text = narrow ? "پخش" : "پخش پلی‌لیست";
            AllPlaylistsButton.Padding = new Thickness(ActualWidth < 640 ? 10 : 12, 8, ActualWidth < 640 ? 10 : 12, 8);
        }
        if (_playlistGridPanel is not null)
        {
            var width = Math.Max(1, ActualWidth - 2);
            var columns = Math.Clamp((int)(width / 220), 1, 6);
            var itemWidth = Math.Floor(width / columns);
            if (Math.Abs(_playlistGridPanel.ItemWidth - itemWidth) > 0.5) _playlistGridPanel.ItemWidth = itemWidth;
        }
    }

    private void ReleaseEntrance()
    {
        if (_headerEntrance is not null)
        {
            HeaderRoot.StopAnimation(_headerEntrance);
            _headerEntrance.Dispose();
            _headerEntrance = null;
        }
        HeaderRoot.Opacity = 1;
    }

    public void Suspend()
    {
        if (_disposed) return;
        ClearReturnAnimation();
        _revision++;
        // Starting playback intentionally navigates to the song page before the
        // source is ready. That owned transition must not cancel its own startup.
        if (!_playback.IsNavigatingToPlayback) _playCancellation?.Cancel();
        _playCancellation?.Dispose();
        _playCancellation = null;
        _menu?.Hide(); _menu = null;
        Library.CancelLoading();
        Details.CancelLoading();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Suspend();
        _disposed = true;
        ClearCoverWarmup();
        ReleaseEntrance();
        _playlistGridPanel = null;
        Library.Dispose(); Details.Dispose();
    }
}
