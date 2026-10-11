param([string]$WindhawkPath, [switch]$SkipNative)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$native = Join-Path $root 'Windhawk\codex-hebdo-taskbar.wh.cpp'
$parts = @('header.inc','helpers.inc','logo.inc','main.inc') | ForEach-Object { Get-Content -LiteralPath (Join-Path $root "Windhawk\$_") -Raw }
[IO.File]::WriteAllText($native, ($parts -join "`n"), [Text.UTF8Encoding]::new($false))
dotnet run --project (Join-Path $root 'Tests') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests échoués.' }
dotnet publish (Join-Path $root 'Companion') -c Release -r win-x64 --self-contained false -o (Join-Path $dist 'Companion')
if ($LASTEXITCODE -ne 0) { throw 'Compilation .NET échouée.' }
# Package symbols are unnecessary at runtime (Skia native PDBs exceed 100 MB).
Get-ChildItem -LiteralPath (Join-Path $dist 'Companion') -Filter '*.pdb' -File | ForEach-Object { Remove-Item -LiteralPath $_.FullName }
Copy-Item -LiteralPath $native -Destination $dist
if (!$SkipNative) {
    if (!$WindhawkPath) { $WindhawkPath = Join-Path ${env:ProgramFiles} 'Windhawk' }
    $compiler = Join-Path $WindhawkPath 'Compiler\bin\clang++.exe'
    if (!(Test-Path -LiteralPath $compiler)) { throw 'Compilateur absent. Fournir -WindhawkPath ou utiliser -SkipNative.' }
    $options = @('-std=c++23','-O2','-shared','-DUNICODE','-D_UNICODE','-DWINVER=0x0A00','-D_WIN32_WINNT=0x0A00',
        '-D_WIN32_IE=0x0A00','-DNTDDI_VERSION=0x0A000008','-D__USE_MINGW_ANSI_STDIO=0','-DWH_MOD',
        '-DWH_MOD_ID=L"codex-hebdo-taskbar"','-DWH_MOD_VERSION=L"1.1.1"',
        (Join-Path $WindhawkPath 'Engine\1.7.3\64\windhawk.lib'),
        '-include','windhawk_api.h','-target','x86_64-w64-mingw32','-Wl,--export-all-symbols',
        '-o',(Join-Path $dist 'codex-hebdo-taskbar.dll'),$native,
        '-lole32','-loleaut32','-lruntimeobject','-luuid','-ladvapi32','-lshlwapi','-lshcore')
    # Engine version is discovered, not tied to the version used during development.
    $lib = Get-ChildItem (Join-Path $WindhawkPath 'Engine') -Filter windhawk.lib -Recurse | Where-Object { $_.Directory.Name -eq '64' } | Select-Object -First 1
    if (!$lib) { throw 'Bibliothèque Windhawk x64 introuvable.' }
    $options[13] = $lib.FullName
    & $compiler @options
    if ($LASTEXITCODE -ne 0) { throw 'Compilation Windhawk échouée.' }
}
Write-Host "Compilation terminée : $dist"
