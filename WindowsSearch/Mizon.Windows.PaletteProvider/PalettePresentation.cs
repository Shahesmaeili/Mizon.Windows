namespace Mizon.Windows.PaletteProvider;

// Native presentation contracts verified against this Windows Palettes build.
// Keep these isolated from API calls and from the app's playback services.
internal static unsafe class PalettePresentation
{
    private static readonly Guid ItemId = new("095b1f54-5206-5f2b-acc5-ae72be801e11");
    private static readonly Guid ProgressFactoryId = new("dbc1b51f-0fd4-5c73-9f3e-d1de7e185b72");
    private static readonly Guid ProgressId = new("f09b2221-cc0a-5fca-953f-a9ce2d1a70a1");
    private static readonly Guid StatusFactoryId = new("4cdc0f27-985e-5f01-b1af-c82431dff1bd");
    private static readonly Guid TagFactoryId = new("64702731-160e-580f-add9-c6a8b77886c3");
    private static readonly Guid IconStaticsId = new("bde0245b-9389-565c-8bf2-0949a7be7cd4");
    private static readonly Guid KnownPropertiesId = new("adb1d8cc-57f9-5d9a-a555-67952d4d9bd5");
    private static readonly Guid MapId = new("1b0d3570-0877-5ec2-8a2c-3b9539506aca");
    private static readonly Guid PropertyValueFactoryId = new("629bdbc8-d932-4ff4-96b9-8d96c5c1e858");
    private static readonly Guid UriFactoryId = new("44a9796f-723e-4fdf-a218-033e75b0c084");

    internal readonly record struct Tag(string Text, string Glyph, string ToolTip);

    internal static IDisposable AttachToItem(nint nativeMutableItem, IPaletteResultState state) => new Attachment(nativeMutableItem, state);

    internal static void ConfigureSearchFallback(nint item)
    {
        SetCommandGlyph(item, "\uE721");
        ConfigureActionSubtitle(item, "\uE721");
        SetStringProperty(item, "CommandTooltip", PaletteSearchFallback.Title);
        SetStringProperty(item, "ExecuteCommandTooltip", PaletteSearchFallback.Title);
        SetRelevanceScore(item, 0.35);
    }

    private static void ConfigureActionSubtitle(nint item, string glyph)
    {
        // Verified provider properties in the installed host. Accent color itself
        // is chosen by Windows; this does not impersonate a WindowsAction kind.
        SetStringProperty(item, "SubtitleGlyph", glyph);
        SetBooleanProperty(item, "ProminentFirstSubtitleElement", true);
        // Search's relative times describe command history, not release/account
        // dates. Do not add these live entities to that history going forward.
        SetBooleanProperty(item, "ExcludeFromHistory", true);
    }

    // Caller owns the returned icon reference.
    internal static nint CreateGlyphIcon(string glyph)
    {
        nint factory = 0, text = 0;
        try
        {
            factory = NativeMethods.Factory("Windows.UI.Palettes.PaletteIconData", IconStaticsId);
            text = NativeMethods.String(glyph);
            nint icon = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(factory)[9])(factory, text, &icon));
            return icon;
        }
        finally { NativeMethods.WindowsDeleteString(text); NativeMethods.Release(factory); }
    }

    internal static nint CreateImageIcon(string address)
    {
        nint uriFactory = 0, uri = 0, factory = 0, text = 0;
        try
        {
            uriFactory = NativeMethods.Factory("Windows.Foundation.Uri", UriFactoryId);
            text = NativeMethods.String(address);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(uriFactory)[6])(uriFactory, text, &uri));
            factory = NativeMethods.Factory("Windows.UI.Palettes.PaletteIconData", IconStaticsId);
            nint icon = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(factory)[8])(factory, uri, &icon));
            return icon;
        }
        finally
        {
            NativeMethods.Release(factory); NativeMethods.Release(uri);
            NativeMethods.WindowsDeleteString(text); NativeMethods.Release(uriFactory);
        }
    }

    internal static void SetTags(nint item, params ReadOnlySpan<Tag> tags)
    {
        if (tags.Length > 16) throw new ArgumentOutOfRangeException(nameof(tags));
        Span<nint> instances = stackalloc nint[tags.Length];
        instances.Clear();
        nint factory = 0, mutable = 0;
        try
        {
            mutable = NativeMethods.As(item, ItemId);
            factory = NativeMethods.Factory("Windows.UI.Palettes.PaletteTag", TagFactoryId);
            for (var index = 0; index < tags.Length; index++)
            {
                nint icon = 0, text = 0, tooltip = 0;
                try
                {
                    // PaletteTag validates a non-null icon and a non-empty label.
                    icon = CreateGlyphIcon(tags[index].Glyph);
                    text = NativeMethods.String(tags[index].Text);
                    tooltip = NativeMethods.String(tags[index].ToolTip);
                    nint tag = 0;
                    NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint, nint, nint*, int>)NativeMethods.Table(factory)[6])(
                        factory, icon, text, tooltip, &tag));
                    instances[index] = tag;
                }
                finally { NativeMethods.WindowsDeleteString(tooltip); NativeMethods.WindowsDeleteString(text); NativeMethods.Release(icon); }
            }
            fixed (nint* values = instances)
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)NativeMethods.Table(mutable)[17])(
                    mutable, (uint)instances.Length, values));
        }
        finally
        {
            foreach (var instance in instances) NativeMethods.Release(instance);
            NativeMethods.Release(factory);
            NativeMethods.Release(mutable);
        }
    }

    internal static void SetCommandGlyph(nint item, string glyph)
    {
        nint properties = 0, key = 0;
        try
        {
            properties = NativeMethods.Factory("Windows.UI.Palettes.KnownPaletteProperties", KnownPropertiesId);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(properties)[7])(properties, &key));
            SetStringProperty(item, NativeMethods.ReadString(key), glyph);
        }
        finally { NativeMethods.WindowsDeleteString(key); NativeMethods.Release(properties); }
    }

    internal static void SetStringProperty(nint item, string key, string value)
    {
        nint mutable = 0, properties = 0, map = 0, factory = 0, name = 0, text = 0, boxed = 0;
        try
        {
            mutable = NativeMethods.As(item, ItemId);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(mutable)[18])(mutable, &properties));
            map = NativeMethods.As(properties, MapId);
            factory = NativeMethods.Factory("Windows.Foundation.PropertyValue", PropertyValueFactoryId);
            name = NativeMethods.String(key);
            text = NativeMethods.String(value);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(factory)[18])(factory, text, &boxed));
            byte replaced = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint, byte*, int>)NativeMethods.Table(map)[10])(map, name, boxed, &replaced));
        }
        finally
        {
            NativeMethods.Release(boxed); NativeMethods.WindowsDeleteString(text); NativeMethods.WindowsDeleteString(name);
            NativeMethods.Release(factory); NativeMethods.Release(map); NativeMethods.Release(properties); NativeMethods.Release(mutable);
        }
    }

    internal static void SetBooleanProperty(nint item, string key, bool value)
    {
        nint mutable = 0, properties = 0, map = 0, factory = 0, name = 0, boxed = 0;
        try
        {
            mutable = NativeMethods.As(item, ItemId);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(mutable)[18])(mutable, &properties));
            map = NativeMethods.As(properties, MapId);
            factory = NativeMethods.Factory("Windows.Foundation.PropertyValue", PropertyValueFactoryId);
            name = NativeMethods.String(key);
            // IPropertyValueStatics.CreateBoolean; WinRT Boolean occupies one byte.
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, byte, nint*, int>)NativeMethods.Table(factory)[17])(factory, value ? (byte)1 : (byte)0, &boxed));
            byte replaced = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint, byte*, int>)NativeMethods.Table(map)[10])(map, name, boxed, &replaced));
        }
        finally
        {
            NativeMethods.Release(boxed); NativeMethods.WindowsDeleteString(name);
            NativeMethods.Release(factory); NativeMethods.Release(map); NativeMethods.Release(properties); NativeMethods.Release(mutable);
        }
    }

    internal static void SetRelevanceScore(nint item, double? score)
    {
        if (score is { } value && (!double.IsFinite(value) || value is < 0 or > 1))
            throw new ArgumentOutOfRangeException(nameof(score));

        nint mutable = 0, properties = 0, map = 0, knownProperties = 0, name = 0, factory = 0, boxed = 0;
        try
        {
            mutable = NativeMethods.As(item, ItemId);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(mutable)[18])(mutable, &properties));
            map = NativeMethods.As(properties, MapId);
            knownProperties = NativeMethods.Factory("Windows.UI.Palettes.KnownPaletteProperties", KnownPropertiesId);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(knownProperties)[11])(knownProperties, &name));
            if (score is { } relevance)
            {
                factory = NativeMethods.Factory("Windows.Foundation.PropertyValue", PropertyValueFactoryId);
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, double, nint*, int>)NativeMethods.Table(factory)[15])(factory, relevance, &boxed));
                byte replaced = 0;
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint, byte*, int>)NativeMethods.Table(map)[10])(map, name, boxed, &replaced));
            }
            else
            {
                byte exists = 0;
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, byte*, int>)NativeMethods.Table(map)[8])(map, name, &exists));
                if (exists != 0)
                    NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)NativeMethods.Table(map)[11])(map, name));
            }
        }
        finally
        {
            NativeMethods.Release(boxed); NativeMethods.Release(factory); NativeMethods.WindowsDeleteString(name);
            NativeMethods.Release(knownProperties); NativeMethods.Release(map); NativeMethods.Release(properties); NativeMethods.Release(mutable);
        }
    }

    internal static void SetIconOverlay(nint item, string? glyph)
    {
        nint mutable = 0, icon = 0;
        try
        {
            mutable = NativeMethods.As(item, ItemId);
            if (!string.IsNullOrEmpty(glyph)) icon = CreateGlyphIcon(glyph);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)NativeMethods.Table(mutable)[11])(mutable, icon));
        }
        finally { NativeMethods.Release(icon); NativeMethods.Release(mutable); }
    }

    private static nint CreateProgress()
    {
        nint factory = 0, instance = 0;
        try
        {
            factory = NativeMethods.Factory("Windows.UI.Palettes.PaletteProgressState", ProgressFactoryId);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, byte, float, nint*, int>)NativeMethods.Table(factory)[6])(factory, 0, 0, &instance));
            return NativeMethods.As(instance, ProgressId);
        }
        finally { NativeMethods.Release(instance); NativeMethods.Release(factory); }
    }

    private static void SetStatus(nint item, nint progress, string message)
    {
        nint factory = 0, text = 0, status = 0;
        try
        {
            factory = NativeMethods.Factory("Windows.UI.Palettes.PaletteStatus", StatusFactoryId);
            text = NativeMethods.String(message);
            if (progress == 0)
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, int, nint*, int>)NativeMethods.Table(factory)[6])(
                    factory, text, 0, &status));
            else
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, int, nint, nint*, int>)NativeMethods.Table(factory)[7])(
                    factory, text, 0, progress, &status));
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)NativeMethods.Table(item)[21])(item, status));
        }
        finally { NativeMethods.Release(status); NativeMethods.WindowsDeleteString(text); NativeMethods.Release(factory); }
    }

    private sealed class Attachment : IDisposable
    {
        private readonly Lock _lifetime = new();
        private readonly Lock _updates = new();
        private readonly IPaletteResultState _state;
        private nint _item;
        private nint _progress;
        private PaletteResultSnapshot? _lastSnapshot;
        private PaletteResultData? _lastResult;

        internal Attachment(nint item, IPaletteResultState state)
        {
            _state = state;
            try
            {
                _item = NativeMethods.As(item, ItemId);
                _progress = CreateProgress();
                var music = state.Result.Kind == PaletteResultKind.Music;
                var demo = state.Result.Kind == PaletteResultKind.Demo;
                SetTags(_item, new Tag(state.Result.TypeLabel, music ? "\uE189" : "\uE77B", state.Result.TypeLabel));
                SetCommandGlyph(_item, demo ? "\uE893" : music ? "\uE768" : "\uE77B");
                ConfigureActionSubtitle(_item, demo ? "\uE713" : music ? "\uE768" : "\uE77B");
                SetStringProperty(_item, "ExecuteCommandTooltip", demo ? "بخش بعدی" : music ? "پخش آهنگ در میزون" : "مشاهدهٔ پروفایل در میزون");
                SetStringProperty(_item, "CommandTooltip", demo ? "بخش بعدی" : music ? "پخش آهنگ" : "مشاهدهٔ پروفایل");
                _state.Changed += OnChanged;
                Refresh();
            }
            catch { Dispose(); throw; }
        }

        private void OnChanged()
        {
            try { Refresh(); }
            catch (Exception error) { Log.Error("Palette presentation refresh", error); }
        }

        private void Refresh()
        {
            // Serialize snapshots, but never hold the lifetime lock while calling
            // Windows or raising native PropertyChanged notifications.
            lock (_updates)
            {
                nint item, progress;
                lock (_lifetime)
                {
                    if (_item == 0) return;
                    item = _item; progress = _progress;
                    NativeMethods.AddRef(item); NativeMethods.AddRef(progress);
                }
                try
                {
                    var snapshot = _state.GetSnapshot();
                    var data = _state.Result;
                    if (_lastSnapshot == snapshot && _lastResult == data) return;
                    if (_lastResult != data)
                    {
                        PaletteData.UpdateSummary(item,data);
                        if (_lastResult?.RelevanceScore != data.RelevanceScore)
                            SetRelevanceScore(item, data.RelevanceScore);
                        var tags = new List<Tag>(4) { new(data.TypeLabel, data.Kind == PaletteResultKind.Demo ? "\uE713" : data.Kind == PaletteResultKind.Music ? "\uE189" : "\uE77B", data.TypeLabel) };
                        if (!string.IsNullOrWhiteSpace(data.DurationText)) tags.Add(new(data.DurationText, "\uE823", "مدت آهنگ"));
                        if (data.LikesCount is { } likes) tags.Add(new($"{likes:N0}", "\uEB51", "تعداد پسندها"));
                        if (data.ViewsCount is { } views) tags.Add(new($"{views:N0}", "\uE890", "تعداد بازدیدها"));
                        SetTags(item, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(tags));
                    }
                    if (_lastSnapshot is null || _lastSnapshot.Value.ImageUri != snapshot.ImageUri)
                    {
                        var hasImage = Uri.TryCreate(snapshot.ImageUri, UriKind.Absolute, out var imageUri) && imageUri.Scheme is "https" or "http" or "file" or "ms-appx";
                        var typeGlyph = data.Kind == PaletteResultKind.Demo ? "\uE713" : data.Kind == PaletteResultKind.Music ? "\uE189" : "\uE77B";
                        var icon = hasImage ? CreateImageIcon(imageUri!.AbsoluteUri) : CreateGlyphIcon(typeGlyph);
                        try { NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)NativeMethods.Table(item)[9])(item, icon)); }
                        finally { NativeMethods.Release(icon); }
                        // The badge describes the result type, never an inferred
                        // playback/online state. The glyph fallback needs no badge.
                        SetIconOverlay(item, hasImage ? typeGlyph : null);
                    }
                    // RunUX multiplies this normalized native value by 100.
                    NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, float, int>)NativeMethods.Table(progress)[9])(
                        progress, Math.Clamp(snapshot.DownloadProgress ?? 0, 0, 100) / 100f));
                    if (snapshot.DownloadProgress is { } percentage)
                    {
                        SetStatus(item, progress, $"{snapshot.StatusMessage ?? "دانلود آهنگ"} · {Math.Clamp(percentage, 0, 100)}٪");
                    }
                    else if (!string.IsNullOrWhiteSpace(snapshot.StatusMessage))
                        SetStatus(item, 0, snapshot.StatusMessage);
                    else
                        NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)NativeMethods.Table(item)[21])(item, 0));
                    _lastSnapshot = snapshot;
                    _lastResult = data;
                }
                finally { NativeMethods.Release(progress); NativeMethods.Release(item); }
            }
        }

        public void Dispose()
        {
            nint item, progress;
            lock (_lifetime)
            {
                item = _item; progress = _progress;
                _item = 0; _progress = 0;
            }
            _state.Changed -= OnChanged;
            NativeMethods.Release(progress); NativeMethods.Release(item);
        }
    }
}
