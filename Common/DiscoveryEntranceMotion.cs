using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Mizon.Windows.Common;

/// <summary>A one-time, bounded entrance for section containers, without a rendering loop.</summary>
public static class DiscoveryEntranceMotion
{
    private sealed class EntranceState { public bool HasPlayed; }
    private static readonly ConditionalWeakTable<FrameworkElement, EntranceState> States = new();
    private static readonly global::Windows.UI.ViewManagement.UISettings AccessibilitySettings = new();

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(DiscoveryEntranceMotion), new PropertyMetadata(false, OnEnabledChanged));
    public static readonly DependencyProperty DelayMillisecondsProperty = DependencyProperty.RegisterAttached(
        "DelayMilliseconds", typeof(int), typeof(DiscoveryEntranceMotion), new PropertyMetadata(0));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static int GetDelayMilliseconds(DependencyObject element) => (int)element.GetValue(DelayMillisecondsProperty);
    public static void SetDelayMilliseconds(DependencyObject element, int value) => element.SetValue(DelayMillisecondsProperty, value);

    private static void OnEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        element.Loaded -= Loaded;
        element.Unloaded -= Unloaded;
        if (args.NewValue is not true)
        {
            Reset(element);
            return;
        }
        element.Loaded += Loaded;
        element.Unloaded += Unloaded;
        if (element.IsLoaded) Play(element);
    }

    private static void Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement element) Play(element);
    }

    private static void Play(FrameworkElement element)
    {
        var state = States.GetValue(element, static _ => new EntranceState());
        // Cached pages and recycled elements retain their state: returning never restarts a reveal.
        if (state.HasPlayed) return;
        state.HasPlayed = true;
        if (!AccessibilitySettings.AnimationsEnabled) return;

        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var delay = TimeSpan.FromMilliseconds(Math.Clamp(GetDelayMilliseconds(element), 0, 600));
        using var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 0.85f), Vector2.One);
        visual.Opacity = 0;
        visual.Properties.InsertVector3("Translation", new Vector3(0, 24, 0));

        using var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, easing);
        fade.DelayTime = delay;
        fade.Duration = TimeSpan.FromMilliseconds(320);
        visual.StartAnimation("Opacity", fade);

        using var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0, new Vector3(0, 24, 0));
        slide.InsertKeyFrame(1, Vector3.Zero, easing);
        slide.DelayTime = delay;
        slide.Duration = TimeSpan.FromMilliseconds(320);
        visual.Properties.StartAnimation("Translation", slide);
    }

    private static void Unloaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement element) Reset(element);
    }

    private static void Reset(FrameworkElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Opacity");
        visual.Opacity = 1;
        // Reparenting during a layout change can unload an element before its entrance ran.
        // Only stop the property-set animation when Translation actually exists.
        if (visual.Properties.TryGetVector3("Translation", out _) == Microsoft.UI.Composition.CompositionGetValueStatus.Succeeded)
        {
            visual.Properties.StopAnimation("Translation");
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
        }
    }
}
