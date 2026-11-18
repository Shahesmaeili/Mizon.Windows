using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Mizon.Windows.UserControls;

public sealed partial class DiscoveryFeatureHero
{
    private static readonly TimeSpan MediaTransitionDuration = TimeSpan.FromMilliseconds(430);
    private readonly HeroMediaReveal _videoReveal;
    private HeroSpectrumBorder? _spectrum;
    private BitmapImage? _videoTransitionFrame;
    private HeroMediaPalette? _videoPalette;
    private string? _sampledVideoPath;
    private Uri? _artworkSource;
    private int _mediaDirection = 1;
    private bool _videoFallback;

    private bool AnimateMedia => CanPresent && _settings.AnimationsEnabled && !HeroVideo.IsFullWindow;

    internal FrameworkElement? SpectrumPointerRoot { get; set; }

    internal void RefreshSpectrumProximity() => _spectrum?.ViewportChanged();

    private void UpdateArtwork(MusicFeatureItemViewModel item)
    {
        if (item.IsVideo && HeroArtwork.HasArtwork && !_videoFallback) return;
        if (!Uri.TryCreate(item.ImagePath, UriKind.Absolute, out var source)) return;
        _artworkSource = source;
        HeroArtwork.TransitionDirection = _mediaDirection;
        HeroArtwork.TransitionDuration = AnimateMedia ? MediaTransitionDuration : TimeSpan.Zero;
        HeroArtwork.RequestSource(source);
    }

    private void Artwork_Presented(object? sender, EventArgs e)
    {
        if (_artworkSource is not { } source || !HeroArtwork.IsDisplaying(source)) return;
        if (_videoPalette is null) _spectrum?.SetPalette(HeroArtwork.Palette);
    }

    private void UpdateVideoStage(bool ready)
    {
        if (!ready)
        {
            HideVideo();
            return;
        }
        if (HeroVideoStage.Visibility == Visibility.Visible) return;
        HeroVideoStage.Visibility = Visibility.Visible;
        if (AnimateMedia && HeroArtwork.HasArtwork) _videoReveal.Start(_mediaDirection, MediaTransitionDuration, HeroRoot);
    }

    private void HideVideo()
    {
        if (HeroVideoStage.Visibility == Visibility.Collapsed) return;
        HeroVideoStage.Visibility = Visibility.Collapsed;
        _videoReveal.Finish();
    }

    private void UpdateMediaMotion()
    {
        var animate = AnimateMedia;
        _spectrum?.SetState(CanPresent && !HeroVideo.IsFullWindow, animate);
        if (!animate)
        {
            _videoReveal.Finish();
            HeroArtwork.FinishTransition();
        }
        HeroArtwork.TransitionDuration = animate ? MediaTransitionDuration : TimeSpan.Zero;
        if (!CanPresent || _selected?.IsVideo != true || _selectionCancellation is not { } selection
            || _media is not { IsReady: true, CachedPath: { Length: > 0 } path } || path == _sampledVideoPath) return;
        _sampledVideoPath = path;
        _ = ReadVideoFrameAsync(path, _selectionVersion, selection.Token);
    }

    private async Task ReadVideoFrameAsync(string path, int version, CancellationToken token)
    {
        try
        {
            // Sample one frame of the completed local video. The shell can reuse its
            // cached thumbnail, and the main playback session remains the only player.
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask(token);
            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.VideosView, 640).AsTask(token);
            if (!IsCurrentVideo(version, token)) return;
            if (thumbnail?.Type != ThumbnailType.Image)
            {
                UseVideoPoster();
                return;
            }
            var palette = await Task.Run(async () =>
            {
                using var stream = thumbnail.CloneStream();
                var device = CanvasDevice.GetSharedDevice();
                using var bitmap = await CanvasBitmap.LoadAsync(device, stream).AsTask(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return HeroMediaPalette.Sample(device, bitmap);
            }, token);
            if (!IsCurrentVideo(version, token)) return;
            thumbnail.Seek(0);
            var frame = new BitmapImage();
            await frame.SetSourceAsync(thumbnail).AsTask(token);
            if (!IsCurrentVideo(version, token)) return;
            _videoTransitionFrame = frame;
            _videoPalette = palette;
            if (palette is { } colors) _spectrum?.SetPalette(colors);
            else UseVideoPoster();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (IsCurrentVideo(version, token))
            {
                AppLog.Error("Featured video frame could not be sampled.", exception);
                UseVideoPoster();
            }
        }
    }

    private void UseVideoPoster()
    {
        if (_selected is not { } item) return;
        _videoFallback = true;
        UpdateArtwork(item);
        _spectrum?.SetPalette(_artworkSource is { } source && HeroArtwork.IsDisplaying(source) ? HeroArtwork.Palette : null);
    }

    private bool IsCurrentVideo(int version, CancellationToken token) =>
        _loaded && version == _selectionVersion && !token.IsCancellationRequested && _media?.IsReady == true;
}
