namespace xaml = winrt::Windows::UI::Xaml;
namespace controls = xaml::Controls;
namespace media = xaml::Media;
namespace slot = folder_menus_slot_lease;
namespace taskbar = folder_menus_taskbar_xaml;
using namespace winrt;

struct Snapshot {
    uint32_t magic = 0, version = 0, valid = 0, reserved = 0;
    double remaining = 0;
    int64_t reset = 0, observed = 0;
    int64_t resetCredits = -1, creditsObserved = 0;
};
static_assert(sizeof(Snapshot) == 56);
static HANDLE g_stop = nullptr, g_refresh = nullptr, g_worker = nullptr;
static std::atomic<bool> g_unloading{false};
static std::atomic<bool> g_attached{false};
static std::atomic<int> g_nativeWidth{0}, g_layoutGap{-999}, g_freshQuota{0};
struct Update { bool received = false; Snapshot snapshot; int32_t options[32]{}; std::vector<uint8_t> png; };
static int32_t g_options[32]{};
static std::atomic<bool> g_openOptions{false};
[[clang::no_destroy]] static controls::StackPanel g_numbers{nullptr}, g_caption{nullptr};
[[clang::no_destroy]] static controls::Image g_image{nullptr};
static Snapshot g_snapshot; // UI thread only; worker passes a value synchronously.
[[clang::no_destroy]] static controls::Panel g_parent{nullptr};
[[clang::no_destroy]] static controls::Grid g_content{nullptr};
[[clang::no_destroy]] static controls::TextBlock g_label{nullptr}, g_percent{nullptr}, g_credits{nullptr};
[[clang::no_destroy]] static controls::ToolTip g_tooltip{nullptr};
[[clang::no_destroy]] static xaml::Shapes::Path g_logo{nullptr};
[[clang::no_destroy]] static slot::Lease g_lease;
static event_token g_tapped{}, g_theme{};

static int64_t Now() {
    FILETIME ft; GetSystemTimeAsFileTime(&ft);
    ULARGE_INTEGER t; t.LowPart = ft.dwLowDateTime; t.HighPart = ft.dwHighDateTime;
    return static_cast<int64_t>(t.QuadPart / 10000000ULL - 11644473600ULL);
}

static xaml::FrameworkElement Find(xaml::DependencyObject root, PCWSTR name, int depth = 0) {
    if (!root || depth > 40) return nullptr;
    if (auto fe = root.try_as<xaml::FrameworkElement>(); fe && fe.Name() == name) return fe;
    const int count = media::VisualTreeHelper::GetChildrenCount(root);
    for (int i = 0; i < count; ++i)
        if (auto found = Find(media::VisualTreeHelper::GetChild(root, i), name, depth + 1)) return found;
    return nullptr;
}

static media::SolidColorBrush Brush(uint8_t r, uint8_t g, uint8_t b, uint8_t a = 255) {
    return media::SolidColorBrush(winrt::Windows::UI::Color{a, r, g, b});
}

static media::SolidColorBrush Rgb(int32_t c) { return Brush((c>>16)&255,(c>>8)&255,c&255); }

static void Configure(Update const& update) {
    if (update.options[0] != 1 || update.options[1] == g_options[1]) return;
    std::copy(std::begin(update.options),std::end(update.options),g_options);
    auto style = [](controls::TextBlock const& text, int offset) {
        text.FontSize(std::clamp(g_options[offset],8,20));
        text.FontWeight(winrt::Windows::UI::Text::FontWeight{static_cast<uint16_t>((g_options[offset+1]&2)?700:400)});
        text.FontStyle((g_options[offset+1]&4)?winrt::Windows::UI::Text::FontStyle::Italic:winrt::Windows::UI::Text::FontStyle::Normal);
        text.Visibility((g_options[offset+1]&1)?xaml::Visibility::Visible:xaml::Visibility::Collapsed);
    };
    style(g_label,2); style(g_percent,6); style(g_credits,10);
    bool visible = (g_options[15]&1) != 0, custom = (g_options[15]&2) != 0 && !update.png.empty();
    g_logo.Visibility(visible && !custom ? xaml::Visibility::Visible : xaml::Visibility::Collapsed);
    g_image.Visibility(visible && custom ? xaml::Visibility::Visible : xaml::Visibility::Collapsed);
    double size = std::clamp(g_options[14],12,32);
    g_logo.Width(size); g_logo.Height(size); g_image.Width(size); g_image.Height(size);
    if (custom) {
        winrt::com_ptr<IStream> memory;
        memory.attach(SHCreateMemStream(update.png.data(),static_cast<UINT>(update.png.size())));
        if (memory) {
            winrt::Windows::Storage::Streams::IRandomAccessStream stream{nullptr};
            winrt::check_hresult(CreateRandomAccessStreamOverStream(memory.get(),BSOS_DEFAULT,winrt::guid_of<winrt::Windows::Storage::Streams::IRandomAccessStream>(),winrt::put_abi(stream)));
            media::Imaging::BitmapImage bitmap; bitmap.SetSource(stream); g_image.Source(bitmap);
        }
    } else g_image.Source(nullptr);
}

static void Layout() {
    double maxHeight = std::max(24.,g_parent.ActualHeight()-4.);
    double lineGap = std::clamp(g_options[19],0,8);
    double labelSize = std::clamp(g_options[2],8,20), percentSize = std::clamp(g_options[6],8,20), creditSize = std::clamp(g_options[10],8,20);
    double wanted = ((g_options[3]&1)?labelSize*1.4:0) + std::max((g_options[7]&1)?percentSize*1.4:0,(g_options[11]&1)?creditSize*1.4:0)+lineGap;
    double scale = wanted > maxHeight ? maxHeight/wanted : 1.;
    g_label.FontSize(labelSize*scale); g_percent.FontSize(percentSize*scale); g_credits.FontSize(creditSize*scale);
    auto width = [](controls::TextBlock const& t) {
        if (t.Visibility() == xaml::Visibility::Collapsed) return 0.;
        t.Width(NAN); t.Measure({1000,100}); return std::ceil(t.DesiredSize().Width)+2.;
    };
    g_credits.Margin({0,0,0,0});
    double pw = width(g_percent), cw = width(g_credits), lw = width(g_label);
    double gap = pw && cw ? std::clamp(g_options[18],0,24) : 0;
    double row = pw+gap+cw, captionWidth = std::max(lw,row);
    g_percent.Width(pw); g_credits.Width(cw); g_credits.Margin({gap,0,0,0});
    g_label.Width((g_options[20] && pw) ? pw : captionWidth);
    g_label.HorizontalAlignment((g_options[20] && pw)?xaml::HorizontalAlignment::Left:xaml::HorizontalAlignment::Center);
    g_numbers.HorizontalAlignment(xaml::HorizontalAlignment::Center);
    g_numbers.Margin({0,(g_options[3]&1)?lineGap:0,0,0});
    double logoWidth = (g_options[15]&1) ? std::clamp(g_options[14],12,32) + (captionWidth?std::clamp(g_options[17],0,24):0) : 0;
    double total = std::max(24.,logoWidth+captionWidth);
    g_content.MinWidth(total); g_content.Width(total);
    g_content.ColumnDefinitions().GetAt(0).Width({logoWidth,xaml::GridUnitType::Pixel});
    g_content.ColumnDefinitions().GetAt(1).Width({captionWidth,xaml::GridUnitType::Pixel});
}

static void Render() {
    if (!g_content) return;
    auto now = Now();
    bool english = g_options[26] == 1;
    g_label.Text(english ? L"Weekly" : L"Hebdo");
    bool protocolValid = g_snapshot.magic == 0x31444843 && g_snapshot.version == 2;
    bool valid = protocolValid && g_snapshot.valid == 1
        && std::isfinite(g_snapshot.remaining) && g_snapshot.remaining >= 0 && g_snapshot.remaining <= 100
        && now >= g_snapshot.observed && now - g_snapshot.observed <= 75 && g_snapshot.reset > now
        && g_snapshot.reset - now <= 7 * 86400 + 300;
    bool light = g_content.ActualTheme() == xaml::ElementTheme::Light;
    g_freshQuota = valid ? 1 : 0;
    auto foreground = light ? Brush(40,40,40) : Brush(232,232,232);
    g_label.Foreground(foreground);
    g_logo.Fill(g_options[16] >= 0 ? Rgb(g_options[16]) : foreground);
    if (!(g_options[3] & 8)) g_label.Foreground(Rgb(g_options[4]));
    bool creditsValid = protocolValid && g_snapshot.resetCredits >= 0
        && now >= g_snapshot.creditsObserved && now - g_snapshot.creditsObserved <= 75;
    std::wstring creditTip = english ? L"\nResets available: " : L"\nRéinitialisations disponibles : ";
    if (creditsValid) {
        auto count = g_snapshot.resetCredits;
        g_credits.Text(L"\u21bb" + (count > 99 ? std::wstring(L"99+") : std::to_wstring(count)));
        g_credits.Foreground(Rgb((g_options[11] & 8) ? g_options[count == 0 ? 23 : 21] : g_options[12]));
        creditTip += std::to_wstring(count);
    } else {
        g_credits.Text(L"\u21bb--");
        g_credits.Foreground(light ? Brush(105,105,105) : Brush(165,165,165));
        creditTip += english ? L"unavailable" : L"information indisponible";
    }
    if (!valid) {
        g_percent.Text(L"--"); g_percent.Foreground(foreground);
        auto tip = std::wstring(english ? L"Weekly quota unavailable. Double-click for settings." : L"Quota hebdomadaire indisponible. Double-cliquez pour les options.") + creditTip;
        g_tooltip.Content(box_value(tip));
        xaml::Automation::AutomationProperties::SetName(g_content, tip);
        return;
    }
    const auto remaining = g_snapshot.remaining;
    auto color = Rgb((g_options[7] & 8) ? g_options[remaining > 50 ? 21 : remaining > 20 ? 22 : 23] : g_options[8]);
    wchar_t percentage[24]; swprintf_s(percentage, L"%.0f%%", std::floor(remaining));
    g_percent.Text(percentage); g_percent.Foreground(color);
    ULARGE_INTEGER t; t.QuadPart = (g_snapshot.reset + 11644473600ULL) * 10000000ULL;
    FILETIME utc{t.LowPart, t.HighPart}, local; SYSTEMTIME st;
    FileTimeToLocalFileTime(&utc, &local); FileTimeToSystemTime(&local, &st);
    wchar_t date[128], time[128], tip[320];
    GetDateFormatEx(english ? L"en-CA" : L"fr-CA", DATE_LONGDATE, &st, nullptr, date, 128, nullptr);
    GetTimeFormatEx(english ? L"en-CA" : L"fr-CA", 0, &st, nullptr, time, 128);
    swprintf_s(tip, english ? L"Weekly quota remaining: %.1f%%\nResets: %s at %s (local time)" : L"Quota hebdomadaire restant : %.1f%%\nRéinitialisation : %s à %s (heure locale)", remaining, date, time);
    auto fullTip = std::wstring(tip) + creditTip;
    g_tooltip.Content(box_value(fullTip));
    xaml::Automation::AutomationProperties::SetName(g_content, fullTip);
}

static void Remove() {
    g_attached = false;
    g_nativeWidth = 0; g_layoutGap = -999; g_freshQuota = 0;
    if (g_content) {
        g_content.DoubleTapped(g_tapped); g_content.ActualThemeChanged(g_theme);
        controls::ToolTipService::SetToolTip(g_content, nullptr);
        if (g_parent) { uint32_t index; if (g_parent.Children().IndexOf(g_content, index)) g_parent.Children().RemoveAt(index); }
    }
    if (g_parent) slot::Release(g_parent, g_lease);
    g_lease = {}; g_parent = nullptr; g_content = nullptr;
    g_label = nullptr; g_percent = nullptr; g_credits = nullptr; g_tooltip = nullptr;
    g_logo = nullptr; g_image = nullptr; g_numbers = nullptr; g_caption = nullptr;
}

static controls::TextBlock Text(PCWSTR text, int column, double width) {
    controls::TextBlock t;
    t.IsTextScaleFactorEnabled(false);
    t.Text(text); t.FontFamily(media::FontFamily(L"Segoe UI")); t.FontSize(12);
    t.Width(width); t.VerticalAlignment(xaml::VerticalAlignment::Center);
    controls::Grid::SetColumn(t, column);
    return t;
}

static void Apply(void* parameter) {
    if (g_unloading) return;
    auto& update = *static_cast<Update*>(parameter);
    // Transport contention must not erase a still-fresh reading. Render enforces the original TTL.
    if (update.received) g_snapshot = update.snapshot;
    auto window = folder_menus_taskbar_window::FindCurrentProcessTaskbarWnd();
    if (!window) return;
    auto root = taskbar::GetTaskbarXamlRoot(window);
    if (!root) return;
    auto parent = Find(root.Content(), L"SystemTrayFrameGrid").try_as<controls::Panel>();
    if (!parent || slot::Classify(parent) == slot::Kind::Unsupported || parent.ActualHeight() <= 0) return;
    if (auto stack = parent.try_as<controls::StackPanel>(); stack && stack.Orientation() != controls::Orientation::Horizontal) return;
    if (g_content) {
        uint32_t index;
        if (parent != g_parent || !parent.Children().IndexOf(g_content, index)) Remove();
    }
    if (!g_content) {
        controls::Grid content;
        content.Name(L"CodexHebdoNative"); content.Width(94); content.MinWidth(94);
        content.Margin({4,0,4,0}); content.VerticalAlignment(xaml::VerticalAlignment::Stretch);
        content.Background(Brush(0,0,0,0));
        for (double width : {28., 66.}) { controls::ColumnDefinition c; c.Width({width, xaml::GridUnitType::Pixel}); content.ColumnDefinitions().Append(c); }
        std::wstring logoXaml = L"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='";
        logoXaml += kLogoPath; logoXaml += L"'/>";
        g_logo = xaml::Markup::XamlReader::Load(logoXaml).as<xaml::Shapes::Path>();
        g_logo.Width(21); g_logo.Height(21); g_logo.Stretch(media::Stretch::Uniform);
        g_logo.HorizontalAlignment(xaml::HorizontalAlignment::Left);
        g_logo.VerticalAlignment(xaml::VerticalAlignment::Center);
        content.Children().Append(g_logo);
        g_image = controls::Image(); g_image.Stretch(media::Stretch::Uniform);
        g_image.HorizontalAlignment(xaml::HorizontalAlignment::Left);
        g_image.VerticalAlignment(xaml::VerticalAlignment::Center);
        content.Children().Append(g_image);
        g_label = Text(L"Hebdo",0,66); g_label.FontSize(10); g_label.TextAlignment(xaml::TextAlignment::Center);
        g_label.HorizontalAlignment(xaml::HorizontalAlignment::Center);
        g_percent = Text(L"--",0,42); g_percent.FontSize(12); g_percent.TextAlignment(xaml::TextAlignment::Center);
        g_credits = Text(L"\u21bb--",0,32); g_credits.FontSize(12);
        g_credits.Margin({-8,0,0,0});
        g_credits.TextAlignment(xaml::TextAlignment::Center);
        controls::StackPanel numbers; g_numbers = numbers;
        numbers.Orientation(controls::Orientation::Horizontal);
        numbers.Children().Append(g_percent); numbers.Children().Append(g_credits);
        controls::StackPanel caption; g_caption = caption;
        caption.Orientation(controls::Orientation::Vertical); caption.VerticalAlignment(xaml::VerticalAlignment::Center);
        caption.Children().Append(g_label); caption.Children().Append(numbers);
        controls::Grid::SetColumn(caption,1); content.Children().Append(caption);
        g_tooltip = controls::ToolTip(); controls::ToolTipService::SetToolTip(content,g_tooltip);
        content.IsDoubleTapEnabled(true);
        g_tapped = content.DoubleTapped([](auto const&, xaml::Input::DoubleTappedRoutedEventArgs const& e) {
            if (!g_unloading) { g_openOptions = true; SetEvent(g_refresh); } e.Handled(true);
        });
        g_theme = content.ActualThemeChanged([](auto const&, auto const&) { try { if (!g_unloading) Render(); } catch (...) { } });
        // The first tray slot is immediately before the native notification chevron.
        if (!slot::AcquireAt(parent,0,L"CodexHebdoColumnLease",g_lease)) return;
        try {
            if (!slot::PlaceChild(parent,g_lease,content)) { slot::Release(parent,g_lease); return; }
        } catch (...) { slot::Release(parent,g_lease); throw; }
        g_parent = parent; g_content = content; g_options[1] = 0;
        Wh_Log(L"Native quota inserted (%s)", parent.try_as<controls::Grid>() ? L"Grid" : L"StackPanel");
    }
    // Some builds restore native Grid.Column bindings during a layout change.
    if (auto grid = parent.try_as<controls::Grid>()) {
        const int column = controls::Grid::GetColumn(g_content);
        for (auto child : grid.Children()) {
            auto element = child.try_as<xaml::FrameworkElement>();
            if (element && element != g_content && element.Name() != L"CodexHebdoColumnLease"
                && controls::Grid::GetColumn(element) == column) controls::Grid::SetColumn(element,column+1);
        }
    }
    Configure(update);
    Render();
    Layout();
    g_attached = true;
    g_nativeWidth = static_cast<int>(std::lround(g_content.ActualWidth()));
    if (g_content.ActualWidth() > 0) {
        auto origin = g_content.TransformToVisual(parent).TransformPoint({0,0});
        double nearest = 1e9;
        for (auto const& child : parent.Children()) {
            auto fe = child.try_as<xaml::FrameworkElement>();
            if (!fe || fe == g_content || fe.Name() == L"CodexHebdoColumnLease"
                || fe.Visibility() == xaml::Visibility::Collapsed || fe.ActualWidth() <= 0) continue;
            double left = fe.TransformToVisual(parent).TransformPoint({0,0}).X;
            nearest = std::min(nearest,left);
        }
        if (nearest < 1e9) g_layoutGap = static_cast<int>(std::lround(nearest - origin.X - g_content.ActualWidth()));
    }
}

static std::wstring PipeName() {
    HANDLE token;
    if (!OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&token)) return {};
    DWORD size = 0; GetTokenInformation(token,TokenUser,nullptr,0,&size);
    std::vector<BYTE> data(size);
    if (!GetTokenInformation(token,TokenUser,data.data(),size,&size)) { CloseHandle(token); return {}; }
    CloseHandle(token);
    LPWSTR sid = nullptr;
    if (!ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(data.data())->User.Sid,&sid)) return {};
    DWORD session = 0; ProcessIdToSessionId(GetCurrentProcessId(),&session);
    std::wstring name = L"\\\\.\\pipe\\CodexHebdo-v1-" + std::wstring(sid) + L"-" + std::to_wstring(session);
    LocalFree(sid); return name;
}

// All pipe reads/writes are OVERLAPPED, exclusively on the worker thread.
// The taskbar thread receives only a small in-memory value, never an I/O operation.
static bool TransferPart(HANDLE pipe, void* buffer, DWORD size, bool write, DWORD& done) {
    OVERLAPPED op{}; op.hEvent = CreateEventW(nullptr,TRUE,FALSE,nullptr);
    if (!op.hEvent) return false;
    done = 0;
    BOOL ok = write ? WriteFile(pipe,buffer,size,&done,&op) : ReadFile(pipe,buffer,size,&done,&op);
    if (!ok && GetLastError() == ERROR_IO_PENDING) {
        HANDLE waits[]{g_stop,op.hEvent};
        if (WaitForMultipleObjects(2,waits,FALSE,750) != WAIT_OBJECT_0+1) CancelIoEx(pipe,&op);
        // Cancellation must finish before the OVERLAPPED or its buffer leaves scope.
        ok = GetOverlappedResult(pipe,&op,&done,TRUE);
    }
    CloseHandle(op.hEvent); return ok && done > 0;
}

static bool Transfer(HANDLE pipe, void* buffer, DWORD size, bool write) {
    DWORD offset = 0;
    while (offset < size) { DWORD done; if (!TransferPart(pipe,static_cast<uint8_t*>(buffer)+offset,size-offset,write,done)) return false; offset += done; }
    return true;
}

static DWORD WINAPI Worker(void*) {
    auto name = PipeName();
    bool reported = false;
    int width = -1, gap = -1000, fresh = -1;
    while (WaitForSingleObject(g_stop,0) != WAIT_OBJECT_0) {
        Update update;
        auto pipe = CreateFileW(name.c_str(),GENERIC_READ|GENERIC_WRITE,0,nullptr,OPEN_EXISTING,
            FILE_FLAG_OVERLAPPED|SECURITY_SQOS_PRESENT|SECURITY_IDENTIFICATION,nullptr);
        if (pipe != INVALID_HANDLE_VALUE) {
            char command = g_openOptions.exchange(false) ? 'U' : 'V';
            if (!Transfer(pipe,&command,1,true)) update = {};
            else {
                if (!Transfer(pipe,&update.snapshot,sizeof(Snapshot),false)
                    || !Transfer(pipe,update.options,sizeof(update.options),false)
                    || update.options[0] != 1 || update.options[25] < 0 || update.options[25] > 131072) update = {};
                else if (update.options[25]) {
                    update.png.resize(update.options[25]);
                    if (!Transfer(pipe,update.png.data(),static_cast<DWORD>(update.png.size()),false)) update = {};
                }
                update.received = update.options[0] == 1;
            }
            CloseHandle(pipe);
        }
        if (auto window = folder_menus_taskbar_window::FindCurrentProcessTaskbarWnd())
            dispatch::RunFromWindowThread(window,Apply,&update,L"CodexHebdo.Dispatch." WH_MOD_ID);
        if (bool attached = g_attached.load(); attached != reported) {
            // Small lifecycle diagnostic, written only on changes and outside the UI thread.
            Wh_SetIntValue(L"NativeAttached", attached ? 1 : 0);
            reported = attached;
        }
        if (int value = g_nativeWidth.load(); value != width) { Wh_SetIntValue(L"NativeWidthDip",value); width=value; }
        if (int value = g_layoutGap.load(); value != gap) { Wh_SetIntValue(L"LayoutGapDip",value); gap=value; }
        if (int value = g_freshQuota.load(); value != fresh) { Wh_SetIntValue(L"FreshQuota",value); fresh=value; }
        HANDLE waits[]{g_stop,g_refresh};
        DWORD result = WaitForMultipleObjects(2,waits,FALSE,2000);
        if (result == WAIT_OBJECT_0) break;

    }
    Wh_SetIntValue(L"NativeAttached",0);
    return 0;
}

BOOL Wh_ModInit() {
    g_unloading = false;
    dispatch::SetExceptionLogger([](PCWSTR context) { Wh_Log(L"XAML unavailable: %s",context); });
    if (!taskbar::HookTaskbarSymbols([] { if (g_refresh && !g_unloading) SetEvent(g_refresh); })) return FALSE;
    g_stop = CreateEventW(nullptr,TRUE,FALSE,nullptr);
    g_refresh = CreateEventW(nullptr,FALSE,FALSE,nullptr);
    if (!g_stop || !g_refresh) { if(g_stop) CloseHandle(g_stop); if(g_refresh) CloseHandle(g_refresh); return FALSE; }
    return TRUE;
}

void Wh_ModAfterInit() { g_worker = CreateThread(nullptr,0,Worker,nullptr,0,nullptr); }

void Wh_ModUninit() {
    g_unloading = true;
    SetEvent(g_stop);
    if (g_worker) { WaitForSingleObject(g_worker,INFINITE); CloseHandle(g_worker); g_worker = nullptr; }
    if (auto window = folder_menus_taskbar_window::FindCurrentProcessTaskbarWnd())
        dispatch::RunFromWindowThread(window,[](void*) { Remove(); },nullptr,L"CodexHebdo.Dispatch." WH_MOD_ID);
    CloseHandle(g_stop); CloseHandle(g_refresh); g_stop = nullptr; g_refresh = nullptr;
}
