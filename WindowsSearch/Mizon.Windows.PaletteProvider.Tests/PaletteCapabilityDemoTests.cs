using Mizon.Windows.PaletteProvider;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

// Exercises the installed native contracts and the real demo state without
// launching Mizon, downloading media, contacting its API or modifying settings.
internal static unsafe class PaletteCapabilityDemoTests
{
    private static readonly Guid InfoId = new("00000036-0000-0000-C000-000000000046");
    private static int _checks;

    internal static int Run()
    {
        _checks = 0;
        var baseline = NativeObject.LiveCount;
        TestState();
        nint factory = 0, query = 0;
        try
        {
            factory = NativePaletteTestData.CreateItemFactory();
            query = NativeMethods.Activate("Windows.UI.Palettes.PaletteQuery");
            var apiCalls = 0;
            using (var session = new PaletteSearchSession(factory, (_, _) => { Interlocked.Increment(ref apiCalls); throw new InvalidOperationException("Demo unexpectedly reached API search."); }))
            {
                foreach (var text in new[] { "mizon demo", "mizondemo", "دمو میزون", " MIZON DEMO " })
                {
                    Check(PaletteCapabilityDemoItem.MatchesQuery(text), "demo alias matches: " + text);
                    var items = Complete(session.Start(query, text));
                    try
                    {
                        Check(items.Length == 1 && Id(items[0]) == PaletteCapabilityDemoItem.Key,
                            "demo query yields exactly one dedicated native result");
                    }
                    finally { ReleaseItems(items); }
                }
                Check(apiCalls == 0, "demo aliases never enumerate the API/authentication search pipeline");
                foreach (var text in new[] { "", "mizon", "demo", "mizon demo song", "prefix mizondemo", "دمو" })
                    Check(!PaletteCapabilityDemoItem.MatchesQuery(text), "ordinary query does not activate the demo: " + text);
            }

            // Restoring the result by identity uses the same local path even
            // without an injected search source or an authenticated session.
            using (var session = new PaletteSearchSession(factory))
            {
                var restored = session.GetItem(PaletteCapabilityDemoItem.Key);
                try { Check(restored != 0 && Id(restored) == PaletteCapabilityDemoItem.Key, "demo restores directly without API or login"); }
                finally { NativeMethods.Release(restored); }
            }
            using var item = new PaletteCapabilityDemoItem(factory);
            var view = item.AcquireView();
            try { TestNativeResult(view, query, item.Dispose); }
            finally { NativeMethods.Release(view); }
        }
        finally { NativeMethods.Release(query); NativeMethods.Release(factory); }
        WaitUntil(() => NativeObject.LiveCount == baseline, "demo native references and host callbacks are released");
        Console.WriteLine($"PASS {_checks} local demo assertions; native parsing/ABI only, no Search UI visibility claim.");
        return _checks;
    }

    internal static void RunRemote()
    {
        _checks = 0;
        var baseline = NativeObject.LiveCount;
        nint extension = 0, factory = 0, queryFactory = 0, query = 0, json = 0;
        var host = new RemoteHost();
        try
        {
            NativeMethods.Check(NativeMethods.CoCreateInstance(ProviderClassFactory.ClassId, 0, 4, MusicPaletteExtension.InterfaceId, out extension));
            factory = NativePaletteTestData.CreateItemFactory();
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)NativeMethods.Table(extension)[6])(extension, factory, host.Identity));
            queryFactory = NativeMethods.Factory("Windows.UI.Palettes.PaletteQuery", new("98f6db9e-99ff-5bc4-84b1-1547927f8830"));
            json = NativeMethods.String("{\"Arguments\":[\"mizon demo\"]}");
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(queryFactory)[6])(queryFactory, json, &query));
            nint operation = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(extension)[9])(extension, query, &operation));
            var items = Complete(operation);
            try
            {
                Check(items.Length == 1 && Id(items[0]) == PaletteCapabilityDemoItem.Key, "installed provider returns exactly the capability demo");
                TestNativeResult(items[0], query);
            }
            finally { ReleaseItems(items); }
        }
        finally
        {
            NativeMethods.Release(query); NativeMethods.Release(queryFactory); NativeMethods.WindowsDeleteString(json);
            NativeMethods.Release(factory); NativeMethods.Release(extension); NativeMethods.Release(host.Identity);
        }
        WaitUntil(() => NativeObject.LiveCount == baseline, "remote demo releases local callbacks");
        Console.WriteLine($"PASS {_checks} installed demo assertions; no real playback/download/app actions executed.");
    }

    private static void TestState()
    {
        using var state = new PaletteCapabilityDemoState();
        var changed = 0;
        state.Changed += () => Interlocked.Increment(ref changed);
        Check(state.Result.Kind == PaletteResultKind.Demo && state.Result.Key == PaletteCapabilityDemoItem.Key,
            "demo state has its own identity instead of a real song/profile ID");
        using var parser = new NativeCardParser();
        string[][] expectedTypes = [["Action.Submit", "TextBlock"], ["Input.Slider", "Input.ToggleSwitch"], ["Chart.Line"], ["ProgressBar", "ProgressRing"], ["Input.Text", "Input.ChoiceSet", "Input.Number", "Input.Toggle"]];
        for (var section = 0; section < PaletteCapabilityDemoState.SectionCount; section++)
        {
            state.Invoke($"demo-section-{section}");
            var preview = state.GetPreview();
            using var card = JsonDocument.Parse(preview.Template);
            using var data = JsonDocument.Parse(preview.Data);
            Check(card.RootElement.GetProperty("type").GetString() == "AdaptiveCard", $"section {section} is an Adaptive Card");
            Check(PaletteResultRenderingTests.VisibilityValuesAreBoolean(card.RootElement) && !preview.Template.Contains("${", StringComparison.Ordinal),
                $"section {section} uses expanded JSON and native booleans");
            foreach (var type in expectedTypes[section])
                Check(Find(card.RootElement, "type", type), $"section {section} includes {type}");
            parser.Validate(preview.Template, "section " + section);
        }

        state.Invoke("demo-reset");
        var reset = state.GetPreview();
        state.Invoke("demo-next");
        Check(state.GetPreview().Template != reset.Template, "next switches the local section");
        state.Invoke("demo-previous");
        Check(state.GetPreview().Template == reset.Template, "previous restores the original section");
        state.Invoke("demo-play");
        Check(state.GetPreview().Template != reset.Template, "simulated play updates local controls without launching the app");
        state.Invoke("demo-like");
        Check(changed > 0, "local actions publish state changes");

        state.Invoke("demo-section-1");
        var beforeDrag = changed;
        state.InvokeCard("demo-volume", "{\"value\":73,\"committed\":false}", "{}");
        Check(changed == beforeDrag, "slider drag avoids rebuilding the card before commit");
        state.InvokeCard("demo-volume", "{\"value\":73,\"committed\":true}", "{}");
        Check(changed > beforeDrag, "committed slider change publishes an update");
        using (var card = JsonDocument.Parse(state.GetPreview().Template))
            Check(Find(card.RootElement, "value", 73), "slider committed value reaches the next native template");
        var beforeToggle = changed;
        state.InvokeCard("demo-toggle", "{\"isOn\":true}", "{}");
        Check(changed > beforeToggle, "native toggle callback changes state");
        state.Invoke("demo-section-4");
        state.InvokeCard("Action.Submit", "{\"data\":{\"action\":\"demo-save\"}}", "{\"demoName\":\"علی آزمون\",\"demoMood\":\"focus\",\"demoRepeats\":3,\"demoNotify\":\"false\"}");
        using (var data = JsonDocument.Parse(state.GetPreview().Data))
            Check(data.RootElement.GetProperty("name").GetString() == "علی آزمون"
                && data.RootElement.GetProperty("mood").GetString() == "focus"
                && data.RootElement.GetProperty("repeats").GetInt32() == 3
                && !data.RootElement.GetProperty("notify").GetBoolean(), "submitted inputs reach local demo state with their native types");
        var beforeInvalid = state.GetPreview().Data;
        state.InvokeCard("demo-volume", "not json", "{}");
        state.InvokeCard("unrecognized", "{}", "{}");
        Check(state.GetPreview().Data == beforeInvalid, "malformed and unrelated callbacks do not mutate demo state");

        // A stopped/disposed timer must not repaint a card retained by Search.
        state.Invoke("demo-progress");
        WaitUntil(() => state.GetSnapshot().IsBusy && state.GetSnapshot().DownloadProgress > 0, "demo progress advances locally");
        state.Invoke("demo-cancel");
        var stopped = state.GetSnapshot();
        Check(!stopped.IsBusy, "cancel stops simulated work");
        Thread.Sleep(350);
        Check(state.GetSnapshot().DownloadProgress == stopped.DownloadProgress, "cancelled progress remains stable");
        state.Invoke("demo-progress");
        WaitUntil(() => state.GetSnapshot().IsBusy, "progress can start again after cancellation");
        state.Invoke("demo-next");
        var changedSection = state.GetSnapshot();
        Check(!changedSection.IsBusy, "leaving progress section stops its timer before showing inputs");
        Thread.Sleep(350);
        Check(state.GetSnapshot() == changedSection, "input section is not rebuilt by old progress ticks");
        state.Invoke("demo-progress");
        state.Dispose();
        var disposedChanges = Volatile.Read(ref changed);
        var disposedState = state.GetSnapshot();
        Thread.Sleep(350);
        state.Invoke("demo-next"); state.InvokeCard("demo-volume", "{\"value\":20,\"committed\":true}", "{}"); state.Dispose();
        Check(Volatile.Read(ref changed) == disposedChanges && state.GetSnapshot() == disposedState,
            "dispose cancels the timer and ignores late input/action callbacks");
    }

    private static void TestNativeResult(nint view, nint query, Action? dispose = null)
    {
        nint contentView = 0, template = 0, icon = 0;
        ChangedCallback? callback = null;
        long token = 0;
        try
        {
            Check(!string.IsNullOrWhiteSpace(PaletteResultRenderingTests.ReadString(view, 10)), "native demo row has its own title");
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(view)[8])(view, &icon));
            Check(icon != 0, "native demo row has a Mizon icon");
            contentView = PaletteResultRenderingTests.GetNativePreview(view);
            template = NativeMethods.As(contentView, PalettePreviewData.TemplateViewId);
            Check(template != 0, "demo supplies the native expanded-card template interface");
            InvokeCard(template, "demo-reset");
            var initial = Template(template);
            callback = new ChangedCallback();
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)NativeMethods.Table(template)[9])(template, callback.Identity, &token));
            InvokeMenu(view, query);
            Check(Template(template) != initial, "native main command switches section instead of launching Mizon");
            Check(callback.Calls >= 2, "host receives TemplateJson and DataJson changes");
            InvokeCard(template, "demo-previous");
            Check(Template(template) == initial, "native card callback preserves the action identifier");
            InvokeCard(template, "demo-section-1");
            NativeMethods.Check(CallCard(template, "demo-volume", "{\"value\":83,\"committed\":true}", "{}"));
            NativeMethods.Check(CallCard(template, "demo-toggle", "{\"isOn\":true}", "{}"));
            using (var data = JsonDocument.Parse(PaletteResultRenderingTests.ReadString(template, 7)))
                Check(data.RootElement.GetProperty("volume").GetInt32() == 83 && data.RootElement.GetProperty("repeat").GetBoolean(),
                    "native bridge preserves custom slider and toggle callback payloads");
            InvokeCard(template, "demo-section-4");
            NativeMethods.Check(CallCard(template, "Action.Submit", "{\"data\":{\"action\":\"demo-save\"}}",
                "{\"demoName\":\"Native form\",\"demoMood\":\"energy\",\"demoRepeats\":4,\"demoNotify\":\"false\"}"));
            using (var data = JsonDocument.Parse(PaletteResultRenderingTests.ReadString(template, 7)))
                Check(data.RootElement.GetProperty("name").GetString() == "Native form"
                    && data.RootElement.GetProperty("mood").GetString() == "energy"
                    && data.RootElement.GetProperty("repeats").GetInt32() == 4
                    && !data.RootElement.GetProperty("notify").GetBoolean(),
                    "native bridge forwards all standard Submit inputs to local state");

            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, int, int>)NativeMethods.Table(query)[22])(query, 2));
            var menu = PaletteResultRenderingTests.QueryMenu(view, query);
            try
            {
                Check(menu.Length == 6, "demo context menu exposes commands, submenu and separator");
                var sections = menu.Single(item => Id(item).EndsWith("/demo-sections", StringComparison.Ordinal));
                var children = PaletteResultRenderingTests.QueryMenu(sections, query);
                try
                {
                    Check(children.Length == PaletteCapabilityDemoState.SectionCount, "native submenu lists all five capability sections");
                    using var parser = new NativeCardParser();
                    for (var index = 0; index < children.Length; index++)
                    {
                        Check(Id(children[index]).EndsWith($"/demo-section-{index}", StringComparison.Ordinal), "submenu preserves section identity " + index);
                        InvokeMenu(children[index], query);
                        parser.Validate(Template(template), "native preview section " + index);
                    }
                }
                finally { ReleaseItems(children); }
                var reset = menu.Single(item => Id(item).EndsWith("/demo-reset", StringComparison.Ordinal));
                InvokeMenu(reset, query);
                Check(Template(template) == initial, "native reset command restores the initial demo");
                var progress = menu.Single(item => Id(item).EndsWith("/demo-progress", StringComparison.Ordinal));
                InvokeMenu(progress, query);
                WaitUntil(() => ReadProgress(view) > 0, "native collapsed row receives simulated progress");
                var runningMenu = PaletteResultRenderingTests.QueryMenu(view, query);
                try
                {
                    var cancel = runningMenu.Single(item => Id(item).EndsWith("/demo-progress", StringComparison.Ordinal));
                    Check(PaletteResultRenderingTests.ReadString(cancel, 10).Contains("لغو", StringComparison.Ordinal), "running native menu offers cancellation");
                    InvokeMenu(cancel, query);
                }
                finally { ReleaseItems(runningMenu); }
            }
            finally { ReleaseItems(menu); }

            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, long, int>)NativeMethods.Table(template)[10])(template, token));
            token = 0;
            var calls = callback.Calls;
            InvokeCard(template, "demo-next");
            Check(callback.Calls == calls, "removed native subscription receives no late state changes");
            if (dispose is not null)
            {
                dispose();
                Check(CallCard(template, "demo-next", "{}", "{}") == unchecked((int)0x80000013), "disposed preview rejects late native actions with RO_E_CLOSED");
            }
        }
        finally
        {
            if (token != 0) ((delegate* unmanaged[Stdcall]<nint, long, int>)NativeMethods.Table(template)[10])(template, token);
            if (callback is not null) NativeMethods.Release(callback.Identity);
            NativeMethods.Release(template); NativeMethods.Release(contentView); NativeMethods.Release(icon);
        }
    }

    private static string Id(nint view) => PaletteResultRenderingTests.ReadString(view, 6);
    private static string Template(nint template) => PaletteResultRenderingTests.ReadString(template, 6);
    private static void ReleaseItems(nint[] items) { foreach (var item in items) NativeMethods.Release(item); }

    private static nint[] Complete(nint operation)
    {
        nint info = 0, result = 0; uint count = 0; nint* items = null;
        try
        {
            info = NativeMethods.As(operation, InfoId);
            var infoPointer = info;
            WaitUntil(() => Status(infoPointer) != 0, "native demo query completes");
            Check(Status(info) == 1, "native demo query completes successfully");
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(operation)[10])(operation, &result));
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint*, nint**, int>)NativeMethods.Table(result)[7])(result, &count, &items));
            return new ReadOnlySpan<nint>(items, checked((int)count)).ToArray();
        }
        finally
        {
            Marshal.FreeCoTaskMem((nint)items); NativeMethods.Release(result);
            if (info != 0) ((delegate* unmanaged[Stdcall]<nint, int>)NativeMethods.Table(info)[10])(info);
            NativeMethods.Release(info); NativeMethods.Release(operation);
        }
    }

    private static int Status(nint info)
    {
        int status = 0;
        NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)NativeMethods.Table(info)[7])(info, &status));
        return status;
    }

    private static void InvokeMenu(nint item, nint query)
    {
        nint invoker = 0;
        try
        {
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(item)[21])(item, &invoker));
            Check(invoker != 0, "demo command has a native delegate invoker");
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)NativeMethods.Table(invoker)[6])(invoker, query));
        }
        finally { NativeMethods.Release(invoker); }
    }

    private static void InvokeCard(nint template, string action) => NativeMethods.Check(CallCard(template, action, "{\"data\":{\"action\":\"" + action + "\"}}", "{}"));
    private static int CallCard(nint template, string action, string json, string inputs)
    {
        nint id = 0, payload = 0, values = 0;
        try
        {
            id = NativeMethods.String(action); payload = NativeMethods.String(json); values = NativeMethods.String(inputs);
            return ((delegate* unmanaged[Stdcall]<nint, nint, nint, nint, int>)NativeMethods.Table(template)[8])(template, id, payload, values);
        }
        finally { NativeMethods.WindowsDeleteString(values); NativeMethods.WindowsDeleteString(payload); NativeMethods.WindowsDeleteString(id); }
    }

    private static float ReadProgress(nint view)
    {
        nint status = 0, progress = 0;
        try
        {
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(view)[15])(view, &status));
            if (status == 0) return 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(status)[8])(status, &progress));
            if (progress == 0) return 0;
            float value = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, float*, int>)NativeMethods.Table(progress)[8])(progress, &value));
            return value;
        }
        finally { NativeMethods.Release(progress); NativeMethods.Release(status); }
    }

    private static bool Find(JsonElement node, string key, string value) => Find(node, key, child => child.ValueKind == JsonValueKind.String && child.GetString() == value);
    private static bool Find(JsonElement node, string key, int value) => Find(node, key, child => child.ValueKind == JsonValueKind.Number && child.TryGetDouble(out var number) && Math.Abs(number - value) < .0001);
    private static bool Find(JsonElement node, string key, Func<JsonElement, bool> predicate)
    {
        if (node.ValueKind == JsonValueKind.Object)
            foreach (var property in node.EnumerateObject())
                if ((property.Name == key && predicate(property.Value)) || Find(property.Value, key, predicate)) return true;
        if (node.ValueKind == JsonValueKind.Array)
            foreach (var item in node.EnumerateArray()) if (Find(item, key, predicate)) return true;
        return false;
    }

    private static void WaitUntil(Func<bool> condition, string message)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (!condition() && Environment.TickCount64 < deadline) Thread.Sleep(10);
        Check(condition(), message);
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        _checks++; Console.WriteLine("PASS " + message);
    }

    private sealed class RemoteHost : NativeObject
    {
        private static readonly nint Table = NewTable(6);
        internal RemoteHost() { AddInterface(new("3e789efc-49dd-5670-8458-7fb58c457265"), Table); EnableMarshaling(); }
        internal override string RuntimeClassName => "Mizon.CapabilityDemoTests.Host";
    }

    private sealed class ChangedCallback : NativeObject
    {
        private static readonly nint Table = CreateTable();
        internal int Calls;
        internal ChangedCallback() : base(false) { AddInterface(new("5a7722ed-bf18-5bb9-a6cb-3950bc80a0a8"), Table); EnableMarshaling(); }
        internal override string RuntimeClassName => "Mizon.CapabilityDemoTests.PropertyChanged";
        private static nint CreateTable()
        {
            var table = (nint*)NewTable(4, false);
            table[3] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, nint, int>)&Invoke;
            return (nint)table;
        }
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int Invoke(nint self, nint sender, nint args) { Interlocked.Increment(ref Owner<ChangedCallback>(self).Calls); return 0; }
    }

    private sealed class NativeCardParser : IDisposable
    {
        private nint _module, _factory, _statics;
        internal NativeCardParser()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SystemApps", "MicrosoftWindows.Client.CBS_cw5n1h2txyewy", "RunUX", "AdaptiveCards.ObjectModel.WinUI3.dll");
            _module = NativeLibrary.Load(path);
            var name = NativeMethods.String("AdaptiveCards.ObjectModel.WinUI3.AdaptiveCard");
            try
            {
                var getFactory = (delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeLibrary.GetExport(_module, "DllGetActivationFactory");
                nint factory = 0;
                NativeMethods.Check(getFactory(name, &factory)); _factory = factory;
                _statics = NativeMethods.As(factory, new("1b938380-0e82-5044-be9b-95ea6f86cadb"));
            }
            catch { Dispose(); throw; }
            finally { NativeMethods.WindowsDeleteString(name); }
        }
        internal void Validate(string json, string stage)
        {
            nint text = NativeMethods.String(json), result = 0, card = 0, errors = 0;
            try
            {
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(_statics)[8])(_statics, text, &result));
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(result)[6])(result, &card));
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(result)[7])(result, &errors));
                uint count = 0;
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)NativeMethods.Table(errors)[7])(errors, &count));
                var messages = new List<string>();
                for (uint index = 0; index < count; index++)
                {
                    nint error = 0;
                    try
                    {
                        NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)NativeMethods.Table(errors)[6])(errors, index, &error));
                        messages.Add(PaletteResultRenderingTests.ReadString(error, 8));
                    }
                    finally { NativeMethods.Release(error); }
                }
                Check(card != 0 && count == 0, stage + " parses in the installed Windows renderer: " + string.Join("; ", messages));
            }
            finally { NativeMethods.Release(errors); NativeMethods.Release(card); NativeMethods.Release(result); NativeMethods.WindowsDeleteString(text); }
        }
        public void Dispose()
        {
            NativeMethods.Release(_statics); _statics = 0; NativeMethods.Release(_factory); _factory = 0;
            if (_module != 0)
            {
                var canUnload = (delegate* unmanaged[Stdcall]<int>)NativeLibrary.GetExport(_module, "DllCanUnloadNow");
                if (canUnload() == 0) NativeLibrary.Free(_module);
                _module = 0;
            }
        }
    }
}
