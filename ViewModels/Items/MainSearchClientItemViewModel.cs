namespace Mizon.Windows.ViewModels.Items;

public sealed partial class MainSearchClientItemViewModel : Mizon.Windows.ViewModels.ViewModelBase
{
    public System.Guid PublicId { get; init; }
    public string FirstName { get; init; } = null!;
    public string LastName { get; init; } = null!;
    public string? NickName { get; init; }
    public string? Username { get; init; }
    public System.Guid? ProfileMediaPublicId { get; init; }

    public string DisplayName => !string.IsNullOrWhiteSpace(NickName) ? NickName
        : !string.IsNullOrWhiteSpace($"{FirstName} {LastName}".Trim()) ? $"{FirstName} {LastName}".Trim()
        : Username ?? "کاربر میزون";
    public string UsernameLabel => string.IsNullOrWhiteSpace(Username) ? "Mizon" : $"@{Username}";
    public Microsoft.UI.Xaml.Media.ImageSource? ProfileImage { get => field; private set => SetField(ref field, value); }
    public Microsoft.UI.Xaml.Media.ImageSource? PreviewImage => Common.ImageSourceFactory.Create(ProfileImagePath, 480);
    public string FirstNameLabel => string.IsNullOrWhiteSpace(FirstName) ? "—" : FirstName;
    public string LastNameLabel => string.IsNullOrWhiteSpace(LastName) ? "—" : LastName;
    public string? ProfileImagePath
    {
        get => field;
        set
        {
            if (!SetField(ref field, value)) return;
            ProfileImage = Common.ImageSourceFactory.Create(value, 144);
            OnPropertyChanged(nameof(PreviewImage));
        }
    }
    public override string ToString() => DisplayName;
}
