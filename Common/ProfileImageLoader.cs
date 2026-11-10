using Microsoft.UI.Xaml.Media;
using Mizon.API;
using Mizon.API.Client.Operations.Media;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Common;

internal static class ProfileImageLoader
{
    internal sealed record Result(string Path, ImageSource Image);

    public static async ValueTask<Result> LoadAsync(MizonApi api, MediaDownloader downloader,
        Guid mediaPublicId, CancellationToken cancellationToken)
    {
        var response = await api.SendRequestAsync(new MediaOperation(new() { MediaPublicId = mediaPublicId }), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!response.IsSuccess || response.Content is not { } media ||
            !Uri.TryCreate(media.StoragePath, UriKind.Absolute, out var uri))
            throw new IOException($"Profile media metadata is unavailable. MediaPublicId='{mediaPublicId}'.");

        var path = await downloader.DownloadToCacheAsync(media, uri, cancellationToken: cancellationToken);
        var image = await ImageSourceFactory.LoadFileAsync(path, 240, cancellationToken);
        return new(path, image);
    }
}
