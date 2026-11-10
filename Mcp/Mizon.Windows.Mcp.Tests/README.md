# MCP protocol checks

These console checks need no test framework. The default verification command uses isolated current-user named pipes and the official MCP SDK. It exercises the production tool/catalog/transport source, shared permission policy and framing without contacting the running Mizon app.

```powershell
dotnet build .\Mcp\Mizon.Windows.Mcp.Tests\Mizon.Windows.Mcp.Tests.csproj -c Release
& .\Mcp\Mizon.Windows.Mcp.Tests\bin\Release\net11.0-windows10.0.19041.0\Mizon.Windows.Mcp.Tests.exe --isolated
```

The isolated test groups cover pending/scoped/revoked permission policy, output schemas and sanitized errors, framing/EOF/oversize/cancellation, pure discovery in multiple SDK workers, a delayed app handshake, connection reuse, no mutation replay, cancellation while queued or active, and deterministic disposal. They also exercise protected credential persistence across six concurrent processes, configured profile identity, exact persisted scopes, offline revocation, corrupt storage, save failure and cancellation. Appearance coverage verifies independent read/control scopes, source-generated enum/output schemas, validation and typed requests through the actual SDK/pipe boundary. Navigation coverage checks all 16 allowed destinations and rejects undeclared routes/actions, invalid IDs and fields before the pipe, through the actual SDK. `--isolated-connections` runs the 13 connection lifecycle/dispatch groups.

The test assembly links the actual companion source. A test-only identity shim chooses a unique `IsolatedTests.<GUID>` pipe suffix; it never uses the real Windows SID or application pipe. Every companion fixture receives an explicit temporary credential directory and test profile GUID, so tests never access the user's real identity store. No identity shim or permission bypass is compiled into the companion executable. Synthetic fixtures return only pending/denied state and never execute real forms. Policy tests approve only independent in-memory policy objects, never a Mizon connection.

Run the sibling [app permission integration checks](../Mizon.Windows.Mcp.AccessTests/README.md) to verify actual service/view-model behavior for reconnecting workers, persisted consent, offline revocation, storage errors and cancellation across consent generations.

Use `--catalog <absolute companion executable path>` to verify an actual published executable (including NativeAOT) without connecting to Mizon. It checks real SDK discovery, input/output schemas and locally rejected invalid arguments. It deliberately skips all valid tool calls, so it cannot create a consent row, read state or change the app.

The optional `--server <absolute companion executable path>` runs the standard checks against that actual executable. Discovery is side-effect-free, but its explicit read/status calls can create a **pending** connection in a running Mizon app. Only use that mode when the task authorizes app integration; leave the test connection unapproved. It never opens the app or submits an account.

The regression reproduced before the fix: with a 900 ms application handshake, the old 400 ms discovery budget created two disconnected rows after two calls, and the old 600 ms call budget incorrectly returned `app_not_running`. The fixed implementation connects only on explicit calls, separates pipe availability from handshake timeout, and retains one pending connection under the same delay.

Results print test-group names and pass/fail summaries; they do not print real form state, credentials or MCP payloads.
