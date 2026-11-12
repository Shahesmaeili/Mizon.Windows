// The actual production session is compiled against deterministic platform
// doubles. No WinUI window, controller, input hook or real account is touched.
namespace Microsoft.UI.Dispatching
{
    public delegate void DispatcherQueueHandler();
    public sealed class DispatcherQueue
    {
        private readonly Queue<DispatcherQueueHandler> _pending = new();
        public List<DispatcherQueueTimer> Timers { get; } = [];
        public bool AcceptWork { get; set; } = true;
        public DispatcherQueueTimer CreateTimer()
        {
            var timer = new DispatcherQueueTimer();
            Timers.Add(timer);
            return timer;
        }
        public bool TryEnqueue(DispatcherQueueHandler action)
        {
            if (!AcceptWork) return false;
            _pending.Enqueue(action);
            return true;
        }
        public void Drain() { while (_pending.TryDequeue(out var action)) action(); }
    }
    public sealed class DispatcherQueueTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsRepeating { get; set; }
        public bool IsRunning { get; private set; }
        public event Action<DispatcherQueueTimer, object>? Tick;
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Fire() { if (IsRunning) Tick?.Invoke(this, EventArgs.Empty); }
        public int SubscriberCount => Tick?.GetInvocationList().Length ?? 0;
    }
}

namespace Microsoft.UI.Xaml.Input
{
    public delegate void KeyEventHandler(object sender, KeyRoutedEventArgs args);
    public delegate void PointerEventHandler(object sender, PointerRoutedEventArgs args);
    public sealed class KeyRoutedEventArgs(Windows.System.VirtualKey key, Windows.System.VirtualKey original)
    {
        public Windows.System.VirtualKey Key => key;
        public Windows.System.VirtualKey OriginalKey => original;
        public bool Handled { get; set; }
    }
    public sealed class PointerRoutedEventArgs;
}

namespace Microsoft.UI.Xaml
{
    using Microsoft.UI.Xaml.Input;
    public sealed class RoutedEventArgs;
    public class UIElement
    {
        public static readonly object PreviewKeyDownEvent = new();
        public static readonly object PreviewKeyUpEvent = new();
        public static readonly object PointerPressedEvent = new();
        private readonly Dictionary<object, object> _handlers = [];
        public int HandlerCount => _handlers.Count;
        public void AddHandler(object key, object handler, bool handledEventsToo) => _handlers.Add(key, handler);
        public void RemoveHandler(object key, object handler) => _handlers.Remove(key);
        public KeyRoutedEventArgs Key(bool down, Windows.System.VirtualKey key, Windows.System.VirtualKey? original = null)
        {
            var args = new KeyRoutedEventArgs(key, original ?? key);
            if (_handlers.TryGetValue(down ? PreviewKeyDownEvent : PreviewKeyUpEvent, out var handler))
                ((KeyEventHandler)handler)(this, args);
            return args;
        }
        public void Pointer()
        {
            if (_handlers.TryGetValue(PointerPressedEvent, out var handler))
                ((PointerEventHandler)handler)(this, new PointerRoutedEventArgs());
        }
    }
    public sealed class FrameworkElement : UIElement
    {
        public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; } = new();
        public bool IsLoaded { get; private set; }
        public event Action<object, RoutedEventArgs>? Loaded;
        public event Action<object, RoutedEventArgs>? Unloaded;
        public int LifetimeSubscriberCount => (Loaded?.GetInvocationList().Length ?? 0) + (Unloaded?.GetInvocationList().Length ?? 0);
        public void Load() { IsLoaded = true; Loaded?.Invoke(this, new RoutedEventArgs()); }
        public void Unload() { IsLoaded = false; Unloaded?.Invoke(this, new RoutedEventArgs()); }
    }
}

namespace Windows.System
{
    public enum VirtualKey
    {
        Enter = 13, A = 65, LeftWindows = 91,
        GamepadA = 195, GamepadB = 196, GamepadRightThumbstickLeft = 218
    }
}

namespace Windows.Gaming.Input
{
    [Flags]
    public enum GamepadButtons : uint { None = 0, Menu = 1, View = 2, A = 4, B = 8, X = 16, Y = 32, DPadUp = 64, DPadDown = 128, DPadLeft = 256, DPadRight = 512, LeftShoulder = 1024, RightShoulder = 2048, LeftThumbstick = 4096, RightThumbstick = 8192 }
    public struct GamepadReading
    {
        public GamepadButtons Buttons;
        public double LeftThumbstickX, LeftThumbstickY, RightThumbstickY, LeftTrigger, RightTrigger;
    }
    public struct GamepadVibration { public double RightMotor; }
    public sealed class Gamepad
    {
        private static readonly List<Gamepad> Devices = [];
        public static event EventHandler<Gamepad>? GamepadAdded;
        public static event EventHandler<Gamepad>? GamepadRemoved;
        public static bool FailEnumeration;
        public static int EventSubscriberCount => (GamepadAdded?.GetInvocationList().Length ?? 0) + (GamepadRemoved?.GetInvocationList().Length ?? 0);
        public static IReadOnlyList<Gamepad> Gamepads => FailEnumeration ? throw new InvalidOperationException("Synthetic device error") : Devices;
        public bool FailReading;
        public GamepadReading Reading;
        public int ReadCount;
        public GamepadVibration Vibration { get; set; }
        public GamepadReading GetCurrentReading()
        {
            ReadCount++;
            return FailReading ? throw new InvalidOperationException("Synthetic read error") : Reading;
        }
        public static void Attach(Gamepad controller) { Devices.Add(controller); GamepadAdded?.Invoke(null, controller); }
        public static void Detach(Gamepad controller) { Devices.Remove(controller); GamepadRemoved?.Invoke(null, controller); }
        public static void Reset() { Devices.Clear(); FailEnumeration = false; }
    }
}
