$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '..\scripts\Install-Transaction.ps1')
$root = Join-Path ([IO.Path]::GetTempPath()) ('CodexHebdo-transaction-tests-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$regPath = 'Software\CodexHebdo-Tests\'+[guid]::NewGuid().ToString('N')
$script:checks=0
function Assert([bool]$Value,[string]$Message) { if (!$Value) { throw $Message }; $script:checks++ }
$state = @{ Stops=0; Starts=0; Restarts=0 }
$stop = { $state.Stops++ }
$start = { $state.Starts++ }
$restart = { $state.Restarts++ }
$old = Join-Path $root 'companion.exe'
$config = Join-Path $root 'mod.ini'
$new = Join-Path $root 'new\native.dll'
$prefs = Join-Path $root 'preferences.json'
[IO.File]::WriteAllText($old,'old companion')
[IO.File]::WriteAllText($config,'old mod')
[IO.File]::WriteAllText($prefs,'user choices')
$files = @(@{Target=$old;Bytes=[Text.Encoding]::UTF8.GetBytes('new companion')},
    @{Target=$new;Bytes=[Text.Encoding]::UTF8.GetBytes('new dll')},
    @{Target=$config;Bytes=[Text.Encoding]::UTF8.GetBytes('new mod')})
$registry = @(@{Key=$regPath;Name='Existing';Value='new startup'},@{Key=$regPath;Name='Added';Value='added'})
$key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($regPath)
try { $key.SetValue('Existing','%OLD%',[Microsoft.Win32.RegistryValueKind]::ExpandString) } finally { $key.Dispose() }
try {
    $failed=$false
    try { Invoke-InstallTransaction -Files @(@{Source=(Join-Path $root 'missing.dll');Target=$new}) -Stop $stop -Start $start -RestartPrevious $restart }
    catch { $failed=$true }
    Assert ($failed -and $state.Stops -eq 0 -and [IO.File]::ReadAllText($old) -eq 'old companion') 'Missing payload must fail before stopping or writing.'

    $failed=$false
    try { Invoke-InstallTransaction -Files $files -RegistryValues $registry -Stop $stop -Start { $state.Starts++;throw 'Injected launch failure' } -RestartPrevious $restart }
    catch { $failed=$_.Exception.Message -like '*Injected launch failure*' }
    Assert $failed 'Launch failure must be reported.'
    Assert ([IO.File]::ReadAllText($old) -eq 'old companion' -and [IO.File]::ReadAllText($config) -eq 'old mod') 'Companion and Windhawk config must roll back.'
    Assert (!(Test-Path $new) -and !(Test-Path (Split-Path $new -Parent))) 'New files and empty directories must roll back.'
    $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($regPath)
    try {
        Assert ($key.GetValue('Existing',$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) -eq '%OLD%' -and $key.GetValueKind('Existing') -eq [Microsoft.Win32.RegistryValueKind]::ExpandString) 'Registry value and kind must roll back.'
        Assert ($key.GetValueNames() -notcontains 'Added') 'New registry values must roll back.'
    } finally { $key.Dispose() }
    Assert ($state.Stops -eq 2 -and $state.Restarts -eq 1) 'Stop new process before rollback and restart previous companion.'
    Assert ([IO.File]::ReadAllText($prefs) -eq 'user choices') 'Preferences must remain untouched.'

    $lock=[IO.File]::Open($config,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try {
        $failed=$false
        try { Invoke-InstallTransaction -Files $files -Stop $stop -Start $start -RestartPrevious $restart }
        catch { $failed=$true }
        Assert ($failed -and [IO.File]::ReadAllText($old) -eq 'old companion' -and !(Test-Path $new)) 'Mid-copy failure must restore earlier files.'
        Assert ($state.Restarts -eq 2) 'A locked unchanged target must not prevent restarting the previous companion.'
    } finally { $lock.Dispose() }

    Invoke-InstallTransaction -Files $files -RegistryValues $registry -Stop $stop -Start $start -RestartPrevious $restart
    Assert ([IO.File]::ReadAllText($old) -eq 'new companion' -and [IO.File]::ReadAllText($config) -eq 'new mod' -and (Test-Path $new)) 'Successful transaction must commit all files.'
    $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($regPath)
    try { Assert ($key.GetValue('Existing') -eq 'new startup' -and $key.GetValue('Added') -eq 'added') 'Successful transaction must commit registry values.' }
    finally { $key.Dispose() }
    Write-Output "$script:checks installation transaction checks passed."
} finally {
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($regPath,$false)
    $resolved=[IO.Path]::GetFullPath($root)
    if ((Split-Path $resolved -Parent) -ne [IO.Path]::GetTempPath().TrimEnd('\') -or (Split-Path $resolved -Leaf) -notmatch '^CodexHebdo-transaction-tests-[a-f0-9]{32}$') { throw 'Invalid test cleanup path.' }
    Assert-InstallPath $resolved
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
