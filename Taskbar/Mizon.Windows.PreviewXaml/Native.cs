using System.Runtime.InteropServices;

namespace Mizon.Windows.PreviewXaml;

internal static unsafe partial class Native
{
    internal static readonly Guid Framework = new("A391D09B-4A99-4B7C-9D8D-6FA5D01F6FBF");
    internal static readonly Guid Dependency = new("5C526665-F60E-4912-AF59-5FE0680F089D");
    internal static readonly Guid Panel = new("A50A4BBD-8361-469C-90DA-E9A40C7474DF");
    internal static readonly Guid TextBlock = new("AE2D9271-3B4A-45FC-8468-F7949548F4D5");
    internal static nint Slot(nint instance, int index) => (*(nint**)instance)[index];
    internal static nint Query(nint instance, Guid iid)
    {
        if (instance == 0) return 0;
        nint result = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(instance, 0))(instance, &iid, &result);
        return hr < 0 ? 0 : result;
    }
    internal static void Release(nint instance)
    {
        if (instance != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(instance, 2))(instance);
    }
    internal static void Check(int result) => Marshal.ThrowExceptionForHR(result);
    internal static nint GetObject(nint instance, int slot)
    {
        nint result = 0;
        Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(instance, slot))(instance, &result));
        return result;
    }
    internal static double GetDouble(nint instance, int slot)
    {
        double result = 0;
        Check(((delegate* unmanaged[Stdcall]<nint, double*, int>)Slot(instance, slot))(instance, &result));
        return result;
    }
    internal static void PutDouble(nint instance, int slot, double value) =>
        Check(((delegate* unmanaged[Stdcall]<nint, double, int>)Slot(instance, slot))(instance, value));
    internal static string GetText(nint instance, int slot)
    {
        var value = GetObject(instance, slot);
        try { return HStringText(value); }
        finally { WindowsDeleteString(value); }
    }
    internal static string HStringText(nint value)
    {
        if (value == 0) return "";
        var buffer = WindowsGetStringRawBuffer(value, out var length);
        return new string(buffer, 0, checked((int)length));
    }
    internal static nint HString(string value)
    {
        fixed (char* buffer = value)
        {
            Check(WindowsCreateString(buffer, (uint)value.Length, out var result));
            return result;
        }
    }
    internal static nint Factory(string name, Guid iid)
    {
        var text = HString(name);
        try { Check(RoGetActivationFactory(text, in iid, out var result)); return result; }
        finally { WindowsDeleteString(text); }
    }
    internal static string ClassName(nint instance) => GetText(instance, 4);
    [LibraryImport("combase.dll")] internal static partial int RoGetActivationFactory(nint name, in Guid iid, out nint result);
    [LibraryImport("combase.dll")] internal static partial int RoInitialize(uint type);
    [LibraryImport("combase.dll")] internal static partial void RoUninitialize();
    [LibraryImport("combase.dll")] internal static partial int WindowsCreateString(char* text, uint length, out nint result);
    [LibraryImport("combase.dll")] internal static partial int WindowsDeleteString(nint value);
    [LibraryImport("combase.dll")] private static partial char* WindowsGetStringRawBuffer(nint value, out uint length);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool IsWindow(nint window);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int GetWindowText(nint window, char* buffer, int length);
    [LibraryImport("user32.dll")] internal static partial uint GetWindowThreadProcessId(nint window, out uint processId);
}
