# Header and independent taskbar profile presence

## Ownership
- Agent: `music_home_widgets` under the coordinated profile-presence task.
- Branch/worktree: `codex/profile-presence-avatar`, `D:/MizonTaskDiagnostics/Presence/avatar`.
- Scope: header avatar XAML, reusable `Controls/PresenceAvatar*`, taskbar snapshot,
  host/presence monitor, companion project links and isolated presence checks.
- Root owns server-confirmed presence, bridge publication, main view model,
  localization resources and MCP coverage. No API/database/consent files changed here.

## Implementation
- A shared code-only WinUI `PresenceAvatar` displays the existing image with a
  circle-minus-circle Composition clip. The badge has a larger actual transparent
  cutout; no background-colored ring or rewritten image is used. Badge stays at
  the physical bottom-right even in RTL. Online is green and offline gray.
- Main header uses `ProfileImage`, `DisplayName`, `IsProfileOnline` and the localized
  `ProfilePresenceLabel`. Its existing button, flyout, hover/press scales and mood
  neighbor remain. Both parent buttons expose ItemStatus for Narrator.
- Companion links the same control with the existing Win2D package. Its 36-DIP
  hit target, 32-DIP portrait and mood spacing/placement remain unchanged.
- Snapshot v1 gains optional presence identity/heartbeat/localization fields;
  older snapshots safely show offline. The bridge must publish `isOnline`,
  `mainProcessId`, `mainProcessStartTimeUtcTicks`, `presenceUpdatedAtUtcTicks`,
  `languageTag`, `onlinePresenceLabel` and `offlinePresenceLabel`.
- Presence requires an authenticated, server-confirmed online snapshot, a matching
  live process ID/start time and a heartbeat no older than 30 seconds. The process
  exit watch clears status immediately; the existing 2-second timer detects stale
  heartbeats within its next tick. The independent companion and cached profile
  remain available when the main process closes. Sign-out still removes the child.
- One verified process watch is reused between heartbeats, released on offline,
  expiry, changed owner or disposal. Old callbacks cannot affect a new generation.
  Invalid/unreadable snapshots fail offline. Heartbeat-only updates do not reload
  images or reapply mood animation state.
- Composition clip resources detach and dispose on unload. The caller owns the
  image source; no per-frame bitmap allocation or extra polling timer is added.

## Validation
| Command/check | Result | Limit |
| --- | --- | --- |
| `dotnet run --project Taskbar/Mizon.Windows.Presence.Tests -c Release` | 50 checks passed | Headless synthetic process/clock boundary |
| NativeAOT win-x64 publish plus executing the published test | 50 checks passed | Production monitor/JSON/metrics, not the WinUI control |
| Header XAML and companion project XML parse, `git diff --check` | Passed | Does not compile generated XAML |

No actual companion/app was launched, no Windows UI automation was performed,
and no backend/account/consent state was modified. The integration owner must
build the combined WinUI tree and records its result separately. Rendering,
actual Narrator output and native taskbar behavior were not visually verified.

## Handoff
Root provides matching view-model bindings, bridge fields and localized labels,
integrates the scoped commit, performs combined builds/MCP checks and publishes.
This agent does not push a parallel task branch as the final integrated result.
