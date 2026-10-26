using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace Mizon.Windows;

public class MulticastDiscoveryServiceV2
{

    private const string MULTICAST_IP = "239.255.42.99";
    private const string PORT = "40404";

    private readonly string _deviceId = Guid.NewGuid().ToString();
    private readonly string _appId = "MyLocalShare";
    private readonly string _deviceName =
        Environment.MachineName + "- " + Process.GetCurrentProcess().Id;

    private readonly Dictionary<string, DeviceInfo> _devices = new();

    private DatagramSocket _socket;
    private CancellationTokenSource _cts;

    public event Action<DeviceInfo> DeviceDiscovered;
    public event Action<string> DeviceLost;

    public IReadOnlyCollection<DeviceInfo> Devices => _devices.Values;

    // ================= START =================
    public async Task StartAsync()
    {
        _cts = new CancellationTokenSource();

        _socket = new DatagramSocket();
        _socket.MessageReceived += OnMessageReceived;

        await _socket.BindServiceNameAsync(PORT);

        _socket.JoinMulticastGroup(new HostName(MULTICAST_IP));

        _ = Task.Run(() => AdvertiseLoopAsync(_cts.Token));
        _ = Task.Run(() => CleanupLoopAsync(_cts.Token));
    }

    // ================= STOP =================
    public void Stop()
    {
        _cts?.Cancel();
        _socket?.Dispose();
    }

    // ================= ADVERTISE =================
    private async Task AdvertiseLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await SendDiscoveryAsync();

            try
            {
                await Task.Delay(2000, token);
            }
            catch
            {
                break;
            }
        }
    }

    private async Task SendDiscoveryAsync()
    {
        var msg = new
        {
            type = "discovery",
            appId = _appId,
            deviceId = _deviceId,
            deviceName = _deviceName,
            port = 5000
        };

        string json = JsonSerializer.Serialize(msg);

        using var stream = await _socket.GetOutputStreamAsync(
            new HostName(MULTICAST_IP),
            PORT);

        using var writer = new DataWriter(stream);
        writer.WriteString(json);
        await writer.StoreAsync();
        await writer.FlushAsync();
    }

    // ================= RECEIVE =================
    private void OnMessageReceived(
        DatagramSocket sender,
        DatagramSocketMessageReceivedEventArgs args)
    {
        try
        {
            //using var reader = new DataReader(args.GetDataStream());
            //string json = reader.ReadString(reader.UnconsumedBufferLength);

            //if (string.IsNullOrWhiteSpace(json))
            //    return;


            using var reader = args.GetDataReader();
            reader.UnicodeEncoding = UnicodeEncoding.Utf8;

            string json = reader.ReadString(reader.UnconsumedBufferLength);

            if (string.IsNullOrWhiteSpace(json))
                return;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.GetProperty("type").GetString() != "discovery")
                return;

            if (root.GetProperty("appId").GetString() != _appId)
                return;

            var deviceId = root.GetProperty("deviceId").GetString();
            if (deviceId == _deviceId)
                return;

            var deviceName = root.GetProperty("deviceName").GetString();
            var port = root.GetProperty("port").GetInt32();

            var ip = IPAddress.Parse(args.RemoteAddress.CanonicalName);

            lock (_devices)
            {
                if (!_devices.ContainsKey(deviceId))
                {
                    var device = new DeviceInfo
                    {
                        DeviceId = deviceId,
                        DeviceName = deviceName,
                        IP = ip,
                        Port = port,
                        LastSeen = DateTime.Now
                    };

                    _devices[deviceId] = device;
                    DeviceDiscovered?.Invoke(device);
                }
                else
                {
                    _devices[deviceId].LastSeen = DateTime.Now;
                }
            }
        }
        catch
        {
            // ignore malformed packets
        }
    }

    // ================= CLEANUP =================
    private async Task CleanupLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            List<string> toRemove = new();

            lock (_devices)
            {
                foreach (var d in _devices)
                {
                    if ((DateTime.Now - d.Value.LastSeen).TotalSeconds > 6)
                        toRemove.Add(d.Key);
                }

                foreach (var id in toRemove)
                {
                    _devices.Remove(id);
                    DeviceLost?.Invoke(id);
                }
            }

            try
            {
                await Task.Delay(3000, token);
            }
            catch
            {
                break;
            }
        }
    }
}
