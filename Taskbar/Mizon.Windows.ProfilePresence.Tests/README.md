# Main-window presence integration checks

These checks link the production `ProfilePresenceService` and
`TaskbarProfilePublisher`, using a synthetic transport, controllable clock and
temporary IPC directory. They never contact a live account, launch the companion,
activate a window or approve MCP consent.

```powershell
dotnet run --project Taskbar/Mizon.Windows.ProfilePresence.Tests -c Release
dotnet publish Taskbar/Mizon.Windows.ProfilePresence.Tests -c Release -r win-x64 -p:PublishAot=true
```

Run the published executable to verify native execution. The 38 assertions cover
lazy allocation before login, confirmation/focus races, disconnection, account
changes, cancellation, bounded retry and a queued retry racing confirmation,
local lease renewal, disposal, localization and atomic profile/mood/presence IPC.

The sibling `Mizon.Windows.Presence.Tests` covers the avatar geometry and taskbar
lease/process monitor. MCP permission/protocol tests and the SDK/server SignalR
tests cover their own boundaries. None replaces a visual WinUI/Narrator check.
