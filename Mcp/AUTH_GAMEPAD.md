# Authentication controller navigation

The authentication page's controller navigation shares the existing authentication
permission group. `mizon_get_auth_state` reports `state.gamepad` only for the active,
loaded Login, Registration or Welcome page and only with authentication read
consent. It contains controller-connected/navigation-active flags, the stable
focused control ID and keyboard/help visibility. It never contains entered
keyboard text, password contents or length, device identifiers or raw input.

`mizon_auth_gamepad_action` requires authentication control consent and accepts
exactly one `action`: `next`, `previous`, `up`, `down`, `left`, `right`,
`openKeyboard`, `closeKeyboard`, `showHelp` or `hideHelp`. It delegates to the
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

Headless verification:

```powershell
dotnet run --project Mcp/Mizon.Windows.Mcp.Tests -c Release -- --isolated
dotnet run --project Mcp/Mizon.Windows.Mcp.AccessTests -c Release
dotnet run --project Mcp/Mizon.Windows.Mcp.AuthTests -c Release
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
