# Match the Windows main window minimum to macOS

- Status: complete
- Owner/agent: Codex
- Date: 2026-11-26
- Repository and branch: Mizon.Windows, codex/match-mac-window-minimum
- Base commit: 3b900380ffde5eb0f981596b1b02edbaa24256f3
- Reference: [Mizon.Apple main window at 59706a1](https://github.com/Shahesmaeili/Mizon.Apple/blob/59706a10dc30a92c326781f2aefdf841766cab7f/App/Mac/MizonApp.swift#L30-L37)
- Owned files: MainWindow.xaml, MainWindow.xaml.cs and this handoff

## Request and acceptance criteria

Use the same 340 × 350 logical minimum for the Windows main window content as the macOS window, including display scaling on Windows.

## Implementation and decisions

Declare the content minimum with the native XAML Window properties in the app's existing experimental Windows App SDK version. The platform converts the dimensions for the current display and accounts for non-client chrome. The window applies one minimum throughout sign-in, welcome and content navigation. Existing window and controller commands continue through their original handlers and MCP permissions.

## Validation

| Command / environment | Result | Limits |
| --- | --- | --- |
| Hidden WinUI fixture with the app's SDK version and XAML values | Managed and NativeAOT runs stopped at a measured 340 × 350 client pixels at 96 DPI | AppWindow test with a hidden standalone fixture |
| .NET 11 WinUI Debug x64 build | Passed | Build only |
| .NET 11 MCP access and window presentation tests | 378 assertions passed | Synthetic consent and no physical controller |

## Handoff

Visual layout at the new minimum and live drag resizing across differently scaled monitors were not exercised. The change applies when the development client next starts with the updated build.

## Publication

Publish on main. This change reads the macOS minimum from the referenced revision.
