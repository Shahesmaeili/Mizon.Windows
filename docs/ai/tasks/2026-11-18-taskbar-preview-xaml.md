# Taskbar thumbnail XAML with .NET 11

- Status: complete for the current tested Windows build.
- Owner: Codex.
- Branch: codex/taskbar-preview-xaml.
- Base: a14ee370940ef5c60df1c07fe9d1fbd3fa996fc3.
- Related repositories: none.
- Owned areas: main-window preview lifecycle, the new C#/XAML TAP and host,
  packaging target, scoped navigation checks and this handoff.

## Request and decisions

The user requested actual interactive XAML inside the existing taskbar preview,
below the four existing transport buttons, with all code in .NET. The earlier
two-button native-toolbar fallback was reverted in e83a64c because it did not
meet this request.

The new NativeAOT DLL supplies native COM entry points implemented in C# and
loads the embedded PreviewActions.xaml into Explorer's system XAML tree. A
separate Grid row reserves space below the native transport controls. Both new
actions use the existing playlists and storage settings routes. The main window
owns configuration publication and the independent hidden host stages immutable
payloads and reconnects after process replacement.

The existing MCP navigation read/control grants and dispatcher remain the
business boundary. New assertions cover both routes through that dispatcher.
The XAML Buttons retain system focus visuals, Tab order, XY navigation and
localized accessible names. Physical Xbox and Narrator behavior in Explorer's
input context remain unverified.

## Verification and actual limits

| Check | Observed result |
| --- | --- |
| NativeAOT TAP and host publication | Passed |
| Real native exports, COM activation and rejected class check | Passed |
| Actual Windows.UI.Xaml parsing in Persian and English | Passed |
| Real synthetic Grid insertion, input subscription and layout restoration | Passed |
| VS MSBuild Debug/x64 app build | Passed, existing experimental API/AOT warnings remain |
| MCP access suite with the two destination assertions | 313 assertions passed |
| User observation in Windows 11 26340.9882 | Text buttons confirmed below the existing controls |
| Live playlist click | Adapter log records mizon://playlists and the main-window title confirms the playlist page |
| Reopening thumbnail after both actions | User confirmed both actions and their persistence; later successful row attachments are logged |
| Live download/cache click | User confirmed the correct settings section; the log records mizon://settings/storage and the main title confirms Settings |

An early prototype live test ended with an Explorer access violation. Its dump
points to DirectUI ReferenceTrackingStarted during OSClient.UWP collection. The
current candidate initializes the native host's COM/WinRT apartment explicitly,
dispatches through the element's own dispatcher, uses a module-specific hidden
window class, and isolates the custom input events. The current Explorer remained
alive after successful playlist activation and later preview creation. This is
an unofficial shell modification and does not establish stability on other
Windows builds. Diagnostic dumps, logs, staging payloads and private user state
are outside the repository.

NativeAOT images remain resident until Explorer exits even after observer/UI
cleanup. See the component README for stop behavior and verification commands.

## Publication

The task commit contains its implementation and handoff. Push must be verified
against the remote SHA. The preceding fallback task had no working Git write
credential and the GitHub integration rejected tree creation with HTTP 403.
