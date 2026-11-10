using Mizon.API.Client.Operations.Media;
using Mizon.API.Client.Responses.Search;
using Mizon.Windows.PaletteProvider;
using Mizon.Windows.SearchIntegration;
using System.Security.Cryptography;

internal static class LiveApiIntegrationTests
{
    private static int _passes;

    // Explicit opt-in integration run. Reads the real encrypted account bridge
    // and may populate the production media cache; never logs account tokens.
    internal static async Task<int> RunAsync()
    {
        _passes = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var authentication = PaletteApiSession.RequireAuthentication();
        Check(authentication.Revision != Guid.Empty, "current account authentication bridge is available");
        var originalBridge = File.GetLastWriteTimeUtc(PaletteAuthenticationStore.FilePath);
        using var api = new PaletteApiSession(authentication);
        var results = new List<GlobalSearchItemResponse>();
        await foreach (var item in api.SearchAsync("st", timeout.Token)) results.Add(item);
        var musics = results.Where(item => item.ItemType == "Music").ToArray();
        var clients = results.Where(item => item.ItemType == "Client").ToArray();
        Console.WriteLine($"Live search 'st': music={musics.Length}, client={clients.Length}.");
        Check(musics.Length > 0, "live streamed search returns real music records");
        Check(clients.Length > 0, "live streamed search returns real client records");
        Check(results.Select(item => (item.ItemType, item.PublicId)).Distinct().Count() == results.Count,
            "live results have distinct type/public-id identities");

        var client = clients.FirstOrDefault(item => item.ProfileMediaPublicId is not null) ?? clients[0];
        var (profile, profileMedia, canDownloadProfile) = await api.DetailsAsync(new()
        { Kind = PaletteResultKind.Client, PublicId = client.PublicId }, timeout.Token);
        Check(!string.IsNullOrWhiteSpace(profile.Username) || !string.IsNullOrWhiteSpace(profile.FirstName) || !string.IsNullOrWhiteSpace(profile.NickName),
            "real ClientProfile endpoint enriches a bare client id");
        Check(profile.PublicId == client.PublicId && !canDownloadProfile, "profile identity remains stable and has no music download action");
        Check(profile.DetailsUri == $"mizon://client/{client.PublicId:D}", "profile activation URI contains the real client id");
        if (client.ProfileMediaPublicId is { } expectedProfile)
        {
            Check(profileMedia == expectedProfile, "profile details and search agree on profile media id");
            await CheckImageAsync(api, expectedProfile, "profile", timeout.Token);
        }
        else Console.WriteLine("INFO selected client has no profile media; no fake image was substituted.");

        Guid? downloadId = null;
        string? downloadedPath = null;
        List<string> unavailable = [];
        // Some seeded search examples deliberately have no audio. Discover an
        // additional existing song through the API rather than inventing a file.
        var candidates = musics.ToList();
        await foreach (var candidate in api.SearchAsync("ar", timeout.Token))
            if (candidate.ItemType == "Music" && candidates.All(item => item.PublicId != candidate.PublicId))
                candidates.Add(candidate);
        foreach (var music in candidates.AsEnumerable().Reverse())
        {
            var (details, coverMedia, canDownload) = await api.DetailsAsync(new()
            { Kind = PaletteResultKind.Music, PublicId = music.PublicId }, timeout.Token);
            Check(!string.IsNullOrWhiteSpace(details.Title) || !string.IsNullOrWhiteSpace(details.PersianTitle),
                "real music details enrich a bare music id");
            Check(details.LikesCount is >= 0 && details.ViewsCount is >= 0, "music counts are returned by backend detail API");
            Check(details.DefaultActionUri == $"mizon://music/{music.PublicId:D}?action=play", "music play URI preserves its real public id");
            if (coverMedia is { } cover) await CheckImageAsync(api, cover, "cover", timeout.Token);
            if (!canDownload) { unavailable.Add(music.PublicId + ": no playable file metadata"); continue; }

            var progress = new DownloadProgress();
            try
            {
                downloadedPath = await api.DownloadMusicAsync(music.PublicId, progress, timeout.Token);
                Check(File.Exists(downloadedPath) && new FileInfo(downloadedPath).Length > 0,
                    "real music download resolves to a non-empty local cached file");
                Check(IsInCache(downloadedPath), "download uses the same Mizon cache as the Windows app");
                Check(progress.Last is { } final && final.Percentage == 100 && final.TotalDownloaded > 0,
                    "real downloader reports completed byte progress");
                downloadId = music.PublicId;
                Console.WriteLine($"PLAYABLE MUSIC {music.PublicId:D}: {details.DisplayTitle}; bytes={new FileInfo(downloadedPath).Length}.");
                break;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                unavailable.Add(music.PublicId + ": " + error.GetType().Name);
            }
        }
        foreach (var failure in unavailable) Console.WriteLine("MEDIA DIAGNOSTIC " + failure);
        Check(downloadId is not null && downloadedPath is not null, "at least one returned real song has a playable downloadable file");

        var file = downloadedPath!;
        var length = new FileInfo(file).Length;
        var modified = File.GetLastWriteTimeUtc(file);
        var digest = await HashAsync(file, timeout.Token);
        var secondProgress = new DownloadProgress();
        var cached = await api.DownloadMusicAsync(downloadId!.Value, secondProgress, timeout.Token);
        Check(string.Equals(file, cached, StringComparison.OrdinalIgnoreCase), "second download returns the existing cache path");
        var cachedDigest = await HashAsync(cached, timeout.Token);
        Check(new FileInfo(cached).Length == length && File.GetLastWriteTimeUtc(cached) == modified &&
            digest.AsSpan().SequenceEqual(cachedDigest), "cache reuse preserves music bytes and modification time");
        Check(secondProgress.Count == 1 && secondProgress.Last is { InstantaneousSpeedBytesPerSecond: 0 },
            "cache hit uses the downloader's cache branch without transfer chunks");

        await CheckFreshTransferAsync(authentication, downloadId.Value, digest, length, timeout.Token);

        using (var cancellation = new CancellationTokenSource())
        {
            var sawItem = false;
            try
            {
                await foreach (var item in api.SearchAsync("st", cancellation.Token))
                {
                    sawItem = true;
                    cancellation.Cancel();
                }
                throw new InvalidOperationException("Search cancellation did not interrupt enumeration.");
            }
            catch (OperationCanceledException) { Check(sawItem, "canceling after a streamed result stops the live query"); }
        }
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try
            {
                await api.DownloadMusicAsync(downloadId.Value, new DownloadProgress(), cancellation.Token);
                throw new InvalidOperationException("Canceled download was accepted.");
            }
            catch (OperationCanceledException) { Check(true, "canceled music download propagates cancellation even with a cached file"); }
        }
        Check(File.GetLastWriteTimeUtc(PaletteAuthenticationStore.FilePath) == originalBridge,
            "integration run does not rewrite the user's authentication bridge");
        Console.WriteLine($"Live API PASS {_passes} assertions; actual Search UI rendering is tested separately.");
        return 0;
    }

    private static async Task CheckImageAsync(PaletteApiSession api, Guid media, string label, CancellationToken token)
    {
        var image = new Uri(await api.ImageAsync(media, token));
        Check(image.IsFile && File.Exists(image.LocalPath) && new FileInfo(image.LocalPath).Length > 0,
            $"real {label} media resolves to a downloaded local image");
        Check(IsInCache(image.LocalPath), $"{label} image uses the original shared production cache file");
        var modified = File.GetLastWriteTimeUtc(image.LocalPath);
        var repeated = await api.ImageAsync(media, token);
        Check(repeated == image.AbsoluteUri && File.GetLastWriteTimeUtc(image.LocalPath) == modified,
            $"{label} selection reuses the original image without rewriting it");
    }

    private static async Task CheckFreshTransferAsync(PaletteAuthenticationSnapshot authentication, Guid musicId,
        byte[] expectedDigest, long expectedLength, CancellationToken token)
    {
        var temporary = Directory.CreateTempSubdirectory("Mizon.Palette.LiveApi.");
        var temporaryPath = Path.GetFullPath(temporary.FullName);
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!temporaryPath.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) ||
            !temporary.Name.StartsWith("Mizon.Palette.LiveApi.", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected integration cache location.");

        try
        {
            using var isolatedApi = new PaletteApiSession(authentication, temporaryPath);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var transfer = new DownloadProgress(progress =>
            {
                if (progress.Percentage is >= 30 and < 100) cancellation.Cancel();
            });
            var canceled = false;
            try { await isolatedApi.DownloadMusicAsync(musicId, transfer, cancellation.Token); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested && !token.IsCancellationRequested)
            { canceled = true; }
            Check(canceled, "fresh real transfer cancels from its byte-progress callback around thirty percent");
            Check(transfer.Count > 1 && transfer.Last is { Percentage: >= 30 and < 100 },
                "fresh real transfer reports multiple chunks before cancellation");

            var partials = Directory.GetFiles(temporaryPath, "*.part", SearchOption.AllDirectories);
            Check(partials.Length == 1, "canceled transfer retains exactly one resumable partial in the isolated cache");
            var partialLength = new FileInfo(partials[0]).Length;
            Check(partialLength > 0 && partialLength < expectedLength && transfer.Last!.Value.TotalDownloaded == partialLength,
                "partial file length matches the reported canceled position");
            Check(!File.Exists(partials[0][..^5]), "cancellation does not publish an incomplete final file");

            var resumedProgress = new DownloadProgress();
            var resumed = await isolatedApi.DownloadMusicAsync(musicId, resumedProgress, token);
            Check(resumedProgress.First is { } first && first.TotalDownloaded > partialLength,
                "resume continues from retained bytes instead of restarting the download");
            Check(resumedProgress.Count > 1 && resumedProgress.Last is { Percentage: 100 },
                "resumed real transfer reports incremental progress through completion");
            Check(File.Exists(resumed) && new FileInfo(resumed).Length == expectedLength &&
                !File.Exists(partials[0]), "resume atomically replaces the partial with the complete music file");
            var resumedDigest = await HashAsync(resumed, token);
            Check(expectedDigest.AsSpan().SequenceEqual(resumedDigest),
                "fresh canceled-and-resumed audio exactly matches the existing production cache by SHA256");
            Console.WriteLine($"FRESH TRANSFER chunks-before-cancel={transfer.Count}, canceled-bytes={partialLength}, resumed-chunks={resumedProgress.Count}, total-bytes={expectedLength}.");
        }
        finally
        {
            // Only this run's newly created directory is removed. The real app
            // cache and source media are never renamed, deleted or overwritten.
            if (Directory.Exists(temporaryPath))
            {
                if ((File.GetAttributes(temporaryPath) & FileAttributes.ReparsePoint) != 0 ||
                    Directory.EnumerateFileSystemEntries(temporaryPath, "*", SearchOption.AllDirectories)
                        .Any(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))
                    throw new IOException("Refusing to clean an integration cache containing a reparse point.");
                Directory.Delete(temporaryPath, recursive: true);
            }
        }
        Check(!Directory.Exists(temporaryPath), "isolated transfer cache is disposed and removed after verification");
    }

    private static bool IsInCache(string path) => Path.GetFullPath(path).StartsWith(
        Path.GetFullPath(PaletteApiSession.CacheDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
        StringComparison.OrdinalIgnoreCase);

    private static async Task<byte[]> HashAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        return await SHA256.HashDataAsync(stream, token);
    }

    private sealed class DownloadProgress(Action<MediaDownloader.MediaDownloadProgress>? onProgress = null) : IProgress<MediaDownloader.MediaDownloadProgress>
    {
        internal int Count;
        internal MediaDownloader.MediaDownloadProgress? First;
        internal MediaDownloader.MediaDownloadProgress? Last;
        public void Report(MediaDownloader.MediaDownloadProgress progress)
        {
            First ??= progress;
            Count++;
            Last = progress;
            onProgress?.Invoke(progress);
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        _passes++; Console.WriteLine("PASS " + description);
    }
}
