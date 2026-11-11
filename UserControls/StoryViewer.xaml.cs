using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Mizon.API.Client.Responses.Stories;
using Mizon.Windows.Common;
using Mizon.Windows.Controls;
using Mizon.Windows.Pages;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using FluentSymbol = FluentIcons.Common.Symbol;
using Color = global::Windows.UI.Color;

namespace Mizon.Windows.UserControls;

public sealed partial class StoryViewer : UserControl, IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Stopwatch _imageClock = new();
    private readonly List<ProgressBar> _segments = [];
    private readonly List<(Button Element, StoryWidgetResponse Widget)> _widgets = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private MusicPageViewModel? _viewModel;
    private IReadOnlyList<StoryItemViewModel> _stories = [];
    private StoryResponse? _detail;
    private StoryRequest? _request;
    private StoryPlaybackSession? _playback;
    private BitmapImage? _image;
    private MusicCoverTransition? _openingTransition;
    private CancellationTokenSource? _transitionLoad, _transitionTimeout;
    private CancellationTokenRegistration _externalCancellation;
    private ContentDialog? _clientDialog;
    private XamlRoot? _root;
    private Task? _closeTask;
    private bool _opened, _closing, _closed, _ready, _paused, _marked, _advancing;
    private bool _transitionAnimating;
    private bool _entrancePending = true;
    private int _index;
    private int _version;

    // Hosts remove the overlay only after this event. Navigation/unload use Close()
    // to dispose synchronously instead of keeping an overlay on the next page.
    public event EventHandler? CloseRequested;

    public StoryViewer()
    {
        _lifetimeToken = _lifetime.Token;
        InitializeComponent();
        PrepareMotion();
        _timer.Tick += Timer_Tick;
    }

    private TaskCompletionSource<bool> _presentationReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task<bool> WaitForPresentationAsync(CancellationToken token) => _presentationReady.Task.WaitAsync(token);

    public Task OpenAsync(MusicPageViewModel viewModel, IReadOnlyList<StoryItemViewModel> stories, int index) =>
        OpenWithTransitionAsync(viewModel, stories, index, null, CancellationToken.None);

    internal async Task OpenWithTransitionAsync(MusicPageViewModel viewModel, IReadOnlyList<StoryItemViewModel> stories,
        int index, MusicCoverTransition? transition, CancellationToken cancellationToken)
    {
        if (_opened || _closed || cancellationToken.IsCancellationRequested || index < 0 || index >= stories.Count)
        {
            transition?.Dispose();
            if (!_opened) Close();
            return;
        }
        _opened = true;
        _viewModel = viewModel;
        _stories = stories;
        _openingTransition = transition;
        StoryContent.Opacity = transition is null ? 1 : 0;
        _externalCancellation = cancellationToken.Register(() => DispatcherQueue.TryEnqueue(Close));
        var foreground = new SolidColorBrush(Colors.White);
        var background = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255));
        for (var i = 0; i < stories.Count; i++)
        {
            ProgressSegments.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 3, Foreground = foreground, Background = background, IsHitTestVisible = false };
            AutomationProperties.SetAccessibilityView(bar, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            Grid.SetColumn(bar, i);
            ProgressSegments.Children.Add(bar);
            _segments.Add(bar);
        }
        // Fetch immediately; entrance and media loading do not wait on each other.
        await Task.WhenAll(OpenIndexAsync(index, preserveEntrance: true), OpenPresentationAsync(transition));
    }

    private async Task OpenPresentationAsync(MusicCoverTransition? transition)
    {
        try
        {
            if (!IsLoaded) await _loaded.Task.WaitAsync(_lifetimeToken);
            if (_closing || _closed) return;
            Focus(FocusState.Programmatic);
            await Task.WhenAll(AnimateOpenAsync(_lifetimeToken, transition is not null), ShowOpeningPreviewAsync(transition));
        }
        catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppLog.Error("Story entrance could not be animated.", exception);
            ReleaseMotion();
            if (!_closing && !_closed) ViewerRoot.Opacity = 1;
        }
        finally { _entrancePending = false; UpdatePlaybackState(); }
    }

    private bool IsCurrent(StoryRequest request) => !_closed && !_closing && ReferenceEquals(request, _request) && !request.Token.IsCancellationRequested;

    private async Task OpenIndexAsync(int index, bool preserveEntrance = false)
    {
        if (_closing || _closed || _viewModel is not { } viewModel || index < 0 || index >= _stories.Count) return;
        _presentationReady.TrySetResult(false);
        _presentationReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _ready = false;
        if (!preserveEntrance) CancelOpeningTransition(clearPreview: true);
        _version++;
        _request?.Cancel();
        var request = _request = new StoryRequest(_lifetimeToken);
        using var lease = request.Retain();
        var token = request.Token;
        StopPlayback();
        ClearImage();
        ClearWidgets();
        _clientDialog?.Hide();
        _index = index;
        _detail = null;
        _marked = _paused = _ready = false;
        StoryVideo.Visibility = Visibility.Collapsed;
        StoryImage.Visibility = Visibility.Visible;
        LoadingPanel.Visibility = Visibility.Visible;
        LoadingRing.IsActive = IsLoaded && XamlRoot?.IsHostVisible != false;
        ErrorPanel.Visibility = Visibility.Collapsed;
        LikeButton.IsEnabled = PauseButton.IsEnabled = false;
        UpdatePause();
        LikesText.Text = ViewsText.Text = "—";
        var story = _stories[index];
        CreatorName.Text = story.CreatorDisplayName;
        CreatorPicture.DisplayName = story.CreatorDisplayName;
        CreatorPicture.ProfilePicture = story.CreatorProfileImage;
        SetTitle(story.Title);
        PositionText.Text = $"{index + 1} / {_stories.Count}";
        PreviousButton.IsEnabled = index > 0;
        AutomationProperties.SetName(NextButton, index + 1 == _stories.Count ? Text("FinishStories") : Text("NextStory"));
        for (var i = 0; i < _segments.Count; i++) _segments[i].Value = i < index ? 100 : 0;
        try
        {
            var detail = await viewModel.GetStoryDetailsAsync(story, token);
            if (!IsCurrent(request)) return;
            if (detail is null) { ShowError("StoryUnavailable"); return; }
            _detail = detail;
            CreatorName.Text = $"{detail.CreatorFirstName} {detail.CreatorLastName}".Trim();
            CreatorPicture.DisplayName = CreatorName.Text;
            SetTitle(detail.Title);
            story.ViewedByMe = detail.ViewedByMe;
            if (story.CreatorProfileMediaPublicId != detail.CreatorProfileMediaPublicId) CreatorPicture.ProfilePicture = null;
            if (CreatorPicture.ProfilePicture is null && detail.CreatorProfileMediaPublicId is Guid profileId)
                _ = LoadCreatorPictureAsync(viewModel, profileId, request);
            UpdateLike();
            UpdateViews(detail.ViewsCount);
            LikeButton.IsEnabled = true;
            var path = await viewModel.PrepareStoryPlaybackAsync(detail.ContentMediaPublicId, token);
            if (!IsCurrent(request)) return;
            if (!Uri.TryCreate(path, UriKind.Absolute, out var uri)) { ShowError("StoryDownloadFailed"); return; }
            BuildWidgets(detail);
            if (string.Equals(detail.StoryContentTypeTitle, "Video", StringComparison.OrdinalIgnoreCase))
            {
                StoryImage.Visibility = Visibility.Collapsed;
                StoryVideo.Visibility = Visibility.Visible;
                var playback = _playback = new StoryPlaybackSession(StoryVideo, DispatcherQueue);
                playback.Opened += Playback_Opened;
                playback.Failed += Playback_Failed;
                playback.Ended += Playback_Ended;
                playback.Open(uri);
            }
            else
            {
                var width = MediaStage.ActualWidth > 0 ? MediaStage.ActualWidth : 460;
                var image = _image = new BitmapImage { DecodePixelWidth = (int)Math.Clamp(width * (XamlRoot?.RasterizationScale ?? 1), 320, 2048) };
                image.ImageOpened += Image_Opened;
                image.ImageFailed += Image_Failed;
                StoryImage.Source = image;
                image.UriSource = uri;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!IsCurrent(request)) return;
            AppLog.Error("Story viewer could not open the story.", exception);
            ShowError("StoryOpenFailed");
        }
    }

    private void SetTitle(string? title)
    {
        StoryTitle.Text = title ?? string.Empty;
        StoryTitle.Visibility = string.IsNullOrWhiteSpace(title) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task ShowOpeningPreviewAsync(MusicCoverTransition? transition)
    {
        if (transition is null || !ReferenceEquals(_openingTransition, transition)) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
        _transitionLoad = cancellation;
        var token = cancellation.Token;
        try
        {
            var width = MediaStage.ActualWidth > 0 ? MediaStage.ActualWidth : 460;
            var preview = await transition.TakePreviewAsync((int)Math.Clamp(width * (XamlRoot?.RasterizationScale ?? 1), 320, 2048), token);
            if (_closing || _closed || !ReferenceEquals(_openingTransition, transition)) return;
            if (preview is null) { CancelOpeningTransition(clearPreview: true); return; }
            TransitionImage.Source = preview;
            TransitionImage.Visibility = Visibility.Visible;
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void LayoutReady(object? sender, object args)
            {
                if (TransitionImage.IsLoaded && TransitionImage.ActualWidth > 0 && TransitionImage.ActualHeight > 0) ready.TrySetResult();
            }
            TransitionImage.LayoutUpdated += LayoutReady;
            try
            {
                LayoutReady(null, EventArgs.Empty);
                await ready.Task.WaitAsync(TimeSpan.FromMilliseconds(500), token);
            }
            finally { TransitionImage.LayoutUpdated -= LayoutReady; }
            if (_closing || _closed || token.IsCancellationRequested || !ReferenceEquals(_openingTransition, transition)) return;
            transition.Animation.Completed += OpeningAnimationCompleted;
            _transitionAnimating = true;
            if (transition.Animation.TryStart(TransitionImage)) _ = EndOpeningTransitionAfterAsync(transition);
            else CancelOpeningTransition(clearPreview: _ready);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (TimeoutException) { if (ReferenceEquals(_openingTransition, transition)) CancelOpeningTransition(clearPreview: true); }
        catch (Exception exception)
        {
            AppLog.Error("Story preview transition was skipped.", exception);
            if (ReferenceEquals(_openingTransition, transition)) CancelOpeningTransition(clearPreview: true);
        }
        finally { if (ReferenceEquals(_transitionLoad, cancellation)) _transitionLoad = null; }
    }

    private async Task EndOpeningTransitionAfterAsync(MusicCoverTransition transition)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
        _transitionTimeout = cancellation;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellation.Token);
            if (ReferenceEquals(_openingTransition, transition)) CancelOpeningTransition(clearPreview: _ready);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally { if (ReferenceEquals(_transitionTimeout, cancellation)) _transitionTimeout = null; }
    }

    private void OpeningAnimationCompleted(ConnectedAnimation sender, object args)
    {
        if (ReferenceEquals(_openingTransition?.Animation, sender)) CancelOpeningTransition(clearPreview: _ready);
    }

    private void CancelOpeningTransition(bool clearPreview)
    {
        var transition = _openingTransition;
        _openingTransition = null;
        _transitionAnimating = false;
        // Async owners dispose their sources after outstanding waits have drained.
        _transitionLoad?.Cancel();
        _transitionTimeout?.Cancel();
        if (transition is not null)
        {
            transition.Animation.Completed -= OpeningAnimationCompleted;
            try { transition.Dispose(); }
            catch (Exception exception) { AppLog.Error("Story preview transition could not be canceled.", exception); }
        }
        StoryContent.Opacity = 1;
        if (clearPreview) { TransitionImage.Source = null; TransitionImage.Visibility = Visibility.Collapsed; }
        UpdatePlaybackState();
    }

    private async Task LoadCreatorPictureAsync(MusicPageViewModel viewModel, Guid mediaPublicId, StoryRequest request)
    {
        using var lease = request.Retain();
        try
        {
            var path = await viewModel.PrepareStoryPlaybackAsync(mediaPublicId, request.Token);
            if (IsCurrent(request) && !string.IsNullOrWhiteSpace(path)) CreatorPicture.ProfilePicture = ImageSourceFactory.Create(path, 96);
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (Exception exception) { AppLog.Error("Story creator image could not be loaded.", exception); }
    }

    private void Playback_Opened(object? sender, EventArgs args) { if (ReferenceEquals(sender, _playback)) ContentReady(); }
    private void Playback_Failed(object? sender, string message) { if (ReferenceEquals(sender, _playback)) ShowError("VideoFailed"); }
    private void Playback_Ended(object? sender, EventArgs args) { if (ReferenceEquals(sender, _playback)) _ = AdvanceAsync(); }
    private void Image_Opened(object sender, RoutedEventArgs args) { if (ReferenceEquals(sender, _image)) ContentReady(); }
    private void Image_Failed(object sender, ExceptionRoutedEventArgs args) { if (ReferenceEquals(sender, _image)) ShowError("ImageFailed"); }

    private void ClearImage()
    {
        var image = _image;
        _image = null;
        if (image is not null) { image.ImageOpened -= Image_Opened; image.ImageFailed -= Image_Failed; }
        StoryImage.Source = null;
    }

    private void ContentReady()
    {
        if (_closing || _closed || _ready || _detail is null || _request is not { } request || !IsCurrent(request)) return;
        _ready = true;
        _presentationReady.TrySetResult(true);
        if (_openingTransition is null) CancelOpeningTransition(clearPreview: true);
        LoadingPanel.Visibility = Visibility.Collapsed;
        LoadingRing.IsActive = false;
        PauseButton.IsEnabled = true;
        PositionWidgets();
        UpdatePlaybackState();
    }

    private void UpdatePlaybackState()
    {
        var presented = _ready && !_closing && !_closed && IsLoaded && _root?.IsHostVisible != false
            && !_entrancePending && !_transitionAnimating && _openingTransition is null
            && _request is { } currentRequest && IsCurrent(currentRequest);
        if (presented && !_paused && _clientDialog is null)
        {
            _playback?.Play();
            if (!_ready || _closing || _closed) return;
            if (_playback is null) _imageClock.Start();
            _timer.Start();
        }
        else { _timer.Stop(); _imageClock.Stop(); _playback?.Pause(); }
        if (presented && !_marked && _request is { } request && _viewModel is { } model)
        {
            _marked = true;
            var description = Localization.Format("MusicSurface/StoryAnnouncement", _index + 1, _stories.Count, CreatorName.Text, StoryTitle.Text);
            AutomationProperties.SetName(this, description);
            AccessibilityAnnouncements.Announce(this, description, "story-ready");
            _ = MarkViewedAsync(model, _stories[_index], request);
        }
    }

    private async Task MarkViewedAsync(MusicPageViewModel model, StoryItemViewModel story, StoryRequest request)
    {
        using var lease = request.Retain();
        var detail = _detail!;
        var alreadyViewed = detail.ViewedByMe;
        try
        {
            var recorded = await model.MarkStoryAsViewedAsync(story, request.Token);
            if (!recorded || !IsCurrent(request)) return;
            if (!alreadyViewed) detail.ViewsCount++;
            detail.ViewedByMe = true;
            UpdateViews(detail.ViewsCount);
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (Exception exception) { AppLog.Error("Story view could not be recorded.", exception); }
    }

    private void UpdateViews(int count)
    {
        ViewsText.Text = count.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo(Localization.LanguageTag));
        AutomationProperties.SetName(ViewsText, Localization.Format("MusicSurface/ViewsCount", count));
    }

    private void Timer_Tick(object? sender, object args)
    {
        if (!_ready || _paused || _closing || _closed || _index >= _segments.Count) return;
        var playback = _playback;
        var duration = playback?.Duration.TotalSeconds ?? 6;
        var position = playback?.Position.TotalSeconds ?? _imageClock.Elapsed.TotalSeconds;
        if (!_ready || _closing || _closed) return;
        var progress = duration > 0 ? Math.Clamp(position / duration * 100, 0, 100) : 0;
        if (Math.Abs(_segments[_index].Value - progress) >= .1) _segments[_index].Value = progress;
        if (playback is null && progress >= 100) _ = AdvanceAsync();
    }

    private async Task AdvanceAsync()
    {
        if (_advancing || !_ready || _closing || _closed) return;
        _advancing = true;
        _timer.Stop();
        try { await MoveNextAsync(); }
        finally { _advancing = false; }
    }

    private void Pause_Click(object sender, RoutedEventArgs args)
    {
        if (!_ready || _closing || _closed) return;
        _paused = !_paused;
        UpdatePause();
        UpdatePlaybackState();
        AccessibilityAnnouncements.Announce(this, _paused ? Text("StoryPaused") : Text("StoryResumed"), "story-playback");
    }

    private void UpdatePause()
    {
        PauseIcon.Symbol = _paused ? FluentSymbol.Play : FluentSymbol.Pause;
        AutomationProperties.SetName(PauseButton, _paused ? Text("ResumeStory") : Text("PauseStory"));
        ToolTipService.SetToolTip(PauseButton, _paused ? Text("Resume") : Text("Pause"));
    }

    private async void Like_Click(object sender, RoutedEventArgs args)
    {
        if (_detail is not { } detail) return;
        try { await SetLikedAsync(!detail.LikedByMe); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { AppLog.Error("Story like could not be updated.", exception); }
    }

    private void UpdateLike()
    {
        if (_detail is null) return;
        LikeIcon.Foreground = new SolidColorBrush(_detail.LikedByMe ? Color.FromArgb(255, 244, 97, 133) : Colors.White);
        LikesText.Text = _detail.LikesCount.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo(Localization.LanguageTag));
        AutomationProperties.SetName(LikeButton, (_detail.LikedByMe ? Text("Unlike") : Text("LikeStoryAction")) + ", " + Localization.Format("MusicSurface/LikesCount", _detail.LikesCount));
    }

    private void ClearWidgets()
    {
        foreach (var (element, _) in _widgets) { element.Click -= Widget_Click; element.Tag = null; }
        _widgets.Clear();
        WidgetCanvas.Children.Clear();
    }

    private void BuildWidgets(StoryResponse detail)
    {
        foreach (var widget in detail.Widgets)
        {
            var title = widget.Music?.Title ?? (widget.Client is { } client ? $"{client.FirstName} {client.LastName}".Trim() : null);
            if (string.IsNullOrWhiteSpace(title)) continue;
            var button = new StoryActionButton
            {
                Content = (widget.Music is null ? "@ " : "♫ ") + title, Tag = widget,
                CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(Color.FromArgb(220, 24, 32, 48)), Foreground = new SolidColorBrush(Colors.White), FontSize = 12,
                FontFamily = Localization.TextFontFamily, FlowDirection = Localization.FlowDirection, Language = Localization.LanguageTag,
                RenderTransformOrigin = new global::Windows.Foundation.Point(.5, .5),
                RenderTransform = new RotateTransform { Angle = double.IsFinite(widget.Rotation) ? widget.Rotation : 0 }
            };
            AutomationProperties.SetName(button, (widget.Music is null ? Text("OpenProfilePrefix") : Text("OpenMusicPrefix")) + title);
            button.Click += Widget_Click;
            Canvas.SetZIndex(button, widget.ZIndex);
            WidgetCanvas.Children.Add(button);
            _widgets.Add((button, widget));
        }
        PositionWidgets();
    }

    private void PositionWidgets()
    {
        if (_closed || _closing || _widgets.Count == 0) return;
        var width = MediaStage.ActualWidth;
        var height = MediaStage.ActualHeight;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return;
        var playback = _playback;
        var sourceWidth = playback?.VideoWidth ?? _image?.PixelWidth ?? 0;
        var sourceHeight = playback?.VideoHeight ?? _image?.PixelHeight ?? 0;
        var left = 0d;
        var top = 0d;
        if (sourceWidth > 0 && sourceHeight > 0)
        {
            var scale = Math.Min(width / sourceWidth, height / sourceHeight);
            left = (width - sourceWidth * scale) / 2;
            top = (height - sourceHeight * scale) / 2;
            width = sourceWidth * scale;
            height = sourceHeight * scale;
        }
        foreach (var (element, widget) in _widgets)
        {
            static double Unit(double value, double minimum = 0) => double.IsFinite(value) ? Math.Clamp(value, minimum, 1) : minimum;
            var widgetWidth = Unit(widget.Width, .1);
            var widgetHeight = Unit(widget.Height);
            element.Width = widgetWidth * width;
            element.Height = Math.Max(28, widgetHeight * height);
            Canvas.SetLeft(element, left + Math.Min(Unit(widget.X), 1 - widgetWidth) * width);
            Canvas.SetTop(element, top + Math.Min(Unit(widget.Y), 1 - widgetHeight) * height);
        }
    }

    private async void Widget_Click(object sender, RoutedEventArgs args)
    {
        if ((sender as Button)?.Tag is not StoryWidgetResponse widget) return;
        try { await OpenWidgetAsync(widget, CancellationToken.None); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { AppLog.Error("Story tag could not be opened.", exception); }
    }

    // Shared by pointer/keyboard and MCP. Navigation is awaited through closing,
    // then checks consent before handing ownership to the destination page.
    private async Task<bool> OpenWidgetAsync(StoryWidgetResponse widget, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_closing || _closed || _clientDialog is not null) return false;
        if (widget.Music is { } music && _viewModel is not null)
        {
            var navigation = _viewModel.NavigationService;
            await CloseAsync();
            token.ThrowIfCancellationRequested();
            navigation.InternalNavigate(typeof(MusicsPage), new() { [PageConstants.MUSIC_ID] = music.MusicPublicId });
            return true;
        }
        if (widget.Client is not { } client || XamlRoot is null) return false;
        var dialog = _clientDialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = $"{client.FirstName} {client.LastName}".Trim(),
            Content = client.Username ?? string.Empty, CloseButtonText = Text("Close"),
            FlowDirection = Localization.FlowDirection, FontFamily = Localization.TextFontFamily,
            Language = Localization.LanguageTag
        };
        UpdatePlaybackState();
        // The observed task owns the modal until dismissal, without holding an MCP
        // request open for as long as a person reads the preview.
        _ = ObserveClientDialogAsync(dialog, _request);
        return ReferenceEquals(_clientDialog, dialog);
    }

    private async Task ObserveClientDialogAsync(ContentDialog dialog, StoryRequest? request)
    {
        try { await dialog.ShowAsync(); }
        catch (Exception exception) { if (!_closed) AppLog.Error("Story profile could not be shown.", exception); }
        finally
        {
            dialog.Content = null;
            if (ReferenceEquals(_clientDialog, dialog)) _clientDialog = null;
            if (request is not null && IsCurrent(request)) UpdatePlaybackState();
        }
    }

    private void ShowError(string message)
    {
        if (_closing || _closed) return;
        _presentationReady.TrySetResult(false);
        _ready = false;
        StopPlayback();
        CancelOpeningTransition(clearPreview: true);
        ClearImage();
        LoadingPanel.Visibility = Visibility.Collapsed;
        LoadingRing.IsActive = false;
        PauseButton.IsEnabled = false;
        _errorResourceKey = message;
        ErrorText.Text = Text(message);
        ErrorPanel.Visibility = Visibility.Visible;
        AccessibilityAnnouncements.Announce(this, ErrorText.Text, "story-error", important: true);
    }

    private void StopPlayback()
    {
        _timer.Stop();
        _imageClock.Reset();
        var playback = _playback;
        _playback = null;
        if (playback is null) return;
        playback.Opened -= Playback_Opened;
        playback.Failed -= Playback_Failed;
        playback.Ended -= Playback_Ended;
        playback.Dispose();
    }

    private void BeginClosing()
    {
        if (_closing) return;
        _closing = true;
        _presentationReady.TrySetResult(false);
        _version++;
        _lifetime.Cancel();
        _request?.Cancel();
        _timer.Stop();
        _imageClock.Stop();
        _playback?.Pause();
        _clientDialog?.Hide();
        LoadingRing.IsActive = false;
        ViewerCard.IsHitTestVisible = false;
        CancelOpeningTransition(clearPreview: false);
    }

    public Task CloseAsync()
    {
        if (_closed) return Task.CompletedTask;
        if (_closeTask is not null) return _closeTask;
        BeginClosing();
        return _closeTask = CloseAnimatedAsync();
    }

    private async Task CloseAnimatedAsync()
    {
        try { await AnimateCloseAsync(CancellationToken.None); }
        finally { Close(); }
    }

    public void Close()
    {
        DetachLocalization();
        if (_closed) return;
        BeginClosing();
        _closed = true;
        _externalCancellation.Dispose();
        ReleaseMotion();
        CancelOpeningTransition(clearPreview: true);
        StopPlayback();
        ClearImage();
        ClearWidgets();
        _timer.Tick -= Timer_Tick;
        if (_root is not null) _root.Changed -= Root_Changed;
        _root = null;
        CreatorPicture.ProfilePicture = null;
        _clientDialog = null;
        _request = null;
        _detail = null;
        _viewModel = null;
        _stories = [];
        _segments.Clear();
        ProgressSegments.Children.Clear();
        ProgressSegments.ColumnDefinitions.Clear();
        _lifetime.Dispose();
        var closed = CloseRequested;
        CloseRequested = null;
        closed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Close();
    private Task MoveNextAsync() => _index + 1 >= _stories.Count ? CloseAsync() : OpenIndexAsync(_index + 1);
    private async void Next_Click(object sender, RoutedEventArgs args) { if (!_closing && !_closed) await MoveNextAsync(); }
    private async void Previous_Click(object sender, RoutedEventArgs args) => await OpenIndexAsync(_index - 1);
    private async void Retry_Click(object sender, RoutedEventArgs args) => await OpenIndexAsync(_index);
    private async void Close_Click(object sender, RoutedEventArgs args) => await CloseAsync();
    private async void Backdrop_PointerPressed(object sender, PointerRoutedEventArgs args) { args.Handled = true; await CloseAsync(); }
    private void Viewer_Unloaded(object sender, RoutedEventArgs args) { if (!IsLoaded) Close(); }
    private void Viewer_Loaded(object sender, RoutedEventArgs args)
    {
        if (_closed) return;
        AttachLocalization();
        if (!ReferenceEquals(_root, XamlRoot))
        {
            if (_root is not null) _root.Changed -= Root_Changed;
            _root = XamlRoot;
            if (_root is not null) _root.Changed += Root_Changed;
        }
        _loaded.TrySetResult();
        LoadingRing.IsActive = LoadingPanel.Visibility == Visibility.Visible && _root?.IsHostVisible != false;
        UpdatePlaybackState();
    }
    private void Root_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (_closing || _closed) return;
        LoadingRing.IsActive = LoadingPanel.Visibility == Visibility.Visible && sender.IsHostVisible;
        UpdatePlaybackState();
    }
    private void MediaStage_SizeChanged(object sender, SizeChangedEventArgs args) => PositionWidgets();

    private async void Viewer_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Handled || _closing || _closed) return;
        if (args.Key == VirtualKey.Escape) { args.Handled = true; await CloseAsync(); return; }
        // Focused controls own Space/Enter; do not also toggle playback on their click.
        for (var source = args.OriginalSource as DependencyObject; source is not null && !ReferenceEquals(source, this); source = VisualTreeHelper.GetParent(source))
            if (source is ButtonBase or TextBox or PasswordBox or Slider) return;
        switch (args.Key)
        {
            case VirtualKey.Left: args.Handled = true; await OpenIndexAsync(_index - 1); break;
            case VirtualKey.Right: args.Handled = true; await MoveNextAsync(); break;
            case VirtualKey.Space: args.Handled = true; Pause_Click(this, new RoutedEventArgs()); break;
        }
    }

    // The source remains alive until detail/avatar/view/like operations finish.
    // All retain/release operations are owned by the UI dispatcher.
    private sealed class StoryRequest
    {
        private readonly CancellationTokenSource _cancellation;
        private int _pending;
        private bool _canceled, _disposed;
        public CancellationToken Token { get; }
        public StoryRequest(CancellationToken token)
        {
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            Token = _cancellation.Token;
        }
        public IDisposable Retain() { Token.ThrowIfCancellationRequested(); _pending++; return new Lease(this); }
        public void Cancel()
        {
            if (_canceled) return;
            _canceled = true;
            _cancellation.Cancel();
            TryDispose();
        }
        private void TryDispose()
        {
            if (!_canceled || _pending != 0 || _disposed) return;
            _disposed = true;
            _cancellation.Dispose();
        }
        private sealed class Lease(StoryRequest owner) : IDisposable
        {
            private StoryRequest? _owner = owner;
            public void Dispose()
            {
                var owner = _owner;
                _owner = null;
                if (owner is null) return;
                owner._pending--;
                owner.TryDispose();
            }
        }
    }
}
