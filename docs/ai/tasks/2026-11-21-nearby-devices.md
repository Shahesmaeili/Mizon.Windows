# 2026-11-21 — Nearby devices in the Windows header

- Owner: Codex Windows task
- Branch: `codex/nearby-devices`
- Base: `ac0ea2c`
- Ownership: Windows header, discovery, local preference, avatar lookup and scoped MCP.
- Related: Mizon.Apple owns Swift implementations independently. No DB or API contract changes.

## Behavior

The signed-in shell starts discovery without presenting a flyout. Pausing from
the header persists a local preference across sign-ins. Signing out clears
discovered peers and stops the Windows watcher and registration. A stopped
session cannot update a new session. Bonjour advertises the friendly Windows
device name and public account labels. They are self-reported, never proof of
account ownership. Avatars use an exact username match in existing Global Search
and the standard profile media cache. Only a bounded queue of realized rows in
the popup requests images. Closing the popup cancels image requests and animation
independently of discovery. The device type glyph remains available without an
avatar and is displayed as a badge once an image is loaded.

The localized flyout honors reduced motion and exposes live counts and rows to
Narrator. Native WinUI focus handles controller A and directional navigation.
B and Esc close the popup. The app's MCP reader has a separate opt-in scope and
returns aggregate device counts and discovery status only.

The subsequent [LAN media task](2026-11-21-lan-media-transfer.md) adds a pinned
TLS data listener while retaining this discovery contract.

## DNS-SD contract

Use service type `_mizon-peer._tcp` in `local.` with instance
`Mizon-<lowercase dashed UUID>`. The instance UUID is randomly generated per
process on Windows. The advertised TCP port only satisfies DNS-SD registration
in this phase. No handshake, content transfer, or authentication is defined.
All TXT values are UTF-8. Limits are measured in encoded bytes.

| Key | Contents |
| --- | --- |
| `v` | Required. `1` |
| `id` | Required. Same UUID as the instance |
| `platform` | Required. `windows`, `macos`, `ios`, `android`, `watchos`, `wearos`; unknown values display generically |
| `app` | Required. Version, up to 32 bytes |
| `device` | Required. OS-friendly device name, up to 96 bytes |
| `account` | Optional. Public Mizon username, up to 64 bytes |
| `display` | Optional with account. Public display name, up to 96 bytes |

No email, token, avatar URL, private account identifier, or media is advertised.
On Apple platforms use Network.framework with a declared Bonjour service and
the local-network permission. Android Kotlin can use platform NSD with the same
TXT fields. File transfer is a later feature.

## Verification

- Visual Studio 18 MSBuild Debug/x64 for the WinUI application passed. Release
  NativeAOT was verified for the headless transport, independently of WinUI.
- Source-linked .NET 11 discovery harness, including NativeAOT publication and
  self-suppression, two-interface deduplication, lifecycle and UTF-8 checks.
- Real WinRT discovery saw Apple peers on the developer LAN. The Mac Mizon
  implementation read the announcement of the Windows NativeAOT probe.
- Named-pipe MCP access checks (377 assertions) and isolated SDK catalog/dispatch
  tests (50/50 groups) exercise
  denied, authorized, restored and revoked grants with synthetic data.
- No automated inspection of the Windows UI. Physical Xbox input, Narrator,
  and live avatar placement are unverified there. Bonjour needs a LAN that
  forwards mDNS between participating devices.
