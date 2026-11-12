# 2026-11-12 Authentication controller input core

- Status: complete; parent task owns integrated UI/MCP delivery
- Owner/agent: Codex auth_gamepad_input
- Date: 2026-11-12
- Repository and branch: Mizon.Windows / codex/auth-gamepad-input
- Base commit: a6bce9a
- Related task: authentication gamepad UI/MCP integration
- Owned files: new Services/Gamepad/*, Mcp/Mizon.Windows.Gamepad.Tests/* and this note

## Request and acceptance criteria

Provide a reusable Xbox controller session for Welcome/Login/Registration with
bounded polling, reliable button navigation/entry commands and complete cleanup.
No Windows UI automation or real authentication submission is authorized here.

## Implementation and decisions

The page owns an AuthGamepadSession and supplies foreground/page/minimized status
through SetActive. Root load/unload also gates its single 60 Hz dispatcher timer.
Disconnected and inactive pages do no polling. Controller arrival/removal events
are coalesced onto the dispatcher; selected-device read failure stops polling.
The first attached controller owns input; all attached devices are counted.

The pure reducer baselines held input on attachment/reactivation/replacement,
uses stick deadzones/dominant-axis hysteresis and trigger hysteresis, repeats only
direction/scroll commands, and emits at most one command per sample. No repeat
catch-up occurs after dispatcher stalls. A/Menu/other command buttons never repeat.
Mapped actions use existing page operations; pages and MCP own their authorization.

Preview key handlers suppress native duplicate Gamepad routing in the active
page subtree, including translated keys identified by OriginalKey. Keyboard and
pointer input leave gamepad mode. No global hooks or OS Guide/Share interception
are added. Optional haptics default off; deactivation/disposal immediately stops
the pulse and removes every native/UI/timer subscription.

## Validation

| Command / environment | Result | Limits |
| --- | --- | --- |
| dotnet run --project Mcp/Mizon.Windows.Gamepad.Tests -c Release | 234 assertions passed | Production reducer/session against synthetic platform doubles |
| NativeAOT win-x64 publish and executable | Passed, same 234 assertions | Headless input/session code only, not full WinUI app |
| Neutral reducer allocation fixture | Zero bytes over 100,000 warmed samples | Reducer only, not WinRT or GUI allocations |

Native projection compilation and integrated page/MCP checks belong to the parent
integration task. Physical controller, visual focus, haptics and UI performance
remain unverified. No real user data, input hook, UI automation or self-approval
was used.

## Handoff

Parent owns page callbacks, foreground/minimize gates, controller keyboard/help,
MCP state/actions and permission/integration tests. Public contract is documented
in source and the test README. Root must dispose on final page lifetime exit.

## Publication

Task-owned files are committed on codex/auth-gamepad-input for parent integration.
Parent owns the final reviewed combined branch push and remote SHA verification.
