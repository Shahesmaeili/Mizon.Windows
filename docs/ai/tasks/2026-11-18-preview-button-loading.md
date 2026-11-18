# Deep-link loading on the taskbar preview buttons

- Owner: Codex.
- Branch: `codex/preview-button-loading`.
- Base: b77c2a4.
- Scope: the two native XAML action buttons, their response transport and the
  removal of the main-window status chip. No server or schema changes.

The playlists and storage buttons use a native progress ring in the existing
icon slot, keep their labels and native focus, and report localized UI Automation
item status. Each pending operation survives thumbnail recreation independently.
Repeated invocations of the same pending action are ignored. A sibling
NativeAOT process uses the app's installed package family, a window it owns,
and a correlated protocol-for-results request. All terminal results restore
the original icon. The receiver brings the existing Mizon window forward
after a successful preview response; the shell can close its preview whenever
its own UI policy requires it. The existing profile avatar is unchanged.

The launcher only accepts the two documented routes. Both use the same
navigation dispatcher and permissions already tested through MCP. The app does
not expose a new remote action or client-specific payload. An updated preview
watcher uses the existing stop marker to hand over from an older payload
without restarting Explorer.

Checks: Visual Studio Debug x64, NativeAOT host and adapter publication, real
Windows.UI.Xaml isolated tests in Persian and English (independent indicators,
duplicate input, thumbnail recreation, success/failure and cleanup), unsupported
launcher route rejection, 56 response assertions, and 316 scoped MCP service
assertions. Live Explorer rendering, package activation, watcher handover,
Xbox hardware and Narrator speech need desktop verification.
