# Return the taskbar profile after temporary items disappear

- Owner: Codex root; independent read-only review: `music_home_widgets`.
- Date: 2026-11-11.
- Repository/branch: Mizon.Windows, `codex/taskbar-reclaim-space`.
- Base: `96b21a3cae43674b1e52cf6962fd1aff0423a20e`.
- Owned files: taskbar layout locator, host and existing deterministic layout checks.

## Problem and fix

The previous placement rule returned any still-safe cached position before
evaluating newly available space. Adding a Windows item moved the profile left;
removing it left that old position safe, so the profile could never return right.

Placement now always chooses the rightmost suitable free interval (bottommost for
a vertical taskbar), preserving full host dimensions, padding and other apps'
occupied areas. Removed the obsolete previous-position argument and screen-bounds
cache. The host still compares resulting native bounds before SetWindowPos, so
unchanged layouts do not cause redundant movement. Existing shell events and the
two-second recovery timer remain unchanged; no extra polling or allocations were
introduced for this fix.

## Verification

- Main WinUI and taskbar payload Debug x64 build passed with existing unrelated
  preview/nullable/WinRT warnings.
- Actual companion `--test-layout` execution passed **36 deterministic checks**.
  This entry exits before WinUI startup, mutex acquisition, UI Automation or shell
  changes. It covers the exact pair sequence `467 -> 423 -> 467`, repeated cycles,
  unchanged/reordered/duplicate obstacles, avatar-only, 150% DPI, negative monitor
  coordinates, vertical layout, no evidence and full-occupancy recovery.
- Existing MCP access checks passed **159 assertions**; the presence contract is
  reused because this is layout-only. No coordinate/control tools were invented.
- Independent source review found no remaining old call sites or placement cache.

No live taskbar manipulation, Explorer restart, UI automation or whole-app
NativeAOT publish was performed. Final live visual return is not claimed as
observed. No backend/database/SDK changes, packages or reflection were added.

Normal fast-forward integration and publication target `origin/main`; identify
the delivered commit through this file's Git history. Rebuild/restart the Windows
client to let its existing payload replacement flow update the taskbar companion.
