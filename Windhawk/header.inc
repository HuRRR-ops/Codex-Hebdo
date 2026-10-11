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
