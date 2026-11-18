# Taskbar preview Debug deployment and mouse startup feedback

- Owner: Codex.
- Branch: `codex/track-preview-build-inputs`.
- Base: 7d25c52.
- Scope: preview build inputs and click-time startup feedback.

The active Explorer module and Visual Studio's deployed AppX copy matched the
previous payload. After switching the watcher to the tested caption-retention
payload without restarting Explorer, the user confirmed that clicking Playlists
opened its destination and the row stayed in place. Visual Studio now tracks
both NativeAOT projects and their linked inputs, a successful publish stamp and
the two packaged output copies.

The experimental startup-cursor changes were removed at the user's request
after desktop testing still showed the cursor feedback. The preview click and
protocol activation code returns to the behavior in `7d25c52`. The build-input
tracking stays in place so preview-only changes are included in Debug builds.
