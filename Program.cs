using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Mizon.Windows.Services;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows;

// Redirect before constructing App, its services, sockets or any XAML window.
internal static partial class Program
{
    private static readonly ConcurrentQueue<AppActivationRequest> Activations = new();
    private static DispatcherQueue? _dispatcher;
    private static Action<AppActivationRequest>? _receiveActivation;
    private static AppInstance? _instance;
    private static readonly ConcurrentQueue<PendingAction> PendingActions = new();
    private static Func<string, bool>? _invokeAction;
    private static bool _exiting;

    [STAThread]
    public static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        _instance = AppInstance.FindOrRegisterForKey("Mizon.MainWindow");
        using var actionProviderReady = new EventWaitHandle(false, EventResetMode.ManualReset,
            $"Local\\Mizon.AppActions.Ready.{_instance.ProcessId}");
        if (!_instance.IsCurrent)
        {
            AllowSetForegroundWindow(_instance.ProcessId);
            // A COM activation has no destination until InvokeAsync arrives.
            // Wait for its factory: owning the instance key does not imply that
            // CoRegisterClassObject has finished during a concurrent cold start.
            if (Array.Exists(args, argument => argument == "--app-actions"))
                WaitForActionProvider(actionProviderReady, _instance.ProcessId);
            else
                RedirectActivation(activation, _instance);
            return 0;
        }
        Activations.Enqueue(AppActivationRequest.FromActivation(activation));
        _instance.Activated += (_, forwarded) =>
        {
            Activations.Enqueue(AppActivationRequest.FromActivation(forwarded));
            _dispatcher?.TryEnqueue(DrainActivations);
        };
        try
        {
            if (AppActionsService.IsSupported)
            {
                try
                {
                    Microsoft.AI.Actions.Helpers.ActionProviderFactory.RegisterActionProvider<
                        MizonActionProvider, global::Windows.AI.Actions.Provider.IActionProvider>(new MizonActionProvider());
                    actionProviderReady.Set();
                    AppLog.Information("Microsoft.AI.Actions COM provider registered.");
                }
                catch (Exception error) { AppLog.Error("App Actions COM provider registration failed.", error); }
            }
            Application.Start(_ =>
            {
                var dispatcher = DispatcherQueue.GetForCurrentThread();
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(dispatcher));
                new App();
            });
        }
        finally
        {
            _exiting = true;
            _invokeAction = null;
            while (PendingActions.TryDequeue(out var pending)) pending.Completion.TrySetResult(false);
            // SDK 0.1.0 owns its COM factory for the process lifetime; it exposes
            // no revocation API. Windows releases that registration at process exit.
            _instance.UnregisterKey();
        }
        return 0;
    }

    internal static void ConnectActivationHandler(DispatcherQueue dispatcher, Action<AppActivationRequest> handler)
    {
        _receiveActivation = handler;
        _dispatcher = dispatcher;
        DrainActivations();
    }

    private static void DrainActivations()
    {
        while (_receiveActivation is { } handler && Activations.TryDequeue(out var request)) handler(request);
    }

    internal static void ConnectAppActionHandler(Func<string, bool> handler)
    {
        _invokeAction = handler;
        DrainAppActions();
    }

    internal static Task<bool> InvokeAppActionAsync(string id, CancellationToken cancellationToken)
    {
        if (_exiting || !AppActionCatalog.TryResolve(id, out _)) return Task.FromResult(false);
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        PendingActions.Enqueue(new(id, completion, cancellationToken));
        _dispatcher?.TryEnqueue(DrainAppActions);
        return completion.Task.WaitAsync(cancellationToken);
    }

    private static void DrainAppActions()
    {
        while (_invokeAction is { } handler && PendingActions.TryDequeue(out var pending))
        {
            if (pending.CancellationToken.IsCancellationRequested)
                pending.Completion.TrySetCanceled(pending.CancellationToken);
            else
            {
                try { pending.Completion.TrySetResult(handler(pending.Id)); }
                catch (Exception error) { pending.Completion.TrySetException(error); }
            }
        }
    }

    private sealed record PendingAction(string Id, TaskCompletionSource<bool> Completion, CancellationToken CancellationToken);

    private static unsafe void WaitForActionProvider(EventWaitHandle ready, uint processId)
    {
        try
        {
            using var primary = Process.GetProcessById(checked((int)processId));
            nint* handles = stackalloc nint[2]
            {
                ready.SafeWaitHandle.DangerousGetHandle(), primary.SafeHandle.DangerousGetHandle()
            };
            var result = CoWaitForMultipleHandles(0, 15000, 2, handles, out var signaled);
            GC.KeepAlive(ready);
            GC.KeepAlive(primary);
            if (result < 0 || signaled != 0)
                AppLog.Information($"App Actions primary factory not ready (HRESULT=0x{result:X8}).");
        }
        catch (ArgumentException) { /* Primary exited during activation. */ }
    }

    private static void RedirectActivation(AppActivationArguments activation, AppInstance instance)
    {
        // Official WinUI STA pattern: redirect on MTA and pump COM while waiting.
        // Blocking the STA with .Wait() can deadlock activation redirection.
        using var completed = new EventWaitHandle(false, EventResetMode.ManualReset);
        var redirect = Task.Run(async () =>
        {
            try { await instance.RedirectActivationToAsync(activation); }
            finally { completed.Set(); }
        });
        var handle = completed.SafeWaitHandle.DangerousGetHandle();
        int result;
        unsafe { result = CoWaitForMultipleHandles(0, uint.MaxValue, 1, &handle, out _); }
        GC.KeepAlive(completed);
        Marshal.ThrowExceptionForHR(result);
        redirect.GetAwaiter().GetResult();
    }

    [LibraryImport("ole32.dll")]
    private static unsafe partial int CoWaitForMultipleHandles(uint flags, uint timeout, uint count, nint* handles, out uint index);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint processId);
}
