# Live localization for the five music-home widgets

- Status: complete; integrated WinUI build is owned by the parent task
- Owner: Codex music_home_widgets
- Date: 2026-11-11
- Repository/branch: Mizon.Windows, `codex/music-home-widgets`
- Base: `799d8a4163ff0a392fe0f03483812c2f0ca5e83e`
- Owned areas: library, weekly rhythm, news, trending and following widget controls;
  their presentation models; shared playlist menu; MusicWidgets resources and
  isolated presentation-model localization checks
- Related task: music-home MCP, main-page localization and integration on
  `codex/music-home-mcp-i18n`

## Behavior

Both resource maps contain matching Persian/English strings for captions, empty
and error states, menus, sample news, counters, dates, tooltips and Narrator text.
Compiled bindings use the existing language service and its revision. Text font,
reading direction and alignment update live. Directional actions, news navigation
and weekly/day indicators follow the chosen language.

Language changes recompute presentation without fetching the API again, replacing
collections, resetting selection or decoding artwork. User playlist titles,
comments and profile names remain user content. Music titles choose the existing
localized API field where available. News articles remain explicitly sample data.
The news detail dialog also updates while open.

Control subscriptions attach while loaded and detach when unloaded. The shared
playlist flyout watches language only while open. Existing renderers, animation,
viewport gating, timers, responsive geometry, image sizing and disposal remain.

## Validation

- Production-linked headless localization suite: **300 checks passed**.
- The same suite published as **NativeAOT win-x64** and passed all 300 checks.
- Tests cover paired resource keys/format arguments, Persian-English round trips,
  chart/sample/selection identities, truthful sample news, activity target IDs,
  preserved user comments, music-video terminology and retained decoded images.
- `git diff --check` passed.

The test uses the real following DTO from the sibling SDK source. WinUI image
primitives and MRT resource lookup are test doubles; no real application, API,
account, consent or database operation occurs. This does not prove WinUI/MRT
packaging, native visual layout, actual Narrator output or animation performance.
The existing reflective `ViewModelBase.SetProperty` emits IL2075 in the linked
suite; the new code uses direct properties and notifications, never that method.

Commands from this isolated worktree included
`dotnet run --project Mcp/Mizon.Windows.MusicHome.LocalizationTests -c Release`
and `dotnet publish` with `-r win-x64 -p:PublishAot=true`, both with the existing
SDK source root supplied as `MizonApiSourceRoot`. Diagnostics remain outside Git.

## Handoff

The main localization branch supplies `DiscoveryMusicItemViewModel.Localization`
and `RefreshLocalization`, used by the trending widget. Integrate that branch
before the full WinUI build. Root owns new MCP scopes/adapters/tests and final
commit publication; this subtask neither grants MCP consent nor changes contracts.
