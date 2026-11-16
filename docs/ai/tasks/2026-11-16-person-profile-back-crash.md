# Returning from an artist profile

- Owner: Codex
- Branch: `codex/fix-person-back-crash`, base `f729c6f`
- Owns: widget entrance animation lifetime in `Controls/WidgetLoadHost.cs`.

The reported failure was a native WinUI `0xc000027b` fail-fast. The actual
Mizon dump put `UIElement.StartAnimation` on the failing thread, called by
`WidgetLoadHost.UpdateMotion` as the following-activity widget finished
loading after Back. Artist-model disposal and the portrait transition did
not appear on that stack.

Widget entrances now start from the dispatcher after the binding or layout
callback returns. The host checks the current viewport, loading state,
reduced-motion preference and presenter attachment before starting the
fade. A WinUI opacity storyboard replaces the facade animation and is
stopped on completion, unload, or loss of visibility. Refresh does not
replay the initial entrance. The existing Back routing and artist portrait
transition are unchanged.

Debug x64 built and the 311 isolated MCP access assertions passed. An
offscreen WinUI navigation probe linked the production host and passed all
24 trips through Frame navigation, Back, late data, reparenting, interrupted
entrance cleanup and refresh. The pre-fix host missed an entrance after
reparenting in the same probe. The full packaged navigation flow, physical
controller and Narrator output were not rerun.
