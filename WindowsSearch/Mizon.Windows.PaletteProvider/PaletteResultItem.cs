namespace Mizon.Windows.PaletteProvider;

internal sealed class PaletteResultItem(nint view, PaletteResultState state, IDisposable presentation, IDisposable preview) : IDisposable
{
    private readonly Lock _gate = new();
    private nint _view = view;
    internal PaletteResultState State => state;
    internal nint AcquireView()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_view == 0, this);
            NativeMethods.AddRef(_view); return _view;
        }
    }
    public void Dispose()
    {
        nint released;
        lock (_gate) { released = _view; _view = 0; }
        if (released == 0) return;
        state.Dispose();
        try { preview.Dispose(); }
        finally { try { presentation.Dispose(); } finally { NativeMethods.Release(released); } }
    }
}
