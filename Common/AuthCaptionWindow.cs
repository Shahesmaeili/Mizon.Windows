using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;

namespace Mizon.Windows.Common;

internal static class AuthCaptionWindow
{
    internal static IAuthCaptionNavigation? Create(Window? window, AppWindow? appWindow)
    {
        if (window is null || appWindow is null) return null;
        InputNonClientPointerSource? pointer = null;
        try { pointer = InputNonClientPointerSource.GetForWindowId(appWindow.Id); }
        catch (Exception exception) when (exception is COMException or InvalidOperationException) { }

        return new NativeAuthCaption(WinRT.Interop.WindowNative.GetWindowHandle(window),
            () => appWindow.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Minimized },
            action =>
            {
                if (appWindow.Presenter is OverlappedPresenter presenter &&
                    (action == AuthWindowAction.Minimize && !presenter.IsMinimizable
                    || action == AuthWindowAction.MaximizeOrRestore && !presenter.IsMaximizable)) return default;
                var kind = action switch
                {
                    AuthWindowAction.Minimize => NonClientRegionKind.Minimize,
                    AuthWindowAction.MaximizeOrRestore => NonClientRegionKind.Maximize,
                    _ => NonClientRegionKind.Close
                };
                try
                {
                    if (pointer is not null)
                        foreach (var region in pointer.GetRegionRects(kind))
                            if (region.Width > 0 && region.Height > 0)
                                return (true, region.X, region.Y, region.Width, region.Height);
                }
                catch (Exception exception) when (exception is COMException or InvalidOperationException) { }
                return (true, 0, 0, 0, 0);
            });
    }
}
