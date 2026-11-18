using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mizon.Windows.PreviewXaml;

internal static unsafe partial class MessageWindow
{
    // Explorer may retain an older NativeAOT module until sign-out. Each module
    // needs its own WNDPROC and owner table, including after a hot upgrade.
    private static readonly string ClassName = "Mizon.PreviewXaml.Dispatcher." + Guid.NewGuid().ToString("N");
    private static readonly ConcurrentDictionary<nint, PreviewTap> Owners = new();
    internal static nint Create(PreviewTap owner)
    {
        fixed (char* name = ClassName)
        {
            var definition = new WindowClass
            {
                Size = (uint)sizeof(WindowClass), ClassName = name,
                Procedure = &WindowProcedure, Instance = GetModuleHandle(null)
            };
            if (RegisterClassEx(in definition) == 0 && Marshal.GetLastPInvokeError() != 1410)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        }
        var window = CreateWindowEx(0, ClassName, "", 0, 0, 0, 0, 0, -3, 0, GetModuleHandle(null), 0);
        if (window == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        Owners[window] = owner;
        SetTimer(window, 1, 1000, 0);
        return window;
    }
    internal static void Queue(nint window) { if (window != 0) PostMessage(window, 0x8001, 0, 0); }
    internal static void Close(nint window)
    {
        if (window == 0) return;
        Owners.TryRemove(window, out _);
        KillTimer(window, 1);
        DestroyWindow(window);
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (Owners.TryGetValue(window, out var owner) && message is 0x8001 or 0x0113)
            {
                if (File.Exists(Path.Combine(PreviewConfiguration.DirectoryPath, "stop"))) owner.Stop();
                else owner.Scan();
                return 0;
            }
        }
        catch (Exception error) { PreviewTap.Log("Message dispatch", error); }
        return DefWindowProc(window, message, wParam, lParam);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        internal uint Size, Style;
        internal delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint> Procedure;
        internal int ClassExtra, WindowExtra;
        internal nint Instance, Icon, Cursor, Background;
        internal char* MenuName;
        internal char* ClassName;
        internal nint SmallIcon;
    }
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? name);
    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    private static partial ushort RegisterClassEx(in WindowClass definition);
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateWindowEx(uint extended, string className, string text, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint module, nint parameter);
    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll")] private static partial nuint SetTimer(nint window, nuint id, uint milliseconds, nint procedure);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool KillTimer(nint window, nuint id);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyWindow(nint window);
}
