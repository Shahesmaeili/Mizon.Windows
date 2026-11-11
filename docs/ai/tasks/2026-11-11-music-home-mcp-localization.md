# Music home live localization and scoped MCP

- Status: complete; reviewed source integrated and published to main
- Owner: Codex root, isolated contracts/center/widget agents; separate worktrees
- Date: 2026-11-11
- Repository/branch: Mizon.Windows, `codex/music-home-mcp-i18n`
- Base: `799d8a4163ff0a392fe0f03483812c2f0ca5e83e`
- Related SDK: unchanged `dd8860e`; .NET 11 / existing WinUI package versions
- Related server: `f939cb1ce2a964eef62a62a094c81ce95de42c20`, published main

## Scope and result

`MusicPage` is the music home; `MusicsPage` remains music details. Added live
Persian/English center and widget labels, API/demo presentation formatters, popups,
story controls/errors, profile-preview captions, fonts/direction and Narrator labels.
Only application-owned text/preferred translated titles change; names, comments,
media, card identity, selection and existing playback are preserved. Language changes
do not reload APIs or decode thumbnails. Subscriptions detach on unload/close.

Added `mizon_get_music_home`, `mizon_refresh_music_home`, `mizon_music_home_action`
with independent persisted read/control/submit consent. The adapter uses the active
cached page and its actual service/control behavior. State is bounded to 20 items
per section and distinguishes API/demo/mixed content, loading/errors, current story,
player, news, artist/day selection and dialogs. It contains no media paths/tokens.
Control-only responses redact state. Likes/follows require additional submit consent.
Demo following stays demo; unsupported demo playback reports unavailable. Existing
genres intentionally have no navigation, per the earlier user instruction.

Refreshes/actions honor consent/caller/page lifetime. An explicit play transfers
ownership to the existing music player/playlist queue; source-page unloading cannot
cancel that intentional handoff, while unrelated departure still cancels pending work.
Playback failures, dialog conflicts and clipboard/save outcomes are not reported as
successful playback/actions. Following-story and regular-story presenters share the
same observable state and controls. Story tag navigation is awaited and cancellation
checked before navigation. No arbitrary SQL, URI, file path or service tools were added.

Latest steering also replaced unsuitable story/hero artwork in the server database.
The server task records 12 stories +4 featured entries, 18 dedicated media records,
licensed concert/studio/instrument pictures and six silent animated-photo loops.
Real music covers/profile pictures/original media were preserved. This is not a new
public API, schema migration or automatic startup seed.

## Verification

| Check | Result |
| --- | --- |
| VS MSBuild main WinUI Debug x64 | Passed, existing warnings remain |
| Actual SDK/stdio/isolated pipe tests | 35/35 groups |
| Production access service + real home adapter / synthetic page | 134 assertions |
| Published NativeAOT MCP host catalog/schema validation | 1/1 |
| Production presentation models/resources, managed | 750 checks |
| Same presentation suite, published NativeAOT win-x64 | 750 checks |
| Paired resource keys/format arguments | MusicHome/MusicWidgets/MusicSurface match |
| Git whitespace check | Passed |

The final build caught nested test `.resw` outputs entering MRT's recursive scan
and conflicting with application resources. The main project excludes those test
resources in `DefaultItemExcludes`; evaluated PRI inputs now contain only the 14
application resource files. Running localization tests before a normal app build
is supported, without deleting build folders or changing SDK installation files.

Evidence under `D:/MizonTaskDiagnostics/MusicHome/`: `access-tests-final.log`,
`protocol-final.log`, `host-aot-final.log`, `native-catalog-final.log`,
`localization-final.log`, `localization-aot-final.log`, `localization-aot-run.log`,
`winui-clean-resources-build.log`, `pri-items.json`. These are local diagnostic
outputs, not repository dependencies. Server artwork evidence is recorded separately.

No Windows UI automation, live-user consent, real MCP form/like/follow submission,
hardware audio test or screenshot comparison was performed. Full WinUI NativeAOT
publication and actual Narrator rendering remain unverified. Existing ViewModelBase
reflection warning is pre-existing; the new typed paths use explicit/source-generated
contracts. The user must approve the new domain scopes inside the updated app.

## Handoff

See `Mcp/MUSIC_HOME.md` for action/section/input semantics and repeatable check
commands. Main-checkout integration uses fast-forward only; do not reset concurrent
work. Publish reviewed task-owned commits and verify remote SHA. No database/schema
or shared SDK source changes are needed beyond the related server artwork commit.
