using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Automation;
using Mizon.Windows.Services;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Common;

public static class AuthInputMotion
{
    private static readonly ConditionalWeakTable<Control, InputState> States = new();
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(AuthInputMotion), new PropertyMetadata(false, EnabledChanged));
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void EnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not Control control || control is not (TextBox or PasswordBox)) return;
        control.Loaded -= Loaded;
        control.Unloaded -= Unloaded;
        if (States.TryGetValue(control, out var old)) old.Detach();
        if (args.NewValue is not true) return;
        control.Loaded += Loaded;
        control.Unloaded += Unloaded;
        if (control.IsLoaded) Loaded(control, new RoutedEventArgs());
    }
    private static void Loaded(object sender, RoutedEventArgs args) =>
        States.GetValue((Control)sender, static control => new InputState(control)).Attach();
    private static void Unloaded(object sender, RoutedEventArgs args)
    {
        if (States.TryGetValue((Control)sender, out var state)) state.Detach();
    }

    private sealed class InputState(Control control)
    {
        private readonly UISettings _settings = new();
        private readonly AccessibilitySettings _accessibility = new();
        private readonly SolidColorBrush _background = new();
        private readonly SolidColorBrush _border = new();
        private readonly Dictionary<string, object?> _original = new();
        private Storyboard? _backgroundAnimation, _borderAnimation;
        private bool _attached, _over, _focused;
        private bool _colorsSubscribed, _highContrastSubscribed, _highContrast;
        private TextBlock? _passwordLineMetrics;
        private Button? _clearButton;
        private double _minimumHeight;
        private object? _minimumHeightLocalValue;
        private object? _backgroundLocalValue, _borderLocalValue;
        private readonly List<(DependencyProperty Property, long Token)> _metricCallbacks = new();

        internal void Attach()
        {
            if (_attached) return;
            _attached = true;
            _backgroundLocalValue = control.ReadLocalValue(Control.BackgroundProperty);
            _borderLocalValue = control.ReadLocalValue(Control.BorderBrushProperty);
            // All native states share two owned brushes: focus no longer swaps the brush instantly.
            foreach (var state in new[] { "", "PointerOver", "Focused", "Disabled" })
            {
                SetResource("TextControlBackground" + state, _background);
                SetResource("TextControlBorderBrush" + state, _border);
            }
            SetResource("TextControlBorderThemeThicknessFocused", new Thickness(1));
            control.Background = _background;
            control.BorderBrush = _border;
            StyleClearButton();
            LocalizationService.Current.PropertyChanged += LocalizationChanged;
            ReservePasswordHeight();
            control.PointerEntered += Entered;
            control.PointerExited += Exited;
            control.GotFocus += GotFocus;
            control.LostFocus += LostFocus;
            control.IsEnabledChanged += Enabled;
            control.ActualThemeChanged += ActualThemeChanged;
            _settings.AnimationsEnabledChanged += AnimationPreferenceChanged;
            // Some desktop Windows builds expose the property but not this native event.
            // Each optional subscription is independent so the remaining refresh paths survive.
            try { _settings.ColorValuesChanged += SystemColorsChanged; _colorsSubscribed = true; }
            catch (COMException) { }
            try { _accessibility.HighContrastChanged += HighContrastChanged; _highContrastSubscribed = true; }
            catch (COMException) { }
            UpdateFocus();
            Paint(false);
        }

        private void ReservePasswordHeight()
        {
            if (control is not PasswordBox) return;
            _minimumHeight = control.MinHeight;
            _minimumHeightLocalValue = control.ReadLocalValue(FrameworkElement.MinHeightProperty);
            _passwordLineMetrics ??= new TextBlock { Text = "آAg" };
            foreach (var property in new[]
            {
                Control.FontFamilyProperty, Control.FontSizeProperty, Control.FontWeightProperty,
                Control.FontStyleProperty, Control.FontStretchProperty, Control.CharacterSpacingProperty,
                Control.IsTextScaleFactorEnabledProperty, FrameworkElement.LanguageProperty,
                Control.PaddingProperty, Control.BorderThicknessProperty
            })
                _metricCallbacks.Add((property, control.RegisterPropertyChangedCallback(property, (_, _) => UpdatePasswordHeight())));
            _settings.TextScaleFactorChanged += TextScaleChanged;
            UpdatePasswordHeight();
        }

        private void TextScaleChanged(UISettings sender, object args) => control.DispatcherQueue.TryEnqueue(() =>
        {
            if (_attached) UpdatePasswordHeight();
        });

        private void UpdatePasswordHeight()
        {
            if (_passwordLineMetrics is not { } metrics) return;
            // The native masked editor uses different metrics from revealed IRANSans.
            // Reserve the actual font's line height in both modes, without fixing the height
            // or turning off Windows accessibility text scaling.
            metrics.FontFamily = control.FontFamily;
            metrics.FontSize = control.FontSize;
            metrics.FontWeight = control.FontWeight;
            metrics.FontStyle = control.FontStyle;
            metrics.FontStretch = control.FontStretch;
            metrics.CharacterSpacing = control.CharacterSpacing;
            metrics.IsTextScaleFactorEnabled = control.IsTextScaleFactorEnabled;
            metrics.Language = control.Language;
            metrics.InvalidateMeasure();
            metrics.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            control.MinHeight = Math.Max(_minimumHeight, Math.Ceiling(metrics.DesiredSize.Height
                + control.Padding.Top + control.Padding.Bottom + control.BorderThickness.Top + control.BorderThickness.Bottom));
        }

        private void StyleClearButton()
        {
            if (control is not TextBox) return;
            control.ApplyTemplate();
            var button = FindClearButton(control);
            if (button is null) return;
            _clearButton = button;
            if (Application.Current.Resources.TryGetValue("AuthClearInputAction", out var style)) button.Style = (Style)style;
            button.Width = 28;
            button.Height = 28;
            button.MinWidth = button.MinHeight = 0;
            button.Padding = new Thickness(0);
            button.Margin = new Thickness(4, 0, 6, 0);
            button.VerticalAlignment = VerticalAlignment.Center;
            // The native clear button disappears when the TextBox loses focus.
            // Keep pointer focus in the editor until its built-in Click handler clears it.
            button.IsTabStop = false;
            button.AllowFocusOnInteraction = false;
            AutomationProperties.SetAccessibilityView(button, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Control);
            UpdateClearButtonText();
        }

        private void LocalizationChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (_attached && args.PropertyName == nameof(LocalizationService.Revision)) UpdateClearButtonText();
        }

        private void UpdateClearButtonText()
        {
            if (_clearButton is not { } button) return;
            var localization = LocalizationService.Current;
            AutomationProperties.SetName(button, localization.GetString("ClearInputName"));
            AutomationProperties.SetHelpText(button, localization.GetString("ClearInputHelp"));
            ToolTipService.SetToolTip(button, localization.GetString("ClearInputName"));
        }

        private static Button? FindClearButton(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Button { Name: "DeleteButton" } button) return button;
                if (FindClearButton(child) is { } nested) return nested;
            }
            return null;
        }

        private void SetResource(string key, object value)
        {
            _original[key] = control.Resources.TryGetValue(key, out var original) ? original : null;
            control.Resources[key] = value;
        }

        internal void Detach()
        {
            if (!_attached) return;
            _attached = false;
            LocalizationService.Current.PropertyChanged -= LocalizationChanged;
            _clearButton = null;
            control.PointerEntered -= Entered;
            control.PointerExited -= Exited;
            control.GotFocus -= GotFocus;
            control.LostFocus -= LostFocus;
            control.IsEnabledChanged -= Enabled;
            control.ActualThemeChanged -= ActualThemeChanged;
            _settings.AnimationsEnabledChanged -= AnimationPreferenceChanged;
            if (_colorsSubscribed)
            {
                try { _settings.ColorValuesChanged -= SystemColorsChanged; }
                catch (COMException) { }
                _colorsSubscribed = false;
            }
            if (_highContrastSubscribed)
            {
                try { _accessibility.HighContrastChanged -= HighContrastChanged; }
                catch (COMException) { }
                _highContrastSubscribed = false;
            }
            _settings.TextScaleFactorChanged -= TextScaleChanged;
            foreach (var (property, token) in _metricCallbacks) control.UnregisterPropertyChangedCallback(property, token);
            _metricCallbacks.Clear();
            if (control is PasswordBox)
            {
                if (_minimumHeightLocalValue == DependencyProperty.UnsetValue) control.ClearValue(FrameworkElement.MinHeightProperty);
                else control.SetValue(FrameworkElement.MinHeightProperty, _minimumHeightLocalValue);
            }
            _backgroundAnimation?.Stop();
            _borderAnimation?.Stop();
            _backgroundAnimation = _borderAnimation = null;
            foreach (var entry in _original)
                if (entry.Value is null) control.Resources.Remove(entry.Key); else control.Resources[entry.Key] = entry.Value;
            _original.Clear();
            RestoreLocalValue(Control.BackgroundProperty, _backgroundLocalValue);
            RestoreLocalValue(Control.BorderBrushProperty, _borderLocalValue);
            _over = _focused = false;
        }

        private void RestoreLocalValue(DependencyProperty property, object? value)
        {
            if (value is null || value == DependencyProperty.UnsetValue) control.ClearValue(property);
            else control.SetValue(property, value);
        }

        private void Entered(object sender, PointerRoutedEventArgs args) { _over = true; Paint(); }
        private void Exited(object sender, PointerRoutedEventArgs args)
        {
            var point = args.GetCurrentPoint(control).Position;
            if (point.X >= 0 && point.Y >= 0 && point.X < control.ActualWidth && point.Y < control.ActualHeight) return;
            _over = false;
            Paint();
        }
        private void GotFocus(object sender, RoutedEventArgs args) { UpdateFocus(); Paint(); }
        private void LostFocus(object sender, RoutedEventArgs args) => control.DispatcherQueue.TryEnqueue(() =>
        {
            if (!_attached) return;
            UpdateFocus();
            Paint();
        });
        private void Enabled(object sender, DependencyPropertyChangedEventArgs args) => Paint();
        private void AnimationPreferenceChanged(UISettings sender, object args) => control.DispatcherQueue.TryEnqueue(() =>
        {
            if (_attached) Paint(false);
        });
        private void ActualThemeChanged(FrameworkElement sender, object args)
        {
            if (_attached) Paint(false);
        }
        private void SystemColorsChanged(UISettings sender, object args) => RefreshAppearance();
        private void HighContrastChanged(AccessibilitySettings sender, object args) => RefreshAppearance();
        private void RefreshAppearance() => control.DispatcherQueue.TryEnqueue(() =>
        {
            if (_attached) Paint(false);
        });
        private void UpdateFocus()
        {
            var focused = control.XamlRoot is null ? null : FocusManager.GetFocusedElement(control.XamlRoot) as DependencyObject;
            while (focused is not null && !ReferenceEquals(focused, control)) focused = VisualTreeHelper.GetParent(focused);
            _focused = focused is not null;
        }
        private void Paint(bool animate = true)
        {
            var focus = control.IsEnabled && _focused;
            var over = control.IsEnabled && _over;
            var state = !control.IsEnabled ? "Disabled" : focus ? "Focused" : over ? "PointerOver" : string.Empty;
            var fill = ThemeColor("AuthInputBackground" + state + "Brush", UIColorType.Background);
            var stroke = ThemeColor("AuthInputBorder" + state + "Brush", UIColorType.Foreground);
            animate &= _settings.AnimationsEnabled && !ReadHighContrast();
            Animate(_background, fill, ref _backgroundAnimation, animate);
            Animate(_border, stroke, ref _borderAnimation, animate);
        }

        private Color ThemeColor(string key, UIColorType fallback)
        {
            var theme = ReadHighContrast() ? "HighContrast"
                : control.ActualTheme == ElementTheme.Light ? "Light" : "Default";
            return FindThemeBrush(Application.Current.Resources, key, theme)?.Color ?? _settings.GetColorValue(fallback);
        }

        private bool ReadHighContrast()
        {
            try { _highContrast = _accessibility.HighContrast; }
            catch (COMException) { } // Retain the last known state if the optional platform API is unavailable.
            return _highContrast;
        }

        private static SolidColorBrush? FindThemeBrush(ResourceDictionary resources, string key, string theme)
        {
            // Choose the control's actual theme explicitly. Application.RequestedTheme can differ
            // from a window's theme override, and the owned animation brushes must match that window.
            if (resources.ThemeDictionaries.TryGetValue(theme, out var themed)
                && themed is ResourceDictionary dictionary
                && dictionary.TryGetValue(key, out var value) && value is SolidColorBrush brush) return brush;
            for (var index = resources.MergedDictionaries.Count - 1; index >= 0; index--)
                if (FindThemeBrush(resources.MergedDictionaries[index], key, theme) is { } merged) return merged;
            return null;
        }
        private static void Animate(SolidColorBrush brush, Color target, ref Storyboard? running, bool animate)
        {
            // Read the animated color before stopping; rapid focus changes continue from that color.
            var current = brush.Color;
            running?.Stop();
            brush.Color = current;
            if (!animate || current == target) { brush.Color = target; running = null; return; }
            var animation = new ColorAnimation
            {
                From = current, To = target, Duration = TimeSpan.FromMilliseconds(180), EnableDependentAnimation = true,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animation, brush);
            Storyboard.SetTargetProperty(animation, "Color");
            running = new Storyboard();
            running.Children.Add(animation);
            running.Begin();
        }
    }
}
