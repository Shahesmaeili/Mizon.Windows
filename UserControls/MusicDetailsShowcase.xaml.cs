using Microsoft.UI.Xaml.Controls;
using Mizon.Windows.Services;

namespace Mizon.Windows.UserControls;

public sealed partial class MusicDetailsShowcase : UserControl
{
    public MusicDetailsShowcase() => InitializeComponent();

    public LocalizationService Localization => LocalizationService.Current;
    private string Text(string key, int revision) => Localization.GetString("MusicDetails/" + key, revision);
}
