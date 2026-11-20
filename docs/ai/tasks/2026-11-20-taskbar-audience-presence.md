# Online audience in the taskbar thumbnail

- Owner: Codex
- Windows base: `9a2ed32`
- Server SignalR contract: `ea35f7b`
- SDK transport: `ba3fb67`

The existing XAML thumbnail row now shows two counts, global connected
accounts and the signed-in account’s online followers. It distinguishes
connecting, reconnecting, disconnected, connected, and unavailable internet.
Disconnect clears both counts. The server counts each authenticated account
once across devices, and follower counts use active accepted relationships.
The presence registry is local to the SignalR server process. A deployment with
multiple independent server instances needs a shared presence store for global
totals.

The main process uses its existing SignalR socket. Global updates are pushed.
Follower refreshes are coalesced and run at most once per two seconds when
membership changes, with a periodic fallback for follow and unfollow on a
stable connection. The Explorer surface receives only status and aggregates
in a file bound to the owner window and process. It owns no socket or token.

The green indicator animates while connected. Both numbers ease toward their
new values, including downward changes. Fast ticks run only during a visible
thumbnail's count transition. Windows reduced motion skips number animation
and the animated indicator. The existing Presence read permission can see
the same aggregates and connection status through MCP.

## Verification

- Production hub WebSocket and SDK diagnostics passed for typed counts,
  unique-account membership, revisions, reconnect and disconnect.
- The compiled NativeAOT XAML adapter passed Persian and English tests for
  layout, caption retention, IPC owner checks, and the complete 18–32 sequence.
- The Windows MCP access suite passed 353 assertions and its isolated protocol
  suite passed all 49 groups. Presence checks cover consent and aggregate state.
- Visual Studio MSBuild built Debug x64 and published Release x64 NativeAOT.
  The standalone .NET CLI XAML compiler reported WMC9999 on its resource lookup
  in an independent rebuild. Existing WinUI and trimming warnings remain.
- The active Explorer thumbnail, real controller input and audible Narrator
  output were not automated. A fresh Windows app and API build are required
  for a running development session to use the new versions.
