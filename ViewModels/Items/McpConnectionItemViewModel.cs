using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services;
using System;
using System.Globalization;
using System.Threading;

namespace Mizon.Windows.ViewModels.Items;

public sealed class McpConnectionItemViewModel : ViewModelBase, IDisposable
{
    private McpAccessPolicy _policy = new();
    private CancellationTokenSource _authorization = new();
    private bool _allowRead = true;
    private bool _allowControl = true;
    private bool _allowSubmit;
    private bool _allowAppearanceRead;
    private bool _allowAppearanceControl;
    private bool _allowNavigationRead;
    private bool _allowNavigationControl;
    private bool _allowTaskbarRead;
    private bool _allowTaskbarControl;
    private bool _isBusy;
    private string _currentOperation = string.Empty;
    private DateTimeOffset? _lastActivity;
    private bool _disposed;
    private int _connectionCount;
    private int _activeOperations;
    private int _consentChanges;
    private bool _isRemembered;
    private long _consentRevision;

    internal McpConnectionItemViewModel(string name, string version, string? credentialId = null)
    {
        CredentialId = credentialId;
        Id = credentialId ?? Guid.NewGuid().ToString("N");
        Name = name;
        Version = version;
        var normalized = name.ToLowerInvariant();
        LogoPath = normalized.Contains("claude", StringComparison.Ordinal) ? "ms-appx:///Assets/Mcp/Claude.svg"
            : normalized.Contains("codex", StringComparison.Ordinal) || normalized.Contains("openai", StringComparison.Ordinal)
                ? "ms-appx:///Assets/Mcp/OpenAI.svg" : "ms-appx:///Assets/AppIcon/Square150x150Logo.png";
    }

    public string Id { get; }
    internal string? CredentialId { get; }
    public string Name { get; }
    public string Version { get; }
    public string LogoPath { get; }
    public string StatusCode => !IsConnected && !_isRemembered && _policy.Status == "pending" ? "disconnected" : _policy.Status;
    public string StatusText => Text(StatusCode switch
    {
        "approved" when !IsConnected && IsRemembered => "StatusApprovedOffline",
        "approved" => "StatusApproved", "denied" => "StatusDenied", "revoked" => "StatusRevoked",
        "disconnected" => "StatusDisconnected", _ => "StatusPending"
    });
    public bool IsConnected => _connectionCount > 0;
    public bool IsRemembered => _isRemembered;
    public bool IsSaving => _consentChanges > 0;
    public string GrantHelpText => Text(CredentialId is null ? "SessionScopeHint" : "ConsentScopeHint");
    public bool IsBusy => _isBusy;
    public bool IsWorking => IsBusy || IsSaving;
    public bool CanApprove => !IsSaving && IsConnected && StatusCode != "approved";
    public bool CanRevoke => !IsSaving && (IsRemembered || StatusCode == "approved");
    public string CurrentOperation => _currentOperation switch
    {
        "get_state" => Text("ReadState"), "open_page" => Text("OpenPage"),
        "set_form" => Text("FillForm"), "submit" => Text("SubmitForm"),
        "clear_form" => Text("ClearForm"), "retry_welcome" => Text("RetryWelcome"),
        "set_language" => Text("ChangeLanguage"), "get_appearance" => Text("ReadAppearance"),
        "set_appearance" => Text("ChangeAppearance"), "get_navigation" => Text("ReadNavigation"),
        "open_destination" => Text("OpenDestination"), "get_taskbar_pin_state" => Text("ReadTaskbarPin"),
        "request_taskbar_pin" => Text("RequestTaskbarPin"), _ => string.Empty
    };
    public string LastActivityText => _lastActivity?.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo(LocalizationService.Current.LanguageTag)) ?? Text("NeverUsed");
    private DateTimeOffset ConnectedAt { get; } = DateTimeOffset.UtcNow;
    public string ConnectedAtText => ConnectedAt.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo(LocalizationService.Current.LanguageTag));
    public bool AllowRead { get => _allowRead; set { if (CanApprove) SetField(ref _allowRead, value); } }
    public bool AllowControl
    {
        get => _allowControl;
        set
        {
            if (!CanApprove) return;
            SetField(ref _allowControl, value);
            if (!value) SetField(ref _allowSubmit, false, nameof(AllowSubmit));
        }
    }
    public bool AllowSubmit { get => _allowSubmit; set { if (CanApprove) SetField(ref _allowSubmit, AllowControl && value); } }
    public bool AllowAppearanceRead { get => _allowAppearanceRead; set { if (CanApprove) SetField(ref _allowAppearanceRead, value); } }
    public bool AllowAppearanceControl { get => _allowAppearanceControl; set { if (CanApprove) SetField(ref _allowAppearanceControl, value); } }
    public bool AllowNavigationRead { get => _allowNavigationRead; set { if (CanApprove) SetField(ref _allowNavigationRead, value); } }
    public bool AllowNavigationControl { get => _allowNavigationControl; set { if (CanApprove) SetField(ref _allowNavigationControl, value); } }
    public bool AllowTaskbarRead { get => _allowTaskbarRead; set { if (CanApprove) SetField(ref _allowTaskbarRead, value); } }
    public bool AllowTaskbarControl { get => _allowTaskbarControl; set { if (CanApprove) SetField(ref _allowTaskbarControl, value); } }
    internal CancellationToken AuthorizationToken => _authorization.Token;
    internal bool CanExecute(string operation) => !_disposed && IsConnected && _policy.CanExecute(operation);
    internal long ConsentRevision => _consentRevision;
    internal long BeginConsentChange() => ++_consentRevision;

    internal void Attach()
    {
        if (_disposed) return;
        if (_policy.Status == "disconnected") _policy = new();
        _connectionCount++;
        NotifyStatus();
    }

    internal void Detach()
    {
        if (_connectionCount > 0) _connectionCount--;
        // Closing one worker must not cancel the other workers or erase a saved
        // grant. Each pipe already cancels its own operation lifetime on EOF.
        if (!IsConnected && !IsRemembered)
        {
            _policy.Disconnect();
            CancelAuthorization();
        }
        NotifyStatus();
    }

    internal void Approve()
    {
        if (_disposed) return;
        if (!AllowControl) SetField(ref _allowSubmit, false, nameof(AllowSubmit));
        if (_policy.Status == "disconnected") _policy = new();
        RenewAuthorization();
        _policy.Approve(AllowRead, AllowControl, AllowSubmit, AllowAppearanceRead, AllowAppearanceControl,
            AllowNavigationRead, AllowNavigationControl, AllowTaskbarRead, AllowTaskbarControl);
        NotifyStatus();
    }

    internal void RestoreGrant(McpRememberedGrant grant)
    {
        if (_disposed || grant.Id != CredentialId) return;
        SetField(ref _allowRead, grant.AllowRead, nameof(AllowRead));
        SetField(ref _allowControl, grant.AllowControl, nameof(AllowControl));
        SetField(ref _allowSubmit, grant.AllowControl && grant.AllowSubmit, nameof(AllowSubmit));
        SetField(ref _allowAppearanceRead, grant.AllowAppearanceRead, nameof(AllowAppearanceRead));
        SetField(ref _allowAppearanceControl, grant.AllowAppearanceControl, nameof(AllowAppearanceControl));
        SetField(ref _allowNavigationRead, grant.AllowNavigationRead, nameof(AllowNavigationRead));
        SetField(ref _allowNavigationControl, grant.AllowNavigationControl, nameof(AllowNavigationControl));
        SetField(ref _allowTaskbarRead, grant.AllowTaskbarRead, nameof(AllowTaskbarRead));
        SetField(ref _allowTaskbarControl, grant.AllowTaskbarControl, nameof(AllowTaskbarControl));
        _isRemembered = true;
        Approve();
    }

    internal void ForgetGrant() { _isRemembered = false; NotifyStatus(); }
    internal void Suspend() => CancelAuthorization();
    internal void Resume() { if (_policy.Status == "approved") RenewAuthorization(); }
    private void RenewAuthorization()
    {
        _authorization.Cancel();
        _authorization.Dispose();
        _authorization = new();
    }

    internal void BeginSaving() { _consentChanges++; NotifyStatus(); }
    internal void EndSaving() { if (_consentChanges > 0) _consentChanges--; NotifyStatus(); }

    internal void Deny() { _policy.Deny(); CancelAuthorization(); NotifyStatus(); }
    internal void Revoke() { _policy.Revoke(); CancelAuthorization(); NotifyStatus(); }

    private void CancelAuthorization()
    {
        if (!_disposed) _authorization.Cancel();
    }

    internal void SetBusy(string? operation)
    {
        _lastActivity = DateTimeOffset.UtcNow;
        if (operation is not null) _activeOperations++;
        else if (_activeOperations > 0) _activeOperations--;
        SetField(ref _isBusy, _activeOperations > 0, nameof(IsBusy));
        if (operation is not null || _activeOperations == 0)
            SetField(ref _currentOperation, operation ?? string.Empty, nameof(CurrentOperation));
        OnPropertyChanged(nameof(LastActivityText));
        OnPropertyChanged(nameof(IsWorking));
    }

    internal McpConnectionStatus Snapshot() => new()
    {
        Id = Id, ClientName = Name, Status = StatusCode,
        IsConnected = IsConnected, IsRemembered = IsRemembered,
        AllowRead = _policy.AllowRead, AllowControl = _policy.AllowControl, AllowSubmit = _policy.AllowSubmit,
        AllowAppearanceRead = _policy.AllowAppearanceRead, AllowAppearanceControl = _policy.AllowAppearanceControl,
        AllowNavigationRead = _policy.AllowNavigationRead, AllowNavigationControl = _policy.AllowNavigationControl,
        AllowTaskbarRead = _policy.AllowTaskbarRead, AllowTaskbarControl = _policy.AllowTaskbarControl
    };

    internal void RefreshLocalization()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(LastActivityText));
        OnPropertyChanged(nameof(ConnectedAtText));
        OnPropertyChanged(nameof(CurrentOperation));
        OnPropertyChanged(nameof(GrantHelpText));
    }

    private static string Text(string key) => LocalizationService.Current.GetString("Mcp/" + key);
    private void NotifyStatus()
    {
        OnPropertyChanged(nameof(StatusCode)); OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsConnected)); OnPropertyChanged(nameof(CanApprove)); OnPropertyChanged(nameof(CanRevoke));
        OnPropertyChanged(nameof(IsRemembered)); OnPropertyChanged(nameof(IsSaving));
        OnPropertyChanged(nameof(IsWorking));
    }
    public void Dispose()
    {
        if (_disposed) return;
        _connectionCount = 0;
        _policy.Disconnect();
        CancelAuthorization();
        _disposed = true;
        _authorization.Dispose();
    }
}
