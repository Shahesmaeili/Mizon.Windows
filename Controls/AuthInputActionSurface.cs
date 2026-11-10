using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Mizon.Windows.Controls;

// Sets the cursor through the protected WinUI API, without reflection under Native AOT.
public sealed partial class AuthInputActionSurface : Grid
{
    private InputSystemCursor? _cursor;
    private bool _attached;

    public AuthInputActionSurface()
    {
        Loaded += SurfaceLoaded;
        Unloaded += SurfaceUnloaded;
    }

    private void SurfaceLoaded(object sender, RoutedEventArgs args)
    {
        if (_attached || !IsLoaded) return;
        _attached = true;
        PointerEntered += SurfacePointerEntered;
        SetHandCursor();
    }

    private void SurfacePointerEntered(object sender, PointerRoutedEventArgs args) => SetHandCursor();

    private void SetHandCursor() => ProtectedCursor = _cursor ??= InputSystemCursor.Create(InputSystemCursorShape.Hand);

    private void SurfaceUnloaded(object sender, RoutedEventArgs args)
    {
        if (!_attached || IsLoaded) return;
        _attached = false;
        PointerEntered -= SurfacePointerEntered;
        ProtectedCursor = null;
        _cursor?.Dispose();
        _cursor = null;
    }
}
