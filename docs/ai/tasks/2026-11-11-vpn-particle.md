# Local VPN Particle control

- Owner: Codex sub-agent `/root/vpn_particle`.
- Repository / branch: Mizon.Windows, `codex/vpn-particle`.
- Base: `7b7d365611a5d524daccd34c4d53643f1b290c4b`.
- Worktree: `D:/MizonTaskDiagnostics/VpnRefactor/Particle`.
- Ownership: `Controls/Particles/*`, scoped particle checks, this task note.
- Integration owner: root VPN refactor task; parent owns page/resources/project
  packaging, MCP, localization and final publication.

## Implementation

Move the complete DevWinUI Particle behavior and DPs into the local namespace,
with pinned upstream and MIT provenance. Preserve the appearance/physics while
fixing its 1 ms timer, unbounded pair work and cached-page lifetime. The page
merges `Controls/Particles/Particle.xaml`; its optional `IsAnimationEnabled`
binding suspends motion without hiding the scene.

See [control provenance and changes](../../../Controls/Particles/README.md).
This is a decorative surface and introduces no protected action/data; MCP uses
the owning VPN page contract rather than a synthetic particle API.

## Verification

- Isolated WinUI class-library compile with .NET 11, Win2D 1.4.0 and the existing
  WindowsAppSDK 2.5.4-experimental: passed including the actual XAML template.
  The generated SDK metadata emitted two existing experimental-provider CS8305
  warnings, with no control diagnostics. Build harness/output live outside Git
  at `D:/MizonTaskDiagnostics/VpnRefactor/ParticleCompile`.
- Production-linked physics/storage checks: **206,733 assertions passed** in
  Release JIT and a published Windows x64 **NativeAOT** executable.
- 10,000 steady physics frames at 144 particles allocated **0 managed bytes**;
  observed 6.1 ms JIT / 7.8 ms AOT for this helper workload. These numbers exclude
  Win2D drawing/GPU work and are not an app frame-rate or memory benchmark.
- Reviewed cached unload/reload, final idempotent dispose, deferred UISettings
  callbacks, stopped timer detachment, root/ancestor observers and a pending
  invalidation during minimize/resume. Visual runtime behavior remains unverified.
- No Windows UI automation, app launch, package registration or new dependency
  installation.

## Integration

Merge this commit into the root VPN task; parent builds the complete app and
performs the scoped VPN MCP checks. Merge the resource dictionary in the page
and include the MIT notice in packaged notices. Do not dispose the control on
ordinary cached page unload; it already releases Win2D then reconstructs on load.
Final `Dispose()` is for permanent page/window disposal.
