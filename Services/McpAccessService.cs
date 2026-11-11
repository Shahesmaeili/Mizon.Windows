using Microsoft.UI.Dispatching;
using Microsoft.Windows.Storage;
using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

/// <summary>Current-user IPC with user-approved, revocable client pairing.</summary>
public sealed class McpAccessService : INotifyPropertyChanged, IDisposable, IAsyncDisposable
{
    public static McpAccessService Current { get; } = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly SemaphoreSlim _consentGate = new(1, 1);
    private readonly ConcurrentDictionary<Task, byte> _consentTasks = new();
    private readonly Dictionary<string, McpConnectionItemViewModel> _clients = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<NamedPipeServerStream, byte> _pipes = new();
    private readonly ConcurrentDictionary<Task, byte> _sessions = new();
    private Task _listenerTask = Task.CompletedTask;
    private Task _shutdownTask = Task.CompletedTask;
    private DispatcherQueue? _dispatcher;
    private McpAuthenticationService? _authentication;
    private IMcpAppearanceService? _appearance;
    private IMcpNavigationService? _navigation;
    private IMcpTaskbarPinService? _taskbar;
    private IMcpMusicHomeService? _musicHome;
    private IMcpPresenceService? _presence;
    private IMcpWindowPresentationService? _windowPresentation;
    private volatile bool _enabled = true;
    private volatile bool _disposed;
    private string? _pipeName;
    private McpRememberedGrantStore? _grantStore;
    private bool _storeReady;
    private string? _storageErrorKey;

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<McpConnectionItemViewModel> Connections { get; } = [];
    public bool HasConnections => Connections.Count != 0;
    public bool HasPendingRequests => PendingCount != 0;
    public int PendingCount => Connections.Count(item => item.IsConnected && item.StatusCode == "pending");
    public bool HasStorageError => _storageErrorKey is not null;
    public string StorageError => _storageErrorKey is null ? string.Empty : LocalizationService.Current.GetString("Mcp/" + _storageErrorKey);
    public string Summary => LocalizationService.Current.Format("Mcp/SummaryFormat",
        Connections.Count(item => item.IsConnected), Connections.Count(item => item.CanRevoke), Connections.Count(item => item.IsBusy));
    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value || _disposed) return;
            _enabled = value;
            // Disabling suspends access without forgetting the user's approvals.
            // Revocation remains a separate, explicit action in settings.
            foreach (var item in Connections)
            {
                if (value) item.Resume();
                else item.Suspend();
            }
            try { ApplicationData.GetDefault().LocalSettings.Values["Mcp.Enabled"] = value; }
            catch (Exception error) { AppLog.Information($"MCP preference was not saved ({error.GetType().Name})."); }
            PropertyChanged?.Invoke(this, new(nameof(IsEnabled)));
        }
    }

    private McpAccessService() { }
    internal McpAccessService(McpRememberedGrantStore grantStore) => _grantStore = grantStore;

    internal void Start(DispatcherQueue dispatcher, McpAuthenticationService authentication, IMcpAppearanceService? appearance = null,
        IMcpNavigationService? navigation = null, IMcpTaskbarPinService? taskbar = null,
        IMcpMusicHomeService? musicHome = null, IMcpPresenceService? presence = null,
        IMcpWindowPresentationService? windowPresentation = null)
    {
        if (_dispatcher is not null || _disposed) return;
        _dispatcher = dispatcher;
        _authentication = authentication;
        _appearance = appearance;
        _navigation = navigation;
        _taskbar = taskbar;
        _musicHome = musicHome;
        _presence = presence;
        _windowPresentation = windowPresentation;
        try
        {
            _enabled = ApplicationData.GetDefault().LocalSettings.Values.TryGetValue("Mcp.Enabled", out var saved)
                ? saved is not false : true;
            _grantStore ??= new(Path.Combine(ApplicationData.GetDefault().LocalFolder.Path, "Mcp", "TrustedClients.dat"));
            using var identity = WindowsIdentity.GetCurrent();
            _pipeName = McpPipeProtocol.PipePrefix + (identity.User?.Value ?? throw new InvalidOperationException("Windows identity unavailable."));
            LocalizationService.Current.PropertyChanged += LocalizationChanged;
            _listenerTask = Task.Run(LoadAndListenAsync);
        }
        catch (Exception error) { AppLog.Information($"MCP bridge could not start ({error.GetType().Name})."); }
    }

    // These methods are invoked by native UI click handlers only. There is deliberately
    // no RPC/tool/protocol route that can approve a connection or change its permissions.
    public Task ApproveAsync(McpConnectionItemViewModel item) => TrackConsent(ApproveCoreAsync(item));
    public void Deny(McpConnectionItemViewModel item)
    {
        if (_disposed || !Connections.Contains(item) || !item.CanApprove) return;
        item.BeginConsentChange();
        item.Deny();
    }
    public Task RevokeAsync(McpConnectionItemViewModel item) => TrackConsent(RevokeCoreAsync(item));
    public Task RevokeAllAsync() => TrackConsent(RevokeAllCoreAsync());

    private async Task ApproveCoreAsync(McpConnectionItemViewModel item)
    {
        if (_disposed || !_enabled || !Connections.Contains(item) || !item.CanApprove) return;
        var revision = item.BeginConsentChange();
        var grant = item.CredentialId is { } id ? new McpRememberedGrant
        {
            Id = id, ClientName = item.Name, ClientVersion = item.Version,
            AllowRead = item.AllowRead, AllowControl = item.AllowControl,
            AllowSubmit = item.AllowControl && item.AllowSubmit, ApprovedAt = DateTimeOffset.UtcNow,
            AllowAppearanceRead = item.AllowAppearanceRead, AllowAppearanceControl = item.AllowAppearanceControl,
            AllowNavigationRead = item.AllowNavigationRead, AllowNavigationControl = item.AllowNavigationControl,
            AllowTaskbarRead = item.AllowTaskbarRead, AllowTaskbarControl = item.AllowTaskbarControl,
            AllowMusicHomeRead = item.AllowMusicHomeRead, AllowMusicHomeControl = item.AllowMusicHomeControl,
            AllowMusicHomeSubmit = item.AllowMusicHomeControl && item.AllowMusicHomeSubmit,
            AllowPresenceRead = item.AllowPresenceRead,
            AllowWindowRead = item.AllowWindowRead, AllowWindowControl = item.AllowWindowControl
        } : null;
        item.BeginSaving();
        var entered = false;
        try
        {
            await _consentGate.WaitAsync(_lifetime.Token);
            entered = true;
            if (_disposed || !_enabled || item.ConsentRevision != revision) return;
            if (grant is not null)
            {
                if (!_storeReady) { SetStorageError("StorageReadError"); return; }
                await _grantStore!.SaveAsync(grant, _lifetime.Token);
                if (_disposed || item.ConsentRevision != revision) return;
                item.RestoreGrant(grant);
            }
            else item.Approve(); // Legacy peers have no proof for persistent pairing.
            if (!_enabled) item.Suspend();
            SetStorageError(null);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { ReportStorageError("StorageSaveError", error); }
        finally
        {
            item.EndSaving();
            if (entered) _consentGate.Release();
        }
    }

    private async Task RevokeCoreAsync(McpConnectionItemViewModel item)
    {
        if (_disposed || !Connections.Contains(item)) return;
        item.BeginConsentChange();
        item.Revoke(); // Cancel every live worker before awaiting disk I/O.
        item.BeginSaving();
        var entered = false;
        try
        {
            await _consentGate.WaitAsync(_lifetime.Token);
            entered = true;
            if (item.CredentialId is { } id)
            {
                if (!_storeReady) { SetStorageError("StorageRevokeError"); return; }
                await _grantStore!.RemoveAsync(id, _lifetime.Token);
            }
            item.ForgetGrant();
            SetStorageError(null);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { ReportStorageError("StorageRevokeError", error); }
        finally
        {
            item.EndSaving();
            if (entered) _consentGate.Release();
        }
    }

    private async Task RevokeAllCoreAsync()
    {
        if (_disposed) return;
        var items = Connections.ToArray();
        foreach (var item in items)
        {
            item.BeginConsentChange();
            item.Revoke();
            item.BeginSaving();
        }
        var entered = false;
        try
        {
            await _consentGate.WaitAsync(_lifetime.Token);
            entered = true;
            // Explicit revoke-all can also recover from a corrupt/unreadable
            // stored document, but still must successfully write the empty set.
            await _grantStore!.ClearAsync(_lifetime.Token);
            _storeReady = true;
            foreach (var item in items) item.ForgetGrant();
            SetStorageError(null);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { ReportStorageError("StorageRevokeError", error); }
        finally
        {
            foreach (var item in items) item.EndSaving();
            if (entered) _consentGate.Release();
        }
    }

    private Task TrackConsent(Task task)
    {
        _consentTasks.TryAdd(task, 0);
        _ = task.ContinueWith(completed =>
        {
            _ = completed.Exception;
            _consentTasks.TryRemove(completed, out _);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return task;
    }

    private async Task LoadAndListenAsync()
    {
        try
        {
            var grants = await _grantStore!.ReadAsync(_lifetime.Token).ConfigureAwait(false);
            await OnUiAsync(() =>
            {
                foreach (var grant in grants)
                {
                    var item = new McpConnectionItemViewModel(CleanMetadata(grant.ClientName, "MCP client"),
                        CleanMetadata(grant.ClientVersion, string.Empty), grant.Id);
                    item.RestoreGrant(grant);
                    if (!_enabled) item.Suspend();
                    AddConnection(item);
                }
                _storeReady = true;
                return Task.FromResult(true);
            }, _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
        catch (Exception error)
        {
            AppLog.Information($"MCP saved permissions could not load ({error.GetType().Name}).");
            try { await OnUiAsync(() => { SetStorageError("StorageReadError"); return Task.FromResult(true); }, _lifetime.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
        // Start listening only after restoration, avoiding a startup approval race.
        await ListenAsync().ConfigureAwait(false);
    }

    private void AddConnection(McpConnectionItemViewModel item)
    {
        if (item.CredentialId is { } id) _clients.Add(id, item);
        item.PropertyChanged += ConnectionChanged;
        Connections.Insert(0, item);
        TrimHistory();
        NotifyConnections();
    }

    private void SetStorageError(string? key)
    {
        if (_disposed) return;
        _storageErrorKey = key;
        PropertyChanged?.Invoke(this, new(nameof(HasStorageError)));
        PropertyChanged?.Invoke(this, new(nameof(StorageError)));
    }

    private void ReportStorageError(string key, Exception error)
    {
        AppLog.Information($"MCP permission storage failed ({error.GetType().Name}).");
        SetStorageError(key);
    }

    private async Task ListenAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var pipe = new NamedPipeServerStream(_pipeName!, PipeDirection.InOut, 8,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                _pipes.TryAdd(pipe, 0);
                try { await pipe.WaitForConnectionAsync(_lifetime.Token).ConfigureAwait(false); }
                catch { _pipes.TryRemove(pipe, out _); pipe.Dispose(); throw; }
                var session = HandleConnectionAsync(pipe);
                _sessions.TryAdd(session, 0);
                _ = session.ContinueWith(completed =>
                {
                    // Observe failures even if a disconnect finished before shutdown's snapshot.
                    _ = completed.Exception;
                    _sessions.TryRemove(completed, out _);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                // Keep a spare listener without allowing an unbounded set of sessions.
                while (_pipes.Count >= 7 && !_lifetime.IsCancellationRequested)
                    await Task.Delay(200, _lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { AppLog.Information($"MCP listener stopped ({error.GetType().Name})."); }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe)
    {
        using var connectionLifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        McpConnectionItemViewModel? connection = null;
        Task? reader = null;
        try
        {
            using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(connectionLifetime.Token);
            helloTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            var hello = await McpPipeProtocol.ReadAsync(pipe, McpJsonContext.Default.McpRequest, helloTimeout.Token).ConfigureAwait(false);
            if (hello?.Operation != "connect") return;
            string? credentialId = null;
            if (hello.ClientCredential is not null)
            {
                if (!McpCredentialIdentity.TryGetId(hello.ClientCredential, out var verifiedId)) return;
                credentialId = verifiedId;
            }
            // Retain only a one-way identifier, never the credential, in app state.
            hello = hello with { ClientCredential = null };
            connection = await OnUiAsync(() =>
            {
                if (credentialId is not null && _clients.TryGetValue(credentialId, out var known))
                {
                    known.Attach();
                    return Task.FromResult(known);
                }
                var name = CleanMetadata(hello.ClientTitle, CleanMetadata(hello.ClientName, "MCP client"));
                var item = new McpConnectionItemViewModel(name, CleanMetadata(hello.ClientVersion, string.Empty), credentialId);
                item.Attach();
                AddConnection(item);
                return Task.FromResult(item);
            }, connectionLifetime.Token).ConfigureAwait(false);
            await WriteStatusAsync(pipe, connection, connectionLifetime.Token).ConfigureAwait(false);

            var requests = Channel.CreateBounded<McpRequest>(new BoundedChannelOptions(1)
            { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
            // Reading independently detects a disconnected client and cancels a pending
            // API submission even while the UI is awaiting its response.
            reader = ReadRequestsAsync(pipe, requests.Writer, connectionLifetime);
            await foreach (var request in requests.Reader.ReadAllAsync(connectionLifetime.Token).ConfigureAwait(false))
            {
                using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(connectionLifetime.Token);
                requestLifetime.CancelAfter(TimeSpan.FromSeconds(40));
                var response = await ExecuteAsync(connection, request, requestLifetime.Token).ConfigureAwait(false);
                await McpPipeProtocol.WriteAsync(pipe, response, McpJsonContext.Default.McpResponse, connectionLifetime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) when (connectionLifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            // Never log requests, credentials, token values, or exception payloads.
            AppLog.Information($"MCP connection ended ({error.GetType().Name}).");
        }
        finally
        {
            connectionLifetime.Cancel();
            pipe.Dispose();
            _pipes.TryRemove(pipe, out _);
            if (reader is not null)
            {
                try { await reader.ConfigureAwait(false); }
                catch (Exception error) { AppLog.Information($"MCP reader ended ({error.GetType().Name})."); }
            }
            if (connection is not null && !_disposed)
            {
                try { await OnUiAsync(() => { connection.Detach(); return Task.FromResult(true); }, _lifetime.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (Exception error) { AppLog.Information($"MCP disconnect cleanup failed ({error.GetType().Name})."); }
            }
        }
    }

    private static async Task ReadRequestsAsync(NamedPipeServerStream pipe, ChannelWriter<McpRequest> requests, CancellationTokenSource lifetime)
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var request = await McpPipeProtocol.ReadAsync(pipe, McpJsonContext.Default.McpRequest, lifetime.Token).ConfigureAwait(false);
                if (request is null) break;
                // The bridge sends one request at a time. Do not block the reader on
                // a full queue: it must keep observing EOF to cancel an active submit.
                if (!requests.TryWrite(request)) throw new InvalidDataException("Too many queued MCP requests.");
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException or System.Text.Json.JsonException) { }
        finally { requests.TryComplete(); lifetime.Cancel(); }
    }

    private Task WriteStatusAsync(NamedPipeServerStream pipe, McpConnectionItemViewModel connection, CancellationToken token) =>
        WriteStatusCoreAsync(pipe, connection, token);

    private async Task WriteStatusCoreAsync(NamedPipeServerStream pipe, McpConnectionItemViewModel connection, CancellationToken token)
    {
        var response = await OnUiAsync(() => Task.FromResult(Status(connection)), token).ConfigureAwait(false);
        await McpPipeProtocol.WriteAsync(pipe, response, McpJsonContext.Default.McpResponse, token).ConfigureAwait(false);
    }

    private async Task<McpResponse> ExecuteAsync(McpConnectionItemViewModel connection, McpRequest request, CancellationToken token)
    {
        return await OnUiAsync(async () =>
        {
            if (request.Operation == "connection_status") return Status(connection);
            if (!_enabled || !connection.CanExecute(request.Operation)) return Denied(connection);
            using var authorized = CancellationTokenSource.CreateLinkedTokenSource(token, connection.AuthorizationToken);
            var mutation = request.Operation is not ("get_state" or "get_appearance" or "get_navigation" or "get_taskbar_pin_state" or "get_music_home" or "get_presence_state" or "get_window_state");
            var enteredGate = false;
            var started = false;
            try
            {
                if (mutation)
                {
                    await _mutationGate.WaitAsync(authorized.Token);
                    enteredGate = true;
                }
                authorized.Token.ThrowIfCancellationRequested();
                // Recheck after waiting behind another connection's operation.
                if (!_enabled || !connection.CanExecute(request.Operation)) return Denied(connection);
                connection.SetBusy(request.Operation);
                started = true;
                var result = request.Operation switch
                {
                    "get_appearance" or "set_appearance" => _appearance is not null
                        ? await _appearance.ExecuteAsync(request, authorized.Token)
                        : new McpResponse { Code = "unavailable", Message = "The appearance service is unavailable." },
                    "get_navigation" or "open_destination" => _navigation is not null
                        ? await _navigation.ExecuteAsync(request, authorized.Token)
                        : new McpResponse { Code = "unavailable", Message = "The navigation service is unavailable." },
                    "get_taskbar_pin_state" or "request_taskbar_pin" => _taskbar is not null
                        ? await _taskbar.ExecuteAsync(request, authorized.Token)
                        : new McpResponse { Code = "unavailable", Message = "The taskbar service is unavailable." },
                    "get_presence_state" => _presence is not null
                        ? await _presence.ExecuteAsync(request, authorized.Token)
                        : new McpResponse { Code = "unavailable", Message = "The presence service is unavailable." },
                    "get_window_state" or "set_fullscreen" => _windowPresentation is not null
                        ? await _windowPresentation.ExecuteAsync(request, authorized.Token)
                        : new McpResponse { Code = "unavailable", Message = "The main-window service is unavailable." },
                    "get_music_home" or "refresh_music_home" or "music_home_action" or "music_home_submit" => _musicHome is not null
                        ? await _musicHome.ExecuteAsync(request, authorized.Token)
                        : new McpResponse { Code = "unavailable", Message = "The music home service is unavailable." },
                    _ => await _authentication!.ExecuteAsync(request, authorized.Token)
                };
                // Reapproval starts a new grant; an operation from the revoked grant
                // must not return its data under the new grant's permissions.
                authorized.Token.ThrowIfCancellationRequested();
                if (!_enabled || !connection.CanExecute(request.Operation)) return Denied(connection);
                return result with
                {
                    State = connection.CanExecute("get_state") ? result.State : null,
                    Appearance = connection.CanExecute("get_appearance") ? result.Appearance : null,
                    Navigation = connection.CanExecute("get_navigation") ? result.Navigation : null,
                    NavigationResult = connection.CanExecute("get_navigation") ? result.NavigationResult : null,
                    TaskbarPin = connection.CanExecute("get_taskbar_pin_state") ? result.TaskbarPin : null,
                    MusicHome = connection.CanExecute("get_music_home") ? result.MusicHome : null,
                    Presence = connection.CanExecute("get_presence_state") ? result.Presence : null,
                    Window = connection.CanExecute("get_window_state") ? result.Window : null,
                    Connection = connection.Snapshot()
                };
            }
            catch (OperationCanceledException)
            {
                return new() { Code = "cancelled", Message = "The request was cancelled or its permission was revoked.", Connection = connection.Snapshot() };
            }
            catch (Exception error)
            {
                AppLog.Information($"MCP operation failed ({error.GetType().Name}).");
                return new() { Code = "operation_failed", Message = "The operation could not be completed.", Connection = connection.Snapshot() };
            }
            finally
            {
                if (started) connection.SetBusy(null);
                if (enteredGate) _mutationGate.Release();
            }
        }, token).ConfigureAwait(false);
    }

    private McpResponse Status(McpConnectionItemViewModel connection) => new()
    {
        Success = true, Code = !_enabled ? "disabled" : connection.StatusCode,
        Message = connection.StatusCode == "pending"
            ? "Waiting for the user to approve this connection inside Mizon: Settings > AI connections. No app data or operations are available before approval."
            : connection.IsRemembered
                ? "This paired client's exact approved permissions are remembered across restarts until the user revokes them in Mizon settings. New feature groups require separate approval."
                : "This client has no remembered approval. Legacy connections without a pairing credential require approval again after reconnecting.",
        Connection = connection.Snapshot()
    };
    private McpResponse Denied(McpConnectionItemViewModel connection) => new()
    {
        Code = !_enabled ? "disabled" : connection.StatusCode == "pending" ? "approval_required" : "permission_denied",
        Message = "Ask the user to manage this connection in Mizon. Only the user can grant access; do not automate the approval UI.",
        Connection = connection.Snapshot()
    };

    private async Task<T> OnUiAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Cancellation can discard work still queued on the dispatcher. Once it
        // starts, keep owning its task until its cancellation/finally blocks finish.
        var dispatchState = 0; // 0 = queued, 1 = running, 2 = cancelled before start
        using var registration = token.Register(() =>
        {
            if (Interlocked.CompareExchange(ref dispatchState, 2, 0) == 0)
                completion.TrySetCanceled(token);
        });
        if (_dispatcher?.TryEnqueue(async () =>
        {
            if (Interlocked.CompareExchange(ref dispatchState, 1, 0) != 0) return;
            try
            {
                token.ThrowIfCancellationRequested();
                if (_disposed) throw new OperationCanceledException(token);
                completion.TrySetResult(await action());
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception error) { completion.TrySetException(error); }
        }) != true)
        {
            completion.TrySetCanceled();
        }
        return await completion.Task.ConfigureAwait(false);
    }

    private void ConnectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(McpConnectionItemViewModel.StatusCode) or nameof(McpConnectionItemViewModel.IsBusy)) NotifyConnections();
    }
    private void NotifyConnections()
    {
        foreach (var property in new[] { nameof(HasConnections), nameof(HasPendingRequests), nameof(PendingCount), nameof(Summary) })
            PropertyChanged?.Invoke(this, new(property));
    }
    private void LocalizationChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(LocalizationService.Revision)) return;
        foreach (var connection in Connections) connection.RefreshLocalization();
        NotifyConnections();
        PropertyChanged?.Invoke(this, new(nameof(StorageError)));
    }
    private void TrimHistory()
    {
        for (var index = Connections.Count - 1; index >= 0 && Connections.Count > 32; index--)
        {
            var item = Connections[index];
            if (item.IsConnected || item.IsRemembered || item.IsSaving) continue;
            item.PropertyChanged -= ConnectionChanged;
            Connections.RemoveAt(index);
            if (item.CredentialId is { } id) _clients.Remove(id);
            item.Dispose();
        }
    }
    private static string CleanMetadata(string? text, string fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        var result = new StringBuilder(100);
        foreach (var rune in text.EnumerateRunes())
        {
            // Client metadata is untrusted. Exclude bidi overrides/isolation and
            // other invisible format controls, not just ASCII control characters.
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator) continue;
            if (result.Length + rune.Utf16SequenceLength > 100) break;
            result.Append(rune.ToString());
        }
        return result.ToString().Trim() is { Length: > 0 } clean ? clean : fallback;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        foreach (var pipe in _pipes.Keys) pipe.Dispose();
        LocalizationService.Current.PropertyChanged -= LocalizationChanged;
        foreach (var connection in Connections)
        {
            connection.PropertyChanged -= ConnectionChanged;
            connection.Dispose();
        }
        _authentication?.Dispose();
        (_navigation as IDisposable)?.Dispose();
        (_taskbar as IDisposable)?.Dispose();
        (_musicHome as IDisposable)?.Dispose();
        (_presence as IDisposable)?.Dispose();
        (_windowPresentation as IDisposable)?.Dispose();
        // No synchronous UI-thread wait: active operations need that dispatcher
        // for their cancellation/finally blocks before shared resources can close.
        _shutdownTask = CompleteDisposalAsync();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return new(_shutdownTask);
    }

    private async Task CompleteDisposalAsync()
    {
        try
        {
            // The listener adds every session before it exits, so its completion
            // establishes the final set of work that shutdown must drain.
            try { await _listenerTask.ConfigureAwait(false); }
            catch (Exception error) { AppLog.Information($"MCP listener cleanup failed ({error.GetType().Name})."); }
            await Task.WhenAll(_sessions.Keys).ConfigureAwait(false);
            await Task.WhenAll(_consentTasks.Keys).ConfigureAwait(false);
        }
        catch (Exception error) { AppLog.Information($"MCP shutdown completed after an error ({error.GetType().Name})."); }
        finally
        {
            _mutationGate.Dispose();
            _consentGate.Dispose();
            _grantStore?.Dispose();
            _lifetime.Dispose();
        }
    }
}
