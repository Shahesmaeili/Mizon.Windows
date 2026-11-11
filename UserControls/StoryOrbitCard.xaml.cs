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
    public int Variant { get; set; }

    private StoryOrbitMotion? _motion;
    private CanvasGeometry? _canvasGeometry;
    private CompositionPath? _compositionPath;
    private CompositionPathGeometry? _pathGeometry;
    private CompositionGeometricClip? _clip;
    private SpriteVisual? _ambientVisual, _contactVisual, _titleShadowVisual;
    private DropShadow? _ambientShadow, _contactShadow, _titleShadow;
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
        }
        catch (Exception error)
        {
            ReleaseComposition();
            PictureFrame.Visibility = Visibility.Collapsed;
            AppLog.Error("Story editorial photo/clip could not initialize.", error);
            return;
        }

        try
        {
            var variant = Math.Clamp(Variant, 0, 2);
            var direction = variant == 1 ? -1 : 1;
            var phase = variant * 113f;
            _motion = new StoryOrbitMotion(this);
            _motion.Add(Artwork, new Vector3(4 * direction, -4, 0), 23 + variant * 2.3, .45f * direction, phase);
            _motion.Add(Portrait, new Vector3(-3 * direction, 3, 0), 27 + variant * 1.7, phaseDegrees: phase + 137);
            _motion.Add(Contour, new Vector3(-3 * direction, 3, 0), 31 + variant * 2.5, -.4f * direction, phase + 71);
            _motion.Add(Edition, new Vector3(-5, 5 * direction, 0), 26 + variant * 1.3, -1.2f, phase + 43);
            _motion.Add(Badge, new Vector3(5 * direction, -5, 0), 21 + variant * 2.1, 2 * direction, phase + 173);
            _motion.Add(Headline, new Vector3(-3 * direction, 3, 0), 25 + variant * 1.8, .5f * direction, phase + 283);
            _motion.Start();
        }
        catch (Exception error)
        {
            // Animation is optional; keep the successfully loaded static image.
            _motion?.Dispose();
            _motion = null;
            AppLog.Error("Story editorial motion could not initialize.", error);
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
        MatteShape.Fill = Outline.Stroke = ink;
        Outline.Opacity = variant switch { 0 => .5, 1 => .16, _ => .44 };
        Edition.Foreground = variant == 2 ? new SolidColorBrush(Color.FromArgb(255, 106, 66, 44)) : ink;
        Edition.Text = (variant + 22).ToString(System.Globalization.CultureInfo.InvariantCulture);
        RatingText.Text = variant switch { 0 => "5,0", 1 => "4,5", _ => "4,8" };
        ApplyReferenceLayout(variant);

        var asset = variant switch { 0 => "orbit-models.png", 1 => "orbit-illustration.jpg", _ => "orbit-team.jpg" };
        Portrait.Source = new BitmapImage
        {
            DecodePixelWidth = variant == 1 ? 1024 : 800,
            UriSource = new Uri($"ms-appx:///Assets/Discovery/{asset}")
        };
        PictureFrame.Visibility = Visibility.Visible;

        // These are two separately traced curves. Expanding the photo path
        // would lose the reference's waist, draped bottom and unequal rim gaps.
        var origin = new Vector2(120 + variant * 440, 320);
        using var builder = new CanvasPathBuilder(CanvasDevice.GetSharedDevice());
        var points = StoryOrbitGeometry.Photo(variant);
        builder.BeginFigure(points[0] - origin);
        for (var index = 1; index < points.Length; index += 3)
            builder.AddCubicBezier(points[index] - origin, points[index + 1] - origin, points[index + 2] - origin);
        builder.EndFigure(CanvasFigureLoop.Closed);

        var visual = ElementCompositionPreview.GetElementVisual(PictureFrame);
        var compositor = visual.Compositor;
        _canvasGeometry = CanvasGeometry.CreatePath(builder);
        _compositionPath = new CompositionPath(_canvasGeometry);
        _pathGeometry = compositor.CreatePathGeometry(_compositionPath);
        _clip = compositor.CreateGeometricClip(_pathGeometry);
        visual.Clip = _clip;

        try
        {
            CreateDecoration(variant, accent, origin, compositor);
        }
        catch (Exception error)
        {
            // A paper-edge or shadow failure must not hide a decoded photograph
            // or remove its successfully created clip.
            ReleaseDecoration();
            AppLog.Error("Story editorial decoration is unavailable; photo retained.", error);
        }
    }

    private void CreateDecoration(int variant, Color accent, Vector2 origin, Compositor compositor)
    {
        var contour = StoryOrbitGeometry.Contour(variant);
        // WinUI Geometry has one owning Path. Share immutable coordinates,
        // never the PathGeometry/PathFigure/BezierSegment DependencyObjects.
        Outline.Data = CreateOutline(contour, origin);
        MatteShape.Data = CreateOutline(contour, origin);

        // The shape's own fill is opaque; only its parent is translucent, so
        // the live alpha mask retains a clean silhouette for both soft shadows.
        var mask = MatteShape.GetAlphaMask();
        _ambientShadow = compositor.CreateDropShadow();
        _ambientShadow.Mask = mask;
        _ambientShadow.Color = accent;
        _ambientShadow.Opacity = variant switch { 0 => .12f, 1 => .16f, _ => .10f };
        _ambientShadow.BlurRadius = variant == 1 ? 60 : 76;
        _ambientShadow.Offset = new Vector3(0, variant == 1 ? 22 : 30, 0);
        _ambientVisual = compositor.CreateSpriteVisual();
        _ambientVisual.Size = new Vector2(480, 520);
        _ambientVisual.Shadow = _ambientShadow;
        ElementCompositionPreview.SetElementChildVisual(AmbientShadowHost, _ambientVisual);

        _contactShadow = compositor.CreateDropShadow();
        _contactShadow.Mask = mask;
        _contactShadow.Color = Color.FromArgb(255, 28, 26, 22);
        _contactShadow.Opacity = variant switch { 0 => .19f, 1 => .035f, _ => .10f };
        _contactShadow.BlurRadius = 46;
        _contactShadow.Offset = new Vector3(0, variant == 1 ? 16 : 22, 0);
        _contactVisual = compositor.CreateSpriteVisual();
        _contactVisual.Size = new Vector2(480, 520);
        _contactVisual.Shadow = _contactShadow;
        ElementCompositionPreview.SetElementChildVisual(ContactShadowHost, _contactVisual);

        _titleShadow = compositor.CreateDropShadow();
        _titleShadow.Mask = TitleText.GetAlphaMask();
        _titleShadow.Color = Color.FromArgb(255, 20, 24, 32);
        _titleShadow.BlurRadius = 12;
        _titleShadow.Offset = new Vector3(0, 3, 0);
        _titleShadow.Opacity = .25f;
        _titleShadowVisual = compositor.CreateSpriteVisual();
        _titleShadowVisual.RelativeSizeAdjustment = Vector2.One;
        _titleShadowVisual.Shadow = _titleShadow;
        ElementCompositionPreview.SetElementChildVisual(TitleShadowHost, _titleShadowVisual);
    }

    private static PathGeometry CreateOutline(ReadOnlySpan<Vector2> points, Vector2 origin)
    {
        var figure = new PathFigure { StartPoint = PointAt(points[0] - origin), IsClosed = true, IsFilled = true };
        for (var index = 1; index < points.Length; index += 3)
            figure.Segments.Add(new BezierSegment
            {
                Point1 = PointAt(points[index] - origin),
                Point2 = PointAt(points[index + 1] - origin),
                Point3 = PointAt(points[index + 2] - origin)
            });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private void ApplyReferenceLayout(int variant)
    {
        var origin = new Point(120 + variant * 440, 320);
        switch (variant)
        {
            case 0:
                Place(Edition, 387 - origin.X, 348 - origin.Y);
                Edition.FontSize = 64;
                Edition.RenderTransform = new RotateTransform { Angle = 8 };
                Place(Badge, 218 - origin.X, 407 - origin.Y);
                Badge.RenderTransform = new RotateTransform { Angle = -13 };
                Place(Headline, 190 - origin.X, 650 - origin.Y);
                Headline.RenderTransform = new RotateTransform { Angle = -10 };
                TitleText.FontSize = 84;
                Portrait.Width = 340; Portrait.Height = 340;
                Place(Portrait, 170 - origin.X, 397 - origin.Y);
                break;
            case 1:
                Place(Edition, 636 - origin.X, 483 - origin.Y);
                Edition.FontSize = 84;
                Edition.RenderTransform = new RotateTransform { Angle = -13 };
                Place(Badge, 872 - origin.X, 481 - origin.Y);
                Badge.RenderTransform = new RotateTransform { Angle = 15 };
                Place(Headline, 672 - origin.X, 670 - origin.Y);
                Headline.RenderTransform = new RotateTransform();
                TitleText.FontSize = 56;
                Portrait.Width = 714.54; Portrait.Height = 1021.18;
                Place(Portrait, 468.25 - origin.X, 429.33 - origin.Y);
                break;
            default:
                Place(Edition, 1382 - origin.X, 443 - origin.Y);
                Edition.FontSize = 70;
                Edition.RenderTransform = new RotateTransform { Angle = 84 };
                Place(Badge, 1368 - origin.X, 550 - origin.Y);
                Badge.RenderTransform = new RotateTransform { Angle = -10 };
                Place(Headline, 1111 - origin.X, 641 - origin.Y);
                Headline.RenderTransform = new RotateTransform();
                TitleText.FontSize = 74;
                Portrait.Width = 300.62; Portrait.Height = 393.72;
                Place(Portrait, 1109.03 - origin.X, 393.06 - origin.Y);
                break;
        }
    }

    private static void Place(FrameworkElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
    }

    private static Point PointAt(Vector2 point) => new(point.X, point.Y);

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
        ReleaseDecoration();
        _clip?.Dispose(); _clip = null;
        _pathGeometry?.Dispose(); _pathGeometry = null;
        _compositionPath = null;
        _canvasGeometry?.Dispose(); _canvasGeometry = null;
    }

    private void ReleaseDecoration()
    {
        ElementCompositionPreview.SetElementChildVisual(AmbientShadowHost, null);
        ElementCompositionPreview.SetElementChildVisual(ContactShadowHost, null);
        ElementCompositionPreview.SetElementChildVisual(TitleShadowHost, null);
        ReleaseShadow(ref _titleShadowVisual, ref _titleShadow);
        ReleaseShadow(ref _contactVisual, ref _contactShadow);
        ReleaseShadow(ref _ambientVisual, ref _ambientShadow);
        Outline.Data = null;
        MatteShape.Data = null;
    }

    private static void ReleaseShadow(ref SpriteVisual? visual, ref DropShadow? shadow)
    {
        if (visual is not null) visual.Shadow = null;
        visual?.Dispose(); visual = null;
        // Alpha masks are live XAML-owned brushes: detach without disposing
        // so the cached page can safely create them again on the next visit.
        if (shadow is not null) shadow.Mask = null;
        shadow?.Dispose(); shadow = null;
    }
}
