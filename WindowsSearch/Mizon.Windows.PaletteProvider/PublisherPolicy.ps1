#Requires -Version 5.1
<#
.SYNOPSIS
Inspects, enables, or rolls back only Mizon's publisher entry for the native Palette catalog.
.NOTES
Enable and Rollback require an elevated process. This does not change feature
flights, disable publisher checks, impersonate Microsoft, or edit other entries.
The Windows gate is publisher-scoped, so the entry applies to this publisher,
not exclusively to one package. No policy is modified by Deploy.ps1.
#>
[CmdletBinding()]
param(
    [ValidateSet('Inspect', 'Enable', 'Rollback', 'Recovery')]
    [string] $Action = 'Inspect',

    # Uses documented REG_OPTION_BACKUP_RESTORE for this value only; never changes ACLs.
    [switch] $UseRestorePrivilege
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$publisherId = 'yvtn54431hrs8'
$packageName = 'Mizon.Windows.PaletteProvider'
$registryPath = 'SOFTWARE\Microsoft\Windows\System Composer\AllowedPublishers'
$stateDirectory = Join-Path $env:USERPROFILE '.mizon\WindowsPaletteProvider'
$backupPath = Join-Path $stateDirectory 'PublisherPolicy-backup.json'
$logPath = Join-Path $stateDirectory 'PublisherPolicy.log'

function Write-PolicyMessage([string] $Message) {
    $line = '{0:yyyy-MM-dd HH:mm:ss.fff zzz} {1}' -f [DateTimeOffset]::Now, $Message
    Write-Host $line
    if ($Action -ne 'Inspect') {
        try {
            [IO.Directory]::CreateDirectory($stateDirectory) | Out-Null
            [IO.File]::AppendAllText($logPath, $line + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
        }
        catch {
            Write-Warning "Could not persist the policy diagnostic log at '$logPath': $($_.Exception.Message)"
        }
    }
}

try {
    Write-PolicyMessage "Start action=$Action; PID=$PID; 64-bit=$([Environment]::Is64BitProcess); script=$PSCommandPath"
    if (-not [Environment]::Is64BitProcess) { throw 'Run the 64-bit PowerShell executable.' }
    if ($Action -eq 'Recovery' -and -not $UseRestorePrivilege) { throw 'Recovery requires the explicit -UseRestorePrivilege switch.' }
    if ($Action -eq 'Inspect' -and $UseRestorePrivilege) { throw 'Inspect is read-only; do not pass the restore-privilege switch.' }
    if ($Action -ne 'Inspect') {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        try {
            $principal = [Security.Principal.WindowsPrincipal]::new($identity)
            $isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
            Write-PolicyMessage "Identity=$($identity.Name); user SID=$($identity.User.Value); administrator=$isAdministrator"
            if (-not $isAdministrator) {
                throw 'This explicit policy action requires an elevated process. No changes were made.'
            }
        }
        finally { $identity.Dispose() }
    }

    if ($UseRestorePrivilege) {
        . (Join-Path $PSScriptRoot 'RestorePublisherPolicy.ps1')
        Invoke-MizonRestorePublisherPolicy -ProtectedAction $Action
        return
    }

    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
    try {
        if ($PSVersionTable.PSEdition -eq 'Desktop') {
            $aclReadKey = $baseKey.OpenSubKey($registryPath, $false)
            try { $policyAcl = $aclReadKey.GetAccessControl([Security.AccessControl.AccessControlSections]'Owner, Access') }
            finally { if ($null -ne $aclReadKey) { $aclReadKey.Dispose() } }
        }
        else { $policyAcl = Get-Acl -LiteralPath "Registry::HKEY_LOCAL_MACHINE\$registryPath" }
        Write-PolicyMessage "Policy owner: $($policyAcl.Owner)"
        foreach ($rule in $policyAcl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
            $sid = $rule.IdentityReference.Value
            if ($sid -eq 'S-1-5-32-544') {
                Write-PolicyMessage "Administrators permission: $($rule.AccessControlType) $($rule.RegistryRights); inherited=$($rule.IsInherited)"
            }
        }
        Write-PolicyMessage "Opening HKLM\$registryPath; writable=$($Action -ne 'Inspect')"
        $key = $baseKey.OpenSubKey($registryPath, $Action -ne 'Inspect')
        try {
            if ($null -eq $key) { throw 'The existing Windows AllowedPublishers key was not found. It will not be created.' }
            $existed = $key.GetValueNames() -contains $publisherId
            Write-PolicyMessage "Mizon publisher '$publisherId' present: $existed"
            if ($Action -eq 'Inspect') {
                Write-PolicyMessage "Allowed publishers: $(($key.GetValueNames() | Sort-Object) -join ', ')"
                Write-PolicyMessage "Rollback record: $backupPath; exists: $(Test-Path -LiteralPath $backupPath -PathType Leaf)"
                return
            }

            if ($Action -eq 'Enable') {
                $packages = @(Get-AppxPackage -Name $packageName -ErrorAction Stop)
                if ($packages.Count -ne 1 -or $packages[0].PublisherId -ne $publisherId -or $packages[0].Publisher -ne 'CN=alish') {
                    throw 'Register the expected Mizon Palette package for this user first; publisher identity did not match.'
                }
                if ($existed) {
                    Write-PolicyMessage 'The publisher was already allowed. Its value and the rollback record were left unchanged.'
                    return
                }
                if (Test-Path -LiteralPath $backupPath) {
                    throw 'An earlier rollback record exists. Inspect it and finish that rollback before another Enable.'
                }
                $backup = [ordered]@{
                    SchemaVersion = 1
                    RegistryPath = $registryPath
                    RegistryView = 'Registry64'
                    PublisherId = $publisherId
                    ValueExisted = $false
                    EnabledForPackage = $packages[0].PackageFullName
                    CreatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
                }
                [IO.Directory]::CreateDirectory($stateDirectory) | Out-Null
                $temporary = Join-Path $stateDirectory ('PublisherPolicy-' + [Guid]::NewGuid().ToString('N') + '.tmp')
                [IO.File]::WriteAllText($temporary, ($backup | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
                [IO.File]::Move($temporary, $backupPath)
                # The existing Windows allowlist uses value names with empty REG_NONE data.
                $key.SetValue($publisherId, [byte[]]::new(0), [Microsoft.Win32.RegistryValueKind]::None)
                $key.Flush()
                if ($key.GetValueNames() -notcontains $publisherId) { throw 'The publisher entry was not present after writing.' }
                Write-PolicyMessage "Enabled only the Mizon publisher entry. Rollback: PublisherPolicy.ps1 -Action Rollback"
            }
            else {
                if (-not (Test-Path -LiteralPath $backupPath -PathType Leaf)) { throw 'No rollback record exists; nothing will be removed.' }
                $backup = Get-Content -LiteralPath $backupPath -Raw | ConvertFrom-Json
                if ($backup.SchemaVersion -ne 1 -or $backup.RegistryPath -ne $registryPath -or $backup.PublisherId -ne $publisherId -or $backup.RegistryView -ne 'Registry64' -or $backup.ValueExisted -ne $false) {
                    throw 'Unexpected rollback record; no registry values were changed.'
                }
                if ($existed) {
                    $currentData = $key.GetValue($publisherId, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                    if ($key.GetValueKind($publisherId) -ne [Microsoft.Win32.RegistryValueKind]::None -or @($currentData).Count -ne 0) {
                        throw 'The publisher entry was modified after Enable; rollback will not overwrite it.'
                    }
                    $key.DeleteValue($publisherId, $false)
                    $key.Flush()
                }
                $archive = Join-Path $stateDirectory ('PublisherPolicy-restored-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '.json')
                [IO.File]::Move($backupPath, $archive)
                Write-PolicyMessage 'Restored the previous absence of the Mizon publisher entry; all other policy values remain unchanged.'
            }
        }
        finally { if ($null -ne $key) { $key.Dispose() } }
    }
    finally { $baseKey.Dispose() }
}
catch {
    Write-PolicyMessage "FAILED: $($_.Exception.ToString())"
    Write-PolicyMessage "Location: $($_.InvocationInfo.PositionMessage)"
    Write-Error -ErrorRecord $_ -ErrorAction Continue
    exit 1
}
