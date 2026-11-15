using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mizon.Windows.Common;

internal enum AuthWindowAction { Minimize, MaximizeOrRestore, Close }

internal interface IAuthCaptionNavigation
{
    AuthWindowAction? Selected { get; }
    bool IsCurrent { get; }
    bool IsMaximized { get; }
    bool Select(int direction);
    void Invoke();
    void Clear();
}

internal readonly record struct AuthCaptionBounds(int X, int Y, int Width, int Height)
{
    internal bool IsValid => Width > 0 && Height > 0;
    internal int CenterX => X + Width / 2;
}

// Windows owns the caption buttons. Report their real bounds for a focus outline
// and use the window's standard system command when an action is selected.
internal sealed partial class NativeAuthCaption(nint window, Func<bool> isWindowed,
    Func<AuthWindowAction, (bool Enabled, int X, int Y, int Width, int Height)> getClientRegion,
    Action<AuthCaptionBounds?> showSelection) : IAuthCaptionNavigation
{
    private const uint GetTitleBarInfoEx = 0x033F;
    private const uint SystemCommand = 0x0112;
    private const nuint MinimizeCommand = 0xF020, MaximizeCommand = 0xF030,
        CloseCommand = 0xF060, RestoreCommand = 0xF120;
    private const uint UnavailableButton = 0x0001 | 0x8000 | 0x10000;
    private AuthCaptionBounds _selectedBounds;

    public AuthWindowAction? Selected { get; private set; }
    public bool IsMaximized => IsZoomed(window);
    private bool IsForeground => window != 0 && GetForegroundWindow() == window;
    public bool IsCurrent => Selected.HasValue && IsForeground && isWindowed();

    public bool Select(int direction)
    {
        if (!IsForeground || !isWindowed()) return false;
        var bestX = long.MaxValue;
        AuthWindowAction? chosen = null;
        AuthCaptionBounds chosenBounds = default;
        for (var index = 0; index < 3; index++)
        {
            var action = (AuthWindowAction)index;
            var bounds = GetButtonBounds(action);
            if (!bounds.IsValid) continue;
            long distance = Selected is null ? bounds.CenterX : ((long)bounds.CenterX - _selectedBounds.CenterX) * direction;
            if ((Selected is not null && distance <= 0) || distance >= bestX) continue;
            bestX = distance;
            chosenBounds = bounds;
            chosen = action;
        }
        if (chosen is null || !IsForeground) return false;
        _selectedBounds = chosenBounds;
        Selected = chosen;
        showSelection(chosenBounds);
        return true;
    }

    public void Invoke()
    {
        if (Selected is not { } action || !IsCurrent)
        {
            Clear();
            return;
        }

        var command = action switch
        {
            AuthWindowAction.Minimize => MinimizeCommand,
            AuthWindowAction.MaximizeOrRestore => IsMaximized ? RestoreCommand : MaximizeCommand,
            _ => CloseCommand
        };
        var available = GetButtonBounds(action) == _selectedBounds;
        Clear();
        if (available && IsForeground && isWindowed())
            PostMessage(window, SystemCommand, command, 0);
    }

    private unsafe AuthCaptionBounds GetButtonBounds(AuthWindowAction action)
    {
        // WinUI extended chrome publishes its own native non-client regions.
        var region = getClientRegion(action);
        if (!region.Enabled) return default;
        if (region.Width > 0 && region.Height > 0)
            return new(region.X, region.Y, region.Width, region.Height);

        // The default Win32 caption can also provide absolute coordinates directly.
        TitleBarInfo info = default;
        info.Size = (uint)sizeof(TitleBarInfo);
        SendMessage(window, GetTitleBarInfoEx, 0, (nint)(&info));
        var index = action switch { AuthWindowAction.Minimize => 2, AuthWindowAction.MaximizeOrRestore => 3, _ => 5 };
        if ((info.States[index] & UnavailableButton) != 0) return default;
        var rect = info.Rectangles[index];
        var origin = new Point(rect.Left, rect.Top);
        return rect.Right > rect.Left && rect.Bottom > rect.Top && ScreenToClient(window, ref origin)
            ? new(origin.X, origin.Y, rect.Right - rect.Left, rect.Bottom - rect.Top)
            : default;
    }

    public void Clear()
    {
        if (Selected is null) return;
        Selected = null;
        _selectedBounds = default;
        showSelection(null);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point(int x, int y)
    {
        public int X = x, Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Rect
    {
        public readonly int Left, Top, Right, Bottom;
    }

    [InlineArray(6)]
    private struct ButtonRects { private Rect _first; }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct TitleBarInfo
    {
        public uint Size;
        public Rect Bounds;
        public fixed uint States[6];
        public ButtonRects Rectangles;
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint hwnd, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ScreenToClient(nint window, ref Point point);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsZoomed(nint hwnd);
}
