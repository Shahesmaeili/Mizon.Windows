using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Numerics;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Controls;

public sealed partial class AuthActionButton : Button
{
    public static readonly DependencyProperty HoverScaleProperty = DependencyProperty.Register(
        nameof(HoverScale), typeof(double), typeof(AuthActionButton), new PropertyMetadata(1.012d, MotionChanged));
    public static readonly DependencyProperty PressedScaleProperty = DependencyProperty.Register(
        nameof(PressedScale), typeof(double), typeof(AuthActionButton), new PropertyMetadata(.98d, MotionChanged));
    public static readonly DependencyProperty AnimationTargetProperty = DependencyProperty.Register(
        nameof(AnimationTarget), typeof(FrameworkElement), typeof(AuthActionButton),
        new PropertyMetadata(null, AnimationTargetChanged));

    public double HoverScale { get => (double)GetValue(HoverScaleProperty); set => SetValue(HoverScaleProperty, value); }
    public double PressedScale { get => (double)GetValue(PressedScaleProperty); set => SetValue(PressedScaleProperty, value); }
    public FrameworkElement? AnimationTarget { get => (FrameworkElement?)GetValue(AnimationTargetProperty); set => SetValue(AnimationTargetProperty, value); }

    private static void AnimationTargetChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((AuthActionButton)sender).ApplyAnimationTarget();

    private static void MotionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((AuthActionButton)sender).Animate();

    private readonly UISettings _settings = new();
    private FrameworkElement? _surface;
    private Visual? _visual, _frame, _light;
    private InputSystemCursor? _cursor;
    private bool _loaded;

    public AuthActionButton()
    {
        foreach (var property in new[] { IsPointerOverProperty, IsPressedProperty, IsEnabledProperty })
            RegisterPropertyChangedCallback(property, (_, _) => Animate());
        UseSystemFocusVisuals = true;
        GotFocus += (_, _) => Animate();
        LostFocus += (_, _) => Animate();
        Loaded += (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            ProtectedCursor = _cursor ??= InputSystemCursor.Create(InputSystemCursorShape.Hand);
            _settings.AnimationsEnabledChanged += PreferenceChanged;
            ApplyAnimationTarget();
        };
        Unloaded += (_, _) =>
        {
            if (!_loaded) return;
            _loaded = false;
            _settings.AnimationsEnabledChanged -= PreferenceChanged;
            if (_surface is not null) _surface.SizeChanged -= SizeChanged;
            Stop();
            if (_visual is not null) _visual.Scale = Vector3.One;
            ProtectedCursor = null;
            _cursor?.Dispose();
            _cursor = null;
        };
    }

    protected override void OnApplyTemplate()
    {
        Stop();
        base.OnApplyTemplate();
        _frame = GetTemplateChild("ActionSurface") is UIElement frame ? ElementCompositionPreview.GetElementVisual(frame) : null;
        _light = GetTemplateChild("ActionLight") is UIElement light ? ElementCompositionPreview.GetElementVisual(light) : null;
        ApplyAnimationTarget();
    }

    internal void InvokeFromGamepad()
    {
        if (!IsEnabled || !IsLoaded) return;
        if (_visual is not null && _settings.AnimationsEnabled)
        {
            using var press = _visual.Compositor.CreateVector3KeyFrameAnimation();
            press.InsertExpressionKeyFrame(0, "this.StartingValue");
            press.InsertKeyFrame(.35f, new Vector3((float)PressedScale, (float)PressedScale, 1));
            press.InsertKeyFrame(1, new Vector3((float)HoverScale, (float)HoverScale, 1));
            press.Duration = TimeSpan.FromMilliseconds(180);
            _visual.StartAnimation("Scale", press);
        }
        var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(this)
            ?? Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(this);
        if (peer?.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)
            is Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider invoke) invoke.Invoke();
    }

    private void ApplyAnimationTarget()
    {
        if (_surface is not null) _surface.SizeChanged -= SizeChanged;
        Stop();
        if (_visual is not null) _visual.Scale = Vector3.One;
        // Animate only the content. Scaling a one-pixel outline causes subpixel flicker
        // and makes it disagree with the stationary hit target and keyboard focus outline.
        // Composite content can select its photo, keeping status decorations stationary.
        _surface = AnimationTarget ?? GetTemplateChild("ActionContent") as FrameworkElement;
        _visual = _surface is null ? null : ElementCompositionPreview.GetElementVisual(_surface);
        if (_loaded && _surface is not null) _surface.SizeChanged += SizeChanged;
        Center();
        Animate(false);
    }
    private new void SizeChanged(object sender, SizeChangedEventArgs args) => Center();
    private void Center()
    {
        if (_surface is not null && _visual is not null)
            _visual.CenterPoint = new Vector3((float)_surface.ActualWidth / 2, (float)_surface.ActualHeight / 2, 0);
    }
    private void PreferenceChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() => { if (_loaded) Animate(false); });
    private void Animate(bool animate = true)
    {
        if (_visual is null) return;
        animate &= _loaded && _settings.AnimationsEnabled;
        var requestedScale = IsEnabled && IsPressed ? PressedScale : IsEnabled && (IsPointerOver || FocusState == FocusState.Keyboard) ? HoverScale : 1;
        var scale = double.IsFinite(requestedScale) ? (float)Math.Clamp(requestedScale, .8, 1.15) : 1f;
        var opacity = IsEnabled ? 1f : .45f;
        var light = !IsEnabled ? 0 : IsPressed ? .04f : (IsPointerOver || FocusState == FocusState.Keyboard) ? .16f : 0;
        if (!animate)
        {
            Stop(); _visual.Scale = new Vector3(scale, scale, 1);
            if (_frame is not null) _frame.Opacity = opacity;
            if (_light is not null) _light.Opacity = light;
            return;
        }
        using var easing = _visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(.2f, .8f), Vector2.One);
        using var animation = _visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertExpressionKeyFrame(0, "this.StartingValue");
        animation.InsertKeyFrame(1, new Vector3(scale, scale, 1), easing);
        animation.Duration = TimeSpan.FromMilliseconds(160);
        _visual.StartAnimation("Scale", animation);
        if (_frame is not null) Fade(_frame, opacity);
        if (_light is not null) Fade(_light, light);
    }
    private static void Fade(Visual visual, float opacity)
    {
        using var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertExpressionKeyFrame(0, "this.StartingValue");
        animation.InsertKeyFrame(1, opacity);
        animation.Duration = TimeSpan.FromMilliseconds(160);
        visual.StartAnimation("Opacity", animation);
    }
    private void Stop()
    {
        _visual?.StopAnimation("Scale"); _frame?.StopAnimation("Opacity"); _light?.StopAnimation("Opacity");
    }
}
