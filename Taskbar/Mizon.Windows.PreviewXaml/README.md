# Mizon taskbar thumbnail XAML

The main app retains its four native thumbnail transport buttons. This adapter
adds a compact presence panel beneath them, inside the existing taskbar
thumbnail template. It shows global connected accounts, the current account’s
online followers, and SignalR or internet connectivity status.

All implementation source is C# and XAML. The TAP is a .NET 11 NativeAOT shared
library with source-generated COM callbacks and explicit Windows SDK ABI calls.
Explorer loads it through XAML Diagnostics. The host executable is also .NET 11
NativeAOT. The system XAML host uses Windows.UI.Xaml. The editable layout is
[PreviewPresence.xaml](PreviewPresence.xaml).

This is an unofficial, build-sensitive shell modification. It was exercised on
Windows 11 build 26340.9882. The template must expose an editable Grid and the
Taskbar.TaskItemThumbnailView runtime class. An unsupported template is skipped
and logged without replacing the native thumbnail toolbar.

## App integration

Build/TaskbarPreviewXaml.targets publishes both components and includes their
EXE/DLL payload in the app. TaskbarPreviewXamlService writes the current HWND,
process ID and Mizon language after entry into HomePage, stages an immutable
payload version under the user's .mizon/TaskbarPreview directory, and launches
the hidden host. It also publishes the two counts and the connection state in an
atomic snapshot bound to the same window and process. No tokens, account
identities or arbitrary XAML or commands cross this boundary. The watcher
validates the Mizon process and current-session
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

## Localization, accessibility and cleanup

The Persian and English status labels follow Mizon’s language. The read-only
panel exposes a localized UI Automation summary and adds no Tab or Xbox focus
stops to the system transport controls. The green indicator and eased count
changes respect the Windows animation setting. Connecting, reconnecting and
offline states clear both counts. Permission-gated MCP presence exposes the same
state without creating or changing a SignalR connection.

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
The test executes the actual native exports and COM factory, rejects unsupported
classes, parses the embedded XAML with Windows.UI.Xaml and adds it to a synthetic
Grid. It checks owner validation, caption retention, count easing, clearing the
counts offline and restoring the original grid height. Run a separate process
with the en-US argument to test the English layout.

The self-test does not click real app buttons or approve MCP permissions.
The MCP access suite covers reading the aggregates with synthetic consent and
isolated pipes. These tests do not establish rendering on other Windows builds
or physical controller or Narrator behavior.
