namespace Mizon.Windows.ViewModels.Items;

public sealed partial class MainSearchMusicItemViewModel : Mizon.Windows.ViewModels.ViewModelBase
{
    public System.Guid PublicId { get; init; }
    public string Title { get; init; } = null!;
    public string? PersianTitle { get; init; }
    public System.Guid? CoverMediaPublicId { get; init; }

    public string DisplayTitle => string.IsNullOrWhiteSpace(PersianTitle) ? Title : PersianTitle;
    public string Subtitle => string.IsNullOrWhiteSpace(PersianTitle) ? "موسیقی در میزون" : Title;
    public Microsoft.UI.Xaml.Media.ImageSource? CoverImage { get => field; private set => SetField(ref field, value); }
    // Only the active preview requests a larger decode; list thumbnails stay small.
    public Microsoft.UI.Xaml.Media.ImageSource? PreviewImage => Common.ImageSourceFactory.Create(CoverImagePath, 480);
    public string? CoverImagePath
    {
        get => field;
        set
        {
            if (!SetField(ref field, value)) return;
            CoverImage = Common.ImageSourceFactory.Create(value, 144);
            OnPropertyChanged(nameof(PreviewImage));
        }
    }
    public override string ToString() => DisplayTitle;
}
