using Mizon.Windows.PaletteProvider;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

internal static unsafe class LiveBackendTests
{
    private static int _previewChecks;
    internal static int Run(nint extension, string text)
    {
        _previewChecks = 0;
        var factory = NativePaletteTestData.CreateItemFactory();
        var host = new Host(); var progress = new ProgressHandler();
        nint queryFactory=0, query=0, operation=0, results=0, info=0, json=0;
        try
        {
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint,nint,nint,int>)NativeMethods.Table(extension)[6])(extension,factory,host.Identity));
            queryFactory=NativeMethods.Factory("Windows.UI.Palettes.PaletteQuery",new("98f6db9e-99ff-5bc4-84b1-1547927f8830"));
            json=NativeMethods.String("{\"Arguments\":[\""+text+"\"]}");
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint,nint,nint*,int>)NativeMethods.Table(queryFactory)[6])(queryFactory,json,&query));
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint,nint,nint*,int>)NativeMethods.Table(extension)[9])(extension,query,&operation));
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint,nint,int>)NativeMethods.Table(operation)[6])(operation,progress.Identity));
            info=NativeMethods.As(operation,new("00000036-0000-0000-C000-000000000046"));
            int status=0; var deadline=Environment.TickCount64+20000;
            while(status==0 && Environment.TickCount64<deadline)
            {
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint,int*,int>)NativeMethods.Table(info)[7])(info,&status));
                if(status==0)Thread.Sleep(20);
            }
            if(status!=1)throw new InvalidOperationException($"Live search terminal status={status}.");
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint,nint*,int>)NativeMethods.Table(operation)[10])(operation,&results));
            var ids=ReadItems(results,7);
            if(ids.Length<2 || ids.Length>20)throw new InvalidOperationException("Expected mixed backend results; authenticate Mizon and start the backend first.");
            if(!ids.Any(id=>id.StartsWith("music/")) || !ids.Any(id=>id.StartsWith("client/")))
                throw new InvalidOperationException("The live query did not return both Music and Client records.");
            if(ids.Distinct().Count()!=ids.Length)throw new InvalidOperationException("Duplicate search result IDs.");
            lock(progress.Gate)
            {
                if(progress.Counts.Count<2 || !progress.Counts.SequenceEqual(progress.Counts.Order()))
                    throw new InvalidOperationException("Expected cumulative native streaming progress.");
                if(progress.Counts[^1]!=ids.Length)throw new InvalidOperationException("Final results differ from progress snapshot.");
                Console.WriteLine($"PASS live backend '{text}': {ids.Length} results, both types, {progress.Counts.Count} cumulative progress events, no duplicates.");
            }
            InspectRealPreviews(results);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint,int>)NativeMethods.Table(info)[10])(info));
        }
        finally
        {
            if (info != 0) ((delegate* unmanaged[Stdcall]<nint,int>)NativeMethods.Table(info)[10])(info);
            NativeMethods.Release(info); NativeMethods.Release(results); NativeMethods.Release(operation);
            NativeMethods.Release(query); NativeMethods.Release(queryFactory); NativeMethods.WindowsDeleteString(json);
            NativeMethods.Release(progress.Identity); NativeMethods.Release(host.Identity); NativeMethods.Release(factory);
        }
        return 5 + _previewChecks;
    }

    private static void InspectRealPreviews(nint results)
    {
        uint count = 0; nint* views = null;
        NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint,uint*,nint**,int>)NativeMethods.Table(results)[7])(results, &count, &views));
        try
        {
            nint music = 0, client = 0;
            for (var index = 0; index < count; index++)
            {
                var key = PaletteResultRenderingTests.ReadString(views[index], 6);
                if (key.StartsWith("music/", StringComparison.Ordinal) && music == 0) music = views[index];
                if (key.StartsWith("client/", StringComparison.Ordinal) && client == 0) client = views[index];
            }
            if (music == 0 || client == 0) throw new InvalidOperationException("Both result types are required for the remote preview test.");
            InspectRealPreview(music, PaletteResultKind.Music);
            InspectRealPreview(client, PaletteResultKind.Client);
        }
        finally
        {
            for (var index = 0; index < count; index++) NativeMethods.Release(views[index]);
            Marshal.FreeCoTaskMem((nint)views);
        }
    }

    private static void InspectRealPreview(nint item, PaletteResultKind kind)
    {
        var key = PaletteResultRenderingTests.ReadString(item, 6);
        var id = Guid.Parse(key[(key.IndexOf('/') + 1)..]);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var api = new PaletteApiSession(PaletteApiSession.RequireAuthentication());
        // Read the same public API independently to establish which fields and
        // image actually exist. This does not launch playback or download audio.
        var (expected, mediaId, canDownload) = api.DetailsAsync(new() { Kind = kind, PublicId = id }, cancellation.Token).GetAwaiter().GetResult();
        nint content = 0, template = 0, menuQuery = 0;
        PreviewChanged? callback = null;
        long subscription = 0;
        try
        {
            byte available = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint,byte*,int>)NativeMethods.Table(item)[16])(item, &available));
            Check(available == 1, $"remote {kind} result exposes preview content");
            content = PaletteResultRenderingTests.GetNativePreview(item);
            template = NativeMethods.As(content, PalettePreviewData.TemplateViewId);
            using (var schema = JsonDocument.Parse(PaletteResultRenderingTests.ReadString(template, 6)))
            {
                Check(schema.RootElement.GetProperty("type").GetString() == "AdaptiveCard" && schema.RootElement.GetProperty("rtl").GetBoolean(), $"remote {kind} preview carries native RTL card JSON");
                Check(PaletteResultRenderingTests.VisibilityValuesAreBoolean(schema.RootElement), $"remote {kind} preview supplies typed visibility accepted by the installed renderer");
            }
            callback = new PreviewChanged();
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint,nint,long*,int>)NativeMethods.Table(template)[9])(template, callback.Identity, &subscription));

            using var data = WaitForData(template, root =>
                root.GetProperty("title").GetString() == expected.DisplayTitle &&
                (kind != PaletteResultKind.Music || (root.GetProperty("hasLikes").GetBoolean() && root.GetProperty("hasViews").GetBoolean())) &&
                (!mediaId.HasValue || root.GetProperty("hasImage").GetBoolean()), cancellation.Token);
            var fields = data.RootElement;
            Check(!string.IsNullOrWhiteSpace(fields.GetProperty("title").GetString()) && fields.GetProperty("title").GetString() == expected.DisplayTitle, $"remote {kind} preview title matches the real detail API");
            Check(fields.GetProperty("secondaryTitle").GetString() == expected.SecondaryTitle, $"remote {kind} preview preserves the real alternate title/username");
            Check(fields.GetProperty("canDownload").GetBoolean() == canDownload, $"remote {kind} download capability matches its real file metadata");
            Check(!fields.GetProperty("hasProgress").GetBoolean(), $"remote {kind} preview has no simulated transfer progress");
            if (kind == PaletteResultKind.Music)
            {
                Check(!string.IsNullOrWhiteSpace(fields.GetProperty("likesText").GetString()) && !string.IsNullOrWhiteSpace(fields.GetProperty("viewsText").GetString()), "remote music preview is enriched with actual counts");
                Check(fields.GetProperty("albumTitle").GetString() == (expected.AlbumTitle ?? string.Empty), "remote music album comes from the detail API");
            }
            else
            {
                Check(fields.GetProperty("firstName").GetString() == (expected.FirstName?.Trim() ?? string.Empty) && fields.GetProperty("lastName").GetString() == (expected.LastName?.Trim() ?? string.Empty), "remote profile keeps first/last names as real separate fields");
            }

            if (mediaId.HasValue)
            {
                var imageUri = new Uri(fields.GetProperty("imageUri").GetString()!);
                Check(imageUri.IsFile && File.Exists(imageUri.LocalPath) && new FileInfo(imageUri.LocalPath).Length > 0, $"remote {kind} preview resolves actual downloaded image bytes");
                Check(ReadRowImage(item) == imageUri.AbsoluteUri, $"remote {kind} native row icon matches its loaded preview image");
                Check(PaletteResultRenderingTests.ReadOverlayGlyph(item) == (kind == PaletteResultKind.Music ? "\uE189" : "\uE77B"), $"remote {kind} native row includes the correct type badge");
            }
            else Console.WriteLine($"INFO real {kind} has no media; image checks are not fabricated.");

            menuQuery = NativeMethods.Activate("Windows.UI.Palettes.PaletteQuery");
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint,int,int>)NativeMethods.Table(menuQuery)[22])(menuQuery, 2));
            var menu = PaletteResultRenderingTests.QueryMenu(item, menuQuery);
            try
            {
                Check(menu.Length == (kind == PaletteResultKind.Music ? canDownload ? 5 : 4 : 3), $"remote {kind} context menu reflects actual capabilities");
                Check(PaletteResultRenderingTests.ReadString(menu[0], 10) == (kind == PaletteResultKind.Music ? "پخش در میزون" : "مشاهدهٔ پروفایل"), $"remote {kind} primary context command is correct");
                Check(PaletteResultRenderingTests.ReadString(menu[^1], 10) == "کپی کردن", $"remote {kind} exposes the native copy submenu");
                var children = PaletteResultRenderingTests.QueryMenu(menu[^1], menuQuery);
                try
                {
                    var hasUsername = kind == PaletteResultKind.Client && !string.IsNullOrWhiteSpace(expected.Username);
                    Check(children.Length == (hasUsername ? 3 : 2), $"remote {kind} submenu only offers available text fields");
                    var expectedCommands = hasUsername ? new[] { "copy-link", "copy-title", "copy-username" } : new[] { "copy-link", "copy-title" };
                    for (var index = 0; index < children.Length; index++)
                    {
                        Check(PaletteResultRenderingTests.ReadString(children[index], 6) == $"{key}/actions/{expectedCommands[index]}", $"remote {kind} child command retains the selected result identity");
                        nint invoker = 0;
                        try
                        {
                            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint,nint*,int>)NativeMethods.Table(children[index])[21])(children[index], &invoker));
                            Check(invoker != 0, $"remote {kind} child command has a native invoker (not executed)");
                        }
                        finally { NativeMethods.Release(invoker); }
                    }
                }
                finally { foreach (var child in children) NativeMethods.Release(child); }
            }
            finally { foreach (var entry in menu) NativeMethods.Release(entry); }

            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint,long,int>)NativeMethods.Table(template)[10])(template, subscription));
            subscription = 0;
            Check(callback.LastProperty is null or "DataJson", $"remote {kind} preview property callbacks use the verified DataJson contract");
            Console.WriteLine($"Remote {kind} preview observed {callback.Calls} data notifications; zero is valid when cached details arrived before subscription.");
        }
        finally
        {
            if (subscription != 0) ((delegate* unmanaged[Stdcall]<nint,long,int>)NativeMethods.Table(template)[10])(template, subscription);
            NativeMethods.Release(menuQuery); NativeMethods.Release(template); NativeMethods.Release(content);
            if (callback is not null) NativeMethods.Release(callback.Identity);
        }
    }

    private static JsonDocument WaitForData(nint template, Func<JsonElement,bool> ready, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var data = JsonDocument.Parse(PaletteResultRenderingTests.ReadString(template, 7));
            if (ready(data.RootElement)) return data;
            data.Dispose();
            Thread.Sleep(40);
        }
    }

    private static string? ReadRowImage(nint view)
    {
        nint icon = 0, uri = 0;
        try
        {
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint,nint*,int>)NativeMethods.Table(view)[8])(view, &icon));
            if (icon == 0) return null;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint,nint*,int>)NativeMethods.Table(icon)[6])(icon, &uri));
            return uri == 0 ? null : PaletteResultRenderingTests.ReadString(uri, 6);
        }
        finally { NativeMethods.Release(uri); NativeMethods.Release(icon); }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        _previewChecks++;
        Console.WriteLine("PASS " + description);
    }

    private sealed class PreviewChanged : NativeObject
    {
        private static readonly nint Vtable = Create();
        private int _calls;
        private string? _lastProperty;
        internal int Calls => Volatile.Read(ref _calls);
        internal string? LastProperty => Volatile.Read(ref _lastProperty);
        internal PreviewChanged() : base(false)
        {
            try { AddInterface(new("5a7722ed-bf18-5bb9-a6cb-3950bc80a0a8"), Vtable); EnableMarshaling(); }
            catch { Dispose(); throw; }
        }
        internal override string RuntimeClassName => "Mizon.RemotePreviewTest.PropertyChanged";
        private static nint Create()
        {
            var table = (nint*)NewTable(4, false);
            table[3] = (nint)(delegate* unmanaged[Stdcall]<nint,nint,nint,int>)&Invoke;
            return (nint)table;
        }
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int Invoke(nint self, nint sender, nint args)
        {
            try
            {
                var owner = Owner<PreviewChanged>(self);
                Volatile.Write(ref owner._lastProperty, PaletteResultRenderingTests.ReadString(args, 6));
                Interlocked.Increment(ref owner._calls);
                return 0;
            }
            catch (Exception error) { return Log.Error("Remote preview callback", error); }
        }
    }
    private static string[] ReadItems(nint source,int slot)
    {
        uint count=0; nint* items=null;
        NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint,uint*,nint**,int>)NativeMethods.Table(source)[slot])(source,&count,&items));
        try
        {
            var ids=new string[count];
            for(var i=0;i<count;i++)
            {
                nint id=0;
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint,nint*,int>)NativeMethods.Table(items[i])[6])(items[i],&id));
                try { ids[i]=NativeMethods.ReadString(id); } finally { NativeMethods.WindowsDeleteString(id); }
            }
            return ids;
        }
        finally { for(var i=0;i<count;i++)NativeMethods.Release(items[i]); Marshal.FreeCoTaskMem((nint)items); }
    }
    private sealed class Host:NativeObject
    {
        private static readonly nint Table=NewTable(6);
        internal Host(){AddInterface(new("3e789efc-49dd-5670-8458-7fb58c457265"),Table);EnableMarshaling();}
        internal override string RuntimeClassName=>"Mizon.LiveBackendTest.Host";
    }
    private sealed class ProgressHandler:NativeObject
    {
        private static readonly nint Table=Create();
        internal readonly Lock Gate=new(); internal readonly List<int> Counts=[];
        internal ProgressHandler():base(false){AddInterface(new("98e908d6-41e7-5f3a-af92-1391b1ac329c"),Table);EnableMarshaling();}
        internal override string RuntimeClassName=>"Mizon.LiveBackendTest.Progress";
        private static nint Create(){var table=(nint*)NewTable(4,false);table[3]=(nint)(delegate* unmanaged[Stdcall]<nint,nint,nint,int>)&Invoke;return(nint)table;}
        [UnmanagedCallersOnly(CallConvs=[typeof(CallConvStdcall)])]
        private static int Invoke(nint self,nint operation,nint progress)
        {
            try { var owner=Owner<ProgressHandler>(self); var ids=ReadItems(progress,6); lock(owner.Gate)owner.Counts.Add(ids.Length); return 0; }
            catch(Exception error){return Log.Error("Live backend progress test",error);}
        }
    }
}
