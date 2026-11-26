# Windows Nearby Devices duplicates

- Status: complete
- Owner/agent: Codex
- Date: 2026-11-26
- Repository and branch: Mizon.Windows, codex/fix-nearby-device-inventory
- Base commit: 87f52df9e7d03c5dee6625767832ee84cdd2d003
- Related tasks: [Nearby Devices](2026-11-21-nearby-devices.md) and [LAN media transfer](2026-11-21-lan-media-transfer.md)
- Owned files: the Windows discovery service and both transports, the discovery regression suite and this handoff

## Request and acceptance criteria

Show a nearby device once when its previous and current DNS-SD instance IDs overlap. Keep differently addressed devices separate even if the account and displayed device name match. The header and consent-gated MCP counts must agree.

## Implementation and decisions

The WinRT watcher and native DNS-SD resolver pass the resolved Bonjour target to the shared discovery service. It correlates reports for the same instance ID and groups overlapping IDs by normalized local DNS target, platform and advertised device name. A delayed report for an already known older instance cannot replace a newer row. A surviving source retains the resolved target until the last source for an instance is withdrawn. Identical snapshots skip publication to the header. Reports without a resolved target still use the validated instance ID. Public LAN media keeps using its existing independent source endpoints and the on-wire contract is unchanged.

## Validation

| Command / environment | Result | Limits |
| --- | --- | --- |
| Read-only Bonjour and WinRT browse on the development network | Both sources resolved the same device IDs and DNS targets with an iPhone and Mac present | The reported duplicate did not appear during the independent sample |
| .NET 11 Discovery.Tests Release | 27 checks passed, including overlapping IDs, hostless fallback, same-name separate hosts, withdrawals and shared MCP counts | Synthetic snapshots for the duplicate regression |
| Discovery.Tests --wire | Verified partial chunks passed repeatedly over pinned TLS on loopback | Synthetic media only |
| .NET 11 Mcp.AccessTests Release | 378 checks passed, including scoped Nearby Devices access, revocation and cancellation | Isolated stores and synthetic consent |

| WinUI .NET 11 Debug x64 build | Passed | Build only, existing warnings |
| Windows x64 discovery NativeAOT publish and execution | 27 checks and the loopback partial transfer passed | Native test harness |

## Handoff

The active Windows client and API were left running. Run the updated Windows client to confirm the corrected list in the original repro. The popup controls, localization, Narrator and Xbox focus paths were kept. The popup and physical controller were not exercised.

## Publication

Publish on main, with no cross-repository contract changes.
