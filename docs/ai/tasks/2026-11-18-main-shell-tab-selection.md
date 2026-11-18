# Match the top navigation tabs to the visible page

- Owner: Codex.
- Scope: WinUI Home shell navigation selection.

The selected item is derived from the actual inner Frame content on load and
after navigation. Only the music, movies and series, apps and VPN landing
pages select their corresponding tab. Settings, profiles and detail pages
clear selection. Back navigation restores the correct tab when returning to a
landing page. Deep links use the same sync as normal navigation. Native
mouse, keyboard and gamepad invocation are unchanged.

Validation: Debug x64 build and 316 existing MCP access assertions, including
the settings routes. The live visual state and physical Xbox input were not
verified.
