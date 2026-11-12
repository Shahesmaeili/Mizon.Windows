using Mizon.Windows.Mcp.Contracts;

internal static partial class Program
{
    private static async Task AuthGamepadScopesAndLifetime()
    {
        var path = Path.Combine(StorageRoot, "auth-gamepad", "grants.dat");
        var proof = Proof();
        var action = new McpRequest { Operation = "auth_gamepad_action", GamepadAction = McpAuthGamepadAction.Next };
        await using (var host = await Host.StartAsync(path))
        {
            await using var peer = await host.ConnectAsync(proof);
            var id = peer.Hello.Connection!.Id;
            var focus = "login-identifier";
            var calls = 0;
            host.Auth.Handler = (request, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (request.Operation == "auth_gamepad_action") { calls++; focus = "login-password"; }
                return Task.FromResult(new McpResponse { Success = true, Code = "state", State = new() { Page = "login",
                    Gamepad = new() { IsNavigationActive = true, IsControllerConnected = true, FocusedFieldId = focus } } });
            };
            Assert((await peer.Call(action)).Code == "approval_required" && calls == 0, "Pending controller action cannot reach the page");
            await host.Ui(() => host.Service.Deny(host.Item(id)));
            Assert((await peer.Call(action)).Code == "permission_denied" && calls == 0, "Denied controller action cannot reach the page");
            await host.Approve(id, read: true, control: false, submit: false);
            Assert((await peer.Call("get_state")).State?.Gamepad?.FocusedFieldId == "login-identifier",
                "Authentication read returns bounded controller state through the production dispatcher");
            Assert(!(await peer.Call(action)).Success && calls == 0, "Authentication read cannot move controller focus");
            await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
            await host.Approve(id, read: false, control: true, submit: false);
            Assert((await peer.Call(action)) is { Success: true, State: null } && calls == 1,
                "Control-only controller action reaches the shared auth adapter and redacts every auth state field");
            Assert(!(await peer.Call("submit")).Success && calls == 1, "Controller control grant does not authorize form submission");
            await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
            await host.Approve(id, read: true, control: true, submit: false);
            Assert((await peer.Call(action)).State?.Gamepad?.FocusedFieldId == "login-password",
                "Authorized controller action reports the resulting focus through source-generated pipe transport");

            host.Auth.Handler = (_, _) => throw new InvalidOperationException("synthetic private controller error");
            var failed = await peer.Call(action);
            Assert(failed is { Code: "operation_failed", State: null } && !failed.Message.Contains("private"),
                "Controller failures are sanitized without leaking state");

            var started = Signal(); var release = Signal(); var cancelled = Signal();
            host.Auth.Handler = async (_, token) =>
            {
                using var registration = token.Register(() => cancelled.TrySetResult());
                started.TrySetResult();
                await release.Task; // Verify stale result suppression even when a page ignores cancellation.
                return new() { Success = true, State = new() { Gamepad = new() { FocusedFieldId = "stale-focus" } } };
            };
            var pending = peer.Call(action);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await host.Ui(() => host.Service.RevokeAsync(host.Item(id)));
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await host.Approve(id, read: true, control: false, submit: false);
            release.TrySetResult();
            Assert((await pending) is { Code: "cancelled", State: null }, "Revoked controller generation cannot return stale focus after reapproval");
            Assert(!(await peer.Call(action)).Success, "Reapproval without control keeps controller actions denied");
            Assert(await host.Ui(() => !host.Item(id).IsBusy), "Cancelled controller action releases busy and mutation ownership");
        }
        await using (var restored = await Host.StartAsync(path))
        {
            await using var peer = await restored.ConnectAsync(proof);
            Assert(peer.Hello.Connection is { AllowRead: true, AllowControl: false, AllowSubmit: false }
                && !(await peer.Call(action)).Success, "Restart restores exact auth grants without adding controller control or submit");
        }
    }
}
