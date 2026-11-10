# Discovery lifecycle regression checks

```powershell
dotnet run --project .\Mcp\Mizon.Windows.Discovery.Tests\Mizon.Windows.Discovery.Tests.csproj -c Release --no-launch-profile
```

This headless executable source-links the actual `MulticastDiscoveryServiceV2`
and uses Windows `DatagramSocket` with OS-assigned ephemeral test ports. It never
binds production port 40404, launches Mizon, automates Windows UI, changes consent,
or sends authentication requests. Received peer packets contain synthetic data.
Reflection-based JSON serialization is disabled, matching the app's AOT constraint.

Checks cover repeated/concurrent startup, stop/rebind, occupied-port failure and
recovery, startup cancellation races, idempotent disposal, 25 page-style lifecycle
cycles, concurrent start/stop, malformed packets, peer cleanup across restart and
draining an in-flight discovery callback during async disposal.
These checks reproduce the socket ownership underlying logout/login failures;
they do not claim to automate the actual login/logout UI or its animations.

The project lives under the app's existing `Mcp/**` source exclusion so the test
entry point is never compiled into WinUI. It adds no MCP tool or permission scope.
Run the sibling [MCP integration checks](../Mizon.Windows.Mcp.Tests/README.md) and
[app permission checks](../Mizon.Windows.Mcp.AccessTests/README.md) for the existing
authentication contracts, cancellation and connection cleanup.
