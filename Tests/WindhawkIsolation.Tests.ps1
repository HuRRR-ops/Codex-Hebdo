$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '..\scripts\Windhawk-Isolation.ps1')
$root=Join-Path ([IO.Path]::GetTempPath()) ('CodexHebdo-isolation-tests-'+[guid]::NewGuid().ToString('N'))
$data=Join-Path $root 'AppData'
$metadata=Join-Path $root 'installation.json'
$marker=Join-Path $root 'codex-hebdo-managed.txt'
$settings=Join-Path $data 'Engine\settings.ini'
function Plan { Get-WindhawkIsolationPlan $root $data $metadata }
try {
    New-Item -ItemType Directory -Force "$data\Engine\Mods","$root\Engine\1.7.3" | Out-Null
    [IO.File]::WriteAllText("$root\Engine\1.7.3\engine.ini","[Storage]`r`nPortable=1`r`nAppDataPath=..\..\AppData\Engine")
    [IO.File]::WriteAllText("$root\windhawk.ini","[Storage]`r`nPortable=1`r`nEnginePath=Engine\1.7.3`r`nAppDataPath=AppData")
    if ((Plan).File) { throw 'Unowned installation modified.' }
    [IO.File]::WriteAllText($marker,'Created by Codex Hebdo')
    $plan=Plan
    if (!$plan.File -or $plan.File.Bytes[0] -ne 255 -or $plan.File.Bytes[1] -ne 254) { throw 'New dedicated install must use UTF16 settings.' }
    $text=[Text.Encoding]::Unicode.GetString($plan.File.Bytes,2,$plan.File.Bytes.Length-2)
    if ($text -cne "[Settings]`r`nExclude=*`r`nInclude=$env:WINDIR\explorer.exe`r`n") { throw 'Only the real Explorer executable must be included.' }
    [IO.File]::WriteAllBytes($settings,$plan.File.Bytes)
    if ((Plan).Status -ne 'already-isolated') { throw 'Not idempotent.' }
    [IO.File]::WriteAllText($settings,"[Settings]`nInclude=other.exe`nExclude=test.exe")
    if ((Plan).File) { throw 'Custom engine settings overwritten.' }
    Remove-Item -LiteralPath $settings
    [IO.File]::WriteAllText("$data\Engine\Mods\other.ini","[Mod]`nDisabled=1")
    if ((Plan).File) { throw 'Even disabled third-party mods must be preserved.' }
    Remove-Item -LiteralPath "$data\Engine\Mods\other.ini"
    Remove-Item -LiteralPath $marker
    [IO.File]::WriteAllText($metadata,(@{WindhawkPath=$root;Portable=$true;NativeInstalled=$true}|ConvertTo-Json))
    if (!(Plan).File) { throw 'Legacy dedicated installation not migrated.' }
    [IO.File]::WriteAllText($metadata,(@{WindhawkPath="$root\other";Portable=$true;NativeInstalled=$true}|ConvertTo-Json))
    if ((Plan).File) { throw 'Different installation claimed.' }
    [IO.File]::WriteAllText($marker,'Created by Codex Hebdo')
    [IO.File]::WriteAllText("$root\Engine\1.7.3\engine.ini","[Storage]`r`nPortable=1`r`nAppDataPath=elsewhere")
    if ((Plan).File) { throw 'Custom engine location modified.' }
    Write-Output '9 Windhawk isolation checks passed.'
} finally {
    $resolved=[IO.Path]::GetFullPath($root)
    if ((Split-Path $resolved -Parent) -ne [IO.Path]::GetTempPath().TrimEnd('\') -or (Split-Path $resolved -Leaf) -notmatch '^CodexHebdo-isolation-tests-[a-f0-9]{32}$') { throw 'Invalid test cleanup path.' }
    if ((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing junction cleanup.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
