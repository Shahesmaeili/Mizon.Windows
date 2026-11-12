# Auth outcome haptics: session engine and navigation handoff

- Status: complete (engine subtask; page/MCP integration is owned separately)
- Owner/agent: Codex / haptics_design_review
- Date: 2026-11-12
- Repository and branch: Mizon.Windows, `codex/auth-result-haptics-engine`
- Base commit: `70049c6cde3b31e810fa1b055e62f930c31ec00e`
- Related task: root integration branch `codex/auth-result-haptics`
- Owned files: `Services/Gamepad/AuthGamepadSession.cs`, new
  `Services/Gamepad/AuthGamepadFeedback.cs`, `Mcp/Mizon.Windows.Gamepad.Tests/**`,
  and this note. No pages, view models, navigation or MCP contracts edited here.

## Request and acceptance criteria

Give actual authentication outcomes distinct optional controller feedback without
delaying successful navigation, retaining a departing page, vibrating an inactive
window, or overriding a user opt-out. Keep NativeAOT and lifecycle cleanup intact.

## Implementation and decisions

- Success is two gentle pulses over 260 ms, server/transport error is two firmer
  pulses over 360 ms, and validation is a quiet 100 ms tap. Smooth 25 ms edges and
  motor amplitudes at most 0.38 keep all three finite and bounded.
- The existing foreground input timer samples the pure envelope; no additional
  timer, async animation loop or static pending queue is introduced. Navigation
  taps cannot overwrite an outcome sequence.
- Feedback remains off by default. `PlayFeedback` returns whether a supported,
  active, connected and opted-in session actually accepted the native output;
  `LastFeedback` records only accepted outcomes, and `IsFeedbackPlaying` describes
  the outcome sequence (not a navigation tap).
- A successful login can create `AuthGamepadSuccessFeedback` using the current
  window as owner and pass it under `ParameterKey` to Welcome. The loaded, active
  Welcome session consumes it once within two seconds, for that same window and
  selected connected controller. It restores the captured enabled intent only
  when the recipient has not explicitly opted out. The source can be disposed
  immediately. Weak references and explicit disposal avoid retaining either owner.
- Opt-out, competing pointer/keyboard input, deactivation, unload, device removal
  and disposal stop both motors. Native failures are contained and never block
  authentication. Normal pages cannot replay an expired or consumed ticket.

## Validation

| Command / environment | Result | Limits |
| --- | --- | --- |
| `dotnet run --project Mcp/Mizon.Windows.Gamepad.Tests -c Release -p:NuGetAudit=false` | 289 assertions passed | Deterministic platform doubles; no Windows UI or real account |
| `dotnet publish Mcp/Mizon.Windows.Gamepad.Tests -c Release -r win-x64 -p:PublishAot=true -p:NuGetAudit=false` and run native executable | 289 assertions passed | Source-linked session/pattern NativeAOT; not full WinUI app publish |
| `git diff --check` | Passed | Whitespace validation only |

Cases cover each pattern, soft edges, motor bounds, completion, replacement,
navigation priority, default-off behavior, write failures, all cleanup boundaries,
and success handoff expiry/reuse/owner/controller/opt-out. Handoff after source
disposal is explicitly tested. Physical motor feel, hardware drivers, rendered
guide UI and Narrator require separate hardware/visual validation.

## Handoff

Root must connect only genuine `SubmitAsync` outcomes, keep cancellation silent,
dispose unconsumed payloads and remove the navigation parameter before retaining
history. Do not set a new Welcome session's feedback preference explicitly false
before consuming a valid handoff. Root owns scoped MCP state/integration tests and
the combined WinUI build. No backend/database or API contract change is required.

## Publication

Engine implementation and tests are published on `codex/auth-result-haptics-engine`
for normal merge by the root integration owner. The containing commit is identified
by this file's git history; no shared checkout or main history was reset.
