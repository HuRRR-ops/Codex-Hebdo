$ErrorActionPreference='Stop'
$root=Join-Path ([IO.Path]::GetTempPath()) ('CodexHebdo-windhawk-tests-'+[guid]::NewGuid().ToString('N'))
$extracted=Join-Path $root 'extracted'
foreach ($folder in @('Compiler','UI','Engine\1.7.3\64')) { New-Item -ItemType Directory -Force (Join-Path $extracted $folder) | Out-Null }
foreach ($file in @('windhawk.exe','windhawk-x64-helper.exe','command-line.txt','Engine\1.7.3\64\windhawk.dll')) { Set-Content -LiteralPath (Join-Path $extracted $file) -Value 'fixture' }
# Authentication is mocked only for these inert test files; production still checks signatures.
function Get-AuthenticodeSignature { param($LiteralPath) return @{Status='Valid';SignerCertificate=@{Subject='Michael Maltsev'}} }
Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;
using System.Text;
public static class TestIniReader {
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]
    public static extern uint GetPrivateProfileString(string section,string key,string fallback,StringBuilder value,uint size,string path);
}
'@
$previousLocal=$env:LOCALAPPDATA
try {
    $env:LOCALAPPDATA=$root
    & (Join-Path $PSScriptRoot '..\scripts\Prepare-Windhawk.ps1') -ExtractedPath $extracted | Out-Null
    foreach ($relative in @('windhawk.ini','Engine\1.7.3\engine.ini')) {
        $path=Join-Path $root ('Programs\Windhawk\'+$relative)
        $bytes=[IO.File]::ReadAllBytes($path)
        if ($bytes[0] -ne 255 -or $bytes[1] -ne 254) { throw 'INI must have a UTF-16 LE BOM.' }
        $value=[Text.StringBuilder]::new(256)
        [void][TestIniReader]::GetPrivateProfileString('Storage','Portable','MISSING',$value,256,$path)
        if ($value.ToString() -ne '1') { throw 'Win32 must recognize the portable flag.' }
        [void][TestIniReader]::GetPrivateProfileString('Storage','AppDataPath','MISSING',$value,256,$path)
        if ($value.ToString() -eq 'MISSING') { throw 'Win32 must recognize AppDataPath.' }
    }
    foreach ($relative in @('windhawk.ini','Engine\1.7.3\engine.ini')) {
        $path=Join-Path $root ('Programs\Windhawk\'+$relative)
        [IO.File]::WriteAllText($path,[IO.File]::ReadAllText($path),[Text.UTF8Encoding]::new($true))
    }
    & (Join-Path $PSScriptRoot '..\scripts\Prepare-Windhawk.ps1') -ExtractedPath $extracted | Out-Null
    foreach ($relative in @('windhawk.ini','Engine\1.7.3\engine.ini')) {
        $path=Join-Path $root ('Programs\Windhawk\'+$relative)
        $bytes=[IO.File]::ReadAllBytes($path)
        if ($bytes[0] -ne 255 -or $bytes[1] -ne 254) { throw 'Existing UTF-8 BOM INI must be repaired.' }
        $value=[Text.StringBuilder]::new(256)
        [void][TestIniReader]::GetPrivateProfileString('Storage','Portable','MISSING',$value,256,$path)
        if ($value.ToString() -ne '1') { throw 'Repaired INI must retain the portable flag.' }
        [void][TestIniReader]::GetPrivateProfileString('Storage','AppDataPath','MISSING',$value,256,$path)
        if ($value.ToString() -ne $(if ($relative -eq 'windhawk.ini') { 'AppData' } else { '..\..\AppData\Engine' })) { throw 'Repair must preserve AppDataPath.' }
    }
    Write-Output '12 Windhawk INI checks passed.'
} finally {
    $env:LOCALAPPDATA=$previousLocal
    $resolved=[IO.Path]::GetFullPath($root)
    if ((Split-Path $resolved -Parent) -ne [IO.Path]::GetTempPath().TrimEnd('\') -or (Split-Path $resolved -Leaf) -notmatch '^CodexHebdo-windhawk-tests-[a-f0-9]{32}$') { throw 'Invalid test cleanup path.' }
    if ((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing junction cleanup.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
