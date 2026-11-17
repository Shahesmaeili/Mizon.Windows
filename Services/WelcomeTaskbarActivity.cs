using Microsoft.UI.Dispatching;
using Microsoft.Windows.BadgeNotifications;
using System;
using System.Threading;

namespace Mizon.Windows.Services;

/// <summary>Shows the shell's activity badge only for a Welcome request that takes time.</summary>
internal sealed class WelcomeTaskbarActivity : IDisposable
{
    private readonly DispatcherQueueTimer _delay;
    private readonly CancellationToken _token;
    private readonly Func<bool, bool> _write;
    private readonly CancellationTokenRegistration _registration;
    private bool _shown, _disposed;

    internal WelcomeTaskbarActivity(DispatcherQueue dispatcher, CancellationToken token)
        : this(dispatcher, token, WriteBadge) { }

    internal WelcomeTaskbarActivity(DispatcherQueue dispatcher, CancellationToken token, Func<bool, bool> write)
    {
        _token = token;
        _write = write;
        _delay = dispatcher.CreateTimer();
        _delay.Interval = TimeSpan.FromMilliseconds(650);
        _delay.IsRepeating = false;
        _delay.Tick += DelayElapsed;
        _registration = token.Register(() => dispatcher.TryEnqueue(Dispose));
        if (!token.IsCancellationRequested) _delay.Start();
    }

    private void DelayElapsed(DispatcherQueueTimer sender, object args)
    {
        if (!_disposed && !_token.IsCancellationRequested) _shown = _write(true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _registration.Dispose();
        _delay.Stop();
        _delay.Tick -= DelayElapsed;
        if (_shown) _write(false);
    }

    private static bool WriteBadge(bool active)
    {
        try
        {
            var badges = BadgeNotificationManager.Current;
            if (active) badges.SetBadgeAsGlyph(BadgeNotificationGlyph.Activity);
            else badges.ClearBadge();
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error("Welcome activity badge could not be updated.", exception);
            return false;
        }
    }
}
