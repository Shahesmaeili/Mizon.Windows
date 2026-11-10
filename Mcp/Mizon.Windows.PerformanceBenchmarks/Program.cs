using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services;

// Only synthetic values and production-linked pure code. No app activation,
// user consent, credentials, network, named pipe or Windows UI is accessed.
var output = args.Length == 2 && args[0] == "--output" ? args[1] : null;
if (args.Length != 0 && output is null)
    throw new ArgumentException("Usage: [--output <json-file>]");

string[] routes =
[
    "mizon://music/0195dd3c-9a3e-7c4a-8c6f-2e9c0f6b1a72",
    "mizon://music/0195dd3c-9a3e-7c4a-8c6f-2e9c0f6b1a72/comments/0195dd3c-9a3e-7c4a-8c6f-2e9c0f6b1a73",
    "mizon://search?query=%D9%85%D9%88%D8%B3%DB%8C%D9%82%DB%8C%20test",
    "mizon://settings/appearance"
];
var response = new McpResponse
{
    Success = true, Code = "ok", Message = "Synthetic benchmark state",
    State = new() { Page = "login", Language = "fa-IR", ControlsEnabled = true, CanSubmit = false },
    Appearance = new()
    {
        Theme = McpAppearanceTheme.System, EffectiveTheme = McpAppearanceTheme.Dark,
        Backdrop = McpAppearanceBackdrop.Acrylic, EffectiveBackdrop = McpAppearanceBackdrop.Acrylic,
        IsTransparencyEnabled = true, IsAcrylicSupported = true, IsPreferenceSaved = true
    },
    Navigation = new() { Destination = "login", Uri = "mizon://login" },
    TaskbarPin = new() { IsSupported = true, IsPinningAllowed = true, CanRequest = true, Code = "ready" }
};
using var frame = new MemoryStream(4096);
long sink = 0;
void ParseRoutes()
{
    foreach (var value in routes)
    {
        if (!AppActivationRequest.TryParse(value, out var request)) throw new InvalidDataException("Route rejected.");
        sink += request.ToUri()!.Length;
    }
}
void RoundTripFrame()
{
    frame.SetLength(0);
    McpPipeProtocol.WriteAsync(frame, response, McpJsonContext.Default.McpResponse, CancellationToken.None).GetAwaiter().GetResult();
    frame.Position = 0;
    var decoded = McpPipeProtocol.ReadAsync(frame, McpJsonContext.Default.McpResponse, CancellationToken.None).GetAwaiter().GetResult();
    if (decoded is not { Success: true, State.Page: "login" } || frame.Position != frame.Length)
        throw new InvalidDataException("MCP framing round-trip failed.");
    sink += decoded.Code.Length;
}

const int samples = 15;
var definitions = new (string Name, Action Run, int Iterations, int OperationsPerIteration)[]
{
    ("windows.deeplink.parse_roundtrip", ParseRoutes, 25_000, routes.Length),
    ("windows.mcp.json_frame_roundtrip", RoundTripFrame, 10_000, 1)
};
var results = new List<(string Name, long Operations, double[] Ns, double[] Bytes)>();
foreach (var definition in definitions)
{
    // Warm tiered compilation and JSON metadata before steady-state measurement.
    for (var warmup = 0; warmup < 5; warmup++)
        for (var iteration = 0; iteration < definition.Iterations; iteration++) definition.Run();
    var operations = (long)definition.Iterations * definition.OperationsPerIteration;
    var elapsedSamples = new double[samples];
    var allocationSamples = new double[samples];
    for (var sample = 0; sample < samples; sample++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var started = Stopwatch.GetTimestamp();
        for (var iteration = 0; iteration < definition.Iterations; iteration++) definition.Run();
        elapsedSamples[sample] = Stopwatch.GetElapsedTime(started).TotalNanoseconds / operations;
        allocationSamples[sample] = (GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore) / (double)operations;
    }
    results.Add((definition.Name, operations, elapsedSamples, allocationSamples));
}

using var json = new MemoryStream();
using (var writer = new Utf8JsonWriter(json, new() { Indented = true }))
{
    writer.WriteStartObject();
    writer.WriteString("runtime", RuntimeInformation.FrameworkDescription);
    writer.WriteString("architecture", RuntimeInformation.ProcessArchitecture.ToString());
    writer.WriteString("os", RuntimeInformation.OSDescription);
    writer.WriteBoolean("serverGc", System.Runtime.GCSettings.IsServerGC);
    writer.WriteString("scope", "Headless production route parsing and in-memory MCP framing; excludes UI, network and pipe IPC.");
    writer.WriteNumber("samples", samples);
    writer.WriteNumber("checksum", sink);
    writer.WriteStartArray("results");
    foreach (var result in results)
    {
        writer.WriteStartObject();
        writer.WriteString("name", result.Name);
        writer.WriteNumber("operationsPerSample", result.Operations);
        writer.WriteNumber("medianNanosecondsPerOperation", Median(result.Ns));
        writer.WriteNumber("medianAllocatedBytesPerOperation", Median(result.Bytes));
        writer.WriteStartArray("nanosecondsPerOperation");
        foreach (var value in result.Ns) writer.WriteNumberValue(value);
        writer.WriteEndArray();
        writer.WriteStartArray("allocatedBytesPerOperation");
        foreach (var value in result.Bytes) writer.WriteNumberValue(value);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
    writer.WriteEndArray();
    writer.WriteEndObject();
}
var text = System.Text.Encoding.UTF8.GetString(json.ToArray());
if (output is not null) File.WriteAllText(output, text);
Console.WriteLine(text);

static double Median(double[] values)
{
    var sorted = (double[])values.Clone();
    Array.Sort(sorted);
    return sorted[sorted.Length / 2];
}
