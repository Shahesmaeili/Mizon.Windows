using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using System.Text.Json;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Graphics;
using Windows.Storage;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Mizon.Windows.PaletteProvider;

// Optional image diagnostic. Repeatedly obtains the same production content's
// view, then lets the installed native renderer build a new tree each time.
// Its non-activating offscreen window provides a real XamlRoot for image decode.
internal static class ImageReselectionTests
{
    internal static async Task RunAsync(NativeCardRenderer renderer, string imagePath)
    {
        var file = await StorageFile.GetFileFromPathAsync(imagePath);
        using (var stream = await file.OpenReadAsync())
        {
            var decoder = await BitmapDecoder.CreateAsync(stream);
            Require(decoder.PixelWidth > 0 && decoder.PixelHeight > 0, "local image decodes successfully");
        }
        var imageUri = new Uri(imagePath).AbsoluteUri;
        var host = new Grid();
        var window = new Window { Content = host };
        window.AppWindow.IsShownInSwitchers = false;
        window.AppWindow.Move(new PointInt32(-32000, -32000));
        window.AppWindow.ResizeClient(new SizeInt32(500, 300));
        try
        {
            // Avoid activating the diagnostic or displaying it on any monitor.
            window.AppWindow.Show(false);
            await Task.Delay(100);
            Require(host.XamlRoot is not null, "offscreen diagnostic has a real XamlRoot");
            await RunConstrainedSelectionsAsync(renderer, host, imageUri);
            await RunSelectionsAsync(renderer, host, imageUri, imagePath);
            await RunSelectionsAsync(renderer, host, imageUri, imagePath, releaseRendererBeforeAttach: true);
        }
        finally
        {
            host.Children.Clear();
            window.AppWindow.Hide();
            window.Content = null;
            window.Close();
        }
    }

    private static async Task RunSelectionsAsync(NativeCardRenderer renderer, Grid host, string imageUri, string imagePath, bool releaseRendererBeforeAttach = false)
    {
        foreach (var kind in new[] { PaletteResultKind.Music, PaletteResultKind.Client })
        {
            var baseline = NativeObject.LiveCount;
            var state = new FixtureState(new()
            {
                Kind = kind, PublicId = Guid.NewGuid(), Title = "Arezome", PersianTitle = "آرزومه",
                FirstName = "Ali", LastName = "Shahesmaeili", NickName = "Alish", Username = "alish",
                DurationText = "03:30", LikesCount = 125, ViewsCount = 987, AlbumTitle = "خاطره‌ها", GenreText = "پاپ"
            }, imageUri);
            var content = PalettePreviewData.BuildPreview(state);
            try
            {
                for (var selection = 1; selection <= 3; selection++)
                {
                    var json = ReadTemplate(content);
                    using var document = JsonDocument.Parse(json);
                    var images = FindImages(document.RootElement).ToArray();
                    Require(images.Length == 1 && images[0].GetProperty("url").GetString() == imageUri,
                        $"{kind} selection {selection}: production view retains its exact local image URI");
                    NativeCardRenderer.RenderedCard rendered;
                    if (releaseRendererBeforeAttach)
                    {
                        using var transientRenderer = new NativeCardRenderer();
                        rendered = transientRenderer.Render(json);
                        rendered.Dispose(); // RunUX retains only the FrameworkElement.
                    }
                    else rendered = renderer.Render(json);
                    using var card = rendered;
                    Require(card.Errors.Count == 0, $"{kind} selection {selection}: native card has no rendering errors");
                    var (image, source) = FindNativeImage(card.Element);
                    Require(images[0].GetProperty("style").GetString() != "Person" || image is Ellipse,
                        $"{kind} selection {selection}: Person styling uses the native ellipse with the original bitmap");
                    Console.WriteLine($"Native image before attach: shape={image.GetType().Name}, rendererReleased={releaseRendererBeforeAttach}, URI={source.UriSource?.AbsoluteUri ?? "<null>"}, pixels={source.PixelWidth}x{source.PixelHeight}");
                    var completed = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    RoutedEventHandler opened = (_, _) => completed.TrySetResult(null);
                    ExceptionRoutedEventHandler failed = (_, error) => completed.TrySetResult(error.ErrorMessage);
                    source.ImageOpened += opened;
                    source.ImageFailed += failed;
                    try
                    {
                        host.Children.Add(card.Element);
                        host.UpdateLayout();
                        if (source.PixelWidth == 0)
                            await Task.WhenAny(completed.Task, Task.Delay(5000));
                        var error = completed.Task.IsCompleted ? await completed.Task : null;
                        Require(error is null, $"{kind} selection {selection}: ImageFailed is not raised ({error})");
                        Require(source.PixelWidth > 0 && source.PixelHeight > 0,
                            $"{kind} selection {selection}: native BitmapImage actually decodes in the offscreen XamlRoot");
                        Require(image.ActualWidth == 80 && image.ActualHeight == 80,
                            $"{kind} selection {selection}: preview image remains allocated at 80px");
                        Console.WriteLine($"Image reselection {kind} #{selection}: decoded={source.PixelWidth}x{source.PixelHeight}, loaded={card.Element.IsLoaded}, valid file={File.Exists(imagePath)}");
                        if (selection == 1 && !releaseRendererBeforeAttach)
                        {
                            await SavePreviewAsync(host, kind);
                            await VerifyOpacityRecoveryAsync(host, card.Element, image, kind);
                        }
                        // Also exercise hosts that cache their rendered tree
                        // instead of asking the renderer for a fresh tree.
                        host.Children.Remove(card.Element);
                        await Task.Delay(100);
                        host.Children.Add(card.Element);
                        host.UpdateLayout();
                        await Task.Delay(100);
                        Require(card.Element.IsLoaded && source.PixelWidth > 0 && source.PixelHeight > 0,
                            $"{kind} selection {selection}: cached tree retains its decoded image after detach/re-attach");
                    }
                    finally
                    {
                        source.ImageOpened -= opened;
                        source.ImageFailed -= failed;
                        host.Children.Remove(card.Element);
                    }
                    await Task.Delay(100);
                }
            }
            finally { NativeMethods.Release(content); }
            Require(NativeObject.LiveCount == baseline, $"{kind}: repeated preview releases its production native references");
        }
        Console.WriteLine($"Image reselection diagnostic PASS: rendererReleased={releaseRendererBeforeAttach}; actual BitmapImage decode succeeds across repeated loaded/unloaded trees. Search host behavior remains a separate check.");
    }

    private static async Task VerifyOpacityRecoveryAsync(Grid host, FrameworkElement card, FrameworkElement image, PaletteResultKind kind)
    {
        host.UpdateLayout();
        await Task.Delay(100);
        var baseline = await CaptureImageDiversityAsync(host, image);
        Console.WriteLine($"Opacity {kind}: baseline image distinct colors={baseline}");
        Require(baseline > 25, $"{kind}: RenderTargetBitmap captures a nonempty photographic image");
        // Give XAML a frame after its documented one-second off-tree resource
        // release interval, then reproduce RunUX's parent opacity fade.
        host.Children.Remove(card);
        await Task.Delay(1700);
        host.Opacity = 0;
        host.Children.Add(card);
        host.UpdateLayout();
        await Task.Delay(200);
        var animation = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(200)) };
        Storyboard.SetTarget(animation, host);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        try
        {
            storyboard.Begin();
            await Task.Delay(400);
            var restored = await CaptureImageDiversityAsync(host, image);
            Console.WriteLine($"Opacity {kind}: after off-tree1700ms +0→1fade image distinct colors={restored}, baseline={baseline}");
            Require(restored >= Math.Max(25, baseline / 4), $"{kind}: image pixels remain visible after unload and parent-opacity fade");
        }
        finally { storyboard.Stop(); host.Opacity = 1; }
    }

    private static async Task<int> CaptureImageDiversityAsync(FrameworkElement root, FrameworkElement image)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(root);
        var buffer = await bitmap.GetPixelsAsync();
        var pixels = buffer.ToArray();
        var bounds = image.TransformToVisual(root).TransformBounds(new Rect(0, 0, image.ActualWidth, image.ActualHeight));
        var scaleX = bitmap.PixelWidth / root.ActualWidth;
        var scaleY = bitmap.PixelHeight / root.ActualHeight;
        var left = Math.Clamp((int)((bounds.Left + bounds.Width * .2) * scaleX), 0, bitmap.PixelWidth - 1);
        var right = Math.Clamp((int)((bounds.Right - bounds.Width * .2) * scaleX), left + 1, bitmap.PixelWidth);
        var top = Math.Clamp((int)((bounds.Top + bounds.Height * .2) * scaleY), 0, bitmap.PixelHeight - 1);
        var bottom = Math.Clamp((int)((bounds.Bottom - bounds.Height * .2) * scaleY), top + 1, bitmap.PixelHeight);
        var colors = new HashSet<int>();
        for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
        {
            var i = (y * bitmap.PixelWidth + x) * 4;
            if (pixels[i + 3] > 0) colors.Add((pixels[i] << 16) | (pixels[i + 1] << 8) | pixels[i + 2]);
        }
        return colors.Count;
    }

    private static async Task SavePreviewAsync(FrameworkElement root, PaletteResultKind kind)
    {
        // Export the rendered control tree for layout review; the downloaded
        // profile/cover file itself is never encoded, rewritten or substituted.
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(root);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        var directory = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "obj");
        Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, $"preview-{kind.ToString().ToLowerInvariant()}.png");
        using var output = File.Open(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        using var stream = output.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
        Console.WriteLine($"Rendered preview exported: {path}");
    }

    private static async Task RunConstrainedSelectionsAsync(NativeCardRenderer renderer, Grid host, string imageUri)
    {
        var baseline = NativeObject.LiveCount;
        var previews = new List<(PaletteResultKind Kind, nint Content, NativeCardRenderer.RenderedCard Card)>();
        try
        {
            foreach (var kind in new[] { PaletteResultKind.Music, PaletteResultKind.Client })
            {
                var state = new FixtureState(new() { Kind = kind, PublicId = Guid.NewGuid(), Title = "Height transition", FirstName = "Height", LastName = "fixture" }, imageUri);
                var content = PalettePreviewData.BuildPreview(state);
                try { previews.Add((kind, content, renderer.Render(ReadTemplate(content)))); }
                catch { NativeMethods.Release(content); throw; }
            }
            host.VerticalAlignment = VerticalAlignment.Top;
            for (var selection = 1; selection <= 3; selection++)
            foreach (var (kind, _, card) in previews)
            {
                var (image, source) = FindNativeImage(card.Element);
                host.Height = 0;
                host.Children.Add(card.Element);
                try
                {
                    foreach (var height in new[] { 0d, 32d, 64d, 100d, 150d, 300d })
                    {
                        host.Height = height;
                        host.UpdateLayout();
                        await Task.Delay(35);
                        var bounds = image.TransformToVisual(host).TransformBounds(new Rect(0, 0, image.ActualWidth, image.ActualHeight));
                        Console.WriteLine($"Height transition {kind} #{selection}: height={height}, image={bounds}, decoded={source.PixelWidth}x{source.PixelHeight}");
                    }
                    var deadline = Environment.TickCount64 + 3000;
                    while (source.PixelWidth == 0 && Environment.TickCount64 < deadline) await Task.Delay(30);
                    var finalBounds = image.TransformToVisual(host).TransformBounds(new Rect(0, 0, image.ActualWidth, image.ActualHeight));
                    Require(source.PixelWidth > 0 && source.PixelHeight > 0, $"{kind} selection {selection}: constrained/cached image is decoded");
                    Require(image.Visibility == Visibility.Visible && image.Opacity > 0 && finalBounds.Width == 80 && finalBounds.Height == 80 &&
                        finalBounds.Left >= -1 && finalBounds.Top >= -1 && finalBounds.Right <= host.ActualWidth + 1 && finalBounds.Bottom <= 301,
                        $"{kind} selection {selection}: image returns in bounds after 0/32/64/100/150→300 height transition");
                }
                finally { host.Children.Remove(card.Element); }
                await Task.Delay(35);
            }
        }
        finally
        {
            host.Children.Clear();
            host.Height = double.NaN;
            host.VerticalAlignment = VerticalAlignment.Stretch;
            foreach (var (_, content, card) in previews) { card.Dispose(); NativeMethods.Release(content); }
        }
        Require(NativeObject.LiveCount == baseline, "alternating constrained previews release their native references");
    }

    private static unsafe string ReadTemplate(nint content)
    {
        nint view = 0, template = 0, value = 0;
        try
        {
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(content)[7])(content, &view));
            template = NativeMethods.As(view, PalettePreviewData.TemplateViewId);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(template)[6])(template, &value));
            return NativeMethods.ReadString(value);
        }
        finally { NativeMethods.WindowsDeleteString(value); NativeMethods.Release(template); NativeMethods.Release(view); }
    }

    private static (FrameworkElement Element, BitmapImage Source) FindNativeImage(FrameworkElement root)
    {
        // Adaptive Cards renders Image.style=Person as an Ellipse whose Fill is
        // an ImageBrush. Observe that exact bitmap, without creating a substitute
        // image or changing the renderer's lifetime, decoding or clipping behavior.
        List<(FrameworkElement Element, BitmapImage Source)> images = [];
        foreach (var node in Descendants(root))
        {
            switch (node)
            {
                case Image { Source: BitmapImage bitmap } image:
                    images.Add((image, bitmap));
                    break;
                case Ellipse { Fill: ImageBrush { ImageSource: BitmapImage bitmap } } ellipse:
                    images.Add((ellipse, bitmap));
                    break;
            }
        }
        Require(images.Count == 1, "native preview retains exactly one photographic bitmap in an Image or Ellipse");
        return images[0];
    }

    private static IEnumerable<JsonElement> FindImages(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("type", out var type) && type.GetString() == "Image") yield return element;
            foreach (var property in element.EnumerateObject())
                foreach (var image in FindImages(property.Value)) yield return image;
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray())
                foreach (var image in FindImages(child)) yield return image;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }

    private sealed class FixtureState(PaletteResultData result, string imageUri) : IPaletteResultState
    {
        public PaletteResultData Result => result;
        public PaletteResultSnapshot GetSnapshot() => new(imageUri, false, null, null, Result.Kind == PaletteResultKind.Music);
        public event Action? Changed { add { } remove { } }
        public void Invoke(string action) => throw new InvalidOperationException("Rendering must not execute an action.");
    }
}
