$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\Installer\Bootstrap.ps1') -FunctionsOnly
$script:checks=0
function Assert([bool]$Value,[string]$Name) { if(!$Value) { throw $Name };$script:checks++ }
$cache=Join-Path ([IO.Path]::GetTempPath()) ('CodexHebdo-installer-tests-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $cache | Out-Null
$sample=Join-Path $cache 'sample'
[IO.File]::WriteAllText($sample,'verified payload')
$hash=(Get-FileHash -LiteralPath $sample -Algorithm SHA256).Hash
$script:attempts=0
function Invoke-WebRequest { param([switch]$UseBasicParsing,$Uri,$OutFile,$TimeoutSec)
    $script:attempts++
    if($script:attempts -lt 3) {throw 'Simulated connection failure.'}
    Copy-Item -LiteralPath $sample -Destination $OutFile
}
$result=Fetch 'asset.zip' 'https://example.invalid/never-requested' $hash
Assert ($script:attempts -eq 3 -and (Test-Path $result)) 'Transient errors must retry and eventually commit a verified download.'
[void](Fetch 'asset.zip' 'https://example.invalid/never-requested' $hash)
Assert ($script:attempts -eq 3) 'Verified cached downloads must not be downloaded again.'
$rejected=$false
try { [void](Fetch 'bad.zip' 'https://example.invalid/never-requested' ('0'*64)) } catch { $rejected=$true }
Assert ($rejected -and !(Test-Path (Join-Path $cache 'bad.zip'))) 'Corrupt downloads must never be committed.'
Assert (@(Get-ChildItem -LiteralPath $cache -Filter '*.download').Count -eq 0) 'Partial downloads must be removed.'
Check-Directory (Join-Path $cache 'new-folder')
$shell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$result=Run-Hidden $shell '-NoProfile -Command "Write-Output test-output; exit 7"' 10
Assert ($result.Code -eq 7 -and $result.Text.Contains('test-output')) 'Child exit status must be propagated without printing child output.'
$timedOut=$false
try { [void](Run-Hidden $shell '-NoProfile -Command "Start-Sleep -Seconds 10"' 1) } catch { $timedOut=$true }
Assert $timedOut 'Hung child processes must time out.'
$extraction=Join-Path $cache ('windhawk-extract-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $extraction | Out-Null
Set-Content -LiteralPath (Join-Path $extraction 'sample.txt') -Value 'temporary data'
Remove-Extraction $extraction
Assert (!(Test-Path -LiteralPath $extraction)) 'Extraction cleanup removes its validated staging directory.'
$rejected=$false
try {Remove-Extraction $cache} catch {$rejected=$true}
Assert ($rejected -and (Test-Path -LiteralPath $cache)) 'Cleanup must reject the cache root.'
Write-Output "$script:checks installer tests passed."
