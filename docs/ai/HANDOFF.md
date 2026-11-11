# Windows handoff

Updated: 2026-11-11. This is a source/verification checkpoint, not an assertion that every visible feature has been tested end to end. Check Git status, remote changes and active task notes before continuing.

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
