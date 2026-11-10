using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Mizon.Windows.PaletteProvider;

// No network, application launch or clipboard mutation: actions terminate at
// this fixture boundary. The production renderers and native facades are used.
internal static unsafe class PaletteResultRenderingTests
{
    private static int _checks;
    private static readonly Guid AsyncInfoId = new("00000036-0000-0000-C000-000000000046");

    internal static void ExportFixtures(string directory, string imagePath)
    {
        Directory.CreateDirectory(directory);
        var image = new Uri(Path.GetFullPath(imagePath)).AbsoluteUri;
        var music = new PaletteResultData { Kind = PaletteResultKind.Music, PublicId = Guid.NewGuid(), Title = "Test music", PersianTitle = "آهنگ فارسی", LikesCount = 125, ViewsCount = 987, AlbumTitle = "آلبوم", GenreText = "پاپ", DurationText = "03:30" };
        var client = new PaletteResultData { Kind = PaletteResultKind.Client, PublicId = Guid.NewGuid(), FirstName = "علی", LastName = "اسماعیلی", NickName = "نام نمایشی", Username = "card_fixture" };
        Save("music", music, new(image, false, null, null, true));
        Save("music-progress", music, new(image, true, 42, "در حال دانلود", true));
        Save("client", client, new(image, false, null, null, false));
        void Save(string name, PaletteResultData result, PaletteResultSnapshot snapshot)
        {
            var data = PalettePreviewData.BuildData(result, snapshot);
            File.WriteAllText(Path.Combine(directory, name + "-data.json"), data);
            File.WriteAllText(Path.Combine(directory, name + ".json"), PaletteCardTemplate.Expand(PalettePreviewData.GetTemplate(result.Kind), data));
        }
    }

    internal static int Run()
    {
        _checks = 0;
        var baseline = NativeObject.LiveCount;
        Exercise(new PaletteResultData { Kind = PaletteResultKind.Music, PublicId = Guid.NewGuid(), Title = "A \"song\" <title>", PersianTitle = "آهنگ فارسی" });
        Exercise(new PaletteResultData { Kind = PaletteResultKind.Client, PublicId = Guid.NewGuid(), Username = "alish", FirstName = "علی", LastName = "اسماعیلی", NickName = "نام نمایشی" });
        var missing = new PaletteResultData { Kind = PaletteResultKind.Client, PublicId = Guid.NewGuid() };
        using (var data = JsonDocument.Parse(PalettePreviewData.BuildData(missing, new("javascript:bad()", false, null, null, false))))
        {
            Check(data.RootElement.GetProperty("hasNoImage").GetBoolean(), "invalid image scheme is not sent to Windows");
            Check(!data.RootElement.GetProperty("hasNames").GetBoolean(), "absent profile fields are hidden");
            Check(!data.RootElement.GetProperty("hasSecondaryTitle").GetBoolean(), "absent username is hidden");
        }
        Check(NativeObject.LiveCount == baseline, "result preview/menu/presentation objects release all native callbacks");
        Console.WriteLine($"Result rendering PASS {_checks} assertions; native contracts tested, host visual layout remains a separate check.");
        return _checks;
    }

    private static void Exercise(PaletteResultData result)
    {
        var state = new FixtureState(result);
        nint factory = 0, item = 0, view = 0, content = 0, contentView = 0, template = 0, query = 0;
        IDisposable? presentation = null, preview = null;
        ChangedCallback? callback = null;
        long token = 0;
        try
        {
            factory = NativePaletteTestData.CreateItemFactory();
            var id = NativeMethods.String(result.Key);
            try { NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(factory)[7])(factory, id, &item)); }
            finally { NativeMethods.WindowsDeleteString(id); }
            PaletteMenuData.Attach(item, state);
            presentation = PalettePresentation.AttachToItem(item, state);
            preview = PalettePreviewData.Attach(item, state);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(item)[19])(item, &view));
            Check(ReadOverlayGlyph(view) is null, "fallback row glyph has no redundant overlay");
            Check(ReadBooleanProperty(view, "ProminentFirstSubtitleElement"), "native action subtitle receives boxed Boolean prominence");
            Check(ReadBooleanProperty(view, "ExcludeFromHistory"), "live entity is excluded from unrelated relative-time history");
            Check(ReadPropertyString(view, "SubtitleGlyph") == (result.Kind == PaletteResultKind.Music ? "\uE768" : "\uE77B"), "native action subtitle has the correct custom glyph");
            var subtitles = ReadSubtitles(view);
            Check(subtitles.Length == 2 && subtitles[0] == (result.Kind == PaletteResultKind.Music ? "پخش آهنگ" : "مشاهدهٔ پروفایل"), "action label is a separate rich-subtitle element from the entity description");

            contentView = GetNativePreview(view);
            Check(state.DetailsRequests == 1, "selection requests lazy details through the native content callback");
            template = NativeMethods.As(contentView, PalettePreviewData.TemplateViewId);
            using (var schema = JsonDocument.Parse(ReadString(template, 6)))
            {
                Check(schema.RootElement.GetProperty("rtl").GetBoolean(), "card uses RTL layout");
                Check(VisibilityValuesAreBoolean(schema.RootElement), "native card receives boolean visibility values instead of template strings");
                Check(!ContainsProperty(schema.RootElement, "style", "emphasis"), "content insets do not introduce an opaque emphasis surface");
                Check(!ContainsProperty(schema.RootElement, "style", "positive"), "primary buttons use the host's normal themed colors");
                Check(!ReadString(template, 6).Contains("آزمایشی", StringComparison.Ordinal), "production card contains no demo actions");
            }
            using (var data = JsonDocument.Parse(ReadString(template, 7)))
            {
                Check(data.RootElement.GetProperty("title").GetString() == result.DisplayTitle, "title preserves real result data");
                Check(!data.RootElement.TryGetProperty("likeCount", out _), "card does not manufacture counters");
                Check(!data.RootElement.GetProperty("hasProgress").GetBoolean(), "idle result has no artificial progress");
            }
            callback = new ChangedCallback();
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)NativeMethods.Table(template)[9])(template, callback.Identity, &token));
            state.Update(new("file:///C:/Mizon/test-image.png", false, null, null, result.Kind == PaletteResultKind.Music));
            Check(callback.Calls == 2 && callback.Names.SequenceEqual(["TemplateJson", "DataJson"]), "real state changes notify the expanded template before its data");
            using (var data = JsonDocument.Parse(ReadString(template, 7)))
                Check(data.RootElement.GetProperty("hasImage").GetBoolean(), "loaded media updates preview data");
            Check(ReadOverlayGlyph(view) == (result.Kind == PaletteResultKind.Music ? "\uE189" : "\uE77B"), "loaded row image receives its native music/profile type badge");
            using (var expanded = JsonDocument.Parse(ReadString(template, 6)))
            {
                Check(ContainsProperty(expanded.RootElement, "url", "file:///C:/Mizon/test-image.png"), "native template contains the actual loaded image URI");
                Check(ContainsProperty(expanded.RootElement, "style", "Person"), "the renderer rounds the unmodified source photo using Person style");
            }

            InvokePreview(template, result.Kind == PaletteResultKind.Music ? "play" : "profile");
            Check(state.LastAction == (result.Kind == PaletteResultKind.Music ? "play" : "profile"), "preview action reaches the real action boundary");
            InvokePreview(template, "copy-link");
            Check(state.LastAction == "copy-link", "copy-link reaches action boundary without writing clipboard in tests");

            query = NativeMethods.Activate("Windows.UI.Palettes.PaletteQuery");
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, int, int>)NativeMethods.Table(query)[22])(query, 2));
            var menu = QueryMenu(view, query);
            try
            {
                Check(menu.Length == (result.Kind == PaletteResultKind.Music ? 5 : 3), "context menu only exposes actions appropriate to the result");
                Check(ReadString(menu[0], 10) == (result.Kind == PaletteResultKind.Music ? "پخش در میزون" : "مشاهدهٔ پروفایل"), "primary menu command matches result type");
                Check(ReadString(menu[^1], 10) == "کپی کردن", "copy actions are grouped in a native submenu");
                var copyMenu = QueryMenu(menu[^1], query);
                try
                {
                    Check(copyMenu.Length == (result.Kind == PaletteResultKind.Client ? 3 : 2), "copy submenu exposes only available result fields");
                    InvokeMenu(copyMenu[0], query);
                    Check(state.LastAction == "copy-link", "native submenu copy link reaches action boundary");
                    InvokeMenu(copyMenu[1], query);
                    Check(state.LastAction == "copy-title", "native submenu copy title reaches action boundary");
                    if (result.Kind == PaletteResultKind.Client)
                    {
                        InvokeMenu(copyMenu[2], query);
                        Check(state.LastAction == "copy-username", "native submenu copy username reaches action boundary");
                    }
                }
                finally { foreach (var child in copyMenu) NativeMethods.Release(child); }
            }
            finally { foreach (var child in menu) NativeMethods.Release(child); }
            if (result.Kind == PaletteResultKind.Music)
            {
                state.UpdateResult(result with { LikesCount = 125, ViewsCount = 987, AlbumTitle = "آلبوم", GenreText = "پاپ", DurationText = "03:30", ReleaseDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) });
                using (var data = JsonDocument.Parse(ReadString(template, 7)))
                {
                    Check(data.RootElement.GetProperty("hasMetrics").GetBoolean(), "real detail response reveals metrics");
                    Check(data.RootElement.GetProperty("albumTitle").GetString() == "آلبوم", "lazy detail response updates the existing card");
                }
                state.Update(new(null, true, 42, "در حال دانلود", true));
                using (var data = JsonDocument.Parse(ReadString(template, 7)))
                {
                    Check(data.RootElement.GetProperty("progress").GetInt32() == 42, "progress comes from the download state");
                    Check(data.RootElement.GetProperty("downloadAction").GetString() == "cancel-download", "active download offers cancellation");
                }
                Check(Math.Abs(ReadNativeProgress(view) - .42f) < .0001f, "native row progress uses normalized real percentage");
                state.Update(new(null, false, null, "دانلود لغو شد", true));
                Check(ReadNativeStatus(view).Contains("لغو", StringComparison.Ordinal), "text-only native status works without progress");
            }

            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, long, int>)NativeMethods.Table(template)[10])(template, token));
            token = 0;
            var calls = callback.Calls;
            state.Update(new(null, false, null, "updated", false));
            Check(callback.Calls == calls, "removed host subscription is no longer notified");
            Check(ReadOverlayGlyph(view) is null, "clearing the row image removes its native type badge");
            preview.Dispose(); preview = null;
            presentation.Dispose(); presentation = null;
            var lateAction = NativeMethods.String("{\"action\":\"copy-link\"}");
            try { Check(((delegate* unmanaged[Stdcall]<nint, nint, nint, nint, int>)NativeMethods.Table(template)[8])(template, 0, lateAction, 0) == unchecked((int)0x80000013), "disposed preview rejects late actions"); }
            finally { NativeMethods.WindowsDeleteString(lateAction); }
        }
        finally
        {
            if (token != 0) ((delegate* unmanaged[Stdcall]<nint, long, int>)NativeMethods.Table(template)[10])(template, token);
            if (callback is not null) NativeMethods.Release(callback.Identity);
            preview?.Dispose(); presentation?.Dispose();
            NativeMethods.Release(query); NativeMethods.Release(template); NativeMethods.Release(contentView); NativeMethods.Release(content);
            NativeMethods.Release(view); NativeMethods.Release(item); NativeMethods.Release(factory);
        }
        Check(state.Subscriptions == 0, "released result has no retained state subscriptions");
    }

    internal static nint GetNativePreview(nint itemView)
    {
        nint operation = 0, info = 0;
        try
        {
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(itemView)[17])(itemView, &operation));
            info = NativeMethods.As(operation, AsyncInfoId);
            var deadline = Environment.TickCount64 + 5000;
            int status;
            do
            {
                status = 0;
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)NativeMethods.Table(info)[7])(info, &status));
                if (status == 0) Thread.Sleep(10);
                if (Environment.TickCount64 > deadline) throw new TimeoutException("Native preview did not complete.");
            } while (status == 0);
            nint result = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(operation)[8])(operation, &result));
            return result;
        }
        finally
        {
            if (info != 0) ((delegate* unmanaged[Stdcall]<nint, int>)NativeMethods.Table(info)[10])(info);
            NativeMethods.Release(info); NativeMethods.Release(operation);
        }
    }

    private static nint ReadProperty(nint view, string key)
    {
        nint properties = 0, name = 0, boxed = 0;
        try
        {
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(view)[13])(view, &properties));
            name = NativeMethods.String(key);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(properties)[6])(properties, name, &boxed));
            return NativeMethods.As(boxed, new Guid("4bd682dd-7554-40e9-9a9b-82654ede7e62"));
        }
        finally { NativeMethods.Release(boxed); NativeMethods.WindowsDeleteString(name); NativeMethods.Release(properties); }
    }

    private static bool ReadBooleanProperty(nint view, string key)
    {
        var property = ReadProperty(view, key);
        try
        {
            int type = 0; byte value = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)NativeMethods.Table(property)[6])(property, &type));
            Check(type == 11, $"{key} uses a WinRT Boolean, not a string");
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, byte*, int>)NativeMethods.Table(property)[18])(property, &value));
            return value != 0;
        }
        finally { NativeMethods.Release(property); }
    }

    private static string ReadPropertyString(nint view, string key)
    {
        var property = ReadProperty(view, key);
        try { return ReadString(property, 19); }
        finally { NativeMethods.Release(property); }
    }

    private static string[] ReadSubtitles(nint view)
    {
        uint count = 0; nint* values = null;
        try
        {
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint*, nint**, int>)NativeMethods.Table(view)[11])(view, &count, &values));
            var text = new string[count];
            for (var index = 0; index < count; index++)
            {
                var value = NativeMethods.As(values[index], new Guid("4bd682dd-7554-40e9-9a9b-82654ede7e62"));
                try { text[index] = ReadString(value, 19); }
                finally { NativeMethods.Release(value); }
            }
            return text;
        }
        finally
        {
            for (var index = 0; index < count; index++) NativeMethods.Release(values[index]);
            Marshal.FreeCoTaskMem((nint)values);
        }
    }

    internal static nint[] QueryMenu(nint view, nint query)
    {
        nint operation = 0, results = 0; uint count = 0; nint* items = null;
        try
        {
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(view)[14])(view, query, &operation));
            var info = NativeMethods.As(operation, AsyncInfoId);
            try
            {
                var deadline = Environment.TickCount64 + 5000;
                int status;
                do
                {
                    status = 0;
                    NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)NativeMethods.Table(info)[7])(info, &status));
                    if (status == 0) Thread.Sleep(10);
                    if (Environment.TickCount64 > deadline) throw new TimeoutException("Native menu query did not complete.");
                } while (status == 0);
            }
            finally { NativeMethods.Release(info); }
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(operation)[10])(operation, &results));
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint*, nint**, int>)NativeMethods.Table(results)[7])(results, &count, &items));
            return new ReadOnlySpan<nint>(items, checked((int)count)).ToArray();
        }
        finally
        {
            Marshal.FreeCoTaskMem((nint)items); NativeMethods.Release(results);
            if (operation != 0)
            {
                var info = NativeMethods.As(operation, AsyncInfoId);
                try { NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, int>)NativeMethods.Table(info)[10])(info)); }
                finally { NativeMethods.Release(info); NativeMethods.Release(operation); }
            }
        }
    }

    private static void InvokeMenu(nint view, nint query)
    {
        nint invoker = 0;
        try
        {
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(view)[21])(view, &invoker));
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)NativeMethods.Table(invoker)[6])(invoker, query));
        }
        finally { NativeMethods.Release(invoker); }
    }

    private static void InvokePreview(nint view, string action)
    {
        var json = NativeMethods.String("{\"data\":{\"action\":\"" + action + "\"}}");
        try { NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint, nint, int>)NativeMethods.Table(view)[8])(view, 0, json, 0)); }
        finally { NativeMethods.WindowsDeleteString(json); }
    }

    private static float ReadNativeProgress(nint view)
    {
        nint status = 0, progress = 0;
        try
        {
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(view)[15])(view, &status));
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(status)[8])(status, &progress));
            float value = -1;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, float*, int>)NativeMethods.Table(progress)[8])(progress, &value));
            return value;
        }
        finally { NativeMethods.Release(progress); NativeMethods.Release(status); }
    }

    internal static string? ReadOverlayGlyph(nint view)
    {
        nint icon = 0;
        try
        {
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(view)[9])(view, &icon));
            return icon == 0 ? null : ReadString(icon, 7);
        }
        finally { NativeMethods.Release(icon); }
    }

    private static string ReadNativeStatus(nint view)
    {
        nint status = 0;
        try
        {
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(view)[15])(view, &status));
            return ReadString(status, 7);
        }
        finally { NativeMethods.Release(status); }
    }

    private static bool ContainsProperty(JsonElement node, string property, string value)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var child in node.EnumerateObject())
                if ((child.Name == property && child.Value.ValueKind == JsonValueKind.String && child.Value.GetString() == value) || ContainsProperty(child.Value, property, value)) return true;
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var child in node.EnumerateArray()) if (ContainsProperty(child, property, value)) return true;
        return false;
    }

    internal static string ReadString(nint source, int slot)
    {
        nint value = 0;
        NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(source)[slot])(source, &value));
        try { return NativeMethods.ReadString(value); }
        finally { NativeMethods.WindowsDeleteString(value); }
    }
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine("PASS " + description); _checks++;
    }

    private sealed class FixtureState(PaletteResultData result) : IPaletteResultState
    {
        private PaletteResultSnapshot _snapshot;
        private Action? _changed;
        public PaletteResultData Result { get; private set; } = result;
        public PaletteResultSnapshot GetSnapshot() => _snapshot;
        public event Action? Changed { add { _changed += value; Subscriptions++; } remove { _changed -= value; Subscriptions--; } }
        internal int Subscriptions { get; private set; }
        internal int DetailsRequests { get; private set; }
        internal string? LastAction { get; private set; }
        public void Invoke(string action) => LastAction = action;
        public void RequestDetails() => DetailsRequests++;
        internal void Update(PaletteResultSnapshot snapshot) { _snapshot = snapshot; _changed?.Invoke(); }
        internal void UpdateResult(PaletteResultData data) { Result = data; _changed?.Invoke(); }
    }

    private sealed class ChangedCallback : NativeObject
    {
        private static readonly Guid InterfaceId = new("5a7722ed-bf18-5bb9-a6cb-3950bc80a0a8");
        private static readonly nint Vtable = CreateTable();
        internal int Calls;
        internal List<string> Names { get; } = [];
        internal ChangedCallback() : base(false)
        {
            try { AddInterface(InterfaceId, Vtable); EnableMarshaling(); }
            catch { Dispose(); throw; }
        }
        internal override string RuntimeClassName => "Mizon.ResultRenderingTests.PropertyChanged";
        private static nint CreateTable()
        {
            var table = (nint*)NewTable(4, false);
            table[3] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, nint, int>)&Invoke;
            return (nint)table;
        }
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int Invoke(nint self, nint sender, nint args)
        {
            var owner = Owner<ChangedCallback>(self);
            owner.Names.Add(ReadString(args, 6));
            Interlocked.Increment(ref owner.Calls);
            return 0;
        }
    }

    internal static bool VisibilityValuesAreBoolean(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
            return element.EnumerateObject().All(property =>
                (property.Name != "isVisible" || property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) &&
                VisibilityValuesAreBoolean(property.Value));
        if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().All(VisibilityValuesAreBoolean);
        return true;
    }
}
