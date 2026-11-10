using System.Text.Json;

namespace Mizon.Windows.PaletteProvider;

// These contracts belong to the installed Windows 26H2 Palettes preview.
// Keep the ABI here isolated: it is not a public Windows SDK contract.
internal static unsafe class PaletteData
{
    private static readonly Guid ItemFactoryId = new("72784abe-da8a-5f48-895c-d8e31d03ae40");
    private static readonly Guid ItemId = new("095b1f54-5206-5f2b-acc5-ae72be801e11");
    private static readonly Guid ItemViewId = new("49d58c99-5bbf-5745-a693-67c16715f0c0");
    private static readonly Guid QueryId = new("a3d9ee47-0151-5a33-964a-d2d4c91e97ea");
    private static readonly Guid QueryResultsFactoryId = new("3c262865-a9e4-5c17-b303-ecc310f5eb71");
    private static readonly Guid QueryResultsId = new("587b3bed-2c37-53af-b479-c2986bca59dc");
    private static readonly Guid UriFactoryId = new("44a9796f-723e-4fdf-a218-033e75b0c084");
    private static readonly Guid UriInvokerFactoryId = new("8f2d6f1b-4b0d-5d77-a465-6fd96731709d");
    private static readonly Guid InvokerId = new("4dce3641-d67a-5402-82e6-f77be462c942");
    private static readonly Guid PropertyValueFactoryId = new("629bdbc8-d932-4ff4-96b9-8d96c5c1e858");
    private static readonly Guid KindsFactoryId = new("be554067-0c61-5273-b66e-dc142f6e01a9");
    private static readonly Guid IconDataStaticsId = new("bde0245b-9389-565c-8bf2-0949a7be7cd4");

    internal static PaletteResultItem BuildResult(nint suppliedItemFactory, PaletteResultState state)
    {
        var data = state.Result;
        var subtitle = string.Join(" · ", new[] { data.RowSubtitle, data.TypeLabel, "Mizon" }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var uri = data.Kind == PaletteResultKind.Music ? $"mizon://music/{data.PublicId:D}?action=play" : $"mizon://client/{data.PublicId:D}";
        var item = CreateMutableItem(suppliedItemFactory, data.Key, data.RowTitle, subtitle, uri);
        IDisposable? presentation = null, preview = null;
        nint view = 0;
        try
        {
            PaletteMenuData.Attach(item, state);
            presentation = PalettePresentation.AttachToItem(item, state);
            preview = PalettePreviewData.Attach(item, state);
            view = GetView(item);
            return new PaletteResultItem(view, state, presentation, preview);
        }
        catch
        {
            state.Dispose();
            preview?.Dispose();
            presentation?.Dispose();
            NativeMethods.Release(view);
            throw;
        }
        finally { NativeMethods.Release(item); }
    }

    internal static nint GetView(nint item)
    {
        nint view = 0;
        NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(item)[19])(item, &view));
        return view;
    }

    internal static nint BuildStatusItem(nint factory, string title, string message)
    {
        var item = CreateMutableItem(factory, "mizon-status", title, message, "mizon://login");
        try { return GetView(item); }
        finally { NativeMethods.Release(item); }
    }

    internal static void UpdateSummary(nint item, PaletteResultData data)
    {
        SetString(item,13,data.RowTitle);
        // The first rich-subtitle element is the native accent action label.
        // Keep the remaining description separate from that emphasized label.
        SetSubtitle(item,
            data.Kind == PaletteResultKind.Demo ? "امتحان قابلیت‌ها" : data.Kind == PaletteResultKind.Music ? "پخش آهنگ" : "مشاهدهٔ پروفایل",
            string.Join(" · ",new[] { data.RowSubtitle,data.TypeLabel,"Mizon" }.Where(value=>!string.IsNullOrWhiteSpace(value))));
    }

    internal static nint CreateMutableItem(nint suppliedItemFactory, string key, string title,
        string subtitle, string launchUri)
    {
        nint factory = 0, instance = 0, item = 0, invoker = 0, kinds = 0, icon = 0;
        try
        {
            factory = NativeMethods.As(suppliedItemFactory, ItemFactoryId);
            kinds = CreateKinds();
            var id = NativeMethods.String(key);
            try
            {
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint, nint*, int>)NativeMethods.Table(factory)[8])(factory, id, kinds, &instance));
            }
            finally { NativeMethods.WindowsDeleteString(id); }

            item = NativeMethods.As(instance, ItemId);
            SetString(item, 13, title);
            SetSubtitle(item, subtitle);
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Public", "Music.png");
            if (File.Exists(iconPath))
            {
                icon = CreateIcon(new Uri(iconPath).AbsoluteUri);
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)NativeMethods.Table(item)[9])(item, icon));
            }
            invoker = CreateUriInvoker(launchUri);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)NativeMethods.Table(item)[24])(item, invoker));

            var result = item;
            item = 0;
            return result;
        }
        finally
        {
            NativeMethods.Release(invoker);
            NativeMethods.Release(icon);
            NativeMethods.Release(kinds);
            NativeMethods.Release(item);
            NativeMethods.Release(instance);
            NativeMethods.Release(factory);
        }
    }

    private static nint CreateKinds()
    {
        nint factory = 0, text = 0;
        try
        {
            factory = NativeMethods.Factory("Windows.UI.Palettes.PaletteKinds", KindsFactoryId);
            text = NativeMethods.String("Command");
            nint kinds = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, nint*, int>)NativeMethods.Table(factory)[6])(factory, 1, &text, &kinds));
            return kinds;
        }
        finally
        {
            NativeMethods.WindowsDeleteString(text);
            NativeMethods.Release(factory);
        }
    }

    private static nint CreateIcon(string address)
    {
        nint uriFactory = 0, uri = 0, factory = 0, text = 0;
        try
        {
            uriFactory = NativeMethods.Factory("Windows.Foundation.Uri", UriFactoryId);
            text = NativeMethods.String(address);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(uriFactory)[6])(uriFactory, text, &uri));
            factory = NativeMethods.Factory("Windows.UI.Palettes.PaletteIconData", IconDataStaticsId);
            nint icon = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(factory)[8])(factory, uri, &icon));
            return icon;
        }
        finally
        {
            NativeMethods.Release(factory);
            NativeMethods.Release(uri);
            NativeMethods.WindowsDeleteString(text);
            NativeMethods.Release(uriFactory);
        }
    }

    // A zero itemView means a successful empty response. Inputs remain caller-owned.
    internal static nint BuildResults(nint query, nint itemView)
        => BuildResults(query, itemView == 0 ? [] : [itemView]);

    internal static nint BuildResults(nint query, ReadOnlySpan<nint> itemViews)
    {
        nint factory = 0, queryInterface = 0, instance = 0, results = 0, viewInterface = 0;
        try
        {
            factory = NativeMethods.Factory("Windows.UI.Palettes.PaletteQueryResults", QueryResultsFactoryId);
            queryInterface = NativeMethods.As(query, QueryId);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(factory)[6])(factory, queryInterface, &instance));
            results = NativeMethods.As(instance, QueryResultsId);
            fixed (nint* views = itemViews)
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)NativeMethods.Table(results)[8])(
                    results, (uint)itemViews.Length, views));
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, byte, int>)NativeMethods.Table(results)[10])(results, 0));

            nint view = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(results)[11])(results, &view));
            return view;
        }
        finally
        {
            NativeMethods.Release(viewInterface);
            NativeMethods.Release(results);
            NativeMethods.Release(instance);
            NativeMethods.Release(queryInterface);
            NativeMethods.Release(factory);
        }
    }

    internal static nint BuildProgress(ReadOnlySpan<nint> items)
    {
        var factory = NativeMethods.Factory("Windows.UI.Palettes.PaletteQueryProgress", new("16412cf9-3f5e-5e6c-85cd-8fe57f1bc538"));
        try
        {
            nint progress = 0;
            fixed (nint* values = items)
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint,uint,nint*,nint,nint*,int>)NativeMethods.Table(factory)[6])(
                    factory, (uint)items.Length, values, 0, &progress));
            return progress;
        }
        finally { NativeMethods.Release(factory); }
    }

    internal static string ReadQuery(nint query)
    {
        nint queryInterface = 0, json = 0;
        try
        {
            queryInterface = NativeMethods.As(query, QueryId);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(queryInterface)[6])(queryInterface, &json));
            using var document = JsonDocument.Parse(NativeMethods.ReadString(json));
            if (!document.RootElement.TryGetProperty("Arguments", out var arguments) || arguments.ValueKind != JsonValueKind.Array)
                return string.Empty;

            var values = new List<string>(arguments.GetArrayLength());
            foreach (var argument in arguments.EnumerateArray())
                if (argument.ValueKind == JsonValueKind.String)
                    values.Add(argument.GetString()!);
            return string.Join(' ', values).Trim();
        }
        finally
        {
            NativeMethods.WindowsDeleteString(json);
            NativeMethods.Release(queryInterface);
        }
    }

    private static nint CreateUriInvoker(string address)
    {
        nint uriFactory = 0, uri = 0, invokerFactory = 0, invoker = 0;
        try
        {
            uriFactory = NativeMethods.Factory("Windows.Foundation.Uri", UriFactoryId);
            var text = NativeMethods.String(address);
            try
            {
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(uriFactory)[6])(uriFactory, text, &uri));
            }
            finally { NativeMethods.WindowsDeleteString(text); }

            invokerFactory = NativeMethods.Factory("Windows.UI.Palettes.PaletteUriInvoker", UriInvokerFactoryId);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(invokerFactory)[6])(invokerFactory, uri, &invoker));
            return NativeMethods.As(invoker, InvokerId);
        }
        finally
        {
            NativeMethods.Release(invoker);
            NativeMethods.Release(invokerFactory);
            NativeMethods.Release(uri);
            NativeMethods.Release(uriFactory);
        }
    }

    private static void SetString(nint instance, int slot, string value)
    {
        var text = NativeMethods.String(value);
        try { NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)NativeMethods.Table(instance)[slot])(instance, text)); }
        finally { NativeMethods.WindowsDeleteString(text); }
    }

    internal static void SetSubtitle(nint item, params ReadOnlySpan<string> values)
    {
        // Subtitle is an IInspectable[] of rich content, not an HSTRING array.
        ArgumentOutOfRangeException.ThrowIfGreaterThan(values.Length, 16);
        Span<nint> boxed = stackalloc nint[values.Length];
        boxed.Clear();
        nint factory = 0;
        try
        {
            factory = NativeMethods.Factory("Windows.Foundation.PropertyValue", PropertyValueFactoryId);
            for (var index = 0; index < values.Length; index++)
            {
                var text = NativeMethods.String(values[index]);
                try
                {
                    nint value = 0;
                    NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(factory)[18])(factory, text, &value));
                    boxed[index] = value;
                }
                finally { NativeMethods.WindowsDeleteString(text); }
            }
            fixed (nint* array = boxed)
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)NativeMethods.Table(item)[15])(item, (uint)boxed.Length, array));
        }
        finally
        {
            foreach (var value in boxed) NativeMethods.Release(value);
            NativeMethods.Release(factory);
        }
    }
}
