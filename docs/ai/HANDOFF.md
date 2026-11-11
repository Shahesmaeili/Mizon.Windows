# Windows handoff

Updated: 2026-11-11. This is a source/verification checkpoint, not an assertion that every visible feature has been tested end to end. Check Git status, remote changes and active task notes before continuing.

## Floating story editorial strip — 2026-11-11

The follow-up now traces six separate photo/paper contours from the supplied
1600x1200 reference, replacing scaled duplicate outlines. A shared layout space
preserves the reference's staggering/crops; tinted ambient and neutral contact
shadows include reserved edge space. See [traced frames and validation](tasks/2026-11-11-story-orbit-traced-frames.md).

MusicPage now places three decorative organic portrait collages immediately
above MusicStories. Native Composition animates portrait, contours, badge and
typography independently. The reference-match follow-up uses the original
Illustration/Team images and a similar generated left portrait approved by the
user when its original could not be found. The English reference labels and
22/23/24 editions are intentional; these decorative labels no longer localize.
The strip is responsive, skips decorative Narrator output and stops
its animation/resource work with the page lifecycle and Windows motion setting.
No new API, MCP action or package is added. See
[initial implementation](tasks/2026-11-11-story-orbit-showcase.md) and
[reference alignment, source credits and verification](tasks/2026-11-11-story-orbit-reference-match.md).
Live visual validation was not performed; existing music-home MCP checks and
Debug x64 compilation passed.

## VPN page and local Particle — 2026-11-11

Follow-up: constrained page content now explicitly centers within the viewport,
and the particle canvas uses LTR physical coordinates even in Persian. Pointer
positions are read from that canvas rather than the RTL parent. See
[centering/input fix](tasks/2026-11-11-vpn-centering-pointer.md); live mouse/resize
verification remains separate from successful build and headless checks.

VPNPage now uses its own upstream-derived Particle control, retaining the effect
with bounded work and explicit Win2D/observer cleanup. DevWinUI remains installed
for other pages, per the user's scope. The page has responsive translucent
cards, live fa/en resources, dark/light/high-contrast, shared loading and
accessible controls. Internet state comes from Windows; no VPN tunnel service
exists yet, so usage and Mizon connection status remain honestly unavailable.
The setup action opens Windows VPN settings; it does not establish a connection.

The page exposes independent MCP VPN read/control scopes, including saved
particle preference, loading and bounded errors. See [VPN contract](../../Mcp/VPN.md)
and [validation/remaining visual checks](tasks/2026-11-11-vpn-page-integration.md).
Debug x64, isolated behavior/permission tests and native helper/catalog checks
passed. Actual UI, Narrator speech and full-app AOT publication were not tested.

## Correlated deep-link responses and taskbar loading — 2026-11-11

The existing `mizon` protocol now declares `ReturnResults="optional"`. Native
results use the bounded version-1 source-generated JSON envelope in
`Services/AppDeepLinkResult.cs`; see [the caller contract](../../DeepLinks.md).
Program retains the native result operation before single-instance redirection
returns. Startup, cancellation, replacement, failure and closure report at most
once through the existing navigation flow. Page-only success is explicitly
`opened`; profile, comment, story readiness and playback/download use their
actual completion outcome. Normal shell links keep their existing behavior.

The independent taskbar profile waits for a verified native response and shows
a stationary ProgressRing beside the unchanged presence badge/photo animation.
It uses the main app's published PFN, a caller-owned HWND, a 60-second deadline,
and account/disposal cancellation. No token is published to the companion.
MCP navigation receives typed receipts and native pending URI under existing
independent navigation permissions; it still cannot play/download music.

Debug x64 and headless/native helper checks passed. Deployment, actual taskbar
child-HWND result delivery/focus, visible ring/DPI and Narrator speech remain
unverified; no Windows UI automation or registration changes were performed.
The changed protocol manifest requires a normal MSIX Deploy before runtime use.
See [integration evidence and limits](tasks/2026-11-11-deep-link-results-integration.md).

## Fullscreen header and window controls — 2026-11-11

Crash follow-up: native thumbnail/F11/button presenter mutations are deferred and
coalesced outside input callbacks; chrome updates follow presenter completion,
toolbar motion is stopped before replacing the presenter, and native header
regions wait for the new inset layout. Late/disposed callbacks are guarded.
Debug x64 build and 215 access/transition assertions passed. The reported
CoreMessagingXP stowed exception lacked its inner exception/dump. The user then
retested fullscreen entry/exit and confirmed no further crash on 2026-11-11.
This is user-reported live validation, not an automated reproduction of its cause.
See [crash evidence](tasks/2026-11-11-fullscreen-crash.md).

The following visual fix suppresses RootGrid's automatic F11 hover tooltip and
removes F11 from the exit-button tooltip. F11 still invokes the same operation and
remains available as Narrator accelerator metadata; see [tooltip follow-up](tasks/2026-11-11-hide-fullscreen-tooltip.md).

Home's hardcoded 148-DIP caption margin is replaced by the actual Windows
RightInset converted through the current XAML scale; fullscreen reserves zero
caption space and clears stale native passthrough regions. A top-edge WinUI
overlay reveals minimize/exit/close controls without reflow; F11 uses the same
presenter operation. This is app-owned UI: the installed SDK has no configurable
OS-provided mouse-hover fullscreen toolbar. Minimize retains fullscreen.

Independent window read/control MCP grants expose a bounded state and explicit
fullscreen setting; older grants do not authorize either. Debug x64 build,
187 access assertions, 41/41 MCP protocol groups and the NativeAOT MCP host/catalog
passed. Live hover/DPI/Narrator/fullscreen restore were not automated.
See [implementation and validation](tasks/2026-11-11-fullscreen-header.md) and
[MCP contract](../../Mcp/WINDOW_PRESENTATION.md).

## Taskbar mood click effect placement — 2026-11-11

The click effect now sits entirely above the actual taskbar top, separated by
8 logical pixels and horizontally centered on the emoji. Screen-to-XAML
conversion preserves placement across DPI changes; invalid/disconnected roots
cancel the popup through its existing cleanup path. The main-window emoji is
unchanged. See [verification and limits](tasks/2026-11-11-taskbar-mood-effect-above.md).

## Taskbar placement recovery — 2026-11-11

The profile/mood host now reclaims the rightmost suitable free space when Windows
removes an item. Removed the sticky previous-position shortcut; unchanged bounds
still skip native movement. Debug x64 build, 36 actual companion layout checks and
159 existing MCP access checks passed. Live shell UI was not manipulated.
See [evidence and limits](tasks/2026-11-11-taskbar-reclaim-space.md).

## Header/taskbar presence — 2026-11-11

Header and taskbar share a bottom-right online/offline badge with a transparent
photo cutout. The main window's focus and existing authenticated SignalR connection
own the state; disconnected, inactive or closed ownership cannot stay green.
Taskbar process/lease checks preserve the companion's independent lifetime.
`mizon_get_presence_state` adds a separate read-only Presence consent group.

The follow-up increases badge clearance to 2.5 logical pixels, keeps the badge
stationary while only the photo animates, and extends the taskbar HWND region to
include the whole badge. See [spacing/motion evidence](tasks/2026-11-11-profile-presence-spacing.md).

Verified: integrated Debug x64 build; 38 coordinator/IPC assertions in managed and
NativeAOT execution; 50 avatar/taskbar checks; 159 MCP access assertions; 38/38 MCP
protocol groups; native MCP catalog and matching server/SDK SignalR diagnostics.
No live UI, Narrator speech, self-approval or whole-app NativeAOT publish was tested.
See [implementation, dependencies and limits](tasks/2026-11-11-profile-presence.md).

## Music home localization, MCP and artwork — 2026-11-11

The current music home (`MusicPage`, not details `MusicsPage`) now uses live
Persian/English resources for center cards, five widgets, story viewer and profile/
demo previews, with language-specific direction/fonts and Narrator labels.
Language changes preserve decoded images, models, card identity and selection.

Three typed MCP tools expose bounded active-page state, refresh and allowlisted
actions through the actual page/controls. Music read/control/submit consent is
independent from all prior grants. Cached offscreen pages cannot be read. Playback
handoff, cancellation, revocation, redaction and remembered scopes are covered.
See [MCP music home](../../Mcp/MUSIC_HOME.md) and
[the task evidence](tasks/2026-11-11-music-home-mcp-localization.md).

Verified: WinUI Debug x64 build; 134 production access-service assertions; 35/35
SDK/protocol groups; published NativeAOT MCP discovery 1/1; 750 presentation and
resource checks in both managed and NativeAOT execution. Test `.resw` outputs are
excluded from the main MRT resource scan, including nested test `bin` folders.
Existing preview/nullability/trim warnings remain; whole-app NativeAOT publishing,
live permission approval, visual layout/animation and actual Narrator output were
not tested. No Windows UI automation or real MCP self-approval was performed.

Related server commit `f939cb1ce2a964eef62a62a094c81ce95de42c20` replaced artwork
for 12 demo stories and 4 featured slides through a narrow .NET Logic operation.
Dedicated media IDs prevent stale image caches and preserve original avatars/music
covers. Canonical server App_Data paths, licensing, checksums, idempotency and actual
read-operation verification are documented in the server task note. No schema or
SDK upgrade is required. The new home MCP scopes need the user's own in-app grant
when the updated application/host is launched; older scopes do not inherit them.

## .NET 11 migration

The current project graph targets .NET 11; SDK 11.0.100 is pinned with patch-only
roll-forward. Windows target/minimum OS versions and Windows App SDK versions stay
unchanged. See [the migration task](tasks/2026-11-10-net11-upgrade.md) for validation
and the benchmark boundary. Previous checkpoint results below remain historical.

The migration reproduced an ExplorerCommands clean-build failure inside Visual
Studio's full-framework MSBuild (`GenerateClsidMap`, incompatible HostModel metadata
method). The project now uses the official .NET TaskHost for the three SDK COM
generation tasks. A fresh VS Rebuild passes; COM hosting remains enabled. No SDK
installation files, Windows registration or running companion processes were patched.

Fresh .NET 11 verification includes all 16 existing projects plus the new headless
benchmark, main WinUI Debug/Release x64, managed regression suites, NativeAOT
activation/pinning checks and actual NativeAOT MCP catalog validation. The detailed
task note separates compile checks, synthetic behavior and unverified visual/live
scenarios. Existing preview/nullability/WinRT and Release trim warnings remain.

## Latest completed behavior

### Canonical deep links

- [DeepLinks.md](../../DeepLinks.md) lists 18 supported route definitions and the nine settings section slugs. `AppDeepLinkCatalog` and `AppActivationRoute` are the runtime authority.
- Public IDs are GUIDs. Normal music detail navigation does not autoplay; explicit play/download links retain separate actions.
- Single-instance activation, pending content after sign-in, invalid-link rejection, cancellation/supersession and sign-out guards are implemented.
- The user explicitly rejected a public Welcome link. `mizon://welcome`, its legacy aliases and typed navigation requests are rejected. Welcome remains internal session validation. MCP can report the internal location as `welcome` with `uri=null`; the dedicated authentication development tools remain available under their existing consent scope.
- The navigation MCP tools can open declared destinations and read location state. They cannot submit forms, play/download tracks or read all destination business data under that permission.

### Pin main application to the taskbar

- The header profile menu contains the localized “Pin Mizon to the taskbar” action, styled with the existing profile actions and Narrator metadata.
- `TaskbarPinService` queries actual Windows pin state when the menu opens and when the main window reactivates while the menu is present. A pinned app hides the action; an external Unpin is detected on the next refresh. There is no persisted fake “pinned” flag or constant polling loop.
- Clicking first waits for the native MenuFlyout to finish closing, then calls the official Windows pin request. The user confirms in Windows. Duplicate requests are gated; cancellation and app shutdown respect the outstanding native operation's ownership.
- The new MCP tools are `mizon_get_taskbar_pin_state` and `mizon_request_taskbar_pin`. Separate taskbar read/control grants default to false, are explicitly approved in the app, and can be revoked. The adapter shares the same service as the profile menu.
- The new implementation targets pinning the **main application icon**. It does not replace or change the existing independent profile/mood taskbar companion.

## Verification evidence for this checkpoint

The checks below were executed during the latest implementation work on the developer machine. They are historical evidence for that source state; rerun relevant checks after changes.

| Check | Observed result | Scope |
| --- | --- | --- |
| Main WinUI build | `Debug x64` passed | Compiles app, generated XAML/projections and dependent payloads; existing warnings remain |
| Activation/App Actions suite | 600 assertions passed, managed and NativeAOT win-x64 | Production parser/catalog/action registration fixtures; no app launch or real navigation |
| Taskbar pin service suite | 25 assertions passed, managed and NativeAOT win-x64 | Production service logic with synthetic OS state, unpin refresh, foreground/policy refusal, duplicates, cancellation and disposal |
| MCP protocol suite | 31/31 groups passed | Actual SDK/stdio/isolated pipe boundary, schemas, allowed navigation destinations, independent taskbar tools and connection lifecycle |
| MCP access integration suite | 110 assertions passed | Production permission service/view model/adapters with synthetic backend and isolated consent store; persisted/revoked scopes, redaction, errors and cleanup |

Local evidence files were written under `D:/MizonTaskDiagnostics`: `taskbar-pin-build.log`, `taskbar-mcp-protocol.log`, `taskbar-mcp-access.log`, `taskbar-pin-aot-build.log` and the Welcome-removal AOT logs. These files are local diagnostics, not required repository inputs and not intended for Git publication.

**Not verified by these checks:** actual Windows pin confirmation/acceptance, final menu rendering or animation, real API-backed page navigation, end-to-end music playback, or a full NativeAOT publication of the entire WinUI app. No Windows UI automation or self-approval of a live MCP connection was performed for this work.

## Existing feature areas to preserve

The source contains music discovery widgets/hero/stories, music details with waveform/lyrics/comments/live presence and stem mixing, playlists, following activities, profiles, PiP, mood emojis, theme/material and language preferences, Windows Search, ExplorerCommands, JumpList, App Actions, taskbar preview buttons and independent taskbar companions. This inventory identifies code ownership; it is not a claim that every area is complete or covered by current MCP tools.

The movies/apps destinations use their existing landing implementation. Do not claim film/series detail infrastructure or unrestricted profile/music MCP control that the source does not provide. Follow the separate server/SDK/database handoffs for cross-layer changes.

## Important limits and next-task checks

- Full feature-by-feature MCP business coverage remains future work. Current domains are authentication, appearance, declared navigation, Mizon taskbar pinning, active music home and current-user presence. See [the development guide](../../Mcp/DEVELOPMENT.md).
- Live consent remains user-owned even though paired grants persist. A tool catalog or configured Codex/Claude server does not imply an active grant for a new domain.
- Windows App SDK and some Windows integration surfaces are experimental or OS-dependent. Record installed runtime/build information for integration-specific failures rather than assuming every machine supports the same host.
- Source paths and the backend solution currently reflect this developer's checkout layout. Verify project references when using an independent worktree or a new machine.
- The repository had accumulated many feature changes before this publication task. The publication coordinator owns the initial all-code snapshot. Future tasks should commit only their scoped changes, with their own handoff and push result, rather than sweeping another agent's unfinished files.
- Markdown entry points help agents read the same durable context; they cannot synchronize an already-running AI conversation or prevent concurrent writes. Use [COLLABORATION.md](COLLABORATION.md), separate worktrees and explicit ownership.
