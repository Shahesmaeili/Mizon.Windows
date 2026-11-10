namespace Mizon.Windows.PaletteProvider;

// Use only for synchronous blocks; an await can resume on a different thread.
internal readonly struct ComApartment : IDisposable
{
    private readonly int _initialized;
    public ComApartment()
    {
        _initialized = NativeMethods.RoInitialize(1);
        if (_initialized != unchecked((int)0x80010106)) NativeMethods.Check(_initialized);
    }
    public void Dispose() { if (_initialized >= 0) NativeMethods.RoUninitialize(); }
}
