using Microsoft.Extensions.Caching.Memory;
using Mizon.API;
using Mizon.API.Client.Operations.Media;
using Mizon.API.Client.Operations.Musics;
using Mizon.API.Client.Operations.Search;
using Mizon.API.Client.Operations.Clients;
using Mizon.API.Client.Responses.Media;
using Mizon.API.Client.Responses.Search;
using Mizon.Windows.SearchIntegration;
using System.Net;
using System.Runtime.CompilerServices;

namespace Mizon.Windows.PaletteProvider;

internal sealed class PaletteApiSession : IDisposable
{
    // Connections live for the provider process; bearer headers remain local to
    // each authenticated session and are never sent through the storage pool.
    private static readonly SocketsHttpHandler ApiTransport = new()
    {
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    };
    private static readonly HttpMessageInvoker ApiInvoker = new(ApiTransport);
    private static readonly SocketsHttpHandler StorageTransport = new()
    {
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    };
    private static readonly SemaphoreSlim ImageSlots = new(4, 4);
    private readonly HttpClient _http;
    private readonly HttpClient _storage;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = null });
    private readonly MizonApi _api;
    private readonly MediaDownloader _downloader;
    internal static string CacheDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "MizonCache");

    internal PaletteApiSession(PaletteAuthenticationSnapshot authentication, string? cacheDirectory = null)
    {
        var handler = new ApiAddressHandler(new Uri(authentication.ApiBaseAddress));
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _storage = new HttpClient(StorageTransport, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(5) };
        _api = new MizonApi(_http, _cache);
        _api.SetToken(authentication.AccessToken);
        _downloader = new MediaDownloader(_storage, cacheDirectory ?? CacheDirectory);
    }

    internal static PaletteAuthenticationSnapshot RequireAuthentication()
    {
        var snapshot = PaletteAuthenticationStore.Read();
        if (snapshot is null || snapshot.ExpiresAtUtc is { } expiry && expiry <= DateTimeOffset.UtcNow)
            throw new PaletteLoginRequiredException();
        return snapshot;
    }
    internal async IAsyncEnumerable<GlobalSearchItemResponse> SearchAsync(string query, [EnumeratorCancellation] CancellationToken token)
    {
        await foreach (var item in _api.SendStreamRequestAsync(new GlobalSearchOperation(new() { Query = query }), token).ConfigureAwait(false))
            if (item.PublicId != Guid.Empty && item.ItemType is "Music" or "Client") yield return item;
    }
    internal async Task<string> ImageAsync(Guid id, CancellationToken token)
    {
        await ImageSlots.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var media = await MediaAsync(id, token).ConfigureAwait(false);
            if (media.FileSize > 20 * 1024 * 1024) throw new InvalidDataException("Search image exceeds the size limit.");
            var path = await _downloader.DownloadToCacheAsync(media, StorageUri(media.StoragePath), cancellationToken: token).ConfigureAwait(false);
            return new Uri(path).AbsoluteUri;
        }
        finally { ImageSlots.Release(); }
    }
    internal async Task<string> DownloadMusicAsync(Guid id, IProgress<MediaDownloader.MediaDownloadProgress> progress, CancellationToken token)
    {
        var response = await _api.SendRequestAsync(new MusicFileOperation(new() { MusicPublicId = id }), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (response.Error?.Code == 401) throw new PaletteLoginRequiredException();
        if (IsMissingMusicFile(response.Error?.Code) || response.IsSuccess && (response.Content is null || response.Content.MediaPublicId == Guid.Empty))
            throw new FileNotFoundException("No playable media is available for this music.");
        if (!response.IsSuccess || response.Content is null) throw new IOException("Music file metadata is temporarily unavailable.");
        // Follow the same metadata/download/cache path as the main application.
        var media = await MediaAsync(response.Content.MediaPublicId, token).ConfigureAwait(false);
        media.Category = "music"; // MusicPage constructs its audio cache metadata with this category.
        return await _downloader.DownloadToCacheAsync(media, StorageUri(media.StoragePath), progress: progress, cancellationToken: token).ConfigureAwait(false);
    }
    internal async Task<(PaletteResultData Result, Guid? MediaId, bool CanDownload)> DetailsAsync(PaletteResultData data, CancellationToken token)
    {
        if (data.Kind == PaletteResultKind.Client)
        {
            var response = await _api.SendRequestAsync(new ClientProfileOperation(new() { ClientPublicId=data.PublicId }),token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (response.Error?.Code == 401) throw new PaletteLoginRequiredException();
            if (!response.IsSuccess || response.Content is not { } profile) throw new IOException("Profile information is unavailable.");
            return (data with { FirstName=profile.FirstName,LastName=profile.LastName,NickName=profile.NickName,Username=profile.Username },profile.ProfileMediaPublicId,false);
        }
        var detailsTask = _api.SendRequestAsync(new MusicOperation(new() { MusicPublicId=data.PublicId }),token).AsTask();
        var fileTask = _api.SendRequestAsync(new MusicFileOperation(new() { MusicPublicId=data.PublicId }),token).AsTask();
        await Task.WhenAll(detailsTask,fileTask).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var details=await detailsTask.ConfigureAwait(false); var file=await fileTask.ConfigureAwait(false);
        if (details.Error?.Code == 401 || file.Error?.Code == 401) throw new PaletteLoginRequiredException();
        if (details.Content is not { } music || !details.IsSuccess) throw new IOException("Music details are unavailable.");
        if (!file.IsSuccess && !IsMissingMusicFile(file.Error?.Code)) throw new IOException("Music file metadata is temporarily unavailable.");
        return (data with
        {
            Title=music.Title, PersianTitle=music.PersianTitle, LikesCount=music.LikesCount, ViewsCount=music.ViewsCount,
            AlbumTitle=music.MusicAlbum?.PersianTitle ?? music.MusicAlbum?.Title,
            GenreText=string.Join(" · ",music.MusicGenres.Select(genre=>genre.PersianTitle ?? genre.Title)),
            ReleaseDate=music.ReleaseDateTime,
            DurationText=file.Content?.Duration.ToString(@"mm\:ss")
        },music.MusicCover?.MediaPublicId,file.IsSuccess && file.Content is { MediaPublicId: var mediaId } && mediaId != Guid.Empty);
    }
    private async Task<MediaResponse> MediaAsync(Guid id, CancellationToken token)
    {
        var response = await _api.SendRequestAsync(new MediaOperation(new() { MediaPublicId = id }), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (response.Error?.Code == 401) throw new PaletteLoginRequiredException();
        if (!response.IsSuccess || response.Content is null) throw new IOException("Media metadata is not available.");
        return response.Content;
    }
    private static Uri StorageUri(string path)
    {
        var uri = new Uri(path, UriKind.Absolute);
        if (uri.IsFile || uri.Scheme is "https" or "http") return uri;
        throw new InvalidDataException("Unsupported media address.");
    }
    // Existing Mizon operation errors are domain codes, not HTTP status codes.
    private static bool IsMissingMusicFile(int? code) => code is 404 or 10000 or 10001 or 10002;
    public void Dispose() { _http.Dispose(); _storage.Dispose(); _cache.Dispose(); }
    internal static void DisposeTransports() { ApiInvoker.Dispose(); StorageTransport.Dispose(); ImageSlots.Dispose(); }

    private sealed class ApiAddressHandler(Uri address) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri is not { } original) throw new InvalidOperationException("Missing API address.");
            request.RequestUri = new Uri(address, original.PathAndQuery.TrimStart('/'));
            return ApiInvoker.SendAsync(request, token);
        }
    }
}

internal sealed class PaletteLoginRequiredException : Exception;
