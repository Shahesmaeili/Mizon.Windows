using Mizon.API;
using Mizon.API.Client.Operations.Media;
using Mizon.API.Client.Responses.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services.Discovery;

internal sealed partial class WindowsPeerMediaExchange(MizonApi api) : IMediaPeerSource
{
    private MediaDownloader? _downloader;
    private PublicPeerMediaIndex? _index;
    private Session? _session;
    private int _cacheRegistration;
    private long _receivedBytes;
    private long _uploadedBytes;

    internal void Attach(MediaDownloader downloader)
    {
        _downloader = downloader;
        _index = new PublicPeerMediaIndex(downloader.CacheDirectory);
    }

    internal INearbyMediaSession? Open(Guid localId, CancellationToken token)
    {
        try
        {
            var session = new Session(this, localId, token);
            Volatile.Write(ref _session, session);
            return session;
        }
        catch (Exception error) when (error is CryptographicException or SocketException)
        {
            AppLog.Information($"Public media sharing could not start ({error.GetType().Name}).");
            return null;
        }
    }

    internal (bool Sharing, long ReceivedBytes, long UploadedBytes) ReadTransferState() =>
        (Volatile.Read(ref _session) is { Active: true }, Interlocked.Read(ref _receivedBytes),
            Interlocked.Read(ref _uploadedBytes));

    public bool EnableLookahead => true;

    public async ValueTask<VerifiedMediaManifest?> GetManifestAsync(MediaResponse media, CancellationToken token)
    {
        if (Volatile.Read(ref _session) is not { Active: true } session || _index is not { } index) return null;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, session.Lifetime);
        var response = await api.SendRequestAsync(new PeerMediaManifestOperation(new() { MediaPublicId = media.MediaPublicId }),
            lifetime.Token).ConfigureAwait(false);
        if (!response.IsSuccess || response.Content is not { } content) return null;
        var manifest = VerifiedMediaManifest.Create(content.FileHash, content.FileSize, content.ChunkSize, content.PieceSha256);
        if (manifest?.Matches(media) != true) return null;
        await index.RegisterAsync(media, content.PieceSha256, manifest, lifetime.Token).ConfigureAwait(false);
        return manifest;
    }

    public void ObserveCachedMedia(MediaResponse media)
    {
        if (Volatile.Read(ref _session) is not { Active: true } session || _index is not { } index
            || !PublicPeerMediaIndex.CanIdentify(media.FileHash, media.ChunkSize)
            || index.HasRecord(media.FileHash, media.ChunkSize)
            || Interlocked.Exchange(ref _cacheRegistration, 1) != 0) return;
        _ = RegisterCachedMediaAsync(media, session.Lifetime);
    }

    private async Task RegisterCachedMediaAsync(MediaResponse media, CancellationToken sessionToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try { await GetManifestAsync(media, deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
        catch (Exception error)
        {
            AppLog.Information($"A cached public media offering was unavailable ({error.GetType().Name}).");
        }
        finally { Volatile.Write(ref _cacheRegistration, 0); }
    }

    public async ValueTask<bool> TryReadPieceAsync(VerifiedMediaManifest manifest, int index, Memory<byte> destination, CancellationToken token)
    {
        if (Volatile.Read(ref _session) is not { Active: true } session) return false;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, session.Lifetime);
        foreach (var peer in session.Candidates())
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(430));
            try
            {
                if (await peer.ResolveAsync(timeout.Token).ConfigureAwait(false) is not { } target
                    || !await LanMediaTransferProtocol.TryReadAsync(target, peer.Fingerprint,
                        manifest, index, destination, timeout.Token).ConfigureAwait(false)) continue;
                Interlocked.Add(ref _receivedBytes, destination.Length);
                return true;
            }
            catch (Exception error) when (!lifetime.IsCancellationRequested
                && error is (IOException or SocketException or AuthenticationException or OperationCanceledException)) { }
        }
        return false;
    }

    private async ValueTask<int> ReadPublicPieceAsync(string hash, int pieceSize, int index, Memory<byte> target, CancellationToken token)
    {
        if (_downloader is not { } downloader || _index is not { } inventory) return 0;
        var entry = await inventory.FindAsync(hash, pieceSize, token).ConfigureAwait(false);
        if (entry is null) return 0;
        var (media, manifest) = entry.Value;
        var length = manifest.GetPieceLength(index);
        if (length == 0 || length > target.Length) return 0;
        var piece = target[..length];
        if (await downloader.TryReadCachedPieceAsync(media, manifest, index, piece, token).ConfigureAwait(false))
            return length;
        return 0;
    }
}
