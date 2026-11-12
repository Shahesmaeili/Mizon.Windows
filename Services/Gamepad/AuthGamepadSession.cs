using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Gaming.Input;
using Windows.System;
using NativeGamepad = Windows.Gaming.Input.Gamepad;

namespace Mizon.Windows.Services.Gamepad;

/// <summary>
/// Page-owned controller input. SetActive must reflect the page AND foreground,
/// non-minimized window lifetime. Construct, activate and dispose on the UI thread.
/// Guide/Xbox/Share and remapped Elite paddles remain owned by Windows/controller
/// configuration; no global input hook or reserved-button interception is used.
/// </summary>
public sealed class AuthGamepadSession : IDisposable
{
    private readonly FrameworkElement _root;
    private readonly Action<AuthGamepadAction> _onAction;
    private readonly DispatcherQueueTimer _timer;
    private readonly DispatcherQueueHandler _refreshControllersAction;
    private readonly AuthGamepadInputReducer _input = new();
    private readonly KeyEventHandler _previewKeyDown;
    private readonly KeyEventHandler _previewKeyUp;
    private readonly PointerEventHandler _pointerPressed;
    private NativeGamepad? _controller;
    private int _connectedControllerCount;
    private int _refreshQueued;
    private bool _addedSubscribed;
    private bool _removedSubscribed;
    private bool _requestedActive;
    private bool _gamepadMode;
    private bool _feedbackEnabled;
    private bool _vibrating;
    private long _feedbackStopAt;
    private long _nextFeedbackAt;
    private volatile bool _disposed;

    public AuthGamepadSession(FrameworkElement root, Action<AuthGamepadAction> onAction)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _onAction = onAction ?? throw new ArgumentNullException(nameof(onAction));
        _timer = root.DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(1000d / 60d);
        _timer.IsRepeating = true;
        _timer.Tick += OnTick;
        _refreshControllersAction = RefreshControllers;
        _previewKeyDown = OnPreviewKeyDown;
        _previewKeyUp = OnPreviewKeyUp;
        _pointerPressed = OnPointerPressed;
        root.AddHandler(UIElement.PreviewKeyDownEvent, _previewKeyDown, true);
        root.AddHandler(UIElement.PreviewKeyUpEvent, _previewKeyUp, true);
        root.AddHandler(UIElement.PointerPressedEvent, _pointerPressed, true);
        root.Loaded += OnLoaded;
        root.Unloaded += OnUnloaded;
        try
        {
            NativeGamepad.GamepadAdded += OnControllersChanged;
            _addedSubscribed = true;
            NativeGamepad.GamepadRemoved += OnControllersChanged;
            _removedSubscribed = true;
            RefreshControllers();
        }
        catch (Exception)
        {
            // Controller hardware/WinRT availability never prevents sign-in.
            DetachControllerEvents();
            PublishState();
        }
    }

    public AuthGamepadState State { get; private set; }
    public event EventHandler<AuthGamepadState>? StateChanged;

    /// <summary>Optional brief feedback; off by default and never continuous.</summary>
    public bool FeedbackEnabled
    {
        get => _feedbackEnabled;
        set
        {
            if (_disposed) return;
            _feedbackEnabled = value;
            if (!value) StopFeedback();
        }
    }

    public void SetActive(bool active)
    {
        if (_disposed) return;
        if (_requestedActive == active) return;
        _requestedActive = active;
        _input.Reset();
        _gamepadMode = false;
        if (active) RefreshControllers();
        else UpdatePolling();
    }

    public void NotifyPointerOrKeyboardInput()
    {
        if (_disposed || !_gamepadMode) return;
        _gamepadMode = false;
        _input.Reset();
        StopFeedback();
        PublishState();
    }

    private bool IsActive => !_disposed && _requestedActive && _root.IsLoaded;

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_disposed) return;
        _input.Reset();
        RefreshControllers();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) => UpdatePolling();

    private void OnControllersChanged(object? sender, NativeGamepad controller)
    {
        // WinRT controller events can arrive off-thread. Coalesce enumeration on
        // the page dispatcher; callbacks queued before disposal are harmless.
        if (_disposed || Interlocked.Exchange(ref _refreshQueued, 1) != 0) return;
        try
        {
            if (_root.DispatcherQueue.TryEnqueue(_refreshControllersAction)) return;
        }
        catch (Exception) { /* Dispatcher/window shutdown is a normal boundary. */ }
        Interlocked.Exchange(ref _refreshQueued, 0);
    }

    private void RefreshControllers()
    {
        Interlocked.Exchange(ref _refreshQueued, 0);
        if (_disposed) return;
        NativeGamepad? selected = null;
        var count = 0;
        try
        {
            var controllers = NativeGamepad.Gamepads;
            count = controllers.Count;
            for (var i = 0; i < count; i++)
            {
                var candidate = controllers[i];
                selected ??= candidate;
                if (_controller is not null && candidate.Equals(_controller))
                {
                    selected = _controller;
                    break;
                }
            }
        }
        catch (Exception)
        {
            selected = null;
            count = 0;
        }

        if (!Equals(selected, _controller))
        {
            StopFeedback();
            _controller = selected;
            _input.Reset();
            _gamepadMode = false;
        }
        _connectedControllerCount = count;
        UpdatePolling();
    }

    private void UpdatePolling()
    {
        if (IsActive && _controller is not null)
        {
            if (!_timer.IsRunning) _timer.Start();
        }
        else
        {
            _timer.Stop();
            StopFeedback();
            _input.Reset();
            _gamepadMode = false;
        }
        PublishState();
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (!IsActive || _controller is null)
        {
            UpdatePolling();
            return;
        }
        var now = Environment.TickCount64;
        if (_vibrating && now >= _feedbackStopAt) StopFeedback();
        GamepadReading reading;
        try { reading = _controller.GetCurrentReading(); }
        catch (Exception)
        {
            // Stop on a failed/disconnected device; reconnect/foreground events
            // re-enumerate. Do not create a background failure/retry loop.
            StopFeedback();
            _controller = null;
            _connectedControllerCount = 0;
            UpdatePolling();
            return;
        }
        var sample = new AuthGamepadReading((AuthGamepadButtons)reading.Buttons,
            reading.LeftThumbstickX, reading.LeftThumbstickY, reading.RightThumbstickY,
            reading.LeftTrigger, reading.RightTrigger);
        if (!_input.TryRead(sample, now, out var action)) return;
        _gamepadMode = true;
        PublishState();
        if (!IsActive || _controller is null) return;
        _onAction(action);
        // UI callbacks can navigate, deactivate or dispose this session.
        if (IsActive && _controller is not null) PulseFeedback(action, now);
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (IsGamepadKey(args.OriginalKey) || IsGamepadKey(args.Key))
        {
            if (IsActive && _controller is not null) args.Handled = true;
            return;
        }
        NotifyPointerOrKeyboardInput();
    }

    private void OnPreviewKeyUp(object sender, KeyRoutedEventArgs args)
    {
        if (IsActive && _controller is not null &&
            (IsGamepadKey(args.OriginalKey) || IsGamepadKey(args.Key))) args.Handled = true;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs args) => NotifyPointerOrKeyboardInput();

    private static bool IsGamepadKey(VirtualKey key) =>
        key >= VirtualKey.GamepadA && key <= VirtualKey.GamepadRightThumbstickLeft;

    private void PulseFeedback(AuthGamepadAction action, long now)
    {
        if (!_feedbackEnabled || _vibrating || now < _nextFeedbackAt || _controller is null) return;
        if (action is not (AuthGamepadAction.Up or AuthGamepadAction.Down or AuthGamepadAction.Left or
            AuthGamepadAction.Right or AuthGamepadAction.Accept)) return;
        try
        {
            _controller.Vibration = new GamepadVibration { RightMotor = 0.10 };
            _vibrating = true;
            _feedbackStopAt = now + 24;
            _nextFeedbackAt = now + 180;
        }
        catch (Exception) { _vibrating = false; }
    }

    private void StopFeedback()
    {
        if (!_vibrating) return;
        _vibrating = false;
        try { if (_controller is not null) _controller.Vibration = default; }
        catch (Exception) { /* A removed controller cannot acknowledge a stop. */ }
    }

    private void PublishState()
    {
        var state = new AuthGamepadState(_connectedControllerCount, IsActive, _gamepadMode && IsActive);
        if (state == State) return;
        State = state;
        StateChanged?.Invoke(this, state);
    }

    private void DetachControllerEvents()
    {
        if (_addedSubscribed)
        {
            _addedSubscribed = false;
            try { NativeGamepad.GamepadAdded -= OnControllersChanged; } catch (Exception) { }
        }
        if (_removedSubscribed)
        {
            _removedSubscribed = false;
            try { NativeGamepad.GamepadRemoved -= OnControllersChanged; } catch (Exception) { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _requestedActive = false;
        _timer.Stop();
        _timer.Tick -= OnTick;
        StopFeedback();
        _controller = null;
        _connectedControllerCount = 0;
        _gamepadMode = false;
        _input.Reset();
        DetachControllerEvents();
        _root.RemoveHandler(UIElement.PreviewKeyDownEvent, _previewKeyDown);
        _root.RemoveHandler(UIElement.PreviewKeyUpEvent, _previewKeyUp);
        _root.RemoveHandler(UIElement.PointerPressedEvent, _pointerPressed);
        _root.Loaded -= OnLoaded;
        _root.Unloaded -= OnUnloaded;
        PublishState();
        StateChanged = null;
    }
}
