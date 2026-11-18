# Keep the taskbar preview row during caption changes

- Owner: Codex.
- Branch: `codex/taskbar-row-retention`.
- Base: 3f8b04e.
- Scope: Explorer preview adapter and its isolated NativeAOT fixture.

A preview action changes the main HWND title as it opens the destination.
The shell can still show the preceding caption when the completion message
refreshes the controls. A full title-only revalidation would remove the row
and reset its layout during that delay.

Initial association still requires an exact title match. An attached row accepts
its last matching caption for the same window/process and has a two-second
grace period for a missing caption during the transition. A confirmed new
caption retires the old match. The adapter releases a removed card when its
XAML diagnostics handle is reported removed and preserves the existing cleanup
on logout, configuration loss and adapter stop. Callback dispatch retains
references until the work is owned by the element's dispatcher.

The NativeAOT XAML fixture exercises an actual caption change, a reply that
clears only one button's spinner, stable reserved height, expired grace, and
rejection of another window, process and caption. The existing scoped MCP
suite covers the unchanged navigation routes. Live Explorer, Xbox hardware
and Narrator were not automated.
