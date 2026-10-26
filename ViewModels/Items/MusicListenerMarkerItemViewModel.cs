using Microsoft.UI.Xaml.Media;
using System;

namespace Mizon.Windows.ViewModels.Items;

public sealed class MusicListenerMarkerItemViewModel : ViewModelBase
{
    public Guid ClientPublicId { get; init; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Username { get; set; }
    public Guid? ProfileMediaPublicId { get; set; }

    public long PositionMilliseconds { get; set; }
    public DateTimeOffset PositionReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastServerUpdateAtUtc { get; set; }

    public bool IsPlaying
    {
        get => field;
        set
        {
            if (SetField(ref field, value))
                BorderBrush = value ? PlayingBorderBrush : PausedBorderBrush;
        }
    }

    public double CanvasLeft
    {
        get => field;
        set => SetField(ref field, value);
    }

    public string ImageSource
    {
        get => field;
        set => SetField(ref field, value);
    } = "/Assets/photo.jpg";

    public SolidColorBrush BorderBrush
    {
        get => field;
        private set => SetField(ref field, value);
    } = PlayingBorderBrush;

    public string DisplayName => string.IsNullOrWhiteSpace(Username)
        ? $"{FirstName} {LastName}".Trim()
        : Username;

    private static readonly SolidColorBrush PlayingBorderBrush = new(global::Windows.UI.Color.FromArgb(230, 0, 180, 255));
    private static readonly SolidColorBrush PausedBorderBrush = new(global::Windows.UI.Color.FromArgb(180, 230, 255, 13));
}
