using Mizon.API.Client.Responses.Search;
using Mizon.Windows.PaletteProvider;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

internal static class PaletteSearchFallbackTests
{
    private static readonly Guid InfoId = new("00000036-0000-0000-C000-000000000046");
    private static readonly Guid MusicId = new("0195dd3c-9a3e-7c4a-8c6f-2e9c0f6b1a72");
    private static int _passes;

    internal static unsafe void RunRemote()
    {
        _passes = 0;
        var baseline = NativeObject.LiveCount;
        const string text = "aflaskjflajsvlakjsv";
        var expected = PaletteSearchFallback.Create(text)!;
        nint extension = 0, factory = 0, queryFactory = 0, query = 0, operation = 0, results = 0, json = 0;
        nint* items = null; uint count = 0;
        var host = new RemoteHost();
        try
        {
            NativeMethods.Check(NativeMethods.CoCreateInstance(ProviderClassFactory.ClassId, 0, 4, MusicPaletteExtension.InterfaceId, out extension));
            factory = NativePaletteTestData.CreateItemFactory();
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)NativeMethods.Table(extension)[6])(extension, factory, host.Identity));
            queryFactory = NativeMethods.Factory("Windows.UI.Palettes.PaletteQuery", new("98f6db9e-99ff-5bc4-84b1-1547927f8830"));
            json = NativeMethods.String("{\"Arguments\":[\"" + text + "\"]}");
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(queryFactory)[6])(queryFactory, json, &query));
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(extension)[9])(extension, query, &operation));
            Check(Wait(operation, 20000) == 1, "installed provider completes the real unmatched search");
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(operation)[10])(operation, &results));
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint*, nint**, int>)NativeMethods.Table(results)[7])(results, &count, &items));
            Check(count == 1, "installed provider returns one fallback action");
            var item = items[0];
            Check(ReadId(item) == expected.Key, "remote fallback carries the exact original query instead of a fabricated music ID");
            Check(PaletteResultRenderingTests.ReadString(item, 10) == PaletteSearchFallback.Title, "remote fallback title is جستجو در میزون");
            nint icon = 0, invoker = 0;
            try
            {
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(item)[8])(item, &icon));
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(item)[21])(item, &invoker));
                Check(icon != 0, "installed package supplies its Mizon logo for the fallback");
                Check(invoker != 0, "remote fallback has a native activation command (not executed)");
            }
            finally { NativeMethods.Release(invoker); NativeMethods.Release(icon); }
        }
        finally
        {
            if (items != null) for (var i = 0; i < count; i++) NativeMethods.Release(items[i]);
            Marshal.FreeCoTaskMem((nint)items);
            NativeMethods.Release(results);
            if (operation != 0) ReleaseOperation(operation);
            NativeMethods.Release(query); NativeMethods.Release(queryFactory); NativeMethods.WindowsDeleteString(json);
            NativeMethods.Release(factory); NativeMethods.Release(extension); NativeMethods.Release(host.Identity);
        }
        for (var retry = 0; retry < 100 && NativeObject.LiveCount != baseline; retry++) Thread.Sleep(20);
        Check(NativeObject.LiveCount == baseline, "remote fallback releases its local native host callback");
        Console.WriteLine($"PASS {_passes} installed fallback assertions. App launch and Search UI visibility were not exercised.");
    }

    private sealed class RemoteHost : NativeObject
    {
        private static readonly nint Table = NewTable(6);
        internal RemoteHost() { AddInterface(new("3e789efc-49dd-5670-8458-7fb58c457265"), Table); EnableMarshaling(); }
        internal override string RuntimeClassName => "Mizon.SearchFallbackTests.Host";
    }

    internal static unsafe int Run()
    {
        _passes = 0;
        var baseline = NativeObject.LiveCount;
        var factory = NativePaletteTestData.CreateItemFactory();
        var query = NativeMethods.Activate("Windows.UI.Palettes.PaletteQuery");
        try
        {
            foreach (var text in new[] { "aflaskjflajsvlakjsv", "معین & پاپ + #? 🎵" })
            {
                var fallback = PaletteSearchFallback.Create(text)!;
                Check(PaletteSearchFallback.FromKey(fallback.Key) == fallback, "fallback identity round-trips reserved characters");
                Check(Uri.UnescapeDataString(new Uri(fallback.LaunchUri).Query[7..]) == text, "fallback activation preserves exact query");
                using var session = new PaletteSearchSession(factory, (q, ct) => Source("empty", ct));
                var ids = Complete(session.Start(query, text));
                Check(ids.SequenceEqual(new[] { fallback.Key }), "empty search returns exactly the Mizon search action");
                var restored = session.GetItem(ids[0]);
                try { Check(restored != 0 && ReadId(restored) == ids[0], "Windows can restore a recent fallback by its query key"); }
                finally { NativeMethods.Release(restored); }
            }
            foreach (var mode in new[] { "failure", "login", "timeout", "one", "partial" })
            {
                using var session = new PaletteSearchSession(factory, (q, ct) => Source(mode, ct), TimeSpan.FromMilliseconds(100));
                var ids = Complete(session.Start(query, "sample"));
                Check(ids.SequenceEqual(new[] { mode is "one" or "partial" ? "music/" + MusicId : "search/sample" }),
                    "search fallback policy: " + mode);
            }
            using (var session = new PaletteSearchSession(factory, (q, ct) => Source("timeout", ct), TimeSpan.FromSeconds(2)))
            {
                var old = session.Start(query, "superseded");
                var latest = session.Start(query, "");
                try { Check(Wait(old) == 2, "superseded query cancels instead of emitting a stale fallback"); }
                finally { ReleaseOperation(old); }
                Check(Complete(latest).Length == 0, "empty input emits no fallback");
            }
            foreach (var invalid in new[] { "search/", "search/%00", "search/a/b", "music/test", "search/%20" })
                Check(PaletteSearchFallback.FromKey(invalid) is null, "invalid fallback key rejected: " + invalid);
        }
        finally { NativeMethods.Release(query); NativeMethods.Release(factory); }
        for (var retry = 0; retry < 100 && NativeObject.LiveCount != baseline; retry++) Thread.Sleep(10);
        Check(NativeObject.LiveCount == baseline, "fallback sessions release native references and cancelled operations");
        Console.WriteLine($"PASS {_passes} search fallback assertions; no API request or app activation performed.");
        return _passes;
    }

    private static async IAsyncEnumerable<GlobalSearchItemResponse> Source(string mode, [EnumeratorCancellation] CancellationToken token)
    {
        await Task.Yield();
        if (mode == "login") throw new PaletteLoginRequiredException();
        if (mode == "failure") throw new HttpRequestException("fixture");
        if (mode == "timeout") await Task.Delay(Timeout.InfiniteTimeSpan, token);
        if (mode is "one" or "partial") yield return new() { ItemType = "Music", PublicId = MusicId, Title = "sample" };
        if (mode == "partial") throw new HttpRequestException("fixture after result");
    }

    private static unsafe string[] Complete(nint operation)
    {
        nint results = 0; uint count = 0; nint* items = null;
        try
        {
            Check(Wait(operation) == 1, "native search operation completes");
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(operation)[10])(operation, &results));
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint*, nint**, int>)NativeMethods.Table(results)[7])(results, &count, &items));
            var ids = new string[count];
            for (var i = 0; i < count; i++) ids[i] = ReadId(items[i]);
            return ids;
        }
        finally
        {
            if (items != null) for (var i = 0; i < count; i++) NativeMethods.Release(items[i]);
            Marshal.FreeCoTaskMem((nint)items);
            NativeMethods.Release(results); ReleaseOperation(operation);
        }
    }

    private static unsafe int Wait(nint operation, int timeout = 5000)
    {
        var info = NativeMethods.As(operation, InfoId);
        try
        {
            var deadline = Environment.TickCount64 + timeout;
            int status = 0;
            do
            {
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)NativeMethods.Table(info)[7])(info, &status));
                if (status != 0) return status;
                Thread.Sleep(10);
            } while (Environment.TickCount64 < deadline);
            throw new TimeoutException("Native search fixture did not complete.");
        }
        finally { NativeMethods.Release(info); }
    }

    private static unsafe string ReadId(nint item)
    {
        nint id = 0;
        try
        {
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(item)[6])(item, &id));
            return NativeMethods.ReadString(id);
        }
        finally { NativeMethods.WindowsDeleteString(id); }
    }

    private static unsafe void ReleaseOperation(nint operation)
    {
        var info = NativeMethods.As(operation, InfoId);
        try { NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, int>)NativeMethods.Table(info)[10])(info)); }
        finally { NativeMethods.Release(info); NativeMethods.Release(operation); }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        _passes++; Console.WriteLine("PASS " + name);
    }
}
