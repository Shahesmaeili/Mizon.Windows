# Stretch music-home artwork

- Owner: Codex root; read-only review by widget agent
- Date: 2026-11-11
- Branch: `codex/music-home-artwork-stretch`
- Base: `57d636be082f749ff7ebee1a9b3ea9f3de05d272`
- Owned files: `DiscoveryFeatureHero.xaml`, `MusicStoriesPanel.xaml`, this note

The user requested stretching the newly replaced music-home artwork. Set the
featured hero artwork and story-card preview to `Stretch=Fill` so the full image
uses both dimensions of its frame, including when the source aspect ratio differs.
`FeatureArtwork` already forwards this property to its image. No additional image
processing, decoding, animation, lifetime or business behavior is introduced.

Scope is the home-page images from the preceding artwork request. Profile avatars,
video rendering, full-screen story viewer/tag coordinates and shadow/clipping
ownership retain their existing behavior.

This is visual-only; existing scoped music-home MCP contracts/checks are reused.
Validation: existing access integration suite passed 134 assertions; VS MSBuild
WinUI Debug x64 build passed with existing warnings. Local logs:
`D:/MizonTaskDiagnostics/MusicHome/stretch-mcp-check.log` and `stretch-build.log`.
No Windows UI automation or visual screenshot comparison. Reviewed change is
committed and published through the normal main-branch fast-forward workflow.
