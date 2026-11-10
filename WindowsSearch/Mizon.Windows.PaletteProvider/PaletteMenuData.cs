using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mizon.Windows.PaletteProvider;

// Contracts verified against the installed OSClient.API proxy descriptors and
// RunUX's native context-menu factory. These are build-specific Palette APIs.
internal static unsafe class PaletteMenuData
{
    private static readonly Guid ItemFactoryId = new("72784abe-da8a-5f48-895c-d8e31d03ae40");
    private static readonly Guid ItemId = new("095b1f54-5206-5f2b-acc5-ae72be801e11");
    private static readonly Guid QueryId = new("a3d9ee47-0151-5a33-964a-d2d4c91e97ea");
    private static readonly Guid QueryResultsFactoryId = new("3c262865-a9e4-5c17-b303-ecc310f5eb71");
    private static readonly Guid QueryResultsId = new("587b3bed-2c37-53af-b479-c2986bca59dc");
    private static readonly Guid KindsFactoryId = new("be554067-0c61-5273-b66e-dc142f6e01a9");
    private static readonly Guid KnownKindsId = new("293f77f5-b048-50f7-a97b-035e4472afd2");
    private static readonly Guid DelegateInvokerFactoryId = new("e7898783-ce65-5218-b594-9a6e9dbc5e6f");
    private static readonly Guid InvokerId = new("4dce3641-d67a-5402-82e6-f77be462c942");
    private const int ContextMenuMode = 2;

    // The native item retains its delegate. No extension/factory or event
    // subscription is retained, so there is no item-extension reference cycle.
    internal static void Attach(nint item, IPaletteResultState state) => AttachMenu(item, state, copyMenu: false);

    internal static void AttachMainAction(nint item, IPaletteResultState state, string action)
    {
        var invoker = CreateInvoker(state, action);
        try { NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)NativeMethods.Table(item)[24])(item, invoker)); }
        finally { NativeMethods.Release(invoker); }
    }

    private static void AttachMenu(nint item, IPaletteResultState state, bool copyMenu)
    {
        var callback = new MenuQueryDelegate(state, copyMenu);
        try
        {
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)NativeMethods.Table(item)[26])(
                item, callback.Identity));
        }
        finally { NativeMethods.Release(callback.Identity); }
    }

    private static nint BuildMenu(nint suppliedFactory, nint query, IPaletteResultState state, bool copyMenu)
    {
        var items = new List<nint>(6);
        nint factory = 0;
        try
        {
            factory = NativeMethods.As(suppliedFactory, ItemFactoryId);
            if (state.Result.Kind == PaletteResultKind.Demo)
            {
                if (copyMenu)
                {
                    string[] sections = ["عکس، دکمه و اطلاعات", "اسلایدر و سوییچ", "نمودار", "پیشرفت", "ورودی‌ها"];
                    for (var index = 0; index < sections.Length; index++)
                        items.Add(CreateItem(factory, $"demo-section-{index}", sections[index], "Command", state, $"demo-section-{index}"));
                }
                else
                {
                    items.Add(CreateItem(factory, "demo-next", "بخش بعدی", "Command", state, "demo-next"));
                    items.Add(CreateItem(factory, "demo-previous", "بخش قبلی", "Command", state, "demo-previous"));
                    items.Add(CreateItem(factory, "demo-sections", "بخش‌های نمونه", "Container", state, copyMenu: true));
                    items.Add(CreateItem(factory, "separator", string.Empty, "Separator", state));
                    var busy = state.GetSnapshot().IsBusy;
                    items.Add(CreateItem(factory, "demo-progress", busy ? "لغو پیشرفت آزمایشی" : "شروع پیشرفت آزمایشی", "Command", state, busy ? "demo-cancel" : "demo-progress"));
                    items.Add(CreateItem(factory, "demo-reset", "بازنشانی نمونه", "Command", state, "demo-reset"));
                }
                return BuildResults(query, items);
            }
            if (copyMenu)
            {
                items.Add(CreateItem(factory, "copy-link", "کپی لینک", "Command", state, "copy-link"));
                items.Add(CreateItem(factory, "copy-title", state.Result.Kind == PaletteResultKind.Music ? "کپی عنوان آهنگ" : "کپی نام نمایشی", "Command", state, "copy-title"));
                if (state.Result.Kind == PaletteResultKind.Client && !string.IsNullOrWhiteSpace(state.Result.Username))
                    items.Add(CreateItem(factory, "copy-username", "کپی نام کاربری", "Command", state, "copy-username"));
                return BuildResults(query, items);
            }
            if (state.Result.Kind == PaletteResultKind.Music)
            {
                items.Add(CreateItem(factory, "play", "پخش در میزون", "Command", state, "play"));
                items.Add(CreateItem(factory, "details", "جزئیات آهنگ", "Command", state, "details"));
                var snapshot = state.GetSnapshot();
                if (snapshot.IsBusy)
                    items.Add(CreateItem(factory, "download", "لغو دانلود", "Command", state, "cancel-download"));
                else if (snapshot.CanDownload)
                    items.Add(CreateItem(factory, "download", "دانلود آهنگ", "Command", state, "download"));
            }
            else
            {
                items.Add(CreateItem(factory, "profile", "مشاهدهٔ پروفایل", "Command", state, "profile"));
            }
            items.Add(CreateItem(factory, "separator", string.Empty, "Separator", state));
            items.Add(CreateItem(factory, "copy", "کپی کردن", "Container", state, copyMenu: true));
            return BuildResults(query, items);
        }
        finally
        {
            foreach (var item in items) NativeMethods.Release(item);
            NativeMethods.Release(factory);
        }
    }

    private static nint CreateItem(nint factory, string id, string title, string kind, IPaletteResultState state, string? action = null, bool copyMenu = false)
    {
        nint kinds = 0, instance = 0, item = 0, invoker = 0;
        try
        {
            kinds = CreateKinds(kind);
            var itemId = NativeMethods.String($"{state.Result.Key}/actions/{id}");
            try
            {
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint, nint*, int>)NativeMethods.Table(factory)[8])(
                    factory, itemId, kinds, &instance));
            }
            finally { NativeMethods.WindowsDeleteString(itemId); }
            item = NativeMethods.As(instance, ItemId);
            var text = NativeMethods.String(title);
            try { NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)NativeMethods.Table(item)[13])(item, text)); }
            finally { NativeMethods.WindowsDeleteString(text); }

            var glyph = id switch
            {
                "play" => "\uE768",
                "details" => "\uE8A7",
                "profile" => "\uE77B",
                "download" => action == "cancel-download" ? "\uE711" : "\uE896",
                "copy" => "\uE8C8",
                "copy-link" => "\uE71B",
                "copy-title" => "\uE8D2",
                "copy-username" => "\uE77B",
                "demo-next" => "\uE893",
                "demo-previous" => "\uE892",
                "demo-sections" => "\uE80A",
                "demo-progress" => "\uE895",
                "demo-reset" => "\uE777",
                "demo-section-0" => "\uEB9F",
                "demo-section-1" => "\uE9E9",
                "demo-section-2" => "\uE9D9",
                "demo-section-3" => "\uE895",
                "demo-section-4" => "\uE70F",
                _ => null
            };
            if (glyph is not null)
            {
                var icon = PalettePresentation.CreateGlyphIcon(glyph);
                try { NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)NativeMethods.Table(item)[9])(item, icon)); }
                finally { NativeMethods.Release(icon); }
            }

            if (copyMenu) AttachMenu(item, state, copyMenu: true);
            if (action is not null)
            {
                invoker = CreateInvoker(state, action);
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)NativeMethods.Table(item)[24])(item, invoker));
            }
            nint view = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(item)[19])(item, &view));
            return view;
        }
        finally
        {
            NativeMethods.Release(invoker);
            NativeMethods.Release(item);
            NativeMethods.Release(instance);
            NativeMethods.Release(kinds);
        }
    }

    private static nint CreateKinds(string kind)
    {
        nint knownKinds = 0, factory = 0, text = 0;
        try
        {
            if (kind is "Container" or "Command")
            {
                knownKinds = NativeMethods.Factory("Windows.UI.Palettes.KnownPaletteKinds", KnownKindsId);
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(knownKinds)[kind == "Container" ? 6 : 7])(
                    knownKinds, &text));
            }
            else text = NativeMethods.String(kind); // Native CreateContextMenuSeparator uses "Separator".
            factory = NativeMethods.Factory("Windows.UI.Palettes.PaletteKinds", KindsFactoryId);
            nint value = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, nint*, int>)NativeMethods.Table(factory)[6])(
                factory, 1, &text, &value));
            return value;
        }
        finally
        {
            NativeMethods.WindowsDeleteString(text);
            NativeMethods.Release(factory);
            NativeMethods.Release(knownKinds);
        }
    }

    private static nint CreateInvoker(IPaletteResultState state, string action)
    {
        var callback = new MenuInvokeDelegate(state, action);
        nint factory = 0, invoker = 0;
        try
        {
            factory = NativeMethods.Factory("Windows.UI.Palettes.PaletteDelegateInvoker", DelegateInvokerFactoryId);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(factory)[6])(
                factory, callback.Identity, &invoker));
            return NativeMethods.As(invoker, InvokerId);
        }
        finally
        {
            NativeMethods.Release(invoker);
            NativeMethods.Release(factory);
            NativeMethods.Release(callback.Identity);
        }
    }

    private static nint BuildResults(nint query, List<nint> items)
    {
        nint factory = 0, instance = 0, results = 0;
        try
        {
            factory = NativeMethods.Factory("Windows.UI.Palettes.PaletteQueryResults", QueryResultsFactoryId);
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)NativeMethods.Table(factory)[6])(factory, query, &instance));
            results = NativeMethods.As(instance, QueryResultsId);
            var values = items.ToArray();
            fixed (nint* pointers = values)
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)NativeMethods.Table(results)[8])(
                    results, (uint)values.Length, pointers));
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, byte, int>)NativeMethods.Table(results)[10])(results, 0));
            nint view = 0;
            NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)NativeMethods.Table(results)[11])(results, &view));
            return view;
        }
        finally
        {
            NativeMethods.Release(results);
            NativeMethods.Release(instance);
            NativeMethods.Release(factory);
        }
    }

    private sealed class MenuQueryDelegate : NativeObject
    {
        private static readonly Guid InterfaceId = new("d28c2de0-91db-55d2-b300-27604baac819");
        private static readonly nint Vtable = CreateTable();
        private readonly IPaletteResultState _state;
        private readonly bool _copyMenu;
        internal MenuQueryDelegate(IPaletteResultState state, bool copyMenu) : base(false)
        {
            try { _state = state; _copyMenu = copyMenu; AddInterface(InterfaceId, Vtable); EnableMarshaling(); }
            catch { Dispose(); throw; }
        }
        internal override string RuntimeClassName => "Mizon.PaletteMenu.Query";
        private static nint CreateTable()
        {
            var table = (nint*)NewTable(4, false);
            table[3] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, nint, nint*, int>)&Invoke;
            return (nint)table;
        }
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int Invoke(nint self, nint factory, nint query, nint* operation)
        {
            if (operation == null) return NativeMethods.PointerError;
            *operation = 0;
            if (factory == 0 || query == 0) return NativeMethods.InvalidArgument;
            nint queryInterface = 0, results = 0;
            try
            {
                var owner = Owner<MenuQueryDelegate>(self);
                queryInterface = NativeMethods.As(query, QueryId);
                int mode = 0;
                NativeMethods.Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)NativeMethods.Table(queryInterface)[21])(queryInterface, &mode));
                results = mode == ContextMenuMode
                    ? BuildMenu(factory, queryInterface, owner._state, owner._copyMenu)
                    : BuildResults(queryInterface, []);
                *operation = new CompletedOperation(results).Identity;
                Program.Activity();
                Log.Write($"Item menu query mode={mode}; kind={owner._state.Result.Kind}; copyMenu={owner._copyMenu}.");
                return 0;
            }
            catch (Exception error) { return Log.Error("ItemMenuQuery", error); }
            finally { NativeMethods.Release(results); NativeMethods.Release(queryInterface); }
        }
    }

    private sealed class MenuInvokeDelegate : NativeObject
    {
        private static readonly Guid InterfaceId = new("a2335a2e-abc8-5c1d-8b62-89c7f39b0e0a");
        private static readonly nint Vtable = CreateTable();
        private readonly IPaletteResultState _state;
        private readonly string _action;
        internal MenuInvokeDelegate(IPaletteResultState state, string action) : base(false)
        {
            try { _state = state; _action = action; AddInterface(InterfaceId, Vtable); EnableMarshaling(); }
            catch { Dispose(); throw; }
        }
        internal override string RuntimeClassName => "Mizon.PaletteMenu.Invoke";
        private static nint CreateTable()
        {
            var table = (nint*)NewTable(4, false);
            table[3] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, int>)&Invoke;
            return (nint)table;
        }
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int Invoke(nint self, nint query)
        {
            try
            {
                var owner = Owner<MenuInvokeDelegate>(self);
                owner._state.Invoke(owner._action);
                Program.Activity();
                return 0;
            }
            catch (Exception error) { return Log.Error("ItemMenuAction", error); }
        }
    }
}
