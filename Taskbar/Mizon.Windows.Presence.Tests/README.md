# Avatar presence checks

The headless executable source-links the production taskbar snapshot, presence
monitor/process-watch boundary and avatar geometry metrics. It verifies that saved
authentication alone cannot imply online state; heartbeat expiry, explicit offline,
process exit, owner replacement, old callbacks and disposal all invalidate the lease
correctly. A fake process factory verifies ownership/generation behavior without
starting, inspecting or stopping Mizon or Explorer. JSON checks use the actual
source-generated snapshot context.

```powershell
dotnet run --project Taskbar/Mizon.Windows.Presence.Tests -c Release
dotnet publish Taskbar/Mizon.Windows.Presence.Tests -c Release -r win-x64 -p:PublishAot=true
```

Run the resulting `Mizon.Windows.Presence.Tests.exe` from its publish directory for
NativeAOT verification. The executable does not read real profile files, make API
calls, alter consent, load WinUI, or create taskbar windows. Geometry checks cover
safe sizes, the physical badge corner and the exclusion margin, not pixel rendering.

Build the main app and companion together separately. Actual clip rendering at
different DPI, hover/click animation, Narrator output and shell lifecycle behavior
require an explicitly authorized visual/live check; these tests do not establish
those outcomes.
