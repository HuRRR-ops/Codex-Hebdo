# Return a transaction entry only for a dedicated, uncustomized portable install.
# Existing engine settings belong to the user and must never be replaced.
function Get-WindhawkIsolationPlan {
    param([string]$WindhawkPath, [string]$AppDataPath, [string]$InstallationFile)
    $result = @{ File=$null; Status='preserved'; Reason='Installation Windhawk non identifiee comme dediee a Codex Hebdo.' }
    $owned = Test-Path -LiteralPath (Join-Path $WindhawkPath 'codex-hebdo-managed.txt') -PathType Leaf
    if (!$owned -and (Test-Path -LiteralPath $InstallationFile -PathType Leaf)) {
        try {
            $previous = [IO.File]::ReadAllText($InstallationFile) | ConvertFrom-Json
            $owned = $previous.Portable -and $previous.NativeInstalled -and
                ([IO.Path]::GetFullPath($previous.WindhawkPath).TrimEnd('\') -eq $WindhawkPath.TrimEnd('\'))
        } catch { $owned = $false }
    }
    if (!$owned) { return $result }
    $mods = Join-Path $AppDataPath 'Engine\Mods'
    $others = @(Get-ChildItem -LiteralPath $mods -Filter '*.ini' -File -ErrorAction SilentlyContinue |
        Where-Object Name -ne 'codex-hebdo-taskbar.ini')
    if ($others.Count -gt 0) { $result.Reason='Autres mods Windhawk presents : ' + (($others | ForEach-Object BaseName) -join ', '); return $result }
    $result.Reason='Disposition portable Windhawk personnalisee ou non prise en charge.'
    # Only the portable layout created by our installer is eligible.
    $storageIni = Join-Path $WindhawkPath 'windhawk.ini'
    if (!(Test-Path -LiteralPath $storageIni -PathType Leaf) -or
        [IO.File]::ReadAllText($storageIni) -notmatch '(?m)^EnginePath=Engine\\1\.7\.3\s*$') { return $result }
    $engineIni = Join-Path $WindhawkPath 'Engine\1.7.3\engine.ini'
    if (!(Test-Path -LiteralPath $engineIni -PathType Leaf)) { return $result }
    $engineText = [IO.File]::ReadAllText($engineIni)
    if ($engineText -notmatch '(?m)^Portable=1\s*$' -or
        $engineText -notmatch '(?m)^AppDataPath=\.\.\\\.\.\\AppData\\Engine\s*$' -or
        [IO.Path]::GetFullPath($AppDataPath).TrimEnd('\') -ne (Join-Path $WindhawkPath 'AppData')) { return $result }
    $target = Join-Path $AppDataPath 'Engine\settings.ini'
    $text = "[Settings]`r`nExclude=*`r`nInclude=$(Join-Path $env:WINDIR 'explorer.exe')`r`n"
    if (Test-Path -LiteralPath $target) {
        $result.Reason='Reglages moteur existants : restriction exclusive a Explorer non confirmee.'
        if ([IO.File]::ReadAllText($target) -eq $text) { $result.Status='already-isolated' }
        return $result
    }
    $encoding = [Text.Encoding]::Unicode
    $result.File = @{ Target=$target; Bytes=[byte[]]($encoding.GetPreamble()+$encoding.GetBytes($text)) }
    $result.Status = 'apply'
    return $result
}
