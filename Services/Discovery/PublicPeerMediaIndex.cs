using Mizon.API.Client.Operations.Media;
using Mizon.API.Client.Responses.Media;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services.Discovery;

internal sealed class PublicPeerMediaIndex(string root)
{
    private const long MaximumRecordBytes = 3 * 1024 * 1024;
    private static readonly TimeSpan EligibilityPeriod = TimeSpan.FromDays(7);
    private readonly string _directory = Path.Combine(root, "public-peer-index");
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    internal static bool CanIdentify(string? hash, int size) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit) && size is >= 1 and <= VerifiedMediaManifest.MaximumPieceSize;

    internal bool HasRecord(string hash, int size)
    {
        var key = Key(hash, size);
        if (_entries.TryGetValue(key, out var entry)) return IsCurrent(entry.IssuedAt);
        var path = Path.Combine(_directory, key);
        try { return File.Exists(path) && IsCurrent(File.GetLastWriteTimeUtc(path)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    internal async ValueTask RegisterAsync(MediaResponse media, byte[] digests, VerifiedMediaManifest manifest, CancellationToken token)
    {
        var key = Key(manifest.FileHash, manifest.PieceSize);
        var issuedAt = DateTimeOffset.UtcNow;
        // The runtime source is a typed media response from the authenticated API.
        var offer = new PublicPeerOffer(media.MediaPublicId, media.FileHash, media.FileSize, media.ChunkSize,
            media.FileName, media.Category, digests, issuedAt);
        var entry = new Entry(Clone(offer, manifest), manifest, issuedAt);
        if (_entries.TryGetValue(key, out var known) && IsCurrent(known.IssuedAt)) return;

        Remember(key, entry);
        string? staging = null;
        try
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, key);
            staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous))
                await JsonSerializer.SerializeAsync(stream, offer, PublicPeerOfferJson.Default.PublicPeerOffer, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(staging, path, overwrite: true);
            staging = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            AppLog.Information($"Public media eligibility could not be cached ({error.GetType().Name}).");
        }
        finally
        {
            if (staging is not null)
            {
                try { File.Delete(staging); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    internal async ValueTask<(MediaResponse Media, VerifiedMediaManifest Manifest)?> FindAsync(string hash, int size, CancellationToken token)
    {
        if (!CanIdentify(hash, size)) return null;
        var key = Key(hash, size);
        if (_entries.TryGetValue(key, out var entry) && IsCurrent(entry.IssuedAt))
            return (entry.Media, entry.Manifest);
        try
        {
            await using var file = new FileStream(Path.Combine(_directory, key), FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
            if (file.Length is <= 0 or > MaximumRecordBytes) return null;
            var offer = await JsonSerializer.DeserializeAsync(file, PublicPeerOfferJson.Default.PublicPeerOffer, token).ConfigureAwait(false);
            if (offer is null || !IsCurrent(offer.IssuedAt) || offer.ChunkSize != size
                || !string.Equals(offer.FileHash, hash, StringComparison.OrdinalIgnoreCase)
                || VerifiedMediaManifest.Create(offer.FileHash, offer.FileSize, size, offer.Digests) is not { } manifest) return null;
            var media = Clone(offer, manifest);
            Remember(key, new(media, manifest, offer.IssuedAt));
            return (media, manifest);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private void Remember(string key, Entry value)
    {
        if (_entries.Count >= 8)
        {
            var oldest = _entries.OrderBy(static entry => entry.Value.IssuedAt).FirstOrDefault();
            if (oldest.Key is not null) _entries.TryRemove(oldest.Key, out _);
        }
        _entries[key] = value;
    }

    private static bool IsCurrent(DateTimeOffset value)
    {
        var elapsed = DateTimeOffset.UtcNow - value;
        return elapsed >= TimeSpan.Zero && elapsed < EligibilityPeriod;
    }

    private static MediaResponse Clone(PublicPeerOffer record, VerifiedMediaManifest verified) => new()
    {
        MediaPublicId = record.MediaId,
        FileHash = record.FileHash,
        FileSize = record.FileSize,
        ChunkSize = record.ChunkSize,
        TotalChunks = verified.PieceCount,
        FileName = record.FileName,
        Category = record.Category
    };

    private static string Key(string hash, int size) =>
        $"{hash.ToLowerInvariant()}.{size.ToString(CultureInfo.InvariantCulture)}.json";

    private sealed record Entry(MediaResponse Media, VerifiedMediaManifest Manifest, DateTimeOffset IssuedAt);
}

internal sealed record PublicPeerOffer(Guid MediaId, string FileHash, long FileSize, int ChunkSize,
    string FileName, string Category, byte[] Digests, DateTimeOffset IssuedAt);

[JsonSerializable(typeof(PublicPeerOffer))]
internal sealed partial class PublicPeerOfferJson : JsonSerializerContext;
