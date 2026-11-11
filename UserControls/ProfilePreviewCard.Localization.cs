using Mizon.Windows.Services;
using System.ComponentModel;

namespace Mizon.Windows.UserControls;

public sealed partial class ProfilePreviewCard
{
    public LocalizationService Localization => LocalizationService.Current;
    private void LanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(LocalizationService.Revision) && IsLoaded) SetProfile(_profile);
    }
}
