# VPN centering and RTL particle input

- Status: complete
- Owner/agent: Codex root; vpn_particle read-only source review
- Date: 2026-11-11
- Repository/branch: Mizon.Windows, codex/vpn-centering-pointer integrated into main
- Base commit: ab30eb3d8e2f5224b8c43d19e0abbde25d1406b3
- Owned files: Pages/VPNPage.xaml, Controls/Particles/Particle.cs and README,
  this note and the VPN handoff.
- Related repositories: none

## Fix

The constrained Stretch content could align to the flow-direction edge on wide
windows. PageContent now explicitly centers, follows ScrollViewer.ViewportWidth
and retains MaxWidth=1420. The scroll viewer vertically centers content when it
fits while retaining normal scrolling when it does not.

The page remains RTL in Persian, but the decorative particle control and its
Win2D canvas use LTR physical coordinates. Pointer positions are measured against
the actual canvas, including a canvas recreated after unload. No guessed
width-minus-X/DPI arithmetic or change to the particle physics is used.
Win2D draws via an internal Image which does not inherit text mirroring.

## Validation and limits

- Current .NET 11 / Visual Studio MSBuild Debug x64 build passed.
- Existing VPN lifecycle checks: 8 scenarios passed.
- Existing production MCP access/service checks: 276 assertions passed.
- git diff --check passed.
- UI-only layout/input correction: reuse existing VPN MCP contracts.
  No artificial pointer/layout tools or implementation-mirroring tests added.
- No Windows UI automation, deployment, network changes or real consent mutation.
  Actual pointer movement, resize appearance and Narrator remain unverified live.

## Publication

Task-owned changes are integrated to main and pushed; the commit containing this
note identifies the fix. Full-app NativeAOT publication was not repeated.
