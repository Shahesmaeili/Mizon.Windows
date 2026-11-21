# 2026-11-21 — Verified public media transfer on the LAN

- Windows branch: `codex/peer-media-transfer`.
- .NET SDK contract: [Mizon.API.DotNet cfe6416](https://github.com/Shahesmaeili/Mizon.API.DotNet/commit/cfe641619116439f0387b6a8f63f96443e801860).
- API contract: [Mizon.Server.API c85573a](https://github.com/Shahesmaeili/Mizon.Server.API/commit/c85573a08ebb125a857b416bd227e88c4dd53f8a).

The app's existing MediaDownloader handles the peer/origin decision without a
second player or download workflow. After the origin authenticates an eligible
public piece table, downloads can use other Mizon devices on the local network.
The receiving app checks every piece against SHA-256 data from the origin. It
falls back to the existing local or HTTP origin for missing pieces. An
independently stored piece can be uploaded before its earlier byte ranges exist.
The Windows peer shares the signed-in Nearby session and closes when it does.
It serves a verified partial cache without exposing unrestricted file paths,
credentials, account identifiers, or filenames on the transfer protocol.

The Nearby MCP read scope now returns aggregate verified byte totals and no
individual transfers. No new interactive action or navigation map is required.
The existing controller-accessible Nearby pause also stops sharing.

## Verification

- WinUI Debug x64 build and isolated discovery MCP permission/SDK contract tests.
- NativeAOT Windows fixture used the production TCP listener to repeatedly serve
  an exact partial piece over pinned TLS, report a missing piece and drain the
  active workers. Windows discovery stays in WinRT and advertises the optional
  `media-port` TXT value for the separate TCP data listener. Older announcements
  without it use the SRV port. The Swift implementation on Mac downloaded the
  expected 4096-byte piece from this Windows NativeAOT announcement and saw the
  matching unavailable response for the next piece.
- NativeAOT SDK fixture combined two real pinned TLS peers with the missing
  origin ranges and reconstructed identical bytes. It exercised certificate pin
  rejection and corrupt-peer fallback.

## Deployment

The API must be configured with manifests only for public media eligible for
redistribution. The server task documents the offline tool to publish a
manifest. Earlier cached files are eligible after being encountered with a
valid manifest. Windows retains a local eligibility index, so an indexed
cache can serve after restart. The local eligibility expires after seven days.
Discovery is LAN-local. There is no Internet
rendezvous/relay and no Android implementation in this task.
