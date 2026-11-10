using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.UserControls;
using Mizon.Windows.ViewModels;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

// A page owns this presenter. The selected story is resolved on demand, not by
// loading a second feed or navigating through an unrelated music detail page.
public sealed class FollowingActivityStoryPresenter : IDisposable
{
    private readonly MusicPageViewModel _music;
    private readonly NavigationService _navigation;
    private readonly DispatcherQueue _dispatcher;
    private MainWindow? _window;
    private StoryViewer? _viewer;
    private Control? _focusSource;
    private CancellationTokenSource? _load;
    private CancellationTokenRegistration _cancellationRegistration;
    private Guid _storyId;
    private bool _disposed;
    internal Guid? CurrentStoryPublicId => _viewer is null ? null : _storyId;

    public FollowingActivityStoryPresenter()
    {
        var services = ((App)Application.Current).Services;
        _music = services.GetRequiredService<MusicPageViewModel>();
        _navigation = services.GetRequiredService<NavigationService>();
        _dispatcher = services.GetRequiredService<DispatcherQueue>();
    }

    public async Task OpenAsync(Guid storyPublicId, Image? source = null, CancellationToken cancellationToken = default)
    {
        if (_disposed || storyPublicId == Guid.Empty || cancellationToken.IsCancellationRequested) return;
        if (_viewer is not null && _storyId == storyPublicId) return;
        if (App.MainMizonWindow is not MainWindow window) return;
        CloseCurrent();

        var transition = MusicCoverTransition.Prepare(source);
        var load = _load = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var viewer = _viewer = new StoryViewer();
        // Return keyboard/Narrator focus to the invoking activity after user close.
        // Navigation and disposal deliberately do not focus the departed page.
        for (DependencyObject? element = source; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is Button button) { _focusSource = button; break; }
        _focusSource ??= FocusManager.GetFocusedElement(window.Content.XamlRoot) as Control;
        _storyId = storyPublicId;
        _window = window;
        viewer.CloseRequested += ViewerClosed;
        _navigation.MainNavigationChanged += NavigationChanged;
        _navigation.InternalNavigationChanged += NavigationChanged;
        window.Closed += WindowClosed;
        _cancellationRegistration = load.Token.Register(() => _dispatcher.TryEnqueue(() =>
        {
            if (ReferenceEquals(_viewer, viewer)) CloseCurrent();
        }));
        var transitionHandedOff = false;
        try
        {
            // Prepare the snapshot while the source still belongs to the live page.
            window.ShowWindowOverlay(viewer);
            transitionHandedOff = true;
            await viewer.OpenWithTransitionAsync(_music,
                [new StoryItemViewModel { PublicId = storyPublicId }], 0, transition, load.Token);
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppLog.Error("Following activity story could not be opened.", exception);
            if (ReferenceEquals(_viewer, viewer)) CloseCurrent();
        }
        finally
        {
            // Once OpenWithTransitionAsync is called the viewer owns the transition,
            // including its cancellation and the decoded preview's lifetime.
            if (!transitionHandedOff) transition?.Dispose();
        }
    }

    private void ViewerClosed(object? sender, EventArgs args)
    {
        if (ReferenceEquals(sender, _viewer)) CloseCurrent(restoreFocus: true);
    }

    private void NavigationChanged(object? sender, NavigationChangedEventArgs args)
    {
        if (!args.IsEnteringPage) CloseCurrent();
    }

    private void WindowClosed(object sender, WindowEventArgs args) => CloseCurrent();

    private void CloseCurrent(bool restoreFocus = false)
    {
        var viewer = _viewer;
        _viewer = null;
        var focusSource = _focusSource;
        _focusSource = null;
        _storyId = Guid.Empty;
        _cancellationRegistration.Dispose();
        _cancellationRegistration = default;
        _navigation.MainNavigationChanged -= NavigationChanged;
        _navigation.InternalNavigationChanged -= NavigationChanged;
        var window = _window;
        _window = null;
        if (window is not null) window.Closed -= WindowClosed;
        var load = _load;
        _load = null;
        load?.Cancel();
        load?.Dispose();
        if (viewer is null) return;
        viewer.CloseRequested -= ViewerClosed;
        try { viewer.Close(); }
        finally { window?.HideWindowOverlay(viewer); }
        if (restoreFocus && focusSource is { IsLoaded: true, IsEnabled: true }) focusSource.Focus(FocusState.Programmatic);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseCurrent();
    }
}
