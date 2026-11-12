using System;

namespace Mizon.Windows.Services.Gamepad;

/// <summary>
/// Deterministic, allocation-free input mapping. One sample produces at most one
/// action; simultaneous press edges are consumed together, never queued as a
/// second submit. The caller supplies monotonic milliseconds.
/// </summary>
internal sealed class AuthGamepadInputReducer
{
    internal const int InitialRepeatMilliseconds = 360;
    internal const int RepeatMilliseconds = 110;
    private const double StickPress = 0.55;
    private const double StickRelease = 0.35;
    private const double AxisSwitchMargin = 0.18;
    private const double TriggerPress = 0.65;
    private const double TriggerRelease = 0.35;
    private const AuthGamepadButtons DirectionButtons = AuthGamepadButtons.Up |
        AuthGamepadButtons.Down | AuthGamepadButtons.Left | AuthGamepadButtons.Right;

    private bool _primed;
    private AuthGamepadButtons _previousButtons;
    private bool _leftTrigger;
    private bool _rightTrigger;
    private Direction _stickDirection;
    private Direction _navigationDirection;
    private Direction _scrollDirection;
    private bool _navigationBlocked;
    private bool _scrollBlocked;
    private long _navigationRepeatAt;
    private long _scrollRepeatAt;

    public void Reset()
    {
        _primed = false;
        _previousButtons = AuthGamepadButtons.None;
        _leftTrigger = _rightTrigger = false;
        _stickDirection = _navigationDirection = _scrollDirection = Direction.None;
        _navigationBlocked = _scrollBlocked = false;
        _navigationRepeatAt = _scrollRepeatAt = 0;
    }

    public bool TryRead(in AuthGamepadReading reading, long milliseconds, out AuthGamepadAction action)
    {
        action = default;
        _stickDirection = ResolveStick(reading.LeftX, reading.LeftY, _stickDirection);
        var direction = ResolveDirection(reading.Buttons, _stickDirection, _navigationDirection);
        var scroll = ResolveScroll(reading.RightY, _scrollDirection);
        var leftTrigger = ResolveTrigger(reading.LeftTrigger, _leftTrigger);
        var rightTrigger = ResolveTrigger(reading.RightTrigger, _rightTrigger);

        if (!_primed)
        {
            _primed = true;
            _previousButtons = reading.Buttons;
            // Treat partially held controls as held until they return to neutral.
            _leftTrigger = reading.LeftTrigger > TriggerRelease;
            _rightTrigger = reading.RightTrigger > TriggerRelease;
            _navigationBlocked = (reading.Buttons & DirectionButtons) != 0 ||
                Math.Abs(reading.LeftX) > StickRelease || Math.Abs(reading.LeftY) > StickRelease;
            _scrollBlocked = Math.Abs(reading.RightY) > StickRelease;
            _navigationDirection = direction;
            _scrollDirection = scroll;
            return false;
        }

        var pressed = reading.Buttons & ~_previousButtons;
        var leftTriggerPressed = leftTrigger && !_leftTrigger;
        var rightTriggerPressed = rightTrigger && !_rightTrigger;
        _previousButtons = reading.Buttons;
        _leftTrigger = leftTrigger;
        _rightTrigger = rightTrigger;

        // Unblock only at physical neutral, not a diagonal/conflicting-button
        // cancellation. Reactivating with a held A/Menu/stick cannot submit/move.
        if ((reading.Buttons & DirectionButtons) == 0 &&
            Math.Abs(reading.LeftX) <= StickRelease && Math.Abs(reading.LeftY) <= StickRelease)
            _navigationBlocked = false;
        if (Math.Abs(reading.RightY) <= StickRelease) _scrollBlocked = false;

        var navigate = IsDue(direction, ref _navigationDirection, ref _navigationRepeatAt,
            milliseconds, _navigationBlocked);
        var scrollDue = IsDue(scroll, ref _scrollDirection, ref _scrollRepeatAt,
            milliseconds, _scrollBlocked);

        if (Has(pressed, AuthGamepadButtons.B)) action = AuthGamepadAction.Back;
        else if (Has(pressed, AuthGamepadButtons.View)) action = AuthGamepadAction.ToggleHelp;
        else if (Has(pressed, AuthGamepadButtons.Menu)) action = AuthGamepadAction.Submit;
        else if (Has(pressed, AuthGamepadButtons.A)) action = AuthGamepadAction.Accept;
        else if (Has(pressed, AuthGamepadButtons.X)) action = AuthGamepadAction.Clear;
        else if (Has(pressed, AuthGamepadButtons.Y)) action = AuthGamepadAction.RevealPassword;
        else if (Has(pressed, AuthGamepadButtons.LeftShoulder)) action = AuthGamepadAction.PreviousSection;
        else if (Has(pressed, AuthGamepadButtons.RightShoulder)) action = AuthGamepadAction.NextSection;
        else if (leftTriggerPressed) action = AuthGamepadAction.PreviousField;
        else if (rightTriggerPressed) action = AuthGamepadAction.NextField;
        else if (Has(pressed, AuthGamepadButtons.LeftThumbstick)) action = AuthGamepadAction.FocusPrimary;
        else if (Has(pressed, AuthGamepadButtons.RightThumbstick)) action = AuthGamepadAction.ResetFocus;
        else if (navigate) action = direction switch
        {
            Direction.Up => AuthGamepadAction.Up,
            Direction.Down => AuthGamepadAction.Down,
            Direction.Left => AuthGamepadAction.Left,
            _ => AuthGamepadAction.Right
        };
        else if (scrollDue) action = scroll == Direction.Up ? AuthGamepadAction.ScrollUp : AuthGamepadAction.ScrollDown;
        else return false;
        return true;
    }

    private static bool IsDue(Direction current, ref Direction previous, ref long repeatAt,
        long milliseconds, bool blocked)
    {
        if (blocked || current == Direction.None)
        {
            previous = current;
            repeatAt = 0;
            return false;
        }
        if (current != previous || repeatAt == 0)
        {
            previous = current;
            repeatAt = milliseconds + InitialRepeatMilliseconds;
            return true;
        }
        if (milliseconds < repeatAt) return false;
        // Never catch up missed repeats after a busy dispatcher or suspend.
        repeatAt = milliseconds + RepeatMilliseconds;
        return true;
    }

    private static bool ResolveTrigger(double value, bool held) =>
        double.IsFinite(value) && value >= (held ? TriggerRelease : TriggerPress);

    private static Direction ResolveStick(double x, double y, Direction previous)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return Direction.None;
        var ax = Math.Abs(x);
        var ay = Math.Abs(y);
        var horizontal = x < 0 ? Direction.Left : Direction.Right;
        var vertical = y < 0 ? Direction.Down : Direction.Up;
        if ((previous == Direction.Left || previous == Direction.Right) &&
            previous == horizontal && ax > StickRelease && !(ay >= StickPress && ay > ax + AxisSwitchMargin))
            return previous;
        if ((previous == Direction.Up || previous == Direction.Down) &&
            previous == vertical && ay > StickRelease && !(ax >= StickPress && ax > ay + AxisSwitchMargin))
            return previous;
        if (Math.Max(ax, ay) < StickPress) return Direction.None;
        return ay >= ax ? vertical : horizontal;
    }

    private static Direction ResolveScroll(double y, Direction previous)
    {
        if (!double.IsFinite(y)) return Direction.None;
        var direction = y < 0 ? Direction.Down : Direction.Up;
        return Math.Abs(y) >= (direction == previous ? StickRelease : StickPress) ? direction : Direction.None;
    }

    private static Direction ResolveDirection(AuthGamepadButtons buttons, Direction stick, Direction previous)
    {
        if ((buttons & DirectionButtons) == 0) return stick;
        var vertical = Has(buttons, AuthGamepadButtons.Up) == Has(buttons, AuthGamepadButtons.Down)
            ? Direction.None : Has(buttons, AuthGamepadButtons.Up) ? Direction.Up : Direction.Down;
        var horizontal = Has(buttons, AuthGamepadButtons.Left) == Has(buttons, AuthGamepadButtons.Right)
            ? Direction.None : Has(buttons, AuthGamepadButtons.Left) ? Direction.Left : Direction.Right;
        if (previous != Direction.None && (previous == vertical || previous == horizontal)) return previous;
        return vertical != Direction.None ? vertical : horizontal;
    }

    private static bool Has(AuthGamepadButtons buttons, AuthGamepadButtons value) => (buttons & value) != 0;
    private enum Direction { None, Up, Down, Left, Right }
}
