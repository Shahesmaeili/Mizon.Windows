# Deep-link result completion and cancellation lifetime

- Status: complete (helper implementation; integrated app verification belongs to the parent task)
- Owner/agent: Codex `/root/deep_link_lifecycle`
- Date: 2026-11-11
- Repository and branch: `Mizon.Windows`, `codex/deep-link-result-lifecycle`
- Base commit: `3b75ffcb0bcf508069b9b5d615031791ff94ca0e`
- Related tasks: shared result contract/MCP adapter, native activation receiver, taskbar caller/loading ring
- Owned files: `Services/AppDeepLinkResponseSession.cs`, `Services/AppDeepLinkResultDispatcher.cs`, `WindowsSearch/Mizon.Windows.DeepLinkResults.Tests/`, this note

## Request and acceptance criteria

Give each response-capable deep link exactly one correlated result. A caller must
receive failure on invalid routes, exceptions, timeout, replacement or app closure;
a delayed operation must not overwrite an earlier failure. Keep existing app
navigation as the behavior owner and keep the lifetime helpers compatible with
NativeAOT.

## Implementation and decisions

- `AppDeepLinkResponseSession` owns a 50-second default deadline and one report
  callback. It releases the callback/timer on completion, validates result metadata,
  and serializes cancellation/disposal races. Cancellation callback exceptions or
  disconnected report callbacks cannot escape a timer or shutdown path.
- `AppDeepLinkResultDispatcher` invokes the shared `IAppDeepLinkNavigation` on the
  caller's app dispatcher. New requests cancel old ones; ordinary activation can
  call `CancelActive`. It does not dispose the shared navigation service.
- Timeout/replacement/closure report without waiting for a navigation task that
  ignores cancellation. The awaited operation still owns disposal until it unwinds,
  and cannot subsequently report success.
- Response messages are fixed and sanitized; raw exceptions are never returned.
- Main/native receiver wiring and the taskbar's transport/loading UI are owned by
  the parent and sibling agents, not implemented by these pure helpers.

## Validation

| Command / environment | Result | Limits |
| --- | --- | --- |
| `dotnet run --project WindowsSearch/Mizon.Windows.DeepLinkResults.Tests -c Release` | 40 assertions passed | Synthetic destinations, no account writes or UI |
| `dotnet publish ... -c Release -r win-x64 -p:PublishAot=true`, then native executable | 40 assertions passed | Proves these production helpers/shared generated JSON, not the full WinUI app |
| `git diff --check` | Passed | Source whitespace check |

The suite covers actual outcome/metadata forwarding, invalid routes, cancellation,
timeout while navigation is blocked, supersession, duplicate activation, late
completion, app closure, disposal reentry, callback exceptions, concurrent
completion, shared-service ownership, and generated JSON transport without
reflection. Native Windows protocol-for-results, single-instance forwarding,
taskbar HWND behavior and the visual loading ring are not exercised here.

## Handoff and publication

This commit depends on the sibling shared `Services/AppDeepLinkResult.cs` contract
and the parent-owned `AppNavigationOutcome.Data` extension. Both were mirrored
locally for compilation but deliberately excluded from this commit. Parent agent
owns integration, final app build and publication; no main-branch push was made by
this agent.
