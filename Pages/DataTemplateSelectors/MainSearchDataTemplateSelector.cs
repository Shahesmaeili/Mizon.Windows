using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mizon.Windows.ViewModels.Items;

namespace Mizon.Windows.Pages.DataTemplateSelectors;

public sealed partial class MainSearchDataTemplateSelector : DataTemplateSelector
{
    public DataTemplate StatusDataTemplate { get; set; } = null!;
    public DataTemplate PersonDataTemplate { get; set; } = null!;
    public DataTemplate ClientDataTemplate { get; set; } = null!;
    public DataTemplate MusicDataTemplate { get; set; } = null!;
    public DataTemplate MovieDataTemplate { get; set; } = null!;
    public DataTemplate SeriesDataTemplate { get; set; } = null!;


    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
    {
        if (item == null)
            return null!;

        if (item is MainSearchStatusItemViewModel) return StatusDataTemplate;

        // تشخیص نوع بر اساس کلاس واقعی شیء
        if (item is MainSearchClientItemViewModel mainSearchClientItemViewModel)
        {
            return ClientDataTemplate;
        }
        else if (item is MainSearchMusicItemViewModel mainSearchMusicItemViewModel)
        {
            return MusicDataTemplate;
        }


        return base.SelectTemplateCore(item, container);
    }
}
