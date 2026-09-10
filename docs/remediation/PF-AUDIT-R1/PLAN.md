# PF-AUDIT-R1 — Plan: regression evidence integrity and revalidation binding

**Slice:** PF-AUDIT-R1 (audit findings F3, F4)
**Canonical checkout:** `D:\Repositories\printflow-Studio`, branch `master`
**HEAD at start:** `3f83863521c9b682f02b19ede1bcdb3a86dc60aa` (the audit commit itself)
**Working tree at start:** clean, except the untracked `printflow-remediation-prompts\` bundle
supplied beside this task. That folder is operator-owned input, not repository content: it is left
untracked and is not part of any commit from this slice.
**Baseline build:** `dotnet build PrintFlowStudio.sln -c Release` — 0 warnings, 0 errors, confirmed
before any edit.
**Historical suite baseline (not re-run by this slice):** 11,779 passed / 0 failed / 0 skipped.

---

## 1. The requirement this slice restores

Re-read from `C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv` under the
repository's established ordinal mapping (SCRUM key = 11060 + zero-based row position):

- **Row 6, CSV work item 11005 → SCRUM-11065** — *Build the Standard Local Regression Image Set.*
  "Create the fixed local regression set … including at least a normal JPG portrait, complex
  background with fine hair, transparent PNG, complete customer design, PSD with a compatible
  composite preview, single-page PDF and a reference production TIFF. Record the expected
  processing path and relevant expected properties for every file. Keep all customer-like test
  data local and suitable for repeatable automated, workstation and upgrade regression testing."

- **Row 64, CSV work item 11608 → SCRUM-11123** — *Build a Versioned Offline Installer and Upgrade
  Procedure.* "… **Any PrintFlow, Windows, Meitu or Photoshop upgrade must require rerunning the
  standard test set before production use rather than silently changing the supported
  environment.**"

The emphasised clause is the requirement F3 and F4 break, and it is the whole of this slice's
scope. Both findings let an environment change be absorbed silently: F3 by copying an old run's
`Passed` onto current environment facts, F4 by letting an old result speak for a new invocation
that failed.

**What is in scope beyond the two defects, and what is not.** Correcting the *documented ordering*
in the live runbook is in scope and required: the slice brief asks for the legitimate sequence
(controlled bootstrap → one complete run → actual operator reviews → evidence-bound revalidation →
normal App live checks → normal App E2E) to be written down, and for the runbook to say that a
synthetic test of this protocol is not a standard-set acceptance. What is **not** in scope is
*performing* any of it: no live acceptance, no operator review of a real run, no production
approval, and no change to the bootstrap mechanism itself. Historical reports are not rewritten.

79-row rescans and unrelated functionality are explicitly out of scope.

---

## 2. Confirmation of the two findings against current source

Both confirmed as still present at HEAD.

### F4 — an old `result.json` can override a failed new invocation

`tools/regression/Invoke-PrintFlowStandardRegressionSet.ps1`

```powershell
& $dotnet test $testProject -c $Configuration --nologo --filter $filter ...
$testExit = $LASTEXITCODE
...
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
...
if ($result.Status -eq 'Passed') { ... exit 0 }
```

`$testExit` is captured and then consulted only in the final `exit` of the *failure* path. A
`Passed` result.json on disk produces `exit 0` whatever the host did. Nothing ties the file to this
invocation: no invocation identity, no freshness proof, and the run folder is reused because the
C# runner calls `Directory.CreateDirectory(runFolder)`
(`tests/PrintFlow.Tests/Smoke/StandardRegressionSetWorkstationSmoke.cs`), which succeeds on an
existing directory.

A second, related defect found while reading the same path: the `-RecordVisualReview` re-derivation
overwrites `completedAtLocal` with `DateTimeOffset.Now`, so a review of an old run makes that run
look as though it completed at review time. That is inside F4's boundary (freshness of evidence)
and is fixed here.

### F3 — the writer copies a `Passed` label onto current environment facts

`tools/installer/Set-PrintFlowProductionRevalidation.ps1` reads the current installation
(product version from the installed `PrintFlow.App.exe`, preset id/version/digest, Windows build,
accepted Meitu and Photoshop digests) and then reads exactly four fields from the supplied run
result:

```powershell
$regressionSetId = $result.setId
$regressionCompleted = $result.completedAtLocal
$regressionEvidence = $result.evidencePath
if ($result.status -eq 'Passed') { $regressionStatus = 'Passed' } else { $regressionStatus = 'Failed' }
```

The run result already carries `Workstation`, `ProductVersion`, `PresetId`, `PresetVersion`,
`AdapterMode`, `RequiredCategories`, `MissingCategories` and `Cases` — none of them are read. So a
genuine `Passed` from environment A, supplied while the installation is B, is recorded as
"B + Passed" with no forgery. `ProductionRevalidationEvaluator.Evaluate` compares the record's
environment fields against the current machine and never reads the run back, so it cannot recover
the missing check. It also compares the Product only by a three-part version string, so different
Product bytes under the same version inherit the approval.

---

## 3. Current call chain

```
operator
  |
  v
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1        (entry point)
  |  Layer 1 preflight (static, in PowerShell)
  |  env: PRINTFLOW_STANDARD_REGRESSION_SET / _SET_ROOT / _RUN_ID / _CATEGORIES
  |       PRINTFLOW_REGRESSION_REAGGREGATE / _VISUAL_DECISIONS
  v
dotnet test --filter StandardRegressionSetWorkstationSmoke.*
  |
  +-- Run_the_standard_local_regression_set_on_the_fixed_workstation   (new execution)
  |      StandardRegressionSet.Load + Validate      (Layer 1 again, in-process)
  |      ServiceRegistration.BuildServiceProvider   (real graph)
  |        + RegressionBootstrapWorkstationVerifier (suppresses only ProductionRevalidation)
  |      IEnvironmentDiagnostics.RunLiveChecksAsync -> readiness.json
  |      IEnvironmentGate.Verify(Production)
  |      per-case runs -> StandardRegressionSetRunResult.From -> result.json
  |
  +-- Re_derive_a_completed_run_from_recorded_visual_decisions        (review)
         reads result.json + decisions.json -> From(...) -> result.json (overwritten)
  |
  v
tools\installer\Set-PrintFlowProductionRevalidation.ps1           (publication)
  |   reads installed exe/preset/OS/binaries + 4 fields of result.json
  v
<workspace>\Revalidation\production-revalidation.json
  |
  v
FileProductionRevalidationReader -> ProductionRevalidationRecord.TryParse
  -> ProductionRevalidationEvaluator.Evaluate  (called from ProductionWorkstationVerifier)
  -> WorkstationCheckResult.ProductionRevalidation -> IEnvironmentGate
```

## 4. Affected files

| File | Change |
|---|---|
| `src/PrintFlow.Infrastructure/Verification/ProductBuildIdentity.cs` | **new** — the Product-assembly identity contract: which assemblies count, how one is digested, how two sets are compared. Observation (file I/O) is separate from comparison (pure). |
| `src/PrintFlow.Infrastructure/Verification/ProductionRevalidation.cs` | record schema 1 → 2; `ProductAssemblies` and the regression run's bound identity added; evaluator compares candidate bytes and refuses an unbound `Passed`; schema 1 recognised as historical with its own diagnostic. |
| `src/PrintFlow.Infrastructure/Verification/ProductionWorkstationVerifier.cs` | observes the running Product assemblies at the one existing evaluator call site and passes them in (evaluator stays pure). |
| `tests/PrintFlow.Tests/Regression/StandardRegressionSetRun.cs` | run result carries the evidence binding, the invocation identity, the set content identity and a review history; `From` rejects duplicate categories and contradictions. |
| `tests/PrintFlow.Tests/Regression/RegressionExecutionClaim.cs` | **new** — claims a run identity with `FileMode.CreateNew`, so a reused or concurrently claimed identity is refused instead of overwriting evidence. |
| `tests/PrintFlow.Tests/Smoke/StandardRegressionSetWorkstationSmoke.cs` | claims the execution before anything else; stamps the invocation and the bound facts into the result; review path preserves execution identity/times and binds each decision to an artefact digest. |
| `tools/regression/Invoke-PrintFlowStandardRegressionSet.ps1` | mints an invocation id, refuses an existing execution destination, requires host exit 0 **and** this invocation's own complete result before reporting Passed; adds the host-command seam the protocol tests drive. |
| `tools/installer/Set-PrintFlowProductionRevalidation.ps1` | validates the whole proposed attestation before replacing an active record; refuses unbindable input without touching the active record; temp-write + replace. |
| `docs/printflow/installer-upgrade-rollback-runbook.md` | §2.4, §3.6, §7.3–7.5 corrected to the legitimate ordering. |

## 5. Chosen identity contract

One versioned contract, `evidenceBindingVersion = 1`, written by the run and read by the review
path, the writer and (for its own half) the Product evaluator.

**Bound facts, all captured by the run that tested them:**

| Fact | Source at run time | Why it is not the existing field |
|---|---|---|
| `runId` + `invocationId` | wrapper mints a GUID per invocation; runner claims it | a run folder and a timestamp are not proof that *this* invocation produced the file |
| `candidate.assemblies[]` — `PrintFlow.App.dll`, `PrintFlow.Domain.dll`, `PrintFlow.Infrastructure.dll`, `PrintFlow.Workflow.dll`, each with SHA-256 **and** build identity | read by the run from the candidate install folder it is attesting | `productVersion` is a three-part marketing string; two different builds share it |
| `harness.assemblies[]` — the same four, as the test host actually loaded them | digested from the loaded assemblies' own files | provenance of the code actually exercised; never required to be installed |
| `presetId`, `presetVersion`, `presetSha256` | configuration + the manifest's bytes | the run already knew id/version; the digest is what makes "the same preset" checkable |
| `operatingSystemBuild`, `meituSha256`, `photoshopSha256` | live readiness + accepted preset | the writer currently reads these from the *machine*, not from the run |
| `adapterMode` | configuration | already recorded, never compared |
| `setId` + `setManifests[]` (`fixtureId`, `category`, `manifestSha256`, `inputSha256`) | the loaded set | a set identity string does not pin the set's content |
| `requiredCategories`, `missingCategories`, `cases[]` | derived | the writer must re-derive the verdict, not copy a label |

**Deliberately excluded:** repository HEAD, the workspace as a whole, customer files, any signing
or PKI. HEAD is not candidate identity — a documentation-only commit changes HEAD and changes
nothing that was tested, and this contract binds *the assemblies that were exercised*, so such a
commit does not invalidate a record. What does invalidate one is a rebuild-and-reinstall, which is
a genuinely different candidate.

**One algorithm, one language.** The composite digest that would need two implementations is
avoided: PowerShell compares *per-file* SHA-256 values (`Get-FileHash`, identical to
`SHA256.HashData` by definition) and never re-implements a composite. The only composite —
`ProductBuildIdentity.Fingerprint`, for diagnostics — lives in C# alone and is re-derivable from
the record's own assembly list, so it cannot disagree with the data it summarises.

**Two identities, because bytes alone cannot span the two build modes.** Measured on this
checkout: the RID-specific self-contained publish and the ordinary Release build of the same commit
produce Product DLLs whose SHA-256 differ (and whose lengths differ by 512 bytes), while
`AssemblyInformationalVersion` — readable identically as `FileVersionInfo.ProductVersion` from both
PowerShell and C# — is `0.1.0+3f83863521c9…` in both. The regression run executes the ordinary
build inside a test host; the installation carries the publish output. A contract that compared only
bytes across those two would be unpublishable, and one that compared only the three-part version
would be the defect F3 describes. So:

- **build identity** (`0.1.0+<source revision>`) binds *the code the run exercised* to *the
  candidate the record will speak for*. The run reads both and refuses to claim a candidate whose
  build identity differs from the harness's.
- **byte identity** (SHA-256) binds *that candidate over time*: run-time reading == publish-time
  reading == the bytes the installed App later loads. This is what stops a re-installed payload
  from inheriting the approval, and what the Product evaluator checks against itself.

Paths differ freely; only digests and build identities are compared. The honest guarantee the
record ends up making is therefore: *the installed payload has not changed since the run, and it was
built from the same source revision as the code the run exercised.* Its limit is stated in the
report: a build identity is a committed revision, not a proof that the tree was clean.

## 6. Semantics being separated

**New execution** — claims `runId` before the host can operate (`FileMode.CreateNew`, so a
concurrent claim of the same identity fails); refuses an existing execution destination rather than
deleting prior evidence; stamps `invocationId` into the result; the wrapper reports Passed only when
the host exited 0 *and* this invocation's own complete, self-consistent result is present.

**Record visual review** — updates the selected existing run through the existing review path;
opens no external application; preserves `runId`, `invocationId`, `startedAtLocal`,
`completedAtLocal`, workstation, candidate, preset, adapter mode, set identity, per-case assertions
and artefact digests; records reviewer identity and review time in a separate appended `reviews[]`
history; binds each decision to the run's own artefact digest and refuses a decision whose artefact
no longer hashes to what the run recorded; can conclude only genuinely outstanding qualitative
checks, and cannot lift Blocked/Failed/unexecuted into success; invents no human reviewer, and a
synthetic decision stays labelled synthetic.

**Publication** — validates the complete proposed attestation first; on rejection writes nothing
and leaves any active record untouched; writes through a temporary file and a replace. The existing
honest-failure path is preserved and kept distinct: supplying no result (or a validly bound
non-passing result) still records `NotAvailable`/`Failed`, which is the operator's legitimate way to
close Production. Unbindable input is neither a pass nor a failure *of this installation*, so it is
refused rather than recorded as either.

## 7. Compatibility policy

- Record `schemaVersion` 1 → **2**. A schema-1 record is still parsed and readable, blocks with its
  own diagnostic naming it historical, and is never enriched into a passing schema-2 record.
- A schema-2 record whose `standardRegressionSet.status` is `Passed` but which carries no bound run
  identity is refused: a pass with nothing behind it is not a pass.
- The four fields the writer has always read from a run result keep their names and casing. Older
  run results simply lack the binding, and the writer refuses to publish a pass from them — they
  remain readable evidence of what happened.
- No force-pass or bypass switch is added anywhere. The one new seam in the wrapper replaces the
  *test-host command* for protocol tests; it cannot manufacture a pass, because the wrapper's
  verdict still requires this invocation's own bound result.

## 8. Reproductions (before the fix, at the public boundaries)

- **A** — a synthetic set with a `Passed` result.json already in the run folder, and an invocation
  whose host exits nonzero without writing a new result: the wrapper exits 0 today.
- **B** — a genuine `Passed` result bound to synthetic candidate A, supplied while the install
  folder is candidate B (same version string, one Product DLL's bytes differ): the writer records
  "B + Passed" today.

Both are driven through the actual scripts, with synthetic candidate folders built from the real
built `PrintFlow.App.exe` (so version info is real) plus the four Product DLLs. Proof that the
entry point obeys the refusal is part of each test, not a test of a helper in isolation. Nothing is
faked inside the real production workspace, and the full Product suite is not used to obtain them.

## 9. Validation matrix (small, one boundary each)

1. stale `Passed` result + nonzero host exit → wrapper fails;
2. reused execution destination → refused, prior evidence intact;
3. concurrently claimed identity → exactly one claim wins;
4. correct fresh matching execution → wrapper reports Passed;
5. candidate bytes differ under an unchanged version string → publication refused;
6. preset/environment mismatch → publication refused;
7. seven-category completeness, duplicates, and a Passed/Pending contradiction → refused;
8. review preserves execution identity, times and artefact digests, and drives no external runner;
9. legacy (unbound) evidence cannot become a current approval by enrichment;
10. invalid publication leaves the existing active record untouched;
11. an accepted synthetic publication is read independently by the Product evaluator, and the same
    record with different Product bytes is refused by it.

Reuses `WorkstationVerificationFixture` and the existing `ProductionRevalidationTests` matrix. No
test per JSON field, per workflow or per OS. Opt-in live tests stay NOT RUN and are reported
separately.

## 10. Scope decision for the final validation

Shared Product code changes (revalidation record schema, evaluator, verifier call site), so a full
Product suite run is justified once at the end, plus a clean Release build. Focused
script/contract/revalidation tests are run during implementation. Dependencies are unchanged, so no
dependency audit is repeated.

## 11. Exclusions

- No Meitu, Photoshop or Maintop is launched; no live-readiness or standard-set acceptance is run.
- The real workstation's production revalidation record and production database are not written,
  replaced, deleted or tested against.
- Preset 1.17.0, accepted assets, adapter mode and Adobe settings are untouched.
- `VerifiedEnvironmentGate`, `ProductionRevalidation` and `ForStandardRegressionRun` are preserved,
  not weakened or broadened; the App still cannot write its own approval.
- No branch, worktree, clone, amend, rebase, push, install or deploy. Local commits only.
- No new revocation subsystem, no distributed transactions, no event platform, no PKI.
- SCRUM-11065 / 11123 / 11130 are **not** marked FULL: those labels need live evidence this slice
  is forbidden from producing. R2 and R3 will change Product code afterwards, so this slice
  deliberately obtains no real production approval.
