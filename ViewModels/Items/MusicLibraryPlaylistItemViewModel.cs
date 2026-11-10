using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Common;
using System;

namespace Mizon.Windows.ViewModels.Items;

public sealed partial class MusicLibraryPlaylistItemViewModel : ViewModelBase
{
    public Guid PublicId { get; init; }
    public string Title { get; private set; } = string.Empty;
    public int MusicsCount { get; private set; }
    public string MusicsCountText => $"{MusicsCount:N0} آهنگ";
    public string AutomationName => $"{Title}، {MusicsCountText}";
    public DateTimeOffset LastUpdatedAt { get; private set; }
    public string UpdatedText => $"به‌روزرسانی: {LastUpdatedAt.ToLocalTime():yyyy/MM/dd HH:mm}";
    public bool CanPlay => MusicsCount > 0;
    public Guid? CoverMediaPublicId { get; private set; }
    public int CoverDecodeWidth { get; init; } = 144;
    public ImageSource? CoverImage { get => field; private set => SetField(ref field, value); }

    internal void Update(string title, int musicsCount, Guid? coverMediaPublicId, DateTimeOffset lastUpdatedAt)
    {
        if (Title != title)
        {
            Title = title;
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(AutomationName));
        }

        if (MusicsCount != musicsCount)
        {
            MusicsCount = musicsCount;
            OnPropertyChanged(nameof(MusicsCountText));
            OnPropertyChanged(nameof(CanPlay));
            OnPropertyChanged(nameof(AutomationName));
        }

        LastUpdatedAt = lastUpdatedAt;
        OnPropertyChanged(nameof(UpdatedText));
        if (CoverMediaPublicId == coverMediaPublicId) return;
        CoverMediaPublicId = coverMediaPublicId;
        CoverImage = null;
    }

    internal void SetCover(string path) => CoverImage = ImageSourceFactory.Create(path, CoverDecodeWidth);
}
