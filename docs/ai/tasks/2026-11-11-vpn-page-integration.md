# VPN page refactor and local Particle integration

- Status: complete (source/build/headless checks; visual checks remain unverified)
- Owner/agent: Codex root; isolated collaborators vpn_state, vpn_particle, vpn_mcp
- Date: 2026-11-11
- Repository and branch: Mizon.Windows; codex/vpn-page-refactor integrated into main
- Base commit: 7b7d365611a5d524daccd34c4d53643f1b290c4b
- Related repositories: no server, SDK or database changes
- Owned areas: VPNPage, local Particle, VPN view model/environment, scoped MCP/consent,
  Vpn resources, App adapter wiring, license packaging and these handoffs.
  Each collaborator used its own worktree/branch; root integrated reviewed commits.

## Request and acceptance criteria

Refactor the VPN page to match Mizon, retain the particle experience without
using DevWinUI on this page, support current .NET/WinUI patterns, responsive
layout, live Persian/English, accessibility, cleanup and scoped MCP.
The user explicitly limited DevWinUI removal to VPN in this task. The global
package/resources remain because other pages still use them.

## Implementation and decisions

- Transparent theme-aware connection/status/traffic cards, stationary button
  outlines with existing AuthActionButton hover/press, existing WidgetLoadHost
  skeleton/reveal, and a local Particle background. Breakpoint changes use VSM
  only when page width crosses 600 or 940; no per-pixel visual-tree rebuild.
- Persian/English MRT resources and live x:Bind localization, inherited IRANSans
  for Persian, RTL reading/alignment, dark/light/high-contrast brushes,
  keyboard focus and labeled actions, loading and network announcements.
- Upstream Particle source and MIT notice are pinned in Controls/Particles.
  Its drawing/physics and original five dependency properties are preserved.
  Local changes bound particle count, coalesce redraws to 60 Hz, reuse storage
  on resize, pause offscreen/minimized/hidden/reduced-motion, release Win2D on
  unload and rebuild the canvas on a later load. The license ships in the package.
- The old page had no VPN transport, provider/configuration or connection
  service. Its hardcoded download/upload/ping values are replaced by unavailable
  values. Internet reachability is an actual event-driven Windows observation,
  deliberately distinct from Mizon VPN connection state.
- Both setup buttons open only Windows' fixed ms-settings:network-vpn page.
  They do not establish a tunnel. The page truthfully says Mizon VPN is not
  configured; no fake connection, server, account or usage numbers were added.
- A saved particles preference is shared by UI and MCP. All OS observers,
  cancellation sources, bindings and animations have page/control lifetimes;
  settings launch is single-flight, cancellable and bounded to 15 seconds.
- Three independently consented tools: mizon_get_vpn_state,
  mizon_set_vpn_particles and mizon_open_vpn_settings. They use the live signed-in
  VPN page, not an alternative implementation. Read/control grants are separate
  from existing navigation/auth grants. Responses expose loading, OS network
  category, preference persistence and bounded error codes; applied_not_saved
  reports a preference that is applied but cannot be persisted.
  See Mcp/VPN.md.

## Validation

| Check | Result | Limits |
| --- | --- | --- |
| Visual Studio 18 MSBuild Debug x64, current .NET 11 / WinUI XAML | Passed | Existing unrelated preview/nullable warnings remain; not full-app AOT publication |
| Production VPN state/lifecycle checks | 8 scenarios passed, managed and native helper | Synthetic OS environment, no Settings launch |
| Production particle physics/storage | 206,733 assertions, managed and native helper | Not GPU rendering or frame-rate profiling |
| Production MCP access/adapter integration | 276 assertions passed | Synthetic isolated app, no real grants changed |
| Actual MCP SDK/stdio/pipe checks | 47 groups passed | Synthetic data and isolated credentials |
| Published NativeAOT MCP host catalog/schema | Passed | No call to a real user's page |
| Localized resources | fa/en have matching 37 nonempty keys; XAML compiles | Speech and visual RTL not live-tested |
| Source review | GoToState retry, Color resources, offscreen intersection and final unknown-network copy corrected | Review does not replace UI validation |

No Windows UI automation, app deployment, registration, network change or
self-approval was performed. A new in-app VPN grant is necessary for live MCP
operations; existing authentication/navigation approval never grants VPN access.

## Handoff

The backend/transport needed for actual Mizon VPN tunnels remains absent, as it
was before this task. This task refactors the existing page; do not claim that
opening Windows settings establishes a protected Mizon connection.

Manual checks still needed after a normal Visual Studio run: narrow/wide
layout in both languages, dark/light/high-contrast, keyboard/Narrator output,
particle hover/minimize/scroll behavior, Settings launch and real in-app VPN
consent. The global DevWinUI package should be removed only after migrating its
remaining consumers in later tasks.

## Publication

Root integrates the task-owned commits on main and verifies the GitHub SHA.
The commit containing this note identifies the final integration.
