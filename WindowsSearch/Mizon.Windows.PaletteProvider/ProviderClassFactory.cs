using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mizon.Windows.PaletteProvider;

internal sealed unsafe class ProviderClassFactory : NativeObject
{
    internal static readonly Guid ClassId = new("6A9CD8C1-B10B-41B6-944A-59CE8A09C182");
    internal static readonly Guid FactoryId = new("00000001-0000-0000-C000-000000000046");
    private static readonly nint Vtable = CreateTable();
    internal ProviderClassFactory() : base(false)
    {
        try { AddInterface(FactoryId, Vtable); }
        catch { Dispose(); throw; }
    }
    internal override string RuntimeClassName => "Mizon.Windows.PaletteProvider.ClassFactory";

    private static nint CreateTable()
    {
        var table = (nint*)NewTable(5, false);
        table[3] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)&CreateInstance;
        table[4] = (nint)(delegate* unmanaged[Stdcall]<nint, int, int>)&LockServer;
        return (nint)table;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int CreateInstance(nint self, nint outer, Guid* iid, nint* result)
    {
        if (iid == null || result == null) return NativeMethods.PointerError;
        *result = 0;
        if (outer != 0) return unchecked((int)0x80040110);
        try
        {
            Log.Write($"CreateInstance iid={*iid}");
            var provider = new MusicPaletteExtension();
            var hr = NativeMethods.Query(provider.Identity, *iid, out var pointer);
            NativeMethods.Release(provider.Identity);
            *result = pointer;
            return hr;
        }
        catch (Exception error) { return Log.Error("CreateInstance", error); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int LockServer(nint self, int locked) { Program.ChangeServerLock(locked != 0); return 0; }
}
