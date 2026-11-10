using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Mizon.Windows.Mcp.Contracts;

// DPAPI binds persisted credentials/grants to the current Windows user. It is
// protection at rest, not a sandbox against other processes running as that user.
[SupportedOSPlatform("windows")]
internal static partial class McpUserDataProtection
{
    public static byte[] Protect(byte[] plaintext, string purpose) => Transform(plaintext, purpose, protect: true);
    public static byte[] Unprotect(byte[] ciphertext, string purpose) => Transform(ciphertext, purpose, protect: false);

    private static unsafe byte[] Transform(byte[] input, string purpose, bool protect)
    {
        var entropy = Encoding.UTF8.GetBytes("Mizon.Mcp.v1/" + purpose);
        DataBlob output = default;
        try
        {
            fixed (byte* inputPointer = input)
            fixed (byte* entropyPointer = entropy)
            {
                var data = new DataBlob { Length = input.Length, Pointer = (nint)inputPointer };
                var extra = new DataBlob { Length = entropy.Length, Pointer = (nint)entropyPointer };
                var succeeded = protect
                    ? CryptProtectData(ref data, 0, ref extra, 0, 0, 1, out output)
                    : CryptUnprotectData(ref data, 0, ref extra, 0, 0, 1, out output);
                if (succeeded == 0) throw new CryptographicException("Mizon MCP protected data is unavailable for this Windows user.");
                if (output.Length <= 0 || output.Length > 1_048_576 || output.Pointer == 0)
                    throw new CryptographicException("Mizon MCP protected data has an invalid size.");
                return new ReadOnlySpan<byte>((void*)output.Pointer, output.Length).ToArray();
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(entropy);
            if (output.Pointer != 0)
            {
                if (output.Length is > 0 and <= 1_048_576)
                    CryptographicOperations.ZeroMemory(new Span<byte>((void*)output.Pointer, output.Length));
                _ = LocalFree(output.Pointer);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Length; public nint Pointer; }

    [LibraryImport("crypt32.dll", SetLastError = true)]
    private static partial int CryptProtectData(ref DataBlob data, nint description, ref DataBlob entropy,
        nint reserved, nint prompt, uint flags, out DataBlob result);
    [LibraryImport("crypt32.dll", SetLastError = true)]
    private static partial int CryptUnprotectData(ref DataBlob data, nint description, ref DataBlob entropy,
        nint reserved, nint prompt, uint flags, out DataBlob result);
    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
