using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Services;

using System;

namespace Mizon.Windows.ViewModels.Items;

public class EqualizerCommentItemViewModel : ViewModelBase
{
    public Guid PublicId { get; init; }
    public TimeSpan? Timestamp { get; init; }
    public string Text { get; init; } = string.Empty;
    public double CanvasTop { get; init; }
    public bool IsOwnComment { get; init; }
    public LocalizationService Localization => LocalizationService.Current;

    public string OwnerHint(int revision) =>
        IsOwnComment ? Localization.GetString("MusicDetails/OwnWaveformComment", revision) : string.Empty;


    public double CanvasLeft
    {
        get => field;
        set => SetField(ref field, value);
    } = 80;

    public ImageSource? ImageSource { get => field; set => SetField(ref field, value); }
    public RelayCommand Command { get; set; }
    public RelayCommand MouseEnterCommand { get; set; }
}
