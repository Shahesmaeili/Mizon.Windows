using Mizon.API.Client.Operations.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services.Discovery;

internal sealed partial class WindowsPeerMediaExchange
{
    private sealed class Session : INearbyMediaSession
    {
        private readonly WindowsPeerMediaExchange _owner;
        private readonly Guid _localId;
        private readonly object _gate = new();
        private readonly X509Certificate2 _certificate;
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _lifetime;
        private readonly Task _listenerTask;
        private readonly Dictionary<string, Candidate> _peers = new(StringComparer.Ordinal);
        private readonly List<Task> _uploads = new(2);
        private bool _closing;

        internal Session(WindowsPeerMediaExchange owner, Guid localId, CancellationToken lifetime)
        {
            _owner = owner;
            _localId = localId;
            _certificate = LanMediaTransferProtocol.CreateCertificate();
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            Lifetime = _lifetime.Token;
            try
            {
                _listener = TcpListener.Create(0);
                _listener.Start(backlog: 4);
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                CertificateFingerprint = Convert.ToHexStringLower(_certificate.GetCertHash(HashAlgorithmName.SHA256));
                _listenerTask = AcceptAsync();
            }
            catch
            {
                _listener?.Stop();
                _certificate.Dispose();
                _lifetime.Dispose();
                throw;
            }
        }

        internal CancellationToken Lifetime { get; }
        internal bool Active => !Lifetime.IsCancellationRequested && !_listenerTask.IsCompleted;
        public int Port { get; }
        public string CertificateFingerprint { get; }

        public void UpdateEndpoint(string sourceId, string? name, IEnumerable<string>? txt, object? addresses, object? port, string? hostName)
        {
            var candidate = GetCandidate(name, txt, addresses, port, hostName);
            lock (_gate)
            {
                if (_closing) return;
                if (candidate is null) _peers.Remove(sourceId);
                else if (_peers.Count < 128 || _peers.ContainsKey(sourceId)) _peers[sourceId] = candidate;
            }
        }

        internal Candidate[] Candidates()
        {
            lock (_gate) return _peers.Values.DistinctBy(static candidate => (candidate.Host, candidate.Port)).Take(6).ToArray();
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (!Lifetime.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(Lifetime).ConfigureAwait(false);
                    lock (_gate)
                    {
                        _uploads.RemoveAll(static task => task.IsCompleted);
                        if (!_closing && _uploads.Count < 2 && client.Client.RemoteEndPoint is IPEndPoint remote
                            && LanMediaTransferProtocol.IsLocal(remote.Address))
                            _uploads.Add(UploadAsync(client));
                        else client.Dispose();
                    }
                }
            }
            catch (Exception error) when (Lifetime.IsCancellationRequested && error is (SocketException or ObjectDisposedException or OperationCanceledException)) { }
            catch (SocketException error)
            {
                AppLog.Information($"Public media listener closed ({error.SocketErrorCode}).");
            }
        }

        private async Task UploadAsync(TcpClient client)
        {
            using (client)
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(Lifetime))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                try
                {
                    var sent = await LanMediaTransferProtocol.ServeAsync(client.GetStream(), _certificate,
                        _owner.ReadPublicPieceAsync, timeout.Token).ConfigureAwait(false);
                    Interlocked.Add(ref _owner._uploadedBytes, sent);
                }
                catch (Exception error) when (error is IOException or SocketException or AuthenticationException
                    or OperationCanceledException or ObjectDisposedException or UnauthorizedAccessException) { }
                catch (Exception error)
                {
                    AppLog.Information($"Public peer transfer failed ({error.GetType().Name}).");
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            Task[] pending;
            lock (_gate)
            {
                if (_closing) return;
                _closing = true;
                _peers.Clear();
                pending = _uploads.ToArray();
                _uploads.Clear();
            }

            Interlocked.CompareExchange(ref _owner._session, null, this);
            try
            {
                await _lifetime.CancelAsync().ConfigureAwait(false);
                _listener.Stop();
                await _listenerTask.ConfigureAwait(false);
                await Task.WhenAll(pending).ConfigureAwait(false);
            }
            finally
            {
                _certificate.Dispose();
                _lifetime.Dispose();
            }
        }

        private Candidate? GetCandidate(string? name, IEnumerable<string>? txt, object? addresses, object? port, string? hostName)
        {
            if (name is null || txt is null || NearbyDeviceProtocol.Parse(name, txt, _localId) is null) return null;
            var socketPort = port switch
            {
                ushort number => number,
                uint number when number <= ushort.MaxValue => (int)number,
                int number when number is >= 1 and <= ushort.MaxValue => number,
                _ => 0
            };
            var supportsMedia = false;
            string? fingerprint = null;
            foreach (var attribute in txt)
            {
                if (attribute == "media=1") supportsMedia = true;
                else if (attribute is { Length: 69 } && attribute.StartsWith("cert=", StringComparison.Ordinal))
                    fingerprint = attribute[5..];
                else if (attribute.StartsWith("media-port=", StringComparison.Ordinal)
                    && ushort.TryParse(attribute.AsSpan(11), NumberStyles.None, CultureInfo.InvariantCulture, out var mediaPort)
                    && mediaPort != 0) socketPort = mediaPort;
            }
            if (!supportsMedia || socketPort == 0 || fingerprint is null || !fingerprint.All(Uri.IsHexDigit)) return null;
            var ipStrings = addresses switch { IEnumerable<string> many => many, string one => [one], _ => [] };
            IPAddress? fallback = null;
            foreach (var value in ipStrings)
            {
                if (!IPAddress.TryParse(value, out var address) || !LanMediaTransferProtocol.IsLocal(address)) continue;
                if (address.AddressFamily == AddressFamily.InterNetwork)
                    return new(address.ToString(), socketPort, Convert.FromHexString(fingerprint));
                if (!address.IsIPv6LinkLocal || address.ScopeId != 0) fallback ??= address;
            }
            var host = fallback?.ToString();
            if (host is null && hostName is { Length: >= 7 and <= 254 } && hostName.TrimEnd('.').EndsWith(".local", StringComparison.OrdinalIgnoreCase)
                && Uri.CheckHostName(hostName) == UriHostNameType.Dns) host = hostName;
            return host is null ? null : new(host, socketPort, Convert.FromHexString(fingerprint));
        }
    }

    private sealed record Candidate(string Host, int Port, byte[] Fingerprint)
    {
        internal async ValueTask<IPEndPoint?> ResolveAsync(CancellationToken token)
        {
            if (IPAddress.TryParse(Host, out var direct)) return new(direct, Port);
            var addresses = await Dns.GetHostAddressesAsync(Host, token).ConfigureAwait(false);
            var address = addresses.FirstOrDefault(static item => LanMediaTransferProtocol.IsLocal(item)
                && (!item.IsIPv6LinkLocal || item.ScopeId != 0));
            return address is null ? null : new(address, Port);
        }
    }
}
