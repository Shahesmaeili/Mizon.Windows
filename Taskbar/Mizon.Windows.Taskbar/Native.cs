using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mizon.Windows.Taskbar;

internal static unsafe partial class Native
{
    internal const uint ChildStyle = 0x40000000 | 0x02000000 | 0x04000000; // WS_CHILD | CLIPCHILDREN | CLIPSIBLINGS
    internal const uint PopupStyle = 0x80000000 | 0x02000000 | 0x04000000;
    internal const uint ToolWindowStyle = 0x80 | 0x08000000; // TOOLWINDOW | NOACTIVATE
    internal const uint PositionFlags = 0x0010 | 0x0040; // NOACTIVATE | SHOWWINDOW
    internal const string HostClass = "Mizon.Taskbar.Profile.Host";

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor, WorkArea;
        public uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public Point Point;
        public uint Private;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        public uint Size, Style;
        public delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint> Procedure;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public char* MenuName;
        public char* ClassName;
        public nint SmallIcon;
    }

    internal static void RegisterHostClass()
    {
        fixed (char* name = HostClass)
        {
            var definition = new WindowClass
            {
                Size = (uint)sizeof(WindowClass), ClassName = name,
                Procedure = &WindowProcedure, Instance = GetModuleHandle(null)
            };
            if (RegisterClassEx(in definition) == 0 && Marshal.GetLastPInvokeError() != 1410)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "Taskbar child class registration failed.");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        // This procedure belongs only to our own child; Explorer is never subclassed.
        try
        {
            if (message == 0x0014) return 1; // XAML paints the client; avoid an opaque GDI erase.
            if (message == 0x0021) return 3; // MA_NOACTIVATE; clicking must not activate Explorer.
            if (message == 0x0010) { PostQuitMessage(0); return 0; }
            if (message is 0x0005 or 0x02E0) TaskbarProfileHost.Current?.QueueGeometryUpdate();
            if (message == 0x0082) TaskbarProfileHost.Current?.HostDestroyed(window);
        }
        catch (Exception error) { TaskbarPaths.Log($"Window message {message:X}: {error.Message}"); }
        return DefWindowProc(window, message, wParam, lParam);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandle(string? module);
    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    private static partial ushort RegisterClassEx(in WindowClass definition);
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint CreateWindowEx(uint extendedStyle, string className, string name, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint FindWindow(string className, string? title);
    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint FindWindowEx(nint parent, nint after, string className, string? title);
    [LibraryImport("user32.dll")]
    internal static partial nint GetParent(nint window);
    [LibraryImport("user32.dll")]
    internal static partial nint GetWindow(nint window, uint command);
    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint window, out uint processId);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsChild(nint parent, nint window);
    [LibraryImport("user32.dll")]
    internal static partial nint SetWinEventHook(uint minimum, uint maximum, nint module,
        delegate* unmanaged[Stdcall]<nint, uint, nint, int, int, uint, uint, void> callback, uint process, uint thread, uint flags);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWinEvent(nint hook);

    internal static nint WatchTaskbarLayout() => SetWinEventHook(0x8002, 0x800B, 0, &TaskbarLayoutChanged, 0, 0, 2);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void TaskbarLayoutChanged(nint hook, uint eventId, nint window, int objectId, int childId, uint threadId, uint time)
    {
        try { if (window != 0) TaskbarProfileHost.Current?.NotifyShellLayoutChanged(window); }
        catch (Exception error) { TaskbarPaths.Log($"Taskbar location event: {error.Message}"); }
    }
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static partial nint GetWindowLongPtr(nint window, int index);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static partial nint SetWindowLongPtr(nint window, int index, nint value);
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint SetParent(nint window, nint parent);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(nint window);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint window);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetClientRect(nint window, out Rect rectangle);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint window, out Rect rectangle);
    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint window, uint flags);
    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo information);

    internal static bool TryGetMonitorBounds(nint window, out Rect bounds)
    {
        var monitor = MonitorFromWindow(window, 2); // MONITOR_DEFAULTTONEAREST also handles an autohidden taskbar.
        var information = new MonitorInfo { Size = (uint)sizeof(MonitorInfo) };
        if (monitor != 0 && GetMonitorInfo(monitor, ref information))
        {
            bounds = information.Monitor;
            return true;
        }
        bounds = default;
        return false;
    }
    [LibraryImport("user32.dll")]
    internal static partial int MapWindowPoints(nint from, nint to, ref Rect rectangle, uint count);
    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(nint window);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint window, int command);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint window);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static partial nint CreateEllipticRgn(int left, int top, int right, int bottom);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static partial int CombineRgn(nint destination, nint first, nint second, int mode);
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(nint handle);
    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    internal static partial int GetMessage(out Message message, nint window, uint minimum, uint maximum);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(in Message message);
    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static partial nint DispatchMessage(in Message message);
    [LibraryImport("user32.dll")]
    internal static partial void PostQuitMessage(int exitCode);
    [LibraryImport("Microsoft.UI.Windowing.Core.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ContentPreTranslateMessage(in Message message);
}
