# Hide the fullscreen shortcut hover tooltip

- Owner: Codex root; read-only review: music_home_widgets.
- Branch: `codex/hide-fullscreen-shortcut-tooltip`, base `7a4cbd0`.
- Scope: MainWindow XAML/labels and fullscreen validation notes.

RootGrid's F11 accelerator automatically generated a tooltip across its hover
surface. `KeyboardAcceleratorPlacementMode="Hidden"` suppresses that presentation
without removing the shortcut or its existing callback. The explicit exit-button
tooltip retains its localized action label; F11 moves to the dedicated UI
Automation accelerator metadata for Narrator.

Reference: [Microsoft keyboard accelerator presentation](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-accelerators).

No new behavior/permission/MCP surface, package, timer or resource ownership.
Existing window MCP and transition checks remain applicable; no artificial tooltip
test is added. Main WinUI Debug x64 build passed (VS MSBuild, exit 0; existing
unrelated warnings remain). Log: `D:/MizonTaskDiagnostics/Presence/verification/fullscreen-tooltip-build.log`. Live tooltip
rendering/Narrator speech are not automated. User confirmation of the preceding
fullscreen crash fix is recorded separately in its task note and handoff.

Publication: scoped commit to origin/main; identify the commit through this file's
Git history. No server, SDK or database changes.
