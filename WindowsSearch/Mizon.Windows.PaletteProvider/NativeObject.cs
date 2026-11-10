using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mizon.Windows.PaletteProvider;

// One COM identity owns every interface pointer, managed state and aggregated FTM.
// No runtime-generated interface proxies or reflection are needed in this server.
internal abstract unsafe class NativeObject : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceEntry { internal nint Vtable; internal nint Owner; }

    private readonly Dictionary<Guid, nint> _interfaces = [];
    private readonly GCHandle _handle;
    private int _references = 1;
    private nint _marshaler;
    private nint _identity;
    private bool _disposed;
    private readonly bool _inspectable;
    private static int _liveCount;
    internal static int LiveCount => Volatile.Read(ref _liveCount);
    protected NativeObject(bool inspectable = true)
    {
        _inspectable = inspectable;
        _handle = GCHandle.Alloc(this);
        Interlocked.Increment(ref _liveCount);
        Program.Activity();
    }
    internal nint Identity => _identity;
    internal abstract string RuntimeClassName { get; }

    protected nint AddInterface(Guid iid, nint table)
    {
        var entry = (InterfaceEntry*)NativeMemory.AllocZeroed((nuint)sizeof(InterfaceEntry));
        entry->Vtable = table;
        entry->Owner = GCHandle.ToIntPtr(_handle);
        _interfaces.Add(iid, (nint)entry);
        if (_identity == 0) _identity = (nint)entry;
        return (nint)entry;
    }

    internal nint GetInterface(Guid iid)
    {
        if (iid == NativeMethods.UnknownId || iid == NativeMethods.AgileId) return _identity;
        if (iid == NativeMethods.InspectableId && _inspectable) return _identity;
        return _interfaces.GetValueOrDefault(iid);
    }

    protected void EnableMarshaling() => NativeMethods.Check(NativeMethods.CoCreateFreeThreadedMarshaler(_identity, out _marshaler));
    internal static T Owner<T>(nint self) where T : NativeObject => (T)GCHandle.FromIntPtr(((InterfaceEntry*)self)->Owner).Target!;

    protected static nint NewTable(int slots, bool inspectable = true)
    {
        var table = (nint*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(NativeObject), slots * sizeof(nint));
        table[0] = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)&QueryInterface;
        table[1] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&AddRef;
        table[2] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&Release;
        if (inspectable)
        {
            table[3] = (nint)(delegate* unmanaged[Stdcall]<nint, uint*, Guid**, int>)&GetIids;
            table[4] = (nint)(delegate* unmanaged[Stdcall]<nint, nint*, int>)&GetRuntimeClassName;
            table[5] = (nint)(delegate* unmanaged[Stdcall]<nint, int*, int>)&GetTrustLevel;
        }
        return (nint)table;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int QueryInterface(nint self, Guid* iid, nint* result)
    {
        if (iid == null || result == null) return NativeMethods.PointerError;
        *result = 0;
        try
        {
            var owner = Owner<NativeObject>(self);
            if (*iid == NativeMethods.MarshalId && owner._marshaler != 0)
            {
                var hr = NativeMethods.Query(owner._marshaler, *iid, out var marshaler);
                *result = marshaler;
                return hr;
            }
            var pointer = owner.GetInterface(*iid);
            if (pointer == 0) return NativeMethods.NoInterface;
            Interlocked.Increment(ref owner._references);
            *result = pointer;
            return 0;
        }
        catch (Exception error) { return Log.Error("QueryInterface", error); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint AddRef(nint self) => (uint)Interlocked.Increment(ref Owner<NativeObject>(self)._references);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint Release(nint self)
    {
        var owner = Owner<NativeObject>(self);
        var count = Interlocked.Decrement(ref owner._references);
        if (count == 0) owner.Dispose();
        return (uint)count;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetIids(nint self, uint* count, Guid** iids)
    {
        if (count == null || iids == null) return NativeMethods.PointerError;
        *count = 0; *iids = null;
        try
        {
            var owner = Owner<NativeObject>(self);
            var values = (Guid*)Marshal.AllocCoTaskMem(owner._interfaces.Count * sizeof(Guid));
            var index = 0;
            foreach (var id in owner._interfaces.Keys) values[index++] = id;
            *count = (uint)index; *iids = values;
            return 0;
        }
        catch (Exception error) { return Log.Error("GetIids", error); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetRuntimeClassName(nint self, nint* name)
    {
        if (name == null) return NativeMethods.PointerError;
        *name = 0;
        try { *name = NativeMethods.String(Owner<NativeObject>(self).RuntimeClassName); return 0; }
        catch (Exception error) { return Log.Error("GetRuntimeClassName", error); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetTrustLevel(nint self, int* level)
    {
        if (level == null) return NativeMethods.PointerError;
        *level = 0;
        return 0;
    }

    protected virtual void ReleaseResources() { }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { ReleaseResources(); }
        catch (Exception error) { Log.Error("Dispose", error); }
        NativeMethods.Release(_marshaler);
        _marshaler = 0;
        foreach (var pointer in _interfaces.Values) NativeMemory.Free((void*)pointer);
        _interfaces.Clear();
        _handle.Free();
        Interlocked.Decrement(ref _liveCount);
        Program.Activity();
    }
}
