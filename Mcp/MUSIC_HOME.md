# Music home MCP

This feature operates on the **active signed-in `MusicPage`** and its existing
view models and controls. `MusicPage` is the discovery/home page;
`MusicsPage` is the separate track-details page. There is no offscreen copy of
the home page and no alternate backend implementation for MCP.

Tool descriptions, inputs and source-generated response schemas are published
by `tools/list`. This document explains the behavioral boundaries behind those
schemas. Read the returned section/item actions and availability before acting;
an action can still become unavailable if the user changes pages or content.

## Permissions and connection

The user grants the independent **music home** permissions inside Mizon:

| Stored permission | Allows |
| --- | --- |
| `AllowMusicHomeRead` | Read the active page's bounded state; receive state attached to an action response. |
| `AllowMusicHomeControl` | Refresh, select, navigate, control the featured media/stories/news, use existing playback/save flows, copy supported links and change the UI language. |
| `AllowMusicHomeSubmit` | Set a like/follow state; also requires music home control. |

Authentication, navigation, appearance and taskbar grants authorize none of these
operations. Adding this feature does not broaden an existing saved grant. A
control-only client can perform authorized actions but receives no music-home
snapshot; the app permission service removes it from responses. Submit alone
does not authorize a mutation.

Paired clients retain exactly their approved scopes until the user revokes them.
Revocation/global suspension use the existing authorization cancellation path.
Discovery does not connect to the app or approve access. There is no tool that
approves itself, changes its own scopes or changes another client's grant.

Control is not necessarily free of side effects: normal playback can record
listening activity, opening a story records a view through the normal UI flow,
and copying a link changes the clipboard. Like/follow requests use the separate
submit gate. Artist follow controls currently update design-preview data only.

## Tools

| Tool | Inputs | Result |
| --- | --- | --- |
| `mizon_get_music_home` | Empty object. | Current page/section/card state without fetching more pages. |
| `mizon_refresh_music_home` | `section`: `all` or a declared section below. | Runs the same home loaders and returns an outcome; state requires read permission. |
| `mizon_music_home_action` | `section`, `action` and the action-specific fields below. | Performs one allowlisted UI operation; state requires read permission. |

The companion selects the internal `music_home_submit` operation for `like` and
`follow`; other actions use `music_home_action`. Both sides validate the request.
Forging a submit action under the control operation is rejected.

Requests do not automatically sign in or navigate to the page. Use existing
authentication/navigation tools with their own approved scopes when needed.
Typical outcomes include `requires_sign_in`, `page_mismatch`, `invalid_request`,
`busy`, `item_not_found`, `action_unavailable`, `operation_failed`, `page_changed`
and `unavailable`. Permission and cancellation outcomes come from the shared
connection gate. Errors are sanitized rather than returning backend bodies,
tokens, cache paths or stack traces.

## Sections and actions

| Section | Current source | Supported behavior |
| --- | --- | --- |
| `featured` | API | Open/play its associated music; select/previous/next featured item; pause/resume its image timer or video; seek; ready-video volume/mute/fullscreen; like; copy link; retry; request download. |
| `spotlight` | Demo | Previous/next/select artist, open/profile preview, set demo follow state, reset the interactive model view. `play` is rejected while no real track is attached. |
| `stories` | API | Open a home story; pause/resume, previous/next/select, retry, like, open creator profile or a visible story tag, dismiss. `add_story` opens the existing explanatory preview, not an upload operation. |
| `genres` | API | Read the genre names/counts and refresh. Genre navigation is intentionally not implemented. |
| `artists` | Demo | Previous/next/select the artist strip, open/profile preview, set demo follow state. |
| `recommended`, `recent`, `trending` | Demo collections | Open the card or contributor preview. `play` requires a real music ID; unattached demo cards show the existing preview dialog and cannot play. |
| `library` | API | Open an available playlist, play a nonempty playlist, copy its link, `open_all` playlists, or `show_recent` on the home page. |
| `weekly` | Demo | Select an existing day, preserving the chart and selected-day state. |
| `news` | Demo editorial samples | Previous/next/select, pause/resume automatic rotation and open an article preview. These are not live news reports. |
| `following` | API, music feed | Open an activity's supported destination or actor profile; `open_all` navigates to the shared all-domain activity page. |
| `all` | Page control | `set_language` or `dismiss` the current home overlays/previews. |

Refresh currently calls the featured, library, following, and home story/genre
loaders. Stories and genres share their existing refresh operation. Refreshing a
demo-only section does not create an API request or convert the data to live data.
The state explicitly records `Source` and item `IsDemo`; callers must not present
demo counts or follow changes as persisted server data.

For featured content, `open` navigates to music details without requesting play;
`play` explicitly hands off to the shared audio player. Featured `pause`/`resume`
control the home hero's own playback/slide timer. They are not general transport
commands for the separate music-details page. `request_download` invokes the
normal Windows save flow and waits for it to end; the user chooses or cancels the
destination. The tool cannot supply an arbitrary filesystem path, and
`save_request_completed` does not assert that the user saved a file.

## Arguments and bounded state

- Use IDs returned by the current state. Real IDs identify current cards;
  demo IDs such as `news:0` or `spotlight:0` are local presentation identities.
  Arbitrary backend IDs are not a way to fetch another record through this tool.
- `id` is accepted only for `open`, `play`, `profile`, `copy_link`, `follow`
  and `open_widget`. It is required for those actions outside `featured`,
  `spotlight` and `news`. On those three sections omission means the current item.
  The limit is 100 characters, with blank/control-character values rejected.
- `select` requires zero-based `index` in 0–255 and an existing item. A music
  card's `profile` may supply contributor `index` 0 or 1; omission means 0.
- `seek` requires finite `value` in seconds, 0–86400, within the actual ready
  media's duration. `volume` requires finite `value` in 0–1, not a percentage.
- `mute`, `fullscreen`, `like` and `follow` require `enabled: true/false`.
  Likes/follows are desired states, never blind toggles. Do not automatically
  retry them after interruption: a request may already have reached the server.
- `set_language` requires `section: "all"` and `language: "fa-IR"` or `"en-US"`.
  It updates the shared UI language in place and persists the preference.
- Unsupported combinations, extra fields, missing values and out-of-range
  arguments are rejected. An action listed in the schema is not valid for every
  section. The shared `McpMusicHomeContract` is the authoritative combination map.

Each section returns at most 20 cards, with `HasMore` for truncation or additional
API data. Story widgets are likewise capped at 20 in the read-state response.
Snapshots include readable metadata, image-presence flags, source, loading/error
state, permitted item actions, and selected/seen flags where relevant. They
include featured/story media position and current selection indexes. They never
contain media bytes, local file paths, credentials or unrestricted DTO dumps.
The snapshot reads already-loaded models; it does not trigger a new query,
thumbnail download, page load or pagination request.

Examples using already-approved music home control:

```json
{"section":"all","action":"set_language","language":"en-US"}
{"section":"weekly","action":"select","index":2}
{"section":"news","action":"open","id":"news:0"}
{"section":"all","action":"dismiss"}
{"section":"featured","action":"volume","value":0.5}
```

## Cancellation, ownership and localization

`McpAccessService` gates access and dispatches to `McpMusicHomeService` on the UI
dispatcher. The adapter locates the active page only. It serializes in-flight home
actions, links caller/authorization and service lifetime cancellation, and links
page lifetime cancellation for work owned by that page.

An intentional `play` handoff can outlive the source home page once the existing
playback service takes ownership. Caller/authorization cancellation still applies;
leaving before the handoff cancels the pending work. Successful navigation returns
no stale home snapshot. Sign-out or session replacement cannot return the old
account's home data. No automatic replay of consequential actions is performed.

Story media, downloads, transitions, timers and event handlers retain their
existing close/unload ownership. The adapter closes page-owned dialogs and
cancels pending work on deactivation. News rotation and widget localization hooks
are detached on unload; responsive reparenting retains item/selection identities.
`fa-IR` uses Persian reading order and IranSans, while `en-US` uses English reading
order and the app's English font. Language changes refresh app labels, formatting
and accessibility text without re-fetching APIs or recreating decoded thumbnails.
User-provided names, comments and playlist titles remain content.

## Deliberate exclusions

This surface does not grant full music-details or global playback control,
arbitrary profile edits, story creation/upload, playlist writes, raw database
queries, generic HTTP calls, arbitrary navigation, script invocation, simulated
pointer dragging or access to unrelated pages. Opening another page through a
home action does not make its business state/actions available. Future domains
need their own typed adapters, permissions and integration checks.

## Validation

From the Windows repository root:

```powershell
dotnet run --project .\Mcp\Mizon.Windows.Mcp.Tests\Mizon.Windows.Mcp.Tests.csproj -c Release -- --isolated
dotnet run --project .\Mcp\Mizon.Windows.Mcp.AccessTests\Mizon.Windows.Mcp.AccessTests.csproj -c Release --no-launch-profile
dotnet run --project .\Mcp\Mizon.Windows.MusicHome.LocalizationTests\Mizon.Windows.MusicHome.LocalizationTests.csproj -c Release
```

The protocol suite checks actual SDK discovery/dispatch, generated schemas,
source-generated DTO round trips and invalid inputs. The access suite links the
production permission service and music-home adapter with a synthetic live-page
boundary. It covers pending/old/insufficient scopes, read-only/control-only
responses, desired-state submission, sanitized failures, sign-out/offscreen page,
revocation, exact saved-scope restoration, cancellation, playback handoff and
disposal. It never approves the user's real connection or performs real likes,
follows, playback or database writes. The localization suite links actual widget
models and resource files while replacing the WinUI/MRT boundary.

For an isolated worktree, pass `-p:MizonApiSourceRoot=<absolute SDK source root>`
to the localization project. For a published companion, use the protocol suite's
`--catalog <absolute executable path>` mode; discovery-only catalog validation
does not connect to Mizon. A NativeAOT publish can be checked independently:

```powershell
dotnet publish .\Mcp\Mizon.Windows.Mcp\Mizon.Windows.Mcp.csproj -c Release -r win-x64 -p:PublishAot=true
dotnet publish .\Mcp\Mizon.Windows.MusicHome.LocalizationTests\Mizon.Windows.MusicHome.LocalizationTests.csproj -c Release -r win-x64 -p:PublishAot=true
```

Run the published localization executable and validate the published companion
with `--catalog`. These checks do not establish that every dependency in the
whole WinUI app can publish with NativeAOT.

No live Windows UI, rendering, animation smoothness, actual Narrator output,
hardware playback or backend integration verification is implied by headless
passes. Respect the user's request not to automate Windows UI unless asked.
Live testing requires user-approved scopes and authorized data changes. Record
actual commands/results and any unverified cases in the task handoff.
