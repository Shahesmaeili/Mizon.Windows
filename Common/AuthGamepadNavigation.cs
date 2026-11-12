using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services;
using Mizon.Windows.Services.Gamepad;
using Mizon.Windows.UserControls;
using Mizon.Windows.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Common;

// Page-scoped controller ownership. This never injects global keyboard input or
// keeps polling after its page has left the foreground.
internal sealed partial class AuthGamepadNavigation : IDisposable
{
    private readonly Grid _root;
    private readonly ScrollViewer _scroller;
    private readonly Func<AuthGamepadAction, Task> _pageAction;
    private readonly Func<Control[]> _fields;
    private readonly Func<Control?, string?> _fieldId;
    private readonly Action _hidePasswords;
    private readonly AuthGamepadSession _session;
    private readonly StackPanel _overlay = new() { Spacing = 8, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12) };
    private readonly TextBlock _hints = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _helpText = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _feedback = new() { MinHeight = 36 };
    private readonly StackPanel _help = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly AuthGamepadKeyboard _keyboard = new();
    private readonly UISettings _settings = new();
    private readonly LocalizationService _localization = LocalizationService.Current;
    private readonly CancellationTokenSource _lifetime = new();
    private Control? _helpOrigin;
    private ComboBox? _openCombo;
    private bool _active, _disposed;
    private readonly Thickness _originalPadding;
    private readonly bool _welcome;

    public AuthGamepadNavigation(Grid root, ScrollViewer scroller,
        Func<AuthGamepadAction, Task> pageAction, Func<Control[]> fields,
        Func<Control?, string?> fieldId, Action hidePasswords, bool welcome = false)
    {
        _root = root; _scroller = scroller; _pageAction = pageAction;
        _originalPadding = scroller.Padding;
        _welcome = welcome;
        _fields = fields; _fieldId = fieldId; _hidePasswords = hidePasswords;
        _help.Children.Add(_helpText);
        _help.Children.Add(_feedback);
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(_help);
        content.Children.Add(_hints);
        // Built-in static markup keeps ThemeResource live across system/app theme changes.
        // No user markup, custom type discovery or reflection-based binding is used.
        var panel = (Border)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                CornerRadius="12" Padding="12,8" BorderThickness="1" MaxWidth="760"
                Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" />
            """);
        panel.Child = content;
        _overlay.Children.Add(panel);
        _overlay.Children.Add(_keyboard);
        _overlay.Visibility = Visibility.Collapsed;
        _root.Children.Add(_overlay);
        _feedback.Checked += FeedbackChanged;
        _feedback.Unchecked += FeedbackChanged;
        _keyboard.Closed += KeyboardClosed;
        _root.PointerPressed += PointerPressed;
        _root.KeyDown += KeyDown;
        _root.LostFocus += LostFocus;
        _root.GotFocus += GotFocus;
        _root.SizeChanged += SizeChanged;
        _overlay.SizeChanged += OverlaySizeChanged;
        _localization.PropertyChanged += LanguageChanged;
        _session = new AuthGamepadSession(root, Dispatch);
        _session.StateChanged += SessionChanged;
        RefreshText();
        Resize();
    }

    internal Control? FocusedControl
    {
        get
        {
            var element = _root.XamlRoot is null ? null : FocusManager.GetFocusedElement(_root.XamlRoot) as DependencyObject;
            Control? candidate = null;
            while (element is not null)
            {
                if (ReferenceEquals(element, _root)) return candidate;
                if (candidate is null && element is TextBox or PasswordBox or ComboBox or ButtonBase) candidate = (Control)element;
                element = VisualTreeHelper.GetParent(element);
            }
            return null;
        }
    }

    internal McpAuthGamepadState GetState() => new()
    {
        IsControllerConnected = _session.State.IsControllerConnected,
        IsNavigationActive = _active && _session.State.IsActive,
        FocusedFieldId = _fieldId(_keyboard.IsOpen ? _keyboard.EditingTarget : FocusedControl),
        IsKeyboardVisible = _keyboard.IsOpen,
        IsHelpVisible = _help.Visibility == Visibility.Visible,
        IsFeedbackEnabled = _session.FeedbackEnabled
    };

    internal void ResetSurface() { Dismiss(); UpdateVisibility(); }
    internal bool IsGamepadMode => _active && _session.State.IsGamepadMode;

    internal void SetActive(bool active)
    {
        if (_disposed) return;
        _active = active;
        _session.SetActive(active);
        if (!active) Dismiss();
        UpdateVisibility();
    }

    private void SessionChanged(object? sender, AuthGamepadState state)
    {
        if (_disposed) return;
        if (!state.IsControllerConnected) Dismiss();
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (_disposed) return;
        _overlay.Visibility = _active && (_session.State.IsGamepadMode || _keyboard.IsOpen || _help.Visibility == Visibility.Visible)
            ? Visibility.Visible : Visibility.Collapsed;
        ReserveOverlaySpace();
    }

    private async void Dispatch(AuthGamepadAction action)
    {
        if (_disposed || !_active) return;
        try { await HandleAsync(action); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) { AppLog.Information($"Authentication controller action unavailable ({exception.GetType().Name})."); }
    }

    private async Task<bool> HandleAsync(AuthGamepadAction action)
    {
        if (_disposed || !_active || !OwnsInput()) return false;
        if (_keyboard.IsOpen) return _keyboard.Handle(action);
        if (_openCombo is { IsDropDownOpen: true } combo)
        {
            if (action is AuthGamepadAction.Up or AuthGamepadAction.Down or AuthGamepadAction.Left or AuthGamepadAction.Right)
            {
                var delta = action is AuthGamepadAction.Up or AuthGamepadAction.Left ? -1 : 1;
                combo.SelectedIndex = Math.Clamp(combo.SelectedIndex + delta, 0, combo.Items.Count - 1);
                return true;
            }
            if (action is AuthGamepadAction.Accept or AuthGamepadAction.Back)
            { combo.IsDropDownOpen = false; _openCombo = null; combo.Focus(FocusState.Keyboard); return true; }
            return false;
        }
        switch (action)
        {
            case AuthGamepadAction.Up: return Move(FocusNavigationDirection.Up);
            case AuthGamepadAction.Down: return Move(FocusNavigationDirection.Down);
            case AuthGamepadAction.Left: return Move(FocusNavigationDirection.Left);
            case AuthGamepadAction.Right: return Move(FocusNavigationDirection.Right);
            case AuthGamepadAction.PreviousField: return _help.Visibility == Visibility.Visible ? Move(FocusNavigationDirection.Previous) : MoveField(-1);
            case AuthGamepadAction.NextField: return _help.Visibility == Visibility.Visible ? Move(FocusNavigationDirection.Next) : MoveField(1);
            case AuthGamepadAction.Accept: return Activate();
            case AuthGamepadAction.ToggleHelp: ShowHelp(_help.Visibility != Visibility.Visible); return true;
            case AuthGamepadAction.Back when _help.Visibility == Visibility.Visible: ShowHelp(false); return true;
            case AuthGamepadAction.ScrollUp:
            case AuthGamepadAction.ScrollDown:
                _scroller.ChangeView(null, Math.Clamp(_scroller.VerticalOffset + (action == AuthGamepadAction.ScrollUp ? -90 : 90), 0, _scroller.ScrollableHeight), null, !_settings.AnimationsEnabled);
                return true;
        }
        if (_help.Visibility == Visibility.Visible) return false;
        await _pageAction(action);
        return true;
    }

    // MCP deliberately excludes Accept, clearing, revealing and submitting.
    // AuthenticationSubmit remains required for actual form submission.
    internal Task<bool> ApplyAsync(McpAuthGamepadAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(action) || _disposed || !_active || !OwnsInput()) return Task.FromResult(false);
        if (action is McpAuthGamepadAction.EnableFeedback or McpAuthGamepadAction.DisableFeedback)
        {
            _feedback.IsChecked = action == McpAuthGamepadAction.EnableFeedback;
            return Task.FromResult(true);
        }
        if (_openCombo is { } combo) { combo.IsDropDownOpen = false; _openCombo = null; combo.Focus(FocusState.Keyboard); }
        if (action == McpAuthGamepadAction.OpenKeyboard) return Task.FromResult(OpenKeyboard());
        if (action == McpAuthGamepadAction.CloseKeyboard)
        { var open = _keyboard.IsOpen; _keyboard.Close(); return Task.FromResult(open); }
        if (action is McpAuthGamepadAction.ShowHelp or McpAuthGamepadAction.HideHelp)
        { ShowHelp(action == McpAuthGamepadAction.ShowHelp); return Task.FromResult(true); }
        var direction = action switch
        {
            McpAuthGamepadAction.Next => FocusNavigationDirection.Next,
            McpAuthGamepadAction.Previous => FocusNavigationDirection.Previous,
            McpAuthGamepadAction.Up => FocusNavigationDirection.Up,
            McpAuthGamepadAction.Down => FocusNavigationDirection.Down,
            McpAuthGamepadAction.Left => FocusNavigationDirection.Left,
            McpAuthGamepadAction.Right => FocusNavigationDirection.Right,
            _ => (FocusNavigationDirection?)null
        };
        // Do not change ComboBox selection or emit synthetic keys from MCP.
        return Task.FromResult(direction is { } value && Move(value));
    }

    private bool OpenKeyboard()
    {
        if (_help.Visibility == Visibility.Visible || FocusedControl is not (TextBox or PasswordBox)
            || FocusedControl is not { IsEnabled: true } input || !Available(input)) return false;
        if (Array.IndexOf(_fields(), input) < 0) return false;
        _hidePasswords();
        _keyboard.Open(input);
        UpdateVisibility();
        return _keyboard.IsOpen;
    }

    private bool Activate()
    {
        var control = FocusedControl;
        if (control is null) return Move(FocusNavigationDirection.Next);
        if (_help.Visibility == Visibility.Visible && !IsWithin(control, _help)) return Move(FocusNavigationDirection.Next);
        if (!control.IsEnabled || !Available(control)) return false;
        if (control is TextBox or PasswordBox) return OpenKeyboard();
        if (control is ComboBox combo)
        { _openCombo = combo; combo.IsDropDownOpen = true; return true; }
        if (control is AuthActionButton action) { action.InvokeFromGamepad(); return true; }
        if (control is AuthSceneSelector scene) { scene.InvokeFromGamepad(); return true; }
        var peer = FrameworkElementAutomationPeer.FromElement(control) ?? FrameworkElementAutomationPeer.CreatePeerForElement(control);
        if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke) { invoke.Invoke(); return true; }
        if (peer?.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle) { toggle.Toggle(); return true; }
        return false;
    }

    private bool MoveField(int delta)
    {
        var fields = _fields();
        if (fields.Length == 0) return Move(delta > 0 ? FocusNavigationDirection.Next : FocusNavigationDirection.Previous);
        var current = Array.IndexOf(fields, FocusedControl);
        return Focus(fields[current < 0 ? (delta > 0 ? 0 : fields.Length - 1) : (current + delta + fields.Length) % fields.Length]);
    }

    private bool Move(FocusNavigationDirection direction)
    {
        var candidates = new List<Control>(16);
        Collect(_keyboard.IsOpen ? _keyboard : _help.Visibility == Visibility.Visible ? _help : _root, candidates);
        if (candidates.Count == 0) return false;
        var focused = FocusedControl;
        var index = candidates.IndexOf(focused!);
        if (index < 0) return Focus(candidates[0]);
        if (direction is FocusNavigationDirection.Next or FocusNavigationDirection.Previous)
            return Focus(candidates[(index + (direction == FocusNavigationDirection.Next ? 1 : -1) + candidates.Count) % candidates.Count]);
        var from = Center(focused!);
        Control? best = null;
        var bestScore = double.MaxValue;
        foreach (var candidate in candidates)
        {
            if (ReferenceEquals(candidate, focused)) continue;
            var to = Center(candidate);
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var primary = direction switch { FocusNavigationDirection.Right => dx, FocusNavigationDirection.Left => -dx, FocusNavigationDirection.Down => dy, _ => -dy };
            if (primary <= 1) continue;
            var cross = direction is FocusNavigationDirection.Left or FocusNavigationDirection.Right ? Math.Abs(dy) : Math.Abs(dx);
            var score = primary + cross * 3;
            if (score < bestScore) { bestScore = score; best = candidate; }
        }
        return best is not null && Focus(best);
    }

    private global::Windows.Foundation.Point Center(FrameworkElement element) => element.TransformToVisual(_root)
        .TransformPoint(new global::Windows.Foundation.Point(element.ActualWidth / 2, element.ActualHeight / 2));

    private static bool Available(FrameworkElement element)
    {
        if (!element.IsLoaded || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        DependencyObject? current = element;
        while (current is not null)
        {
            if (current is UIElement { Visibility: Visibility.Collapsed } or UIElement { IsHitTestVisible: false }) return false;
            if (current is Control { IsEnabled: false }) return false;
            current = VisualTreeHelper.GetParent(current);
        }
        return true;
    }

    private static void Collect(DependencyObject parent, List<Control> result)
    {
        if (parent is FrameworkElement element && !Available(element)) return;
        if (parent is TextBox or PasswordBox or ComboBox or ButtonBase)
        {
            var control = (Control)parent;
            if (control.IsTabStop) result.Add(control);
            return; // Never focus internal clear/reveal/template parts as separate fields.
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) Collect(VisualTreeHelper.GetChild(parent, i), result);
    }

    private bool Focus(Control control)
    {
        if (!Available(control)) return false;
        _hidePasswords();
        var focused = control.Focus(FocusState.Keyboard);
        if (focused && !_keyboard.IsOpen) control.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = _settings.AnimationsEnabled });
        return focused;
    }

    private void ShowHelp(bool show)
    {
        if (show == (_help.Visibility == Visibility.Visible)) return;
        if (show) { _helpOrigin = _keyboard.IsOpen ? _keyboard.EditingTarget : FocusedControl; _keyboard.Close(false); }
        _help.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        UpdateVisibility();
        if (show) _feedback.Focus(FocusState.Keyboard);
        else
        {
            if (_helpOrigin is { } origin && Available(origin)) origin.Focus(FocusState.Keyboard);
            _helpOrigin = null;
        }
    }

    private bool OwnsInput()
    {
        if (_root.XamlRoot is null) return false;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot))
        {
            if (popup.Child is ToolTip) continue;
            if (_openCombo is { IsDropDownOpen: true } && ContainsOwnedComboItem(popup.Child)) continue;
            return false;
        }
        var focused = FocusManager.GetFocusedElement(_root.XamlRoot) as DependencyObject;
        if (focused is null) return true;
        for (var element = focused; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is ContentDialog) return false;
            if (ReferenceEquals(element, _root)) return true;
            if (_openCombo is { IsDropDownOpen: true } && element is ComboBoxItem item
                && ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(item), _openCombo)) return true;
        }
        // A dialog or the window header owns the current focus. Do not activate
        // controls, submit a form or pull directional focus away from that surface.
        return false;
    }

    private bool ContainsOwnedComboItem(DependencyObject? element)
    {
        if (element is null) return false;
        if (element is ComboBoxItem item && ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(item), _openCombo)) return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (ContainsOwnedComboItem(VisualTreeHelper.GetChild(element, i))) return true;
        return false;
    }

    private static bool IsWithin(DependencyObject? element, DependencyObject parent)
    {
        while (element is not null)
        {
            if (ReferenceEquals(element, parent)) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private void Dismiss()
    {
        _keyboard.Close(false);
        _help.Visibility = Visibility.Collapsed;
        if (_openCombo is { } combo) combo.IsDropDownOpen = false;
        _openCombo = null;
        _helpOrigin = null;
        _hidePasswords();
    }

    private void RefreshText()
    {
        _overlay.FlowDirection = _localization.FlowDirection;
        _hints.FontFamily = _helpText.FontFamily = _localization.TextFontFamily;
        _hints.Text = _localization.GetString(_welcome ? "GamepadWelcomeHints" : "GamepadHints");
        _helpText.Text = _localization.GetString(_welcome ? "GamepadWelcomeHelp" : "GamepadHelp");
        _feedback.Content = _localization.GetString("GamepadFeedback");
        AutomationProperties.SetName(_feedback, _localization.GetString("GamepadFeedback"));
        AutomationProperties.SetName(_overlay, _localization.GetString("GamepadControls"));
    }
    private void LanguageChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(LocalizationService.Revision)) RefreshText(); }
    private void FeedbackChanged(object sender, RoutedEventArgs args) => _session.FeedbackEnabled = _feedback.IsChecked == true;
    private void KeyboardClosed(object? sender, EventArgs args) { _hidePasswords(); UpdateVisibility(); }
    private void PointerPressed(object sender, PointerRoutedEventArgs args) { _session.NotifyPointerOrKeyboardInput(); UpdateVisibility(); }
    private void KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if ((int)args.OriginalKey is < 195 or > 218) { _session.NotifyPointerOrKeyboardInput(); UpdateVisibility(); }
    }
    private void LostFocus(object sender, RoutedEventArgs args)
    {
        // Never leave a revealed password behind when moving to another field.
        if (args.OriginalSource is PasswordBox) _hidePasswords();
    }
    private void GotFocus(object sender, RoutedEventArgs args)
    {
        if (_keyboard.IsOpen && FocusedControl is TextBox or PasswordBox
            && !ReferenceEquals(FocusedControl, _keyboard.EditingTarget)) _keyboard.Close(false);
    }
    private void SizeChanged(object sender, SizeChangedEventArgs args) => Resize();
    private void Resize() => _keyboard.MaxHeight = Math.Max(100, _root.ActualHeight * .6);
    internal static bool IsForeground(Window? window) => window is not null
        && GetForegroundWindow() == WinRT.Interop.WindowNative.GetWindowHandle(window);

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();
    private void OverlaySizeChanged(object sender, SizeChangedEventArgs args) => ReserveOverlaySpace();
    private void ReserveOverlaySpace()
    {
        var padding = _originalPadding;
        if (_overlay.Visibility == Visibility.Visible && (_keyboard.IsOpen || _help.Visibility == Visibility.Visible))
            padding.Bottom += _overlay.ActualHeight + 24;
        if (_scroller.Padding != padding) _scroller.Padding = padding;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _active = false; _lifetime.Cancel();
        _session.StateChanged -= SessionChanged;
        _session.Dispose();
        Dismiss();
        _keyboard.Closed -= KeyboardClosed;
        _keyboard.Dispose();
        _feedback.Checked -= FeedbackChanged; _feedback.Unchecked -= FeedbackChanged;
        _localization.PropertyChanged -= LanguageChanged;
        _root.PointerPressed -= PointerPressed; _root.KeyDown -= KeyDown; _root.LostFocus -= LostFocus; _root.SizeChanged -= SizeChanged;
        _root.GotFocus -= GotFocus;
        _overlay.SizeChanged -= OverlaySizeChanged;
        _root.Children.Remove(_overlay);
        _scroller.Padding = _originalPadding;
        _lifetime.Dispose();
    }
}
