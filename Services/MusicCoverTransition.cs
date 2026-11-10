using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Mizon.Windows.Common;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Services;

// A one-use navigation payload. Never retain the source page in the back stack.
internal sealed class MusicCoverTransition : IDisposable
{
    internal const string ParameterKey = "MusicCoverTransition";
    internal const string ReturnMusicKey = "MusicCoverReturnId";
    internal ConnectedAnimation Animation { get; }
    internal ImageSource? Preview { get; private set; }
    private Task<ImageSource?>? _preparedPreview;

    private MusicCoverTransition(ConnectedAnimation animation, ImageSource preview, Task<ImageSource?>? preparedPreview)
    {
        Animation = animation;
        Preview = preview;
        _preparedPreview = preparedPreview;
    }

    internal static MusicCoverTransition? Prepare(Image? source, Task<ImageSource?>? preparedPreview = null)
    {
        if (source is not { IsLoaded: true, Source: { } preview } || source.ActualWidth <= 0 || source.ActualHeight <= 0
            || !new UISettings().AnimationsEnabled) return null;
        if (preview is BitmapSource bitmap && bitmap.PixelWidth <= 0) return null;
        try
        {
            var animation = ConnectedAnimationService.GetForCurrentView()
                .PrepareToAnimate($"MusicCover-{Guid.NewGuid():N}", source);
            // Use WinUI's default gravity/duration; Direct forces a short 150 ms trip.
            return new(animation, preview, preparedPreview);
        }
        catch (Exception error)
        {
            AppLog.Error("Music cover transition could not be prepared.", error);
            return null;
        }
    }

    internal async ValueTask<ImageSource?> TakePreviewAsync(int decodePixelWidth, CancellationToken cancellationToken)
    {
        var preview = Preview;
        Preview = null;
        var prepared = _preparedPreview;
        _preparedPreview = null;
        if (prepared is not null)
        {
            var image = await prepared.WaitAsync(cancellationToken);
            if (image is not null) return image;
        }
        if (preview is BitmapImage { UriSource: { IsFile: true } uri })
        {
            // The list bitmap is intentionally a thumbnail. Decode only the selected
            // cached file at the destination's physical size before animating it.
            return await ImageSourceFactory.LoadNavigationCoverAsync(uri.LocalPath, decodePixelWidth, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (preview is BitmapSource bitmap && bitmap.PixelWidth < decodePixelWidth)
            return null;
        return preview;
    }

    public void Dispose()
    {
        Animation.Cancel();
        Preview = null;
        _preparedPreview = null;
    }
}
