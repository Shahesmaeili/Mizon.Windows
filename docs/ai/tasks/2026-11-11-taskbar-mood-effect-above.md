# Place the taskbar mood click effect above the emoji

- Owner: Codex root; independent read-only review: `music_home_widgets`.
- Date: 2026-11-11.
- Repository/branch: Mizon.Windows, `codex/taskbar-mood-effect-above`.
- Base: `093282e61d0c6fe74ec3a461e13b74e690ac147b`.
- Owned files: TaskbarMoodButton, TaskbarProfileHost, taskbar README and this handoff.

## Change

The effect previously centered its 208-DIP square on the taskbar emoji. It now
keeps that horizontal center but places its bottom 8 DIPs above the owning
taskbar's actual top edge. The island is vertically centered inside the shell,
so its local origin is not a substitute for the taskbar edge. XamlRoot's
coordinate converter maps the native screen edge into local logical units.

The existing layout/root/host notifications reposition the effect without a new
timer or UI Automation query. Missing shell bounds abort opening; invalid
layout/root coordinates cancel an open popup. Coordinate conversion failures
during island teardown are handled without escaping XAML event callbacks.
Existing popup input transparency, single-play cancellation, animation preference,
scene disposal and cached animation reuse remain in place. No main-window emoji,
API, database, SDK, package or MCP permission/contract change is involved.

## Verification

- Integrated main WinUI/taskbar payload Debug x64 build passed (exit code 0);
  `taskbar-mood-above-build-final.log` is under
  `D:\MizonTaskDiagnostics\Presence\verification`.
- Existing MCP access checks passed **159 assertions**, including permission,
  cancellation and disposal cases. This presentation-only change reuses those
  contracts; no artificial coordinate tools or styling tests were introduced.
- Independent source review checked screen/local units, horizontal alignment,
  pre-open failure cleanup and root-change cancellation.

No live taskbar manipulation, Explorer restart, Windows UI automation or whole-app
NativeAOT publish was performed. Final visual alignment and multi-monitor/DPI
transitions are not claimed as observed. No reflection or dynamic code was added.

Fast-forward integration/publication targets `origin/main`. Identify the delivered
commit through this file's history. Rebuild/restart the Windows client to let its
existing payload replacement flow update the independent taskbar companion.
