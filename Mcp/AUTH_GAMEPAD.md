# Authentication controller navigation

The authentication page's controller navigation shares the existing authentication
permission group. `mizon_get_auth_state` reports `state.gamepad` only for the active,
loaded Login, Registration or Welcome page and only with authentication read
consent. It contains controller-connected/navigation-active flags, the stable
focused control ID, keyboard/help visibility, `isFeedbackEnabled`, `lastFeedback`
and `isFeedbackPlaying`. `lastFeedback` is `success`, `error`, `validation` or
null before accepted result feedback. Playing state describes the session's
bounded output, not proof that physical motors moved. Reads never replay feedback.
It never contains entered
keyboard text, password contents or length, device identifiers or raw input.

`mizon_auth_gamepad_action` requires authentication control consent and accepts
exactly one `action`: `next`, `previous`, `up`, `down`, `left`, `right`,
`openKeyboard`, `closeKeyboard`, `showHelp`, `hideHelp`, `enableFeedback` or
`disableFeedback`. Feedback actions explicitly set the optional page-session
feedback preference; they do not toggle it, start vibration or accept raw motor
values/durations. Feedback is enabled when the auth page loads and can be turned
off in its View help or with the existing control action. Feedback accompanies
physical controller actions and actual login/registration outcomes on the active
page with a connected controller. The tool delegates to the
active page's controller navigation flow. Directions move focus within the page
or its controller keyboard; they do not activate a key or button. The tool does
not simulate OS input, accept an arbitrary key, navigate another page, enter text,
retry Welcome or submit a form. Relative focus actions must not be automatically
replayed after interruption.

There is intentionally no MCP `accept`, `menu` or `submit` controller action.
Physical controller activation belongs to the UI. MCP form submission continues
through `mizon_submit_auth_form` and still requires authentication control plus
separate submit consent. Readback from controller actions requires authentication
read consent; a control-only client receives the outcome without authentication
state. This is authentication behavior, not a new domain or a grant to control
other application pages.

The production adapter rejects invalid actions, unavailable/unloaded pages and
busy authentication. Cancellation flows into the page operation; replacing or
unloading the page invalidates a late result. The existing access dispatcher
serializes mutations, cancels revoked authorization generations, suppresses stale
state, sanitizes errors and releases operation ownership.

Submission feedback comes from a completed authentication attempt, never from
changes to translated error text or property notifications. Repeated backend
errors each produce one outcome per actual request. Disabled/busy submits and
cancelled/abandoned requests are silent. Registration validation uses its own
brief pattern; network/backend failure uses error; a committed token uses success.
Successful navigation carries an owner-bound, expiring, single-use feedback
handoff to Welcome so unloading Login does not cut off the new success pattern.
The handoff does not authorize a new submit or store credentials. Feedback stops
on disable, deactivation, removal/disposal and is not replayed on reactivation.
No MCP arbitrary-vibration tool is exposed: form submission still uses the
separately permitted shared authentication operation.

On Login, Registration and Welcome, controller Up from the highest control
selects a real button on the windowed native caption. A focus outline marks
its Windows bounds while the mouse pointer stays in place.
Left/Right moves across the caption. A sends the selected system command.
Down/B returns to the previously focused WinUI control. Pointer presses,
keyboard input, window resize, deactivation and disposal clear the selection.
The existing state can report
`windowMinimize`, `windowMaximizeRestore` or `windowClose` during physical
selection. MCP focus navigation cannot enter this mode.
Window MCP permissions and commands remain independent.

For the login identifier and registration email, typing, paste, controller
keyboard and MCP edits accept English letters, digits and `._@+%-`. Registration
username permits letters, digits, dot and underscore. MCP rejects invalid text
before changing any field. Final registration validation still checks email
syntax and username length. The controller keyboard keeps Persian input for
other text fields.

If rejection disables the selected form control and Windows moves focus into
the inline AI connection notice, focus returns to the form. A later directional
controller action can reclaim that focus without activating the notice or the
form. An actual consent dialog retains its own focus.

After invalid login credentials, an active controller session restores focus
to the password once the form is enabled again, including when WinUI selected
the language picker during submission. The existing gamepad snapshot then
reports `focusedFieldId: "password"`. An open keyboard, controller help, native
caption selection, or consent dialog retains input ownership.

Headless verification:

```powershell
dotnet run --project Mcp/Mizon.Windows.Mcp.Tests -c Release -- --isolated
dotnet run --project Mcp/Mizon.Windows.Mcp.AccessTests -c Release
dotnet run --project Mcp/Mizon.Windows.Mcp.AuthTests -c Release
dotnet run --project Mcp/Mizon.Windows.Gamepad.NavigationTests -c Release
dotnet run --project Mcp/Mizon.Windows.AuthOutcome.Tests -c Release
```

The SDK suite tests public schemas, generated JSON, all bounded actions and
rejection of unsafe arguments before a connection is opened. Access checks run
the real permission dispatcher and named-pipe listener against synthetic auth
state, including pending/denied/read-only/control-only access, no submit
escalation, errors, revocation, new authorization generations, restart and cleanup.
The authentication adapter suite links the production service against synthetic
pages to verify current-page selection, busy/unloaded refusal, no submit/retry,
cancellation and lifecycle subscription cleanup. None of these suites open the
real application, approve a real connection, enter credentials or submit an
account. Controller hardware, visual focus, keyboard rendering/animation and
Narrator behavior require separate manual validation.

The navigator suite source-links the production focus/overlay coordinator against
minimal visual-tree doubles. It checks hidden/disabled ancestry, modal ownership,
help/keyboard focus bounds and restoration, stale keyboard targets, explicit
feedback state, no implicit control activation, cancellation and disposal.

The outcome suite links the production LoginPageViewModel and completion-event
contract against synthetic API/storage/navigation boundaries. It covers successful
login/registration and ticket ordering, invalid registrations, repeated identical
backend errors, network failures, missing success tokens, concurrent-submit
suppression, pre/in-flight/late cancellation, observer exceptions and no outcome
on language/credential changes. The same executable is tested as native x64 AOT.
These tests do not validate physical vibration strength, comfort or controller
drivers; those remain hardware checks.
