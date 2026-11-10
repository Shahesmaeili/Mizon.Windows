using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Mizon.Windows.Pages;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;

namespace Mizon.Windows.Services;

public class NavigationService
{
    private Frame? _mainFrame;
    private Frame? _internalFrame;
    private Type? _mainCurrentPageType;
    private Type? _internalCurrentPageType;
    internal Page? CurrentMainPage => _mainFrame?.Content as Page;
    internal Page? CurrentInternalPage => _internalFrame?.Content as Page;
    public bool CanInternalGoBack => _internalFrame?.CanGoBack == true;

    public event EventHandler<NavigationChangedEventArgs>? MainNavigationChanged;
    public event EventHandler<NavigationChangedEventArgs>? InternalNavigationChanged;

    public void MainInitialize(Frame mainFrame)
    {
        ArgumentNullException.ThrowIfNull(mainFrame);
        if (ReferenceEquals(_mainFrame, mainFrame)) return;
        if (_mainFrame is not null)
        {
            _mainFrame.Navigating -= OnMainFrameNavigating;
            _mainFrame.Navigated -= OnMainFrameNavigated;
        }
        _mainFrame = mainFrame;
        _mainCurrentPageType = mainFrame.Content?.GetType();
        _mainFrame.Navigating += OnMainFrameNavigating;
        _mainFrame.Navigated += OnMainFrameNavigated;
    }

    public void InternalInitialize(Frame internalFrame)
    {
        ArgumentNullException.ThrowIfNull(internalFrame);
        if (ReferenceEquals(_internalFrame, internalFrame)) return;
        if (_internalFrame is not null)
        {
            _internalFrame.Navigating -= OnInternalFrameNavigating;
            _internalFrame.Navigated -= OnInternalFrameNavigated;
        }
        _internalFrame = internalFrame;
        _internalCurrentPageType = internalFrame.Content?.GetType();
        _internalFrame.Navigating += OnInternalFrameNavigating;
        _internalFrame.Navigated += OnInternalFrameNavigated;
    }

    private void OnMainFrameNavigating(object sender, NavigatingCancelEventArgs e)
    {
        if (_mainCurrentPageType != null)
        {
            var param = e.Parameter as Dictionary<string, object>;

            MainNavigationChanged?.Invoke(this, new NavigationChangedEventArgs(
                fromPageType: _mainCurrentPageType,
                toPageType: e.SourcePageType,
                parameter: param,
                isEnteringPage: false));
        }
    }

    private void OnInternalFrameNavigating(object sender, NavigatingCancelEventArgs e)
    {
        if (_internalCurrentPageType != null)
        {
            var param = e.Parameter as Dictionary<string, object>;

            InternalNavigationChanged?.Invoke(this, new NavigationChangedEventArgs(
                fromPageType: _internalCurrentPageType,
                toPageType: e.SourcePageType,
                parameter: param,
                isEnteringPage: false));
        }
    }

    private void OnMainFrameNavigated(object sender, NavigationEventArgs e)
    {
        _mainCurrentPageType = e.SourcePageType;

        var param = e.Parameter as Dictionary<string, object>;

        MainNavigationChanged?.Invoke(this, new NavigationChangedEventArgs(
            fromPageType: null,
            toPageType: e.SourcePageType,
            parameter: param,
            isEnteringPage: true));
    }

    private void OnInternalFrameNavigated(object sender, NavigationEventArgs e)
    {
        _internalCurrentPageType = e.SourcePageType;

        var param = e.Parameter as Dictionary<string, object>;

        InternalNavigationChanged?.Invoke(this, new NavigationChangedEventArgs(
            fromPageType: null,
            toPageType: e.SourcePageType,
            parameter: param,
            isEnteringPage: true));
    }

    public void MainNavigate(Type pageType, Dictionary<string, object>? parameter = null)
    {
        _mainFrame?.Navigate(pageType, parameter);
    }

    public void InternalNavigate(Type pageType, Dictionary<string, object>? parameter = null, Image? coverSource = null,
        Task<ImageSource?>? coverPreview = null)
    {
        if (_internalFrame is null) return;
        if (pageType == typeof(MusicsPage) && CurrentInternalPage is MusicsPage current
            && parameter?.TryGetValue(PageConstants.MUSIC_ID, out var value) == true
            && Guid.TryParse(value?.ToString(), out var id) && current.MusicPublicId == id)
        {
            // Same-song navigation is usually a no-op, except a request for one specific comment.
            if (parameter.TryGetValue(PageConstants.MUSIC_COMMENT_ID, out var comment)
                && comment is Guid commentId && commentId != Guid.Empty)
                _ = current.OpenCommentAsync(commentId);
            return;
        }

        var transition = pageType == typeof(MusicsPage) ? MusicCoverTransition.Prepare(coverSource, coverPreview) : null;
        if (transition is not null)
        {
            parameter = parameter is null ? new() : new(parameter);
            parameter[MusicCoverTransition.ParameterKey] = transition;
        }
        try
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var navigated = transition is null
                ? _internalFrame.Navigate(pageType, parameter)
                : _internalFrame.Navigate(pageType, parameter, new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo());
            if (pageType == typeof(MusicsPage))
                System.Diagnostics.Debug.WriteLine($"[MusicNavigation] Frame.Navigate: {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms");
            if (!navigated) transition?.Dispose();
        }
        catch
        {
            transition?.Dispose();
            throw;
        }
        finally
        {
            // OnNavigatedTo takes ownership synchronously. Failed/canceled navigation
            // must not leave a snapshot in a caller-owned dictionary or journal.
            parameter?.Remove(MusicCoverTransition.ParameterKey);
        }
    }

    public void OpenFollowingActivities()
    {
        if (CurrentInternalPage is FollowingActivitiesPage) return;
        InternalNavigate(typeof(FollowingActivitiesPage));
    }

    public void OpenSettings(string? section = null)
    {
        _ = ObserveSettingsNavigationAsync(OpenSettingsAsync(section));
    }

    internal async Task OpenSettingsAsync(string? section = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var index = SettingsPage.GetSectionIndex(section);
        if (CurrentInternalPage is SettingsPage current)
        {
            await current.OpenSectionAsync(section, cancellationToken);
            return;
        }
        if (_internalFrame is null)
            throw new InvalidOperationException("The application navigation frame is not ready.");

        InternalNavigate(typeof(SettingsPage), new()
        {
            [PageConstants.SETTINGS_SECTION] = SettingsPage.GetSectionSlug(index)
        });
        if (CurrentInternalPage is not SettingsPage)
            throw new InvalidOperationException("Settings navigation did not complete.");
    }

    private static async Task ObserveSettingsNavigationAsync(Task navigation)
    {
        try { await navigation; }
        catch (OperationCanceledException) { }
        catch (Exception error) { AppLog.Error("Settings navigation could not be completed.", error); }
    }

    public void OpenMusicPlaylists()
    {
        if (CurrentInternalPage is MusicPlaylistsPage) return;
        InternalNavigate(typeof(MusicPlaylistsPage));
    }

    public void OpenClientProfile(Guid publicId)
    {
        if (publicId == Guid.Empty) return;
        if (CurrentInternalPage is ClientProfilePage page && page.ClientPublicId == publicId) return;
        InternalNavigate(typeof(ClientProfilePage), new() { [PageConstants.CLIENT_ID] = publicId });
    }

    public void OpenMusicPlaylist(Guid publicId)
    {
        if (publicId == Guid.Empty) return;
        if (CurrentInternalPage is MusicPlaylistPage page && page.PlaylistPublicId == publicId) return;
        InternalNavigate(typeof(MusicPlaylistPage), new() { [PageConstants.MUSIC_PLAYLIST_ID] = publicId });
    }

    public void MainGoBack()
    {
        if (_mainFrame?.CanGoBack == true)
            _mainFrame.GoBack();
    }

    public void InternalGoBack()
    {
        if (_internalFrame?.CanGoBack != true) return;
        var destination = _internalFrame.BackStack[^1];
        if (CurrentInternalPage is not MusicsPage music || destination.SourcePageType != typeof(MusicPlaylistPage)
            || destination.Parameter is not Dictionary<string, object> parameters)
        {
            _internalFrame.GoBack();
            return;
        }
        var transition = music.PrepareReturnCover();
        if (transition is null) { _internalFrame.GoBack(); return; }
        parameters[MusicCoverTransition.ParameterKey] = transition;
        parameters[MusicCoverTransition.ReturnMusicKey] = music.MusicPublicId;
        try { _internalFrame.GoBack(new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo()); }
        catch { transition.Dispose(); throw; }
        finally
        {
            if (parameters.Remove(MusicCoverTransition.ParameterKey)) transition.Dispose();
            parameters.Remove(MusicCoverTransition.ReturnMusicKey);
        }
    }
}


public class NavigationChangedEventArgs : EventArgs
{
    public Type? FromPageType { get; }
    public Type ToPageType { get; }
    public Dictionary<string, object>? Parameter { get; }
    public bool IsEnteringPage { get; }

    public NavigationChangedEventArgs(Type? fromPageType, Type toPageType, Dictionary<string, object>? parameter, bool isEnteringPage)
    {
        FromPageType = fromPageType;
        ToPageType = toPageType;
        Parameter = parameter;
        IsEnteringPage = isEnteringPage;
    }
}
