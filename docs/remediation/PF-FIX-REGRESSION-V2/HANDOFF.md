# PF-FIX-REGRESSION-V2 — Handoff

## Outcome

**PASS — PF-FIX-REGRESSION-V2 EXPECTATION AND RUNNER VERIFIED; LIVE ACCEPTANCE NOT RUN**

The separate local set at `D:\PrintFlowStudio\TestData\v2` is materialized and passes the supported
non-live preflight as `printflow-regression-v2` with content digest
`21AF322BE9172B406DD1B12716C2CBC8977A6B057D63A39571A446ECE86232F0`. Its seven category bytes and
two required fixed reference outputs are byte-identical to current v1. It contains no runs or host
results. v1 assets, manifests and historical evidence remain untouched.

Code/tooling/tests are committed at `df162aae64dc8a18a7e671a27f11c20fea8419e2`. The generator,
PowerShell preflight, C# loader and actual portrait caller consume
`enhancedOutputIsNotSmallerThanSource`. The caller compares both decoded axes from the workflow's
managed pre-Enhancement Revision and that attempt's Enhancement Revision. Size success does not
conclude the case; the unchanged Operator question remains Pending until genuinely decided.

Focused validation is 79/0/0, Release build 0 warnings/errors, no full suite. Detailed semantic and
hash evidence is in
[`regression-v2-portrait-expectation-correction.md`](../../printflow/regression-v2-portrait-expectation-correction.md).

## Controlled pair and later command

Exactly one fresh post-commit pair was produced and verified:

- Pair: `a4237f41-17d2-47f8-affd-8d829d98fd5f`
- Source: `df162aae64dc8a18a7e671a27f11c20fea8419e2`
- Receipt: `artifacts/pf-fix-regression-v2/build-pairs/a4237f41-17d2-47f8-affd-8d829d98fd5f/build-pair.json`
- Receipt SHA-256: `BC4D3084E4CBB9440778E06940634A761B418AA9E009EFD7C5171D3682E317C4`
- Input digest: `B40780B6BEF7967F49CE03A71C2650A754D0FCD7ACF12F46B616EBDA26F60621`
- Loaded-harness proof: 1/0/0.

Safe static verification syntax:

```powershell
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1 `
    -SetRoot D:\PrintFlowStudio\TestData\v2 -PreflightOnly
```

The following is documented for a later separately authorized live window and was **not run**:

```powershell
$receipt = 'D:\Repositories\printflow-Studio\artifacts\pf-fix-regression-v2\build-pairs\a4237f41-17d2-47f8-affd-8d829d98fd5f\build-pair.json'
$pair = tools\regression\New-PrintFlowBuildPair.ps1 -VerifyOnly -ReceiptPath $receipt
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1 `
    -SetRoot D:\PrintFlowStudio\TestData\v2 `
    -BuildPairReceipt $receipt -CandidateInstallFolder $pair.CandidateFolder `
    -RunId '<fresh-run-id>'
```

Do not omit `-SetRoot` for a new run: the historical v1 default is intentionally refused for new
execution and remains available only for reading/original review semantics. Do not add a
`MeituExecutablePath` exception without separate authority; the existing 7.8.8.2 exception remains
nonpublishable and is not accepted by this correction.

## Stop and preserved state

No live RunId, case result, review decision or production revalidation was created. Both retained
historical pairs remain unchanged. No Product adapter, preset, lease, workstation, application,
Jira status, install, push, publish or deployment was touched. The operator-owned untracked
`printflow-remediation-prompts/` bundle remains preserved. Stop before live A1/A2/A3.

Policy v2.3, `EXECUTE_HANDOFF`, `CONTINUE`, route_offset -1. NormalRoute
`gpt-5.6-sol/medium`, RequestedRoute `gpt-5.6-sol/low`, ActualRoute `UNVERIFIED`,
`MODEL_SWITCH_UNAVAILABLE`. Self-review only.
