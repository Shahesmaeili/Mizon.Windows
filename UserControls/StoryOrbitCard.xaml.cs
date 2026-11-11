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
using System.ComponentModel;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;

namespace Mizon.Windows.UserControls;

/// <summary>Decorative editorial artwork; it does not imply a music/profile action.</summary>
public sealed partial class StoryOrbitCard : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(StoryOrbitCard), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(StoryOrbitCard), new PropertyMetadata(string.Empty));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    // Three fixed local illustrations, selected once by the parent XAML.
    public int Variant { get; set; }

    private StoryOrbitMotion? _motion;
    private CanvasGeometry? _canvasGeometry;
    private CompositionPath? _compositionPath;
    private CompositionPathGeometry? _pathGeometry;
    private CompositionGeometricClip? _clip;
    private SpriteVisual? _shadowVisual;
    private DropShadow? _shadow;
    private CompositionBrush? _shadowBrush;
    private bool _loaded;

    public StoryOrbitCard() => InitializeComponent();

    private void Card_Loaded(object sender, RoutedEventArgs args)
    {
        if (_loaded || !IsLoaded) return;
        _loaded = true;
        Bindings.Update();
        LocalizationService.Current.PropertyChanged += LanguageChanged;
        ApplyLanguage();
        try
        {
            CreateArtwork();
            var variant = Math.Clamp(Variant, 0, 2);
            var direction = variant == 1 ? -1 : 1;
            _motion = new StoryOrbitMotion(this);
            _motion.Add(Artwork, new Vector3(5 * direction, -6, 0), 13 + variant * 2.3, 1.8f * direction);
            _motion.Add(Portrait, new Vector3(-3 * direction, 4, 0), 17 + variant * 1.7);
            _motion.Add(Contour, new Vector3(-6 * direction, 5, 0), 19 + variant * 2.5, -2.4f * direction);
            _motion.Add(EchoContour, new Vector3(4 * direction, -5, 0), 23 + variant * 1.9, 2.8f * direction);
            _motion.Add(Edition, new Vector3(-3, 5 * direction, 0), 16 + variant * 1.3, -1.4f);
            _motion.Add(Badge, new Vector3(4 * direction, -7, 0), 11 + variant * 2.1, 5 * direction);
            _motion.Add(Headline, new Vector3(-2 * direction, 3, 0), 14 + variant * 1.8);
            _motion.Add(Signature, new Vector3(2 * direction, -2, 0), 21 + variant * 1.4);
            _motion.Start();
        }
        catch (Exception error)
        {
            // Decoration must not prevent opening the music home page on device loss.
            _motion?.Dispose();
            _motion = null;
            ReleaseComposition();
            PictureFrame.CornerRadius = new CornerRadius(56, 76, 62, 42);
            PictureFrame.Clip = new RectangleGeometry { Rect = new Rect(0, 0, 260, 230) };
            AppLog.Error("Story editorial artwork could not initialize composition.", error);
        }
    }

    private void CreateArtwork()
    {
        var variant = Math.Clamp(Variant, 0, 2);
        var accent = variant switch
        {
            0 => Color.FromArgb(255, 173, 191, 160),
            1 => Color.FromArgb(255, 85, 133, 255),
            _ => Color.FromArgb(255, 239, 193, 112)
        };
        ColorStart.Color = accent;
        ColorEnd.Color = variant switch
        {
            0 => Color.FromArgb(255, 56, 83, 72),
            1 => Color.FromArgb(255, 19, 63, 200),
            _ => Color.FromArgb(255, 156, 91, 56)
        };
        var ink = new SolidColorBrush(accent);
        Outline.Stroke = EchoOutline.Stroke = Edition.Foreground = SignatureLine.Fill = ink;
        Edition.Text = (variant + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        BadgeIcon.Symbol = variant switch
        {
            0 => FluentIcons.Common.Symbol.Headphones,
            1 => FluentIcons.Common.Symbol.MusicNote1,
            _ => FluentIcons.Common.Symbol.Sparkle
        };
        var asset = variant switch
        {
            0 => "spotlight-performer-02.png",
            1 => "featured-performer.png",
            _ => "spotlight-performer-03.png"
        };
        // A bounded decode of the existing original cutouts; no network, extra
        // full-size image copy, browser or per-frame bitmap rendering.
        Portrait.Source = new BitmapImage
        {
            DecodePixelWidth = 560,
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
        _shadow.Opacity = 0.22f;
        _shadow.BlurRadius = 28;
        _shadow.Offset = new Vector3(0, 8, 0);
        _shadowVisual = compositor.CreateSpriteVisual();
        _shadowVisual.Size = new Vector2(260, 230);
        _shadowVisual.Shadow = _shadow;
        ElementCompositionPreview.SetElementChildVisual(ShadowHost, _shadowVisual);
    }

    private static Point PointAt(Vector2 point) => new(point.X, point.Y);

    // One source for the XAML outlines, shadow and GPU clip. No runtime XAML parsing.
    private static Vector2[] GetShape(int variant) => variant switch
    {
        0 => [new(35, 32), new(78, 12), new(183, -8), new(215, 16),
              new(237, 42), new(237, 118), new(248, 166), new(263, 217), new(220, 224), new(160, 211),
              new(116, 207), new(65, 218), new(26, 193), new(-3, 174), new(21, 135), new(14, 99),
              new(7, 65), new(5, 46), new(35, 32)],
        1 => [new(34, 27), new(69, 4), new(152, 15), new(205, 41),
              new(246, 60), new(278, 88), new(247, 119), new(221, 145), new(151, 159), new(115, 188),
              new(65, 226), new(16, 224), new(12, 183), new(10, 150), new(2, 59), new(34, 27)],
        _ => [new(70, 16), new(118, -8), new(188, -5), new(217, 36),
              new(247, 73), new(246, 161), new(212, 200), new(186, 229), new(110, 233), new(71, 201),
              new(30, 171), new(-6, 79), new(25, 43), new(38, 28), new(52, 23), new(70, 16)]
    };

    private void LanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_loaded && args.PropertyName == nameof(LocalizationService.Revision)) ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        var language = LocalizationService.Current;
        TitleText.FontFamily = CaptionText.FontFamily = language.TextFontFamily;
        Headline.FlowDirection = language.FlowDirection;
        TitleText.TextAlignment = CaptionText.TextAlignment = language.TextAlignment;
        Language = language.LanguageTag;
    }

    private void Card_Unloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded || !_loaded) return;
        _loaded = false;
        LocalizationService.Current.PropertyChanged -= LanguageChanged;
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
