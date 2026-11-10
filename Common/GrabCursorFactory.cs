using Microsoft.UI.Input;
using Mizon.Windows.Services;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace Mizon.Windows.Common;

/// <summary>Native open/closed-hand cursors. Each returned cursor is owned by its caller.</summary>
internal static partial class GrabCursorFactory
{
    public static InputCursor Create(bool grabbing, double rasterizationScale = 1)
    {
        try { return CreateCore(grabbing, rasterizationScale); }
        catch (Exception exception) when (exception is COMException or Win32Exception or IOException)
        {
            // A missing/corrupt deployment asset must not interrupt model input.
            AppLog.Error($"[GrabCursor] Failed creating {(grabbing ? "closed" : "open")}-hand cursor.", exception);
            return InputSystemCursor.Create(InputSystemCursorShape.Hand);
        }
    }

    private static unsafe InputCursor CreateCore(bool grabbing, double rasterizationScale)
    {
        var scale = double.IsFinite(rasterizationScale) ? Math.Clamp(rasterizationScale, .5, 4) : 1;
        var dpi = (uint)Math.Round(96 * scale);
        var width = Math.Max(16, GetSystemMetricsForDpi(13 /* SM_CXCURSOR */, dpi));
        var height = Math.Max(16, GetSystemMetricsForDpi(14 /* SM_CYCURSOR */, dpi));
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Cursors", grabbing ? "grabbing.cur" : "grab.cur");
        var nativeCursor = LoadImageW(0, path, 2 /* IMAGE_CURSOR */, width, height, 0x10 /* LR_LOADFROMFILE */);
        if (nativeCursor == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Unable to load cursor '{path}'.");

        nint className = 0, factory = 0, cursorAbi = 0;
        try
        {
            const string runtimeClass = "Microsoft.UI.Input.InputCursor";
            Marshal.ThrowExceptionForHR(WindowsCreateString(runtimeClass, (uint)runtimeClass.Length, out className));
            var iid = new Guid("AC6F5065-90C4-46CE-BEB7-05E138E54117");
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(className, in iid, out factory));

            // IInputCursorStaticsInterop inherits IInspectable (six base slots).
            // Raw ABI invocation avoids reflection/RCW generation and is NativeAOT-safe.
            var vtable = *(nint**)factory;
            var create = (delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)vtable[6];
            Marshal.ThrowExceptionForHR(create(factory, nativeCursor, &cursorAbi));
            return WinRT.MarshalInterface<InputCursor>.FromAbi(cursorAbi);
        }
        finally
        {
            if (cursorAbi != 0) Release(cursorAbi);
            if (factory != 0) Release(factory);
            if (className != 0) WindowsDeleteString(className);
            // CreateFromHCursor copies custom cursors; the InputCursor owns its copy.
            DestroyCursor(nativeCursor);
        }
    }

    private static unsafe void Release(nint instance) =>
        ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)instance)[2])(instance);

    [LibraryImport("user32.dll", EntryPoint = "LoadImageW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint LoadImageW(nint instance, string name, uint type, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyCursor(nint cursor);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WindowsCreateString(string value, uint length, out nint result);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(nint value);

    [LibraryImport("combase.dll")]
    private static partial int RoGetActivationFactory(nint className, in Guid iid, out nint factory);
}
