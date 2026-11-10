using Mizon.API.Client.Operations.Media;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.ViewModels;

public partial class MusicPageViewModel
{
    // Runs on the UI dispatcher, like the existing playback/download activation path.
    // Both paths share the per-media task and the downloader's cross-process cache lease.
    internal async Task<string> DownloadCurrentMusicAsync()
    {
        var musicId = _musicPublicId;
        var loadToken = _musicLoadCancellationTokenSource?.Token ?? CancellationToken.None;
        if (musicId == Guid.Empty) throw new InvalidOperationException("هنوز آهنگی انتخاب نشده است.");
        await _musicLoadTask.WaitAsync(loadToken);
        loadToken.ThrowIfCancellationRequested();
        if (!IsCurrentMusicLoad(musicId, loadToken)) throw new OperationCanceledException(loadToken);
        var media = await GetMusicMediaAsync(loadToken)
            ?? throw new InvalidOperationException("فایل این آهنگ در دسترس نیست.");
        if (!IsCurrentMusicLoad(musicId, loadToken)) throw new OperationCanceledException(loadToken);

        var path = _mediaDownloader.GetCachedFilePath(media);
        if (File.Exists(path)) return path;
        if (_activeMusicDownloads.TryGetValue(media.MediaPublicId, out var existing))
        {
            await existing.WaitAsync(loadToken);
            if (!IsCurrentMusicLoad(musicId, loadToken)) throw new OperationCanceledException(loadToken);
            if (!File.Exists(path)) throw new IOException("فایل دانلودشده پیدا نشد.");
            return path;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _activeMusicDownloads.Add(media.MediaPublicId, completion.Task);
        var finished = false;
        var progress = new Progress<MediaDownloader.MediaDownloadProgress>(value =>
        {
            if (!finished && IsCurrentMusicLoad(musicId, loadToken)) MusicDownloadProgress = value.Percentage;
        });
        IsMusicDownloading = true;
        MusicDownloadProgress = 0;
        try
        {
            path = await _mediaDownloader.DownloadToCacheAsync(media, new Uri(media.StoragePath),
                progress: progress, cancellationToken: loadToken);
            if (!IsCurrentMusicLoad(musicId, loadToken)) throw new OperationCanceledException(loadToken);
            MusicDownloadProgress = 100;
            completion.TrySetResult();
            return path;
        }
        catch (OperationCanceledException)
        {
            completion.TrySetCanceled(loadToken);
            throw;
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
            _ = completion.Task.Exception;
            throw;
        }
        finally
        {
            finished = true;
            _activeMusicDownloads.Remove(media.MediaPublicId);
            if (IsCurrentMusicLoad(musicId, loadToken)) IsMusicDownloading = false;
        }
    }
}
