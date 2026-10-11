// ==WindhawkMod==
// @id              codex-hebdo-taskbar
// @name            Codex Hebdo
// @description     Quota hebdomadaire Codex restant, intégré à la disposition native Windows 11.
// @version         1.2.2
// @author          Codex Hebdo contributors
// @include         explorer.exe
// @architecture    amd64
// @compilerOptions -lole32 -loleaut32 -lruntimeobject -luuid -ladvapi32 -lshlwapi -lshcore
// ==/WindhawkMod==

// ==WindhawkModReadme==
/*
# Codex Hebdo
Installer et démarrer le compagnon CodexHebdo.exe avant d'activer ce mod.
Le compteur réserve une place native à gauche du chevron de notification.
Double-clic uniquement : options et actualisation. Survol : quota restant et réinitialisation locale.
Sans données récentes : Hebdo --. Aucun quota de session n'est affiché.
Windows 11 x64, barre horizontale principale. Désactiver le mod restaure la disposition.

Helpers de disposition et d'accès XAML adaptés de Taskbar Folder Menus 2.1
par sb4ssman (MIT), dépôt ramensoftware/windhawk-mods.
Voir THIRD-PARTY-NOTICES.txt dans la distribution.
*/
// ==/WindhawkModReadme==

#include <windows.h>
#include <sddl.h>
#include <shlwapi.h>
#include <shcore.h>
#undef GetCurrentTime
#include <winrt/Windows.Storage.Streams.h>
#include <winrt/Windows.UI.Text.h>
#include <winrt/Windows.UI.Xaml.Media.Imaging.h>
#include <windhawk_utils.h>
#undef GetCurrentTime
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Controls.Primitives.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <winrt/Windows.UI.Xaml.Input.h>
#include <winrt/Windows.UI.Xaml.Markup.h>
#include <winrt/Windows.UI.Xaml.Shapes.h>
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <string>
#include <vector>


/*
The layout lease, taskbar discovery, UI dispatch, and XamlRoot helpers in
Windhawk/helpers.inc are adapted from taskbar-folder-menus.wh.cpp 2.1 by
sb4ssman, obtained from ramensoftware/windhawk-mods on 2026-10-11.
The repository README specifies MIT for mods without another declared license.
Changes include fail-closed offset detection and offline-only symbol resolution.

MIT License

Copyright (c) 2026 sb4ssman and contributors
Copyright (c) 2026 Codex Hebdo contributors (new application code)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

Assets/lolo.svg was supplied by the user. The OpenAI logo and associated
trademarks remain the property of their respective owner; the code license
does not grant trademark rights. This is an independent local utility.

Windhawk is a separate dependency downloaded from its official distribution;
its license and third-party notices remain applicable. Its binaries are not
included in the Codex Hebdo project archive.

Other references studied, without copying application code:
rate-tray: 186528c061ed7fa8ac5a33c30c6eac9fd113d098
CodexQuota: 3e1822713f2f7d1b4a3124b2f485324c5fcdf070

*/

// -- Tray slot lease --------------------------------------------------------
// Lease a position in the tray panel - a column on a Grid, a child index on
// a StackPanel - tracked by a named zero-size marker so the release stays
// exact after other mods inject siblings around it.
namespace folder_menus_slot_lease {

using winrt::Windows::UI::Xaml::FrameworkElement;
using winrt::Windows::UI::Xaml::GridUnitType;
using winrt::Windows::UI::Xaml::Controls::ColumnDefinition;
using winrt::Windows::UI::Xaml::Controls::Grid;
using winrt::Windows::UI::Xaml::Controls::Panel;
using winrt::Windows::UI::Xaml::Controls::StackPanel;

enum class Anchor {
    BeforeIcons,
    BeforeOmni,
    BeforeClock,
    AfterClock,
    AfterShowDesktop,
};

// Which layout contract the live tray panel follows.
enum class Kind {
    Unsupported,  // some other Panel: do not guess its layout semantics.
    Columns,      // Grid. A slot is a column index.
    Order,        // StackPanel. A slot is a child index.
};

struct Lease {
    std::wstring markerName;
    int slot = -1;
    Kind kind = Kind::Unsupported;
};

inline Kind Classify(FrameworkElement const& parent) {
    if (!parent)
        return Kind::Unsupported;
    if (parent.try_as<Grid>())
        return Kind::Columns;
    if (parent.try_as<StackPanel>())
        return Kind::Order;
    return Kind::Unsupported;
}

// Class name of an unexpected panel, so a mod can log what it actually got
// instead of reporting a bare "not found" for an element that is right there.
inline std::wstring ClassName(FrameworkElement const& element) {
    if (!element)
        return L"(null)";
    try {
        return std::wstring(winrt::get_class_name(element));
    } catch (...) {
        return L"(unknown)";
    }
}

// Named direct children, for logging when an anchor cannot be resolved. A tray
// restructure shows up here as missing or renamed names, which is the one thing
// a user's debug log otherwise cannot tell us.
inline std::wstring DescribeChildren(Panel const& parent) {
    if (!parent)
        return L"(none)";
    std::wstring names;
    try {
        for (auto const& child : parent.Children()) {
            auto element = child.try_as<FrameworkElement>();
            if (!element || element.Name().empty())
                continue;
            if (!names.empty())
                names += L", ";
            names += element.Name();
        }
    } catch (...) {
        return L"(unreadable)";
    }
    return names.empty() ? L"(no named children)" : names;
}

inline FrameworkElement FindDirectChild(Panel const& parent,
                                        wchar_t const* name) {
    if (!parent || !name)
        return nullptr;
    for (auto const& child : parent.Children()) {
        auto element = child.try_as<FrameworkElement>();
        if (element && element.Name() == name)
            return element;
    }
    return nullptr;
}

inline int IndexOfChild(Panel const& parent, FrameworkElement const& child) {
    if (!parent || !child)
        return -1;
    for (uint32_t i = 0; i < parent.Children().Size(); ++i) {
        if (parent.Children().GetAt(i).try_as<FrameworkElement>() == child)
            return static_cast<int>(i);
    }
    return -1;
}

inline bool ResolveSlot(Panel const& parent, Anchor anchor, int& slot) {
    Kind kind = Classify(parent);
    if (kind == Kind::Unsupported)
        return false;

    if (anchor == Anchor::BeforeIcons) {
        slot = 0;
        return true;
    }

    wchar_t const* referenceName = nullptr;
    bool after = false;
    switch (anchor) {
        case Anchor::BeforeOmni:
            referenceName = L"ControlCenterButton";
            break;
        case Anchor::BeforeClock:
            referenceName = L"NotificationCenterButton";
            break;
        case Anchor::AfterClock:
            referenceName = L"ShowDesktopStack";
            break;
        case Anchor::AfterShowDesktop:
            referenceName = L"ShowDesktopStack";
            after = true;
            break;
        case Anchor::BeforeIcons:
            break;
    }

    auto reference = FindDirectChild(parent, referenceName);
    if (!reference)
        return false; // Never silently turn an unavailable anchor into slot 0.

    if (kind == Kind::Columns) {
        slot = Grid::GetColumn(reference) +
               (after ? std::max(1, Grid::GetColumnSpan(reference)) : 0);
        return true;
    }

    // Order: the reference's own child index is the slot. There is no span to
    // step over -- a StackPanel child occupies exactly one position.
    int index = IndexOfChild(parent, reference);
    if (index < 0)
        return false;
    slot = index + (after ? 1 : 0);
    return true;
}

inline bool Release(Panel const& parent, Lease& lease);

inline bool AcquireAt(Panel const& parent, int slot,
                      std::wstring const& markerName, Lease& lease) {
    Kind kind = Classify(parent);
    if (kind == Kind::Unsupported || slot < 0 || markerName.empty())
        return false;

    // A marker with this name already in the tray is either this caller's own
    // live lease - refuse, acquiring twice would strand the first slot - or a
    // leftover from an instance whose teardown never reached the UI thread.
    // Refusing the leftover would block every later apply until Explorer
    // restarts, so release it and take the slot fresh, correcting the
    // requested slot if the released one sat before it.
    if (auto stale = FindDirectChild(parent, markerName.c_str())) {
        if (lease.markerName == markerName)
            return false;
        int staleSlot = kind == Kind::Columns
                            ? Grid::GetColumn(stale)
                            : IndexOfChild(parent, stale);
        Lease leftover{markerName, staleSlot, kind};
        if (!Release(parent, leftover))
            return false;
        if (staleSlot >= 0 && staleSlot < slot)
            --slot;
    }

    Grid marker;
    marker.Name(markerName);
    marker.Width(0.0);
    marker.Height(0.0);
    marker.IsHitTestVisible(false);

    if (kind == Kind::Columns) {
        auto grid = parent.try_as<Grid>();
        if (!grid)
            return false;
        ColumnDefinition definition;
        definition.Width({1.0, GridUnitType::Auto});
        if (static_cast<uint32_t>(slot) < grid.ColumnDefinitions().Size())
            grid.ColumnDefinitions().InsertAt(slot, definition);
        else
            grid.ColumnDefinitions().Append(definition);

        for (auto const& child : grid.Children()) {
            auto element = child.try_as<FrameworkElement>();
            if (!element) continue;
            int start = Grid::GetColumn(element);
            int span = Grid::GetColumnSpan(element);
            if (start >= slot)
                Grid::SetColumn(element, start + 1);
            else if (start + span > slot)
                Grid::SetColumnSpan(element, span + 1);
        }

        Grid::SetColumn(marker, slot);
        grid.Children().Append(marker);
    } else {
        // Order: no column is created or owned. The marker simply holds the
        // position, and PlaceChild drops the content next to it.
        uint32_t index = std::min(static_cast<uint32_t>(slot),
                                  parent.Children().Size());
        parent.Children().InsertAt(index, marker);
        slot = static_cast<int>(index);
    }

    lease = {markerName, slot, kind};
    return true;
}

// Live index of the lease marker. Other mods inject and remove siblings around
// us, so the acquire-time index is a hint, never the truth at removal time.
inline bool FindMarker(Panel const& parent, Lease const& lease,
                       uint32_t& index) {
    if (!parent || lease.markerName.empty())
        return false;
    for (uint32_t i = 0; i < parent.Children().Size(); ++i) {
        auto element = parent.Children().GetAt(i).try_as<FrameworkElement>();
        if (element && element.Name() == lease.markerName) {
            index = i;
            return true;
        }
    }
    return false;
}

// Put mod content into the leased slot. On a Grid the content joins the leased
// column; on a StackPanel it is inserted directly after the marker, so the
// marker's position is the content's position.
inline bool PlaceChild(Panel const& parent, Lease const& lease,
                       FrameworkElement const& content) {
    if (!parent || !content || lease.kind == Kind::Unsupported)
        return false;

    uint32_t markerIndex = 0;
    if (!FindMarker(parent, lease, markerIndex))
        return false;

    if (lease.kind == Kind::Columns) {
        auto marker = parent.Children().GetAt(markerIndex)
                          .try_as<FrameworkElement>();
        Grid::SetColumn(content, marker ? Grid::GetColumn(marker) : lease.slot);
        parent.Children().Append(content);
        return true;
    }

    parent.Children().InsertAt(markerIndex + 1, content);
    return true;
}

inline bool Release(Panel const& parent, Lease& lease) {
    if (!parent || lease.markerName.empty())
        return false;

    uint32_t markerIndex = 0;
    if (!FindMarker(parent, lease, markerIndex))
        return false;

    if (lease.kind == Kind::Order) {
        parent.Children().RemoveAt(markerIndex);
        lease = {};
        return true;
    }

    auto grid = parent.try_as<Grid>();
    auto marker =
        parent.Children().GetAt(markerIndex).try_as<FrameworkElement>();
    int liveColumn = marker ? Grid::GetColumn(marker) : lease.slot;
    if (!grid || liveColumn < 0)
        return false;

    grid.Children().RemoveAt(markerIndex);
    if (static_cast<uint32_t>(liveColumn) < grid.ColumnDefinitions().Size())
        grid.ColumnDefinitions().RemoveAt(liveColumn);

    for (auto const& child : grid.Children()) {
        auto element = child.try_as<FrameworkElement>();
        if (!element) continue;
        int start = Grid::GetColumn(element);
        int span = Grid::GetColumnSpan(element);
        if (start > liveColumn)
            Grid::SetColumn(element, start - 1);
        else if (start < liveColumn && start + span > liveColumn)
            Grid::SetColumnSpan(element, std::max(1, span - 1));
    }

    lease = {};
    return true;
}

}  // namespace folder_menus_slot_lease

// -- Taskbar window discovery -----------------------------------------------
// Find this process's Shell_TrayWnd, and validate a cached handle before
// preferring it.
namespace folder_menus_taskbar_window {

// ---- Window discovery -------------------------------------------------------

inline HWND FindCurrentProcessTaskbarWnd() {
    HWND result = nullptr;
    EnumWindows(
        [](HWND window, LPARAM parameter) -> BOOL {
            DWORD processId = 0;
            WCHAR className[32];
            if (GetWindowThreadProcessId(window, &processId) &&
                processId == GetCurrentProcessId() &&
                GetClassName(window, className, ARRAYSIZE(className)) &&
                _wcsicmp(className, L"Shell_TrayWnd") == 0) {
                *reinterpret_cast<HWND*>(parameter) = window;
                return FALSE;
            }
            return TRUE;
        },
        reinterpret_cast<LPARAM>(&result));
    return result;
}

// Shell_TrayWnd can be recreated inside Explorer. A cache is useful only while
// it names a live window; otherwise rediscover before dispatch or teardown.
inline HWND ResolveTaskbarWnd(HWND cached) {
    if (cached && IsWindow(cached))
        return cached;
    return FindCurrentProcessTaskbarWnd();
}

}  // namespace folder_menus_taskbar_window

// -- UI-thread dispatch -----------------------------------------------------
// Marshal a callback onto the taskbar's UI thread with a CALLWNDPROC hook
// and a private registered message, reporting whether it actually ran.
namespace folder_menus_dispatch {

// ---- UI-thread marshalling --------------------------------------------------
//
// XAML may only be touched from the thread that owns it. This posts work onto
// the taskbar's thread with a CALLWNDPROC hook and a private registered
// message, and reports whether the callback actually ran - a caller that
// assumes it did will corrupt its own state when the dispatch failed.

using ThreadProc = void (*)(void*);
using ExceptionLogFn = void (*)(PCWSTR context);

inline ExceptionLogFn g_logException = nullptr;

// Point this at the mod's logger once in Wh_ModInit so failures inside a UI
// callback are reported in the mod's own voice.
inline void SetExceptionLogger(ExceptionLogFn logger) {
    g_logException = logger;
}

inline bool Invoke(ThreadProc proc, void* parameter) {
    try {
        proc(parameter);
        return true;
    } catch (...) {
        if (g_logException) g_logException(L"UI callback");
    }
    return false;
}

struct Dispatch {
    ThreadProc proc;
    void* parameter;
    bool succeeded = false;
    // Every concurrent caller installs its own hook with this same proc, and
    // each hook instance sees every message equal to g_dispatchMessage. With
    // two dispatches in flight, both hooks are in the chain when either
    // message arrives, so without this each callback would run twice. The
    // hooks run one after another on the UI thread, so a plain flag suffices.
    bool ran = false;
};

// The private message this mod dispatches on. Set before the hook is
// installed, and read by the hook proc to recognise its own message.
//
// A CALLWNDPROC HOOK SEES EVERY MESSAGE SENT TO EVERY WINDOW ON THE TASKBAR'S
// UI THREAD. `lParam` for all of those is arbitrary - an integer, a flag, a
// pointer to something else entirely. So the message MUST be checked first,
// against a value that does not come from lParam, and only then may lParam be
// treated as a Dispatch*. Reading anything out of lParam before that check
// dereferences whatever happened to be in the message and takes Explorer down
// with it.
//
// Atomic because the caller may be the retry thread while the hook
// proc runs on the taskbar's UI thread. RegisterWindowMessageW returns the
// same value for the same string for the lifetime of the session, so this
// settles on one value immediately and never changes again.
inline std::atomic<UINT> g_dispatchMessage{0};

// messageName must embed WH_MOD_ID, so two mods cannot collide on the message.
inline bool RunFromWindowThread(HWND window, ThreadProc proc, void* parameter,
                                PCWSTR messageName) {
    UINT message = RegisterWindowMessageW(messageName);
    if (!message) return false;

    DWORD threadId = GetWindowThreadProcessId(window, nullptr);
    if (!threadId) return false;
    if (threadId == GetCurrentThreadId()) return Invoke(proc, parameter);

    g_dispatchMessage.store(message, std::memory_order_release);

    HHOOK hook = SetWindowsHookExW(
        WH_CALLWNDPROC,
        [](int code, WPARAM wParam, LPARAM lParam) -> LRESULT {
            if (code == HC_ACTION) {
                auto const* call = reinterpret_cast<CWPSTRUCT const*>(lParam);
                // Message first. Only our own private message carries a
                // Dispatch* in lParam; everything else carries something we
                // must not touch.
                UINT expected =
                    g_dispatchMessage.load(std::memory_order_acquire);
                if (expected && call->message == expected) {
                    if (auto* dispatch =
                            reinterpret_cast<Dispatch*>(call->lParam);
                        dispatch && !dispatch->ran) {
                        dispatch->ran = true;
                        dispatch->succeeded =
                            Invoke(dispatch->proc, dispatch->parameter);
                    }
                }
            }
            return CallNextHookEx(nullptr, code, wParam, lParam);
        },
        nullptr, threadId);
    if (!hook) return false;

    Dispatch dispatch{proc, parameter};
    SendMessageW(window, message, 0, reinterpret_cast<LPARAM>(&dispatch));
    UnhookWindowsHookEx(hook);
    return dispatch.succeeded;
}

}  // namespace folder_menus_dispatch

// -- Taskbar XamlRoot -------------------------------------------------------
// Hook the taskbar.dll symbols, reach the taskbar's XamlRoot, and call back
// when Explorer rebuilds the taskbar in place.
namespace dispatch = folder_menus_dispatch;
namespace folder_menus_taskbar_xaml {

using winrt::Windows::UI::Xaml::FrameworkElement;
using winrt::Windows::UI::Xaml::XamlRoot;

// ---- XamlRoot ---------------------------------------------------------------

using CTaskBand_GetTaskbarHost_t = void*(WINAPI*)(void*, void*);
using TaskbarHost_FrameHeight_t = int(WINAPI*)(void*);
using Ref_count_base_Decref_t = void(WINAPI*)(void*);
using TrayUI_StartTaskbar_t = void(WINAPI*)(void*);

inline CTaskBand_GetTaskbarHost_t CTaskBand_GetTaskbarHost_Original = nullptr;
inline TaskbarHost_FrameHeight_t TaskbarHost_FrameHeight_Original = nullptr;
inline Ref_count_base_Decref_t Ref_count_base_Decref_Original = nullptr;
inline TrayUI_StartTaskbar_t TrayUI_StartTaskbar_Original = nullptr;
inline void* CTaskBand_ITaskListWndSite_vftable = nullptr;

// The mod's rebuild callback, invoked after Explorer rebuilds the taskbar.
inline void (*g_onTaskbarRebuilt)() = nullptr;

inline void WINAPI TrayUI_StartTaskbar_Hook(void* self) {
    TrayUI_StartTaskbar_Original(self);
    try {
        if (g_onTaskbarRebuilt) g_onTaskbarRebuilt();
    } catch (...) {
        if (dispatch::g_logException)
            dispatch::g_logException(L"TrayUI::StartTaskbar hook");
    }
}

inline bool HookTaskbarSymbols(void (*onTaskbarRebuilt)()) {
    g_onTaskbarRebuilt = onTaskbarRebuilt;
    HMODULE taskbar = LoadLibraryExW(L"taskbar.dll", nullptr,
                                     LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!taskbar) return false;
    WindhawkUtils::SYMBOL_HOOK taskbarDllHooks[] = {
        {{LR"(const CTaskBand::`vftable'{for `ITaskListWndSite'})"},
         &CTaskBand_ITaskListWndSite_vftable},
        {{LR"(public: virtual class std::shared_ptr<class TaskbarHost> __cdecl CTaskBand::GetTaskbarHost(void)const )"},
         &CTaskBand_GetTaskbarHost_Original},
        {{LR"(public: int __cdecl TaskbarHost::FrameHeight(void)const )"},
         &TaskbarHost_FrameHeight_Original},
        {{LR"(public: void __cdecl std::_Ref_count_base::_Decref(void))"},
         &Ref_count_base_Decref_Original},
        {{LR"(public: virtual void __cdecl TrayUI::StartTaskbar(void))"},
         &TrayUI_StartTaskbar_Original, TrayUI_StartTaskbar_Hook},
    };
    // Symbols are provisioned by the installer outside Explorer. Never let
    // symbol resolution contact an online cache or a symbol server here.
    WH_HOOK_SYMBOLS_OPTIONS options{sizeof(options), L"", FALSE, L""};
    return WindhawkUtils::HookSymbols(taskbar, taskbarDllHooks,
                                      ARRAYSIZE(taskbarDllHooks), &options);
}

// The FrameworkElement lives at an offset inside TaskbarHost that MOVES
// between Windows builds, so it is read out of TaskbarHost::FrameHeight's
// prologue at runtime rather than hardcoded.
inline size_t FrameworkElementOffset() {
    size_t offset = 0; // Unknown prologue: refuse integration instead of guessing an object offset.
#if defined(_M_X64)
    BYTE const* code =
        reinterpret_cast<BYTE const*>(TaskbarHost_FrameHeight_Original);
    if (code[0] == 0x48 && code[1] == 0x83 && code[2] == 0xEC &&
        code[4] == 0x48 && code[5] == 0x83 && code[6] == 0xC1 &&
        code[7] <= 0x7F) {
        offset = code[7];
    }
#elif defined(_M_ARM64)
    DWORD const* code =
        reinterpret_cast<DWORD const*>(TaskbarHost_FrameHeight_Original);
    if (code[0] == 0xD503237F && (code[1] & 0xFFC07FFF) == 0xA9807BFD &&
        code[2] == 0x910003FD && (code[3] & 0xFFF00FE0) == 0xF8400C00) {
        offset = (code[3] >> 12) & 0xFF;
    }
#else
#error "Unsupported architecture"
#endif
    return offset;
}

inline XamlRoot GetTaskbarXamlRoot(HWND taskbarWnd) {
    if (!CTaskBand_GetTaskbarHost_Original ||
        !TaskbarHost_FrameHeight_Original || !Ref_count_base_Decref_Original ||
        !CTaskBand_ITaskListWndSite_vftable)
        return nullptr;

    HWND taskSwWnd = (HWND)GetProp(taskbarWnd, L"TaskbandHWND");
    if (!taskSwWnd) return nullptr;
    void* taskBand = (void*)GetWindowLongPtr(taskSwWnd, 0);
    if (!taskBand) return nullptr;

    void* site = taskBand;
    for (int i = 0; *(void**)site != CTaskBand_ITaskListWndSite_vftable; ++i) {
        if (i == 20) return nullptr;
        site = (void**)site + 1;
    }

    void* host[2]{};
    CTaskBand_GetTaskbarHost_Original(site, host);
    if (!host[0] || !host[1]) {
        if (host[1]) Ref_count_base_Decref_Original(host[1]);
        return nullptr;
    }

    size_t offset = FrameworkElementOffset();
    if (!offset) {
        Ref_count_base_Decref_Original(host[1]);
        return nullptr;
    }
    auto* unknown = *(IUnknown**)((BYTE*)host[0] + offset);
    if (!unknown) {
        Ref_count_base_Decref_Original(host[1]);
        return nullptr;
    }
    FrameworkElement element = nullptr;
    unknown->QueryInterface(winrt::guid_of<FrameworkElement>(),
                            winrt::put_abi(element));
    auto result = element ? element.XamlRoot() : nullptr;
    Ref_count_base_Decref_Original(host[1]);
    return result;
}

}  // namespace folder_menus_taskbar_xaml

// SVG commands expanded to absolute coordinates to preserve SVG subpath semantics in XAML.
static constexpr wchar_t kLogoPath[] = LR"(F1 M 11.248 18.25 Q 10.423 18.25 9.68 17.936 A 4.3 4.3 0 0 1 8.36 17.062 A 4 4 0 0 1 7.056 17.276 A 4 4 0 0 1 5.01 16.732 A 4.27 4.27 0 0 1 3.492 15.247 A 4 4 0 0 1 2.932 13.152 Q 2.932 12.672 3.063 12.112 A 4.4 4.4 0 0 1 2.04 10.71 A 4.07 4.07 0 0 1 2.057 7.31 A 4.2 4.2 0 0 1 3.113 5.892 A 3.8 3.8 0 0 1 4.713 5.05 A 3.9 3.9 0 0 1 5.473 3.367 Q 6.066 2.608 6.924 2.179 A 4.04 4.04 0 0 1 8.756 1.75 Q 9.581 1.75 10.323 2.063 Q 11.065 2.377 11.643 2.938 A 4 4 0 0 1 12.947 2.723 Q 14.053 2.723 14.993 3.268 A 4.14 4.14 0 0 1 16.494 4.753 Q 17.072 5.694 17.072 6.848 Q 17.072 7.328 16.94 7.888 Q 17.6 8.498 17.963 9.307 Q 18.326 10.099 18.326 10.973 Q 18.326 11.865 17.946 12.69 A 4.3 4.3 0 0 1 16.874 14.125 A 3.8 3.8 0 0 1 15.29 14.95 A 3.8 3.8 0 0 1 14.515 16.633 A 4.06 4.06 0 0 1 13.079 17.821 A 4.04 4.04 0 0 1 11.247 18.25 M 7.171 16.188 Q 7.996 16.188 8.606 15.841 L 11.709 14.059 A 0.36 0.36 0 0 0 11.873 13.746 L 11.873 12.326 L 7.881 14.62 A 0.67 0.67 0 0 1 7.155 14.62 L 4.037 12.822 A 0.5 0.5 0 0 1 4.02 12.937 L 4.02 13.135 Q 4.02 13.976 4.416 14.686 Q 4.829 15.379 5.555 15.775 A 3.2 3.2 0 0 0 7.172 16.187 M 7.337 13.497 A 0.4 0.4 0 0 0 7.518 13.547 Q 7.601 13.547 7.683 13.497 L 8.921 12.787 L 4.944 10.477 A 0.7 0.7 0 0 1 4.581 9.834 L 4.581 6.254 Q 3.756 6.616 3.261 7.376 A 2.9 2.9 0 0 0 2.766 9.026 Q 2.766 9.835 3.179 10.576 Q 3.591 11.319 4.251 11.699 Z  M 11.247 17.16 Q 12.122 17.16 12.832 16.764 A 2.96 2.96 0 0 0 14.366 14.124 L 14.366 10.56 A 0.32 0.32 0 0 0 14.201 10.263 L 12.947 9.537 L 12.947 14.141 A 0.7 0.7 0 0 1 12.584 14.784 L 9.465 16.583 A 3 3 0 0 0 11.248 17.16 M 11.875 11.121 L 11.875 8.878 L 10.01 7.822 L 8.129 8.878 L 8.129 11.122 L 10.01 12.178 Z  M 7.057 5.859 A 0.7 0.7 0 0 1 7.42 5.215 L 10.539 3.417 A 3 3 0 0 0 8.757 2.839 Q 7.883 2.839 7.173 3.235 A 2.96 2.96 0 0 0 6.05 4.324 A 3.07 3.07 0 0 0 5.654 5.875 L 5.654 9.422 Q 5.654 9.621 5.819 9.736 L 7.056 10.462 Z  M 15.44 13.746 Q 16.265 13.382 16.743 12.623 Q 17.238 11.865 17.238 10.973 A 3.15 3.15 0 0 0 16.826 9.423 Q 16.413 8.68 15.753 8.3 L 12.667 6.518 Q 12.568 6.453 12.486 6.469 A 0.3 0.3 0 0 0 12.321 6.519 L 11.083 7.211 L 15.076 9.538 A 0.6 0.6 0 0 1 15.34 9.802 A 0.64 0.64 0 0 1 15.44 10.165 Z  M 12.123 5.364 A 0.63 0.63 0 0 1 12.849 5.364 L 15.984 7.195 L 15.984 6.898 Q 15.984 6.106 15.588 5.397 A 2.86 2.86 0 0 0 14.483 4.242 Q 13.773 3.812 12.833 3.812 Q 12.008 3.812 11.397 4.159 L 8.294 5.941 A 0.36 0.36 0 0 0 8.129 6.255 L 8.129 7.673 Z )";

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
    g_label.Margin({0,0,0,0});
    double pw = width(g_percent), cw = width(g_credits), lw = width(g_label);
    double gap = pw && cw ? std::clamp(g_options[18],0,24) : 0;
    double row = pw+gap+cw, captionWidth = std::max(lw,row);
    double labelLeft = (captionWidth-lw)/2, numbersLeft = (captionWidth-row)/2;
    if (g_options[20] && pw > 0 && lw > 0) {
        // Preserve the full measured title. Reserve space on the left when it is
        // wider than the percentage, then align their centers without clipping.
        numbersLeft = std::max(0.,(lw-pw)/2);
        labelLeft = std::max(0.,(pw-lw)/2);
        captionWidth = std::max(labelLeft+lw,numbersLeft+row);
    }
    g_percent.Width(pw); g_credits.Width(cw); g_credits.Margin({gap,0,0,0});
    g_label.Width(lw); g_label.Margin({labelLeft,0,0,0});
    g_label.HorizontalAlignment(xaml::HorizontalAlignment::Left);
    g_numbers.HorizontalAlignment(xaml::HorizontalAlignment::Left);
    g_numbers.Margin({numbersLeft,(g_options[3]&1)?lineGap:0,0,0});
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
