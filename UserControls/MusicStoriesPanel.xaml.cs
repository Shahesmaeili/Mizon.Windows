using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Mizon.Windows.Controls;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.UserControls;

public sealed partial class MusicStoriesPanel : UserControl
{
    public LocalizationService Localization => LocalizationService.Current;

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(MusicPageViewModel), typeof(MusicStoriesPanel), new PropertyMetadata(null, ViewModelChanged));

    private readonly Dictionary<StoryActionButton, StoryItemViewModel> _realized = [];
    private readonly Dictionary<StoryActionButton, CancellationTokenSource> _loads = [];
    private readonly UISettings _settings = new();
    private MusicPageViewModel? _observed;
    private ImageLoadSession? _session;
    private bool _initialized;
    private bool _loaded;
    private long _lastWheel;
    private double _wheelTarget;

    public MusicStoriesPanel()
    {
        InitializeComponent();
        _initialized = true;
    }

    public MusicPageViewModel? ViewModel
    {
        get => (MusicPageViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public event EventHandler<StoryRequestedEventArgs>? StoryRequested;
    public event EventHandler? AddRequested;
    public event EventHandler? RefreshRequested;

    private static void ViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var panel = (MusicStoriesPanel)sender;
        if (!panel._initialized) return;
        panel.DetachSource();
        panel.StopImages();
        panel._realized.Clear();
        panel.Bindings.Update();
        if (panel._loaded) panel.AttachSource();
    }

    private void Panel_Loaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        Bindings.Update();
        StoriesScrollViewer.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(Rail_Wheel), true);
        AttachSource();
    }

    private void Panel_Unloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded || !_loaded) return;
        _loaded = false;
        Bindings.StopTracking();
        StoriesScrollViewer.RemoveHandler(PointerWheelChangedEvent, new PointerEventHandler(Rail_Wheel));
        DetachSource();
        StopImages();
        _lastWheel = 0;
    }

    private void AttachSource()
    {
        DetachSource();
        _observed = ViewModel;
        if (_observed is null) return;
        _observed.PropertyChanged += SourceChanged;
        if (!_observed.IsHomeStoriesLoading) StartImages();
    }

    private void DetachSource()
    {
        if (_observed is not null) _observed.PropertyChanged -= SourceChanged;
        _observed = null;
    }

    private void SourceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(MusicPageViewModel.IsHomeStoriesLoading)) return;
        // A refresh can retain the same item objects. Their elements will not be
        // prepared again, so restart their leases after the new metadata arrives.
        StopImages(releaseImages: false);
        if (_loaded && ViewModel is { IsHomeStoriesLoading: false }) StartImages();
    }

    private void StartImages()
    {
        if (_session is not null || !_loaded || !IsLoaded) return;
        _session = new();
        foreach (var (button, story) in _realized) StartImage(button, story);
    }

    private void StopImages(bool releaseImages = true)
    {
        var session = _session;
        _session = null;
        session?.Stop();
        _loads.Clear();
        AddStoryButton.ResetInteraction();
        foreach (var (button, story) in _realized)
        {
            button.ResetInteraction();
            if (releaseImages) story.ReleaseImages();
        }
    }

    private void Story_Prepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not StoryActionButton button || ViewModel is not { } model
            || args.Index < 0 || args.Index >= model.HomeStories.Count) return;
        var story = model.HomeStories[args.Index];
        button.ResetInteraction();
        _realized[button] = story;
        StartImage(button, story);
    }

    private void Story_Clearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is not StoryActionButton button) return;
        if (_loads.Remove(button, out var load)) load.Cancel();
        button.ResetInteraction();
        if (_realized.Remove(button, out var story)) story.ReleaseImages();
    }

    private void StartImage(StoryActionButton button, StoryItemViewModel story)
    {
        if (!_loaded || !IsLoaded || _session is not { } session || ViewModel is not { IsHomeStoriesLoading: false } model) return;
        if (_loads.Remove(button, out var previous)) previous.Cancel();
        var load = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        _loads[button] = load;
        session.Pending++;
        _ = LoadImagesAsync(model, button, story, session, load);
    }

    private async Task LoadImagesAsync(MusicPageViewModel model, StoryActionButton button, StoryItemViewModel story,
        ImageLoadSession session, CancellationTokenSource load)
    {
        var entered = false;
        try
        {
            await session.Slots.WaitAsync(load.Token);
            entered = true;
            await model.LoadHomeStoryImagesAsync(story, load.Token);
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested) { }
        catch (Exception exception) { AppLog.Error("Story preview could not be loaded.", exception); }
        finally
        {
            if (entered) session.Slots.Release();
            if (_loads.TryGetValue(button, out var current) && ReferenceEquals(current, load)) _loads.Remove(button);
            load.Dispose();
            session.Complete();
        }
    }

    private void Rail_ViewChanging(object sender, ScrollViewerViewChangingEventArgs args)
    {
        // A card can move out from under a stationary cursor without a pointer-exit.
        AddStoryButton.ResetInteraction(animate: true);
        foreach (var button in _realized.Keys) button.ResetInteraction(animate: true);
    }

    private void Rail_Wheel(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(StoriesScrollViewer).Properties;
        if (point.IsHorizontalMouseWheel || point.MouseWheelDelta == 0 || StoriesScrollViewer.ScrollableWidth <= 0
            || args.KeyModifiers.HasFlag(global::Windows.System.VirtualKeyModifiers.Control)) return;
        var now = Environment.TickCount64;
        var offset = now - _lastWheel > 350 ? StoriesScrollViewer.HorizontalOffset : _wheelTarget;
        _lastWheel = now;
        _wheelTarget = Math.Clamp(offset - point.MouseWheelDelta / 120d * 160, 0, StoriesScrollViewer.ScrollableWidth);
        if (Math.Abs(StoriesScrollViewer.HorizontalOffset - _wheelTarget) < .5) return;
        StoriesScrollViewer.ChangeView(_wheelTarget, null, null, !_settings.AnimationsEnabled);
        args.Handled = true;
    }

    private void Story_Click(object sender, RoutedEventArgs args)
    {
        if (sender is StoryActionButton button && _realized.TryGetValue(button, out var story))
            StoryRequested?.Invoke(this, new(story, button));
    }

    private void Add_Click(object sender, RoutedEventArgs args) => AddRequested?.Invoke(this, EventArgs.Empty);
    private void Retry_Click(object sender, RoutedEventArgs args) => RefreshRequested?.Invoke(this, EventArgs.Empty);

    // All access is on the UI dispatcher. Canceled sessions drain independently;
    // an in-flight download never releases an already disposed semaphore.
    private sealed class ImageLoadSession
    {
        private readonly CancellationTokenSource _cancellation = new();
        private bool _stopped;
        private bool _disposed;
        public SemaphoreSlim Slots { get; } = new(4, 4);
        public CancellationToken Token => _cancellation.Token;
        public int Pending { get; set; }
        public void Complete() { Pending--; if (_stopped && Pending == 0) Dispose(); }
        public void Stop()
        {
            if (_stopped) return;
            _stopped = true;
            _cancellation.Cancel();
            if (Pending == 0) Dispose();
        }
        private void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Slots.Dispose();
            _cancellation.Dispose();
        }
    }
}

public sealed class StoryRequestedEventArgs(StoryItemViewModel story, Button source) : EventArgs
{
    public StoryItemViewModel Story { get; } = story;
    public Button Source { get; } = source;
}
