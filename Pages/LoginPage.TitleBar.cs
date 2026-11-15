using Microsoft.UI.Xaml;
using Mizon.Windows.Common;

namespace Mizon.Windows.Pages;

public sealed partial class LoginPage
{
    private AuthHeaderLanguageRegion? _languageRegion;

    private void AttachLanguagePicker() =>
        _languageRegion = new AuthHeaderLanguageRegion(this, LanguagePickerHost, CaptionInset, _hostAppWindow,
            inCaption => CompactBrand.HorizontalAlignment = inCaption ? HorizontalAlignment.Center : HorizontalAlignment.Right);

    private void DetachLanguagePicker()
    {
        _languageRegion?.Dispose();
        _languageRegion = null;
    }
}
