// A diagnostics TAP attaches to Explorer's existing Windows.UI.Xaml tree.
// This is not a WinUI 3 overlay. See README for the unsupported shell boundary.
#include <windows.h>
#include <shlobj.h>
#undef GetCurrentTime
#include <xamlom.h>
#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Foundation.Numerics.h>
#include <winrt/Windows.UI.h>
#include <winrt/Windows.UI.Core.h>
#include <winrt/Windows.UI.ViewManagement.h>
#include <winrt/Windows.UI.Composition.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Controls.Primitives.h>
#include <winrt/Windows.UI.Xaml.Input.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.UI.Xaml.Media.Imaging.h>
#include <winrt/Windows.UI.Xaml.Hosting.h>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <winrt/Windows.UI.Text.h>
#include <winrt/Windows.System.h>
#include <filesystem>
#include <fstream>
#include <memory>
#include <vector>
#include <chrono>
#include <mutex>
#include <set>
#include <dbghelp.h>
#include "Native/MinHook/include/MinHook.h"

using namespace winrt;
using namespace Windows::Foundation;
using namespace Windows::UI;
using namespace Windows::UI::Xaml;
using namespace Windows::UI::Xaml::Controls;
using namespace Windows::UI::Xaml::Media;
using namespace Windows::UI::Xaml::Media::Imaging;
namespace fs = std::filesystem;
static HMODULE module;
static constexpr CLSID TapId{0x34f64231,0x33cd,0x4e63,{0xa8,0x51,0xe2,0xd6,0x80,0x35,0xa1,0x92}};
static fs::path ModuleDirectory() { wchar_t b[32768]; GetModuleFileNameW(module,b,32768); return fs::path(b).parent_path(); }
static void Log(std::wstring const& text) noexcept {
    try {
        wchar_t home[32768]; GetEnvironmentVariableW(L"USERPROFILE",home,32768);
        auto path=fs::path(home)/L".mizon"/L"Widgets"; fs::create_directories(path);
        std::ofstream file(path/L"native.log",std::ios::app);
        file << GetTickCount64() << " " << to_string(text) << "\n";
    } catch (...) {}
}
static SolidColorBrush MakeBrush(Color c) { return SolidColorBrush(c); }
struct Reminder { wchar_t const* title; wchar_t const* category; wchar_t const* subtitle; wchar_t const* asset; int days; Color accent; };
static const Reminder items[]{
    {L"Interstellar",L"سینمایی",L"نمایش ویژه",L"interstellar.jpg",9,{255,103,202,255}},
    {L"SILO",L"سریال",L"قسمت بعدی",L"silo.ico",17,{255,222,190,117}},
    {L"Severance",L"سریال",L"فصل جدید",L"severance.webp",28,{255,151,225,188}}
};
static TextBlock MakeText(hstring value,double size,Color color) {
    TextBlock t; t.Text(value); t.FontSize(size); t.Foreground(MakeBrush(color));
    t.FontFamily(FontFamily(L"Segoe UI")); t.TextTrimming(TextTrimming::CharacterEllipsis); return t;
}
static void OpenApp() { ShellExecuteW(nullptr,L"open",L"mizon://movies",nullptr,nullptr,SW_SHOWNORMAL); Log(L"Open Mizon movies"); }

struct HiddenChild { UIElement element; Visibility visibility; bool hitTest; };
struct Widget : std::enable_shared_from_this<Widget> {
    FrameworkElement owner{nullptr}; Panel panel{nullptr}; Grid root{nullptr}, current{nullptr}, outgoing{nullptr};
    std::vector<HiddenChild> hidden;
    DispatcherTimer timer{nullptr}; event_token tick{};bool layoutLogged{};
    HANDLE stop{}; int index{}; bool paused{},hover{},menuOpen{},disposed{};
    ULONGLONG next{},transitionEnd{}; double oldMinWidth{},oldWidth{};
    double oldPanelWidth{},oldPanelMinWidth{};
    Thickness oldOwnerMargin{},oldPanelMargin{},oldPadding{};
    HorizontalAlignment oldOwnerAlignment{},oldPanelAlignment{},oldContentAlignment{};
    event_token entered{},exited{},tapped{},rightTapped{},pressed{},released{};
    MenuFlyout menu{nullptr};
    ~Widget() { if(stop) CloseHandle(stop); }

    Grid Card() {
        auto const& r=items[index];
        Grid card; card.Height(40); card.ColumnSpacing(9);
        ColumnDefinition imageCol; imageCol.Width(GridLengthHelper::FromPixels(33)); card.ColumnDefinitions().Append(imageCol);
        ColumnDefinition textCol; textCol.Width(GridLengthHelper::FromValueAndType(1,GridUnitType::Star)); card.ColumnDefinitions().Append(textCol);
        Border cover; cover.Width(32); cover.Height(36); cover.CornerRadius({5,5,5,5}); cover.Background(MakeBrush(r.accent));
        Image image; image.Stretch(Stretch::UniformToFill);
        auto file=ModuleDirectory()/L"Assets"/r.asset;
        if(fs::exists(file)) { auto path=file.wstring(); for(auto& c:path) if(c==L'\\') c=L'/'; image.Source(BitmapImage(Uri(L"file:///"+path))); cover.Child(image); }
        else { auto letter=MakeText(hstring(std::wstring(r.title).substr(0,1)),20,Colors::Black()); letter.HorizontalAlignment(HorizontalAlignment::Center); cover.Child(letter); }
        card.Children().Append(cover);
        Grid content; Grid::SetColumn(content,1);
        RowDefinition row; row.Height(GridLengthHelper::FromPixels(18)); content.RowDefinitions().Append(row);
        RowDefinition row2; row2.Height(GridLengthHelper::FromPixels(21)); content.RowDefinitions().Append(row2);
        Grid first; first.ColumnSpacing(8);
        ColumnDefinition titleCol; titleCol.Width(GridLengthHelper::FromValueAndType(1,GridUnitType::Star)); first.ColumnDefinitions().Append(titleCol);
        ColumnDefinition badgeCol; badgeCol.Width(GridLengthHelper::Auto()); first.ColumnDefinitions().Append(badgeCol);
        auto title=MakeText(r.title,12,Colors::White()); title.FontWeight(Windows::UI::Text::FontWeights::SemiBold());
        first.Children().Append(title);
        auto badge=MakeText(L"MIZON · نمونه",9,{255,167,176,190}); Grid::SetColumn(badge,1); first.Children().Append(badge);
        content.Children().Append(first);
        Grid second; Grid::SetRow(second,1);
        auto subtitle=MakeText(hstring(r.subtitle)+L" · "+to_hstring(r.days)+L" روز مانده",11,r.accent);
        subtitle.FlowDirection(FlowDirection::RightToLeft); subtitle.TextAlignment(TextAlignment::Right);
        second.Children().Append(subtitle); content.Children().Append(second); card.Children().Append(content);
        Automation::AutomationProperties::SetName(root,hstring(r.title)+L"، "+r.subtitle+L"، "+to_hstring(r.days)+L" روز مانده. یادآوری نمایشی. برای باز کردن میزون کلیک کنید.");
        ToolTipService::SetToolTip(root,box_value(hstring(r.category)+L" • "+r.title+L"\n"+r.subtitle+L" · "+to_hstring(r.days)+L" روز مانده\nدادهٔ نمایشی — تاریخ انتشار واقعی نیست"));
        return card;
    }

    void Animate(Grid const& target, bool arriving) {
        auto visual=Hosting::ElementCompositionPreview::GetElementVisual(target);
        auto compositor=visual.Compositor();
        auto opacity=compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0,arriving?0.f:1.f); opacity.InsertKeyFrame(1,arriving?1.f:0.f);
        opacity.Duration(std::chrono::milliseconds(320)); visual.StartAnimation(L"Opacity",opacity);
        Hosting::ElementCompositionPreview::SetIsTranslationEnabled(target,true);
        auto movement=compositor.CreateVector3KeyFrameAnimation();
        auto easing=compositor.CreateCubicBezierEasingFunction({.18f,.7f},{.2f,1.f});
        movement.InsertKeyFrame(0,{0,arriving?10.f:0.f,0}); movement.InsertKeyFrame(1,{0,arriving?0.f:-10.f,0},easing);
        movement.Duration(std::chrono::milliseconds(320)); visual.StartAnimation(L"Translation",movement);
    }
    void Advance(int direction) {
        if(disposed) return;
        if(outgoing) { uint32_t p; if(root.Children().IndexOf(outgoing,p)) root.Children().RemoveAt(p); outgoing=nullptr; }
        index=(index+direction+3)%3;
        outgoing=current; current=Card(); root.Children().Append(current);
        if(Windows::UI::ViewManagement::UISettings().AnimationsEnabled()) { Animate(outgoing,false); Animate(current,true); transitionEnd=GetTickCount64()+400; }
        else { uint32_t p; if(root.Children().IndexOf(outgoing,p)) root.Children().RemoveAt(p); outgoing=nullptr; }
        next=GetTickCount64()+7000; Log(L"Showing reminder: "+std::wstring(items[index].title));
    }
    void ShowMenu(Windows::UI::Xaml::Input::RightTappedRoutedEventArgs const& e) {
        e.Handled(true); menu=MenuFlyout(); menuOpen=true; 
        auto weak=weak_from_this();
        auto add=[&](wchar_t const* label,wchar_t const* glyph,auto action) {
            MenuFlyoutItem item; item.FlowDirection(FlowDirection::RightToLeft); item.Text(label); FontIcon icon; icon.FontFamily(FontFamily(L"Segoe Fluent Icons")); icon.Glyph(glyph); item.Icon(icon);
            item.Click([weak,action](auto&&,auto&&) { if(auto self=weak.lock()) if(!self->disposed) action(*self); }); menu.Items().Append(item);
        };
        add(L"باز کردن میزون",L"\xE8A7",[](Widget&){OpenApp();});
        menu.Items().Append(MenuFlyoutSeparator());
        add(L"یادآوری بعدی",L"\xE893",[](Widget& s){s.Advance(1);});
        add(L"یادآوری قبلی",L"\xE892",[](Widget& s){s.Advance(-1);});
        add(paused?L"ادامهٔ جابه‌جایی خودکار":L"توقف جابه‌جایی خودکار",paused?L"\xE768":L"\xE769",[](Widget& s){s.paused=!s.paused;s.next=GetTickCount64()+7000;});
        menu.Items().Append(MenuFlyoutSeparator());
        add(L"بازگرداندن ویجت ویندوز",L"\xE777",[](Widget& s){SetEvent(s.stop);});
        menu.Closed([weak](auto&&,auto&&){if(auto self=weak.lock()){self->menuOpen=false;self->next=GetTickCount64()+7000;}});
        Controls::Primitives::FlyoutShowOptions options; options.Position(e.GetPosition(root)); menu.ShowAt(root,options);
        Log(L"Context menu opened");
    }
    void Attach(FrameworkElement target,Panel host) {
        owner=target;panel=host;oldMinWidth=owner.MinWidth();oldWidth=owner.Width();
        oldOwnerMargin=owner.Margin();oldOwnerAlignment=owner.HorizontalAlignment();
        oldPanelWidth=panel.Width();oldPanelMinWidth=panel.MinWidth();oldPanelMargin=panel.Margin();oldPanelAlignment=panel.HorizontalAlignment();
        if(auto control=owner.try_as<Control>()) {
            oldPadding=control.Padding();oldContentAlignment=control.HorizontalContentAlignment();
            control.Padding({0,0,0,0});control.HorizontalContentAlignment(HorizontalAlignment::Left);
        }
        root=Grid();root.Name(L"MizonReminderWidget");root.Width(238);root.Height(40);root.Padding({4,0,4,0});root.CornerRadius({6,6,6,6});root.Background(MakeBrush(Colors::Transparent()));
        root.VerticalAlignment(VerticalAlignment::Center);root.HorizontalAlignment(HorizontalAlignment::Left);
        for(auto child:panel.Children()) { hidden.push_back({child,child.Visibility(),child.IsHitTestVisible()});child.Visibility(Visibility::Collapsed);child.IsHitTestVisible(false); }
        owner.MinWidth(246);owner.Width(246);
        // The stock template centers a 152-DIP TaskListButtonPanel inside the
        // enlarged owner. That both adds a left gutter and clips our 238-DIP card.
        owner.Margin({8,0,0,0});owner.HorizontalAlignment(HorizontalAlignment::Left);
        panel.Width(238);panel.MinWidth(238);panel.Margin({0,0,0,0});panel.HorizontalAlignment(HorizontalAlignment::Left);
        current=Card();root.Children().Append(current);panel.Children().Append(root);
        auto weak=weak_from_this();
        entered=root.PointerEntered([weak](auto&&,auto&&){if(auto s=weak.lock()){s->hover=true;s->root.Background(MakeBrush({24,255,255,255}));try{Window::Current().CoreWindow().PointerCursor(Windows::UI::Core::CoreCursor(Windows::UI::Core::CoreCursorType::Hand,0));}catch(...){}}});
        exited=root.PointerExited([weak](auto&&,auto&&){if(auto s=weak.lock()){s->hover=false;s->next=GetTickCount64()+7000;s->root.Background(MakeBrush(Colors::Transparent()));try{Window::Current().CoreWindow().PointerCursor(Windows::UI::Core::CoreCursor(Windows::UI::Core::CoreCursorType::Arrow,0));}catch(...){}}});
        pressed=root.PointerPressed([](auto&&,auto const& e){e.Handled(true);});
        released=root.PointerReleased([](auto&&,auto const& e){e.Handled(true);});
        tapped=root.Tapped([](auto&&,auto const& e){e.Handled(true);OpenApp();});
        rightTapped=root.RightTapped([weak](auto&&,auto const& e){if(auto s=weak.lock())s->ShowMenu(e);});
        stop=CreateEventW(nullptr,TRUE,FALSE,L"Local\\Mizon.Widgets.Stop");
        timer=DispatcherTimer();timer.Interval(std::chrono::milliseconds(500));next=GetTickCount64()+7000;
        tick=timer.Tick([weak](auto&&,auto&&){if(auto s=weak.lock())try{
            if(WaitForSingleObject(s->stop,0)==WAIT_OBJECT_0){s->Dispose();return;}
                        auto now=GetTickCount64();
            if(!s->layoutLogged){s->layoutLogged=true;FrameworkElement n=s->root;
                for(int d=0;n&&d<9;d++) {Log(L"Layout "+std::wstring(get_class_name(n))+L"/"+std::wstring(n.Name())+L" actual="+std::to_wstring(n.ActualWidth())+L"x"+std::to_wstring(n.ActualHeight())+L" max="+std::to_wstring(n.MaxWidth())+L"x"+std::to_wstring(n.MaxHeight()));auto p=VisualTreeHelper::GetParent(n);n=p?p.try_as<FrameworkElement>():nullptr;}}
            if(s->outgoing&&now>=s->transitionEnd){uint32_t p;if(s->root.Children().IndexOf(s->outgoing,p))s->root.Children().RemoveAt(p);s->outgoing=nullptr;}
            // The shell may refresh its own announcement; keep only our reserved content visible.
            for(auto const& child:s->hidden) if(child.element.Visibility()!=Visibility::Collapsed)child.element.Visibility(Visibility::Collapsed);
            if(!s->paused&&!s->hover&&!s->menuOpen&&now>=s->next)s->Advance(1);
        }catch(...){Log(L"Tick error "+std::to_wstring(winrt::to_hresult()));}});
        timer.Start();Log(L"Attached Mizon widget to actual Widgets XAML host");
    }
    void Dispose() noexcept {
        if(disposed)return;disposed=true;
        try{
            timer.Stop();timer.Tick(tick);timer=nullptr;if(menu)menu.Hide();menu=nullptr;
            root.PointerEntered(entered);root.PointerExited(exited);root.PointerPressed(pressed);root.PointerReleased(released);root.Tapped(tapped);root.RightTapped(rightTapped);
            uint32_t p;if(panel.Children().IndexOf(root,p))panel.Children().RemoveAt(p);
            for(auto const& c:hidden){c.element.Visibility(c.visibility);c.element.IsHitTestVisible(c.hitTest);}hidden.clear();
            panel.Width(oldPanelWidth);panel.MinWidth(oldPanelMinWidth);panel.Margin(oldPanelMargin);panel.HorizontalAlignment(oldPanelAlignment);
            owner.Width(oldWidth);owner.MinWidth(oldMinWidth);owner.Margin(oldOwnerMargin);owner.HorizontalAlignment(oldOwnerAlignment);
            if(auto control=owner.try_as<Control>()){control.Padding(oldPadding);control.HorizontalContentAlignment(oldContentAlignment);}
            root=nullptr;current=nullptr;outgoing=nullptr;panel=nullptr;owner=nullptr;
            Log(L"Restored original Widgets content and disposed timers/events");
        }catch(...){Log(L"Restore error");}
    }
};

static thread_local std::vector<std::shared_ptr<Widget>> widgets;
static bool Stopped() { auto e=OpenEventW(SYNCHRONIZE,FALSE,L"Local\\Mizon.Widgets.Stop");if(!e)return false;auto stopped=WaitForSingleObject(e,0)==WAIT_OBJECT_0;CloseHandle(e);return stopped; }
static Panel FindPanel(FrameworkElement const& element) {
    auto count=VisualTreeHelper::GetChildrenCount(element);
    for(int i=0;i<count;i++) if(auto child=VisualTreeHelper::GetChild(element,i).try_as<FrameworkElement>()) {
        if(auto p=child.try_as<Panel>())return p;
        if(auto p=FindPanel(child))return p;
    }return nullptr;
}
static void TryAttach(FrameworkElement element) {
    if(!element||Stopped())return;
    auto type=get_class_name(element);
    if(std::wstring_view(type).find(L"AugmentedEntryPoint")==std::wstring_view::npos&&element.Name()!=L"WidgetsButton"&&element.Name()!=L"Widgets")return;
    for(auto const& existing:widgets)if(existing->owner==element)return;
    if(auto p=FindPanel(element)) { auto widget=std::make_shared<Widget>();widgets.push_back(widget);widget->Attach(element,p); }
}
static void ScanTree(FrameworkElement const& node,int depth=0) {
    if(!node||depth>40)return;TryAttach(node);
    if(node.Name()==L"MizonReminderWidget")return;
    auto n=VisualTreeHelper::GetChildrenCount(node);
    for(int i=0;i<n;i++)if(auto child=VisualTreeHelper::GetChild(node,i).try_as<FrameworkElement>())ScanTree(child,depth+1);
}using UpdateStates=void(__fastcall*)(void*);
static UpdateStates originalUpdate;
static thread_local bool queued=false;
static void __fastcall UpdateHook(void* instance) {
    originalUpdate(instance);
    if(queued||Stopped())return;
    try {
        // Taskbar.View's C++/WinRT implementation base: same interface location used
        // by Windhawk Weather. Enabled only with an exact matching public PDB symbol.
        Windows::Foundation::IUnknown unknown{nullptr};copy_from_abi(unknown,static_cast<void**>(instance)+3);
        if(auto element=unknown.try_as<FrameworkElement>()) {
            queued=true;auto weak=make_weak(element);
            element.Dispatcher().RunAsync(Windows::UI::Core::CoreDispatcherPriority::Low,[weak]{
                queued=false;if(Stopped())return;
                try{if(auto element=weak.get()){
                    while(auto parent=VisualTreeHelper::GetParent(element)){auto node=parent.try_as<FrameworkElement>();if(!node)break;element=node;}
                    bool hasActive=false;for(auto const& widget:widgets)if(!widget->disposed)hasActive=true;
                    if(!hasActive)ScanTree(element);
                }}catch(...){Log(L"Hook scan failed "+std::to_wstring(to_hresult()));}
            });
        }
    }catch(...){Log(L"Hook object error "+std::to_wstring(to_hresult()));}
}
static void StartShellHook() {
    auto view=GetModuleHandleW(L"Taskbar.View.dll");if(!view){Log(L"Taskbar.View not loaded");return;}
    std::ifstream input(ModuleDirectory()/L"taskbar-symbol.txt");uint64_t rva=0;input>>rva;
    auto dos=reinterpret_cast<IMAGE_DOS_HEADER*>(view);auto pe=reinterpret_cast<IMAGE_NT_HEADERS*>(reinterpret_cast<uint8_t*>(view)+dos->e_lfanew);
    if(!rva||rva>=pe->OptionalHeader.SizeOfImage){Log(L"Invalid or missing matching taskbar symbol");return;}
    auto initialized=MH_Initialize();if(initialized!=MH_OK&&initialized!=MH_ERROR_ALREADY_INITIALIZED){Log(L"MinHook initialization failed");return;}
    auto target=reinterpret_cast<uint8_t*>(view)+rva;
    auto status=MH_CreateHook(target,reinterpret_cast<void*>(UpdateHook),reinterpret_cast<void**>(&originalUpdate));
    if(status==MH_OK)status=MH_EnableHook(target);
    Log(L"Taskbar visual-state hook: "+std::to_wstring(status));
    if(status==MH_OK){auto taskbar=FindWindowW(L"Shell_TrayWnd",nullptr);DWORD_PTR ignored;SendMessageTimeoutW(taskbar,WM_THEMECHANGED,0,0,SMTO_ABORTIFHUNG,1000,&ignored);}
}
struct Tap:implements<Tap,IObjectWithSite,non_agile> {
    com_ptr<::IUnknown> site;
    HRESULT STDMETHODCALLTYPE SetSite(::IUnknown* value) noexcept override {
        try{site.copy_from(value);if(!site)return S_OK;
            // Keep native callback code resident until Explorer exits, even after restoring UI.
            HMODULE pin;GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,reinterpret_cast<LPCWSTR>(&ModuleDirectory),&pin);
            auto thread=CreateThread(nullptr,0,[](void*)->DWORD{
                auto apartment=CoInitializeEx(nullptr,COINIT_MULTITHREADED);StartShellHook();
                auto stop=CreateEventW(nullptr,TRUE,FALSE,L"Local\\Mizon.Widgets.Stop");
                WaitForSingleObject(stop,INFINITE);CloseHandle(stop);
                MH_DisableHook(MH_ALL_HOOKS);MH_RemoveHook(MH_ALL_HOOKS);MH_Uninitialize();
                Log(L"Native hooks removed; UI restoration scheduled by its dispatcher timer");
                if(SUCCEEDED(apartment))CoUninitialize();return 0;
            },nullptr,0,nullptr);
            if(thread)CloseHandle(thread);Log(L"Diagnostics TAP connected");return S_OK;
        }catch(...){return to_hresult();}
    }
    HRESULT STDMETHODCALLTYPE GetSite(REFIID iid,void** value) noexcept override{return site.as(iid,value);}
};
struct Factory:implements<Factory,IClassFactory,non_agile> {
    HRESULT STDMETHODCALLTYPE CreateInstance(::IUnknown* outer,REFIID iid,void** value) noexcept override {if(outer)return CLASS_E_NOAGGREGATION;try{return make<Tap>().as(iid,value);}catch(...){return to_hresult();}}
    HRESULT STDMETHODCALLTYPE LockServer(BOOL) noexcept override{return S_OK;}
};
STDAPI DllGetClassObject(REFCLSID clsid,REFIID iid,void** value) {if(clsid!=TapId)return CLASS_E_CLASSNOTAVAILABLE;try{return make<Factory>().as(iid,value);}catch(...){return to_hresult();}}
STDAPI DllCanUnloadNow(){return S_FALSE;}
extern "C" __declspec(dllexport) HRESULT __stdcall AttachToExplorer(DWORD pid,LPCWSTR path) noexcept {
    auto xaml=LoadLibraryExW(L"Windows.UI.Xaml.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);if(!xaml)return HRESULT_FROM_WIN32(GetLastError());
    auto fn=reinterpret_cast<decltype(&InitializeXamlDiagnosticsEx)>(GetProcAddress(xaml,"InitializeXamlDiagnosticsEx"));
    HRESULT result=E_NOINTERFACE;if(fn)for(int i=1;i<=100;i++){auto name=L"VisualDiagConnection"+std::to_wstring(i);result=fn(name.c_str(),pid,L"",path,TapId,nullptr);if(result!=HRESULT_FROM_WIN32(ERROR_NOT_FOUND))break;}
    FreeLibrary(xaml);return result;
}
extern "C" __declspec(dllexport) uint64_t __stdcall ResolveTaskbarSymbol(LPCWSTR taskbarPath,LPCWSTR cachePath) noexcept {
    // Runs in the companion, never blocks Explorer on downloading symbols.
    auto session=CreateFileW(L"NUL",GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,OPEN_EXISTING,0,nullptr);
    std::wstring search=L"srv*"+std::wstring(cachePath)+L"*https://msdl.microsoft.com/download/symbols";
    SymSetOptions(SYMOPT_UNDNAME|SYMOPT_DEFERRED_LOADS|SYMOPT_FAIL_CRITICAL_ERRORS|SYMOPT_NO_PROMPTS);
    uint64_t offset=0;
    if(SymInitializeW(session,search.c_str(),FALSE)) {
        auto base=SymLoadModuleExW(session,nullptr,taskbarPath,nullptr,0,0,nullptr,0);
        struct Result{DWORD64 base;uint64_t offset;};Result result{base,0};
        if(base)SymEnumSymbols(session,base,"*TaskListButton*UpdateVisualStates*",[](PSYMBOL_INFO symbol,ULONG,void* data)->BOOL{
            if(std::string_view(symbol->Name)!="winrt::Taskbar::implementation::TaskListButton::UpdateVisualStates")return TRUE; auto r=static_cast<Result*>(data);r->offset=symbol->Address-r->base;Log(L"Resolved symbol "+std::wstring(to_hstring(symbol->Name)));return FALSE;
        },&result);
        offset=result.offset;SymCleanup(session);
    }CloseHandle(session);return offset;
}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,void*){if(reason==DLL_PROCESS_ATTACH){module=instance;DisableThreadLibraryCalls(instance);}return TRUE;}
