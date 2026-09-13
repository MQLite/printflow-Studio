# PF-AUDIT-A0 — Build-origin association closure

**PASS WITH NOTES — PF-AUDIT-A0 BUILD-ORIGIN ASSOCIATION VERIFIED WITH EXPLICIT LOCAL-TRUST LIMITS**

A1 build-origin prerequisite satisfied for the preserved pair below. Release acceptance, a final
candidate freeze and A1/A2/A3 are not completed or authorized by this result.

Date: 14 September 2026. Execution host: DESKTOP-0BG8884. Canonical checkout D:\Repositories\printflow-Studio, master.
Initial HEAD: 1c591e986472644446d1112462b688e9c11e022b. Installed routing policy v2.3;
explicit route_offset 0. Per-user SDK reports 10.0.400. Operator prompt bundle preserved.

## Exact remaining gap and chosen scope

R1 checked candidate bytes across capture/publication/runtime, but harness-to-candidate association
was only AssemblyInformationalVersion. A clean candidate at H and a changed-source harness built
without moving H can have the same label. Present-time git cleanliness cannot identify old binaries.
R1's run claim, invocation/host verdict, case completeness, file integrity, synthetic review refusal,
explicit readiness attestation and refusal-versus-revocation behavior remain required.

A0 uses a narrow command that actually builds both outputs. It does not certify arbitrary existing
folders. Release test-host output and win-x64 self-contained candidate output each match their own
receipt inventory; they are not required to be byte-equal across profiles. Running published Product
DLLs under a different test dependency graph was rejected because it would introduce loader changes.

## Contract and consumers

- Local receipt version 1 identifies source/build inputs, resolved SDK, exact commands/profiles,
  concrete output folders, four R1 Product identities on each side and the full test-host output set.
- Regression evidence binding version 2 records BuildOrigin {ReceiptPath, ReceiptSha256, PairId}.
  Historical evidence remains readable and cannot be enriched into current publishability.
- New execution: wrapper selects the receipt's DLL through dotnet vstest. The C# run claims its
  identity first, then validates the receipt against its loaded Product and runtime files and the
  chosen candidate before configuration or composition. Explicit diagnostic unbound execution
  remains ineligible for publication.
- Visual review: wrapper selects the original run's checked paired reviewer for bound evidence;
  current review preserves the complete original binding, timestamps, artifact hashes and history.
- Publication: shared PowerShell contract checks original receipt hash/id, retained harness and
  candidate output sets, and run-recorded identities; existing writer checks target candidate bytes
  and all R1 requirements before replacing any active record. No receipt parameter can attach a
  fresh pair to an old result.
- Runtime Product evaluator/schema are unchanged. ProductBuildIdentity changes are comments only;
  the runtime still compares actual candidate bytes. Receipt validation grants no App bypass.

## Verification scope

Focused consumers and producer failure paths first; then the affected architecture, regression,
verification, diagnostics and smoke union with inherited PRINTFLOW_* opt-ins cleared process-locally.
A full Product suite is not warranted by tooling/test-contract changes and comment-only Product
edits. The controlled pair is verified both from PowerShell and by a read-only test in its actual
loaded harness. No readiness/standard-set bodies or real workstation stores are used.

| Check | Actual result | Evidence under `artifacts/pf-audit-a0/` |
|---|---|---|
| Focused consumer class, final C# source | 29 passed / 0 failed / 0 skipped | `origin-consumer-tests.log` |
| Affected architecture/regression/verification/diagnostics/smoke/gate union | 777 passed / 0 failed / 0 skipped | `affected-tests.log`, `affected-results/affected.trx` |
| Producer behavioral Pester, final producer source | 4 passed / 0 failed | `producer-tests.log` |
| Controlled Release harness build | exit 0, 0 warnings / 0 errors | `controlled-build.log`, pair `harness-build.log` |
| Controlled Release win-x64 self-contained publish | exit 0; no warnings/errors reported | pair `candidate-build.log` |
| Read-only receipt verification | exit 0 | `New-PrintFlowBuildPair.ps1 -VerifyOnly` tool output |
| Read-only check inside the actual paired test DLL | 1 passed / 0 failed | `loaded-pair-proof.log`, pair `verification-results/loaded-pair.trx` |

These counts are separate, not additive. Opt-in live smoke bodies in the union did not execute;
the existing no-op discovery convention reports them as passed, not skipped. R3's 11,836/0/0 and
R4's 18/18 and 758/758 remain inherited historical evidence, not new A0 suite runs.

Initial compile failed on missing System.IO and an AddRange overload ambiguity (`focused-build.log`).
The first focused run was 9 passed / 19 failed from Windows PowerShell child module lookup; the
next was 25 passed / 3 failed before obsolete version/message assertions were corrected. Logs are
`origin-consumer-tests-initial-failure.log` and `origin-consumer-tests-three-failures.log`.
`origin-consumer-tests-28-pass.log` predates the final added consumer proof. None is the final result.
No failing assertion was removed to manufacture a pass. A redundant passing source-string Pester
test was removed; actual builds establish profiles, leaving four behavioral tests.

The first real preparation attempt, at `8f94f98`, refused before either build because it classified
old `.trx` test logs as source inputs. Failed pair `943fd426-3203-47c3-b4c4-7e400447620d` and
`controlled-build-preparation-refusal.log` remain local; there is no completed receipt for it.
Correction `ce29954` excludes only that known noncompiled/noncopied log pattern, keeps source files
inside TestResults checked, and handles Update-only XML items with `GetAttribute('Include')`.
The corrected fixture exercises both cases, producer tests passed again, and the real proof used
fresh outputs. No old output or historical evidence was deleted or reused.

The same-label counterexample is executable evidence: two tiny isolated source builds carry the
loaded Product's informational label. The actual pre-A0 writer at `1c591e9` accepts a run naming
different harness-source bytes while the candidate stays fixed; the current writer refuses the
recorded-harness mismatch and preserves the existing synthetic record. The actual runner also
refuses different loaded harness bytes after staking its permanent claim and before configuration.
Separate tests cover replaced harness/candidate/receipt, legacy non-enrichment, and actual review
preservation plus selection of the original paired reviewer.

## Independent review

A separate native reviewer received fresh context containing the original A0 prompt, current source
and diffs. It found an ignored-compilable-input hole and a stale conventional reviewer that could
drop origin. Both were corrected; narrowed re-review, including the preparation correction, found
no further actionable defect. No signing, certificates, PKI or generic provenance platform was added.

Routing record: policy v2.3, offset 0, UNCHANGED. Analysis/review requested Astra High; independent
producer and test subtasks requested Sol High via native fresh-context agents. Actual model/effort
metadata is unavailable, recorded UNVERIFIED. Parent live switching is unavailable
(`MODEL_SWITCH_UNAVAILABLE`); no switch or downgrade is claimed. Implementation context continued;
review had a genuinely separate context with original AC and source, not full implementer history.

## Trust and stop boundary

This is trusted-local-tooling evidence against accidental dirty, stale and mixed builds. An
administrator able to rewrite tools, receipts and run evidence remains outside this unsigned local
threat model. Sampling stable inputs at build boundaries is not filesystem isolation against an
adversary who changes and restores inputs between samples. No claim of cross-machine reproducibility
or final release freeze is made.

R4 remains unchanged: one fresh-launch unwaited COM runtime-fact read returned MK_E_UNAVAILABLE;
the same Photoshop process later passed attach checks twice. This refutes only persistently broken
registration. Timing, Generator startup, desktop interaction and window-state change were uncontrolled.
The fresh-launch finding remains supported, unreproduced and unrepaired, to resolve or explicitly
accept before A1 relies on fresh launches. Meitu window loss remains unexplained. R4's run-1 wrapper
may still wait on inherited application handles; it was not stopped or inspected by A0.

Live operations NOT EXECUTED. Real production revalidation NOT WRITTEN. Jira statuses unchanged.
No Operator decision, A1/A2/A3, install, Product launch, push or deploy.

## Controlled artifacts and commits

- `8f94f986113acbc60f13d12fcc58bf1371db8338`: implementation, consumer tests and runbook.
- `ce299546e593cbc4c9b8dc59bd6aff9a9fc41cb8`: source-input preparation correction; actual paired source.
- Subsequent closure commit changes reports/HANDOFF/PLAN only. Its HEAD is obtained with `git log -1`;
  no self-referential commit hash is embedded and no documentation-only rebuild is required.

Pair root: `artifacts/pf-audit-a0/build-pairs/ef6182db-6f48-4ed2-ae8f-c4e52307ca9e/`.
The receipt pins `harness/` and `candidate/`; they remain staged, not installed or launched.
Completed at `2026-09-13T23:51:39.2653825Z` (14 September locally).

| Identity | Value |
|---|---|
| Pair id | `ef6182db-6f48-4ed2-ae8f-c4e52307ca9e` |
| Receipt SHA-256 | `E0373C56D015FED9E09D8B0F77C55B026CAE40ED3B685ABFB899709944077FBF` |
| Input digest (601 committed relevant files) | `32E37750703A447F6D341D400497D05D6AE4F25EA5FAB8FA67463854E74A359C` |
| SDK | `10.0.400` |
| dotnet | `C:\Users\admin\AppData\Local\Microsoft\dotnet\dotnet.exe` |
| Harness inventory | 182 files, retained recursively in the receipt |
| PrintFlow.Tests.dll SHA-256 | `777279AB5451E0B22F4F278754E3AE448ED2F5E5FCC50C39AA5F2DDD202CE400` |

All four Product labels are `0.1.0+ce299546e593cbc4c9b8dc59bd6aff9a9fc41cb8`.
All four pairs below differ legitimately; neither side was patched or normalized.

| Product DLL | Harness SHA-256 | Candidate SHA-256 |
|---|---|---|
| App | `71CA5F7E55881BF40588CE49AA40EEE42B62CB2E0631ADD22DCADD2631A47CBD` | `7BEBDFBAAB0C126B9912545B8FCC0299B46924F09C7DE0BE390A3BD77E41A380` |
| Domain | `9B20FB60907C2A9E2396499FCBA1A65FD62DE48361366B305EF2C2EABF75F1E5` | `01308934CB2C9821DF01F201248A4BF516780DA19B5553FD1E3FC5F293B3E908` |
| Infrastructure | `72B933627C70DBD35BD67503CBA8D65D0EF013459F514831179CF75F81FEA756` | `DBA168795B3449DBCAB059807C8014FD3AA432B07318A9210DE8F8F526E77622` |
| Workflow | `5306B43DD734FBEAF0D09F1114F6A9DA90715C5BEDD8F1CBAF6A4B0B225F9FFE` | `BD2B7562693A36229CEEDFBA8EAE6C1D23DEFF9CA983DDF2B98902D072D9C873` |

`build-pair-summary.json` records the same measured values. The receipt contains the exact argument
arrays for both commands. Profiles are ordinary Release test build and Release win-x64 self-contained
publish, each with fresh `--artifacts-path` and `-o`, pinned repo NuGet/MSBuild inputs and locked
restore. No MSI was generated. SDK/candidate runtime installation or machine-wide state was not changed.

## Supported commands

Run from the canonical checkout. For isolated verification clear inherited opt-ins only in this
process. This includes R4 diagnostic opt-in; it does not touch the R4 wrapper's process environment.

```powershell
Set-Location D:\Repositories\printflow-Studio
git branch --show-current
git rev-parse HEAD
git status --short
Get-ChildItem Env: | Where-Object Name -Like 'PRINTFLOW_*' |
    ForEach-Object { Remove-Item -LiteralPath ('Env:' + $_.Name) }
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"

# Preparation: creates a NEW pair; never retroactively approves supplied folders.
$pair = tools\regression\New-PrintFlowBuildPair.ps1 -OutputRoot artifacts\build-pairs
$receipt = Join-Path (Split-Path $pair.HarnessFolder) 'build-pair.json'

# Or select the preserved A0 pair without building:
$receipt = 'D:\Repositories\printflow-Studio\artifacts\pf-audit-a0\build-pairs\ef6182db-6f48-4ed2-ae8f-c4e52307ca9e\build-pair.json'
$pair = tools\regression\New-PrintFlowBuildPair.ps1 -VerifyOnly -ReceiptPath $receipt

# Actual loaded-harness verification: read-only, no operational setup or external application.
$env:PRINTFLOW_BUILD_PAIR_PROOF_RECEIPT = $receipt
$env:PRINTFLOW_BUILD_PAIR_PROOF_CANDIDATE = $pair.CandidateFolder
try {
    & $pair.DotnetPath vstest (Join-Path $pair.HarnessFolder 'PrintFlow.Tests.dll') `
        '/TestCaseFilter:FullyQualifiedName~BuildPairVerificationSmoke' `
        '/Logger:console;verbosity=detailed' `
        "/ResultsDirectory:$(Join-Path (Split-Path $pair.HarnessFolder) 'verification-results')"
    if ($LASTEXITCODE -ne 0) { throw 'Loaded build-pair verification failed.' }
} finally {
    Remove-Item Env:PRINTFLOW_BUILD_PAIR_PROOF_RECEIPT, Env:PRINTFLOW_BUILD_PAIR_PROOF_CANDIDATE -ErrorAction SilentlyContinue
}
```

The following are source-verified future commands, **NOT EXECUTED or authorized by A0**:

```powershell
# After separate live authorization and preparation; wrapper uses paired DLL via vstest, no build.
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1 `
    -BuildPairReceipt $receipt -CandidateInstallFolder $pair.CandidateFolder -RunId '<new-run-id>'
# Real review uses the original run's checked harness; do not supply a replacement receipt.
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1 `
    -RunId '<existing-run-id>' -RecordVisualReview '<actual-decisions.json>'
# Later, separately authorized publication: target can be staged or installed matching candidate bytes.
# EnvironmentReadinessPassed remains the explicit operator attestation, not an origin inference.
tools\installer\Set-PrintFlowProductionRevalidation.ps1 `
    -InstallFolder '<actual-target-folder>' -EnvironmentReadinessPassed `
    -StandardRegressionSetPath '<actual-manifests-folder>' `
    -StandardRegressionSetResult '<actual-run-result.json>'
```

No latest-HEAD check is made at review/publication. Retained original artifacts remain associated
across documentation-only changes. Any changed/rebuilt Product or harness needs a new controlled
pair and a new run before it can be evidence for that candidate. Losing or altering the retained
receipt/harness closes publication; a new receipt cannot rescue an old run. This proof is not an A1
candidate freeze and does not pre-accept a later separately approved R4 Product correction.

After the report-only closure commit, `artifacts/pf-audit-a0/documentation-only-verification.json`
records the actual new HEAD and the unchanged receipt hash after read-only verification. No paired
artifact is rebuilt for that check; it demonstrates that documentation history is not origin authority.
