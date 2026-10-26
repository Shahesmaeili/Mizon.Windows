using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mizon.Windows.ViewModels.Items;

namespace Mizon.Windows.Pages.DataTemplateSelectors;

public class MainSearchDataTemplateSelector : DataTemplateSelector
{
    public DataTemplate PersonDataTemplate { get; set; }
    public DataTemplate ClientDataTemplate { get; set; }
    public DataTemplate MusicDataTemplate { get; set; }
    public DataTemplate MovieDataTemplate { get; set; }
    public DataTemplate SeriesDataTemplate { get; set; }


    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
    {
        if (item == null)
            return null;

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
