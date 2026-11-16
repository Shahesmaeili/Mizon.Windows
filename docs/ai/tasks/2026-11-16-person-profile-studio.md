# Full-width artist profiles

- Owner: Codex
- Branch: `codex/person-profile-studio`, base `0c5c03a`
- Dependency: Mizon.Server.API `0193391` supplies the optional local fixtures.
- Owns: artist spotlight portrait, profile layout, and portrait transition.

The profile fills the content area of the existing window. It keeps a single
scrolling, virtualized discography, and adjusts the cover, portrait, and model
stage at narrow, medium, and wide sizes. The layout stacks the model below
the identity on narrow windows.

The music spotlight has a circular, focusable portrait immediately to the
right of the artist name. Clicking it or activating it with the controller
opens the same artist route as the profile button. WinUI Connected Animation
uses the visible portrait as the source. The destination holds that image
until the transition finishes, with the full portrait ready to replace it.
Navigation from a deep link or MCP opens directly. Reduced motion also opens
directly. Leaving the page drops the pending transition and image.

The route and existing artist MCP read/control permissions are unchanged.
Debug x64 built and Release x64 published with NativeAOT. The isolated MCP
suite passed 311 checks, including artist permission boundaries, revocation,
and restart. The source-linked navigator passed 70 checks with a synthetic
visual tree. The ten local fixtures were checked through the production
services, and the app's GLB loader parsed all ten sample models. Live
animation, Narrator, and physical Xbox controller input were not exercised.
