using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Mizon.Windows.Mcp.Contracts;

namespace Mizon.Windows.Mcp.Tests;

internal static class ProtocolTests
{
    public static Task ResponseContract()
    {
        var response = new McpResponse
        {
            Success = true, Code = "state", Message = "Synthetic test",
            State = new() { HasSession = true, HasPassword = true, HasConfirmPassword = true }
        };
        var json = JsonSerializer.SerializeToElement(response, McpJsonContext.Default.McpResponse);
        AssertNoSecrets(json);
        var state = json.GetProperty("state");
        Check.That(state.GetProperty("hasPassword").GetBoolean(), "Password-presence flag was omitted.");
        Check.That(state.GetProperty("hasConfirmPassword").GetBoolean(), "Confirmation-presence flag was omitted.");
        // Inspect source-generated metadata as well: a null secret field could otherwise hide in JSON.
        foreach (var metadata in new System.Text.Json.Serialization.Metadata.JsonTypeInfo[] { McpJsonContext.Default.McpResponse, McpJsonContext.Default.McpAuthState, McpJsonContext.Default.McpConnectionStatus })
            foreach (var property in metadata.Properties)
                Check.That(!IsSecret(property.Name), "Response metadata includes a secret field.");
        var appearance = JsonSerializer.SerializeToElement(new McpResponse
        {
            Success = true, Code = "appearance_state", Message = "Synthetic appearance",
            Appearance = new() { Theme = McpAppearanceTheme.System, EffectiveTheme = McpAppearanceTheme.Dark,
                Backdrop = McpAppearanceBackdrop.AcrylicThin, FallbackReason = McpAppearanceFallback.TransparencyDisabled }
        }, McpJsonContext.Default.McpResponse);
        SchemaAssertions.Conforms(appearance, Mizon.Windows.Mcp.MizonAuthSchemas.Output);
        Check.That(appearance.GetProperty("appearance").GetProperty("backdrop").GetString() == "acrylicThin",
            "Source-generated appearance enum casing differs from discovery.");
        var legacy = JsonSerializer.Deserialize("{\"operation\":\"get_state\"}", McpJsonContext.Default.McpRequest);
        Check.That(legacy is { Operation: "get_state", Appearance: null }, "Additive appearance contracts changed old auth request behavior.");
        var navigation = JsonSerializer.SerializeToElement(new McpResponse
        {
            Success = true, Code = "navigation_state", Navigation = new()
            { Destination = "music", Uri = "mizon://music", HasSession = true, CanGoBack = true }
        }, McpJsonContext.Default.McpResponse);
        SchemaAssertions.Conforms(navigation, Mizon.Windows.Mcp.MizonAuthSchemas.Output);
        AssertNoSecrets(navigation);
        Check.That(legacy?.Navigation is null, "Existing authentication request gained a navigation side effect.");
        return Task.CompletedTask;
    }

    public static async Task RoundTrip()
    {
        using var buffer = new MemoryStream();
        var first = new McpRequest { Operation = "set_form", Page = "login", Form = new() { Identifier = "کاربر", Password = "synthetic-credential" } };
        var second = new McpRequest { Operation = "connection_status" };
        await McpPipeProtocol.WriteAsync(buffer, first, McpJsonContext.Default.McpRequest, default);
        await McpPipeProtocol.WriteAsync(buffer, second, McpJsonContext.Default.McpRequest, default);
        buffer.Position = 0;
        using var fragmented = new FragmentedStream(buffer);
        var actualFirst = await McpPipeProtocol.ReadAsync(fragmented, McpJsonContext.Default.McpRequest, default);
        var actualSecond = await McpPipeProtocol.ReadAsync(fragmented, McpJsonContext.Default.McpRequest, default);
        Check.That(actualFirst == first && actualSecond == second, "Fragmented or consecutive framing corrupted a message.");
        Check.That(buffer.Position == buffer.Length, "Framing did not consume exactly both messages.");
    }

    public static async Task EndOfStream()
    {
        using var empty = new MemoryStream();
        Check.That(await McpPipeProtocol.ReadAsync(empty, McpJsonContext.Default.McpRequest, default) is null, "Clean EOF should close a connection normally.");
        using var partialHeader = new MemoryStream([1, 0]);
        await Check.Throws<EndOfStreamException>(async () => await McpPipeProtocol.ReadAsync(partialHeader, McpJsonContext.Default.McpRequest, default));
        using var partialBody = Frame(20, [123, 125]);
        await Check.Throws<EndOfStreamException>(async () => await McpPipeProtocol.ReadAsync(partialBody, McpJsonContext.Default.McpRequest, default));
    }

    public static async Task InvalidFrames()
    {
        foreach (var size in new[] { 0, -1, McpPipeProtocol.MaximumMessageBytes + 1, int.MaxValue })
        {
            using var stream = Frame(size, []);
            await Check.Throws<InvalidDataException>(async () => await McpPipeProtocol.ReadAsync(stream, McpJsonContext.Default.McpRequest, default));
        }
        var invalidJson = Encoding.UTF8.GetBytes("not-json");
        using var invalid = Frame(invalidJson.Length, invalidJson);
        await Check.Throws<JsonException>(async () => await McpPipeProtocol.ReadAsync(invalid, McpJsonContext.Default.McpRequest, default));
        using var nullPayload = Frame(4, "null"u8.ToArray());
        await Check.Throws<InvalidDataException>(async () => await McpPipeProtocol.ReadAsync(nullPayload, McpJsonContext.Default.McpRequest, default));
    }

    public static async Task OversizeWrite()
    {
        using var stream = new MemoryStream();
        var request = new McpRequest { Operation = new string('x', McpPipeProtocol.MaximumMessageBytes) };
        await Check.Throws<InvalidDataException>(() => McpPipeProtocol.WriteAsync(stream, request, McpJsonContext.Default.McpRequest, default));
        Check.That(stream.Length == 0, "An oversize message wrote a partial header.");
    }

    public static async Task Cancellation()
    {
        using var stream = new BlockingStream();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Check.Throws<OperationCanceledException>(async () => await McpPipeProtocol.ReadAsync(stream, McpJsonContext.Default.McpRequest, timeout.Token));
    }

    public static void AssertNoSecrets(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                Check.That(!IsSecret(property.Name), "Response contains a secret property.");
                AssertNoSecrets(property.Value);
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) AssertNoSecrets(child);
    }

    private static bool IsSecret(string name) => name.Equals("password", StringComparison.OrdinalIgnoreCase)
        || name.Equals("confirmPassword", StringComparison.OrdinalIgnoreCase)
        || name.Equals("token", StringComparison.OrdinalIgnoreCase)
        || name.Equals("accessToken", StringComparison.OrdinalIgnoreCase)
        || name.Equals("refreshToken", StringComparison.OrdinalIgnoreCase)
        || name.Equals("clientCredential", StringComparison.OrdinalIgnoreCase)
        || name.Equals("credential", StringComparison.OrdinalIgnoreCase)
        || name.Equals("cookies", StringComparison.OrdinalIgnoreCase);

    private static MemoryStream Frame(int size, byte[] body)
    {
        var bytes = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, size);
        body.CopyTo(bytes, 4);
        return new MemoryStream(bytes);
    }

    private sealed class FragmentedStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(count, 1));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class BlockingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
