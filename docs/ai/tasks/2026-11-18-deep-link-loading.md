# Response-driven loading for deep links

- Status: implemented.
- Owner: Codex.
- Repository and branch: Mizon.Windows, `codex/deep-link-loading`.
- Base: d692db3.
- Related repositories: none.
- Owned areas: WinUI window loading, external activation and response lifetime,
  music/playlist/activity route readiness, navigation checks and documentation.

Use the same response lifetime for ordinary and protocol-for-results links.
The main window has one localized, noninteractive progress ring. It appears
after 150 ms to avoid flashing on fast routes and disappears on the latest
request's success, failure, cancellation or deadline. It lives above the
navigation frame and preserves focus and layout for mouse, keyboard and gamepad.
The status uses the existing accessibility announcement helper. The independent
taskbar avatar continues to consume its own native protocol response.

Ordinary signed-out content links keep their queued destination until sign-in,
and Welcome uses its own validation progress. A queued link does not consume
the response deadline before its destination can start. Routes for music
details, a playlist, the library and following activities now await their
existing first data load. The playback/download/story/comment/profile routes
keep their established completion rules. Secondary downloads, such as covers,
remain owned by each page. Search opens the in-app search surface, which owns
its existing result loading UI. Windows-owned UI, including the Search pane
and Jump List, still displays what their host supports.

MCP keeps its existing navigation read permission for the pending URI. The
integration check exercises an ordinary request through the shared dispatcher
and the real permission/pipe boundary. It verifies denied access, approved
readback and cleanup before an older handler finishes unwinding.

| Check | Result | Scope |
| --- | --- | --- |
| Visual Studio MSBuild Debug x64 | Passed | Windows app and dependent payloads, no deployment |
| Deep-link response suite | 56 assertions, managed and NativeAOT win-x64 | Correlated receipts, deadlines, failure and replacement |
| MCP access integration | 316 assertions | Synthetic permission, real isolated pipe and production access/dispatch |

No Windows UI automation or real account changes were performed. Actual
rendering, live routes on the installed app, physical Xbox and Narrator speech
were not verified. NativeAOT verification covers the response code, not a full
native publication of the WinUI app. The commit is tracked on the task branch
and in the main integration history; check the remote SHA for push status.
