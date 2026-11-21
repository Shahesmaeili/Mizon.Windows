using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using Mizon.API;
using Mizon.API.Client.Operations.Media;
using Mizon.API.Client.Responses.Media;
using Mizon.Windows.Services.Discovery;

internal static class NativeMediaFixture
{
    internal static async Task RunAsync(bool advertise, int? receiveIndex, (IPEndPoint Address, byte[] Fingerprint)? directAddress)
    {
        var directory = Path.Combine(Path.GetTempPath(), "MizonMediaWire", Guid.NewGuid().ToString("N"));
        using var sourceHttp = new HttpClient();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var api = new MizonApi(sourceHttp, cache);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(advertise || receiveIndex.HasValue ? 190 : 20));
        var exchange = new WindowsPeerMediaExchange(api);
        var downloader = new MediaDownloader(sourceHttp, directory, exchange);
        exchange.Attach(downloader);
        const int size = 4096;
        var payload = Enumerable.Range(0, size * 3).Select(index => (byte)(index % 251)).ToArray();
        var digest = Convert.ToHexStringLower(SHA256.HashData(payload));
        var hashes = payload.Chunk(size).SelectMany(static part => SHA256.HashData(part)).ToArray();
        var manifest = VerifiedMediaManifest.Create(digest, payload.Length, size, hashes)!;
        var media = new MediaResponse
        {
            FileHash = digest, FileSize = payload.Length, ChunkSize = size, TotalChunks = 3,
            MediaPublicId = Guid.NewGuid(), FileName = "probe.bin", Category = "fixtures"
        };
        var inventory = new PublicPeerMediaIndex(directory);
        try
        {
            if (receiveIndex is int directIndex && directAddress is { } endpoint)
            {
                var received = new byte[size];
                if (!await LanMediaTransferProtocol.TryReadAsync(endpoint.Address, endpoint.Fingerprint, manifest, directIndex, received, lifetime.Token)
                    || !received.AsSpan().SequenceEqual(payload.AsSpan(directIndex * size, size)))
                    throw new InvalidOperationException("The direct LAN peer did not return the verified fixture piece.");
                Console.WriteLine("PASS Windows fetched the verified Swift fixture over direct pinned TLS.");
                return;
            }
            if (receiveIndex is int index)
            {
                using var discovery = new NearbyDeviceDiscoveryService(new WindowsNearbyDeviceTransport(exchange.Open), Console.Error.WriteLine);
                discovery.Start("11.0-wire", "Mizon Windows receiving probe", "receiver-probe", null);
                Console.WriteLine($"READY Windows receiver waiting for fixture index={index}");
                var target = new byte[size];
                while (!await exchange.TryReadPieceAsync(manifest, index, target, lifetime.Token))
                    await Task.Delay(300, lifetime.Token);
                if (!target.AsSpan().SequenceEqual(payload.AsSpan(index * size, size))) throw new InvalidOperationException("Received bytes do not match the LAN fixture.");
                await discovery.StopAsync();
                Console.WriteLine("PASS native Windows discovered a peer and fetched its verified piece over advertised TLS.");
                return;
            }

            await inventory.RegisterAsync(media, hashes, manifest, lifetime.Token);
            await File.WriteAllBytesAsync(downloader.GetCachedFilePath(media) + ".part", payload[..size], lifetime.Token);
            if (advertise)
            {
                using var discovery = new NearbyDeviceDiscoveryService(new WindowsNearbyDeviceTransport(exchange.Open), Console.Error.WriteLine);
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                discovery.Changed += () =>
                {
                    if (discovery.ReadState().Status == NearbyDiscoveryStatus.Searching) ready.TrySetResult();
                    else if (discovery.ReadState().Status == NearbyDiscoveryStatus.Unavailable)
                        ready.TrySetException(new IOException("Unable to advertise the native test peer."));
                };
                discovery.Start("11.0-wire", "Mizon Windows media probe", "windows-probe", null);
                await ready.Task.WaitAsync(lifetime.Token);
                Console.WriteLine($"READY fixture hash={digest} size={size} index=0 available index=1 missing");
                await Task.Delay(TimeSpan.FromSeconds(175), lifetime.Token);
                await discovery.StopAsync();
                Console.WriteLine($"Windows native peer uploaded {exchange.ReadTransferState().UploadedBytes} verified bytes");
                return;
            }

            await using var session = exchange.Open(Guid.NewGuid(), lifetime.Token)!;
            var address = new IPEndPoint(IPAddress.Loopback, session.Port);
            var fingerprint = Convert.FromHexString(session.CertificateFingerprint);
            var result = new byte[size];
            for (var iteration = 0; iteration < 3; iteration++)
            {
                if (!await LanMediaTransferProtocol.TryReadAsync(address, fingerprint, manifest, 0, result, lifetime.Token)
                    || !result.SequenceEqual(payload[..size]))
                    throw new InvalidOperationException("Windows did not serve the verified partial piece through TLS.");
                if (await LanMediaTransferProtocol.TryReadAsync(address, fingerprint, manifest, 1, result, lifetime.Token))
                    throw new InvalidOperationException("Windows served a sparse piece it does not have.");
            }
            await session.DisposeAsync();
            Console.WriteLine("PASS Windows repeatedly served exact partial pieces and drained the TLS workers.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
