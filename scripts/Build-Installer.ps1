param([string]$SevenZipPath = "$env:ProgramFiles\7-Zip")
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dist=Join-Path $root 'dist'
$stage=Join-Path ([IO.Path]::GetTempPath()) ('CodexHebdo-payload-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $stage,(Join-Path $stage 'dist'),(Join-Path $stage 'scripts'),(Join-Path $stage 'tools'),(Join-Path $stage 'licenses') | Out-Null
dotnet publish (Join-Path $root 'Companion') -c Release -r win-x64 --self-contained true -o (Join-Path $stage 'dist\Companion')
if($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }
Get-ChildItem -LiteralPath (Join-Path $stage 'dist\Companion') -Filter '*.pdb' -File | ForEach-Object { Remove-Item -LiteralPath $_.FullName }
foreach($file in @('codex-hebdo-taskbar.dll','codex-hebdo-taskbar.wh.cpp')) { Copy-Item -LiteralPath (Join-Path $dist $file) -Destination (Join-Path $stage 'dist') }
foreach($file in @('Install.ps1','Uninstall.ps1','LICENSE','THIRD-PARTY-NOTICES.txt')) { Copy-Item -LiteralPath (Join-Path $root $file) -Destination $stage }
Copy-Item -LiteralPath (Join-Path $root 'Installer\Bootstrap.ps1') -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'scripts\Prepare-Symbols.ps1') -Destination (Join-Path $stage 'scripts')
Get-ChildItem -LiteralPath (Join-Path $root 'licenses') -File | Copy-Item -Destination (Join-Path $stage 'licenses')
$sevenArchive=Join-Path $dist '7z2604-x64.exe'
$sevenSource=Join-Path $dist '7z2604-src.tar.xz'
foreach($asset in @(@($sevenArchive,'7z2604-x64.exe','d54bf805f9f3704d1e8db2fa3498ae7ef2df0312b40b558e7c71c734430a665d'),@($sevenSource,'7z2604-src.tar.xz','9691944c0fe0d01bb49373a704fb983fd33bc98b1738695179dfbf99ac1734f6'))) {
    if(!(Test-Path $asset[0])) { Invoke-WebRequest ('https://github.com/ip7z/7zip/releases/download/26.04/'+$asset[1]) -OutFile $asset[0] }
    if((Get-FileHash $asset[0] -Algorithm SHA256).Hash -ne $asset[2]) { throw '7-Zip integrity failure.' }
}
$sevenExtract=Join-Path ([IO.Path]::GetTempPath()) ('CodexHebdo-7zip-'+[guid]::NewGuid().ToString('N'))
& (Join-Path $SevenZipPath '7z.exe') x $sevenArchive "-o$sevenExtract" -y | Out-Null
if($LASTEXITCODE -ne 0) {throw '7-Zip extraction failed.'}
foreach($file in @('7z.exe','7z.dll')) { Copy-Item -LiteralPath (Join-Path $sevenExtract $file) -Destination (Join-Path $stage 'tools') }
Copy-Item -LiteralPath (Join-Path $sevenExtract 'License.txt') -Destination (Join-Path $stage 'licenses\7-Zip.txt')
Copy-Item -LiteralPath $sevenSource -Destination (Join-Path $stage 'licenses')
# Runtime NuGet packs include the redistribution notices. Preserve each available notice.
$nuget=Join-Path $env:USERPROFILE '.nuget\packages'
foreach($id in @('microsoft.netcore.app.runtime.win-x64')) {
    $pack=Get-ChildItem -LiteralPath (Join-Path $nuget $id) -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    foreach($name in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
        $notice=Get-ChildItem -LiteralPath $pack.FullName -Recurse -File | Where-Object Name -eq $name | Select-Object -First 1
        if(!$notice) { throw "Missing runtime notice: $id/$name" }
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $stage "licenses\$id-$name")
    }
}
# Windows PowerShell 5.1 requires a BOM for non-ASCII source text.
Get-ChildItem -LiteralPath $stage -Filter '*.ps1' -Recurse -File | ForEach-Object {
    [IO.File]::WriteAllText($_.FullName,[IO.File]::ReadAllText($_.FullName),[Text.UTF8Encoding]::new($true))
}
$manifest=[ordered]@{}
Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object {
    if($_.Name -match '^(preferences\.json|codex-path\.txt|installation\.json|probe\.json)$') { throw 'Personal file in payload.' }
    $manifest[$_.FullName.Substring($stage.Length+1)]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'manifest.json') -Encoding utf8
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath (Join-Path $dist 'setup-payload.zip') -Force
dotnet publish (Join-Path $root 'Installer') -c Release -o (Join-Path $dist 'Setup')
if($LASTEXITCODE -ne 0) { throw 'Installer publish failed.' }
$exe=Join-Path $dist 'CodexHebdo-1.2.0-Setup.exe'
Copy-Item -LiteralPath (Join-Path $dist 'Setup\CodexHebdo-Setup.exe') -Destination $exe -Force
$check=Start-Process -FilePath $exe -ArgumentList '--verify' -WindowStyle Hidden -Wait -PassThru
if($check.ExitCode -ne 0) { throw 'Embedded payload verification failed.' }
"$((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($exe))" | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS-1.2.0.txt') -Encoding ascii
Write-Output $exe
