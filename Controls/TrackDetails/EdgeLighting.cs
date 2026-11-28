// Adapted from DevWinUI 10.4.1 (30a7bbb), MIT. See LICENSE.DevWinUI.txt.
using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Mizon.Windows.Controls.TrackDetails;

[TemplatePart(Name = nameof(PART_Border), Type = typeof(Grid))]
public partial class EdgeLighting : ContentControl
{
    private const string PART_Border = "PART_Border";

    private Border? border;

    private Compositor? _compositor;
    private CompositionRadialGradientBrush? brush;
    private ExpressionAnimation? expression;
    private CompositionPropertySet? props;
    private ScalarKeyFrameAnimation? progressAnimation;
    private CompositionSpriteShape? bgShape;
    private CompositionSpriteShape? glowShape;
    private readonly List<CompositionObject> _owned = [];
    private Vector2 _size;
    private CompositionColorGradientStop[]? _gradientStops;

    public EdgeLighting()
    {
        Loaded += (_, _) => Build();
        Unloaded += (_, _) => ReleaseLighting();
    }

    private T Own<T>(T value) where T : CompositionObject
    {
        _owned.Add(value);
        return value;
    }

    private void ReleaseLighting()
    {
        if (border is not null)
            ElementCompositionPreview.SetElementChildVisual(border, null);

        brush?.StopAnimation(nameof(brush.CenterPoint));
        brush?.StopAnimation(nameof(brush.EllipseCenter));
        props?.StopAnimation("progress");
        if (_gradientStops is not null)
        {
            brush?.ColorStops.Clear();
            foreach (var stop in _gradientStops) stop.Dispose();
            _gradientStops = null;
        }
        for (var index = _owned.Count - 1; index >= 0; index--)
            _owned[index].Dispose();
        _owned.Clear();
        bgShape = null;
        glowShape = null;
        brush = null;
        props = null;
        expression = null;
        progressAnimation = null;
        _compositor = null;
        _size = default;
    }
    protected override void OnApplyTemplate()
    {
        ReleaseLighting();
        base.OnApplyTemplate();

        border = GetTemplateChild(PART_Border) as Border;

        SizeChanged -= OnSizeChanged;
        SizeChanged += OnSizeChanged;
        if (IsLoaded) Build();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        Build();
        UpdateBorderCornerRadius();
    }

    private void UpdateBorderBackground()
    {
        if (bgShape == null || _compositor == null)
            return;

        bgShape.StrokeBrush = Own(_compositor.CreateColorBrush(BorderStrokeColor));
    }
    private void UpdateStrokeThickness()
    {
        if (bgShape == null || glowShape == null || _compositor == null)
            return;

        bgShape.StrokeThickness = (float)StrokeThickness;
        glowShape.StrokeThickness = (float)StrokeThickness;

    }
    private void Build()
    {
        if (!IsLoaded || border is null || border.ActualWidth <= 0 || border.ActualHeight <= 0)
            return;

        float width = (float)border.ActualWidth;
        float height = (float)border.ActualHeight;
        var size = new Vector2(width, height);
        if (_size == size) return;

        ReleaseLighting();
        _size = size;
        _compositor = ElementCompositionPreview.GetElementVisual(border).Compositor;

        // Geometry
        if (IsRounded)
        {
            float radius = Math.Min(width, height) / 2f;
            var ellipseGeometry = Own(_compositor.CreateEllipseGeometry());
            ellipseGeometry.Center = new Vector2(width / 2f, height / 2f);
            ellipseGeometry.Radius = new Vector2(radius, radius);

            bgShape = Own(_compositor.CreateSpriteShape(ellipseGeometry));
            glowShape = Own(_compositor.CreateSpriteShape(ellipseGeometry));
        }
        else
        {
            var roundedRectangleGeometry = Own(_compositor.CreateRoundedRectangleGeometry());
            roundedRectangleGeometry.Size = new System.Numerics.Vector2(width, height);
            roundedRectangleGeometry.CornerRadius = new System.Numerics.Vector2((float)CornerRadius);

            bgShape = Own(_compositor.CreateSpriteShape(roundedRectangleGeometry));
            glowShape = Own(_compositor.CreateSpriteShape(roundedRectangleGeometry));
        }

        // Background border
        bgShape.StrokeBrush = Own(_compositor.CreateColorBrush(BorderStrokeColor));
        bgShape.StrokeThickness = (float)StrokeThickness;
        bgShape.FillBrush = null;

        // Glow brush
        brush = Own(_compositor.CreateRadialGradientBrush());
        brush.MappingMode = CompositionMappingMode.Absolute;

        brush.EllipseRadius = new System.Numerics.Vector2(width, height);

        UpdateBrush();

        // Glow shape
        glowShape.StrokeBrush = brush;
        glowShape.StrokeThickness = (float)StrokeThickness;
        glowShape.FillBrush = null;

        // Shape visual
        var shapeVisual = Own(_compositor.CreateShapeVisual());
        shapeVisual.Size = new System.Numerics.Vector2(width, height);

        shapeVisual.Shapes.Add(bgShape);
        shapeVisual.Shapes.Add(glowShape);

        // Clip
        var clipGeo = Own(_compositor.CreateRoundedRectangleGeometry());
        clipGeo.Size = new System.Numerics.Vector2(width, height);
        clipGeo.CornerRadius = new System.Numerics.Vector2((float)CornerRadius);

        shapeVisual.Clip = Own(_compositor.CreateGeometricClip(clipGeo));

        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetElementChildVisual(border, shapeVisual);

        props = Own(_compositor.CreatePropertySet());
        props.InsertScalar("progress", 0f);

        progressAnimation = Own(_compositor.CreateScalarKeyFrameAnimation());

        // Linear easing
        var linear = Own(_compositor.CreateLinearEasingFunction());
        progressAnimation.InsertKeyFrame(1f, (float)(Math.PI * 2), linear);
        progressAnimation.Duration = Duration;
        progressAnimation.IterationBehavior = IterationBehavior;
        progressAnimation.IterationCount = IterationCount;

        float radiusParameter = Math.Min(width, height) / 2;

        expression = Own(_compositor.CreateExpressionAnimation(
            "Vector2(cx + cos(props.progress) * r, cy + sin(props.progress) * r)"));

        expression.SetReferenceParameter("props", props);
        expression.SetScalarParameter("cx", width / 2);
        expression.SetScalarParameter("cy", height / 2);
        expression.SetScalarParameter("r", radiusParameter);

        UpdateLightingState();
    }
    private void UpdateBrush()
    {
        if (brush == null || _compositor == null)
            return;

        if (_gradientStops is null)
        {
            _gradientStops =
            [
                _compositor.CreateColorGradientStop((float)HighlightOffset, HighlightColor),
                _compositor.CreateColorGradientStop((float)MidOffset, MidColor),
                _compositor.CreateColorGradientStop((float)FadeOffset, FadeColor),
                _compositor.CreateColorGradientStop(1f, TailColor)
            ];
            foreach (var stop in _gradientStops) brush.ColorStops.Add(stop);
            return;
        }

        _gradientStops[0].Offset = (float)HighlightOffset;
        _gradientStops[0].Color = HighlightColor;
        _gradientStops[1].Offset = (float)MidOffset;
        _gradientStops[1].Color = MidColor;
        _gradientStops[2].Offset = (float)FadeOffset;
        _gradientStops[2].Color = FadeColor;
        _gradientStops[3].Color = TailColor;
    }
    private void UpdateLightingState()
    {
        if (brush == null || expression == null || props == null || progressAnimation == null)
            return;

        if (IsLighting && new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            props.StartAnimation("progress", progressAnimation);

            brush.StartAnimation(nameof(brush.CenterPoint), expression);
            brush.StartAnimation(nameof(brush.EllipseCenter), expression);
        }
        else
        {
            props.StopAnimation("progress");

            brush.StopAnimation(nameof(brush.CenterPoint));
            brush.StopAnimation(nameof(brush.EllipseCenter));
        }
    }

    private void UpdateBorderCornerRadius()
    {
        if (border == null)
            return;

        if (IsRounded)
        {
            double gridRadius = Math.Min(border.ActualWidth, border.ActualHeight) / 2;
            border.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(gridRadius);
        }
        else
        {
            border.CornerRadius = new CornerRadius(CornerRadius);
        }
    }
}
