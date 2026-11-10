using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Mizon.Windows.PaletteProvider;

// The Windows host renders this Adaptive Card. It does not load application XAML.
internal static unsafe class PalettePreviewData
{
    internal static readonly Guid ContentId = new("42cd7936-3fdd-59cf-af35-55844289125f");
    internal static readonly Guid TemplateViewId = new("50740111-193a-590d-b3f5-fdb4ab0b71fc");
    private static readonly Guid ContentViewId = new("01760c19-8ad1-545a-881b-e8fa57a4ee2b");

    // Keep host-owned material and colors. Insets are layout-only and do not
    // depend on HostConfig padding (Search can set it to zero). Each row stays
    // separate because RunUX hides an entire oversized atomic child.
    private static readonly string MusicTemplateJson = WithContentInsets("""
        {
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "type": "AdaptiveCard", "version": "1.5", "rtl": true,
          "body": [
            { "type": "ColumnSet", "spacing": "None", "columns": [
              { "type": "Column", "width": "stretch", "verticalContentAlignment": "Center", "items": [
                { "type": "TextBlock", "text": "MIZON / موسیقی", "size": "Small", "weight": "Bolder", "color": "Accent", "spacing": "None", "horizontalAlignment": "Right" },
                { "type": "TextBlock", "text": "${title}", "size": "Medium", "weight": "Bolder", "wrap": true, "maxLines": 2, "horizontalAlignment": "Right", "spacing": "None", "rtl": "${titleRtl}" },
                { "type": "TextBlock", "text": "${secondaryTitle}", "isSubtle": true, "wrap": false, "horizontalAlignment": "Right", "rtl": false, "spacing": "Small", "isVisible": "${hasSecondaryTitle}" }
              ] },
              { "type": "Column", "width": "80px", "spacing": "Medium", "verticalContentAlignment": "Center", "items": [
                { "type": "Image", "url": "${imageUri}", "width": "80px", "height": "80px", "style": "Person", "altText": "${imageAlt}", "isVisible": "${hasImage}" },
                { "type": "TextBlock", "text": "♪", "size": "ExtraLarge", "color": "Accent", "isVisible": "${hasNoImage}" }
              ] }
            ] },
            { "type": "ColumnSet", "spacing": "Small", "isVisible": "${hasMetrics}", "columns": [
              { "type": "Column", "width": "stretch", "isVisible": "${hasDuration}", "items": [{ "type": "TextBlock", "text": "${durationMetric}", "size": "Small", "horizontalAlignment": "Center", "spacing": "None" }] },
              { "type": "Column", "width": "stretch", "separator": true, "spacing": "Small", "isVisible": "${hasLikes}", "items": [{ "type": "TextBlock", "text": "${likesMetric}", "size": "Small", "horizontalAlignment": "Center", "spacing": "None" }] },
              { "type": "Column", "width": "stretch", "separator": true, "spacing": "Small", "isVisible": "${hasViews}", "items": [{ "type": "TextBlock", "text": "${viewsMetric}", "size": "Small", "horizontalAlignment": "Center", "spacing": "None" }] }
            ] },
            { "type": "ActionSet", "spacing": "Small", "isVisible": "${canDownload}", "actions": [
              { "type": "Action.Submit", "title": "پخش", "tooltip": "پخش در میزون", "data": { "action": "play" } },
              { "type": "Action.Submit", "title": "جزئیات", "tooltip": "جزئیات آهنگ", "data": { "action": "details" } },
              { "type": "Action.Submit", "title": "${downloadLabel}", "tooltip": "${downloadTooltip}", "data": { "action": "${downloadAction}" } },
              { "type": "Action.Submit", "title": "کپی لینک", "tooltip": "کپی لینک آهنگ", "data": { "action": "copy-link" } }
            ] },
            { "type": "ActionSet", "spacing": "Small", "isVisible": "${cannotDownload}", "actions": [
              { "type": "Action.Submit", "title": "پخش", "tooltip": "پخش در میزون", "data": { "action": "play" } },
              { "type": "Action.Submit", "title": "جزئیات", "tooltip": "جزئیات آهنگ", "data": { "action": "details" } },
              { "type": "Action.Submit", "title": "کپی لینک", "tooltip": "کپی لینک آهنگ", "data": { "action": "copy-link" } }
            ] },
            { "type": "ProgressBar", "value": "${progress}", "max": 100, "color": "Accent", "label": "${progressLabel}", "spacing": "Small", "isVisible": "${hasProgress}", "fallback": { "type": "TextBlock", "text": "${progressLabel}", "size": "Small", "wrap": true } },
            { "type": "TextBlock", "text": "${detailsText}", "size": "Small", "isSubtle": true, "wrap": false, "horizontalAlignment": "Right", "spacing": "Small", "isVisible": "${hasDetails}" },
            { "type": "TextBlock", "text": "${statusMessage}", "wrap": false, "size": "Small", "horizontalAlignment": "Right", "spacing": "Small", "isVisible": "${hasStatus}" }
          ]
        }
        """);

    private static readonly string ClientTemplateJson = WithContentInsets("""
        {
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "type": "AdaptiveCard", "version": "1.5", "rtl": true,
          "body": [
            { "type": "ColumnSet", "spacing": "None", "columns": [
              { "type": "Column", "width": "stretch", "verticalContentAlignment": "Center", "items": [
                { "type": "TextBlock", "text": "MIZON / پروفایل", "size": "Small", "weight": "Bolder", "color": "Accent", "horizontalAlignment": "Right", "spacing": "None" },
                { "type": "TextBlock", "text": "${title}", "size": "Medium", "weight": "Bolder", "wrap": true, "maxLines": 2, "horizontalAlignment": "Right", "spacing": "None", "rtl": "${titleRtl}" },
                { "type": "TextBlock", "text": "${secondaryTitle}", "color": "Accent", "wrap": false, "horizontalAlignment": "Right", "rtl": false, "spacing": "Small", "isVisible": "${hasSecondaryTitle}" },
                { "type": "TextBlock", "text": "${fullName}", "size": "Small", "isSubtle": true, "wrap": false, "horizontalAlignment": "Right", "spacing": "Small", "isVisible": "${hasDistinctFullName}" }
              ] },
              { "type": "Column", "width": "80px", "spacing": "Medium", "verticalContentAlignment": "Center", "items": [
                { "type": "Image", "url": "${imageUri}", "width": "80px", "height": "80px", "style": "Person", "altText": "${imageAlt}", "isVisible": "${hasImage}" },
                { "type": "TextBlock", "text": "${initials}", "size": "ExtraLarge", "weight": "Bolder", "color": "Accent", "isVisible": "${hasNoImage}" }
              ] }
            ] },
            { "type": "ActionSet", "spacing": "Medium", "actions": [
              { "type": "Action.Submit", "title": "مشاهدهٔ پروفایل", "data": { "action": "profile" } },
              { "type": "Action.Submit", "title": "کپی لینک", "data": { "action": "copy-link" } }
            ] },
            { "type": "TextBlock", "text": "${statusMessage}", "wrap": false, "size": "Small", "horizontalAlignment": "Right", "spacing": "Small", "isVisible": "${hasStatus}" }
          ]
        }
        """);

    internal static string WithContentInsets(string template)
    {
        using var document = JsonDocument.Parse(template);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name != "body") { property.WriteTo(writer); continue; }
                writer.WriteStartArray("body");
                WriteVerticalInset(writer);
                foreach (var row in property.Value.EnumerateArray())
                {
                    var isActionRow = row.GetProperty("type").GetString() == "ActionSet";
                    if (isActionRow) WriteVerticalInset(writer, 8, row);
                    writer.WriteStartObject();
                    writer.WriteString("type", "ColumnSet");
                    if (!isActionRow && row.TryGetProperty("spacing", out var spacing))
                        writer.WriteString("spacing", spacing.GetString());
                    else writer.WriteString("spacing", "None");
                    // Omit the entire row, including its gutters and spacing,
                    // when the original template element is not applicable.
                    if (row.TryGetProperty("isVisible", out var visible))
                    { writer.WritePropertyName("isVisible"); visible.WriteTo(writer); }
                    writer.WriteStartArray("columns");
                    WriteHorizontalInset(writer);
                    writer.WriteStartObject();
                    writer.WriteString("type", "Column");
                    writer.WriteString("width", "stretch");
                    writer.WriteString("spacing", "None");
                    writer.WriteStartArray("items");
                    row.WriteTo(writer);
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                    WriteHorizontalInset(writer);
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                    if (isActionRow) WriteVerticalInset(writer, 8, row);
                }
                WriteVerticalInset(writer);
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    private static void WriteHorizontalInset(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "Column");
        writer.WriteString("width", "12px");
        writer.WriteString("spacing", "None");
        writer.WriteStartArray("items"); writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteVerticalInset(Utf8JsonWriter writer, int height = 12, JsonElement? source = null)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "Container");
        writer.WriteString("minHeight", $"{height}px");
        writer.WriteString("spacing", "None");
        if (source is { } row && row.TryGetProperty("isVisible", out var visible))
        { writer.WritePropertyName("isVisible"); visible.WriteTo(writer); }
        writer.WriteStartArray("items"); writer.WriteEndArray();
        writer.WriteEndObject();
    }
    // Retain this attachment for the lifetime of the cached native result.
    internal static IDisposable Attach(nint nativeItem, IPaletteResultState state)
    {
        var view = new PreviewView(state);
        PreviewContent? content = null;
        ContentCallback? callback = null;
        try
        {
            content = new PreviewContent(view.Identity);
            callback = new ContentCallback(content.Identity, state);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)NativeMethods.Table(nativeItem)[28])(nativeItem, callback.Identity));
            return new Attachment(view);
        }
        catch { NativeMethods.Release(view.Identity); throw; }
        finally
        {
            if (callback is not null) NativeMethods.Release(callback.Identity);
            if (content is not null) NativeMethods.Release(content.Identity);
        }
    }

    private sealed class Attachment : IDisposable
    {
        private PreviewView? _view;
        internal Attachment(PreviewView view) => _view = view;
        public void Dispose()
        {
            var view = Interlocked.Exchange(ref _view, null);
            if (view is null) return;
            view.Close();
            NativeMethods.Release(view.Identity);
        }
    }

    internal static nint BuildPreview(IPaletteResultState state)
    {
        var view = new PreviewView(state);
        try { return new PreviewContent(view.Identity).Identity; }
        finally { NativeMethods.Release(view.Identity); }
    }

    internal static string GetTemplate(PaletteResultKind kind) => kind == PaletteResultKind.Music ? MusicTemplateJson : ClientTemplateJson;

    internal static string BuildData(PaletteResultData result, PaletteResultSnapshot state)
    {
        var imageUri = SafeImageUri(state.ImageUri);
        var title = result.DisplayTitle;
        var secondaryTitle = result.SecondaryTitle;
        var releaseDate = result.ReleaseDate?.ToString("yyyy/MM/dd", System.Globalization.CultureInfo.GetCultureInfo("fa-IR")) ?? string.Empty;
        var metrics = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(result.DurationText)) metrics.Add(result.DurationText);
        if (result.LikesCount is { } likes) metrics.Add($"{likes:N0} پسند");
        if (result.ViewsCount is { } views) metrics.Add($"{views:N0} بازدید");
        var details = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(result.AlbumTitle)) details.Add($"آلبوم {result.AlbumTitle}");
        if (!string.IsNullOrWhiteSpace(result.GenreText)) details.Add(result.GenreText);
        if (releaseDate.Length > 0) details.Add($"انتشار {releaseDate}");
        var progress = state.DownloadProgress is { } percentage ? Math.Clamp(percentage, 0, 100) : 0;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("imageUri", imageUri);
            writer.WriteBoolean("hasImage", imageUri.Length > 0);
            writer.WriteBoolean("hasNoImage", imageUri.Length == 0);
            writer.WriteString("imageAlt", $"{(result.Kind == PaletteResultKind.Music ? "کاور آهنگ" : "عکس پروفایل")} {title}");
            writer.WriteString("title", title);
            writer.WriteBoolean("titleRtl", IsRightToLeft(title));
            writer.WriteString("secondaryTitle", secondaryTitle);
            writer.WriteBoolean("hasSecondaryTitle", secondaryTitle.Length > 0);
            writer.WriteBoolean("hasLikes", result.LikesCount.HasValue);
            writer.WriteString("likesText", result.LikesCount?.ToString("N0") ?? string.Empty);
            writer.WriteBoolean("hasViews", result.ViewsCount.HasValue);
            writer.WriteString("viewsText", result.ViewsCount?.ToString("N0") ?? string.Empty);
            writer.WriteBoolean("hasDuration", !string.IsNullOrWhiteSpace(result.DurationText));
            writer.WriteString("durationText", result.DurationText ?? string.Empty);
            writer.WriteString("durationMetric", $"زمان {result.DurationText}");
            writer.WriteString("likesMetric", result.LikesCount is { } likeCount ? $"{likeCount:N0} پسند" : string.Empty);
            writer.WriteString("viewsMetric", result.ViewsCount is { } viewCount ? $"{viewCount:N0} بازدید" : string.Empty);
            writer.WriteBoolean("hasMetrics", result.LikesCount.HasValue || result.ViewsCount.HasValue || !string.IsNullOrWhiteSpace(result.DurationText));
            writer.WriteString("metricsText", string.Join("  ·  ", metrics));
            writer.WriteString("detailsText", string.Join("  ·  ", details));
            writer.WriteBoolean("hasAlbum", !string.IsNullOrWhiteSpace(result.AlbumTitle));
            writer.WriteString("albumTitle", result.AlbumTitle ?? string.Empty);
            writer.WriteBoolean("hasGenres", !string.IsNullOrWhiteSpace(result.GenreText));
            writer.WriteString("genreText", result.GenreText ?? string.Empty);
            writer.WriteBoolean("hasReleaseDate", result.ReleaseDate.HasValue);
            writer.WriteString("releaseDate", releaseDate);
            writer.WriteBoolean("hasDetails", !string.IsNullOrWhiteSpace(result.AlbumTitle) || !string.IsNullOrWhiteSpace(result.GenreText) || result.ReleaseDate.HasValue);
            writer.WriteString("initials", System.Globalization.StringInfo.GetNextTextElement(title));
            writer.WriteString("firstName", result.FirstName?.Trim() ?? string.Empty);
            writer.WriteString("lastName", result.LastName?.Trim() ?? string.Empty);
            writer.WriteString("fullName", result.FullName);
            writer.WriteBoolean("hasDistinctFullName", result.FullName.Length > 0 && !string.Equals(result.FullName, title, StringComparison.Ordinal));
            writer.WriteBoolean("hasFirstName", !string.IsNullOrWhiteSpace(result.FirstName));
            writer.WriteBoolean("hasLastName", !string.IsNullOrWhiteSpace(result.LastName));
            writer.WriteBoolean("hasNames", !string.IsNullOrWhiteSpace(result.FirstName) || !string.IsNullOrWhiteSpace(result.LastName));
            writer.WriteBoolean("canDownload", state.CanDownload);
            writer.WriteBoolean("cannotDownload", !state.CanDownload);
            writer.WriteBoolean("canStartDownload", state.CanDownload && !state.IsBusy);
            writer.WriteString("downloadLabel", state.IsBusy ? "لغو" : "دانلود");
            writer.WriteString("downloadTooltip", state.IsBusy ? "لغو دانلود آهنگ" : "دانلود آهنگ");
            writer.WriteString("downloadAction", state.IsBusy ? "cancel-download" : "download");
            writer.WriteBoolean("hasProgress", state.DownloadProgress.HasValue);
            writer.WriteNumber("progress", progress);
            writer.WriteString("progressLabel", $"دانلود آهنگ · {progress}٪");
            writer.WriteString("statusMessage", state.StatusMessage ?? string.Empty);
            writer.WriteBoolean("hasStatus", !string.IsNullOrWhiteSpace(state.StatusMessage));
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    private static string SafeImageUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "file" or "ms-appx"
        ? uri.AbsoluteUri : string.Empty;

    private static bool IsRightToLeft(string value)
    {
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Value is >= 0x0590 and <= 0x08FF or >= 0xFB1D and <= 0xFDFF or >= 0xFE70 and <= 0xFEFF) return true;
            if (System.Text.Rune.IsLetter(rune)) return false;
        }
        return true;
    }

    private sealed class ContentCallback : NativeObject
    {
        private static readonly Guid InterfaceId = new("aa523f7d-e64d-514f-8fd7-35d65e866ee7");
        private static readonly nint Table = CreateTable();
        private nint _content;
        private readonly IPaletteResultState _state;
        private int _requests;

        internal ContentCallback(nint content, IPaletteResultState state) : base(inspectable: false)
        {
            _state = state;
            try
            {
                _content = content;
                NativeMethods.AddRef(content);
                AddInterface(InterfaceId, Table);
                EnableMarshaling();
            }
            catch { Dispose(); throw; }
        }

        internal override string RuntimeClassName => "Windows.UI.Palettes.PaletteContentDelegate";
        private static nint CreateTable()
        {
            var table = (nint*)NewTable(4, inspectable: false);
            table[3] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)&Invoke;
            return (nint)table;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int Invoke(nint self, nint request, nint* operation)
        {
            if (operation == null) return NativeMethods.PointerError;
            *operation = 0;
            try
            {
                var owner = Owner<ContentCallback>(self);
                Log.Write($"Preview requested: {NativeMethods.ReadString(request)}");
                try { owner._state.RequestDetails(); }
                catch (Exception error) { Log.Error("Preview.RequestDetails", error); }
                if (Log.PreviewDiagnosticsEnabled)
                {
                    var image = owner._state.GetSnapshot().ImageUri;
                    var localImage = Uri.TryCreate(image, UriKind.Absolute, out var uri) && uri.IsFile;
                    Log.Write($"Preview.Selection: key={owner._state.Result.Key}, selection={Interlocked.Increment(ref owner._requests)}, hasImage={!string.IsNullOrEmpty(image)}, local={localImage}, exists={localImage && File.Exists(uri!.LocalPath)}");
                }
                *operation = new CompletedOperation(owner._content, content: true).Identity;
                return 0;
            }
            catch (Exception error) { return Log.Error("Preview.Content", error); }
        }

        protected override void ReleaseResources()
        {
            NativeMethods.Release(_content);
            _content = 0;
            Log.Write("Preview content callback released.");
        }
    }

    // Separate content and view identities prevent the native preview build's
    // cached TemplateContent <-> TemplateContentView reference cycle.
    private sealed class PreviewContent : NativeObject
    {
        private static readonly nint Table = CreateTable();
        private nint _view;
        internal PreviewContent(nint view)
        {
            try
            {
                _view = NativeMethods.As(view, ContentViewId);
                AddInterface(ContentId, Table);
                EnableMarshaling();
            }
            catch { Dispose(); throw; }
        }
        internal override string RuntimeClassName => "Windows.UI.Palettes.PaletteContent";
        private static nint CreateTable()
        {
            var table = (nint*)NewTable(8);
            table[6] = (nint)(delegate* unmanaged[Stdcall]<nint, int*, int>)&GetKind;
            table[7] = (nint)(delegate* unmanaged[Stdcall]<nint, nint*, int>)&GetView;
            return (nint)table;
        }
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int GetKind(nint self, int* value) { if (value == null) return NativeMethods.PointerError; *value = 1; return 0; }
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int GetView(nint self, nint* value)
        {
            if (value == null) return NativeMethods.PointerError;
            *value = Owner<PreviewContent>(self)._view;
            NativeMethods.AddRef(*value);
            return 0;
        }
        protected override void ReleaseResources() { NativeMethods.Release(_view); _view = 0; }
    }

    private sealed class PreviewView : NativeObject
    {
        private static readonly nint TemplateTable = CreateTemplateTable();
        private static readonly nint ContentTable = CreateContentTable();
        private readonly Lock _gate = new();
        private readonly IPaletteResultState _state;
        private string _template;
        private readonly Dictionary<long, nint> _handlers = [];
        private long _nextToken;
        private string _data;
        private bool _closed;

        internal PreviewView(IPaletteResultState state)
        {
            _state = state;
            (_template, _data) = ReadPreview();
            try
            {
                AddInterface(TemplateViewId, TemplateTable);
                AddInterface(ContentViewId, ContentTable);
                EnableMarshaling();
                state.Changed += OnChanged;
            }
            catch { Dispose(); throw; }
        }
        internal override string RuntimeClassName => "Windows.UI.Palettes.PaletteTemplateContentView";

        private (string Template, string Data) ReadPreview()
        {
            if (_state is IPaletteCustomPreview custom) return custom.GetPreview();
            var data = BuildData(_state.Result, _state.GetSnapshot());
            return (PaletteCardTemplate.Expand(PalettePreviewData.GetTemplate(_state.Result.Kind), data), data);
        }

        private static nint CreateTemplateTable()
        {
            var table = (nint*)NewTable(11);
            table[6] = (nint)(delegate* unmanaged[Stdcall]<nint, nint*, int>)&GetTemplate;
            table[7] = (nint)(delegate* unmanaged[Stdcall]<nint, nint*, int>)&GetData;
            table[8] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, nint, nint, int>)&Invoke;
            table[9] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, long*, int>)&AddChanged;
            table[10] = (nint)(delegate* unmanaged[Stdcall]<nint, long, int>)&RemoveChanged;
            return (nint)table;
        }
        private static nint CreateContentTable()
        {
            var table = (nint*)NewTable(7);
            table[6] = (nint)(delegate* unmanaged[Stdcall]<nint, int*, int>)&GetKind;
            return (nint)table;
        }
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int GetKind(nint self, int* value) { if (value == null) return NativeMethods.PointerError; *value = 1; return 0; }
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int GetTemplate(nint self, nint* value)
        {
            var owner = Owner<PreviewView>(self);
            lock (owner._gate)
            {
                if (Log.PreviewDiagnosticsEnabled)
                    Log.Write($"Preview.GetTemplate: key={owner._state.Result.Key}, kind={owner._state.Result.Kind}, chars={owner._template.Length}, closed={owner._closed}");
                return GetString(value, owner._template);
            }
        }
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int GetData(nint self, nint* value)
        {
            var owner = Owner<PreviewView>(self);
            lock (owner._gate)
            {
                if (Log.PreviewDiagnosticsEnabled)
                    Log.Write($"Preview.GetData: key={owner._state.Result.Key}, chars={owner._data.Length}, handlers={owner._handlers.Count}, closed={owner._closed}");
                return GetString(value, owner._data);
            }
        }
        private static int GetString(nint* value, string text)
        {
            if (value == null) return NativeMethods.PointerError;
            *value = 0;
            try { *value = NativeMethods.String(text); return 0; }
            catch (Exception error) { return Log.Error("Preview.GetString", error); }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int Invoke(nint self, nint actionType, nint actionJson, nint inputsJson)
        {
            try
            {
                var owner = Owner<PreviewView>(self);
                lock (owner._gate) if (owner._closed) return unchecked((int)0x80000013);
                if (owner._state is IPaletteCustomPreview custom)
                {
                    var type = NativeMethods.ReadString(actionType);
                    var actionPayload = NativeMethods.ReadString(actionJson);
                    custom.InvokeCard(type, actionPayload, NativeMethods.ReadString(inputsJson));
                    // Trace control identities, never the user's input text.
                    var command = type.StartsWith("demo-", StringComparison.Ordinal) ? type : ReadAction(actionPayload);
                    if (command is { Length: <= 64 } && command.StartsWith("demo-", StringComparison.Ordinal))
                        Log.Write($"Capability demo action: {command}");
                    return 0;
                }
                var action = ReadAction(NativeMethods.ReadString(actionJson)) ?? ReadAction(NativeMethods.ReadString(inputsJson));
                Log.Write($"Preview action type={NativeMethods.ReadString(actionType)}, command={action ?? "<none>"}");
                if (action is not null) owner._state.Invoke(action);
                return 0;
            }
            catch (Exception error) { return Log.Error("Preview.Action", error); }
        }

        private static string? ReadAction(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 16_384) return null;
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;
                if (root.TryGetProperty("action", out var action) && action.ValueKind == JsonValueKind.String)
                    return action.GetString();
                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("action", out action) && action.ValueKind == JsonValueKind.String)
                    return action.GetString();
                return null;
            }
            catch (JsonException) { return null; }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int AddChanged(nint self, nint handler, long* token)
        {
            if (token == null || handler == 0) return NativeMethods.PointerError;
            *token = 0;
            try
            {
                var owner = Owner<PreviewView>(self);
                lock (owner._gate)
                {
                    if (owner._closed) return unchecked((int)0x80000013);
                    var id = ++owner._nextToken;
                    NativeMethods.AddRef(handler);
                    try { owner._handlers.Add(id, handler); }
                    catch { NativeMethods.Release(handler); throw; }
                    *token = id;
                    if (Log.PreviewDiagnosticsEnabled)
                        Log.Write($"Preview.AddChanged: key={owner._state.Result.Key}, handlers={owner._handlers.Count}, token={id}");
                }
                return 0;
            }
            catch (Exception error) { return Log.Error("Preview.AddChanged", error); }
        }
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int RemoveChanged(nint self, long token)
        {
            var owner = Owner<PreviewView>(self);
            nint handler;
            lock (owner._gate)
            {
                owner._handlers.Remove(token, out handler);
                if (Log.PreviewDiagnosticsEnabled)
                    Log.Write($"Preview.RemoveChanged: key={owner._state.Result.Key}, handlers={owner._handlers.Count}, token={token}");
            }
            NativeMethods.Release(handler);
            return 0;
        }
        private void OnChanged()
        {
            nint[] handlers;
            bool templateChanged;
            lock (_gate)
            {
                if (_closed) return;
                var (template, data) = ReadPreview();
                if (string.Equals(data, _data, StringComparison.Ordinal) && string.Equals(template, _template, StringComparison.Ordinal)) return;
                templateChanged = !string.Equals(template, _template, StringComparison.Ordinal);
                _template = template;
                _data = data;
                if (Log.PreviewDiagnosticsEnabled)
                    Log.Write($"Preview.StateChanged: key={_state.Result.Key}, templateChanged={templateChanged}, dataChars={data.Length}, handlers={_handlers.Count}");
                handlers = _handlers.Values.ToArray();
                foreach (var handler in handlers) NativeMethods.AddRef(handler);
                NativeMethods.AddRef(Identity);
            }
            PropertyChangedArgs? args = null;
            try
            {
                if (handlers.Length == 0) return;
                if (templateChanged)
                {
                    args = new PropertyChangedArgs("TemplateJson");
                    NotifyChanged(handlers, args.Identity);
                    NativeMethods.Release(args.Identity);
                    args = null;
                }
                args = new PropertyChangedArgs("DataJson");
                NotifyChanged(handlers, args.Identity);
            }
            catch (Exception error) { Log.Error("Preview.DataChanged", error); }
            finally
            {
                if (args is not null) NativeMethods.Release(args.Identity);
                foreach (var handler in handlers) NativeMethods.Release(handler);
                NativeMethods.Release(Identity);
            }
        }

        private void NotifyChanged(nint[] handlers, nint arguments)
        {
            foreach (var handler in handlers)
            {
                var hr = ((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)NativeMethods.Table(handler)[3])(handler, Identity, arguments);
                if (hr < 0) Log.Write($"Preview.PropertyChanged callback returned 0x{hr:X8}");
            }
        }
        internal void Close()
        {
            nint[] handlers;
            lock (_gate)
            {
                if (_closed) return;
                _closed = true;
                handlers = _handlers.Values.ToArray();
                _handlers.Clear();
            }
            _state.Changed -= OnChanged;
            foreach (var handler in handlers) NativeMethods.Release(handler);
        }
        protected override void ReleaseResources() => Close();
    }

    private sealed class PropertyChangedArgs : NativeObject
    {
        private static readonly Guid InterfaceId = new("0e40bb5f-96a6-52ac-af88-f4e1e3025e1d");
        private static readonly nint Table = CreateTable();
        private readonly string _name;
        internal PropertyChangedArgs(string name)
        {
            _name = name;
            try { AddInterface(InterfaceId, Table); EnableMarshaling(); }
            catch { Dispose(); throw; }
        }
        internal override string RuntimeClassName => "Windows.UI.Palettes.PalettePropertyChangedEventArgs";
        private static nint CreateTable()
        {
            var table = (nint*)NewTable(7);
            table[6] = (nint)(delegate* unmanaged[Stdcall]<nint, nint*, int>)&GetName;
            return (nint)table;
        }
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int GetName(nint self, nint* value)
        {
            if (value == null) return NativeMethods.PointerError;
            *value = 0;
            try { *value = NativeMethods.String(Owner<PropertyChangedArgs>(self)._name); return 0; }
            catch (Exception error) { return Log.Error("Preview.PropertyName", error); }
        }
    }
}
