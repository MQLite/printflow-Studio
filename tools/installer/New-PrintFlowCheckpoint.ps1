<#
.SYNOPSIS
    Creates a pre-upgrade rollback checkpoint for PrintFlow Studio (SCRUM-11123 Part J).

.DESCRIPTION
    The smallest checkpoint that makes rollback truthful, and nothing more.

    What it captures, and why each item is the minimum rather than a choice:

      * the SQLite database (printflow.db and its -wal / -shm siblings) — the only artefact a
        newer application changes irreversibly, because migrations run forward only. Without a
        pre-upgrade copy, installing the previous executable after an upgrade puts an old
        application in front of a schema it does not understand, and that is not a rollback;
      * appsettings.json and appsettings.local.json — the shipped default and the operator's
        validated deviations, because an upgrade replaces the first and the second is the only
        record of what this installation was configured to differ on;
      * the installed product version and the preset identity — so a restore can state which
        installer to reinstall rather than leaving the operator to remember;
      * the production revalidation record, if one exists.

    What it deliberately does NOT capture: source images, InputSnapshots, Revisions, approved
    PNGs, PrintOutput TIFFs, Evidence and operator diagnostic packages. Those already live in the
    production workspace, which no upgrade, rollback or uninstall removes, and copying gigabytes
    of customer work into a second location would create a second place for it to leak from
    without protecting anything (Part J §23).

    PrintFlow must not be running. A database copied out from under a live writer is a copy of a
    torn moment, and the script refuses rather than producing one that looks valid.

    Every checkpoint goes in its own timestamped folder. Nothing is ever overwritten, and the
    script distinguishes "created" from "verified": files are written, then re-hashed from disk
    and compared against the manifest before the checkpoint is reported as verified (§24).

.PARAMETER WorkspaceRoot
    The production workspace. Defaults to the Workspace:Root in the installed appsettings.json.

.PARAMETER InstallFolder
    Where PrintFlow Studio is installed. Defaults to %ProgramFiles%\PrintFlow Studio.

.PARAMETER CheckpointRoot
    Where checkpoints are kept. Defaults to <WorkspaceRoot>\Checkpoints.

.PARAMETER Reason
    A short free-text note recorded in the manifest, e.g. "before upgrade to 0.2.0".
#>
[CmdletBinding()]
param(
    [string] $WorkspaceRoot,
    [string] $InstallFolder = (Join-Path $env:ProgramFiles 'PrintFlow Studio'),
    [string] $CheckpointRoot,
    [string] $Reason = 'pre-upgrade checkpoint'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --------------------------------------------------------------------------------------------
# Resolve the installation being checkpointed
# --------------------------------------------------------------------------------------------
$appSettingsPath = Join-Path $InstallFolder 'appsettings.json'
$localSettingsPath = Join-Path $InstallFolder 'appsettings.local.json'
$exePath = Join-Path $InstallFolder 'PrintFlow.App.exe'

if (-not (Test-Path $appSettingsPath)) {
    throw "No appsettings.json under '$InstallFolder'. Point -InstallFolder at the installed application."
}

$settings = Get-Content -LiteralPath $appSettingsPath -Raw | ConvertFrom-Json
if (Test-Path $localSettingsPath) {
    # The local override wins section by section, exactly as PrintFlowConfiguration.LoadFromFile
    # applies it. A checkpoint that read only the committed file would checkpoint a database the
    # running application does not use.
    $localSettings = Get-Content -LiteralPath $localSettingsPath -Raw | ConvertFrom-Json
    foreach ($section in $localSettings.PSObject.Properties.Name) {
        $settings | Add-Member -NotePropertyName $section -NotePropertyValue $localSettings.$section -Force
    }
}

if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) { $WorkspaceRoot = $settings.Workspace.Root }
if ([string]::IsNullOrWhiteSpace($CheckpointRoot)) { $CheckpointRoot = Join-Path $WorkspaceRoot 'Checkpoints' }

$databasePath = Join-Path $WorkspaceRoot $settings.Database.RelativePath

# Two spellings, both recorded, because they answer different questions. The three-part product
# version is the one the installer file name and the revalidation record use, so it is what the
# restore procedure quotes when it tells the operator which .msi to reinstall. The raw string is
# whatever the file version resource says — it carries the commit the build came from — and is
# kept for support rather than for naming a file that would not exist.
$productVersion = '(not installed)'
$productVersionRaw = '(not installed)'
if (Test-Path $exePath) {
    $versionInfo = (Get-Item -LiteralPath $exePath).VersionInfo
    $productVersionRaw = $versionInfo.ProductVersion
    if ([string]::IsNullOrWhiteSpace($productVersionRaw)) { $productVersionRaw = $versionInfo.FileVersion }

    $parts = ($productVersionRaw -split '[^0-9]') | Where-Object { $_ -ne '' }
    if ($parts.Count -lt 3) {
        throw "Could not read a three-part product version from '$exePath' (saw '$productVersionRaw')."
    }
    $productVersion = "$($parts[0]).$($parts[1]).$($parts[2])"
}

Write-Host "Workspace       : $WorkspaceRoot"
Write-Host "Install folder  : $InstallFolder"
Write-Host "Product version : $productVersion"
Write-Host "Database        : $databasePath"

# --------------------------------------------------------------------------------------------
# Refuse to checkpoint a running application
# --------------------------------------------------------------------------------------------
$running = @(Get-Process -Name 'PrintFlow.App' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    throw ("PrintFlow Studio is running (PID " + (($running | ForEach-Object { $_.Id }) -join ', ') +
           "). Close it before taking a checkpoint: a database copied from under a live writer " +
           "is not a restorable copy.")
}

# --------------------------------------------------------------------------------------------
# Create — one timestamped folder, never overwritten
# --------------------------------------------------------------------------------------------
$stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
$checkpointDir = Join-Path $CheckpointRoot "checkpoint-$stamp"
if (Test-Path $checkpointDir) {
    throw "Checkpoint folder '$checkpointDir' already exists. Checkpoints are never overwritten."
}
New-Item -ItemType Directory -Path $checkpointDir -Force | Out-Null

$captured = New-Object System.Collections.Generic.List[object]

function Copy-Captured([string] $Source, [string] $RelativeName) {
    if (-not (Test-Path $Source)) { return }
    $target = Join-Path $checkpointDir $RelativeName
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $target -Force
    $script:captured.Add([ordered] @{
        name   = $RelativeName
        source = $Source
        bytes  = (Get-Item -LiteralPath $target).Length
        sha256 = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    })
}

Copy-Captured $databasePath              'database\printflow.db'
Copy-Captured "$databasePath-wal"        'database\printflow.db-wal'
Copy-Captured "$databasePath-shm"        'database\printflow.db-shm'
Copy-Captured $appSettingsPath           'configuration\appsettings.json'
Copy-Captured $localSettingsPath         'configuration\appsettings.local.json'
Copy-Captured (Join-Path $WorkspaceRoot 'Revalidation\production-revalidation.json') `
                                         'revalidation\production-revalidation.json'

if (-not ($captured | Where-Object { $_.name -eq 'database\printflow.db' })) {
    throw "No database was captured from '$databasePath'. A checkpoint without the database cannot support a rollback."
}

$manifest = [ordered] @{
    schemaVersion   = 1
    kind            = 'printflow.rollback-checkpoint'
    createdAtLocal  = (Get-Date).ToString('o')
    createdBy       = "$env:USERDOMAIN\$env:USERNAME"
    machine         = $env:COMPUTERNAME
    reason          = $Reason
    productVersion  = $productVersion
    productVersionRaw = $productVersionRaw
    installFolder   = $InstallFolder
    workspaceRoot   = $WorkspaceRoot
    preset          = [ordered] @{
        id      = $settings.Preset.Id
        version = $settings.Preset.Version
        sha256  = $settings.Preset.ExpectedSha256
    }
    # ToArray() rather than @(): under Set-StrictMode -Version Latest, Windows PowerShell 5.1
    # throws "Argument types do not match" when a generic List is wrapped in @() inside an
    # [ordered] literal.
    files           = $captured.ToArray()
    restoreNote     = ('Restoring this checkpoint returns the metadata database to its state at ' +
                       'creation time. Production and customer files under the workspace are not ' +
                       'in this checkpoint and are not touched by a restore: work recorded after ' +
                       'this point stays on disk while the restored database no longer references ' +
                       'it. See Rollback in docs\printflow\installer-upgrade-rollback-runbook.md.')
}
$manifestPath = Join-Path $checkpointDir 'checkpoint-manifest.json'
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8

Write-Host ''
Write-Host "Created  : $checkpointDir" -ForegroundColor Green

# --------------------------------------------------------------------------------------------
# Verify — created is not verified (§24)
# --------------------------------------------------------------------------------------------
$problems = New-Object System.Collections.Generic.List[string]
foreach ($entry in $manifest.files) {
    $path = Join-Path $checkpointDir $entry.name
    if (-not (Test-Path $path)) { $problems.Add("missing: $($entry.name)"); continue }
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($actual -ne $entry.sha256) { $problems.Add("digest mismatch: $($entry.name)") }
}

if ($problems.Count -gt 0) {
    throw ("The checkpoint was created but did NOT verify. Do not upgrade against it:`n  " +
           ($problems -join "`n  "))
}

Write-Host ("Verified : {0} file(s) re-hashed from disk and matching the manifest." -f $manifest.files.Count) -ForegroundColor Green
Write-Host "Manifest : $manifestPath"
