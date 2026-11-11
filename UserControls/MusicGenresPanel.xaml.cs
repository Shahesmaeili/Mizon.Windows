using Mizon.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Mizon.Windows.Controls;
using Mizon.Windows.ViewModels;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Generic;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.UserControls;

public sealed partial class MusicGenresPanel : UserControl
{
    public LocalizationService Localization => LocalizationService.Current;

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(MusicPageViewModel), typeof(MusicGenresPanel), new PropertyMetadata(null, ViewModelChanged));

    private readonly HashSet<DiscoveryCardButton> _realized = [];
    private readonly UISettings _settings = new();
    private bool _initialized;
    private bool _loaded;
    private long _lastWheel;
    private double _wheelTarget;

    public MusicGenresPanel()
    {
        InitializeComponent();
        _initialized = true;
    }

    public MusicPageViewModel? ViewModel
    {
        get => (MusicPageViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public event EventHandler<MusicGenreTypeItemViewModel>? GenreRequested;
    public event EventHandler? ClearRequested;
    public event EventHandler? RefreshRequested;

    private static void ViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var panel = (MusicGenresPanel)sender;
        if (!panel._initialized) return;
        panel.ResetCards();
        panel.Bindings.Update();
    }

    private void Panel_Loaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        Bindings.Update();
        GenreScroll.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(Rail_Wheel), true);
        ResetCards();
    }

    private void Panel_Unloaded(object sender, RoutedEventArgs args)
    {
        if (!_loaded || IsLoaded) return;
        _loaded = false;
        Bindings.StopTracking();
        GenreScroll.RemoveHandler(PointerWheelChangedEvent, new PointerEventHandler(Rail_Wheel));
        ResetCards();
        _lastWheel = 0;
    }

    private void Genre_Prepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not DiscoveryCardButton button) return;
        _realized.Add(button);
        button.ResetInteraction();
    }

    private void Genre_Clearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is not DiscoveryCardButton button) return;
        button.ResetInteraction();
        _realized.Remove(button);
    }

    private void ResetCards(bool animate = false)
    {
        foreach (var button in _realized) button.ResetInteraction(animate);
    }

    private void Rail_ViewChanging(object sender, ScrollViewerViewChangingEventArgs args) => ResetCards(animate: true);

    private void Rail_Wheel(object sender, PointerRoutedEventArgs args)
    {
        var properties = args.GetCurrentPoint(GenreScroll).Properties;
        if (properties.IsHorizontalMouseWheel || properties.MouseWheelDelta == 0 || GenreScroll.ScrollableWidth <= 0
            || args.KeyModifiers.HasFlag(global::Windows.System.VirtualKeyModifiers.Control)) return;
        var now = Environment.TickCount64;
        var offset = now - _lastWheel > 350 ? GenreScroll.HorizontalOffset : _wheelTarget;
        _lastWheel = now;
        _wheelTarget = Math.Clamp(offset - properties.MouseWheelDelta / 120d * 160, 0, GenreScroll.ScrollableWidth);
        if (Math.Abs(GenreScroll.HorizontalOffset - _wheelTarget) < .5) return;
        GenreScroll.ChangeView(_wheelTarget, null, null, !_settings.AnimationsEnabled);
        args.Handled = true;
    }

    private void Genre_Click(object sender, RoutedEventArgs args)
    {
        if (sender is DiscoveryCardButton { Tag: MusicGenreTypeItemViewModel genre }) GenreRequested?.Invoke(this, genre);
    }

    private void Clear_Click(object sender, RoutedEventArgs args) => ClearRequested?.Invoke(this, EventArgs.Empty);
    private void Refresh_Click(object sender, RoutedEventArgs args) => RefreshRequested?.Invoke(this, EventArgs.Empty);
}
