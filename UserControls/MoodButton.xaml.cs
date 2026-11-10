using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Mizon.Windows.Controls;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using VirtualKey = Windows.System.VirtualKey;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.UserControls;

public sealed partial class MoodButton : UserControl, IDisposable
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(HomePageViewModel), typeof(MoodButton), new PropertyMetadata(null, ModelChanged));
    public HomePageViewModel? ViewModel { get => (HomePageViewModel?)GetValue(ViewModelProperty); set => SetValue(ViewModelProperty, value); }

    private readonly DispatcherQueueTimer _hoverTimer, _dismissTimer;
    private readonly UISettings _settings = new();
    private HomePageViewModel? _observed;
    private CancellationTokenSource? _lifetime, _animationRequest, _effectRequest, _effectPreparationRequest;
    private MoodPicker? _picker;
    private Popup? _effectPopup;
    private AnimatedMoodEmoji? _effectPlayer;
    private XamlRoot? _effectRoot;
    private Guid? _animationMediaId, _effectMoodId;
    private Task<string>? _effectPreparation;
    private Task? _pickerCloseTask;
    private TaskCompletionSource? _pickerCloseCompletion;
    private bool _disposed, _pickerOpen, _keyboardOpen, _pointerOverPicker, _suppressHover;
    private int _animationRevision;

    public MoodButton()
    {
        InitializeComponent();
        _hoverTimer = DispatcherQueue.CreateTimer();
        _hoverTimer.Interval = TimeSpan.FromMilliseconds(280);
        _hoverTimer.IsRepeating = false;
        _hoverTimer.Tick += HoverTimer_Tick;
        _dismissTimer = DispatcherQueue.CreateTimer();
        _dismissTimer.Interval = TimeSpan.FromMilliseconds(450);
        _dismissTimer.IsRepeating = false;
        _dismissTimer.Tick += DismissTimer_Tick;
        Loaded += Button_Loaded;
        Unloaded += Button_Unloaded;
        PickerFlyout.OverlayInputPassThroughElement = EmojiButton;
    }

    private static void ModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (MoodButton)sender;
        if (control.IsLoaded) control.Observe();
    }

    private async void Button_Loaded(object sender, RoutedEventArgs args)
    {
        if (_disposed) return;
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = new CancellationTokenSource();
        Observe();
        try
        {
            if (ViewModel is { } model) await model.EnsureMoodAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { AppLog.Error("Initial mood could not be loaded.", exception); }
    }

    private void Observe()
    {
        if (_observed is not null) _observed.PropertyChanged -= Model_PropertyChanged;
        _observed = ViewModel;
        if (_observed is not null) _observed.PropertyChanged += Model_PropertyChanged;
        _ = UpdateAnimationAsync();
    }

    private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(HomePageViewModel.CurrentMood)) _ = UpdateAnimationAsync();
        if (args.PropertyName == nameof(HomePageViewModel.IsSigningOut))
            EmojiButton.IsEnabled = ViewModel?.IsSigningOut != true;
    }

    private async Task UpdateAnimationAsync()
    {
        if (_lifetime is null || !IsLoaded) return;
        var mood = ViewModel?.CurrentMood;
        var name = mood is null ? "انتخاب حال‌وهوا" : "حال‌وهوای من: " + (string.IsNullOrWhiteSpace(mood.PersianTitle) ? mood.Title : mood.PersianTitle);
        AutomationProperties.SetName(EmojiButton, name);
        FallbackEmoji.Text = mood?.Emoji ?? "🤢";
        // Refreshing Me must not rebuild or restart the same composition scene.
        if (mood?.AnimationMediaPublicId == _animationMediaId &&
            ((LoopPlayer.AnimationPath is not null && LoopPlayer.ErrorMessage is null) || _animationRequest is not null))
        {
            if (mood?.PublicId != _effectMoodId) _ = WarmEffectAsync();
            return;
        }
        _animationRequest?.Cancel();
        _animationRequest = null;
        InvalidateEffect();
        var revision = ++_animationRevision;
        _animationMediaId = mood?.AnimationMediaPublicId;
        FallbackEmoji.Visibility = Visibility.Visible;
        LoopPlayer.AnimationPath = null;
        if (mood is null || ViewModel is not { } model) return;
        var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _animationRequest = request;
        try
        {
            var path = await model.GetMoodMediaPathAsync(mood.AnimationMediaPublicId, request.Token);
            if (revision != _animationRevision || request.IsCancellationRequested || !IsLoaded) return;
            LoopPlayer.AnimationPath = path;
            _ = WarmEffectAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { AppLog.Error("Mood animation could not be downloaded.", exception); }
        finally
        {
            if (ReferenceEquals(_animationRequest, request)) _animationRequest = null;
            request.Dispose();
        }
    }

    private void Loop_Ready(object? sender, EventArgs args) => FallbackEmoji.Visibility = Visibility.Collapsed;
    private void Loop_Failed(object? sender, string message)
    {
        FallbackEmoji.Visibility = Visibility.Visible;
        AppLog.Information("Mood renderer: " + message);
    }

    private void Emoji_PointerEntered(object sender, PointerRoutedEventArgs args)
    {
        _dismissTimer.Stop();
        _ = WarmEffectAsync();
        if (!_suppressHover && !_pickerOpen) { _hoverTimer.Stop(); _hoverTimer.Start(); }
    }

    private void Emoji_PointerExited(object sender, PointerRoutedEventArgs args)
    {
        // Opening a native popup can transfer pointer ownership even though the
        // cursor has not left its anchor. Do not immediately close that popup.
        var point = args.GetCurrentPoint(EmojiButton).Position;
        if (point.X >= 0 && point.Y >= 0 && point.X < EmojiButton.ActualWidth && point.Y < EmojiButton.ActualHeight) return;
        _suppressHover = false;
        _hoverTimer.Stop();
        if (_pickerOpen && !_keyboardOpen) _dismissTimer.Start();
    }

    private void HoverTimer_Tick(DispatcherQueueTimer sender, object args) => OpenPicker(false);
    private void DismissTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (!_pointerOverPicker && !EmojiButton.IsPointerOver && _picker?.IsSaving != true && !_keyboardOpen)
            PickerFlyout.Hide();
    }

    private void OpenPicker(bool keyboard)
    {
        if (_disposed || !IsLoaded || XamlRoot is null || ViewModel is null || ViewModel.IsSigningOut || _pickerOpen) return;
        _keyboardOpen = keyboard;
        _hoverTimer.Stop();
        PickerFlyout.ShowAt(EmojiButton, new FlyoutShowOptions { ShowMode = keyboard ? FlyoutShowMode.Standard : FlyoutShowMode.Transient });
    }

    private void Picker_Opening(object sender, object args)
    {
        _pickerOpen = true;
        if (_picker is null && ViewModel is { } model)
        {
            _picker = new MoodPicker(model);
            _picker.SelectionSaved += Picker_SelectionSaved;
            _picker.PointerEntered += Picker_PointerEntered;
            _picker.PointerExited += Picker_PointerExited;
            _picker.KeyDown += Picker_KeyDown;
            PickerHost.Tag = _picker;
        }
        if (_picker is null) return;
        var size = XamlRoot?.Size ?? new Size(360, 600);
        _picker.Width = Math.Clamp(size.Width - 32, 1, 344);
        _picker.Height = Math.Clamp(size.Height - 100, 180, 390);
        _picker.PrepareForOpen();
    }

    private void Picker_Opened(object sender, object args)
    {
        _pickerOpen = true;
        if (_keyboardOpen) _picker?.FocusSearch();
    }

    private void Picker_Closed(object sender, object args)
    {
        _pickerOpen = _pointerOverPicker = _keyboardOpen = false;
        _dismissTimer.Stop();
        _pickerCloseCompletion?.TrySetResult();
    }

    private void Picker_PointerEntered(object sender, PointerRoutedEventArgs args) { _pointerOverPicker = true; _dismissTimer.Stop(); }
    private void Picker_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        // Once the user starts typing/navigating, moving the mouse away must not
        // dismiss the keyboard interaction. Native Escape/light-dismiss still work.
        if (args.Key != VirtualKey.Escape) { _keyboardOpen = true; _dismissTimer.Stop(); }
    }
    private void Picker_PointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (_picker is null) return;
        var point = args.GetCurrentPoint(_picker).Position;
        if (point.X >= 0 && point.Y >= 0 && point.X < _picker.ActualWidth && point.Y < _picker.ActualHeight) return;
        _pointerOverPicker = false;
        if (!_keyboardOpen) _dismissTimer.Start();
    }

    private void Picker_SelectionSaved(object? sender, EventArgs args) { _suppressHover = true; PickerFlyout.Hide(); }
    private void Emoji_RightTapped(object sender, RightTappedRoutedEventArgs args) { args.Handled = true; OpenPicker(true); }
    private void Emoji_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is VirtualKey.Down or VirtualKey.F4) { args.Handled = true; OpenPicker(true); }
    }

    private async void Emoji_Click(object sender, RoutedEventArgs args)
    {
        _hoverTimer.Stop();
        _suppressHover = true;
        if (_lifetime is null) return;
        try
        {
            // Do not create a second native popup while the picker is still
            // tearing its popup down. This path skips only the picker exit
            // transition; normal hover/Escape closing keeps WinUI's animation.
            await ClosePickerForEffectAsync(_lifetime.Token);
            await PlayEffectAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { AppLog.Error("Mood click could not be completed.", exception); }
    }

    private Task ClosePickerForEffectAsync(CancellationToken token)
    {
        if (_pickerCloseTask is { IsCompleted: false }) return _pickerCloseTask;
        return _pickerCloseTask = ClosePickerCoreAsync(token);
    }

    private async Task ClosePickerCoreAsync(CancellationToken token)
    {
        if (!_pickerOpen && !PickerFlyout.IsOpen) return;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pickerCloseCompletion = completion;
        var animate = PickerFlyout.AreOpenCloseAnimationsEnabled;
        PickerFlyout.AreOpenCloseAnimationsEnabled = false;
        try
        {
            PickerFlyout.Hide();
            await completion.Task.WaitAsync(token);
        }
        finally
        {
            PickerFlyout.AreOpenCloseAnimationsEnabled = animate;
            if (ReferenceEquals(_pickerCloseCompletion, completion)) _pickerCloseCompletion = null;
        }
    }

    private async Task WarmEffectAsync()
    {
        if (_lifetime is null || !IsLoaded || !_settings.AnimationsEnabled || ViewModel?.CurrentMood is null) return;
        try { await EnsureEffectPrepared(); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { AppLog.Error("Mood effect preparation failed.", exception); }
    }

    private Task<string> EnsureEffectPrepared()
    {
        var model = ViewModel ?? throw new InvalidOperationException("Mood view model is unavailable.");
        var mood = model.CurrentMood ?? throw new InvalidOperationException("Mood is unavailable.");
        if (_effectMoodId == mood.PublicId && _effectPreparation is { IsCanceled: false, IsFaulted: false })
            return _effectPreparation;

        InvalidateEffect();
        _effectMoodId = mood.PublicId;
        // Retain one parsed document for the current mood, never a list of native scenes.
        var player = new AnimatedMoodEmoji { IsLooping = false, RetainSourceOnUnload = true, Width = 208, Height = 208 };
        _effectPlayer = player;
        var effects = mood.EffectMediaPublicIds;
        var mediaId = effects.Count == 0 ? mood.AnimationMediaPublicId : effects[Random.Shared.Next(effects.Count)];
        var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime!.Token);
        request.CancelAfter(TimeSpan.FromSeconds(25));
        _effectPreparationRequest = request;
        return _effectPreparation = PrepareEffectAsync(model, mediaId, player, request);
    }

    private async Task<string> PrepareEffectAsync(HomePageViewModel model, Guid mediaId, AnimatedMoodEmoji player, CancellationTokenSource request)
    {
        try
        {
            var path = await model.GetMoodMediaPathAsync(mediaId, request.Token);
            await player.PrepareAsync(path, request.Token);
            request.Token.ThrowIfCancellationRequested();
            return path;
        }
        finally
        {
            if (ReferenceEquals(_effectPreparationRequest, request)) _effectPreparationRequest = null;
            request.Dispose();
        }
    }

    private async Task PlayEffectAsync()
    {
        if (_lifetime is null || !_settings.AnimationsEnabled ||
            XamlRoot?.IsHostVisible != true || ViewModel?.CurrentMood is null) return;
        var preparation = EnsureEffectPrepared();
        var previous = _effectRequest;
        var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        request.CancelAfter(TimeSpan.FromSeconds(25));
        _effectRequest = request;
        // A new click restarts the single effect instead of being silently ignored
        // or accumulating multiple GPU scenes while the previous one is playing.
        previous?.Cancel();
        try
        {
            var path = await preparation.WaitAsync(request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (!IsLoaded || XamlRoot is null || _effectPlayer is not { } effectPlayer) return;
            if (_effectPopup is null)
            {
                _effectPopup = new Popup
                {
                    XamlRoot = XamlRoot,
                    // A native popup lets the effect extend past the title bar.
                    ShouldConstrainToRootBounds = false,
                    IsHitTestVisible = false,
                    IsLightDismissEnabled = false,
                    Child = effectPlayer
                };
                _effectRoot = XamlRoot;
                _effectRoot.Changed += Effect_RootChanged;
                _effectPopup.Closed += Effect_Closed;
                LoopPlayer.LayoutUpdated += Effect_AnchorLayoutUpdated;
            }
            UpdateEffectPosition();
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            RoutedEventHandler loaded = (_, _) => ready.TrySetResult();
            effectPlayer.Loaded += loaded;
            _effectPopup.IsOpen = true;
            try { if (!effectPlayer.IsLoaded) await ready.Task.WaitAsync(request.Token); }
            finally { effectPlayer.Loaded -= loaded; }
            request.Token.ThrowIfCancellationRequested();
            await effectPlayer.PlayOnceAsync(path, request.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { AppLog.Error("Mood interaction could not be played.", exception); }
        finally
        {
            if (ReferenceEquals(_effectRequest, request))
            {
                _effectRequest = null;
                ReleaseEffect();
            }
            request.Dispose();
        }
    }

    private void UpdateEffectPosition()
    {
        if (_effectPopup is not { } popup || _effectPlayer is not { } player || !LoopPlayer.IsLoaded) return;
        // Both the anchor and popup offsets use XamlRoot DIPs. Do not clamp to
        // the app bounds or apply DPI again: the native popup handles screen DPI.
        var center = LoopPlayer.TransformToVisual(null).TransformPoint(new Point(LoopPlayer.ActualWidth / 2, LoopPlayer.ActualHeight / 2));
        var left = center.X - player.Width / 2;
        var top = center.Y - player.Height / 2;
        if (Math.Abs(popup.HorizontalOffset - left) > .01) popup.HorizontalOffset = left;
        if (Math.Abs(popup.VerticalOffset - top) > .01) popup.VerticalOffset = top;
    }

    private void Effect_AnchorLayoutUpdated(object? sender, object args) => UpdateEffectPosition();
    private void Effect_Closed(object? sender, object args)
    {
        if (ReferenceEquals(sender, _effectPopup)) _effectRequest?.Cancel();
    }
    private void Effect_RootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (!sender.IsHostVisible) _effectRequest?.Cancel();
        else UpdateEffectPosition();
    }

    private void ReleaseEffect()
    {
        LoopPlayer.LayoutUpdated -= Effect_AnchorLayoutUpdated;
        if (_effectRoot is not null) _effectRoot.Changed -= Effect_RootChanged;
        _effectRoot = null;
        if (_effectPopup is not null) _effectPopup.Closed -= Effect_Closed;
        if (_effectPopup is not null) { _effectPopup.IsOpen = false; _effectPopup.Child = null; _effectPopup = null; }
    }

    private void InvalidateEffect()
    {
        var request = _effectRequest;
        _effectRequest = null;
        request?.Cancel();
        _effectPreparationRequest?.Cancel();
        _effectPreparationRequest = null;
        ReleaseEffect();
        _effectPlayer?.Dispose();
        _effectPlayer = null;
        _effectPreparation = null;
        _effectMoodId = null;
    }

    private void Button_Unloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded && !_disposed) return;
        _animationRevision++;
        _animationMediaId = null;
        _hoverTimer.Stop();
        _dismissTimer.Stop();
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        _animationRequest?.Cancel();
        InvalidateEffect();
        PickerFlyout.Hide();
        if (_picker is not null)
        {
            _picker.SelectionSaved -= Picker_SelectionSaved;
            _picker.PointerEntered -= Picker_PointerEntered;
            _picker.PointerExited -= Picker_PointerExited;
            _picker.KeyDown -= Picker_KeyDown;
            _picker.Dispose();
            _picker = null;
            PickerHost.Tag = null;
        }
        if (_observed is not null) _observed.PropertyChanged -= Model_PropertyChanged;
        _observed = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Button_Unloaded(this, new RoutedEventArgs());
        LoopPlayer.Dispose();
        PickerHost.Dispose();
        _hoverTimer.Tick -= HoverTimer_Tick;
        _dismissTimer.Tick -= DismissTimer_Tick;
        Loaded -= Button_Loaded;
        Unloaded -= Button_Unloaded;
        ViewModel = null;
    }
}
