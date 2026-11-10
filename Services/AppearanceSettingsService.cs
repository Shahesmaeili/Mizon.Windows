using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage;
using System;
using System.ComponentModel;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Services;

public enum AppearanceThemeMode { System, Light, Dark }
public enum AppearanceBackdropMode { Acrylic, AcrylicBase, AcrylicThin, Mica, MicaAlt }
public enum AppearanceFallbackReason { None, WindowUnavailable, HighContrast, TransparencyDisabled, Unsupported, Unavailable }

public sealed record AppearanceState
{
    public AppearanceThemeMode ThemeMode { get; init; }
    public AppearanceThemeMode EffectiveTheme { get; init; }
    public AppearanceBackdropMode BackdropMode { get; init; }
    public AppearanceBackdropMode? EffectiveBackdrop { get; init; }
    public AppearanceFallbackReason FallbackReason { get; init; }
    public bool IsHighContrast { get; init; }
    public bool IsTransparencyEnabled { get; init; }
    public bool IsMicaSupported { get; init; }
    public bool IsAcrylicSupported { get; init; }
    public bool IsPreferenceSaved { get; init; }
    public bool IsWindowAttached { get; init; }
}

/// <summary>Owns the main window's saved theme and material, shared by Settings and MCP.</summary>
public sealed class AppearanceSettingsService : INotifyPropertyChanged
{
    private const string ThemeModeSettingKey = "Appearance.ThemeMode";
    private const string BackdropModeSettingKey = "Appearance.BackdropMode";
    private static readonly Lazy<AppearanceSettingsService> Instance = new(() => new AppearanceSettingsService());
    private AppearanceState _state;
    private event EventHandler? PreferencesChanged;
    public static AppearanceSettingsService Current => Instance.Value;

    private AppearanceSettingsService()
    {
        ThemeModeIndex = ReadPreference(ThemeModeSettingKey, 2, 0);
        BackdropModeIndex = ReadPreference(BackdropModeSettingKey, 4, 0);
        _state = new()
        {
            ThemeMode = (AppearanceThemeMode)ThemeModeIndex,
            EffectiveTheme = (AppearanceThemeMode)ThemeModeIndex,
            BackdropMode = (AppearanceBackdropMode)BackdropModeIndex,
            FallbackReason = AppearanceFallbackReason.WindowUnavailable,
            IsPreferenceSaved = true
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public int ThemeModeIndex { get; private set; }
    public int BackdropModeIndex { get; private set; }
    public AppearanceState State => _state;
    public void SetThemeMode(int index) => SetPreferences((AppearanceThemeMode)index);
    public void SetBackdropMode(int index) => SetPreferences(backdropMode: (AppearanceBackdropMode)index);

    /// <summary>Call on the window's dispatcher. Changes both preferences as one live update.</summary>
    public void SetPreferences(AppearanceThemeMode? themeMode = null, AppearanceBackdropMode? backdropMode = null)
    {
        if (themeMode is { } theme && (int)theme is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(themeMode));
        if (backdropMode is { } backdrop && (int)backdrop is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(backdropMode));
        var themeChanged = themeMode is { } selectedTheme && ThemeModeIndex != (int)selectedTheme;
        var backdropChanged = backdropMode is { } selectedBackdrop && BackdropModeIndex != (int)selectedBackdrop;
        if (!themeChanged && !backdropChanged && _state.IsPreferenceSaved) return;
        if (themeMode is { } newTheme) ThemeModeIndex = (int)newTheme;
        if (backdropMode is { } newBackdrop) BackdropModeIndex = (int)newBackdrop;
        var saved = true;
        try
        {
            var values = ApplicationData.GetDefault().LocalSettings.Values;
            values[ThemeModeSettingKey] = ThemeModeIndex;
            values[BackdropModeSettingKey] = BackdropModeIndex;
        }
        catch (Exception error)
        {
            saved = false;
            AppLog.Information($"Appearance preferences could not be saved ({error.GetType().Name}).");
        }
        UpdateState(_state with
        {
            ThemeMode = (AppearanceThemeMode)ThemeModeIndex,
            BackdropMode = (AppearanceBackdropMode)BackdropModeIndex,
            IsPreferenceSaved = saved
        });
        if (themeChanged) OnPropertyChanged(nameof(ThemeModeIndex));
        if (backdropChanged) OnPropertyChanged(nameof(BackdropModeIndex));
        PreferencesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Attach to the main window after InitializeComponent; dispose when it closes.</summary>
    public IDisposable Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!window.DispatcherQueue.HasThreadAccess)
            throw new InvalidOperationException("Attach appearance settings on the window's UI thread.");
        return new WindowAttachment(this, window);
    }

    private static int ReadPreference(string key, int maximum, int fallback)
    {
        try
        {
            if (ApplicationData.GetDefault().LocalSettings.Values.TryGetValue(key, out var value)
                && value is int mode && mode >= 0 && mode <= maximum) return mode;
        }
        catch (Exception error)
        {
            AppLog.Information($"Appearance preference could not be read ({error.GetType().Name}).");
        }
        return fallback;
    }

    private void UpdateState(AppearanceState state)
    {
        if (_state == state) return;
        _state = state;
        OnPropertyChanged(nameof(State));
    }
    private void OnPropertyChanged(string property) => PropertyChanged?.Invoke(this, new(property));

    private sealed class WindowAttachment : IDisposable
    {
        private readonly AppearanceSettingsService _appearance;
        private readonly DispatcherQueue _dispatcher;
        private readonly UISettings _systemSettings = new();
        private readonly AccessibilitySettings _accessibility = new();
        private readonly Panel? _root;
        private readonly FrameworkElement? _themeRoot;
        private readonly Brush? _originalBackground;
        private readonly bool _micaSupported = MicaController.IsSupported();
        private readonly bool _acrylicSupported = DesktopAcrylicController.IsSupported();
        private Window? _window;
        private SystemBackdrop? _backdrop;
        private AppearanceBackdropMode? _appliedBackdrop;
        private bool _observingSystemColors;
        private bool _observingAdvancedEffects;
        private bool _observingHighContrast;
        private bool _observingActualTheme;
        private bool _applyingAppearance;
        private bool _disposed;

        public WindowAttachment(AppearanceSettingsService appearance, Window window)
        {
            _appearance = appearance;
            _window = window;
            _dispatcher = window.DispatcherQueue;
            _root = window.Content as Panel;
            _themeRoot = window.Content as FrameworkElement;
            _originalBackground = _root?.Background;
            _appearance.PreferencesChanged += PreferencesChanged;
            // Some desktop hosts expose the settings getters but reject individual
            // notification registrations (for example HighContrastChanged, 0x80070490).
            // Observe every available source without making optional events a launch dependency.
            _observingSystemColors = TrySubscribe(
                () => _systemSettings.ColorValuesChanged += SystemColorsChanged, nameof(UISettings.ColorValuesChanged));
            _observingAdvancedEffects = TrySubscribe(
                () => _systemSettings.AdvancedEffectsEnabledChanged += AdvancedEffectsChanged, nameof(UISettings.AdvancedEffectsEnabledChanged));
            _observingHighContrast = TrySubscribe(
                () => _accessibility.HighContrastChanged += HighContrastChanged, nameof(AccessibilitySettings.HighContrastChanged));
            if (_themeRoot is not null)
                _observingActualTheme = TrySubscribe(
                    () => _themeRoot.ActualThemeChanged += ActualThemeChanged, nameof(FrameworkElement.ActualThemeChanged));
            window.Closed += WindowClosed;
            try { ApplyAppearance(); }
            catch { Dispose(); throw; }
        }
        private void PreferencesChanged(object? sender, EventArgs args) => QueueApply();
        private void SystemColorsChanged(UISettings sender, object args) => QueueApply();
        private void AdvancedEffectsChanged(UISettings sender, object args) => QueueApply();
        private void HighContrastChanged(AccessibilitySettings sender, object args) => QueueApply();
        private void ActualThemeChanged(FrameworkElement sender, object args) => QueueApply();

        private static bool TrySubscribe(Action subscribe, string notification)
        {
            try { subscribe(); return true; }
            catch (Exception error)
            {
                AppLog.Information($"Appearance notification {notification} is unavailable ({error.GetType().Name}).");
                return false;
            }
        }

        private static void Unsubscribe(ref bool subscribed, Action unsubscribe, string notification)
        {
            if (!subscribed) return;
            subscribed = false;
            try { unsubscribe(); }
            catch (Exception error)
            {
                AppLog.Information($"Appearance notification {notification} could not be detached ({error.GetType().Name}).");
            }
        }
        private void QueueApply()
        {
            if (_disposed) return;
            if (_dispatcher.HasThreadAccess) ApplyAppearance();
            else _dispatcher.TryEnqueue(ApplyAppearance);
        }

        private void ApplyAppearance()
        {
            if (_disposed || _applyingAppearance) return;
            _applyingAppearance = true;
            try { ApplyAppearanceCore(); }
            finally { _applyingAppearance = false; }
        }

        private void ApplyAppearanceCore()
        {
            if (_disposed || _window is null) return;
            var highContrast = _accessibility.HighContrast;
            var theme = _appearance.ThemeModeIndex switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => GetSystemTheme()
            };
            // Default inherits App's startup request, so resolve the Windows theme explicitly.
            if (_themeRoot is not null && _themeRoot.RequestedTheme != theme) _themeRoot.RequestedTheme = theme;
            var mode = (AppearanceBackdropMode)_appearance.BackdropModeIndex;
            var transparency = _systemSettings.AdvancedEffectsEnabled;
            var supported = mode is AppearanceBackdropMode.Mica or AppearanceBackdropMode.MicaAlt
                ? _micaSupported : _acrylicSupported;
            var fallback = highContrast ? AppearanceFallbackReason.HighContrast
                : !transparency ? AppearanceFallbackReason.TransparencyDisabled
                : !supported ? AppearanceFallbackReason.Unsupported : AppearanceFallbackReason.None;
            AppearanceBackdropMode? effective = fallback == AppearanceFallbackReason.None ? mode : null;
            if (_appliedBackdrop != effective || (effective is not null && _backdrop is null))
            {
                ReleaseBackdrop();
                if (effective is not null)
                {
                    try
                    {
                        _backdrop = mode switch
                        {
                            AppearanceBackdropMode.Mica => new MicaBackdrop { Kind = MicaKind.Base },
                            AppearanceBackdropMode.MicaAlt => new MicaBackdrop { Kind = MicaKind.BaseAlt },
                            AppearanceBackdropMode.AcrylicBase => new AppearanceAcrylicBackdrop(DesktopAcrylicKind.Base),
                            AppearanceBackdropMode.AcrylicThin => new AppearanceAcrylicBackdrop(DesktopAcrylicKind.Thin),
                            _ => new AppearanceAcrylicBackdrop(DesktopAcrylicKind.Default)
                        };
                        _window.SystemBackdrop = _backdrop;
                        _appliedBackdrop = mode;
                    }
                    catch (Exception error)
                    {
                        ReleaseBackdrop();
                        effective = null;
                        fallback = AppearanceFallbackReason.Unavailable;
                        AppLog.Information($"Window material is unavailable ({error.GetType().Name}).");
                    }
                }
            }
            if (_root is not null)
            {
                var fallbackColor = highContrast ? _systemSettings.GetColorValue(UIColorType.Background)
                    : theme == ElementTheme.Dark ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 243, 243, 243);
                if (effective is not null) _root.Background = _originalBackground;
                else if (_root.Background is not SolidColorBrush brush || brush.Color != fallbackColor)
                    _root.Background = new SolidColorBrush(fallbackColor);
            }
            ApplyTitleBar(theme, highContrast);
            _appearance.UpdateState(_appearance.State with
            {
                EffectiveTheme = theme == ElementTheme.Dark ? AppearanceThemeMode.Dark : AppearanceThemeMode.Light,
                EffectiveBackdrop = effective,
                FallbackReason = fallback,
                IsHighContrast = highContrast,
                IsTransparencyEnabled = transparency,
                IsMicaSupported = _micaSupported,
                IsAcrylicSupported = _acrylicSupported,
                IsWindowAttached = true
            });
        }

        private void ApplyTitleBar(ElementTheme theme, bool highContrast)
        {
            if (_window is null || !AppWindowTitleBar.IsCustomizationSupported()) return;
            var foreground = highContrast ? _systemSettings.GetColorValue(UIColorType.Foreground)
                : theme == ElementTheme.Dark ? Colors.White : Colors.Black;
            var background = highContrast ? _systemSettings.GetColorValue(UIColorType.Background) : Colors.Transparent;
            var inactiveForeground = highContrast ? foreground : Color.FromArgb(153, foreground.R, foreground.G, foreground.B);
            var titleBar = _window.AppWindow.TitleBar;
            titleBar.ForegroundColor = foreground;
            titleBar.InactiveForegroundColor = inactiveForeground;
            titleBar.BackgroundColor = background;
            titleBar.InactiveBackgroundColor = background;
            titleBar.ButtonBackgroundColor = background;
            titleBar.ButtonInactiveBackgroundColor = background;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonInactiveForegroundColor = inactiveForeground;
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonPressedForegroundColor = foreground;
            titleBar.ButtonHoverBackgroundColor = highContrast ? background : Color.FromArgb(24, foreground.R, foreground.G, foreground.B);
            titleBar.ButtonPressedBackgroundColor = highContrast ? background : Color.FromArgb(40, foreground.R, foreground.G, foreground.B);
        }
        private ElementTheme GetSystemTheme()
        {
            var background = _systemSettings.GetColorValue(UIColorType.Background);
            var luminance = (299 * background.R) + (587 * background.G) + (114 * background.B);
            return luminance >= 128000 ? ElementTheme.Light : ElementTheme.Dark;
        }
        private void ReleaseBackdrop()
        {
            var backdrop = _backdrop;
            _backdrop = null;
            _appliedBackdrop = null;
            try
            {
                if (_window is not null && backdrop is not null) _window.SystemBackdrop = null;
            }
            catch (Exception error)
            {
                AppLog.Information($"Window material could not be detached ({error.GetType().Name}).");
            }
            finally
            {
                if (backdrop is AppearanceAcrylicBackdrop acrylic) acrylic.Dispose();
            }
        }
        private void WindowClosed(object sender, WindowEventArgs args) => Dispose();
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _appearance.PreferencesChanged -= PreferencesChanged;
            Unsubscribe(ref _observingSystemColors,
                () => _systemSettings.ColorValuesChanged -= SystemColorsChanged, nameof(UISettings.ColorValuesChanged));
            Unsubscribe(ref _observingAdvancedEffects,
                () => _systemSettings.AdvancedEffectsEnabledChanged -= AdvancedEffectsChanged, nameof(UISettings.AdvancedEffectsEnabledChanged));
            Unsubscribe(ref _observingHighContrast,
                () => _accessibility.HighContrastChanged -= HighContrastChanged, nameof(AccessibilitySettings.HighContrastChanged));
            if (_themeRoot is not null)
                Unsubscribe(ref _observingActualTheme,
                    () => _themeRoot.ActualThemeChanged -= ActualThemeChanged, nameof(FrameworkElement.ActualThemeChanged));
            ReleaseBackdrop();
            if (_window is not null)
            {
                _window.Closed -= WindowClosed;
                _window = null;
            }
            _appearance.UpdateState(_appearance.State with
            {
                IsWindowAttached = false,
                EffectiveBackdrop = null,
                FallbackReason = AppearanceFallbackReason.WindowUnavailable
            });
        }
    }
}

// DesktopAcrylicBackdrop has no Kind selector. Keep the prior glass defaults,
// using the native controller and configuration with deterministic cleanup.
internal sealed partial class AppearanceAcrylicBackdrop(DesktopAcrylicKind kind) : SystemBackdrop, IDisposable
{
    private DesktopAcrylicController? _controller;
    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        Dispose();
        var controller = new DesktopAcrylicController { Kind = kind };
        try
        {
            controller.SetSystemBackdropConfiguration(GetDefaultSystemBackdropConfiguration(target, xamlRoot));
            controller.AddSystemBackdropTarget(target);
            _controller = controller;
        }
        catch
        {
            controller.Dispose();
            throw;
        }
    }
    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot);
        _controller?.SetSystemBackdropConfiguration(GetDefaultSystemBackdropConfiguration(target, xamlRoot));
    }
    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        Dispose();
        base.OnTargetDisconnected(target);
    }
    public void Dispose()
    {
        _controller?.Dispose();
        _controller = null;
    }
}
