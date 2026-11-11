using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mizon.Windows.ViewModels.Items;
using Mizon.Windows.ViewModels;
using Mizon.Windows.Services;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Threading;

namespace Mizon.Windows.UserControls;

public sealed partial class FriendMusicActivityPanel : UserControl
{
    public LocalizationService Localization => LocalizationService.Current;
    public FluentIcons.Common.Symbol ForwardSymbol(bool isPersian) =>
        isPersian ? FluentIcons.Common.Symbol.ArrowLeft : FluentIcons.Common.Symbol.ArrowRight;

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(FollowingActivitiesViewModel), typeof(FriendMusicActivityPanel),
        new PropertyMetadata(null, ItemsChanged));

    private INotifyCollectionChanged? _observedCollection;
    private FriendMusicActivityItemViewModel[] _items = [];
    private bool _loaded;
    private int _columns;
    private int _refreshPending;

    public FriendMusicActivityPanel() => InitializeComponent();

    public FollowingActivitiesViewModel? ViewModel
    {
        get => (FollowingActivitiesViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public event EventHandler<FollowingActivityRequestedEventArgs>? ActivityRequested;
    public event EventHandler? MoreRequested;
    public event EventHandler? RefreshRequested;

    private static void ItemsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var panel = (FriendMusicActivityPanel)sender;
        panel.DetachSource();
        panel.Bindings.Update();
        if (panel.ViewModel is null)
        {
            panel._items = [];
            panel.CountText.Text = string.Empty;
            panel.ActivityItemsHost.Children.Clear();
            panel.ActivityItemsHost.RowDefinitions.Clear();
            return;
        }
        if (!panel._loaded) return;
        panel.AttachSource();
        panel.QueueRefresh();
    }

    private void Panel_Loaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        Localization.PropertyChanged += LanguageChanged;
        ViewModel?.RefreshLocalization();
        AttachSource();
        RefreshItems();
        UpdateColumns(ActualWidth);
    }

    private void Panel_Unloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded) return;
        _loaded = false;
        Localization.PropertyChanged -= LanguageChanged;
        DetachSource();
    }

    private void LanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(LocalizationService.Revision) || !_loaded) return;
        ViewModel?.RefreshLocalization();
        CountText.Text = _items.Length.ToString("00", CultureInfo.GetCultureInfo(Localization.LanguageTag));
        UpdateColumns(ActualWidth, force: true);
    }

    private void AttachSource()
    {
        var source = ViewModel?.Items;
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
        if (Interlocked.Exchange(ref _refreshPending, 1) != 0) return;
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            Interlocked.Exchange(ref _refreshPending, 0);
            if (_loaded) RefreshItems();
        })) Interlocked.Exchange(ref _refreshPending, 0);
    }

    private void RefreshItems()
    {
        var source = ViewModel?.Items;
        var count = Math.Min(source?.Count ?? 0, 4);
        if (CountText.Text.Length == 0 || _items.Length != count)
            CountText.Text = count.ToString("00", CultureInfo.GetCultureInfo(Localization.LanguageTag));
        var changed = _items.Length != count;
        for (var index = 0; !changed && index < count; index++)
            changed = !ReferenceEquals(_items[index], source![index]);
        if (!changed) return;

        // Retain the existing card instances when the page moves this panel between rails.
        // Reuse unchanged cards across a feed refresh, including their decoded images
        // and automation peers. Resizing only changes Grid slots.
        _items = new FriendMusicActivityItemViewModel[count];
        var children = ActivityItemsHost.Children;
        var template = (DataTemplate)Resources["ActivityTemplate"];
        for (var index = 0; index < count; index++)
        {
            var item = source![index];
            _items[index] = item;
            if (index < children.Count && ReferenceEquals(((ContentControl)children[index]).Content, item)) continue;
            ContentControl? existing = null;
            for (var previous = index + 1; previous < children.Count; previous++)
            {
                if (!ReferenceEquals(((ContentControl)children[previous]).Content, item)) continue;
                existing = (ContentControl)children[previous];
                children.RemoveAt(previous);
                break;
            }
            children.Insert(index, existing ?? new ContentControl
            {
                Content = item,
                ContentTemplate = template,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                IsTabStop = false
            });
        }
        while (children.Count > count) children.RemoveAt(children.Count - 1);
        UpdateColumns(ActualWidth, force: true);
    }

    private void Panel_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (args.NewSize.Width != args.PreviousSize.Width) UpdateColumns(args.NewSize.Width);
    }

    private void UpdateColumns(double width, bool force = false)
    {
        if (!double.IsFinite(width) || width <= 0) return;
        var columns = width >= 560 && _items.Length > 1 ? 2 : 1;
        if (!force && _columns == columns) return;
        if (_columns != columns)
        {
            _columns = columns;
            SecondColumn.Width = columns == 2 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            ActivityItemsHost.ColumnSpacing = columns == 2 ? 10 : 0;
        }
        var rows = (_items.Length + columns - 1) / columns;
        while (ActivityItemsHost.RowDefinitions.Count > rows)
            ActivityItemsHost.RowDefinitions.RemoveAt(ActivityItemsHost.RowDefinitions.Count - 1);
        while (ActivityItemsHost.RowDefinitions.Count < rows)
            ActivityItemsHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < ActivityItemsHost.Children.Count; index++)
        {
            var presenter = (ContentControl)ActivityItemsHost.Children[index];
            var row = index / columns;
            var column = columns == 2 ? (Localization.IsPersian ? 1 - index % columns : index % columns) : 0;
            if (Grid.GetRow(presenter) != row) Grid.SetRow(presenter, row);
            if (Grid.GetColumn(presenter) != column) Grid.SetColumn(presenter, column);
        }
    }

    private void Activity_Click(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: FriendMusicActivityItemViewModel item } button)
            ActivityRequested?.Invoke(this, new(item, FollowingActivityNavigation.FindCover(button)));
    }

    private void More_Click(object sender, RoutedEventArgs args) => MoreRequested?.Invoke(this, EventArgs.Empty);
    private void Refresh_Click(object sender, RoutedEventArgs args) => RefreshRequested?.Invoke(this, EventArgs.Empty);
}
