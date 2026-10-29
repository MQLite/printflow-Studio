$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$baseline = '7fd87f6ef7ac349633af19aa04355def00c79b41'
$paths = @(git diff --name-only $baseline HEAD -- src tests)
$rows = @(foreach ($path in $paths) {
    [ordered]@{ Path = $path; Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash; GitBlobId = (git rev-parse ('HEAD:' + $path)).Trim() }
})
$rows | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $taskRoot 'source-hashes.json') -Encoding utf8
$diffPath = Join-Path $taskRoot 'source.diff'
git diff --binary ("--output=" + $diffPath) $baseline HEAD -- src tests
if ($LASTEXITCODE -ne 0) { throw 'Unable to refresh source diff' }

# Re-evaluate the original action contracts against the current XAML, not a prior report.
[xml]$view = Get-Content 'src/PrintFlow.App/Views/SessionScreenView.xaml' -Raw
$actions = Get-Content (Join-Path $taskRoot 'action-identity-comparison.json') -Raw | ConvertFrom-Json
foreach ($action in $actions) {
    $matches = @($view.SelectNodes('//*') | Where-Object { $_.GetAttribute('AutomationProperties.AutomationId') -eq $action.Id })
    $action.AfterCount = $matches.Count
    foreach ($attribute in $action.Attributes) {
        $attribute.After = if ($matches.Count -eq 1) { $matches[0].GetAttribute($attribute.Name) } else { '' }
        $attribute.Equal = $attribute.Before -ceq $attribute.After
    }
}
$actions | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $taskRoot 'action-identity-comparison.json') -Encoding utf8
[ordered]@{ Baseline = $baseline; SourceHead = (git rev-parse HEAD).Trim(); SourceAndTestFiles = $rows.Count; OriginalActions = @($actions).Count } |
    ConvertTo-Json | Set-Content (Join-Path $PSScriptRoot 'source-evidence-revision.json') -Encoding utf8
