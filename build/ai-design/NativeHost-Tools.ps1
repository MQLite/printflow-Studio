Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:NativeHostName = 'com.printflow.ai_design'
$script:NativeInstallation = [IO.Path]::Combine([Environment]::GetFolderPath('LocalApplicationData'), 'PrintFlowStudio', 'AiDesignBridge')
function Get-NativeRegistryKey([string]$Browser) {
    switch -CaseSensitive ($Browser) {
        'Edge' { return 'Software\Microsoft\Edge\NativeMessagingHosts\com.printflow.ai_design' }
        'Chrome' { return 'Software\Google\Chrome\NativeMessagingHosts\com.printflow.ai_design' }
        default { throw 'Unsupported native messaging browser.' }
    }
}
function Get-RegistryKeyForRecord($Record) {
    $hasBrowser = $null -ne $Record.PSObject.Properties['browser']
    $hasKey = $null -ne $Record.PSObject.Properties['registryKey']
    if (-not $hasBrowser -and -not $hasKey) { return Get-NativeRegistryKey 'Edge' } # Existing Edge registration records.
    if (-not $hasBrowser -or -not $hasKey) { throw 'Incomplete registration browser identity.' }
    $expected = Get-NativeRegistryKey $Record.browser
    if ($Record.registryKey -cne $expected) { throw 'Registration browser identity mismatch.' }
    return $expected
}
function Assert-RegularPath([string]$Path, [bool]$Directory) {
    if ($Path -notmatch '^[A-Za-z]:[\\/]') { throw 'Absolute local path required.' }
    $absolute = [IO.Path]::GetFullPath($Path)
    $item = Get-Item -LiteralPath $absolute -Force
    if ($item.PSIsContainer -ne $Directory) { throw 'Unexpected path type.' }
    Assert-NoReparse $absolute
    return $absolute
}
function Assert-NoReparse([string]$Path) {
    for ($current = [IO.Path]::GetFullPath($Path); $current; $current = [IO.Path]::GetDirectoryName($current)) {
        if (Test-Path -LiteralPath $current) {
            if (((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse traversal refused.' }
        }
    }
}
function Get-OwnedPaths {
    return @([IO.Path]::Combine($script:NativeInstallation, 'native-host.json'),
        [IO.Path]::Combine($script:NativeInstallation, 'bridge.local.json'),
        [IO.Path]::Combine($script:NativeInstallation, 'registration.local.json'))
}
function Get-ByteHash([byte[]]$Bytes) {
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hash.ComputeHash($Bytes)).Replace('-', '') } finally { $hash.Dispose() }
}
function Get-JsonBytes($Value) { return [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 8)) }
function Publish-NewFile([string]$Path, [byte[]]$Bytes) {
    Assert-NoReparse $Path
    $temporary = [IO.Path]::Combine($script:NativeInstallation, ('.registration-' + [Guid]::NewGuid().ToString('N') + '.tmp'))
    try {
        $file = [IO.FileStream]::new($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
        try { $file.Write($Bytes, 0, $Bytes.Length); $file.Flush($true) } finally { $file.Dispose() }
        [IO.File]::Move($temporary, $Path)
    } finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
}
function Get-IdleLocks {
    $held = [Collections.Generic.List[IDisposable]]::new()
    try {
        foreach ($name in @('connection.lock', 'browser.lock', '.registration.lock')) {
            $path = [IO.Path]::Combine($script:NativeInstallation, $name); Assert-NoReparse $path
            $held.Add([IO.FileStream]::new($path, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None))
        }
        return ,$held
    } catch { foreach ($item in $held) { $item.Dispose() }; throw 'Bridge or registration operation is active.' }
}
