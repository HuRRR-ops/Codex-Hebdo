param([string]$ExtractedPath)
$ErrorActionPreference = 'Stop'
$destination = Join-Path $env:LOCALAPPDATA 'Programs\Windhawk'
if (Test-Path (Join-Path $destination 'windhawk.exe')) {
    # Repair INIs produced by older versions under Windows PowerShell 5.1.
    # Leave standard installations and every configuration value unchanged.
    foreach ($relative in @('windhawk.ini','Engine\1.7.3\engine.ini')) {
        $config = Join-Path $destination $relative
        if (!(Test-Path -LiteralPath $config -PathType Leaf)) { continue }
        $bytes = [IO.File]::ReadAllBytes($config)
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) {
            [IO.File]::WriteAllText($config,[IO.File]::ReadAllText($config),[Text.Encoding]::Unicode)
        }
    }
    Write-Output $destination; return
}
if (!$ExtractedPath) {
    $sevenZip = Join-Path $env:ProgramFiles '7-Zip\7z.exe'
    if (!(Test-Path -LiteralPath $sevenZip)) { throw '7-Zip absent. Installer Windhawk depuis https://windhawk.net, ou fournir -ExtractedPath.' }
    $stage = Join-Path ([IO.Path]::GetTempPath()) ('CodexHebdo-Windhawk-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $stage | Out-Null
    $archive = Join-Path $stage 'windhawk_setup_offline.exe'
    Invoke-WebRequest 'https://github.com/ramensoftware/windhawk/releases/download/v1.7.3/windhawk_setup_offline.exe' -OutFile $archive
    $signature = Get-AuthenticodeSignature -LiteralPath $archive
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Michael Maltsev') { throw 'Signature Windhawk invalide.' }
    $ExtractedPath = Join-Path $stage 'extracted'
    & $sevenZip x $archive "-o$ExtractedPath" -y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Extraction Windhawk échouée.' }
}
$ExtractedPath = (Resolve-Path -LiteralPath $ExtractedPath).Path
$signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $ExtractedPath 'windhawk.exe')
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Michael Maltsev') { throw 'Exécutable Windhawk non vérifié.' }
$engine = Get-ChildItem -LiteralPath (Join-Path $ExtractedPath 'Engine') -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName '64\windhawk.dll') } | Select-Object -First 1
if (!$engine) { throw 'Moteur Windhawk absent.' }
New-Item -ItemType Directory -Force $destination,(Join-Path $destination 'Engine'),(Join-Path $destination 'AppData') | Out-Null
foreach ($directory in @('Compiler','UI')) {
    Copy-Item -LiteralPath (Join-Path $ExtractedPath $directory) -Destination $destination -Recurse
}
Copy-Item -LiteralPath $engine.FullName -Destination (Join-Path $destination 'Engine\1.7.3') -Recurse
foreach ($file in @('windhawk.exe','windhawk-x64-helper.exe','command-line.txt')) {
    Copy-Item -LiteralPath (Join-Path $ExtractedPath $file) -Destination $destination
}
@'
[Storage]
Portable=1
EnginePath=Engine\1.7.3
UIPath=UI
CompilerPath=Compiler
AppDataPath=AppData
'@ | Set-Content -LiteralPath (Join-Path $destination 'windhawk.ini') -Encoding Unicode
@'
[Storage]
Portable=1
AppDataPath=..\..\AppData\Engine
'@ | Set-Content -LiteralPath (Join-Path $destination 'Engine\1.7.3\engine.ini') -Encoding Unicode
Write-Output $destination
