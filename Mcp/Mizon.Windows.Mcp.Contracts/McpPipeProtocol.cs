using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Mizon.Windows.Mcp.Contracts;

public static class McpPipeProtocol
{
    public const int MaximumMessageBytes = 64 * 1024;
    public const string PipePrefix = "Mizon.Mcp.Auth.";

    public static async Task<T?> ReadAsync<T>(Stream stream, JsonTypeInfo<T> typeInfo, CancellationToken token)
    {
        var header = new byte[4];
        var first = await stream.ReadAsync(header.AsMemory(0, 1), token).ConfigureAwait(false);
        if (first == 0) return default;
        await stream.ReadExactlyAsync(header.AsMemory(1), token).ConfigureAwait(false);
        var size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size is <= 0 or > MaximumMessageBytes) throw new InvalidDataException("Invalid MCP bridge frame size.");
        var bytes = new byte[size];
        try
        {
            await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
            return JsonSerializer.Deserialize(bytes, typeInfo) ?? throw new InvalidDataException("Empty MCP bridge message.");
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static async Task WriteAsync<T>(Stream stream, T value, JsonTypeInfo<T> typeInfo, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        try
        {
            if (bytes.Length > MaximumMessageBytes) throw new InvalidDataException("MCP bridge message is too large.");
            var header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
            await stream.WriteAsync(header, token).ConfigureAwait(false);
            await stream.WriteAsync(bytes, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
