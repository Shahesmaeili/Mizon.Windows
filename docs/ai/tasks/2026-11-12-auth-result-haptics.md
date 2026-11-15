# Authentication result vibration

- Owner: Codex integration; task `2026-11-12-auth-result-haptics`.
- Repository: Mizon.Windows; branch `codex/auth-result-haptics`.
- Base: `70049c6cde3b31e810fa1b055e62f930c31ec00e`.
- Ownership: LoginPageViewModel completion event, Login/Welcome gamepad adapters,
  auth navigator readback, localized feedback labels, this note and HANDOFF.
- Delegated engine and synthetic checks have separate branches/task notes.

## Behavior

Login and registration report a typed outcome once per completed attempt, after
the existing validation/API/token-storage work. Rejected or malformed responses
and network errors play error feedback; local registration validation has a
different lighter pattern. Editing fields, retranslating an error, busy duplicate
submissions and cancelled/late responses do not replay feedback. Optional feedback
observers cannot fail authentication, and no credential/token enters their event.

Success immediately navigates to Welcome with an internal one-shot payload.
The destination consumes it only in the same foreground window, on the same
connected controller, within two seconds. This preserves feedback across Login
unload without delaying navigation or keeping a global vibration service alive.
Welcome removes the payload from navigation parameters and disposes consumed or
abandoned payloads, so cached/back navigation and ordinary startup cannot replay it.

Patterns use the existing foreground controller timer and allocation-free frames:

| Outcome | Duration | Pattern |
| --- | --- | --- |
| Successful login/registration | 260 ms | Two soft pulses, second slightly stronger |
| Rejected login/registration or request failure | 360 ms | Two firmer pulses |
| Invalid registration fields | 100 ms | One light pulse |

Smooth edges and bounded motor strengths avoid abrupt sustained feedback. Result
feedback takes priority over navigation clicks. Disabling feedback, changing input
device, deactivation/minimization, unload, disconnect/read failure and disposal
stop the motors. No additional package, background loop or API is introduced.

The session itself requires an explicit preference from its UI owner. The
[integration follow-up](2026-11-15-auth-controller-window.md) enables it when an
auth page loads. Press controller **View** to open help and turn feedback off
for the current page. Its enabled intent follows the successful navigation to
Welcome. An explicit destination opt-out is respected. No persisted preference is
added in this task.

## MCP and compatibility

Authentication read-state now includes `isFeedbackPlaying` and the bounded nullable
`lastFeedback` code (`success`, `error`, `validation`). Existing authentication
control can enable/disable feedback; actual login/registration still requires the
separate submit grant. No arbitrary motor-control tool or permission bypass exists.
Source-generated schemas/JSON, fa/en labels, Narrator checkbox metadata and the
existing controller mappings remain. Backend/SDK changes are not required.

## Verification

- Production-linked input/session suite: 289 assertions, managed .NET 11 and
  published NativeAOT x64, including one-shot handoff, expiry, controller identity,
  destination opt-out, output bounds, cancellation and disposal.
- Production LoginPageViewModel suite: 36 assertions, managed and NativeAOT,
  synthetic API/storage/navigation only; outcome ordering and repeated failure,
  overlap, cancellation and observer failure are covered.
- Production navigator: 42 assertions; outcome snapshot, modal/inactive/disposed
  refusal and success handoff covered with synthetic platform boundaries.
- MCP protocol: 49/49 groups; permission/lifecycle suite: 295 assertions.
- Integrated WinUI Debug x64 build: passed with the existing preview/nullability
  warnings; log at `D:/MizonTaskDiagnostics/AuthHaptics/build.log`.

No Windows UI automation, real account submission or live MCP consent was used.
Actual controller vibration strength/comfort, native timing under UI load, visual
behavior and Narrator speech remain manual checks. These headless AOT checks do
not establish a full NativeAOT publication of the WinUI application.

## Publication

The integration owner commits the reviewed changes and publishes the resulting
main history. Remote verification is recorded in the final task response; do not
infer successful publication solely from this note.
