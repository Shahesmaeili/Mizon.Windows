# Authentication adapter checks

```powershell
dotnet run --project Mcp/Mizon.Windows.Mcp.AuthTests -c Release
```

The console suite source-links `Services/McpAuthenticationService.cs`. Synthetic
page, session, localization and dispatcher types replace WinUI and backend state.
It checks active Login/Register/Welcome controller routing, loaded/busy guards,
invalid action rejection, non-secret snapshots, no implicit submit/retry,
cancellation, superseded-page results, temporary lifecycle subscriptions and
idempotent disposal. `IsAotCompatible` plus generated JSON are preserved; a managed
run alone does not establish an actual NativeAOT publish.

Run the sibling protocol and app access suites for actual SDK/schema/pipe and
permission-generation coverage. No test controls Windows UI, real game controllers,
the running application or real forms/consent.
