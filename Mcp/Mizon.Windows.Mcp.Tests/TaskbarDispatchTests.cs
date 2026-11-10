using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Mizon.Windows.Mcp.Tests;

internal static partial class ConnectionLifecycleTests
{
    public static async Task TaskbarTypedDispatch()
    {
        await using var app = new SyntheticApp(TimeSpan.Zero);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var client = await CreateFixtureClientAsync(timeout.Token);
        foreach (var (name, operation) in new[]
        {
            ("mizon_get_taskbar_pin_state", "get_taskbar_pin_state"), ("mizon_request_taskbar_pin", "request_taskbar_pin")
        })
        {
            var result = await client.CallToolAsync(new CallToolRequestParams { Name = name }, cancellationToken: timeout.Token);
            Check.That(result.IsError == true && result.StructuredContent?.GetProperty("code").GetString() == "approval_required",
                "Pending taskbar operation bypassed approval.");
            Check.That(app.LastRequest?.Operation == operation, "Taskbar operation was not preserved across actual SDK/pipe.");
            var previous = app.LastRequest;
            using var json = JsonDocument.Parse("{\"approve\":true,\"appId\":\"other-app\",\"unpin\":true}");
            var invalid = await client.CallToolAsync(new CallToolRequestParams
            {
                Name = name, Arguments = json.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())
            }, cancellationToken: timeout.Token);
            Check.That(invalid.StructuredContent?.GetProperty("code").GetString() == "invalid_arguments"
                && ReferenceEquals(previous, app.LastRequest), "Taskbar tool accepted undeclared app, approval or unpin parameters.");
        }
        Check.That(app.Connections == 1, "Taskbar calls unnecessarily recreated consent sessions.");
    }
}
