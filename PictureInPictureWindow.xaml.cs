using FluentIcons.Common;
using DevWinUI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using System;
using System.IO;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.ViewManagement;
using Symbol = FluentIcons.Common.Symbol;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Mizon.Windows;

public sealed partial class PictureInPictureWindow : Window
{
    private IPictureInPictureSession? _session;
    private readonly DispatcherQueueTimer _refresh;
    private readonly DispatcherQueueTimer _seekDebounce;
    private readonly UISettings _settings = new();
    private bool _closed, _updating, _scrubbing, _commandBusy;
    private object? _seekIdentity;
    private string? _artworkPath;
    private double _lastPulse = 1;
    private readonly PointerEventHandler _seekPressed, _seekReleased, _seekLost;
    private readonly PointerEventHandler _pointerActivity;
    private readonly KeyEventHandler _keyActivity;
    private long _lastInteraction = Environment.TickCount64;
    private int _openFlyouts;
    private bool _controlsHidden;
    private Task _pendingCommand = Task.CompletedTask;
    private SpriteVisual? _artworkGlow;
    private DropShadow? _artworkShadow;
    private string? _accentText;
    private bool _accentInitialized;
    private global::Windows.UI.Color _accent = global::Windows.UI.Color.FromArgb(255, 145, 222, 197);
    private bool? _wasPlaying;
    private float _lastLevel = -1;

    public PictureInPictureWindow(IPictureInPictureSession session)
    {
        InitializeComponent();
        _refresh = DispatcherQueue.CreateTimer();
        _refresh.Interval = TimeSpan.FromMilliseconds(200);
        _refresh.Tick += Refresh_Tick;
        _seekDebounce = DispatcherQueue.CreateTimer();
        _seekDebounce.Interval = TimeSpan.FromMilliseconds(180);
        _seekDebounce.IsRepeating = false;
        _seekDebounce.Tick += Seek_Tick;
        _seekPressed = (_, _) => { _scrubbing = true; _seekDebounce.Stop(); _seekIdentity = _session?.MediaIdentity; };
        _seekReleased = (_, _) => { if (_scrubbing) { _scrubbing = false; CommitSeek(); } };
        _seekLost = (_, _) => { if (_scrubbing) { _scrubbing = false; CommitSeek(); } };
        SeekSlider.AddHandler(UIElement.PointerPressedEvent, _seekPressed, true);
        SeekSlider.AddHandler(UIElement.PointerReleasedEvent, _seekReleased, true);
        SeekSlider.AddHandler(UIElement.PointerCaptureLostEvent, _seekLost, true);
        _pointerActivity = (_, _) => ShowControls();
        _keyActivity = (_, _) => ShowControls();
        Root.AddHandler(UIElement.PointerMovedEvent, _pointerActivity, true);
        Root.AddHandler(UIElement.PointerPressedEvent, _pointerActivity, true);
        Root.AddHandler(UIElement.KeyDownEvent, _keyActivity, true);
        AddShortcut(VirtualKey.Space, VirtualKeyModifiers.None, () => RunAsync(s => s.ToggleAsync()));
        AddShortcut(VirtualKey.Left, VirtualKeyModifiers.Control, () => RunAsync(s => s.SeekAsync(s.Position - TimeSpan.FromSeconds(10))));
        AddShortcut(VirtualKey.Right, VirtualKeyModifiers.Control, () => RunAsync(s => s.SeekAsync(s.Position + TimeSpan.FromSeconds(10))));
        AddShortcut(VirtualKey.Escape, VirtualKeyModifiers.None, () => { Close(); return Task.CompletedTask; });
        Closed += Window_Closed;
        AppWindow.Changed += AppWindow_Changed;
        try
        {
            Title = "Mizon · PiP";
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon", "Mizon.ico"));
            AppWindow.IsShownInSwitchers = false;
            // Use the same backdrop and defaults as MainWindow.
            SystemBackdrop = new AcrylicSystemBackdrop(DesktopAcrylicKind.Default);
            SetSize(CompactOverlaySize.Large);
            SetSession(session);
        }
        catch
        {
            // A failed native presenter or surface must not leave a hidden HWND
            // or live dispatcher timer behind after the constructor throws.
            Close();
            throw;
        }
    }

    internal void SetSession(IPictureInPictureSession session)
    {
        _refresh.Stop();
        _seekDebounce.Stop(); _scrubbing = false;
        _session?.Dispose();
        _session = session;
        _artworkPath = null;
        ShowControls();
        Artwork.Source = null;
        try
        {
            session.AttachVideo(VideoSurface);
            VideoSurface.Visibility = session.IsVideo ? Visibility.Visible : Visibility.Collapsed;
            VideoScrim.Visibility = session.IsVideo ? Visibility.Visible : Visibility.Collapsed;
            MusicHero.Visibility = session.IsVideo ? Visibility.Collapsed : Visibility.Visible;
            MediaKindText.Text = session.IsVideo ? "ویدیو" : "پلیر شناور";
            Refresh();
            _refresh.Start();
        }
        catch { _session = null; session.Dispose(); throw; }
    }

    internal Task PauseVideoAsync() => _session is { IsVideo: true, IsPlaying: true } session
        ? session.ToggleAsync() : Task.CompletedTask;

    internal Task WaitForPendingCommandAsync() => _pendingCommand;

    internal void UpdateVideoMetadata(string title, string? subtitle, string? artworkPath)
    {
        if (_session is VideoPictureInPictureSession video) video.UpdateMetadata(title, subtitle, artworkPath);
    }

    private void Root_Loaded(object sender, RoutedEventArgs args)
    {
        UpdateCaptionInset();
        EnsureArtworkGlow();
        if (!_settings.AnimationsEnabled) return;
        var visual = ElementCompositionPreview.GetElementVisual(LayoutRoot);
        using var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, 0); animation.InsertKeyFrame(1, 1);
        animation.Duration = TimeSpan.FromMilliseconds(220);
        visual.StartAnimation("Opacity", animation);
    }
    private void Root_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        var size = Math.Clamp(args.NewSize.Height - 144, 42, 118);
        ArtworkHost.Width = ArtworkHost.Height = size;
        TrackTitle.FontSize = args.NewSize.Width < 330 ? 14 : args.NewSize.Height < 230 ? 15 : 19;
        TrackSubtitle.FontSize = args.NewSize.Height < 230 ? 10 : 11;
        TrackTitle.LineHeight = args.NewSize.Height < 230 ? 20 : 26;
        TrackSubtitle.LineHeight = args.NewSize.Height < 230 ? 14 : 16;
        TrackTitle.MaxLines = args.NewSize.Height < 240 ? 1 : 2;
        StateBadge.Visibility = args.NewSize.Height < 200 ? Visibility.Collapsed : Visibility.Visible;
        MediaKindText.MaxWidth = Math.Max(0, args.NewSize.Width - 190);
        LayoutRoot.Padding = args.NewSize.Width < 330 ? new Thickness(10, 0, 10, 8) : new Thickness(12, 0, 12, 10);
        UpdateCaptionInset();
        if (_artworkGlow is not null) _artworkGlow.Size = new Vector2((float)size, (float)size);
    }
    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!_closed) UpdateCaptionInset();
    }
    private void UpdateCaptionInset()
    {
        var scale = Root.XamlRoot?.RasterizationScale ?? 1;
        CaptionInset.Width = new GridLength(Math.Max(0, AppWindow.TitleBar.RightInset / scale - LayoutRoot.Padding.Right));
    }
    private void EnsureArtworkGlow()
    {
        if (_artworkGlow is not null) return;
        var compositor = ElementCompositionPreview.GetElementVisual(ArtworkHost).Compositor;
        _artworkShadow = compositor.CreateDropShadow();
        _artworkShadow.BlurRadius = 28;
        _artworkShadow.Opacity = .32f;
        _artworkShadow.Offset = new Vector3(0, 4, 0);
        _artworkShadow.Color = _accent;
        _artworkGlow = compositor.CreateSpriteVisual();
        _artworkGlow.Size = new Vector2((float)ArtworkHost.Width, (float)ArtworkHost.Height);
        _artworkGlow.Shadow = _artworkShadow;
        ElementCompositionPreview.SetElementChildVisual(ArtworkGlowHost, _artworkGlow);
    }
    private void ApplyAccent(string? value)
    {
        if (_accentInitialized && string.Equals(value, _accentText, StringComparison.Ordinal)) return;
        _accentInitialized = true;
        _accentText = value;
        _accent = global::Windows.UI.Color.FromArgb(255, 145, 222, 197);
        var hex = value.AsSpan().Trim().TrimStart('#');
        if ((hex.Length == 6 || hex.Length == 8) && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            _accent = global::Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        // Keep dark artwork accents legible on the slate surface; preserve its hue.
        var readable = MixWithWhite(_accent, .35);
        ((SolidColorBrush)Root.Resources["PipAccent"]).Color = readable;
        ArtworkEdgeTint.Color = WithAlpha(readable, 90);
        PlayTint.Color = MixWithWhite(_accent, .82);
        if (_artworkShadow is not null) _artworkShadow.Color = _accent;
        _wasPlaying = null;
    }
    private static global::Windows.UI.Color WithAlpha(global::Windows.UI.Color color, byte alpha) => global::Windows.UI.Color.FromArgb(alpha, color.R, color.G, color.B);
    private static global::Windows.UI.Color MixWithWhite(global::Windows.UI.Color color, double amount) => global::Windows.UI.Color.FromArgb(255,
        (byte)(color.R + (255 - color.R) * amount), (byte)(color.G + (255 - color.G) * amount), (byte)(color.B + (255 - color.B) * amount));
    private void Refresh_Tick(DispatcherQueueTimer sender, object args)
    {
        try { Refresh(); }
        catch (Exception error) { ShowError(error); _refresh.Stop(); }
    }
    private void Refresh()
    {
        if (_closed || _session is not { } session) return;
        _updating = true;
        try
        {
            var title = session.Title;
            if (TrackTitle.Text != title)
            {
                TrackTitle.Text = title;
                Title = "Mizon · " + title;
            }
            TrackSubtitle.Text = session.Subtitle;
            if (session.IsVideo) MediaKindText.Text = title;
            ApplyAccent(session.AccentColor);
            var busy = _commandBusy || session.IsBusy;
            PlayButton.IsEnabled = !busy;
            NextButton.IsEnabled = !busy && session.CanNext;
            PreviousButton.IsEnabled = !busy && session.CanPrevious;
            SeekSlider.IsEnabled = !busy && session.CanSeek;
            var playing = session.IsPlaying;
            PlayIcon.Symbol = playing ? Symbol.Pause : Symbol.Play;
            AutomationProperties.SetName(PlayButton, playing ? "مکث" : "پخش");
            StateText.Text = busy ? "آماده‌سازی…" : playing ? "در حال پخش" : "آمادهٔ شنیدن";
            if (_wasPlaying != playing)
            {
                _wasPlaying = playing;
                var stateColor = playing ? _accent : global::Windows.UI.Color.FromArgb(255, 230, 255, 13);
                StateFill.Color = WithAlpha(stateColor, 18);
                StateStroke.Color = WithAlpha(stateColor, 32);
            }
            var duration = Math.Max(0, session.Duration.TotalSeconds);
            SeekSlider.Maximum = Math.Max(1, duration);
            // Never overwrite the thumb while the user is scrubbing or a keyboard
            // seek is awaiting its debounce. Commit only once after a drag.
            if (!_scrubbing && !_seekDebounce.IsRunning)
                SeekSlider.Value = Math.Clamp(session.Position.TotalSeconds, 0, SeekSlider.Maximum);
            PositionText.Text = Format(TimeSpan.FromSeconds(SeekSlider.Value));
            DurationText.Text = Format(session.Duration);
            VolumeSlider.Value = Math.Clamp(session.Volume * 100, 0, 100);
            if (!string.Equals(_artworkPath, session.ArtworkPath, StringComparison.Ordinal))
            {
                _artworkPath = session.ArtworkPath;
                Artwork.Source = ImageSourceFactory.Create(_artworkPath, 480);
            }
            Pulse(playing ? session.Intensity : 0);
            AnimateLevel(playing ? session.Intensity : 0);
            var keyboardFocus = session.IsVideo && Root.XamlRoot is { } root
                && FocusManager.GetFocusedElement(root) is Control { FocusState: FocusState.Keyboard };
            SetControlsHidden(session.IsVideo && session.IsPlaying && !busy && !_scrubbing
                && _openFlyouts == 0 && !keyboardFocus && Environment.TickCount64 - _lastInteraction > 2800);
        }
        finally { _updating = false; }
    }
    private static string Format(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
    private void ShowControls()
    {
        _lastInteraction = Environment.TickCount64;
        SetControlsHidden(false);
    }
    private void SetControlsHidden(bool hidden)
    {
        if (_controlsHidden == hidden || _closed) return;
        _controlsHidden = hidden;
        LayoutRoot.IsHitTestVisible = !hidden;
        FadeControls(LayoutRoot, hidden ? 0 : 1);
        FadeControls(VideoScrim, hidden ? 0 : 1);
    }
    private void FadeControls(UIElement element, float opacity)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        if (!_settings.AnimationsEnabled) { visual.StopAnimation("Opacity"); visual.Opacity = opacity; return; }
        using var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertExpressionKeyFrame(0, "this.StartingValue");
        animation.InsertKeyFrame(1, opacity);
        animation.Duration = TimeSpan.FromMilliseconds(180);
        visual.StartAnimation("Opacity", animation);
    }
    private void ControlsFlyout_Opened(object sender, object args) { ++_openFlyouts; ShowControls(); }
    private void ControlsFlyout_Closed(object sender, object args) { _openFlyouts = Math.Max(0, _openFlyouts - 1); ShowControls(); }
    private void Pulse(float value)
    {
        var target = _settings.AnimationsEnabled ? 1 + Math.Clamp(value, 0, 1) * .018 : 1;
        if (Math.Abs(target - _lastPulse) < .001) return;
        _lastPulse = target;
        var visual = ElementCompositionPreview.GetElementVisual(ArtworkHost);
        visual.CenterPoint = new Vector3((float)ArtworkHost.ActualWidth / 2, (float)ArtworkHost.ActualHeight / 2, 0);
        using var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertExpressionKeyFrame(0, "this.StartingValue");
        animation.InsertKeyFrame(1, new Vector3((float)target, (float)target, 1));
        animation.Duration = TimeSpan.FromMilliseconds(200);
        if (_settings.AnimationsEnabled) visual.StartAnimation("Scale", animation);
        else { visual.StopAnimation("Scale"); visual.Scale = Vector3.One; }
    }
    private void AnimateLevel(float intensity)
    {
        var level = float.IsFinite(intensity) ? Math.Clamp(intensity, 0, 1) : 0;
        if (Math.Abs(level - _lastLevel) < .015f) return;
        _lastLevel = level;
        AnimateLevelBar(LevelOne, .35f + MathF.Sqrt(level) * .65f);
        AnimateLevelBar(LevelTwo, .25f + MathF.Sqrt(level) * .75f);
        AnimateLevelBar(LevelThree, .3f + MathF.Sqrt(level) * .7f);
    }
    private void AnimateLevelBar(FrameworkElement element, float scale)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        if (!_settings.AnimationsEnabled) { visual.StopAnimation("Scale"); visual.Scale = Vector3.One; return; }
        using var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertExpressionKeyFrame(0, "this.StartingValue");
        animation.InsertKeyFrame(1, new Vector3(1, scale, 1));
        animation.Duration = TimeSpan.FromMilliseconds(180);
        visual.StartAnimation("Scale", animation);
    }
    private void Seek_ValueChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_updating || _closed || _session is null) return;
        PositionText.Text = Format(TimeSpan.FromSeconds(args.NewValue));
        if (_scrubbing) return;
        _seekIdentity = _session.MediaIdentity;
        _seekDebounce.Stop(); _seekDebounce.Start();
    }
    private void Seek_Tick(DispatcherQueueTimer sender, object args) => CommitSeek();
    private void CommitSeek()
    {
        _seekDebounce.Stop();
        try
        {
            if (_closed) return;
            if (_session is null || !Equals(_seekIdentity, _session.MediaIdentity)) { Refresh(); return; }
            var position = TimeSpan.FromSeconds(SeekSlider.Value);
            _ = RunAsync(session => session.SeekAsync(position));
        }
        catch (Exception error) { ShowError(error); }
    }
    private void Volume_ValueChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_updating || _closed || _session is null) return;
        try { _session.Volume = args.NewValue / 100; }
        catch (Exception error) { ShowError(error); }
    }
    private Task RunAsync(Func<IPictureInPictureSession, Task> action)
    {
        if (_closed || _commandBusy || _session is not { } session) return Task.CompletedTask;
        _commandBusy = true; ErrorPanel.Visibility = Visibility.Collapsed;
        return _pendingCommand = RunCommandAsync(session, action);
    }
    private async Task RunCommandAsync(IPictureInPictureSession session, Func<IPictureInPictureSession, Task> action)
    {
        try { Refresh(); await action(session); }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_closed) ShowError(error); }
        finally
        {
            _commandBusy = false;
            if (!_closed)
            {
                try { Refresh(); }
                catch (Exception error) { ShowError(error); }
            }
        }
    }
    private void ShowError(Exception error)
    {
        AppLog.Error("PiP operation failed.", error);
        if (!_closed) { ShowControls(); ErrorPanel.Visibility = Visibility.Visible; }
    }
    private async void Play_Click(object sender, RoutedEventArgs args) => await RunAsync(s => s.ToggleAsync());
    private async void Next_Click(object sender, RoutedEventArgs args) => await RunAsync(s => s.NextAsync());
    private async void Previous_Click(object sender, RoutedEventArgs args) => await RunAsync(s => s.PreviousAsync());
    private void Return_Click(object sender, RoutedEventArgs args)
    {
        try { _session?.ReturnToApp(); Close(); }
        catch (Exception error) { ShowError(error); }
    }
    private void Size_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            if (sender is MenuFlyoutItem item && Enum.TryParse<CompactOverlaySize>(item.Tag?.ToString(), out var size)) SetSize(size);
        }
        catch (Exception error) { ShowError(error); }
    }
    private void SetSize(CompactOverlaySize size)
    {
        // Size is controlled by the OS, in monitor-aware logical units.
        var presenter = CompactOverlayPresenter.Create();
        presenter.InitialSize = size;
        AppWindow.SetPresenter(presenter);
        // Keep content beneath the native caption area; Windows owns its close
        // button. The AppWindow property also repairs a prior ResetToDefault.
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonForegroundColor = Microsoft.UI.Colors.White;
        UpdateCaptionInset();
    }
    private void AddShortcut(VirtualKey key, VirtualKeyModifiers modifiers, Func<Task> action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += async (_, args) => { args.Handled = true; await action(); };
        Root.KeyboardAccelerators.Add(accelerator);
    }
    private void Window_Closed(object sender, WindowEventArgs args)
    {
        _closed = true;
        AppWindow.Changed -= AppWindow_Changed;
        // Disconnect the backdrop target and release the window's reference.
        SystemBackdrop = null;
        _refresh.Stop(); _refresh.Tick -= Refresh_Tick;
        _seekDebounce.Stop(); _seekDebounce.Tick -= Seek_Tick;
        SeekSlider.RemoveHandler(UIElement.PointerPressedEvent, _seekPressed);
        SeekSlider.RemoveHandler(UIElement.PointerReleasedEvent, _seekReleased);
        SeekSlider.RemoveHandler(UIElement.PointerCaptureLostEvent, _seekLost);
        Root.RemoveHandler(UIElement.PointerMovedEvent, _pointerActivity);
        Root.RemoveHandler(UIElement.PointerPressedEvent, _pointerActivity);
        Root.RemoveHandler(UIElement.KeyDownEvent, _keyActivity);
        Root.KeyboardAccelerators.Clear();
        ElementCompositionPreview.GetElementVisual(ArtworkHost).StopAnimation("Scale");
        ElementCompositionPreview.GetElementVisual(LayoutRoot).StopAnimation("Opacity");
        ElementCompositionPreview.GetElementVisual(VideoScrim).StopAnimation("Opacity");
        ElementCompositionPreview.GetElementVisual(LevelOne).StopAnimation("Scale");
        ElementCompositionPreview.GetElementVisual(LevelTwo).StopAnimation("Scale");
        ElementCompositionPreview.GetElementVisual(LevelThree).StopAnimation("Scale");
        ElementCompositionPreview.SetElementChildVisual(ArtworkGlowHost, null);
        _artworkGlow?.Dispose(); _artworkGlow = null;
        _artworkShadow?.Dispose(); _artworkShadow = null;
        _session?.Dispose(); _session = null;
        Artwork.Source = null;
        Closed -= Window_Closed;
    }
}
