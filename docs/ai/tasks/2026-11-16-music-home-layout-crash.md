# Featured artist charts and native layout failure

- Owner: Codex, branch `codex/fix-music-home-layout-crash`, base `69ff7f1`.
- Owned paths: artist spotlight and rail chart presentation and their shared geometry producer.

A native WinUI failure occurred during music home layout after featured people
loaded. The previous spotlight submitted an empty geometry to a stretched Path.
An isolated offscreen WinUI window reproduced the same `0xc000027b` error,
XAML module version and fault offset using the previous chart markup.

Both charts now omit the shape when there is no data and draw valid samples on
a fixed coordinate surface. The Viewbox scales the surface, so a flat series
does not need a non-zero geometry extent. Chart resources are released when
the corresponding views unload.

The same runtime passed the fixed spotlight and rail with empty, flat, varying
and cleared data across three widths in the offscreen renderer. Debug x64 built
successfully. Full navigation from the existing Visual Studio session was not
re-run with the fixed build.
