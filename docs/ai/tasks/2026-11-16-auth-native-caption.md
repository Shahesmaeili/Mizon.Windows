# Native caption, left header language picker and identifier input

- Owner: Codex. Repository: Mizon.Windows. Branch: `codex/auth-native-caption`.
- Base: `63b493032dfa97aa8e759a653237ab8823337a39`.
- Scope: the authentication page layout, controllers and existing MCP adapter.
  No backend or SDK changes.

Login and registration use the left edge of the existing title strip for the
language picker. Its native input region is constrained to free title-bar
space and cleared on unload. When the native strip is absent or too small, the
picker overlays the upper corner and the compact logo moves clear. The form
retains its own full-height scroll viewport. A medium visual state begins at
680 DIP wide with 420 DIP of available body height and scales down the showcase.
The music and cinema scene labels bind explicitly to the localized font.

The duplicate View buttons are removed. A physical controller selects native
caption buttons with Up at the top of the active auth page. WinUI non-client
regions and the Win32 title-bar API supply the actual button bounds. The cursor
produces the existing native hover and a scoped Windows system command performs
the selected action. The prior WinUI focus stays available on Down/B and the
cursor is restored only if it has not been moved by the user. MCP can observe
the selected stable ID but cannot start native pointer navigation.

The form uses a single character rule for keyboard and paste input, the
controller keyboard, MCP editing and submission. Login/email allow English
letters/digits and `._@+%-`. Registration username allows letters/digits and
`._`. Name/password fields keep their prior input behavior. MCP's validation
is atomic and its schema describes the rules without exposing new operations.

Visual Studio MSBuild Debug x64 passed. The navigator passed 62 assertions in
managed and NativeAOT x64 runs. Authentication outcome checks passed 37,
controller text edit checks 34, and the MCP auth adapter 49. The isolated SDK
and protocol suite passed 49/49 groups. Logs are under
`D:/MizonTaskDiagnostics/AuthCaption`. System hover rendering, Narrator speech,
physical hardware, and actual title-button activation remain manual checks.
