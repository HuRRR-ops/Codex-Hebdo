param([string]$WindhawkPath, [string]$CodexPath, [switch]$NoStart)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$destination = Join-Path $env:LOCALAPPDATA 'CodexHebdo'
$source = Join-Path $PSScriptRoot 'dist\Companion'
if (!(Test-Path (Join-Path $source 'CodexHebdo.exe'))) { throw 'Exécuter Build.ps1 avant installation.' }
if (!(Test-Path (Join-Path $source 'hostpolicy.dll')) -and (!(Get-Command dotnet -ErrorAction SilentlyContinue) -or !(dotnet --list-runtimes | Select-String 'Microsoft.WindowsDesktop.App 10\.'))) {
    throw 'Installer le runtime .NET Desktop 10 x64 : https://dotnet.microsoft.com/download/dotnet/10.0'
}
$codex = if ($CodexPath) { (Resolve-Path -LiteralPath $CodexPath -ErrorAction Stop).Path } else { (Get-Command codex.exe -ErrorAction Stop).Source }
if (!$WindhawkPath) {
    $WindhawkPath = @("$env:LOCALAPPDATA\Programs\Windhawk", "${env:ProgramFiles}\Windhawk", "${env:ProgramFiles(x86)}\Windhawk") |
        Where-Object { Test-Path (Join-Path $_ 'windhawk.exe') } | Select-Object -First 1
}
if (!$WindhawkPath -or !(Test-Path (Join-Path $WindhawkPath 'windhawk.exe'))) {
    throw 'Windhawk absent. Installer https://windhawk.net puis relancer, ou fournir -WindhawkPath.'
}
$WindhawkPath = (Resolve-Path -LiteralPath $WindhawkPath).Path
if (Test-Path (Join-Path $WindhawkPath 'windhawk.ini')) {
    & (Join-Path $PSScriptRoot 'scripts\Prepare-Symbols.ps1') -WindhawkPath $WindhawkPath
}
$oldExe = Join-Path $destination 'CodexHebdo.exe'
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$enableStartup = !(Test-Path (Join-Path $destination 'preferences.json')) -or
    ($null -ne (Get-ItemProperty -Path $run -Name CodexHebdo -ErrorAction SilentlyContinue))
if (Test-Path -LiteralPath $oldExe) {
    Start-Process -FilePath $oldExe -ArgumentList '--stop' -WindowStyle Hidden -Wait
    $running = Get-Process CodexHebdo -ErrorAction SilentlyContinue | Where-Object Path -eq $oldExe
    if ($running) { $running | Wait-Process -Timeout 15 -ErrorAction Stop }
}
New-Item -ItemType Directory -Force $destination | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $destination -Force -Recurse
Set-Content -LiteralPath (Join-Path $destination 'codex-path.txt') -Value $codex -Encoding utf8
Copy-Item (Join-Path $PSScriptRoot 'Uninstall.ps1') $destination -Force
Copy-Item (Join-Path $PSScriptRoot 'dist\codex-hebdo-taskbar.wh.cpp') $destination -Force
$portable = $false
$iniPath = Join-Path $WindhawkPath 'windhawk.ini'
if (Test-Path -LiteralPath $iniPath) {
    $ini = Get-Content -LiteralPath $iniPath -Raw
    $portable = $ini -match '(?m)^Portable=1\s*$'
}
$nativeInstalled = $false
if ($portable) {
    if ($ini -notmatch '(?m)^AppDataPath=(.+)$') { throw 'AppDataPath Windhawk introuvable.' }
    $appData = [Environment]::ExpandEnvironmentVariables($Matches[1].Trim())
    if (![IO.Path]::IsPathRooted($appData)) { $appData = Join-Path $WindhawkPath $appData }
    $mods = Join-Path $appData 'Engine\Mods'
    $dll = Join-Path $PSScriptRoot 'dist\codex-hebdo-taskbar.dll'
    if (!(Test-Path $dll)) { throw 'Compiler le module natif avec Build.ps1 -WindhawkPath.' }
    New-Item -ItemType Directory -Force (Join-Path $mods '64') | Out-Null
    # Unique DLL name permits an in-place update without overwriting a loaded library.
    $dllName = 'codex-hebdo-taskbar_' + [Guid]::NewGuid().ToString('N') + '.dll'
    Copy-Item -LiteralPath $dll -Destination (Join-Path $mods "64\$dllName")
    foreach ($pair in @(@('libc++.dll','libc++.whl'),@('libunwind.dll','libunwind.whl'),@('windhawk-mod-shim.dll','windhawk-mod-shim.dll'))) {
        $target = Join-Path $mods "64\$($pair[1])"
        if (!(Test-Path -LiteralPath $target)) { Copy-Item -LiteralPath (Join-Path $WindhawkPath "Compiler\x86_64-w64-mingw32\bin\$($pair[0])") -Destination $target }
    }
    @"
[Mod]
LibraryFileName=$dllName
Disabled=0
LoggingEnabled=0
DebugLoggingEnabled=0
Include=explorer.exe
Architecture=amd64
Version=1.2.1
"@ | Set-Content -LiteralPath (Join-Path $mods 'codex-hebdo-taskbar.ini') -Encoding Unicode
    New-Item -ItemType Directory -Force (Join-Path $appData 'ModsSource') | Out-Null
    Copy-Item (Join-Path $PSScriptRoot 'dist\codex-hebdo-taskbar.wh.cpp') (Join-Path $appData 'ModsSource\codex-hebdo-taskbar.wh.cpp') -Force
    $nativeInstalled = $true
}
@{ WindhawkPath=$WindhawkPath; Portable=$portable; NativeInstalled=$nativeInstalled } | ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $destination 'installation.json') -Encoding utf8
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-Item -Path $run -Force | Out-Null
if ($enableStartup) {
New-ItemProperty -Path $run -Name 'CodexHebdo' -Value ('"' + $oldExe + '"') -PropertyType String -Force | Out-Null
if ($portable) {
    New-ItemProperty -Path $run -Name 'CodexHebdo.Windhawk' -Value ('"' + (Join-Path $WindhawkPath 'windhawk.exe') + '" -tray-only') -PropertyType String -Force | Out-Null
}
}
if (!$NoStart) {
    Start-Process -FilePath $oldExe -WindowStyle Hidden
    Start-Process -FilePath (Join-Path $WindhawkPath 'windhawk.exe') -ArgumentList '-tray-only' -WindowStyle Hidden
}
if ($nativeInstalled) { Write-Host "Installé : $destination. Démarrage automatique configuré pour cet utilisateur." }
else {
    Write-Host "Compagnon installé. Dans Windhawk : Créer un nouveau mod, remplacer le code par $destination\codex-hebdo-taskbar.wh.cpp, puis Compiler (F7) et activer."
}
