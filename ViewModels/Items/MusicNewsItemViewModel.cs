using Microsoft.UI.Xaml.Media.Imaging;
using Mizon.Windows.Services;
using System;

namespace Mizon.Windows.ViewModels.Items;

// Local editorial samples; neither dates nor announcements are presented as live news.
public sealed partial class MusicNewsItemViewModel : ViewModelBase
{
    private BitmapImage? _image;
    private readonly int _index;
    private readonly int _readingMinutes;
    public LocalizationService Localization => LocalizationService.Current;

    private MusicNewsItemViewModel(int index, int readingMinutes, string imagePath)
    {
        _index = index;
        _readingMinutes = readingMinutes;
        ImagePath = imagePath;
    }

    public string Title => Localization.GetString($"MusicWidgets/News{_index}Title");
    public string Summary => Localization.GetString($"MusicWidgets/News{_index}Summary");
    public string Body => Localization.GetString($"MusicWidgets/News{_index}Body");
    public string Category => Localization.GetString($"MusicWidgets/News{_index}Category");
    public string EditionLabel => Localization.Format("MusicWidgets/NewsEdition", _index + 1);
    public string ReadingTimeText => Localization.Format("MusicWidgets/NewsMinutes", _readingMinutes);
    public string ImagePath { get; }
    // The UI requests only displayed slides; changing language retains decoded artwork.
    public BitmapImage Image => _image ??= new BitmapImage { DecodePixelWidth = 720, UriSource = new Uri(ImagePath) };
    public string AutomationName => Localization.Format("MusicWidgets/NewsAutomation", Category, Title, Summary, ReadingTimeText);

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Body));
        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(EditionLabel));
        OnPropertyChanged(nameof(ReadingTimeText));
        OnPropertyChanged(nameof(AutomationName));
    }

    public static MusicNewsItemViewModel[] CreateSamples() =>
    [
        new(0, 2, "ms-appx:///Assets/Discovery/slow-motion.jpg"),
        new(1, 3, "ms-appx:///Assets/Discovery/artist-stage-02.jpg"),
        new(2, 2, "ms-appx:///Assets/Discovery/city-lights.jpg"),
        new(3, 2, "ms-appx:///Assets/Discovery/quiet-room.jpg")
    ];
}
