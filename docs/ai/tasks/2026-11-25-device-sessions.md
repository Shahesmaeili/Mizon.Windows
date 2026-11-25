# 2026-11-25 — Devices in Windows Security settings

- Status: Windows implementation with build and headless MCP coverage.
- Owner: Codex Windows/server. Branch: main.
- Owned scope: settings panel, localized resources, session notifications and scoped MCP read state.
- Related repositories: Mizon.API.DotNet, Mizon.Server.API and Mizon.Database.

The Security settings section shows the current device and other active sessions,
platform logos, version metadata, live status, last activity, first sign-in
and IP in the device details. Use the same panel to review a new device, revoke
one or all other devices, and choose inactivity expiry. The Windows app signs
out when the affected sign-in is revoked through the matching API.

The panel uses WinUI input and accessibility controls and has Persian and English
resources. Check the physical Xbox focus path, Narrator, theme, and layout on the
deployed app after the API and database rollout. Platform logo vectors are from
Bootstrap Icons v1.13.1 with the license included alongside the assets.
Live updates reuse each existing device row. Later refreshes keep the visible
layout stable, and changing the language updates the loaded data without an HTTP request.
The device badge uses the WinUI ControlFillColorSecondaryBrush theme resource.
The former resource name caused a XAML parse failure when the first device row
was materialized in Security settings.

mizon_get_device_sessions requires the independent Device Sessions read grant.
It returns summary counters and inactivity policy, omitting device identities
and IP addresses. Session termination stays inside the app with user confirmation.
Headless MCP checks cover tool discovery, consent scope, lifecycle and protocol.
