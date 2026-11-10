using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Mizon.Windows.Common;

public static class DiscoveryCardMotion
{
    private sealed class HoverState { public bool IsOver; public bool IsPressed; public bool IsAttached; public long PointerOverToken; }
    private static readonly ConditionalWeakTable<Button, HoverState> States = new();
    private static readonly global::Windows.UI.ViewManagement.UISettings AccessibilitySettings = new();
    private static readonly PointerEventHandler EnteredHandler = Entered;
    private static readonly PointerEventHandler PressedHandler = Pressed;
    private static readonly PointerEventHandler ReleasedHandler = Released;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(DiscoveryCardMotion), new PropertyMetadata(false, OnEnabledChanged));
    public static readonly DependencyProperty HoverRotationProperty = DependencyProperty.RegisterAttached(
        "HoverRotation", typeof(double), typeof(DiscoveryCardMotion), new PropertyMetadata(0d));
    public static readonly DependencyProperty HoverLiftProperty = DependencyProperty.RegisterAttached(
        "HoverLift", typeof(double), typeof(DiscoveryCardMotion), new PropertyMetadata(4d));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static double GetHoverRotation(DependencyObject element) => (double)element.GetValue(HoverRotationProperty);
    public static void SetHoverRotation(DependencyObject element, double value) => element.SetValue(HoverRotationProperty, value);
    public static double GetHoverLift(DependencyObject element) => (double)element.GetValue(HoverLiftProperty);
    public static void SetHoverLift(DependencyObject element, double value) => element.SetValue(HoverLiftProperty, value);

    private static void OnEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not Button button) return;
        button.Loaded -= Loaded;
        button.Unloaded -= Unloaded;
        Detach(button);
        var state = States.GetValue(button, static _ => new HoverState());
        state.IsOver = false;
        state.IsPressed = false;
        Reset(button);
        if (args.NewValue is not true) return;
        button.Loaded += Loaded;
        button.Unloaded += Unloaded;
        if (button.IsLoaded) Attach(button);
    }

    private static void Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is Button button) Attach(button);
    }

    private static void Detach(Button button)
    {
        if (!States.TryGetValue(button, out var state) || !state.IsAttached) return;
        state.IsAttached = false;
        button.RemoveHandler(UIElement.PointerEnteredEvent, EnteredHandler);
        button.UnregisterPropertyChangedCallback(Button.IsPointerOverProperty, state.PointerOverToken);
        button.RemoveHandler(UIElement.PointerPressedEvent, PressedHandler);
        button.RemoveHandler(UIElement.PointerReleasedEvent, ReleasedHandler);
        button.RemoveHandler(UIElement.PointerCanceledEvent, ReleasedHandler);
        button.RemoveHandler(UIElement.PointerCaptureLostEvent, ReleasedHandler);
    }

    private static void Attach(Button button)
    {
        var state = States.GetValue(button, static _ => new HoverState());
        if (state.IsAttached) return;
        state.IsAttached = true;
        button.AddHandler(UIElement.PointerEnteredEvent, EnteredHandler, true);
        state.PointerOverToken = button.RegisterPropertyChangedCallback(Button.IsPointerOverProperty, PointerOverChanged);
        // Observe handled Button presses without consuming Click or navigation.
        button.AddHandler(UIElement.PointerPressedEvent, PressedHandler, true);
        button.AddHandler(UIElement.PointerReleasedEvent, ReleasedHandler, true);
        button.AddHandler(UIElement.PointerCanceledEvent, ReleasedHandler, true);
        button.AddHandler(UIElement.PointerCaptureLostEvent, ReleasedHandler, true);
    }

    private static void Entered(object sender, PointerRoutedEventArgs args)
    {
        if (sender is not Button button || !button.IsEnabled || args.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch) return;
        var state = States.GetValue(button, static _ => new HoverState());
        if (state.IsOver) return;
        state.IsOver = true;
        Animate(button, true);
    }

    private static void PointerOverChanged(DependencyObject sender, DependencyProperty property)
    {
        // Native hover also ends when clipping or a popup changes the hit target;
        // pointer coordinates can still be inside the card in those cases.
        if (sender is not Button button || button.IsPointerOver) return;
        if (!States.TryGetValue(button, out var state) || !state.IsAttached) return;
        if (!state.IsOver && !state.IsPressed) return;
        state.IsOver = false;
        state.IsPressed = false;
        Animate(button, false);
    }

    private static void Pressed(object sender, PointerRoutedEventArgs args)
    {
        if (sender is not Button button || !button.IsEnabled) return;
        if (!args.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;
        var state = States.GetValue(button, static _ => new HoverState());
        state.IsPressed = true;
        Animate(button, state.IsOver);
    }

    private static void Released(object sender, PointerRoutedEventArgs args)
    {
        if (sender is not Button button || !States.TryGetValue(button, out var state) || !state.IsPressed) return;
        state.IsPressed = false;
        Animate(button, state.IsOver);
    }

    private static void Unloaded(object sender, RoutedEventArgs args)
    {
        if (sender is not Button button) return;
        Detach(button);
        var state = States.GetValue(button, static _ => new HoverState());
        state.IsOver = false;
        state.IsPressed = false;
        Reset(button);
    }

    private static void Animate(Button button, bool hover)
    {
        if (button.Content is not FrameworkElement content) return;
        if (!AccessibilitySettings.AnimationsEnabled)
        {
            Reset(button);
            return;
        }
        var state = States.GetValue(button, static _ => new HoverState());
        ElementCompositionPreview.SetIsTranslationEnabled(content, true);
        var visual = ElementCompositionPreview.GetElementVisual(content);
        // Initialize the custom property before animating it, including after reparenting.
        if (visual.Properties.TryGetVector3("Translation", out _) != Microsoft.UI.Composition.CompositionGetValueStatus.Succeeded)
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
        visual.CenterPoint = new Vector3((float)content.ActualWidth / 2, (float)content.ActualHeight / 2, 0);
        var compositor = visual.Compositor;
        using var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.18f, 0.8f), Vector2.One);
        using var lift = compositor.CreateVector3KeyFrameAnimation();
        lift.InsertKeyFrame(1, new Vector3(0, hover && !state.IsPressed ? -(float)GetHoverLift(button) : 0, 0), easing);
        lift.Duration = TimeSpan.FromMilliseconds(280);
        visual.Properties.StartAnimation("Translation", lift);
        using var press = compositor.CreateVector3KeyFrameAnimation();
        var scale = state.IsPressed ? 0.98f : 1f;
        press.InsertKeyFrame(1, new Vector3(scale, scale, 1), easing);
        press.Duration = TimeSpan.FromMilliseconds(state.IsPressed ? 110 : 240);
        visual.StartAnimation("Scale", press);
        var rotation = (float)GetHoverRotation(button);
        AnimateElement(content.FindName("CardArtwork") as FrameworkElement,
            hover ? 1.035f : 1f, hover ? rotation : 0f);
    }

    private static void AnimateElement(FrameworkElement? element, float scale, float rotation)
    {
        if (element is null || element.ActualWidth <= 0 || element.ActualHeight <= 0) return;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        var compositor = visual.Compositor;
        using var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.7f), Vector2.One);
        using var zoom = compositor.CreateVector3KeyFrameAnimation();
        zoom.InsertKeyFrame(1, new Vector3(scale, scale, 1), easing);
        zoom.Duration = TimeSpan.FromMilliseconds(330);
        visual.StartAnimation("Scale", zoom);
        using var turn = compositor.CreateScalarKeyFrameAnimation();
        turn.InsertKeyFrame(1, rotation, easing);
        turn.Duration = TimeSpan.FromMilliseconds(420);
        visual.StartAnimation("RotationAngleInDegrees", turn);
    }

    private static void Reset(Button button)
    {
        if (button.Content is not FrameworkElement content) return;
        ResetVisual(content, true);
        ResetVisual(content.FindName("CardArtwork") as FrameworkElement, false);
    }

    private static void ResetVisual(FrameworkElement? element, bool resetTranslation)
    {
        if (element is null) return;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Scale");
        visual.StopAnimation("RotationAngleInDegrees");
        visual.Scale = Vector3.One;
        visual.RotationAngleInDegrees = 0;
        if (!resetTranslation) return;
        if (visual.Properties.TryGetVector3("Translation", out _) == Microsoft.UI.Composition.CompositionGetValueStatus.Succeeded)
        {
            visual.Properties.StopAnimation("Translation");
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
        }
    }
}
