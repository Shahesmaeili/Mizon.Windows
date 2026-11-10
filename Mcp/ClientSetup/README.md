# Connect an AI client to Mizon

The .NET 10 MCP companion exposes Welcome, Sign in, Create account, appearance
preferences, declared page navigation and Mizon taskbar pinning with separate permission groups. The
WinUI app owns every operation and its permission checks. This setup uses local
stdio and a Windows pipe restricted to the current user; it opens no TCP port.

## Prepare the launcher

Build and deploy the main Mizon Windows application first. Its MSIX payload
contains `Mcp\Payload\Mizon.Windows.Mcp.exe` and the companion's dependencies.
The launcher resolves the currently registered package on every start, so client
configuration does not depend on a Debug/Release directory or MSIX version.

From this directory, run:

```powershell
.\Configure-Client.ps1
```

This writes the launchers and two configuration snippets under
`%LOCALAPPDATA%\Mizon\Mcp`. It does **not** edit any AI client's configuration,
start Mizon, or approve a connection. No Administrator access is needed.
`-WhatIf` previews the destination without writing files.

The resolver uses the package family verified in this project:
`83999a60-c1ad-4cca-af16-4c69bf4a0067_yvtn54431hrs8`.
If the package identity/publisher changes for distribution, update the resolver
to the actual signed package family. It deliberately does not launch arbitrary
executables matching a client-supplied path.

## Codex

Merge the generated `codex-mcp-example.toml` section into
`%USERPROFILE%\.codex\config.toml`, retaining all existing configuration. The
section uses a stdio `command` and `args`; restart the MCP connection after
saving. A trusted project's `.codex/config.toml` can scope the setup to that
project. See [official OpenAI MCP configuration](https://learn.chatgpt.com/docs/extend/mcp?surface=cli).

Alternatively, enter the generated command and arguments in the app's MCP server
settings. Use a **local STDIO** server, not a remote URL. This integration does
not connect a web-only chat directly to a Windows process.

## Claude Desktop

Merge the `mizon` entry from `claude-mcp-example.json` into the existing
`mcpServers` object in `%APPDATA%\Claude\claude_desktop_config.json`. Preserve
other servers, then fully restart Claude Desktop. This is a local MCP entry,
not a remote connector. The [official MCP host setup guide](https://py.sdk.modelcontextprotocol.io/get-started/real-host/#claude-desktop)
documents that format and configuration path.

The generated examples use `cmd.exe /d /c` to run the stable `.cmd` launcher:
`Mizon-Mcp-codex.cmd` for Codex and `Mizon-Mcp-claude.cmd` for Claude. Each wrapper
passes its own locally generated `--client-profile` GUID. Rerunning setup preserves
these profiles in `client-profiles.json`; do not regenerate them during updates.
The GUID is a profile selector, not a credential or approval. Never derive it from
MCP client metadata or reuse one host's wrapper for a different client.

The generic `Mizon-Mcp.cmd` remains available for legacy session-only connections.
The actual server inherits stdin/stdout directly; PowerShell only resolves the
package path and never forwards MCP messages. Launch diagnostics use stderr.

## Approve a connection inside Mizon

1. Start Mizon and enable the local MCP server in the AI client.
2. Call `mizon_get_connection` to create a pending request. Listing tools alone
   does not connect to Mizon or request permission.
3. In Mizon, open **AI connections** from its connection notice or Settings.
4. Review the client label and choose read, control and optional submit access.
5. Approve the connection. The client can call `mizon_get_connection` to check
   its status, then use only its granted operations.

Names and versions are self-reported labels, not verified identities; bundled
logos do not authenticate the client. Approve a connection you initiated.
Approval for a paired client remains across Mizon, Codex and Windows restarts
until the user revokes it inside Mizon. The companion stores a random proof using
Windows current-user DPAPI; the app stores only its hash and the approved scopes
in a separately protected document. A name, title or version never restores access.
Submitting a login/registration form is a
separate permission, off by default. Changing scopes requires revoke/edit/approve.
There is no tool, environment variable or CLI flag to approve itself.

There is no idle permission-expiration timer. **Offline · permission saved** means
the client is not currently connected but its approved scopes will be restored
when the same paired client returns. Multiple workers using the same configured
profile share one settings entry; closing one worker does not revoke the others.
Revoke works even while the client is offline. Global disable suspends access,
whereas Revoke/Revoke all erase saved approval. No grants transfer by client name.

Existing approvals from before persistent pairing cannot be migrated by name:
they had no saved credential. After updating the app and switching Codex to its
profile-specific wrapper, approve the new paired client once inside Mizon.
Unconfigured/legacy peers still require approval after reconnecting. Losing the
protected identity file also creates a genuinely new client that needs approval.
DPAPI protects data at rest for the current Windows user; it is not a publisher
identity check or isolation from other processes already running as that user.

## How the AI knows inputs and outputs

MCP `tools/list` supplies each tool's name, purpose, `inputSchema` and
`outputSchema`. Input fields include descriptions, required fields and valid
page/language values. Results include `success`, `code`, `message` and, when
permitted, structured `state` and this `connection`'s grants. The client does not
need Mizon's source code or UI automation to understand these contracts.

The schema is generated from the same source-generated JSON metadata as the
response DTOs; password fields are write-only. Password/token contents never
appear in state or results. A caller must inspect the actual response before
claiming success. See the [companion README](../Mizon.Windows.Mcp/README.md) for
the current tool list.

## Check the connection safely

Ask the AI: “Check the Mizon connection, then read its current authentication
page after I approve it. Do not submit any form.” Confirm pending access first,
approve read access in Mizon, and repeat. Revoke it and verify private reads are
denied again. Restart a paired client and confirm the exact scopes are retained;
a different profile with the same name must remain pending. Revoke the offline
entry, reconnect and confirm it no longer restores access.

Tool discovery alone does not open a closed app. An explicitly requested
`mizon_open_auth_page` can activate Mizon; consent is still required before
controlling the form. Login and registration submissions need the backend that
Mizon normally uses; local connection/status and UI navigation do not add a new
backend service.

The resolver never executes directly from MSIX/AppX. It validates and stages the
entire companion payload in `%LOCALAPPDATA%\Mizon\Mcp\Runtime\<content hash>`,
then starts that immutable copy. An already-running client can therefore survive
Visual Studio rebuilding or deleting the AppX folder. On its next MCP restart it
resolves the current package and uses the matching payload; a changed DLL gets a
new content hash even if the package version did not change.

Staging is atomic and serialized across clients. Missing dependencies, a changing
source during deployment, or a corrupted cached runtime fail closed; no old version
is silently substituted. Old runtimes are not deleted while they might be in use.
Client-profile GUIDs, protected identity files and app-owned permissions are outside
these version directories and are not reset by staging or deployment.

When upgrading from the old direct-AppX launcher, stop/restart those old MCP helper
processes once after running `Configure-Client.ps1`; they still hold their old
mapped DLLs until they exit. Subsequent launches use the local copy. If the resolver
reports a missing companion, finish deploying a build that includes
`BuildMcpCompanion`. Do not point the configuration at an old build folder.

The isolated launcher checks in `Tests/Test-MizonMcpRuntime.ps1` verify complete
copying, immutable-version reuse, concurrent staging, deployment changes and
failures, and a real companion staying alive after its temporary source folder
is removed. They use temporary runtime directories and tool discovery only;
they do not contact Mizon or change real profiles or permissions.
