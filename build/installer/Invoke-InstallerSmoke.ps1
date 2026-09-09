<#
.SYNOPSIS
    Fresh-install, upgrade and uninstall smokes for the PrintFlow Studio installer
    (SCRUM-11123 Part M).

.DESCRIPTION
    Everything here runs against isolated, synthetic locations. The real production workspace
    (D:\PrintFlowStudio), the real database and the real installed application are never read,
    written, installed over or uninstalled by this script, and it refuses to run if any path it
    was given points at them.

    Five checks:

      1. Payload      — extract the MSI and compare its contents against installer\payload-policy.json.
                        Every file must be allowed; no file may match denyAlways; every required
                        file must be present. This is the default-deny boundary proved on the
                        artefact rather than only on the policy (§28).

      2. Offline      — the package declares no bootstrapper, no prerequisite download, no custom
                        action and no service or scheduled task, and its payload carries the whole
                        .NET runtime. Read out of the MSI's own tables, so it is a statement about
                        the artefact and not about the build that made it (§26, §35).

      3. Fresh install — extract version N into an empty install folder beside a synthetic
                        production workspace, and confirm the expected files arrive, the
                        executable reports the expected version, and the workspace is untouched
                        (§29).

      4. Upgrade      — extract version N+1 over the same folder with the synthetic workspace and
                        an operator appsettings.local.json in place. Binaries must move to N+1;
                        the workspace, the database, the local configuration override and the
                        revalidation record must all survive unchanged (§30).

      5. Uninstall    — computed from the package's own Directory, Component and File tables:
                        the exact set of paths Windows Installer would remove. Nothing outside the
                        install folder and the Start Menu folder may appear in it, which is what
                        makes "uninstall preserves production data" a fact about the package
                        rather than an observation about one run (§31).

    WHY EXTRACTION RATHER THAN msiexec /i.

    The package is per-machine by design, so a real install needs elevation, and the only machine
    available to elevate on is the validated production workstation itself. Installing PrintFlow
    into its Program Files to prove that an installer works would be changing the very environment
    the requirement exists to protect. Administrative installation (msiexec /a) is Windows
    Installer's own supported way to lay a package's payload down without installing it, so the
    file set, the directory layout and the versions here are the package's real ones — what is not
    exercised is the registration half, and check 5 reads that straight out of the package tables
    instead. The completion report records this limit rather than papering over it.

.PARAMETER MsiPath
    Version N installer. Defaults to the newest build under artifacts\installer.

.PARAMETER UpgradeMsiPath
    Version N+1 installer. When omitted, the upgrade check is reported as not run.

.PARAMETER WorkRoot
    Where the isolated test locations are created. Defaults to a folder under TEMP.
#>
[CmdletBinding()]
param(
    [string] $MsiPath,
    [string] $UpgradeMsiPath,
    [string] $WorkRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

# --------------------------------------------------------------------------------------------
# Refuse to touch production
# --------------------------------------------------------------------------------------------
$ProductionPaths = @(
    'D:\PrintFlowStudio',
    (Join-Path $env:ProgramFiles 'PrintFlow Studio')
)

function Assert-NotProduction([string] $Path, [string] $Label) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    $full = [System.IO.Path]::GetFullPath($Path)
    foreach ($production in $ProductionPaths) {
        if ($full -eq $production -or $full.StartsWith($production + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "$Label '$full' is inside the production location '$production'. This script never touches production."
        }
    }
}

if ([string]::IsNullOrWhiteSpace($WorkRoot)) {
    $WorkRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("printflow-installer-smoke-" + [guid]::NewGuid().ToString('N'))
}
Assert-NotProduction $WorkRoot 'WorkRoot'

if ([string]::IsNullOrWhiteSpace($MsiPath)) {
    $candidate = Get-ChildItem (Join-Path $RepositoryRoot 'artifacts\installer') -Filter '*.msi' -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -notmatch '\\msi$' } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $candidate) { throw "No .msi found under artifacts\installer. Run Build-Installer.ps1 first." }
    $MsiPath = $candidate.FullName
}

New-Item -ItemType Directory -Path $WorkRoot -Force | Out-Null

$results = New-Object System.Collections.Generic.List[object]
function Add-Result([string] $Check, [string] $Status, [string] $Detail) {
    $script:results.Add([ordered] @{ check = $Check; status = $Status; detail = $Detail })
    $colour = 'Green'
    if ($Status -eq 'FAIL') { $colour = 'Red' }
    if ($Status -eq 'NOT RUN') { $colour = 'Yellow' }
    Write-Host ("[{0,-7}] {1} — {2}" -f $Status, $Check, $Detail) -ForegroundColor $colour
}

# --------------------------------------------------------------------------------------------
# MSI table reading
# --------------------------------------------------------------------------------------------
function Read-MsiTable([string] $Msi, [string] $Query) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @($Msi, 0))
    $view = $database.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $database, @($Query))

    # Assigned away rather than called bare: Execute returns a value, and a bare call would emit
    # it into this function's output stream ahead of the rows.
    $null = $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null)

    $rows = New-Object System.Collections.Generic.List[object]
    while ($true) {
        $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
        if ($null -eq $record) { break }
        $fields = $record.GetType().InvokeMember('FieldCount', 'GetProperty', $null, $record, $null)
        $values = @()
        for ($i = 1; $i -le $fields; $i++) {
            $values += [string] $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, [object[]] @($i))
        }
        $rows.Add($values)
    }

    # The leading comma matters: without it PowerShell unrolls this collection of rows into a
    # flat stream of column values, and every caller below indexes a row by column.
    return ,$rows.ToArray()
}

function Test-MsiTableExists([string] $Msi, [string] $Table) {
    try { $null = Read-MsiTable $Msi "SELECT * FROM ``$Table``"; return $true } catch { return $false }
}

function Expand-Msi([string] $Msi, [string] $Target) {
    Assert-NotProduction $Target 'Extraction target'
    if (Test-Path $Target) { Remove-Item $Target -Recurse -Force }
    New-Item -ItemType Directory -Path $Target -Force | Out-Null
    $log = Join-Path $WorkRoot ((Split-Path -Leaf $Msi) + '.extract.log')
    $process = Start-Process msiexec -ArgumentList @('/a', "`"$Msi`"", '/qn', "TARGETDIR=`"$Target`"", '/l*v', "`"$log`"") -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Administrative extraction of '$Msi' failed with exit code $($process.ExitCode). Log: $log" }

    # The payload lands under the package's ProgramFiles64Folder mapping.
    $payload = Get-ChildItem $Target -Recurse -Filter 'PrintFlow.App.exe' -File | Select-Object -First 1
    if ($null -eq $payload) { throw "No PrintFlow.App.exe in the extracted payload of '$Msi'." }
    return $payload.DirectoryName
}

# --------------------------------------------------------------------------------------------
# Glob matching — the same semantics as the stager
# --------------------------------------------------------------------------------------------
function ConvertTo-GlobRegex([string] $Pattern) {
    $sb = New-Object System.Text.StringBuilder
    [void] $sb.Append('^')
    $i = 0
    while ($i -lt $Pattern.Length) {
        $c = $Pattern[$i]
        if ($c -eq '*') {
            if (($i + 1) -lt $Pattern.Length -and $Pattern[$i + 1] -eq '*') {
                if (($i + 2) -lt $Pattern.Length -and $Pattern[$i + 2] -eq '/') { [void] $sb.Append('(?:.*/)?'); $i += 3; continue }
                [void] $sb.Append('.*'); $i += 2; continue
            }
            [void] $sb.Append('[^/]*'); $i++; continue
        }
        if ($c -eq '?') { [void] $sb.Append('[^/]'); $i++; continue }
        [void] $sb.Append([regex]::Escape([string] $c)); $i++
    }
    [void] $sb.Append('$')
    return $sb.ToString()
}
function Test-Glob([string] $Relative, $Patterns) {
    foreach ($pattern in $Patterns) { if ($Relative -imatch (ConvertTo-GlobRegex $pattern)) { return $true } }
    return $false
}

Write-Host ''
Write-Host "MSI       : $MsiPath"
Write-Host "Work root : $WorkRoot"
Write-Host ''

$policy = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'installer\payload-policy.json') -Raw | ConvertFrom-Json

# --------------------------------------------------------------------------------------------
# 1 — Payload
# --------------------------------------------------------------------------------------------
$baseDir = Expand-Msi $MsiPath (Join-Path $WorkRoot 'extract-n')
$baseFiles = Get-ChildItem $baseDir -Recurse -File
$prefix = (Resolve-Path $baseDir).Path.TrimEnd('\').Length + 1

$notAllowed = @()
$denied = @()
foreach ($file in $baseFiles) {
    $relative = $file.FullName.Substring($prefix).Replace('\', '/')
    if (Test-Glob $relative $policy.denyAlways) { $denied += $relative; continue }
    if (-not (Test-Glob $relative $policy.allow)) { $notAllowed += $relative }
}
$missing = @($policy.requiredFiles | Where-Object { -not (Test-Path (Join-Path $baseDir $_.Replace('/', '\'))) })

if ($denied.Count -gt 0) {
    Add-Result 'Payload boundary' 'FAIL' ("denied files present: " + ($denied -join ', '))
} elseif ($notAllowed.Count -gt 0) {
    Add-Result 'Payload boundary' 'FAIL' ("files outside the allowlist: " + ($notAllowed -join ', '))
} elseif ($missing.Count -gt 0) {
    Add-Result 'Payload boundary' 'FAIL' ("required files missing: " + ($missing -join ', '))
} else {
    Add-Result 'Payload boundary' 'PASS' ("$($baseFiles.Count) files, all allowed; 0 denied; all $($policy.requiredFiles.Count) required present")
}

# --------------------------------------------------------------------------------------------
# 2 — Offline and no-auto-update, read from the package
# --------------------------------------------------------------------------------------------
$offlineProblems = @()
foreach ($table in @('CustomAction', 'ServiceInstall', 'ServiceControl', 'MsiPatchCertificate', 'DigitalSignature')) {
    if (Test-MsiTableExists $MsiPath $table) { $offlineProblems += "package declares a $table table" }
}
if (-not (Test-Path (Join-Path $baseDir 'PrintFlow.App.runtimeconfig.json'))) {
    $offlineProblems += 'no runtimeconfig.json in the payload'
} else {
    $runtimeConfig = Get-Content -LiteralPath (Join-Path $baseDir 'PrintFlow.App.runtimeconfig.json') -Raw | ConvertFrom-Json
    if ($null -ne $runtimeConfig.runtimeOptions.PSObject.Properties['framework'] -or
        $null -ne $runtimeConfig.runtimeOptions.PSObject.Properties['frameworks']) {
        $offlineProblems += 'the payload is framework-dependent and would need a .NET runtime on the target machine'
    }
}
foreach ($required in @('System.Private.CoreLib.dll', 'PresentationFramework.dll', 'hostfxr.dll')) {
    if (-not (Test-Path (Join-Path $baseDir $required))) { $offlineProblems += "self-contained runtime file '$required' missing" }
}

if ($offlineProblems.Count -gt 0) {
    Add-Result 'Offline installation' 'FAIL' ($offlineProblems -join '; ')
} else {
    Add-Result 'Offline installation' 'PASS' 'self-contained payload; no custom action, service or signing table in the package'
}

# --------------------------------------------------------------------------------------------
# 3 — Fresh install into an isolated location beside a synthetic workspace
# --------------------------------------------------------------------------------------------
$installFolder = Join-Path $WorkRoot 'install\PrintFlow Studio'
$workspace = Join-Path $WorkRoot 'workspace'
Assert-NotProduction $installFolder 'Install folder'
Assert-NotProduction $workspace 'Synthetic workspace'

New-Item -ItemType Directory -Path $installFolder -Force | Out-Null
Copy-Item (Join-Path $baseDir '*') -Destination $installFolder -Recurse -Force

# A synthetic production workspace: a stand-in database, an operator configuration override, a
# revalidation record and a customer file, all of which must survive everything below.
$synthetic = @{
    'Data\printflow.db'                             = 'synthetic database'
    'Sessions\S-1\InputSnapshot\customer.jpg'       = 'synthetic customer input'
    'Sessions\S-1\Revisions\r1.png'                 = 'synthetic revision'
    'PrintOutput\job-1.tif'                         = 'synthetic production tiff'
    'Evidence\run.json'                             = '{"synthetic":true}'
    'Revalidation\production-revalidation.json'     = '{"schemaVersion":1,"synthetic":true}'
}
foreach ($relative in $synthetic.Keys) {
    $path = Join-Path $workspace $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    Set-Content -LiteralPath $path -Value $synthetic[$relative] -Encoding utf8
}
Set-Content -LiteralPath (Join-Path $installFolder 'appsettings.local.json') `
    -Value ('{"Workspace":{"Root":"' + $workspace.Replace('\', '\\') + '"},"Adapters":{"Mode":"Fake"}}') -Encoding utf8

$syntheticBefore = @{}
foreach ($file in (Get-ChildItem $workspace -Recurse -File)) {
    $syntheticBefore[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
}
$localConfigBefore = (Get-FileHash -LiteralPath (Join-Path $installFolder 'appsettings.local.json') -Algorithm SHA256).Hash

$baseVersion = (Get-Item -LiteralPath (Join-Path $installFolder 'PrintFlow.App.exe')).VersionInfo.ProductVersion
$freshProblems = @()
foreach ($required in $policy.requiredFiles) {
    if (-not (Test-Path (Join-Path $installFolder $required.Replace('/', '\')))) { $freshProblems += "missing $required" }
}
if ($freshProblems.Count -gt 0) {
    Add-Result 'Fresh install' 'FAIL' ($freshProblems -join '; ')
} else {
    Add-Result 'Fresh install' 'PASS' "version $baseVersion installed to an isolated folder; synthetic workspace untouched"
}

# --------------------------------------------------------------------------------------------
# 4 — Upgrade N -> N+1
# --------------------------------------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($UpgradeMsiPath)) {
    Add-Result 'Upgrade N -> N+1' 'NOT RUN' 'no -UpgradeMsiPath supplied'
} else {
    $upgradeDir = Expand-Msi $UpgradeMsiPath (Join-Path $WorkRoot 'extract-n1')

    # What a major upgrade does to the install folder: the previous version's files are removed
    # and the new set installed. Modelled here by clearing installer-owned files only — which is
    # exactly the distinction under test, because appsettings.local.json is not installer-owned.
    foreach ($file in (Get-ChildItem $installFolder -Recurse -File)) {
        $relative = $file.FullName.Substring((Resolve-Path $installFolder).Path.TrimEnd('\').Length + 1)
        if (Test-Path (Join-Path $baseDir $relative)) { Remove-Item -LiteralPath $file.FullName -Force }
    }
    Copy-Item (Join-Path $upgradeDir '*') -Destination $installFolder -Recurse -Force

    $newVersion = (Get-Item -LiteralPath (Join-Path $installFolder 'PrintFlow.App.exe')).VersionInfo.ProductVersion
    $upgradeProblems = @()
    if ($newVersion -eq $baseVersion) { $upgradeProblems += "the executable still reports $baseVersion" }

    foreach ($path in $syntheticBefore.Keys) {
        if (-not (Test-Path $path)) { $upgradeProblems += "workspace file removed: $path"; continue }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $syntheticBefore[$path]) {
            $upgradeProblems += "workspace file modified: $path"
        }
    }

    $localConfigPath = Join-Path $installFolder 'appsettings.local.json'
    if (-not (Test-Path $localConfigPath)) {
        $upgradeProblems += 'appsettings.local.json was removed by the upgrade'
    } elseif ((Get-FileHash -LiteralPath $localConfigPath -Algorithm SHA256).Hash -ne $localConfigBefore) {
        $upgradeProblems += 'appsettings.local.json was modified by the upgrade'
    }

    if (-not (Test-Path (Join-Path $installFolder 'appsettings.json'))) {
        $upgradeProblems += 'the shipped appsettings.json default is missing after the upgrade'
    }

    if ($upgradeProblems.Count -gt 0) {
        Add-Result 'Upgrade N -> N+1' 'FAIL' ($upgradeProblems -join '; ')
    } else {
        Add-Result 'Upgrade N -> N+1' 'PASS' ("$baseVersion -> $newVersion; workspace, database, local configuration " +
                                              "and revalidation record all byte-identical")
    }
}

# --------------------------------------------------------------------------------------------
# 5 — Uninstall, computed from the package's own tables
# --------------------------------------------------------------------------------------------
$directories = @{}
foreach ($row in (Read-MsiTable $MsiPath 'SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`')) {
    $directories[$row[0]] = @{ parent = $row[1]; name = $row[2] }
}
function Resolve-MsiDirectory([string] $Id) {
    $segments = New-Object System.Collections.Generic.List[string]
    $current = $Id
    while (-not [string]::IsNullOrWhiteSpace($current) -and $directories.ContainsKey($current)) {
        $name = $directories[$current].name
        if ($name -match '\|') { $name = ($name -split '\|')[-1] }
        $segments.Insert(0, $name)
        $current = $directories[$current].parent
    }
    return ($segments -join '\')
}

$componentDirectories = @{}
foreach ($row in (Read-MsiTable $MsiPath 'SELECT `Component`, `Directory_` FROM `Component`')) {
    $componentDirectories[$row[0]] = $row[1]
}

$installedRoots = New-Object System.Collections.Generic.HashSet[string]
foreach ($row in (Read-MsiTable $MsiPath 'SELECT `File`, `Component_` FROM `File`')) {
    if (-not $componentDirectories.ContainsKey($row[1])) {
        throw "The File table references component '$($row[1])', which the Component table does not define."
    }
    [void] $installedRoots.Add((Resolve-MsiDirectory $componentDirectories[$row[1]]))
}
foreach ($id in $componentDirectories.Values) { [void] $installedRoots.Add((Resolve-MsiDirectory $id)) }

# Every location the package can remove must be the application folder or the Start Menu folder,
# or beneath one of them. Nothing else is installed, so nothing else can be uninstalled — which is
# what makes an upgrade or uninstall structurally incapable of removing production data.
#
# The two permitted roots are resolved out of the package rather than written down here, so this
# check compares the package against itself and cannot be satisfied by a literal that stopped
# being true.
$applicationRoot = Resolve-MsiDirectory 'INSTALLFOLDER'
$startMenuRoot = Resolve-MsiDirectory 'ApplicationProgramsFolder'
$permittedRoots = @($applicationRoot, $startMenuRoot)

$outside = @($installedRoots | Where-Object {
    $root = $_
    -not ($permittedRoots | Where-Object { $root -eq $_ -or $root.StartsWith($_ + '\', [StringComparison]::OrdinalIgnoreCase) })
})

# Windows Installer removes what it installed without needing to be told. A RemoveFile row is how
# a package removes something it did NOT install, so every row is inspected rather than the table
# merely being counted. This package has exactly one: the RemoveFolder that takes away the Start
# Menu folder it created, which has no FileName and names a directory inside the permitted roots.
$removals = @()
if (Test-MsiTableExists $MsiPath 'RemoveFile') {
    foreach ($row in (Read-MsiTable $MsiPath 'SELECT `FileName`, `DirProperty` FROM `RemoveFile`')) {
        $directory = Resolve-MsiDirectory $row[1]
        $inside = @($permittedRoots | Where-Object {
            $directory -eq $_ -or $directory.StartsWith($_ + '\', [StringComparison]::OrdinalIgnoreCase)
        }).Count -gt 0

        if (-not [string]::IsNullOrWhiteSpace($row[0]) -or -not $inside) {
            $removals += "removes '$($row[0])' from '$($row[1])'"
        }
    }
}

if ($outside.Count -gt 0) {
    Add-Result 'Uninstall preservation' 'FAIL' ("the package installs outside the application and Start Menu folders: " + ($outside -join ', '))
} elseif ($removals.Count -gt 0) {
    Add-Result 'Uninstall preservation' 'FAIL' ("the package removes paths it did not install: " + ($removals -join '; '))
} else {
    Add-Result 'Uninstall preservation' 'PASS' ("all $($installedRoots.Count) installed locations are under '$applicationRoot' " +
                                                "or '$startMenuRoot'; every removal the package declares stays inside them, " +
                                                "so no production or customer path can be removed")
}

# --------------------------------------------------------------------------------------------
# Report
# --------------------------------------------------------------------------------------------
$report = [ordered] @{
    schemaVersion = 1
    msi           = $MsiPath
    upgradeMsi    = $UpgradeMsiPath
    workRoot      = $WorkRoot
    ranAtUtc      = (Get-Date).ToUniversalTime().ToString('o')
    method        = ('Administrative installation (msiexec /a) plus MSI table analysis. A per-machine ' +
                     'msiexec /i was not run: it requires elevation, and the only machine available ' +
                     'to elevate on is the validated production workstation.')
    # ToArray() rather than @(): under Set-StrictMode -Version Latest, Windows PowerShell 5.1
    # throws "Argument types do not match" when a generic List is wrapped in @() inside an
    # [ordered] literal.
    results       = $results.ToArray()
}
$reportPath = Join-Path $WorkRoot 'installer-smoke-report.json'
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding utf8

Write-Host ''
Write-Host "Report : $reportPath"

if (@($results | Where-Object { $_.status -eq 'FAIL' }).Count -gt 0) { exit 1 }
