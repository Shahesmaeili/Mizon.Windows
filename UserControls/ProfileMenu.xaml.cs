using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;

namespace Mizon.Windows.UserControls;

public sealed partial class ProfileMenu : UserControl, IDisposable
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(HomePageViewModel), typeof(ProfileMenu), new PropertyMetadata(null, ViewModelChanged));
    private bool _initialized;
    private HomePageViewModel? _observedModel;
    private CancellationTokenSource? _menuCancellation;
    private bool _disposed;
    private Window? _observedWindow;
    private ITaskbarPinService? _taskbarPin;
    private bool _taskbarCheckedThisOpen;
    private bool _pinActionPending;
    private string? _pinStatusKey;
    public LocalizationService Localization => LocalizationService.Current;
    public string ProfileText(string key, int revision) => Localization.GetString("ProfileMenu/" + key, revision);
    public event EventHandler? DismissRequested;
    internal Func<Task>? DismissAsync { get; set; }

    public ProfileMenu()
    {
        InitializeComponent();
        _initialized = true;
    }

    public HomePageViewModel? ViewModel
    {
        get => (HomePageViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void ViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var menu = (ProfileMenu)sender;
        if (menu._initialized) menu.Bindings.Update();
        if (menu.IsLoaded) menu.ObserveModel();
    }

    private async void Menu_Loaded(object sender, RoutedEventArgs args)
    {
        if (_disposed || _menuCancellation is not null) return;
        _menuCancellation = new CancellationTokenSource();
        KeyDown += Menu_KeyDown;
        _taskbarCheckedThisOpen = false;
        PinTaskbarButton.Visibility = Visibility.Collapsed;
        _taskbarPin = TaskbarPinService.Current;
        _taskbarPin.StateChanged += TaskbarPin_StateChanged;
        Localization.PropertyChanged += Localization_Changed;
        _observedWindow = App.MainMizonWindow;
        if (_observedWindow is not null) _observedWindow.Activated += MainWindow_Activated;
        ObserveModel();
        AccountDetails.IsExpanded = false;
        UpdatePinStatus();
        await Task.WhenAll(RefreshAsync(), RefreshTaskbarAsync());
    }

    private async void Refresh_Click(object sender, RoutedEventArgs args) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_disposed || _menuCancellation is null || ViewModel is not { } model) return;
        try { await model.RefreshProfileAsync(_menuCancellation.Token); }
        catch (OperationCanceledException) { }
    }

    private void ObserveModel()
    {
        if (_observedModel is not null) _observedModel.PropertyChanged -= Model_Changed;
        _observedModel = ViewModel;
        if (_observedModel is not null) _observedModel.PropertyChanged += Model_Changed;
    }

    private void Menu_Unloaded(object sender, RoutedEventArgs args)
    {
        KeyDown -= Menu_KeyDown;
        if (_taskbarPin is not null) _taskbarPin.StateChanged -= TaskbarPin_StateChanged;
        _taskbarPin = null;
        Localization.PropertyChanged -= Localization_Changed;
        if (_observedWindow is not null) _observedWindow.Activated -= MainWindow_Activated;
        _observedWindow = null;
        _taskbarCheckedThisOpen = false;
        _menuCancellation?.Cancel();
        _menuCancellation?.Dispose();
        _menuCancellation = null;
        if (_observedModel is not null) _observedModel.PropertyChanged -= Model_Changed;
        _observedModel = null;
    }

    private async Task RefreshTaskbarAsync()
    {
        if (_disposed || _menuCancellation is not { } lifetime || _taskbarPin is not { } taskbarPin) return;
        try
        {
            await taskbarPin.RefreshAsync(lifetime.Token);
            if (_disposed || !ReferenceEquals(_menuCancellation, lifetime) || lifetime.IsCancellationRequested) return;
            _taskbarCheckedThisOpen = true;
            UpdateTaskbarAction();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { AppLog.Error("Taskbar pin state could not be refreshed.", error); }
    }

    private async void MainWindow_Activated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != Microsoft.UI.Xaml.WindowActivationState.Deactivated)
            await RefreshTaskbarAsync();
    }

    private void TaskbarPin_StateChanged(object? sender, EventArgs args)
    {
        if (DispatcherQueue.HasThreadAccess) UpdateTaskbarAction();
        else DispatcherQueue.TryEnqueue(UpdateTaskbarAction);
    }

    private void UpdateTaskbarAction()
    {
        if (_disposed || !IsLoaded || !_taskbarCheckedThisOpen || _taskbarPin is not { } taskbarPin) return;
        var state = taskbarPin.State;
        var visible = state.IsSupported && !state.IsPinned && state.IsPinningAllowed;
        PinTaskbarButton.IsEnabled = state.CanRequest && !_pinActionPending;
        // Move keyboard focus before removing the action that currently owns it.
        if (!visible && PinTaskbarButton.FocusState != FocusState.Unfocused)
            RefreshButton.Focus(FocusState.Programmatic);
        PinTaskbarButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (state.IsPinned)
        {
            _pinStatusKey = null;
            UpdatePinStatus();
        }
    }

    private async void PinTaskbar_Click(object sender, RoutedEventArgs args)
    {
        if (_disposed || _pinActionPending || ViewModel is not { CanUseProfile: true }
            || _taskbarPin is not { } taskbarPin || !taskbarPin.State.CanRequest) return;
        _pinActionPending = true;
        PinTaskbarButton.IsEnabled = false;
        _pinStatusKey = null;
        UpdatePinStatus();
        // The Windows confirmation owns its lifetime. Closing this popup must not
        // cancel it or allow a second prompt; the shared service handles shutdown.
        try
        {
            if (DismissAsync is { } dismiss) await dismiss();
            else DismissRequested?.Invoke(this, EventArgs.Empty);
            if (_disposed || ViewModel is not { CanUseProfile: true }) return;
            var result = await taskbarPin.RequestPinAsync();
            if (_disposed) return;
            _pinStatusKey = result.Success ? null : result.Code switch
            {
                "cancelled" or "canceled" or "declined" or "not_pinned" or "not_confirmed" => "PinNotCompleted",
                "not_foreground" or "foreground_required" => "PinForegroundRequired",
                "not_allowed" or "policy_blocked" => "PinNotAllowed",
                "unsupported" or "laf_required" or "access_token_required" => "PinUnavailable",
                _ => "PinFailed"
            };
            UpdateTaskbarAction();
            UpdatePinStatus();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            AppLog.Error("Taskbar pin request could not be completed.", error);
            if (!_disposed) { _pinStatusKey = "PinFailed"; UpdatePinStatus(); }
        }
        finally
        {
            _pinActionPending = false;
            UpdateTaskbarAction();
        }
    }

    private void Localization_Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(LocalizationService.Revision)) UpdatePinStatus();
    }

    private void UpdatePinStatus()
    {
        if (_disposed) return;
        var message = _pinStatusKey is null ? string.Empty : ProfileText(_pinStatusKey, Localization.Revision);
        PinTaskbarStatus.Text = message;
        PinTaskbarStatus.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (IsLoaded && message.Length != 0)
            AccessibilityAnnouncements.Announce(PinTaskbarStatus, message, "profile-taskbar-pin");
    }

    public void FocusFirstAction()
    {
        if (!_disposed && IsLoaded && FocusManager.FindFirstFocusableElement(this) is UIElement first)
            first.Focus(FocusState.Programmatic);
    }

    internal void MoveTabFocus(bool backwards)
    {
        if (_disposed || !IsLoaded) return;
        // Desktop WinUI requires SearchRoot, but its directional-navigation API
        // rejects Next/Previous with that option. Traverse this small menu's tab
        // stops explicitly, including selectable text in expanded account details.
        var candidates = new List<UIElement>();
        CollectTabStops(this, candidates);
        var ordered = candidates.OrderBy(element => element.TabIndex).ToArray();
        if (ordered.Length == 0) return;
        var current = FocusManager.GetFocusedElement(XamlRoot) as UIElement;
        var index = Array.IndexOf(ordered, current);
        if (index < 0) index = backwards ? 0 : -1;
        for (var count = 0; count < ordered.Length; count++)
        {
            index = (index + (backwards ? -1 : 1) + ordered.Length) % ordered.Length;
            if (ordered[index].Focus(FocusState.Keyboard)) break;
        }
    }

    private static void CollectTabStops(DependencyObject parent, List<UIElement> candidates)
    {
        if (parent is UIElement element)
        {
            if (element.Visibility != Visibility.Visible || !element.IsHitTestVisible || element is Control { IsEnabled: false }) return;
            if (element.IsTabStop && element is not UserControl && element is not ScrollViewer &&
                element.ActualSize.X > 0 && element.ActualSize.Y > 0)
                candidates.Add(element);
        }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            CollectTabStops(VisualTreeHelper.GetChild(parent, index), candidates);
    }

    private void Menu_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Tab)
        {
            var shift = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0;
            MoveTabFocus(shift);
            args.Handled = true;
        }
        else if (args.Key is VirtualKey.Up or VirtualKey.Down)
        {
            // Keep text-selection shortcuts inside the read-only account fields.
            if (args.OriginalSource is TextBlock { IsTextSelectionEnabled: true }) return;
            var direction = args.Key == VirtualKey.Up ? FocusNavigationDirection.Up : FocusNavigationDirection.Down;
            if (!FocusManager.TryMoveFocus(direction, new FindNextElementOptions { SearchRoot = this }))
            {
                var edge = args.Key == VirtualKey.Up ? FocusManager.FindLastFocusableElement(this) : FocusManager.FindFirstFocusableElement(this);
                if (edge is UIElement element) element.Focus(FocusState.Keyboard);
            }
            args.Handled = true;
        }
        // Escape is left to MenuFlyout, which dismisses and restores button focus.
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Menu_Unloaded(this, new RoutedEventArgs());
        Loaded -= Menu_Loaded;
        Unloaded -= Menu_Unloaded;
        ViewModel = null;
        Bindings.StopTracking();
        DismissRequested = null;
        DismissAsync = null;
    }

    private void Model_Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(HomePageViewModel.ProfileStatus))
            AccessibilityAnnouncements.Announce(StatusText, ViewModel?.ProfileStatus ?? string.Empty, "profile-status");
        if (args.PropertyName == nameof(HomePageViewModel.IsSigningOut) && ViewModel?.IsSigningOut == true)
            AccessibilityAnnouncements.Announce(this, "در حال خروج از حساب", "profile-signout");
    }

    private async void Downloads_Click(object sender, RoutedEventArgs args)
    {
        if (ViewModel is { } model && await model.OpenDownloadsAsync() && IsLoaded)
            DismissRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Settings_Click(object sender, RoutedEventArgs args)
    {
        if (_disposed || ViewModel is not { CanUseProfile: true } model) return;
        DismissRequested?.Invoke(this, EventArgs.Empty);
        model.NavigationService.OpenSettings();
    }
}
