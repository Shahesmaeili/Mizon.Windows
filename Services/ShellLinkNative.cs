using System;
using System.Runtime.InteropServices;

namespace Mizon.Windows.Services;

// Windows SDK ABI, like TaskbarNative: no runtime-generated COM wrappers.
// Create, use and release on the same thread, with no await while COM is owned.
internal sealed unsafe partial class ShellLinkNative : IDisposable
{
    private nint _link;
    private nint _file;
    private bool _uninitialize;

    internal ShellLinkNative()
    {
        var result = CoInitializeEx(0, 0);
        if (result != unchecked((int)0x80010106)) Marshal.ThrowExceptionForHR(result);
        _uninitialize = result >= 0;
        try
        {
            var clsid = new Guid("00021401-0000-0000-C000-000000000046");
            var iid = new Guid("000214F9-0000-0000-C000-000000000046");
            Marshal.ThrowExceptionForHR(CoCreateInstance(in clsid, 0, 1, in iid, out _link));
            iid = new Guid("0000010B-0000-0000-C000-000000000046");
            nint file;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(_link, 0))(_link, &iid, &file));
            _file = file;
        }
        catch { Dispose(); throw; }
    }

    internal void SetTarget(string value) => SetString(20, value);
    internal void SetArguments(string value) => SetString(11, value);
    internal void SetDescription(string value) => SetString(7, value);
    internal void SetWorkingDirectory(string value) => SetString(9, value);
    internal void SetIcon(string path)
    {
        fixed (char* text = path)
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, char*, int, int>)Slot(_link, 17))(_link, text, 0));
    }
    internal string GetArguments()
    {
        var text = stackalloc char[2048];
        text[0] = '\0';
        Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, char*, int, int>)Slot(_link, 10))(_link, text, 2048));
        return new string(text);
    }
    internal void Load(string path)
    {
        fixed (char* text = path)
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, char*, uint, int>)Slot(_file, 5))(_file, text, 0));
    }
    internal void Save(string path)
    {
        fixed (char* text = path)
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, char*, int, int>)Slot(_file, 6))(_file, text, 1));
    }
    private void SetString(int slot, string value)
    {
        fixed (char* text = value)
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, char*, int>)Slot(_link, slot))(_link, text));
    }
    private static nint Slot(nint instance, int index) => (*(nint**)instance)[index];
    public void Dispose()
    {
        if (_file != 0) { ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(_file, 2))(_file); _file = 0; }
        if (_link != 0) { ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(_link, 2))(_link); _link = 0; }
        if (_uninitialize) { _uninitialize = false; CoUninitialize(); }
    }
    [LibraryImport("ole32.dll")] private static partial int CoInitializeEx(nint reserved, uint flags);
    [LibraryImport("ole32.dll")] private static partial void CoUninitialize();
    [LibraryImport("ole32.dll")] private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
}
