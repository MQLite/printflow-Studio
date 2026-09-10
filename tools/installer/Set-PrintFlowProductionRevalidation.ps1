<#
.SYNOPSIS
    Records that this PrintFlow Studio installation has been revalidated (SCRUM-11123 Part H).

.DESCRIPTION
    Writes the production revalidation record that PrintFlow's environment verification reads.
    Until a record exists that matches the installed product version, the configured preset and
    its digest, the current Windows build and the accepted Meitu and Photoshop binaries,
    Production is closed. Installing a new PrintFlow version, upgrading Windows, or accepting a
    new preset that names a different Meitu or Photoshop binary each invalidate the record and
    close Production again — which is the whole point.

    This script is the only thing that writes that record. PrintFlow itself cannot: there is no
    writer for it anywhere in the application, and an architecture test asserts so. An
    application that could write its own production approval would be approving itself.

    THE STANDARD REGRESSION SET IS CHECKED, NOT ASSERTED.

    SCRUM-11123 requires the standard test set to be rerun and to pass before production use
    resumes. SCRUM-11065 defines that set: at least a normal JPG portrait, a complex background
    with fine hair, a transparent PNG, a complete customer design, a PSD with a compatible
    composite preview, a single-page PDF, and a reference production TIFF, each with its expected
    processing path and properties recorded.

    This script verifies that the set named by -StandardRegressionSetPath actually contains every
    one of those categories before it will record a pass. If the set is absent or incomplete, it
    writes the record with the standard regression set marked NotAvailable, which PrintFlow reads
    as blocking. There is no switch that turns "the set does not exist" into "the set passed",
    and adding one would defeat the requirement this script exists to enforce.

.PARAMETER InstallFolder
    Where PrintFlow Studio is installed. Defaults to %ProgramFiles%\PrintFlow Studio.

.PARAMETER WorkspaceRoot
    The production workspace. Defaults to the Workspace:Root in the installed configuration.

.PARAMETER EnvironmentReadinessPassed
    Set only after Environment Readiness has been run from Settings and reported PASS.

.PARAMETER StandardRegressionSetPath
    The root of the standard local regression set, i.e. the folder holding its manifests.

.PARAMETER StandardRegressionSetResult
    A JSON file produced by the regression run.

    THE RESULT IS BOUND TO THIS INSTALLATION, NOT COPIED FROM.

    This script used to read four fields from that file and, if its status said Passed, record that
    word alongside the environment facts it had just read from this machine. Those are two
    unrelated things, and pairing them made a genuine pass from one environment into a record about
    another (PF-AUDIT-R1, audit finding F3). No forgery was needed: pointing this script at the
    wrong result file was enough.

    It now reads the whole proposed attestation and validates it before anything is written:

      * the run's evidence binding is present and of a version this script knows;
      * every environment fact the run recorded matches this installation - product version, the
        preset's id, version and digest, the Windows build, the accepted Meitu and Photoshop
        digests, and the adapter mode;
      * the Product assemblies the run attested are byte-for-byte the ones installed here;
      * the set the run read is the set named by -StandardRegressionSetPath, manifest by manifest;
      * the seven required categories each appear exactly once, and the summary status agrees with
        the case outcomes underneath it.

    If any of that fails, NOTHING IS WRITTEN and any existing record is left exactly as it was.
    Unbindable input is not evidence that this installation failed either, so it is refused rather
    than recorded as a failure - see "Refusal and revocation are different" below.

.NOTES
    REFUSAL AND REVOCATION ARE DIFFERENT.

    Running this script with no -StandardRegressionSetResult, or with a validly bound result whose
    status is not Passed, still writes a record stating that honestly. That is the operator's
    existing and legitimate way to close Production, and it is preserved unchanged.

    Refusing invalid input is not that. A result that cannot be bound to this installation says
    nothing about this installation - not that it passed, and not that it failed - so recording
    either would be an invention. The active record is left alone and this script exits non-zero.
#>
[CmdletBinding()]
param(
    [string] $InstallFolder = (Join-Path $env:ProgramFiles 'PrintFlow Studio'),
    [string] $WorkspaceRoot,
    [switch] $EnvironmentReadinessPassed,
    [string] $StandardRegressionSetPath,
    [string] $StandardRegressionSetResult
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The seven categories SCRUM-11065 requires of the standard local regression set. Quoted from the
# original requirement rather than paraphrased, because this list is what decides whether a
# production gate opens.
$RequiredCategories = @(
    'NORMAL_JPG_PORTRAIT',
    'COMPLEX_BACKGROUND_FINE_HAIR',
    'TRANSPARENT_PNG',
    'COMPLETE_CUSTOMER_DESIGN',
    'PSD_WITH_COMPOSITE_PREVIEW',
    'SINGLE_PAGE_PDF',
    'REFERENCE_PRODUCTION_TIFF'
)

# --------------------------------------------------------------------------------------------
# Read the installation
# --------------------------------------------------------------------------------------------
$appSettingsPath = Join-Path $InstallFolder 'appsettings.json'
$localSettingsPath = Join-Path $InstallFolder 'appsettings.local.json'
$exePath = Join-Path $InstallFolder 'PrintFlow.App.exe'

if (-not (Test-Path -LiteralPath $appSettingsPath)) { throw "No appsettings.json under '$InstallFolder'." }
if (-not (Test-Path -LiteralPath $exePath)) { throw "No PrintFlow.App.exe under '$InstallFolder'." }

$settings = Get-Content -LiteralPath $appSettingsPath -Raw | ConvertFrom-Json
if (Test-Path -LiteralPath $localSettingsPath) {
    $localSettings = Get-Content -LiteralPath $localSettingsPath -Raw | ConvertFrom-Json
    foreach ($section in $localSettings.PSObject.Properties.Name) {
        $settings | Add-Member -NotePropertyName $section -NotePropertyValue $localSettings.$section -Force
    }
}
if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) { $WorkspaceRoot = $settings.Workspace.Root }

# Resolved here rather than at the point of writing, because the refusal path needs to name it: an
# operator whose input was refused has to be told, in the same breath, that the record they already
# had is still there.
$recordPath = Join-Path $WorkspaceRoot 'Revalidation\production-revalidation.json'

# Three components, matching the product version and not the assembly's four-part spelling.
$fileVersion = (Get-Item -LiteralPath $exePath).VersionInfo
$rawVersion = $fileVersion.ProductVersion
if ([string]::IsNullOrWhiteSpace($rawVersion)) { $rawVersion = $fileVersion.FileVersion }
$parts = ($rawVersion -split '[^0-9]') | Where-Object { $_ -ne '' }
if ($parts.Count -lt 3) { throw "Could not read a three-part product version from '$exePath' (saw '$rawVersion')." }
$productVersion = "$($parts[0]).$($parts[1]).$($parts[2])"

$presetPath = Join-Path $WorkspaceRoot $settings.Preset.Path
if (-not (Test-Path -LiteralPath $presetPath)) { throw "The configured preset manifest '$presetPath' does not exist." }
$presetSha = (Get-FileHash -LiteralPath $presetPath -Algorithm SHA256).Hash
if ($presetSha -ne $settings.Preset.ExpectedSha256) {
    throw ("The preset manifest does not hash to the configured ExpectedSha256. Resolve the preset " +
           "integrity failure before recording a revalidation.")
}

$preset = Get-Content -LiteralPath $presetPath -Raw | ConvertFrom-Json
$meituSha = $preset.meituContract.executableSha256
$photoshopSha = $preset.photoshopContract.executableSha256
$osBuild = [string] (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuildNumber
$adapterMode = [string] $settings.Adapters.Mode

# Which PrintFlow bytes are installed here. A three-part version is a name two different builds
# share, so it cannot answer "is this the thing that was tested"; these digests can, and the running
# application asks the same question of itself against the record this script writes. The four names
# are the product code the installer payload policy requires, and no others: the self-contained
# runtime beside them is not what a PrintFlow candidate is.
$ProductAssemblyFileNames = @(
    'PrintFlow.App.dll',
    'PrintFlow.Domain.dll',
    'PrintFlow.Infrastructure.dll',
    'PrintFlow.Workflow.dll'
)

$productAssemblies = @()
foreach ($name in $ProductAssemblyFileNames) {
    $assemblyPath = Join-Path $InstallFolder $name
    if (Test-Path -LiteralPath $assemblyPath) {
        $productAssemblies += [ordered] @{
            name           = $name
            sha256         = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
            buildIdentity  = [string] (Get-Item -LiteralPath $assemblyPath).VersionInfo.ProductVersion
        }
    } else {
        throw ("The installation at '$InstallFolder' is missing '$name'. A revalidation record " +
               "names the PrintFlow assemblies it covers, and this installation cannot be identified.")
    }
}

Write-Host "Product version : $productVersion"
Write-Host "Preset          : $($settings.Preset.Id) $($settings.Preset.Version)"
Write-Host "Windows build   : $osBuild"
Write-Host "Adapter mode    : $adapterMode"

# --------------------------------------------------------------------------------------------
# The standard regression set — verified, never asserted
# --------------------------------------------------------------------------------------------
$regressionStatus = 'NotAvailable'
$regressionSetId = $null
$regressionCompleted = $null
$regressionEvidence = $null
$regressionRunId = $null
$regressionInvocationId = $null
$regressionBindingVersion = 0
$regressionSetContentDigest = $null
$missingCategories = @($RequiredCategories)

# The evidence contract this script knows how to check. A run result written against a version it
# does not know is refused rather than read optimistically: the whole failure being repaired here is
# a reader that took what it recognised and ignored the rest.
$KnownEvidenceBindingVersion = 1

<#
.SYNOPSIS
    Decides whether a run result may become an attestation about this installation.
.DESCRIPTION
    Returns the reasons it may not, and an empty result means it may. Every comparison is between a
    fact the run recorded at the time it ran and the same fact read from this machine now. Nothing
    here fills a missing fact in from the machine: that substitution is precisely how a run of one
    environment came to be recorded against another.
#>
function Test-ProposedAttestation {
    param(
        [Parameter(Mandatory)] $Result,
        [Parameter(Mandatory)] [string] $ManifestFolder
    )

    $problems = New-Object System.Collections.Generic.List[string]

    # --- the binding exists and is one we understand -----------------------------------------
    if ($null -eq $Result.PSObject.Properties['Binding'] -or $null -eq $Result.Binding) {
        $problems.Add('The run result carries no evidence binding. It records what happened, but ' +
                      'not the identity of what it tested, so it cannot be shown to be about this ' +
                      'installation. Run the standard set again with the current tooling.')
        return ,$problems
    }

    # The binding's shape is checked before any of its fields are read. Under StrictMode reading an
    # absent property throws, and a validator whose whole job is refusing malformed input must
    # report that input rather than fail on it.
    $binding = $Result.Binding
    foreach ($field in @('Version', 'InvocationId', 'CandidateProductAssemblies', 'CandidateProblems',
                         'PresetSha256', 'OperatingSystemBuild', 'MeituSha256', 'PhotoshopSha256',
                         'SetManifests')) {
        if ($null -eq $binding.PSObject.Properties[$field]) {
            $problems.Add("The run result's evidence binding records no $field.")
        }
    }
    if ($problems.Count -gt 0) { return ,$problems }

    if ($binding.Version -ne $KnownEvidenceBindingVersion) {
        $problems.Add("The run result's evidence binding is version $($binding.Version); this " +
                      "script checks version $KnownEvidenceBindingVersion.")
        return ,$problems
    }

    foreach ($required in @('RunId', 'Workstation', 'ProductVersion', 'PresetId', 'PresetVersion', 'AdapterMode')) {
        if ($null -eq $Result.PSObject.Properties[$required] -or
            [string]::IsNullOrWhiteSpace([string] $Result.$required)) {
            $problems.Add("The run result records no $required.")
        }
    }
    # These four must be non-empty, not merely present. They are serialised as JSON null when the run
    # could not read them, and comparing null with null succeeds - so a run that never established
    # the preset digest or the accepted binary identities would have passed the comparisons below and
    # then had those fields written into the record FROM THIS MACHINE. That is exactly the
    # substitution this repair exists to prevent: a fact the run did not establish is missing, and a
    # missing fact is a refusal, not a blank to fill in.
    foreach ($field in @('InvocationId', 'PresetSha256', 'OperatingSystemBuild',
                         'MeituSha256', 'PhotoshopSha256')) {
        if ([string]::IsNullOrWhiteSpace([string] $binding.$field)) {
            $problems.Add("The run result's evidence binding records no $field, so that fact was " +
                          "never established by the run and cannot be supplied now.")
        }
    }
    if ($problems.Count -gt 0) { return ,$problems }

    # --- the environment the run tested is the one in front of us -----------------------------
    if ($Result.ProductVersion -ne $productVersion) {
        $problems.Add("The run tested PrintFlow $($Result.ProductVersion); this installation is $productVersion.")
    }
    if ($Result.Workstation -ne $env:COMPUTERNAME) {
        $problems.Add("The run ran on '$($Result.Workstation)'; this workstation is '$env:COMPUTERNAME'.")
    }
    if ($Result.PresetId -ne $settings.Preset.Id -or $Result.PresetVersion -ne $settings.Preset.Version) {
        $problems.Add(("The run tested preset $($Result.PresetId) $($Result.PresetVersion); this " +
                       "installation is configured for $($settings.Preset.Id) $($settings.Preset.Version)."))
    }
    if ($binding.PresetSha256 -ne $presetSha) {
        $problems.Add('The preset manifest the run verified is not the one this installation requires.')
    }
    if ($binding.OperatingSystemBuild -ne $osBuild) {
        $problems.Add("The run ran on Windows build $($binding.OperatingSystemBuild); this is build $osBuild.")
    }
    if ($binding.MeituSha256 -ne $meituSha) {
        $problems.Add('The accepted Meitu binary the run ran against is not the one accepted here.')
    }
    if ($binding.PhotoshopSha256 -ne $photoshopSha) {
        $problems.Add('The accepted Photoshop binary the run ran against is not the one accepted here.')
    }
    if ($Result.AdapterMode -ne 'Production') {
        $problems.Add("The run used adapter mode '$($Result.AdapterMode)'. Only a Production run is evidence.")
    }
    if ($adapterMode -ne 'Production') {
        $problems.Add("This installation is configured for adapter mode '$adapterMode', not Production.")
    }

    # --- the candidate the run attested is the payload installed here -------------------------
    $candidate = @($binding.CandidateProductAssemblies)
    if (@($binding.CandidateProblems).Count -gt 0) {
        foreach ($problem in @($binding.CandidateProblems)) {
            $problems.Add("The run could not bind an installed candidate: $problem")
        }
    } elseif ($candidate.Count -eq 0) {
        $problems.Add('The run attested no installed candidate, so there is nothing to compare this installation with.')
    } else {
        foreach ($installed in $productAssemblies) {
            $attested = $candidate | Where-Object {
                $null -ne $_.PSObject.Properties['Name'] -and $_.Name -eq $installed.name
            }
            if ($null -eq $attested) {
                $problems.Add("The run attested no identity for $($installed.name).")
            } elseif ($null -eq $attested.PSObject.Properties['Sha256'] -or
                      [string]::IsNullOrWhiteSpace([string] $attested.Sha256)) {
                # An identity that could not be established is not a matching identity.
                $problems.Add("The run attested no digest for $($installed.name).")
            } elseif ($attested.Sha256 -ne $installed.sha256) {
                $problems.Add(("$($installed.name) is not the file the run attested. The version " +
                               "string is unchanged; the bytes are not."))
            }
        }
    }

    # --- the set the run read is the set named here -------------------------------------------
    $attestedManifests = @($binding.SetManifests)
    if ($attestedManifests.Count -eq 0) {
        $problems.Add('The run recorded no set content, so "the same set" is a claim about a name only.')
    } else {
        $onDisk = @{}
        foreach ($file in (Get-ChildItem -LiteralPath $ManifestFolder -Recurse -Filter '*.json' -File)) {
            $onDisk[(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash] = $file.Name
        }
        $attestedCategories = New-Object System.Collections.Generic.HashSet[string]
        foreach ($manifest in $attestedManifests) {
            if ($null -eq $manifest.PSObject.Properties['ManifestSha256'] -or
                [string]::IsNullOrWhiteSpace([string] $manifest.ManifestSha256)) {
                $problems.Add('The run recorded a set manifest with no digest.')
            } elseif (-not $onDisk.ContainsKey([string] $manifest.ManifestSha256)) {
                $problems.Add(("The manifest the run read for $($manifest.FixtureId) is not among the " +
                               "manifests under '$ManifestFolder'. The set has changed since the run."))
            }
            if ($null -ne $manifest.PSObject.Properties['Category'] -and
                -not [string]::IsNullOrWhiteSpace([string] $manifest.Category)) {
                [void] $attestedCategories.Add(([string] $manifest.Category).ToUpperInvariant())
            }
        }

        # Both directions. Every manifest the run claims must be on disk, and the run must have read
        # a manifest for each required category - otherwise a binding naming one manifest out of
        # seven would satisfy a check that every manifest it named was present.
        foreach ($required in $RequiredCategories) {
            if (-not $attestedCategories.Contains($required)) {
                $problems.Add("The run read no manifest for required category $required.")
            }
        }
    }

    # --- the verdict is re-derived, never copied ----------------------------------------------
    if ($null -eq $Result.PSObject.Properties['Cases'] -or $null -eq $Result.Cases) {
        $problems.Add('The run result records no cases, so there is no verdict to re-derive.')
        return ,$problems
    }

    $cases = @($Result.Cases)
    $seen = @{}
    foreach ($case in $cases) {
        $category = ([string] $case.Category).ToUpperInvariant()
        if ($seen.ContainsKey($category)) {
            $problems.Add("Category $category is claimed by more than one case; that cannot resolve to one verdict.")
        }
        $seen[$category] = $true
    }
    foreach ($required in $RequiredCategories) {
        if (-not $seen.ContainsKey($required)) {
            $problems.Add("The run recorded no case for required category $required.")
        }
    }

    if ($Result.status -eq 'Passed') {
        foreach ($case in $cases) {
            if ($case.Outcome -ne 'Passed') {
                $problems.Add(("The summary says Passed while $($case.Category) is $($case.Outcome). " +
                               "A summary that contradicts its own cases is not a verdict."))
            }
            foreach ($decision in @($case.ManualDecisions)) {
                if ($decision.Outcome -ne 'Passed') {
                    $problems.Add(("The summary says Passed while the qualitative check " +
                                   "$($decision.Id) is $($decision.Outcome)."))
                }
                if ([string]::IsNullOrWhiteSpace([string] $decision.DecidedBy)) {
                    $problems.Add("The qualitative check $($decision.Id) records no decider.")
                }
            }
        }
        if (@($Result.MissingCategories).Count -gt 0) {
            $problems.Add("The summary says Passed while recording missing categories: $(@($Result.MissingCategories) -join ', ').")
        }

        # A synthetic decision stays synthetic all the way to the gate. The review path labels
        # decisions that came from a test of this protocol rather than from a person, and that label
        # is load-bearing here: a protocol test must not be able to produce a production approval,
        # however correctly bound the rest of its evidence is.
        if ($null -ne $Result.PSObject.Properties['Reviews']) {
            foreach ($review in @($Result.Reviews)) {
                if ($null -ne $review -and
                    $null -ne $review.PSObject.Properties['Synthetic'] -and
                    [bool] $review.Synthetic) {
                    $problems.Add(("The run's qualitative checks were concluded by a synthetic review " +
                                   "($($review.DecidedBy)). A synthetic decision is not an operator's " +
                                   "acceptance and cannot open Production."))
                }
            }
        }
    }

    return ,$problems
}

# A parameter that was supplied but does not resolve is refused, and refused BEFORE anything is
# written. Omitting a parameter and mistyping one are different acts: omitting -StandardRegressionSetResult
# is the operator deliberately recording "no passing set", while a path with a typo in it is input
# this script cannot read - and recording a NotAvailable record from that would close Production
# and supersede the operator's existing record on the strength of a slip. -LiteralPath throughout,
# because a bare Test-Path treats [ and ] as wildcards and would report a real folder as absent.
$suppliedButUnreadable = New-Object System.Collections.Generic.List[string]
if (-not [string]::IsNullOrWhiteSpace($StandardRegressionSetPath) -and
    -not (Test-Path -LiteralPath $StandardRegressionSetPath)) {
    $suppliedButUnreadable.Add("-StandardRegressionSetPath '$StandardRegressionSetPath' does not exist.")
}
if (-not [string]::IsNullOrWhiteSpace($StandardRegressionSetResult) -and
    -not (Test-Path -LiteralPath $StandardRegressionSetResult)) {
    $suppliedButUnreadable.Add("-StandardRegressionSetResult '$StandardRegressionSetResult' does not exist.")
}
if (-not [string]::IsNullOrWhiteSpace($StandardRegressionSetResult) -and
    [string]::IsNullOrWhiteSpace($StandardRegressionSetPath)) {
    $suppliedButUnreadable.Add(
        'A run result was supplied without -StandardRegressionSetPath, so the set it claims to have ' +
        'run cannot be checked.')
}

if ($suppliedButUnreadable.Count -gt 0) {
    Write-Host ''
    Write-Host 'THE PROPOSED REVALIDATION WAS REFUSED' -ForegroundColor Red
    foreach ($problem in $suppliedButUnreadable) { Write-Host "  - $problem" -ForegroundColor Red }
    Write-Host ''
    Write-Host ('Nothing was written and nothing existing was changed.') -ForegroundColor Red
    if (Test-Path -LiteralPath $recordPath) {
        Write-Host "The active record at '$recordPath' is untouched." -ForegroundColor Red
    }
    Write-Host ''
    Write-Host ('To close Production deliberately, run this script without ' +
                '-StandardRegressionSetResult: that records the set as NotAvailable, honestly and ' +
                'on purpose.') -ForegroundColor Yellow
    exit 3
}

if (-not [string]::IsNullOrWhiteSpace($StandardRegressionSetPath)) {
    $present = New-Object System.Collections.Generic.HashSet[string]
    foreach ($manifest in (Get-ChildItem -LiteralPath $StandardRegressionSetPath -Recurse -Filter '*.json' -File)) {
        try {
            $entry = Get-Content -LiteralPath $manifest.FullName -Raw | ConvertFrom-Json
        } catch { continue }
        # A manifest, not merely a JSON file that happens to name a category. The per-case evidence a
        # run writes into its own folder carries a Category too, so counting those would let a
        # previous run's output satisfy the seven-category gate if this parameter were pointed at the
        # set root instead of its manifests folder. A manifest states its fixture and its file.
        if ($null -ne $entry.PSObject.Properties['category'] -and
            -not [string]::IsNullOrWhiteSpace($entry.category) -and
            $null -ne $entry.PSObject.Properties['fixtureId'] -and
            $null -ne $entry.PSObject.Properties['file']) {
            [void] $present.Add(([string] $entry.category).ToUpperInvariant())
        }
    }
    $missingCategories = @($RequiredCategories | Where-Object { -not $present.Contains($_) })

    if ($missingCategories.Count -eq 0) {
        if ([string]::IsNullOrWhiteSpace($StandardRegressionSetResult) -or -not (Test-Path -LiteralPath $StandardRegressionSetResult)) {
            Write-Host ''
            Write-Host ('The regression set is complete but no run result was supplied. ' +
                        'Recording the set as NotAvailable: a set that exists is not a set that ran.') -ForegroundColor Yellow
        } else {
            $result = Get-Content -LiteralPath $StandardRegressionSetResult -Raw | ConvertFrom-Json

            # Validated before anything is written, and a refusal ends the script here. The record
            # on disk - whatever it says - is left exactly as it was: input that cannot be bound to
            # this installation is not evidence that this installation passed, and it is not
            # evidence that it failed either, so neither is recorded.
            $attestationProblems = Test-ProposedAttestation -Result $result -ManifestFolder $StandardRegressionSetPath

            if ($attestationProblems.Count -gt 0) {
                Write-Host ''
                Write-Host 'THE PROPOSED REVALIDATION WAS REFUSED' -ForegroundColor Red
                foreach ($problem in $attestationProblems) { Write-Host "  - $problem" -ForegroundColor Red }
                Write-Host ''
                Write-Host ("This run result cannot be shown to be about this installation, so no " +
                            "record was written and nothing existing was changed.") -ForegroundColor Red
                if (Test-Path -LiteralPath $recordPath) {
                    Write-Host "The active record at '$recordPath' is untouched." -ForegroundColor Red
                }
                Write-Host ''
                Write-Host ('To close Production deliberately instead, run this script without ' +
                            '-StandardRegressionSetResult: that records the set as NotAvailable, ' +
                            'honestly and on purpose.') -ForegroundColor Yellow
                exit 3
            }

            $regressionSetId = $result.setId
            $regressionCompleted = $result.completedAtLocal
            $regressionEvidence = $result.evidencePath
            $regressionRunId = $result.RunId
            $regressionInvocationId = $result.Binding.InvocationId
            $regressionBindingVersion = $result.Binding.Version
            $regressionSetContentDigest = $result.Binding.SetContentDigest
            if ($result.status -eq 'Passed') { $regressionStatus = 'Passed' } else { $regressionStatus = 'Failed' }
        }
    }
}

if ($regressionStatus -ne 'Passed') {
    Write-Host ''
    Write-Host 'STANDARD REGRESSION SET: NOT PASSED' -ForegroundColor Red
    if ($missingCategories.Count -gt 0) {
        Write-Host ('The standard local regression set required by SCRUM-11065 is absent or ' +
                    'incomplete. Missing categories:') -ForegroundColor Red
        foreach ($category in $missingCategories) { Write-Host "  - $category" -ForegroundColor Red }
    }
    Write-Host ('A record will still be written, stating this honestly. PrintFlow reads it as ' +
                'blocking, so Production stays closed until the set exists and passes.') -ForegroundColor Red
}

# --------------------------------------------------------------------------------------------
# Write the record
# --------------------------------------------------------------------------------------------
$record = [ordered] @{
    schemaVersion              = 2
    productVersion             = $productVersion
    productAssemblies          = $productAssemblies
    presetId                   = $settings.Preset.Id
    presetVersion              = $settings.Preset.Version
    presetSha256               = $presetSha
    operatingSystemBuild       = $osBuild
    meituSha256                = $meituSha
    photoshopSha256            = $photoshopSha
    environmentReadinessPassed = [bool] $EnvironmentReadinessPassed
    standardRegressionSet      = [ordered] @{
        setId                 = $regressionSetId
        status                = $regressionStatus
        completedAtLocal      = $regressionCompleted
        evidencePath          = $regressionEvidence
        runId                 = $regressionRunId
        invocationId          = $regressionInvocationId
        evidenceBindingVersion = $regressionBindingVersion
        setContentDigest      = $regressionSetContentDigest
    }
    attestedBy                 = "$env:USERDOMAIN\$env:USERNAME"
    attestedAtLocal            = (Get-Date).ToString('o')
}

New-Item -ItemType Directory -Path (Split-Path -Parent $recordPath) -Force | Out-Null

# Written beside the record and moved over it, so the active record is either the old one or the
# new one and never half of either. A crash or a full disk partway through a direct write would
# leave PrintFlow reading an unparseable record - which fails closed, but closes Production for a
# reason that has nothing to do with the environment.
$stagedPath = "$recordPath.staging"
$record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $stagedPath -Encoding utf8

# Read back and parsed before it replaces anything. The one thing worse than refusing a valid
# attestation is publishing an invalid one.
try {
    $staged = Get-Content -LiteralPath $stagedPath -Raw | ConvertFrom-Json
} catch {
    Remove-Item -LiteralPath $stagedPath -Force -ErrorAction SilentlyContinue
    throw "The revalidation record could not be written as readable JSON. Nothing was replaced. $($_.Exception.Message)"
}
if ($staged.schemaVersion -ne 2 -or $staged.standardRegressionSet.status -ne $regressionStatus) {
    Remove-Item -LiteralPath $stagedPath -Force -ErrorAction SilentlyContinue
    throw 'The revalidation record did not read back as it was written. Nothing was replaced.'
}

if (Test-Path -LiteralPath $recordPath) {
    $stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
    Copy-Item -LiteralPath $recordPath -Destination "$recordPath.superseded-$stamp" -Force
}
Move-Item -LiteralPath $stagedPath -Destination $recordPath -Force

Write-Host ''
Write-Host "Record written : $recordPath"
Write-Host "Readiness      : $([bool] $EnvironmentReadinessPassed)"
Write-Host "Regression set : $regressionStatus"

if ($EnvironmentReadinessPassed -and $regressionStatus -eq 'Passed') {
    Write-Host ''
    Write-Host 'This installation is revalidated. Production may resume.' -ForegroundColor Green
} else {
    Write-Host ''
    Write-Host 'Production remains CLOSED. Both Environment Readiness and the standard regression set must pass.' -ForegroundColor Red
    exit 2
}
