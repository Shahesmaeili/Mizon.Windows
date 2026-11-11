using Symbol = FluentIcons.Common.Symbol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Mizon.API.Client.Operations.Media;
using Mizon.Windows.Common;
using Mizon.Windows.Controls;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.UserControls;

// One bounded API feed, one active decoder. Ordinary navigation never starts music.
public sealed partial class DiscoveryFeatureHero : UserControl
{
    public LocalizationService Localization => LocalizationService.Current;

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(MusicFeaturesViewModel), typeof(DiscoveryFeatureHero), new PropertyMetadata(null, ViewModelChanged));
    private readonly UISettings _settings = new();
    private readonly RectangleGeometry _clip = new();
    private readonly SolidColorBrush _selectedStroke = new(global::Windows.UI.Color.FromArgb(255, 6, 151, 234));
    private readonly SolidColorBrush _normalStroke = new(global::Windows.UI.Color.FromArgb(80, 78, 85, 93));
    private readonly SolidColorBrush _likedBrush = new(global::Windows.UI.Color.FromArgb(255, 255, 155, 190));
    private readonly SolidColorBrush _normalBrush = new(Colors.White);
    private MusicFeaturesViewModel? _observed;
    private MusicFeatureItemViewModel? _selected;
    private FeaturedMediaPlaybackSession? _media;
    private DiscoveryHeroLighting? _lighting;
    private CancellationTokenSource? _selectionCancellation;
    private DispatcherQueueTimer? _timer;
    private XamlRoot? _root;
    private bool _loaded, _initialized, _inViewport, _hostVisible, _pointerInside, _focusInside, _menuOpen;
    private bool _playRequested, _updatingPosition, _seeking, _refreshQueued, _videoEnded, _sourceLoading, _saving;
    private int _index, _selectionVersion;
    private Guid _selectedMediaId;
    private string? _selectedContentType;
    private bool _lastMuted = true, _audioPausePending;
    private long _lastTick, _fullWindowToken;
    private double _imagePosition;
    private string? _layoutState;
    private string? _statusResourceKey;
    private bool _statusIsError;

    public DiscoveryFeatureHero()
    {
        InitializeComponent();
        HeroRoot.Clip = _clip;
        PositionSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Seek_Pressed), true);
        PositionSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Seek_Released), true);
        PositionSlider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(Seek_Released), true);
        _initialized = true;
    }

    public MusicFeaturesViewModel? ViewModel
    {
        get => (MusicFeaturesViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public MusicFeatureItemViewModel? SelectedItem => _selected;
    public event EventHandler<MusicFeatureItemViewModel>? OpenMusicRequested;
    public event EventHandler<MusicFeatureItemViewModel>? PlayMusicRequested;
    public event EventHandler<MusicFeatureItemViewModel>? SelectedItemChanged;

    private MediaDownloader Downloader => ((App)Application.Current).Services.GetRequiredService<MediaDownloader>();

    private static void ViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var hero = (DiscoveryFeatureHero)sender;
        hero.DetachViewModel();
        if (!hero._loaded) return;
        hero.AttachViewModel();
        hero.QueueRefresh();
    }

    private void Hero_Loaded(object sender, RoutedEventArgs args)
    {
        if (_loaded || !IsLoaded) return;
        _loaded = true;
        Bindings.Update();
        Localization.PropertyChanged += HeroLocalizationChanged;
        _playRequested = _settings.AnimationsEnabled;
        _lighting = new(HeroSurface);
        _media = new(HeroVideo, Downloader);
        _media.Changed += Media_Changed;
        _media.Ended += Media_Ended;
        _media.Failed += Media_Failed;
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += Timer_Tick;
        _settings.AnimationsEnabledChanged += AnimationsChanged;
        _root = XamlRoot;
        if (_root is not null) _root.Changed += Root_Changed;
        _hostVisible = _root?.IsHostVisible == true;
        _fullWindowToken = HeroVideo.RegisterPropertyChangedCallback(MediaPlayerElement.IsFullWindowProperty, FullWindowChanged);
        AttachViewModel();
        UpdateLayoutState(ActualWidth);
        RefreshItems();
        UpdateActive();
    }

    private void Hero_Unloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded || !_loaded) return;
        _loaded = false;
        Localization.PropertyChanged -= HeroLocalizationChanged;
        Bindings.StopTracking();
        MediaOptionsFlyout.Hide();
        CancelSelection();
        DetachViewModel();
        if (_selected is not null) _selected.PropertyChanged -= Item_Changed;
        _selected = null;
        _settings.AnimationsEnabledChanged -= AnimationsChanged;
        if (_root is not null) _root.Changed -= Root_Changed;
        _root = null;
        HeroVideo.UnregisterPropertyChangedCallback(MediaPlayerElement.IsFullWindowProperty, _fullWindowToken);
        HeroVideo.IsFullWindow = false;
        HeroVideo.AreTransportControlsEnabled = false;
        HeroVideo.IsHitTestVisible = false;
        if (_timer is not null) { _timer.Stop(); _timer.Tick -= Timer_Tick; _timer = null; }
        if (_media is not null)
        {
            _media.Changed -= Media_Changed;
            _media.Ended -= Media_Ended;
            _media.Failed -= Media_Failed;
            _media.Dispose();
            _media = null;
        }
        _lighting?.Dispose();
        _lighting = null;
        _inViewport = _hostVisible = _pointerInside = _focusInside = _menuOpen = _seeking = false;
        HeroArtwork.Source = null;
        ArtistPortrait.ProfilePicture = null;
        SongBadgeImage.Source = null;
        foreach (Button button in ThumbnailButtons.Children) button.Click -= Thumbnail_Click;
        ThumbnailButtons.Children.Clear();
    }

    private void AttachViewModel()
    {
        if (ReferenceEquals(_observed, ViewModel)) return;
        DetachViewModel();
        _observed = ViewModel;
        if (_observed is null) return;
        _observed.Items.CollectionChanged += Items_Changed;
        _observed.PropertyChanged += ViewModel_Changed;
    }

    private void DetachViewModel()
    {
        if (_observed is null) return;
        _observed.Items.CollectionChanged -= Items_Changed;
        _observed.PropertyChanged -= ViewModel_Changed;
        _observed = null;
    }

    private void Items_Changed(object? sender, NotifyCollectionChangedEventArgs args) => QueueRefresh();
    private void ViewModel_Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MusicFeaturesViewModel.IsLoading) or nameof(MusicFeaturesViewModel.HasError)
            or nameof(MusicFeaturesViewModel.StatusText) or nameof(MusicFeaturesViewModel.HasItems)
            or nameof(MusicFeaturesViewModel.Items)) QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (_refreshQueued || !_loaded) return;
        _refreshQueued = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _refreshQueued = false;
            if (_loaded) RefreshItems();
        });
    }

    private void RefreshItems()
    {
        var items = ViewModel?.Items;
        if (items is null || items.Count == 0)
        {
            MediaOptionsFlyout.Hide();
            CancelSelection();
            HeroVideo.IsFullWindow = false;
            _media?.Stop();
            if (_selected is not null) _selected.PropertyChanged -= Item_Changed;
            _selected = null;
            HeroSurface.Visibility = Visibility.Collapsed;
            EmptyPanel.Visibility = Visibility.Visible;
            EmptyText.Text = ViewModel?.StatusText ?? Localization.GetString("MusicHome/NoFeatures");
            UpdateActive();
            return;
        }
        HeroSurface.Visibility = Visibility.Visible;
        EmptyPanel.Visibility = Visibility.Collapsed;
        var previousIndex = _selected is null ? -1 : items.IndexOf(_selected);
        _index = previousIndex >= 0 ? previousIndex : Math.Clamp(_index, 0, items.Count - 1);
        if (!ReferenceEquals(_selected, items[_index]) || _selectedMediaId != items[_index].MediaPublicId
            || _selectedContentType != items[_index].ContentTypeCode) Select(_index, false);
        else UpdatePresentation();
    }

    private void Previous_Click(object sender, RoutedEventArgs args) => SelectRelative(-1, true);
    private void Next_Click(object sender, RoutedEventArgs args) => SelectRelative(1, true);
    private void SelectRelative(int direction, bool manual)
    {
        if (ViewModel?.Items is not { Count: > 0 } items) return;
        Select((_index + direction + items.Count) % items.Count, manual);
    }
    private void Thumbnail_Click(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: int index } && index != _index) Select(index, true);
    }

    private void Select(int index, bool announce)
    {
        if (!_loaded || ViewModel?.Items is not { } items || index < 0 || index >= items.Count) return;
        MediaOptionsFlyout.Hide();
        CancelSelection();
        HeroVideo.IsFullWindow = false;
        _media?.Stop();
        if (_selected is not null) _selected.PropertyChanged -= Item_Changed;
        _index = index;
        _selected = items[index];
        _selectedMediaId = _selected.MediaPublicId;
        _selectedContentType = _selected.ContentTypeCode;
        _selected.PropertyChanged += Item_Changed;
        _imagePosition = 0;
        _videoEnded = _seeking = false;
        HeroVideo.Visibility = Visibility.Collapsed;
        HeroArtwork.Source = null;
        ShowStatus(string.Empty);
        UpdatePresentation();
        _selectionCancellation = new();
        if (_selected.IsVideo) _ = LoadVideoAsync(_selected, _selectionVersion, _selectionCancellation.Token);
        if (announce) AccessibilityAnnouncements.Announce(MainTitle, _selected.AutomationName, "hero-selection");
        SelectedItemChanged?.Invoke(this, _selected);
        UpdateActive();
    }

    private void CancelSelection()
    {
        _selectionVersion++;
        _selectionCancellation?.Cancel();
        _selectionCancellation?.Dispose();
        _selectionCancellation = null;
        _sourceLoading = false;
    }

    private async Task LoadVideoAsync(MusicFeatureItemViewModel item, int version, CancellationToken token)
    {
        try
        {
            _sourceLoading = true;
            ShowLocalizedStatus("MusicHome/PreparingVideo");
            UpdateTransport();
            var info = await ViewModel!.GetMediaAsync(item, token);
            token.ThrowIfCancellationRequested();
            if (!_loaded || version != _selectionVersion) return;
            if (info is null || !Uri.TryCreate(info.StoragePath, UriKind.Absolute, out var uri))
                throw new InvalidDataException("Featured video media is unavailable.");
            if (_media is not null)
            {
                _sourceLoading = false;
                _media.SetActive(CanPresent);
                await _media.OpenVideoAsync(info, uri, _playRequested, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppLog.Error("Featured video could not be loaded.", exception);
            if (_loaded && version == _selectionVersion) ShowLocalizedStatus("MusicHome/VideoFailed", true);
        }
        finally
        {
            if (version == _selectionVersion) { _sourceLoading = false; UpdateTransport(); }
        }
    }

    private void Item_Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (_loaded && ReferenceEquals(sender, _selected)) QueueRefresh();
    }

    private void UpdatePresentation()
    {
        if (!_initialized || _selected is not { } item) return;
        MainTitle.Text = item.Title;
        PersianTitle.Text = item.PersianTitle ?? string.Empty;
        ArtistName.Text = item.ArtistText;
        ArtistPortrait.DisplayName = item.ArtistText;
        ArtistPortrait.ProfilePicture = item.ArtistProfileImage;
        SongBadgeImage.Source = item.CoverImage;
        GenreText.Text = item.GenreTitle.ToUpperInvariant();
        TrackStatistics.Text = HeaderDuration.Text = item.DurationText;
        LikesCount.Text = item.LikesCountText;
        ToolTipService.SetToolTip(MainTitle, item.Title);
        ToolTipService.SetToolTip(ArtistName, item.ArtistText);
        PageNumber.Text = $"{_index + 1:00} / {ViewModel!.Items.Count:00}";
        FavoriteIcon.Foreground = item.LikedByMe ? _likedBrush : _normalBrush;
        FavoriteButton.IsChecked = item.LikedByMe;
        FavoriteButton.IsEnabled = !item.IsLikeBusy;
        AutomationProperties.SetName(FavoriteButton, item.LikedByMe ? Localization.GetString("MusicHome/UnlikeTrack") : Localization.GetString("MusicHome/LikeTrack"));
        AutomationProperties.SetItemStatus(FavoriteButton, item.LikedByMe ? Localization.GetString("MusicHome/Liked") : Localization.GetString("MusicHome/NotLiked"));
        AutomationProperties.SetName(OpenButton, Localization.Format("MusicHome/DiscoverTrackName", item.DisplayTitle));
        AutomationProperties.SetName(ArtworkButton, Localization.Format("MusicHome/OpenTrackName", item.DisplayTitle));
        AutomationProperties.SetName(PlayTrackButton, Localization.Format("MusicHome/PlayTrackName", item.DisplayTitle));
        AutomationProperties.SetName(PositionSlider, item.IsVideo ? Localization.GetString("MusicHome/VideoPosition") : Localization.GetString("MusicHome/ImagePosition"));
        if (Uri.TryCreate(item.ImagePath, UriKind.Absolute, out var uri) && !Equals(HeroArtwork.Source, uri))
        {
            HeroArtwork.TransitionDuration = _settings.AnimationsEnabled && CanPresent ? TimeSpan.FromMilliseconds(260) : TimeSpan.Zero;
            HeroArtwork.Source = uri;
        }
        if (!item.IsVideo)
        {
            if (!string.IsNullOrEmpty(item.ArtworkErrorText)) ShowStatus(item.ArtworkErrorText, true);
            else if (item.IsArtworkLoading) ShowLocalizedStatus("MusicHome/DownloadingImage");
            else if (_statusResourceKey == "MusicHome/DownloadingImage") ShowStatus(string.Empty);
        }
        PreviousButton.IsEnabled = NextButton.IsEnabled = ViewModel.Items.Count > 1;
        BuildThumbnails();
        UpdateTransport();
    }

    private void BuildThumbnails()
    {
        if (ViewModel?.Items is not { } items) return;
        var count = Math.Min(3, items.Count);
        var start = Math.Clamp(_index - 1, 0, Math.Max(0, items.Count - count));
        while (ThumbnailButtons.Children.Count > count)
        {
            var old = (Button)ThumbnailButtons.Children[^1];
            old.Click -= Thumbnail_Click;
            ThumbnailButtons.Children.RemoveAt(ThumbnailButtons.Children.Count - 1);
        }
        for (var offset = 0; offset < count; offset++)
        {
            var index = start + offset;
            var item = items[index];
            Button button;
            if (offset < ThumbnailButtons.Children.Count) button = (Button)ThumbnailButtons.Children[offset];
            else
            {
                button = new FeatureButton
                {
                    Padding = new(1), CornerRadius = new(8), Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new(1),
                    Content = new Grid { CornerRadius = new(7), Children = { new Image { Stretch = Stretch.UniformToFill } } }
                };
                button.Click += Thumbnail_Click;
                ThumbnailButtons.Children.Add(button);
            }
            button.Tag = index;
            button.Width = button.Height = _layoutState == "CompactHero" ? 24 : 30;
            button.BorderBrush = index == _index ? _selectedStroke : _normalStroke;
            ((Image)((Grid)button.Content).Children[0]).Source = item.PreviewImage;
            AutomationProperties.SetName(button, Localization.Format("MusicHome/FeatureNumber", index + 1, item.DisplayTitle));
            ToolTipService.SetToolTip(button, item.Title);
            AutomationProperties.SetPositionInSet(button, index + 1);
            AutomationProperties.SetSizeOfSet(button, items.Count);
            AutomationProperties.SetItemStatus(button, index == _index ? Localization.GetString("MusicHome/Selected") : string.Empty);
        }
    }

    private bool CanPresent => _loaded && _hostVisible && (_inViewport || HeroVideo.IsFullWindow);
    private bool CanAdvance => CanPresent && _playRequested && !_pointerInside && !_focusInside && !_menuOpen && !_saving && !_seeking;
    private void UpdateActive()
    {
        _lastTick = Stopwatch.GetTimestamp();
        _media?.SetActive(CanPresent);
        var needsClock = _selected is { IsVideo: true }
            ? _sourceLoading || _media?.IsLoading == true || _media?.WantsToPlay == true || (_videoEnded && CanAdvance)
            : CanAdvance;
        if (CanPresent && _selected is not null && needsClock) _timer?.Start(); else _timer?.Stop();
        if (!CanPresent) DownloadProgress.IsIndeterminate = false;
    }

    private void Timer_Tick(DispatcherQueueTimer sender, object args)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = Math.Min(1, Stopwatch.GetElapsedTime(_lastTick, now).TotalSeconds);
        _lastTick = now;
        if (_selected is not { } item) return;
        if (!item.IsVideo && CanAdvance && !string.IsNullOrWhiteSpace(item.ImagePath))
        {
            _imagePosition += elapsed;
            if (_imagePosition >= item.DisplaySeconds)
            {
                if (ViewModel!.Items.Count > 1) { SelectRelative(1, false); return; }
                _imagePosition = 0;
            }
        }
        else if (_videoEnded && CanAdvance && ViewModel!.Items.Count > 1) { SelectRelative(1, false); return; }
        UpdateTransport();
    }

    private void UpdateTransport()
    {
        if (!_initialized || !_loaded || _selected is not { } item) return;
        var video = item.IsVideo;
        var duration = video ? _media?.Duration.TotalSeconds ?? 0 : Math.Clamp(item.DisplaySeconds, 3, 120);
        var position = video ? _media?.Position.TotalSeconds ?? 0 : _imagePosition;
        var loading = _sourceLoading || _media?.IsLoading == true;
        var playing = video ? _media?.WantsToPlay == true : _playRequested;
        PlayPauseIcon.Symbol = playing ? Symbol.Pause : Symbol.Play;
        PlayPauseButton.IsEnabled = !video || _media?.IsReady == true;
        AutomationProperties.SetName(PlayPauseButton, playing ? Localization.GetString("MusicHome/PausePresentation") : Localization.GetString("MusicHome/ResumePresentation"));
        MuteButton.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
        // Extra video controls stay in the headphone flyout; the artwork overlay retains its original layout.
        VolumeSlider.IsEnabled = video;
        VolumeSlider.Opacity = video ? 1 : 0;
        VolumeSlider.IsHitTestVisible = video;
        VolumeSlider.IsTabStop = video;
        MuteIcon.Symbol = _media?.IsMuted != false ? Symbol.SpeakerMute : Symbol.Speaker2;
        AutomationProperties.SetName(MuteButton, _media?.IsMuted != false ? Localization.GetString("MusicHome/UnmuteVideo") : Localization.GetString("MusicHome/MuteVideo"));
        FullscreenMenuItem.IsEnabled = video && _media?.IsReady == true;
        SaveMenuItem.IsEnabled = !_saving;
        HeroVideo.Visibility = video && _media?.IsReady == true ? Visibility.Visible : Visibility.Collapsed;
        DownloadProgress.Visibility = video && loading ? Visibility.Visible : Visibility.Collapsed;
        DownloadProgress.IsIndeterminate = video && loading && (_sourceLoading || _media?.DownloadProgress <= 0) && CanPresent && _settings.AnimationsEnabled;
        DownloadProgress.Value = _media?.DownloadProgress ?? 0;
        if (!_seeking)
        {
            _updatingPosition = true;
            try
            {
                PositionSlider.Maximum = Math.Max(1, duration);
                PositionSlider.IsEnabled = !video || _media?.CanSeek == true;
                var value = Math.Clamp(position, 0, PositionSlider.Maximum);
                if (Math.Abs(PositionSlider.Value - value) >= .15) PositionSlider.Value = value;
            }
            finally { _updatingPosition = false; }
        }
        PositionText.Text = $"{TimeText(_seeking ? PositionSlider.Value : position)} / {TimeText(duration)}";
    }

    private static string TimeText(double seconds) => TimeSpan.FromSeconds(Math.Max(0, double.IsFinite(seconds) ? seconds : 0)).ToString(@"m\:ss", CultureInfo.InvariantCulture);
    private async void Media_Changed(object? sender, EventArgs args)
    {
        if (!_loaded) return;
        if (_media?.IsReady == true) ShowStatus(_media.IsBuffering ? Localization.GetString("MusicHome/Buffering") : string.Empty);
        if (_media is { IsReady: true } media && !_videoEnded)
        {
            if (media.IsPlaying) _playRequested = true;
            else if (!media.WantsToPlay && media.Position < media.Duration - TimeSpan.FromMilliseconds(150))
                _playRequested = false;
        }
        UpdateTransport();
        UpdateActive();
        var muted = _media?.IsMuted != false;
        var unmuted = _lastMuted && !muted;
        _lastMuted = muted;
        if (!unmuted || _audioPausePending) return;
        _audioPausePending = true;
        try
        {
            // Also covers the native full-window video's volume controls.
            var audio = ((App)Application.Current).Services.GetRequiredService<IAudioPlayerService>();
            if (audio.AudioPlayerState == AudioPlayerState.Playing) await audio.PauseAsync();
        }
        catch (Exception exception) { AppLog.Error("Main audio could not pause for the featured video.", exception); }
        finally { _audioPausePending = false; }
    }
    private void Media_Ended(object? sender, EventArgs args) { _videoEnded = true; UpdateTransport(); UpdateActive(); }
    private void Media_Failed(object? sender, string message) => ShowLocalizedStatus("MusicHome/VideoFailed", true);
    private void Artwork_Failed(object? sender, string message)
    {
        if (_loaded && _selected?.IsVideo == false) ShowLocalizedStatus("MusicHome/ImageFailed", true);
    }
    private void HeroLocalizationChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!_loaded || args.PropertyName != nameof(LocalizationService.Revision)) return;
        var statusKey = _statusResourceKey;
        var statusError = _statusIsError;
        EmptyText.Text = ViewModel?.StatusText ?? Localization.GetString("MusicHome/NoFeatures");
        UpdatePresentation();
        if (statusKey is not null) ShowLocalizedStatus(statusKey, statusError);
    }

    private void ShowLocalizedStatus(string resourceKey, bool error = false)
    {
        ShowStatus(Localization.GetString(resourceKey), error);
        _statusResourceKey = resourceKey;
    }

    private void ShowStatus(string text, bool error = false)
    {
        _statusResourceKey = null;
        _statusIsError = error;
        StatusText.Text = text;
        StatusPanel.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        RetryButton.Visibility = error ? Visibility.Visible : Visibility.Collapsed;
        if (error) AccessibilityAnnouncements.Announce(StatusText, text, "hero-media-error");
    }

    private void PlayPause_Click(object sender, RoutedEventArgs args)
    {
        if (_selected?.IsVideo == true && _media is not null)
        {
            _playRequested = !_media.WantsToPlay;
            if (_playRequested)
            {
                if (_videoEnded) { _media.Seek(TimeSpan.Zero); _videoEnded = false; }
                _media.Play();
            }
            else _media.Pause();
        }
        else _playRequested = !_playRequested;
        UpdateActive();
        UpdateTransport();
    }

    private async void Mute_Click(object sender, RoutedEventArgs args)
    {
        if (_media is not { } media) return;
        var version = _selectionVersion;
        try
        {
            if (media.IsMuted)
            {
                var audio = ((App)Application.Current).Services.GetRequiredService<IAudioPlayerService>();
                if (audio.AudioPlayerState == AudioPlayerState.Playing) await audio.PauseAsync();
            }
            if (_loaded && version == _selectionVersion) media.IsMuted = !media.IsMuted;
        }
        catch (Exception exception) { AppLog.Error("Featured video audio could not be changed.", exception); ShowLocalizedStatus("MusicHome/VolumeFailed"); }
        UpdateTransport();
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_media is not null) _media.Volume = args.NewValue / 100;
    }
    private void Seek_Pressed(object sender, PointerRoutedEventArgs args) { if (PositionSlider.IsEnabled) _seeking = true; }
    private void Seek_Released(object sender, PointerRoutedEventArgs args)
    {
        if (!_seeking) return;
        _seeking = false;
        CommitSeek();
    }
    private void PositionSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_initialized && !_updatingPosition && !_seeking) CommitSeek();
    }
    private void CommitSeek()
    {
        if (_selected?.IsVideo == true) { _media?.Seek(TimeSpan.FromSeconds(PositionSlider.Value)); _videoEnded = false; }
        else _imagePosition = PositionSlider.Value;
        _lastTick = Stopwatch.GetTimestamp();
    }

    private void Open_Click(object sender, RoutedEventArgs args)
    {
        if (_selected is not { } item) return;
        MediaOptionsFlyout.Hide();
        _media?.Pause();
        OpenMusicRequested?.Invoke(this, item);
    }
    private void PlayTrack_Click(object sender, RoutedEventArgs args)
    {
        if (_selected is not { } item) return;
        MediaOptionsFlyout.Hide();
        _media?.Pause();
        PlayMusicRequested?.Invoke(this, item);
    }
    private async void Favorite_Click(object sender, RoutedEventArgs args)
    {
        if (_selected is not { } item || ViewModel is not { } model) return;
        FavoriteButton.IsChecked = item.LikedByMe;
        var version = _selectionVersion;
        var succeeded = await model.ToggleLikeAsync(item);
        if (_loaded && version == _selectionVersion)
        {
            FavoriteButton.IsChecked = item.LikedByMe;
            if (!succeeded) ShowStatus(model.ActionStatusText);
            else AccessibilityAnnouncements.Announce(FavoriteButton, item.LikedByMe ? Localization.GetString("MusicHome/TrackLiked") : Localization.GetString("MusicHome/TrackUnliked"), "hero-like");
        }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs args)
    {
        if (ViewModel is { IsLoading: false } model) await model.RefreshAsync();
    }
    private void Retry_Click(object sender, RoutedEventArgs args)
    {
        if (_selected?.IsVideo == true) Select(_index, false);
        else Refresh_Click(sender, args);
    }
    private void CopyLink_Click(object sender, RoutedEventArgs args)
    {
        if (_selected is not { } item) return;
        try
        {
            var data = new DataPackage();
            data.SetText($"mizon://music/{item.MusicPublicId:D}");
            Clipboard.SetContent(data);
            ShowLocalizedStatus("MusicHome/LinkCopied");
            AccessibilityAnnouncements.Announce(StatusText, StatusText.Text, "hero-copy");
        }
        catch (Exception exception) { AppLog.Error("Featured music link could not be copied.", exception); ShowLocalizedStatus("MusicHome/CopyFailed"); }
    }
    private async void Save_Click(object sender, RoutedEventArgs args)
    {
        try { await SaveCurrentAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { AppLog.Error("Featured media export failed.", exception); }
    }

    private async Task SaveCurrentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_saving || _selected is not { } item || ViewModel is not { } model) return;
        _saving = true;
        var version = _selectionVersion;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _selectionCancellation?.Token ?? CancellationToken.None);
        var token = request.Token;
        try
        {
            var info = await model.GetMediaAsync(item, token);
            if (info is null || !Uri.TryCreate(info.StoragePath, UriKind.Absolute, out var uri)) return;
            var path = await Downloader.DownloadToCacheAsync(info, uri, cancellationToken: token);
            token.ThrowIfCancellationRequested();
            if (!_loaded || version != _selectionVersion) return;
            var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(info.FileName) };
            var extension = Path.GetExtension(info.FileName);
            picker.FileTypeChoices.Add(item.IsVideo ? Localization.GetString("MusicHome/Video") : Localization.GetString("MusicHome/Image"), new List<string> { string.IsNullOrEmpty(extension) ? ".bin" : extension });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainMizonWindow));
            var target = await picker.PickSaveFileAsync().AsTask(token);
            token.ThrowIfCancellationRequested();
            if (!_loaded || version != _selectionVersion) return;
            if (target is not null)
            {
                var source = await StorageFile.GetFileFromPathAsync(path).AsTask(token);
                await source.CopyAndReplaceAsync(target).AsTask(token);
                if (_loaded && version == _selectionVersion) ShowLocalizedStatus("MusicHome/Saved");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            AppLog.Error("Featured media export failed.", exception);
            if (_loaded && version == _selectionVersion) ShowLocalizedStatus("MusicHome/SaveFailed");
            throw;
        }
        finally
        {
            _saving = false;
            if (_loaded) { UpdateTransport(); UpdateActive(); }
        }
    }

    private void More_Opening(object sender, object args) { _menuOpen = true; UpdateTransport(); UpdateActive(); }
    private void More_Closed(object sender, object args)
    {
        if (_seeking)
        {
            _seeking = false;
            if (_loaded) CommitSeek();
        }
        _menuOpen = false;
        UpdateActive();
    }
    private void Fullscreen_Click(object sender, RoutedEventArgs args)
    {
        if (_media?.IsReady != true || _selected?.IsVideo != true) return;
        MediaOptionsFlyout.Hide();
        HeroVideo.AreTransportControlsEnabled = true;
        HeroVideo.IsHitTestVisible = true;
        HeroVideo.IsFullWindow = true;
    }
    private void FullWindowChanged(DependencyObject sender, DependencyProperty property)
    {
        HeroVideo.AreTransportControlsEnabled = HeroVideo.IsFullWindow;
        HeroVideo.IsHitTestVisible = HeroVideo.IsFullWindow;
        UpdateActive();
    }
    private void Hero_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (args.NewSize.Width != args.PreviousSize.Width) UpdateLayoutState(args.NewSize.Width);
    }
    private void UpdateLayoutState(double width)
    {
        if (!_initialized || !double.IsFinite(width) || width <= 0) return;
        width = Math.Max(1, width - HeroSurface.Margin.Left - HeroSurface.Margin.Right);
        var height = Math.Clamp(width * 360d / 770d, 190, 340);
        if (Math.Abs(HeroSurface.Height - height) > .5) HeroSurface.Height = height;
        var bounds = new global::Windows.Foundation.Rect(0, 0, width, height);
        if (_clip.Rect != bounds) _clip.Rect = bounds;
        var state = width >= 560 ? "WideHero" : "CompactHero";
        if (_layoutState != state && VisualStateManager.GoToState(this, state, false))
        {
            _layoutState = state;
            BuildThumbnails();
        }
    }
    private void Hero_EffectiveViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
    {
        var rect = args.EffectiveViewport;
        var visible = rect.Width > 0 && rect.Height > 0 && rect.Right > 0 && rect.Bottom > 0 && rect.Left < ActualWidth && rect.Top < ActualHeight;
        if (_inViewport == visible) return;
        _inViewport = visible;
        UpdateActive();
    }
    private void Root_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (_hostVisible == sender.IsHostVisible) return;
        _hostVisible = sender.IsHostVisible;
        UpdateActive();
    }
    private void Hero_PointerEntered(object sender, PointerRoutedEventArgs args)
    {
        if (_pointerInside) return;
        _pointerInside = true;
        UpdateActive();
    }
    private void Hero_PointerExited(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(this).Position;
        if (point.X < 0 || point.Y < 0 || point.X >= ActualWidth || point.Y >= ActualHeight)
        {
            _pointerInside = false;
            UpdateActive();
        }
    }
    private void Hero_FocusChanged(object sender, RoutedEventArgs args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_loaded || XamlRoot is null) return;
        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        while (focused is not null && !ReferenceEquals(focused, this)) focused = VisualTreeHelper.GetParent(focused);
        _focusInside = ReferenceEquals(focused, this);
        UpdateActive();
    });
    private void AnimationsChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_loaded) return;
        if (!_settings.AnimationsEnabled) { _playRequested = false; _media?.Pause(); }
        UpdateActive();
        UpdateTransport();
    });
}

