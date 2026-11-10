using Mizon.Windows.PaletteProvider;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

internal static unsafe class LiveQueryProgressTests
{
    private static readonly Guid ProgressId = new("ee7066df-9701-5950-b0b4-ab5335cd423c");
    private static readonly Guid InfoId = new("00000036-0000-0000-C000-000000000046");
    private static int _passes;

    internal static int Run()
    {
        _passes = 0;
        nint factory = 0, first = 0, second = 0, query = 0, info = 0;
        PendingQueryOperation? operation = null;
        ProgressCallback? callback = null;
        var baseline = NativeObject.LiveCount;
        try
        {
            factory = NativePaletteTestData.CreateItemFactory();
            first = CreateItem(factory, "progress-first");
            second = CreateItem(factory, "progress-second");
            var empty = PaletteData.BuildProgress([]);
            try { Check(ReadIds(empty, progress: true).Length == 0, "native progress factory accepts empty items and null state"); }
            finally { NativeMethods.Release(empty); }
            var batch = PaletteData.BuildProgress([first, second]);
            try
            {
                Check(ReadIds(batch, progress: true).SequenceEqual(new[] { "progress-first", "progress-second" }), "native progress retains ordered item identities");
                nint typed = 0, state = 0;
                try
                {
                    typed = NativeMethods.As(batch, ProgressId);
                    NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(typed)[7])(typed, &state));
                    Check(state == 0, "native query progress supports items without a percentage state");
                }
                finally { NativeMethods.Release(state); NativeMethods.Release(typed); }
            }
            finally { NativeMethods.Release(batch); }

            query = NativeMethods.Activate("Windows.UI.Palettes.PaletteQuery");
            operation = new PendingQueryOperation();
            callback = new ProgressCallback();
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)NativeMethods.Table(operation.Identity)[6])(operation.Identity, callback.Identity));
            var mainThread = Environment.CurrentManagedThreadId;
            var item1 = first; var item2 = second; var inputQuery = query;
            operation.Start((pending, cancellation) =>
            {
                var initialized = NativeMethods.RoInitialize(1);
                NativeMethods.Check(initialized);
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    var one = PaletteData.BuildProgress([item1]);
                    try { pending.Report(one); }
                    finally { NativeMethods.Release(one); }
                    var two = PaletteData.BuildProgress([item1, item2]);
                    try { pending.Report(two); }
                    finally { NativeMethods.Release(two); }
                    return Task.FromResult(PaletteData.BuildResults(inputQuery, [item1, item2]));
                }
                finally { NativeMethods.RoUninitialize(); }
            });
            info = NativeMethods.As(operation.Identity, InfoId);
            var deadline = Environment.TickCount64 + 5000;
            int status = 0;
            do
            {
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)NativeMethods.Table(info)[7])(info, &status));
                if (status == 0) Thread.Sleep(10);
            } while (status == 0 && Environment.TickCount64 < deadline);
            Check(status == 1, "asynchronous progress producer completes successfully");
            if (callback.Failure is { } failure) throw new InvalidOperationException("Native progress callback failed.", failure);
            Check(callback.Batches.Count == 2, "real WinRT progress callback receives both reports");
            Check(callback.Batches[0].SequenceEqual(new[] { "progress-first" }) &&
                callback.Batches[1].SequenceEqual(new[] { "progress-first", "progress-second" }), "callback reads growing native snapshots without reordering");
            Check(callback.Operation == operation.Identity, "progress callback identifies its originating operation");
            Check(callback.ThreadId != mainThread, "progress callback is delivered from asynchronous worker");
            nint results = 0;
            try
            {
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(operation.Identity)[10])(operation.Identity, &results));
                Check(ReadIds(results, progress: false).SequenceEqual(callback.Batches[1]), "final native results match the last progressive snapshot exactly");
            }
            finally { NativeMethods.Release(results); }
        }
        finally
        {
            if (info != 0) ((delegate* unmanaged[Stdcall]<nint, int>)NativeMethods.Table(info)[10])(info);
            NativeMethods.Release(info);
            if (operation is not null) NativeMethods.Release(operation.Identity);
            if (callback is not null) NativeMethods.Release(callback.Identity);
            NativeMethods.Release(query); NativeMethods.Release(second); NativeMethods.Release(first); NativeMethods.Release(factory);
        }
        for (var index = 0; index < 50 && NativeObject.LiveCount != baseline; index++) Thread.Sleep(10);
        Check(NativeObject.LiveCount == baseline, "progress operation and callback release all owned references");
        Console.WriteLine($"Live progress PASS {_passes} assertions; host deduplication requires separate RunUX evidence.");
        return _passes;
    }

    private static nint CreateItem(nint factory, string id)
    {
        var text = NativeMethods.String(id); nint item = 0;
        try
        {
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(factory)[7])(factory, text, &item));
            nint view = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(item)[19])(item, &view));
            return view;
        }
        finally { NativeMethods.Release(item); NativeMethods.WindowsDeleteString(text); }
    }

    private static string[] ReadIds(nint source, bool progress)
    {
        nint typed = 0; uint count = 0; nint* items = null;
        try
        {
            typed = progress ? NativeMethods.As(source, ProgressId) : source;
            if (!progress) NativeMethods.AddRef(typed);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint*, nint**, int>)NativeMethods.Table(typed)[progress ? 6 : 7])(typed, &count, &items));
            var result = new string[count];
            for (var index = 0; index < count; index++)
            {
                nint id = 0;
                try
                {
                    NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(items[index])[6])(items[index], &id));
                    result[index] = NativeMethods.ReadString(id);
                }
                finally { NativeMethods.WindowsDeleteString(id); }
            }
            return result;
        }
        finally
        {
            if (items != null) for (var index = 0; index < count; index++) NativeMethods.Release(items[index]);
            Marshal.FreeCoTaskMem((nint)items); NativeMethods.Release(typed);
        }
    }

    private sealed class ProgressCallback : NativeObject
    {
        private static readonly nint Vtable = CreateTable();
        internal readonly List<string[]> Batches = [];
        internal nint Operation;
        internal int ThreadId;
        internal Exception? Failure;
        internal ProgressCallback() : base(inspectable: false)
        {
            AddInterface(new("98e908d6-41e7-5f3a-af92-1391b1ac329c"), Vtable);
            EnableMarshaling();
        }
        internal override string RuntimeClassName => "Mizon.LiveQueryProgressTests.Callback";
        private static nint CreateTable()
        {
            var table = (nint*)NewTable(4, inspectable: false);
            table[3] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, nint, int>)&Invoke;
            return (nint)table;
        }
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int Invoke(nint self, nint operation, nint progress)
        {
            var owner = Owner<ProgressCallback>(self);
            try
            {
                owner.Operation = operation;
                owner.ThreadId = Environment.CurrentManagedThreadId;
                owner.Batches.Add(ReadIds(progress, progress: true));
                return 0;
            }
            catch (Exception error) { owner.Failure = error; return error.HResult; }
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        _passes++; Console.WriteLine("PASS " + description);
    }
}
