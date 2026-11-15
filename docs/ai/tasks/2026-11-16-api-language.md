# 2026-11-16 Windows API language and localized Jump List

- Owner: Codex
- Branch: `codex/api-language`; base `3213faf`
- Dependencies on main: Mizon.Server.API `805a468`; Mizon.API.DotNet `28516a1`
- Scope: app language preference, SDK registration, shell API callers, Jump List and activation diagnostics

The Windows app passes `LocalizationService.LanguageTag` to the SDK per
request. The setting remains independent of the OS locale. The main app
atomically mirrors the two allowed language tags for Windows Search and Explorer
API calls. The mirror contains only the language tag and defaults to Persian
before it first exists. Existing login error mappings remain available so an
error already on screen immediately follows a language change.

The Jump List reads localized resource keys for titles, descriptions and groups.
It reacts to app language and account state, including the Welcome startup
transition. Arguments and artwork are shared across locales, and removed items
are respected. The app owns and disposes the service.

Validation: WinUI Debug x64 build; Persian and English strings verified in PRI;
activation/Jump List suite and Palette provider ABI diagnostics passed. Native
taskbar display and physical-controller testing were not run. The separate
Welcome header task records its MCP and controller checks.
