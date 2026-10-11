param([Parameter(Mandatory=$true)][string]$WindhawkPath)
$ErrorActionPreference = 'Stop'
$ini = Get-Content -LiteralPath (Join-Path $WindhawkPath 'windhawk.ini') -Raw
if ($ini -notmatch '(?m)^AppDataPath=(.+)$') { throw 'AppDataPath Windhawk introuvable.' }
$data = [Environment]::ExpandEnvironmentVariables($Matches[1].Trim())
if (![IO.Path]::IsPathRooted($data)) { $data = Join-Path $WindhawkPath $data }
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
if (!(Test-Path -LiteralPath $pdb)) {
    New-Item -ItemType Directory -Force $folder | Out-Null
    $temporary = Join-Path $folder ([guid]::NewGuid().ToString('N') + '.download')
    try {
        Invoke-WebRequest "https://msdl.microsoft.com/download/symbols/Taskbar.pdb/$key/Taskbar.pdb" -OutFile $temporary -TimeoutSec 90
        $stream = [IO.File]::OpenRead($temporary)
        try { $header=[byte[]]::new(24); [void]$stream.Read($header,0,24) } finally { $stream.Dispose() }
        if (![Text.Encoding]::ASCII.GetString($header).StartsWith('Microsoft C/C++ MSF')) { throw 'Fichier PDB inattendu.' }
        Move-Item -LiteralPath $temporary -Destination $pdb
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
}
Write-Host "Symboles Taskbar prêts (hors Explorer) : $key"
