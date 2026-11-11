# Local Particle control

This is the Win2D Particle control used by the VPN page, moved into
`Mizon.Windows.Controls.Particle`. It does not depend on DevWinUI. It uses the
application's existing Microsoft.Graphics.Win2D dependency.

## Provenance

Copyright (c) 2023 Mahdi Hosseini, MIT; full notice in [LICENSE.txt](LICENSE.txt).

The installed DevWinUI **10.4.1** package records repository commit
`30a7bbb0b97271bb148cdee0cda04462214eaa27`. Its complete source was compared with
upstream commit **`e5a4f70b9cc67f03e0093970ac502d5f8a344de0`**
(2026-11-11). The local code is derived from these files at the latter pin:

- [Particle.cs](https://github.com/ghost1372/DevWinUI/blob/e5a4f70b9cc67f03e0093970ac502d5f8a344de0/dev/DevWinUI/Controls/Win2DAndComposition/Particle/Particle.cs)
- [Particle.Properties.cs](https://github.com/ghost1372/DevWinUI/blob/e5a4f70b9cc67f03e0093970ac502d5f8a344de0/dev/DevWinUI/Controls/Win2DAndComposition/Particle/Particle.Properties.cs)
- [ParticleModel.cs](https://github.com/ghost1372/DevWinUI/blob/e5a4f70b9cc67f03e0093970ac502d5f8a344de0/dev/DevWinUI/Controls/Win2DAndComposition/Particle/ParticleModel.cs)
- [Particle.xaml](https://github.com/ghost1372/DevWinUI/blob/e5a4f70b9cc67f03e0093970ac502d5f8a344de0/dev/DevWinUI/Themes/Styles/Controls/Particle.xaml)
- [InternalExtensions.cs](https://github.com/ghost1372/DevWinUI/blob/e5a4f70b9cc67f03e0093970ac502d5f8a344de0/dev/DevWinUI/Common/Extension/InternalExtensions.cs)
  (only the random-vector and random-sign helpers)

## Preserved behavior and explicit changes

All original dependency properties and their defaults are retained: `Density`,
`Paused`, `IsPointerEnable`, `LineColor`, `ParticleColor`. The original density
formula, 120-pixel links, line thickness falloff, attraction between 80 and 120
pixels, integer random positions, depth-based circle radius, velocity, bounce,
pointer idle limit and draw/step order remain. The squared-distance shortcut and
safe max-absolute-axis pointer normalization match the current upstream fix.

Local changes:

- Replace the 1 ms redraw timer with a coalesced 60 Hz timer. Resize retains
  existing particles, skips unchanged sizes and bounds the O(n²) pair work to
  256 particles; invalid/empty sizes safely clear the scene.
- CanvasControl draws on the UI thread, so the upstream SpinLock is unnecessary.
  Frames do not allocate lists, brushes, tasks or animation objects.
- Suspend the timer while unloaded, minimized, outside the viewport, hidden by
  an ancestor, or when Windows animation effects are off.
- `IsAnimationEnabled=false` keeps a static canvas; `Paused=true` preserves the
  upstream behavior of hiding it. The hosting page can bind the former to its
  own animation preference.
- Release the Win2D canvas with `RemoveFromVisualTree`, stop the timer, detach
  pointer/root/settings/ancestor observers and clear particles on unload.
  The template's extra Grid recreates the canvas when a cached page returns.
  `Dispose()` is idempotent final teardown on the UI thread.
- Retain theme-resource colors, exclude decorative particles from Narrator and
  keyboard focus, and guard late settings callbacks by attachment generation.
- Use typed code, value-type math and `Random.Shared`; no reflection, dynamic
  XAML loading, source generator substitutes or added packages.

## Use

Merge `ms-appx:///Controls/Particles/Particle.xaml` into the hosting page's
resource dictionaries, then use `<controls:Particle Density="5"/>`.
The MIT notice must remain in source distributions; include LICENSE.txt in
packaged notices when distributing the application.

## Verification boundary

Headless checks link the production ParticleModel/ParticleScene and cover
physics bounds, extreme/invalid/resized viewports, reuse, cleanup and no steady
frame allocation. They do not prove GPU rendering, reduced-motion notifications,
Narrator output or navigation/minimize animation behavior. The owning VPN task
performs the real WinUI/XAML build; visual runtime checks remain explicit.
