# Windows profile and taskbar account presence

- Owner Codex Windows app workstream
- Date 2026-11-25
- Repository Mizon.Windows
- Base commit `315d795119102715dd4a5b2440b517ea290a343a`
- Dependencies Mizon.Server.API `fd64e0f0032964be74551626a79fcbe94b343a0a` and Mizon.API.DotNet `34d80cd0ffef50765776de6cb44b2552408b4a1b`
- Owned areas profile presence transport and service, taskbar presence tests, MCP presence read description and access test

## Request and acceptance criteria

Windows shows the signed-in account as online when another device, such as an active Mac window, keeps the account online. The main-window flag still describes Windows focus. The last active device's deactivation or disconnect turns the account indicator offline.

## Implementation

The authenticated Windows shell watches its own account through the shared SignalR socket, including while its window is inactive. The existing publisher reports Windows focus independently. Confirmed account snapshots drive the header and taskbar profile. The companion uses the existing local presence lease, which continues while another device is active. The existing read-only MCP consent scope exposes the same state.

The watch is requested once per connected session with a bounded initial request. On disconnect or logout the watcher and cached confirmation are cleared. A server without account subscriptions falls back to its local window acknowledgements.

## Validation

The production-linked profile presence suite passed 59 assertions in both the managed Release and macOS arm64 NativeAOT builds. It exercises cross-device updates, fallback to local focus, taskbar publication, reconnect, logout, stale completion and separate local focus retries. The MCP access suite compiled with Windows targeting. Its persisted-consent checks require Windows DPAPI, so the complete suite and the WinUI app still need a Windows host.

An isolated Kestrel probe exercised the Swift publisher, the account hub, the .NET watcher and the Windows profile presence transport and publication callback together. The inactive Windows window received online from the active Mac session. The probe also covered re-subscription, an unrelated account and the final active device disconnecting.
