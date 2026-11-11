# Main-window presentation through MCP

The main Mizon window has independent **Window read** and **Window control**
permissions. Existing authentication, appearance, navigation, music-home and
presence grants do not authorize this domain. Missing fields in saved grants
default to denied; the user approves access inside Mizon and can revoke it there.

| Tool | Input | Permission | Behavior |
| --- | --- | --- | --- |
| `mizon_get_window_state` | None | Window read | Read the current main-window snapshot without activation. |
| `mizon_set_fullscreen` | Required `fullScreen: boolean` | Window control | Set the desired main-window fullscreen mode through its existing UI operation. |

The response's optional `window` contains only `isAvailable`, `fullScreen` and
`controlsVisible`. `controlsVisible` means the fullscreen controls are currently
shown, not merely that fullscreen is active. A missing/closed host returns
`unavailable` with `isAvailable: false` when read permission permits disclosure.
Control-only calls return no snapshot. This is independent from featured-video
fullscreen in the music-home domain and from saved theme/backdrop preferences.

No margin/hover simulation, arbitrary HWND, other windows, position, size,
minimization, closure, foreground activation or raw native invocation is exposed.
The top-edge toolbar's actual rendering and pointer behavior need visual checking;
headless MCP checks do not claim to verify them.

`McpWindowPresentationService` resolves the current `IWindowPresentationHost` on
the existing authorized UI dispatcher. MainWindow implements that interface; the
adapter owns no window or native handle. Request cancellation, consent-generation
checks, mutation serialization, failure sanitization and disposal use the same
access-service flow as existing domains. Tool discovery remains side-effect free.

Meaningful checks live in `Mizon.Windows.Mcp.AccessTests/WindowPresentationTests.cs`
(actual access service and adapter over isolated pipes) and
`Mizon.Windows.Mcp.Tests/WindowPresentation*Tests.cs` (actual MCP SDK dispatch,
source-generated schema/framing and remembered-grant storage). Synthetic fixture
approval cannot approve a real client or change a real window.
