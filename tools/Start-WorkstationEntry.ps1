[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Validate','PrepareAndSmoke','Interactive')][string]$Mode,
    [Parameter(Mandatory)][string]$Root,
    [Parameter(Mandatory)][string]$ScenarioManifest,
    [Parameter(Mandatory)][string]$CandidateManifest,
    [Parameter(Mandatory)][string]$HostAssembly,
    [switch]$Resume,
    [switch]$OwnedRestart,
    [switch]$InjectHostFault,
    [switch]$SafeDesktopConfirmed
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
foreach ($entryPath in @($Root,$ScenarioManifest,$CandidateManifest,$HostAssembly)) {
    if (-not [IO.Path]::IsPathFullyQualified($entryPath) -or [IO.Path]::GetFullPath($entryPath) -cne $entryPath) {
        throw 'Every entry path must be explicit, absolute and canonical.'
    }
}
if (-not [IO.File]::Exists($HostAssembly)) { throw 'Build the test-owned host successfully before invoking the launcher.' }
if ($Mode -eq 'Interactive' -and -not $SafeDesktopConfirmed) { throw 'Interactive requires a fresh explicit desktop acknowledgment.' }
if ($Mode -ne 'Interactive' -and $SafeDesktopConfirmed) { throw 'Desktop acknowledgment is valid only for Interactive.' }
$entryArguments=@($HostAssembly,'--Mode',$Mode,'--Root',$Root,'--ScenarioManifest',$ScenarioManifest,'--CandidateManifest',$CandidateManifest)
if ($Resume) { $entryArguments+='--Resume' }
if ($OwnedRestart) { $entryArguments+='--OwnedRestart' }
if ($InjectHostFault) { $entryArguments+='--InjectHostFault' }
if ($SafeDesktopConfirmed) { $entryArguments+='--SafeDesktopConfirmed' }
& dotnet @entryArguments
exit $LASTEXITCODE
