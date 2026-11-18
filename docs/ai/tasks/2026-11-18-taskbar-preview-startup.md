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

The hidden click helper uses STARTF_FORCEOFFFEEDBACK on process creation. The
actual child reports the inherited flag in the NativeAOT test. The running
Explorer adapter was verified by module path. The mouse feedback remained in
the user's test with the older Mizon app binary. The newly built short-lived
Mizon protocol receiver acknowledges startup feedback with a private Win32
thread message before redirecting the two preview activations. This branch
does not modify the system cursor or remove the ring on the clicked button.

Verified: Debug x64 build, MSBuild design-time inputs/stamp and binary hashes,
the startup flag in the actual native child, isolated Persian and English
Windows XAML tests and 316 existing MCP access assertions. The revised Mizon
binary needs a fresh application run to verify both layers together. No
physical-controller or Narrator check was performed.
