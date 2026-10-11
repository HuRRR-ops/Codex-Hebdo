param([string]$Version = '1.2.1')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'dist'
$stage = Join-Path ([IO.Path]::GetTempPath()) ('CodexHebdo-release-' + [guid]::NewGuid().ToString('N'))
$package = Join-Path $stage "CodexHebdo-$Version-win-x64"
New-Item -ItemType Directory -Force $package,(Join-Path $package 'dist'),(Join-Path $package 'scripts') | Out-Null
foreach ($file in @('Install.ps1','Uninstall.ps1','LICENSE','THIRD-PARTY-NOTICES.txt')) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $package
}
Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $package -Recurse
Copy-Item -LiteralPath (Join-Path $root 'scripts\Prepare-Symbols.ps1') -Destination (Join-Path $package 'scripts')
Copy-Item -LiteralPath (Join-Path $output 'Companion') -Destination (Join-Path $package 'dist') -Recurse
foreach ($file in @('codex-hebdo-taskbar.dll','codex-hebdo-taskbar.wh.cpp')) {
    Copy-Item -LiteralPath (Join-Path $output $file) -Destination (Join-Path $package 'dist')
}
@'
@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
if errorlevel 1 (
  echo Installation failed. See the message above and INSTALLATION.txt.
  pause
  exit /b 1
)
pause
'@ | Set-Content -LiteralPath (Join-Path $package 'Installer.cmd') -Encoding ascii
@"
Codex Hebdo $Version - Windows 11 x64

FRANCAIS
1. Installer le runtime .NET Desktop 10 x64.
2. Installer et connecter Codex. codex.exe doit etre accessible dans PATH.
3. Disposer de Windhawk portable 1.7.3, avec son dossier Compiler.
4. Extraire TOUTE cette archive, puis lancer Installer.cmd.
   Pour un chemin Windhawk personnalise, ouvrir PowerShell dans ce dossier :
   .\Install.ps1 -WindhawkPath "C:\chemin\vers\Windhawk"
5. Double-cliquer sur le widget pour ouvrir les options.

Archive deja compilee : aucun SDK ou recompilation necessaire.
Les dependances ne sont pas incluses. Ce n'est pas un installateur tout-en-un.
Internet peut etre necessaire pour les symboles Windows.
Installation dans LOCALAPPDATA\CodexHebdo. Les preferences sont conservees.
Desinstallation : .\Uninstall.ps1

ENGLISH
Requires Windows 11 x64, .NET Desktop Runtime 10 x64, signed-in Codex available
as codex.exe in PATH, and portable Windhawk 1.7.3 including its Compiler folder.
Extract the entire archive and run Installer.cmd. No build or SDK is required.
Dependencies are not bundled. Internet may be needed for Windows symbols.
Double-click the widget for settings. Run Uninstall.ps1 to uninstall.

Project: https://github.com/HuRRR-ops/Codex-Hebdo
.NET: https://dotnet.microsoft.com/download/dotnet/10.0
Windhawk: https://windhawk.net/
"@ | Set-Content -LiteralPath (Join-Path $package 'INSTALLATION.txt') -Encoding utf8
$forbidden = Get-ChildItem -LiteralPath $package -Recurse -File | Where-Object { $_.Name -match '^(preferences\.json|codex-path\.txt|installation\.json|probe\.json)$' -or $_.Extension -in @('.pdb','.log','.key','.pem') }
if ($forbidden) { throw 'Package contains local files or debug artifacts.' }
$zip = Join-Path $output "CodexHebdo-$Version-win-x64.zip"
Compress-Archive -LiteralPath $package -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
Write-Output $zip
