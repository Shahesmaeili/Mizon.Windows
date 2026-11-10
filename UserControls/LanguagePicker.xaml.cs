using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using System.ComponentModel;

namespace Mizon.Windows.UserControls;

public sealed partial class LanguagePicker : UserControl
{
    public LocalizationService Localization => LocalizationService.Current;
    private bool _synchronizing;

    public LanguagePicker()
    {
        InitializeComponent();
        Loaded += Picker_Loaded;
        Unloaded += Picker_Unloaded;
    }

    private void Picker_Loaded(object sender, RoutedEventArgs args)
    {
        Localization.PropertyChanged -= LocalizationChanged;
        Localization.PropertyChanged += LocalizationChanged;
        Bindings.Update();
        SynchronizeSelection();
    }

    private void Picker_Unloaded(object sender, RoutedEventArgs args)
    {
        Localization.PropertyChanged -= LocalizationChanged;
        Bindings.StopTracking();
    }

    private void LocalizationChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(LocalizationService.Revision))
            SynchronizeSelection();
    }

    private void SynchronizeSelection()
    {
        _synchronizing = true;
        try { LanguageSelector.SelectedIndex = Localization.IsPersian ? 0 : 1; }
        finally { _synchronizing = false; }
    }

    private void LanguageSelector_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_synchronizing || !IsLoaded || LanguageSelector.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        if (Localization.LanguageTag == tag) return;
        Localization.SetLanguage(tag);
        AccessibilityAnnouncements.Announce(this, Localization.GetString("LanguageChangedAnnouncement"), "Mizon.Language.Changed");
    }
}
