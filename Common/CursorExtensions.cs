using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using System.Reflection;

namespace Mizon.Windows.Common;

public static class CursorExtensions
{
    public static void ChangeCursor(this UIElement uiElement, InputCursor cursor)
    {
        var type = typeof(UIElement);
        type.InvokeMember("ProtectedCursor", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.SetProperty | BindingFlags.Instance, null, uiElement, new object[] { cursor });
    }

    public static InputSystemCursorShape GetCursor(UIElement element)
            => (InputSystemCursorShape)element.GetValue(CursorProperty);

    public static void SetCursor(UIElement element, InputSystemCursorShape value)
        => element.SetValue(CursorProperty, value);

    public static readonly DependencyProperty CursorProperty =
        DependencyProperty.RegisterAttached(
            "Cursor",
            typeof(InputSystemCursorShape),
            typeof(CursorExtensions),
            new PropertyMetadata(InputSystemCursorShape.Arrow, OnCursorChanged));

    private static void OnCursorChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            var shape = (InputSystemCursorShape)e.NewValue;
            var cursor = InputSystemCursor.Create(shape);
            element.ChangeCursor(cursor);
        }
    }
}
