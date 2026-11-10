using Mizon.Windows.PaletteProvider;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

internal static unsafe class PendingQueryOperationTests
{
    private static readonly Guid InfoId = new("00000036-0000-0000-c000-000000000046");
    private static int _passes;

    internal static int Run()
    {
        _passes = 0;
        var baseline = NativeObject.LiveCount;
        TestDelayedAndLateCompletion(false);
        TestDelayedAndLateCompletion(true);
        TestCancellation(close: false, throwingRegistration: false);
        TestCancellation(close: false, throwingRegistration: true);
        TestCancellation(close: true, throwingRegistration: true);
        TestReentrantClose();
        TestFailure();
        WaitUntil(() => NativeObject.LiveCount == baseline);
        Check(NativeObject.LiveCount == baseline, "pending workers, callbacks and result references all return to baseline");
        Console.WriteLine($"Pending query PASS {_passes} assertions; no network or installed provider activation.");
        return _passes;
    }

    private static void TestDelayedAndLateCompletion(bool lateHandler)
    {
        var operation = new PendingQueryOperation();
        var info = NativeMethods.As(operation.Identity, InfoId);
        using var start = new ManualResetEventSlim();
        using var ready = new ManualResetEventSlim();
        var callback = new Completion();
        DummyResult? result = null;
        try
        {
            operation.Start((_, token) =>
            {
                ready.Set();
                if (!start.Wait(TimeSpan.FromSeconds(5), token)) throw new TimeoutException("Test work gate.");
                result = new DummyResult();
                return Task.FromResult(result.Identity);
            });
            Check(ready.Wait(5000), "pending worker starts without blocking caller");
            Check(Status(info) == 0, "operation remains Started before work completes");
            nint value = 99;
            Check(GetResults(operation.Identity, &value) == unchecked((int)0x8000000e) && value == 0, "early GetResults rejects incomplete query");
            if (!lateHandler) NativeMethods.Check(PutCompleted(operation.Identity, callback.Identity));
            var duplicateStart = false;
            try { operation.Start((_, _) => Task.FromResult<nint>(0)); }
            catch (InvalidOperationException) { duplicateStart = true; }
            Check(duplicateStart, "a pending operation can only start once");
            start.Set();
            WaitUntil(() => Status(info) == 1);
            if (lateHandler) NativeMethods.Check(PutCompleted(operation.Identity, callback.Identity));
            WaitUntil(() => Volatile.Read(ref callback.Calls) == 1);
            Check(callback.LastStatus == 1 && callback.LastOperation == operation.Identity,
                lateHandler ? "late completion handler is invoked exactly once" : "preassigned completion handler is invoked exactly once");
            Check(PutCompleted(operation.Identity, callback.Identity) == unchecked((int)0x80000018), "completion handler cannot be reassigned");
            value = 0;
            NativeMethods.Check(GetResults(operation.Identity, &value));
            Check(value == result!.Identity && !result.Disposed, "worker result remains owned by the async operation");
            NativeMethods.Release(value);
            Close(info);
            Check(result.Disposed, "closing releases the completed result");
            value = 99;
            Check(GetResults(operation.Identity, &value) == unchecked((int)0x80000013) && value == 0, "closed pending query clears GetResults output");
        }
        finally
        {
            start.Set(); Close(info); NativeMethods.Release(callback.Identity); NativeMethods.Release(info); NativeMethods.Release(operation.Identity);
        }
    }

    private static void TestCancellation(bool close, bool throwingRegistration)
    {
        var baseline = NativeObject.LiveCount;
        var operation = new PendingQueryOperation();
        var info = NativeMethods.As(operation.Identity, InfoId);
        var callback = new Completion();
        using var ready = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
        try
        {
            NativeMethods.Check(PutCompleted(operation.Identity, callback.Identity));
            operation.Start((_, token) =>
            {
                using var registration = token.Register(() => { if (throwingRegistration) throw new InvalidOperationException("Expected cancellation callback failure."); });
                ready.Set();
                try
                {
                    if (!token.WaitHandle.WaitOne(5000)) throw new TimeoutException("Cancellation did not arrive.");
                    token.ThrowIfCancellationRequested();
                    return Task.FromResult<nint>(0);
                }
                finally { finished.Set(); }
            });
            Check(ready.Wait(5000), "cancellation test worker is running");
            var hr = ((delegate* unmanaged[Stdcall]<nint, int>)NativeMethods.Table(info)[close ? 10 : 9])(info);
            Check(hr == 0, "Cancel/Close contains cancellation registration exceptions at COM boundary");
            Check(finished.Wait(5000), "Cancel/Close reaches the actual worker token");
            if (close)
            {
                WaitUntil(() => NativeObject.LiveCount <= baseline + 2);
                Check(callback.Calls == 0, "Close suppresses uncommitted completion callback");
                nint value = 99;
                Check(GetResults(operation.Identity, &value) == unchecked((int)0x80000013) && value == 0, "Close during work preserves closed HRESULT");
            }
            else
            {
                WaitUntil(() => Status(info) == 2 && callback.Calls == 1);
                Check(callback.LastStatus == 2, "cancelled operation completes with Canceled status");
                nint value = 99;
                Check(GetResults(operation.Identity, &value) == unchecked((int)0x800704c7) && value == 0, "cancelled GetResults preserves cancellation HRESULT");
            }
        }
        finally { Close(info); NativeMethods.Release(callback.Identity); NativeMethods.Release(info); NativeMethods.Release(operation.Identity); }
    }

    private static void TestReentrantClose()
    {
        var operation = new PendingQueryOperation();
        var info = NativeMethods.As(operation.Identity, InfoId);
        var callback = new Completion { OnInvoke = () => Close(info) };
        try
        {
            NativeMethods.Check(PutCompleted(operation.Identity, callback.Identity));
            operation.Start((_, _) => Task.FromResult<nint>(0));
            WaitUntil(() => callback.Calls == 1);
            Check(callback.CallbackError is null, "completion callback can reenter Close without deadlock or use-after-release");
            nint value = 99;
            Check(GetResults(operation.Identity, &value) == unchecked((int)0x80000013), "reentrant Close disposes the result contract");
        }
        finally { Close(info); NativeMethods.Release(callback.Identity); NativeMethods.Release(info); NativeMethods.Release(operation.Identity); }
    }

    private static void TestFailure()
    {
        var operation = new PendingQueryOperation();
        var info = NativeMethods.As(operation.Identity, InfoId);
        var callback = new Completion();
        try
        {
            NativeMethods.Check(PutCompleted(operation.Identity, callback.Identity));
            operation.Start((_, _) => throw new ArgumentException("Expected test failure."));
            WaitUntil(() => callback.Calls == 1);
            Check(Status(info) == 3 && callback.LastStatus == 3, "worker failure completes as Error");
            nint value = 99;
            Check(GetResults(operation.Identity, &value) == unchecked((int)0x80070057) && value == 0, "worker error preserves original HRESULT");
        }
        finally { Close(info); NativeMethods.Release(callback.Identity); NativeMethods.Release(info); NativeMethods.Release(operation.Identity); }
    }

    private static int Status(nint info) { int status = -1; NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)NativeMethods.Table(info)[7])(info, &status)); return status; }
    private static int PutCompleted(nint operation, nint callback) => ((delegate* unmanaged[Stdcall]<nint, nint, int>)NativeMethods.Table(operation)[8])(operation, callback);
    private static int GetResults(nint operation, nint* value) => ((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(operation)[10])(operation, value);
    private static void Close(nint info) => NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, int>)NativeMethods.Table(info)[10])(info));
    private static void WaitUntil(Func<bool> condition)
    {
        if (!SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(5))) throw new TimeoutException("Pending operation did not reach expected state.");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _passes++; Console.WriteLine("PASS " + message);
    }
    private sealed class DummyResult : NativeObject
    {
        private static readonly nint Vtable = NewTable(6);
        internal bool Disposed;
        internal DummyResult() { AddInterface(Guid.NewGuid(), Vtable); }
        internal override string RuntimeClassName => "Mizon.Tests.PendingResult";
        protected override void ReleaseResources() => Disposed = true;
    }
    private sealed class Completion : NativeObject
    {
        private static readonly nint Vtable = BuildTable();
        internal int Calls, LastStatus;
        internal nint LastOperation;
        internal Action? OnInvoke;
        internal Exception? CallbackError;
        internal Completion() : base(false)
        {
            AddInterface(new Guid("f1cfcc26-9c34-5561-9f97-39c1ee603eda"), Vtable); EnableMarshaling();
        }
        internal override string RuntimeClassName => "Mizon.Tests.PendingCompletion";
        private static nint BuildTable()
        {
            var table = (nint*)NewTable(4, false);
            table[3] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, int, int>)&Invoke;
            return (nint)table;
        }
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int Invoke(nint self, nint operation, int status)
        {
            var owner = Owner<Completion>(self);
            try { owner.LastOperation = operation; owner.LastStatus = status; owner.OnInvoke?.Invoke(); return 0; }
            catch (Exception error) { owner.CallbackError = error; return error.HResult; }
            finally { Interlocked.Increment(ref owner.Calls); }
        }
    }
}
