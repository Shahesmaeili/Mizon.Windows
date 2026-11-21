using System;
using System.Collections.Generic;

namespace Mizon.Windows.Services.Discovery;

internal interface INearbyMediaSession : IAsyncDisposable
{
    string CertificateFingerprint { get; }
    int Port { get; }
    void UpdateEndpoint(string sourceId, string? name, IEnumerable<string>? txt, object? addresses, object? port, string? hostName);
}
