using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Services;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace Mizon.Windows;

public sealed partial class MainWindow : IWindowPresentationHost
{
    private readonly UISettings _windowMotionSettings = new();
    private OverlappedPresenter? _windowedPresenter;
    private DispatcherQueueTimer? _fullScreenHideTimer;
    private CompositionScopedBatch? _fullScreenMotion;
    private Visual? _fullScreenVisual;
    private WindowPresentationTransition? _windowPresentationTransition;
    private PointerEventHandler? _fullScreenPointerMoved;
    private bool _windowPresentationClosed, _fullScreenControlsShown, _pointerInFullScreenControls;
    private bool _fullScreenPointerAttached;
    private bool _windowPresenterChanging, _windowPresentationUpdateQueued;
    private bool _windowPresentationActive;

    public bool IsAvailable => !_windowPresentationClosed;
    public bool IsFullScreen => !_windowPresentationClosed && AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
    public bool IsFullScreenControlsVisible => IsFullScreen && _fullScreenControlsShown;

    private void InitializeWindowPresentation()
    {
        _windowPresentationTransition = new(
            callback => DispatcherQueue.TryEnqueue(() => callback()),
            () => IsFullScreen, ApplyFullScreen,
            exception => AppLog.Error("Window presentation request failed; no automatic retry.", exception));
        _fullScreenPointerMoved = FullScreenRoot_PointerMoved;
        _windowedPresenter = AppWindow.Presenter as OverlappedPresenter;
        _fullScreenHideTimer = DispatcherQueue.CreateTimer();
        _fullScreenHideTimer.Interval = TimeSpan.FromMilliseconds(650);
        _fullScreenHideTimer.IsRepeating = false;
        _fullScreenHideTimer.Tick += FullScreenHideTimer_Tick;
        AppWindow.Changed += WindowPresentation_Changed;
        Activated += WindowPresentation_Activated;
        UpdateWindowPresentationLabels();
        UpdateWindowPresentation();
    }

    public void SetFullScreen(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_windowPresentationClosed, this);
        // MCP already dispatches to the UI thread and needs the actual new state
        // before returning. UI input instead uses RequestFullScreen below.
        _windowPresentationTransition!.Set(enabled);
    }

    private void RequestFullScreen(bool? enabled = null)
    {
        if (_windowPresentationClosed) return;
        // Do not replace the HWND presenter inside a routed input/thumbnail
        // WndProc callback. Rapid toggles are coalesced before the next UI turn.
        _windowPresentationTransition?.Request(enabled);
    }

    private void ApplyFullScreen(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_windowPresentationClosed, this);
        if (enabled == IsFullScreen) return;
        var previous = AppWindow.Presenter.Kind;
        AppLog.Information($"Window presentation: begin {previous} -> fullscreen={enabled}.");
        var phase = "hide controls";
        try
        {
            // Retire the current Composition batch before replacing the presenter;
            // its completion must not update XAML in a nested native message loop.
            SetFullScreenControlsVisible(false, animate: false);
            _windowPresenterChanging = true;
            phase = "replace presenter";
            // SetPresenter can synchronously raise Changed/Activated and resize
            // notifications. Reconcile XAML only after it finishes.
            if (enabled)
            {
                _windowedPresenter = AppWindow.Presenter as OverlappedPresenter ?? _windowedPresenter;
                AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            }
            else if (_windowedPresenter is { } presenter)
                AppWindow.SetPresenter(presenter);
            else
                AppWindow.SetPresenter(AppWindowPresenterKind.Default);
            AppLog.Information($"Window presentation: applied {AppWindow.Presenter.Kind}.");
        }
        catch (Exception exception)
        {
            AppLog.Error($"Window presentation: failed during {phase}, from {previous}, target fullscreen={enabled}, HRESULT=0x{exception.HResult:X8}.", exception);
            throw;
        }
        finally
        {
            _windowPresenterChanging = false;
            QueueWindowPresentationUpdate();
        }
    }

    private void WindowPresentation_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange) QueueWindowPresentationUpdate();
    }

    private void QueueWindowPresentationUpdate()
    {
        if (_windowPresentationClosed || _windowPresenterChanging || _windowPresentationUpdateQueued) return;
        _windowPresentationUpdateQueued = DispatcherQueue.TryEnqueue(() =>
        {
            _windowPresentationUpdateQueued = false;
            if (_windowPresentationClosed || _windowPresenterChanging) return;
            try
            {
                UpdateWindowPresentation();
                // Deactivation may have occurred inside SetPresenter.
                if (!_fullScreenControlsShown || !IsFullScreen)
                    SetFullScreenControlsVisible(false, animate: false);
                AppLog.Information($"Window presentation: chrome settled; presenter={AppWindow.Presenter.Kind}, controls={_fullScreenControlsShown}.");
            }
            catch (Exception exception) when (exception is COMException or ArgumentException or InvalidOperationException)
            {
                AppLog.Error($"Window presentation: chrome refresh failed, HRESULT=0x{exception.HResult:X8}.", exception);
            }
        });
    }

    private void UpdateWindowPresentation()
    {
        if (_windowPresentationClosed) return;
        var fullScreen = IsFullScreen;
        FullScreenEdge.Visibility = fullScreen ? Visibility.Visible : Visibility.Collapsed;
        if (fullScreen && !_fullScreenPointerAttached)
        {
            RootGrid.AddHandler(UIElement.PointerMovedEvent, _fullScreenPointerMoved!, true);
            _fullScreenPointerAttached = true;
        }
        else if (!fullScreen)
        {
            DetachFullScreenPointer();
            SetFullScreenControlsVisible(false, animate: false);
            _pointerInFullScreenControls = false;
        }
    }

    private void WindowPresentation_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_windowPresentationClosed) return;
        _windowPresentationActive = args.WindowActivationState != WindowActivationState.Deactivated;
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            _fullScreenControlsShown = false;
            _pointerInFullScreenControls = false;
            QueueWindowPresentationUpdate();
        }
    }

    private void FullScreenShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        RequestFullScreen();
    }

    private void FullScreenEdge_PointerEntered(object sender, PointerRoutedEventArgs args) => SetFullScreenControlsVisible(true);

    private void FullScreenRoot_PointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_windowPresentationClosed || _windowPresenterChanging || !IsFullScreen) return;
        var position = args.GetCurrentPoint(RootGrid).Position;
        if (position.Y <= FullScreenEdge.Height) SetFullScreenControlsVisible(true);
        else if (position.Y > FullScreenControls.ActualHeight + FullScreenControls.Margin.Top + 8)
            ScheduleFullScreenControlsHide();
    }

    private void FullScreenControls_PointerEntered(object sender, PointerRoutedEventArgs args)
    {
        _pointerInFullScreenControls = true;
        SetFullScreenControlsVisible(true);
    }

    private void FullScreenControls_PointerExited(object sender, PointerRoutedEventArgs args)
    {
        _pointerInFullScreenControls = false;
        ScheduleFullScreenControlsHide();
    }

    private void FullScreenControls_LostFocus(object sender, RoutedEventArgs args) => ScheduleFullScreenControlsHide();

    private void ScheduleFullScreenControlsHide()
    {
        if (_windowPresentationClosed || _windowPresenterChanging) return;
        if (_fullScreenControlsShown && !_pointerInFullScreenControls && _fullScreenHideTimer?.IsRunning == false)
            _fullScreenHideTimer.Start();
    }

    private void FullScreenHideTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_windowPresentationClosed || _windowPresenterChanging || _pointerInFullScreenControls) return;
        var focused = RootGrid.XamlRoot is { } root ? FocusManager.GetFocusedElement(root) as DependencyObject : null;
        for (var current = focused; current is not null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, FullScreenControls)) return;
        SetFullScreenControlsVisible(false);
    }

    private void SetFullScreenControlsVisible(bool visible, bool animate = true)
    {
        if (_windowPresentationClosed || _windowPresenterChanging) return;
        if (visible && (!_windowPresentationActive || !IsFullScreen)) return;
        _fullScreenHideTimer?.Stop();
        if (_fullScreenControlsShown == visible && (animate || FullScreenControls.Visibility == Visibility.Collapsed)) return;
        _fullScreenControlsShown = visible;
        if (!visible) _pointerInFullScreenControls = false;
        StopFullScreenMotion();
        var wasCollapsed = FullScreenControls.Visibility == Visibility.Collapsed;
        FullScreenControls.Visibility = Visibility.Visible;
        FullScreenControls.IsHitTestVisible = visible;
        ElementCompositionPreview.SetIsTranslationEnabled(FullScreenControls, true);
        var visual = _fullScreenVisual ??= ElementCompositionPreview.GetElementVisual(FullScreenControls);
        if (!animate || !_windowMotionSettings.AnimationsEnabled)
        {
            visual.Opacity = visible ? 1 : 0;
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
            if (!visible) FullScreenControls.Visibility = Visibility.Collapsed;
            return;
        }
        if (wasCollapsed)
        {
            visual.Opacity = 0;
            visual.Properties.InsertVector3("Translation", new Vector3(0, -8, 0));
        }
        var compositor = visual.Compositor;
        _fullScreenMotion = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _fullScreenMotion.Completed += FullScreenMotion_Completed;
        using var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(.2f, .8f), Vector2.One);
        using var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertExpressionKeyFrame(0, "this.StartingValue");
        opacity.InsertKeyFrame(1, visible ? 1 : 0, easing);
        opacity.Duration = TimeSpan.FromMilliseconds(160);
        visual.StartAnimation("Opacity", opacity);
        using var translation = compositor.CreateVector3KeyFrameAnimation();
        translation.InsertExpressionKeyFrame(0, "this.StartingValue");
        translation.InsertKeyFrame(1, new Vector3(0, visible ? 0 : -8, 0), easing);
        translation.Duration = opacity.Duration;
        visual.Properties.StartAnimation("Translation", translation);
        _fullScreenMotion.End();
    }

    private void FullScreenMotion_Completed(object sender, CompositionBatchCompletedEventArgs args)
    {
        if (_windowPresentationClosed || !ReferenceEquals(sender, _fullScreenMotion)) return;
        StopFullScreenMotion();
        if (_fullScreenVisual is { } visual)
        {
            visual.Opacity = _fullScreenControlsShown ? 1 : 0;
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
        }
        if (!_fullScreenControlsShown) FullScreenControls.Visibility = Visibility.Collapsed;
    }

    private void StopFullScreenMotion()
    {
        if (_fullScreenMotion is { } motion)
        {
            _fullScreenMotion = null;
            motion.Completed -= FullScreenMotion_Completed;
            motion.Dispose();
        }
        _fullScreenVisual?.StopAnimation("Opacity");
        _fullScreenVisual?.Properties.StopAnimation("Translation");
    }

    private void ExitFullScreen_Click(object sender, RoutedEventArgs args) => RequestFullScreen(false);
    private void MinimizeWindow_Click(object sender, RoutedEventArgs args)
    {
        SetFullScreenControlsVisible(false, animate: false);
        // FullScreenPresenter has no Minimize API. Keep the presenter while the
        // official HWND show-state API minimizes this window, so restore stays fullscreen.
        ShowWindow(WindowNative.GetWindowHandle(this), 6); // SW_MINIMIZE
    }
    private void CloseWindow_Click(object sender, RoutedEventArgs args) => Close();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

    private void UpdateWindowPresentationLabels()
    {
        var localization = LocalizationService.Current;
        var exit = localization.GetString("Window/ExitFullScreen");
        ExitFullScreenLabel.Text = exit;
        ExitFullScreenLabel.FontFamily = localization.TextFontFamily;
        AutomationProperties.SetName(FullScreenControls, localization.GetString("Window/FullScreenControls"));
        SetWindowControlLabel(ExitFullScreenButton, exit);
        AutomationProperties.SetAcceleratorKey(ExitFullScreenButton, "F11");
        SetWindowControlLabel(MinimizeWindowButton, localization.GetString("Window/Minimize"));
        SetWindowControlLabel(CloseWindowButton, localization.GetString("Window/Close"));
    }

    private static void SetWindowControlLabel(Button button, string label)
    {
        AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
    }

    private void DetachFullScreenPointer()
    {
        if (!_fullScreenPointerAttached) return;
        RootGrid.RemoveHandler(UIElement.PointerMovedEvent, _fullScreenPointerMoved!);
        _fullScreenPointerAttached = false;
    }

    private void DisposeWindowPresentation()
    {
        if (_windowPresentationClosed) return;
        _windowPresentationClosed = true;
        _windowPresentationTransition?.Dispose();
        _windowPresentationTransition = null;
        AppWindow.Changed -= WindowPresentation_Changed;
        Activated -= WindowPresentation_Activated;
        DetachFullScreenPointer();
        if (_fullScreenHideTimer is { } timer)
        {
            timer.Stop();
            timer.Tick -= FullScreenHideTimer_Tick;
            _fullScreenHideTimer = null;
        }
        StopFullScreenMotion();
        _fullScreenVisual = null;
        _windowedPresenter = null;
        _fullScreenPointerMoved = null;
    }
}
