using Mizon.Windows.Services;
using System.ComponentModel;
using System.Globalization;

namespace Mizon.Windows.UserControls;

public sealed partial class StoryViewer
{
    public LocalizationService Localization => LocalizationService.Current;
    private bool _localizationAttached;
    private string? _errorResourceKey;
    private string Text(string key) => Localization.GetString("MusicSurface/" + key);

    private void AttachLocalization()
    {
        if (_localizationAttached) return;
        _localizationAttached = true;
        Localization.PropertyChanged += LanguageChanged;
    }
    private void DetachLocalization()
    {
        if (!_localizationAttached) return;
        _localizationAttached = false;
        Localization.PropertyChanged -= LanguageChanged;
    }
    private void LanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(LocalizationService.Revision) || _closing || _closed) return;
        UpdatePause();
        UpdateLike();
        if (_detail is { } detail) UpdateViews(detail.ViewsCount);
        if (_errorResourceKey is { } key) ErrorText.Text = Text(key);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(NextButton,
            Text(_index + 1 == _stories.Count ? "FinishStories" : "NextStory"));
        if (_clientDialog is { } dialog)
        {
            dialog.CloseButtonText = Text("Close");
            dialog.FlowDirection = Localization.FlowDirection;
            dialog.FontFamily = Localization.TextFontFamily;
        }
        foreach (var (button, widget) in _widgets)
        {
            button.FontFamily = Localization.TextFontFamily;
            button.FlowDirection = Localization.FlowDirection;
            button.Language = Localization.LanguageTag;
            var title = widget.Music?.Title ?? $"{widget.Client?.FirstName} {widget.Client?.LastName}".Trim();
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,
                Text(widget.Music is null ? "OpenProfilePrefix" : "OpenMusicPrefix") + title);
        }
    }
}
