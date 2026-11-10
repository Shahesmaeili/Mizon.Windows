# Model rotation cursors

Original open-hand (`grab.cur`) and closed-hand (`grabbing.cur`) artwork for Mizon.
The pointer hotspot is the center of each image, so grabbing never shifts the drag position.
Each CUR contains antialiased 32, 48, 64, 96 and 128 pixel images for display scaling.

Editable vector sources: `grab.svg` and `grabbing.svg`.
Run `python generate.py` to regenerate the CUR and SVG files with no third-party dependencies.
Only the `.cur` files are packaged with the app.

`GrabCursorFactory` loads the matching system cursor size at the view's DPI and uses
the Windows App SDK's supported `IInputCursorStaticsInterop.CreateFromHCursor`.
That API copies a custom HCURSOR; temporary native handles are released immediately,
and the control disposes its cached `InputCursor` objects when it unloads.

API references:
- https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/win32/microsoft.ui.input.inputcursor.interop/nf-microsoft-ui-input-inputcursor-interop-iinputcursorstaticsinterop-createfromhcursor
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-loadimagew
