# Authentication Xbox controller support

## Scope and ownership

Implemented on `codex/auth-gamepad-ui` in an isolated Windows worktree, with
separate input, MCP, keyboard and shared-policy branches integrated after review.
Login, Registration and Welcome are covered. This does not claim controller
coverage for the existing music, settings or other application pages.

The standing user requirement is recorded in all four repositories' identical
`COLLABORATION.md` guides, revision 2026-11-12.1, and their AGENTS entry points.
New/changed interactive work must include meaningful Xbox controller support.

## Behavior

- Native `Windows.Gaming.Input.Gamepad`, scoped to a loaded, foreground,
  non-minimized authentication page; no additional input package/global hook.
- D-pad/left stick move focus. A activates the selected control or opens the
  local keyboard. B closes keyboard/help/language list, or returns registration
  to sign-in. It never exits the application.
- LB/RB change authentication face; LT/RT move between input fields. X clears
  the focused field; Y toggles only the focused password's visibility. Passwords
  hide again on focus departure, keyboard opening, deactivation and unloading.
- Menu calls the existing guarded submit flow. L3 focuses the primary button;
  R3 focuses the first field. Right-stick vertical movement scrolls. View opens
  contextual localized help with optional brief vibration, off by default.
- Welcome uses its existing preparation/error flow: A activates available
  controls, B/LB opens sign-in on error, Menu retries, L3 focuses retry and
  RB/R3 focuses sign-in. Existing busy guards remain in force.
- The keyboard supports Persian/English, symbols, numbers, case, selection,
  Unicode-safe backspace and length limits. Password edits append/delete at the
  end; the password is never copied into a visible keyboard preview.
- In keyboard context A types, X backspaces, Y changes case, LB/RB choose
  letters/symbols, LT/RT move a text caret, L3 inserts space, R3 selects the first
  key, View changes keyboard language, and Menu/B finish typing without submitting.
- Xbox/Guide and Share remain system-owned. Elite paddles follow their configured
  standard mappings. Controller input does not activate controls behind dialogs.

## UI, accessibility and lifecycle

Existing page design, animated card flip, language selector, form validation and
scene selection remain. Action/scene buttons share their existing animation paths
with keyboard focus; actual scene selection uses the same click operation.
The compact keyboard has equal-width keys and bounded scrolling on short windows.
Hints, help and accessible key names follow the current fa/en app language and
font. Theme resources track light/dark/high contrast; motion honors Windows settings.

Held buttons are suppressed until release after attachment/reactivation. Only
direction/scroll navigation repeats; submit/activation does not repeat. The first
connected controller owns input, preventing several devices from issuing merged
actions. No timer runs without an active page and connected controller. Disposal
stops polling/vibration, detaches WinRT/window/control observers, clears references
and removes the transient keyboard/help overlay. Modal focus is never stolen.

## MCP

See `Mcp/AUTH_GAMEPAD.md`. Existing auth read/control consent covers bounded
controller state, directional focus, keyboard/help visibility and vibration
preference. There is no arbitrary key injection, Accept or Menu operation.
Submitting an account continues through the separately gated submit tool.
No actual user consent or account was created/submitted by the test suites.

## Verification

- Integrated Debug x64 WinUI build passed against the real Windows projections.
  Existing unrelated compiler/experimental API warnings remain.
- 234 production reducer/session assertions passed, including allocation,
  attachment/foreground/lifetime, native duplicate-key suppression and held inputs.
  The same suite passed NativeAOT win-x64 execution.
- 34 production text-edit assertions passed in managed and NativeAOT execution.
- Production-linked navigator, auth-adapter, permission/pipe and SDK/schema suites
  cover scope, cancellation, revoked grants, no submit escalation, keyboard/help
  focus, modal dialogs and cleanup. Exact final results are recorded in HANDOFF.
- All 23 new controller resource keys match between Persian and English.
- No Windows UI automation was run, in accordance with project instructions.
  Physical Xbox hardware/driver behavior, vibration, controller latency, visual
  focus/flip/keyboard layout and Narrator output still require manual validation.
  A successful core/companion NativeAOT test is not a full application AOT publish.

## Follow-up

Future pages should reuse the input/session principles but own their domain
mapping, focus scopes and MCP permission contract. Do not route every page through
authentication grants, automatically approve MCP, or intercept system buttons.
