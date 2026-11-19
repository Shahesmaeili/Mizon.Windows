# Track detail responsive layout and input

- Owner: Codex
- Branch: `codex/music-details-modernization`, base `401ea80`
- Owns: Windows track details, timed waveform presentation, transport, cover
  preview, contributor portraits, and MCP track-detail permissions
- API dependencies: existing track details, credits and media contracts

The header measures the title and its action group before placing them. The
four actions share the title row whenever their natural widths fit. At smaller
widths, the group moves below and can wrap internally. The four lower cards
retain the original sample heart rate, listener feedback, heart-rate trend and
AI summary. The four cards keep the original compact height, colored icons,
rings, arrow edges and line series. Native XAML shapes draw the three rings
and seven original trend samples without the chart engine, load callbacks,
or composition animations. Cards fill the available width and retain the
16-unit gap when wrapping into one, two or four columns. The fullscreen
presenter uses a more compact four-column threshold. In narrow cards only the
middle content scales down. The sample caption remains absent.

The transport exposes native Play/Pause and Repeat controls with independent
focus stops. Only the original waveform is shown for seeking. With focus on
the waveform, LT/RT or Page Up/Page Down move the same playback position by
five seconds. D-pad and left stick move focus, A activates native controls,
and B closes an open cover preview before leaving the page. The cover preview
has native zoom buttons, respects the Windows animation setting and gives
focus back to the cover when dismissed. Contributor portraits are decoded from
the local cache at display size, shared between duplicate credits and replaced
by initials when unavailable. The themed support button opens a native flyout
that shares the real track deep link. Payment requires a separate integration.
Endpoint master-volume reads use the documented Core Audio ABI via
source-generated interop. Each interface and COM initialization reference is
released on the calling thread. This path reads the device without changing
its volume.

MCP discovery exposes `mizon_get_music_details` and
`mizon_set_music_details`. Normal navigation remains scoped to the existing
navigation tools. Independent Track details read and control grants cover the
active track only. The control accepts either `repeatEnabled` or `coverOpen`,
each as an explicit boolean. Read status includes whether the cover and
contributor portraits are available. No media files, URLs, credentials or
payment data are returned.

Verification:

- .NET 11 x64 Debug build and full Windows x64 Release NativeAOT publication.
  Existing unrelated trim/AOT diagnostics are still reported for DevWinUI,
  SkiaSharp and existing reflection paths.
- An offscreen WinUI probe passed nine checks in managed and NativeAOT builds:
  the measured row boundary, responsive card layout in both directions, and
  cache-file portrait decoding and cancellation.
- A separate NativeAOT Core Audio probe read the current Windows render
  endpoint and handled a nonexistent endpoint without changing any settings.
- 330 MCP access assertions, 49 isolated companion protocol groups, the
  published NativeAOT companion catalog, 301 controller input/session assertions
  and 70 source-linked focus-navigation assertions passed.
- The visual follow-up repeated Debug, NativeAOT, MCP access and controller
  session checks. An offscreen WinUI fixture rendered the four original cards
  in one, two and four columns and verified the support template in Light and
  Dark using substitute glyphs and system brush values. A separate 14-case
  probe exercised the waveform's production keyboard and controller handler
  with synthetic focus and audio.
- The four-widget redesign was checked in offscreen WinUI at 260, 725, 1000
  and 1320 pixels. The middle widths use two balanced columns, the widest
  fits four, and the narrow layout shows one. The fixture also verified there
  is no sample caption in Persian or English.
- The original-layout restoration tested the actual compiled control in a
  hidden WinUI scroller. It verified chart creation on visibility, retention
  during scrolling, release on detach and recreation on return. It also
  verified the original card offsets, 16-unit row spacing and fit at a
  240-unit viewport. The existing MCP permission checks and the app's
  Debug build and NativeAOT publication passed again. The separate native
  fixture failed at startup because its linker inputs omitted WinRT.Runtime.
  Chart lifecycle was exercised in the fixture's managed WinUI run.
- The follow-up Windows event reported a native CoreMessagingXP failure at
  track-page entry without a corresponding managed exception. It did not
  identify the exact failing control. The replacement native-shape widgets
  passed an isolated compiled WinUI test across 11 widths or presentation
  combinations and 24 attach, resize and detach cycles. The fixture verified
  that the last card reaches the end of the available row at each width.
- Packaged-page visuals and animations, physical Xbox hardware and live
  Narrator output were not exercised.

## Stem mixing, waveform comments and transport notch

- Explicit mixer switches preserve the active track's rendered position and pause state. The player opens both disconnected Windows AudioGraph inputs before it stops the current source, and a failed or cancelled open leaves that source in place. Starting a different track begins at zero.
- The mixer is available when the server returns two distinct stem media IDs for the currently selected music-file ID. Revisiting the playing track restores its mixer state and balance. The slider fixes left to accompaniment, center to both and right to vocals, with matching Persian and English Narrator help.
- Waveform comments capture the content-relative click coordinate and selected track duration once, then render the confirmed comment immediately at that timestamp. The signed-in portrait is reused if available. With waveform focus, Xbox A or Enter selects the center of the visible waveform for the same dialog. The existing track-details MCP read grant exposes mixer status and the confirmed waveform comment count. Existing repeat and cover control scopes still apply.
- The fixed Play/Repeat notch has four additional device-independent units of clearance, with the same button sizes and focus positions.
- The native Windows AudioGraph probe used generated WAV tones and analyzed the output samples to verify all three mix positions, live and paused source swaps, cancellation and failed loading, seek, repeat and starting another source. MCP access tests cover the new read state and waveform timestamp round-trips at both ends and at fractional content positions. These checks used synthetic media and consent. Live clicking, real controller hardware and live Narrator output remain unverified.

- The playback-device picker uses a WinUI MenuFlyout with native exclusive selection. It keeps the live volume and battery details, applies the selected device through the AudioGraph service, and detaches watchers on close. The track read scope includes language and bounded output availability.
- Track details support live fa-IR/en-US switching across controls, menus, dialogs, tooltips, Narrator labels, and the original showcase. Alternate titles and genre names prefer translated API data when available. The playback and waveform axes keep their physical direction.
- Waveform hover tracks the pointer at the timeline level. Painting touches only the changed positions, leaves the played range intact, and avoids per-bar brush transitions. Regression tests cover forward/backward pointer jumps, exact bar boundaries, and timestamp round trips with a scrolled view.
- Comment markers use transient canvas ordering and animate only the emphasized marker and its predecessor. Keyboard and Xbox focus can emphasize a marker, and pointer exit or page detach restores the normal order. Comments at identical timestamps retain their horizontal position and use small vertical offsets.
- The Windows resource manager resolved all 87 track-detail keys across three fa-IR/en-US/fa-IR passes. The render-graph probe also verified a paused endpoint rebuild preserves the two-second position and instrumental-only gains.

## Device volume, local controls and linked music videos

- The output picker uses a compact WinUI acrylic flyout so the native radio choices and focus-engageable slider share one popup. The status next to the picker distinguishes a muted endpoint and a zero-volume endpoint. The slider changes the currently selected Windows render endpoint through Core Audio, coalesces drag updates, and restores sound when the user raises a muted output above zero. The track-details read scope reports volume and silence without exposing the endpoint identifier.
- Track-details and its transport control use local DevWinUI-derived OffsetBox, EdgeLighting, SelectorBarSegmented, WaveProgressBar and three cover lights. Source and retained MIT terms are in Controls/TrackDetails. The pin matches NuGet package 10.4.1, upstream 30a7bbb0b97271bb148cdee0cda04462214eaa27. The local wrappers release compositions and template listeners in their lifecycle.
- The compact music-video thumbnail is shown only for a track with an active linked video. The page uses the existing media API and native player, waits for media metadata, and releases its playback session on navigation. The canonical URI is mizon://music/{musicPublicId}?action=video. MCP navigation uses the existing signed-in destination permission and never starts playback. The database migration must precede deployment of the matching track-detail API. A linked record supplies the video and optional cover on a real environment.
- The completed change passed Debug/x64 and NativeAOT publication, 98 resource keys on three Windows MRT language passes, the track/MCP route and access checks, and the 301 existing gamepad input/session assertions. AudioGraph validated stem routing and position with generated WAV data. Core Audio read the real endpoint and rejected invalid or nonexistent writes with the system level unchanged. Packaged-page appearance, actual endpoint manipulation, real controller and Narrator output, and playback of a real linked video remain unverified.

The track details layout retains its original left-to-right control order in both app languages. Translated text and text reading order are independent of the layout.
