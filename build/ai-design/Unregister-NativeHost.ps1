[CmdletBinding(SupportsShouldProcess=$true)]
param([switch]$ValidateOnly)
. (Join-Path $PSScriptRoot 'NativeHost-Tools.ps1')
Assert-NoReparse $script:NativeInstallation
$paths = Get-OwnedPaths
if (-not [IO.File]::Exists($paths[2])) { throw 'Owned registration record required; no registry or files changed.' }
Assert-RegularPath $paths[2] $false | Out-Null
$recordBytes = [IO.File]::ReadAllBytes($paths[2])
if ($recordBytes.Length -gt 65536) { throw 'Invalid registration record.' }
$record = [Text.Encoding]::UTF8.GetString($recordBytes) | ConvertFrom-Json
if ($record.hostName -cne $script:NativeHostName -or $record.protocolVersion -ne 1 -or $record.manifestPath -cne $paths[0] -or $record.configurationPath -cne $paths[1]) { throw 'Registration ownership mismatch.' }
$registryKey = Get-RegistryKeyForRecord $record
$registrationId = [Guid]::Parse($record.registrationId)
if ($registrationId -eq [Guid]::Empty) { throw 'Invalid registration identity.' }
foreach ($i in @(0,1)) { Assert-RegularPath $paths[$i] $false | Out-Null }
if ((Get-ByteHash ([IO.File]::ReadAllBytes($paths[0]))) -cne $record.manifestHash -or (Get-ByteHash ([IO.File]::ReadAllBytes($paths[1]))) -cne $record.configurationHash) { throw 'Registration files changed; rollback refused.' }
$key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryKey)
try {
    if ($null -ne $key -and ($key.GetValue('') -cne $paths[0] -or $key.SubKeyCount -ne 0 -or @($key.GetValueNames()).Count -ne 1)) { throw 'Native host binding is no longer owned.' }
} finally { if ($null -ne $key) { $key.Dispose() } }
if ($ValidateOnly) { [ordered]@{ registrationId=$registrationId.ToString(); ownedFiles=$paths; registryKey=('HKCU\' + $registryKey); writesPerformed=$false } | ConvertTo-Json; exit 0 }
if (-not $PSCmdlet.ShouldProcess(('HKCU\' + $registryKey), 'Remove only the matching owned native registration and its three files')) { return }
$locks = Get-IdleLocks
try {
    # Re-read under the operation/connection locks; never recursively delete app or workspace data.
    if ((Get-ByteHash ([IO.File]::ReadAllBytes($paths[2]))) -cne (Get-ByteHash $recordBytes) -or (Get-ByteHash ([IO.File]::ReadAllBytes($paths[0]))) -cne $record.manifestHash -or (Get-ByteHash ([IO.File]::ReadAllBytes($paths[1]))) -cne $record.configurationHash) { throw 'Registration changed during rollback.' }
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryKey)
    try {
        if ($null -ne $key -and ($key.GetValue('') -cne $paths[0] -or $key.SubKeyCount -ne 0 -or @($key.GetValueNames()).Count -ne 1)) { throw 'Native host binding changed during rollback.' }
    } finally { if ($null -ne $key) { $key.Dispose() } }
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($registryKey, $false)
    foreach ($path in $paths) { [IO.File]::Delete($path) }
    [ordered]@{ unregistered=$true; registrationId=$registrationId.ToString() } | ConvertTo-Json
} finally { foreach ($item in $locks) { $item.Dispose() } }
