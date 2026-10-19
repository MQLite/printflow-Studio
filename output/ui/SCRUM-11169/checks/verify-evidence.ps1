$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$sourceRows = Get-Content (Join-Path $taskRoot 'source-hashes.json') -Raw | ConvertFrom-Json
foreach ($row in $sourceRows) {
    if ((Get-FileHash -LiteralPath $row.Path -Algorithm SHA256).Hash -ne $row.Sha256) {
        throw ('Source hash mismatch: ' + $row.Path)
    }
    if ($row.GitBlobId -and (git rev-parse ('HEAD:' + $row.Path)).Trim() -ne $row.GitBlobId) {
        throw ('Committed source blob mismatch: ' + $row.Path)
    }
}
$images = Get-Content (Join-Path $taskRoot 'evidence-index.json') -Raw | ConvertFrom-Json
foreach ($row in $images) {
    if ((Get-FileHash -LiteralPath (Join-Path $taskRoot $row.Path) -Algorithm SHA256).Hash -ne $row.Sha256) {
        throw ('Image hash mismatch: ' + $row.Path)
    }
}
$sourceHash = (Get-FileHash -LiteralPath (Join-Path $taskRoot 'renders/synthetic-source.png') -Algorithm SHA256).Hash
$snapshots = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'renders') -Filter '*.json')
foreach ($file in $snapshots) {
    $snapshot = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    $png = [System.IO.Path]::ChangeExtension($file.FullName, '.png')
    if ((Get-FileHash -LiteralPath $png -Algorithm SHA256).Hash -ne $snapshot.ScreenshotSha256 -or $sourceHash -ne $snapshot.SourceSha256) {
        throw ('Snapshot/source mismatch: ' + $file.Name)
    }
}
$actions = Get-Content (Join-Path $taskRoot 'action-identity-comparison.json') -Raw | ConvertFrom-Json
foreach ($action in $actions) {
    if ($action.AfterCount -ne 1 -or @($action.Attributes | Where-Object { -not $_.Equal }).Count -ne 0) {
        throw ('Action identity mismatch: ' + $action.Id)
    }
}
[ordered]@{
    SourceAndTestFiles = @($sourceRows).Count
    PageImages = @($images).Count
    ImagesInspectedByExecutor = @($images | Where-Object InspectedByExecutor).Count
    MainSnapshots = $snapshots.Count
    OriginalActionContracts = @($actions).Count
    SourceSha256 = $sourceHash
    HashesAndRecordedActionComparison = 'PASS'
    Note = 'SHA256 hashes describe current worktree bytes (line-ending sensitive); GitBlobId pins committed text. Not human, native keyboard or production validation.'
} | ConvertTo-Json | Set-Content (Join-Path $PSScriptRoot 'evidence-integrity.json') -Encoding utf8
Get-Content (Join-Path $PSScriptRoot 'evidence-integrity.json')
