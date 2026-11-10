namespace Mizon.Windows.Mcp;

// This name is resolved only by the production source linked into the test
// assembly. It substitutes its SID lookup, never transport/permission behavior.
// The companion executable has no test override or configurable production pipe.
internal sealed class WindowsIdentity : IDisposable
{
    internal static string PipeSuffix { get; private set; } = "IsolatedTests." + Guid.NewGuid().ToString("N");
    internal static string StorageDirectory => Path.Combine(Path.GetTempPath(), "Mizon.Mcp.Tests", PipeSuffix);
    internal static string CredentialDirectory => Path.Combine(StorageDirectory, "identities");
    internal const string ClientProfile = "2e4cb72ed0604736b37e527bbdbe93cf";
    internal static void UseIsolatedPipe(string suffix)
    {
        if (!suffix.StartsWith("IsolatedTests.", StringComparison.Ordinal)
            || !Guid.TryParseExact(suffix[14..], "N", out _)) throw new ArgumentException("Invalid isolated fixture ID.");
        PipeSuffix = suffix;
    }
    public TestUser User { get; } = new();
    public static WindowsIdentity GetCurrent() => new();
    public void Dispose() { }

    internal sealed class TestUser
    {
        public string Value => PipeSuffix;
    }
}
