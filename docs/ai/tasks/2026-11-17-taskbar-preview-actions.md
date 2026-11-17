# 2026-11-17-taskbar-preview-actions

- Status: complete for the native thumbnail toolbar fallback.
- Owner: Codex.
- Repository and branch: Mizon.Windows, codex/taskbar-preview-actions.
- Base: 4fc4e7956e2922aa8eaef2402ec43f1a209bdc6c.
- Owned areas: main-window taskbar dispatch, thumbnail icons and tooltips, navigation checks, and this handoff. No cross-repository changes.

Two .NET 11 buttons in the main window's native taskbar thumbnail toolbar open the
existing playlist list and the Downloads & Media Cache settings section. The
existing four buttons keep their IDs and order. Windows owns the toolbar and
shows all six buttons for the signed-in home shell when there is room. The two
destinations are hidden in account pages and ignored if the home shell has
unloaded or is signing out. Dispatch leaves the native callback before restoring
the window and uses the existing app navigation flow. The toolbar updates
Persian/English tooltips when the app language changes. New icon handles are
released with the existing DPI and window lifetime handling.

The shell UI is Windows-owned. The existing in-app playlist button and Storage
NavigationViewItem remain the equivalent focusable controller paths. The
existing scoped MCP navigation tools use the same destinations with separate
read/control consent. Neither route starts playback, submits a form, or alters
settings. The storage shortcut opens the existing section; it does not open an
active transfer queue.

| Verification | Result |
| --- | --- |
| Visual Studio MSBuild Debug x64 with package generation and signing disabled | Passed |
| MCP access tests including playlist and storage destination dispatch | 313 assertions passed with a synthetic app, isolated pipes and consent store |
| Activation test suite published and executed with NativeAOT on Windows x64 | 613 assertions passed |
| Final PRI resource inspection | All seven taskbar labels found in compiled resources |

NativeAOT coverage here applies to the route and activation test executable.
Actual thumbnail placement, clicking, Narrator output, and physical Xbox controller
use in the Windows-owned flyout were not tested. Windows may trim the toolbar
when space is restricted. The supported toolbar exposes icon buttons with
tooltips, rather than a custom interactive XAML row.
