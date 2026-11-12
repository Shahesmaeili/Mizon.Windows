using Mizon.Windows.Mcp.Contracts;

namespace Mizon.Windows.Mcp.Tests;

internal static class PolicyTests
{
    private static readonly string[] PrivateOperations =
        ["get_state", "open_page", "set_form", "clear_form", "retry_welcome", "set_language", "auth_gamepad_action", "submit"];

    public static Task Pending()
    {
        var policy = new McpAccessPolicy();
        Check.That(policy.Status == "pending", "New connections must be pending.");
        Check.That(policy.CanExecute("connection_status"), "Own consent status should be readable before approval.");
        foreach (var operation in PrivateOperations)
            Check.That(!policy.CanExecute(operation), "Pending connections must not access private operations.");
        Check.That(!policy.CanExecute("get_appearance") && !policy.CanExecute("set_appearance"), "Pending connections cannot use appearance.");
        Check.That(!policy.CanExecute("get_navigation") && !policy.CanExecute("open_destination"), "Pending connections cannot navigate or read destinations.");
        Check.That(!policy.CanExecute("approve"), "There must not be a remote approval operation.");
        Check.That(!policy.CanExecute("connect"), "Connection handshake must not itself grant a scope.");
        return Task.CompletedTask;
    }

    public static Task Scopes()
    {
        for (var grants = 0; grants < 8; grants++)
        {
            var read = (grants & 1) != 0;
            var control = (grants & 2) != 0;
            var submit = (grants & 4) != 0;
            var policy = new McpAccessPolicy();
            policy.Approve(read, control, submit);
            Check.That(policy.Status == "approved", "Approved policy has the wrong status.");
            Check.That(policy.CanExecute("get_state") == read, "Read scope is incorrectly coupled to another scope.");
            foreach (var operation in PrivateOperations.Where(operation => operation is not ("get_state" or "submit")))
                Check.That(policy.CanExecute(operation) == control, "Control operation does not follow control scope.");
            Check.That(policy.CanExecute("submit") == (control && submit), "Submission must require both control and submit.");
            Check.That(!policy.CanExecute("execute_shell"), "Unknown operations must remain unavailable with all grants.");
            Check.That(!policy.CanExecute("get_appearance") && !policy.CanExecute("set_appearance"), "Authentication grants authorize a new feature domain.");
            Check.That(!policy.CanExecute("get_navigation") && !policy.CanExecute("open_destination"), "Authentication grants authorize navigation.");
        }
        for (var grants = 0; grants < 4; grants++)
        {
            var policy = new McpAccessPolicy();
            policy.Approve(false, false, false, (grants & 1) != 0, (grants & 2) != 0);
            Check.That(policy.CanExecute("get_appearance") == ((grants & 1) != 0)
                && policy.CanExecute("set_appearance") == ((grants & 2) != 0), "Appearance scopes are incorrectly coupled.");
            foreach (var operation in PrivateOperations) Check.That(!policy.CanExecute(operation), "Appearance grants authorize authentication.");
            Check.That(!policy.CanExecute("get_navigation") && !policy.CanExecute("open_destination"), "Appearance grants authorize navigation.");
            policy.Revoke();
            Check.That(!policy.AllowAppearanceRead && !policy.AllowAppearanceControl
                && !policy.CanExecute("get_appearance") && !policy.CanExecute("set_appearance"), "Revocation retained appearance grants.");
        }
        for (var grants = 0; grants < 4; grants++)
        {
            var policy = new McpAccessPolicy();
            policy.Approve(false, false, false, navigationRead: (grants & 1) != 0, navigationControl: (grants & 2) != 0);
            Check.That(policy.CanExecute("get_navigation") == ((grants & 1) != 0)
                && policy.CanExecute("open_destination") == ((grants & 2) != 0), "Navigation scopes are coupled.");
            foreach (var operation in PrivateOperations) Check.That(!policy.CanExecute(operation), "Navigation grants authorize authentication.");
            Check.That(!policy.CanExecute("set_appearance") && !policy.CanExecute("get_appearance"), "Navigation grants authorize appearance.");
            policy.Revoke();
            Check.That(!policy.AllowNavigationRead && !policy.AllowNavigationControl
                && !policy.CanExecute("get_navigation") && !policy.CanExecute("open_destination"), "Revocation retained navigation grants.");
        }
        return Task.CompletedTask;
    }

    public static Task Revocation()
    {
        foreach (var status in new[] { "denied", "revoked", "disconnected" })
        {
            var policy = new McpAccessPolicy();
            policy.Approve(true, true, true);
            if (status == "denied") policy.Deny();
            else if (status == "revoked") policy.Revoke();
            else policy.Disconnect();
            Check.That(policy.Status == status, "Consent reset has an incorrect lifecycle status.");
            Check.That(!policy.AllowRead && !policy.AllowControl && !policy.AllowSubmit, "A reset retained permissions.");
            foreach (var operation in PrivateOperations)
                Check.That(!policy.CanExecute(operation), "A reset retained access to a private operation.");
            Check.That(policy.CanExecute("connection_status"), "Reset connection should still describe its own status.");
            if (status == "disconnected")
            {
                policy.Approve(true, true, true);
                Check.That(policy.Status == "disconnected" && !policy.CanExecute("submit"), "A disconnected connection was revived.");
            }
        }
        return Task.CompletedTask;
    }
}
