// Minimal observable platform surface for the source-linked production navigator.
// These doubles hold tree/focus state; they do not implement navigation decisions.
using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Windows.Foundation { public readonly record struct Point(double X, double Y); }
namespace Windows.UI.ViewManagement { public sealed class UISettings { public bool AnimationsEnabled => true; } }
namespace WinRT.Interop { public static class WindowNative { public static nint GetWindowHandle(Window window) => 0; } }

namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public enum FocusState { Keyboard }
    public enum FocusNavigationDirection { Next, Previous, Up, Down, Left, Right }
    public enum VerticalAlignment { Bottom }
    public enum HorizontalAlignment { Stretch }
    public enum TextWrapping { Wrap }
    public enum FlowDirection { LeftToRight, RightToLeft }
    public record struct Thickness(double Left, double Top, double Right, double Bottom)
    {
        public Thickness(double all) : this(all, all, all, all) { }
        public Thickness(double horizontal, double vertical) : this(horizontal, vertical, horizontal, vertical) { }
    }
    public readonly record struct CornerRadius(double Value);
    public class RoutedEventArgs : EventArgs { public object? OriginalSource { get; init; } }
    public sealed class SizeChangedEventArgs : RoutedEventArgs;
    public sealed class BringIntoViewOptions { public bool AnimationDesired { get; set; } }
    public sealed class XamlRoot;
    public sealed class Window;
    public class DependencyObject
    {
        internal DependencyObject? Parent;
        internal List<DependencyObject> Nodes { get; } = [];
    }
    public class UIElement : DependencyObject
    {
        public Visibility Visibility { get; set; }
        public bool IsHitTestVisible { get; set; } = true;
        public event Action<object, Input.PointerRoutedEventArgs>? PointerPressed;
        public event Action<object, Input.KeyRoutedEventArgs>? KeyDown;
        public event Action<object, RoutedEventArgs>? LostFocus;
        public event Action<object, RoutedEventArgs>? GotFocus;
        public int InputSubscribers => Count(PointerPressed) + Count(KeyDown) + Count(LostFocus) + Count(GotFocus);
        private static int Count(Delegate? value) => value?.GetInvocationList().Length ?? 0;
        public void Pointer() => PointerPressed?.Invoke(this, new());
        public void Key() => KeyDown?.Invoke(this, new());
        public void LoseFocus(object source) => LostFocus?.Invoke(this, new() { OriginalSource = source });
        internal void ReceiveFocus(object source) => GotFocus?.Invoke(this, new() { OriginalSource = source });
    }
    public class FrameworkElement : UIElement
    {
        public bool IsLoaded { get; set; } = true;
        public double ActualWidth { get; set; } = 120;
        public double ActualHeight { get; set; } = 40;
        public double Left { get; set; }
        public double Top { get; set; }
        public string Name { get; set; } = string.Empty;
        public double MaxHeight { get; set; }
        public double MaxWidth { get; set; }
        public double MinHeight { get; set; }
        public VerticalAlignment VerticalAlignment { get; set; }
        public HorizontalAlignment HorizontalAlignment { get; set; }
        public FlowDirection FlowDirection { get; set; }
        public Thickness Margin { get; set; }
        public XamlRoot? XamlRoot { get; set; } = TestRoot;
        internal static XamlRoot TestRoot { get; } = new();
        public event Action<object, SizeChangedEventArgs>? SizeChanged;
        public int LayoutSubscribers => SizeChanged?.GetInvocationList().Length ?? 0;
        public void Resize() => SizeChanged?.Invoke(this, new());
        public Media.GeneralTransform TransformToVisual(FrameworkElement root) => new(Left, Top);
        public int BringIntoViewCount { get; private set; }
        public void StartBringIntoView(BringIntoViewOptions options) => BringIntoViewCount++;
    }
    public sealed class Application
    {
        public static Application Current { get; } = new();
        public Dictionary<string, object> Resources { get; } = new()
        { ["CardBackgroundFillColorDefaultBrush"] = new Media.Brush(), ["CardStrokeColorDefaultBrush"] = new Media.Brush() };
    }
    public sealed class ElementCollection(DependencyObject parent) : Collection<UIElement>
    {
        protected override void InsertItem(int index, UIElement item) { base.InsertItem(index, item); item.Parent = parent; parent.Nodes.Insert(index, item); }
        protected override void RemoveItem(int index) { this[index].Parent = null; parent.Nodes.RemoveAt(index); base.RemoveItem(index); }
    }
}
namespace Microsoft.UI.Xaml.Input
{
    public sealed class PointerRoutedEventArgs : RoutedEventArgs;
    public sealed class KeyRoutedEventArgs : RoutedEventArgs { public int OriginalKey { get; set; } }
    public static class FocusManager
    {
        public static object? Current { get; set; }
        public static object? GetFocusedElement(XamlRoot root) => Current;
    }
}
namespace Microsoft.UI.Xaml.Markup { public static class XamlReader { public static object Load(string markup) => new Border(); } }
namespace Microsoft.UI.Xaml.Media
{
    public sealed class Brush;
    public sealed class GeneralTransform(double x, double y) { public Windows.Foundation.Point TransformPoint(Windows.Foundation.Point point) => new(point.X + x, point.Y + y); }
    public static class VisualTreeHelper
    {
        public static DependencyObject? GetParent(DependencyObject element) => element.Parent;
        public static int GetChildrenCount(DependencyObject element) => element.Nodes.Count;
        public static DependencyObject GetChild(DependencyObject element, int index) => element.Nodes[index];
        public static List<Controls.Primitives.Popup> OpenPopups { get; } = [];
        public static IReadOnlyList<Controls.Primitives.Popup> GetOpenPopupsForXamlRoot(XamlRoot root) => OpenPopups;
    }
}
namespace Microsoft.UI.Xaml.Controls
{
    public class Control : FrameworkElement
    {
        public bool IsEnabled { get; set; } = true;
        public bool IsTabStop { get; set; } = true;
        public Thickness Padding { get; set; }
        public virtual bool Focus(FocusState state)
        {
            if (!IsEnabled || !IsLoaded) return false;
            for (DependencyObject? item = this; item is not null; item = item.Parent)
                if (item is UIElement { Visibility: Visibility.Collapsed }) return false;
            Input.FocusManager.Current = this;
            for (DependencyObject? item = this; item is not null; item = item.Parent)
                if (item is UIElement element) element.ReceiveFocus(this);
            return true;
        }
    }
    public class Panel : FrameworkElement { public Panel() => Children = new(this); public ElementCollection Children { get; } }
    public class Grid : Panel;
    public class StackPanel : Panel { public double Spacing { get; set; } }
    public class TextBox : Control { public string Text { get; set; } = string.Empty; }
    public class PasswordBox : Control;
    public class ContentDialog : Control;
    public class ToolTip : Control;
    public class ComboBoxItem : Control { public Control? Owner { get; set; } }
    public class ItemsControl : Control { public static Control? ItemsControlFromItemContainer(DependencyObject item) => (item as ComboBoxItem)?.Owner; }
    public class ComboBox : Control { public bool IsDropDownOpen { get; set; } public int SelectedIndex { get; set; } public List<object> Items { get; } = ["one", "two"]; }
    public class ScrollViewer : Control { public double VerticalOffset { get; set; } public double ScrollableHeight { get; set; } = 1000; public void ChangeView(double? x, double? y, float? zoom, bool disableAnimation) => VerticalOffset = y ?? VerticalOffset; }
    public sealed class Border : FrameworkElement
    {
        public CornerRadius CornerRadius { get; set; }
        public Thickness Padding { get; set; }
        public Thickness BorderThickness { get; set; }
        public Media.Brush? Background { get; set; }
        public Media.Brush? BorderBrush { get; set; }
        public UIElement? Child { get => Nodes.FirstOrDefault() as UIElement; set { Nodes.Clear(); if (value is not null) { Nodes.Add(value); value.Parent = this; } } }
    }
    public sealed class TextBlock : FrameworkElement { public double FontSize { get; set; } public TextWrapping TextWrapping { get; set; } public object? FontFamily { get; set; } public string? Text { get; set; } }
    public class CheckBox : Primitives.ButtonBase
    {
        private bool? _isChecked;
        public bool? IsChecked { get => _isChecked; set { if (_isChecked == value) return; _isChecked = value; if (value == true) Checked?.Invoke(this, new()); else Unchecked?.Invoke(this, new()); } }
        public object? Content { get; set; }
        public event Action<object, RoutedEventArgs>? Checked;
        public event Action<object, RoutedEventArgs>? Unchecked;
        internal void Toggle() => IsChecked = IsChecked != true;
    }
}
namespace Microsoft.UI.Xaml.Controls.Primitives
{
    public class ButtonBase : Control { public int Invocations { get; private set; } public void Invoke() => Invocations++; }
    public class Popup : FrameworkElement { public UIElement? Child { get; set; } public bool IsOpen { get; set; } = true; }
}
namespace Microsoft.UI.Xaml.Automation { public static class AutomationProperties { public static void SetName(DependencyObject element, string name) { } } }
namespace Microsoft.UI.Xaml.Automation.Provider
{
    public interface IInvokeProvider { void Invoke(); }
    public interface IToggleProvider { void Toggle(); }
}
namespace Microsoft.UI.Xaml.Automation.Peers
{
    public enum PatternInterface { Invoke, Toggle }
    public sealed class FrameworkElementAutomationPeer(Control control) : Provider.IInvokeProvider, Provider.IToggleProvider
    {
        public static FrameworkElementAutomationPeer FromElement(Control control) => new(control);
        public static FrameworkElementAutomationPeer CreatePeerForElement(Control control) => new(control);
        public object? GetPattern(PatternInterface pattern) => pattern == PatternInterface.Toggle ? control is CheckBox ? this : null : control is CheckBox ? null : this;
        public void Invoke() { if (control is Controls.Primitives.ButtonBase button) button.Invoke(); }
        public void Toggle() { if (control is CheckBox check) check.Toggle(); }
    }
}
namespace Mizon.Windows.Services
{
    public static class AppLog { public static void Information(string value) { } }
    public sealed class LocalizationService : INotifyPropertyChanged
    {
        public static LocalizationService Current { get; } = new();
        public FlowDirection FlowDirection => FlowDirection.LeftToRight;
        public object TextFontFamily => "Synthetic";
        public int Revision => 0;
        public event PropertyChangedEventHandler? PropertyChanged;
        public int Subscribers => PropertyChanged?.GetInvocationList().Length ?? 0;
        public string GetString(string key) => key;
        public void Refresh() => PropertyChanged?.Invoke(this, new(nameof(Revision)));
    }
}
namespace Mizon.Windows.Services.Gamepad
{
    public sealed class AuthGamepadSession : IDisposable
    {
        private readonly Action<AuthGamepadAction> _action;
        public AuthGamepadSession(FrameworkElement root, Action<AuthGamepadAction> action) { _action = action; Latest = this; }
        public static AuthGamepadSession Latest { get; private set; } = null!;
        public AuthGamepadState State { get; private set; } = new(1, false, false);
        public event EventHandler<AuthGamepadState>? StateChanged;
        public bool FeedbackEnabled { get; set; }
        public bool Disposed { get; private set; }
        public void SetActive(bool active) { State = State with { IsActive = active }; StateChanged?.Invoke(this, State); }
        public void NotifyPointerOrKeyboardInput() { }
        public void Emit(AuthGamepadAction action) => _action(action);
        public void Disconnect() { State = new(0, false, false); StateChanged?.Invoke(this, State); }
        public void Dispose() => Disposed = true;
    }
}
namespace Mizon.Windows.UserControls
{
    public sealed class AuthGamepadKeyboard : Grid, IDisposable
    {
        public AuthGamepadKeyboard() { Latest = this; Visibility = Visibility.Collapsed; Children.Add(new Microsoft.UI.Xaml.Controls.Primitives.ButtonBase()); Children.Add(new Microsoft.UI.Xaml.Controls.Primitives.ButtonBase()); }
        public static AuthGamepadKeyboard Latest { get; private set; } = null!;
        public bool IsOpen { get; private set; }
        public Control? EditingTarget { get; private set; }
        public int HandledActions { get; private set; }
        public event EventHandler? Closed;
        public void Open(Control control) { EditingTarget = control; IsOpen = true; Visibility = Visibility.Visible; }
        public void Close(bool restoreFocus = true) { var target = EditingTarget; var open = IsOpen; IsOpen = false; EditingTarget = null; Visibility = Visibility.Collapsed; if (restoreFocus) target?.Focus(FocusState.Keyboard); if (open) Closed?.Invoke(this, EventArgs.Empty); }
        public bool Handle(Services.Gamepad.AuthGamepadAction action) { HandledActions++; return true; }
        public void Dispose() => Close(false);
    }
}
namespace Mizon.Windows.Controls
{
    public sealed class AuthActionButton : Microsoft.UI.Xaml.Controls.Primitives.ButtonBase { public void InvokeFromGamepad() => Invoke(); }
    public sealed class AuthSceneSelector : Microsoft.UI.Xaml.Controls.Primitives.ButtonBase { public void InvokeFromGamepad() => Invoke(); }
}
