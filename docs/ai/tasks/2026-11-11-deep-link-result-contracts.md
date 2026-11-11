# 2026-11-11: typed deep-link results and MCP receipts

- Status: shared contract/MCP work complete; native integration owned by parent
- Owner: Codex `music_home_contracts`
- Repository/branch: Mizon.Windows, `codex/deep-link-result-contracts`
- Base: `3b75ffc`
- Owned: `Services/AppDeepLinkResult.cs`, MCP navigation adapter/redaction,
  contracts/schema/descriptions, related protocol/access tests and this note
- Parent owns native protocol launch/results, Program/App/Home/route readiness,
  taskbar behavior and `AppDeepLinkNavigationContracts.cs`

## Contract and implementation

The shared source-only result contract has version 1, a request UUID, success,
stable code, sanitized message and optional typed destination data. Data includes
canonical URI, public/comment IDs, settings section and opened/completed stage.
No arbitrary object/dictionary payload, file path or WinRT dependency is introduced.
The source-generated parser rejects missing/unknown/duplicate fields, wrong types,
invalid stage/version/correlation, malformed text and responses over 16 KiB UTF-8.
Serialization validates the same fields. Producer messages must already be sanitized.

MCP reuses its existing navigation scopes and allowlisted routes. Its optional
`navigationResult` maps an actual route outcome and uses a fresh request UUID;
it does not infer readiness from current page state. Navigation read permission
is required for the receipt as well as the navigation snapshot, independently of
control or other read grants. Results are per invocation and not retained by
`get_navigation`. Cancellation, revoke, disposal and invalid data suppress stale
results. Playback/download actions remain excluded from navigation MCP.

## Integration dependency

Parent must extend the existing contract exactly as agreed:

```csharp
internal sealed record AppNavigationOutcome(
    bool Success, string Code, string Message, AppDeepLinkResultData? Data = null);
```

This extension was mirrored locally for compile checks but is deliberately excluded
from this commit because the parent owns that source file. All other test inputs
are synthetic. Native sender validation and actual route completion belong to the
parent's transport/UI work, not to this DTO.

## Validation

| Check | Result |
| --- | --- |
| Release AccessTests | 227 assertions passed using production permission service/adapter and isolated synthetic pipe/store |
| Release protocol suite, `--isolated` | 44/44 groups passed, including real MCP SDK/stdio/pipe receipt success/failure |
| Protocol suite NativeAOT `win-x64` publish | Passed |
| Published native protocol suite, `--isolated` | 44/44 groups passed; includes shared strict JSON parsing and generated metadata |
| Git diff checks | Passed |

Logs: `D:/MizonTaskDiagnostics/DeepLinkResults/` (not committed). No Windows UI,
real consent, account writes, external protocol launch, full WinUI build or full
application NativeAOT publication was performed. These checks do not prove native
launcher delivery or the taskbar spinner's visual behavior.

## Publication

Parent integrates and publishes the combined task. This branch commits only its
owned changes; no shared-main push or unrelated source staging is performed.
