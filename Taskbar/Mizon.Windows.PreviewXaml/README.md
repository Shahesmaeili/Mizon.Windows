# Mizon taskbar thumbnail XAML

The main app retains its four native thumbnail transport buttons. This adapter
adds a separate 50-DIP Grid row beneath them, inside the existing taskbar
thumbnail template. The two text-and-icon actions open the existing playlists
and download/cache settings destinations.

All implementation source is C# and XAML. The TAP is a .NET 11 NativeAOT shared
library with source-generated COM callbacks and explicit Windows SDK ABI calls.
Explorer loads it through XAML Diagnostics. The host executable is also .NET 11
NativeAOT. The system XAML host uses Windows.UI.Xaml. The editable layout is
[PreviewActions.xaml](PreviewActions.xaml).

This is an unofficial, build-sensitive shell modification. It was exercised on
Windows 11 build 26340.9882. The template must expose an editable Grid and the
Taskbar.TaskItemThumbnailView runtime class. An unsupported template is skipped
and logged without replacing the native thumbnail toolbar.

## App integration

Build/TaskbarPreviewXaml.targets publishes both components and includes their
EXE/DLL payload in the app. TaskbarPreviewXamlService writes the current HWND,
process ID and Mizon language after entry into HomePage, stages an immutable
payload version under the user's .mizon/TaskbarPreview directory, and launches
the hidden host. No tokens, profile information or arbitrary XAML/commands cross
this boundary. The watcher validates the Mizon process and current-session
Explorer, and reconnects after a main-window or Explorer restart.

The adapter matches the current Mizon window title before attaching. It does
not use an overlay window as its visible surface. Its message-only HWND queues
diagnostics work. XAML operations are dispatched through the element's own
CoreDispatcher, after visual-tree notifications return.

## Input, localization and cleanup

The controls are real Button instances with native focus visuals, Tab order,
XY navigation and localized AutomationProperties.Name. Windows owns the taskbar
input context. Xbox hardware navigation and Narrator speech require validation
in that shell context. Clicks use the same declared mizon:// destinations as
the app and the permission-gated MCP navigation service. No playback, download
mutation or consent bypass is introduced.

Pointer/tap events are marked handled at the new row so they do not propagate
into Explorer's thumbnail-activation handlers. Protocol launch runs after the
pointer callback. The adapter removes registered events, injected elements and
added RowDefinitions, and restores the original heights when detached. Signing
out removes the window configuration and visible row. Closing Mizon allows the
host to exit after its bounded restart grace period and stop diagnostics.

NativeAOT shared libraries stay resident until Explorer exits. Disabling the
feature releases its UI/observer resources. It cannot unload the runtime image
from the live Explorer process. The host's --stop argument writes the local stop
marker. No Run registry entry or persistent startup task is registered.

## Verification

Publish the library, then run the host in --self-test mode against that DLL.
The test executes the real native exports and COM factory, rejects unsupported
classes, parses the embedded XAML with Windows.UI.Xaml, adds it to a synthetic
Grid, subscribes input handlers, and verifies height restoration on removal.
Run a separate process with the en-US argument to test the English layout.

The self-test does not click real app buttons or approve MCP permissions.
The normal MCP access suite covers both declared navigation destinations with
synthetic consent and isolated pipes. These tests do not establish stability
on other Windows builds or physical controller/Narrator behavior.
