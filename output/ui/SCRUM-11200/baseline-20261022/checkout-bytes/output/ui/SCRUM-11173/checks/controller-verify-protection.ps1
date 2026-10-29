param()
$ErrorActionPreference = 'Stop'
$taskStart = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'controller-startup.json') -Raw | ConvertFrom-Json
$taskProtected = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'controller-protected-worktrees.json') -Raw | ConvertFrom-Json
$taskMain = 'D:\Repositories\printflow-Studio'
$taskTracked = @($taskStart.trackedHashes | ForEach-Object {
    $taskFile = Join-Path $taskMain $_.path
    [pscustomobject]@{ path=$_.path; unchanged=((Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash -ceq $_.sha256) }
})
$taskUntracked = @($taskStart.untrackedDocs | ForEach-Object {
    $taskFile = Join-Path $taskMain $_.path
    [pscustomobject]@{ path=$_.path; unchanged=((Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash -ceq $_.sha256) }
})
$taskTrees = @($taskProtected | ForEach-Object {
    $taskStatus = @(git -C $_.path status --short)
    [pscustomobject]@{
        path=$_.path
        headUnchanged=((git -C $_.path rev-parse HEAD) -ceq $_.head)
        statusUnchanged=(($taskStatus -join "`n") -ceq ($_.status -join "`n"))
    }
})
$taskReport = [pscustomobject]@{
    trackedFiles=$taskTracked
    untrackedDocs=$taskUntracked
    protectedWorktrees=$taskTrees
    allUnchanged=(@($taskTracked | Where-Object { -not $_.unchanged }).Count -eq 0 -and
        @($taskUntracked | Where-Object { -not $_.unchanged }).Count -eq 0 -and
        @($taskTrees | Where-Object { -not $_.headUnchanged -or -not $_.statusUnchanged }).Count -eq 0)
}
$taskReport | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'controller-protection-final.json') -Encoding utf8
$taskReport | Select-Object allUnchanged,@{n='trackedFiles';e={$_.trackedFiles.Count}},@{n='untrackedDocs';e={$_.untrackedDocs.Count}},@{n='protectedWorktrees';e={$_.protectedWorktrees.Count}} | ConvertTo-Json -Compress
if (-not $taskReport.allUnchanged) { exit 1 }
