using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.System;

namespace Mizon.Windows.Controls;

// MenuFlyoutItemBase has no composable constructor. Use MenuFlyoutItem as the
// native container, but suppress its command behavior: only children act on input.
public sealed partial class MenuContentHost : MenuFlyoutItem, IDisposable
{
    private readonly PointerEventHandler _pointerHandler;
    private InputSystemCursor? _hand, _arrow, _text;
    private bool _disposed;

    public MenuContentHost()
    {
        _pointerHandler = PointerChanged;
        Loaded += HostLoaded;
        Unloaded += HostUnloaded;
    }

    // Children have already received these bubbling events. Blank space must
    // not become a menu command or let the click reach the light-dismiss layer.
    protected override void OnPointerPressed(PointerRoutedEventArgs args) => args.Handled = true;
    protected override void OnPointerReleased(PointerRoutedEventArgs args) => args.Handled = true;
    protected override void OnTapped(TappedRoutedEventArgs args) => args.Handled = true;
    protected override void OnRightTapped(RightTappedRoutedEventArgs args) => args.Handled = true;

    protected override void OnKeyDown(KeyRoutedEventArgs args)
    {
        if (IsInvokeKey(args.Key)) args.Handled = true;
        else base.OnKeyDown(args);
    }

    protected override void OnKeyUp(KeyRoutedEventArgs args)
    {
        if (IsInvokeKey(args.Key)) args.Handled = true;
        else base.OnKeyUp(args);
    }

    private static bool IsInvokeKey(VirtualKey key) => key is VirtualKey.Enter or VirtualKey.Space or VirtualKey.GamepadA or VirtualKey.NavigationAccept;

    protected override AutomationPeer OnCreateAutomationPeer() => new ContentHostAutomationPeer(this);

    private sealed partial class ContentHostAutomationPeer(MenuContentHost owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(MenuContentHost);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
    }

    private void HostLoaded(object sender, RoutedEventArgs args)
    {
        if (_disposed) return;
        AddHandler(PointerEnteredEvent, _pointerHandler, true);
        AddHandler(PointerMovedEvent, _pointerHandler, true);
        AddHandler(PointerExitedEvent, _pointerHandler, true);
    }

    internal InputSystemCursorShape CursorFor(DependencyObject? source)
    {
        var shape = InputSystemCursorShape.Arrow;
        for (var current = source; current is not null && current != this; current = VisualTreeHelper.GetParent(current))
        {
            if (current is Control { IsEnabled: false }) return InputSystemCursorShape.Arrow;
            if (current is ButtonBase or SelectorItem) shape = InputSystemCursorShape.Hand;
            else if (current is TextBox or PasswordBox or TextBlock { IsTextSelectionEnabled: true } && shape != InputSystemCursorShape.Hand)
                shape = InputSystemCursorShape.IBeam;
        }
        return shape;
    }

    private void PointerChanged(object sender, PointerRoutedEventArgs args)
    {
        var position = args.GetCurrentPoint(this).Position;
        if (position.X < 0 || position.Y < 0 || position.X >= ActualWidth || position.Y >= ActualHeight)
        {
            ProtectedCursor = null;
            return;
        }
        ProtectedCursor = CursorFor(args.OriginalSource as DependencyObject) switch
        {
            InputSystemCursorShape.Hand => _hand ??= InputSystemCursor.Create(InputSystemCursorShape.Hand),
            InputSystemCursorShape.IBeam => _text ??= InputSystemCursor.Create(InputSystemCursorShape.IBeam),
            _ => _arrow ??= InputSystemCursor.Create(InputSystemCursorShape.Arrow)
        };
    }

    private void HostUnloaded(object sender, RoutedEventArgs args)
    {
        RemoveHandler(PointerEnteredEvent, _pointerHandler);
        RemoveHandler(PointerMovedEvent, _pointerHandler);
        RemoveHandler(PointerExitedEvent, _pointerHandler);
        ProtectedCursor = null;
        _hand?.Dispose(); _hand = null;
        _arrow?.Dispose(); _arrow = null;
        _text?.Dispose(); _text = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        HostUnloaded(this, new RoutedEventArgs());
        Loaded -= HostLoaded;
        Unloaded -= HostUnloaded;
    }
}
