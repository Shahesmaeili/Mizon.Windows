# Pointer glow on music side widgets

The five side widgets use a 0.5 DIP rounded Composition stroke in their own
accent color. The pointer lights nearby parts of the border, with a fade at the
edge of its range, following the interaction pattern of DevWinUI's
[GlowingBorder Cursor mode](https://github.com/ghost1372/DevWinUI/blob/main/dev/DevWinUI/Controls/Win2DAndComposition/GlowingBorder/GlowingBorder.cs).

The page coalesces pointer updates, allocates each stroke lazily and detaches
the pointer handlers and composition visuals on unload. The decoration stays
out of hit testing, focus and Narrator. Controller focus uses the existing
native visuals. Reduced-motion settings skip the fade, and high contrast
suppresses the decoration. MCP requires no new tools for the visual effect.

Owned paths are WidgetPointerGlow, WidgetLoadHost and the five music-home
widget host declarations. Verify appearance and DPI behavior on a live desktop.
