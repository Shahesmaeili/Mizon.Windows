using Mizon.Windows.Services;
using System;

namespace Mizon.Windows.ViewModels.Items;

public sealed class MusicGenreItemViewModel
{
    public Guid PublicId { get; set; }
    public string Title { get; set; } = null!;
    public string? PersianTitle { get; set; }
    public LocalizationService Localization => LocalizationService.Current;
    public string DisplayTitle(int revision) => Localization.IsPersian && !string.IsNullOrWhiteSpace(PersianTitle) ? PersianTitle : Title;
}
