# MCP app permission integration checks

```powershell
dotnet run --project .\Mcp\Mizon.Windows.Mcp.AccessTests\Mizon.Windows.Mcp.AccessTests.csproj -c Release --no-launch-profile
```

This headless executable links the **actual app permission service and connection
view model, appearance adapter and deep-link navigation adapter/catalog**. The dispatcher, app settings, localization,
authentication business adapter, appearance preference backend and live-page navigation backend are test doubles.
The service runs its actual listener,
handshake, permission checks, authorization cancellation and disposal code.

A test-only Windows identity shim gives each service an isolated named pipe;
the injected DPAPI-backed grant store lives in a unique temporary directory.
No test accesses the running app, its real grants, forms or backend. Approvals
call the native-consent service method only on synthetic service instances.
There is no production tool or protocol operation that approves access.

Coverage includes saved approval across reconstructed app services, exact scopes,
offline revocation, shared client rows across simultaneous workers, one worker
disconnecting while another operates, revocation/reapproval generation isolation,
global suspension/resumption, revoke-all, storage failures and legacy clients
without proof. The companion's SDK/schema/DPAPI/protocol checks remain in the
sibling `Mizon.Windows.Mcp.Tests` project.

Appearance checks use the production adapter and a synthetic preference backend
through the real app pipe/dispatcher. They cover independent scopes, legacy grant
isolation, read-only and control-only behavior, omitted-field preservation,
effective fallback/persistence results, invalid input, sanitized failures,
revocation cancellation, multi-worker denial, exact scope restoration and offline
revocation across service restarts.

Navigation checks source-link the actual production route builder and MCP adapter.
They verify independent scopes, old-grant isolation, read-only/control-only results,
canonical comment/search/settings links, Unicode query round trips, forbidden
play/download actions (including mixed case), malformed fields and IDs, sign-in
gating without deferred work, cancellation, sanitized backend failures, readiness
timeouts, multi-worker revocation, exact persisted scopes, offline revocation and
adapter disposal. They do not call an external deep link or open a real page.

Presence checks link the production read-only adapter and its shared service contract.
Synthetic snapshots cover active/inactive, disconnected, pending confirmation and
signed-out states. The actual permission service enforces independent read consent,
redacts presence on other responses, restores exact scopes, persists offline
revocation and suppresses data when consent is revoked during a read. Cancellation,
sanitized failures and adapter/shared-service lifetime separation are also checked.

These checks establish permission and lifecycle behavior, not visual layout,
animations, Narrator output or actual UI performance. They do not open Windows
UI automation or approve the user's real Codex connection.
