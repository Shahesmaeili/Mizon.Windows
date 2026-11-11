namespace Mizon.Windows.Mcp.Tests;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args is ["--isolated-mcp-host", var suffix])
            return await ConnectionLifecycleTests.RunIsolatedHostAsync(suffix);
        if (args is ["--credential-fixture", var credentialSuffix])
            return await PersistentGrantTests.RunCredentialWorkerAsync(credentialSuffix);
        var isolatedConnections = args is ["--isolated-connections"];
        var allIsolated = args is ["--isolated"];
        var catalogOnly = args is ["--catalog", _];
        var serverPath = args is ["--server" or "--catalog", var path] ? path : null;
        if (!isolatedConnections && !allIsolated && (serverPath is null || !File.Exists(serverPath)))
        {
            Console.Error.WriteLine("Usage: Mizon.Windows.Mcp.Tests --isolated | --isolated-connections | --catalog <companion exe> | --server <companion exe>");
            return 2;
        }

        (string Name, Func<Task> Run)[] connectionTests =
        [
            ("SDK discovery in multiple workers creates no app sessions", ConnectionLifecycleTests.DiscoveryCreatesNoConnection),
            ("SDK appearance requests preserve typed preferences and require consent", ConnectionLifecycleTests.AppearanceTypedDispatch),
            ("SDK navigation accepts only declared destinations and preserves typed IDs", ConnectionLifecycleTests.NavigationTypedDispatch),
            ("SDK navigation returns invocation receipts and additional data without changing outcomes", ConnectionLifecycleTests.NavigationReceiptDispatch),
            ("SDK taskbar tools require isolated consent and reject undeclared actions", ConnectionLifecycleTests.TaskbarTypedDispatch),
            ("SDK presence is read-only, argument-free and independently gated", ConnectionLifecycleTests.PresenceTypedDispatch),
            ("SDK VPN tools preserve typed inputs, reject arbitrary URIs and require consent", ConnectionLifecycleTests.VpnTypedDispatch),
            ("VPN permissions and generated state are independent and bounded", VpnTests.ScopeAndSnapshot),
            ("VPN remembered consent defaults denied and persists exact grants", VpnTests.PersistentScopes),
            ("SDK main-window tools have typed input and independent scopes", ConnectionLifecycleTests.WindowTypedDispatch),
            ("Window permissions and generated state are independent and bounded", WindowPresentationTests.ScopeAndSnapshot),
            ("Window remembered consent defaults denied and persists exact grants", WindowPresentationTests.PersistentScopes),
            ("SDK music home routes typed actions, rejects incompatible inputs and preserves consent", ConnectionLifecycleTests.MusicHomeTypedDispatch),
            ("Calls distinguish a delayed handshake from an absent app", ConnectionLifecycleTests.CallSurvivesSlowHandshake),
            ("Repeated calls reuse a live connection", ConnectionLifecycleTests.RepeatedCallsRetainConnection),
            ("Unconfigured profiles remain session-only without identity files", ConnectionLifecycleTests.UnconfiguredProfileStaysSessionOnly),
            ("Cancelled handshakes close the pipe before another call", ConnectionLifecycleTests.HandshakeCancellationClosesConnection),
            ("Handshake EOF does not trigger an automatic reconnect", ConnectionLifecycleTests.HandshakeEofDoesNotRetry),
            ("Failed mutation responses are never replayed", ConnectionLifecycleTests.MutationIsNotReplayed),
            ("Cancelled exchanges cannot contaminate later replies", ConnectionLifecycleTests.CancellationCannotMixReplies),
            ("Queued cancellation preserves the active connection", ConnectionLifecycleTests.QueuedCancellationKeepsActiveConnection),
            ("Disposal cancels active I/O without waiting for its timeout", ConnectionLifecycleTests.DisposalCancelsActiveExchange),
            ("Companion disposal is idempotent", ConnectionLifecycleTests.DisposalIsIdempotent)
        ];
        (string Name, Func<Task> Run)[] standardTests =
        [
            ("Pending consent blocks every private operation", PolicyTests.Pending),
            ("Read, control and submit scopes are independent", PolicyTests.Scopes),
            ("Deny, revoke and disconnect clear grants", PolicyTests.Revocation),
            ("Music home scopes and consequential submissions are independent", MusicHomeContractTests.Permissions),
            ("Presence consent is independent and source-generated snapshots carry no private IDs", PresenceTests.ScopeAndSnapshot),
            ("Music home schemas and bounded snapshots survive actual framing", MusicHomeContractTests.SnapshotRoundTrip),
            ("Responses expose presence flags, never credential fields", ProtocolTests.ResponseContract),
            ("Deep-link results validate strict versioned correlated JSON", DeepLinkResultTests.Protocol),
            ("Navigation receipts preserve typed outcome data through generated framing", DeepLinkResultTests.McpReceiptRoundTrip),
            ("Framing preserves Unicode and consecutive fragmented messages", ProtocolTests.RoundTrip),
            ("Framing distinguishes clean EOF from truncated messages", ProtocolTests.EndOfStream),
            ("Framing rejects invalid sizes and malformed JSON", ProtocolTests.InvalidFrames),
            ("Oversize writes do not send a partial frame", ProtocolTests.OversizeWrite),
            ("Cancellation interrupts a blocked frame read", ProtocolTests.Cancellation),
            ("Actual SDK discovery, schemas and unapproved calls", allIsolated
                ? ConnectionLifecycleTests.CompanionCatalogIsolated
                : () => CompanionTests.Run(Path.GetFullPath(serverPath!)))
        ];
        (string Name, Func<Task> Run)[] persistenceTests =
        [
            ("Concurrent companions retain a protected client identity", PersistentGrantTests.CredentialSurvivesConcurrentWorkers),
            ("Approval restores exact scopes across store restarts", PersistentGrantTests.ExactScopesSurviveRestart),
            ("Music home grants persist exactly and older grants default denied", MusicHomeContractTests.PersistentScopes),
            ("Presence consent defaults denied and survives exact-scope restoration", PresenceTests.PersistentScope),
            ("Names and malformed proofs cannot restore another grant", PersistentGrantTests.NamesCannotRestoreOtherCredentials),
            ("Offline revocation and revoke-all survive restart", PersistentGrantTests.OfflineRevocationSurvivesRestart),
            ("Corrupt identities and grants fail closed", PersistentGrantTests.CorruptionFailsClosed),
            ("Persistence failure cannot report a saved approval", PersistentGrantTests.PersistenceFailureCannotClaimSuccess),
            ("Cancelled persistence retains existing grants", PersistentGrantTests.CancellationPreservesExistingGrants)
        ];
        (string Name, Func<Task> Run)[] tests = catalogOnly
            ? [("Published companion discovery, schemas and local argument validation", () => CompanionTests.Run(Path.GetFullPath(serverPath!), catalogOnly: true))]
            : isolatedConnections ? connectionTests : allIsolated ? [.. standardTests, .. persistenceTests, .. connectionTests] : standardTests;
        var failed = 0;
        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                failed++;
                // Test payloads are synthetic; never dump a real MCP response.
                Console.Error.WriteLine($"FAIL {test.Name}: {exception.GetType().Name}: {exception.Message}");
            }
        }
        CleanupStorage();
        Console.WriteLine($"{tests.Length - failed}/{tests.Length} test groups passed. No app launch, real consent approval or account submission was performed.");
        return failed == 0 ? 0 : 1;
    }

    private static void CleanupStorage()
    {
        var root = Path.GetFullPath(WindowsIdentity.StorageDirectory);
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Mizon.Mcp.Tests"));
        if (!string.Equals(Path.GetDirectoryName(root), parent, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(root).StartsWith("IsolatedTests.", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected test storage location.");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

internal static class Check
{
    public static void That(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static async Task Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
