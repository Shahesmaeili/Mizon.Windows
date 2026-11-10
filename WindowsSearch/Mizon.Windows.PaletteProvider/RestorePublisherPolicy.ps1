# Loaded only by PublisherPolicy.ps1 after -UseRestorePrivilege and elevation checks.
# No action runs merely by dot-sourcing this file.
function Invoke-MizonRestorePublisherPolicy {
    param([ValidateSet('Enable', 'Rollback', 'Recovery')][string] $ProtectedAction)

    if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Use 64-bit Windows PowerShell 5.1 (powershell.exe) for the explicit restore-privilege installer.' }
    Add-Type -TypeDefinition (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'RestorePublisherPolicy.cs.txt') -Raw)
    $protectedDirectory = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)) 'Mizon.PaletteProvider.Policy'
    $protectedBackup = Join-Path $protectedDirectory 'OriginalPolicy.json'
    $administrators = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    $system = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
    $userSid = [MizonRestorePublisherPolicy]::CurrentSid()

    # The durable recovery record must not be writable by a non-elevated process.
    # Use a dedicated direct child of ProgramData with a protected administrator ACL.
    if (-not (Test-Path -LiteralPath $protectedDirectory)) {
        if ($ProtectedAction -ne 'Enable') { throw 'Protected recovery directory is missing; nothing was changed.' }
        $directorySecurity = [Security.AccessControl.DirectorySecurity]::new()
        $directorySecurity.SetOwner($administrators)
        $directorySecurity.SetAccessRuleProtection($true, $false)
        foreach ($sid in @($administrators, $system)) {
            $directorySecurity.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid, [Security.AccessControl.FileSystemRights]::FullControl, [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit', [Security.AccessControl.PropagationFlags]::None, [Security.AccessControl.AccessControlType]::Allow))
        }
        [IO.Directory]::CreateDirectory($protectedDirectory, $directorySecurity) | Out-Null
    }
    $directoryInfo = Get-Item -LiteralPath $protectedDirectory -Force
    if (($directoryInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'The protected backup directory is a reparse point; refusing it.' }
    $directoryAcl = Get-Acl -LiteralPath $protectedDirectory
    if (-not $directoryAcl.AreAccessRulesProtected -or $directoryAcl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin @($administrators.Value, $system.Value)) {
        throw 'The protected backup directory has an unexpected owner or inherited ACL.'
    }
    foreach ($rule in $directoryAcl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or $rule.IdentityReference.Value -notin @($administrators.Value, $system.Value)) {
            throw 'The protected backup directory permits an unexpected principal. Nothing was changed.'
        }
    }

    $lockPath = Join-Path $protectedDirectory 'Operation.lock'
    if ((Test-Path -LiteralPath $lockPath) -and ((Get-Item -LiteralPath $lockPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Unexpected operation lock reparse point.' }
    $lockStream = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        function Save-ProtectedRecord($Record) {
            $temporaryPath = Join-Path $protectedDirectory ([Guid]::NewGuid().ToString('N') + '.tmp')
            $data = [Text.UTF8Encoding]::new($false).GetBytes(($Record | ConvertTo-Json -Depth 5))
            $fileSecurity = [Security.AccessControl.FileSecurity]::new()
            $fileSecurity.SetOwner($administrators)
            $fileSecurity.SetAccessRuleProtection($true, $false)
            foreach ($sid in @($administrators, $system)) {
                $fileSecurity.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid, [Security.AccessControl.FileSystemRights]::FullControl, [Security.AccessControl.AccessControlType]::Allow))
            }
            $stream = [IO.FileStream]::new($temporaryPath, [IO.FileMode]::CreateNew, [Security.AccessControl.FileSystemRights]'Write, ReadPermissions', [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough, $fileSecurity)
            try { $stream.Write($data, 0, $data.Length); $stream.Flush($true) }
            finally { $stream.Dispose() }
            if (Test-Path -LiteralPath $protectedBackup) {
                # Windows PowerShell 5.1 coerces $null into an invalid empty path.
                # Retain the prior phase as a uniquely named checkpoint instead.
                $checkpoint = Join-Path $protectedDirectory ('Checkpoint-' + [Guid]::NewGuid().ToString('N') + '.json')
                [IO.File]::Replace($temporaryPath, $protectedBackup, $checkpoint)
            }
            else { [IO.File]::Move($temporaryPath, $protectedBackup) }
        }
        function Get-DescriptorHash([byte[]] $Bytes) {
            $sha = [Security.Cryptography.SHA256]::Create()
            try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '') }
            finally { $sha.Dispose() }
        }
        function Read-ValidatedProtectedRecord {
            if (-not (Test-Path -LiteralPath $protectedBackup -PathType Leaf)) { throw 'Protected backup is missing; no recovery or reconciliation is permitted.' }
            $backupInfo = Get-Item -LiteralPath $protectedBackup -Force
            if (($backupInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $backupInfo.Length -gt 131072) { throw 'Unsafe protected backup file.' }
            $backupAcl = Get-Acl -LiteralPath $protectedBackup
            if ($backupAcl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin @($administrators.Value, $system.Value)) { throw 'The recovery record owner is not trusted.' }
            foreach ($rule in $backupAcl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
                if ($rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or $rule.IdentityReference.Value -notin @($administrators.Value, $system.Value)) { throw 'Unsafe recovery record permissions.' }
            }
            $saved = Get-Content -LiteralPath $protectedBackup -Raw | ConvertFrom-Json
            if ($saved.SchemaVersion -ne 1 -or $saved.KeyPath -ne [MizonRestorePublisherPolicy]::KeyPath -or $saved.PublisherId -ne [MizonRestorePublisherPolicy]::PublisherId -or $saved.RegistryView -ne 'Registry64' -or $saved.MachineName -ne [Environment]::MachineName -or $saved.UserSid -ne $userSid -or $saved.OriginalValueExisted -isnot [bool] -or $saved.OriginalValueExisted -ne $false -or $saved.Phase -notin @('Prepared', 'Enabled', 'RollbackInProgress', 'RecoveryInProgress')) {
                throw 'The protected recovery record does not match this machine, user, publisher and exact key.'
            }
            $savedSecurity = [Convert]::FromBase64String($saved.OriginalSecurity)
            if ((Get-DescriptorHash $savedSecurity) -ne $saved.OriginalSecuritySha256 -or [MizonRestorePublisherPolicy]::Sddl($savedSecurity) -ne $saved.OriginalSddl) { throw 'The original descriptor backup failed integrity checks.' }
            return $saved
        }

        if ($ProtectedAction -eq 'Enable') {
            $packages = @(Get-AppxPackage -Name 'Mizon.Windows.PaletteProvider' -ErrorAction Stop)
            if ($packages.Count -ne 1 -or $packages[0].Publisher -ne 'CN=alish' -or $packages[0].PublisherId -ne 'yvtn54431hrs8') { throw 'The expected installed Mizon publisher was not verified.' }
            if (Test-Path -LiteralPath $protectedBackup) {
                $record = Read-ValidatedProtectedRecord
                if ($record.Phase -ne 'Prepared' -or $record.PackageFullName -ne $packages[0].PackageFullName) {
                    throw 'An existing backup is not the matching interrupted Enable. Use explicit Recovery or Rollback.'
                }
                if (-not [MizonRestorePublisherPolicy]::ReadOwnValue()) { throw 'The interrupted Enable did not add the expected publisher value. Use explicit Recovery.' }
                $original = [Convert]::FromBase64String($record.OriginalSecurity)
                $currentSecurity = [MizonRestorePublisherPolicy]::ReadSecurity()
                if ((Get-DescriptorHash $currentSecurity) -ne $record.OriginalSecuritySha256 -or -not [MizonRestorePublisherPolicy]::SameSecurity($currentSecurity, $original)) {
                    throw 'The current owner/DACL no longer exactly matches the original descriptor. Reconciliation refused.'
                }
                # Only the durable phase record is reconciled. No Apply call,
                # token privilege adjustment, or registry write occurs in this path.
                $record.Phase = 'Enabled'
                Save-ProtectedRecord $record
                Write-PolicyMessage 'Reconciled the matching Prepared record to Enabled; Mizon value and Windows security were already correct. No registry write performed.'
                return
            }
            if ([MizonRestorePublisherPolicy]::ReadOwnValue()) {
                Write-PolicyMessage 'The Mizon publisher already exists. Protected mode made no registry changes.'
                return
            }
            $original = [MizonRestorePublisherPolicy]::ReadSecurity()
            $record = [ordered]@{
                SchemaVersion = 1
                KeyPath = [MizonRestorePublisherPolicy]::KeyPath
                PublisherId = [MizonRestorePublisherPolicy]::PublisherId
                RegistryView = 'Registry64'
                MachineName = [Environment]::MachineName
                UserSid = $userSid
                OriginalValueExisted = $false
                OriginalSecurity = [Convert]::ToBase64String($original)
                OriginalSecuritySha256 = Get-DescriptorHash $original
                OriginalSddl = [MizonRestorePublisherPolicy]::Sddl($original)
                PackageFullName = $packages[0].PackageFullName
                Phase = 'Prepared'
                CreatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
            }
            Save-ProtectedRecord $record
            Write-PolicyMessage "Original descriptor and value state backed up durably to $protectedBackup. Opening the existing key using backup/restore privileges; owner and DACL will not change."
            [MizonRestorePublisherPolicy]::Apply($original, $true)
            $record.Phase = 'Enabled'
            Save-ProtectedRecord $record
            Write-PolicyMessage 'Added only Mizon publisher; original owner/group/DACL verified unchanged. Protected backup retained for rollback.'
        }
        else {
            $record = Read-ValidatedProtectedRecord
            if ($ProtectedAction -eq 'Rollback' -and $record.Phase -ne 'Enabled') { throw 'The operation was interrupted. Use the explicit Recovery action.' }
            $original = [Convert]::FromBase64String($record.OriginalSecurity)
            $record.Phase = if ($ProtectedAction -eq 'Recovery') { 'RecoveryInProgress' } else { 'RollbackInProgress' }
            Save-ProtectedRecord $record
            Write-PolicyMessage "$ProtectedAction restoring the previous absence of Mizon publisher; Windows owner/DACL remain untouched."
            [MizonRestorePublisherPolicy]::Apply($original, $false)
            $archive = Join-Path $protectedDirectory ('Restored-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '.json')
            [IO.File]::Move($protectedBackup, $archive)
            Write-PolicyMessage "Original publisher state restored; owner/group/DACL verified unchanged. Record archived: $archive"
        }
    }
    catch {
        Write-PolicyMessage "Value update did not complete. Owner/DACL were never changed. Keep the backup. Recovery command: PublisherPolicy.ps1 -Action Recovery -UseRestorePrivilege"
        throw
    }
    finally { $lockStream.Dispose() }
}
