# 2026-11-12 Authentication controller MCP contracts and verification

- Status: complete (MCP subtask; coordinated UI integration remains with root)
- Owner/agent: Codex auth_gamepad_mcp subagent; root agent owns integration
- Date: 2026-11-12
- Repository and branch: Mizon.Windows, `codex/auth-gamepad-mcp`
- Base commit: `a6bce9a3e9320e5c5a6ddbe301e7fcaf423b1c04`
- Related task: authentication controller navigation and page keyboard/help
- Owned files/areas: `Mcp/**`, `Services/McpAuthenticationService.cs`, this note

## Request and acceptance criteria

Expose the changed authentication controller behavior through narrow, typed MCP
state/actions without OS input injection or authentication submit-scope bypass.
Keep reads gated and verify permission, cancellation, failure and page lifetime.

## Implementation and decisions

`McpAuthState.Gamepad` reports controller/navigation flags, focused control ID and
keyboard/help visibility. The active loaded auth page owns the snapshot.
`mizon_auth_gamepad_action` provides ten allowlisted page-local focus/keyboard/help
actions under existing authentication control consent. Accept/menu/submit and
text entry are excluded; existing form submit remains separately consented.

The adapter uses the root agent's `GetGamepadState` and
`ApplyGamepadActionAsync(McpAuthGamepadAction, CancellationToken)` page hooks.
It rejects unloaded/offscreen/busy pages and invalidates results after navigation
or unload. Existing access-service redaction, mutation serialization, sanitized
errors and authorization cancellation apply without a parallel business path.
See [the contract](../../../Mcp/AUTH_GAMEPAD.md).

## Validation

| Command / environment | Result | Limits |
| --- | --- | --- |
| `dotnet run --project Mcp/Mizon.Windows.Mcp.Tests -c Release --no-restore -- --isolated` | 49/49 groups passed | Synthetic SDK/stdio/pipe only |
| `dotnet run --project Mcp/Mizon.Windows.Mcp.AccessTests -c Release --no-restore` | 288 assertions passed | Actual permission service with synthetic consent/auth state |
| `dotnet run --project Mcp/Mizon.Windows.Mcp.AuthTests -c Release --no-restore` | 42 assertions passed | Production auth adapter with synthetic pages |
| `dotnet publish Mcp/Mizon.Windows.Mcp/Mizon.Windows.Mcp.csproj -c Release -r win-x64 -p:PublishAot=true -p:NuGetAudit=false`; protocol fixture `--catalog` against published exe | NativeAOT publish passed; actual catalog 1/1 passed | Does not validate whole WinUI app |
| Full WinUI integration build, controller hardware, visuals, animations, Narrator | Root integration/manual checks pending | No Windows UI automation used |

Fresh restore initially stalled. Local verification disabled CLI workload-update
notifications and used `NuGetAudit=false` for restore; no project/repository setting
was changed. These runs do not establish a package vulnerability audit.

## Handoff

Root must integrate the page hooks and controller session, compile the combined
WinUI app, and update the main handoff. No backend or SDK change is needed.
No live consent or real account/form was used.

## Publication

Task-owned commit is published on `codex/auth-gamepad-mcp` and handed to the root
integration agent. Root publishes the combined task to the intended integration
branch and verifies its remote SHA.
