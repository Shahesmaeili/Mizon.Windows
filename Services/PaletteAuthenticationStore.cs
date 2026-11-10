using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Mizon.Windows.SearchIntegration;

public sealed class PaletteAuthenticationSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public string AccessToken { get; set; } = string.Empty;
    public string ApiBaseAddress { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public Guid Revision { get; set; }
}

// Linked into the separate search package. DPAPI CurrentUser protects the token
// at rest; neither package assets, command lines nor logs contain credentials.
public static class PaletteAuthenticationStore
{
    private const int MaximumBytes = 64 * 1024;
    private static readonly Lock Gate = new();
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Mizon.Windows.PaletteProvider.Auth.v1");
    private static string? _lastToken, _lastAddress;
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".mizon", "WindowsPaletteProvider", "Auth", "session.dat");

    public static PaletteAuthenticationSnapshot? Read()
    {
        byte[] encrypted;
        try
        {
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length is < 1 or > MaximumBytes) throw new InvalidDataException("Invalid search authentication file size.");
            encrypted = new byte[checked((int)stream.Length)];
            stream.ReadExactly(encrypted);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        var plaintext = Transform(encrypted, protect: false);
        try
        {
            var value = JsonSerializer.Deserialize(plaintext, PaletteAuthenticationJsonContext.Default.PaletteAuthenticationSnapshot);
            if (value is null || value.SchemaVersion != 1 || value.Revision == Guid.Empty ||
                string.IsNullOrWhiteSpace(value.AccessToken) || value.AccessToken.Length > 32768 || !IsApiAddress(value.ApiBaseAddress))
                throw new InvalidDataException("Invalid search authentication data.");
            return value;
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public static void Write(string? token, string apiBaseAddress)
    {
        if (string.IsNullOrWhiteSpace(token)) { Clear(); return; }
        if (token.Length > 32768) throw new ArgumentOutOfRangeException(nameof(token));
        if (!IsApiAddress(apiBaseAddress)) throw new ArgumentException("An HTTPS API address is required.", nameof(apiBaseAddress));
        lock (Gate)
        {
            if (string.Equals(_lastToken, token, StringComparison.Ordinal) &&
                string.Equals(_lastAddress, apiBaseAddress, StringComparison.Ordinal) && File.Exists(FilePath)) return;
            PaletteAuthenticationSnapshot? existing = null;
            try { existing = Read(); }
            catch (Exception error) when (error is InvalidDataException or JsonException or Win32Exception) { }
            if (existing is not null && string.Equals(existing.AccessToken, token, StringComparison.Ordinal) &&
                string.Equals(existing.ApiBaseAddress, apiBaseAddress, StringComparison.Ordinal))
            {
                _lastToken = token; _lastAddress = apiBaseAddress;
                return;
            }
            var snapshot = new PaletteAuthenticationSnapshot
            {
                AccessToken = token, ApiBaseAddress = apiBaseAddress,
                ExpiresAtUtc = ReadExpiration(token), Revision = Guid.NewGuid()
            };
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(snapshot, PaletteAuthenticationJsonContext.Default.PaletteAuthenticationSnapshot);
            byte[] encrypted;
            try { encrypted = Transform(plaintext, protect: true); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $"session.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(encrypted);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, FilePath, overwrite: true);
                _lastToken = token; _lastAddress = apiBaseAddress;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            try { File.Delete(FilePath); }
            catch (DirectoryNotFoundException) { }
            _lastToken = null; _lastAddress = null;
        }
    }

    private static bool IsApiAddress(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

    private static DateTimeOffset? ReadExpiration(string token)
    {
        // Only an expiry hint: the API still authenticates the token signature
        // and session. Parsing here never authenticates or renews a token.
        var pieces = token.Split('.');
        if (pieces.Length != 3) return null;
        try
        {
            var payload = pieces[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            var bytes = Convert.FromBase64String(payload);
            try
            {
                using var json = JsonDocument.Parse(bytes);
                return json.RootElement.TryGetProperty("exp", out var expiration) && expiration.TryGetInt64(out var seconds)
                    ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception error) when (error is FormatException or JsonException or ArgumentOutOfRangeException) { return null; }
    }

    private static unsafe byte[] Transform(byte[] value, bool protect)
    {
        fixed (byte* inputBytes = value)
        fixed (byte* entropyBytes = Entropy)
        {
            var input = new Blob { Length = value.Length, Data = (nint)inputBytes };
            var entropy = new Blob { Length = Entropy.Length, Data = (nint)entropyBytes };
            Blob output = default;
            try
            {
                var succeeded = protect
                    ? CryptProtectData(ref input, null, ref entropy, 0, 0, 1, out output)
                    : CryptUnprotectData(ref input, 0, ref entropy, 0, 0, 1, out output);
                if (!succeeded) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not protect the search authentication data.");
                if (output.Length is < 1 or > MaximumBytes) throw new InvalidDataException("Invalid protected authentication size.");
                return new ReadOnlySpan<byte>((void*)output.Data, output.Length).ToArray();
            }
            finally
            {
                if (output.Data != 0)
                {
                    CryptographicOperations.ZeroMemory(new Span<byte>((void*)output.Data, output.Length));
                    LocalFree(output.Data);
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Blob { internal int Length; internal nint Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, ref Blob entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, nint description, ref Blob entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PaletteAuthenticationSnapshot))]
internal partial class PaletteAuthenticationJsonContext : JsonSerializerContext;
