namespace Mizon.Windows.Mcp.Contracts;

// Transport-independent policy, shared by the app and protocol tests.
// Effective live-session permissions; remembered grants are restored by the
// app-owned consent service only after verifying the client's private proof.
public sealed class McpAccessPolicy
{
    public string Status { get; private set; } = "pending";
    public bool AllowRead { get; private set; }
    public bool AllowControl { get; private set; }
    public bool AllowSubmit { get; private set; }
    public bool AllowAppearanceRead { get; private set; }
    public bool AllowAppearanceControl { get; private set; }
    public bool AllowNavigationRead { get; private set; }
    public bool AllowNavigationControl { get; private set; }
    public bool AllowTaskbarRead { get; private set; }
    public bool AllowTaskbarControl { get; private set; }
    public bool AllowMusicHomeRead { get; private set; }
    public bool AllowMusicHomeControl { get; private set; }
    public bool AllowMusicHomeSubmit { get; private set; }
    public bool AllowPresenceRead { get; private set; }
    public bool AllowWindowRead { get; private set; }
    public bool AllowWindowControl { get; private set; }

    public bool AllowVpnRead { get; private set; }
    public bool AllowVpnControl { get; private set; }

    public void Approve(bool read, bool control, bool submit, bool appearanceRead = false, bool appearanceControl = false,
        bool navigationRead = false, bool navigationControl = false, bool taskbarRead = false, bool taskbarControl = false,
        bool musicHomeRead = false, bool musicHomeControl = false, bool musicHomeSubmit = false, bool presenceRead = false,
        bool windowRead = false, bool windowControl = false, bool vpnRead = false, bool vpnControl = false)
    {
        if (Status == "disconnected") return;
        AllowRead = read;
        AllowControl = control;
        AllowSubmit = control && submit;
        AllowAppearanceRead = appearanceRead;
        AllowAppearanceControl = appearanceControl;
        AllowNavigationRead = navigationRead;
        AllowNavigationControl = navigationControl;
        AllowTaskbarRead = taskbarRead;
        AllowTaskbarControl = taskbarControl;
        AllowMusicHomeRead = musicHomeRead;
        AllowMusicHomeControl = musicHomeControl;
        AllowMusicHomeSubmit = musicHomeControl && musicHomeSubmit;
        AllowPresenceRead = presenceRead;
        AllowWindowRead = windowRead;
        AllowWindowControl = windowControl;
        AllowVpnRead = vpnRead;
        AllowVpnControl = vpnControl;
        Status = "approved";
    }

    public void Deny() => Reset("denied");
    public void Revoke() => Reset("revoked");
    public void Disconnect() => Reset("disconnected");

    public bool CanExecute(string operation) => operation switch
    {
        "connection_status" => true,
        "get_state" => Status == "approved" && AllowRead,
        "open_page" or "set_form" or "clear_form" or "retry_welcome" or "set_language"
            => Status == "approved" && AllowControl,
        "submit" => Status == "approved" && AllowControl && AllowSubmit,
        "get_appearance" => Status == "approved" && AllowAppearanceRead,
        "set_appearance" => Status == "approved" && AllowAppearanceControl,
        "get_navigation" => Status == "approved" && AllowNavigationRead,
        "open_destination" => Status == "approved" && AllowNavigationControl,
        "get_taskbar_pin_state" => Status == "approved" && AllowTaskbarRead,
        "request_taskbar_pin" => Status == "approved" && AllowTaskbarControl,
        "get_music_home" => Status == "approved" && AllowMusicHomeRead,
        "get_presence_state" => Status == "approved" && AllowPresenceRead,
        "get_vpn_state" => Status == "approved" && AllowVpnRead,
        "set_vpn_particles" or "open_vpn_settings" => Status == "approved" && AllowVpnControl,
        "get_window_state" => Status == "approved" && AllowWindowRead,
        "set_fullscreen" => Status == "approved" && AllowWindowControl,
        "refresh_music_home" or "music_home_action" => Status == "approved" && AllowMusicHomeControl,
        "music_home_submit" => Status == "approved" && AllowMusicHomeControl && AllowMusicHomeSubmit,
        _ => false
    };

    private void Reset(string status)
    {
        if (Status == "disconnected") return;
        Status = status;
        AllowRead = AllowControl = AllowSubmit = false;
        AllowAppearanceRead = AllowAppearanceControl = false;
        AllowNavigationRead = AllowNavigationControl = false;
        AllowTaskbarRead = AllowTaskbarControl = false;
        AllowMusicHomeRead = AllowMusicHomeControl = AllowMusicHomeSubmit = false;
        AllowPresenceRead = false;
        AllowWindowRead = AllowWindowControl = false;
        AllowVpnRead = AllowVpnControl = false;
    }
}
