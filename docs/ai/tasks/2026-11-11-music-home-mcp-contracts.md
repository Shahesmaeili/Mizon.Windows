# Music home MCP protocol and consent

- Status: complete (protocol/consent subtask; app adapter integration belongs to the parent task)
- Owner: Codex music_home_contracts
- Date: 2026-11-11
- Repository/branch: Mizon.Windows, `codex/music-home-mcp-contracts`
- Base: `799d8a4163ff0a392fe0f03483812c2f0ca5e83e`
- Owned areas: MCP contracts, host and protocol tests; access-service routing,
  connection view model and consent card; Persian/English MCP resources
- Parent integration: `codex/music-home-mcp-i18n`

## Implementation

Added three explicit tools: `mizon_get_music_home`, `mizon_refresh_music_home`,
and `mizon_music_home_action`. Typed inputs, bounded page/card/player/story outputs,
source-generated JSON and schema discovery share the existing SDK/stdio/pipe path.
The shared `McpMusicHomeContract` validates action/section combinations, required
parameters, limits, finite seek/volume values and submission-vs-control routing.
The page adapter additionally checks current items and availability.

New `AllowMusicHomeRead`, `AllowMusicHomeControl` and `AllowMusicHomeSubmit` grants
are independent and default denied. Like/follow requests use `music_home_submit`
and require both control and submit. Previously approved identities do not gain
new access. Exact permissions persist under the existing protected pairing proof;
revocation and authorization-generation cancellation stay unchanged. Control-only
responses have music-home snapshots removed by the app access boundary.

The native consent card describes the scopes in both languages, including normal
play/view activity recording, file-picker interaction and currently demo-only
follow behavior. No arbitrary paths, URLs, SQL, backend invocation or self-approval
tools were added. Runtime writes still go through the live page adapter.

## Validation

| Check | Result |
| --- | --- |
| `dotnet run --project Mcp/Mizon.Windows.Mcp.Tests -c Release -- --isolated` | 35/35 groups passed |
| Actual SDK/stdio/isolated pipe | Typed music requests preserved; pending access denied; invalid combinations rejected before pipe; one connection reused |
| Scope/persistence checks | Read/control/submit independence; legacy default denial; exact restart restoration; revoked grants stay removed |
| Generated output and framing | Unicode bounded card arrays and fractional playback times round-trip; nested array items conform to schema; no media paths/credentials in DTOs |
| `dotnet publish Mcp/Mizon.Windows.Mcp -c Release -r win-x64 -p:PublishAot=true -p:SelfContained=true` | Passed without warnings |
| Published native host `--catalog` verification | 1/1 group passed; 17 tools, generated schemas and invalid-input behavior verified |
| Resource XML | Persian/English keys match; no duplicate keys |

Evidence stays outside Git in `D:/MizonTaskDiagnostics/MusicHome/contracts-*.log`.
No app launch, live permission approval, real forms, backend writes, UI automation
or main WinUI NativeAOT publication was performed by this subtask.

## Handoff

The parent supplies `IMcpMusicHomeService`, the live-page adapter and DI/startup
wiring, adds actual access-service/adapter integration cases, and verifies the
combined WinUI build. `McpAccessService.Start` accepts `musicHome` as its final
optional parameter. The parent integrates this commit and publishes the final
feature; this branch does not independently deploy or publish an incomplete app.
