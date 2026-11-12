using System;

namespace Mizon.Windows.Services.Gamepad;

public enum AuthGamepadAction
{
    Up, Down, Left, Right,
    Accept, Back, Clear, RevealPassword,
    PreviousSection, NextSection, PreviousField, NextField,
    ToggleHelp, Submit, FocusPrimary, ResetFocus,
    ScrollUp, ScrollDown
}

public readonly record struct AuthGamepadState(
    int ConnectedControllerCount,
    bool IsActive,
    bool IsGamepadMode)
{
    public bool IsControllerConnected => ConnectedControllerCount > 0;
}

// These values deliberately match Windows.Gaming.Input.GamepadButtons. Keep the
// portable reducer independent of WinRT so all input/lifetime rules can be tested.
[Flags]
internal enum AuthGamepadButtons : uint
{
    None = 0,
    Menu = 1,
    View = 2,
    A = 4,
    B = 8,
    X = 16,
    Y = 32,
    Up = 64,
    Down = 128,
    Left = 256,
    Right = 512,
    LeftShoulder = 1024,
    RightShoulder = 2048,
    LeftThumbstick = 4096,
    RightThumbstick = 8192
}

internal readonly record struct AuthGamepadReading(
    AuthGamepadButtons Buttons = AuthGamepadButtons.None,
    double LeftX = 0,
    double LeftY = 0,
    double RightY = 0,
    double LeftTrigger = 0,
    double RightTrigger = 0);
