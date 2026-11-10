using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mizon.Windows.Mcp.Contracts;

namespace Mizon.Windows.Mcp.Tests;

internal static class PersistentGrantTests
{
    public static async Task<int> RunCredentialWorkerAsync(string suffix)
    {
        WindowsIdentity.UseIsolatedPipe(suffix);
        var credential = await new McpClientCredentialStore(WindowsIdentity.CredentialDirectory)
            .GetOrCreateAsync(WindowsIdentity.ClientProfile, CancellationToken.None);
        Check.That(McpCredentialIdentity.TryGetId(credential, out var id), "Credential was invalid.");
        Console.WriteLine(id); // Public fingerprint only; never the pairing credential.
        return 0;
    }

    public static async Task CredentialSurvivesConcurrentWorkers()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ids = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var worker = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                ArgumentList = { "--credential-fixture", WindowsIdentity.PipeSuffix },
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
            }) ?? throw new IOException("Could not start isolated credential fixture.");
            try
            {
                var output = worker.StandardOutput.ReadToEndAsync(timeout.Token);
                var errors = worker.StandardError.ReadToEndAsync(timeout.Token);
                await worker.WaitForExitAsync(timeout.Token);
                Check.That(worker.ExitCode == 0, "An isolated credential worker failed.");
                Check.That(string.IsNullOrWhiteSpace(await errors), "An isolated credential worker wrote unexpected diagnostics.");
                return (await output).Trim();
            }
            finally { if (!worker.HasExited) worker.Kill(entireProcessTree: true); }
        }));
        Check.That(ids.Distinct(StringComparer.Ordinal).Count() == 1, "Concurrent workers created different identities for one client.");
        var store = new McpClientCredentialStore(WindowsIdentity.CredentialDirectory);
        var credential = await store.GetOrCreateAsync(Guid.ParseExact(WindowsIdentity.ClientProfile, "N").ToString("D"), timeout.Token);
        Check.That(McpCredentialIdentity.TryGetId(credential, out var id) && id == ids[0], "Restart changed the same client's identity.");
        var other = await store.GetOrCreateAsync(Guid.NewGuid().ToString("N"), timeout.Token);
        Check.That(other != credential, "Different MCP clients share one credential.");
        await Check.Throws<ArgumentException>(async () => await store.GetOrCreateAsync("Codex", timeout.Token));
        foreach (var file in Directory.EnumerateFiles(WindowsIdentity.CredentialDirectory))
        {
            var bytes = await File.ReadAllBytesAsync(file, timeout.Token);
            Check.That(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(credential)) < 0, "Credential was saved as plaintext.");
            Check.That(bytes.AsSpan().IndexOf(Convert.FromBase64String(credential)) < 0, "Credential was saved as unprotected bytes.");
        }
    }

    public static async Task ExactScopesSurviveRestart()
    {
        var path = NewGrantPath();
        var grant = Grant();
        using (var first = new McpRememberedGrantStore(path))
        {
            Check.That((await first.ReadAsync(default)).Count == 0, "A fresh store granted access before approval.");
            await first.SaveAsync(grant, default);
        }
        using var restarted = new McpRememberedGrantStore(path);
        var restored = (await restarted.ReadAsync(default)).Single();
        Check.That(restored == grant, "Saved approval metadata or scopes changed across restart.");
        var policy = new McpAccessPolicy();
        policy.Approve(restored.AllowRead, restored.AllowControl, restored.AllowSubmit);
        Check.That(policy.CanExecute("get_state") && !policy.CanExecute("set_form") && !policy.CanExecute("submit"),
            "Restoring a read-only grant broadened its permissions.");
        Check.That(!policy.CanExecute("music_play") && !policy.CanExecute("settings_update"), "Authentication approval inherited another feature domain.");
        await restarted.SaveAsync(grant with { AllowRead = false, AllowControl = true, AllowSubmit = true }, default);
        using var nextRestart = new McpRememberedGrantStore(path);
        var changed = (await nextRestart.ReadAsync(default)).Single();
        Check.That(!changed.AllowRead && changed.AllowControl && changed.AllowSubmit, "Changed exact scopes were not durable.");
    }

    public static async Task NamesCannotRestoreOtherCredentials()
    {
        var first = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var second = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Check.That(McpCredentialIdentity.TryGetId(first, out var firstId), "First credential was invalid.");
        Check.That(McpCredentialIdentity.TryGetId(second, out var secondId), "Second credential was invalid.");
        using var store = new McpRememberedGrantStore(NewGrantPath());
        await store.SaveAsync(Grant(firstId), default);
        var saved = await store.ReadAsync(default);
        Check.That(saved.Any(g => g.Id == firstId) && saved.All(g => g.Id != secondId), "Matching names restored a different credential.");
        foreach (var malformed in new string?[] { null, "", "Codex", first[..^1], Convert.ToBase64String(new byte[31]), Convert.ToBase64String(new byte[33]) })
            Check.That(!McpCredentialIdentity.TryGetId(malformed, out _), "Missing/malformed proof was accepted.");
    }

    public static async Task OfflineRevocationSurvivesRestart()
    {
        var path = NewGrantPath();
        var first = Grant();
        var second = Grant() with { ClientName = "Claude" };
        using (var store = new McpRememberedGrantStore(path))
        {
            await store.SaveAsync(first, default);
            await store.SaveAsync(second, default);
        }
        using (var offline = new McpRememberedGrantStore(path)) await offline.RemoveAsync(first.Id, default);
        using (var restarted = new McpRememberedGrantStore(path))
        {
            Check.That((await restarted.ReadAsync(default)).Single().Id == second.Id, "Offline revoke did not persist or revoked an unrelated client.");
            await restarted.ClearAsync(default);
        }
        using var final = new McpRememberedGrantStore(path);
        Check.That((await final.ReadAsync(default)).Count == 0, "Revoke-all restored grants after restart.");
    }

    public static async Task CorruptionFailsClosed()
    {
        var path = NewGrantPath();
        using (var store = new McpRememberedGrantStore(path)) await store.SaveAsync(Grant(), default);
        await File.WriteAllBytesAsync(path, "corrupt synthetic grant store"u8.ToArray());
        using var corrupt = new McpRememberedGrantStore(path);
        await ExpectStorageFailure(async () => await corrupt.ReadAsync(default));
        await ExpectStorageFailure(() => corrupt.SaveAsync(Grant(), default));
        Check.That(Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path)) == "corrupt synthetic grant store", "Corrupt grant state was silently overwritten.");
        await corrupt.ClearAsync(default);
        Check.That((await corrupt.ReadAsync(default)).Count == 0, "Explicit revoke-all could not recover a corrupt grant store safely.");

        var directory = Path.Combine(WindowsIdentity.StorageDirectory, Guid.NewGuid().ToString("N"), "credentials");
        await new McpClientCredentialStore(directory).GetOrCreateAsync(WindowsIdentity.ClientProfile, default);
        var credentialPath = Directory.EnumerateFiles(directory).Single(file => new FileInfo(file).Length != 0);
        await File.WriteAllBytesAsync(credentialPath, "corrupt synthetic identity"u8.ToArray());
        await ExpectStorageFailure(async () => await new McpClientCredentialStore(directory).GetOrCreateAsync(WindowsIdentity.ClientProfile, default));
        Check.That(Encoding.UTF8.GetString(await File.ReadAllBytesAsync(credentialPath)) == "corrupt synthetic identity", "Corrupt client identity was silently replaced.");
    }

    public static async Task PersistenceFailureCannotClaimSuccess()
    {
        var blocker = Path.Combine(WindowsIdentity.StorageDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(blocker)!);
        await File.WriteAllTextAsync(blocker, "file blocks directory creation");
        using var store = new McpRememberedGrantStore(Path.Combine(blocker, "grants.dat"));
        await ExpectStorageFailure(() => store.SaveAsync(Grant(), default));
    }

    public static async Task CancellationPreservesExistingGrants()
    {
        var path = NewGrantPath();
        var grant = Grant();
        using var store = new McpRememberedGrantStore(path);
        await store.SaveAsync(grant, default);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Check.Throws<OperationCanceledException>(() => store.RemoveAsync(grant.Id, cancel.Token));
        await Check.Throws<OperationCanceledException>(() => store.ClearAsync(cancel.Token));
        await Check.Throws<OperationCanceledException>(() => store.SaveAsync(grant with { AllowRead = false }, cancel.Token));
        Check.That((await store.ReadAsync(default)).Single() == grant, "Cancelled persistence changed an existing grant.");
    }

    private static McpRememberedGrant Grant(string? id = null)
    {
        if (id is null)
        {
            McpCredentialIdentity.TryGetId(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), out var generated);
            id = generated;
        }
        return new() { Id = id, ClientName = "Codex", ClientVersion = "synthetic", AllowRead = true, AllowControl = false, AllowSubmit = false, ApprovedAt = DateTimeOffset.UtcNow };
    }

    private static string NewGrantPath() => Path.Combine(WindowsIdentity.StorageDirectory, Guid.NewGuid().ToString("N"), "grants.dat");

    private static async Task ExpectStorageFailure(Func<Task> action)
    {
        try { await action(); }
        catch (Exception error) when (error is IOException or CryptographicException or UnauthorizedAccessException or JsonException) { return; }
        throw new InvalidOperationException("Invalid storage unexpectedly reported success.");
    }
}
