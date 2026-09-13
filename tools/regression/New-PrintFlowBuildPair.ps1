<#
.SYNOPSIS
    Produces one controlled local harness/candidate build pair for regression evidence.

.DESCRIPTION
    Builds the Release regression test host and the Release self-contained win-x64 candidate
    from one stable set of committed inputs. Every intermediate and output path is owned by a new
    pair directory under an ignored staging root. The command does not run tests, applications,
    readiness checks, leases, or publication.

    build-pair.json is written only after both builds finish, the input snapshot is unchanged, all
    expected outputs are present, and the shared receipt reader accepts the completed receipt.
    A failed attempt remains as build-pair.json.incomplete and cannot be used as origin evidence.

.PARAMETER OutputRoot
    Ignored repository-local directory that will contain a new GUID-named pair directory. Defaults
    to artifacts\build-pairs.

.PARAMETER DotNetPath
    Explicit dotnet executable to use. Defaults to the repository workstation's per-user dotnet
    when present, then the application resolved from PATH. The exact path and reported SDK version
    are recorded and checked at each build boundary.

.PARAMETER VerifyOnly
    Validate an existing completed receipt and its current output files without building anything.

.PARAMETER ReceiptPath
    Completed build-pair.json to validate with -VerifyOnly.
#>
[CmdletBinding(DefaultParameterSetName = 'Create')]
param(
    [Parameter(ParameterSetName = 'Create')]
    [string] $OutputRoot,

    [Parameter(ParameterSetName = 'Create')]
    [string] $DotNetPath,

    [Parameter(Mandatory, ParameterSetName = 'Verify')]
    [switch] $VerifyOnly,

    [Parameter(Mandatory, ParameterSetName = 'Verify')]
    [string] $ReceiptPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$ContractPath = Join-Path $PSScriptRoot 'PrintFlowBuildPair.ps1'
if (-not (Test-Path -LiteralPath $ContractPath -PathType Leaf)) {
    throw "The shared build-pair contract was not found at '$ContractPath'."
}
. $ContractPath

if ($VerifyOnly) {
    $verified = Read-PrintFlowBuildPair -ReceiptPath $ReceiptPath
    Write-Host "Build pair verified: $($verified.PairId)" -ForegroundColor Green
    return $verified
}

function Invoke-Git {
    param([Parameter(Mandatory)][string[]] $Arguments)

    $output = & git -C $RepositoryRoot @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
    return @($output)
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Path
    )

    $rootPrefix = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "'$fullPath' is outside the owned root '$Root'."
    }
    return $fullPath.Substring($rootPrefix.Length).Replace('\', '/')
}

function Get-Sha256 {
    param([Parameter(Mandatory)][string] $Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-BuildInputSnapshot {
    # These are the source trees in the two project graphs plus the root files MSBuild/NuGet
    # automatically import. Generated output is deliberately absent. The tracked set is the
    # authority; the filesystem scan below separately catches untracked or ignored files whose
    # type can be included by SDK defaults or explicit Compile/Resource/Import items.
    $rootInputs = @(
        '.editorconfig',
        'Directory.Build.props',
        'Directory.Build.targets',
        'Directory.Packages.props',
        'Directory.Packages.targets',
        'Version.props',
        'global.json',
        'nuget.config',
        'appsettings.json'
    )
    # These local tools define how the pair is produced and how it is accepted by the two real
    # consumers. They do not change Product bytes, but an uncommitted tool must not mint trusted
    # local evidence about committed Product source.
    $toolInputs = @(
        'build/installer/Build-Installer.ps1',
        'tools/regression/New-PrintFlowBuildPair.ps1',
        'tools/regression/PrintFlowBuildPair.ps1',
        'tools/regression/Invoke-PrintFlowStandardRegressionSet.ps1',
        'tools/installer/Set-PrintFlowProductionRevalidation.ps1'
    )

    $pathspecs = @('src', 'tests') + $rootInputs + $toolInputs
    $tracked = @(
        @(Invoke-Git -Arguments (@('ls-files', '--') + $pathspecs)) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_.Replace('\', '/') } |
            Sort-Object -Unique
    )

    if ($tracked.Count -eq 0) {
        throw 'No tracked build inputs were found.'
    }

    $changed = @(
        @(Invoke-Git -Arguments (@('diff', '--name-only', 'HEAD', '--') + $pathspecs)) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_.Replace('\', '/') }
    )
    if ($changed.Count -gt 0) {
        throw ("Controlled builds require committed relevant inputs. Modified inputs:`n  " +
            (($changed | Sort-Object -Unique) -join "`n  "))
    }

    $trackedSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $tracked) { [void] $trackedSet.Add($name) }

    $unexpected = New-Object System.Collections.Generic.List[string]
    foreach ($sourceRootName in @('src', 'tests')) {
        $sourceRoot = Join-Path $RepositoryRoot $sourceRootName
        foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -Force -File) {
            $relative = Get-RelativePath -Root $RepositoryRoot -Path $file.FullName
            $segments = $relative.Split('/')
            # Only each known project's actual default output roots are excluded. A nested folder
            # merely named bin/obj/artifacts remains input-capable under SDK default globs and is
            # therefore checked (the same-label dirty-build counterexample can hide there).
            $isProjectOutput =
                ($segments.Count -ge 3 -and $segments[0] -eq 'src' -and
                    $segments[1] -like 'PrintFlow.*' -and $segments[2] -in @('bin', 'obj')) -or
                ($segments.Count -ge 3 -and $segments[0] -eq 'tests' -and
                    $segments[1] -eq 'PrintFlow.Tests' -and $segments[2] -in @('bin', 'obj'))
            if ($isProjectOutput) {
                continue
            }
            if (-not $trackedSet.Contains($relative)) {
                $unexpected.Add($relative)
            }
        }
    }

    # An untracked root import changes every SDK-style project before a normal status check can
    # explain why. Detect the names MSBuild and central package management discover implicitly.
    foreach ($name in $rootInputs) {
        $path = Join-Path $RepositoryRoot $name
        if ((Test-Path -LiteralPath $path -PathType Leaf) -and -not $trackedSet.Contains($name)) {
            $unexpected.Add($name)
        }
    }
    foreach ($name in $toolInputs) {
        $path = Join-Path $RepositoryRoot $name.Replace('/', '\')
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or -not $trackedSet.Contains($name)) {
            $unexpected.Add($name)
        }
    }

    if ($unexpected.Count -gt 0) {
        throw ("Untracked or ignored files can affect this build and must be reviewed and committed or removed:`n  " +
            (($unexpected | Sort-Object -Unique) -join "`n  "))
    }

    $editorConfigPath = Join-Path $RepositoryRoot '.editorconfig'
    if (-not (Test-Path -LiteralPath $editorConfigPath -PathType Leaf) -or
        -not (Get-Content -LiteralPath $editorConfigPath -Raw).StartsWith('root = true', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The repository .editorconfig must begin with 'root = true' so parent analyzer settings cannot enter the pair."
    }

    # Explicit imports are outside the SDK default item globs. Resolve the one repository import
    # used today and any future literal import only when its target is already in this snapshot.
    # Property-expanded or external imports require an intentional extension of this bounded
    # producer rather than silently inheriting machine/user MSBuild state.
    foreach ($name in $tracked | Where-Object { [System.IO.Path]::GetExtension($_) -in @('.csproj', '.props', '.targets') }) {
        $path = Join-Path $RepositoryRoot $name.Replace('/', '\')
        [xml] $projectXml = Get-Content -LiteralPath $path -Raw
        foreach ($import in @($projectXml.SelectNodes("//*[local-name()='Import']"))) {
            $project = [string] $import.Project
            if ($name -eq 'Directory.Build.props' -and
                $project -eq '$(MSBuildThisFileDirectory)Version.props') {
                continue
            }
            if ([string]::IsNullOrWhiteSpace($project) -or $project.Contains('$(') -or
                $project.Contains('*') -or $project.Contains('?')) {
                throw "Build input '$name' has an unresolved explicit import '$project'. Extend the controlled input contract first."
            }
            $importPath = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $path) $project))
            $importRelative = Get-RelativePath -Root $RepositoryRoot -Path $importPath
            if (-not $trackedSet.Contains($importRelative)) {
                throw "Build input '$name' imports '$project', which is outside the committed input snapshot."
            }
        }

        if ([System.IO.Path]::GetExtension($name) -eq '.csproj') {
            foreach ($reference in @($projectXml.SelectNodes("//*[local-name()='ProjectReference']"))) {
                $include = [string] $reference.Include
                if ([string]::IsNullOrWhiteSpace($include) -or $include.Contains('$(') -or
                    $include.Contains('*') -or $include.Contains('?')) {
                    throw "Project '$name' has an unresolved ProjectReference '$include'. Extend the controlled input contract first."
                }
                $referencePath = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $path) $include))
                $referenceRelative = Get-RelativePath -Root $RepositoryRoot -Path $referencePath
                if (($referenceRelative -notlike 'src/*' -and $referenceRelative -notlike 'tests/*') -or
                    -not $trackedSet.Contains($referenceRelative)) {
                    throw "Project '$name' references '$include', which is outside the committed source/test input graph."
                }
            }

            foreach ($itemName in @('Compile', 'EmbeddedResource', 'Content', 'Resource', 'Page',
                    'ApplicationDefinition', 'AdditionalFiles', 'Analyzer')) {
                foreach ($item in @($projectXml.SelectNodes("//*[local-name()='$itemName']"))) {
                    $include = [string] $item.Include
                    if ([string]::IsNullOrWhiteSpace($include)) { continue }
                    foreach ($part in $include.Split(';')) {
                        if ($part.Contains('$(')) {
                            throw "Project '$name' has an unresolved $itemName input '$part'. Extend the controlled input contract first."
                        }
                        $literalPrefix = $part
                        $wildcard = $literalPrefix.IndexOfAny([char[]] @('*', '?'))
                        if ($wildcard -ge 0) {
                            $literalPrefix = $literalPrefix.Substring(0, $wildcard)
                            $literalPrefix = Split-Path -Parent $literalPrefix
                            if ([string]::IsNullOrWhiteSpace($literalPrefix)) { $literalPrefix = '.' }
                        }
                        $itemPath = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $path) $literalPrefix))
                        $itemRelative = Get-RelativePath -Root $RepositoryRoot -Path $itemPath
                        if ($itemRelative -notlike 'src/*' -and $itemRelative -notlike 'tests/*' -and
                            -not ($rootInputs -contains $itemRelative)) {
                            throw "Project '$name' includes $itemName input '$part' outside the bounded source/test/root inputs."
                        }
                    }
                }
            }
        }
    }

    $inputs = @(
        foreach ($name in $tracked) {
            $path = Join-Path $RepositoryRoot $name.Replace('/', '\')
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "Tracked build input '$name' is missing from the canonical checkout."
            }
            [ordered] @{
                Name = $name
                Sha256 = Get-Sha256 -Path $path
            }
        }
    )

    $canonical = [System.Text.StringBuilder]::new()
    foreach ($input in $inputs) {
        [void] $canonical.Append($input.Name).Append(':').Append($input.Sha256).Append("`n")
    }
    $digestBytes = [System.Text.Encoding]::UTF8.GetBytes($canonical.ToString())
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $digest = ([BitConverter]::ToString($sha256.ComputeHash($digestBytes))).Replace('-', '')
    } finally {
        $sha256.Dispose()
    }

    $revisionOutput = @(Invoke-Git -Arguments @('rev-parse', 'HEAD'))
    return [pscustomobject] @{
        SourceRevision = [string] $revisionOutput[0]
        InputDigest = $digest
        Inputs = $inputs
    }
}

function Assert-SameInputSnapshot {
    param(
        [Parameter(Mandatory)] $Expected,
        [Parameter(Mandatory)][string] $Boundary
    )

    $actual = Get-BuildInputSnapshot
    if ($actual.SourceRevision -ne $Expected.SourceRevision -or
        $actual.InputDigest -ne $Expected.InputDigest) {
        throw "Relevant source/build inputs changed $Boundary. The pair is incomplete and must be rebuilt."
    }
}

function Assert-SameSdkVersion {
    param(
        [Parameter(Mandatory)][string] $DotNetPath,
        [Parameter(Mandatory)][string] $Expected,
        [Parameter(Mandatory)][string] $Boundary
    )

    Push-Location $RepositoryRoot
    try {
        $observed = @(& $DotNetPath --version)
        $sdkExit = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    if ($sdkExit -ne 0 -or $observed.Count -ne 1 -or $observed[0].Trim() -ne $Expected) {
        throw "The resolved .NET SDK changed or became unavailable $Boundary. The pair is incomplete."
    }
}

function Get-ProductAssemblyNames {
    $authorityPath = Join-Path $RepositoryRoot 'src\PrintFlow.Infrastructure\Verification\ProductBuildIdentity.cs'
    $source = Get-Content -LiteralPath $authorityPath -Raw
    $matches = [regex]::Matches($source, '"(PrintFlow\.[A-Za-z0-9.]+\.dll)"')
    $names = @($matches | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    if ($names.Count -ne 4) {
        throw "Expected ProductBuildIdentity.cs to define four Product assembly names; found $($names.Count)."
    }
    return $names
}

function Get-ProductAssemblies {
    param(
        [Parameter(Mandatory)][string] $Folder,
        [Parameter(Mandatory)][string[]] $Names
    )

    return @(
        foreach ($name in $Names) {
            $path = Join-Path $Folder $name
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "Build succeeded but required Product assembly '$path' is missing."
            }
            $buildIdentity = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($path).ProductVersion
            if ([string]::IsNullOrWhiteSpace($buildIdentity)) {
                throw "Build identity could not be read from '$path'."
            }
            [ordered] @{
                Name = $name
                Sha256 = Get-Sha256 -Path $path
                BuildIdentity = $buildIdentity.Trim()
            }
        }
    )
}

function Get-HarnessArtifacts {
    param([Parameter(Mandatory)][string] $Folder)

    $files = @(Get-ChildItem -LiteralPath $Folder -Recurse -File | Sort-Object FullName)
    if ($files.Count -eq 0) { throw "The harness output folder '$Folder' is empty." }
    return @(
        foreach ($file in $files) {
            [ordered] @{
                Name = Get-RelativePath -Root $Folder -Path $file.FullName
                Sha256 = Get-Sha256 -Path $file.FullName
            }
        }
    )
}

function Write-IncompleteReceipt {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)] $State
    )
    $State | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Path -Encoding utf8
}

$canonicalOutput = @(Invoke-Git -Arguments @('rev-parse', '--show-toplevel'))
$canonicalRoot = [string] $canonicalOutput[0]
if (-not [System.IO.Path]::GetFullPath($canonicalRoot).TrimEnd('\').Equals(
        $RepositoryRoot.TrimEnd('\'), [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The script is not running from the canonical repository checkout '$RepositoryRoot'."
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $RepositoryRoot 'artifacts\build-pairs'
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$outputRelative = Get-RelativePath -Root $RepositoryRoot -Path (Join-Path $OutputRoot '.ownership-probe')
$null = & git -C $RepositoryRoot check-ignore -q -- $outputRelative
if ($LASTEXITCODE -ne 0) {
    throw "OutputRoot '$OutputRoot' must be inside a git-ignored repository staging directory."
}

if (-not [string]::IsNullOrWhiteSpace($DotNetPath)) {
    if (-not (Test-Path -LiteralPath $DotNetPath -PathType Leaf)) {
        throw "The requested dotnet executable does not exist at '$DotNetPath'."
    }
    $DotNetPath = (Resolve-Path -LiteralPath $DotNetPath).Path
} else {
    $dotnetCandidate = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $dotnetCandidate -PathType Leaf) {
        $DotNetPath = (Resolve-Path -LiteralPath $dotnetCandidate).Path
    } else {
        $dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1
        $DotNetPath = $dotnetCommand.Source
    }
}

$PairId = [guid]::NewGuid().ToString()
$PairRoot = Join-Path $OutputRoot $PairId
$HarnessArtifactsRoot = Join-Path $PairRoot 'harness-artifacts'
$HarnessFolder = Join-Path $PairRoot 'harness'
$CandidateArtifactsRoot = Join-Path $PairRoot 'candidate-artifacts'
$CandidateFolder = Join-Path $PairRoot 'candidate'
$IncompletePath = Join-Path $PairRoot 'build-pair.json.incomplete'
$ReceiptPath = Join-Path $PairRoot 'build-pair.json'
$HarnessLog = Join-Path $PairRoot 'harness-build.log'
$CandidateLog = Join-Path $PairRoot 'candidate-build.log'

New-Item -ItemType Directory -Path $PairRoot | Out-Null
$failureState = [ordered] @{
    Version = 1
    PairId = $PairId
    Status = 'Incomplete'
    Phase = 'InputSnapshot'
    StartedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
}
Write-IncompleteReceipt -Path $IncompletePath -State $failureState

try {
    $snapshot = Get-BuildInputSnapshot
    $failureState.SourceRevision = $snapshot.SourceRevision
    $failureState.InputDigest = $snapshot.InputDigest
    $failureState.Inputs = $snapshot.Inputs

    $unsupportedEnvironment = @(
        @(
            'MSBuildSDKsPath', 'MSBuildExtensionsPath', 'MSBuildUserExtensionsPath',
            'CustomBeforeMicrosoftCommonProps', 'CustomAfterMicrosoftCommonProps',
            'CustomBeforeMicrosoftCommonTargets', 'CustomAfterMicrosoftCommonTargets',
            'MSBuildProjectExtensionsPath', 'BaseOutputPath', 'BaseIntermediateOutputPath',
            'OutputPath', 'IntermediateOutputPath', 'ArtifactsPath', 'RestoreConfigFile',
            'DirectoryBuildPropsPath', 'DirectoryBuildTargetsPath', 'DirectoryPackagesPropsPath'
        ) | Where-Object { $null -ne [Environment]::GetEnvironmentVariable($_, 'Process') }
    )
    if ($unsupportedEnvironment.Count -gt 0) {
        throw ("Controlled builds refuse inherited MSBuild path/import overrides: " +
            ($unsupportedEnvironment -join ', '))
    }

    Push-Location $RepositoryRoot
    try {
        $sdkOutput = @(& $DotNetPath --version)
        $sdkExit = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    if ($sdkExit -ne 0 -or $sdkOutput.Count -ne 1 -or [string]::IsNullOrWhiteSpace($sdkOutput[0])) {
        throw "'$DotNetPath --version' failed or returned no SDK version."
    }
    $SdkVersion = $sdkOutput[0].Trim()

    $testProject = Join-Path $RepositoryRoot 'tests\PrintFlow.Tests\PrintFlow.Tests.csproj'
    $appProject = Join-Path $RepositoryRoot 'src\PrintFlow.App\PrintFlow.App.csproj'
    $nugetConfig = Join-Path $RepositoryRoot 'nuget.config'
    $directoryBuildProps = Join-Path $RepositoryRoot 'Directory.Build.props'
    $directoryBuildTargets = Join-Path $RepositoryRoot 'Directory.Build.targets'
    $directoryPackagesProps = Join-Path $RepositoryRoot 'Directory.Packages.props'
    $controlledBuildProperties = @(
        "-p:RestoreConfigFile=$nugetConfig",
        "-p:DirectoryBuildPropsPath=$directoryBuildProps",
        "-p:DirectoryBuildTargetsPath=$directoryBuildTargets",
        '-p:ImportDirectoryBuildTargets=false',
        "-p:DirectoryPackagesPropsPath=$directoryPackagesProps",
        '-p:RestoreLockedMode=true',
        '-p:RestoreForceEvaluate=true'
    )
    $harnessArguments = @(
        'build', $testProject,
        '-c', 'Release',
        '--nologo',
        '--artifacts-path', $HarnessArtifactsRoot,
        '-o', $HarnessFolder,
        '--no-incremental'
    ) + $controlledBuildProperties + @('-v:minimal')
    $candidateArguments = @(
        'publish', $appProject,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '--nologo',
        '--artifacts-path', $CandidateArtifactsRoot,
        '-o', $CandidateFolder
    ) + $controlledBuildProperties + @('-v:minimal')
    if (@($harnessArguments + $candidateArguments | Where-Object { $_ -isnot [string] }).Count -ne 0) {
        throw 'A controlled build command contains a non-string argument.'
    }
    $HarnessCommand = @($DotNetPath) + $harnessArguments
    $CandidateCommand = @($DotNetPath) + $candidateArguments
    $failureState.SdkVersion = $SdkVersion
    $failureState.DotnetPath = $DotNetPath
    $failureState.HarnessCommand = $HarnessCommand
    $failureState.CandidateCommand = $CandidateCommand
    Write-IncompleteReceipt -Path $IncompletePath -State $failureState

    $failureState.Phase = 'HarnessBuild'
    Write-IncompleteReceipt -Path $IncompletePath -State $failureState
    Push-Location $RepositoryRoot
    try {
        & $DotNetPath @harnessArguments 2>&1 | Tee-Object -FilePath $HarnessLog | Out-Host
        $harnessExit = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    if ($harnessExit -ne 0) { throw "Harness build failed with exit code $harnessExit. See '$HarnessLog'." }

    Assert-SameInputSnapshot -Expected $snapshot -Boundary 'between the harness and candidate builds'
    Assert-SameSdkVersion -DotNetPath $DotNetPath -Expected $SdkVersion `
        -Boundary 'between the harness and candidate builds'

    $failureState.Phase = 'CandidateBuild'
    Write-IncompleteReceipt -Path $IncompletePath -State $failureState
    Push-Location $RepositoryRoot
    try {
        & $DotNetPath @candidateArguments 2>&1 | Tee-Object -FilePath $CandidateLog | Out-Host
        $candidateExit = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    if ($candidateExit -ne 0) { throw "Candidate publish failed with exit code $candidateExit. See '$CandidateLog'." }

    Assert-SameInputSnapshot -Expected $snapshot -Boundary 'after the candidate build'
    Assert-SameSdkVersion -DotNetPath $DotNetPath -Expected $SdkVersion -Boundary 'after the candidate build'

    $failureState.Phase = 'OutputValidation'
    Write-IncompleteReceipt -Path $IncompletePath -State $failureState
    $productNames = Get-ProductAssemblyNames
    $receipt = [ordered] @{
        Version = 1
        PairId = $PairId
        SourceRevision = $snapshot.SourceRevision
        InputDigest = $snapshot.InputDigest
        Inputs = $snapshot.Inputs
        SdkVersion = $SdkVersion
        DotnetPath = $DotNetPath
        HarnessCommand = $HarnessCommand
        CandidateCommand = $CandidateCommand
        HarnessFolder = [System.IO.Path]::GetFullPath($HarnessFolder)
        CandidateFolder = [System.IO.Path]::GetFullPath($CandidateFolder)
        HarnessProductAssemblies = Get-ProductAssemblies -Folder $HarnessFolder -Names $productNames
        CandidateProductAssemblies = Get-ProductAssemblies -Folder $CandidateFolder -Names $productNames
        HarnessArtifacts = Get-HarnessArtifacts -Folder $HarnessFolder
        CompletedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    }

    $receiptTemporaryPath = Join-Path $PairRoot 'build-pair.json.pending'
    $receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $receiptTemporaryPath -Encoding utf8
    $verifiedReceipt = Read-PrintFlowBuildPair -ReceiptPath $receiptTemporaryPath -AllowPending
    Assert-SameInputSnapshot -Expected $snapshot -Boundary 'during final output inventory and receipt validation'
    Assert-SameSdkVersion -DotNetPath $DotNetPath -Expected $SdkVersion `
        -Boundary 'during final output inventory and receipt validation'
    Move-Item -LiteralPath $receiptTemporaryPath -Destination $ReceiptPath
    Remove-Item -LiteralPath $IncompletePath -Force

    Write-Host "Build pair complete: $PairId" -ForegroundColor Green
    Write-Host "Receipt: $ReceiptPath"
    return $verifiedReceipt
} catch {
    $failureState.Status = 'Failed'
    $failureState.FailedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    $failureState.Error = $_.Exception.Message
    Write-IncompleteReceipt -Path $IncompletePath -State $failureState
    throw
}
