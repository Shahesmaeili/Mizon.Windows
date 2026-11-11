# Profile presence MCP handoff

- Owner: Codex sub-agent `/root/music_home_localization`.
- Branch: `codex/profile-presence-mcp`.
- Base: `32562ae861c87785cd78ae2608cfd2341c4a95a0`.
- Scope: Windows MCP contracts, SDK discovery/dispatch, consent UI/view model,
  access adapter, synthetic integration tests and MCP documentation.
- Root owns the actual presence service/transport, main-window/taskbar wiring,
  DI, localized resource values and final publication.

## Implemented

The sole new tool, `mizon_get_presence_state`, reads the shared current-user
presence snapshot without writes, activation, connections or arbitrary IDs.
`AllowPresenceRead` is a separate default-denied user grant, persisted with the
existing protected identity and redacted independently on all responses.

The root-owned `Services/ProfilePresenceContracts.cs` supplies
`IProfilePresenceService.ReadState()` and `ProfilePresenceState`. The production
test project links that file. The adapter owns no subscriptions or background
work and never disposes the shared service.

Integration calls `McpAccessService.Start(..., presence: new McpPresenceService(sharedPresence))`.
Consent resource keys required in both `Mcp.resw` files: `PresenceGroup`,
`PresenceGroupHint`, `PermissionPresenceRead`, `PermissionPresenceReadHint`,
`ReadPresence`.

## Verification

- Release production MCP access integration: **159 assertions passed**, including
  25 new presence checks using synthetic data and isolated pipes/grant files.
- Release SDK/protocol suite: **38/38 groups passed**, including policy,
  persistence, typed dispatch and strict discovery/schema checks.
- NativeAOT companion publish and actual executable discovery: **1/1 catalog
  group passed**, including the new typed presence result, with no app connection.
- `git diff --check` passed.
- Main WinUI build, real UI/taskbar presence and real consent were not exercised
  by this agent; root owns integration. No Windows UI automation or user-data writes.

Do not treat SDK discovery as authorization: users must grant Presence read
inside the updated Mizon app. Existing authentication/music grants stay unchanged.
