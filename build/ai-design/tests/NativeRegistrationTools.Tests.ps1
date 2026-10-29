$ErrorActionPreference = 'Stop'
$scriptDirectory = Split-Path -Parent $PSScriptRoot
$register = Join-Path $scriptDirectory 'Register-NativeHost.ps1'
$tools = Join-Path $scriptDirectory 'NativeHost-Tools.ps1'
function Assert-Equal($Actual, $Expected, [string]$Message) {
    if ($Actual -cne $Expected) { throw "$Message`: expected '$Expected', got '$Actual'." }
}
function Invoke-Preview([string]$Browser, [string]$HostPath, [string]$WorkspaceRoot, [string]$DownloadRoot) {
    $ErrorActionPreference = 'Continue' # Windows PowerShell 5.1 surfaces an expected nonzero native exit as a nonterminating error.
    $arguments = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $register,
        '-ValidateOnly', '-ExtensionId', ('a' * 32), '-HostPath', $HostPath,
        '-WorkspaceRoot', $WorkspaceRoot, '-DownloadRoot', $DownloadRoot)
    if ($Browser) { $arguments += @('-Browser', $Browser) }
    $output = & powershell.exe @arguments 2>&1 | Out-String
    return @{ Code=$LASTEXITCODE; Output=$output }
}

$root = Join-Path ([IO.Path]::GetTempPath()) ('printflow-native-preview-' + [Guid]::NewGuid().ToString('N'))
$hostPath = Join-Path $root 'PrintFlow.NativeHost.exe'
[IO.Directory]::CreateDirectory($root) | Out-Null
try {
    [IO.File]::WriteAllBytes($hostPath, [byte[]]@(0))

    # Regression: Chrome preview must name its actual HKCU binding and pin the exact extension origin.
    $chrome = Invoke-Preview 'Chrome' $hostPath $root $root
    Assert-Equal $chrome.Code 0 'Chrome preview exit code'
    $chromeJson = $chrome.Output | ConvertFrom-Json
    Assert-Equal $chromeJson.registryKey 'HKCU\Software\Google\Chrome\NativeMessagingHosts\com.printflow.ai_design' 'Chrome binding'
    Assert-Equal $chromeJson.manifest.name 'com.printflow.ai_design' 'Chrome manifest host'
    Assert-Equal $chromeJson.manifest.allowed_origins[0] ('chrome-extension://' + ('a' * 32) + '/') 'Chrome origin'
    Assert-Equal $chromeJson.configuration.extensionOrigin ('chrome-extension://' + ('a' * 32) + '/') 'Chrome configured origin'
    Assert-Equal $chromeJson.writesPerformed $false 'Chrome preview writes'

    # ValidateSet accepts case-insensitive input; registration must persist a canonical identity for rollback.
    $lowercase = Invoke-Preview 'chrome' $hostPath $root $root
    Assert-Equal $lowercase.Code 0 'Lowercase Chrome preview exit code'
    $lowercaseJson = $lowercase.Output | ConvertFrom-Json
    Assert-Equal $lowercaseJson.registryKey 'HKCU\Software\Google\Chrome\NativeMessagingHosts\com.printflow.ai_design' 'Lowercase Chrome binding'
    Assert-Equal $lowercaseJson.registration.browser 'Chrome' 'Canonical persisted browser'

    # Existing callers without a browser argument must still preview the Edge binding.
    $edge = Invoke-Preview '' $hostPath $root $root
    Assert-Equal $edge.Code 0 'Edge preview exit code'
    $edgeJson = $edge.Output | ConvertFrom-Json
    Assert-Equal $edgeJson.registryKey 'HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.printflow.ai_design' 'Edge binding'
    Assert-Equal $edgeJson.writesPerformed $false 'Edge preview writes'

    # A typo cannot produce a preview for an unsupported browser.
    $invalid = Invoke-Preview 'Chromium' $hostPath $root $root
    if ($invalid.Code -eq 0) { throw 'Unsupported browser was accepted.' }

    # Rollback must select and validate the binding saved at registration, not a current default.
    . $tools
    $record = $chromeJson.registration
    Assert-Equal $record.browser 'Chrome' 'Persisted browser selection'
    Assert-Equal (Get-RegistryKeyForRecord $record) 'Software\Google\Chrome\NativeMessagingHosts\com.printflow.ai_design' 'Chrome rollback binding'
    $record.registryKey = 'Software\Microsoft\Edge\NativeMessagingHosts\com.printflow.ai_design'
    $refused = $false
    try { Get-RegistryKeyForRecord $record | Out-Null } catch { $refused = $true }
    if (-not $refused) { throw 'Cross-browser rollback record was accepted.' }

    'Native registration preview tests passed.'
} finally {
    if ([IO.File]::Exists($hostPath)) { [IO.File]::Delete($hostPath) }
    [IO.Directory]::Delete($root)
}
