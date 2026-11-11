# Mizon Windows project map

This is the Windows client source repository, not the backend or database repository. Read [AGENTS.md](../../AGENTS.md) and [COLLABORATION.md](COLLABORATION.md) before editing. [HANDOFF.md](HANDOFF.md) records the latest verified state; source and a fresh build take precedence over historical counts in documents.

## Repository boundaries and dependencies

| Repository/component | Responsibility |
| --- | --- |
| `Mizon.Windows` (this repository) | WinUI application, local playback, native Windows integration, Windows MCP adapter and companion tools |
| `Mizon.API.DotNet` on GitHub; local folder `Mizon.API` | Shared .NET HTTP/streaming/SignalR infrastructure and typed operation SDK |
| `Mizon.Server.API` | HTTP endpoints, business operations, authentication and server-side processing |
| `Mizon.Database` | EF code-first entities, configuration and migrations |

The current developer checkout places this project at `D:/source/repos/Mizon.Windows/Mizon.Windows`, the SDK at `D:/source/repos/Mizon.API`, and the backend at `C:/Users/Ali/source/repos/Mizon.Server.API`. These are machine-specific locations, not portable API contracts. Resolve the real repository roots on a new machine. The application project currently references the SDK through `../../Mizon.API/Mizon.API.Client/Mizon.API.Client.csproj`; the backend solution also references the Windows project. Preserve or deliberately update that checkout layout instead of creating a duplicate source tree.

`ExplorerCommands/`, `Mcp/`, `Taskbar/` and `WindowsSearch/` belong to this Git repository. They contain separate build projects, not separate Git repositories. Their generated `bin`, `obj`, package payloads and installed copies are not the source of truth.

## Runtime and build configuration

- Main project: `Mizon.Windows.csproj`, target `net11.0-windows10.0.26100.0`, minimum Windows target `10.0.19041.0`.
- `global.json` selects .NET SDK 11.0.100 with patch-only roll-forward. All companion
  and verification projects target .NET 11; the OS target/minimum versions are independent.
- The project uses WinUI 3 and currently references experimental Windows App SDK packages plus preview language/SDK components. Read the project file for exact versions rather than silently upgrading them.
- Use the existing `x64` development configuration. A mixed AnyCPU/x64 dependency graph has caused problems before; keep related project mappings consistent.
- `Program.cs` owns the custom entry point and single-instance activation. `App.xaml.cs` owns DI, startup, protocol/App Actions handling and shared service lifetime. `MainWindow` owns the window material, title bar and main navigation frame.
- Debug compilation, package deployment and NativeAOT publication are different checks. Passing one does not establish the others.
- ExplorerCommands uses the supported .NET TaskHost for the SDK's three COM-host
  generation tasks when built inside full-framework Visual Studio MSBuild. This
  avoids a reproduced SDK 11 `GenerateClsidMap` dependency-binding failure while
  preserving COM hosting and the normal `dotnet build` path. Do not remove the
  explicit SDK imports/overrides merely because incremental outputs already exist.
- The build also builds/packages the MCP host, taskbar profile companion and ExplorerCommands payload. Inspect custom targets before changing build ordering or resource merging. A build may stop the Mizon Explorer command host to release its binaries; do not replace this with an indiscriminate Explorer restart.
- Package installation/registration, certificates and native shell integration have additional machine requirements. Build-only checks do not install or enable them.

## UI and feature ownership

| Area | Main source locations and constraints |
| --- | --- |
| Entry and authentication | `Pages/LoginPage*`, `Pages/WelcomePage*`, corresponding view models, `UserControls/Auth*`, `Services/LocalizationService.cs`; registration is a mode of LoginPage |
| Main shell | `Pages/HomePage*`, `ViewModels/HomePageViewModel*`, `UserControls/ProfileMenu*`, `Controls/MainSearchBox.cs`; keep title-bar hit regions and drag behavior intact |
| Music discovery | **`MusicPage`**, discovery view models and individual widget user controls; preserve widget disposal, skeleton/load behavior and responsive column layout |
| Music details | **`MusicsPage`**, `MusicsPage.Layout.cs`, `MusicsPage.CommentNavigation.cs` and music view-model partials; preserve waveform, lyrics, playback, stems, comments and live presence |
| Playlists and following | `MusicPlaylistsPage`, `MusicPlaylistPage`, `FollowingActivitiesPage`, typed SDK operations; shared following page is not music-only |
| Profiles and stories | `ClientProfilePage`, `Common/ProfilePreviewPopup.cs`, `UserControls/ProfilePreviewCard*`, `StoryViewer*`, `StoryPlaybackSession`, `FollowingActivityStoryPresenter`; release video/animation/image work when closed |
| Settings and localization | `SettingsPage*`, `SettingsPageViewModel`, `AppearanceSettingsService`, `Strings/fa-IR` and `Strings/en-US`; theme/material preferences use the existing service, not hardcoded independent state |
| Playback and system controls | `AudioPlayerService`, existing audio-player interface, `TaskbarService`, `TaskbarNative`, `ThumbnailTransportIcons`, PiP session services; keep the real playback pipeline as the state owner |
| Native search | `WindowsSearch/Mizon.Windows.PaletteProvider`; shares real API search, caching and app activation rather than a second backend |
| MCP | `Mcp/` contracts/host/tests and `Services/Mcp*` app adapters; access is approved only inside Mizon |

The names `MusicPage` (discovery) and `MusicsPage` (details) are intentionally documented because they are easy to confuse. Do not rename them during unrelated work. View-model names end with `ViewModel`.

`SectionLandingPage` currently hosts the movies/apps landing destinations. Do not invent fully implemented movie, series or genre detail pages merely because a future feature is mentioned in conversation.

## Navigation and deep links

The supported catalog is [DeepLinks.md](../../DeepLinks.md), backed by `Services/AppDeepLinkCatalog.cs` and `AppActivationRoute.cs`. Use public GUID identifiers, never database integer IDs. Route strings are independent of the displayed language.

- `mizon://welcome` is deliberately unsupported. Welcome remains an internal session-validation page; dedicated authentication MCP development tools can still inspect/open/retry it through the app's permission gate.
- A normal music detail link does not autoplay. Explicit `action=play`/`action=download` links retain their own behavior.
- Content links received while signed out preserve their target until normal authentication succeeds. Explicit logout cancels pending activation; navigation cannot race an outstanding sign-out.
- Invalid input must not erase a valid queued destination. Newer requests supersede older asynchronous navigation work.
- MCP page navigation uses the same routing flow, but never exposes arbitrary URI execution, automatic playback or download under a navigation permission.
- Connected animations are optional inputs to navigation. Pages must also work with direct links, cached-page reuse and no animation source.

## MCP capability boundary

See [Mcp/DEVELOPMENT.md](../../Mcp/DEVELOPMENT.md) for the authoritative contract and consent requirements.

Implemented domains are authentication pages, appearance, declared destination navigation, pinning the Mizon application to the taskbar, the active music home page and current-user presence. Music home has separate read/control/submit grants; see [MUSIC_HOME.md](../../Mcp/MUSIC_HOME.md). Presence has its own read-only permission and shares the header/taskbar service; see [PRESENCE.md](../../Mcp/PRESENCE.md). This coverage is not a general music-detail/profile/backend API. Appearance, navigation and taskbar pinning each have separate read/control grants. Existing grants never imply permission for a new domain.

Opening a music/profile/settings page through navigation is not permission to read all its business data, change settings, play music or submit a form. Full feature-by-feature business MCP coverage has **not** been implemented. Extend narrowly when adding new behavior, and use the same underlying service/operation as the UI.

Main-window presentation also has independent Window read/control grants. They
expose availability/fullscreen/control visibility and an explicit fullscreen
setting only; they do not authorize arbitrary window geometry, closing or
minimization. See [WINDOW_PRESENTATION.md](../../Mcp/WINDOW_PRESENTATION.md).

Pairing can persist across restarts, but only the exact user-approved scopes are restored. Discovery may be public; protected calls are not. No test may approve a real connection, enter real account details or click the Windows pin confirmation just to make a test pass.

## Headless verification map

Run from this repository root. Most suites are console applications using production-linked code; do not assume `dotnet test` discovers them. Choose checks relevant to the change and avoid concurrently building the same project/configuration from multiple agents.

```powershell
dotnet run --project WindowsSearch/Mizon.Windows.Activation.Tests -c Release
dotnet run --project Taskbar/Mizon.Windows.TaskbarPin.Tests -c Release
dotnet run --project Mcp/Mizon.Windows.Mcp.Tests -c Release -- --isolated
dotnet run --project Mcp/Mizon.Windows.Mcp.AccessTests -c Release
dotnet run --project Mcp/Mizon.Windows.Discovery.Tests -c Release --no-launch-profile
dotnet run --project WindowsSearch/Mizon.Windows.Shortcut.Tests -c Release
```

The default MCP commands above use synthetic consent and isolated pipes/stores. They do not approve the configured user connection or submit real forms. The discovery lifecycle suite uses ephemeral test UDP ports rather than the production discovery port.

Additional suites have specific requirements:

- [Palette provider tests](../../WindowsSearch/Mizon.Windows.PaletteProvider.Tests/README.md): native ABI, streaming, parser and optionally registered/live backend integration. Read the README before using `--remote`, `--live-api` or other opt-in modes; these have different runtime/data effects.
- `WindowsSearch/Mizon.Windows.PaletteProvider.RendererTests`: installed WinUI/Windows renderer diagnostics, not a generic headless replacement for visual testing.
- [Shortcut tests](../../WindowsSearch/Mizon.Windows.Shortcut.Tests/README.md): isolated temporary fixtures; do not create real desktop shortcuts just for regression checks.
- `WindowsSearch/Mizon.MediaDownloader.Tests`: shared downloader/cache regression coverage. Inspect its entry point and fixtures before choosing a live transfer.

Build the WinUI application with Visual Studio MSBuild and the required Windows tooling. On the current machine the installation is Visual Studio 18 Insiders; use discovery rather than hardcoding that installation on another machine:

```powershell
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$msbuild = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find 'MSBuild/**/Bin/MSBuild.exe' | Select-Object -First 1
& $msbuild Mizon.Windows.csproj /t:Build /m:2 /p:Configuration=Debug /p:Platform=x64 /p:GenerateAppxPackageOnBuild=false /p:AppxPackageSigningEnabled=false /nologo
```

For a real app run, use the user's existing Visual Studio solution/startup profile when requested. It may start both client and backend. Do not launch a second Visual Studio instance or kill the user's active debugging session merely to verify a build. Do not run Windows UI automation unless the user requests it.

## NativeAOT and performance boundaries

`Mcp/Mizon.Windows.PerformanceBenchmarks` measures production deep-link parsing
and source-generated MCP JSON/framing with synthetic data, outside the WinUI host.
Its README documents warmup, raw samples, allocation units and baseline isolation.
It cannot establish GUI working set, rendering, playback or native window performance.

Use typed DTOs, source-generated JSON, explicit dispatch and existing C#/WinRT projection patterns. Keep UI changes on the dispatcher and I/O asynchronous. Propagate cancellation, unsubscribe events, cancel outstanding work and release native/video/composition resources at the actual owner's lifetime boundary.

An AOT-friendly source change is not proof of a successful native publication. Pure activation and taskbar pin service suites can be published with `-r win-x64 -p:PublishAot=true`; this tests their included production logic, not the entire WinUI application or the real Windows pin dialog. The main Debug build is not a full native publish. Some projects/integrations have their own publication modes; inspect each project file and report precisely what was built/run.

Do not quote memory or latency improvements without measurements from the same scenario and configuration. Debugger, Windows UI/runtime allocations, decoded media, GPU resources and managed heap sizes are different quantities. Respect the user's preference to preserve UI design and playback behavior while optimizing.

## Windows integration caveats

- Taskbar **pinning** uses official `TaskbarManager`, checks fresh OS pin state and requires a foreground user confirmation. It does not use the independent profile companion's taskbar embedding mechanism. Never substitute a persistent application flag for the OS pin state.
- `Taskbar/Mizon.Windows.Taskbar` is the independent profile/mood companion. Its lifetime intentionally differs from the main window. Its native hosting and the separate widgets experiment are OS-sensitive; inspect their own code/docs before changing them.
- ExplorerCommands runs as a separate COM integration. Manifest, architecture, built payload and registered/loaded version must agree. Do not edit generated AppX manifests or assume a successful build replaced a loaded shell binary.
- The new Palettes/System Composer Search integration depends on the installed Windows runtime and verified ABI. Its supported host controls are not unrestricted application XAML. The superseded Search protocol-indexer/catalogue implementation was removed; do not reintroduce it as a shortcut for a live-search feature.
- Taskbar thumbnail transport buttons belong to the main app preview, not the independent profile companion. JumpList, PiP, App Actions, deep links and taskbar pinning are separate integration surfaces with distinct lifecycle requirements.

## Handoff expectations

Record changed behavior, owned paths, affected repositories/contracts, checks actually executed, unverified UI/consent-dependent cases and known limits. Link source documents rather than copying stale inventories. Finished task changes need an accurate commit and push under the standing user instruction; leave another agent's unfinished changes untouched.
