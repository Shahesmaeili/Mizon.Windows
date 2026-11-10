using Microsoft.AI.Actions.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Windows.AI.Actions;
using Windows.Foundation.Metadata;

namespace Mizon.Windows.Services;

/// <summary>Synchronizes available Windows actions with the app's language and account.</summary>
internal sealed class AppActionsService : IDisposable
{
    private readonly LocalizationService _localization;
    private readonly LocalStorageService _storage;
    private readonly Action<AppActivationRequest> _navigate;
    private readonly Channel<AvailabilityState> _updates = Channel.CreateBounded<AvailabilityState>(
        new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;
    private bool _signedIn;
    private bool _disposed;

    internal static bool IsSupported => ApiInformation.IsMethodPresent(
        "Windows.AI.Actions.ActionRuntime", "SetActionAvailability");

    internal AppActionsService(LocalStorageService storage, bool signedIn, Action<AppActivationRequest> navigate)
    {
        _storage = storage;
        _signedIn = signedIn;
        _navigate = navigate;
        _localization = LocalizationService.Current;
        _storage.AuthenticationChanged += AuthenticationChanged;
        _localization.PropertyChanged += LanguageChanged;
        _worker = Task.Run(UpdateAvailabilityAsync);
        QueueUpdate();
    }

    // Called only on the XAML dispatcher after account initialization. Availability
    // is a discovery hint, not authorization: recheck stale cached invocations here.
    internal bool Invoke(string id)
    {
        if (_disposed || !AppActionCatalog.TryResolve(id, out var entry)
            || entry.RequiresSignIn != _signedIn)
            return false;

        _navigate(new AppActivationRequest(Destination: entry.Destination));
        AppLog.Information($"App Action invoked: {entry.Id}.");
        return true;
    }

    private void AuthenticationChanged(object? sender, bool signedIn)
    {
        _signedIn = signedIn;
        QueueUpdate();
    }

    private void LanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(LocalizationService.LanguageTag)) QueueUpdate();
    }

    private void QueueUpdate()
    {
        if (!_disposed) _updates.Writer.TryWrite(new(_signedIn, _localization.LanguageTag));
    }

    private async Task UpdateAvailabilityAsync()
    {
        ActionRuntime? runtime = null;
        var applied = new Dictionary<string, bool>(StringComparer.Ordinal);
        try
        {
            await foreach (var state in _updates.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
            {
                try
                {
                    // Activating the Windows broker is synchronous. Keep it off
                    // the UI thread; a failed broker must not block login or navigation.
                    runtime ??= ActionRuntimeFactory.CreateActionRuntime();
                    // Disable the old set first. There is no atomic batch in this SDK.
                    foreach (var enabling in new[] { false, true })
                    {
                        foreach (var entry in AppActionCatalog.Entries)
                        {
                            _shutdown.Token.ThrowIfCancellationRequested();
                            var available = AppActionCatalog.IsAvailable(entry, state.SignedIn, state.LanguageTag);
                            if (available != enabling) continue;
                            if (applied.TryGetValue(entry.Id, out var previous) && previous == available) continue;
                            runtime.SetActionAvailability(entry.Id, available);
                            applied[entry.Id] = available;
                        }
                    }
                    AppLog.Information($"App Actions synchronized: signedIn={state.SignedIn}, language={state.LanguageTag}.");
                }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { break; }
                catch (Exception error)
                {
                    // Retry on the next account/language change, never in a tight loop.
                    runtime = null;
                    applied.Clear();
                    AppLog.Error("Windows App Actions availability could not be synchronized.", error);
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        finally
        {
            // ActionRuntime is not IClosable; release references to its WinRT proxy.
            runtime = null;
            applied.Clear();
            _shutdown.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _storage.AuthenticationChanged -= AuthenticationChanged;
        _localization.PropertyChanged -= LanguageChanged;
        _shutdown.Cancel();
        _updates.Writer.TryComplete();
        // The worker owns CTS disposal, avoiding a race with an in-flight COM call.
        // Do not synchronously wait on the UI thread for the Windows broker.
        _ = _worker;
    }

    private readonly record struct AvailabilityState(bool SignedIn, string LanguageTag);
}
