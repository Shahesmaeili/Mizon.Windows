using Microsoft.UI.Dispatching;
using Mizon.API;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

/// <summary>Publishes only this window's identity to the independent XAML preview adapter.</summary>
internal sealed partial class TaskbarPreviewXamlService : IDisposable
{
    private readonly nint _window;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mizon", "TaskbarPreview");
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<MizonSocket> _getSocket;
    private bool _started;
    private bool _disposed;

    internal TaskbarPreviewXamlService(nint window, DispatcherQueue dispatcher, Func<MizonSocket> getSocket)
    {
        _window = window;
        _dispatcher = dispatcher;
        _getSocket = getSocket;
    }

    internal void Update(bool signedIn)
    {
        if (_disposed) return;
        try
        {
            Directory.CreateDirectory(_directory);
            var destination = Path.Combine(_directory, "window.txt");
            if (!signedIn)
            {
                StopOnlineStatus();
                File.Delete(destination);
                return;
            }
            var temporary = Path.Combine(_directory, "window-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temporary, $"{_window}|{Environment.ProcessId}|{LocalizationService.Current.LanguageTag}");
                File.Move(temporary, destination, true);
            }
            finally { File.Delete(temporary); }
            StartOnlineStatus();
            if (_started) return;
            _started = true;
            _ = StartAsync(_lifetime.Token);
        }
        catch (Exception error) { AppLog.Error("Taskbar XAML preview configuration failed.", error); }
    }

    private async Task StartAsync(CancellationToken token)
    {
        try
        {
            await Task.Run(async () =>
            {
                var source = Path.Combine(AppContext.BaseDirectory, "Taskbar", "PreviewXaml");
                var library = Path.Combine(source, "Mizon.Windows.PreviewXaml.dll");
                if (!File.Exists(library)) throw new FileNotFoundException("The experimental taskbar XAML payload was not built.");
                var payload = Directory.GetFiles(source).OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                foreach (var file in payload) hash.AppendData(await File.ReadAllBytesAsync(file, token));
                var version = Convert.ToHexString(hash.GetHashAndReset())[..24];
                var runtime = Path.Combine(_directory, "runtime", version);
                Directory.CreateDirectory(runtime);
                foreach (var file in payload)
                {
                    token.ThrowIfCancellationRequested();
                    var target = Path.Combine(runtime, Path.GetFileName(file));
                    if (!File.Exists(target)) File.Copy(file, target);
                }
                token.ThrowIfCancellationRequested();
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = Path.Combine(runtime, "Mizon.Windows.PreviewXaml.Host.exe"),
                        UseShellExecute = false, CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    }
                };
                process.StartInfo.ArgumentList.Add("--watch");
                process.StartInfo.ArgumentList.Add(Path.Combine(runtime, "Mizon.Windows.PreviewXaml.dll"));
                process.Start();
                await process.WaitForExitAsync(token);
                if (process.ExitCode != 0) throw new InvalidOperationException("Taskbar XAML attachment failed. See the preview adapter log.");
            }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            AppLog.Error("Taskbar XAML preview attachment failed.", error);
            _dispatcher.TryEnqueue(() => { if (!_disposed) _started = false; });
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { StopOnlineStatus(); }
        catch (Exception error) { AppLog.Error("Taskbar online status cleanup failed.", error); }
        _lifetime.Cancel();
        _lifetime.Dispose();
        try { File.Delete(Path.Combine(_directory, "window.txt")); }
        catch (Exception error) { AppLog.Error("Taskbar XAML preview configuration cleanup failed.", error); }
    }
}
