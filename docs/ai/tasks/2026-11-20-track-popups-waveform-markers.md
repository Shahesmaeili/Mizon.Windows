# Track popups, waveform markers and taskbar presence

- Owner: Codex
- Base: `72c717b`
- Owns: the track flyout styling and waveform comment preview, its Track details
  MCP adapter, the reaction row, and the taskbar presence panel spacing and indicator.

The live-listener and view-history flyout uses the header profile menu’s
in-tree Desktop Acrylic and menu outline. The shared presenter attaches the
material after loading and releases it on close. Its themed fallback stays
visible when the native material cannot be attached. The waveform marker opens
a shared flyout anchored to the avatar. The icon goes through the existing
comment resolver after the flyout closes, so comments outside the initial
listing can still be focused. The marker has no playback command. Both stroke
colors are softened and the first marker starts six DIPs below the seek lane.
The icon receives initial focus, and Gamepad B or Escape closes the bubble.
Leaving the track or detaching the marker drops the preview state.

The existing Track details MCP read permission returns up to 64 loaded marker
IDs and the open ID. Track details control can select an available marker or
close the bubble. The existing scoped `music-comment` navigation opens the exact
comment in its list.

The taskbar presence box reserves a six-DIP top gap. The progress ring is
visible only during connection or reconnection. A brief green glow accompanies
count changes, using the same animation timer and respecting reduced motion.
The API’s local Development launch profiles supply random counts for review
(`Mizon.Server.API` commit `d175b5c`).

The playback output and other social actions share the existing measured panel
with the reaction rail. It calculates the breakpoint from both clusters and
keeps horizontal scroll on the lower rail at narrow widths. Language changes,
new reactions and count-width changes participate in normal WinUI measurement.

Verification used the actual panel in an offscreen WinUI fixture. It checked
one unit below and at the measured width in three cases, plus a narrow viewport.
The acrylic follow-up used a native offscreen flyout with the production
presenter and reopened it in Light and Dark. It checked that the rounded
backdrop was attached on open, the fallback resolved in each theme, and the
native material was released on close.
Visual Studio MSBuild built Debug x64 and published Release x64 NativeAOT.
Existing WinUI evaluation and trimming warnings remain.
The NativeAOT preview self-test passed in Persian and English, including
connection-only progress, the green pulse and numerical easing. Isolated MCP
access passed 358 assertions, the companion passed all 49 protocol groups,
and the controller input suite passed 301 assertions. Live window visuals,
physical controllers and audible Narrator behavior were not exercised.
