# Track details discussion

- Status: complete
- Owner: Codex
- Date: 2026-11-20
- Repository and branch: Mizon.Windows, `codex/music-comment-discussion`
- Base commit: `075cd99`
- Related repositories: Mizon.Database, Mizon.Server.API, Mizon.API.DotNet
- Owned areas: track discussion UI and playback entry point, comment navigation, resources, scoped MCP and tests.

## Request and acceptance criteria

Complete track comments with pagination, nested replies, source-platform
icons, one like or dislike per account, searchable typed tags, sorting, a
composer, an optional timestamp and a direct link to each comment.

## Implementation and decisions

24-item pages are virtualized with a load-more action. Sorting supports newest,
oldest, most liked and most disliked. The thread view loads direct replies and
allows each reply to start another thread. The composer can attach the current
playback position or the position selected on the waveform. Selecting a time
seeks and starts playback through the existing audio service, including the
stem mixer. An exact protocol route `mizon://music/{trackId}/comments/{commentId}`
selects and reveals the target even when it was outside the loaded page.
Tag buttons use existing profile, track and video routes. The Translate
button shows a localized availability message for now.

WinUI wraps controls at narrow widths. Persian and English resources,
Narrator names and announcements, native directional focus, Gamepad A on the
focused input/action, B to dismiss a draft or return from a thread, and the
existing controller text keyboard cover discussion interaction. Leaving the
track cancels pending reads, tag lookups and selection. The MCP tools use
separate read and control consent, restricting actions to the active track.

## Validation

| Command / environment | Result | Limits |
| --- | --- | --- |
| Visual Studio MSBuild, Debug x64 and Release x64 NativeAOT publish | Passed | Existing WinUI and trim warnings, published binary not launched |
| MCP AccessTests and protocol suite | 367 assertions and 49 groups passed | Isolated synthetic consent and no account writes |
| Gamepad navigation checks, activation and deep-link suites | 70, 623 and 56 assertions passed | Synthetic controller and activation inputs |
| Matching SQL Server fixture and SDK diagnostics | Passed | Disposable database, generated JSON, synthetic data only |

## Handoff

The shared database needs the comment migration and updated API before using
this release. Physical controller, Narrator speech, live layout and audible
seek from the comment UI remain unverified.

## Publication

- Database: `05f3a3ebf084a1b848acd30589ef7ee2d63ff0db`
- Server: `0e0c77bbd47b46c238c3f7bf937eeb311ced81e7`
- SDK: `bce141724e967c9bf35342d0a78765d0d3613761`
- Target branch: `main`
