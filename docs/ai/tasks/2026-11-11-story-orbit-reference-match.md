# Match the three floating editorial collages to the supplied reference

- Status: complete
- Owner: Codex root; read-only source/layout review by orbit_reference_review and orbit_photo_source
- Date: 2026-11-11
- Repository/branch: Mizon.Windows, codex/refine-story-orbit-layout
- Base commit: c2b45445ba9a664694806f74db222a579c2423fd
- Related task: 2026-11-11-story-orbit-showcase; no server/SDK/database change
- Owned files: StoryOrbitCard/Showcase, StoryOrbitMotion, six obsolete MusicHome
  resource keys in each language, three orbit artwork assets, Bodoni Moda font
  and license, packaging entry, source credits and this handoff.

## Request and acceptance criteria

Match the reference's three different silhouettes, picture crops, overlapping
white titles, edition numbers and star badges above the music-home stories.
Every layer should move slowly and independently. The user explicitly chose
the original images and English titles, then approved a similar left portrait
because the original could not be found. Preserve the surrounding glass UI,
responsive strip behavior, current music features and lifecycle/performance work.

## Implementation and decisions

- `Models / Illustration / Team`, `22 / 23 / 24`, and `5,0 / 4,5 / 4,8` match
  the supplied design content. These decorative English labels intentionally
  remain unchanged in Persian; unrelated localization and RTL are preserved.
- Authored separate cubic paths: crumpled left frame, lower/wider middle wedge,
  tall right portrait with a concave upper-right scoop. One path supplies native
  clipping, outline and shadow; no runtime XAML parsing or WebView.
- Original middle illustration and right portrait were downloaded and visually
  matched. The left asset is a new fictional generated portrait with comparable
  blue hair, olive coat and flowers, per the user's relaxed requirement. It is
  not presented as the original person/photo. Complete sources, rights notes and
  final built-in imagegen prompt are in `Assets/Discovery/SOURCES.md`.
- Bodoni Moda is a bundled high-contrast serif selected for the reference look;
  the author's exact font was not identified. The original font and OFL notice
  ship together; no new NuGet package was added.
- Titles overlay the photos with a soft native alpha shadow. Removed the old
  captions/gradient veil. Pictures remain unmodified files with native view crops
  and bounded decoding (800 / 1024 / 800 px); all are packaged, with no runtime
  network access for this decoration.
- Per-layer phase, speed and direction differ. Subtracting each initial phase
  keeps the first frame at its authored position without a starting jump. All
  sine excursions are bounded and driven by Composition rather than UI timers.
- Existing viewport, ancestor visibility, minimized window and Windows reduced
  motion handling remain. Unload detaches masks/clips/visuals, releases owned
  animation objects, unsubscribes observers and clears image sources. XAML-owned
  alpha masks/visuals are never disposed by this control.
- Decorative content stays out of the Narrator focus tree. There is no new
  data/action behavior: reuse the existing independently consented music-home
  MCP surface, without cosmetic-only tools or tests.

## Validation

| Check | Result | Limits |
| --- | --- | --- |
| Visual Studio 18 Insiders MSBuild, Debug x64, .NET 11 | Passed after all code/XAML/asset changes | Existing preview/nullability/trim warnings remain; no deployment/full-app AOT publish |
| `dotnet run --project Mcp/Mizon.Windows.Mcp.Tests -c Release -- --isolated` | 47/47 groups passed | Synthetic consent/IPC; no real app/account |
| `dotnet run --project Mcp/Mizon.Windows.Mcp.AccessTests -c Release --no-launch-profile` | 276 assertions passed | Production adapters with isolated stores; no UI/self-approval |
| Independent code review | Fixed obsolete bindings, Team proportions/fallback and font line bounds | Not a live visual test |

All three packaged images, font and license were checked against source SHA-256; matching output copies and PNG/JPEG/font signatures were verified. Only whitespace formatting changed after the final successful build.

Actual WinUI layout, optical crop/font matching, GPU frame pacing, Narrator speech
and DPI/resize animation remain unverified. No Windows UI automation or restart
of the user's Visual Studio/session was performed. The result is not claimed to
be pixel-identical: the left photo is intentionally similar, and typography uses
a selected open font. Check the music home above its story row on the next run.

## Publication

Task-owned reviewed files only. The normal integration checkout must be clean
before a fast-forward; do not overwrite another contributor's changes. Remote
commit verification is recorded by the delivering turn; this note's commit is
identified by Git history without embedding a self-referential hash.
