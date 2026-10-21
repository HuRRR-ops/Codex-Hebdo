$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '..\scripts\Prepare-Symbols.ps1') -FunctionsOnly
$root=Join-Path ([IO.Path]::GetTempPath()) ('CodexHebdo-symbol-tests-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$script:checks=0
function Assert([bool]$Value,[string]$Message) { if (!$Value) { throw $Message };$script:checks++ }
$guid=[guid]'6d92b267-f776-4c9e-9eb5-6a7527854a22'
$key=$guid.ToString('N').ToUpperInvariant()+'1'
# Minimal MSF 7 container: free maps 1/2, directory map 3, directory 4, info 5.
$fixture=[byte[]]::new(3584)
[Text.Encoding]::ASCII.GetBytes("Microsoft C/C++ MSF 7.00`r`n$([char]26)DS$([char]0)$([char]0)$([char]0)").CopyTo($fixture,0)
foreach ($pair in @(@(32,512),@(36,1),@(40,7),@(44,28),@(52,3),@(1536,4),@(2048,4),@(2052,[uint32]::MaxValue),@(2056,28),@(2060,0),@(2064,64),@(2068,5),@(2072,6),@(2560,20000404),@(2568,1),@(3072,[uint32]::MaxValue),@(3076,19990903),@(3080,1))) {
    [BitConverter]::GetBytes([uint32]$pair[1]).CopyTo($fixture,[int]$pair[0])
}
$guid.ToByteArray().CopyTo($fixture,2572)
$path=Join-Path $root 'Taskbar.pdb'
$state=@{Downloads=0;Mode='valid'}
function Invoke-WebRequest { param([switch]$UseBasicParsing,$Uri,$OutFile,$TimeoutSec)
    $state.Downloads++
    if ($state.Mode -eq 'offline') { throw 'Simulated offline' }
    if ($state.Mode -eq 'invalid') { [IO.File]::WriteAllText($OutFile,'corrupt') }
    else { [IO.File]::WriteAllBytes($OutFile,$fixture) }
}
try {
    Assert (Ensure-TaskbarPdb $path $key) 'Missing symbols must download.'
    Assert (Test-TaskbarPdb $path $key) 'Valid PDB identity must pass.'
    Assert (!(Ensure-TaskbarPdb $path $key) -and $state.Downloads -eq 1) 'Healthy cache must not download again.'
    Assert (!(Test-TaskbarPdb $path ('0'*32+'1'))) 'Wrong GUID must be rejected.'
    Assert (!(Test-TaskbarPdb $path ($guid.ToString('N')+'2'))) 'Wrong age must be rejected.'
    [IO.File]::WriteAllText($path,'corrupt')
    Assert (Ensure-TaskbarPdb $path $key) 'Corrupt cache must be replaced.'
    Assert ((Test-TaskbarPdb $path $key) -and $state.Downloads -eq 2) 'Replacement must contain the matching PDB.'
    $damaged=$fixture.Clone();$damaged[3000]=1
    [IO.File]::WriteAllBytes($path,$damaged)
    Assert (Ensure-TaskbarPdb $path $key) 'Checksum must detect corruption outside the headers.'
    [IO.File]::WriteAllBytes($path,$fixture[0..2700])
    Assert (!(Test-TaskbarPdb $path $key)) 'Truncated allocation must be rejected.'
    $damaged=$fixture.Clone();[BitConverter]::GetBytes([uint32]999).CopyTo($damaged,2068)
    [IO.File]::WriteAllBytes($path,$damaged)
    Assert (!(Test-TaskbarPdb $path $key)) 'Out-of-range stream block must be rejected.'
    $damaged=$fixture.Clone();[BitConverter]::GetBytes([uint32]999).CopyTo($damaged,1536)
    [IO.File]::WriteAllBytes($path,$damaged)
    Assert (!(Test-TaskbarPdb $path $key)) 'Out-of-range directory block must be rejected.'
    $damaged=$fixture.Clone();[BitConverter]::GetBytes([uint32]3).CopyTo($damaged,2568)
    [IO.File]::WriteAllBytes($path,$damaged)
    Assert (Test-TaskbarPdb $path $key) 'Stripped public PDB with newer Info age and matching DBI age must pass.'
    [BitConverter]::GetBytes([uint32]2).CopyTo($damaged,3080)
    [IO.File]::WriteAllBytes($path,$damaged)
    Assert (!(Test-TaskbarPdb $path $key)) 'Wrong DBI age must be rejected even when Info age is newer.'
    foreach ($mode in @('offline','invalid')) {
        [IO.File]::WriteAllText($path,'old corrupt cache')
        $state.Mode=$mode;$failed=$false
        try { [void](Ensure-TaskbarPdb $path $key) } catch { $failed=$true }
        Assert ($failed -and [IO.File]::ReadAllText($path) -eq 'old corrupt cache') 'A failed download must not replace the existing file or report success.'
        Assert (@(Get-ChildItem -LiteralPath $root -Filter '*.download').Count -eq 0) 'Partial download must be cleaned up.'
    }
    $state.Mode='valid'
    Assert (Ensure-TaskbarPdb $path $key) 'Recovery must succeed when connectivity returns.'
    Write-Output "$script:checks symbol cache checks passed."
} finally {
    $resolved=[IO.Path]::GetFullPath($root)
    if ((Split-Path $resolved -Parent) -ne [IO.Path]::GetTempPath().TrimEnd('\') -or (Split-Path $resolved -Leaf) -notmatch '^CodexHebdo-symbol-tests-[a-f0-9]{32}$') { throw 'Invalid test cleanup path.' }
    if ((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing junction cleanup.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
