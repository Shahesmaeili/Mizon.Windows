# Artist profile in WinUI

- Owner: Codex
- Branch: `codex/person-profile`, base `7607751`
- Dependencies: Database `a377c8a`, API `eb8fa48`, SDK `0181f14`
- Owns: profile page and view model, music contributor entry point, person route,
  artist model input/localization, consent contracts and verification

Open a real Person by selecting the contributor in music details or using
`mizon://person/{personPublicId}`. Discovery artists with real IDs use the
same navigation; legacy demo cards keep their preview. The single virtualized
track list uses a stable loading skeleton and 24-track server pages. Each
identity projection includes profile artwork, cover artwork and optional GLB
media IDs. Images decode at display-appropriate sizes; recycled row requests
and page downloads are cancelled when their owners leave. The existing model
viewer handles GLB parsing, spring motion, reduced motion and native-resource
cleanup. Missing media fall back to initials, artwork tint or a localized
model placeholder.

The profile applies the configured model yaw from the Persons API before it
loads the GLB. This also sets the pose used by Reset and spring return. The
3D surface uses the same physical axes in both app languages. The page's text
and layout continue to follow the selected language.
This depends on server `81b82d2` and SDK `e6db3aa`. Debug x64, the isolated MCP
access and gamepad checks, and an offscreen WinUI probe using the Moein GLB
passed. The probe compared the initial pose, reset and language direction
changes. The packaged page and physical controller were not exercised.

Use D-pad/left stick to move, A for native controls and tracks, B to go back,
and the right stick to rotate the focused model. Narrator names, headings,
localized status text and default focus visuals share the live Persian/English
language service. Following an artist applies immediately and updates the count
from the server. A missing or private linked account shows no outgoing count.

The active profile exposes bounded MCP read and control. Tools show only page
state and up to 48 recently loaded tracks with read consent, and can page,
open an already loaded track or set desired follow state with control consent.
Navigation consent only opens the route. See [the tool guide](../../../Mcp/DEVELOPMENT.md).

Verification: WinUI Debug x64, 613 activation checks, 307 isolated MCP access
assertions including restart/cancellation, 49/49 MCP protocol groups and 301
gamepad checks. The SDK published and ran under win-x64 NativeAOT with JSON
reflection off. The full Windows Release win-x64 NativeAOT publish also produced
an executable. Global trim/interop warnings remain in DevWinUI, SkiaSharp and
the existing audio endpoint reader; no profile-specific warning was reported.
Execution of the published app, visual, Narrator and hardware checks remain.
No live migration, artist data or media registration was performed.
