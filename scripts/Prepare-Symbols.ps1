param([string]$WindhawkPath, [switch]$RepairMod, [switch]$FunctionsOnly)
$ErrorActionPreference = 'Stop'
function Repair-TaskbarMod([string]$Data, [string]$Key, [bool]$Downloaded) {
    $mod = Join-Path $Data 'Engine\Mods\codex-hebdo-taskbar.ini'
    $cache = Join-Path $Data 'Engine\ModsWritable\codex-hebdo-taskbar.ini'
    if (!(Test-Path -LiteralPath $mod)) { return }
    $settings = [IO.File]::ReadAllText($mod)
    if ($settings -notmatch '(?m)^Disabled=0\s*$') { return }
    $cached = if (Test-Path -LiteralPath $cache) { [IO.File]::ReadAllText($cache) } else { '' }
    $errorPattern = '(?m)^pdb_' + [regex]::Escape($Key) + '=error:[^\r\n]*\r?\n?'
    if (!$Downloaded -and $cached -notmatch $errorPattern) { return }
    if ($cached -match $errorPattern) {
        [IO.File]::WriteAllText($cache, [regex]::Replace($cached, $errorPattern, ''), [Text.Encoding]::Unicode)
    }
    # Reload only our enabled mod. Windhawk observes this file; Explorer stays running.
    [IO.File]::WriteAllText($mod, ($settings -replace '(?m)^Disabled=0','Disabled=1'), [Text.Encoding]::Unicode)
    try { Start-Sleep -Seconds 2 }
    finally {
        # Respect a concurrent change made by the user in Windhawk.
        $disabled = $settings -replace '(?m)^Disabled=0','Disabled=1'
        if ([IO.File]::ReadAllText($mod) -eq $disabled) {
            [IO.File]::WriteAllText($mod, $settings, [Text.Encoding]::Unicode)
        }
    }
}
if ($FunctionsOnly) { return }
if (!$WindhawkPath) { throw 'WindhawkPath requis.' }
$ini = Get-Content -LiteralPath (Join-Path $WindhawkPath 'windhawk.ini') -Raw
if ($ini -notmatch '(?m)^AppDataPath=(.+)$') { throw 'AppDataPath Windhawk introuvable.' }
$data = [Environment]::ExpandEnvironmentVariables($Matches[1].Trim())
if (![IO.Path]::IsPathRooted($data)) { $data = Join-Path $WindhawkPath $data }
if ($RepairMod) {
    $mod = Join-Path $data 'Engine\Mods\codex-hebdo-taskbar.ini'
    if (!(Test-Path -LiteralPath $mod) -or [IO.File]::ReadAllText($mod) -notmatch '(?m)^Disabled=0\s*$') { return }
}
$bytes = [IO.File]::ReadAllBytes((Join-Path $env:WINDIR 'System32\taskbar.dll'))
# Read the PE debug-directory (CodeView RSDS), never guess the PDB for a build.
$pe = [BitConverter]::ToInt32($bytes,0x3c)
if ([BitConverter]::ToUInt32($bytes,$pe) -ne 0x4550) { throw 'PE invalide.' }
$sections = [BitConverter]::ToUInt16($bytes,$pe+6)
$optionalSize = [BitConverter]::ToUInt16($bytes,$pe+20)
$optional = $pe+24
if ([BitConverter]::ToUInt16($bytes,$optional) -ne 0x20b) { throw 'Windows x64 requis.' }
$debugRva = [BitConverter]::ToUInt32($bytes,$optional+112+6*8)
$debugSize = [BitConverter]::ToUInt32($bytes,$optional+112+6*8+4)
$table = $optional + $optionalSize
$debugOffset = $null
for ($i=0; $i -lt $sections; $i++) {
    $entry = $table + $i*40
    $rva = [BitConverter]::ToUInt32($bytes,$entry+12)
    $rawSize = [BitConverter]::ToUInt32($bytes,$entry+16)
    if ($debugRva -ge $rva -and $debugRva -lt ($rva+$rawSize)) {
        $debugOffset = [BitConverter]::ToUInt32($bytes,$entry+20) + $debugRva - $rva
        break
    }
}
if ($null -eq $debugOffset) { throw 'Debug directory introuvable.' }
$key = $null
for ($i=0; $i -lt $debugSize; $i+=28) {
    if ([BitConverter]::ToUInt32($bytes,$debugOffset+$i+12) -ne 2) { continue }
    $cv = [BitConverter]::ToUInt32($bytes,$debugOffset+$i+24)
    if ([Text.Encoding]::ASCII.GetString($bytes,$cv,4) -ne 'RSDS') { continue }
    $guidBytes = [byte[]]::new(16)
    [Array]::Copy($bytes,$cv+4,$guidBytes,0,16)
    $guid = [guid]::new($guidBytes)
    $age = [BitConverter]::ToUInt32($bytes,$cv+20)
    $key = $guid.ToString('N').ToUpperInvariant() + $age.ToString('X')
    break
}
if (!$key) { throw 'Signature PDB introuvable.' }
$folder = Join-Path $data "Engine\Symbols\Taskbar.pdb\$key"
$pdb = Join-Path $folder 'Taskbar.pdb'
$downloaded = $false
if (!(Test-Path -LiteralPath $pdb)) {
    New-Item -ItemType Directory -Force $folder | Out-Null
    $temporary = Join-Path $folder ([guid]::NewGuid().ToString('N') + '.download')
    try {
        Invoke-WebRequest -UseBasicParsing "https://msdl.microsoft.com/download/symbols/Taskbar.pdb/$key/Taskbar.pdb" -OutFile $temporary -TimeoutSec 90
        $stream = [IO.File]::OpenRead($temporary)
        try { $header=[byte[]]::new(24); [void]$stream.Read($header,0,24) } finally { $stream.Dispose() }
        if (![Text.Encoding]::ASCII.GetString($header).StartsWith('Microsoft C/C++ MSF')) { throw 'Fichier PDB inattendu.' }
        Move-Item -LiteralPath $temporary -Destination $pdb
        $downloaded = $true
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
}
if ($RepairMod) { Repair-TaskbarMod $data $key $downloaded }
Write-Host "Symboles Taskbar prêts (hors Explorer) : $key"
