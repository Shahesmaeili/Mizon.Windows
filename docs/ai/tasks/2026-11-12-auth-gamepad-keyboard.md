# 2026-11-12-auth-gamepad-keyboard

- Status: complete component; combined WinUI integration/verification remains root-owned.
- Owner/agent: Codex gamepad_standing_docs.
- Date: 2026-11-12
- Repository and branch: Mizon.Windows, codex/auth-gamepad-keyboard.
- Base commit: a6bce9a3e9320e5c5a6ddbe301e7fcaf423b1c04.
- Related task: authentication controller input/UI/MCP integration; scoped Xbox controller policy.
- Owned files: UserControls/AuthGamepadKeyboard.cs, Services/Gamepad/AuthGamepadTextEditing.cs, Mcp/Mizon.Windows.AuthGamepadKeyboard.Tests/ and this note.

## Request and acceptance criteria

Provide a native in-page controller keyboard for authentication TextBox and
PasswordBox controls. Keep target values in those original controls, preserve
selection and length constraints, support Persian/English letters and symbols,
and consume keyboard actions without submitting the form or revealing passwords.

## Implementation and decisions

The control exposes Open, Close, IsOpen, Handle, Closed, Dispose and an internal
EditingTarget for the page's safe MCP field identity. Its parent owns active
foreground/controller lifetime, viewport sizing and input dispatch. The keyboard
closes on target disable/unload/visibility/read-only changes and its own unload;
Close/Dispose detach target/localization subscriptions and invalidate queued focus.

Ten equal columns contain five character rows and two action rows. Built-in button
focus visuals, constrained directional navigation and a vertical ScrollViewer
support a small viewport. Native ThemeResource expressions retain theme/high-contrast
updates. Header/action labels follow application localization; keyboard language
starts with the app language and can change independently. Persian letter keys use
the existing IRANSans font. No keyboard animations are introduced.

Mappings while open: directions/right-stick scroll move key focus; A activates the
focused key; X backspaces; Y changes English case; LB/RB select letters/symbols;
LT/RT move the text caret; View changes keyboard language; L3 inserts space; R3
resets key focus; Menu finishes editing and B closes. Finishing does not submit.
On-screen buttons additionally expose select-all and the same editing actions.

TextBox edits replace the current selection through SelectedText. The pure edit
helper respects MaxLength and complete Unicode graphemes for deletion, movement
and truncation. PasswordBox editing appends/backspaces only, with a localized
limitation label; selection/caret buttons are disabled. Open forces Hidden reveal
mode and never restores a revealed mode. Passwords have no preview, diagnostics,
extra persistent buffer, clipboard operation or MCP-facing value.

## Validation

| Command / environment | Result | Limits |
| --- | --- | --- |
| dotnet run --project Mcp/Mizon.Windows.AuthGamepadKeyboard.Tests -c Release | Passed: 34 assertions | Production-linked editing helper, synthetic strings, selection replacement/deletion, max-length rejection, Persian, surrogate pairs, combining sequences, joined emoji and caret boundaries |
| git diff --check and scoped source review | Passed | Source/documentation review only |

No real controller, rendered keyboard, focus traversal, touch/keyboard interaction,
Narrator output, consent or Windows UI automation was exercised. Main WinUI compile,
full feature MCP permission/lifecycle checks and actual hardware validation belong
to the coordinating feature task and must be reported separately.

## Handoff

Integrate with AuthGamepadAction from the input branch. Root supplies the localized
GamepadKeyboard resource keys and attaches this control below its hint bar with
viewport limits. Close when app/controller/page ownership changes. Route open
keyboard actions before form commands; MCP may use only its allowlisted safe
navigation actions and must not invoke Accept to type. Review actual keyboard
layout, focus and password behavior with hardware before claiming those verified.

## Publication

Branch: codex/auth-gamepad-keyboard. The integration owner receives the verified
remote commit; no main merge is performed by this component subtask. Use git log
for this note to identify its containing commit.
