using Microsoft.UI.Xaml;
using Mizon.Windows.Services.Gamepad;
using Windows.Gaming.Input;
using Windows.System;

var assertions = 0;
void Check(bool condition, string message)
{
    assertions++;
    if (!condition) throw new InvalidOperationException(message);
}
void None(AuthGamepadInputReducer reducer, AuthGamepadReading reading, long now, string context) =>
    Check(!reducer.TryRead(reading, now, out _), context);
void Expect(AuthGamepadInputReducer reducer, AuthGamepadReading reading, long now, AuthGamepadAction expected, string context)
{
    var emitted = reducer.TryRead(reading, now, out var actual);
    Check(emitted && actual == expected, $"{context}: expected {expected}, got {(emitted ? actual.ToString() : "none")}");
}
AuthGamepadInputReducer Fresh()
{
    var reducer = new AuthGamepadInputReducer();
    None(reducer, default, 0, "Initial sample is baseline");
    return reducer;
}

// Actual button mappings, held-button edge suppression and re-press behavior.
var mappings = new (AuthGamepadButtons Button, AuthGamepadAction Action)[]
{
    (AuthGamepadButtons.A, AuthGamepadAction.Accept),
    (AuthGamepadButtons.B, AuthGamepadAction.Back),
    (AuthGamepadButtons.X, AuthGamepadAction.Clear),
    (AuthGamepadButtons.Y, AuthGamepadAction.RevealPassword),
    (AuthGamepadButtons.LeftShoulder, AuthGamepadAction.PreviousSection),
    (AuthGamepadButtons.RightShoulder, AuthGamepadAction.NextSection),
    (AuthGamepadButtons.View, AuthGamepadAction.ToggleHelp),
    (AuthGamepadButtons.Menu, AuthGamepadAction.Submit),
    (AuthGamepadButtons.LeftThumbstick, AuthGamepadAction.FocusPrimary),
    (AuthGamepadButtons.RightThumbstick, AuthGamepadAction.ResetFocus)
};
foreach (var (button, action) in mappings)
{
    var reducer = Fresh();
    var reading = new AuthGamepadReading(button);
    Expect(reducer, reading, 1, action, $"{button} mapping");
    None(reducer, reading, 10000, $"Holding {button} never repeats");
    None(reducer, default, 10001, $"Releasing {button} has no action");
    Expect(reducer, reading, 10002, action, $"Repress {button}");
    reducer.Reset();
    None(reducer, reading, 11000, $"{button} held on resume is baselined");
    None(reducer, reading, 21000, $"Held {button} stays suppressed");
    None(reducer, default, 22000, "Return to neutral after resume");
    Expect(reducer, reading, 23000, action, "New press after resume");
}

foreach (var (button, action) in new[]
{
    (AuthGamepadButtons.Up, AuthGamepadAction.Up), (AuthGamepadButtons.Down, AuthGamepadAction.Down),
    (AuthGamepadButtons.Left, AuthGamepadAction.Left), (AuthGamepadButtons.Right, AuthGamepadAction.Right)
})
{
    var reducer = Fresh();
    var reading = new AuthGamepadReading(button);
    Expect(reducer, reading, 10, action, "D-pad immediate move");
    None(reducer, reading, 369, "Initial repeat delay");
    Expect(reducer, reading, 370, action, "First repeat");
    None(reducer, reading, 479, "Repeat interval enforced");
    Expect(reducer, reading, 480, action, "Subsequent repeat");
    Expect(reducer, reading, 10000, action, "One repeat after dispatcher stall");
    None(reducer, reading, 10001, "No catch-up burst");
    reducer.Reset();
    None(reducer, reading, 11000, "Held D-pad baseline");
    None(reducer, reading, 21000, "Held D-pad cannot repeat on activation");
    None(reducer, default, 22000, "D-pad releases");
    Expect(reducer, reading, 22001, action, "D-pad new press works");
}

{
    var reducer = Fresh();
    None(reducer, new(LeftX: .54), 1, "Drift inside deadzone");
    Expect(reducer, new(LeftX: .56), 2, AuthGamepadAction.Right, "Stick enters deadzone threshold");
    None(reducer, new(LeftX: .40), 200, "Hysteresis stays held without restarting");
    Expect(reducer, new(LeftX: .40), 362, AuthGamepadAction.Right, "Held stick repeats below enter threshold");
    None(reducer, new(LeftX: .34), 363, "Stick release");
    None(reducer, new(LeftX: .50), 364, "Released stick cannot resume below enter threshold");
    Expect(reducer, new(LeftX: -.8), 365, AuthGamepadAction.Left, "Opposite axis sign switches immediately");
    Expect(reducer, new(LeftY: -.9), 366, AuthGamepadAction.Down, "Vertical negative is down");
    Expect(reducer, new(LeftY: .9), 367, AuthGamepadAction.Up, "Vertical positive is up");
    None(reducer, new(LeftX: .84, LeftY: .80), 368, "Near diagonal keeps vertical axis");
    Expect(reducer, new(LeftX: 1, LeftY: .7), 369, AuthGamepadAction.Right, "Dominant axis crosses hysteresis margin");
    None(reducer, new(LeftX: double.NaN), 370, "Nonfinite input does not navigate");
    None(reducer, new(LeftY: double.PositiveInfinity), 371, "Infinity does not navigate");
}

{
    var reducer = Fresh();
    Expect(reducer, new(AuthGamepadButtons.Up | AuthGamepadButtons.Right), 1, AuthGamepadAction.Up, "D-pad diagonal chooses one axis");
    None(reducer, new(AuthGamepadButtons.Up | AuthGamepadButtons.Right), 2, "Diagonal cannot duplicate movement");
    Expect(reducer, new(AuthGamepadButtons.Right), 3, AuthGamepadAction.Right, "D-pad change is immediate");
    None(reducer, new(AuthGamepadButtons.Left | AuthGamepadButtons.Right), 4, "Opposing directions cancel");
    reducer.Reset();
    None(reducer, new(AuthGamepadButtons.Left | AuthGamepadButtons.Right), 5, "Conflicting held baseline");
    None(reducer, new(AuthGamepadButtons.Right), 1000, "Conflict release is not physical neutral");
    None(reducer, default, 1001, "Physical neutral");
    Expect(reducer, new(AuthGamepadButtons.Right), 1002, AuthGamepadAction.Right, "New direction after full release");
}

foreach (var left in new[] { true, false })
{
    AuthGamepadReading Trigger(double value) => left ? new(LeftTrigger: value) : new(RightTrigger: value);
    var expected = left ? AuthGamepadAction.PreviousField : AuthGamepadAction.NextField;
    var reducer = Fresh();
    None(reducer, Trigger(.64), 1, "Partial trigger cannot navigate");
    Expect(reducer, Trigger(.66), 2, expected, "Trigger press threshold");
    None(reducer, Trigger(.60), 3, "Trigger hysteresis prevents repeated field changes");
    None(reducer, Trigger(.90), 1000, "Holding trigger never repeats");
    None(reducer, Trigger(.34), 1001, "Trigger release threshold");
    Expect(reducer, Trigger(.66), 1002, expected, "Trigger repress");
    reducer.Reset();
    None(reducer, Trigger(.4), 2000, "Partially held activation baseline");
    None(reducer, Trigger(1), 2001, "Increasing already held trigger cannot act");
    None(reducer, Trigger(0), 2002, "Trigger returns neutral");
    Expect(reducer, Trigger(1), 2003, expected, "Trigger after full release");
}

{
    var reducer = Fresh();
    Expect(reducer, new(RightY: .8), 1, AuthGamepadAction.ScrollUp, "Right stick up scroll");
    None(reducer, new(RightY: .5), 100, "Scroll hysteresis");
    Expect(reducer, new(RightY: .5), 361, AuthGamepadAction.ScrollUp, "Scroll repeat");
    Expect(reducer, new(RightY: -.8), 362, AuthGamepadAction.ScrollDown, "Right stick down scroll");
    reducer.Reset();
    None(reducer, new(RightY: .8), 500, "Held scroll activation baseline");
    None(reducer, new(RightY: .8), 1000, "Held scroll cannot repeat on resume");
    None(reducer, default, 1001, "Scroll neutral");
    Expect(reducer, new(RightY: -.8), 1002, AuthGamepadAction.ScrollDown, "Scroll rearmed after neutral");
    None(reducer, default, 1003, "Neutral before chord");
    Expect(reducer, new(AuthGamepadButtons.A | AuthGamepadButtons.Menu), 1004, AuthGamepadAction.Submit, "Submit chord yields one action");
    None(reducer, new(AuthGamepadButtons.A), 1005, "Consumed chord cannot queue second accept");
    None(reducer, default, 1006, "Chord release");
    Expect(reducer, new(AuthGamepadButtons.B | AuthGamepadButtons.Menu), 1007, AuthGamepadAction.Back, "Back takes precedence over submit");
}

// Device discovery, page/foreground lifetime and actual production timer session.
Gamepad.Reset();
var root = new FrameworkElement();
var delivered = new List<AuthGamepadAction>();
using (var session = new AuthGamepadSession(root, delivered.Add))
{
    var timer = root.DispatcherQueue.Timers.Single();
    Check(!timer.IsRunning, "No poll while unloaded/disconnected");
    Check(Gamepad.EventSubscriberCount == 2, "Event-driven connection/removal subscription");
    session.SetActive(true);
    Check(!timer.IsRunning && !session.State.IsActive, "Activation still requires loaded root");
    root.Load();
    Check(!timer.IsRunning && session.State.IsActive, "No timer without controller");
    var controller = new Gamepad { Reading = new() { Buttons = GamepadButtons.A } };
    Gamepad.Attach(controller);
    Check(!session.State.IsControllerConnected, "Off-thread device event marshaled before state change");
    root.DispatcherQueue.Drain();
    Check(timer.IsRunning && session.State.ConnectedControllerCount == 1, "Connection starts active polling");
    timer.Fire();
    timer.Fire();
    Check(delivered.Count == 0, "Connect with held accept never submits");
    controller.Reading = default;
    timer.Fire();
    controller.Reading = new() { Buttons = GamepadButtons.A };
    timer.Fire();
    Check(delivered.SequenceEqual([AuthGamepadAction.Accept]), "New accept reaches callback exactly once");
    Check(session.State.IsGamepadMode, "Actual input enables controller hints");
    Check(root.Key(true, VirtualKey.Enter, VirtualKey.GamepadA).Handled, "Translated native Enter is suppressed by OriginalKey");
    Check(root.Key(false, VirtualKey.GamepadA).Handled, "Native gamepad keyup suppressed");
    Check(!root.Key(true, VirtualKey.LeftWindows).Handled, "Windows/Guide remains OS owned");
    Check(!session.State.IsGamepadMode, "Keyboard restores ordinary focus mode");
    controller.Reading = default;
    timer.Fire();
    controller.Reading = new() { Buttons = GamepadButtons.A };
    timer.Fire();
    Check(session.State.IsGamepadMode, "Controller mode restores on new input");
    root.Pointer();
    Check(!session.State.IsGamepadMode, "Pointer restores ordinary focus mode");
    session.SetActive(false);
    var readCount = controller.ReadCount;
    timer.Fire();
    Check(!timer.IsRunning && !session.State.IsActive && !session.State.IsGamepadMode, "Inactive foreground stops polling/mode");
    Check(controller.ReadCount == readCount, "No background device reads");
    Check(!root.Key(true, VirtualKey.GamepadA).Handled, "Inactive session does not intercept input");
    session.SetActive(true);
    var before = delivered.Count;
    timer.Fire();
    timer.Fire();
    Check(delivered.Count == before, "Reactivation with held accept suppressed");
    var second = new Gamepad();
    Gamepad.Attach(second);
    root.DispatcherQueue.Drain();
    Check(session.State.ConnectedControllerCount == 2, "All controllers counted");
    second.Reading = new() { Buttons = GamepadButtons.Menu };
    timer.Fire();
    Check(delivered.Count == before && second.ReadCount == 0, "Second controller cannot double-submit");
    Gamepad.Detach(controller);
    root.DispatcherQueue.Drain();
    timer.Fire();
    Check(delivered.Count == before && session.State.ConnectedControllerCount == 1, "Controller switch baselines held submit");
    second.Reading = default;
    timer.Fire();
    session.FeedbackEnabled = true;
    second.Reading = new() { Buttons = GamepadButtons.A };
    timer.Fire();
    Check(second.Vibration.RightMotor > 0, "Optional accept feedback starts small pulse");
    root.Unload();
    Check(!timer.IsRunning && second.Vibration.RightMotor == 0, "Unloading immediately stops timer and haptics");
    root.Load();
    timer.Fire();
    Check(!session.State.IsGamepadMode, "Reload baselines held input");
    Gamepad.Detach(second);
    root.DispatcherQueue.Drain();
    Check(!timer.IsRunning && !session.State.IsControllerConnected, "Disconnect stops timer");
    Gamepad.Attach(controller);
    session.Dispose();
    root.DispatcherQueue.Drain();
    Check(!timer.IsRunning && timer.SubscriberCount == 0, "Disposed timer detaches tick callback");
    Check(root.HandlerCount == 0 && root.LifetimeSubscriberCount == 0, "Dispose removes rooted UI handlers");
    Check(Gamepad.EventSubscriberCount == 0 && !session.State.IsControllerConnected, "Dispose removes global device handlers");
    session.SetActive(true);
    timer.Fire();
    Check(!timer.IsRunning && !session.State.IsActive, "Disposed session cannot reactivate");
}

Gamepad.Reset();
Gamepad.FailEnumeration = true;
var failingRoot = new FrameworkElement();
failingRoot.Load();
using (var session = new AuthGamepadSession(failingRoot, delivered.Add))
{
    session.SetActive(true);
    Check(!session.State.IsControllerConnected, "Enumeration error degrades to unavailable");
    Gamepad.FailEnumeration = false;
    var failed = new Gamepad { FailReading = true };
    Gamepad.Attach(failed);
    failingRoot.DispatcherQueue.Drain();
    var timer = failingRoot.DispatcherQueue.Timers.Single();
    timer.Fire();
    Check(!timer.IsRunning && !session.State.IsControllerConnected, "Read failure stops without retry loop");
    failed.FailReading = false;
    session.SetActive(false);
    session.SetActive(true);
    Check(timer.IsRunning, "Foreground activation retries unavailable controller");
}

Gamepad.Reset();
var disposingRoot = new FrameworkElement();
disposingRoot.Load();
var disposingController = new Gamepad();
Gamepad.Attach(disposingController);
AuthGamepadSession? disposingSession = null;
disposingSession = new AuthGamepadSession(disposingRoot, _ => disposingSession!.Dispose());
disposingSession.FeedbackEnabled = true;
disposingSession.SetActive(true);
var disposingTimer = disposingRoot.DispatcherQueue.Timers.Single();
disposingTimer.Fire();
disposingController.Reading = new() { Buttons = GamepadButtons.A };
disposingTimer.Fire();
Check(!disposingTimer.IsRunning && disposingController.Vibration.RightMotor == 0, "Navigation/disposal in callback cannot restart feedback");
Check(Gamepad.EventSubscriberCount == 0, "Reentrant disposal removes static subscriptions");
Gamepad.Reset();

var allocationReducer = Fresh();
for (var i = 0; i < 10000; i++) allocationReducer.TryRead(default, i, out _);
var allocated = GC.GetAllocatedBytesForCurrentThread();
for (var i = 0; i < 100000; i++) allocationReducer.TryRead(default, i, out _);
allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
Check(allocated == 0, $"Idle reducer must allocate zero managed bytes; got {allocated}");
Console.WriteLine($"PASS: {assertions} gamepad input/session assertions. No Windows UI, real controller or account operation used.");
