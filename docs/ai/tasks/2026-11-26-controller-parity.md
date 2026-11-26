# Windows controller parity reference

- Owner: Codex
- Date: 2026-11-26
- Repository and branch: Mizon.Windows, codex/controller-parity-reference
- Base commit: a7010d7a0e67e8f31e68712d5fb2314a599b53aa
- Owned files: portable auth samples, their Windows adapter tests and this note
- Integration: Apple macOS/iOS/watchOS and Android mobile/TV/Wear OS work belong to their platform branches

Use [auth-gamepad-v1.json](../contracts/auth-gamepad-v1.json) for the Windows authentication reducer reference. Each input case starts with a fresh reducer. Frames provide monotonic milliseconds, button states and axes normalized to -1 through 1 for sticks and 0 through 1 for triggers. Positive Y points up. Missing controls are neutral. A reset is applied before its sample to model active-surface or device changes. A null action means that sample must have no effect. Feedback samples contain elapsed milliseconds, two normalized motor strengths and a completion flag. Use existing device capability checks before playing them.

This reference deliberately covers the portable auth input reducer and vibration envelope. Each platform must also verify focus, screen readiness, text entry, overlays and lifecycle against its production UI. Windows uses scoped adapters for Login, Registration and Welcome. Music detail, music video, artist and Nearby Devices have dedicated input handlers. Other pages must be assessed against the active WinUI tree and should not inherit the authentication button map globally. The [controller and MCP rules](../../../Mcp/AUTH_GAMEPAD.md) describe the auth permissions and existing state.

## Verification

- Windows synthetic input and session harness passed 463 assertions including the shared vectors.
- Windows source-linked focus navigator passed 70 assertions and its controller text editor passed 34.
- The platform handoff covers the application targets independently. No physical device, native focus painting, real motor movement or real authentication was exercised by these Windows tests.
