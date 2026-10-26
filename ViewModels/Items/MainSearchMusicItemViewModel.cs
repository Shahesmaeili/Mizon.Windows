namespace Mizon.Windows.ViewModels.Items;

public sealed class MainSearchMusicItemViewModel : Mizon.Windows.ViewModels.ViewModelBase
{
    public System.Guid PublicId { get; init; }
    public string Title { get; init; } = null!;
    public string? PersianTitle { get; init; }
    public System.Guid? CoverMediaPublicId { get; init; }

    public string? CoverImagePath { get => field; set => SetField(ref field, value); }
}
