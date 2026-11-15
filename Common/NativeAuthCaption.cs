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

// Windows owns caption visuals and hit targets. Move the real cursor only while
// the device is explicitly navigating this window's native title bar.
internal sealed partial class NativeAuthCaption(nint window, Func<bool> isWindowed,
    Func<AuthWindowAction, (bool Enabled, int X, int Y, int Width, int Height)> getClientRegion) : IAuthCaptionNavigation
{
    private const uint GetTitleBarInfoEx = 0x033F;
    private const uint SystemCommand = 0x0112;
    private const nuint MinimizeCommand = 0xF020, MaximizeCommand = 0xF030,
        CloseCommand = 0xF060, RestoreCommand = 0xF120;
    private const uint UnavailableButton = 0x0001 | 0x8000 | 0x10000;
    private Point _pointerBeforeSelection;
    private Point _selectedPoint;

    public AuthWindowAction? Selected { get; private set; }
    public bool IsMaximized => IsZoomed(window);
    private bool IsForeground => window != 0 && GetForegroundWindow() == window;
    public bool IsCurrent => Selected.HasValue && IsForeground && isWindowed() &&
        IsPointerAtSelection();

    public bool Select(int direction)
    {
        if (!IsForeground || !isWindowed() || !GetCursorPos(out var pointer)) return false;
        var bestX = long.MaxValue;
        AuthWindowAction? chosen = null;
        Point center = default;
        for (var index = 0; index < 3; index++)
        {
            var action = (AuthWindowAction)index;
            var rect = GetButtonRect(action);
            if (!rect.TryGetCenter(out var point)) continue;
            long distance = Selected is null ? point.X : ((long)point.X - _selectedPoint.X) * direction;
            if ((Selected is not null && distance <= 0) || distance >= bestX) continue;
            if (!IsPointOnWindow(point)) continue;
            bestX = distance;
            center = point;
            chosen = action;
        }
        if (chosen is null || !IsForeground || !SetCursorPos(center.X, center.Y)) return false;
        if (Selected is null) _pointerBeforeSelection = pointer;
        _selectedPoint = center;
        Selected = chosen;
        return true;
    }

    public void Invoke()
    {
        if (Selected is not { } action || !IsCurrent || !IsPointOnWindow(_selectedPoint))
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
        var available = GetButtonRect(action).Contains(_selectedPoint);
        Clear();
        if (available && IsForeground && isWindowed())
            PostMessage(window, SystemCommand, command, 0);
    }

    private unsafe Rect GetButtonRect(AuthWindowAction action)
    {
        // WinUI extended chrome publishes its own native non-client regions.
        var region = getClientRegion(action);
        if (!region.Enabled) return default;
        if (region.Width > 0 && region.Height > 0)
        {
            var origin = new Point(region.X, region.Y);
            if (ClientToScreen(window, ref origin))
                return new(origin.X, origin.Y, origin.X + region.Width, origin.Y + region.Height);
        }

        // The default Win32 caption can also provide absolute coordinates directly.
        TitleBarInfo info = default;
        info.Size = (uint)sizeof(TitleBarInfo);
        SendMessage(window, GetTitleBarInfoEx, 0, (nint)(&info));
        var index = action switch { AuthWindowAction.Minimize => 2, AuthWindowAction.MaximizeOrRestore => 3, _ => 5 };
        return (info.States[index] & UnavailableButton) == 0 ? info.Rectangles[index] : default;
    }

    public void Clear()
    {
        if (Selected is null) return;
        Selected = null;
        // Leave actual mouse movement alone and never relocate the cursor after
        // the user has switched to another window.
        if (IsForeground && IsPointerAtSelection())
            SetCursorPos(_pointerBeforeSelection.X, _pointerBeforeSelection.Y);
    }

    private bool IsPointOnWindow(Point point) => GetAncestor(WindowFromPoint(point), 2) == window;
    private bool IsPointerAtSelection() => GetCursorPos(out var point) &&
        point.X == _selectedPoint.X && point.Y == _selectedPoint.Y;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point(int x, int y)
    {
        public int X = x, Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Rect(int left, int top, int right, int bottom)
    {
        public readonly int Left = left, Top = top, Right = right, Bottom = bottom;

        public bool TryGetCenter(out Point point)
        {
            point = new(Left + (Right - Left) / 2, Top + (Bottom - Top) / 2);
            return Right > Left && Bottom > Top;
        }

        public bool Contains(Point point) => point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;
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
    private static partial nint WindowFromPoint(Point point);
    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint hwnd, uint flags);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point point);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint window, ref Point point);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsZoomed(nint hwnd);
}
