using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

// One-way account presentation IPC. The companion neither receives credentials nor
// repeats the authenticated profile/media requests already made by the app.
internal static class TaskbarProfileBridge
{
    private static readonly TaskbarProfilePublisher Publisher = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mizon", "TaskbarProfile"),
        Path.Combine(AppContext.BaseDirectory, "Taskbar", "Payload"));
    internal static long CurrentSession => Publisher.CurrentSession;
    internal static void SynchronizeAuthentication(string? token) => Publisher.SynchronizeAuthentication(token);
    internal static void PublishProfile(long session, string displayName, string? imagePath, Guid? clientPublicId = null)
        => Publisher.PublishProfile(session, displayName, imagePath, clientPublicId);
    internal static void PublishMood(long session, Guid? publicId, string? emoji, string? title, string? animationPath, string? effectPath)
        => Publisher.PublishMood(session, publicId, emoji, title, animationPath, effectPath);
}

internal sealed class TaskbarProfilePublisher(string directoryPath, string payloadDirectory)
{
    private readonly System.Threading.Lock Gate = new();
    private string? _authenticationFingerprint;
    private long _session, _revision = DateTime.UtcNow.Ticks;
    private bool _authenticated, _writing, _launching, _launched;
    private Snapshot? _pending, _current;
    private Task _writeTask = Task.CompletedTask, _launchTask = Task.CompletedTask;

    internal long CurrentSession { get { lock (Gate) return _session; } }

    internal void SynchronizeAuthentication(string? token)
    {
        var fingerprint = string.IsNullOrWhiteSpace(token) ? null
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        Task? logoutWrite = null;
        lock (Gate)
        {
            if (_session != 0 && string.Equals(_authenticationFingerprint, fingerprint, StringComparison.Ordinal)) return;
            _authenticationFingerprint = fingerprint;
            _authenticated = fingerprint is not null;
            _launched = false;
            ++_session;
            // A saved token alone does not expose an old account. Wait for the
            // existing Me request to validate it and publish the current user.
            Queue(new(_session, NextRevision(), false, null, string.Empty, null));
            if (fingerprint is null) logoutWrite = _writeTask;
        }
        // SetJwtToken(null) also runs on Welcome/auth failure. Make that tiny
        // logout snapshot durable before returning, even if the app closes at
        // once. No lock is held and payload staging runs on a different task.
        logoutWrite?.GetAwaiter().GetResult();
    }

    internal void PublishProfile(long session, string displayName, string? imagePath, Guid? clientPublicId = null)
    {
        lock (Gate)
        {
            if (!_authenticated || session != _session) return;
            var name = string.IsNullOrWhiteSpace(displayName) ? "حساب میزون" : displayName.Trim();
            var path = LocalMediaPath(imagePath);
            if (_current is { IsAuthenticated: true } current && current.Session == session &&
                current.DisplayName == name && current.ImagePath == path && current.ClientPublicId == clientPublicId)
            {
                ScheduleLaunch();
                return;
            }
            // Completing an avatar download or refreshing Me must not erase the
            // independently prepared mood paths for this same account.
            var snapshot = _current is { IsAuthenticated: true } previous && previous.Session == session && previous.ClientPublicId == clientPublicId
                ? previous with { Revision = NextRevision(), DisplayName = name, ImagePath = path }
                : new Snapshot(session, NextRevision(), true, clientPublicId, name, path);
            Queue(snapshot);
        }
    }

    internal void PublishMood(long session, Guid? publicId, string? emoji, string? title, string? animationPath, string? effectPath)
    {
        lock (Gate)
        {
            if (!_authenticated || session != _session || _current is not { IsAuthenticated: true } current || current.Session != session) return;
            var hasMood = publicId is { } id && id != Guid.Empty && !string.IsNullOrWhiteSpace(emoji);
            publicId = hasMood ? publicId : null;
            emoji = hasMood ? emoji!.Trim() : null;
            title = hasMood ? title?.Trim() : null;
            animationPath = hasMood ? LocalMediaPath(animationPath) : null;
            effectPath = hasMood ? LocalMediaPath(effectPath) : null;
            if (current.MoodPublicId == publicId && current.MoodEmoji == emoji && current.MoodTitle == title &&
                current.MoodAnimationPath == animationPath && current.MoodEffectPath == effectPath) return;
            Queue(current with
            {
                Revision = NextRevision(), MoodPublicId = publicId, MoodEmoji = emoji, MoodTitle = title,
                MoodAnimationPath = animationPath, MoodEffectPath = effectPath
            });
        }
    }

    private long NextRevision() => _revision = Math.Max(DateTime.UtcNow.Ticks, _revision + 1);

    private static string? LocalMediaPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                if (!uri.IsFile || uri.IsUnc) return null;
                value = uri.LocalPath;
            }
            if (!Path.IsPathFullyQualified(value)) return null;
            var path = Path.GetFullPath(value);
            return File.Exists(path) ? path : null;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException) { return null; }
    }

    // Called under Gate. A single writer coalesces rapid image/name changes.
    private void Queue(Snapshot snapshot)
    {
        _current = _pending = snapshot;
        if (_writing) return;
        _writing = true;
        _writeTask = Task.Run(WritePending);
    }

    private void WritePending()
    {
        while (true)
        {
            Snapshot snapshot;
            lock (Gate)
            {
                if (_pending is null) { _writing = false; return; }
                snapshot = _pending; _pending = null;
            }
            try
            {
                Directory.CreateDirectory(directoryPath);
                var temporary = Path.Combine(directoryPath, $"profile.{Guid.NewGuid():N}.tmp");
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var writer = new Utf8JsonWriter(stream))
                    {
                        writer.WriteStartObject(); writer.WriteNumber("version", 1); writer.WriteNumber("revision", snapshot.Revision);
                        writer.WriteBoolean("isAuthenticated", snapshot.IsAuthenticated);
                        if (snapshot.ClientPublicId is { } id) writer.WriteString("clientPublicId", id); else writer.WriteNull("clientPublicId");
                        writer.WriteString("displayName", snapshot.DisplayName); writer.WriteString("imagePath", snapshot.ImagePath);
                        if (snapshot.MoodPublicId is { } moodId) writer.WriteString("moodPublicId", moodId); else writer.WriteNull("moodPublicId");
                        writer.WriteString("moodEmoji", snapshot.MoodEmoji); writer.WriteString("moodTitle", snapshot.MoodTitle);
                        writer.WriteString("moodAnimationPath", snapshot.MoodAnimationPath); writer.WriteString("moodEffectPath", snapshot.MoodEffectPath);
                        writer.WriteEndObject(); writer.Flush();
                    }
                    lock (Gate)
                    {
                        // Logout can happen while this file is being encoded.
                        // Never publish an older session after its invalidation.
                        if (_current?.Revision != snapshot.Revision) continue;
                        File.Move(temporary, Path.Combine(directoryPath, "profile.json"), overwrite: true);
                    }
                    if (snapshot.IsAuthenticated) ScheduleLaunch();
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            catch (Exception error) { AppLog.Information($"Taskbar profile synchronization failed ({error.GetType().Name})."); }
        }
    }

    private void ScheduleLaunch()
    {
        lock (Gate)
        {
            if (_launching || !_authenticated || _current?.IsAuthenticated != true) return;
            if (_launched && IsCompanionRunning()) return;
            _launched = false;
            _launching = true;
            // Payload hashing/copying must not delay logout snapshot publication.
            _launchTask = Task.Run(() =>
            {
                try
                {
                    var (executable, fingerprint) = PrepareRuntime();
                    var activePath = Path.Combine(directoryPath, "active-runtime.txt");
                    var previous = File.Exists(activePath) ? File.ReadAllText(activePath).Trim() : null;
                    lock (Gate)
                    {
                        if (!_authenticated || _current?.IsAuthenticated != true) return;
                        var start = new ProcessStartInfo(executable)
                        {
                            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                            WorkingDirectory = Path.GetDirectoryName(executable)!
                        };
                        if (previous is not null && !string.Equals(previous, fingerprint, StringComparison.Ordinal)) start.ArgumentList.Add("--replace");
                        using var process = Process.Start(start) ?? throw new InvalidOperationException("The taskbar companion did not start.");
                        _launched = true;
                    }
                    var temporary = activePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try { File.WriteAllText(temporary, fingerprint); File.Move(temporary, activePath, overwrite: true); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                catch (Exception error) { AppLog.Information($"Taskbar companion launch failed ({error.GetType().Name})."); }
                finally { lock (Gate) _launching = false; }
            });
        }
    }

    private static bool IsCompanionRunning()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (!System.Threading.Mutex.TryOpenExisting(@"Local\Mizon.Taskbar.Profile." + identity.User?.Value, out var mutex)) return false;
            using (mutex)
            {
                try
                {
                    if (!mutex.WaitOne(0)) return true;
                    mutex.ReleaseMutex();
                }
                catch (System.Threading.AbandonedMutexException) { mutex.ReleaseMutex(); }
                return false;
            }
        }
        catch (UnauthorizedAccessException) { return false; }
    }

    internal async Task WaitForIdleAsync()
    {
        while (true)
        {
            Task writer, launcher;
            lock (Gate) { writer = _writeTask; launcher = _launchTask; }
            await Task.WhenAll(writer, launcher).ConfigureAwait(false);
            lock (Gate)
                if (!_writing && !_launching && ReferenceEquals(writer, _writeTask) && ReferenceEquals(launcher, _launchTask)) return;
        }
    }

    private (string Executable, string Fingerprint) PrepareRuntime()
    {
        var source = payloadDirectory;
        const string executableName = "Mizon.Windows.Taskbar.exe";
        if (!File.Exists(Path.Combine(source, executableName))) throw new FileNotFoundException("Taskbar companion payload is missing.");
        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(source, path), StringComparer.Ordinal).ToArray();
        var entries = new List<(string Source, string Relative, byte[] Hash)>(files.Length);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(source, file);
            using var stream = File.OpenRead(file);
            var contentHash = SHA256.HashData(stream);
            hash.AppendData(Encoding.UTF8.GetBytes(relative.Replace('\\', '/') + "\n")); hash.AppendData(contentHash);
            entries.Add((file, relative, contentHash));
        }
        var fingerprint = Convert.ToHexStringLower(hash.GetHashAndReset());
        var runtimeRoot = Path.Combine(directoryPath, "Runtime");
        var runtime = Path.Combine(runtimeRoot, fingerprint);
        var marker = Path.Combine(runtime, ".complete");
        if (!File.Exists(marker))
        {
            Directory.CreateDirectory(runtimeRoot);
            var staging = Path.Combine(runtimeRoot, "staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                foreach (var entry in entries)
                {
                    var destination = Path.Combine(staging, entry.Relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(entry.Source, destination, overwrite: false);
                    using var stream = File.OpenRead(destination);
                    if (!SHA256.HashData(stream).AsSpan().SequenceEqual(entry.Hash)) throw new IOException("Taskbar payload changed during staging.");
                }
                File.WriteAllText(Path.Combine(staging, ".complete"), fingerprint);
                try { Directory.Move(staging, runtime); }
                catch (IOException) when (File.Exists(marker)) { }
            }
            finally
            {
                // Only this invocation's generated staging directory is removed.
                var relative = Path.GetRelativePath(runtimeRoot, Path.GetFullPath(staging));
                if (relative.StartsWith("staging-", StringComparison.Ordinal) && !relative.Contains(Path.DirectorySeparatorChar) && Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
        }
        return (Path.Combine(runtime, executableName), fingerprint);
    }

    private sealed record Snapshot(long Session, long Revision, bool IsAuthenticated, Guid? ClientPublicId, string DisplayName, string? ImagePath,
        Guid? MoodPublicId = null, string? MoodEmoji = null, string? MoodTitle = null, string? MoodAnimationPath = null, string? MoodEffectPath = null);
}
