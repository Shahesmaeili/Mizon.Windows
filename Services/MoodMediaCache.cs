using Mizon.API;
using Mizon.API.Client.Operations.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

// Only paths are retained. Decoded images and composition scenes belong to visible controls.
internal sealed class MoodMediaCache(MizonApi api, MediaDownloader downloader) : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, string> _paths = [];
    private readonly Dictionary<Guid, MediaGate> _mediaDownloads = [];
    private readonly SemaphoreSlim _downloads = new(4, 4);
    private readonly CancellationTokenSource _lifetime = new();
    private int _active;
    private bool _disposed, _released;

    public async ValueTask<string> GetAsync(Guid mediaPublicId, CancellationToken cancellationToken)
    {
        CancellationToken lifetime;
        MediaGate mediaGate;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (_paths.TryGetValue(mediaPublicId, out var cached) && File.Exists(cached)) return cached;
            if (!_mediaDownloads.TryGetValue(mediaPublicId, out mediaGate!))
                _mediaDownloads.Add(mediaPublicId, mediaGate = new MediaGate());
            mediaGate.Users++;
            _active++;
            lifetime = _lifetime.Token;
        }

        var entered = false;
        var mediaEntered = false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var token = timeout.Token;
            // Header playback, its click effect and taskbar IPC can request the
            // same media together. Serialize that media before taking a global
            // download slot; successful followers reuse the completed path.
            await mediaGate.Semaphore.WaitAsync(token).ConfigureAwait(false);
            mediaEntered = true;
            lock (_sync)
                if (_paths.TryGetValue(mediaPublicId, out var cached) && File.Exists(cached)) return cached;
            await _downloads.WaitAsync(token).ConfigureAwait(false);
            entered = true;

            var response = await api.SendRequestAsync(new MediaOperation(new() { MediaPublicId = mediaPublicId }), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!response.IsSuccess || response.Content is not { } media ||
                !Uri.TryCreate(media.StoragePath, UriKind.Absolute, out var uri))
                throw new IOException("Mood media is unavailable.");
            var path = await downloader.DownloadToCacheAsync(media, uri, cancellationToken: token).ConfigureAwait(false);
            if (string.Equals(Path.GetExtension(path), ".tgs", StringComparison.OrdinalIgnoreCase))
                path = await ExpandAnimationAsync(path, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            lock (_sync)
            {
                // A full picker contains about 580 tiny preview paths; never flush
                // the entire cache while the user scrolls through the catalog.
                if (_paths.Count >= 1024)
                {
                    using var entries = _paths.GetEnumerator();
                    if (entries.MoveNext()) _paths.Remove(entries.Current.Key);
                }
                if (!_disposed) _paths[mediaPublicId] = path;
            }
            return path;
        }
        finally
        {
            if (entered) _downloads.Release();
            if (mediaEntered) mediaGate.Semaphore.Release();
            lock (_sync)
            {
                // Every waiting caller owns a lease. Cancelling one consumer
                // cannot dispose the semaphore while another still uses it.
                if (--mediaGate.Users == 0)
                {
                    _mediaDownloads.Remove(mediaPublicId);
                    mediaGate.Semaphore.Dispose();
                }
                _active--;
                ReleaseWhenIdle();
            }
        }
    }

    private static async Task<string> ExpandAnimationAsync(string path, CancellationToken token)
    {
        var target = path + ".json";
        if (File.Exists(target)) return target;
        return await Task.Run(async () =>
        {
            // The original TGS remains in the shared, hash-keyed download cache.
            // Only this selected animation is expanded, off the XAML thread.
            const int maximumJsonBytes = 8 * 1024 * 1024;
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16384, FileOptions.Asynchronous | FileOptions.SequentialScan))
                using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16384, FileOptions.Asynchronous))
                {
                    var buffer = new byte[16384];
                    var total = 0;
                    int read;
                    while ((read = await gzip.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                    {
                        total += read;
                        if (total > maximumJsonBytes) throw new InvalidDataException("Mood animation exceeds the decoded size limit.");
                        await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    }
                    if (total == 0) throw new InvalidDataException("Mood animation is empty.");
                    await output.FlushAsync(token).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
                try { File.Move(temporary, target, overwrite: false); }
                catch (IOException) when (File.Exists(target)) { /* Another visible consumer completed the same file. */ }
                return target;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }, token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            _paths.Clear();
            ReleaseWhenIdle();
        }
    }

    private void ReleaseWhenIdle()
    {
        if (!_disposed || _active != 0 || _released) return;
        _released = true;
        _downloads.Dispose();
        _lifetime.Dispose();
    }

    private sealed class MediaGate
    {
        internal readonly SemaphoreSlim Semaphore = new(1, 1);
        internal int Users;
    }
}
