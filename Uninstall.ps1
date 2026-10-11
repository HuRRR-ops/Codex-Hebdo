param()
$ErrorActionPreference = 'Stop'
$destination = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'CodexHebdo'))
$expected = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'CodexHebdo'))
if ($destination -ne $expected -or (Split-Path $destination -Leaf) -ne 'CodexHebdo') { throw 'Chemin de désinstallation invalide.' }
$config = Join-Path $destination 'installation.json'
if (!(Test-Path -LiteralPath $config)) { throw 'Installation CodexHebdo introuvable ; aucun fichier supprimé.' }
$installation = Get-Content -LiteralPath $config -Raw | ConvertFrom-Json
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -Path $run -Name 'CodexHebdo' -ErrorAction SilentlyContinue
Remove-ItemProperty -Path $run -Name 'CodexHebdo.Windhawk' -ErrorAction SilentlyContinue
$exe = Join-Path $destination 'CodexHebdo.exe'
if (Test-Path -LiteralPath $exe) {
    Start-Process -FilePath $exe -ArgumentList '--stop' -WindowStyle Hidden -Wait
    $running = Get-Process CodexHebdo -ErrorAction SilentlyContinue | Where-Object Path -eq $exe
    if ($running) { $running | Wait-Process -Timeout 15 }
}
if ($installation.Portable -and $installation.NativeInstalled) {
    $windhawk = [IO.Path]::GetFullPath($installation.WindhawkPath)
    $ini = Get-Content -LiteralPath (Join-Path $windhawk 'windhawk.ini') -Raw
    if ($ini -notmatch '(?m)^AppDataPath=(.+)$') { throw 'Configuration Windhawk inconnue.' }
    $data = [Environment]::ExpandEnvironmentVariables($Matches[1].Trim())
    if (![IO.Path]::IsPathRooted($data)) { $data = Join-Path $windhawk $data }
    $data = [IO.Path]::GetFullPath($data)
    $mod = Join-Path $data 'Engine\Mods\codex-hebdo-taskbar.ini'
    if (Test-Path -LiteralPath $mod) {
        $settings = (Get-Content -LiteralPath $mod -Raw) -replace '(?m)^Disabled=0','Disabled=1'
        Set-Content -LiteralPath $mod -Value $settings -Encoding utf8
        # Windhawk watches this directory and removes the native slot on disabling.
        Start-Sleep -Seconds 3
        Remove-Item -LiteralPath $mod
    }
    $source = Join-Path $data 'ModsSource\codex-hebdo-taskbar.wh.cpp'
    if (Test-Path -LiteralPath $source) { Remove-Item -LiteralPath $source }
    $metadata = Join-Path $data 'Engine\ModsWritable\codex-hebdo-taskbar.ini'
    if (Test-Path -LiteralPath $metadata) { Remove-Item -LiteralPath $metadata }
    Get-ChildItem -LiteralPath (Join-Path $data 'Engine\Mods\64') -Filter 'codex-hebdo-taskbar*.dll' | ForEach-Object {
        try { Remove-Item -LiteralPath $_.FullName } catch { Write-Warning "DLL encore chargée : $($_.FullName). Elle ne sera plus rechargée." }
    }
} else { Write-Host 'Dans Windhawk, supprimer le mod Codex Hebdo Taskbar.' }
# The target is fixed, checked above, and has our installation marker. Do not follow a junction.
if ((Get-Item -LiteralPath $destination).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refus de supprimer une jonction.' }
Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexHebdo' -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $destination -Recurse -Force
Write-Host 'Codex Hebdo désinstallé. Windhawk et Codex sont conservés.'
