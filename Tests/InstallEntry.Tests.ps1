$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=Join-Path ([IO.Path]::GetTempPath()) ('CodexHebdo-install-entry-tests-'+[guid]::NewGuid().ToString('N'))
$payload=Join-Path $root 'payload'
$local=Join-Path $root 'local'
$windhawk=Join-Path $root 'windhawk'
$app=Join-Path $local 'CodexHebdo'
foreach ($dir in @($app,"$payload\dist\Companion","$payload\scripts","$payload\licenses","$windhawk\Compiler\x86_64-w64-mingw32\bin")) { New-Item -ItemType Directory -Force $dir | Out-Null }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\Install.ps1') -Destination $payload
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\scripts\Install-Transaction.ps1') -Destination "$payload\scripts"
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\scripts\Windhawk-Isolation.ps1') -Destination "$payload\scripts"
# Only network symbol provisioning is replaced. The real installer/transaction run.
Set-Content -LiteralPath "$payload\scripts\Prepare-Symbols.ps1" -Value 'param($WindhawkPath)'
foreach ($file in @('Uninstall.ps1','LICENSE','THIRD-PARTY-NOTICES.txt','licenses\notice.txt','dist\codex-hebdo-taskbar.wh.cpp','dist\Companion\hostpolicy.dll','dist\Companion\CodexHebdo.exe')) { [IO.File]::WriteAllText((Join-Path $payload $file),'new payload') }
foreach ($file in @('windhawk.exe','Compiler\x86_64-w64-mingw32\bin\libc++.dll','Compiler\x86_64-w64-mingw32\bin\libunwind.dll','Compiler\x86_64-w64-mingw32\bin\windhawk-mod-shim.dll')) { [IO.File]::WriteAllText((Join-Path $windhawk $file),'fixture') }
[IO.File]::WriteAllText("$windhawk\windhawk.ini","[Storage]`r`nPortable=1`r`nEnginePath=Engine\1.7.3`r`nAppDataPath=AppData",[Text.Encoding]::Unicode)
[IO.File]::WriteAllText("$app\CodexHebdo.exe",'old companion')
[IO.File]::WriteAllText("$app\preferences.json",'preserved preferences')
[IO.File]::WriteAllText("$payload\dist\Companion\preferences.json",'must never overwrite preferences')
$codex=Join-Path $root 'codex.exe';[IO.File]::WriteAllText($codex,'fixture')
New-Item -ItemType Directory -Force "$windhawk\Engine\1.7.3" | Out-Null
[IO.File]::WriteAllText("$windhawk\Engine\1.7.3\engine.ini","[Storage]`r`nPortable=1`r`nAppDataPath=..\..\AppData\Engine")
[IO.File]::WriteAllText("$windhawk\codex-hebdo-managed.txt",'Created by Codex Hebdo')
$state=@{Launches=0;Fail=$true;WindhawkRunning=$false;LastArguments=$null}
function Get-Process { param($Name,$ErrorAction) if ($Name -eq 'CodexHebdo') { return [pscustomobject]@{Path="$app\CodexHebdo.exe"} }; if ($Name -eq 'windhawk' -and $state.WindhawkRunning) { return [pscustomobject]@{Path="$windhawk\windhawk.exe"} } }
function Wait-Process { param([Parameter(ValueFromPipeline=$true)]$InputObject,$Timeout) }
function Get-ItemProperty { param($Path,$Name,$ErrorAction) return $null } # Autostart remains disabled.
function Start-Process { param($FilePath,$ArgumentList,$WindowStyle,[switch]$Wait)
    $state.Launches++
    if ($FilePath -eq "$windhawk\windhawk.exe") { $state.LastArguments=$ArgumentList }
    if ($state.Fail -and $FilePath -eq "$windhawk\windhawk.exe") { throw 'Injected Windhawk launch failure' }
}
$previousLocal=$env:LOCALAPPDATA
try {
    $env:LOCALAPPDATA=$local
    $failed=$false
    try { & "$payload\Install.ps1" -WindhawkPath $windhawk -CodexPath $codex } catch { $failed=$true }
    if (!$failed -or $state.Launches -ne 0 -or [IO.File]::ReadAllText("$app\CodexHebdo.exe") -ne 'old companion') { throw 'Missing native DLL must fail before stopping the companion.' }
    [IO.File]::WriteAllText("$payload\dist\codex-hebdo-taskbar.dll",'native fixture')
    $failed=$false
    try { & "$payload\Install.ps1" -WindhawkPath $windhawk -CodexPath $codex } catch {
        $failed=$_.Exception.Message -like '*Injected Windhawk launch failure*'
        if (!$failed) { throw }
    }
    if (!$failed -or [IO.File]::ReadAllText("$app\CodexHebdo.exe") -ne 'old companion') { throw 'Entry-point rollback must restore the previous companion.' }
    if (Test-Path "$windhawk\AppData\Engine\Mods\codex-hebdo-taskbar.ini") { throw 'Entry-point rollback must remove the new mod configuration.' }
    if (Test-Path "$windhawk\AppData\Engine\settings.ini") { throw 'Rollback must remove new isolation settings.' }
    if ($state.Launches -ne 5) { throw 'Entry point must stop, start, fail, stop and restart the previous companion.' }
    $state.Fail=$false
    & "$payload\Install.ps1" -WindhawkPath $windhawk -CodexPath $codex -NoStart
    if ([IO.File]::ReadAllText("$app\CodexHebdo.exe") -ne 'new payload' -or [IO.File]::ReadAllText("$app\preferences.json") -ne 'preserved preferences') { throw 'Successful install must preserve preferences.' }
    $ini=[IO.File]::ReadAllText("$windhawk\AppData\Engine\Mods\codex-hebdo-taskbar.ini")
    if ($ini -notmatch '(?m)^LibraryFileName=(.+)') { throw 'Native configuration missing.' }
    if (!(Test-Path (Join-Path "$windhawk\AppData\Engine\Mods\64" $Matches[1].Trim()))) { throw 'Configuration must refer to the installed DLL.' }
    if ([IO.File]::ReadAllText("$windhawk\AppData\Engine\settings.ini") -notmatch '(?m)^Exclude=\*') { throw 'Isolation settings missing after install.' }
    Remove-Item -LiteralPath "$windhawk\AppData\Engine\settings.ini"
    $state.WindhawkRunning=$true
    & "$payload\Install.ps1" -WindhawkPath $windhawk -CodexPath $codex
    if ($state.LastArguments -ne '-restart -tray-only') { throw 'Running dedicated Windhawk must restart to load isolation settings.' }
    Write-Output '9 installer entry-point checks passed.'
} finally {
    $env:LOCALAPPDATA=$previousLocal
    $resolved=[IO.Path]::GetFullPath($root)
    if ((Split-Path $resolved -Parent) -ne [IO.Path]::GetTempPath().TrimEnd('\') -or (Split-Path $resolved -Leaf) -notmatch '^CodexHebdo-install-entry-tests-[a-f0-9]{32}$') { throw 'Invalid test cleanup path.' }
    if ((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing junction cleanup.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
