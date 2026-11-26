using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Networking.ServiceDiscovery.Dnssd;
using Windows.Networking.Sockets;

[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(List<string>))]

namespace Mizon.Windows.Services.Discovery;

internal sealed class WindowsNearbyDeviceTransport(Func<Guid, CancellationToken, INearbyMediaSession?>? openMedia = null) : INearbyDeviceTransport
{
    private const string FullNameProperty = "System.Devices.Dnssd.FullName";
    private const string TextProperty = "System.Devices.Dnssd.TextAttributes";
    private const string AddressProperty = "System.Devices.IpAddress";
    private const string PortProperty = "System.Devices.Dnssd.PortNumber";
    private const string HostProperty = "System.Devices.Dnssd.HostName";
    private const string Query = """
        System.Devices.AepService.ProtocolId:="{4526e8c1-8aac-4153-9b16-55e86ada0e54}" AND System.Devices.Dnssd.Domain:="local" AND System.Devices.Dnssd.ServiceName:="_mizon-peer._tcp"
        """;

    public async Task RunAsync(NearbyDevice local, Action ready, Action<string, string?, IEnumerable<string>?, string?> update, CancellationToken cancellationToken)
    {
        await using var media = openMedia?.Invoke(local.Id, cancellationToken);
        using var mediaBrowser = media is not null ? new WindowsMediaDnsBrowser(media, update) : null;
        using var listener = new StreamSocketListener();
        listener.ConnectionReceived += RejectConnection;
        var watcher = DeviceInformation.CreateWatcher(Query, new List<string> { FullNameProperty, TextProperty, AddressProperty, PortProperty, HostProperty },
            DeviceInformationKind.AssociationEndpointService);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sync = new object();
        var endpoints = new Dictionary<string, DeviceInformation>(StringComparer.Ordinal);
        var active = true;

        void Read(DeviceInformation info)
        {
            // An Added event can precede TXT availability. Updated merges partial native properties.
            if (info.Properties.TryGetValue(FullNameProperty, out var fullName) && fullName is string name
                && info.Properties.TryGetValue(TextProperty, out var values) && values is IEnumerable<string> txt)
            {
                info.Properties.TryGetValue(HostProperty, out var host);
                update(info.Id, name, txt, host as string);
                if (media is not null)
                {
                    info.Properties.TryGetValue(AddressProperty, out var addresses);
                    info.Properties.TryGetValue(PortProperty, out var port);
                    media.UpdateEndpoint(info.Id, name, txt, addresses, port, host as string);
                }
            }
            else
            {
                update(info.Id, null, null, null);
                media?.UpdateEndpoint(info.Id, null, null, null, null, null);
            }
        }

        void Added(DeviceWatcher sender, DeviceInformation info)
        {
            lock (sync)
            {
                if (!active || (endpoints.Count >= 128 && !endpoints.ContainsKey(info.Id))) return;
                endpoints[info.Id] = info;
                Read(info);
            }
        }

        void Updated(DeviceWatcher sender, DeviceInformationUpdate change)
        {
            lock (sync)
            {
                if (!active || !endpoints.TryGetValue(change.Id, out var info)) return;
                info.Update(change);
                Read(info);
            }
        }

        void Removed(DeviceWatcher sender, DeviceInformationUpdate change)
        {
            lock (sync)
            {
                if (!active || !endpoints.Remove(change.Id)) return;
                update(change.Id, null, null, null);
                media?.UpdateEndpoint(change.Id, null, null, null, null, null);
            }
        }

        void Stopped(DeviceWatcher sender, object args) => stopped.TrySetResult();

        watcher.Added += Added;
        watcher.Updated += Updated;
        watcher.Removed += Removed;
        watcher.Stopped += Stopped;
        try
        {
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startup.CancelAfter(TimeSpan.FromSeconds(12));
            await listener.BindServiceNameAsync("").AsTask(startup.Token).ConfigureAwait(false);
            var advertisement = new DnssdServiceInstance(NearbyDeviceProtocol.FullName(local.Id), null, 0);
            foreach (var (key, value) in NearbyDeviceProtocol.ToTxt(local))
                advertisement.TextAttributes.Add(key, value);
            if (media is not null)
            {
                advertisement.TextAttributes.Add("media", "1");
                advertisement.TextAttributes.Add("cert", media.CertificateFingerprint);
                advertisement.TextAttributes.Add("media-port", media.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            var registration = await advertisement.RegisterStreamSocketListenerAsync(listener).AsTask(startup.Token).ConfigureAwait(false);
            if (registration.Status != DnssdRegistrationStatus.Success)
                throw new IOException($"DNS-SD registration status {registration.Status}.");
            watcher.Start();
            mediaBrowser?.Start();
            ready();
            await stopped.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (sync) active = false;
            watcher.Added -= Added;
            watcher.Updated -= Updated;
            watcher.Removed -= Removed;
            watcher.Stopped -= Stopped;
            listener.ConnectionReceived -= RejectConnection;
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                watcher.Stop();
        }
    }

    private static void RejectConnection(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args) =>
        args.Socket.Dispose();
}
