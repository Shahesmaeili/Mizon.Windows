using Mizon.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mizon.Windows.Common;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.UserControls;

/// <summary>A translucent artist card with independent profile and follow actions.</summary>
public sealed partial class TopArtistCard : UserControl
{
    public LocalizationService Localization => LocalizationService.Current;

    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(TopArtistItemViewModel), typeof(TopArtistCard), new PropertyMetadata(null, ItemChanged));

    private CancellationTokenSource? _artwork;
    private bool _initialized;
    private bool _loaded;
    private TopArtistItemViewModel? _chartItem;

    public TopArtistCard()
    {
        InitializeComponent();
        _initialized = true;
    }

    public TopArtistItemViewModel? Item
    {
        get => (TopArtistItemViewModel?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public event EventHandler<TopArtistItemViewModel>? ProfileRequested;
    public event EventHandler<TopArtistItemViewModel>? FollowRequested;

    private static void ItemChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var card = (TopArtistCard)sender;
        if (!card._initialized) return;
        if (!card._loaded)
        {
            card.Bindings.StopTracking();
            return;
        }
        card.Bindings.Update();
        card.UpdateChart();
        card.RequestArtwork();
    }

    private void Card_Loaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        Bindings.Update();
        UpdateChart();
        RequestArtwork();
    }

    private void Card_Unloaded(object sender, RoutedEventArgs args)
    {
        if (!_loaded || IsLoaded) return;
        _loaded = false;
        Bindings.StopTracking();
        CancelArtwork();
        ActivityPath.Data = null;
        _chartItem = null;
    }

    private void UpdateChart()
    {
        if (!_loaded || ReferenceEquals(_chartItem, Item)) return;
        _chartItem = Item;
        var geometry = Item?.CreateActivityPath();
        // Stretch uses geometry bounds, which exclude unfilled figures on this
        // WinUI version. The Path has no Fill, so the open contour is still a line.
        if (geometry is not null)
            foreach (var figure in geometry.Figures) figure.IsFilled = true;
        ActivityPath.Data = geometry;
    }

    private void Profile_Click(object sender, RoutedEventArgs e)
    {
        if (Item is { } artist) ProfileRequested?.Invoke(this, artist);
    }

    private void Follow_Click(object sender, RoutedEventArgs e)
    {
        if (Item is { CanToggleFollow: true } artist) FollowRequested?.Invoke(this, artist);
    }

    private void CancelArtwork()
    {
        var request = _artwork;
        _artwork = null;
        request?.Cancel();
    }

    private void RequestArtwork()
    {
        CancelArtwork();
        if (Item is not { } artist) return;
        var request = _artwork = new();
        _ = LoadArtworkAsync(artist, request);
    }

    private async Task LoadArtworkAsync(TopArtistItemViewModel artist, CancellationTokenSource request)
    {
        using (request)
        {
            try { await artist.LoadArtworkAsync(true, request.Token); }
            finally
            {
                if (ReferenceEquals(_artwork, request)) _artwork = null;
            }
        }
    }
}
