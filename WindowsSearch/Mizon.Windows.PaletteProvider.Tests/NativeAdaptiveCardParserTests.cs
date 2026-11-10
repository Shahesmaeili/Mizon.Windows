using System.Runtime.InteropServices;
using System.Text.Json;
using Mizon.Windows.PaletteProvider;

// Explicit opt-in compatibility test for the Adaptive Cards DLL installed with
// Windows Search. No UI, activation of another provider, network or registry writes.
internal static unsafe class NativeAdaptiveCardParserTests
{
    private static readonly Guid CardStaticsId = new("1b938380-0e82-5044-be9b-95ea6f86cadb");
    private static int _checks;

    internal static int Run()
    {
        _checks = 0;
        var dll = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "SystemApps", "MicrosoftWindows.Client.CBS_cw5n1h2txyewy", "RunUX", "AdaptiveCards.ObjectModel.WinUI3.dll");
        if (!File.Exists(dll))
            throw new PlatformNotSupportedException("The tested Windows Search native Adaptive Cards runtime is not installed.");
        Console.WriteLine($"Native card parser: {dll}");
        var module = NativeLibrary.Load(dll);
        nint factory = 0, statics = 0;
        try
        {
            var getFactory = (delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeLibrary.GetExport(module, "DllGetActivationFactory");
            var name = NativeMethods.String("AdaptiveCards.ObjectModel.WinUI3.AdaptiveCard");
            try { NativeMethods.Check(getFactory(name, &factory)); }
            finally { NativeMethods.WindowsDeleteString(name); }
            statics = NativeMethods.As(factory, CardStaticsId);

            // This was the concrete failure in the Windows host: an unexpanded
            // string cannot stand in for an Adaptive Cards boolean property.
            var invalid = Parse(statics, """
                {"type":"AdaptiveCard","version":"1.5","body":[
                    {"type":"TextBlock","text":"Header"},
                    {"type":"TextBlock","text":"Details","isVisible":"${visible}"}]}
                """);
            Check(!invalid.HasCard && invalid.Errors.Any(error => error.Contains("isVisible", StringComparison.Ordinal)),
                "installed parser reproduces rejection of string visibility bindings");

            var baseline = NativeObject.LiveCount;
            Exercise(statics, new PaletteResultData
            {
                Kind = PaletteResultKind.Music, PublicId = Guid.NewGuid(), Title = "Quoted \"track\" <title>",
                PersianTitle = "آهنگ فارسی", LikesCount = 15, ViewsCount = 42,
                AlbumTitle = "آلبوم", GenreText = "پاپ", DurationText = "03:30"
            });
            Exercise(statics, new PaletteResultData
            {
                Kind = PaletteResultKind.Client, PublicId = Guid.NewGuid(),
                FirstName = "علی", LastName = "اسماعیلی", Username = "parser_fixture"
            });
            Exercise(statics, new PaletteResultData { Kind = PaletteResultKind.Client, PublicId = Guid.NewGuid() });
            ExerciseDemo(statics);
            Check(NativeObject.LiveCount == baseline, "parser test releases production content/view objects");
            Console.WriteLine($"Native Adaptive Card parser PASS {_checks} assertions; this verifies parsing, not host visual layout.");
            return 0;
        }
        finally
        {
            NativeMethods.Release(statics); NativeMethods.Release(factory);
            // Do not unload a vendor DLL while its own activation caches are live.
            var canUnload = (delegate* unmanaged[Stdcall]<int>)NativeLibrary.GetExport(module, "DllCanUnloadNow");
            if (canUnload() == 0) NativeLibrary.Free(module);
        }
    }

    private static void ExerciseDemo(nint statics)
    {
        using var state = new PaletteCapabilityDemoState();
        var seenTypes = new HashSet<string>(StringComparer.Ordinal);
        var seenCards = new HashSet<string>(StringComparer.Ordinal);
        for (var section = 0; section < 5; section++)
        {
            var (template, _) = ((IPaletteCustomPreview)state).GetPreview();
            Check(seenCards.Add(template), $"demo section {section + 1} has its own card");
            var parsed = Parse(statics, template);
            Check(parsed.HasCard && parsed.Errors.Length == 0,
                $"demo section {section + 1}: native parser accepts the actual card ({string.Join("; ", parsed.Errors)})");
            using var document = JsonDocument.Parse(template);
            foreach (var element in Elements(document.RootElement))
            {
                if (!element.TryGetProperty("type", out var typeValue) || typeValue.ValueKind != JsonValueKind.String) continue;
                var type = typeValue.GetString()!;
                if (type is not ("Input.Slider" or "Input.ToggleSwitch" or "Input.Text" or "Input.ChoiceSet" or "ProgressBar" or "ProgressRing" or "Chart.Line" or "FontIcon")) continue;
                seenTypes.Add(type);
                // Parsing must retain the custom type for RunUX's registered
                // renderer. Accepting only its fallback would hide a broken demo.
                var elementCard = "{\"type\":\"AdaptiveCard\",\"version\":\"1.5\",\"body\":[" + element.GetRawText() + "]}";
                var single = Parse(statics, elementCard);
                Check(single.HasCard && single.Errors.Length == 0 && single.BodyTypes.SequenceEqual([type]),
                    $"demo {type}: native model retains the original element instead of replacing it with fallback");
            }
            state.Invoke("demo-next");
        }
        foreach (var expected in new[] { "Input.Slider", "Input.ToggleSwitch", "Input.Text", "Input.ChoiceSet", "ProgressBar", "ProgressRing", "Chart.Line", "FontIcon" })
            Check(seenTypes.Contains(expected), $"demo exposes {expected} in at least one section");
    }

    private static IEnumerable<JsonElement> Elements(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            yield return value;
            foreach (var property in value.EnumerateObject())
                foreach (var child in Elements(property.Value)) yield return child;
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
                foreach (var child in Elements(item)) yield return child;
    }

    private static void Exercise(nint statics, PaletteResultData result)
    {
        var state = new FixtureState(result);
        nint content = 0, view = 0, template = 0;
        try
        {
            content = PalettePreviewData.BuildPreview(state);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(content)[7])(content, &view));
            template = NativeMethods.As(view, PalettePreviewData.TemplateViewId);
            Validate("initial");
            state.Update(new("file:///C:/Mizon/cover%20image.png", false, null, null, result.Kind == PaletteResultKind.Music));
            Validate("loaded image");
            if (result.Kind == PaletteResultKind.Music)
            {
                state.Update(new("file:///C:/Mizon/cover%20image.png", true, 42, "در حال دانلود", true));
                Validate("download progress");
            }
        }
        finally { NativeMethods.Release(template); NativeMethods.Release(view); NativeMethods.Release(content); }
        Check(state.Subscriptions == 0, "production preview unsubscribes its fixture state");
        return;

        void Validate(string stage)
        {
            nint text = 0;
            try
            {
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(template)[6])(template, &text));
                var json = NativeMethods.ReadString(text);
                var parsed = Parse(statics, json);
                Check(parsed.HasCard && parsed.Errors.Length == 0,
                    $"{result.Kind}/{stage}: production TemplateJson accepted without native parse errors ({string.Join("; ", parsed.Errors)})");
                Check(parsed.BodyTypes[0] == "Container" && parsed.BodyTypes[^1] == "Container" &&
                    parsed.BodyTypes.Contains("ColumnSet") && parsed.BodyTypes.All(type => type is "Container" or "ColumnSet"),
                    $"{result.Kind}/{stage}: native body retains separate inset rows and vertical spacing");
            }
            finally { NativeMethods.WindowsDeleteString(text); }
        }
    }

    private static ParseResult Parse(nint statics, string json)
    {
        nint text = NativeMethods.String(json), result = 0, card = 0, errors = 0, body = 0;
        try
        {
            // IAdaptiveCardStatics.FromJsonString and the returned native model,
            // matching the renderer's own call path and the Microsoft WinUI3 IDL.
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(statics)[8])(statics, text, &result));
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(result)[6])(result, &card));
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(result)[7])(result, &errors));
            var errorMessages = ReadVectorStrings(errors, 8); // AdaptiveError.Message after StatusCode get/set.
            if (card == 0) return new(false, [], errorMessages);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(card)[6])(card, &body));
            return new(true, ReadVectorStrings(body, 7), errorMessages); // IAdaptiveCardElement.ElementTypeString.
        }
        finally
        {
            NativeMethods.Release(body); NativeMethods.Release(errors); NativeMethods.Release(card);
            NativeMethods.Release(result); NativeMethods.WindowsDeleteString(text);
        }
    }

    private static string[] ReadVectorStrings(nint vector, int slot)
    {
        uint count = 0;
        NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)NativeMethods.Table(vector)[7])(vector, &count));
        var values = new string[count];
        for (uint i = 0; i < count; i++)
        {
            nint item = 0, text = 0;
            try
            {
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)NativeMethods.Table(vector)[6])(vector, i, &item));
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(item)[slot])(item, &text));
                values[i] = NativeMethods.ReadString(text);
            }
            finally { NativeMethods.WindowsDeleteString(text); NativeMethods.Release(item); }
        }
        return values;
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        _checks++; Console.WriteLine("PASS " + message);
    }
    private sealed record ParseResult(bool HasCard, string[] BodyTypes, string[] Errors);
    private sealed class FixtureState(PaletteResultData result) : IPaletteResultState
    {
        private Action? _changed;
        private PaletteResultSnapshot _snapshot;
        public PaletteResultData Result => result;
        internal int Subscriptions;
        public event Action? Changed
        {
            add { _changed += value; Subscriptions++; }
            remove { _changed -= value; Subscriptions--; }
        }
        public PaletteResultSnapshot GetSnapshot() => _snapshot;
        public void Invoke(string action) => throw new InvalidOperationException("The parser test must not invoke actions.");
        internal void Update(PaletteResultSnapshot snapshot) { _snapshot = snapshot; _changed?.Invoke(); }
    }
}
