# PF-AUDIT-R1 — Regression evidence integrity and revalidation binding

**Date:** 11 September 2026
**Workstation:** DESKTOP-0BG8884, Windows 10 Pro build 19045
**Branch:** `master` (local commits only; nothing pushed)
**Audit reference:** `printflow-source-audit-3f83863.md`, findings F3 and F4, at
`3f83863521c9b682f02b19ede1bcdb3a86dc60aa`
**Slice scope:** the evidence chain only. No live acceptance, no production approval.

---

## 1. What was wrong

Two defects, both of the same shape: a claim was carried from one context to another without
anything checking that it belonged there.

### F4 — a previous run's pass could be reported as this invocation's success

`Invoke-PrintFlowStandardRegressionSet.ps1` captured the test host's exit code and then ignored it:

```powershell
& $dotnet test $testProject -c $Configuration --nologo --filter $filter ...
$testExit = $LASTEXITCODE
...
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
...
if ($result.Status -eq 'Passed') { ... exit 0 }
```

`$testExit` reached only the *failure* path's final `exit`. Nothing tied `result.json` to the
invocation that had just run: not an identity, not a freshness proof, nothing but the file being
there. And the C# runner created the run folder with `Directory.CreateDirectory`, which succeeds on
a folder that already holds a completed run — so a second execution inherited the first's
destination.

**The counterexample, reproduced at the audit commit rather than described:** a run folder holding a
genuine `Passed` result, and an invocation whose host exits 7 without writing anything. The audited
wrapper exits **0**. `The_audited_wrapper_reported_a_previous_runs_pass_as_this_invocations_success`
runs the audit commit's own script, fetched with `git show`, to demonstrate exactly that;
`A_previous_runs_pass_is_not_this_invocations_success` asserts the repaired behaviour on the same
state and needs no `git`.

### F3 — a pass could be recorded against an installation it never tested

`Set-PrintFlowProductionRevalidation.ps1` read the current installation, the current preset, the
current Windows build and the currently accepted binaries, and then read four fields from a run
result:

```powershell
$regressionSetId = $result.setId
$regressionCompleted = $result.completedAtLocal
$regressionEvidence = $result.evidencePath
if ($result.status -eq 'Passed') { $regressionStatus = 'Passed' } else { $regressionStatus = 'Failed' }
```

The run result already recorded `Workstation`, `ProductVersion`, `PresetId`, `PresetVersion`,
`AdapterMode`, `RequiredCategories`, `MissingCategories` and every case outcome. None of them was
read. A genuine `Passed` from environment A, supplied while the installation was B, was recorded as
"B, and the standard set passed". No forgery and no hand-editing were involved — pointing the script
at the wrong file was enough.

`ProductionRevalidationEvaluator` could not recover the missing check, because it compares the
record against the machine and never reads the run back. It also identified the Product by a
three-part version string, so different Product bytes under the same version inherited the approval.

**The counterexample, likewise reproduced rather than described:** a genuine passing run that tested
preset 0.0.2, supplied while the installation is configured for 0.0.1. The audit commit's own writer
exits **0** and writes a record whose `presetVersion` is the machine's and whose
`standardRegressionSet.status` is the run's — the two halves of F3 in one file.
`The_audited_writer_recorded_a_pass_from_a_run_of_another_environment` runs that script and asserts
the false record; `Publication_refuses_a_run_of_a_different_environment` asserts the refusal.

This is the exact clause of the original requirement that broke. CSV row 64 (work item 11608 →
SCRUM-11123), re-read for this slice:

> Any PrintFlow, Windows, Meitu or Photoshop upgrade must require rerunning the standard test set
> before production use **rather than silently changing the supported environment**.

---

## 2. The evidence contract

One versioned contract, `evidenceBindingVersion = 1`, written by the run that tested the facts and
read by the review path, the revalidation writer and — for its own half — the Product evaluator.

| Bound fact | Read by the run from | Checked by |
|---|---|---|
| `runId`, `binding.invocationId` | the claim staked before the host could operate | wrapper |
| `binding.harnessProductAssemblies` | the Product assemblies this test host loaded | run (against the candidate) |
| `binding.candidateProductAssemblies` | the install folder the run attests | writer, then the Product evaluator |
| `productVersion`, `workstation`, `presetId`, `presetVersion` | configuration and the machine | writer |
| `binding.presetSha256` | the preset manifest's bytes | writer |
| `binding.operatingSystemBuild` | the running OS | writer |
| `binding.meituSha256`, `binding.photoshopSha256` | the accepted preset, through the reader the App uses | writer |
| `adapterMode` | configuration | writer |
| `setId`, `binding.setManifests[]`, `binding.setContentDigest` | the manifests as loaded | writer |
| `requiredCategories`, `missingCategories`, `cases[]` | derived from what ran | writer, re-derived |

### Identity source: two identities, because bytes alone cannot span two build modes

Measured on this checkout, and the reason the obvious design does not work:

| | `PrintFlow.App.dll` SHA-256 | length | `AssemblyInformationalVersion` |
|---|---|---|---|
| Release build (what the run drives) | `1547306A2548…` | 445,952 | `0.1.0+3f83863521c9…` |
| RID self-contained publish (what is installed) | `1376288FED83…` | 445,440 | `0.1.0+3f83863521c9…` |

The regression run executes the ordinary build inside a test host; an installation carries the
publish output. Requiring those to be the same bytes would make publication impossible; comparing
only the three-part version is the F3 defect. So the contract uses both:

- **build identity** (`0.1.0+<source revision>`, readable identically as
  `FileVersionInfo.ProductVersion` from PowerShell and from C#) binds *the code the run exercised*
  to *the candidate the record will speak for*. The run refuses to attest a candidate whose build
  identity differs from its harness's.
- **byte identity** (SHA-256, per assembly) pins *that candidate across time*: recorded by the run,
  re-read by the writer before it publishes, and compared by the running application against its own
  loaded assemblies.

The guarantee the record therefore makes is: **the installed payload has not changed since the run,
and it was built from the same source revision as the code the run exercised.** Its limit is stated
in §8.

**One algorithm, one language.** PowerShell compares *per-file* SHA-256 values — `Get-FileHash` is
`SHA256.HashData` — and never re-implements a composite. The one composite,
`ProductBuildIdentity.Fingerprint`, exists for diagnostics only, lives in C# alone, decides nothing,
and is re-derivable from the list it summarises. There is no second interpretation to drift.

**What is deliberately not identity:** paths, timestamps, folder names, repository HEAD, the
workspace as a whole, and any customer file. A documentation-only commit changes HEAD and changes
nothing that was tested, so it does not invalidate a record; what does is a rebuild and reinstall,
which is a different candidate. Nothing here is a signature, a certificate or a PKI.

---

## 3. What each of the three operations now means

### New execution

- Claims its run identity **before** the host can operate — before configuration is read, before the
  service graph is composed, before anything approaches an external application.
  `RegressionExecutionClaim.Stake` opens the claim with `FileMode.CreateNew`, so the file system
  decides a concurrent race and exactly one claimant wins.
- Refuses an existing execution destination and **deletes nothing**: what is there is the evidence
  that the identity was already used.
- Stamps this invocation's id into the result it writes.
- The wrapper reports `Passed`/exit 0 only when the host exited 0 **and** the result on disk carries
  this run id and this invocation id. A host failure is reported as a host failure whatever the disk
  says.

### Record visual review

- Still the existing `-RecordVisualReview` path, still re-deriving from evidence already on disk,
  still opening no external application.
- Preserves the execution: `runId`, `invocationId`, `startedAtLocal`, **`completedAtLocal`**,
  workstation, candidate, preset, adapter mode, set identity, every case assertion and every artefact
  digest. Re-stamping the completion time to the review moment — which this path used to do — made an
  old run read as one that had just executed.
- Records the reviewer's identity and time in an appended `reviews[]` history, so a run reviewed
  twice has two reviews and the earlier one survives.
- Binds each decision to the artefact digest the run recorded, and refuses a decision whose artefact
  is missing or no longer hashes to it.
- Concludes only genuinely outstanding checks: a decision naming an already-decided check is
  refused, and `Conclude()` still returns early for Blocked, Cancelled and Failed, so no decision can
  lift one into a success.
- Invents no reviewer. A decision file may declare itself `"synthetic": true`, and that flag is
  written into the review history: a decision made by a test never reads as one made by a person.

### Publication

- Validates the complete proposed attestation first (`Test-ProposedAttestation`), then writes.
- Rejected input: **nothing is written**, and any active record is left exactly as it was.
- Writes through `production-revalidation.json.staging`, reads it back and parses it, and only then
  supersedes and replaces. A partial write cannot become the active record.
- **Refusal and revocation stay distinct.** Running the writer with no `-StandardRegressionSetResult`
  still records `NotAvailable`, and a validly bound non-passing run still records `Failed` — the
  operator's existing, legitimate ways to close Production, preserved unchanged. Refusing unbindable
  input is neither: such input says nothing about this installation, so recording either verdict
  would be an invention. Exit 2 means recorded-and-closed; exit 3 means refused-and-untouched.

---

## 4. Compatibility

- Record `schemaVersion` 1 → **2**. A schema-1 record is still parsed and still readable, and blocks
  with its own diagnostic — "predates the regression evidence binding … stays readable as history" —
  rather than a generic unknown-schema message. It is never enriched into a passing schema-2 record.
- A schema-2 record claiming `Passed` with no `runId`, `invocationId` or binding version is refused:
  a status with no execution behind it is what F3 exploited.
- The four fields the writer has always read keep their names and casing. Run results written before
  this contract simply have no binding, remain readable as history, and cannot produce a pass.
- **No force-pass or bypass switch was added anywhere.** The protocol tests replace the test host
  through the script's own `dotnet` resolution order (`LOCALAPPDATA`, then `PATH`) rather than
  through a flag — and that seam cannot manufacture a pass, because the wrapper's verdict still
  requires this invocation's own bound result.

---

## 5. Tested entry points and validation

Deterministic, and every one of them drives a real boundary rather than a helper in isolation.
`RegressionEvidenceIntegrityTests` starts `powershell.exe` against the scripts in `tools\`; the two
runner tests call the very test methods the wrapper filters to.

| Boundary | Test |
|---|---|
| stale `Passed` + failed host, at the audit commit | `The_audited_wrapper_reported_a_previous_runs_pass_as_this_invocations_success` |
| the same state now | `A_previous_runs_pass_is_not_this_invocations_success` |
| a pass from another environment recorded, at the audit commit | `The_audited_writer_recorded_a_pass_from_a_run_of_another_environment` |
| host failure not overruled by a complete, correctly bound result | `A_result_cannot_overrule_a_host_that_failed` |
| a result from another invocation | `A_result_from_another_invocation_is_not_this_ones_outcome` |
| a correct fresh matching execution passes | `A_matching_execution_reports_the_pass_it_produced` |
| concurrent claim of one identity | `One_run_identity_admits_one_invocation` |
| the runner obeys the claim before composing anything | `The_runner_refuses_to_start_on_a_claimed_identity` |
| review preserves identity, times and artefact digests | `A_review_preserves_the_execution_it_reviews` |
| the reused-identity rule does not break reviewing a run | `A_review_is_not_a_new_execution` |
| a decision about a changed artefact | `A_review_of_a_changed_artefact_is_refused` |
| candidate bytes differ under one version string | `Publication_refuses_a_candidate_whose_bytes_differ_under_the_same_version` |
| preset/environment mismatch now | `Publication_refuses_a_run_of_a_different_environment` |
| seven-category completeness, duplicates, Passed/Pending contradiction | `Publication_re_derives_the_verdict_rather_than_copying_it` |
| legacy evidence cannot be enriched | `Legacy_evidence_cannot_be_enriched_into_a_current_approval` |
| refused input leaves the active record alone | `Refused_publication_leaves_the_active_record_untouched` |
| deliberate revocation still works | `Recording_no_passing_set_remains_a_deliberate_operation` |
| the Product evaluator reads an accepted publication, and refuses it against other bytes | `An_accepted_publication_is_read_by_the_product_evaluator` |
| a pre-binding record stays historical | `A_pre_binding_record_stays_historical` |
| a recorded pass names its execution | `A_recorded_pass_names_the_execution_it_passed_in` |
| a supplied path that does not resolve is refused, not read as absent | `A_supplied_path_that_does_not_resolve_is_refused_rather_than_read_as_absent` |
| a synthetic review cannot open Production | `A_synthetic_review_cannot_open_production` |
| a candidate built from different source is not the tested candidate | `A_candidate_from_different_source_is_not_the_tested_candidate` |
| a run that bound no candidate is not publishable, at either boundary | `A_run_that_bound_no_candidate_is_not_publishable` |
| a review concludes only what is outstanding, and keeps the earlier review | `A_review_concludes_only_what_is_outstanding_and_keeps_the_earlier_one` |

**Nothing real was touched.** No Meitu, Photoshop or Maintop was launched. No live readiness or
standard-set acceptance was run. Every synthetic set, run folder, installation, workspace and
revalidation record lived under one temporary directory per test. The real workstation's production
revalidation record and production database were neither read nor written.

**A synthetic test of this protocol is not a standard-set acceptance.** These tests prove the
evidence chain refuses what it should and accepts what it should. They exercise no real image and
accept nothing. Only a real run on the fixed workstation, reviewed by a person, is an acceptance.

### Counts

- Focused script, contract and revalidation tests during implementation: 25 passed in
  `RegressionEvidenceIntegrityTests`; 59 passed across the existing revalidation and regression
  tests.
- Full Product suite, run once at the end because shared Product schema and evaluator behaviour
  changed - the record's schema version, the evaluator's checks and its call site are all shared
  Product code, so tooling-only scoping would not have been honest here: see §9.
- Clean Release build: 0 warnings, 0 errors.
- Opt-in live tests: **NOT RUN**, as before. Test discovery was not rewritten to change skip counts.
- Dependencies unchanged; no dependency audit repeated.

---

## 6. Authorization, unchanged

`VerifiedEnvironmentGate` is not weakened. `ProductionRevalidation` is not removed — it gained a
check. `ForStandardRegressionRun` is not broadened: it still suppresses exactly one check, still only
when that check is the sole thing blocking, and the run still records whether it was needed. The
application still has no writer for its own approval, and the architecture test that asserts so is
untouched. Production code references no test assembly: the shared contract lives in
`PrintFlow.Infrastructure`, and the test assembly references it, not the other way round.

The live runbook's ordering (§2.4, §3.6, §7.3–7.6) now states the legitimate sequence:

```text
controlled standard-set bootstrap -> one complete run -> actual operator reviews
  -> evidence-bound revalidation -> normal App live checks -> normal App E2E
```

Historical reports were not rewritten.

---

## 7. Independent review

A genuinely separate read-only reviewer was run against the original R1 requirements, the diff, the
producer and consumer paths and the test evidence — not against a summary of the implementation. It
was asked specifically about false PASS, identity mismatch, stale evidence, review preservation,
unnecessary scope and vacuous tests. Its findings were resolved as follows; the ones it raised that
were genuine defects are the reason this section exists rather than a claim of first-pass
correctness.

### Fixed as a result of the review

| Finding | What was wrong | Resolution |
|---|---|---|
| A mistyped `-StandardRegressionSetResult` or `-StandardRegressionSetPath` was read as "no result supplied", so it wrote a `NotAvailable` record **over the operator's active record** and exited 2 — indistinguishable from deliberately closing Production. The runbook table this slice added asserted the opposite. | The validator was only ever called inside the two path-exists gates, so unreadable input skipped it entirely. | A supplied parameter that does not resolve is now refused before anything is written: exit 3, active record untouched. Asserted by `A_supplied_path_that_does_not_resolve_is_refused_rather_than_read_as_absent`. |
| `MeituSha256` and `PhotoshopSha256` compared **null with null** and passed, after which the writer wrote those fields **from the machine** — the exact substitution this repair exists to prevent. | The four binding digests were checked for property *presence*, not for being non-empty, and the run left them null when the preset could not be read. | Those fields must now be non-empty or publication is refused, and the run records a candidate problem instead of narrating the gap to its log. |
| The seven-category gate counted **any** JSON with a `category` property, recursively — so a previous run's per-case evidence could satisfy it if the parameter were pointed at the set root. | The scan predates this slice, but the runbook text added here claimed it checked manifests. | It now requires a `fixtureId` and a `file` block too, which is what a manifest has and per-case evidence does not. |
| The set-content check was one-directional: a binding naming one manifest out of seven satisfied "every manifest the run named is present". | Only the run→disk direction was checked. | The run must also have read a manifest for each required category. |
| `Synthetic` on a review was decorative — nothing consumed it, so a protocol test's decisions could have produced a production approval. | The label was written and never read. | Publication now refuses a run whose qualitative checks were concluded by a synthetic review. Asserted by `A_synthetic_review_cannot_open_production`. |
| Eight `Test-Path` calls without `-LiteralPath`, so a path containing `[` or `]` reported a real file as absent — and fell into the silent-revocation path above. | Wildcard interpretation. | All converted to `-LiteralPath`. |
| The wrapper's review branch verified nothing about the result it reported a verdict from, so reviewing a pre-binding `Passed` result printed "Record the revalidation with…". | The whole binding check was skipped on that path. | Both paths now check the run identity and the presence of a binding; only a new execution checks the invocation identity, because a review deliberately updates another invocation's run. |
| A refused claim left an empty run folder behind, which the wrapper then read as an identity already used — burning that `RunId` for good. | `Stake` created the directory before it could refuse. | It now removes the directory only if this call created it and it is still empty. |
| `Fingerprint` would throw on a default array while constructing a diagnostic message. | Unreachable in practice, but a check must not fail while explaining itself. | Takes an `ImmutableArray` and answers for a default one. |
| `A_matching_execution_reports_the_pass_it_produced` asserted a phrase both exit-0 branches print, so it would have kept passing if candidate binding broke. | Vacuous assertion. | Asserts the publish instruction instead. |
| The concurrency the claim's whole design rests on was tested **sequentially**. | The race was never run. | Eight claimants released together on one identity; exactly one leaves with the claim. |
| `CompareBuildIdentity` — one of the two identities the contract rests on — and both consumers of `CandidateProblems` were asserted nowhere. | No coverage. | `A_candidate_from_different_source_is_not_the_tested_candidate` and `A_run_that_bound_no_candidate_is_not_publishable`. |
| Three review requirements untested: re-deciding a decided check, a Failed case surviving a decision, and prior review evidence being kept. | No coverage. | `A_review_concludes_only_what_is_outstanding_and_keeps_the_earlier_one`. |

### Raised and deliberately not changed, with reasons

- **"`environmentReadinessPassed` is still an unverified operator switch, and the run already proves
  it."** Correct on the facts, and deliberately left alone: the slice brief requires that operator
  EnvironmentReadiness attestation stay *distinct from tool-verified facts* and that no command-line
  boolean be treated as proof a live probe ran. Binding the run's observed readiness into the record
  as a tool-verified fact is a change to what opens Production and belongs to a slice that scopes it,
  not to this one. It is listed as residual risk 7 below.
- **"`setContentDigest` and the harness identity are written and never compared."** Both are required
  by the brief — a deterministic content identity for the manifests, and the identity of the Product
  assemblies actually exercised as *provenance*. The per-manifest digests underneath the composite are
  what the writer compares; the composite is explicitly non-authoritative and documented as such.
- **"The runbook sequencing material was excluded by the plan."** The plan's wording was wrong, not
  the runbook: the brief requires the ordering to be corrected. `PLAN.md` §1 now says so.
- **"The audit-commit reproductions should not be permanent tests."** Kept, because they are the only
  evidence that distinguishes a fixed defect from a described one, and split so that each historical
  half is its own test and the repaired behaviour is asserted by a separate test needing no `git`.
  The dependency is listed as residual risk 6.

---

## 8. Remaining risk and honest limits

1. **Build identity is a committed revision, not a clean tree.** `SourceRevisionId` names the commit
   the build was made from; a build from a dirty working tree carries the same identity as a clean
   one. The byte comparison still detects a re-installed payload, and the run-to-publication window
   is still pinned, but "same source revision" assumes the operator built from a clean checkout.
   Closing that would mean a signing or provenance system, which this slice is explicitly not.
2. **The candidate is bound, not exercised.** The run drives PrintFlow from the built repository, not
   from the installed executable. The record's claim is "the installed payload was built from the
   source this run exercised, and has not changed since" — not "the installed binary was executed".
   That is a real and useful claim, and it is not a stronger one.
3. **A single-file publish would blind the byte comparison.** `ProductBuildIdentity.Running()`
   resolves assemblies through `Assembly.Location`, which a single-file publish leaves empty. This
   product does not use one, and the failure is closed rather than open — an unreadable identity
   never compares equal — but it would present as a blocked workstation rather than as a packaging
   change.
4. **The writer trusts a validly bound run's own arithmetic for `setContentDigest`.** It validates
   the per-manifest digests that digest summarises, so the composite cannot disagree with checked
   data; it does not recompute the composite in PowerShell, deliberately, to avoid a second
   implementation.
5. **A run identity, once claimed, is spent.** Nothing expires or clears a claim, so an invocation
   killed mid-run has used that `RunId` and the operator picks another. That is deliberate — the
   alternative is a timeout after which a second execution may overwrite a first one's evidence — but
   it is a thing an operator has to know rather than discover, so §7.3 of the runbook now says it.
6. **Two tests assert that the audit commit's own scripts still exit 0.** Intentional: it keeps both
   counterexamples honest rather than paraphrased. It does mean those two depend on `git show` of a
   fixed commit, so a shallow clone, an export or a rewritten history fails them with a stated
   reason. Each is a test of its own and the repaired behaviour is asserted by separate tests that
   need no `git`, so such a checkout loses the historical evidence and keeps the regression cover.
7. **`environmentReadinessPassed` is still an operator attestation, not a tool-verified fact.** The
   run does prove it — it refuses to start a single case unless every blocking live check passed, and
   writes `readiness.json` — but that proof is deliberately *not* bound into the record here. The
   slice brief requires operator attestation to stay distinct from tool-verified facts and forbids
   treating a command-line boolean as proof a probe ran, and binding the run's observed readiness
   would change what opens Production. So after this slice the gate's inputs are validated evidence
   plus one unverified switch. That is the largest remaining unverified input, it is a deliberate
   boundary rather than an oversight, and it is the obvious candidate for a later slice that scopes
   it properly.
8. **This slice obtained no production approval, deliberately.** R2 and R3 will change Product code,
   which would immediately invalidate any record written now — and correctly so, because the
   candidate bytes would change.

---

## 9. Result

Full Product suite, Release: **11,804 passed / 0 failed / 0 skipped** (5 m 40 s).
Clean Release build: **0 warnings / 0 errors**.

Run once, at the end, on the settled source — after the review findings in §7 were resolved, so the
recorded run is the one that describes what was committed. The audit-era baseline was 11,779; the
25 additions are this slice's. Opt-in live tests remain inert and are counted as passing no-ops
exactly as before, which is why the skip count is still 0: test discovery was not touched.

Real live acceptance **NOT RUN**. Real production revalidation **NOT WRITTEN**. Jira acceptance
statuses unchanged. SCRUM-11065, SCRUM-11123 and SCRUM-11130 are **not** marked FULL: those labels
require live evidence this slice is forbidden from producing.

---

## 10. Audit delta

Appended to the record, not rewriting it.

| Finding | State at `3f83863` | State now |
|---|---|---|
| F4 | Statically reachable: an old `Passed` result.json could be reported as a failed invocation's success; run identities were reusable. | Closed at the wrapper and the runner. Reproduced at the audit commit and refused by the current script. |
| F3 | Statically reachable: a genuine pass from one environment could be recorded against another; the Product identified a candidate by version string alone. | Closed at the writer and the evaluator. The writer validates the whole attestation and refuses without writing; the evaluator compares candidate bytes and refuses an unbound pass. |

Neither finding asserted that a historical false approval had actually occurred, and this report
asserts none either. Both were statically reachable defects; both are now closed with
counterexamples that fail before the fix and pass after it.
