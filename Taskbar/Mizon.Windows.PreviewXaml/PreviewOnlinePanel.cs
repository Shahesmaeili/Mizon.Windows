using System.Globalization;
using Mizon.Windows.Services;

namespace Mizon.Windows.PreviewXaml;

internal sealed unsafe class PreviewOnlinePanel(nint root, bool persian) : IDisposable
{
    private static readonly Guid ElementId = new("676D0BE9-B65C-41C6-BA40-58CF87F201C1");
    private static readonly Guid ProgressRingId = new("6DA5E49E-6E9D-425C-BD7C-02173E39763F");
    private static readonly Guid AutomationId = new("B618FD7B-32D0-4970-9C42-7C039AC7BE78");
    private readonly CultureInfo _culture = CultureInfo.GetCultureInfo(persian ? "fa-IR" : "en-US");
    private nint _global, _followers, _status, _onlineDot, _onlineGlow, _offlineDot, _ring, _ringActivity;
    private TaskbarOnlineSnapshot? _current;
    private PreviewCountMotion _onlineMotion, _followersMotion;
    private int? _shownGlobal, _shownFollowers;
    private long? _glowStarted;
    private double _shownGlow;

    internal void Attach()
    {
        _global = Find("GlobalCount", Native.TextBlock);
        _followers = Find("FollowerCount", Native.TextBlock);
        _status = Find("ConnectionState", Native.TextBlock);
        _onlineDot = Find("OnlineDot", ElementId);
        _onlineGlow = Find("OnlineGlow", ElementId);
        _offlineDot = Find("OfflineDot", ElementId);
        _ring = Find("ConnectionRing", ElementId);
        _ringActivity = Find("ConnectionRing", ProgressRingId);
    }

    internal bool Apply(TaskbarOnlineSnapshot snapshot, long now)
    {
        if (_current == snapshot) return false;
        _current = snapshot;
        var connected = snapshot.Phase == TaskbarOnlinePhase.Connected;
        var connecting = snapshot.Animate && snapshot.Phase is
            TaskbarOnlinePhase.Connecting or TaskbarOnlinePhase.Reconnecting;
        SetVisible(_onlineDot, connected);
        SetVisible(_offlineDot, !connected && !connecting);
        SetVisible(_ring, connecting);
        Native.Check(((delegate* unmanaged[Stdcall]<nint, byte, int>)Native.Slot(_ringActivity, 7))(
            _ringActivity, connecting ? (byte)1 : (byte)0));

        var state = snapshot.Phase switch
        {
            TaskbarOnlinePhase.Connecting => persian ? "در حال اتصال…" : "Connecting…",
            TaskbarOnlinePhase.Reconnecting => persian ? "اتصال دوباره…" : "Reconnecting…",
            TaskbarOnlinePhase.NoInternet => persian ? "بدون اینترنت" : "No internet",
            TaskbarOnlinePhase.Connected => persian ? "متصل" : "Connected",
            _ => persian ? "ارتباط قطع" : "Disconnected"
        };
        SetText(_status, state);
        _onlineMotion.Set(connected ? snapshot.OnlineCount : null, snapshot.Animate, now);
        _followersMotion.Set(connected ? snapshot.OnlineFollowerCount : null, snapshot.Animate, now);
        var animatingCounts = _onlineMotion.IsRunning(now) || _followersMotion.IsRunning(now);
        _glowStarted = animatingCounts ? now : null;
        Frame(now);
        SetAccessibleSummary(snapshot, state);
        return animatingCounts;
    }

    internal void Frame(long now)
    {
        var global = _onlineMotion.Read(now);
        var followers = _followersMotion.Read(now);
        if (global != _shownGlobal)
        {
            _shownGlobal = global;
            SetText(_global, Format(global));
        }
        if (followers != _shownFollowers)
        {
            _shownFollowers = followers;
            SetText(_followers, Format(followers));
        }
        var elapsed = _glowStarted is long started ? now - started : 600;
        var glow = elapsed is > 0 and < 600 ? Math.Sin(Math.PI * elapsed / 600) * 0.42 : 0;
        if (glow != _shownGlow)
        {
            // IUIElement::put_Opacity
            Native.PutDouble(_onlineGlow, 10, glow);
            _shownGlow = glow;
        }
    }

    internal void VerifyDisplayed(int? online, int? followers)
    {
        if (_shownGlobal != online || _shownFollowers != followers)
            throw new InvalidOperationException("The preview count did not reach its expected value.");
    }

    internal void VerifyIndicator(bool ring, bool glow)
    {
        byte active = 0;
        Native.Check(((delegate* unmanaged[Stdcall]<nint, byte*, int>)Native.Slot(_ringActivity, 6))(_ringActivity, &active));
        if ((active != 0) != ring || (Native.GetDouble(_onlineGlow, 9) > 0) != glow)
            throw new InvalidOperationException("The preview indicator did not match its connection or count state.");
    }

    private string Format(int? count) => count?.ToString("N0", _culture) ?? "—";

    private void SetAccessibleSummary(TaskbarOnlineSnapshot snapshot, string state)
    {
        var summary = persian
            ? $"{state}، {Format(snapshot.OnlineCount)} حساب آنلاین، {Format(snapshot.OnlineFollowerCount)} دنبال‌کننده آنلاین"
            : $"{state}, {Format(snapshot.OnlineCount)} accounts online, {Format(snapshot.OnlineFollowerCount)} followers online";
        var automation = Native.Factory("Windows.UI.Xaml.Automation.AutomationProperties", AutomationId);
        var dependency = Native.Query(root, Native.Dependency);
        var text = Native.HString(summary);
        try { Native.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)Native.Slot(automation, 23))(automation, dependency, text)); }
        finally { Native.WindowsDeleteString(text); Native.Release(dependency); Native.Release(automation); }
    }

    private nint Find(string name, Guid type)
    {
        var framework = Native.Query(root, Native.Framework);
        nint element = 0;
        try
        {
            element = PreviewRow.FindName(framework, name);
            var typed = Native.Query(element, type);
            return typed != 0 ? typed : throw new InvalidOperationException("Missing preview presence element.");
        }
        finally { Native.Release(element); Native.Release(framework); }
    }

    private static void SetVisible(nint element, bool visible) =>
        Native.Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Native.Slot(element, 22))(element, visible ? 0 : 1));

    private static void SetText(nint target, string value)
    {
        var text = Native.HString(value);
        try { Native.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Native.Slot(target, 27))(target, text)); }
        finally { Native.WindowsDeleteString(text); }
    }

    public void Dispose()
    {
        if (_ringActivity != 0)
            ((delegate* unmanaged[Stdcall]<nint, byte, int>)Native.Slot(_ringActivity, 7))(_ringActivity, 0);
        Native.Release(_global);
        Native.Release(_followers);
        Native.Release(_status);
        Native.Release(_onlineDot);
        Native.Release(_onlineGlow);
        Native.Release(_offlineDot);
        Native.Release(_ring);
        Native.Release(_ringActivity);
        _global = _followers = _status = _onlineDot = _onlineGlow = _offlineDot = _ring = _ringActivity = 0;
    }
}
