using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Mizon.Windows.Mcp.Contracts;

[SupportedOSPlatform("windows")]
internal static class McpProtectedFile
{
    public static async Task<FileStream> AcquireAsync(string path, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None, 1, FileOptions.Asynchronous);
            }
            catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(15))
            {
                await Task.Delay(30, token).ConfigureAwait(false);
            }
        }
    }

    public static async Task<byte[]?> ReadAsync(string path, string purpose, CancellationToken token)
    {
        byte[] encrypted;
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous);
            if (file.Length is <= 0 or > 1_048_576) throw new IOException("Mizon MCP protected file has an invalid size.");
            encrypted = new byte[(int)file.Length];
            await file.ReadExactlyAsync(encrypted, token).ConfigureAwait(false);
        }
        catch (FileNotFoundException) { return null; }
        try { return McpUserDataProtection.Unprotect(encrypted, purpose); }
        finally { CryptographicOperations.ZeroMemory(encrypted); }
    }

    public static async Task WriteAsync(string path, byte[] plaintext, string purpose, CancellationToken token)
    {
        var encrypted = McpUserDataProtection.Protect(plaintext, purpose);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await file.WriteAsync(encrypted, token).ConfigureAwait(false);
                await file.FlushAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);
            // The temporary path is generated in the same known directory only.
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
