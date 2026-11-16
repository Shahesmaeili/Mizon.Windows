using System.Numerics;
using Windows.System;

namespace Mizon.Windows.Common;

internal static class ArtistModelInput
{
    internal static (int Mask, Vector2 Step) Rotation(VirtualKey key) => key switch
    {
        VirtualKey.Left or VirtualKey.GamepadRightThumbstickLeft => (1, new(-.15f, 0)),
        VirtualKey.Right or VirtualKey.GamepadRightThumbstickRight => (2, new(.15f, 0)),
        VirtualKey.Up or VirtualKey.GamepadRightThumbstickUp => (4, new(0, .1f)),
        VirtualKey.Down or VirtualKey.GamepadRightThumbstickDown => (8, new(0, -.1f)),
        _ => default
    };
}
