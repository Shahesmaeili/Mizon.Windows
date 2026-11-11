using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using System;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;

namespace Mizon.Windows.UserControls;

/// <summary>Decorative editorial artwork; it does not imply a music/profile action.</summary>
public sealed partial class StoryOrbitCard : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(StoryOrbitCard), new PropertyMetadata(string.Empty));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    // Three fixed local editorial images, selected once by the parent XAML.
    public int Variant { get; set; }

    private StoryOrbitMotion? _motion;
    private CanvasGeometry? _canvasGeometry;
    private CompositionPath? _compositionPath;
    private CompositionPathGeometry? _pathGeometry;
    private CompositionGeometricClip? _clip;
    private SpriteVisual? _shadowVisual;
    private DropShadow? _shadow;
    private CompositionBrush? _shadowBrush;
    private SpriteVisual? _titleShadowVisual;
    private DropShadow? _titleShadow;
    private bool _loaded;

    public StoryOrbitCard() => InitializeComponent();

    private void Card_Loaded(object sender, RoutedEventArgs args)
    {
        if (_loaded || !IsLoaded) return;
        _loaded = true;
        Bindings.Update();
        try
        {
            CreateArtwork();
            var variant = Math.Clamp(Variant, 0, 2);
            var direction = variant == 1 ? -1 : 1;
            var phase = variant * 113f;
            _motion = new StoryOrbitMotion(this);
            _motion.Add(Artwork, new Vector3(6 * direction, -6, 0), 19 + variant * 2.3, 1.2f * direction, phase);
            _motion.Add(Portrait, new Vector3(-4 * direction, 4, 0), 23 + variant * 1.7, phaseDegrees: phase + 137);
            _motion.Add(Contour, new Vector3(-7 * direction, 5, 0), 27 + variant * 2.5, -1.8f * direction, phase + 71);
            _motion.Add(EchoContour, new Vector3(4 * direction, -5, 0), 31 + variant * 1.9, 2 * direction, phase + 219);
            _motion.Add(Edition, new Vector3(-4, 5 * direction, 0), 22 + variant * 1.3, -1.2f, phase + 43);
            _motion.Add(Badge, new Vector3(5 * direction, -6, 0), 17 + variant * 2.1, 3 * direction, phase + 173);
            _motion.Add(Headline, new Vector3(-3 * direction, 3, 0), 21 + variant * 1.8, .6f * direction, phase + 283);
            _motion.Start();
        }
        catch (Exception error)
        {
            // Decoration must not prevent opening the music home page on device loss.
            _motion?.Dispose();
            _motion = null;
            ReleaseComposition();
            PictureFrame.CornerRadius = new CornerRadius(56, 76, 62, 42);
            PictureFrame.Clip = new RectangleGeometry { Rect = new Rect(0, 0, PictureFrame.Width, PictureFrame.Height) };
            AppLog.Error("Story editorial artwork could not initialize composition.", error);
        }
    }

    private void CreateArtwork()
    {
        var variant = Math.Clamp(Variant, 0, 2);
        var accent = variant switch
        {
            0 => Color.FromArgb(255, 163, 170, 148),
            1 => Color.FromArgb(255, 0, 88, 245),
            _ => Color.FromArgb(255, 244, 194, 64)
        };
        ArtworkColor.Color = accent;
        var ink = new SolidColorBrush(accent);
        Outline.Stroke = EchoOutline.Stroke = ink;
        Edition.Foreground = variant == 2 ? new SolidColorBrush(Color.FromArgb(255, 106, 66, 44)) : ink;
        Edition.Text = (variant + 22).ToString(System.Globalization.CultureInfo.InvariantCulture);
        RatingText.Text = variant switch { 0 => "5,0", 1 => "4,5", _ => "4,8" };
        ApplyReferenceLayout(variant);
        var artworkHeight = variant == 2 ? 260 : 230;
        Artwork.Height = PictureFrame.Height = Contour.Height = EchoContour.Height = artworkHeight;
        var asset = variant switch { 0 => "orbit-models.png", 1 => "orbit-illustration.jpg", _ => "orbit-team.jpg" };
        // Packaged images: a user-approved similar fictional model and the
        // original Illustration/Team artwork. Cropping stays in the native view.
        Portrait.Source = new BitmapImage
        {
            DecodePixelWidth = variant == 1 ? 1024 : 800,
            UriSource = new Uri($"ms-appx:///Assets/Discovery/{asset}")
        };

        var points = GetShape(variant);
        var figure = new PathFigure { StartPoint = PointAt(points[0]), IsClosed = true, IsFilled = true };
        using var builder = new CanvasPathBuilder(CanvasDevice.GetSharedDevice());
        builder.BeginFigure(points[0]);
        for (var index = 1; index < points.Length; index += 3)
        {
            builder.AddCubicBezier(points[index], points[index + 1], points[index + 2]);
            figure.Segments.Add(new BezierSegment
            {
                Point1 = PointAt(points[index]), Point2 = PointAt(points[index + 1]), Point3 = PointAt(points[index + 2])
            });
        }
        builder.EndFigure(CanvasFigureLoop.Closed);
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        Outline.Data = EchoOutline.Data = ShadowMask.Data = geometry;

        var visual = ElementCompositionPreview.GetElementVisual(PictureFrame);
        var compositor = visual.Compositor;
        _canvasGeometry = CanvasGeometry.CreatePath(builder);
        _compositionPath = new CompositionPath(_canvasGeometry);
        _pathGeometry = compositor.CreatePathGeometry(_compositionPath);
        _clip = compositor.CreateGeometricClip(_pathGeometry);
        PictureFrame.Clip = null;
        PictureFrame.CornerRadius = new CornerRadius(0);
        visual.Clip = _clip;
        _shadowBrush = ShadowMask.GetAlphaMask();
        _shadow = compositor.CreateDropShadow();
        _shadow.Mask = _shadowBrush;
        _shadow.Color = accent;
        _shadow.Opacity = 0.16f;
        _shadow.BlurRadius = 32;
        _shadow.Offset = new Vector3(0, 14, 0);
        _shadowVisual = compositor.CreateSpriteVisual();
        _shadowVisual.Size = new Vector2(260, artworkHeight);
        _shadowVisual.Shadow = _shadow;
        ElementCompositionPreview.SetElementChildVisual(ShadowHost, _shadowVisual);

        _titleShadow = compositor.CreateDropShadow();
        _titleShadow.Mask = TitleText.GetAlphaMask();
        _titleShadow.Color = Color.FromArgb(255, 20, 24, 32);
        _titleShadow.BlurRadius = 9;
        _titleShadow.Offset = new Vector3(0, 2, 0);
        _titleShadow.Opacity = 0.35f;
        _titleShadowVisual = compositor.CreateSpriteVisual();
        _titleShadowVisual.RelativeSizeAdjustment = Vector2.One;
        _titleShadowVisual.Shadow = _titleShadow;
        ElementCompositionPreview.SetElementChildVisual(TitleShadowHost, _titleShadowVisual);
    }

    private void ApplyReferenceLayout(int variant)
    {
        // Author the three different silhouettes/anchors in one design space,
        // matching the reference's lower middle card and scooped right portrait.
        var artwork = variant switch { 0 => new Point(31, 60), 1 => new Point(25, 127), _ => new Point(30, 59) };
        Place(Artwork, artwork.X, artwork.Y);
        Place(Contour, artwork.X - 2, artwork.Y + 5);
        Place(EchoContour, artwork.X - 2, artwork.Y + 5);
        Contour.RenderTransform = new CompositeTransform { ScaleX = 1.065, ScaleY = 1.065, Rotation = variant == 2 ? -3 : 0 };
        EchoContour.RenderTransform = new CompositeTransform { ScaleX = 1.11, ScaleY = 1.11, Rotation = variant == 2 ? -4 : -1 };
        switch (variant)
        {
            case 0:
                Place(Edition, 196, 0); Edition.RenderTransform = new RotateTransform { Angle = 8 };
                Place(Badge, 58, 64); Badge.RenderTransform = new RotateTransform { Angle = -13 };
                Place(Headline, 26, 213); Headline.RenderTransform = new RotateTransform { Angle = -10 };
                TitleText.FontSize = 76;
                Portrait.Width = 286; Portrait.Height = 286; Place(Portrait, -20, -12);
                break;
            case 1:
                Place(Edition, 26, 74); Edition.RenderTransform = new RotateTransform { Angle = -13 };
                Place(Badge, 221, 103); Badge.RenderTransform = new RotateTransform { Angle = 15 };
                Place(Headline, 37, 236); Headline.RenderTransform = new RotateTransform();
                TitleText.FontSize = 52;
                Portrait.Width = 720; Portrait.Height = 1029; Place(Portrait, -196, -82);
                break;
            default:
                Place(Edition, 247, 75); Edition.RenderTransform = new RotateTransform { Angle = 84 };
                Place(Badge, 263, 177); Badge.RenderTransform = new RotateTransform { Angle = -10 };
                Place(Headline, 38, 219); Headline.RenderTransform = new RotateTransform();
                TitleText.FontSize = 76;
                Portrait.Width = 252; Portrait.Height = 330; Place(Portrait, 10, -4);
                break;
        }
    }

    private static void Place(FrameworkElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
    }

    private static Point PointAt(Vector2 point) => new(point.X, point.Y);

    // One source for the XAML outlines, shadow and GPU clip. No runtime XAML parsing.
    private static Vector2[] GetShape(int variant)
    {
        Vector2[] points = variant switch
        {
            0 => [new(30, 45), new(68, 22), new(158, -3), new(197, 7),
                  new(223, 14), new(233, 65), new(246, 119), new(261, 177), new(259, 220), new(227, 221),
                  new(196, 221), new(151, 199), new(111, 202), new(73, 206), new(29, 209), new(11, 193),
                  new(-3, 180), new(31, 164), new(24, 134), new(16, 98), new(0, 65), new(30, 45)],
            1 => [new(28, 9), new(67, -2), new(144, 2), new(191, 23),
                  new(231, 40), new(266, 66), new(257, 90), new(249, 118), new(183, 131), new(145, 151),
                  new(104, 174), new(49, 198), new(18, 180), new(-1, 169), new(8, 141), new(8, 112),
                  new(10, 71), new(0, 24), new(28, 9)],
            _ => [new(35, 66), new(61, 20), new(133, -2), new(166, 12),
                  new(193, 23), new(173, 78), new(188, 108), new(198, 132), new(220, 142), new(248, 144),
                  new(232, 195), new(193, 225), new(140, 225), new(85, 225), new(57, 207), new(43, 176),
                  new(25, 136), new(14, 100), new(35, 66)]
        };
        // The right silhouette is taller than the left, not a circular avatar.
        // Match its portrait proportions without stretching the original image.
        if (variant == 2)
            for (var index = 0; index < points.Length; index++)
                points[index] = new Vector2(140 + (points[index].X - 140) * .95f, points[index].Y * 1.1f);
        return points;
    }

    private void Card_Unloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded || !_loaded) return;
        _loaded = false;
        Bindings.StopTracking();
        _motion?.Dispose();
        _motion = null;
        ReleaseComposition();
        Portrait.Source = null;
    }

    private void ReleaseComposition()
    {
        ElementCompositionPreview.GetElementVisual(PictureFrame).Clip = null;
        ElementCompositionPreview.SetElementChildVisual(ShadowHost, null);
        ElementCompositionPreview.SetElementChildVisual(TitleShadowHost, null);
        if (_titleShadowVisual is not null) _titleShadowVisual.Shadow = null;
        _titleShadowVisual?.Dispose(); _titleShadowVisual = null;
        if (_titleShadow is not null) _titleShadow.Mask = null;
        _titleShadow?.Dispose(); _titleShadow = null;
        if (_shadowVisual is not null) _shadowVisual.Shadow = null;
        _shadowVisual?.Dispose(); _shadowVisual = null;
        if (_shadow is not null) _shadow.Mask = null;
        _shadow?.Dispose(); _shadow = null;
        // GetAlphaMask returns a live XAML-owned mask; detach our reference,
        // never close it while the cached control may be loaded again.
        _shadowBrush = null;
        _clip?.Dispose(); _clip = null;
        _pathGeometry?.Dispose(); _pathGeometry = null;
        _compositionPath = null;
        _canvasGeometry?.Dispose(); _canvasGeometry = null;
    }
}
