using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;

namespace Mizon.Windows.Services;

public class NavigationService
{
    private Frame? _mainFrame;
    private Frame? _internalFrame;
    private Type? _mainCurrentPageType;
    private Type? _internalCurrentPageType;

    public event EventHandler<NavigationChangedEventArgs>? MainNavigationChanged;
    public event EventHandler<NavigationChangedEventArgs>? InternalNavigationChanged;

    public void MainInitialize(Frame mainFrame)
    {
        _mainFrame = mainFrame;
        _mainFrame.Navigating += OnMainFrameNavigating;
        _mainFrame.Navigated += OnMainFrameNavigated;
    }

    public void InternalInitialize(Frame internalFrame)
    {
        _internalFrame = internalFrame;
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

    public void InternalNavigate(Type pageType, Dictionary<string, object>? parameter = null)
    {
        _internalFrame?.Navigate(pageType, parameter);
    }

    public void MainGoBack()
    {
        if (_mainFrame?.CanGoBack == true)
            _mainFrame.GoBack();
    }

    public void InternalGoBack()
    {
        if (_internalFrame?.CanGoBack == true)
            _internalFrame.GoBack();
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