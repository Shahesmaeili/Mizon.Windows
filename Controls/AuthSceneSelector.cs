using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Controls;

// A stable hit target with independently animated artwork. Selection never toggles itself off.
public sealed partial class AuthSceneSelector : ToggleButton
{
    private readonly UISettings _settings = new();
    private readonly AccessibilitySettings _accessibility = new();
    private FrameworkElement? _surface;
    private Visual? _motion;
    private Visual? _tint;
    private Visual? _line;
    private Visual? _content;
    private (float Scale, float Lift, float Tint, float Line, float Content)? _last;
    private bool _loaded;
    private bool _colorsSubscribed, _highContrastSubscribed, _highContrast;

    public AuthSceneSelector()
    {
        ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        foreach (var property in new[] { IsPointerOverProperty, IsPressedProperty, IsEnabledProperty, IsCheckedProperty })
            RegisterPropertyChangedCallback(property, (_, _) => UpdateMotion());
        UseSystemFocusVisuals = true;
        GotFocus += (_, _) => UpdateMotion();
        LostFocus += (_, _) => UpdateMotion();
        Loaded += SelectorLoaded;
        Unloaded += SelectorUnloaded;
    }

    private void SelectorLoaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        _settings.AnimationsEnabledChanged += AnimationsChanged;
        try { _settings.ColorValuesChanged += ColorsChanged; _colorsSubscribed = true; }
        catch (COMException) { }
        try { _accessibility.HighContrastChanged += HighContrastChanged; _highContrastSubscribed = true; }
        catch (COMException) { }
        ActualThemeChanged += ThemeChanged;
        UpdateMotion(false);
    }

    private void SelectorUnloaded(object sender, RoutedEventArgs args)
    {
        _loaded = false;
        _settings.AnimationsEnabledChanged -= AnimationsChanged;
        if (_colorsSubscribed)
        {
            try { _settings.ColorValuesChanged -= ColorsChanged; }
            catch (COMException) { }
            _colorsSubscribed = false;
        }
        if (_highContrastSubscribed)
        {
            try { _accessibility.HighContrastChanged -= HighContrastChanged; }
            catch (COMException) { }
            _highContrastSubscribed = false;
        }
        ActualThemeChanged -= ThemeChanged;
        StopMotion();
    }

    protected override void OnToggle()
    {
        if (IsChecked != true) base.OnToggle();
    }

    internal void InvokeFromGamepad()
    {
        if (IsEnabled && IsLoaded) GamepadInvoked?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? GamepadInvoked;

    protected override void OnApplyTemplate()
    {
        if (_surface is not null) _surface.SizeChanged -= Surface_SizeChanged;
        StopMotion();
        base.OnApplyTemplate();
        _surface = GetTemplateChild("SelectorSurface") as FrameworkElement;
        _motion = GetPartVisual("SelectorSurface");
        _tint = GetPartVisual("SelectorTint");
        _line = GetPartVisual("SelectorLine");
        _content = GetPartVisual("SelectorContent");
        if (_surface is not null)
        {
            ElementCompositionPreview.SetIsTranslationEnabled(_surface, true);
            _surface.SizeChanged += Surface_SizeChanged;
            UpdateCenter();
        }
        _last = null;
        UpdateMotion(false);
    }

    private Visual? GetPartVisual(string name) => GetTemplateChild(name) is UIElement element
        ? ElementCompositionPreview.GetElementVisual(element) : null;
    private void Surface_SizeChanged(object sender, SizeChangedEventArgs args) => UpdateCenter();
    private void UpdateCenter()
    {
        if (_motion is not null && _surface is not null)
            _motion.CenterPoint = new Vector3((float)_surface.ActualWidth / 2, (float)_surface.ActualHeight / 2, 0);
    }
    private void AnimationsChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_loaded) UpdateMotion(false);
    });
    private void HighContrastChanged(AccessibilitySettings sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_loaded) UpdateMotion(false);
    });
    private void ColorsChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_loaded) UpdateMotion(false);
    });
    private void ThemeChanged(FrameworkElement sender, object args) => UpdateMotion(false);

    private void UpdateMotion(bool animate = true)
    {
        if (_motion is null) return;
        var over = IsEnabled && (IsPointerOver || FocusState == FocusState.Keyboard);
        var pressed = IsEnabled && IsPressed;
        var selected = IsChecked == true;
        var highContrast = ReadHighContrast();
        var preserveContrast = ActualTheme == ElementTheme.Light || highContrast;
        var next = (Scale: pressed ? .97f : over ? 1.025f : 1f,
            Lift: over && !pressed ? -2f : 0f,
            Tint: !IsEnabled ? .03f : (selected ? .12f : 0f) + (over ? .07f : 0f) + (pressed ? .04f : 0f),
            Line: selected ? 1f : 0f,
            Content: highContrast ? 1f : !IsEnabled ? .3f : preserveContrast ? 1f : pressed ? .8f : over || selected ? 1f : .64f);
        animate &= _loaded && _settings.AnimationsEnabled && !highContrast;
        if (!animate || _last?.Scale != next.Scale)
            SetVector(_motion, "Scale", new Vector3(next.Scale, next.Scale, 1), animate);
        if (!animate || _last?.Lift != next.Lift)
            SetVector(_motion, "Translation", new Vector3(0, next.Lift, 0), animate);
        if (!animate || _last?.Tint != next.Tint) SetOpacity(_tint, next.Tint, animate);
        if (!animate || _last?.Line != next.Line) SetOpacity(_line, next.Line, animate);
        if (!animate || _last?.Content != next.Content) SetOpacity(_content, next.Content, animate);
        _last = next;
    }

    private bool ReadHighContrast()
    {
        try { _highContrast = _accessibility.HighContrast; }
        catch (COMException) { }
        return _highContrast;
    }

    private static void SetVector(Visual visual, string property, Vector3 value, bool animate)
    {
        var translation = property == "Translation";
        if (!animate)
        {
            if (translation) { visual.Properties.StopAnimation(property); visual.Properties.InsertVector3(property, value); }
            else { visual.StopAnimation(property); visual.Scale = value; }
            return;
        }
        using var easing = visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(.2f, .8f), Vector2.One);
        using var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertExpressionKeyFrame(0, "this.StartingValue");
        animation.InsertKeyFrame(1, value, easing);
        animation.Duration = TimeSpan.FromMilliseconds(180);
        if (translation) visual.Properties.StartAnimation(property, animation);
        else visual.StartAnimation(property, animation);
    }

    private static void SetOpacity(Visual? visual, float value, bool animate)
    {
        if (visual is null) return;
        if (!animate) { visual.StopAnimation("Opacity"); visual.Opacity = value; return; }
        using var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertExpressionKeyFrame(0, "this.StartingValue");
        animation.InsertKeyFrame(1, value);
        animation.Duration = TimeSpan.FromMilliseconds(180);
        visual.StartAnimation("Opacity", animation);
    }

    private void StopMotion()
    {
        _motion?.StopAnimation("Scale");
        _motion?.Properties.StopAnimation("Translation");
        foreach (var visual in new[] { _tint, _line, _content }) visual?.StopAnimation("Opacity");
        _last = null;
    }
}
