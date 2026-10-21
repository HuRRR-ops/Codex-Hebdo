param([string]$WindhawkPath, [string]$CodexPath, [switch]$NoStart, [switch]$RegisterUninstall)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'scripts\Install-Transaction.ps1')
. (Join-Path $PSScriptRoot 'scripts\Windhawk-Isolation.ps1')
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
$oldExe = Join-Path $destination 'CodexHebdo.exe'
$run = 'Software\Microsoft\Windows\CurrentVersion\Run'
$enableStartup = !(Test-Path (Join-Path $destination 'preferences.json')) -or
    ($null -ne (Get-ItemProperty -Path "HKCU:\$run" -Name CodexHebdo -ErrorAction SilentlyContinue))
$files = New-Object 'System.Collections.Generic.List[object]'
function Add-File([string]$From, [string]$To) {
    if (!(Test-Path -LiteralPath $From -PathType Leaf)) { throw "Installation file missing: $From" }
    $files.Add(@{ Source=$From; Target=$To })
}
function Add-Text([string]$Text, [string]$To, [Text.Encoding]$Encoding = [Text.UTF8Encoding]::new($false)) {
    $files.Add(@{ Bytes=[byte[]]($Encoding.GetPreamble()+$Encoding.GetBytes($Text)); Target=$To })
}
foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File) {
    # Build output must never overwrite user preferences or local configuration.
    if ($file.Name -in @('preferences.json','preferences.json.tmp','codex-path.txt','installation.json','probe.json')) { continue }
    Add-File $file.FullName (Join-Path $destination $file.FullName.Substring($source.Length+1))
}
Add-Text $codex (Join-Path $destination 'codex-path.txt')
foreach ($name in @('Uninstall.ps1','LICENSE','THIRD-PARTY-NOTICES.txt')) { Add-File (Join-Path $PSScriptRoot $name) (Join-Path $destination $name) }
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Recurse -File) {
    Add-File $file.FullName (Join-Path $destination $file.FullName.Substring($PSScriptRoot.Length+1))
}
Add-File (Join-Path $PSScriptRoot 'dist\codex-hebdo-taskbar.wh.cpp') (Join-Path $destination 'codex-hebdo-taskbar.wh.cpp')
$portable = $false
$iniPath = Join-Path $WindhawkPath 'windhawk.ini'
if (Test-Path -LiteralPath $iniPath) {
    $ini = [IO.File]::ReadAllText($iniPath)
    $portable = $ini -match '(?m)^Portable=1\s*$'
}
$nativeInstalled = $false
$isolation = @{ File=$null; Status='preserved' }
if ($portable) {
    if ($ini -notmatch '(?m)^AppDataPath=(.+)$') { throw 'AppDataPath Windhawk introuvable.' }
    $appData = [Environment]::ExpandEnvironmentVariables($Matches[1].Trim())
    if (![IO.Path]::IsPathRooted($appData)) { $appData = Join-Path $WindhawkPath $appData }
    $isolation = Get-WindhawkIsolationPlan -WindhawkPath $WindhawkPath -AppDataPath $appData -InstallationFile (Join-Path $destination 'installation.json')
    if ($isolation.File) { $files.Add($isolation.File) }
    $mods = Join-Path $appData 'Engine\Mods'
    $dll = Join-Path $PSScriptRoot 'dist\codex-hebdo-taskbar.dll'
    if (!(Test-Path $dll)) { throw 'Compiler le module natif avec Build.ps1 -WindhawkPath.' }
    # Unique DLL name permits an in-place update without overwriting a loaded library.
    $dllName = 'codex-hebdo-taskbar_' + [Guid]::NewGuid().ToString('N') + '.dll'
    Add-File $dll (Join-Path $mods "64\$dllName")
    foreach ($pair in @(@('libc++.dll','libc++.whl'),@('libunwind.dll','libunwind.whl'),@('windhawk-mod-shim.dll','windhawk-mod-shim.dll'))) {
        $target = Join-Path $mods "64\$($pair[1])"
        if (!(Test-Path -LiteralPath $target)) { Add-File (Join-Path $WindhawkPath "Compiler\x86_64-w64-mingw32\bin\$($pair[0])") $target }
    }
    $modText = @"
[Mod]
LibraryFileName=$dllName
Disabled=0
LoggingEnabled=0
DebugLoggingEnabled=0
Include=explorer.exe
Architecture=amd64
Version=1.2.3
"@
    Add-File (Join-Path $PSScriptRoot 'dist\codex-hebdo-taskbar.wh.cpp') (Join-Path $appData 'ModsSource\codex-hebdo-taskbar.wh.cpp')
    $nativeInstalled = $true
}
Add-Text (@{ WindhawkPath=$WindhawkPath; Portable=$portable; NativeInstalled=$nativeInstalled } | ConvertTo-Json) (Join-Path $destination 'installation.json')
# Activate only after every application/support file has been staged and written.
if ($portable) { Add-Text $modText (Join-Path $mods 'codex-hebdo-taskbar.ini') ([Text.Encoding]::Unicode) }
foreach ($entry in $files) { Assert-InstallPath $entry.Target }
if ($portable) { & (Join-Path $PSScriptRoot 'scripts\Prepare-Symbols.ps1') -WindhawkPath $WindhawkPath }
$registry = New-Object 'System.Collections.Generic.List[object]'
if ($enableStartup) {
    $registry.Add(@{ Key=$run; Name='CodexHebdo'; Value=('"'+$oldExe+'"') })
    if ($portable) { $registry.Add(@{ Key=$run; Name='CodexHebdo.Windhawk'; Value=('"'+(Join-Path $WindhawkPath 'windhawk.exe')+'" -tray-only') }) }
}
if ($RegisterUninstall) {
    $shell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $values = @{ DisplayName='Codex Hebdo'; DisplayVersion='1.2.3'; Publisher='Codex Hebdo'; InstallLocation=$destination;
        DisplayIcon=$oldExe; UninstallString=('"'+$shell+'" -NoProfile -ExecutionPolicy Bypass -File "'+(Join-Path $destination 'Uninstall.ps1')+'"') }
    foreach ($name in $values.Keys) { $registry.Add(@{ Key='Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexHebdo'; Name=$name; Value=$values[$name] }) }
}
$wasRunning = @(Get-Process CodexHebdo -ErrorAction SilentlyContinue | Where-Object Path -eq $oldExe).Count -gt 0
$windhawkWasRunning = @(Get-Process windhawk -ErrorAction SilentlyContinue | Where-Object Path -eq (Join-Path $WindhawkPath 'windhawk.exe')).Count -gt 0
$stopCompanion = {
    if (Test-Path -LiteralPath $oldExe) {
        Start-Process -FilePath $oldExe -ArgumentList '--stop' -WindowStyle Hidden -Wait
        $running = Get-Process CodexHebdo -ErrorAction SilentlyContinue | Where-Object Path -eq $oldExe
        if ($running) { $running | Wait-Process -Timeout 15 -ErrorAction Stop }
    }
}
Invoke-InstallTransaction -Files $files.ToArray() -RegistryValues $registry.ToArray() -Stop $stopCompanion -Start {
    if (!$NoStart) {
        Start-Process -FilePath $oldExe -WindowStyle Hidden
        $windhawkArguments = if ($isolation.File -and $windhawkWasRunning) { '-restart -tray-only' } else { '-tray-only' }
        Start-Process -FilePath (Join-Path $WindhawkPath 'windhawk.exe') -ArgumentList $windhawkArguments -WindowStyle Hidden
    }
} -RestartPrevious {
    if ($isolation.File -and $windhawkWasRunning -and !$NoStart) {
        Start-Process -FilePath (Join-Path $WindhawkPath 'windhawk.exe') -ArgumentList '-restart -tray-only' -WindowStyle Hidden
    }
    if ($wasRunning) { Start-Process -FilePath $oldExe -WindowStyle Hidden }
}
if ($isolation.Status -eq 'preserved') { Write-Warning 'Configuration Windhawk partagee/personnalisee conservee : restriction globale a Explorer non appliquee.' }
elseif ($NoStart -and $isolation.File) { Write-Warning 'Restriction a Explorer enregistree. Redemarrer Windhawk pour l appliquer aux prochains lancements de jeux.' }
else { Write-Host 'Windhawk limite a Explorer. Relancer les jeux deja ouverts pour beneficier de cette restriction.' }
if ($nativeInstalled) { Write-Host "Installé : $destination. Démarrage automatique configuré pour cet utilisateur." }
else {
    Write-Host "Compagnon installé. Dans Windhawk : Créer un nouveau mod, remplacer le code par $destination\codex-hebdo-taskbar.wh.cpp, puis Compiler (F7) et activer."
}
