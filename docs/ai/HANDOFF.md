# Windows handoff

Updated: 2026-11-21. This is a source/verification checkpoint, not an assertion that every visible feature has been tested end to end. Check Git status, remote changes and active task notes before continuing.

## Nearby devices — 2026-11-21

The home header shows live Windows, Apple and other compatible Mizon devices over
Bonjour DNS-SD. The Windows session starts discovery independently of the popup.
See the [cross-platform contract and task checks](tasks/2026-11-21-nearby-devices.md).

## Track comment discussion — 2026-11-20

Track details has a paged discussion, nested replies, votes, tagged links and
a composer that can attach a playback time. A copied comment link reveals the
specific comment even outside the loaded page. See the
[task record](tasks/2026-11-20-music-comment-discussion.md) for dependencies and verification.

## Track flyouts and waveform markers — 2026-11-20

The audience panel and waveform bubble use Desktop Acrylic inside the popup
presenter, like the header profile menu. The presenter has a theme-colored
fallback when Windows cannot attach the material.
Waveform comment avatars open a text bubble, and its icon focuses the matching
comment in the existing list. See the [task record](tasks/2026-11-20-track-popups-waveform-markers.md)
for the scoped MCP actions and the taskbar indicator follow-up.

## Online audience in the taskbar preview — 2026-11-20

The existing preview adapter displays global online accounts and the signed-in
account’s online followers below the four native playback controls. It shares the
app’s SignalR connection and exposes the same aggregates through the existing
permission-gated MCP presence read. See the [task record](tasks/2026-11-20-taskbar-audience-presence.md).

## Track detail layout, transport and input — 2026-11-20

Track actions move below the heading only when their measured sizes no longer
fit. The waveform stays on its own and can seek from keyboard or Xbox focus.
The four insights retain the compact design, with native XAML rings and a
seven-point chart. Cards fill the section width in one, two or four columns.
Full-screen presentation allows four columns at a narrower viewport. The support button uses its own
theme-aware native template. The page uses native transport
and cover actions, resolves credit portraits through the media cache, and
exposes independent track-detail grants in MCP.
See the [task record](tasks/2026-11-20-music-details.md).

## Welcome typography and profile entrance — 2026-11-18

The Welcome labels, error actions and language tooltip bind to the current
locale font. Home seeds the native caption inset before layout and starts the
connected profile animation after the destination bounds settle. See the
[task record](tasks/2026-11-18-welcome-font-profile-entrance.md).

## Main shell tab selection — 2026-11-18

The four top navigation items now reflect the actual page in the inner frame.
Pages such as Settings and profiles clear the selection. Returning to a
section landing page selects its tab. See the
[task record](tasks/2026-11-18-main-shell-tab-selection.md).

## Taskbar preview build inputs — 2026-11-18

The deployed Debug payload was older than the caption-retention fix. After the
correct NativeAOT payload was loaded, the user confirmed the route opened and
the row stayed in place. Visual Studio now tracks the preview inputs and copies.
The experimental startup-cursor handling was reverted at the user's request.
See the [task record](tasks/2026-11-18-taskbar-preview-startup.md).

## Taskbar preview row retention — 2026-11-18

The bound card retains its action row when Mizon changes its title before the
shell refreshes the caption. The isolated XAML test covers the stale caption,
a short missing caption, and rejecting a different window. See the
[task record](tasks/2026-11-18-taskbar-row-retention.md).

## Deep-link loading — 2026-11-18

Loading is displayed at the source button. The taskbar preview's playlists
and storage action buttons show individual native rings until their
protocol-for-results receipts finish. The music details, playlists and
following-activity routes still wait for their initial data. The main-window
loading chip was removed at the user's request. Existing navigation read consent
controls pending-link visibility in MCP. See the
[preview guide](../../Taskbar/Mizon.Windows.PreviewXaml/README.md) and the
[preview loading task record](tasks/2026-11-18-preview-button-loading.md).

## XAML inside the taskbar preview — 2026-11-18

A .NET 11 NativeAOT C#/XAML adapter adds a real Grid row beneath the existing four
transport controls. The native-toolbar fallback was reverted. See the
[task record](tasks/2026-11-18-taskbar-preview-xaml.md) and the
[component guide](../../Taskbar/Mizon.Windows.PreviewXaml/README.md) for the
confirmed rendering and both destination clicks, the earlier
prototype Explorer crash and the Windows-build/NativeAOT lifetime boundaries.

## Account focus follow-up — 2026-11-16

An invalid controller sign-in returns to the password after the form's enabled
state settles, including if WinUI sent focus to the language picker. The picker
uses a single globe in the login header and retains its native dropdown.
Caption navigation marks the selected Windows button with a passive outline.
It no longer reads or repositions the mouse. See the current
[auth controller contract](../../Mcp/AUTH_GAMEPAD.md).

## Native caption and account layout — 2026-11-16

The native title bar in windowed authentication is selectable with controller Up
from the highest control and Left/Right across the buttons. The duplicate buttons
from View help have been removed. The language picker now uses the left side of
the title strip. Medium layouts retain a smaller showcase and explicitly apply
the current language's font to its labels. Typed identifier restrictions also
cover paste, controller keyboard and the existing auth MCP edit operation.
See [the task note](tasks/2026-11-16-auth-native-caption.md).

## Auth controller and turning card — 2026-11-15

Successful login/registration, rejected requests and local registration validation
now have separate bounded haptic patterns. Feedback starts enabled and the
controller View help can turn it off. Success transfers once to Welcome on the same window and
controller, without delaying authentication; cancellation, deactivation, disconnect
and page disposal stop feedback. No credential data is included in the handoff.

Authentication MCP read-state includes result feedback and focused window
controls. Read/control/submit permission separation is preserved. The View
help exposes selectable Minimize, Maximize/Restore and Close buttons. Rejected
submissions recover focus from the inline AI notice while modal consent keeps
its scope. The existing card spring reserves projection space inside the form
viewport, leaving the showcase and language picker independent. See
[controller, focus and card requirements](tasks/2026-11-15-auth-controller-window.md).

## Xbox controller authentication — 2026-11-12

Login/Registration/Welcome now own native controller sessions, bounded focus,
contextual button help and a Persian/English controller keyboard. Original form
validation, animated face transitions, accessibility/localization and busy guards
remain. All standard controls have contextual mappings; Guide/Share remain OS-owned.
No polling without active foreground ownership and a connected controller; unload,
window-close and deactivation stop resource work. Modal dialogs retain input ownership.

MCP authentication read/control adds a typed controller snapshot and bounded focus,
keyboard/help and optional vibration actions. There is no arbitrary input injection,
Accept/Menu or submission bypass. Other domain permissions do not change. See
[MCP contract](../../Mcp/AUTH_GAMEPAD.md) and
[UI mappings, lifecycle and validation](tasks/2026-11-12-auth-controller-ui.md).

Validation: Debug x64 WinUI build; 234 input/session, 34 text-edit, 32 production
navigator, 46 auth-adapter and 291 permission assertions; 49 isolated MCP protocol
groups. Input/text-edit NativeAOT executables and companion NativeAOT publication/
catalog checks passed. The 23 new resource keys match in fa/en. Physical controller,
actual vibration, visual layout/motion, and Narrator output remain manually unverified;
no Windows UI automation or real account submission was performed.

The future-controller requirement is now shared policy revision 2026-11-12.1 in
all four repositories. This does not imply that all pre-existing pages support it.

## Floating story editorial strip — 2026-11-11

Stutter follow-up (2026-11-12): native pointer-position expressions replace
per-mouse-sample dispatcher callbacks and eight keyframe restarts. Only
enter/exit transitions run finite animations, sampling the current pose;
ordinary motion keeps the compositor connection. The recovery-only move
handler re-arms hover after resize or deactivation. See
[smooth-motion changes and validation](tasks/2026-11-12-story-orbit-smooth-motion.md).

Pointer-motion follow-up (2026-11-12): each collage now uses a stationary input
canvas and independent mouse responses for photo, frame, shadows, headline,
edition and star. Composition combines those offsets with the existing idle
motion, retargets without resetting and restores the pose on lifecycle exit.
The reference-derived shapes, images and typography remain unchanged. See
[motion implementation and validation](tasks/2026-11-12-story-orbit-pointer-motion.md).

Image-loading fix (2026-11-12): the packaged app log showed all three cards
failing in `Path.set_Data`: the outline and matte reused one parent-owned XAML
PathGeometry. Each now owns a separate geometry tree. Optional decoration/motion
failure no longer hides the successfully initialized photo. See
[cause and checks](tasks/2026-11-12-story-orbit-image-loading.md).

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

## Featured media spectrum and transitions — 2026-11-18

- The border watches high contrast through the host window's desktop `ThemeSettings`. The legacy `AccessibilitySettings.HighContrastChanged` registration returned `0x80070490` in the packaged app's `Hero_Loaded` and interrupted player initialization. Managed and NativeAOT tests with a hidden WinUI window reproduced the failure and verified repeated initialization and disposal with the desktop API. If desktop notifications are unavailable, the border reads the current contrast setting without relying on that registration.
- The music home hero draws a thin radial Composition border that follows the mouse on the music page within 76 DIPs of the player. The light strengthens smoothly toward the edge, tracks scroll and viewport changes, and fades farther away. Its palette comes from the selected artwork or one cached video frame, with the selected video's poster as fallback. Reduced motion makes visibility immediate, and inactive or high-contrast surfaces suppress the effect.
- A separate exterior halo takes its input from the live spectrum stroke, retains its local colors through a GPU Gaussian blur, and shares the stroke's proximity opacity. The halo is created on demand and stays outside the rounded player through a geometric clip. The artwork shadow has independent ownership. Managed and NativeAOT WinUI smoke checks covered the effect graph, bounds changes, visibility and disposal. Live rendering in the installed page was not examined.
- The hero's Combined Lights treatment pairs its ambient and pointer-follow lights with a press ripple. Primary clicks and touch presses inside the active hero start a warm Composition spotlight at the press position, expand it and fade it out. Reduced motion, viewport loss and fullscreen stop the dynamic lights. Native pointer and focus handling stay with the existing controls. Managed and NativeAOT WinUI checks covered the light registrations, press gating, rapid restarts and disposal. The installed player was not visually tested.
- The hero rechecks its bounds against the main feed viewport when the retained music page reattaches, including after scroll restoration. An unchanged offset can leave XAML with no new effective-viewport event. The refresh reactivates visible content and lights without forcing a scroll or changing focus. Managed and NativeAOT hidden WinUI checks covered same-offset reattachment without a viewport notification and the offscreen case. The installed app's full navigation and appearance were not tested.
- Managed and NativeAOT proximity checks covered all four sides, diagonal distance, the range boundary and repeated Composition attachment and cleanup. Live pointer routing during scroll was not exercised in the installed app.
- The outgoing image or representative video frame remains behind the incoming layer until it is ready. The directional fade, scaling and pivot all use the media layer's Composition visual while the transport and focus targets stay in place. Assigning the XAML element's `CenterPoint` after `GetElementVisual` previously threw `0x80070005`. Selection cancels stale sampling and previous transitions.
- Existing MCP music-home action scopes and controller mappings remain the entry points. Managed and NativeAOT checks exercised palette sampling with generated media, and a hidden WinUI window verified the reveal regression, repeated interruption and hover state transitions. The MCP access and gamepad suites passed. Actual animation appearance, Narrator, and physical Xbox input were not checked.

## Music home localization, MCP and artwork — 2026-11-11

- Music home uses Light, Dark and High Contrast resources for widget ink, captions and loading surfaces. Artist and section accents follow the local theme, editorial story text adapts where it extends beyond photos, and the hero's dark media overlays use matching transport foregrounds. Managed and NativeAOT WinUI checks exercised the palette and skeleton brushes through a Light–Dark–Light round trip. Full-page visual inspection in the installed app was not performed.
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
