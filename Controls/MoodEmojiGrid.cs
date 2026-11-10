using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Mizon.Windows.Controls;

public sealed partial class MoodEmojiGrid : GridView
{
    protected override DependencyObject GetContainerForItemOverride() => new MoodEmojiGridItem();
    protected override bool IsItemItsOwnContainerOverride(object item) => item is MoodEmojiGridItem;
}

// Set the protected cursor directly; no reflection or native cursor handles leak on recycling.
public sealed partial class MoodEmojiGridItem : GridViewItem
{
    private InputSystemCursor? _cursor;
    public MoodEmojiGridItem()
    {
        DefaultStyleKey = typeof(GridViewItem);
        Loaded += Item_Loaded;
        Unloaded += Item_Unloaded;
    }

    private void Item_Loaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded) ProtectedCursor = _cursor ??= InputSystemCursor.Create(InputSystemCursorShape.Hand);
    }

    private void Item_Unloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded) return;
        ProtectedCursor = null;
        _cursor?.Dispose();
        _cursor = null;
    }
}
