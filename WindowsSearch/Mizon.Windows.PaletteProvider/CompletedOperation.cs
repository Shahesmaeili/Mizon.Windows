using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mizon.Windows.PaletteProvider;

// The static test result completes immediately. Windows still receives the real
// WinRT async/progress contract, with independently reference-counted IAsyncInfo.
internal sealed unsafe class CompletedOperation : NativeObject
{
    internal static readonly Guid QueryOperationId = new("cb62b7ba-8e45-55c1-bd2e-81b7f513d135");
    internal static readonly Guid ActionId = new("5A648006-843A-4DA9-865B-9D26E5DFAD7B");
    internal static readonly Guid ContentOperationId = new("cb73ad0d-69fd-5ae3-988c-ad2bf68a1660");
    private static readonly Guid AsyncInfoId = new("00000036-0000-0000-C000-000000000046");
    private static readonly nint OperationTable = CreateOperationTable();
    private static readonly nint ActionTable = CreateActionTable();
    private static readonly nint ContentTable = CreateContentTable();
    private static readonly nint InfoTable = CreateInfoTable();
    private static int _nextId;
    private readonly uint _id = (uint)Interlocked.Increment(ref _nextId);
    private readonly Lock _gate = new();
    private readonly bool _action;
    private readonly bool _content;
    private nint _result;
    private nint _completed;
    private nint _progress;
    private readonly int _error;
    private bool _closed;
    private bool _completedAssigned;

    internal CompletedOperation(nint result = 0, int error = 0, bool action = false, bool content = false)
    {
        try
        {
            _action = action; _content = content; _result = result; _error = error;
            NativeMethods.AddRef(result);
            AddInterface(action ? ActionId : content ? ContentOperationId : QueryOperationId,
                action ? ActionTable : content ? ContentTable : OperationTable);
            AddInterface(AsyncInfoId, InfoTable);
            EnableMarshaling();
        }
        catch { Dispose(); throw; }
    }

    internal override string RuntimeClassName => _action ? "Windows.Foundation.IAsyncAction" : _content
        ? "Windows.Foundation.IAsyncOperation`1<Windows.UI.Palettes.PaletteContent>"
        : "Windows.Foundation.IAsyncOperationWithProgress`2<Windows.UI.Palettes.PaletteQueryResultsView, Windows.UI.Palettes.PaletteQueryProgress>";
    private int Status => _error < 0 ? 3 : 1;

    private static nint CreateOperationTable()
    {
        var table = (nint*)NewTable(11);
        table[6] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, int>)&PutProgress;
        table[7] = (nint)(delegate* unmanaged[Stdcall]<nint, nint*, int>)&GetProgress;
        table[8] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, int>)&PutCompleted;
        table[9] = (nint)(delegate* unmanaged[Stdcall]<nint, nint*, int>)&GetCompleted;
        table[10] = (nint)(delegate* unmanaged[Stdcall]<nint, nint*, int>)&GetResults;
        return (nint)table;
    }

    private static nint CreateActionTable()
    {
        var table = (nint*)NewTable(9);
        table[6] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, int>)&PutCompleted;
        table[7] = (nint)(delegate* unmanaged[Stdcall]<nint, nint*, int>)&GetCompleted;
        table[8] = (nint)(delegate* unmanaged[Stdcall]<nint, int>)&GetActionResults;
        return (nint)table;
    }

    private static nint CreateContentTable()
    {
        var table = (nint*)NewTable(9);
        table[6] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, int>)&PutCompleted;
        table[7] = (nint)(delegate* unmanaged[Stdcall]<nint, nint*, int>)&GetCompleted;
        table[8] = (nint)(delegate* unmanaged[Stdcall]<nint, nint*, int>)&GetResults;
        return (nint)table;
    }

    private static nint CreateInfoTable()
    {
        var table = (nint*)NewTable(11);
        table[6] = (nint)(delegate* unmanaged[Stdcall]<nint, uint*, int>)&GetId;
        table[7] = (nint)(delegate* unmanaged[Stdcall]<nint, int*, int>)&GetStatus;
        table[8] = (nint)(delegate* unmanaged[Stdcall]<nint, int*, int>)&GetError;
        table[9] = (nint)(delegate* unmanaged[Stdcall]<nint, int>)&Cancel;
        table[10] = (nint)(delegate* unmanaged[Stdcall]<nint, int>)&Close;
        return (nint)table;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int PutCompleted(nint self, nint handler)
    {
        try
        {
            var owner = Owner<CompletedOperation>(self);
            lock (owner._gate)
            {
                if (owner._closed) return unchecked((int)0x80000013);
                if (owner._completedAssigned) return unchecked((int)0x80000018);
                owner._completedAssigned = true;
                NativeMethods.AddRef(handler);
                owner._completed = handler;
                NativeMethods.AddRef(handler);
            }
            if (handler == 0) return 0;
            NativeMethods.AddRef(owner.Identity);
            try
            {
                var hr = ((delegate* unmanaged[Stdcall]<nint, nint, int, int>)NativeMethods.Table(handler)[3])(handler, owner.Identity, owner.Status);
                if (hr < 0) Log.Write($"Completed callback returned 0x{hr:X8}");
            }
            finally { NativeMethods.Release(handler); NativeMethods.Release(owner.Identity); }
            return 0;
        }
        catch (Exception error) { return Log.Error("PutCompleted", error); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetCompleted(nint self, nint* result) => GetHandler(self, result, true);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetProgress(nint self, nint* result) => GetHandler(self, result, false);
    private static int GetHandler(nint self, nint* result, bool completed)
    {
        if (result == null) return NativeMethods.PointerError;
        *result = 0;
        var owner = Owner<CompletedOperation>(self);
        lock (owner._gate)
        {
            if (owner._closed) return unchecked((int)0x80000013);
            *result = completed ? owner._completed : owner._progress;
            NativeMethods.AddRef(*result);
        }
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int PutProgress(nint self, nint handler)
    {
        var owner = Owner<CompletedOperation>(self);
        nint previous;
        lock (owner._gate)
        {
            if (owner._closed) return unchecked((int)0x80000013);
            NativeMethods.AddRef(handler);
            previous = owner._progress;
            owner._progress = handler;
        }
        NativeMethods.Release(previous);
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetResults(nint self, nint* result)
    {
        if (result == null) return NativeMethods.PointerError;
        *result = 0;
        var owner = Owner<CompletedOperation>(self);
        lock (owner._gate)
        {
            if (owner._closed) return unchecked((int)0x80000013);
            if (owner._error < 0) return owner._error;
            *result = owner._result;
            NativeMethods.AddRef(*result);
        }
        return 0;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetActionResults(nint self)
    {
        var owner = Owner<CompletedOperation>(self);
        lock (owner._gate) return owner._closed ? unchecked((int)0x80000013) : owner._error;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetId(nint self, uint* value) { if (value == null) return NativeMethods.PointerError; *value = Owner<CompletedOperation>(self)._id; return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetStatus(nint self, int* value) { if (value == null) return NativeMethods.PointerError; *value = Owner<CompletedOperation>(self).Status; return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetError(nint self, int* value) { if (value == null) return NativeMethods.PointerError; *value = Owner<CompletedOperation>(self)._error; return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Cancel(nint self) => 0; // Already completed; cancellation cannot change the terminal state.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Close(nint self) { Owner<CompletedOperation>(self).ReleaseResources(); return 0; }
    protected override void ReleaseResources()
    {
        nint result; nint completed; nint progress;
        lock (_gate)
        {
            if (_closed) return;
            _closed = true;
            result = _result; completed = _completed; progress = _progress;
            _result = 0; _completed = 0; _progress = 0;
        }
        NativeMethods.Release(result); NativeMethods.Release(completed); NativeMethods.Release(progress);
    }
}
