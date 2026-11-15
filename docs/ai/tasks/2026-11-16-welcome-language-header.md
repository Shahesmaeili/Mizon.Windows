# 2026-11-16 Welcome language control in the title bar

- Owner: Codex
- Branch: `codex/api-language`; same integration as the API language task
- Scope: Login and Welcome pages, shared login selector style and native hit
  testing, existing auth MCP and controller diagnostics

Welcome uses the same globe icon, ComboBox, tooltip and localized accessibility
strings as Login. The picker stays at the physical top left outside the
scroller in both right-to-left and left-to-right layouts. The shared title-bar
helper sizes the passthrough rectangle against the real caption insets and
transfers ownership between account pages. Fullscreen or insufficient caption
space uses the regular client area near the top instead. Instances release
layout events and their own native region on unload or window close.

Validation: WinUI Debug x64 build, 53 auth MCP adapter checks and 70 gamepad
navigator checks. Checks cover Welcome locale changes and unsupported language
through existing MCP permissions and opening, selecting and dismissing the
stationary picker with the controller navigation logic. The native title bar,
Narrator and physical Xbox hardware were not exercised.
