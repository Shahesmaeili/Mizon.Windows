using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.ObjectModel;

namespace Mizon.Windows.ViewModels.Items;

public sealed class MusicLyricLineItemViewModel : ViewModelBase
{
    private static readonly Brush ActiveLineBrush = new SolidColorBrush(global::Windows.UI.Color.FromArgb(54, 0, 120, 212));
    private static readonly Brush TransparentBrush = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0, 0, 0, 0));

    public int StartTimeInMilliseconds { get; init; }
    public int EndTimeInMilliseconds { get; init; }
    public int LineOrder { get; init; }
    public string Text { get; init; } = string.Empty;
    public ObservableCollection<MusicLyricWordItemViewModel> Words { get; } = [];

    public bool HasTimedWords => Words.Count > 0;

    public string UntimedText => HasTimedWords ? string.Empty : Text;

    public bool IsActive
    {
        get => field;
        set
        {
            if (!SetField(ref field, value))
                return;

            OnPropertyChanged(nameof(Background));
        }
    }

    // A line without word timing is highlighted as one unit.
    public Brush Background => HasTimedWords || !IsActive ? TransparentBrush : ActiveLineBrush;
}

public sealed class MusicLyricWordItemViewModel : ViewModelBase
{
    private static readonly Brush ActiveWordBrush = new SolidColorBrush(global::Windows.UI.Color.FromArgb(190, 0, 120, 212));
    private static readonly Brush TransparentBrush = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0, 0, 0, 0));

    public int StartTimeInMilliseconds { get; init; }
    public int EndTimeInMilliseconds { get; init; }
    public int WordOrder { get; init; }
    public string Text { get; init; } = string.Empty;

    public bool IsActive
    {
        get => field;
        set
        {
            if (!SetField(ref field, value))
                return;

            OnPropertyChanged(nameof(Background));
        }
    }

    public Brush Background => IsActive ? ActiveWordBrush : TransparentBrush;
}
