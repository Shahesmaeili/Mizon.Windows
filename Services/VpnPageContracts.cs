using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

/// <summary>A bounded snapshot; it never includes adapter names, addresses or credentials.</summary>
public readonly record struct VpnPageEnvironmentState(
    bool IsReady,
    string NetworkStatus,
    bool MotionAllowed);

/// <summary>Platform boundary for the active VPN page. Events are delivered on its UI dispatcher.</summary>
public interface IVpnPageEnvironment : IDisposable
{
    event EventHandler? StateChanged;

    VpnPageEnvironmentState ReadState();
    bool ReadParticlesEnabled();
    bool TrySaveParticlesEnabled(bool enabled);
    void Start();
    void Stop();
    Task<bool> OpenSystemSettingsAsync(CancellationToken cancellationToken);
}
