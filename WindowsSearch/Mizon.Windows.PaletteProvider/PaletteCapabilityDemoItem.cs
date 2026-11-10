namespace Mizon.Windows.PaletteProvider;

// One local diagnostic result. It has its own identity and never represents a
// music/client database record or starts a real download/playback operation.
internal sealed class PaletteCapabilityDemoItem : IDisposable
{
    internal const string Key = "demo/capabilities";
    private readonly Lock _gate = new();
    private readonly PaletteCapabilityDemoState _state;
    private IDisposable? _presentation;
    private IDisposable? _preview;
    private nint _view;

    internal static bool MatchesQuery(string text) => text.Trim() is var query
        && (string.Equals(query, "mizon demo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(query, "mizondemo", StringComparison.OrdinalIgnoreCase)
            || query == "دمو میزون");

    internal PaletteCapabilityDemoItem(nint factory)
    {
        _state = new PaletteCapabilityDemoState();
        nint item = 0;
        try
        {
            item = PaletteData.CreateMutableItem(factory, Key, _state.Result.DisplayTitle,
                "آزمایش قابلیت‌های Search · اطلاعات ساختگی", _state.Result.DetailsUri);
            PaletteMenuData.Attach(item, _state);
            PaletteMenuData.AttachMainAction(item, _state, "demo-next");
            _presentation = PalettePresentation.AttachToItem(item, _state);
            _preview = PalettePreviewData.Attach(item, _state);
            _view = PaletteData.GetView(item);
        }
        catch { Dispose(); throw; }
        finally { NativeMethods.Release(item); }
    }

    internal nint AcquireView()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_view == 0, this);
            NativeMethods.AddRef(_view);
            return _view;
        }
    }

    public void Dispose()
    {
        nint view;
        lock (_gate) { view = _view; _view = 0; }
        _state.Dispose();
        try { Interlocked.Exchange(ref _preview, null)?.Dispose(); }
        finally
        {
            try { Interlocked.Exchange(ref _presentation, null)?.Dispose(); }
            finally { NativeMethods.Release(view); }
        }
    }
}
