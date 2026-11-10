using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Mizon.Windows.PaletteProvider;

// An explicit, local-only playground. None of these controls call the API,
// launch media, write a playlist or alter the signed-in user's preferences.
internal sealed class PaletteCapabilityDemoState : IPaletteResultState, IPaletteCustomPreview, IDisposable
{
    internal const int SectionCount = 5;
    private static readonly string[] SectionTitles =
        ["کارت و عملیات", "کنترل‌های تعاملی", "نمودار و داده", "پیشرفت زنده", "ورودی‌ها"];
    private readonly Lock _gate = new();
    private readonly string? _image = FindDemoImage();
    private Timer? _timer;
    private long _revision, _timerGeneration;
    private int _section, _volume = 65, _progress, _repeats = 2;
    private bool _closed, _playing, _liked, _repeat, _notify = true, _hasProgress;
    private string _name = "پلی‌لیست شب", _mood = "calm", _status = "نمونهٔ محلی؛ اطلاعات حساب شما تغییر نمی‌کند.";

    public PaletteResultData Result { get; } = new()
    {
        Kind = PaletteResultKind.Demo,
        PublicId = new("ecf67482-e5be-4ead-a101-78ef2b55b970"),
        Title = "Mizon Demo · آزمایشگاه میزون",
        MatchedSubtitle = "کنترل، نمودار، ورودی و پیشرفت • دادهٔ آزمایشی",
        RelevanceScore = 1.0
    };

    public event Action? Changed;

    public PaletteResultSnapshot GetSnapshot()
    {
        lock (_gate) return new(_image, _timer is not null, _hasProgress ? _progress : null, _status, false);
    }

    public (string Template, string Data) GetPreview()
    {
        Snapshot snapshot;
        lock (_gate)
            snapshot = new(_section, _revision, _volume, _progress, _repeats, _playing, _liked,
                _repeat, _notify, _timer is not null, _name, _mood, _status, _image);
        return (WriteJson(writer => WriteCard(writer, snapshot)), WriteJson(writer => WriteData(writer, snapshot)));
    }

    public void RequestDetails() { }

    public void Invoke(string action)
    {
        lock (_gate)
        {
            if (_closed) return;
            if (action.StartsWith("demo-section-", StringComparison.Ordinal) &&
                int.TryParse(action.AsSpan(13), NumberStyles.None, CultureInfo.InvariantCulture, out var section))
            {
                if (section is < 0 or >= SectionCount || section == _section) return;
                _section = section;
            }
            else
            {
                switch (action)
                {
                    case "demo-next": _section = (_section + 1) % SectionCount; break;
                    case "demo-previous": _section = (_section + SectionCount - 1) % SectionCount; break;
                    case "demo-play":
                        _playing = !_playing;
                        _status = _playing ? "پخش آزمایشی فعال است؛ صدایی پخش نمی‌شود." : "پخش آزمایشی متوقف شد.";
                        break;
                    case "demo-like":
                        _liked = !_liked;
                        _status = _liked ? "پسندیدن آزمایشی ثبت شد؛ فقط در همین کارت." : "پسندیدن آزمایشی برداشته شد.";
                        break;
                    case "demo-progress":
                        if (_timer is not null) return;
                        _section = 3; _progress = 0; _hasProgress = true;
                        _status = "پیشرفت آزمایشی؛ هیچ فایلی دانلود نمی‌شود.";
                        var generation = ++_timerGeneration;
                        _timer = new Timer(AdvanceProgress, generation, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
                        break;
                    case "demo-cancel":
                        if (_timer is null) return;
                        StopTimer(); _status = "عملیات آزمایشی لغو شد."; break;
                    case "demo-reset":
                        StopTimer(); _section = 0; _volume = 65; _progress = 0; _repeats = 2;
                        _playing = _liked = _repeat = _hasProgress = false; _notify = true;
                        _name = "پلی‌لیست شب"; _mood = "calm";
                        _status = "نمونهٔ محلی؛ اطلاعات حساب شما تغییر نمی‌کند.";
                        break;
                    default: return;
                }
            }
            if (_section != 3 && _timer is not null)
            {
                // A background demo tick must not rebuild an input form and
                // steal focus or discard the text the user is still entering.
                StopTimer();
                _status = "پیشرفت آزمایشی با تغییر بخش متوقف شد.";
            }
            ++_revision;
        }
        Notify();
    }

    public void InvokeCard(string actionType, string actionJson, string inputsJson)
    {
        // RunUX custom inputs pass their actionId as the first argument, whereas
        // standard Action.Submit puts our command in the JSON data object.
        using var actionDocument = ReadDocument(actionJson);
        using var inputsDocument = ReadDocument(inputsJson);
        var action = actionType.StartsWith("demo-", StringComparison.Ordinal)
            ? actionType : ReadAction(actionDocument?.RootElement) ?? ReadAction(inputsDocument?.RootElement);
        if (action is null) return;
        if (action is not ("demo-volume" or "demo-toggle" or "demo-text" or "demo-choice" or "demo-save"))
        {
            Invoke(action);
            return;
        }
        var payload = actionDocument?.RootElement;
        var inputs = inputsDocument?.RootElement;
        var notify = true;
        lock (_gate)
        {
            if (_closed) return;
            switch (action)
            {
                case "demo-volume":
                    if (!TryNumber(payload, "value", out var volume) || !double.IsFinite(volume)) return;
                    _volume = (int)Math.Round(Math.Clamp(volume, 0, 100));
                    _status = $"شدت صدای آزمایشی: {_volume}٪";
                    // Rebuilding a preview on every drag sample replaces the
                    // native thumb. Persist samples but render only on commit.
                    notify = !TryBoolean(payload, "committed", out var committed) || committed;
                    break;
                case "demo-toggle":
                    if (!TryBoolean(payload, "isOn", out var repeat)) return;
                    _repeat = repeat; _status = repeat ? "تکرار آزمایشی روشن شد." : "تکرار آزمایشی خاموش شد.";
                    break;
                case "demo-text":
                    if (ReadText(payload, "value") is not { } name) return;
                    _name = name.Trim()[..Math.Min(name.Trim().Length, 60)];
                    _status = "عنوان ورودی فقط در این نمونه نگه داشته شد.";
                    break;
                case "demo-choice":
                    var mood = ReadText(payload, "value");
                    if (mood is not ("calm" or "energy" or "focus")) return;
                    _mood = mood; _status = "حالت انتخابی در همین نمونه به‌روز شد.";
                    break;
                case "demo-save":
                    SaveInputs(inputs ?? payload);
                    break;
            }
            ++_revision;
        }
        if (notify) Notify();
    }

    private void SaveInputs(JsonElement? inputs)
    {
        var name = (ReadText(inputs, "demoName") ?? _name).Trim();
        if (name.Length is < 1 or > 60) { _status = "عنوان باید بین ۱ تا ۶۰ نویسه باشد."; return; }
        var mood = ReadText(inputs, "demoMood") ?? _mood;
        if (mood is not ("calm" or "energy" or "focus")) { _status = "یکی از حالت‌های موجود را انتخاب کنید."; return; }
        var repeats = (double)_repeats;
        if (HasProperty(inputs, "demoRepeats") && !TryNumber(inputs, "demoRepeats", out repeats))
        { _status = "تعداد تکرار باید بین ۱ تا ۱۰ باشد."; return; }
        if (!double.IsFinite(repeats) || repeats is < 1 or > 10 || repeats != Math.Truncate(repeats))
        { _status = "تعداد تکرار باید بین ۱ تا ۱۰ باشد."; return; }
        _name = name; _mood = mood; _repeats = (int)repeats;
        if (TryBoolean(inputs, "demoNotify", out var notify)) _notify = notify;
        _status = $"«{_name}» ذخیرهٔ آزمایشی شد؛ {_repeats} تکرار.";
    }

    private void AdvanceProgress(object? state)
    {
        lock (_gate)
        {
            if (_closed || _timer is null || state is not long generation || generation != _timerGeneration) return;
            _progress = Math.Min(100, _progress + 5);
            if (_progress == 100) { StopTimer(); _status = "عملیات آزمایشی کامل شد؛ فایلی ساخته نشد."; }
            ++_revision;
        }
        Notify();
    }

    private void StopTimer()
    {
        ++_timerGeneration;
        var timer = _timer; _timer = null;
        timer?.Dispose();
    }

    private void Notify()
    {
        Action? handlers;
        lock (_gate) { if (_closed) return; handlers = Changed; }
        if (handlers is null) return;
        try
        {
            using var apartment = new ComApartment();
            foreach (Action handler in handlers.GetInvocationList())
            {
                lock (_gate) if (_closed) return;
                try { handler(); } catch (Exception error) { Log.Error("Demo preview update", error); }
            }
        }
        catch (Exception error) { Log.Error("Demo notification apartment", error); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_closed) return;
            _closed = true; StopTimer(); Changed = null;
        }
    }

    private static void WriteCard(Utf8JsonWriter writer, Snapshot state)
    {
        writer.WriteStartObject(); writer.WriteString("$schema", "http://adaptivecards.io/schemas/adaptive-card.json");
        writer.WriteString("type", "AdaptiveCard"); writer.WriteString("version", "1.5"); writer.WriteBoolean("rtl", true);
        writer.WriteStartArray("body");
        Inset(writer, 10);
        Row(writer, w => Columns(w,
            ("stretch", a => Text(a, $"{SectionTitles[state.Section]}  •  {state.Section + 1} / {SectionCount}", "Small", true, "Accent")),
            ("24px", a =>
            {
                a.WriteStartObject(); a.WriteString("type", "FontIcon"); a.WriteString("glyph", "\uE189");
                a.WriteNumber("size", 18); a.WriteString("label", "آزمایشگاه میزون");
                Fallback(a, "♪"); a.WriteEndObject();
            })));
        switch (state.Section)
        {
            case 0: WriteOverview(writer, state); break;
            case 1: WriteControls(writer, state); break;
            case 2: WriteChart(writer); break;
            case 3: WriteProgress(writer, state); break;
            case 4: WriteInputs(writer, state); break;
        }
        Row(writer, w => Text(w, state.Status, "Small", subtle: true), "Small");
        Inset(writer, 6);
        Row(writer, w => Actions(w, ("قبلی", "demo-previous"), ("بخش بعدی", "demo-next")));
        Inset(writer, 10);
        writer.WriteEndArray(); writer.WriteEndObject();
    }

    private static void WriteOverview(Utf8JsonWriter writer, Snapshot state)
    {
        Row(writer, w =>
        {
            Columns(w,
                ("stretch", a =>
                {
                    Text(a, "شبِ آبی", "Medium", true);
                    Text(a, "Mizon Sessions · نمونهٔ موسیقی", "Small", subtle: true);
                    Text(a, $"{(state.Liked ? "۱۲۵" : "۱۲۴")} پسند  ·  ۰۳:۳۰  ·  دادهٔ ساختگی", "Small", subtle: true);
                }),
                ("64px", a =>
                {
                    if (state.Image is { } image)
                    {
                        a.WriteStartObject(); a.WriteString("type", "Image"); a.WriteString("url", image);
                        a.WriteString("width", "64px"); a.WriteString("height", "64px"); a.WriteString("style", "Person");
                        a.WriteString("altText", "کاور آزمایشی میزون"); a.WriteEndObject();
                    }
                    else Text(a, "♪", "ExtraLarge", color: "Accent");
                }));
        }, "Small");
        Inset(writer, 8);
        Row(writer, w => Actions(w, (state.Playing ? "مکث" : "پخش", "demo-play"),
            (state.Liked ? "پسندیده شد" : "پسندیدن", "demo-like"), ("شروع پیشرفت", "demo-progress")));
    }

    private static void WriteControls(Utf8JsonWriter writer, Snapshot state)
    {
        Row(writer, w =>
        {
            w.WriteStartObject(); w.WriteString("type", "Input.Slider"); w.WriteString("id", "demoVolume");
            w.WriteString("actionId", "demo-volume"); w.WriteString("title", $"بلندی صدا · {state.Volume}٪");
            w.WriteNumber("min", 0); w.WriteNumber("max", 100); w.WriteNumber("step", 1); w.WriteNumber("value", state.Volume);
            w.WriteBoolean("isEnabled", true); Fallback(w, $"بلندی صدا: {state.Volume}٪"); w.WriteEndObject();
        }, "Small");
        Row(writer, w =>
        {
            w.WriteStartObject(); w.WriteString("type", "Input.ToggleSwitch"); w.WriteString("id", "demoRepeat");
            w.WriteString("actionId", "demo-toggle"); w.WriteString("title", "تکرار آهنگ (آزمایشی)");
            w.WriteBoolean("value", state.Repeat); w.WriteBoolean("isEnabled", true);
            Fallback(w, state.Repeat ? "تکرار روشن" : "تکرار خاموش"); w.WriteEndObject();
        }, "Small");
        Row(writer, w => Text(w, "کنترل‌های بومی خود Search؛ مقدارها فقط اینجا تغییر می‌کنند.", "Small", subtle: true), "Small");
    }

    private static void WriteChart(Utf8JsonWriter writer)
    {
        Row(writer, w =>
        {
            w.WriteStartObject(); w.WriteString("type", "Chart.Line"); w.WriteString("title", "ریتم هفتهٔ آزمایشی");
            w.WriteNumber("plotHeight", 84); w.WriteBoolean("showTitle", false); w.WriteBoolean("showScale", true);
            w.WriteBoolean("showLegend", true); w.WriteNumber("yMin", 0); w.WriteNumber("yMax", 90);
            w.WriteStartArray("data");
            Series(w, "موسیقی", [24, 42, 34, 68, 52, 84, 63]);
            Series(w, "موزیک‌ویدیو", [12, 18, 26, 19, 38, 30, 42]);
            w.WriteEndArray(); Fallback(w, "نمودار آزمایشی: ۳۶۷ دقیقه موسیقی و ۱۸۵ دقیقه موزیک‌ویدیو.");
            w.WriteEndObject();
        }, "Small");
        Row(writer, w => Text(w, "۵۵۲ دقیقه  ·  ۲ مسیر  ·  تمام اعداد ساختگی‌اند", "Small", subtle: true), "Small");
    }

    private static void WriteProgress(Utf8JsonWriter writer, Snapshot state)
    {
        Row(writer, w => Columns(w,
            ("stretch", a =>
            {
                Text(a, state.Running ? "در حال آماده‌سازی آزمایشی" : state.Progress == 100 ? "نمونه آماده است" : "آمادهٔ شروع", "Default", true);
                Text(a, $"{state.Progress}٪  ·  بدون دریافت یا نوشتن فایل", "Small", subtle: true);
            }),
            ("44px", a =>
            {
                a.WriteStartObject(); a.WriteString("type", "ProgressRing"); a.WriteNumber("value", state.Progress);
                a.WriteNumber("max", 100); a.WriteNumber("size", 40); Fallback(a, $"{state.Progress}٪"); a.WriteEndObject();
            })), "Small");
        Row(writer, w =>
        {
            w.WriteStartObject(); w.WriteString("type", "ProgressBar"); w.WriteNumber("value", state.Progress);
            w.WriteNumber("max", 100); w.WriteString("label", $"پیشرفت آزمایشی {state.Progress}٪");
            Fallback(w, $"پیشرفت {state.Progress}٪"); w.WriteEndObject();
        }, "Small");
        Inset(writer, 6);
        Row(writer, w => Actions(w, state.Running ? ("لغو", "demo-cancel") : ("شروع دوباره", "demo-progress")));
    }

    private static void WriteInputs(Utf8JsonWriter writer, Snapshot state)
    {
        Row(writer, w => Columns(w,
            ("stretch", a =>
            {
                a.WriteStartObject(); a.WriteString("type", "Input.Text"); a.WriteString("id", "demoName");
                a.WriteString("label", "عنوان نمونه"); a.WriteString("value", state.Name); a.WriteNumber("maxLength", 60);
                a.WriteBoolean("isRequired", true); a.WriteString("errorMessage", "عنوان را وارد کنید.");
                a.WriteString("actionId", "demo-text"); a.WriteEndObject();
            }),
            ("stretch", a =>
            {
                a.WriteStartObject(); a.WriteString("type", "Input.ChoiceSet"); a.WriteString("id", "demoMood");
                a.WriteString("label", "حال‌وهوا"); a.WriteString("style", "compact"); a.WriteString("value", state.Mood);
                a.WriteString("actionId", "demo-choice"); a.WriteStartArray("choices");
                Choice(a, "آرام", "calm"); Choice(a, "پرانرژی", "energy"); Choice(a, "تمرکز", "focus");
                a.WriteEndArray(); a.WriteEndObject();
            })), "Small");
        Row(writer, w => Columns(w,
            ("stretch", a =>
            {
                a.WriteStartObject(); a.WriteString("type", "Input.Number"); a.WriteString("id", "demoRepeats");
                a.WriteString("label", "تعداد تکرار"); a.WriteNumber("value", state.Repeats);
                a.WriteNumber("min", 1); a.WriteNumber("max", 10); a.WriteEndObject();
            }),
            ("stretch", a =>
            {
                a.WriteStartObject(); a.WriteString("type", "Input.Toggle"); a.WriteString("id", "demoNotify");
                a.WriteString("title", "یادآوری آزمایشی"); a.WriteString("value", state.Notify ? "true" : "false");
                a.WriteString("valueOn", "true"); a.WriteString("valueOff", "false"); a.WriteEndObject();
            })), "Small");
        Inset(writer, 6);
        Row(writer, w => Actions(w, ("ذخیرهٔ آزمایشی", "demo-save")));
    }

    private static void Series(Utf8JsonWriter writer, string title, ReadOnlySpan<int> values)
    {
        writer.WriteStartObject(); writer.WriteString("legend", title); writer.WriteStartArray("values");
        for (var i = 0; i < values.Length; i++)
        { writer.WriteStartObject(); writer.WriteNumber("x", i + 1); writer.WriteNumber("y", values[i]); writer.WriteEndObject(); }
        writer.WriteEndArray(); writer.WriteEndObject();
    }

    private static void Choice(Utf8JsonWriter writer, string title, string value)
    { writer.WriteStartObject(); writer.WriteString("title", title); writer.WriteString("value", value); writer.WriteEndObject(); }

    private static void Fallback(Utf8JsonWriter writer, string text)
    { writer.WritePropertyName("fallback"); Text(writer, text, "Small"); }

    private static void Text(Utf8JsonWriter writer, string text, string size = "Default", bool bold = false, string? color = null, bool subtle = false)
    {
        writer.WriteStartObject(); writer.WriteString("type", "TextBlock"); writer.WriteString("text", text);
        writer.WriteString("size", size); writer.WriteString("spacing", "None"); writer.WriteString("horizontalAlignment", "Right");
        writer.WriteBoolean("wrap", false); writer.WriteNumber("maxLines", 1);
        if (bold) writer.WriteString("weight", "Bolder");
        if (color is not null) writer.WriteString("color", color);
        if (subtle) writer.WriteBoolean("isSubtle", true);
        writer.WriteEndObject();
    }

    private static void Actions(Utf8JsonWriter writer, params ReadOnlySpan<(string Title, string Action)> actions)
    {
        writer.WriteStartObject(); writer.WriteString("type", "ActionSet"); writer.WriteStartArray("actions");
        foreach (var (title, action) in actions)
        {
            writer.WriteStartObject(); writer.WriteString("type", "Action.Submit"); writer.WriteString("title", title);
            if (action != "demo-save") writer.WriteString("associatedInputs", "none");
            writer.WriteStartObject("data"); writer.WriteString("action", action); writer.WriteEndObject(); writer.WriteEndObject();
        }
        writer.WriteEndArray(); writer.WriteEndObject();
    }

    // Layout-only gutters preserve the Windows host's native material. Keeping
    // rows separate avoids WholeItemsPanel dropping an oversized atomic card.
    private static void Row(Utf8JsonWriter writer, Action<Utf8JsonWriter> content, string spacing = "None")
    {
        writer.WriteStartObject(); writer.WriteString("type", "ColumnSet"); writer.WriteString("spacing", spacing);
        writer.WriteStartArray("columns");
        Column(writer, "12px", static _ => { }, "None");
        Column(writer, "stretch", content, "None");
        Column(writer, "12px", static _ => { }, "None");
        writer.WriteEndArray(); writer.WriteEndObject();
    }

    private static void Columns(Utf8JsonWriter writer, params ReadOnlySpan<(string Width, Action<Utf8JsonWriter> Content)> columns)
    {
        writer.WriteStartObject(); writer.WriteString("type", "ColumnSet"); writer.WriteString("spacing", "None");
        writer.WriteStartArray("columns");
        for (var i = 0; i < columns.Length; i++) Column(writer, columns[i].Width, columns[i].Content, i == 0 ? "None" : "Small");
        writer.WriteEndArray(); writer.WriteEndObject();
    }

    private static void Column(Utf8JsonWriter writer, string width, Action<Utf8JsonWriter> content, string spacing)
    {
        writer.WriteStartObject(); writer.WriteString("type", "Column"); writer.WriteString("width", width);
        writer.WriteString("spacing", spacing); writer.WriteString("verticalContentAlignment", "Center");
        writer.WriteStartArray("items"); content(writer); writer.WriteEndArray(); writer.WriteEndObject();
    }

    private static void Inset(Utf8JsonWriter writer, int height)
    {
        writer.WriteStartObject(); writer.WriteString("type", "Container"); writer.WriteString("spacing", "None");
        writer.WriteString("minHeight", $"{height}px"); writer.WriteStartArray("items"); writer.WriteEndArray(); writer.WriteEndObject();
    }

    private static void WriteData(Utf8JsonWriter writer, Snapshot state)
    {
        writer.WriteStartObject(); writer.WriteBoolean("demo", true); writer.WriteNumber("revision", state.Revision);
        writer.WriteNumber("section", state.Section); writer.WriteString("sectionTitle", SectionTitles[state.Section]);
        writer.WriteNumber("volume", state.Volume); writer.WriteBoolean("repeat", state.Repeat);
        writer.WriteBoolean("playing", state.Playing); writer.WriteBoolean("liked", state.Liked);
        writer.WriteNumber("progress", state.Progress); writer.WriteBoolean("running", state.Running);
        writer.WriteString("name", state.Name); writer.WriteString("mood", state.Mood);
        writer.WriteNumber("repeats", state.Repeats); writer.WriteBoolean("notify", state.Notify);
        writer.WriteString("status", state.Status); writer.WriteEndObject();
    }

    private static string WriteJson(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) write(writer);
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    private static JsonDocument? ReadDocument(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 16_384) return null;
        try { return JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 }); }
        catch (JsonException) { return null; }
    }

    private static string? ReadAction(JsonElement? element)
    {
        if (ReadText(element, "action") is { } action) return action;
        return element is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("data", out var data)
            ? ReadText(data, "action") : null;
    }

    private static string? ReadText(JsonElement? element, string key) =>
        element is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static bool HasProperty(JsonElement? element, string key) =>
        element is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty(key, out _);

    private static bool TryNumber(JsonElement? element, string key, out double number)
    {
        number = 0;
        if (element is not { ValueKind: JsonValueKind.Object } root || !root.TryGetProperty(key, out var value)) return false;
        return value.ValueKind == JsonValueKind.Number ? value.TryGetDouble(out number)
            : value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private static bool TryBoolean(JsonElement? element, string key, out bool boolean)
    {
        boolean = false;
        if (element is not { ValueKind: JsonValueKind.Object } root || !root.TryGetProperty(key, out var value)) return false;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) { boolean = value.GetBoolean(); return true; }
        return value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out boolean);
    }

    private static string? FindDemoImage()
    {
        foreach (var name in new[] { Path.Combine("Public", "Cover.jpg"), Path.Combine("Assets", "Square150x150Logo.png") })
        {
            var path = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(path)) return new Uri(path).AbsoluteUri;
        }
        return null;
    }

    private readonly record struct Snapshot(int Section, long Revision, int Volume, int Progress, int Repeats,
        bool Playing, bool Liked, bool Repeat, bool Notify, bool Running, string Name, string Mood, string Status, string? Image);
}
