using System;
using System.Runtime.InteropServices;

namespace Mizon.Windows.Services;

/// <summary>Blittable Windows SDK ABI; no SafeArray marshalling or runtime COM generation.</summary>
internal sealed unsafe partial class TaskbarNative : IDisposable
{
    private nint _instance;
    internal TaskbarNative()
    {
        var clsid = new Guid("56FDF344-FD6D-11D0-958A-006097C9A090");
        var iid = new Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF"); // ITaskbarList3, shobjidl_core.h
        Marshal.ThrowExceptionForHR(CoCreateInstance(in clsid, 0, 1, in iid, out _instance));
        try { Check(((delegate* unmanaged[Stdcall]<nint, int>)Slot(3))(_instance)); }
        catch { Dispose(); throw; }
    }
    internal void SetState(nint window, uint state) => Check(((delegate* unmanaged[Stdcall]<nint, nint, uint, int>)Slot(10))(_instance, window, state));
    internal void SetValue(nint window, ulong current, ulong total) => Check(((delegate* unmanaged[Stdcall]<nint, nint, ulong, ulong, int>)Slot(9))(_instance, window, current, total));
    internal readonly record struct Button(uint Id, nint Icon, string Tooltip, bool Enabled);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ThumbButton
    {
        internal uint Mask, Id, Bitmap;
        internal nint Icon;
        internal fixed char Tooltip[260];
        internal uint Flags;
    }
    internal void SetButtons(nint window, bool add, ReadOnlySpan<Button> buttons)
    {
        if (buttons.Length is < 1 or > 7) throw new ArgumentOutOfRangeException(nameof(buttons));
        var native = stackalloc ThumbButton[buttons.Length];
        for (var index = 0; index < buttons.Length; index++)
        {
            native[index] = default;
            native[index].Mask = 2 | 4 | 8;
            native[index].Id = buttons[index].Id;
            native[index].Icon = buttons[index].Icon;
            native[index].Flags = buttons[index].Enabled ? 0u : 1u;
            buttons[index].Tooltip.AsSpan(0, Math.Min(259, buttons[index].Tooltip.Length)).CopyTo(new Span<char>(native[index].Tooltip, 260));
        }
        Check(((delegate* unmanaged[Stdcall]<nint, nint, uint, ThumbButton*, int>)Slot(add ? 15 : 16))(_instance, window, (uint)buttons.Length, native));
    }
    internal static int ButtonAbiSize => sizeof(ThumbButton);
    private nint Slot(int index) => (*(nint**)_instance)[index];
    private static void Check(int result) => Marshal.ThrowExceptionForHR(result);
    public void Dispose() { if (_instance == 0) return; ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(2))(_instance); _instance = 0; }
    [LibraryImport("ole32.dll")] private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] internal delegate nint SubclassProc(nint window, uint message, nint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowSubclass(nint window, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool RemoveWindowSubclass(nint window, SubclassProc callback, nuint id);
    [LibraryImport("comctl32.dll")] internal static partial nint DefSubclassProc(nint window, uint message, nint wParam, nint lParam);
    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)] internal static partial uint RegisterWindowMessage(string name);
}

