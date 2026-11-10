using Mizon.Windows.Mcp.Contracts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

// Called exclusively on the access service's UI dispatcher. Uses the same
// preference service as Settings, including persistence and platform fallback.
internal sealed class McpAppearanceService(AppearanceSettingsService appearance) : IMcpAppearanceService
{
    public Task<McpResponse> ExecuteAsync(McpRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.Operation == "set_appearance")
        {
            if (request.Appearance is not { } preferences
                || preferences.Theme is null && preferences.Backdrop is null
                || preferences.Theme is { } theme && !Enum.IsDefined(theme)
                || preferences.Backdrop is { } backdrop && !Enum.IsDefined(backdrop))
                return Task.FromResult(new McpResponse { Code = "invalid_arguments", Message = "Choose a listed theme or backdrop." });
            token.ThrowIfCancellationRequested();
            appearance.SetPreferences(
                preferences.Theme is { } selectedTheme ? (AppearanceThemeMode)(int)selectedTheme : null,
                preferences.Backdrop is { } selectedBackdrop ? (AppearanceBackdropMode)(int)selectedBackdrop : null);
        }
        else if (request.Operation != "get_appearance")
            return Task.FromResult(new McpResponse { Code = "unsupported_operation", Message = "Unsupported appearance operation." });

        var state = appearance.State;
        return Task.FromResult(new McpResponse
        {
            Success = true,
            Code = request.Operation == "get_appearance" ? "appearance_state" : state.IsPreferenceSaved ? "appearance_updated" : "appearance_not_saved",
            Message = request.Operation == "get_appearance" ? "Current appearance preferences and effective window state."
                : state.IsPreferenceSaved ? "Appearance preferences applied and saved." : "Appearance applied for this session; the preference could not be saved.",
            Appearance = new()
            {
                Theme = (McpAppearanceTheme)(int)state.ThemeMode,
                EffectiveTheme = (McpAppearanceTheme)(int)state.EffectiveTheme,
                Backdrop = (McpAppearanceBackdrop)(int)state.BackdropMode,
                EffectiveBackdrop = state.EffectiveBackdrop is { } effective ? (McpAppearanceBackdrop)(int)effective : null,
                FallbackReason = (McpAppearanceFallback)(int)state.FallbackReason,
                IsHighContrast = state.IsHighContrast,
                IsTransparencyEnabled = state.IsTransparencyEnabled,
                IsMicaSupported = state.IsMicaSupported,
                IsAcrylicSupported = state.IsAcrylicSupported,
                IsPreferenceSaved = state.IsPreferenceSaved,
                IsWindowAttached = state.IsWindowAttached
            }
        });
    }
}
