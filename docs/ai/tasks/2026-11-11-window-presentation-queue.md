# 2026-11-11: defer UI fullscreen presenter transitions

- Status: helper complete; main-window wiring belongs to integration owner
- Owner: Codex `music_home_contracts`
- Repository/branch: Mizon.Windows, `codex/window-transition-queue`
- Base: `bd514ec`
- Owned files: `Services/WindowPresentationTransition.cs`, AccessTests project/entry point/new transition tests, this note
- Related work: parent owns MainWindow fullscreen and HomePage crash fix

## Request and behavior

Move UI-triggered presenter changes outside WinUI input callbacks without changing
the existing independent MCP window contract. The pure UI-thread-owned helper
accepts a dispatcher enqueue callback, current-state getter, native setter and
failure reporter. The enqueue delegate must schedule a later dispatcher turn.

`Request(bool? enabled = null)` coalesces pending intents into one callback; null
toggles the pending target or current state. The latest explicit target wins.
`Set(bool)` cancels an older pending intent, applies synchronously and propagates
errors for immediate MCP readback. A reentrant direct Set is rejected explicitly.
Requests received during apply are queued only after apply returns. Failed enqueue
does not poison later requests, and failed apply is not automatically retried.
Dispose makes queued/UI work inert and rejects direct Set; the window owner must
dispose this helper before releasing native resources. No timers, background loops,
native resources, reflection or new MCP tools/scopes are introduced.

## Validation

`dotnet run --project Mcp/Mizon.Windows.Mcp.AccessTests -c Release` passed **215
assertions**, including 28 new assertions against the actual linked helper.
Coverage includes rapid double toggle, last-target coalescing, synchronous direct
override, idempotency, rejected/throwing enqueue, native and state-lookup failures,
reentrancy, next-turn requests during apply, disposal and late callbacks. Existing
production MCP permission/lifecycle and pipe checks also pass with synthetic data.

No real app, user consent, Windows UI automation, whole WinUI build or full-app
NativeAOT publication was performed. Integration owner will wire the actual window
and validate its build; headless checks do not establish native crash resolution.

## Publication

Commit only these owned changes. Parent integrates and publishes the combined
crash fix; this branch does not push or modify shared main independently.
