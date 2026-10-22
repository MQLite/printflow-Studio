$ErrorActionPreference='Stop'
$repo=(& git rev-parse --show-toplevel).Trim()
$documents=@(
    'docs/codex/printflow-operator-ux-backlog/SCRUM-11173_RESULTS.md',
    'docs/codex/printflow-operator-ux-backlog/SCRUM-11173_HANDOFF.md',
    'docs/codex/printflow-operator-ux-backlog/SCRUM-11173_REVIEW.md',
    'docs/printflow/guided-acceptance-maintenance.md',
    'docs/printflow/guided-acceptance-walkthrough.md',
    'output/ui/SCRUM-11173/COVERAGE.md',
    'output/ui/SCRUM-11173/RULINGS.md',
    'output/ui/SCRUM-11173/REVIEW_PACKAGE.md',
    'output/ui/SCRUM-11173/visual-inspection.md',
    'output/ui/SCRUM-11173/inherited-failures.md',
    'output/ui/SCRUM-11173/candidate/README.md'
)
$links=@()
foreach($relative in $documents) {
    $path=Join-Path $repo $relative
    $content=Get-Content -LiteralPath $path -Raw
    if ($content -cmatch '\bPENDING\b') { throw "Unresolved placeholder: $relative" }
    foreach($match in [regex]::Matches($content,'\[[^\]]+\]\(([^\)]+)\)')) {
        $target=$match.Groups[1].Value.Trim('<','>')
        if ($target -match '^(https?:|#)') { continue }
        $withoutFragment=$target.Split('#')[0]
        $resolved=[IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($path)) $withoutFragment))
        if (-not (Test-Path -LiteralPath $resolved)) { throw "Broken link in ${relative}: $target" }
        $links += [ordered]@{Source=$relative;Target=$target;Exists=$true}
    }
}
& git diff --check 65c5853e0e3f4f828b7851479bb418d043b0e44e -- src tests docs/printflow/guided-acceptance-maintenance.md docs/printflow/guided-acceptance-walkthrough.md docs/superpowers/plans/2026-10-22-scrum-11173-guided-acceptance.md docs/codex/printflow-operator-ux-backlog/SCRUM-11173_RESULTS.md docs/codex/printflow-operator-ux-backlog/SCRUM-11173_HANDOFF.md
if ($LASTEXITCODE -ne 0) { throw 'Source/document diff whitespace check failed.' }
[ordered]@{
    Documents=$documents;LocalLinkCount=$links.Count;Links=$links
    DiffCheck='PASS for product/tests/authored docs; raw logs/TRX deliberately preserved verbatim'
    HumanStatus='NOT RUN';ParticipantAvailability='NOT_CONFIRMED';HumanScheduling='DEFERRED_BY_USER';ActualWorkstation='NOT RUN'
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'handoff-verification.json') -Encoding utf8
Write-Output "Verified $($documents.Count) authored documents and $($links.Count) local links; source/document diff check passed."
