# Welcome activity badge timing

- Owner: Codex
- Branch: `codex/taskbar-welcome-badge`, base `a4b6e50`
- Owns: Welcome's taskbar activity indication and lifetime.

Welcome used to set an Activity badge on every login and keep it through the
visual reveal. It now starts a one-shot 650 ms dispatcher timer while preparing
the session and profile. Fast requests perform no shell write. A slow request
sets the badge once and clears it when preparation ends. Cancellation, page
unload and window closure stop pending activity. An attempt clears only a badge
that it successfully set. The visual reveal and the connected animation retain
their previous timing.

The existing Welcome status and retry action cover the same operation for MCP
and controller input. The taskbar decoration adds no command.

Debug x64 compiled. The production timing policy passed eight checks on a real
WinUI dispatcher with a synthetic shell writer, including quick completion,
background cancellation, retry and failed writes. Isolated MCP access passed
311 assertions and auth outcomes passed 37. The packaged app's Explorer
rendering was not exercised.
