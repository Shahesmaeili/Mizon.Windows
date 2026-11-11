# VPN page MCP

The VPN adapter shares the active page's operations and state. It never creates
another page or a parallel VPN implementation. The current feature has no tunnel
engine: `isConfigured` and `isConnected` are false. Windows network availability
is reported separately and must not be presented as VPN protection.

| Tool | Input | Consent | Effect |
| --- | --- | --- | --- |
| `mizon_get_vpn_state` | None | VPN read | Read the active page's bounded snapshot. |
| `mizon_set_vpn_particles` | Required `enabled: boolean` | VPN control | Set the page's decorative-particle preference. Windows motion preference still applies. |
| `mizon_open_vpn_settings` | None | VPN control | Invoke the page's existing operation for `ms-settings:network-vpn`. No arbitrary URI or configuration is accepted. |

VPN read and VPN control are separate, initially unchecked permissions in the
localized native consent card. Existing persisted grants default both to false.
Only the user can approve them inside Mizon. Control-only calls return an outcome
without VPN state; redaction also covers responses accidentally carrying VPN
data from another adapter. A successful settings launch only means Windows
accepted the launch, not that the user connected or configured a tunnel.

The snapshot contains only `isAvailable`, `isConfigured`, `isConnected`, `isBusy`,
`networkStatus` (`internet`, `local`, `offline`, `unknown`), `particlesEnabled` and
`motionAllowed`. It never exposes adapter identifiers, SSIDs, IP addresses,
credentials, paths or server configuration. The generated JSON model supplies the
discovery output schema without runtime reflection. Tool inputs reject missing,
mistyped and extra fields before contacting the application.

Use the declared navigation tool to open `vpn` first, with its own navigation
permission. Signed-out calls return `requires_sign_in`; an inactive page returns
`page_unavailable`. An operation links its lifetime to the page, caller and
adapter. Consent revocation/global disable cancels pending work through the
existing access service. Unloading or replacing a page suppresses stale results.
Already dispatched Windows UI cannot be recalled; interrupted settings launches
must not be retried automatically. The adapter does not dispose its shared page.

## Verification

`Mizon.Windows.Mcp.Tests/Vpn*Tests.cs` exercises actual SDK/stdio dispatch and
isolated pipes, strict schema/argument validation, source-generated framing,
scope independence, old grants, persistence and revocation. The production
`McpAccessService`, consent view model and `McpVpnService` are linked into
`Mizon.Windows.Mcp.AccessTests/VpnTests.cs` with synthetic page operations to
check readback redaction, real dispatch, failures, busy state, no-session/inactive
state, cancellation, revocation, page unload, restart and disposal.

```powershell
dotnet run --project .\Mcp\Mizon.Windows.Mcp.AccessTests -c Release
dotnet run --project .\Mcp\Mizon.Windows.Mcp.Tests -c Release -- --isolated
dotnet publish .\Mcp\Mizon.Windows.Mcp\Mizon.Windows.Mcp.csproj -c Release -r win-x64 -p:PublishAot=true
```

The published companion can be checked using the protocol test executable with
`--catalog <absolute published exe>`. These checks never approve a real client,
launch Windows settings, connect a VPN, change network configuration or automate
the Windows UI. They do not verify visual layout, particles, Narrator speech or
the actual Settings app launch; those require an explicitly authorized live test.
