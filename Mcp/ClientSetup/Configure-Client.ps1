<#
Copies only Mizon's launcher and produces client configuration examples.
Never modifies Codex/Claude configuration, grants permissions or starts the app.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Destination = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Mizon\Mcp')
)

$ErrorActionPreference = 'Stop'
$destinationPath = [IO.Path]::GetFullPath($Destination)
if (-not $PSCmdlet.ShouldProcess($destinationPath, 'Install Mizon MCP launcher and write configuration examples')) {
    return
}

[void][IO.Directory]::CreateDirectory($destinationPath)
foreach ($name in @('Mizon-Mcp.cmd', 'Resolve-MizonMcp.ps1', 'Stage-MizonMcpRuntime.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $destinationPath $name) -Force
}

# These are locally configured host profiles, not MCP client-supplied names and
# not credentials. Retain them across reruns/updates; each host gets its own proof.
$profilesPath = Join-Path $destinationPath 'client-profiles.json'
$profileGate = [IO.File]::Open($profilesPath + '.lock', [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    if (Test-Path -LiteralPath $profilesPath) {
        $profiles = [IO.File]::ReadAllText($profilesPath) | ConvertFrom-Json
        foreach ($hostName in @('codex', 'claude')) {
            $profileGuid = [Guid]::Empty
            if (-not [Guid]::TryParseExact([string]$profiles.$hostName, 'N', [ref]$profileGuid) -or $profileGuid -eq [Guid]::Empty) {
                throw 'Saved MCP host profiles are invalid; no identity was replaced.'
            }
        }
        if ($profiles.codex -eq $profiles.claude) { throw 'MCP hosts must use distinct profiles.' }
    }
    else {
        $profiles = [pscustomobject]@{ codex=[Guid]::NewGuid().ToString('N'); claude=[Guid]::NewGuid().ToString('N') }
        $temporaryProfiles = $profilesPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
        try {
            [IO.File]::WriteAllText($temporaryProfiles, ($profiles | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))
            [IO.File]::Move($temporaryProfiles, $profilesPath)
        }
        finally { if (Test-Path -LiteralPath $temporaryProfiles) { Remove-Item -LiteralPath $temporaryProfiles } }
    }
}
finally { $profileGate.Dispose() }

$launcherTemplate = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Mizon-Mcp.cmd'))
foreach ($hostName in @('codex', 'claude')) {
    # Substitute a validated GUID literal only. No arbitrary shell arguments are
    # accepted or forwarded; MCP metadata can never select this profile.
    $hostLauncher = $launcherTemplate.Replace('"%MIZON_MCP_EXE%"', ('"%MIZON_MCP_EXE%" --client-profile ' + $profiles.$hostName))
    [IO.File]::WriteAllText((Join-Path $destinationPath ('Mizon-Mcp-' + $hostName + '.cmd')), $hostLauncher, (New-Object System.Text.UTF8Encoding($false)))
}

$command = Join-Path $env:SystemRoot 'System32\cmd.exe'
$launcher = Join-Path $destinationPath 'Mizon-Mcp-codex.cmd'
$launchArgs = @('/d', '/c', $launcher)
$claudeLaunchArgs = @('/d', '/c', (Join-Path $destinationPath 'Mizon-Mcp-claude.cmd'))
$example = [ordered]@{
    mcpServers = [ordered]@{
        mizon = [ordered]@{
            command = $command
            args = $claudeLaunchArgs
        }
    }
}
$json = $example | ConvertTo-Json -Depth 6
$jsonPath = Join-Path $destinationPath 'claude-mcp-example.json'
$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText($jsonPath, $json + [Environment]::NewLine, $utf8)

# JSON's escaped Windows path strings are valid TOML basic strings as well.
$commandLiteral = ConvertTo-Json -InputObject $command -Compress
$argLiterals = ($launchArgs | ForEach-Object { ConvertTo-Json -InputObject $_ -Compress }) -join ', '
$toml = @"
[mcp_servers.mizon]
command = $commandLiteral
args = [$argLiterals]
startup_timeout_sec = 30
tool_timeout_sec = 60
"@
$tomlPath = Join-Path $destinationPath 'codex-mcp-example.toml'
[IO.File]::WriteAllText($tomlPath, $toml + [Environment]::NewLine, $utf8)

Write-Output "Mizon launcher: $launcher"
Write-Output "Codex configuration snippet: $tomlPath"
Write-Output "Claude configuration snippet: $jsonPath"
Write-Output 'No AI client configuration was changed. Initial approval is saved until revoked inside Mizon.'
