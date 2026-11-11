# MCP is part of feature development

The user requires every new or changed user-facing Mizon feature/page to be
accessible through narrowly scoped MCP read-state/actions and to receive meaningful
MCP integration verification. This is a standing development requirement, not a
claim that existing features are already covered.

Current implemented coverage is **Welcome, Login, Registration, Appearance, declared page navigation,
Mizon taskbar pinning, the active music home page, current-user online presence and main-window presentation**.
Main-window read/control permissions are independent from appearance and music;
see [window presentation](WINDOW_PRESENTATION.md) for its bounded state/actions.
Appearance, navigation and taskbar pinning have independent read/control permissions; authentication grants never authorize them.
Music home has its own read/control/submit permissions. Its bounded cards, featured
player, stories and widget actions are documented in [MUSIC_HOME.md](MUSIC_HOME.md).
It is not a general music-detail, profile, database or backend API surface.
Online presence has an independent read-only scope; see [PRESENCE.md](PRESENCE.md).
Its shared indicator snapshot cannot change focus, publish presence or inspect other users.
Navigation itself exposes location state and opening allowlisted pages/popups only.
Its approval does not authorize domain data, playback or downloads. Extend
coverage with the feature being worked on. Do not silently expose unrelated pages,
all services, all endpoints, or arbitrary code execution.

The configured Codex server is named `mizon`. At the user's explicit request,
approval of a paired client now persists across app/host restarts until revoked
inside Mizon. Initial consent still belongs exclusively to the user. A configured
server/profile or a self-reported name does not itself grant access.

For visual-only styling changes, reuse the existing MCP contract and relevant
checks; do not invent tools or implementation-mirroring tests for colors, margins
or layout. New behavior or observable state requires meaningful MCP coverage.

## Existing implementation map

Paths below are relative to the Windows app repository root:

| Responsibility | Existing path |
| --- | --- |
| Typed request/result DTOs and source-generated JSON | `Mcp/Mizon.Windows.Mcp.Contracts/McpAuthContracts.cs` |
| Bounded current-user pipe protocol | `Mcp/Mizon.Windows.Mcp.Contracts/McpPipeProtocol.cs` |
| Scope and connection permission policy | `Mcp/Mizon.Windows.Mcp.Contracts/McpAccessPolicy.cs` |
| DPAPI-protected client proof and remembered scopes | `Mcp/Mizon.Windows.Mcp.Contracts/McpClientCredentialStore.cs`, `McpRememberedGrantStore.cs` |
| App-owned consent, connection lifetime and IPC dispatch | `Services/McpAccessService.cs` |
| Live authentication-page/business adapter | `Services/McpAuthenticationService.cs` |
| Typed appearance contract and shared-settings adapter | `Mcp/Mizon.Windows.Mcp.Contracts/McpAppearanceContracts.cs`, `Services/McpAppearanceService.cs` |
| Typed navigation contract and shared deep-link adapter | `Mcp/Mizon.Windows.Mcp.Contracts/McpNavigationContracts.cs`, `Services/McpNavigationService.cs`, `Services/AppDeepLinkCatalog.cs` |
| Typed taskbar pin contract and shared profile-menu adapter | `Mcp/Mizon.Windows.Mcp.Contracts/McpTaskbarPinContracts.cs`, `Services/McpTaskbarPinService.cs`, `Services/TaskbarPinService.cs` |
| Typed music home contract, input validation and independent permission flags | `Mcp/Mizon.Windows.Mcp.Contracts/McpMusicHomeContracts.cs`, `McpAccessPolicy.cs`, `McpAuthContracts.cs` |
| Music home adapter and active-page lifetime boundary | `Services/IMcpMusicHomeService.cs`, `Services/McpMusicHomeService.cs`, `Pages/MusicPage.Mcp.cs` |
| Shared featured player, story and widget controls | `UserControls/DiscoveryFeatureHero.Mcp.cs`, `StoryViewer.Mcp.cs`, `ArtistSpotlight.Mcp.cs`, `WeeklyRhythmPanel.Mcp.cs`, `LatestMusicNewsPanel.Mcp.cs`, `MusicDiscoveryRail.Mcp.cs` |
| Stdio host using the official MCP SDK | `Mcp/Mizon.Windows.Mcp/Program.cs` |
| Explicit tool discovery and dispatch | `Mcp/Mizon.Windows.Mcp/MizonAuthTools.cs` |
| Music home tool input schemas and explicit parser | `Mcp/Mizon.Windows.Mcp/MizonMusicHomeTools.cs` |
| Current-user presence contract and read-only adapter | `Mcp/Mizon.Windows.Mcp.Contracts/McpPresenceContracts.cs`, `Services/McpPresenceService.cs`, `Services/ProfilePresenceContracts.cs` |
| Input descriptions and generated output schemas | `Mcp/Mizon.Windows.Mcp/MizonAuthSchemas.cs` |
| Companion-to-app connection and cancellation | `Mcp/Mizon.Windows.Mcp/MizonAuthPipeClient.cs` |
| Consent/settings interface | `UserControls/McpAccessPanel.xaml`, `UserControls/McpConnectionCard.xaml`, `MainWindow.Mcp.cs` |
| Protocol, schema, SDK and permission checks | `Mcp/Mizon.Windows.Mcp.Tests/` |
| App permission/appearance adapter integration checks | `Mcp/Mizon.Windows.Mcp.AccessTests/` |
| Music home contract, SDK dispatch and production adapter checks | `Mcp/Mizon.Windows.Mcp.Tests/MusicHomeContractTests.cs`, `MusicHomeDispatchTests.cs`, `Mcp/Mizon.Windows.Mcp.AccessTests/MusicHomeTests.cs` |
| Live music-widget localization model checks | `Mcp/Mizon.Windows.MusicHome.LocalizationTests/` |
| Client launcher and setup | `Mcp/ClientSetup/README.md` |

Backend operations live in
`C:/Users/Ali/source/repos/Mizon.Server.API/Mizon.Server.Logic/Operations`.
Shared contracts and client SDK live in `D:/source/repos/Mizon.API/Mizon.API`
and `D:/source/repos/Mizon.API/Mizon.API.Client`. If these repositories move,
resolve their actual roots before editing; do not create a second copy.

## Implementing a feature

1. Define the minimal observable state and user-intended actions. Add a
   feature-specific typed contract/adapter when extending beyond authentication;
   do not turn the authentication DTO into an arbitrary command envelope.
2. Publish explicit tool names, descriptions, required/optional inputs, enums,
   limits and output schemas through `tools/list`. Keep schema and runtime DTOs
   synchronized. Agents should know the contract without seeing app source.
3. Call the same business service, operation or live-page view model as the UI.
   Preserve its validation, authorization, navigation and lifecycle. Do not
   resolve a second transient page/view model that is disconnected from the UI.
   Marshal UI-owned changes to its dispatcher; keep I/O asynchronous.
4. Add an explicit permission group for each new feature domain in the existing
   in-app gate and consent UI. Authentication read/control approval must never
   implicitly authorize music, settings or other future tools. Within each group,
   distinguish reads, control and consequential submission where appropriate.
   Show the user the new requested scopes before granting access. Discovery may
   list the catalog before approval, but protected data/actions remain gated.
   There must be no MCP operation to approve itself or alter its own scopes.
5. Preserve the user-requested persistent pairing policy. A random credential tied
   to a locally configured host profile restores only the exact scopes approved by
   the user. Never restore by client name, title, version or logo. New domains must
   still get explicit approval; authentication grants do not authorize future
   features. Revocation removes the saved grant and cancels every live session for
   that identity, including work from an older authorization generation. Offline
   approved clients stay visible and revocable. Global disable suspends access
   without forgetting grants. Legacy clients with no pairing proof stay session-only.
   Tool discovery must be side-effect free: return schemas without connecting to
   the app or creating a consent request. Only explicit tool calls connect. Keep
   pipe-availability deadlines separate from the UI-dispatched handshake budget;
   a temporarily busy UI must not cause repeated consent requests.
6. Use source-generated JSON and explicit dispatch compatible with .NET 11 and
   NativeAOT. Avoid reflection-based discovery, dynamic invocation and generic
   shell/SQL/HTTP execution tools. Propagate cancellation and release subscriptions,
   streams, UI bindings and connection resources. Never automatically retry a
   mutation whose result may already have reached the backend.
7. Return sanitized structured outcomes. Distinguish permission, validation,
   unavailable-page, cancellation and backend errors. Read-state DTOs must not
   contain passwords, tokens or cookies; diagnostics must not log secret input
   or raw authentication responses.

## Definition of done

- The feature's intended reads/actions are discoverable with accurate input and
  output schemas; existing callers continue to work or receive an explicit,
  documented contract change.
- Meaningful MCP tests exercise the feature through its actual MCP transport or
  dispatcher boundary and check the observable result, not merely method calls.
  Include applicable pending/denied/revoked and insufficient-scope cases,
  cancellation/errors, restart restoration, offline/live revocation, wrong proof,
  corrupt/failed storage, stale/disconnected sessions and cleanup. New business
  scenarios need coverage beyond the existing authentication smoke checks.
- Reuse existing tests and test doubles where possible. For real app integration,
  use a user-approved connection and only task-authorized changes. Never automate
  consent, create a real account, submit real credentials or mutate real user
  data solely to obtain a green test. Prior task authorization does not remove
  the technical in-app permission gate.
- Run the relevant build, source-generation/AOT checks and MCP integration checks.
  The existing baseline commands are in
  [the protocol test README](Mizon.Windows.Mcp.Tests/README.md). They exercise the
  actual SDK without opening the app, approving consent or submitting a form.
- Headless MCP tests verify functional behavior/state and do not replace visual,
  animation or UI performance checks. They cannot prove layout, hover, connected
  animation, translucency or rendering smoothness. Respect the user's existing
  request not to use Windows UI automation unless they explicitly ask for it.
- Report what was implemented and checked, including any test blocked by missing
  user consent or unavailable services. Do not call unexecuted app integration,
  visual behavior or every existing feature verified.
