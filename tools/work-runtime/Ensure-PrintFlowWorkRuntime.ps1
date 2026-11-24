param([string] $StudioConfig, [string] $WorkspaceRoot)

# Dot-source for service lifecycle tests. The normal entry point runs below.
function Ensure-WorkService($IsReady, $FindProcess, $StartService) {
    if (& $IsReady) { return }
    $process = & $FindProcess
    if (!$process) { $process = & $StartService }
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (!(& $IsReady)) {
        if (!$process -or $process.HasExited -or $timer.Elapsed.TotalSeconds -ge 20) {
            throw 'Work service did not become ready.'
        }
        Start-Sleep -Milliseconds 250
    }
}

function Find-WorkProcess([string] $Executable, [string[]] $Arguments) {
    $name = [IO.Path]::GetFileName($Executable)
    foreach ($p in Get-CimInstance Win32_Process -Filter "Name='$name'") {
        if ($p.ExecutablePath -ne $Executable) { continue }
        $matchesAll = $true
        foreach ($argument in $Arguments) {
            $pattern = '(?<!\S)"?' + [regex]::Escape($argument) + '"?(?!\S)'
            if ($p.CommandLine -notmatch $pattern) { $matchesAll = $false; break }
        }
        if ($matchesAll) { return Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue }
    }
}

function Start-WorkProcess([string] $Executable, [string[]] $Arguments) {
    # Windows PowerShell Start-Process joins an argument array into one command line.
    $quoted = foreach ($arg in $Arguments) {
        if ($arg.Contains('"') -or $arg.EndsWith('\')) { throw 'Invalid startup argument.' }
        '"' + $arg + '"'
    }
    Start-Process -FilePath $Executable -ArgumentList ($quoted -join ' ') -WorkingDirectory ([IO.Path]::GetDirectoryName($Executable)) -WindowStyle Hidden -PassThru
}

function Test-ReceiverPipe([string] $Name) {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', $Name, [IO.Pipes.PipeDirection]::InOut)
    try { $pipe.Connect(150); return $true }
    catch [TimeoutException] { return $false }
    finally { $pipe.Dispose() }
}

function Test-TunnelReady($Process) {
    if (!$Process -or $Process.HasExited) { return $false }
    $ports = Get-NetTCPConnection -State Listen -LocalAddress 127.0.0.1 -OwningProcess $Process.Id -ErrorAction SilentlyContinue
    foreach ($port in $ports) {
        try {
            $request = [Net.HttpWebRequest]::Create("http://127.0.0.1:$($port.LocalPort)/readyz")
            $request.Proxy = $null
            $request.AllowAutoRedirect = $false
            $request.Timeout = 1000
            $response = $request.GetResponse()
            try {
                $reader = [IO.StreamReader]::new($response.GetResponseStream())
                try { if ($response.StatusCode -eq 200 -and $reader.ReadLine() -ceq 'ready') { return $true } }
                finally { $reader.Dispose() }
            } finally { $response.Dispose() }
        } catch [Net.WebException] { continue }
    }
    return $false
}

if ($MyInvocation.InvocationName -eq '.') { return }
$ErrorActionPreference = 'Stop'
$failure = 10
$mutex = [Threading.Mutex]::new($false, 'Local\PrintFlow-Studio-WorkRuntime-Startup')
$held = $false
try {
    try { $held = $mutex.WaitOne(30000) } catch [Threading.AbandonedMutexException] { $held = $true }
    if (!$held) { throw 'Another Work startup is busy.' }
    $startupFile = [IO.Path]::ChangeExtension($StudioConfig, 'startup.json')
    $startup = Get-Content -Raw -Encoding UTF8 -LiteralPath $startupFile | ConvertFrom-Json
    $studio = Get-Content -Raw -Encoding UTF8 -LiteralPath $StudioConfig | ConvertFrom-Json
    $receiver = Get-Content -Raw -Encoding UTF8 -LiteralPath $startup.receiverConfig | ConvertFrom-Json
    if ($studio.pipeName -cnotmatch '^PrintFlow-[A-Za-z0-9-]{2,70}$' -or $receiver.studioPipe -cne $studio.pipeName -or
        !$receiver.controlBearer -or $receiver.controlBearer -cne $studio.controlBearer -or
        !$receiver.readBearer -or $receiver.readBearer -cne $studio.readBearer -or
        [IO.Path]::GetFullPath($studio.workspaceRoot).TrimEnd('\') -ne [IO.Path]::GetFullPath($WorkspaceRoot).TrimEnd('\')) {
        throw 'Receiver configuration mismatch.'
    }
    Ensure-WorkService { Test-ReceiverPipe $studio.pipeName } {
        Find-WorkProcess $startup.node @('serve', '--config', $startup.receiverConfig)
    } {
        Start-WorkProcess $startup.node @($startup.receiverCli, 'serve', '--config', $startup.receiverConfig)
    }

    $failure = 11
    $profileFile = Get-Item -LiteralPath $startup.tunnelProfile
    $profileArgs = @('--profile-dir', $profileFile.DirectoryName, '--profile', $profileFile.BaseName)
    $tunnel = @{ process = Find-WorkProcess $startup.tunnelClient (@('run') + $profileArgs) }
    Ensure-WorkService { Test-TunnelReady $tunnel.process } { $tunnel.process } {
        $key = $env:PRINTFLOW_TUNNEL_RUNTIME_KEY
        if (!$key) { $key = [Environment]::GetEnvironmentVariable('PRINTFLOW_TUNNEL_RUNTIME_KEY', 'User') }
        if (!$key -or !$receiver.writerBearer) { throw 'Missing tunnel credentials.' }
        $env:CONTROL_PLANE_API_KEY = $key
        $env:PRINTFLOW_TUNNEL_RUNTIME_KEY = $key
        $env:PRINTFLOW_MCP_WRITER_HEADER = 'Bearer ' + $receiver.writerBearer
        $headers = @('--control-plane.api-key', 'env:CONTROL_PLANE_API_KEY',
            '--mcp.extra-headers', 'Authorization: env:PRINTFLOW_MCP_WRITER_HEADER',
            '--mcp.discovery-extra-headers', 'Authorization: env:PRINTFLOW_MCP_WRITER_HEADER')
        $tunnel.process = Start-WorkProcess $startup.tunnelClient (@('run') + $profileArgs + $headers)
        $tunnel.process
    }
    exit 0
} catch {
    # Studio maps these codes to localised messages. Never emit credential-bearing configuration.
    exit $failure
} finally {
    if ($held) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
