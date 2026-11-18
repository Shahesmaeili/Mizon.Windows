using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Composition;
using System;
using System.Numerics;

namespace Mizon.Windows.Common;

/// <summary>Samples the visible spectrum stroke and blurs it outside the player's bounds.</summary>
internal sealed class HeroBorderHalo : IDisposable
{
    private const float Padding = 20;
    private const string StrokeSource = "stroke";
    private readonly CompositionVisualSurface _surface;
    private readonly CompositionSurfaceBrush _surfaceBrush;
    private readonly CompositionEffectBrush _effect;
    private readonly CompositionPathGeometry _clipPath;
    private readonly CompositionGeometricClip _clip;
    private CanvasGeometry? _outside;

    public SpriteVisual Visual { get; }

    public HeroBorderHalo(ShapeVisual stroke)
    {
        var compositor = stroke.Compositor;
        var input = new CompositionEffectSourceParameter(StrokeSource);
        using var blur = new GaussianBlurEffect
        {
            Source = input,
            BlurAmount = 5.5f,
            BorderMode = EffectBorderMode.Soft
        };
        // The stroke is subpixel-thin. Increase premultiplied color and alpha
        // together so the blurred copy is visible and keeps the same hue.
        using var transparent = new ColorSourceEffect { Color = Microsoft.UI.Colors.Transparent };
        using var gain = new ArithmeticCompositeEffect
        {
            Source1 = blur,
            Source2 = transparent,
            Source1Amount = 3,
            Source2Amount = 0,
            MultiplyAmount = 0,
            ClampOutput = true
        };
        using var factory = compositor.CreateEffectFactory(gain);
        _effect = factory.CreateBrush();
        _surface = compositor.CreateVisualSurface();
        _surface.SourceVisual = stroke;
        _surface.SourceOffset = new Vector2(-Padding);
        _surfaceBrush = compositor.CreateSurfaceBrush(_surface);
        _effect.SetSourceParameter(StrokeSource, _surfaceBrush);
        _clipPath = compositor.CreatePathGeometry();
        _clip = compositor.CreateGeometricClip(_clipPath);
        Visual = compositor.CreateSpriteVisual();
        Visual.Offset = new Vector3(-Padding, -Padding, 0);
        Visual.Brush = _effect;
        Visual.Clip = _clip;
    }

    public void Resize(Vector2 playerSize, float cornerRadius)
    {
        var size = playerSize + new Vector2(2 * Padding);
        var device = CanvasDevice.GetSharedDevice();
        using var extent = CanvasGeometry.CreateRectangle(device, 0, 0, size.X, size.Y);
        using var player = CanvasGeometry.CreateRoundedRectangle(device, Padding, Padding,
            playerSize.X, playerSize.Y, cornerRadius, cornerRadius);
        var outside = extent.CombineWith(player, Matrix3x2.Identity, CanvasGeometryCombine.Exclude);
        try { _clipPath.Path = new CompositionPath(outside); }
        catch { outside.Dispose(); throw; }
        _outside?.Dispose();
        _outside = outside;
        _surface.SourceSize = size;
        Visual.Size = size;
    }

    public void Dispose()
    {
        Visual.Brush = null;
        Visual.Clip = null;
        _surface.SourceVisual = null;
        Visual.Dispose();
        _clip.Dispose();
        _clipPath.Dispose();
        _outside?.Dispose();
        _effect.Dispose();
        _surfaceBrush.Dispose();
        _surface.Dispose();
    }
}
