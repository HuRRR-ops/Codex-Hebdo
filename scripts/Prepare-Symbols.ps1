param([string]$WindhawkPath, [switch]$RepairMod, [switch]$FunctionsOnly)
$ErrorActionPreference = 'Stop'
# Validate MSF allocation and the PDB identity, not just a recognizable prefix.
# Format references: https://llvm.org/docs/PDB/MsfFile.html and PdbStream.html.
function Test-TaskbarPdb([string]$Path, [string]$Key) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    if (!('CodexHebdo.PdbValidation' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;
namespace CodexHebdo {
    public static class PdbValidation {
        static uint U(byte[] b, int offset) { return BitConverter.ToUInt32(b, offset); }
        public static bool Matches(string path, string key) {
            try {
                using (var file = File.OpenRead(path)) {
                    if (file.Length < 56 || file.Length > 512L * 1024 * 1024) return false;
                    var reader = new BinaryReader(file);
                    var header = reader.ReadBytes(56);
                    if (Encoding.ASCII.GetString(header, 0, 32) != "Microsoft C/C++ MSF 7.00\r\n\u001aDS\0\0\0") return false;
                    uint block = U(header,32), count = U(header,40), size = U(header,44), map = U(header,52);
                    if (block < 512 || block > 65536 || (block & (block-1)) != 0
                        || (long)count * block != file.Length || size < 12 || size > 16 * 1024 * 1024
                        || map >= count) return false;
                    uint pages = (size + block - 1) / block;
                    if (pages * 4 > block) return false;
                    file.Position = (long)map * block;
                    var directoryPages = reader.ReadBytes((int)pages * 4);
                    var directory = new byte[size];
                    for (int i=0; i<pages; i++) {
                        uint page = U(directoryPages,i*4);
                        if (page >= count) return false;
                        file.Position = (long)page * block;
                        int length = (int)Math.Min(block, size-(long)i*block);
                        var bytes = reader.ReadBytes(length);
                        if (bytes.Length != length) return false;
                        Array.Copy(bytes,0,directory,(long)i*block,length);
                    }
                    uint streams = U(directory,0);
                    if (streams < 4 || streams > (size-4)/4) return false;
                    int cursor = checked(4+(int)streams*4);
                    uint infoPage=0, infoSize=0, dbiPage=0, dbiSize=0;
                    for (int i=0; i<streams; i++) {
                        uint length = U(directory,4+i*4);
                        if (length == uint.MaxValue) continue;
                        long blocks = ((long)length+block-1)/block;
                        if (blocks > (size-cursor)/4) return false;
                        for (int j=0; j<blocks; j++) {
                            uint page = U(directory,cursor); cursor+=4;
                            if (page >= count) return false;
                            if (i==1 && j==0) { infoPage=page; infoSize=length; }
                            if (i==3 && j==0) { dbiPage=page; dbiSize=length; }
                        }
                    }
                    if (infoSize < 28 || dbiSize < 64 || key.Length < 33) return false;
                    file.Position=(long)infoPage*block;
                    var info=reader.ReadBytes(28);
                    var guidBytes=new byte[16]; Array.Copy(info,12,guidBytes,0,16);
                    uint imageAge;
                    if (!uint.TryParse(key.Substring(32),System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture,out imageAge)) return false;
                    file.Position=(long)dbiPage*block;
                    var dbi=reader.ReadBytes(12);
                    // Public Windows PDBs may have a newer Info age after stripping.
                    // Match like microsoft-pdb PDB1::OpenValidate4: GUID, Info >= image,
                    // and DBI == image (legacy DBI age zero is allowed).
                    return new Guid(guidBytes).ToString("N").Equals(key.Substring(0,32),StringComparison.OrdinalIgnoreCase)
                        && U(info,8) >= imageAge && U(dbi,0) == uint.MaxValue
                        && (U(dbi,8) == 0 || U(dbi,8) == imageAge);
                }
            } catch (IOException) { return false; }
              catch (ArgumentException) { return false; }
              catch (OverflowException) { return false; }
        }
    }
}
'@
    }
    return [CodexHebdo.PdbValidation]::Matches($Path, $Key)
}

function Ensure-TaskbarPdb([string]$Path, [string]$Key) {
    $hashPath = $Path + '.sha256'
    $valid = Test-TaskbarPdb $Path $Key
    if ($valid -and (Test-Path -LiteralPath $hashPath)) {
        $valid = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq ([IO.File]::ReadAllText($hashPath).Trim())
    }
    if ($valid) {
        if (!(Test-Path -LiteralPath $hashPath)) {
            [IO.File]::WriteAllText($hashPath, (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash)
        }
        return $false
    }
    $folder = Split-Path $Path -Parent
    New-Item -ItemType Directory -Force $folder | Out-Null
    $temporary = Join-Path $folder ([guid]::NewGuid().ToString('N') + '.download')
    try {
        Invoke-WebRequest -UseBasicParsing "https://msdl.microsoft.com/download/symbols/Taskbar.pdb/$Key/Taskbar.pdb" -OutFile $temporary -TimeoutSec 90
        if (!(Test-TaskbarPdb $temporary $Key)) { throw 'Invalid or mismatched Taskbar PDB.' }
        $hash = (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash
        # Atomic replacement preserves the previous file if downloading/validation fails.
        if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($temporary, $Path, [NullString]::Value) }
        else { [IO.File]::Move($temporary, $Path) }
        [IO.File]::WriteAllText($hashPath, $hash)
        return $true
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
}

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
$downloaded = Ensure-TaskbarPdb $pdb $key
if ($RepairMod) { Repair-TaskbarMod $data $key $downloaded }
Write-Host "Symboles Taskbar prêts (hors Explorer) : $key"
