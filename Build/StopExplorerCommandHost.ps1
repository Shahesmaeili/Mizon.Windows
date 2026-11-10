$ErrorActionPreference = 'Stop'

# Release only this user's Mizon COM surrogate, before deployment copies its DLLs.
# Explorer itself and other applications' COM servers must remain running.
$sessionId = (Get-Process -Id $PID).SessionId
$mizonHostPattern = '/Processid:\{(?:E72D6A3B-29A8-4B1F-B0FB-D78D3DF8B8B7|C1B40E19-79ED-4A8A-A099-9F7B4A5DD7E3)\}'
$commandHosts = Get-CimInstance Win32_Process -Filter "Name = 'dllhost.exe'" |
    Where-Object { $_.SessionId -eq $sessionId } |
    Where-Object { $_.CommandLine -match $mizonHostPattern }

foreach ($commandHost in $commandHosts) {
    $process = Get-Process -Id $commandHost.ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) { continue }
    try {
        Write-Output "Releasing Mizon Explorer command host $($process.Id)."
        $process.Kill()
        if (-not $process.WaitForExit(5000)) { throw 'The Mizon Explorer command host did not exit.' }
    }
    finally { $process.Dispose() }
}
