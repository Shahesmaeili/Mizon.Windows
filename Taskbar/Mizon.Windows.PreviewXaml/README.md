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
Explorer, and reconnects after a main-window or Explorer restart. A newer
payload asks the previous watcher and adapter to detach via the existing stop
marker before reconnecting. Its old NativeAOT image may remain resident.

The adapter matches the current Mizon window title before first attaching.
Once attached, the row belongs to that card and to the validated window handle
and process. Explorer can keep the last matched caption while Mizon updates
its title after navigation. A brief missing-caption grace period applies
during that transition. A real card removal reported by XAML Diagnostics
releases its row and retained references. The message-only HWND queues this
work, and XAML operations run on the element's own CoreDispatcher.

## Input, localization and cleanup

The controls are real Button instances with native focus visuals, Tab order,
XY navigation and localized AutomationProperties.Name. Windows owns the taskbar
input context. Xbox hardware navigation and Narrator speech require validation
in that shell context. Clicks use the same declared mizon:// destinations as
the app and the permission-gated MCP navigation service. No playback, download
mutation or consent bypass is introduced.

Pointer/tap events are marked handled at the new row so they do not propagate
into Explorer's thumbnail-activation handlers. A click switches that button's
icon to a same-size native XAML ring and exposes localized UI Automation item
status without moving focus. The per-action pending state outlives a recycled
card and rejects duplicate clicks. The sibling launcher starts the allowlisted
route with LaunchUriForResultsAsync after the pointer callback, validates the
correlation ID and result, and exits. Success, failure and the bounded deadline
all restore the icon. Mizon defers raising an already-running window for these
two actions until successful completion. Windows still owns preview visibility.

The adapter removes registered events, injected elements and
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
Grid, subscribes input handlers, and verifies the individual rings through
duplicate input, overlapping replies, card recreation, success and failure,
then checks height restoration. Run a separate process with the en-US argument
to test the English layout.

The self-test does not click real app buttons or approve MCP permissions.
The normal MCP access suite covers both declared navigation destinations with
synthetic consent and isolated pipes. These tests do not establish stability
on other Windows builds or physical controller/Narrator behavior.
