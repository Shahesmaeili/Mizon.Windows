using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Mizon.Windows.Controls;

// Native button templates and automation peers; only cursor ownership differs.
public sealed partial class FeatureButton : Button
{
    private InputSystemCursor? _cursor;

    public FeatureButton()
    {
        DefaultStyleKey = typeof(Button);
        Loaded += Button_Loaded;
        Unloaded += Button_Unloaded;
    }

    private void Button_Loaded(object sender, RoutedEventArgs args)
    {
        if (!IsLoaded || _cursor is not null) return;
        ProtectedCursor = _cursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    }

    private void Button_Unloaded(object sender, RoutedEventArgs args)
    {
        // Reparenting can deliver an earlier Unloaded after the same control reloads.
        if (IsLoaded) return;
        ProtectedCursor = null;
        _cursor?.Dispose();
        _cursor = null;
    }
}

public sealed partial class FeatureToggleButton : ToggleButton
{
    private InputSystemCursor? _cursor;

    public FeatureToggleButton()
    {
        DefaultStyleKey = typeof(ToggleButton);
        Loaded += Button_Loaded;
        Unloaded += Button_Unloaded;
    }

    private void Button_Loaded(object sender, RoutedEventArgs args)
    {
        if (!IsLoaded || _cursor is not null) return;
        ProtectedCursor = _cursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    }

    private void Button_Unloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded) return;
        ProtectedCursor = null;
        _cursor?.Dispose();
        _cursor = null;
    }
}
