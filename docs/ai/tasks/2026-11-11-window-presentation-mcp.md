# 2026-11-11 — Scoped main-window presentation tools

- Status: component complete; root owns native window implementation/integration.
- Owner: Codex `music_home_contracts`.
- Branch/base: `codex/window-presentation-mcp`, `ed0ac2b`.
- Owned: MCP contracts/host/access service/consent strings/tests, window adapter and
  interface, plus the narrow `App.xaml.cs` service binding.
- Not owned: MainWindow presentation/toolbar, HomePage title-bar layout, toolbar
  resources or shared handoff. No database, server or SDK changes in this task.

## Contract and behavior

Adds independent persisted Window read/control consent, default denied for all
existing grants. `mizon_get_window_state` returns only availability, fullscreen and
fullscreen-control visibility. `mizon_set_fullscreen` accepts a required boolean
desired state and uses the same `IWindowPresentationHost.SetFullScreen` as UI.
It does not inherit appearance or music permissions and exposes no close/minimize,
focus, geometry, arbitrary HWND or hover action. Control-only results are redacted.

Production `McpWindowPresentationService` runs on the existing authorized UI
dispatcher; closed/disposed hosts cannot supply stale state or receive mutations.
Cancellation, generation/revocation checks, serialization and sanitized failures
reuse the existing MCP access flow. The adapter never owns/disposes the main window.
Persian/English consent labels and Narrator descriptions match the new bounded scope.

See [MCP contract](../../../Mcp/WINDOW_PRESENTATION.md).

## Verification

| Check | Result | Limits |
| --- | --- | --- |
| `dotnet run --project Mcp/Mizon.Windows.Mcp.AccessTests -c Release` | 187 assertions passed | Production access service/adapter, synthetic window and isolated pipes/consent store. |
| `dotnet run --project Mcp/Mizon.Windows.Mcp.Tests -c Release -- --isolated` | 41/41 groups passed | Actual MCP SDK dispatch, strict schema/parser, generated framing and stored-grant checks. |
| `dotnet publish Mcp/Mizon.Windows.Mcp -c Release -r win-x64 -p:PublishAot=true` then test runner `--catalog <native host>` | Publish and 1/1 native catalog group passed | No actual app launch or real permission approval. |
| Consent XAML/resources XML parse, locale key parity and duplicate check; `git diff --check` | Passed | Root owns final WinUI XAML compilation and visual checks. |

New checks cover pending/older grants, independent read/control, redaction,
idempotent desired-state operations, invalid payloads, sanitized failure, window
closure, pre/during-operation cancellation, revocation, disable/re-enable, exact
restart restoration, offline revocation and adapter disposal. Tools discovery and
invalid input never open the application or create permission requests.

## Handoff/publication

Root must integrate its MainWindow implementation of `IWindowPresentationHost`
before building the main app: the binding intentionally references that interface.
No whole WinUI build, Windows UI automation, live Narrator or top-edge hover check
was performed in this worktree. Root integrates the task-owned commit and publishes
with the native window/layout change; this component does not claim deployment.
