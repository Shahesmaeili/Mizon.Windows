using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Mizon.Windows.Services;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Controls;

/// <summary>A cached-file artwork surface with a rounded, colored native Composition shadow.</summary>
public sealed partial class FeatureArtwork : UserControl
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(Uri), typeof(FeatureArtwork), new PropertyMetadata(null, SourceChanged));
    public static readonly DependencyProperty ShadowColorProperty = DependencyProperty.Register(
        nameof(ShadowColor), typeof(Color), typeof(FeatureArtwork), new PropertyMetadata(Colors.Black, AppearanceChanged));
    public static readonly DependencyProperty UseAutoShadowColorProperty = DependencyProperty.Register(
        nameof(UseAutoShadowColor), typeof(bool), typeof(FeatureArtwork), new PropertyMetadata(true, AppearanceChanged));
    public static readonly DependencyProperty TransitionDurationProperty = DependencyProperty.Register(
        nameof(TransitionDuration), typeof(TimeSpan), typeof(FeatureArtwork), new PropertyMetadata(TimeSpan.FromMilliseconds(260)));
    public static readonly DependencyProperty StretchProperty = DependencyProperty.Register(
        nameof(Stretch), typeof(Stretch), typeof(FeatureArtwork), new PropertyMetadata(Stretch.UniformToFill, AppearanceChanged));

    private readonly Grid _shadowHost = new() { IsHitTestVisible = false };
    private readonly Grid _imageHost = new() { IsHitTestVisible = false };
    private readonly Rectangle _shadowMask = new()
    {
        Fill = new SolidColorBrush(Color.FromArgb(255, 9, 12, 15)),
        IsHitTestVisible = false
    };
    private readonly Image _image = new() { Stretch = Stretch.UniformToFill };
    private readonly UISettings _settings = new();
    private readonly Dictionary<Uri, Color> _edgeColors = [];
    private readonly Queue<Uri> _edgeColorOrder = [];
    private CancellationTokenSource? _decode;
    private Uri? _decodedSource;
    private Color? _edgeColor;
    private SpriteVisual? _shadowVisual;
    private DropShadow? _shadow;
    private CompositionRoundedRectangleGeometry? _geometry;
    private CompositionGeometricClip? _imageClip;
    private ScalarKeyFrameAnimation? _fade;
    private CompositionScopedBatch? _fadeBatch;
    private long _cornerRadiusToken;
    private int _version;
    private bool _loaded;
    private Vector2 _lastSize;
    private float _lastRadius = -1;

    public FeatureArtwork()
    {
        IsHitTestVisible = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        _imageHost.Children.Add(_shadowMask);
        _imageHost.Children.Add(_image);
        // CornerRadius belongs to this control, not to the outer Grid. A rounded
        // Grid clips all descendants, including the glow outside the artwork.
        var root = new Grid();
        root.Children.Add(_shadowHost);
        root.Children.Add(_imageHost);
        Content = root;
        Loaded += ArtworkLoaded;
        Unloaded += ArtworkUnloaded;
        SizeChanged += ArtworkSizeChanged;
    }

    public Uri? Source { get => (Uri?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public Color ShadowColor { get => (Color)GetValue(ShadowColorProperty); set => SetValue(ShadowColorProperty, value); }
    public bool UseAutoShadowColor { get => (bool)GetValue(UseAutoShadowColorProperty); set => SetValue(UseAutoShadowColorProperty, value); }
    public TimeSpan TransitionDuration { get => (TimeSpan)GetValue(TransitionDurationProperty); set => SetValue(TransitionDurationProperty, value); }
    public Stretch Stretch { get => (Stretch)GetValue(StretchProperty); set => SetValue(StretchProperty, value); }
    public event EventHandler<string>? ImageFailed;

    private static void SourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (FeatureArtwork)sender;
        control.CancelDecode();
        if (control.Source is null)
        {
            control.EndFade();
            control._image.Source = null;
            control._decodedSource = null;
            control._edgeColor = null;
            control.UpdateAppearance();
        }
        else if (control._loaded) _ = control.LoadSourceAsync(control.Source);
    }

    private static void AppearanceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((FeatureArtwork)sender).UpdateAppearance();

    private void ArtworkLoaded(object sender, RoutedEventArgs args)
    {
        if (_loaded || !IsLoaded) return;
        _loaded = true;
        _cornerRadiusToken = RegisterPropertyChangedCallback(CornerRadiusProperty, CornerRadiusChanged);
        _settings.AnimationsEnabledChanged += AnimationsChanged;
        CreateShadow();
        UpdateBounds();
        UpdateAppearance();
        if (Source is { } source && (_image.Source is null || !Equals(source, _decodedSource)))
            _ = LoadSourceAsync(source);
    }

    private void ArtworkUnloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded || !_loaded) return;
        _loaded = false;
        CancelDecode();
        _settings.AnimationsEnabledChanged -= AnimationsChanged;
        UnregisterPropertyChangedCallback(CornerRadiusProperty, _cornerRadiusToken);
        EndFade();
        ReleaseShadow();
        // A responsive move may reattach this same control. Keep its one decoded
        // bitmap; clearing Source or collecting the control releases that bitmap.
    }

    private async Task LoadSourceAsync(Uri source)
    {
        CancelDecode();
        var version = _version;
        var cancellation = _decode = new();
        var token = cancellation.Token;
        try
        {
            // All network access/cache policy belongs to MediaDownloader. This view
            // decodes the completed local file; it never opens a second HTTP request.
            StorageFile file;
            if (source.IsFile) file = await StorageFile.GetFileFromPathAsync(source.LocalPath).AsTask(token);
            else if (source.Scheme is "ms-appx" or "ms-appdata")
                file = await StorageFile.GetFileFromApplicationUriAsync(source).AsTask(token);
            else throw new ArgumentException("Artwork must resolve to a cached or packaged image.", nameof(source));
            using var stream = await file.OpenReadAsync().AsTask(token);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token);
            var maxDimension = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
            var scale = Math.Min(1d, 1600d / Math.Max(1u, maxDimension));
            var width = Math.Max(1, (int)Math.Round(decoder.PixelWidth * scale));
            stream.Seek(0);
            var image = new BitmapImage { DecodePixelWidth = width };
            await image.SetSourceAsync(stream).AsTask(token);
            var edgeColor = await GetEdgeColorAsync(source, file.Path, token);
            if (!_loaded || token.IsCancellationRequested || version != _version || !Equals(Source, source)) return;
            _decodedSource = source;
            _edgeColor = edgeColor;
            if (edgeColor is { } color && _edgeColors.TryAdd(source, color))
            {
                _edgeColorOrder.Enqueue(source);
                if (_edgeColorOrder.Count > 12) _edgeColors.Remove(_edgeColorOrder.Dequeue());
            }
            _image.Source = image;
            UpdateAppearance();
            BeginFade();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!_loaded || token.IsCancellationRequested || version != _version) return;
            AppLog.Error("Featured artwork could not be decoded.", error);
            ImageFailed?.Invoke(this, "نمایش تصویر انجام نشد. دوباره تلاش کن.");
        }
        finally
        {
            if (ReferenceEquals(_decode, cancellation)) _decode = null;
            cancellation.Dispose();
        }
    }

    private async Task<Color?> GetEdgeColorAsync(Uri source, string path, CancellationToken token)
    {
        if (_edgeColors.TryGetValue(source, out var cached)) return cached;
        try
        {
            return await Task.Run<Color?>(async () =>
            {
                token.ThrowIfCancellationRequested();
                // Keep the previous hero's exact perimeter average, including the
                // repeated corner pixels. GPU readback stays off the UI thread.
                var device = CanvasDevice.GetSharedDevice();
                using var bitmap = await CanvasBitmap.LoadAsync(device, path).AsTask(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var width = checked((int)bitmap.SizeInPixels.Width);
                var height = checked((int)bitmap.SizeInPixels.Height);
                if (width == 0 || height == 0) return null;
                long red = 0, green = 0, blue = 0;
                void AddEdge(Color pixel)
                {
                    red += pixel.R;
                    green += pixel.G;
                    blue += pixel.B;
                }
                foreach (var pixel in bitmap.GetPixelColors(0, 0, width, 1)) AddEdge(pixel);
                foreach (var pixel in bitmap.GetPixelColors(0, height - 1, width, 1)) AddEdge(pixel);
                foreach (var pixel in bitmap.GetPixelColors(0, 0, 1, height)) AddEdge(pixel);
                foreach (var pixel in bitmap.GetPixelColors(width - 1, 0, 1, height)) AddEdge(pixel);
                token.ThrowIfCancellationRequested();
                var count = 2L * (width + height);
                return Color.FromArgb(255, (byte)(red / count), (byte)(green / count), (byte)(blue / count));
            }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            // Shadow analysis must never prevent a valid cached image from loading.
            AppLog.Error("Featured artwork perimeter color could not be read.", error);
            return null;
        }
    }

    private void CancelDecode()
    {
        _version++;
        _decode?.Cancel();
        _decode?.Dispose();
        _decode = null;
    }

    private void CreateShadow()
    {
        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        _geometry = compositor.CreateRoundedRectangleGeometry();
        _shadow = compositor.CreateDropShadow();
        _shadow.BlurRadius = 80;
        _shadow.Offset = Vector3.Zero;
        // Use the live XAML shape's alpha mask instead of an unattached visual
        // surface. XAML updates this mask as the rounded artwork is resized.
        _shadow.Mask = _shadowMask.GetAlphaMask();
        _shadowVisual = compositor.CreateSpriteVisual();
        _shadowVisual.Shadow = _shadow;
        ElementCompositionPreview.SetElementChildVisual(_shadowHost, _shadowVisual);
        // Only artwork is clipped. The sibling shadow remains free to extend
        // outside the rounded rectangle on every edge.
        _imageClip = compositor.CreateGeometricClip(_geometry);
        ElementCompositionPreview.GetElementVisual(_imageHost).Clip = _imageClip;
        _lastSize = Vector2.Zero;
        _lastRadius = -1;
    }

    private void UpdateAppearance()
    {
        if (_image.Stretch != Stretch) _image.Stretch = Stretch;
        if (_shadow is null) return;
        _shadow.Color = UseAutoShadowColor && _edgeColor is { } color ? color : ShadowColor;
        _shadow.Opacity = _image.Source is null ? 0 : 1;
    }

    private void ArtworkSizeChanged(object sender, SizeChangedEventArgs args) => UpdateBounds();
    private void CornerRadiusChanged(DependencyObject sender, DependencyProperty property) => UpdateBounds();

    private void UpdateBounds()
    {
        if (!_loaded || _geometry is null || !double.IsFinite(ActualWidth) || !double.IsFinite(ActualHeight)
            || ActualWidth <= 0 || ActualHeight <= 0) return;
        var size = new Vector2((float)Math.Max(0, ActualWidth), (float)Math.Max(0, ActualHeight));
        var requestedRadius = double.IsFinite(CornerRadius.TopLeft) ? CornerRadius.TopLeft : 0;
        var radius = (float)Math.Clamp(requestedRadius, 0, Math.Min(size.X, size.Y) / 2);
        if (_lastSize == size && _lastRadius == radius) return;
        _lastSize = size;
        _lastRadius = radius;
        _geometry.Size = size;
        _geometry.CornerRadius = new(radius);
        _shadowVisual!.Size = size;
        _shadowMask.RadiusX = radius;
        _shadowMask.RadiusY = radius;
    }

    private void BeginFade()
    {
        EndFade();
        if (!_loaded || !_settings.AnimationsEnabled || TransitionDuration <= TimeSpan.Zero) return;
        var compositor = ElementCompositionPreview.GetElementVisual(_imageHost).Compositor;
        _fadeBatch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _fadeBatch.Completed += FadeCompleted;
        _fade = compositor.CreateScalarKeyFrameAnimation();
        _fade.Target = nameof(Opacity);
        _fade.InsertKeyFrame(0, 0);
        _fade.InsertKeyFrame(1, 1);
        _fade.Duration = TimeSpan.FromMilliseconds(Math.Clamp(TransitionDuration.TotalMilliseconds, 1, 1000));
        _imageHost.StartAnimation(_fade);
        _fadeBatch.End();
    }

    private void FadeCompleted(object sender, CompositionBatchCompletedEventArgs args)
    {
        if (ReferenceEquals(sender, _fadeBatch)) EndFade();
    }

    private void AnimationsChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_loaded && !_settings.AnimationsEnabled) EndFade();
    });

    private void EndFade()
    {
        if (_fadeBatch is not null)
        {
            _fadeBatch.Completed -= FadeCompleted;
            _fadeBatch.Dispose();
            _fadeBatch = null;
        }
        if (_fade is not null)
        {
            _imageHost.StopAnimation(_fade);
            _fade.Dispose();
            _fade = null;
        }
        if (_imageHost.Opacity != 1) _imageHost.Opacity = 1;
    }

    private void ReleaseShadow()
    {
        ElementCompositionPreview.SetElementChildVisual(_shadowHost, null);
        ElementCompositionPreview.GetElementVisual(_imageHost).Clip = null;
        if (_shadowVisual is not null) _shadowVisual.Shadow = null;
        if (_shadow is not null) _shadow.Mask = null;
        _imageClip?.Dispose(); _imageClip = null;
        _shadowVisual?.Dispose(); _shadowVisual = null;
        _shadow?.Dispose(); _shadow = null;
        _geometry?.Dispose(); _geometry = null;
    }
}
