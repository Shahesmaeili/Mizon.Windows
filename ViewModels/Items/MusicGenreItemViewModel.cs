using System;

namespace Mizon.Windows.ViewModels.Items;

public sealed class MusicGenreItemViewModel
{
    public Guid PublicId { get; set; }
    public string Title { get; set; } = null!;
    public string? PersianTitle { get; set; } = null!;
}
