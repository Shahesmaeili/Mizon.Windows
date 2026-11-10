using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Storage;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

namespace Mizon.Windows.Services;

/// <summary>
/// Localized UI resources. Call SetLanguage on the UI thread; bindings refresh in place.
/// Explicit MRT contexts keep the selected UI language independent of Windows regional settings.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    private const string LanguageSettingKey = "UiLanguage";
    private const string PersianLanguageTag = "fa-IR";
    private const string EnglishLanguageTag = "en-US";
    private static readonly Lazy<LocalizationService> Instance = new(() => new LocalizationService());
    private readonly Lazy<ResourceManager> _resourceManager = new(() => new ResourceManager());
    private readonly Dictionary<string, ResourceContext> _contexts = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Language, string Key), string> _strings = new();
    private readonly Dictionary<string, ResourceMap> _resourceMaps = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unavailableMaps = new(StringComparer.Ordinal);
    private FontFamily? _persianFont;
    private FontFamily? _englishFont;

    public static LocalizationService Current => Instance.Value;

    private LocalizationService()
    {
        LanguageTag = ReadLanguage();
        UpdateUICulture();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string LanguageTag { get; private set; }
    public bool IsPersian => LanguageTag == PersianLanguageTag;
    public FlowDirection FlowDirection => IsPersian ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    // Start follows FlowDirection: the right edge in Persian and the left edge in English.
    public TextAlignment TextAlignment => TextAlignment.Start;
    public FontFamily TextFontFamily => IsPersian
        ? _persianFont ??= new FontFamily("ms-appx:///Assets/Fonts/IRANSANS.TTF#IRANSans")
        : _englishFont ??= new FontFamily("Segoe UI Variable");

    /// <summary>Pass this value to GetString in one-way x:Bind expressions to track language changes.</summary>
    public int Revision { get; private set; }

    public void SetLanguage(string languageTag)
    {
        var normalized = NormalizeLanguage(languageTag)
            ?? throw new ArgumentException("Supported UI languages are fa-IR and en-US.", nameof(languageTag));
        if (LanguageTag == normalized)
            return;

        LanguageTag = normalized;
        UpdateUICulture();
        SaveLanguage(normalized);
        unchecked { Revision++; }
        OnPropertyChanged(nameof(LanguageTag));
        OnPropertyChanged(nameof(IsPersian));
        OnPropertyChanged(nameof(FlowDirection));
        OnPropertyChanged(nameof(TextAlignment));
        OnPropertyChanged(nameof(TextFontFamily));
        OnPropertyChanged(nameof(Revision));
    }

    public string GetString(string key, int revision) => GetString(key);

    public string GetString(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var cacheKey = (LanguageTag, key);
        if (_strings.TryGetValue(cacheKey, out var cached))
            return cached;

        var value = TryGetString(key, LanguageTag);
        // Retain a readable resource if a translation is accidentally omitted from a future build.
        if (value is null)
            value = TryGetString(key, IsPersian ? EnglishLanguageTag : PersianLanguageTag);
        if (value is null)
        {
            AppLog.Information($"UI resource '{key}' was not found for '{LanguageTag}'.");
            value = key;
        }
        _strings[cacheKey] = value;
        return value;
    }

    public string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.GetCultureInfo(LanguageTag), GetString(key), arguments);

    private string? TryGetString(string key, string language)
    {
        var separator = key.IndexOf('/');
        var mapName = separator < 0 ? "Auth" : key[..separator];
        var resourceKey = separator < 0 ? key : key[(separator + 1)..];
        if (_unavailableMaps.Contains(mapName))
            return null;

        if (!_resourceMaps.TryGetValue(mapName, out var resources))
        {
            try
            {
                resources = _resourceManager.Value.MainResourceMap.GetSubtree(mapName);
                _resourceMaps.Add(mapName, resources);
            }
            catch (Exception error)
            {
                // A missing PRI must not prevent the app from opening, including in the XAML designer.
                _unavailableMaps.Add(mapName);
                AppLog.Information($"Resources '{mapName}' could not be loaded ({error.GetType().Name}).");
                return null;
            }
        }

        try
        {
            if (!_contexts.TryGetValue(language, out var context))
            {
                context = _resourceManager.Value.CreateResourceContext();
                context.QualifierValues["Language"] = language;
                _contexts.Add(language, context);
            }
            return resources.GetValue(resourceKey, context).ValueAsString;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void UpdateUICulture()
    {
        if (!string.Equals(CultureInfo.CurrentUICulture.Name, LanguageTag, StringComparison.OrdinalIgnoreCase))
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(LanguageTag);
        // CurrentCulture remains unchanged: API values, dates, and numeric input retain their conventions.
    }

    private static string ReadLanguage()
    {
        try
        {
            var settings = ApplicationData.GetDefault().LocalSettings;
            if (settings.Values.TryGetValue(LanguageSettingKey, out var value))
                return NormalizeLanguage(value as string) ?? PersianLanguageTag;
        }
        catch (Exception error)
        {
            AppLog.Information($"UI language preference could not be read ({error.GetType().Name}).");
        }
        return PersianLanguageTag;
    }

    private static void SaveLanguage(string language)
    {
        try
        {
            ApplicationData.GetDefault().LocalSettings.Values[LanguageSettingKey] = language;
        }
        catch (Exception error)
        {
            // Switching the current UI still works when local settings are unavailable.
            AppLog.Information($"UI language preference could not be saved ({error.GetType().Name}).");
        }
    }

    private static string? NormalizeLanguage(string? language)
    {
        var normalized = language?.Trim().Replace('_', '-');
        if (string.Equals(normalized, "fa", StringComparison.OrdinalIgnoreCase)
            || normalized?.StartsWith("fa-", StringComparison.OrdinalIgnoreCase) == true)
            return PersianLanguageTag;
        if (string.Equals(normalized, "en", StringComparison.OrdinalIgnoreCase)
            || normalized?.StartsWith("en-", StringComparison.OrdinalIgnoreCase) == true)
            return EnglishLanguageTag;
        return null;
    }

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
