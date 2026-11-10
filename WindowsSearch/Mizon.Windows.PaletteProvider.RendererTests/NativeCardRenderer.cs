using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Mizon.Windows.PaletteProvider;

internal sealed unsafe class NativeCardRenderer : IDisposable
{
    private readonly List<nint> _modules = [];
    private nint _cardFactory, _cardStatics, _rendererFactory, _renderer;

    internal NativeCardRenderer()
    {
        try
        {
            _cardFactory = Factory("AdaptiveCards.ObjectModel.WinUI3.dll", "AdaptiveCards.ObjectModel.WinUI3.AdaptiveCard");
            _cardStatics = NativeMethods.As(_cardFactory, new("1b938380-0e82-5044-be9b-95ea6f86cadb"));
            _rendererFactory = Factory("AdaptiveCards.Rendering.WinUI3.dll", "AdaptiveCards.Rendering.WinUI3.AdaptiveCardRenderer");
            nint renderer = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(_rendererFactory)[6])(_rendererFactory, &renderer));
            _renderer = renderer;
        }
        catch { Dispose(); throw; }
    }

    private nint Factory(string dll, string runtimeClass)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SystemApps", "MicrosoftWindows.Client.CBS_cw5n1h2txyewy", "RunUX", dll);
        if (!File.Exists(path)) throw new PlatformNotSupportedException($"Required Windows Search component is absent: {dll}");
        var module = NativeLibrary.Load(path);
        _modules.Add(module);
        var export = (delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeLibrary.GetExport(module, "DllGetActivationFactory");
        nint name = NativeMethods.String(runtimeClass), factory = 0;
        try { NativeMethods.Check(export(name, &factory)); return factory; }
        finally { NativeMethods.WindowsDeleteString(name); }
    }

    // Test-only host configuration. Search may use no automatic card padding,
    // unlike the standalone renderer's default 20px. Exercise that environment
    // without changing Windows settings or adding a background to the card.
    // Slots follow Microsoft's AdaptiveCards.Rendering.WinUI3.idl:
    // IAdaptiveCardRenderer.HostConfig get=7, IAdaptiveHostConfig.Spacing get=24,
    // IAdaptiveSpacingConfig.Padding get=16/set=17. Runtime class names and the
    // written value are checked before this renderer is used by a layout test.
    internal void SetHostPadding(uint padding)
    {
        ObjectDisposedException.ThrowIf(_renderer == 0, this);
        nint host = 0, spacing = 0;
        try
        {
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(_renderer)[7])(_renderer, &host));
            RequireClass(host, "AdaptiveCards.Rendering.WinUI3.AdaptiveHostConfig");
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(host)[24])(host, &spacing));
            RequireClass(spacing, "AdaptiveCards.Rendering.WinUI3.AdaptiveSpacingConfig");
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint, int>)NativeMethods.Table(spacing)[17])(spacing, padding));
            uint actual = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)NativeMethods.Table(spacing)[16])(spacing, &actual));
            if (actual != padding) throw new InvalidOperationException("Native renderer did not retain the requested host padding.");
        }
        finally { NativeMethods.Release(spacing); NativeMethods.Release(host); }
    }

    private static void RequireClass(nint value, string expected)
    {
        if (value == 0) throw new InvalidOperationException($"Native renderer returned no {expected}.");
        nint name = 0;
        try
        {
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(value)[4])(value, &name));
            if (!string.Equals(NativeMethods.ReadString(name), expected, StringComparison.Ordinal))
                throw new PlatformNotSupportedException($"Unexpected native renderer contract; expected {expected}.");
        }
        finally { NativeMethods.WindowsDeleteString(name); }
    }

    internal RenderedCard Render(string json)
    {
        nint text = NativeMethods.String(json), parsed = 0, card = 0, rendered = 0, element = 0;
        try
        {
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(_cardStatics)[8])(_cardStatics, text, &parsed));
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(parsed)[6])(parsed, &card));
            if (card == 0) throw new InvalidDataException("Native Adaptive Cards parser did not produce a card: " + string.Join("; ", Messages(parsed, 7)));
            // Verified against the installed projection and official WinUI3 IDL.
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(_renderer)[23])(_renderer, card, &rendered));
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(rendered)[8])(rendered, &element));
            if (element == 0) throw new InvalidDataException("The native renderer produced no FrameworkElement.");
            var errors = Messages(rendered, 10);
            var result = new RenderedCard(rendered, WinRT.MarshalInspectable<FrameworkElement>.FromAbi(element), errors);
            rendered = 0;
            return result;
        }
        finally
        {
            NativeMethods.Release(element); NativeMethods.Release(rendered); NativeMethods.Release(card); NativeMethods.Release(parsed);
            NativeMethods.WindowsDeleteString(text);
        }
    }

    private static string[] Messages(nint result, int slot)
    {
        nint vector = 0;
        NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(result)[slot])(result, &vector));
        try
        {
            uint count = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)NativeMethods.Table(vector)[7])(vector, &count));
            var values = new string[count];
            for (uint i = 0; i < count; i++)
            {
                nint item = 0, message = 0;
                try
                {
                    NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)NativeMethods.Table(vector)[6])(vector, i, &item));
                    NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(item)[8])(item, &message));
                    values[i] = NativeMethods.ReadString(message);
                }
                finally { NativeMethods.WindowsDeleteString(message); NativeMethods.Release(item); }
            }
            return values;
        }
        finally { NativeMethods.Release(vector); }
    }

    public void Dispose()
    {
        NativeMethods.Release(Interlocked.Exchange(ref _renderer, 0));
        NativeMethods.Release(Interlocked.Exchange(ref _rendererFactory, 0));
        NativeMethods.Release(Interlocked.Exchange(ref _cardStatics, 0));
        NativeMethods.Release(Interlocked.Exchange(ref _cardFactory, 0));
        // Native render elements can retain vendor activation caches beyond the
        // wrapper lifetime. Unload only when that DLL declares itself idle.
        for (var i = _modules.Count - 1; i >= 0; i--)
        {
            var canUnload = (delegate* unmanaged[Stdcall]<int>)NativeLibrary.GetExport(_modules[i], "DllCanUnloadNow");
            if (canUnload() == 0) NativeLibrary.Free(_modules[i]);
        }
        _modules.Clear();
    }

    internal sealed class RenderedCard(nint pointer, FrameworkElement element, IReadOnlyList<string> errors) : IDisposable
    {
        private nint _pointer = pointer;
        internal FrameworkElement Element { get; } = element;
        internal IReadOnlyList<string> Errors { get; } = errors;
        public void Dispose() => NativeMethods.Release(Interlocked.Exchange(ref _pointer, 0));
    }
}
