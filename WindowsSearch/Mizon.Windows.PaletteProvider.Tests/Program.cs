using Mizon.Windows.PaletteProvider;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

internal static unsafe class ProviderAbiTests
{
    static int _passes;
    static readonly Guid AsyncInfoId = new("00000036-0000-0000-C000-000000000046");
    [DllImport("ole32.dll")] static extern int CoMarshalInterThreadInterfaceInStream(in Guid iid, nint instance, out nint stream);
    [DllImport("ole32.dll")] static extern int CoGetInterfaceAndReleaseStream(nint stream, in Guid iid, out nint instance);
    static int Main(string[] args)
    {
        Console.WriteLine($"Mizon Palette ABI tests; process ID={Environment.ProcessId}. These tests do not verify Windows Search UI visibility.");
        NativeMethods.Check(NativeMethods.RoInitialize(1));
        try
        {
            if (args is ["--catalog"]) return CatalogDiagnostics.Run();
            if (args is ["--live-api"]) return LiveApiIntegrationTests.RunAsync().GetAwaiter().GetResult();
            if (args is ["--native-parser"]) return NativeAdaptiveCardParserTests.Run();
            if (args is ["--search-fallback"]) { PaletteSearchFallbackTests.Run(); return 0; }
            if (args is ["--remote-search-fallback"]) { PaletteSearchFallbackTests.RunRemote(); return 0; }
            if (args is ["--demo"]) { PaletteCapabilityDemoTests.Run(); return 0; }
            if (args is ["--remote-demo"]) { PaletteCapabilityDemoTests.RunRemote(); return 0; }
            if (args is ["--export-card-fixtures", var fixtureDirectory, var fixtureImage]) { PaletteResultRenderingTests.ExportFixtures(fixtureDirectory, fixtureImage); return 0; }
            if (args is ["--remote"]) TestRemote();
            else if (args.Length == 0)
            {
                TestFactory(); TestOperations();
                _passes += PaletteResultRenderingTests.Run(); _passes += LiveQueryProgressTests.Run();
                _passes += PendingQueryOperationTests.Run();
                _passes += PaletteSearchRelevanceTests.Run();
                _passes += PaletteSearchFallbackTests.Run();
                _passes += PaletteCapabilityDemoTests.Run();
                
            }
            else throw new ArgumentException("Usage: Mizon.Windows.PaletteProvider.Tests [--remote|--catalog|--live-api|--native-parser|--demo|--remote-demo]");
            Console.WriteLine($"PASS core/preview: {_passes} assertions; menu/presentation totals are reported separately above.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { NativeMethods.RoUninitialize(); }
    }
    static void TestRemote()
    {
        var baseline = NativeObject.LiveCount;
        nint instance = 0;
        NativeMethods.Check(NativeMethods.CoCreateInstance(ProviderClassFactory.ClassId, 0, 4, MusicPaletteExtension.InterfaceId, out instance));
        try
        {
            Check(instance != 0, "remote packaged COM activation returns Palettes extension");
            nint name = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(instance)[4])(instance, &name));
            Console.WriteLine("Remote class: " + NativeMethods.ReadString(name)); NativeMethods.WindowsDeleteString(name);
            uint count = 99; nint* items = null;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint*, nint**, int>)NativeMethods.Table(instance)[8])(instance, &count, &items));
            Check(count == 0, "remote GetItems has no pre-indexed catalogue");
            Marshal.FreeCoTaskMem((nint)items);
            _passes += LiveBackendTests.Run(instance, "st");
            nint operation = 0;
            nint unused = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, nint*, int>)NativeMethods.Table(instance)[10])(instance, 0, &unused, &operation));
            try
            {
                Check(operation != 0, "remote async action crosses process boundary");
                var info = NativeMethods.As(operation, AsyncInfoId);
                try
                {
                    int status = 0;
                    NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)NativeMethods.Table(info)[7])(info, &status));
                    Check(status == 1, "remote IAsyncInfo reports completed");
                    NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, int>)NativeMethods.Table(operation)[8])(operation));
                    var callback = new Callback(action: true);
                    try
                    {
                        NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)NativeMethods.Table(operation)[6])(operation, callback.Identity));
                        Check(callback.Calls == 1 && callback.LastStatus == 1, "remote async completion callback round-trip succeeds");
                    }
                    finally { NativeMethods.Release(callback.Identity); }
                    NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, int>)NativeMethods.Table(info)[10])(info));
                    Check(((delegate* unmanaged[Stdcall]<nint, int>)NativeMethods.Table(operation)[8])(operation) == unchecked((int)0x80000013), "remote closed action preserves HRESULT");
                }
                finally { NativeMethods.Release(info); }
            }
            finally { NativeMethods.Release(operation); }
        }
        finally { NativeMethods.Release(instance); }
        // Initialize intentionally retains the host until the extension itself
        // is released. Allow the out-of-process release notification to arrive.
        for (var retry = 0; retry < 100 && NativeObject.LiveCount != baseline; retry++) Thread.Sleep(20);
        Check(NativeObject.LiveCount == baseline, "remote extension and previews release all local COM callbacks");
    }
    static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL " + name);
        _passes++; Console.WriteLine("PASS " + name);
    }
    static void TestFactory()
    {
        var factory = new ProviderClassFactory();
        var create = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)NativeMethods.Table(factory.Identity)[3];
        Guid iid = MusicPaletteExtension.InterfaceId;
        nint instance = 0;
        Check(create(factory.Identity, 1, &iid, &instance) == unchecked((int)0x80040110) && instance == 0, "factory rejects aggregation");
        var missing = Guid.NewGuid();
        Check(create(factory.Identity, 0, &missing, &instance) == NativeMethods.NoInterface && instance == 0 && MusicPaletteExtension.ActiveCount == 0, "factory rejects unsupported IID without live object leak");
        Check(create(factory.Identity, 0, &iid, &instance) == 0 && instance != 0 && MusicPaletteExtension.ActiveCount == 1, "factory creates extension");
        var identity = NativeMethods.As(instance, NativeMethods.UnknownId);
        var inspectable = NativeMethods.As(instance, NativeMethods.InspectableId);
        Check(identity == instance && inspectable == instance, "extension single controlling identity");
        NativeMethods.Release(identity); NativeMethods.Release(inspectable);
        var marshal = NativeMethods.As(instance, NativeMethods.MarshalId);
        Check(marshal != 0, "extension exposes free-threaded marshaler"); NativeMethods.Release(marshal);
        uint count = 99; nint* items = (nint*)1;
        var getItems = (delegate* unmanaged[Stdcall]<nint, uint*, nint**, int>)NativeMethods.Table(instance)[8];
        Check(getItems(instance, &count, &items) == 0 && count == 0 && items == null, "GetItems returns no static catalogue");
        Check(getItems(instance, null, &items) == NativeMethods.PointerError, "GetItems validates output pointer");
        var initialize = (delegate* unmanaged[Stdcall]<nint, nint, nint, int>)NativeMethods.Table(instance)[6];
        Check(initialize(instance, 0, 0) == NativeMethods.InvalidArgument, "Initialize rejects missing factory/host");
        var execute = (delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(instance)[9];
        nint operation = 1;
        Check(execute(instance, 0, &operation) == NativeMethods.InvalidArgument && operation == 0, "Execute validates query and clears output");
        var missingId = NativeMethods.String("unrecognized-test-id"); nint item = 1;
        var getItem = (delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(instance)[7];
        Check(getItem(instance, missingId, &item) == 0 && item == 0, "GetItem returns no unrelated song");
        NativeMethods.WindowsDeleteString(missingId);
        var childId = NativeMethods.String($"music/{Guid.NewGuid():D}/actions/copy-link");
        try { Check(getItem(instance, childId, &item) == 0 && item == 0, "GetItem does not confuse a menu command with the song"); }
        finally { NativeMethods.WindowsDeleteString(childId); }
        var handler = new Callback();
        long token = 0;
        var add = (delegate* unmanaged[Stdcall]<nint, nint, long*, int>)NativeMethods.Table(instance)[12];
        var remove = (delegate* unmanaged[Stdcall]<nint, long, int>)NativeMethods.Table(instance)[13];
        Check(add(instance, handler.Identity, &token) == 0 && token != 0, "ItemsChanged stores handler");
        NativeMethods.Release(handler.Identity);
        Check(!handler.Disposed && remove(instance, token) == 0 && handler.Disposed, "ItemsChanged removal releases retained handler");
        for (var slot = 10; slot <= 11; slot++)
        {
            var report = (delegate* unmanaged[Stdcall]<nint, uint, nint*, nint*, int>)NativeMethods.Table(instance)[slot];
            operation = 0;
            Check(report(instance, 0, null, &operation) == 0 && operation != 0, $"analytics slot{slot} returns action");
            var action = NativeMethods.As(operation, CompletedOperation.ActionId);
            Check(((delegate* unmanaged[Stdcall]<nint, int>)NativeMethods.Table(action)[8])(action) == 0, $"analytics slot{slot} completes");
            NativeMethods.Release(action); NativeMethods.Release(operation);
        }
        Check(NativeMethods.Release(instance) == 0 && MusicPaletteExtension.ActiveCount == 0, "extension final release decrements active count");
        NativeMethods.Release(factory.Identity);
    }
    static void TestOperations()
    {
        var result = new DummyResult();
        var operation = new CompletedOperation(result.Identity);
        NativeMethods.Release(result.Identity);
        Check(!result.Disposed, "operation retains result");
        var pointer = operation.Identity;
        var info = NativeMethods.As(pointer, AsyncInfoId);
        var identity = NativeMethods.As(info, NativeMethods.UnknownId);
        Check(identity == pointer && info != pointer, "async info tearoff shares controlling identity"); NativeMethods.Release(identity);
        int status = 0;
        Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)NativeMethods.Table(info)[7])(info, &status) == 0 && status == 1, "async status Completed");
        nint returned = 0;
        var getResults = (delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(pointer)[10];
        Check(getResults(pointer, &returned) == 0 && returned == result.Identity, "GetResults returns retained result"); NativeMethods.Release(returned);
        Check(getResults(pointer, null) == NativeMethods.PointerError, "GetResults validates output pointer");
        var handler = new Callback();
        var putCompleted = (delegate* unmanaged[Stdcall]<nint, nint, int>)NativeMethods.Table(pointer)[8];
        Check(putCompleted(pointer, handler.Identity) == 0 && handler.Calls == 1 && handler.LastOperation == pointer && handler.LastStatus == 1, "completed callback fires immediately with correct ABI");
        Check(putCompleted(pointer, handler.Identity) == unchecked((int)0x80000018), "completion handler assignment is one-time");
        returned = 0;
        Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(pointer)[9])(pointer, &returned) == 0 && returned == handler.Identity, "completed getter retains handler"); NativeMethods.Release(returned);
        NativeMethods.Release(handler.Identity);
        Check(!handler.Disposed, "operation retains completion handler");
        NativeMethods.Check(CoMarshalInterThreadInterfaceInStream(CompletedOperation.QueryOperationId, pointer, out var stream));
        Exception? threadError = null;
        var thread = new Thread(() =>
        {
            var hr = NativeMethods.RoInitialize(0);
            try
            {
                NativeMethods.Check(hr);
                NativeMethods.Check(CoGetInterfaceAndReleaseStream(stream, CompletedOperation.QueryOperationId, out var other));
                try
                {
                    nint value = 0;
                    NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(other)[10])(other, &value));
                    NativeMethods.Release(value);
                }
                finally { NativeMethods.Release(other); }
            }
            catch (Exception error) { threadError = error; }
            finally { if (hr >= 0) NativeMethods.RoUninitialize(); }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Check(thread.Join(5000) && threadError == null, "async operation marshals MTA to STA");
        Check(((delegate* unmanaged[Stdcall]<nint, int>)NativeMethods.Table(info)[10])(info) == 0 && handler.Disposed && result.Disposed, "Close releases handler and result");
        returned = 1;
        Check(getResults(pointer, &returned) == unchecked((int)0x80000013) && returned == 0, "GetResults rejects closed operation");
        Check(((delegate* unmanaged[Stdcall]<nint, int>)NativeMethods.Table(info)[10])(info) == 0, "Close is idempotent");
        NativeMethods.Release(info); Check(NativeMethods.Release(pointer) == 0, "operation final release completes");
        var action = new CompletedOperation(action: true); var actionInfo = NativeMethods.As(action.Identity, AsyncInfoId);
        ((delegate* unmanaged[Stdcall]<nint, int>)NativeMethods.Table(actionInfo)[10])(actionInfo);
        Check(((delegate* unmanaged[Stdcall]<nint, int>)NativeMethods.Table(action.Identity)[8])(action.Identity) == unchecked((int)0x80000013), "IAsyncAction.GetResults rejects closed action");
        NativeMethods.Release(actionInfo); NativeMethods.Release(action.Identity);
        var failed = new CompletedOperation(error: NativeMethods.InvalidArgument);
        returned = 1;
        Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(failed.Identity)[10])(failed.Identity, &returned) == NativeMethods.InvalidArgument && returned == 0, "failed operation preserves HRESULT"); NativeMethods.Release(failed.Identity);
        var reentrant = new CompletedOperation(action: true); var reentrantInfo = NativeMethods.As(reentrant.Identity, AsyncInfoId);
        var reentrantHandler = new Callback { OnInvoke = () => ((delegate* unmanaged[Stdcall]<nint, int>)NativeMethods.Table(reentrantInfo)[10])(reentrantInfo) };
        Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)NativeMethods.Table(reentrant.Identity)[6])(reentrant.Identity, reentrantHandler.Identity) == 0 && reentrantHandler.Calls == 1, "completion callback can reenter Close");
        NativeMethods.Release(reentrantHandler.Identity); NativeMethods.Release(reentrantInfo); NativeMethods.Release(reentrant.Identity);
    }
    sealed class DummyResult : NativeObject
    {
        static readonly nint Table = NewTable(6);
        internal bool Disposed;
        internal DummyResult() { AddInterface(Guid.NewGuid(), Table); }
        internal override string RuntimeClassName => "Mizon.Test.Result";
        protected override void ReleaseResources() => Disposed = true;
    }
    sealed class DiagnosticHost : NativeObject
    {
        static readonly nint Table = NewTable(6);
        internal DiagnosticHost() { AddInterface(new("3e789efc-49dd-5670-8458-7fb58c457265"), Table); EnableMarshaling(); }
        internal override string RuntimeClassName => "Mizon.Diagnostic.PaletteExtensionHost";
    }
    sealed class Callback : NativeObject
    {
        static readonly nint Table = BuildTable();
        internal int Calls; internal int LastStatus; internal nint LastOperation; internal bool Disposed; internal Action? OnInvoke;
        internal Callback(bool action = false) : base(false) { AddInterface(action ? new("a4ed5c81-76c9-40bd-8be6-b1d90fb20ae7") : new("f1cfcc26-9c34-5561-9f97-39c1ee603eda"), Table); EnableMarshaling(); }
        internal override string RuntimeClassName => "Mizon.Test.Callback";
        protected override void ReleaseResources() => Disposed = true;
        static nint BuildTable() { var table = (nint*)NewTable(4, false); table[3] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, int, int>)&Invoke; return (nint)table; }
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        static int Invoke(nint self, nint operation, int status)
        {
            var owner = Owner<Callback>(self); owner.Calls++; owner.LastStatus = status; owner.LastOperation = operation; owner.OnInvoke?.Invoke(); return 0;
        }
    }
}
