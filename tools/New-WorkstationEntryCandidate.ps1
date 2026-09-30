[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$BuildDirectory,
    [Parameter(Mandatory)][string]$ManifestDirectory,
    [Parameter(Mandatory)][string]$RunId,
    [Parameter(Mandatory)][string]$BuildEvidence,
    [string[]]$ExcludedRoot=@(),
    [ValidateSet('Debug','Release')][string]$Configuration='Debug'
)
# Metadata preparation only. Run after a successful fresh build; this script never builds,
# executes a product assembly, creates a runtime run root, or authorizes desktop use.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

function Full-ExistingDirectory([string]$Value) {
    $full=[IO.Path]::GetFullPath($Value)
    if ($Value -cne $full -or -not [IO.Directory]::Exists($full)) { throw "Explicit existing absolute directory required: $Value" }
    return $full
}
function Within([string]$Child,[string]$Parent) {
    return $Child.StartsWith($Parent.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)
}
function Reject-ReparseAncestors([string]$Path) {
    for ($current=$Path; $current; $current=[IO.Path]::GetDirectoryName($current)) {
        if (Test-Path -LiteralPath $current) {
            if (((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse path refused: $current" }
        }
    }
}
function Get-ModuleId([string]$Path) {
    if ([IO.Path]::GetExtension($Path) -notin '.dll','.exe') { return $null }
    $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try {
        $reader=[System.Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            if (-not $reader.HasMetadata) { return $null }
            $metadata=[System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($reader)
            return $metadata.GetGuid($metadata.GetModuleDefinition().Mvid).ToString()
        } finally { $reader.Dispose() }
    } finally { $stream.Dispose() }
}

$repo=Full-ExistingDirectory $RepositoryRoot
$build=Full-ExistingDirectory $BuildDirectory
$manifest=[IO.Path]::GetFullPath($ManifestDirectory)
$taskArtifacts=Join-Path $repo 'artifacts\pf-opux-scrum11154-workstation-entry'
if ($ManifestDirectory -cne $manifest -or -not (Within $manifest $taskArtifacts) -or (Within $manifest (Join-Path $taskArtifacts 'runs'))) { throw 'Manifests must have an explicit task-artifacts destination outside runs.' }
if (-not (Within $build (Join-Path $taskArtifacts 'build'))) { throw 'Use the task-isolated build output, not installed or ordinary application output.' }
if (Test-Path -LiteralPath $manifest) { throw 'Manifest destination exists; refuse overwrite.' }
if ($RunId -notmatch '^[A-Za-z0-9][A-Za-z0-9-]*$') { throw 'RunId must be a nonempty alphanumeric/hyphen leaf.' }
Reject-ReparseAncestors $repo
Reject-ReparseAncestors $build
Reject-ReparseAncestors $manifest
foreach ($name in 'PrintFlow.WorkstationEntry.dll','PrintFlow.App.dll','PrintFlow.Domain.dll','PrintFlow.Workflow.dll','PrintFlow.Infrastructure.dll','PrintFlow.WorkstationEntry.runtimeconfig.json','PrintFlow.WorkstationEntry.deps.json') {
    if (-not [IO.File]::Exists((Join-Path $build $name))) { throw "Missing built host dependency: $name" }
}
$evidence=[IO.Path]::GetFullPath($BuildEvidence)
if ($BuildEvidence -cne $evidence -or -not (Within $evidence $taskArtifacts) -or -not [IO.File]::Exists($evidence)) { throw 'Explicit task-owned successful-build evidence path required.' }
Reject-ReparseAncestors $evidence
$gitRoot=(& git -C $repo rev-parse --show-toplevel).Trim().Replace('/','\')
if ($LASTEXITCODE -ne 0 -or -not $gitRoot.Equals($repo,[StringComparison]::OrdinalIgnoreCase)) { throw 'RepositoryRoot must be the actual Git root.' }
$head=(& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') { throw 'Cannot read exact source HEAD.' }
$sourcePaths=@(& git -C $repo ls-files --cached --others --exclude-standard -- src tests tools build Config .editorconfig .gitattributes Directory.Build.props Directory.Packages.props Version.props global.json nuget.config appsettings.json PrintFlowStudio.sln)
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate scoped source.' }
$sourcePaths=@($sourcePaths | Sort-Object -Unique)
$sourceSet=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$files=[Collections.Generic.List[object]]::new()
$sources=[Collections.Generic.List[object]]::new()
foreach ($relative in $sourcePaths) {
    $path=Join-Path $repo $relative
    if (-not [IO.File]::Exists($path)) { throw "Tracked source missing: $relative" }
    Reject-ReparseAncestors $path
    if (-not $sourceSet.Add($path)) { throw "Duplicate source path: $relative" }
    $hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    $files.Add([ordered]@{Path=$path;Sha256=$hash;Mvid=$null})
    $sources.Add([ordered]@{Path=$relative.Replace('\','/');Sha256=$hash})
}
$artifacts=[Collections.Generic.List[object]]::new()
foreach ($item in Get-ChildItem -LiteralPath $build -Recurse -File | Sort-Object FullName) {
    Reject-ReparseAncestors $item.FullName
    if (-not $sourceSet.Add($item.FullName)) { throw "Duplicate artifact path: $($item.FullName)" }
    $record=[ordered]@{Path=$item.FullName;Sha256=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash;Mvid=(Get-ModuleId $item.FullName)}
    $files.Add($record); $artifacts.Add($record)
}
foreach ($file in $files) {
    if ((Get-FileHash -LiteralPath $file.Path -Algorithm SHA256).Hash -ne $file.Sha256) { throw "Candidate mutated during hashing: $($file.Path)" }
}
$dirty=@(& git -C $repo status --porcelain=v1 --untracked-files=all -- src tests tools build Config .editorconfig .gitattributes Directory.Build.props Directory.Packages.props Version.props global.json nuget.config appsettings.json PrintFlowStudio.sln)
if ($LASTEXITCODE -ne 0) { throw 'Cannot read scoped dirty state.' }
$exclusions=@(@('D:\PrintFlowStudio',(Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PrintFlow Studio'),(Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'PrintFlow Studio'))+$ExcludedRoot | Sort-Object -Unique)
foreach ($excluded in $exclusions) { if (-not [IO.Path]::IsPathFullyQualified($excluded)) { throw 'Excluded roots must be explicit absolute paths.' } }
$candidate=[ordered]@{RepositoryRoot=$repo;SourceHead=$head;ExcludedRoots=$exclusions;Files=$files.ToArray()}
$scenario=[ordered]@{RunId=$RunId;AdapterMode='Fake';Families=@('F1','F2','F3','F4','F5','F6')}
$provenance=[ordered]@{
    SourceHead=$head;CreatedUtc=[DateTime]::UtcNow.ToString('o');Configuration=$Configuration
    BuildDirectory=$build;BuildEvidence=$evidence;BuildEvidenceSha256=(Get-FileHash -LiteralPath $evidence -Algorithm SHA256).Hash
    BuildClaim='Metadata only; caller must establish successful fresh build and tests. No runtime graph or desktop authorization.'
    DotnetSdk=(& dotnet --version | Out-String).Trim();PowerShell=$PSVersionTable.PSVersion.ToString()
    ScopedDirtyState=$dirty;SourceFiles=$sources.ToArray();Artifacts=$artifacts.ToArray()
    DocumentationIdentity='Tracked separately from code/runtime candidate; historical audit residues excluded from source scope.'
}
if ($LASTEXITCODE -ne 0) { throw 'Cannot record toolchain.' }
New-Item -ItemType Directory -Path $manifest -ErrorAction Stop | Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
foreach ($entry in @(@('candidate-manifest.json',$candidate),@('scenario-manifest.json',$scenario),@('candidate-provenance.json',$provenance))) {
    $json=$entry[1] | ConvertTo-Json -Depth 12
    $destination=Join-Path $manifest $entry[0]
    $stream=[IO.File]::Open($destination,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try { $bytes=$utf8.GetBytes($json+"`n"); $stream.Write($bytes,0,$bytes.Length) } finally { $stream.Dispose() }
}
Write-Output "Candidate metadata prepared: $manifest"
Write-Output "Source HEAD: $head; files: $($files.Count). Runtime run root was not created."
