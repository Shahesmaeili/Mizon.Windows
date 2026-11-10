using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Threading;

namespace Mizon.Windows.UserControls;

/// <summary>A compact chart with a leading record and shared, already decoded artwork.</summary>
public sealed partial class TrendingMusicPanel : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IReadOnlyList<DiscoveryMusicItemViewModel>), typeof(TrendingMusicPanel),
        new PropertyMetadata(null, ItemsChanged));

    private INotifyCollectionChanged? _observedCollection;
    private DiscoveryMusicItemViewModel? _featured;
    private DiscoveryMusicItemViewModel[] _ranked = [];
    private bool _loaded;
    private bool? _wide;
    private int _visibleCount = -1;
    private double _rowSpacing = double.NaN;
    private int _refreshPending;

    public TrendingMusicPanel() => InitializeComponent();

    public IReadOnlyList<DiscoveryMusicItemViewModel>? ItemsSource
    {
        get => (IReadOnlyList<DiscoveryMusicItemViewModel>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public event EventHandler<DiscoveryMusicItemViewModel>? MusicRequested;

    private static void ItemsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var panel = (TrendingMusicPanel)sender;
        panel.DetachSource();
        if (!panel._loaded) return;
        panel.AttachSource();
        panel.QueueRefresh();
    }

    private void Panel_Loaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        AttachSource();
        RefreshItems();
        UpdateLayout(ActualWidth);
    }

    private void Panel_Unloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded) return;
        _loaded = false;
        DetachSource();
    }

    private void AttachSource()
    {
        var source = ItemsSource as INotifyCollectionChanged;
        if (ReferenceEquals(_observedCollection, source)) return;
        DetachSource();
        _observedCollection = source;
        if (_observedCollection is not null) _observedCollection.CollectionChanged += CollectionChanged;
    }

    private void DetachSource()
    {
        if (_observedCollection is not null) _observedCollection.CollectionChanged -= CollectionChanged;
        _observedCollection = null;
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => QueueRefresh();

    private void QueueRefresh()
    {
        // A refresh replaces the chart's collection with Clear + Add calls. Present
        // that batch once, keeping existing cards while the synchronous batch runs.
        if (Interlocked.Exchange(ref _refreshPending, 1) != 0) return;
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            Interlocked.Exchange(ref _refreshPending, 0);
            if (_loaded) RefreshItems();
        })) Interlocked.Exchange(ref _refreshPending, 0);
    }

    private void RefreshItems()
    {
        var items = ItemsSource;
        var count = Math.Min(items?.Count ?? 0, 6);
        if (_visibleCount != count)
        {
            _visibleCount = count;
            Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
            CountText.Text = count.ToString("00", CultureInfo.InvariantCulture);
        }
        var first = count > 0 ? items![0] : null;
        if (!ReferenceEquals(_featured, first))
        {
            _featured = first;
            HeroPresenter.Content = first;
        }
        var rowCount = Math.Max(0, count - 1);
        var changed = _ranked.Length != rowCount;
        for (var index = 0; !changed && index < rowCount; index++)
            changed = !ReferenceEquals(_ranked[index], items![index + 1]);
        if (changed)
        {
            var ranked = new DiscoveryMusicItemViewModel[rowCount];
            for (var index = 0; index < rowCount; index++) ranked[index] = items![index + 1];
            _ranked = ranked;
            RankedItems.ItemsSource = ranked;
        }
        RankedItems.Visibility = rowCount == 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateLayout(ActualWidth);
    }

    private void Panel_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (args.NewSize.Width != args.PreviousSize.Width) UpdateLayout(args.NewSize.Width);
    }

    private void UpdateLayout(double width)
    {
        if (!double.IsFinite(width) || width <= 0) return;
        var wide = width >= 560 && _ranked.Length > 0;
        var rowSpacing = wide || _ranked.Length == 0 ? 0 : 8;
        if (_rowSpacing != rowSpacing)
        {
            _rowSpacing = rowSpacing;
            ChartLayout.RowSpacing = rowSpacing;
        }
        if (_wide == wide) return;
        _wide = wide;
        HeroColumn.Width = new GridLength(wide ? .9 : 1, GridUnitType.Star);
        ListColumn.Width = wide ? new GridLength(1.1, GridUnitType.Star) : new GridLength(0);
        ChartLayout.ColumnSpacing = wide ? 12 : 0;
        Grid.SetColumn(RankedItems, wide ? 1 : 0);
        Grid.SetRow(RankedItems, wide ? 0 : 1);
        ListRow.Height = wide ? new GridLength(0) : GridLength.Auto;
        UpdateHeroHeight(wide ? RankedItems.ActualHeight : 190);
    }

    private void RankedItems_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        // Follow the measured rows when Windows text scaling increases their height.
        if (_wide == true) UpdateHeroHeight(args.NewSize.Height);
    }

    private void UpdateHeroHeight(double measuredHeight)
    {
        if (!double.IsFinite(measuredHeight)) return;
        var height = Math.Max(190, measuredHeight);
        if (Math.Abs(HeroPresenter.Height - height) > .5) HeroPresenter.Height = height;
    }

    private void Music_Click(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: DiscoveryMusicItemViewModel item }) MusicRequested?.Invoke(this, item);
    }
}
