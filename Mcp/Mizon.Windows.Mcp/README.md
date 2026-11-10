# Mizon MCP companion

This .NET 11 stdio server uses the official `ModelContextProtocol.Core` SDK. It
connects to the already-running WinUI application through a current-user named
pipe. The application remains the authority for authentication, validation,
navigation, language selection, appearance settings and connection permissions.

## Consent

Every pipe connection begins with a private client-proof handshake. The companion
creates a random 256-bit credential for each locally configured client profile
and protects it with Windows user DPAPI under `%LOCALAPPDATA%\Mizon\Mcp\Identities`.
The startup argument `--client-profile <GUID>` identifies that profile; it is not
read from MCP metadata. ClientSetup generates separate, stable Codex and Claude
launchers with their profile identifier embedded as a validated literal. A client
claiming the display name "Codex" cannot thereby select Codex's saved proof.
Labels are **not verified publisher identities**; name/version/title updates do
not rotate the configured profile or its proof.
Creation uses a cross-process file lock and atomic replacement so concurrent
companions use the same saved credential.

Mizon requires a human to approve that identity's individual authentication
read/control/submit and separate appearance, navigation and taskbar read/control permissions inside the application. It persists only the
credential hash and the exact approved scopes in a separate DPAPI-protected grant
store. The raw credential never appears in tool schemas, results or logs. There
is no tool, command-line switch or environment shortcut for granting approval.

`mizon_get_connection` reports only this connection's status. Account state and
all application operations remain gated by the WinUI server. Approved scopes
survive companion/app restarts until revoked inside Settings, including while the
client is offline. Multiple live sessions proving the same identity share that
permission entry. Disabling MCP suspends access without forgetting approval;
revoking removes it and cancels all active work for that identity. Newly added
feature domains do not inherit authentication grants.

Legacy connections started without a configured profile retain session-only consent.
Old approvals cannot be migrated by display name: one explicit approval pairs
the new identity. A corrupt or unavailable credential is not silently replaced;
the companion returns `identity_unavailable`. DPAPI protects data at rest for the
current Windows account, not against arbitrary programs already running as that
same account.

## Tools

| Tool | Purpose |
| --- | --- |
| `mizon_get_connection` | Inspect connection approval and granted permissions |
| `mizon_get_auth_state` | Read non-secret state of the authentication screens |
| `mizon_open_auth_page` | Open `login`, `register` or `welcome` |
| `mizon_set_auth_form` | Set supplied fields without submitting |
| `mizon_submit_auth_form` | Submit login/registration through the existing app logic |
| `mizon_clear_auth_form` | Clear the selected authentication form |
| `mizon_retry_welcome` | Retry the existing welcome initialization |
| `mizon_set_app_language` | Switch to `fa-IR` or `en-US` |
| `mizon_get_appearance` | Read chosen/effective theme, material, fallback and persistence state |
| `mizon_set_appearance` | Apply/save optional `theme` and/or `backdrop` preferences |
| `mizon_get_navigation` | Read the current canonical destination, pending deep link and back-stack availability |
| `mizon_open_destination` | Open a typed, allowlisted destination through the existing deep-link route handler |
| `mizon_get_taskbar_pin_state` | Refresh Mizon's actual Windows taskbar pin status, support and availability |
| `mizon_request_taskbar_pin` | Request Windows' native confirmation to pin Mizon; never confirm it automatically |

Taskbar scopes default to false, including in existing remembered approvals. The
tools share `TaskbarPinService` with the profile menu and cannot choose another app,
unpin, manipulate registry/taskbar files or confirm Windows UI. Pinning requires
the user's request, an allowed/available Windows API and the foreground Mizon
window. A control-only grant receives the outcome without `taskbarPin` read state.
Refresh queries Windows again so external pin/unpin changes are reflected. Native
confirmation is never a test side effect: automated integration tests use a
synthetic backend, not actual taskbar changes. Cancelling/revoking prevents queued
work and suppresses stale responses; it cannot undo a Windows confirmation that
the user has already accepted. Re-read actual status rather than automatically
retrying an interrupted request.

Appearance scopes are unchecked by default. Existing version-1 remembered grants
load with both appearance scopes disabled. In AI connections, revoke the client's
access, select the required appearance permissions, then approve again. No tool
can approve or expand its own permissions. A control-only appearance grant can
change preferences but never receives appearance state in the response.

Theme values are `system`, `light`, `dark`. Backdrop values are `acrylic`,
`acrylicBase`, `acrylicThin`, `mica`, `micaAlt`. System accessibility preferences
and material support may produce a solid fallback; `appearance` distinguishes
requested from effective state. `appearance_not_saved` means a change applies
for this session but failed to persist. These tools use the existing Settings
service and never navigate, submit credentials or expose other settings.

Navigation read/control are also unchecked by default and absent from old saved
grants. Its read state can include public IDs and the search text in a current or
pending link, but not page contents or account credentials. Control-only requests
receive the operation outcome without location state. The tool input schema lists
all allowed destinations, identifiers and settings sections. It accepts no raw URI,
page type, command or media action. Music details never auto-play. The external
protocol supports explicit play/download links, but these are deliberately outside
navigation MCP permission. See [the deep-link catalog](../../Services/AppDeepLinkCatalog.cs).

Content navigation requires a signed-in shell. A signed-out MCP request returns
`requires_sign_in` and is not queued for a later login or permission generation.
Unknown destinations/parameter combinations fail before navigation. Cancellation,
revocation and disconnect cancel outstanding work; interrupted requests are not
automatically replayed. Opening the AI connections page cannot approve or change
the client's grants.

The shared pipe envelope is now named `McpRequest`/`McpResponse` in C#; existing
authentication JSON field names and operations are unchanged. The optional typed
`appearance`/`navigation` payloads and their separate permission flags are additive.

Listing tools only returns the catalog. It never connects to or launches the
application, even when an AI host discovers tools for several agents or chats.
The first explicit tool call connects and restores a remembered approval or
queues a new in-app request. An explicit
`mizon_open_auth_page` call may activate an allowlisted `mizon://` URI if the
application is closed; an unknown or revoked identity still requires in-app approval.

Permissions have no idle expiry timer. App/companion restarts and cancellation
close live transports but do not delete remembered permissions.
The initial pipe-availability check has a short deadline, separate from the
handshake budget: waiting for the app's UI dispatcher must not be mistaken for
the application being absent.

Account passwords are write-only. The companion never persists passwords or writes
request bodies or exception details to logs. The private MCP pairing credential
is separate from account passwords/tokens. Stdout contains MCP protocol data
only. Framing uses a bounded 64 KiB, four-byte little-endian length prefix and
source-generated JSON. Requests are serialized per connection; timeout or
cancellation disconnects it to prevent a late reply being mistaken for the next
request. Mutations are never automatically retried.

## Build and connect

```powershell
dotnet build .\Mizon.Windows.Mcp.csproj -c Release
```

Configure a local **stdio** MCP connection using the built executable as the
command. Use the generated per-host launcher for remembered consent, or configure
`--client-profile` once with a distinct stable GUID per host. This argument is a
local profile selector, not a private token. No password configuration is required.
The main application's build also packages this companion; use that packaged
location when installing Mizon for another user. Start Mizon and review its
connection request before invoking private tools.

The host leaves the protocol version negotiation to the SDK so clients using
older MCP handshakes remain supported. Explicit tool schemas and source-generated
bridge DTOs avoid runtime tool discovery and reflection-based JSON serialization.
