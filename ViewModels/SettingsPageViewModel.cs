using Microsoft.UI.Xaml.Controls;
using Mizon.API.Client.Operations.Media;
using Mizon.Windows.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Mizon.Windows.ViewModels;

public sealed class SettingsPageViewModel : ViewModelBase, IDisposable
{
    private static readonly HashSet<string> AllowedSettingsUris = new(StringComparer.OrdinalIgnoreCase)
    {
        "ms-settings:notifications",
        "ms-settings:privacy",
        "ms-settings:easeofaccess-visualeffects",
        "ms-settings:signinoptions"
    };
    private readonly LocalizationService _localization = LocalizationService.Current;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly string? _packageVersion;
    private SettingsStorageSnapshot? _storageSnapshot;
    private int _selectedSection;
    private bool _isStorageBusy;
    private bool _storageError;
    private string _statusKey = string.Empty;
    private bool _statusIsError;
    private bool _disposed;

    public SettingsPageViewModel(HomePageViewModel account, MediaDownloader downloader)
    {
        Account = account ?? throw new ArgumentNullException(nameof(account));
        Storage = new SettingsStorageService(downloader);
        _packageVersion = ReadPackageVersion();
        _localization.PropertyChanged += LocalizationChanged;
    }

    public HomePageViewModel Account { get; }
    public SettingsStorageService Storage { get; }

    public int SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (value is < 0 or > 8 || !SetField(ref _selectedSection, value))
                return;
            OnPropertyChanged(nameof(IsAccount));
            OnPropertyChanged(nameof(IsSecurity));
            OnPropertyChanged(nameof(IsLanguage));
            OnPropertyChanged(nameof(IsAppearance));
            OnPropertyChanged(nameof(IsNotifications));
            OnPropertyChanged(nameof(IsStorage));
            OnPropertyChanged(nameof(IsUpdates));
            OnPropertyChanged(nameof(IsPlatforms));
            OnPropertyChanged(nameof(IsAiConnections));
        }
    }

    public bool IsAccount => SelectedSection == 0;
    public bool IsSecurity => SelectedSection == 1;
    public bool IsLanguage => SelectedSection == 2;
    public bool IsAppearance => SelectedSection == 3;
    public bool IsNotifications => SelectedSection == 4;
    public bool IsStorage => SelectedSection == 5;
    public bool IsUpdates => SelectedSection == 6;
    public bool IsPlatforms => SelectedSection == 7;
    public bool IsAiConnections => SelectedSection == 8;

    public string VersionLabel => _packageVersion ?? Text("VersionUnavailable");
    public string FrameworkLabel => RuntimeInformation.FrameworkDescription;
    public string ArchitectureLabel => RuntimeInformation.ProcessArchitecture.ToString();

    public bool IsStorageBusy => _isStorageBusy;
    public string StorageSizeLabel => _storageSnapshot is null
        ? Text("StorageNotScanned") : FormatBytes(_storageSnapshot.Bytes);
    public string StorageFilesLabel => _storageSnapshot is null
        ? Text("StorageNotScanned") : Format("StorageFilesFormat", _storageSnapshot.FileCount);
    public string StoragePendingLabel => _storageSnapshot is null
        ? Text("StorageNotScanned") : Format("StoragePendingFormat", _storageSnapshot.PendingFileCount);
    public string StorageState => _storageError ? Text("StorageError") : string.Empty;

    public string StatusText => HasStatus ? _localization.GetString(_statusKey) : string.Empty;
    public bool HasStatus => _statusKey.Length != 0;
    public InfoBarSeverity StatusSeverity => _statusIsError ? InfoBarSeverity.Error : InfoBarSeverity.Informational;

    public void ShowStatus(string key, bool error = false)
    {
        if (_disposed)
            return;
        _statusKey = string.IsNullOrWhiteSpace(key) ? string.Empty
            : key.StartsWith("Settings/", StringComparison.Ordinal) ? key : "Settings/" + key;
        _statusIsError = error;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(StatusSeverity));
    }

    public async Task RefreshStorageAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || _isStorageBusy || cancellationToken.IsCancellationRequested)
            return;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCancellation.Token);
        SetField(ref _isStorageBusy, true, nameof(IsStorageBusy));
        _storageError = false;
        OnPropertyChanged(nameof(StorageState));
        try
        {
            // The service performs file enumeration on a worker; resume here on the UI thread.
            var snapshot = await Storage.GetSnapshotAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (_disposed)
                return;
            _storageSnapshot = snapshot;
            NotifyStorageLabels();
            if (_statusKey == "Settings/StorageError")
                ShowStatus(string.Empty);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            // Navigating away cancels the scan without replacing a previously valid snapshot.
        }
        catch (Exception error)
        {
            if (_disposed)
                return;
            AppLog.Error("Could not read application storage usage.", error);
            _storageError = true;
            OnPropertyChanged(nameof(StorageState));
            ShowStatus("StorageError", true);
        }
        finally
        {
            _isStorageBusy = false;
            if (!_disposed)
                OnPropertyChanged(nameof(IsStorageBusy));
        }
    }

    public async Task OpenFolderAsync(string knownPath)
    {
        if (_disposed)
            return;
        try
        {
            if (!await Storage.OpenFolderAsync(knownPath))
                ShowStatus("FolderError", true);
        }
        catch (Exception error)
        {
            AppLog.Error("Could not open a settings storage folder.", error);
            ShowStatus("FolderError", true);
        }
    }

    public async Task OpenWindowsSettingsAsync(string uri)
    {
        if (_disposed)
            return;
        if (!AllowedSettingsUris.Contains(uri))
        {
            ShowStatus("SystemSettingsError", true);
            return;
        }
        try
        {
            if (!await Launcher.LaunchUriAsync(new Uri(uri)))
                ShowStatus("SystemSettingsError", true);
        }
        catch (Exception error)
        {
            AppLog.Error("Could not open Windows settings.", error);
            ShowStatus("SystemSettingsError", true);
        }
    }

    public void CopyVersionInformation()
    {
        if (_disposed)
            return;
        try
        {
            var content = new DataPackage();
            content.SetText($"Mizon {VersionLabel}\n{FrameworkLabel}\n{ArchitectureLabel}\n{_localization.LanguageTag}");
            Clipboard.SetContent(content);
            ShowStatus("VersionCopied");
        }
        catch (Exception error)
        {
            AppLog.Error("Could not copy application version information.", error);
            ShowStatus("ClipboardError", true);
        }
    }

    private static string? ReadPackageVersion()
    {
        try
        {
            var version = Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
        catch (Exception error)
        {
            AppLog.Information($"Package version could not be read ({error.GetType().Name}).");
            return null;
        }
    }

    private string Text(string key) => _localization.GetString("Settings/" + key);

    private string Format(string key, long value) =>
        string.Format(CultureInfo.GetCultureInfo(_localization.LanguageTag), Text(key), value);

    private string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];
        double size = Math.Max(0, bytes);
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return size.ToString(unit == 0 ? "N0" : "0.##", CultureInfo.GetCultureInfo(_localization.LanguageTag)) + " " + units[unit];
    }

    private void LocalizationChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(LocalizationService.Revision))
            return;
        OnPropertyChanged(nameof(VersionLabel));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StorageState));
        NotifyStorageLabels();
    }

    private void NotifyStorageLabels()
    {
        OnPropertyChanged(nameof(StorageSizeLabel));
        OnPropertyChanged(nameof(StorageFilesLabel));
        OnPropertyChanged(nameof(StoragePendingLabel));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _localization.PropertyChanged -= LocalizationChanged;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
    }
}
