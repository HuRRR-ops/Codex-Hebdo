param([ValidateSet('fr','en')][string]$Language='fr', [switch]$Unattended, [switch]$PrepareOnly, [string]$PreparationRoot, [switch]$FunctionsOnly)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
$ProgressPreference='SilentlyContinue'
function Say([string]$fr,[string]$en) { if($Language -eq 'fr') { Write-Output $fr } else { Write-Output $en } }
function Fail([string]$fr,[string]$en) { if($Language -eq 'fr') { throw $fr } else { throw $en } }
function Check-Directory([string]$Path) {
    $cursor=[IO.Path]::GetFullPath($Path)
    while($cursor) {
        if((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Installation path contains a junction or symbolic link.' }
        $parent=Split-Path $cursor -Parent
        if($parent -eq $cursor) {break};$cursor=$parent
    }
}
function Fetch([string]$Name,[string]$Url,[string]$Hash) {
    $target=Join-Path $cache $Name
    if((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $Hash) { return $target }
    for($attempt=1;$attempt -le 3;$attempt++) {
        $part=Join-Path $cache ($Name+'.'+[guid]::NewGuid().ToString('N')+'.download')
        try {
            Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $part -TimeoutSec 600
            if((Get-FileHash -LiteralPath $part -Algorithm SHA256).Hash -ne $Hash) { throw 'SHA-256 mismatch.' }
            Move-Item -LiteralPath $part -Destination $target -Force
            return $target
        } catch {
            if($attempt -eq 3) { throw "Download failed: $Name. Check your Internet connection and retry. $($_.Exception.Message)" }
            Start-Sleep -Seconds 2
        } finally { if(Test-Path -LiteralPath $part) { Remove-Item -LiteralPath $part -Force } }
    }
}
function Run-Hidden([string]$File,[string]$Arguments,[int]$Timeout=60) {
    $info=New-Object Diagnostics.ProcessStartInfo
    $info.FileName=$File;$info.Arguments=$Arguments;$info.UseShellExecute=$false;$info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $process=New-Object Diagnostics.Process
    $process.StartInfo=$info
    try {
        [void]$process.Start()
        $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        if(!$process.WaitForExit($Timeout*1000)) { $process.Kill();$process.WaitForExit();throw 'Process timed out. Retry the installation.' }
        # Never persist or forward Codex authentication output.
        return @{ Code=$process.ExitCode; Text=($stdout.Result+$stderr.Result) }
    } finally { $process.Dispose() }
}
function Remove-Extraction([string]$Path) {
    $resolved=(Resolve-Path -LiteralPath $Path).Path
    $parent=(Resolve-Path -LiteralPath $cache).Path
    if(!(Split-Path $resolved -Parent).Equals($parent,[StringComparison]::OrdinalIgnoreCase) -or !(Split-Path $resolved -Leaf).StartsWith('windhawk-extract-')) { throw 'Invalid extraction cleanup path.' }
    Check-Directory $resolved
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
if($FunctionsOnly) { return }
try {
    if(![Environment]::Is64BitOperatingSystem -or ![Environment]::Is64BitProcess -or $env:PROCESSOR_ARCHITECTURE -ne 'AMD64') { throw 'Windows 11 x64 is required.' }
    $build=[int](Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuildNumber
    if($build -lt 22000) { throw 'Windows 11 is required.' }
    if($PreparationRoot -and !$PrepareOnly) { throw 'PreparationRoot requires PrepareOnly.' }
    $base=if($PreparationRoot) { [IO.Path]::GetFullPath($PreparationRoot) } else { $env:LOCALAPPDATA }
    Check-Directory $base
    $cache=Join-Path $base 'CodexHebdo-Setup\Downloads'
    New-Item -ItemType Directory -Force $cache | Out-Null
    $disk=New-Object IO.DriveInfo ([IO.Path]::GetPathRoot($base))
    if($disk.AvailableFreeSpace -lt 3GB) { throw 'At least 3 GB of free disk space is required.' }
    Say 'Vérification de Windhawk…' 'Checking Windhawk…'
    $windhawk=Join-Path $base 'Programs\Windhawk'
    if(!$PreparationRoot) {
        foreach($standard in @((Join-Path $env:ProgramFiles 'Windhawk'),(Join-Path ${env:ProgramFiles(x86)} 'Windhawk'))) {
            if((Test-Path (Join-Path $standard 'windhawk.exe')) -and (!(Test-Path (Join-Path $standard 'windhawk.ini')) -or (Get-Content (Join-Path $standard 'windhawk.ini') -Raw) -notmatch '(?m)^Portable=1\s*$')) {
                Fail 'Windhawk standard est déjà installé. Cette version automatique utilise Windhawk portable et ne remplace pas votre installation standard. Consultez le guide technique.' 'Standard Windhawk is already installed. This automatic version uses portable Windhawk and will not replace your standard installation. See the technical guide.'
            }
        }
        $running=@(Get-Process windhawk -ErrorAction SilentlyContinue | Where-Object Path)
        foreach($instance in $running) {
            $candidate=Split-Path $instance.Path -Parent
            if((Test-Path (Join-Path $candidate 'windhawk.ini')) -and (Get-Content (Join-Path $candidate 'windhawk.ini') -Raw) -match '(?m)^Portable=1\s*$') { $windhawk=$candidate;break }
            Fail 'Une installation standard de Windhawk est active. Fermez cet installateur et utilisez la procédure standard du guide technique pour conserver vos mods.' 'A standard Windhawk installation is active. Close this installer and follow the standard installation procedure in the technical guide to preserve your mods.'
        }
    }
    Check-Directory $windhawk
    if(Test-Path (Join-Path $windhawk 'windhawk.exe')) {
        if(!(Test-Path (Join-Path $windhawk 'windhawk.ini')) -or (Get-Content (Join-Path $windhawk 'windhawk.ini') -Raw) -notmatch '(?m)^Portable=1\s*$') { throw 'An incompatible Windhawk installation already exists at the destination.' }
        if(!(Test-Path (Join-Path $windhawk 'Engine\1.7.3\64\windhawk.dll'))) { throw 'This installer requires Windhawk portable 1.7.3. Your existing installation was preserved.' }
    } else {
        Say 'Téléchargement de Windhawk 1.7.3 (149 Mo)…' 'Downloading Windhawk 1.7.3 (149 MB)…'
        $archive=Fetch 'windhawk-1.7.3.exe' 'https://github.com/ramensoftware/windhawk/releases/download/v1.7.3/windhawk_setup_offline.exe' '4d93016570f982326eebdfc9068e924ff21f6448534ea32672bb4c1d52a8193b'
        $signature=Get-AuthenticodeSignature -LiteralPath $archive
        if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Michael Maltsev') { throw 'Windhawk signature verification failed.' }
        $unpack=Join-Path $cache ('windhawk-extract-'+[guid]::NewGuid().ToString('N'))
        Say 'Installation de Windhawk portable…' 'Installing portable Windhawk…'
        $result=Run-Hidden (Join-Path $PSScriptRoot 'tools\7z.exe') ('x "'+$archive+'" -o"'+$unpack+'" -y') 180
        if($result.Code -ne 0) { throw 'Windhawk extraction failed.' }
        $engine=Get-ChildItem -LiteralPath (Join-Path $unpack 'Engine') -Directory | Where-Object {Test-Path (Join-Path $_.FullName '64\windhawk.dll')} | Select-Object -First 1
        if(!$engine) { throw 'Windhawk engine is missing.' }
        $pending=$windhawk+'.pending-'+[guid]::NewGuid().ToString('N')
        New-Item -ItemType Directory -Force $pending,(Join-Path $pending 'Engine'),(Join-Path $pending 'AppData') | Out-Null
        foreach($directory in @('Compiler','UI')) { Copy-Item -LiteralPath (Join-Path $unpack $directory) -Destination $pending -Recurse }
        Copy-Item -LiteralPath $engine.FullName -Destination (Join-Path $pending 'Engine\1.7.3') -Recurse
        foreach($file in @('windhawk.exe','windhawk-x64-helper.exe','command-line.txt')) { Copy-Item -LiteralPath (Join-Path $unpack $file) -Destination $pending }
        "[Storage]`r`nPortable=1`r`nEnginePath=Engine\1.7.3`r`nUIPath=UI`r`nCompilerPath=Compiler`r`nAppDataPath=AppData" | Set-Content (Join-Path $pending 'windhawk.ini') -Encoding Unicode
        "[Storage]`r`nPortable=1`r`nAppDataPath=..\..\AppData\Engine" | Set-Content (Join-Path $pending 'Engine\1.7.3\engine.ini') -Encoding Unicode
        New-Item -ItemType Directory -Force (Split-Path $windhawk -Parent) | Out-Null
        if(Test-Path -LiteralPath $windhawk) { throw 'Windhawk destination already exists; refusing to overwrite it.' }
        Move-Item -LiteralPath $pending -Destination $windhawk
        Remove-Extraction $unpack
    }
    # Win32 profile APIs require UTF-16 for Unicode INI files. Repair earlier UTF-8 BOM files.
    foreach($relative in @('windhawk.ini','Engine\1.7.3\engine.ini')) {
        $config=Join-Path $windhawk $relative
        $bytes=[IO.File]::ReadAllBytes($config)
        if($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) {
            [IO.File]::WriteAllText($config,[IO.File]::ReadAllText($config),[Text.Encoding]::Unicode)
        }
    }
    Say 'Vérification de Codex…' 'Checking Codex…'
    $codex=$null
    if(!$PreparationRoot) {
        $command=Get-Command codex.exe -ErrorAction SilentlyContinue
        $candidates=@($(if($command){$command.Source}), (Join-Path $base 'Programs\OpenAI\Codex\bin\codex.exe'),(Join-Path $base 'Programs\CodexHebdo-Codex\codex.exe'))
        foreach($candidate in $candidates) { if($candidate -and (Test-Path -LiteralPath $candidate)) { $codex=$candidate;break } }
    }
    $managedCodex=Join-Path $base 'Programs\CodexHebdo-Codex\codex.exe'
    if(!$codex -and (Test-Path -LiteralPath $managedCodex)) { $codex=$managedCodex }
    if(!$codex) {
        Say 'Téléchargement de Codex CLI (165 Mo)…' 'Downloading Codex CLI (165 MB)…'
        $archive=Fetch 'codex-0.162.1.zip' 'https://github.com/openai/codex/releases/download/rust-v0.162.1/codex-x86_64-pc-windows-msvc.exe.zip' '4b17206ed3045de64064b2016627d51fba3bde386d03838312054882a64d781d'
        $codexFolder=Join-Path $base 'Programs\CodexHebdo-Codex'
        Check-Directory $codexFolder
        $pending=$codexFolder+'.pending-'+[guid]::NewGuid().ToString('N')
        Expand-Archive -LiteralPath $archive -DestinationPath $pending
        $binary=Get-ChildItem -LiteralPath $pending -Filter 'codex-x86_64-pc-windows-msvc.exe' -Recurse | Select-Object -First 1
        if(!$binary) { throw 'Codex executable is missing from the archive.' }
        Rename-Item -LiteralPath $binary.FullName -NewName 'codex.exe'
        if(Test-Path -LiteralPath $codexFolder) { throw 'Codex dependency directory already exists but is incomplete.' }
        Move-Item -LiteralPath $pending -Destination $codexFolder
        $codex=(Get-ChildItem -LiteralPath $codexFolder -Filter 'codex.exe' -Recurse | Select-Object -First 1).FullName
    }
    $version=Run-Hidden $codex '--version'
    if($version.Code -ne 0) { throw 'Codex could not start.' }
    if($PrepareOnly) { Say 'Préparation vérifiée. Aucun widget activé, aucun compte modifié.' 'Preparation verified. No widget activated and no account modified.';exit 0 }
    Say 'Vérification de la connexion au compte…' 'Checking account sign-in…'
    $auth=Run-Hidden $codex 'login status'
    if($auth.Code -ne 0) {
        if($Unattended) { throw 'Codex sign-in required. Run the installer interactively.' }
        Say 'Connectez-vous dans le navigateur ouvert par Codex. Attente de la connexion (10 minutes maximum)…' 'Sign in using the browser opened by Codex. Waiting for sign-in (up to 10 minutes)…'
        $auth=Run-Hidden $codex 'login' 600
        if($auth.Code -ne 0) { throw 'Codex sign-in did not complete. Please retry.' }
    }
    Say 'Installation du widget et préparation des symboles Windows…' 'Installing the widget and preparing Windows symbols…'
    # The launcher acknowledges the non-cancellable commit phase before any app mutation.
    $deadline=[DateTime]::UtcNow.AddSeconds(30)
    while(!(Test-Path (Join-Path $PSScriptRoot 'commit.allowed'))) {
        if([DateTime]::UtcNow -gt $deadline) { throw 'Installer commit was not acknowledged.' }
        Start-Sleep -Milliseconds 100
    }
    Check-Directory (Join-Path $env:LOCALAPPDATA 'CodexHebdo')
    & (Join-Path $PSScriptRoot 'Install.ps1') -WindhawkPath $windhawk -CodexPath $codex -RegisterUninstall
    Say 'Codex Hebdo est installé. Les préférences existantes sont conservées.' 'Codex Hebdo is installed. Existing preferences were preserved.'
    exit 0
} catch {
    Write-Output ('ERROR: '+$_.Exception.Message)
    exit 1
}
