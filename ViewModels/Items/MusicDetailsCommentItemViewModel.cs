using System;

namespace Mizon.Windows.ViewModels.Items;

public sealed class MusicDetailsCommentItemViewModel : ViewModelBase
{
    public Guid PublicId { get; init; }
    public Guid ClientPublicId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? Username { get; init; }
    public string Text { get; init; } = string.Empty;
    public TimeSpan? Timestamp { get; init; }
    public Guid? ProfileMediaPublicId { get; init; }

    public string FullName => $"{FirstName} {LastName}".Trim();
    public string TimestampText => Timestamp is null ? string.Empty : Timestamp.Value.ToString(@"m\:ss");

    public string? ProfileImagePath
    {
        get => field;
        set => SetField(ref field, value);
    }
}
