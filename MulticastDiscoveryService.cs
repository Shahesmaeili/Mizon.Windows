using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows;

public class DeviceInfo
{
    public string DeviceId { get; set; }
    public string DeviceName { get; set; }
    public IPAddress IP { get; set; }
    public int Port { get; set; }
    public DateTime LastSeen { get; set; }
}
public class MulticastDiscoveryService
{
    private const string MULTICAST_IP = "239.255.42.99";
    private const int PORT = 40404;

    private readonly string _deviceId = Guid.NewGuid().ToString();
    private readonly string _appId = "MyLocalShare";
    private readonly string _deviceName =
    Environment.MachineName + "-" + Process.GetCurrentProcess().Id;
    private readonly Dictionary<string, DeviceInfo> _devices = new();
    private UdpClient _udp;
    private CancellationTokenSource _cts;

    public event Action<DeviceInfo> DeviceDiscovered;
    public event Action<string> DeviceLost;

    public IReadOnlyCollection<DeviceInfo> Devices => _devices.Values;

    // ================= START =================
    public void Start()
    {
        _cts = new CancellationTokenSource();

        _udp = new UdpClient();
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udp.ExclusiveAddressUse = false;

        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, PORT));
        _udp.JoinMulticastGroup(IPAddress.Parse(MULTICAST_IP));

        Task.Run(() => ListenAsync(_cts.Token));
        Task.Run(() => AdvertiseAsync(_cts.Token));
        Task.Run(() => CleanupAsync(_cts.Token));
    }

    // ================= STOP =================
    public void Stop()
    {
        _cts?.Cancel();
        _udp?.DropMulticastGroup(IPAddress.Parse(MULTICAST_IP));
        _udp?.Dispose();
    }

    // ================= ADVERTISE =================
    private async Task AdvertiseAsync(CancellationToken token)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse(MULTICAST_IP), PORT);

        while (!token.IsCancellationRequested)
        {
            var msg = new
            {
                type = "discovery",
                appId = _appId,
                deviceId = _deviceId,
                deviceName = _deviceName,
                port = 5000
            };

            byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg));
            await _udp.SendAsync(data, data.Length, endpoint);

            await Task.Delay(2000, token);
        }
    }

    // ================= LISTENER =================
    private async Task ListenAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            UdpReceiveResult result;

            try
            {
                result = await _udp.ReceiveAsync(token);
            }
            catch
            {
                break;
            }

            var json = Encoding.UTF8.GetString(result.Buffer);

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.GetProperty("type").GetString() != "discovery")
                    continue;

                if (root.GetProperty("appId").GetString() != _appId)
                    continue;

                var deviceId = root.GetProperty("deviceId").GetString();
                if (deviceId == _deviceId)
                    continue; // خودمون

                var deviceName = root.GetProperty("deviceName").GetString();
                var port = root.GetProperty("port").GetInt32();

                lock (_devices)
                {
                    if (!_devices.ContainsKey(deviceId))
                    {
                        var device = new DeviceInfo
                        {
                            DeviceId = deviceId,
                            DeviceName = deviceName,
                            IP = result.RemoteEndPoint.Address,
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
    }

    // ================= CLEANUP =================
    private async Task CleanupAsync(CancellationToken token)
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

            await Task.Delay(3000, token);
        }
    }
}