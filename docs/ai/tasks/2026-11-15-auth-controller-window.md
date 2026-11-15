# Authentication vibration, focus, window controls and card flip

- Owner: Codex. Repository: Mizon.Windows. Integration branch: `codex/auth-controller-window`.
- Base: `965cc14243c4109e4d4f186e3348eb81bbe3bc61`.
- Scope: integrates the pending auth haptics engine/VM/MCP work, page lifecycle,
  focus handling, caption commands, local resources and the shared FlipSide.
  No server or SDK source changes.

Existing login and registration operations report one feedback outcome. Result
vibration is enabled on page load and can be disabled through controller View.
The failed-login pattern uses two pulses. Cancelled or duplicate attempts do not
start extra feedback. Success transfers once to the Welcome page when active
on the same window and connected controller.

Rejected login restores password focus only when focus is missing or has moved
to the inline AI notice. Directional input or B can also recover from that notice
without submitting or invoking any command. A modal consent dialog or unrelated
focused control is preserved. Page changes, detachment and window deactivation
are checked where asynchronous follow-up could run.

Controller View help on the three authentication screens contains Minimize,
Maximize/Restore and Close. Controller A performs existing Windows window
operations on the foreground owner after the input callback returns. The native
mouse caption stays available. Scoped MCP focus can read the selected control
and move within help, but cannot execute these window commands.

FlipSide accounts for both card faces during the spring. A bounded perspective
keeps the turning corners inside a form-only 8 DIP horizontal and 24 DIP vertical
gutter. The scroll reset does not add a second animation. The showcase, wordmark
and language picker stay outside the form's viewport.

The synthetic navigator covers focus reclaim, modal ownership, physical A routing
and bounded MCP focus over the window controls. Visual Studio MSBuild Debug x64
passed. Navigator checks passed 60 assertions. The input/session and submit
outcome executables passed 289 and 36 assertions in both managed and NativeAOT
x64 runs. The auth adapter passed 46 assertions, MCP access/lifetime passed 295,
and MCP SDK/protocol passed 49/49 groups. A corner projection sweep from compact
form widths up to 440 DIP fit inside the new gutter. Actual motor drivers,
native window actions and rendered pixels during a resize remain manual checks.
See `D:/MizonTaskDiagnostics/AuthControllerFix` for local build and test logs.
