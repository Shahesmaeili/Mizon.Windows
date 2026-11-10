using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Controls;
using Windows.Foundation;
using Windows.UI.ViewManagement;
using WinRT;

namespace Mizon.Windows.Taskbar;

/// <summary>Shows the account's cached mood without opening another authenticated connection.</summary>
internal sealed partial class TaskbarMoodButton : Grid, IDisposable
{
    private const double EffectSize = 208;
    private readonly AuthActionButton _button;
    private readonly AnimatedMoodEmoji _loop;
    private readonly TextBlock _fallback;
    private readonly UISettings _settings = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Guid? _moodId;
    private string? _animationPath, _effectPath;
    private AnimatedMoodEmoji? _effect;
    private Popup? _popup;
    private TaskbarEffectInputScope? _popupInput;
    private XamlRoot? _root;
    private Task? _preparation;
    private CancellationTokenSource? _preparationRequest, _playRequest;
    private TaskCompletionSource? _popupClosed;
    private bool _hostVisible = true, _loaded, _disposed;
    private long _identity;

    internal TaskbarMoodButton()
    {
        Width = Height = 36;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        _fallback = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 26,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        AutomationProperties.SetAccessibilityView(_fallback, AccessibilityView.Raw);
        _loop = new AnimatedMoodEmoji
        {
            Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _loop.Ready += LoopReady;
        _loop.Failed += LoopFailed;
        var content = new Grid { IsHitTestVisible = false };
        content.Children.Add(_fallback);
        content.Children.Add(_loop);
        _button = new AuthActionButton
        {
            Style = Application.Current.Resources["ProfileAvatarButtonStyle"].As<Style>(),
            HoverScale = 1.06, PressedScale = .91, Width = 36, Height = 36,
            MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
            CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent), Content = content,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch, UseSystemFocusVisuals = true,
            ClickMode = ClickMode.Press
        };
        _button.Click += ButtonClicked;
        Children.Add(_button);
        Loaded += ButtonLoaded;
        Unloaded += ButtonUnloaded;
        Visibility = Visibility.Collapsed;
    }

    internal bool HasMood { get; private set; }

    internal void Apply(ProfileSnapshot snapshot)
    {
        if (_disposed) return;
        var hasMood = snapshot.IsAuthenticated && snapshot.MoodPublicId is { } moodId && moodId != Guid.Empty
            && !string.IsNullOrWhiteSpace(snapshot.MoodEmoji);
        var animationPath = hasMood ? CachedPath(snapshot.MoodAnimationPath) : null;
        var effectPath = hasMood ? CachedPath(snapshot.MoodEffectPath) ?? animationPath : null;
        var identityChanged = _moodId != (hasMood ? snapshot.MoodPublicId : null);
        if (identityChanged || !PathEquals(_effectPath, effectPath))
        {
            ++_identity;
            InvalidateEffect();
        }

        _moodId = hasMood ? snapshot.MoodPublicId : null;
        _effectPath = effectPath;
        HasMood = hasMood;
        Visibility = hasMood ? Visibility.Visible : Visibility.Collapsed;
        _fallback.Text = hasMood ? snapshot.MoodEmoji! : string.Empty;
        var title = string.IsNullOrWhiteSpace(snapshot.MoodTitle) ? snapshot.MoodEmoji : snapshot.MoodTitle;
        AutomationProperties.SetName(_button, hasMood ? $"حال‌وهوای من: {title}" : "حال‌وهوا");
        AutomationProperties.SetHelpText(_button, "برای پخش انیمیشن حال‌وهوا کلیک کنید یا کلید فاصله را بزنید.");
        ToolTipService.SetToolTip(_button, hasMood ? title : null);
        _button.IsEnabled = hasMood;
        if (identityChanged || !PathEquals(_animationPath, animationPath))
        {
            _animationPath = animationPath;
            _fallback.Visibility = Visibility.Visible;
            _loop.AnimationPath = animationPath;
        }
        _loop.IsPlaybackEnabled = hasMood && _hostVisible;
        if (hasMood && _loaded && _hostVisible) _ = WarmEffectAsync();
    }

    private static bool PathEquals(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string? CachedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) return null;
            var local = Path.GetFullPath(path);
            return File.Exists(local) ? local : null;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TaskbarPaths.Log($"Mood cache path: {error.GetType().Name}.");
            return null;
        }
    }

    internal void SetHostVisible(bool visible)
    {
        if (_disposed || _hostVisible == visible) return;
        _hostVisible = visible;
        _loop.IsPlaybackEnabled = visible && HasMood;
        if (!visible) CancelPlayback();
        else if (_loaded) _ = WarmEffectAsync();
    }

    private void ButtonLoaded(object sender, RoutedEventArgs args)
    {
        if (_disposed || _loaded || !IsLoaded) return;
        _loaded = true;
        _root = XamlRoot;
        if (_root is not null) _root.Changed += RootChanged;
        _settings.AnimationsEnabledChanged += MotionPreferenceChanged;
        _loop.IsPlaybackEnabled = _hostVisible && HasMood;
        _ = WarmEffectAsync();
    }

    private void ButtonUnloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded || !_loaded) return;
        _loaded = false;
        CancelPlayback();
        DetachObservers();
    }

    private void DetachObservers()
    {
        if (_root is not null) _root.Changed -= RootChanged;
        _root = null;
        _settings.AnimationsEnabledChanged -= MotionPreferenceChanged;
    }

    private void LoopReady(object? sender, EventArgs args)
    {
        if (!_disposed) _fallback.Visibility = Visibility.Collapsed;
    }

    private void LoopFailed(object? sender, string message)
    {
        if (_disposed) return;
        _fallback.Visibility = Visibility.Visible;
        TaskbarPaths.Log("Mood loop: " + message);
    }

    private async void ButtonClicked(object sender, RoutedEventArgs args)
    {
        try { await PlayEffectAsync(); }
        catch (Exception error) { TaskbarPaths.Log($"Mood click: {error.GetType().Name}: {error.Message}"); }
    }

    private async Task WarmEffectAsync()
    {
        if (_disposed || !_loaded || !_hostVisible || !HasMood || !_settings.AnimationsEnabled || _effectPath is null) return;
        try { await EnsurePrepared(); }
        catch (OperationCanceledException) { }
        catch (Exception error) { TaskbarPaths.Log($"Mood effect preparation: {error.GetType().Name}: {error.Message}"); }
    }

    private Task EnsurePrepared()
    {
        if (_preparation is { IsCanceled: false, IsFaulted: false }) return _preparation;
        InvalidateEffect();
        var path = _effectPath ?? throw new InvalidOperationException("The mood effect is not cached yet.");
        var effect = _effect = new AnimatedMoodEmoji
        {
            Width = EffectSize, Height = EffectSize, IsLooping = false,
            RetainSourceOnUnload = true, IsHitTestVisible = false
        };
        AutomationProperties.SetAccessibilityView(effect, AccessibilityView.Raw);
        var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        request.CancelAfter(TimeSpan.FromSeconds(25));
        _preparationRequest = request;
        return _preparation = PrepareAsync(effect, path, request);
    }

    private async Task PrepareAsync(AnimatedMoodEmoji effect, string path, CancellationTokenSource request)
    {
        try { await effect.PrepareAsync(path, request.Token); }
        finally
        {
            if (ReferenceEquals(_preparationRequest, request)) _preparationRequest = null;
            request.Dispose();
        }
    }

    internal async Task PlayEffectAsync()
    {
        if (_disposed || !_loaded || !_hostVisible || !HasMood || XamlRoot?.IsHostVisible != true
            || !_settings.AnimationsEnabled || _effectPath is null) return;
        var preparation = EnsurePrepared();
        var effect = _effect!;
        var path = _effectPath;
        var identity = _identity;
        var previous = _playRequest;
        var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        request.CancelAfter(TimeSpan.FromSeconds(25));
        _playRequest = request;
        previous?.Cancel();
        try
        {
            await preparation.WaitAsync(request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (_disposed || !_loaded || !_hostVisible || identity != _identity || !ReferenceEquals(_effect, effect)) return;
            if (_popupClosed is { } closing) await closing.Task.WaitAsync(request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (_popup is null)
            {
                _popup = new Popup
                {
                    XamlRoot = XamlRoot, ShouldConstrainToRootBounds = false,
                    IsLightDismissEnabled = false, IsHitTestVisible = false, Child = effect
                };
                AutomationProperties.SetAccessibilityView(_popup, AccessibilityView.Raw);
                _popup.Closed += PopupClosed;
            }
            _loop.LayoutUpdated -= AnchorLayoutUpdated;
            _loop.LayoutUpdated += AnchorLayoutUpdated;
            effect.IsPlaybackEnabled = true;
            UpdateEffectPosition();
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            RoutedEventHandler loaded = (_, _) => ready.TrySetResult();
            effect.Loaded += loaded;
            try
            {
                _popup.IsOpen = true;
                if (!effect.IsLoaded) await ready.Task.WaitAsync(request.Token);
            }
            finally { effect.Loaded -= loaded; }
            request.Token.ThrowIfCancellationRequested();
            if (_popupInput is null) _popupInput = await PreparePopupInputAsync(effect, request.Token);
            TaskbarPaths.Log("Mood effect started in an unconstrained popup.");
            await effect.PlayOnceAsync(path, request.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { TaskbarPaths.Log($"Mood effect: {error.GetType().Name}: {error.Message}"); }
        finally
        {
            if (ReferenceEquals(_playRequest, request))
            {
                _playRequest = null;
                ClosePopup();
            }
            request.Dispose();
        }
    }

    internal void UpdateEffectPosition()
    {
        if (_disposed || _popup is not { } popup || !_loop.IsLoaded) return;
        var center = _loop.TransformToVisual(null).TransformPoint(new Point(_loop.ActualWidth / 2, _loop.ActualHeight / 2));
        var left = center.X - EffectSize / 2;
        var top = center.Y - EffectSize / 2;
        if (!double.IsFinite(left) || !double.IsFinite(top)) return;
        if (Math.Abs(popup.HorizontalOffset - left) > .01) popup.HorizontalOffset = left;
        if (Math.Abs(popup.VerticalOffset - top) > .01) popup.VerticalOffset = top;
    }

    private static async Task<TaskbarEffectInputScope> PreparePopupInputAsync(AnimatedMoodEmoji effect, CancellationToken token)
    {
        // Loaded can precede the native popup's first presentation, especially
        // after reopening. Give layout/composition time to publish its bounds;
        // never block the UI thread or configure an unrelated HWND as a fallback.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (TaskbarEffectInputScope.TryCreate(effect) is { } scope) return scope;
            await Task.Delay(16, token);
        }
        throw new InvalidOperationException("The owned mood effect popup was not presented in time.");
    }

    private void AnchorLayoutUpdated(object? sender, object args) => UpdateEffectPosition();

    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (!sender.IsHostVisible) CancelPlayback();
        else UpdateEffectPosition();
    }

    private void MotionPreferenceChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
        => DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed || !_loaded) return;
            if (!_settings.AnimationsEnabled) CancelPlayback();
            else _ = WarmEffectAsync();
        });

    private void PopupClosed(object? sender, object args)
    {
        if (!ReferenceEquals(sender, _popup)) return;
        _popupInput?.Dispose();
        _popupInput = null;
        var completion = _popupClosed;
        _popupClosed = null;
        completion?.TrySetResult();
    }

    private void ClosePopup()
    {
        _loop.LayoutUpdated -= AnchorLayoutUpdated;
        _effect?.Stop();
        if (_popup?.IsOpen != true) return;
        // A rapid click after completion waits for the native popup to finish
        // closing before reattaching its retained document to a new scene.
        _popupClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _popup.IsOpen = false;
        TaskbarPaths.Log("Mood effect closed; native animation scene released on unload.");
    }

    private void CancelPlayback()
    {
        var request = _playRequest;
        _playRequest = null;
        request?.Cancel();
        ClosePopup();
    }

    private void InvalidateEffect()
    {
        CancelPlayback();
        _preparationRequest?.Cancel();
        _preparationRequest = null;
        _loop.LayoutUpdated -= AnchorLayoutUpdated;
        if (_popup is not null)
        {
            _popup.Closed -= PopupClosed;
            _popup.IsOpen = false;
            _popup.Child = null;
            _popup = null;
        }
        _popupInput?.Dispose();
        _popupInput = null;
        _popupClosed?.TrySetResult();
        _popupClosed = null;
        _effect?.Dispose();
        _effect = null;
        _preparation = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ++_identity;
        _lifetime.Cancel();
        InvalidateEffect();
        DetachObservers();
        _loaded = false;
        _button.Click -= ButtonClicked;
        Loaded -= ButtonLoaded;
        Unloaded -= ButtonUnloaded;
        _loop.Ready -= LoopReady;
        _loop.Failed -= LoopFailed;
        _loop.Dispose();
        _button.Content = null;
        Children.Clear();
        _lifetime.Dispose();
    }
}
