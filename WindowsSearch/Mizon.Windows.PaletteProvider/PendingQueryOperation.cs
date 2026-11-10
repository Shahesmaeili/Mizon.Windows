using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mizon.Windows.PaletteProvider;

// A genuinely asynchronous WinRT query: never block the Search host on HTTP.
internal sealed class PendingQueryOperation : NativeObject
{
    private static readonly Guid InfoId = new("00000036-0000-0000-C000-000000000046");
    private static readonly nint OperationTable = CreateOperationTable();
    private static readonly nint InfoTable = CreateInfoTable();
    private static int _nextId;
    private readonly uint _id = (uint)Interlocked.Increment(ref _nextId);
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private nint _result, _completed, _progress;
    private int _status, _error;
    private bool _closed, _assigned, _delivered, _started;

    internal PendingQueryOperation()
    {
        try
        {
            AddInterface(CompletedOperation.QueryOperationId, OperationTable);
            AddInterface(InfoId, InfoTable);
            EnableMarshaling();
        }
        catch { Dispose(); throw; }
    }
    internal override string RuntimeClassName => "Windows.Foundation.IAsyncOperationWithProgress`2<Windows.UI.Palettes.PaletteQueryResultsView, Windows.UI.Palettes.PaletteQueryProgress>";

    internal void Start(Func<PendingQueryOperation, CancellationToken, Task<nint>> work)
    {
        lock (_gate)
        {
            if (_started || _closed) throw new InvalidOperationException("A query can only be started once.");
            _started = true;
        }
        NativeMethods.AddRef(Identity); // Worker owns an independent COM lifetime.
        _ = Task.Run(async () =>
        {
            nint result = 0;
            try
            {
                result = await work(this, _cancellation.Token).ConfigureAwait(false);
                _cancellation.Token.ThrowIfCancellationRequested();
                Complete(result, 1, 0);
            }
            catch (OperationCanceledException) { Complete(0, 2, unchecked((int)0x800704C7)); }
            catch (Exception error) { Complete(0, 3, Log.Error("Live search query", error)); }
            finally { using var apartment = new ComApartment(); NativeMethods.Release(result); NativeMethods.Release(Identity); }
        });
    }

    internal void RequestCancellation()
    {
        lock (_gate) { if (_closed || _status != 0) return; }
        CancelSafely();
    }
    internal void Report(nint progress)
    {
        nint handler;
        lock (_gate)
        {
            if (_closed || _status != 0 || _cancellation.IsCancellationRequested) return;
            handler = _progress;
            NativeMethods.AddRef(handler);
        }
        if (handler == 0) return;
        try { InvokeProgress(handler, Identity, progress); }
        finally { NativeMethods.Release(handler); }
    }
    private static unsafe void InvokeProgress(nint handler, nint operation, nint progress)
    {
        var hr = ((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)NativeMethods.Table(handler)[3])(handler, operation, progress);
        if (hr < 0) Log.Write($"Query progress callback returned 0x{hr:X8}");
    }
    private void Complete(nint result, int status, int error)
    {
        using var apartment = new ComApartment();
        nint handler = 0;
        lock (_gate)
        {
            if (_closed || _status != 0) return;
            _status = status; _error = error;
            NativeMethods.AddRef(result); _result = result;
            if (_assigned && !_delivered)
            {
                _delivered = true; handler = _completed; NativeMethods.AddRef(handler);
            }
        }
        InvokeCompletion(handler, status);
    }
    private unsafe void InvokeCompletion(nint handler, int status)
    {
        if (handler == 0) return;
        using var apartment = new ComApartment();
        NativeMethods.AddRef(Identity);
        try
        {
            var hr = ((delegate* unmanaged[Stdcall]<nint, nint, int, int>)NativeMethods.Table(handler)[3])(handler, Identity, status);
            if (hr < 0) Log.Write($"Query completion callback returned 0x{hr:X8}");
        }
        finally { NativeMethods.Release(handler); NativeMethods.Release(Identity); }
    }
    private static unsafe nint CreateOperationTable()
    {
        var table = (nint*)NewTable(11);
        table[6] = (nint)(delegate* unmanaged[Stdcall]<nint,nint,int>)&PutProgress;
        table[7] = (nint)(delegate* unmanaged[Stdcall]<nint,nint*,int>)&GetProgress;
        table[8] = (nint)(delegate* unmanaged[Stdcall]<nint,nint,int>)&PutCompleted;
        table[9] = (nint)(delegate* unmanaged[Stdcall]<nint,nint*,int>)&GetCompleted;
        table[10] = (nint)(delegate* unmanaged[Stdcall]<nint,nint*,int>)&GetResults;
        return (nint)table;
    }
    private static unsafe nint CreateInfoTable()
    {
        var table = (nint*)NewTable(11);
        table[6] = (nint)(delegate* unmanaged[Stdcall]<nint,uint*,int>)&GetId;
        table[7] = (nint)(delegate* unmanaged[Stdcall]<nint,int*,int>)&GetStatus;
        table[8] = (nint)(delegate* unmanaged[Stdcall]<nint,int*,int>)&GetError;
        table[9] = (nint)(delegate* unmanaged[Stdcall]<nint,int>)&Cancel;
        table[10] = (nint)(delegate* unmanaged[Stdcall]<nint,int>)&Close;
        return (nint)table;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int PutCompleted(nint self, nint handler)
    {
        try
        {
            var owner = Owner<PendingQueryOperation>(self); int status;
            lock (owner._gate)
            {
                if (owner._closed) return unchecked((int)0x80000013);
                if (owner._assigned) return unchecked((int)0x80000018);
                owner._assigned = true; NativeMethods.AddRef(handler); owner._completed = handler;
                status = owner._status;
                if (status == 0) return 0;
                owner._delivered = true; NativeMethods.AddRef(handler);
            }
            owner.InvokeCompletion(handler, status); return 0;
        }
        catch (Exception error) { return Log.Error("LiveQuery.PutCompleted", error); }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int PutProgress(nint self, nint handler)
    {
        var owner = Owner<PendingQueryOperation>(self); nint old;
        lock (owner._gate)
        {
            if (owner._closed) return unchecked((int)0x80000013);
            NativeMethods.AddRef(handler); old = owner._progress; owner._progress = handler;
        }
        NativeMethods.Release(old); return 0;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe int GetProgress(nint self, nint* value) => GetHandler(self,value,false);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe int GetCompleted(nint self, nint* value) => GetHandler(self,value,true);
    private static unsafe int GetHandler(nint self,nint* value,bool completed)
    {
        if (value == null) return NativeMethods.PointerError;
        *value = 0; var owner = Owner<PendingQueryOperation>(self);
        lock (owner._gate)
        {
            if (owner._closed) return unchecked((int)0x80000013);
            *value = completed ? owner._completed : owner._progress; NativeMethods.AddRef(*value); return 0;
        }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe int GetResults(nint self,nint* value)
    {
        if (value == null) return NativeMethods.PointerError;
        *value = 0; var owner = Owner<PendingQueryOperation>(self);
        lock (owner._gate)
        {
            if (owner._closed) return unchecked((int)0x80000013);
            if (owner._status == 0) return unchecked((int)0x8000000E);
            if (owner._error < 0) return owner._error;
            *value = owner._result; NativeMethods.AddRef(*value); return 0;
        }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe int GetId(nint self,uint* value) { if(value == null)return NativeMethods.PointerError; *value=Owner<PendingQueryOperation>(self)._id; return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe int GetStatus(nint self,int* value) { if(value == null)return NativeMethods.PointerError; var owner=Owner<PendingQueryOperation>(self); lock(owner._gate)*value=owner._status; return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe int GetError(nint self,int* value) { if(value == null)return NativeMethods.PointerError; var owner=Owner<PendingQueryOperation>(self); lock(owner._gate)*value=owner._error; return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Cancel(nint self) { Owner<PendingQueryOperation>(self).RequestCancellation(); return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Close(nint self) { Owner<PendingQueryOperation>(self).CloseCore(); return 0; }
    private void CloseCore()
    {
        nint result, completed, progress;
        lock (_gate)
        {
            if (_closed) return;
            _closed=true; result=_result; completed=_completed; progress=_progress;
            _result=0; _completed=0; _progress=0;
        }
        try { CancelSafely(); }
        finally { NativeMethods.Release(result); NativeMethods.Release(completed); NativeMethods.Release(progress); }
    }
    private void CancelSafely()
    {
        try { _cancellation.Cancel(); }
        catch (ObjectDisposedException) { }
        catch (Exception error) { Log.Error("Query cancellation callback", error); }
    }
    protected override void ReleaseResources() { CloseCore(); _cancellation.Dispose(); }
}
