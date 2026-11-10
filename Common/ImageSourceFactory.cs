using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Graphics.Imaging;

namespace Mizon.Windows.Common;

internal static class ImageSourceFactory
{
    // The navigation path must not decode/resize a large original on the UI thread.
    // Only copying the final, display-sized pixels resumes on the UI dispatcher.
    public static async Task<ImageSource?> LoadNavigationCoverAsync(string path, int pixelWidth, CancellationToken token)
    {
        var decoded = await Task.Run(async () =>
        {
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask(token).ConfigureAwait(false);
            using var stream = await file.OpenReadAsync().AsTask(token).ConfigureAwait(false);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token).ConfigureAwait(false);
            var ratio = Math.Min(1d, (double)pixelWidth / Math.Max(1u, decoder.OrientedPixelWidth));
            var transform = new BitmapTransform
            {
                ScaledWidth = Math.Max(1u, (uint)Math.Round(decoder.PixelWidth * ratio)),
                ScaledHeight = Math.Max(1u, (uint)Math.Round(decoder.PixelHeight * ratio)),
                InterpolationMode = BitmapInterpolationMode.Fant
            };
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb)
                .AsTask(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var pixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)];
            bitmap.CopyToBuffer(pixels.AsBuffer());
            return (Pixels: pixels, Width: bitmap.PixelWidth, Height: bitmap.PixelHeight);
        }, token);
        token.ThrowIfCancellationRequested();

        // SoftwareBitmapSource can read its SoftwareBitmap again after SetBitmapAsync
        // completes (for example when reattaching a cached cover). Disposing that
        // bitmap caused a native RO_E_CLOSED fail-fast in WinUI's image worker.
        // Give XAML its own pixel storage instead; no disposable decoder resource
        // escapes the worker, and no late native upload can access a closed bitmap.
        var source = new WriteableBitmap(decoded.Width, decoded.Height);
        using (var pixels = source.PixelBuffer.AsStream())
            pixels.Write(decoded.Pixels);
        source.Invalidate();
        return source;
    }

    // Cached files can be outside the package's URI-accessible folders. Open the
    // file explicitly and finish decoding before handing it to a connected animation.
    public static async ValueTask<BitmapImage> LoadFileAsync(string path, int decodePixelWidth = 256,
        CancellationToken cancellationToken = default)
    {
        var file = await StorageFile.GetFileFromPathAsync(path).AsTask(cancellationToken);
        using var stream = await file.OpenReadAsync().AsTask(cancellationToken);
        var image = new BitmapImage { DecodePixelWidth = decodePixelWidth };
        await image.SetSourceAsync(stream).AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return image;
    }

    public static ImageSource? Create(string? path, int decodePixelWidth = 256)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var source = path.StartsWith("/Assets/", StringComparison.OrdinalIgnoreCase)
            ? "ms-appx:///" + path.TrimStart('/')
            : path;

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme is not ("file" or "https" or "http" or "ms-appx" or "ms-appdata"))
            return null;

        return new BitmapImage { DecodePixelWidth = decodePixelWidth, UriSource = uri };
    }
}
