namespace Mizon.Windows.ViewModels.Items;

public sealed class MainSearchClientItemViewModel : Mizon.Windows.ViewModels.ViewModelBase
{
    public System.Guid PublicId { get; init; }
    public string FirstName { get; init; } = null!;
    public string LastName { get; init; } = null!;
    public string? NickName { get; init; }
    public string? Username { get; init; }
    public System.Guid? ProfileMediaPublicId { get; init; }

    public string DisplayName => $"{FirstName} {LastName}".Trim();
    public string? ProfileImagePath { get => field; set => SetField(ref field, value); }
}
