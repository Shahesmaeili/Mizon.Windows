// Adapted from DevWinUI 10.4.1 (30a7bbb), MIT. See LICENSE.DevWinUI.txt.
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Mizon.Windows.Controls.TrackDetails;

[TemplatePart(Name = PART_ItemsView, Type = typeof(ItemsView))]
public partial class SelectorBarSegmented : SelectorBar
{
    private const string PART_ItemsView = "PART_ItemsView";
    private ItemsView? _ItemsView { get; set; }

    public IReadOnlyList<object> SelectedItems { get; internal set; } = [];

    public int SelectedIndex
    {
        get { return (int)GetValue(SelectedIndexProperty); }
        set { SetValue(SelectedIndexProperty, value); }
    }

    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(SelectorBarSegmented), new PropertyMetadata(-1, OnSelectedIndexChanged));

    private static void OnSelectedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (SelectorBarSegmented)d;
        if (ctl != null)
        {
            var value = (int)e.NewValue;

            ctl.UpdateSelectedIndex(value);
        }
    }
    private void UpdateSelectedIndex(int value)
    {
        if (Items == null || Items.Count == 0)
            return;

        if (value < 0)
        {
            SelectedItem = Items.FirstOrDefault(item => item.IsSelected == true);
        }
        else
        {
            if (value >= Items.Count)
                return;

            SelectedItem = Items[value];
        }

        SyncItemsState();
    }
    public ItemsViewSelectionMode SelectionMode
    {
        get { return (ItemsViewSelectionMode)GetValue(SelectionModeProperty); }
        set { SetValue(SelectionModeProperty, value); }
    }

    public static readonly DependencyProperty SelectionModeProperty =
        DependencyProperty.Register(nameof(SelectionMode), typeof(ItemsViewSelectionMode), typeof(SelectorBarSegmented), new PropertyMetadata(ItemsViewSelectionMode.Single));

    public Orientation Orientation
    {
        get { return (Orientation)GetValue(OrientationProperty); }
        set { SetValue(OrientationProperty, value); }
    }

    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(SelectorBarSegmented), new PropertyMetadata(Orientation.Horizontal, OnOrientationChanged));

    private static void OnOrientationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (SelectorBarSegmented)d;
        if (ctl != null)
        {
            ctl.UpdateItemsView((Orientation)e.NewValue);
        }
    }

    public SelectorBarSegmented()
    {
        this.DefaultStyleKey = typeof(SelectorBarSegmented);

        this.IsEnabledChanged += SelectorBarSegmented_IsEnabledChanged;
        this.Loaded += SelectorBarSegmented_Loaded;

        this.SelectionChanged += SelectorBarSegmented_SelectionChanged;
    }

    private void SelectorBarSegmented_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (SelectedItem == null)
        {
            SelectedIndex = -1;
            return;
        }

        var index = Items.IndexOf(SelectedItem);

        if (index >= 0 && index != SelectedIndex)
        {
            SelectedIndex = index;
        }
    }
    private void SelectorBarSegmented_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        SyncItemsState();
    }

    private void SelectorBarSegmented_Loaded(object sender, RoutedEventArgs e)
    {
        SyncItemsState();
    }

    public void SyncItemsState()
    {
        if (Items == null || Items.Count == 0) return;
        bool enabled = IsEnabled;
        int selectedIdx = SelectedIndex;
        if (selectedIdx < 0 && SelectedItem != null)
        {
            selectedIdx = Items.IndexOf(SelectedItem);
        }

        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i] is SelectorBarItem item)
            {
                item.IsSelected = (i == selectedIdx);
                VisualStateManager.GoToState(item, enabled && item.IsEnabled ? "Enabled" : "Disabled", false);
            }
        }
    }

    protected override void OnApplyTemplate()
    {
        if (_ItemsView is not null)
            _ItemsView.SelectionChanged -= _ItemsView_SelectionChanged;

        base.OnApplyTemplate();

        _ItemsView = GetTemplateChild(PART_ItemsView) as ItemsView;

        if (_ItemsView != null)
        {
            _ItemsView.SelectionChanged += _ItemsView_SelectionChanged;
        }

        SelectedItems = _ItemsView?.SelectedItems ?? [];
        UpdateItemsView(Orientation);
        UpdateSelectedIndex(SelectedIndex);
    }

    private void _ItemsView_SelectionChanged(ItemsView sender, ItemsViewSelectionChangedEventArgs args)
    {
        SelectedItems = sender.SelectedItems;
    }

    private void UpdateItemsView(Orientation orientation)
    {
        if (_ItemsView is not { } itemsView) return;

        var selectedIndices = new List<int>();
        var itemsSource = itemsView.ItemsSource;
        if (FindItemsRepeater(itemsView) is { } itemsRepeater)
        {
            foreach (SelectorBarItem item in itemsView.SelectedItems)
            {
                var index = itemsRepeater.GetElementIndex(item);
                if (index >= 0) selectedIndices.Add(index);
            }
        }

        itemsView.ItemsSource = null;
        itemsView.Layout = new StackLayout { Orientation = orientation };
        itemsView.ItemsSource = itemsSource;
        foreach (var index in selectedIndices)
        {
            itemsView.Select(index);
        }

        itemsView.UpdateLayout();
    }
    private static ItemsRepeater? FindItemsRepeater(DependencyObject parent)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is ItemsRepeater repeater)
                return repeater;
            if (FindItemsRepeater(child) is { } nested)
                return nested;
        }
        return null;
    }
}
