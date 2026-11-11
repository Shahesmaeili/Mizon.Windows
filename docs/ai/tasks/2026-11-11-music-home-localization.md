# Live music-home localization

- Status: implemented; combined WinUI/MCP validation belongs to the integration owner
- Owner: music_home_localization
- Date: 2026-11-11
- Repository/branch: Mizon.Windows, `codex/music-home-localization`
- Base: `799d8a4163ff0a392fe0f03483812c2f0ca5e83e`
- Scope: MusicPage, center home controls, discovery/featured/home item view models,
  `Strings/fa-IR/MusicHome.resw` and `Strings/en-US/MusicHome.resw`
- Shared dependencies: root supplies MusicPage MCP lifetime methods/dialog field;
  widget localization and MCP transport/consent are separate task-owned changes.

## Implementation

The existing LocalizationService and revision-based compiled bindings supply 168
paired Persian/English resources: page/section headings, commands, media controls,
tooltips, Narrator metadata, empty/error/loading states and preview disclosures.
Text follows the selected language/font; physical media/three-column geometry and
existing artwork, animations, interactions and backend data remain unchanged.

Discovery/artist/story/genre/featured item properties refresh in place. Existing
sample names and backend content remain content, with language-specific track and
genre titles chosen where both already exist. Transient statuses retain resource
keys, allowing translation of already-visible failures without a network reload.
Hero text refresh does not replace its decoder; spotlight text refresh does not
reset the model angle, selection or track scroll. Localization subscriptions and
compiled binding tracking are detached on unload and restored on load.

Home widget refresh accepts linked caller cancellation, waits for both independent
requests to drain and clears its loading state. The featured media save handler
now wraps one awaitable `SaveCurrentAsync` implementation shared with the MCP
partial, propagating cancellation through download, picker and file operations.
Normal UI catches errors/cancellation; no alternate export implementation exists.

## Validation and boundaries

- All nine changed XAML documents parse as XML.
- Both resource maps have matching keys and matching format placeholders.
- All referenced MusicHome resource keys resolve; `git diff --check` passes.
- No application execution, UI automation, real account write or media export was
  performed. Combined WinUI build, MCP tests and NativeAOT checks run centrally
  after merging the disjoint task branches.
- The root owns the story-viewer/profile-popup localization and MCP adapter.
  The five side widgets have an independent MusicWidgets resource map.

## Handoff

Integrate this commit with the root's `MusicPage.Mcp.cs` before compilation:
`ActivateMcpHome`, `DeactivateMcpHome` and `_mcpPreviewDialog` are deliberate
cross-partial integration points. Build and run the combined checks before
publishing. Changes are task-owned; no backend contract/schema changes are needed.
