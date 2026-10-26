using System;

namespace Mizon.Windows.ViewModels.Items;

public sealed class MusicPlaylistItemViewModel : ViewModelBase
{
    public Guid PublicId { get; init; }
    public string Title { get => field; set => SetField(ref field, value); } = null!;
    public int MusicsCount
    {
        get => field;
        set
        {
            if (SetField(ref field, value))
                OnPropertyChanged(nameof(MusicsCountText));
        }
    }
    public string MusicsCountText => $"{MusicsCount} آهنگ";
    public bool ContainsCurrentMusic { get => field; set => SetField(ref field, value); }
    public Guid? LastMusicCoverMediaPublicId { get => field; set => SetField(ref field, value); }
    public string? LastMusicCoverImagePath { get => field; set => SetField(ref field, value); }
}
