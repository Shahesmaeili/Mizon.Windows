# Fullscreen header spacing and top-edge controls

- Status: complete in source; live visual checks remain unverified.
- Owner: Codex root; read-only API/UI review: music_home_widgets; scoped MCP:
  music_home_contracts.
- Date: 2026-11-11.
- Repository/branch: Mizon.Windows, `codex/fullscreen-header-insets`.
- Base: `ed0ac2be294a0425616b00f513c9d387a02b4258`.
- Owned: MainWindow presentation/XAML, HomePage title-bar layout, Window locale
  resources, integration App binding and documentation. MCP files were owned in
  a separate worktree and integrated from `e6a36bc89215c894ce6a97a93ab957206fb142a7`.
- No backend, database, SDK, package or Windows installation changes.

## Request and implementation

Replace Home's fixed right margin with real caption-button space, remove it in
fullscreen, and reveal window controls when the pointer reaches the screen top.

- A named XAML spacer uses `AppWindow.TitleBar.RightInset / RasterizationScale`.
  Fullscreen explicitly reserves zero. Presenter/size, root/DPI and existing layout
  notifications keep it current. Passthrough input regions are cleared in
  fullscreen and recalculated on return; all added subscriptions are removed on
  page unload. Ordinary maximization still reserves native caption buttons.
- The installed Windows App SDK 2.5.4-experimental metadata exposes no fullscreen
  title-bar auto-hide/visibility switch. Its AppWindowTitleBar has no events;
  LayoutMetricsChanged/IsVisibleChanged belong to other API families and are not
  used. The UWP-style mouse-hover shell feature remains an open Microsoft issue.
- The application supplies an overlay at the top center with minimize,
  exit-fullscreen and close. Pointer movement reveals it at the top edge; a
  one-shot hide timer respects pointer and keyboard focus. Composition animates
  opacity/translation without resizing underlying content, respects reduced
  motion and releases batches/timers/handlers on close. Labels/tooltips/Narrator
  names follow Persian/English locale and theme.
- F11 and the existing thumbnail toolbar call the same explicit fullscreen
  setter. Retaining the previous OverlappedPresenter preserves its settings on
  exit. HWND ShowWindow(SW_MINIMIZE) minimizes without replacing fullscreen;
  single-instance activation also restores iconic fullscreen windows.
- MCP shares that setter and reads only bounded state with independent read/
  control consent. See [contract](../../../Mcp/WINDOW_PRESENTATION.md) and
  [component evidence](2026-11-11-window-presentation-mcp.md).

## Sources

- [Microsoft title-bar guidance](https://learn.microsoft.com/en-us/windows/apps/develop/title-bar#system-caption-buttons): actual insets and conversion to XAML units.
- [FullScreenPresenter](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.fullscreenpresenter): official fullscreen presenter; its swipe description does not expose a mouse-hover toolbar configuration property.
- [Windows App SDK issue 786](https://github.com/microsoft/WindowsAppSDK/issues/786): requested UWP-style edge UI is still open. This implementation is explicitly app-owned, not an OS toolbar.
- [ShowWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindow) and [IsIconic](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-isiconic): official minimize/restore show state.

## Validation

| Check | Result | Limit |
| --- | --- | --- |
| VS MSBuild main project Debug x64, signing/package generation disabled | Passed, exit 0 | Existing unrelated preview/nullability warnings remain. |
| Production MCP access service + adapter through isolated pipes | 187 assertions passed | Synthetic window and consent; no self-approval. |
| Actual MCP SDK/protocol suite | 41/41 groups passed | Headless schemas/dispatch/cancellation, not GUI rendering. |
| NativeAOT MCP host publish and native discovery | Passed, 1/1 catalog check | Does not establish whole WinUI app NativeAOT publication. |
| Independent source review | Insets, presenter retention, hide state, disposal reviewed | No Windows UI automation, real hover/focus/DPI/Narrator measurement. |

Build log: `D:\MizonTaskDiagnostics\Presence\verification\fullscreen-header-build-final.log`.
The initial integrated build caught the existing App.MainMizonWindow property's
base Window type; the adapter now safely casts to its implemented host interface.

## Handoff and publication

Fast-forward/push reviewed commits to origin/main; identify delivery through this
file's Git history. Rebuild/restart Mizon to use the new code. Live multi-monitor
insets, top-edge pointer/keyboard interactions and fullscreen minimize/restore are
manual verification limits; no running Visual Studio/app/Explorer was stopped.
