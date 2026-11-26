# Windows deep-link v1 parity reference

- Status: complete for the Windows reference
- Owner: Codex
- Date: 2026-11-26
- Repository and branch: Mizon.Windows, codex/deep-link-parity
- Base commit: 221b82d068e7a9230c3158a9d12843901edeaa4d
- Related: Mizon.Apple and the Android, Android TV and Wear OS client
- Owned files: Windows navigation success/response policy, the results harness, and the contract fixtures and documentation

## Request

Treat Windows as the reference for the public Mizon links and their observable behavior across native clients.

## Implementation

[deep-links-v1.json](../contracts/deep-links-v1.json) verifies all 20 Windows routes and nine settings sections. It includes canonical links, legacy aliases, blocked routes, encoded Persian and boundary cases, authentication, response stage and deadline, correlation and success payload fields. The harness checks the fixture inventory against the runtime catalog. Windows now builds success receipts through one production path and retains its existing navigation, authentication and response timing.

The fixture is independent of transport. URI-for-results on Windows and the corresponding mechanisms on other platforms use the same versioned receipt. Without a valid session, a normal protected URI can wait for sign-in. A result request returns an error instead of being resumed later. Unsupported platform destinations must not report success for a different page.

## Verification

- 623 Windows activation tests and 369 deep-link response/lifecycle/parity assertions passed. The latter passed in the native x64 test executable as well.
- WinUI Debug x64 built and 378 MCP access assertions passed on isolated fixtures.
- No live account, app activation, animation or physical Xbox controller was tested.

## Handoff and publication

Publish the canonical fixture on Windows main and use the exact revision when mirroring it in native clients. Validate each destination and OS delivery path independently. This Windows checkpoint does not certify other binaries, unsupported platform pages or a client repo without an accessible checkout.
