using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Mizon.API;
using Mizon.Windows.Services;
using System.Collections.ObjectModel;

namespace Mizon.Windows.ViewModels;

public class MusicTopUserControlViewModel : MainViewModel
{
    public MusicTopUserControlViewModel(MizonApi mizonApi,
        MizonSocket mizonSocket,
        NavigationService navigationService,
        IAudioPlayerService audioPlayerService) : base(mizonApi, mizonSocket ,navigationService, audioPlayerService)
    {
        TopMusic = new()
        {
            MainItems = new()
            {
                new()
                {
                    Title = "عنوان تست",
                    Description = "متنی به عنوان توضیحات اضافه",
                    ToolTip = "توضیحات اضافه",
                    ButtonTitle = "عنوان دکمه",
                    
                },
                new()
                {
                    Title = "عنوان تست",
                    Description = "متنی به عنوان توضیحات اضافه",
                    ToolTip = "توضیحات اضافه",
                    ButtonTitle = "عنوان دکمه"
                },
            }
        };
    }

    public TopMusicModel TopMusic { get; set; }


    public class TopMusicModel
    {
        public ObservableCollection<TopMusicMainItem> MainItems { get; set; } = new();


    }

    public class TopMusicMainItem
    {
        public string Title { get; set; } = null!;
        public Brush TitleForeground { get; set; } = null!;
        public string Description { get; set; } = null!;
        public Brush DescriptionForeground { get; set; } = null!;
        public string ToolTip { get; set; } = null!;
        public string Image { get; set; } = null!;

        public string ButtonTitle { get; set; } = null!;
        public string ButtonArgument { get; set; } = null!;
        public Brush ButtonBackground { get; set; } = null!;
        public Brush ButtonBorderBrush { get; set; } = null!;
    }
}
