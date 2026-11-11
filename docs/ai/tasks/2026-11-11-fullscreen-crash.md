# Fullscreen transition crash investigation and callback isolation

- Status: source hardening implemented; user retested entry/exit and reported
  no further crash on 2026-11-11. The original inner exception remains unknown.
- Owner: Codex root; queue helper/tests: music_home_contracts; independent review:
  music_home_widgets.
- Date: 2026-11-11.
- Repository/branch: Mizon.Windows, `codex/fullscreen-crash-fix`.
- Base: `bd514ec6856f6a0ad3769361d53e0b9d6b780f7f`.
- Owned: MainWindow fullscreen callbacks, HomePage title-bar lifecycle, queue
  helper/access tests and task documentation. No server, SDK or database changes.

## Evidence and scope

The user reports a crash on entering/exiting fullscreen. Windows Application
Event 1000 at 13:22:40 recorded Mizon.Windows.exe failing in CoreMessagingXP.dll
10.0.27200.1055 with `0xc000027b` (a stowed exception). The loaded runtime was
Windows App Runtime experimental 2.5.4.0. The packaged app log did not contain
the underlying managed exception, and no matching crash dump was available.
The module/error code alone does not establish a Windows or application cause.

An isolated installed-SDK diagnostic passed routed pointer registration/removal,
empty/cleared passthrough regions and four minimal fullscreen/restore cycles.
That probe had no real Mizon page tree, native thumbnail input callback or music
services. Reapplying a cached presenter briefly showed its diagnostic window;
it was not a live Mizon UI test. A first probe run incorrectly closed its only
window before creating another, producing an unrelated shutdown failure; that
probe failure is excluded from the original-crash evidence.

Source inspection identified a concrete nested callback path: TaskbarService
invokes fullscreen subscribers directly inside its native subclass procedure.
The old setter immediately replaced the presenter, whose synchronous Changed
callback then changed XAML and registered routed input handlers. Header regions
were also computed immediately after changing their spacer width, before its
new layout had completed. These boundaries are corrected below; they are not
claimed as a reproduced root cause of the stowed exception.

## Changes

- F11, thumbnail fullscreen and the exit toolbar button defer/coalesce intent to
  a later UI-dispatch turn. Rapid toggles, explicit overrides, rejected enqueue,
  failed native changes and window disposal are handled by the tested helper.
- MCP retains the same synchronous setter/readback and existing independent
  Window read/control permissions. No new tool, permission bypass or API exists.
- Presenter notifications coalesce a later chrome refresh instead of mutating
  XAML inside SetPresenter. Active toolbar animations are retired before the
  native change; inactive windows cannot reveal the toolbar from stale pointer
  callbacks. A single retained pointer delegate is detached on cleanup.
- Header region publication waits for layout after changing the caption inset.
  Fullscreen uses ClearRegionRects, and queued callbacks are tied to the page's
  load generation. Closed windows/pages do not update native regions.
- Presenter phases and native title-bar operations log through AppLog. Recoverable
  COM/argument/state failures at queued chrome/region boundaries log their original
  stack and HRESULT instead of escaping through WinRT dispatch. No automatic
  native mutation retry, global swallowed exception or background polling added.
- Existing visual design, caption insets, top-edge toolbar, animation, localization,
  Narrator, minimize/restore and playback behavior remain in scope to preserve.

## Validation

| Check | Result | Boundary |
| --- | --- | --- |
| VS MSBuild Debug x64 with package generation/signing disabled | Passed | Existing unrelated warnings remain; no deployment or real app launch. |
| `dotnet run --project Mcp/Mizon.Windows.Mcp.AccessTests -c Release` | 215 assertions passed | Production service/helper, synthetic consent, isolated pipes/store. |
| Rapid targetless toggle during an in-flight native update | Regression reproduced with initial helper, passes after correction | Native state is simulated; tests exercise actual production queue helper. |
| Independent source review | Toolbar completion/deactivation and page-generation edges corrected | No claim about visual smoothness. |

Evidence logs: `D:/MizonTaskDiagnostics/Presence/verification/fullscreen-crash-build-final.log`
and `fullscreen-crash-access.log`. No package dependency, reflection or new JSON
contract was introduced. Whole-app NativeAOT publication was not performed.

Follow-up live validation: the user rebuilt/ran in Visual Studio, repeated the
previously failing fullscreen action and answered “no longer crashed.” This
confirms their tested scenario; it does not establish a reproduced root cause,
all monitor/DPI combinations or measured animation smoothness. The configured MCP catalog
does not expose the new window tools yet, and no Window-domain consent was granted
for that connection; access was not self-approved or bypassed.

## Handoff / publication

The requested live retest is complete by user confirmation. If it fails again, collect the new
`Window presentation:` / `Title-bar native regions:` entries and the debugger's
inner exception or dump. Do not infer resolution from the headless queue checks.
Task-owned commits are integrated and pushed to origin/main after review; Git
history identifies the final published commit without a self-referential hash.
