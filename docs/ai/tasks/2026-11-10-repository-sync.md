# 2026-11-10 repository synchronization

- Status: source and documentation complete; publication is represented by this branch's Git history.
- Owner: Codex, coordinated by the user.
- Repository: Shahesmaeili/Mizon.Windows, branch main.
- Base commit: eb809c1 (initial public source import).
- Scope: publish accumulated reviewed Windows source and durable AI collaboration docs.

## Source snapshot

Includes discovery widgets/hero, playlists/activity/profile pages, stories and
profile previews, lyrics/playback/stem/PiP handling, moods, settings/materials,
localization/accessibility, Windows integration and companion sources. Obsolete
InfoWindow and old indexed Search provider sources are removed as already requested.

MCP contains independent persisted consent for authentication, appearance,
declared navigation and taskbar pinning. It does not expose arbitrary app code or
all domain data. Deep links share the normal navigation lifecycle; Welcome is
internal and has no public URI. Profile-menu pinning queries Windows on each open,
uses native confirmation and detects external unpinning on refresh.

Build/StopExplorerCommandHost.ps1 and runtime assets are source inputs. AppPackages,
generated build files, logs, credentials and install artifacts are excluded.
AGENTS/CLAUDE/Copilot entrypoints use one shared workflow and task handoff convention.

## Validation for the latest code, before this documentation publication

- Main application Debug x64 build passed, including generated XAML and dependencies.
- Route/App Actions tests: 600 checks, managed and NativeAOT x64.
- Pin-service logic: 25 checks, managed and NativeAOT x64 with synthetic OS state.
- MCP SDK/stdio/pipe protocol: 31/31 groups.
- Production MCP access service/adapters: 110 checks with synthetic consent/backend.
- No actual Windows confirmation, real consent approval or UI automation was performed.
- No full WinUI NativeAOT publish or all-feature playback validation is claimed.

## Matching repositories

- SDK: b547c08aa7c968ca3fd2ac9a6f5df728d30e8154 in Mizon.API.DotNet (folder Mizon.API).
- Database: c31909d2baf3b741aed604d0e4ab31f42e1dcdb5 in Mizon.Database.
- Server source is published in the same task; its task note records the final set.

Use git log for this file to identify its containing commit. Future agents should
fetch that branch, read HANDOFF.md and claim independent files/worktrees before editing.
