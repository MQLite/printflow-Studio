# Epic 11500 Part B — Verified Environment Gate Integration

**Verdict: 11500-B PASS WITH NOTES — VERIFIED ENVIRONMENT GATE READY; PRODUCTION MODE STILL DISABLED**

Part A established, factually, what this workstation is. Part B makes that establishment the
authority behind production permission, and does nothing else: `Adapters.Mode` is still `Fake`,
`appsettings.json` is untouched, no operator switch was added, and no Photoshop or Meitu
automation logic changed.

The two notes are recorded in §18 and §19 below. Neither affects the gate's behaviour.

---

## 1. The 11500-A verifier is the authority

`IProductionWorkstationVerifier` is unchanged. Every accepted value it compares against still
comes from the hash-verified v1.15.0 manifest; every observed value still comes through the two
narrow readers. Part B added no workstation fact, minted no preset version, and hard-coded no
requirement — §33's contract gap was never reached.

What changed is one sentence of its own documentation. Part A's remarks said "nothing consumes
this"; they now name `VerifiedEnvironmentGate` as the single consumer. The same correction was
applied to `WorkstationVerificationResult.Verified`, whose `<param>` described itself as a
candidate answer.

## 2. The EnvironmentGate implementation

`src/PrintFlow.Infrastructure/Gate/VerifiedEnvironmentGate.cs` — one class, implementing
`IEnvironmentGate` and `IEnvironmentDiagnostics`, holding exactly one field: the verifier.

```
SessionService / Workflow
    → IEnvironmentGate            (Workflow port, the only authorisation seam)
        → IProductionWorkstationVerifier   (Infrastructure, facts)
```

The direction is asserted structurally, not just intended:

- `Only_the_gate_implementation_consumes_the_verifier` scans all of Infrastructure and finds
  `IProductionWorkstationVerifier` named only in the gate and in the file that declares it.
- `No_adapter_source_file_names_the_verifier` (Part A) still holds.
- `No_adapter_holds_an_environment_gate` is new: an adapter may mention the gate in a doc comment
  and may not hold one in code. An adapter that could ask the gate could act on the answer, and
  the day the two checks disagree is the day a production adapter runs unverified.
- `The_gate_is_the_only_thing_the_workflow_asks_about_production_permission` proves it as
  behaviour: substituting the gate is sufficient to change what the workflow may do, which could
  not be true if the session service or the adapter consulted the workstation itself.

## 3. Fake behaviour

Fake is allowed unconditionally, and the verifier is **not consulted at all** — asserted with a
verifier that throws if it is called. A wrong Photoshop digest, a second monitor, a locked screen:
Fake still works. This is the mode a developer, QA, or the person repairing the workstation is
using, and a gate that consulted the verifier first would make a broken workstation unable to
diagnose itself.

## 4. Production PASS behaviour

Production is allowed when `WorkstationVerificationResult.Verified` is true — which the result
derives from its own checks, so no caller can hand the gate a result claiming to be verified while
carrying a failure.

One question about the whole workstation, not one per adapter (§13). The accepted preset describes
one production workstation, so a Meitu digest mismatch closes Production for Photoshop too. The
interface itself is the guarantee: `Verify` takes an `AdapterExecutionMode` and nothing that could
name an adapter, so a per-adapter rule has nowhere to live. If capability-specific gates are ever
needed, that is a separate design change to a contract that does not currently describe them.

## 5. Blocking versus advisory

Authorisation reads only blocking checks. The two advisories the current preset produces —
`FilesystemReadOnlyPolicyAdvisory` and `ExternalApplicationUiLanguage` — are carried into the
failure context and into the diagnostics report, labelled as advisories, and never close
Production. `An_advisory_only_result_still_authorises_production` proves both halves: the gate
allows, and the advisory is still visible rather than swallowed.

The `IsBlocking` flag on `EnvironmentCheckReport` is false for every advisory, so a support screen
separates the two without re-deriving the rule.

## 6. Dynamic re-evaluation

No caching in the gate. Every Production request calls `Verify()` afresh; no TTL, no freshness
window, no stored verdict. `ObservedAt` is audit information and is never read as permission (§7).

`Every_production_request_re_observes_the_dynamic_facts` counts reads through the fact reader:
three gate calls produce exactly three times the first call's read count. `A_fake_request_observes_nothing`
shows the count stays at zero for Fake.

## 7. Secure desktop and session behaviour

A secure desktop (`Winlogon`), a non-interactive session, and an unsupported remote session each
refuse Production. `A_locked_screen_closes_production_and_unlocking_reopens_without_a_restart`
walks the operator case end to end in one process: pass → lock → refuse → non-interactive → refuse
→ restore → **pass, same gate instance, no restart**.

Foreground ownership is deliberately *not* a gate check (§17). The gate verifies desktop
readiness; the operation-time automation guard verifies ownership when a guarded input actually
runs, and still answers `PhotoshopTargetLost` / `inputSent=false`.

## 8. Display-change behaviour

`A_display_change_closes_production_and_restoring_it_reopens_without_a_restart`: pass → second
active display → refuse → accepted geometry at 96 DPI instead of 120 → refuse → restore → pass.
Same gate, same process. This is the proof that dynamic re-evaluation is real rather than
described; a caching gate would still be refusing on the fourth call.

Produced through the synthetic fact reader (§26). The live workstation was never altered to prove
a refusal.

## 9. Immutable cache and restart semantics — **audited, and this is a real constraint**

Part A caches the signed baseline (manifest, evidence chain, both binaries, the Action file) for
the life of the process, because those bytes cannot change without also invalidating the hash that
approved them, and re-hashing two application binaries on every production step buys nothing
against an attacker who can already write to them.

The consequence, stated plainly rather than left to be discovered:

> **Changing an accepted baseline artefact while PrintFlow is running does not close Production
> until PrintFlow is restarted.** An operator or administrator who replaces the Photoshop
> executable, the Action file, the manifest or an evidence file must restart PrintFlow before the
> new production authorisation can be trusted.

`Immutable_baseline_drift_during_a_run_is_process_cached_until_restart` pins this: a running gate
keeps authorising after the Photoshop binary is tampered with, and a newly constructed gate — a
restarted PrintFlow — refuses. The test exists so that changing this behaviour later is a
deliberate decision rather than a silent one.

No file watcher was introduced (§20). The security model is unchanged from Part A.

## 10. Composition-root wiring

`ServiceRegistration.RegisterEnvironmentGate` registers, from the configuration already parsed for
`IWorkstationPresetProvider` — no second parse of `appsettings.json`, no second hash parse:

| Registration | Lifetime | Notes |
|---|---|---|
| `IProductionWorkstationVerifier` | Singleton | via `ProductionWorkstationVerifier.ForWorkstation` |
| `VerifiedEnvironmentGate` | Singleton | the concrete object |
| `IEnvironmentGate` | Singleton | resolves to that object |
| `IEnvironmentDiagnostics` | Singleton | resolves to the *same* object |

`Exactly_one_environment_gate_is_registered_and_it_consults_the_verifier` asserts
`GetServices<IEnvironmentGate>()` has a single item, that it is `VerifiedEnvironmentGate`, and that
the diagnostics seam is reference-equal to it — so the process has one consumer of verification
rather than two that could drift apart. There is no branch on `Adapters:Mode`, no development gate,
and no permissive fallback: a missing verifier is impossible to construct around, because the
constructor rejects null.

**Deviation from §14, recorded.** §14 asked for the narrow fact readers to be registered.
`Win32WorkstationFactReader` and `FileSystemArtifactReader` are `internal` to Infrastructure by
Part A §18's design, and publishing them so the composition root could name them would widen
exactly the surface Part A narrowed. They are composed by `ProductionWorkstationVerifier.ForWorkstation`
instead — the public factory Part A already provides for this. What §14 was protecting (the
verifier reads the machine through those two readers and through no shell, script, or
caller-chosen registry path) is asserted by the Part A boundary tests, unchanged.

`PresetWorkstationRequirements` is an `internal static` reader invoked by the verifier, not an
injectable service; there is nothing to register.

## 11. Startup behaviour

Registration constructs; it does not verify. The manifest, evidence, binaries and session are read
on the first `Verify()`, which happens when something asks for Production authorisation.

`Startup_succeeds_and_the_shell_opens_when_production_verification_cannot_pass` runs the real
`ApplicationStartup` against a layout whose preset is not a workstation manifest at all — so
verification *cannot* pass — and asserts `CanShowShell`, no startup failure, a resolvable
`WorkflowSelectionViewModel`, and a gate that is present and refusing. A display mismatch closes
Production; it does not become "PrintFlow cannot start".

## 12. Diagnostics and localisation

**Diagnostics (§21).** `IEnvironmentDiagnostics.Read()` in `PrintFlow.Workflow/Ports` returns an
App-safe `EnvironmentReadinessReport`: `Verified`, `PresetIdentity`, `ObservedAt`, and one
`EnvironmentCheckReport` per check with `CheckKey`, `Status`, `IsBlocking`, `MessageKey` and a
capped `Detail`. `BlockingFailures` and `Advisories` are separate projections.

Read-only, and structurally incapable of becoming permission: one method, named `Read`, and the
report carries no `Enable`, `Override`, `Ignore` or `Continue anyway` — asserted by test. No UI was
added; the full operator screen is left to the next slice, as §21 permits. Nothing above
Infrastructure names a verification type to consume it.

**Localisation (§22).** Twelve `EnvironmentCheck_*` keys — one per `WorkstationVerificationCheck` —
plus four `Environment_*` labels, in both `Strings.resx` and `Strings.zh-CN.resx`. The gate sets
`OperationFailure.MessageKey` to the first blocking failure's key, so the operator reads
"Display configuration does not match the verified workstation." rather than
`DisplayConfiguration`. `DisplayNames.Failure` already resolves arbitrary message keys, so no App
code changed.

`Every_workstation_check_has_operator_wording_in_both_languages` is driven off the enum, so a check
added to the accepted contract cannot reach an operator as a bare resource key.
`No_environment_string_offers_a_way_past_the_gate` scans both languages for override wording,
because a label is easier to add than a code path.

## 13. Audit and logging (§23)

The refusal's `OperationFailure` carries a bounded, itemised context:
`verification.result`, `verification.observedAt`, `verification.preset` (id, version, 12-character
digest), `verification.failedChecks`, one `verification.failure.<Check>` per failure (its
`FailureCode` and a 200-character-capped explanation, at most 8), and `verification.advisories`.

`TechnicalDetail` is the verifier's own `Describe()` summary, capped.

Deliberately absent: manifest JSON, the evidence chain, registry state, machine inventory, and the
`Expected`/`Observed` pair — which on a check result can hold an accepted path or digest and has no
business being persisted on a workflow attempt. `A_refusal_is_one_stable_code_with_bounded_structured_context`
asserts every context value is ≤260 characters and contains no JSON. No telemetry, no network
reporting, no customer data.

**Stable workflow code (§10).** Every refusal is `FailureCode.EnvironmentNotVerified`. No workflow
failure code was minted per workstation sub-check, so display scaling does not become permanent
product state vocabulary. The typed detail lives in the context and the diagnostics report.

## 14. Absence of bypass (§24)

`No_product_source_offers_a_verification_bypass` scans all four `src` projects for
`SkipEnvironmentCheck`, `IgnoreWorkstationVerification`, `ForceProduction`, `AllowUnsafeProduction`,
`BypassEnvironmentGate`, `SkipVerification`, `OverrideEnvironment` — all absent.
`The_temporary_foundation_gate_no_longer_ships` asserts `VerifiedEnvironmentGate` is the only
concrete `IEnvironmentGate` in the Infrastructure assembly.

**FoundationEnvironmentGate history (§5).** Epic 11100's gate is removed, not left dormant beside
its replacement. Its historical fail-closed intent is not weakened: an unverified workstation is
refused exactly as before, now on evidence rather than on the absence of any. The suite's need for
a gate that refuses on any machine is met by a test-only `UnverifiedEnvironmentGate` in
`tests/Fixtures`, which is the opposite of permissive and is not product code.

## 15. Targeted tests

| Suite | Count |
|---|---|
| `VerifiedEnvironmentGateTests` | 30 |
| `EnvironmentGateCompositionTests` | 5 |
| `EnvironmentGateTests` | 2 |
| `WorkstationVerificationBoundaryTests` | 36 |
| Targeted run (with localisation, verifier, dependency-rule suites) | **174 passed, 0 failed** |

Coverage against §28: (1) Fake allowed without the verifier ✓ (2) verified → allowed ✓
(3)–(12) each blocking failure refuses, as a ten-case theory plus secure-desktop and remote-session
facts ✓ (13) advisory-only allowed ✓ (14) dynamic re-evaluation counted ✓ (15) display
fail → restore → pass, no restart ✓ (16) session fail → restore → pass, no restart ✓
(17) bounded structured context ✓ (18) startup usable when verification fails ✓ (19) exactly one
gate registered ✓ (20) no adapter depends on the verifier or the gate ✓ (21) no bypass ✓
(22) `Adapters.Mode` remains Fake ✓

## 16. Controlled live gate result (§25)

`VerifiedEnvironmentGateWorkstationSmoke`, opt-in behind `PRINTFLOW_WORKSTATION_VERIFY=1`, builds
the graph with the **real `ServiceRegistration`** from the **real committed `appsettings.json`** and
the real workspace root, resolves `IEnvironmentGate` from it, and asks for Production. The one
substitution is a throwaway database, which the gate does not consult.

```
configured preset : printflow-workstation-v1 1.15.0
configured mode   : Adapters.Mode = Fake
gate type         : VerifiedEnvironmentGate
preset identity   : printflow-workstation-v1 1.15.0 (3392873ED0CA)
observed at       : 2026-09-01 05:29:11Z

[Passed  ] blocking PresetIntegrity              manifest hashes to the configured 3392873ED0CA
[Passed  ] blocking EvidenceIntegrity            all 27 evidence files present and hashing exactly
[Advisory] advisory FilesystemReadOnlyPolicyAdvisory
                                                 16 of 27 evidence files no longer carry read-only;
                                                 SHA-256 remains authoritative and every digest matched
[Passed  ] blocking OperatingSystem
[Passed  ] blocking MeituExecutable
[Passed  ] blocking PhotoshopExecutable
[Passed  ] blocking PhotoshopActionArtifact      canonical 'PrintFlow DTF' Action file
[Passed  ] blocking WorkspaceRoot
[Passed  ] blocking InteractiveSession           supported interactive local-console desktop
[Passed  ] blocking DisplayConfiguration
[Passed  ] blocking UiCulture
[Advisory] advisory ExternalApplicationUiLanguage

verified          : True
gate(Production)  : ALLOWED
gate(Fake)        : ALLOWED
```

**10 blocking checks passed. 2 advisories. Gate result: Production ALLOWED.**

No external application was launched to reach that answer, and no production job was run to prove
it. The same run then asserts the configured application still composes fake adapters —
`IMeituProcessor.Mode` and `IPhotoshopOutputProcessor.Mode` are both `Fake` — so observing that the
gate would authorise Production enabled nothing.

**Controlled dynamic-refusal proof (§26).** Wrong display, non-interactive session, secure desktop,
remote session, wrong executable digest, wrong Action digest, wrong culture, wrong OS build,
tampered manifest, tampered evidence, wrong workspace root — each refuses through the synthetic
fact reader, and restoring the accepted facts passes again. The live machine was never
intentionally broken.

## 17. Complete suite

Run once, after targeted tests, the live gate proof, a clean build, and final product source:

```
dotnet test PrintFlowStudio.sln --no-build --no-restore
Passed!  -  Failed: 0, Passed: 9911, Skipped: 0, Total: 9911, Duration: 1 m 55 s
```

Build: **0 warnings, 0 errors.**

Dependencies unchanged — no `.csproj`, `Directory.Packages.props`, `packages.lock.json`,
`nuget.config` or `global.json` was touched — so the Epic 11400 Final QA vulnerability audit was
not repeated (§32).

## 18. Note — a pre-existing latent test defect, found and fixed

The first complete-suite run failed one test:
`PhotoshopFinalReviewBoundaryTests.CleanupWorking_still_has_no_production_interpreter_or_caller`.

The cause predates this slice. Commit `3e1c320` (11500-A) added a doc comment to
`ProductionWorkstationVerifier.VerifyWorkspaceRoot` reading *"No cleanup runs and `CleanupWorking`
is not reachable from here"* — a sentence asserting the **absence** of the behaviour. That scan was
the only boundary test in the suite doing a raw `File.ReadAllText().Contains(...)` without skipping
comment lines, so it read the promise as a violation. 11500-A's targeted test policy did not run
this file, which is why it stayed latent.

Fixed by making the scan skip comment lines, matching the convention every other boundary scan in
this suite already uses (`IsComment` in `WorkstationVerificationBoundaryTests`). The check is not
weakened: a real call site is not a comment line. Leaving it as-is would have pushed the next
author to delete a true sentence rather than keep the promise.

This is a test-only change. Product source was unchanged between the two suite runs; the suite was
rerun because the first run did not pass, not because product source moved.

## 19. Note — a retained synthetic Photoshop document was not closed

The preflight asked for any retained synthetic Photoshop document to be closed manually with
*Don't Save*, and explicitly forbade automating the discard dialog. At preflight, Photoshop was
running with `Elaine Rudolph_A4.tif @ 16.7% (图层 1, W1/16)` open, and Meitu (`XiuXiu`) was running.

Neither was closed, and neither blocked this slice: nothing in Part B launches, drives, or reads a
live external application. The gate hashes the Photoshop and Meitu binaries on disk, which is
unaffected by whether they are running — as the live gate proof above shows, both executable checks
passed with both applications open. The document is left for the operator to discard.

## 20. `Adapters.Mode` status

`appsettings.json` is **unmodified**. It is not in the change set at all.

```json
"Preset":   { "Id": "printflow-workstation-v1", "Version": "1.15.0", ... },
"Adapters": { "Mode": "Fake" }
```

`The_shipped_configuration_still_runs_fake_against_the_accepted_preset` asserts both against the
committed file, so a later edit to either is a test failure rather than a discovery.

## 21. Remaining scope before Production mode can be activated

Part B makes the gate correct. It does not open Production. What is still outstanding:

1. **`ServiceRegistration.RegisterAdapters` still throws for `Mode: "Production"`.** Its message
   now names two owners that are complete (Epic 11400's Photoshop adapter, Epic 11500's gate) and
   should be revisited by whichever slice actually enables the mode. Failing closed is still the
   right behaviour until then; this slice deliberately did not touch adapter registration.
2. **The operator diagnostics UI.** The read model ships; no screen consumes it. A person told
   "Production workstation verification failed" currently has no place in the application to see
   which check failed.
3. **A production activation decision**, with whatever release process it needs. Nothing in this
   slice constitutes one.
4. **The §9 restart semantic** should reach the operator documentation, not only this report:
   replacing an accepted artefact requires restarting PrintFlow.

## 22. Git state

Branch `master`, ahead of `origin/master`. No push, no amend, no history rewrite. 11500-A commits
`3e1c320` and `593f5f8` are intact.

**Added**
- `src/PrintFlow.Workflow/Ports/IEnvironmentDiagnostics.cs`
- `src/PrintFlow.Infrastructure/Gate/VerifiedEnvironmentGate.cs`
- `tests/PrintFlow.Tests/Fixtures/UnverifiedEnvironmentGate.cs`
- `tests/PrintFlow.Tests/Integration/Verification/VerifiedEnvironmentGateTests.cs`
- `tests/PrintFlow.Tests/Integration/Startup/EnvironmentGateCompositionTests.cs`
- `tests/PrintFlow.Tests/Smoke/VerifiedEnvironmentGateWorkstationSmoke.cs`
- `docs/printflow/phase-11500-b-verified-environment-gate-integration.md`

**Removed**
- `src/PrintFlow.Infrastructure/Gate/FoundationEnvironmentGate.cs`

**Modified**
- `src/PrintFlow.App/Composition/ServiceRegistration.cs` — gate and verifier registration
- `src/PrintFlow.App/Resources/Strings.resx`, `Strings.zh-CN.resx` — 16 keys each
- `src/PrintFlow.Infrastructure/Verification/ProductionWorkstationVerifier.cs` — doc comment
- `src/PrintFlow.Infrastructure/Verification/WorkstationVerificationResult.cs` — doc comment
- `src/PrintFlow.Infrastructure/Adapters/{Meitu,Photoshop}/*Composition.cs` — doc comments
- six test files updated for the gate replacement, plus the §18 scan fix

No machine snapshot, registry dump, screenshot, runtime database, generated production artefact or
external preset/evidence file is included.

---

## Verdict

**11500-B PASS WITH NOTES — VERIFIED ENVIRONMENT GATE READY; PRODUCTION MODE STILL DISABLED**

- `IEnvironmentGate` is the only Workflow authorisation seam ✓
- Production uses the accepted 11500-A verifier ✓
- every blocking verification failure refuses Production ✓
- advisories do not block Production ✓
- dynamic checks are re-observed on every Production request ✓
- restored workstation state passes without an application restart ✓
- Fake remains usable when workstation verification fails ✓
- exactly one product gate is registered ✓
- no adapter can self-authorise ✓
- no bypass or override ships ✓
- controlled live gate passes on the current workstation ✓
- `Adapters.Mode` remains `Fake` ✓
- targeted tests pass (174) ✓
- build has 0 warnings / 0 errors ✓
- the one justified complete suite passes (9911 / 0 failed / 0 skipped) ✓

Notes: §18 (a pre-existing latent test defect from 11500-A, found by this slice's complete-suite
run and fixed in the test) and §19 (a retained synthetic Photoshop document left open, which
blocked nothing).

Production mode is not enabled in this slice.
