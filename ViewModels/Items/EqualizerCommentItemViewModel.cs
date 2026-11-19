using Microsoft.UI.Xaml.Media;

using System;

namespace Mizon.Windows.ViewModels.Items;

public class EqualizerCommentItemViewModel : ViewModelBase
{
    public Guid PublicId { get; init; }
    public TimeSpan? Timestamp { get; init; }
    public string Text { get; init; } = string.Empty;
    public double X
    {
        get; set;
    }

    public double Y
    {
        get; set;
    }

    public double Width { get; set; } = 28;

    public double Height { get; set; } = 28;

    // موقعیت‌های محاسبه شده برای Canvas
    //public double CanvasLeft => X - (Width / 2);
    public double CanvasTop => Y - (Height / 2);


    public double CanvasLeft
    {
        get => field;
        set => SetField(ref field, value);
    } = 80;

    public ImageSource? ImageSource { get => field; set => SetField(ref field, value); }
    public RelayCommand Command { get; set; }
    public RelayCommand MouseEnterCommand { get; set; }
}
