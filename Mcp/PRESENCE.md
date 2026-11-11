# Current-user online presence

`mizon_get_presence_state` is an argument-free, read-only tool. It reads the
same `IProfilePresenceService` snapshot used by Mizon's header and taskbar
indicator. It does not change window focus, send a heartbeat, start SignalR,
publish presence or query another user.

The **Presence → Read online status** permission is independent of authentication,
appearance, navigation, taskbar pinning and music-home scopes. Old saved grants
default to denied. Only the user can approve or revoke this scope in Mizon.
The exact approved scope survives restart under the existing protected pairing
identity; client names do not grant access.

## Snapshot

| Property | Meaning |
| --- | --- |
| `hasSession` | The shared service has a current signed-in session. |
| `isWindowActive` | Mizon's main window is active. Reading does not activate it. |
| `isConnected` | The presence SignalR connection is currently connected. |
| `isOnline` | The current indicator state from the shared service. |
| `hasConfirmedState` | The current presence state is confirmed by the server. Do not treat an unconfirmed indicator as authoritative. |

These are the only presence fields. No account IDs, profile details, tokens,
last-seen times, window titles or other users are returned. A signed-out snapshot
can still be read with consent so an agent can observe the indicator's offline
state. Network disconnection and pending confirmation remain distinct states.

The result contains `presence` only with Presence read permission. Calls return
sanitized `approval_required`/denial, `unavailable`, `invalid_request`,
`operation_failed` or `cancelled` outcomes where applicable. Cancellation and
revocation are checked before reading and again before returning the snapshot.
Closing the MCP adapter does not dispose the application's shared presence service.

## Verification

- `Mizon.Windows.Mcp.Tests`: actual SDK discovery and argument rejection before
  connection; isolated pipe dispatch; independent policy; source-generated JSON;
  exact scope persistence and revocation. There is no presence mutation tool.
- `Mizon.Windows.Mcp.AccessTests`: production access service, connection view model
  and presence adapter over isolated pipes; synthetic active/inactive/disconnected/
  pending/signed-out snapshots; old-scope denial; redaction; cancellation during
  read and revocation; restart restoration; offline revoke; failure and disposal.
- NativeAOT companion discovery is verified with `--catalog`, which never contacts
  the running Mizon app or creates a real consent request.

These tests do not claim real focus/SignalR/UI synchronization; those belong to
the shared presence service's integration checks. No Windows UI automation or
real MCP self-approval is performed by the headless tests.
