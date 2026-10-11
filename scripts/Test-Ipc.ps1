param([ValidateSet('S','R','V')][string]$Command = 'S')
$ErrorActionPreference = 'Stop'
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$session = (Get-Process -Id $PID).SessionId
$pipe = [IO.Pipes.NamedPipeClientStream]::new('.', "CodexHebdo-v1-$sid-$session", [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
try {
    $pipe.Connect(2000)
    $pipe.WriteByte([byte][char]$Command)
    $buffer = [byte[]]::new(56)
    $offset = 0
    $deadline = [Threading.CancellationTokenSource]::new(2000)
    try {
        while ($offset -lt 56) {
            $n = $pipe.ReadAsync($buffer,$offset,56-$offset,$deadline.Token).GetAwaiter().GetResult()
            if ($n -eq 0) { throw 'Trame IPC incomplète.' }
            $offset += $n
        }
    } finally { $deadline.Dispose() }
    if ([BitConverter]::ToUInt32($buffer,0) -ne 0x31444843 -or [BitConverter]::ToUInt32($buffer,4) -ne 2) { throw 'Protocole inattendu.' }
    [pscustomobject]@{ Valid=[BitConverter]::ToUInt32($buffer,8); Remaining=[BitConverter]::ToDouble($buffer,16); ResetsAt=[BitConverter]::ToInt64($buffer,24); ObservedAt=[BitConverter]::ToInt64($buffer,32); ResetCredits=[BitConverter]::ToInt64($buffer,40); CreditsObservedAt=[BitConverter]::ToInt64($buffer,48) }
    if ($Command -eq 'V') {
        $options = [byte[]]::new(128)
        $deadline = [Threading.CancellationTokenSource]::new(2000)
        try { $pipe.ReadExactlyAsync($options,$deadline.Token).GetAwaiter().GetResult() } finally { $deadline.Dispose() }
        if ([BitConverter]::ToInt32($options,0) -ne 1) { throw 'Version des préférences inattendue.' }
        $length = [BitConverter]::ToInt32($options,100)
        if ($length -lt 0 -or $length -gt 131072) { throw 'Logo trop grand.' }
        if ($length) {
            $png = [byte[]]::new($length)
            $deadline = [Threading.CancellationTokenSource]::new(2000)
            try { $pipe.ReadExactlyAsync($png,$deadline.Token).GetAwaiter().GetResult() } finally { $deadline.Dispose() }
        }
        [pscustomobject]@{ SettingsVersion=1; Revision=[BitConverter]::ToInt32($options,4); CounterGap=[BitConverter]::ToInt32($options,72); Language=(@('fr','en')[[BitConverter]::ToInt32($options,104)]); LogoBytes=$length }
    }
} finally { $pipe.Dispose() }
