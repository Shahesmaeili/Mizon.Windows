# Nearby discovery checks

```powershell
dotnet run -c Release --project Mcp/Mizon.Windows.Discovery.Tests --no-launch-profile
dotnet publish Mcp/Mizon.Windows.Discovery.Tests -c Release -r win-x64 -p:PublishAot=true
```

The harness links the production Windows discovery service, protocol and WinRT
transport. By default it uses synthetic peers and never opens a socket,
launches Mizon, authenticates or changes consent. Run the published EXE to
exercise the same lifecycle and parser checks with NativeAOT.

For an intentional LAN test, run the harness with `--lan <peer UUID>` and
advertise that peer from another device using the published DNS-SD contract.
Use `--advertise 120` to publish a synthetic Windows peer for two minutes while
testing Apple or another client. These modes use an ephemeral listening port.
They announce the fictitious name `Mizon Windows probe` and `windows-probe`.

Use `--wire` for the production Windows TCP listener and pinned TLS on loopback. The opt-in
`--apple-peer` probe advertises a fictitious, known partial file. A second device
can run `--lan-media-client 0` against that probe or provide an equivalent seed.

See the [feature record](../../docs/ai/tasks/2026-11-21-nearby-devices.md) for the
wire contract. The sibling [protocol suite](../Mizon.Windows.Mcp.Tests/README.md)
and [permission suite](../Mizon.Windows.Mcp.AccessTests/README.md) cover the
MCP catalog, adapters and consent.
