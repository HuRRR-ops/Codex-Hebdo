$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\scripts\Prepare-Symbols.ps1') -FunctionsOnly
$data = Join-Path $env:TEMP ('CodexHebdo-recovery-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force (Join-Path $data 'Engine\Mods'),(Join-Path $data 'Engine\ModsWritable') | Out-Null
$mod = Join-Path $data 'Engine\Mods\codex-hebdo-taskbar.ini'
$cache = Join-Path $data 'Engine\ModsWritable\codex-hebdo-taskbar.ini'
$settings = "[Mod]`r`nDisabled=0`r`nLibraryFileName=example.dll`r`n"
[IO.File]::WriteAllText($mod,$settings,[Text.Encoding]::Unicode)
[IO.File]::WriteAllText($cache,"[SymbolCache]`r`npdb_CURRENT=error:1`r`npdb_OTHER=1#valid`r`n[LocalStorage]`r`nFreshQuota=1`r`n",[Text.Encoding]::Unicode)
Repair-TaskbarMod $data 'CURRENT' $false
if ([IO.File]::ReadAllText($mod) -ne $settings) { throw 'Enabled mod must be restored after reload.' }
$result=[IO.File]::ReadAllText($cache)
if ($result -match 'pdb_CURRENT=' -or $result -notmatch 'pdb_OTHER=1#valid' -or $result -notmatch 'FreshQuota=1') { throw 'Only the current failed symbol entry may be removed.' }
$timestamp=(Get-Item $mod).LastWriteTimeUtc
Repair-TaskbarMod $data 'CURRENT' $false
if ((Get-Item $mod).LastWriteTimeUtc -ne $timestamp) { throw 'Healthy mod must not be reloaded every five minutes.' }
[IO.File]::WriteAllText($mod,($settings -replace 'Disabled=0','Disabled=1'),[Text.Encoding]::Unicode)
Repair-TaskbarMod $data 'CURRENT' $true
if ([IO.File]::ReadAllText($mod) -notmatch 'Disabled=1') { throw 'User-disabled mod must stay disabled.' }
[IO.File]::WriteAllText($mod,$settings,[Text.Encoding]::Unicode)
Repair-TaskbarMod $data 'CURRENT' $true
if ([IO.File]::ReadAllText($mod) -ne $settings) { throw 'New symbols must trigger a completed reload.' }
Write-Output '5 taskbar recovery checks passed.'
