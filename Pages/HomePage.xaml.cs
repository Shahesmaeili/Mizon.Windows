using DevWinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;
using Windows.Foundation;
using Windows.Graphics;

namespace Mizon.Windows.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class HomePage : Page
    {
        private static readonly TimeSpan SearchResultPreviewDelay = TimeSpan.FromMilliseconds(250);
        private HomePageViewModel ViewModel => (HomePageViewModel)DataContext;
        private CancellationTokenSource? _searchCancellationTokenSource;
        private InputNonClientPointerSource? _titleBarPointerSource;

        public HomePage()
        {
            this.InitializeComponent();
            DataContext = ((App)App.Current).Services.GetRequiredService<HomePageViewModel>();

            ViewModel.NavigationService.MainNavigationChanged += (s, args) =>
            {
                if (args.IsEnteringPage && args.ToPageType == typeof(HomePage))
                {
                    var connectedAnimationService = ConnectedAnimationService.GetForCurrentView();
                    var connectedAnimation = connectedAnimationService.GetAnimation("ProfilePictureAnimation");
                    connectedAnimation?.TryStart(ProfilePersonButtton);
                }
            };

            Loaded += HomePage_Loaded;
            Unloaded += HomePage_Unloaded;
            SizeChanged += (_, _) => UpdateTitleBarPointerRegions();
            DragAreaOverlayGrid.SizeChanged += (_, _) => UpdateTitleBarPointerRegions();

        }

        private async void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeTitleBarPointerRegions();
            DispatcherQueue.TryEnqueue(UpdateTitleBarPointerRegions);

            var navigationService = ((App)App.Current).Services.GetRequiredService<NavigationService>();
            navigationService.InternalInitialize(InternalFrame);

            if (App.PendingMusicPublicId is Guid musicPublicId)
            {
                App.PendingMusicPublicId = null;
                navigationService.InternalNavigate(typeof(MusicsPage), new()
                {
                    [PageConstants.MUSIC_ID] = musicPublicId
                });
            }

            //MulticastDiscoveryService discovery = new();

            //discovery.DeviceDiscovered += d =>
            //{
            //    // DispatcherQueue برای UI
            //    DispatcherQueue.TryEnqueue(() =>
            //    {

            //    });
            //};

            //discovery.DeviceLost += id =>
            //{
            //    DispatcherQueue.TryEnqueue(() =>
            //    {

            //    });
            //};

            //discovery.Start();


            var discoveryV2 = new MulticastDiscoveryServiceV2();

            discoveryV2.DeviceDiscovered += d =>
            {

            };

            await discoveryV2.StartAsync();


            var access = await RequestLocationAccessAsync();

            if (!access)
            {
                Debug.WriteLine("Location access denied.");
                return;
            }

            var geolocator = new Geolocator
            {
                DesiredAccuracyInMeters = 1,
                
            };

            try
            {

                Geoposition position = await geolocator.GetGeopositionAsync();
                geolocator.PositionChanged += Geolocator_PositionChanged;
                
                double latitude = position.Coordinate.Point.Position.Latitude;
                double longitude = position.Coordinate.Point.Position.Longitude;

                Debug.WriteLine($"Lat: {latitude}");
                Debug.WriteLine($"Lon: {longitude}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error: {ex.Message}");
            }


            var _ble = new BleHeartRateClient();
            _ble.HeartRateReceived += bpm =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    
                });
            };

            _ble.Log += msg =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                   
                });
            };

            await _ble.ConnectAsync();
        }

        private void InitializeTitleBarPointerRegions()
        {
            if (App.MainMizonWindow is null)
                return;

            _titleBarPointerSource = InputNonClientPointerSource.GetForWindowId(App.MainMizonWindow.AppWindow.Id);
            UpdateTitleBarPointerRegions();
        }

        private void UpdateTitleBarPointerRegions()
        {
            if (_titleBarPointerSource is null)
                return;

            RectInt32? captionRect = GetWindowPixelBounds(DragAreaOverlayGrid);
            if (captionRect is null)
                return;

            _titleBarPointerSource.SetRegionRects(NonClientRegionKind.Caption, [captionRect.Value]);

            RectInt32[] interactiveRects = FindInteractiveTitleBarElements(DragAreaOverlayGrid)
                .Select(GetWindowPixelBounds)
                .Where(rect => rect is not null)
                .Select(rect => rect!.Value)
                .ToArray();

            _titleBarPointerSource.SetRegionRects(NonClientRegionKind.Passthrough, interactiveRects);
        }

        private static IEnumerable<FrameworkElement> FindInteractiveTitleBarElements(DependencyObject root)
        {
            int childrenCount = VisualTreeHelper.GetChildrenCount(root);

            for (int index = 0; index < childrenCount; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, index);

                if (child is FrameworkElement element && child is ButtonBase or AutoSuggestBox or ComboBox or Microsoft.UI.Xaml.Controls.TextBox or ToggleSwitch)
                    yield return element;

                foreach (FrameworkElement nestedElement in FindInteractiveTitleBarElements(child))
                    yield return nestedElement;
            }
        }

        private static RectInt32? GetWindowPixelBounds(FrameworkElement element)
        {
            if (element.XamlRoot is null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
                return null;

            Point position = element.TransformToVisual(null).TransformPoint(new Point());
            double scale = element.XamlRoot.RasterizationScale;

            return new RectInt32(
                Math.Max(0, (int)Math.Round(position.X * scale)),
                Math.Max(0, (int)Math.Round(position.Y * scale)),
                Math.Max(1, (int)Math.Ceiling(element.ActualWidth * scale)),
                Math.Max(1, (int)Math.Ceiling(element.ActualHeight * scale)));
        }

        private void HomePage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_titleBarPointerSource is null)
                return;

            _titleBarPointerSource.SetRegionRects(NonClientRegionKind.Caption, []);
            _titleBarPointerSource.SetRegionRects(NonClientRegionKind.Passthrough, []);
            _titleBarPointerSource = null;
        }

        private void Geolocator_PositionChanged(Geolocator sender, PositionChangedEventArgs args)
        {
           
        }

        private async void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.ProgrammaticChange)
                return;

            _searchCancellationTokenSource?.Cancel();
            _searchCancellationTokenSource?.Dispose();
            _searchCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _searchCancellationTokenSource.Token;
            var query = sender.Text;

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken);

                ViewModel.MainSearch.Clear();
                AppLog.Information($"Search started. Query='{query}'.");
                if (query.Trim().Length < 2)
                {
                    sender.IsSuggestionListOpen = false;
                    return;
                }

                var suggestionListOpened = false;
                await foreach (var item in ViewModel.SearchAsync(query, cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    ViewModel.MainSearch.Add(item);
                    if (!suggestionListOpened)
                    {
                        sender.IsSuggestionListOpen = true;
                        suggestionListOpened = true;
                    }

                    // زمان‌بندی آزمایشی برای مشاهدهٔ ورود تدریجی نتایج در UI.
                  //  await Task.Delay(SearchResultPreviewDelay, cancellationToken);
                }

                AppLog.Information($"Search completed. Query='{query}', ResultCount={ViewModel.MainSearch.Count}.");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Search request failed: {exception}");
                AppLog.Error($"Search request failed. Query='{query}'.", exception);
            }
        }

        private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is not MainSearchMusicItemViewModel music)
                return;

            _searchCancellationTokenSource?.Cancel();
            sender.IsSuggestionListOpen = false;

            // AutoSuggestBox replaces Text with SelectedItem.ToString() after
            // this event. Queue the reset so it runs after that built-in step.
            DispatcherQueue.TryEnqueue(() =>
            {
                sender.Text = string.Empty;
                sender.IsSuggestionListOpen = false;
                ViewModel.MainSearch.Clear();
            });

            ViewModel.NavigationService.InternalNavigate(typeof(MusicsPage), new()
            {
                [PageConstants.MUSIC_ID] = music.PublicId
            });
        }

        private async Task<bool> RequestLocationAccessAsync()
        {
            var accessStatus = await Geolocator.RequestAccessAsync();

            return accessStatus == GeolocationAccessStatus.Allowed;
        }

        private void NavigationViewMain_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            var navigationViewItem = args.SelectedItem as NavigationViewItem;


            Dictionary<string, object>? parameter = new();

            if (navigationViewItem is not null)
            {
                if (navigationViewItem.Tag.ToString() == "MusicsPage")
                {
                    parameter.Add(PageConstants.MUSIC_ID, Guid.Parse("0195dd3c-9a3e-7c4a-8c6f-2e9c0f6b1a72"));
                    ViewModel.NavigationService.InternalNavigate(typeof(MusicsPage), parameter);
                }

                else if (navigationViewItem.Tag.ToString() == "VPNPage")
                    InternalFrame.Navigate(typeof(VPNPage));
                else
                {
                    ViewModel.NavigationService.InternalNavigate(typeof(MusicPage), parameter);
                }
                ElementSoundPlayer.Play(ElementSoundKind.MoveNext);

            }
        }

        private void NavigationViewMain_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            if (InternalFrame.CanGoBack)
                InternalFrame.GoBack();
        }
    }
}
