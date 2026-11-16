# Featured artist status after a successful load

- Owner: Codex, branch `codex/fix-featured-artist-status`, base `b30073b`.
- Owned path: `ViewModels/FeaturedArtistsViewModel.cs`.

The packaged WinUI crash log identified the compiled MusicPage binding's
read of `StatusText` after loading featured artists. A populated successful
result passed an empty key to the localization service. Loading and populated
success now return empty display text directly. Empty catalogs and failed
requests still use their Persian and English resource keys, including a failed
refresh with retained cards. The same state remains available to the existing
music-home MCP reader.

A headless probe reproduced the original exception and exercised both actual
resource files with the corrected property. It also ran as a NativeAOT console
executable. The music-home MCP permission regression suite passed.
