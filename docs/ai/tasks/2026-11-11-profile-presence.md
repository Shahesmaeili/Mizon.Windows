# 2026-11-11 — Header and taskbar online presence

- Status: complete; visual verification remains explicitly unperformed.
- Integration owner: Codex root; server/SDK, avatar and MCP delegated to isolated branches.
- Repository/branch: Mizon.Windows, `codex/profile-presence`, integrated to `main`.
- Base: `32562ae861c87785cd78ae2608cfd2341c4a95a0`.
- Root ownership: main-window focus/session service, SDK adapter, Home view model,
  taskbar IPC publisher, localized strings, DI, integration tests and this handoff.
- Agent ownership: `Controls/PresenceAvatar*`, taskbar receiver/process lease and
  geometry tests; MCP contracts/permission/adapter/tests. Their separate task notes
  retain detailed verification and ownership.

## Behavior

The existing SignalR connection confirms the current authenticated main window's
focus. Green means active and connected with matching server confirmation; grey
means inactive, disconnected, signed out or awaiting confirmation. Deactivation
updates immediately; late acknowledgements cannot restore an obsolete online state.
This is current-window presence, not a cross-device aggregate or public activity feed.

`PresenceAvatar` is shared by the header and independent taskbar companion. The
bottom-right badge has a genuine larger circular cutout in the photo, preserving
transparent background, existing dimensions and hover/click animation. Rounded
parent Grid clipping was removed while keeping a separate rounded background.
Geometry resources are released on unload; status changes do not rebuild the clip.

Only the main window owns focus and SignalR. Existing taskbar IPC publishes the
same confirmed state, process identity and localized labels. An online-only
10-second local lease needs no extra network heartbeat. The taskbar's existing
timer and one disposable process watcher turn stale/closed ownership offline;
the companion itself remains independent. Presence-only changes do not re-decode
the photo or reload the mood animation.

The coordinator is lazy before login, ignores obsolete sessions, retries transient
send failures at 1/2/5 seconds with a three-retry limit, and cancels/releases
subscriptions and timers on close. The SDK restores current intent after reconnect.
Persian/English labels, Narrator status and the physical bottom-right position are
preserved independently of RTL reading order.

`mizon_get_presence_state` is read-only and uses the same service. Its new Presence
read permission defaults to denied, including existing grants. It cannot activate
the app, manufacture online state, approve itself or query another account.
See [PRESENCE.md](../../../Mcp/PRESENCE.md).

## Verification

| Check | Result | Scope/limits |
| --- | --- | --- |
| VS 18 Insiders MSBuild, Debug x64, integrated worktree | passed | Main WinUI and dependent taskbar/SDK/MCP payloads; existing preview/nullability/WinRT warnings remain |
| ProfilePresence.Tests, Release and actual NativeAOT executable | 38 assertions passed in each | Production coordinator and publisher with synthetic transport/clock/temporary IPC |
| Presence.Tests | 50 assertions passed, managed and NativeAOT | Avatar metrics and taskbar lease/process lifetime; no live shell |
| MCP access integration | 159 assertions passed | Actual permission service/adapters, isolated consent and cancellation/revocation/failure checks |
| MCP SDK/protocol | 38/38 groups passed | Typed discovery/dispatch, policy, persistence and isolated pipes |
| MCP NativeAOT discovery | 1/1 catalog group passed | Actual native host; no real app connection or consent |
| Matching server/SDK diagnostics | passed | Real production authenticated SignalR hub and actual SDK, generated JSON; SDK native executable also passed |

No Windows UI automation, real account writes, database changes, running Visual
Studio interruption, shell restart or self-approval was performed. Pixel placement,
actual Narrator speech and live OS focus transitions are not claimed as visually
verified. Whole WinUI/server NativeAOT publication was not performed.

## Publication and next run

Dependency commits:
- Server: `e51b5fe38f9b4d1282a429d4e9e4aec6308f7de5`.
- .NET SDK: `e384e3803e8cbab667fec55fa454c7c4046d6996`.

Publish all three repositories to `origin/main` using normal fast-forwards after
review; the final response records verified remote commits. Restart/build the
server and main client together to load this contract; the normal companion
payload upgrade flow installs the updated taskbar renderer. A live MCP presence
read needs the user's new Presence permission inside Mizon.
