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
$reviewImages = @()
if (Test-Path (Join-Path $taskRoot 'review-evidence-index.json')) {
    $reviewImages = @(Get-Content (Join-Path $taskRoot 'review-evidence-index.json') -Raw | ConvertFrom-Json)
    foreach ($row in $reviewImages) {
        $path = Join-Path $taskRoot $row.Path
        $snapshot = Get-Content ([IO.Path]::ChangeExtension($path, '.json')) -Raw | ConvertFrom-Json
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $row.Sha256 -or
            $snapshot.ScreenshotSha256 -ne $row.Sha256 -or $snapshot.SourceSha256 -ne $sourceHash) {
            throw ('Post-review image/snapshot mismatch: ' + $row.Path)
        }
    }
    if ((Get-FileHash -LiteralPath (Join-Path $taskRoot 'review-renders/synthetic-source.png')).Hash -ne $sourceHash) { throw 'Post-review synthetic source changed' }
    $comparisons = Get-Content (Join-Path $PSScriptRoot 'review-render-comparison.json') -Raw | ConvertFrom-Json
    foreach ($row in $comparisons) {
        if ((Get-FileHash (Join-Path $taskRoot ('renders/' + $row.File))).Hash -ne $row.BeforeSha256 -or
            (Get-FileHash (Join-Path $taskRoot ('review-renders/' + $row.File))).Hash -ne $row.AfterSha256 -or
            $row.Identical -ne ($row.BeforeSha256 -eq $row.AfterSha256)) { throw ('Render comparison mismatch: ' + $row.File) }
    }
}
[ordered]@{
    SourceAndTestFiles = @($sourceRows).Count
    PageImages = @($images).Count
    ImagesInspectedByExecutor = @($images | Where-Object InspectedByExecutor).Count
    MainSnapshots = $snapshots.Count
    OriginalActionContracts = @($actions).Count
    PostReviewPageImages = $reviewImages.Count
    PostReviewImagesInspected = @($reviewImages | Where-Object InspectedByExecutor).Count
    SourceSha256 = $sourceHash
    HashesAndRecordedActionComparison = 'PASS'
    Note = 'SHA256 hashes describe current worktree bytes (line-ending sensitive); GitBlobId pins committed text. Not human, native keyboard or production validation.'
} | ConvertTo-Json | Set-Content (Join-Path $PSScriptRoot 'evidence-integrity.json') -Encoding utf8
Get-Content (Join-Path $PSScriptRoot 'evidence-integrity.json')
