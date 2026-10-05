[CmdletBinding(SupportsShouldProcess=$true)]
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[a-p]{32}$')][string]$ExtensionId,
    [Parameter(Mandatory=$true)][string]$HostPath,
    [Parameter(Mandatory=$true)][string]$WorkspaceRoot,
    [Parameter(Mandatory=$true)][string]$DownloadRoot,
    [switch]$ValidateOnly
)
. (Join-Path $PSScriptRoot 'NativeHost-Tools.ps1')
$hostFile = Assert-RegularPath $HostPath $false
if ([IO.Path]::GetExtension($hostFile) -ine '.exe') { throw 'Native host executable required.' }
$workspace = Assert-RegularPath $WorkspaceRoot $true
$downloads = Assert-RegularPath $DownloadRoot $true
Assert-NoReparse $script:NativeInstallation
$paths = Get-OwnedPaths
$manifest = [ordered]@{ name=$script:NativeHostName; description='PrintFlow AI design local bridge'; path=$hostFile; type='stdio'; allowed_origins=@("chrome-extension://$ExtensionId/") }
$configuration = [ordered]@{ protocolVersion=1; hostVersion='1.0.0'; extensionOrigin="chrome-extension://$ExtensionId/"; workspaceRoot=$workspace; downloadRoot=$downloads; verifiedWorkContractHash=$null }
if ($ValidateOnly) {
    [ordered]@{ manifest=$manifest; configuration=$configuration; manifestPath=$paths[0]; registryKey=('HKCU\' + $script:NativeRegistryKey); writesPerformed=$false } | ConvertTo-Json -Depth 8
    exit 0
}
if (-not $PSCmdlet.ShouldProcess(('HKCU\' + $script:NativeRegistryKey), 'Register this exact user extension and native executable')) { return }
[IO.Directory]::CreateDirectory($script:NativeInstallation) | Out-Null
$locks = Get-IdleLocks
$created = [Collections.Generic.List[object]]::new(); $registryCreated = $false
try {
    foreach ($path in $paths) { Assert-NoReparse $path; if (Test-Path -LiteralPath $path) { throw 'Existing bridge registration must be explicitly rolled back first.' } }
    $existing = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($script:NativeRegistryKey)
    if ($null -ne $existing) { $existing.Dispose(); throw 'Existing native host binding refused.' }
    $manifestBytes = Get-JsonBytes $manifest; $configurationBytes = Get-JsonBytes $configuration
    $record = [ordered]@{ registrationId=[Guid]::NewGuid().ToString('D'); hostName=$script:NativeHostName; protocolVersion=1; manifestPath=$paths[0]; configurationPath=$paths[1]; manifestHash=(Get-ByteHash $manifestBytes); configurationHash=(Get-ByteHash $configurationBytes) }
    Publish-NewFile $paths[0] $manifestBytes; $created.Add(@{ Path=$paths[0]; Hash=(Get-ByteHash $manifestBytes) })
    Publish-NewFile $paths[1] $configurationBytes; $created.Add(@{ Path=$paths[1]; Hash=(Get-ByteHash $configurationBytes) })
    $recordBytes = Get-JsonBytes $record
    Publish-NewFile $paths[2] $recordBytes; $created.Add(@{ Path=$paths[2]; Hash=(Get-ByteHash $recordBytes) })
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($script:NativeRegistryKey); $registryCreated = $true
    try { $key.SetValue('', $paths[0], [Microsoft.Win32.RegistryValueKind]::String); $key.Flush() } finally { $key.Dispose() }
    [ordered]@{ registrationId=$record.registrationId; registered=$true; workAvailable=$false; reason='WorkContractUnverified' } | ConvertTo-Json
} catch {
    if ($registryCreated) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($script:NativeRegistryKey)
        $owned = $false
        try { $owned = $null -ne $key -and $key.GetValue('') -ceq $paths[0] -and $key.SubKeyCount -eq 0 -and @($key.GetValueNames()).Count -eq 1 } finally { if ($null -ne $key) { $key.Dispose() } }
        if ($owned) { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($script:NativeRegistryKey, $false) }
    }
    foreach ($file in $created) {
        Assert-NoReparse $file.Path
        if ([IO.File]::Exists($file.Path) -and (Get-ByteHash ([IO.File]::ReadAllBytes($file.Path))) -ceq $file.Hash) { [IO.File]::Delete($file.Path) }
    }
    throw
} finally { foreach ($item in $locks) { $item.Dispose() } }
