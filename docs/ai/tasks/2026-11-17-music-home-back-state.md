# Music Home return state

- Owner: Codex
- Branch: `codex/music-home-back-state`, base `a907976`
- Owns: the music landing page's lifetime, viewport restoration and child
  presentation on reattachment.

The signed-in shell retains one Music Home page with its bounded, loaded
widget data. Back and forward reuse the page; any first-load request already
in flight can finish once. Leaving Music Home detaches bindings and input
handlers, stops visual work and closes popups. Video players and native
model scenes are released by their controls on unload. Leaving the signed-in
shell cancels loading, clears account data and artwork, and resets offsets.

Before navigation, Music Home captures its three vertical viewports and
the horizontal positions of stories, top artists and spotlight tracks.
It applies the offsets to the measured content without scroll animation,
clamping them to the current layout. It restores prior keyboard/controller
focus if the original control is still visible in those viewports.

The retained spotlight selection keeps its model path and loaded artwork.
Reattaching rebuilds the native scene without replaying its entrance. The
hero keeps the displayed bitmap while its media session is closed. An
explicit refresh still fetches data. A confirmed playlist change refreshes
only the library widget, and follow changes from a person page update the
matching artist item in place.

Reuses existing Music Home and person profile MCP scopes and UI actions.
An offscreen WinUI Frame probe passed eight round trips, explicitly clearing
the viewport positions while away to exercise restoration. It checked all six
offsets, a reduced scroll extent and a session reset. Debug x64, 311 isolated
MCP assertions and 301 controller assertions passed. The packaged app's
visual timing, Narrator and a physical controller were not exercised.
