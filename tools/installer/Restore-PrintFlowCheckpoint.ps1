<#
.SYNOPSIS
    Restores a PrintFlow Studio rollback checkpoint (SCRUM-11123 Part I).

.DESCRIPTION
    The database and configuration half of a rollback. It is one step of the procedure, never the
    whole of it: the runbook's Rollback section is the authority, and this script refuses to be
    mistaken for it.

    What it restores: the metadata database captured by New-PrintFlowCheckpoint.ps1, and, with
    -RestoreConfiguration, the configuration files beside it.

    What it does not touch, ever: source images, InputSnapshots, Revisions, approved PNGs,
    PrintOutput TIFFs, Evidence, operator diagnostic packages, or any other customer or
    production file. They are not in the checkpoint and this script does not delete them
    (Part I §22, Part K §25).

    The consequence of that, stated plainly because it is real and cannot be designed away:
    restoring an older metadata database does not remove the files produced after the checkpoint
    was taken. Those files remain physically present in the workspace and the restored database
    has no rows for them. They are not lost, and they are not visible in Recent Processing — they
    have to be reconciled by hand, or the work redone. The runbook says so in the same words.

    The production revalidation record is deliberately NOT restored. A rollback installs a
    different application version, which is exactly the case that must close Production until
    Environment Readiness and the standard regression set have been rerun. Restoring the old
    record would hand the rolled-back installation an approval it has not re-earned.

.PARAMETER CheckpointPath
    The checkpoint folder to restore, i.e. the one containing checkpoint-manifest.json.

.PARAMETER RestoreConfiguration
    Also restore appsettings.json and appsettings.local.json into the install folder. Requires
    write access to the installation directory.

.PARAMETER Force
    Proceed without the interactive confirmation.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $CheckpointPath,
    [switch] $RestoreConfiguration,
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$manifestPath = Join-Path $CheckpointPath 'checkpoint-manifest.json'
if (-not (Test-Path $manifestPath)) {
    throw "No checkpoint-manifest.json in '$CheckpointPath'. That is not a PrintFlow checkpoint folder."
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.kind -ne 'printflow.rollback-checkpoint') {
    throw "'$manifestPath' is not a PrintFlow rollback checkpoint manifest."
}

Write-Host "Checkpoint      : $CheckpointPath"
Write-Host "Created         : $($manifest.createdAtLocal) by $($manifest.createdBy)"
Write-Host "Product version : $($manifest.productVersion)"
Write-Host "Workspace       : $($manifest.workspaceRoot)"
Write-Host "Preset          : $($manifest.preset.id) $($manifest.preset.version)"

# --------------------------------------------------------------------------------------------
# Verify the checkpoint before trusting it
# --------------------------------------------------------------------------------------------
$problems = New-Object System.Collections.Generic.List[string]
foreach ($entry in $manifest.files) {
    $path = Join-Path $CheckpointPath $entry.name
    if (-not (Test-Path $path)) { $problems.Add("missing: $($entry.name)"); continue }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) {
        $problems.Add("digest mismatch: $($entry.name)")
    }
}
if ($problems.Count -gt 0) {
    throw ("This checkpoint does not verify and must not be restored:`n  " + ($problems -join "`n  "))
}
Write-Host "Verified        : $($manifest.files.Count) file(s) match the manifest." -ForegroundColor Green

$running = @(Get-Process -Name 'PrintFlow.App' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    throw "PrintFlow Studio is running. Close it before restoring a checkpoint."
}

if (-not $Force) {
    Write-Host ''
    Write-Host 'This replaces the current metadata database with the checkpoint copy.' -ForegroundColor Yellow
    Write-Host 'Work recorded after the checkpoint will no longer appear in Recent Processing.' -ForegroundColor Yellow
    Write-Host 'Its files stay on disk and are not deleted, but must be reconciled by hand.' -ForegroundColor Yellow
    $answer = Read-Host 'Type RESTORE to continue'
    if ($answer -ne 'RESTORE') { Write-Host 'Cancelled.'; return }
}

# --------------------------------------------------------------------------------------------
# Set the current database aside before replacing it — a restore is not a delete
# --------------------------------------------------------------------------------------------
$settings = Get-Content -LiteralPath (Join-Path $CheckpointPath 'configuration\appsettings.json') -Raw | ConvertFrom-Json
$databasePath = Join-Path $manifest.workspaceRoot $settings.Database.RelativePath
$stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')

foreach ($suffix in @('', '-wal', '-shm')) {
    $current = "$databasePath$suffix"
    if (Test-Path $current) {
        Move-Item -LiteralPath $current -Destination "$current.replaced-$stamp" -Force
    }
}

New-Item -ItemType Directory -Path (Split-Path -Parent $databasePath) -Force | Out-Null
foreach ($suffix in @('', '-wal', '-shm')) {
    $source = Join-Path $CheckpointPath ("database\printflow.db" + $suffix)
    if (Test-Path $source) { Copy-Item -LiteralPath $source -Destination "$databasePath$suffix" -Force }
}
Write-Host "Database restored to $databasePath" -ForegroundColor Green
Write-Host "Replaced database kept as $databasePath.replaced-$stamp"

if ($RestoreConfiguration) {
    foreach ($name in @('appsettings.json', 'appsettings.local.json')) {
        $source = Join-Path $CheckpointPath "configuration\$name"
        if (Test-Path $source) {
            $target = Join-Path $manifest.installFolder $name
            if (Test-Path $target) { Copy-Item -LiteralPath $target -Destination "$target.replaced-$stamp" -Force }
            Copy-Item -LiteralPath $source -Destination $target -Force
            Write-Host "Configuration restored: $target" -ForegroundColor Green
        }
    }
}

Write-Host ''
Write-Host 'Restore complete. This is NOT a return to Production.' -ForegroundColor Yellow
Write-Host 'Still required, in order:' -ForegroundColor Yellow
Write-Host '  1. Install the application version this checkpoint records:' -ForegroundColor Yellow
Write-Host "     PrintFlowStudio-$($manifest.productVersion)-win-x64.msi" -ForegroundColor Yellow
Write-Host '  2. Run Environment Readiness and confirm it passes.' -ForegroundColor Yellow
Write-Host '  3. Run the standard regression set and confirm it passes.' -ForegroundColor Yellow
Write-Host '  4. Record the result with Set-PrintFlowProductionRevalidation.ps1.' -ForegroundColor Yellow
Write-Host 'Production stays closed until all four are done.' -ForegroundColor Yellow
