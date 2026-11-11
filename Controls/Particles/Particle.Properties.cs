// Derived from DevWinUI Particle (MIT). See README.md and LICENSE.txt.
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace Mizon.Windows.Controls;

public sealed partial class Particle
{
    public Color LineColor
    {
        get => (Color)GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
    }

    public static readonly DependencyProperty LineColorProperty = DependencyProperty.Register(
        nameof(LineColor), typeof(Color), typeof(Particle),
        new PropertyMetadata(Colors.DarkGray, OnColorChanged));

    public Color ParticleColor
    {
        get => (Color)GetValue(ParticleColorProperty);
        set => SetValue(ParticleColorProperty, value);
    }

    public static readonly DependencyProperty ParticleColorProperty = DependencyProperty.Register(
        nameof(ParticleColor), typeof(Color), typeof(Particle),
        new PropertyMetadata(Colors.Gray, OnColorChanged));

    // Preserve the upstream meaning: Paused hides the particle canvas.
    public bool Paused
    {
        get => (bool)GetValue(PausedProperty);
        set => SetValue(PausedProperty, value);
    }

    public static readonly DependencyProperty PausedProperty = DependencyProperty.Register(
        nameof(Paused), typeof(bool), typeof(Particle), new PropertyMetadata(false, OnPlaybackChanged));

    public bool IsPointerEnable
    {
        get => (bool)GetValue(IsPointerEnableProperty);
        set => SetValue(IsPointerEnableProperty, value);
    }

    public static readonly DependencyProperty IsPointerEnableProperty = DependencyProperty.Register(
        nameof(IsPointerEnable), typeof(bool), typeof(Particle), new PropertyMetadata(true, OnPointerEnabledChanged));

    /// <summary>Upstream density from zero to nine; negative values use five.</summary>
    public int Density
    {
        get => (int)GetValue(DensityProperty);
        set => SetValue(DensityProperty, value);
    }

    public static readonly DependencyProperty DensityProperty = DependencyProperty.Register(
        nameof(Density), typeof(int), typeof(Particle), new PropertyMetadata(5, OnDensityChanged));

    /// <summary>Suspends motion without hiding the canvas. Windows reduced motion is also respected.</summary>
    public bool IsAnimationEnabled
    {
        get => (bool)GetValue(IsAnimationEnabledProperty);
        set => SetValue(IsAnimationEnabledProperty, value);
    }

    public static readonly DependencyProperty IsAnimationEnabledProperty = DependencyProperty.Register(
        nameof(IsAnimationEnabled), typeof(bool), typeof(Particle), new PropertyMetadata(true, OnPlaybackChanged));

    private static void OnColorChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (Particle)sender;
        control._lineColor = control.LineColor;
        control._particleColor = control.ParticleColor;
        control.Invalidate();
    }

    private static void OnPlaybackChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((Particle)sender).ApplyPlayback();

    private static void OnPointerEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((Particle)sender).UpdatePointerHandlers();

    private static void OnDensityChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((Particle)sender).UpdateParticles();
}
