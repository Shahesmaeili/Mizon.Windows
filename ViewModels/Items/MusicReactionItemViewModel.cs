using System;

namespace Mizon.Windows.ViewModels.Items;

public sealed partial class MusicReactionItemViewModel : ViewModelBase
{
    public int Count
    {
        get => field;
        set => SetField(ref field, value);
    }

    public string Emoji { get; set; } = null!;

    public bool ReactedByMe
    {
        get => field;
        set => SetField(ref field, value);
    }


    public Guid MusicReactionTypePublicId { get; set; }
}
