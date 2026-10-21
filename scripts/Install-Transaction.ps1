# Shared by the manual installer and Setup. No application state changes on import.
function Assert-InstallPath([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Installation path contains a link: $cursor" }
        $parent = Split-Path $cursor -Parent
        if ($parent -eq $cursor) { break }; $cursor = $parent
    }
}

function Invoke-InstallTransaction {
    param([array]$Files, [array]$RegistryValues = @(), [scriptblock]$Stop,
        [scriptblock]$Start, [scriptblock]$RestartPrevious)
    $ErrorActionPreference = 'Stop'
    $tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    $stage = Join-Path $tempParent ('CodexHebdo-transaction-' + [guid]::NewGuid().ToString('N'))
    Assert-InstallPath $stage
    New-Item -ItemType Directory -Path $stage | Out-Null
    $journal = New-Object 'System.Collections.Generic.List[object]'
    $registryJournal = New-Object 'System.Collections.Generic.List[object]'
    $directories = New-Object 'System.Collections.Generic.List[string]'
    $stopped = $false; $startAttempted = $false; $keepBackup = $false
    try {
        # Copy and snapshot everything before stopping the installed companion.
        foreach ($entry in $Files) {
            $target = [IO.Path]::GetFullPath($entry.Target)
            Assert-InstallPath $target
            if (Test-Path -LiteralPath $target -PathType Container) { throw "Expected a file: $target" }
            if (@($journal | Where-Object { $_.Target -eq $target }).Count) { throw "Duplicate installation target: $target" }
            $index = $journal.Count
            $prepared = Join-Path $stage "$index.new"
            if ($entry.ContainsKey('Bytes')) { [IO.File]::WriteAllBytes($prepared, $entry.Bytes) }
            else { [IO.File]::Copy($entry.Source, $prepared) }
            $backup = Join-Path $stage "$index.old"
            $existed = Test-Path -LiteralPath $target -PathType Leaf
            if ($existed) { [IO.File]::Copy($target, $backup) }
            $journal.Add(@{ Target=$target; Prepared=$prepared; Backup=$backup; Existed=$existed; Touched=$false })
        }
        foreach ($entry in $RegistryValues) {
            $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($entry.Key)
            try {
                $exists = $null -ne $key -and $key.GetValueNames() -contains $entry.Name
                $registryJournal.Add(@{ Key=$entry.Key; Name=$entry.Name; Value=$entry.Value;
                    Existed=$exists; Touched=$false; KeyExisted=($null -ne $key);
                    OldValue=$(if ($exists) { $key.GetValue($entry.Name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) });
                    OldKind=$(if ($exists) { $key.GetValueKind($entry.Name) }) })
            } finally { if ($key) { $key.Dispose() } }
        }
        # Retained backups include a manifest if rollback itself fails.
        @{ Files=$journal.ToArray(); Registry=$registryJournal.ToArray() } | ConvertTo-Json -Depth 6 |
            Set-Content -LiteralPath (Join-Path $stage 'rollback.json') -Encoding UTF8
        $stopped = $true
        & $Stop
        foreach ($entry in $journal) {
            Assert-InstallPath $entry.Target
            $parent = Split-Path $entry.Target -Parent
            $missing = New-Object 'System.Collections.Generic.List[string]'
            for ($cursor=$parent; !(Test-Path -LiteralPath $cursor); $cursor=Split-Path $cursor -Parent) { $missing.Add($cursor) }
            for ($i=$missing.Count-1; $i -ge 0; $i--) {
                New-Item -ItemType Directory -Path $missing[$i] | Out-Null
                $directories.Add($missing[$i])
            }
            $entry.Touched = $true
            [IO.File]::Copy($entry.Prepared, $entry.Target, $true)
        }
        foreach ($entry in $registryJournal) {
            $entry.Touched = $true
            $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($entry.Key)
            try { $key.SetValue($entry.Name, $entry.Value, [Microsoft.Win32.RegistryValueKind]::String) }
            finally { $key.Dispose() }
        }
        $startAttempted = $true
        & $Start
    } catch {
        $failure = $_
        $errors = New-Object 'System.Collections.Generic.List[string]'
        if ($startAttempted) { try { & $Stop } catch { $errors.Add($_.Exception.Message) } }
        for ($i=$registryJournal.Count-1; $i -ge 0; $i--) {
            $entry = $registryJournal[$i]
            if (!$entry.Touched) { continue }
            try {
                $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($entry.Key)
                try {
                    if ($entry.Existed) { $key.SetValue($entry.Name,$entry.OldValue,$entry.OldKind) }
                    else { $key.DeleteValue($entry.Name,$false) }
                    $empty = $key.ValueCount -eq 0 -and $key.SubKeyCount -eq 0
                } finally { $key.Dispose() }
                if (!$entry.KeyExisted -and $empty) { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($entry.Key,$false) }
            } catch { $errors.Add($_.Exception.Message) }
        }
        for ($i=$journal.Count-1; $i -ge 0; $i--) {
            $entry = $journal[$i]
            if (!$entry.Touched) { continue }
            try {
                Assert-InstallPath $entry.Target
                if ($entry.Existed) {
                    # A failed copy to a locked file may not have changed it at all.
                    $unchanged = (Test-Path -LiteralPath $entry.Target -PathType Leaf) -and
                        (Get-FileHash -LiteralPath $entry.Target -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $entry.Backup -Algorithm SHA256).Hash
                    if (!$unchanged) { [IO.File]::Copy($entry.Backup,$entry.Target,$true) }
                }
                elseif (Test-Path -LiteralPath $entry.Target) {
                    # Windhawk observes the restored INI asynchronously and can briefly
                    # retain the newly loaded DLL. Never kill Explorer to remove it.
                    for ($attempt=0; ; $attempt++) {
                        try { [IO.File]::Delete($entry.Target); break }
                        catch {
                            if ($attempt -ge 9) { throw }
                            Start-Sleep -Milliseconds 200
                        }
                    }
                }
            } catch { $errors.Add($_.Exception.Message) }
        }
        for ($i=$directories.Count-1; $i -ge 0; $i--) {
            try { [IO.Directory]::Delete($directories[$i],$false) } catch { } # Never remove unrelated contents.
        }
        if ($stopped -and $errors.Count -eq 0) {
            try { & $RestartPrevious } catch { $errors.Add($_.Exception.Message) }
        }
        if ($errors.Count) {
            $keepBackup = $true
            throw "Installation failed: $($failure.Exception.Message). Rollback incomplete; backups: $stage. $($errors -join '; ')"
        }
        throw $failure
    } finally {
        if (!$keepBackup) {
            $resolved = [IO.Path]::GetFullPath($stage)
            if ((Split-Path $resolved -Parent) -ne $tempParent -or (Split-Path $resolved -Leaf) -notmatch '^CodexHebdo-transaction-[a-f0-9]{32}$') { throw 'Invalid transaction cleanup path.' }
            Assert-InstallPath $resolved
            try { Remove-Item -LiteralPath $resolved -Recurse -Force } catch { Write-Warning "Staging cleanup failed: $resolved" }
        }
    }
}
