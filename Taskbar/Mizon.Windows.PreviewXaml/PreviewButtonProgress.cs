namespace Mizon.Windows.PreviewXaml;

// All projected XAML references stay on the shell element's owning dispatcher.
internal sealed unsafe class PreviewButtonProgress : IDisposable
{
    private static readonly Guid ElementId = new("676D0BE9-B65C-41C6-BA40-58CF87F201C1");
    private static readonly Guid ProgressRingId = new("6DA5E49E-6E9D-425C-BD7C-02173E39763F");
    private static readonly Guid AutomationId = new("B618FD7B-32D0-4970-9C42-7C039AC7BE78");
    private readonly string _waiting;
    private nint _button, _icon, _ring, _progress;
    private bool _shown;

    internal PreviewButtonProgress(nint button, nint icon, nint ring, bool persian)
    {
        _waiting = persian ? "در حال باز کردن…" : "Opening…";
        _button = Native.Query(button, Native.Dependency);
        _icon = Native.Query(icon, ElementId);
        _ring = Native.Query(ring, ElementId);
        _progress = Native.Query(ring, ProgressRingId);
        if (_button != 0 && _icon != 0 && _ring != 0 && _progress != 0) return;
        Dispose();
        throw new InvalidOperationException("The preview button is missing its progress elements.");
    }

    internal void Apply(bool waiting)
    {
        if (_button == 0 || _shown == waiting) return;
        // Windows SDK IUIElement::put_Visibility and IProgressRing::put_IsActive.
        SetVisible(_icon, !waiting);
        SetVisible(_ring, waiting);
        Native.Check(((delegate* unmanaged[Stdcall]<nint, byte, int>)Native.Slot(_progress, 7))(_progress, waiting ? (byte)1 : (byte)0));
        _shown = waiting;

        var properties = Native.Factory("Windows.UI.Xaml.Automation.AutomationProperties", AutomationId);
        var status = Native.HString(waiting ? _waiting : "");
        try
        {
            // IAutomationPropertiesStatics::SetItemStatus preserves the button's actual Name and focus.
            Native.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)Native.Slot(properties, 23))(properties, _button, status));
        }
        finally { Native.WindowsDeleteString(status); Native.Release(properties); }
    }

    private static void SetVisible(nint element, bool visible) =>
        Native.Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Native.Slot(element, 22))(element, visible ? 0 : 1));

    private static bool Visible(nint element)
    {
        int value;
        Native.Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)Native.Slot(element, 21))(element, &value));
        return value == 0;
    }

    internal void Verify(bool waiting)
    {
        byte active;
        Native.Check(((delegate* unmanaged[Stdcall]<nint, byte*, int>)Native.Slot(_progress, 6))(_progress, &active));
        if ((active != 0) != waiting || Visible(_icon) == waiting || Visible(_ring) != waiting)
            throw new InvalidOperationException("The preview button progress state did not match its request.");
    }

    public void Dispose()
    {
        try { Apply(false); }
        finally
        {
            Native.Release(_button);
            Native.Release(_icon);
            Native.Release(_ring);
            Native.Release(_progress);
            _button = _icon = _ring = _progress = 0;
        }
    }
}
