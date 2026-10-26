using System;
using System.Linq;

namespace Mizon.Windows.ViewModels.Items;

public sealed class StoryItemViewModel : ViewModelBase
{
    public Guid PublicId { get; init; }
    public Guid PreviewMediaPublicId { get; init; }
    public string CreatorFirstName { get; init; } = string.Empty;
    public string CreatorLastName { get; init; } = string.Empty;
    public string? Title { get; init; }
    public bool ViewedByMe { get => field; set => SetField(ref field, value); }
    public string CreatorDisplayName => string.Join(" ", new[] { CreatorFirstName, CreatorLastName }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public string PreviewImagePath { get => field; set => SetField(ref field, value); } = "/Assets/photo.jpg";
    public string CreatorProfileImagePath { get => field; set => SetField(ref field, value); } = "/Assets/photo.jpg";
}
