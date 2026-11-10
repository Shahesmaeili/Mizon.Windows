using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Mizon.Windows.ViewModels;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Generic;

namespace Mizon.Windows.UserControls;

public sealed partial class WeeklyRhythmPanel : UserControl
{
    public static readonly DependencyProperty SummaryProperty = DependencyProperty.Register(
        nameof(Summary), typeof(WeeklyRhythmViewModel), typeof(WeeklyRhythmPanel), new PropertyMetadata(null, SummaryChanged));

    private readonly Dictionary<int, ToggleButton> _dayButtons = [];
    private WeeklyRhythmViewModel? _renderedSummary;
    private WeeklyRhythmDayItemViewModel? _selectedDay;
    private bool? _wide;
    private bool _initialized;
    private bool _hasRendered;
    private bool _isPanelLoaded;
    private bool _updatingSelection;
    private bool _dayHandlersAttached;
    private bool _selectionUpdateQueued;

    public WeeklyRhythmPanel()
    {
        InitializeComponent();
        _initialized = true;
    }

    public WeeklyRhythmViewModel? Summary
    {
        get => (WeeklyRhythmViewModel?)GetValue(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    private static void SummaryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var panel = (WeeklyRhythmPanel)sender;
        if (panel._initialized && panel._isPanelLoaded) panel.RenderSummary();
    }

    private void Panel_Loaded(object sender, RoutedEventArgs args)
    {
        if (!IsLoaded) return;
        _isPanelLoaded = true;
        RenderSummary();
        SetDayHandlersAttached(true);
        ShowSelectedDay();
        UpdateLayout(ActualWidth);
    }

    private void Panel_Unloaded(object sender, RoutedEventArgs args)
    {
        // WinUI queues Unloaded. A responsive reparent may have already loaded
        // this same instance again by the time the old notification arrives.
        if (IsLoaded) return;
        _isPanelLoaded = false;
        // Keep the seven owned visuals and their selection when the responsive
        // rail is moved. They are released with this control, not rebuilt on resize.
        SetDayHandlersAttached(false);
    }

    private void SetDayHandlersAttached(bool attached)
    {
        if (_dayHandlersAttached == attached) return;
        _dayHandlersAttached = attached;
        foreach (var button in _dayButtons.Values)
        {
            if (attached)
            {
                button.Click += Day_Click;
                button.Checked += Day_Checked;
                button.Unchecked += Day_Unchecked;
            }
            else
            {
                button.Click -= Day_Click;
                button.Checked -= Day_Checked;
                button.Unchecked -= Day_Unchecked;
            }
        }
    }

    private void RenderSummary()
    {
        var summary = Summary;
        if (_hasRendered && ReferenceEquals(_renderedSummary, summary)) return;
        _hasRendered = true;
        _renderedSummary = summary;
        var previousDay = _selectedDay;
        _selectedDay = null;
        SetDayHandlersAttached(false);
        _dayButtons.Clear();
        DayColumnsHost.Children.Clear();
        DayColumnsHost.ColumnDefinitions.Clear();
        var count = summary?.Days.Count ?? 0;
        Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (count == 0) return;
        PreviewLabel.Visibility = summary!.IsDemo ? Visibility.Visible : Visibility.Collapsed;
        var template = (DataTemplate)Resources["DayTemplate"];
        var style = (Style)Resources["DayButtonStyle"];
        // Determine the selection before creating any visual children. Mutating
        // IsChecked from a template's Loaded callback can reenter native state
        // transitions while that template is still being connected/replaced.
        foreach (var day in summary.Days)
            if (_selectedDay is null || day.IsPeak) _selectedDay = day;
        // Responsive reparenting must preserve the user's choice for this data.
        foreach (var day in summary.Days)
            if (ReferenceEquals(day, previousDay)) { _selectedDay = day; break; }

        for (var index = 0; index < count; index++)
        {
            var day = summary.Days[index];
            DayColumnsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var button = new ToggleButton
            {
                // Set the initial state before the button has a template/tree.
                IsChecked = ReferenceEquals(day, _selectedDay),
                Style = style, Content = day, ContentTemplate = template, Tag = day
            };
            ToolTipService.SetToolTip(button, day.TooltipText);
            AutomationProperties.SetName(button, day.TooltipText);
            AutomationProperties.SetHelpText(button, "برای نمایش جزئیات این روز، فاصله یا اینتر را فشار بده");
            AutomationProperties.SetPositionInSet(button, index + 1);
            AutomationProperties.SetSizeOfSet(button, count);
            _dayButtons.Add(day.Index, button);
            // Keep the data in chronological order, with Saturday at the physical right.
            Grid.SetColumn(button, count - 1 - index);
            DayColumnsHost.Children.Add(button);
        }
        SetDayHandlersAttached(true);
        ShowSelectedDay();
    }

    private void Day_Click(object sender, RoutedEventArgs args)
    {
        if (!_isPanelLoaded || _updatingSelection) return;
        if (sender is not ToggleButton { Tag: WeeklyRhythmDayItemViewModel day } button) return;
        if (!_dayButtons.TryGetValue(day.Index, out var current) || !ReferenceEquals(current, button)) return;
        _selectedDay = day;
        ShowSelectedDay();
    }

    // Also handle UI Automation's Toggle pattern, which does not raise Click.
    private void Day_Checked(object sender, RoutedEventArgs args) => Day_Click(sender, args);

    private void Day_Unchecked(object sender, RoutedEventArgs args)
    {
        if (!_isPanelLoaded || _updatingSelection) return;
        if (sender is ToggleButton { Tag: WeeklyRhythmDayItemViewModel day } && ReferenceEquals(day, _selectedDay))
            ShowSelectedDay();
    }

    private void ShowSelectedDay()
    {
        if (!_isPanelLoaded || _selectedDay is not { } day) return;
        if (SelectedDayName.Text != day.DayName) SelectedDayName.Text = day.DayName;
        if (SelectedDayDuration.Text != day.TotalDurationText) SelectedDayDuration.Text = day.TotalDurationText;
        ToolTipService.SetToolTip(SelectedDayDetail, day.TooltipText);
        AutomationProperties.SetName(SelectedDayDuration, day.TooltipText);
        // Checked/Unchecked can be raised while WinUI is still changing a native
        // visual state. Synchronize after that callback has unwound, once per turn.
        if (_selectionUpdateQueued) return;
        _selectionUpdateQueued = DispatcherQueue.TryEnqueue(SynchronizeSelection);
    }

    private void SynchronizeSelection()
    {
        _selectionUpdateQueued = false;
        if (!_isPanelLoaded || _selectedDay is not { } day) return;
        _updatingSelection = true;
        try
        {
            foreach (var button in _dayButtons.Values)
            {
                var selected = ReferenceEquals(button.Tag, day);
                if (button.IsChecked != selected) button.IsChecked = selected;
            }
        }
        finally { _updatingSelection = false; }
    }

    private void Panel_SizeChanged(object sender, SizeChangedEventArgs args) => UpdateLayout(args.NewSize.Width);

    private void UpdateLayout(double width)
    {
        if (width <= 0) return;
        var wide = width >= 560;
        if (_wide == wide) return;
        _wide = wide;
        ChartColumn.Width = new GridLength(wide ? 1.6 : 1, GridUnitType.Star);
        SummaryColumn.Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        RhythmBody.ColumnSpacing = wide ? 20 : 0;
        RhythmBody.RowSpacing = wide ? 0 : 8;
        Grid.SetColumn(SummaryArea, wide ? 1 : 0);
        Grid.SetRow(ChartArea, wide ? 0 : 1);
    }
}
