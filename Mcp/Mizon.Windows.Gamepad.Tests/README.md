# Authentication gamepad input checks

Run from the Windows repository root:

```powershell
dotnet run --project Mcp/Mizon.Windows.Gamepad.Tests -c Release
dotnet publish Mcp/Mizon.Windows.Gamepad.Tests -c Release -r win-x64 -p:PublishAot=true
& Mcp/Mizon.Windows.Gamepad.Tests/bin/Release/net11.0/win-x64/publish/Mizon.Windows.Gamepad.Tests.exe
```

The console suite compiles the actual `Services/Gamepad` reducer and session
against deterministic platform doubles. It checks mapping, activation baselines,
single press versus bounded repeat, stick/trigger hysteresis, diagonal selection,
scrolling, native key suppression, foreground/page/device lifetime, connection
marshaling, unavailable devices, controller switching and reentrant disposal.
An allocation check covers 100,000 neutral reducer samples after warmup.

No real controller, window, account, consent grant or form submission is used.
These checks cannot validate WinUI focus visuals, controller drivers, physical
latency, actual vibration, keyboard entry, Narrator or final page rendering.
The integrated WinUI build separately validates the real platform projections;
the authentication MCP suites separately validate permission and transport rules.

The page owns `AuthGamepadSession` and must call `SetActive` with its active-page,
foreground and non-minimized window status. Unloading stops polling; disposal
removes all event handlers. With no connected controller there is no timer work.
The first connected controller remains the input owner until detached, while
the state reports all connected controllers. Held inputs are suppressed until
physical release after attachment, activation and controller replacement.

Guide/Xbox/Share remain OS-owned. Elite paddles follow the controller's configured
standard mappings. The session only suppresses routed Gamepad virtual keys inside
its active page subtree; it never installs a global input hook.

Native input API references:
- [Gamepad snapshot readings](https://learn.microsoft.com/en-us/uwp/api/windows.gaming.input.gamepad.getcurrentreading)
- [Gamepad and remote UI input](https://learn.microsoft.com/en-us/windows/uwp/ui-input/gamepad-and-remote-interactions)
