# Epic 11500 Part D — Production Adapter Activation & Final Live Gate

**Verdict: 11500-D PASS — PRODUCTION MODE ACTIVATED**

Committed `Adapters.Mode` is now `Production`.

Part A established what this workstation is. Part B made that establishment the authority behind
production permission. Part C made the answer legible and settled the trust model for baseline
drift. Part D is the slice where the switch is actually thrown: the composition root now composes
the accepted production adapters, the whole path was proved live on the accepted workstation
through the real gate, and only then did the committed configuration change.

One value changed in one file. Everything else in this report exists to justify that one line.

---

## 1. Preflight

Established before any source was edited.

| | Observed |
|---|---|
| Branch | `master`, clean working tree |
| HEAD | `f0080c0` — *Report: Epic 11500 Part C production activation readiness and operator diagnostics* |
| Build | `dotnet build` — 0 warnings, 0 errors |
| Committed adapter mode | `Fake` |
| Committed preset | `printflow-workstation-v1` **1.15.0**, `3392873ED0CA…` |
| `RegisterAdapters` Production branch | `throw new NotSupportedException(...)` |
| Live gate (`PRINTFLOW_WORKSTATION_VERIFY=1`) | **Production ALLOWED** — 10 blocking checks passed, 2 advisories |
| Photoshop / Meitu running | neither |

The live gate reading is the one that mattered: activation could only be attempted at all because
the accepted workstation was already passing every blocking check on evidence.

---

## 2. Source and configuration changes

**Added**

| File | What it is |
|---|---|
| `tests/PrintFlow.Tests/Integration/Startup/ProductionCompositionTests.cs` | §2, §4, §14, §16 — what each configured mode composes |
| `tests/PrintFlow.Tests/Integration/Automation/ProductionGateSideEffectTests.cs` | §6 gate-before-side-effect, §12 refusal matrix |
| `tests/PrintFlow.Tests/Architecture/ProductionActivationBoundaryTests.cs` | §7 structural rules for the now-open Production branch |
| `tests/PrintFlow.Tests/Fixtures/ExternalApplicationProbe.cs` | counts running Photoshop/Meitu processes |
| `tests/PrintFlow.Tests/Smoke/ProductionActivationWorkstationSmoke.cs` | §10–§11 live activation smoke, §14 live rollback |
| `docs/printflow/production-activation-runbook.md` | §15 operator runbook |
| `docs/printflow/phase-11500-d-production-activation-final-gate.md` | this report |

**Modified**

| File | Change |
|---|---|
| `src/PrintFlow.App/Composition/ServiceRegistration.cs` | the Production branch composes the accepted adapters; `RegisterAdapters` gained the workspace root, manifest path and expected digest, and takes a nullable mode so an absent section fails closed |
| `appsettings.json` | `Adapters.Mode`: `Fake` → `Production` |
| `tests/…/Startup/ApplicationStartupTests.cs` | the Production-refuses-to-start test became Production-still-starts, plus a new unknown-mode fail-closed test |
| `tests/…/Startup/EnvironmentGateCompositionTests.cs` | shipped-mode assertion → Production; "Production refuses to compose" → "composing Production does not authorise Production" |
| `tests/…/Verification/ProductionWorkstationVerifierTests.cs` | shipped-mode assertion → Production |
| `tests/…/Preset/WorkstationPresetResizeContractEvidenceTests.cs` | shipped-mode assertion → Production |
| `tests/…/Architecture/EnvironmentDiagnosticsBoundaryTests.cs` | §11.9 restated: one adapter decision, in the composition root, failing closed on an unrecognised mode |
| `tests/…/Smoke/VerifiedEnvironmentGateWorkstationSmoke.cs` | derives the expected composed mode from the committed configuration instead of asserting `Fake` |
| `tests/…/Smoke/PhotoshopFinalGateWorkstationSmoke.cs`, `PhotoshopFinalReviewWorkstationSmoke.cs`, `PhotoshopWorkflowOutputWorkstationSmoke.cs`, `WorkstationVerificationSmoke.cs` | the shipped mode is reported rather than asserted; each of these composes its own seam and is independent of it |

**Removed** — nothing. No adapter, verifier, gate, workflow or diagnostics behaviour was
redesigned.

The complete configuration change:

```diff
   "Adapters": {
-    "Mode": "Fake"
+    "Mode": "Production"
   },
```

The `Preset` block — id, version, path, expected digest — is byte-identical. Activation did not
mint a new preset identity to make anything pass.

---

## 3. Production composition architecture

```
appsettings.json
  Adapters.Mode
      │
      ▼
ServiceRegistration.RegisterAdapters(mode, workspaceRoot, manifestPath, expectedDigest)
      │
      ├── "Fake"        → FakeMeituProcessor + FakePhotoshopOutputProcessor
      │
      ├── "Production"  → MeituAutomationComposition.CreateProductionProcessor(…)
      │                        → ProductionMeituProcessor
      │                   PhotoshopAutomationComposition.CreateProductionProcessor(…)
      │                        → ProductionPhotoshopOutputProcessor
      │
      └── anything else → NotSupportedException
```

Four properties, each asserted rather than asserted-in-prose:

**One mode, both processors.** `RegisterAdapters` takes exactly one mode parameter, and both
branches register both ports. The accepted preset describes one production workstation and the
gate asks one question about it, so a configuration that ran a real Photoshop against a fake Meitu
would make that single answer meaningless. There is no arrangement that produces one of each.

**The accepted adapters, not new ones.** Production composes through
`MeituAutomationComposition.CreateProductionProcessor` and
`PhotoshopAutomationComposition.CreateProductionProcessor` — the same factories the controlled
workstation smokes have used since Epics 11300 and 11400. The composition root could have
hand-assembled a locator, a driver and a processor and it would have worked; it would also have
been a second object graph nobody accepted. `There_is_exactly_one_production_adapter_per_port`
enumerates the assembly and fails if a duplicate appears.

**No fallback, in either direction.** The Production branch contains no `Fake`, no `catch` and no
`??`; the Fake branch names no production type or factory. An unknown, empty or absent mode throws
rather than defaulting — including `"production"` and `"PRODUCTION"`, since a case-insensitive
match would be a configuration that no longer means what it says.

**Registration is inert.** Both compositions assemble Win32 locators, input sinks and
preset-backed baseline providers, and every one is lazy: the baseline providers read the signed
manifest on first use, and the evidence sink creates `…\Evidence\` only when it captures.
`Composing_production_reads_no_workstation_file_at_all` proves this the strong way — it points the
preset at a path that does not exist and composes both production adapters anyway. A composition
that never opens the manifest cannot have launched the accepted applications, because the manifest
is where their paths live.

---

## 4. The gate remains the only authorisation seam

Unchanged by this slice, and re-asserted against a graph in which the adapters are now real:

```
SessionService.RunProducingStepAsync
    │  definition.IsAdapterBacked
    ▼
IEnvironmentGate.Verify(AdapterModeFor(work.Adapter))
    │  → VerifiedEnvironmentGate → IProductionWorkstationVerifier
    │
    ├── failure → return, before the automation lock is taken
    └── success → automation lock → attempt row → adapter
```

* `RegisterEnvironmentGate` is untouched. One `IEnvironmentGate`, one `IEnvironmentDiagnostics`,
  the same instance.
* No adapter names the gate, the verifier or the diagnostics seam
  (`EnvironmentDiagnosticsBoundaryTests`, unchanged and green).
* `ServiceRegistration` pre-authorises nothing.
  `Composing_for_production_does_not_authorise_production` builds the real graph with
  `Adapters.Mode = Production` on a workstation that cannot verify, resolves both production
  adapters, and gets `EnvironmentNotVerified` from the gate anyway.
* The gate is consulted **before** the automation lock and before any attempt row, so a refusal
  cannot leave a half-open lock or an orphan row behind.

`AdapterModeFor` reads the mode the composed adapter *declares*, which is the accepted mechanism —
one configuration, two adapters that agree, one question. It is deliberately not on the banned
per-adapter-selector list, and the test that enumerates that list says so.

---

## 5. Gate-before-side-effect proof (§6)

Four tests, at two levels, because they answer different objections.

**Level 1 — the real composed application, observed.**
`A_refused_gate_stops_the_meitu_step_before_anything_external_happens` and its Photoshop
counterpart build the real graph with `Adapters.Mode = Production` on a synthetic layout that
cannot verify, drive the real `SessionService` to an adapter-backed step, and then check
everything that would exist if the adapter had been reached:

| Claim | How it is checked |
|---|---|
| the workflow refuses | `FailureCode.EnvironmentNotVerified` |
| no Photoshop process launched | running-process count unchanged |
| no Meitu process launched | running-process count unchanged |
| no production output file | no `*.tif` anywhere under the workspace |
| no attempt row | aggregate has no attempt for the step |
| no Revision | aggregate has no revision for the operation |
| no automation lock | `GetAutomationLockAsync().IsHeld` is false |
| no evidence capture | `…\Evidence\` still absent |

**Level 2 — the same production adapters over an operating system that only records.**
`A_refused_gate_sends_no_input_and_launches_nothing_through_the_meitu_seams` and
`A_refused_gate_invokes_no_photoshop_action_through_the_photoshop_seams` compose the *real*
`ProductionMeituProcessor` and `ProductionPhotoshopOutputProcessor` over recording seams, so the
remaining §6 claims are counted rather than inferred:

```
locator.LaunchCount        = 0     no application launched
input.Sends                = []    no keyboard or mouse input sent
elements.Invocations       = []    no UI element invoked
controls.Presses / Writes  = []    no control pressed or typed into
evidence.Captures          = []    no screen captured
w1Bridge.Calls             = 0     the canonical Action invoked zero times
tiffBridge.Calls           = 0     no production output file saved
preparationBridge.Calls    = 0     no document resized
adapter.Calls              = 0     the adapter was never even asked
```

The W1 bridge count is the literal form of "no Photoshop Action is invoked", asserted through the
same closed bridge the accepted adapter uses rather than a stand-in that could never have invoked
one.

**The other direction.** `A_verified_workstation_lets_the_workflow_reach_the_production_adapter`
puts a verified synthetic workstation behind the same gate and asserts the adapter *is* entered
(call count 1) and that whatever went wrong afterwards was not the gate.

---

## 6. Operation-time integrity

Untouched by this slice, and deliberately so: activation must not let the environment gate's
verification stand in for the adapters' own checks. Both are still there, and both still run
immediately before the thing they protect.

| Before every… | Verified | Where |
|---|---|---|
| Photoshop run | accepted executable path, product/file version, SHA-256 | `PhotoshopExecutableIdentityRule.Verify`, called from `ProductionPhotoshopOutputProcessor.EnsureReadyAsync` and `GuardedPhotoshopDocumentPreparer` |
| Meitu run | accepted executable path and SHA-256 | `ProductionMeituProcessor.VerifyExecutableIdentity` |
| Action invocation | canonical `.atn` re-hashed against the accepted digest | `GuardedPhotoshopW1Executor.VerifyArtifact`, immediately before the invocation |

Drift refuses before launch, before input and with zero Action invocations. Part C's operation-time
drift tests are unchanged and green. Nothing in Part D substitutes the gate's process-lifetime
cache for any of them.

---

## 7. Restart-bound semantics (§9)

Part C's Model 1 stands. Activation discovered no contradiction, and nothing was added: no file
watcher, no periodic hashing, no second cache, no automatic restart, no silent manifest reload.

The readiness screen still distinguishes the two, and the runbook now says the same thing in
operator language:

```
Check again        → dynamic facts: desktop session, display, UI culture, working folder
Restart PrintFlow  → immutable accepted baseline: workstation record, Photoshop, Meitu, Action file
```

---

## 8. Targeted test results

Run against the activated source.

```
dotnet test --filter ProductionCompositionTests | ProductionGateSideEffectTests
             | ProductionActivationBoundaryTests | EnvironmentGateCompositionTests
             | EnvironmentDiagnosticsBoundaryTests | ProductionWorkstationVerifierTests
             | WorkstationPresetResizeContractEvidenceTests | ApplicationStartupTests
             | ImmutableBaselineTrustModelTests | VerifiedEnvironmentGateTests
             | EnvironmentReadinessScreenTests | ProductionAdapterGateTests

Passed!  -  Failed: 0, Passed: 235, Skipped: 0, Total: 235, Duration: 8 s
```

Covering, by §16's list:

* **Composition** — Fake → Fake/Fake; Production → the two accepted production types by concrete
  type; six unrecognised modes and an absent section fail closed; a refused mode registers no
  adapter at all; Production composition launches nothing, creates no evidence directory and reads
  no workstation file.
* **Gate** — Production + verified reaches the adapter; Production + unverified leaves every
  external side-effect count at zero; advisory-only still authorises; a dynamic failure that is
  repaired reopens Production in the same process without a restart; immutable-baseline restart
  semantics unchanged.
* **Integrity** — every Part C operation-time executable and `.atn` drift test green.
* **UI / boundaries** — diagnostics, bypass-vocabulary and mode-selection structural tests green,
  extended so no shell screen may name a production adapter or a composition factory.
* **Rollback** — Production config → production processors; Fake config → fake processors; a
  running graph keeps the mode it was composed with, so changing mode requires recomposition.

---

## 9. Controlled Production live smoke (§10, §11)

`ProductionActivationWorkstationSmoke`, opt-in behind `PRINTFLOW_PRODUCTION_ACTIVATION_SMOKE=1`.

**Nothing is bypassed.** Earlier epics proved their adapters through a controlled seam with a gate
that said yes, because global Production composition was closed. Here the graph is built by the
real `ServiceRegistration` from the committed `appsettings.json`, the gate is the
`VerifiedEnvironmentGate` resolved out of that graph, and the adapters are whatever
`Adapters.Mode` composed.

**Isolation.** The database is redirected to a throwaway QA file, so no row reaches the operator's
installation. The inputs are synthetic PNGs generated at run time. The workspace root is *not*
redirected and cannot be — the accepted preset names the root this workstation is verified for,
and pointing the installation elsewhere would be a workstation that has not verified. The sessions
therefore live under the real `Sessions\`, each in its own fresh directory.

**Preflight.** The smoke stops and reports itself blocked if Photoshop is holding an unrecognised
document with unsaved changes. It never answers a discard prompt — and neither does the adapter,
whose driver refuses any dialog it did not raise.

### The final run, against the committed configuration

```
=== configuration ===
committed Adapters.Mode : Production
composed  Adapters.Mode : Production
override applied        : no (committed configuration)
configured preset       : printflow-workstation-v1 1.15.0
workspace root          : D:\PrintFlowStudio

=== composition ===
gate                    : VerifiedEnvironmentGate
Meitu adapter           : ProductionMeituProcessor / meitu-xiuxiu-production-v1 / Production
Photoshop adapter       : ProductionPhotoshopOutputProcessor / photoshop-cc2019-production-v1 / Production

=== readiness ===
preset identity         : printflow-workstation-v1 1.15.0 (3392873ED0CA)
[Passed  ] blocking PresetIntegrity      [Passed  ] blocking WorkspaceRoot
[Passed  ] blocking EvidenceIntegrity    [Passed  ] blocking InteractiveSession
[Passed  ] blocking OperatingSystem      [Passed  ] blocking DisplayConfiguration
[Passed  ] blocking MeituExecutable      [Passed  ] blocking UiCulture
[Passed  ] blocking PhotoshopExecutable
[Passed  ] blocking PhotoshopActionArtifact
[Advisory] advisory FilesystemReadOnlyPolicyAdvisory
[Advisory] advisory ExternalApplicationUiLanguage
verified                : True
blocking failures       : 0
advisories              : 2
gate(Production)        : ALLOWED
```

**Job A — GENERATE_PRINT_TIFF, real Photoshop.**

```
synthetic source : PF_11500D_20260902-121945-EFB468BD.png  (1200x800 @ 240 ppi, generated now)
size decision    : max 50.8x100 mm @ 300 ppi     W1 branch : W1_1px
result           : SUCCEEDED
produced         : Sessions/S_20260902T001947Z_66554070/Working/…/…_51mm_CMYK_W.tif
SHA-256          : A1B2A0BF7CE939FB41DAA669D6AD171BC61C11DF7EED3A0AC564C515275F5EAF
bytes / pixels   : 2 452 724 / 600x400
step state       : ReviewRequired
validation       : 600x400 px @ 300x300 dpi; little-endian, compression none,
                   5x8-bit interleaved separated CMYK; spot W1 (photoshop spot True,
                   non-white 240000 px); alpha False, pyramid False, layers 1 all-RLE True;
                   backing unchanged 9B627484613A; save TIFFEncoding.NONE /
                   LayerCompression.RLE / ByteOrder.IBM, as copy True;
                   settled over 3 observations in 2.1 s
```

**Job B — PREPARE_ASSET / Enhancement, real Meitu.** Activation opens Production for *both*
adapters, so proving one live would have left the other enabled on the strength of an earlier
epic's controlled seam.

```
synthetic source : PF_11500D_20260902-121945-EFB468BD_ENHANCE.png  (320x240 @ 300 ppi)
result           : SUCCEEDED
produced         : Sessions/S_20260902T001959Z_44b78c78/Working/…/…_ENH_HD.png
SHA-256          : 5207F744E04267CE1AA68BEAC7C0602CF5FBA62A8D17A2EA9B3139417F643ECC
bytes / pixels   : 1 159 183 / 1280x960
step state       : ReviewRequired
adapter notes    : source Png 320x240 4363 bytes sha256=C2BFBF036791…; format png;
                   settled after 4 observation(s); enhancement auto-started by Meitu and
                   waited out; cleanup — the editor was returned to its signed empty state
```

**The workstation afterwards.**

```
external apps running    : 2 (was 2)     — both already attached from the preceding run
external app launched    : no            — the launch path was exercised on the first run
evidence captures        : 0             — nothing failed, so nothing was captured
sessions before/after    : 23 / 25       — the two this run created, and no others
session directories lost : 0
Comparison files         : 70 (was 70)
Quarantine files         : 2 (was 2)
```

The chain §11 asks for is complete, end to end, through the committed configuration:

```
committed Production configuration
  → real ServiceRegistration
    → real SessionService workflow
      → real VerifiedEnvironmentGate → ALLOWED
        → ProductionPhotoshopOutputProcessor / ProductionMeituProcessor resolved
          → operation ran
            → operation-time executable, version and .atn integrity checks passed
              → validated synthetic output produced, ReviewRequired reached
```

Earlier runs of the same smoke, before the flip, reported `override applied: yes` and are the
pre-activation half of §4: the whole path was proved with the committed file still saying `Fake`.

**No customer artefact was touched.** Every input was generated by the smoke itself, seconds
before use. No existing session directory was read, written or removed; `Comparison\` and
`Quarantine\` are unchanged; nothing was promoted, approved or overwritten. Photoshop and Meitu
were driven only through the accepted adapters, and only for the two synthetic jobs above.

---

## 10. Refusal matrix (§12)

Proved synthetically, against `WorkstationVerificationFixture`'s invented workstation. The live
production workstation was never intentionally broken.

Each case breaks exactly one thing on an otherwise accepted workstation, then drives the real
workflow into an adapter-backed step with the real production adapter behind recording seams.

| Condition | Gate | Adapter asked | External side effects |
|---|---|---|---|
| display mismatch (second monitor) | REFUSED · `EnvironmentNotVerified` | 0 | 0 |
| non-interactive session | REFUSED · `EnvironmentNotVerified` | 0 | 0 |
| secure desktop (screen locked) | REFUSED · `EnvironmentNotVerified` | 0 | 0 |
| wrong Meitu executable digest | REFUSED · `EnvironmentNotVerified` | 0 | 0 |
| wrong Photoshop executable digest | REFUSED · `EnvironmentNotVerified` | 0 | 0 |
| wrong Action digest | REFUSED · `EnvironmentNotVerified` | 0 | 0 |
| wrong workspace root | REFUSED · `EnvironmentNotVerified` | 0 | 0 |
| invalid preset (manifest digest) | REFUSED · `EnvironmentNotVerified` | 0 | 0 |
| invalid evidence chain | REFUSED · `EnvironmentNotVerified` | 0 | 0 |

"External side effects" is the full seam tally from §5: launches, keystrokes, UI invocations,
control presses, control writes, evidence captures, resize calls, Action invocations, save calls.

---

## 11. Rollback proof (§14)

Rollback is a configuration edit and a restart. No migration, in either direction.

**Unit level** — `Rolling_the_mode_back_to_fake_restores_the_fake_pair` composes the same
installation as Production, then as Fake, and asserts the fake pair returns and no external
application was touched. `A_running_graph_keeps_the_mode_it_was_composed_with` asserts the other
half: a second provider built from a different configuration is a second graph, and the first goes
on resolving what it was built with — so changing mode requires recomposition, never a runtime
toggle.

**Live, on the accepted workstation** — `Rolling_the_committed_mode_back_to_fake_restores_the_fake_adapters`,
the real `ServiceRegistration` against the real preset and the real workspace root:

```
=== rollback ===
committed Adapters.Mode : Production
composed  Adapters.Mode : Fake
Meitu adapter           : FakeMeituProcessor / fake-meitu-v1 / Fake
Photoshop adapter       : FakePhotoshopOutputProcessor / fake-photoshop-v1 / Fake
external apps running   : 2 (was 2)      — composing Fake interacts with nothing
sessions before/after   : 25 / 25        — no session created, none removed
Comparison files        : 70 (was 70)
Quarantine files        : 2 (was 2)
```

The committed file is not rewritten by the test. Rollback is a change an operator makes; a test
that edited the shipped configuration to prove it would be doing something considerably more
dangerous than the thing it was checking.

**Procedure** — full text in `docs/printflow/production-activation-runbook.md` §4:

1. Close PrintFlow.
2. In `appsettings.json`, set `"Adapters": { "Mode": "Fake" }`.
3. Save, start PrintFlow.
4. Confirm: PrintFlow starts normally; a Meitu or Photoshop step completes without either
   application appearing.

Only the `Mode` value changes. The `Preset` block is not part of switching modes. Sessions,
revisions, attempts, approved outputs and the database are untouched.

---

## 12. Operator runbook (§15)

`docs/printflow/production-activation-runbook.md` — tracked, and written for the person at the
machine rather than for this report. It covers:

* **What decides whether production runs** — the difference between the adapter mode (a file, a
  restart) and workstation verification (observed, every step). `Production` means PrintFlow *may*
  use the real applications, never that it will.
* **Before starting production** — open Production Readiness; it must show **Ready**; advisories
  may remain and do not block; restart PrintFlow first if the accepted workstation record, the
  Photoshop installation, the Meitu installation or the Action file has been replaced.
* **If production is refused** — do not bypass; read the blocking checks; repair the workstation;
  a table mapping each failing check to **Check again** or **Restart PrintFlow**; the four cases an
  operator actually meets.
* **Rollback** — the four steps above, and how to confirm them.
* **Things the runbook will not ask you to do** — four named temptations, including running a job
  in Fake and treating the result as production output.

There are no operator override instructions, because there is no override.

---

## 13. Structural rules (§7)

| Rule | Where it is asserted |
|---|---|
| exactly one configured mode controls both processors | `One_method_takes_one_mode_and_registers_both_ports`, `Both_processors_follow_the_same_configured_mode` |
| Production uses the already-accepted implementations | `The_production_branch_uses_the_accepted_composition_factories`, `Production_mode_composes_the_accepted_production_pair` |
| no duplicate Production adapter | `There_is_exactly_one_production_adapter_per_port` |
| no fallback to Fake inside the Production branch | `The_production_branch_has_no_fallback` (`Fake`, `catch`, `??`), `The_fake_branch_names_no_production_adapter` |
| no adapter references the gate / verifier / diagnostics | `EnvironmentDiagnosticsBoundaryTests.No_adapter_names_verification_authorisation_or_diagnostics` |
| no View/ViewModel/Navigation names `AdapterExecutionMode`, `Adapters.Mode` or a switch API | `No_shell_screen_can_choose_an_adapter_mode`, `No_shell_screen_names_a_production_adapter` |
| no bypass / override vocabulary ships | `No_product_source_offers_production_activation_vocabulary`, `No_product_source_offers_a_runtime_mode_switch` |
| Readiness screen remains read-only | `The_readiness_screen_offers_only_refresh_and_back` |
| Production cannot be selected from the UI | the two rows above, plus `Only_the_composition_root_names_an_adapter_mode_literal` |
| configuration is the only activation mechanism | `Only_the_composition_root_names_an_adapter_mode_literal` — `"Production"` and `"Fake"` appear in exactly one file |
| no hybrid or per-adapter mode | `No_product_source_offers_a_per_adapter_mode` |

Every source scan skips comment lines, following the convention the earlier boundary tests use: a
comment promising the absence of a behaviour must not read as the behaviour.

---

## 14. QA (§17)

**Build.** `dotnet build PrintFlowStudio.sln` — **0 warnings, 0 errors**
(`TreatWarningsAsErrors`, `WarningLevel 9999`).

**Complete suite.** Run once, against the final source, after the committed activation decision.

```
dotnet test PrintFlowStudio.sln --no-build --no-restore
Passed!  -  Failed: 0, Passed: 10059, Skipped: 0, Total: 10059, Duration: 2 m 5 s
```

Part C finished at 9997; the 62 additional cases are this slice's.

No product source changed after this run.

**Dependencies and security.**

```
dotnet list PrintFlowStudio.sln package --vulnerable --include-transitive
  → no vulnerable packages in any of the five projects

dotnet list PrintFlowStudio.sln package --deprecated
  → PrintFlow.Tests: xunit 2.9.3 — "Legacy", alternative xunit.v3
  → no deprecated package in any product project
```

The xunit notice is a test-only dependency, pre-existing, and a maintenance signal rather than a
vulnerability. It is recorded here rather than acted on: swapping the test framework is not an
activation change, and doing it in the same slice that flips the shipped runtime mode would make
both harder to judge.

No `.csproj`, `Directory.Packages.props`, `packages.lock.json`, `nuget.config` or `global.json`
changed. No package was added, removed or moved.

**Banned-API, secret and configuration scans.** `BannedApiEnforcementTests`, `DependencyRuleTests`,
`ScopeGuardTests`, `LocalisationResourceTests` and the ten boundary suites are part of the complete
run above and are green. `appsettings.json` holds no secret: the preset hash is a public integrity
check. The activation diff adds no credential, path or endpoint.

---

## 15. What was actually touched on the workstation

**External applications.** Photoshop CC 2019 and Meitu XiuXiu, both driven only through the
accepted production adapters, and only for the synthetic jobs in §9. Photoshop was launched by the
first activation run and attached to thereafter; Meitu was launched by the Meitu job. Both were
left in the state the accepted adapters leave them in — Photoshop holding the smoke's own working
document, Meitu's editor returned to its signed empty state.

**Synthetic output generated.** Per activation run: one 1200×800 PNG and one 320×240 PNG under
`D:\PrintFlowStudio\QA\Epic11500D\<token>\`, one production TIFF and one enhanced PNG under their
own new session directories, plus a throwaway SQLite database per run. All retained so the hashes
in §9 can be checked.

**Not touched.** No customer artwork was read, written, opened or overwritten. No existing session
was modified or removed. `Comparison\` and `Quarantine\` file counts are unchanged. No display
setting was changed, no installed binary altered, no accepted baseline file edited, and no dialog
answered on anyone's behalf.

**Not persisted anywhere in this repository.** No machine inventory, no registry dump, no
evidence-chain contents, no screenshot, and no accepted path or digest beyond the preset identity
`appsettings.json` already carried.

---

## 16. Git state

Three local commits on `master`, one per §18 boundary. Parts A–C are untouched: nothing was
amended, rebased or rewritten, and nothing was pushed.

| | |
|---|---|
| `f0080c0` | *(Part C, unchanged)* Report: Epic 11500 Part C production activation readiness and operator diagnostics |
| `5708050` | 11500: let the configured mode compose the production adapters |
| `06298c6` | 11500: run production on the workstation that verified |
| *(this commit)* | Report: Epic 11500 Part D production adapter activation and final live gate |

---

## 17. Definition of done

| | |
|---|---|
| Production configuration composes the accepted Production adapters | ✅ by concrete type |
| Fake configuration still composes Fake adapters | ✅ |
| invalid configuration fails closed | ✅ six unrecognised modes, plus an absent section |
| the environment gate remains the only workflow Production permission seam | ✅ |
| a refused gate produces zero external side effects | ✅ counted at nine seams |
| operation-time executable and Action integrity checks remain intact | ✅ unchanged, green |
| the real accepted workstation passes the Production gate | ✅ 10 blocking passed, 2 advisories |
| one controlled synthetic Production workflow succeeds on the real workstation | ✅ two — Photoshop and Meitu |
| rollback to Fake is explicitly proved | ✅ unit and live |
| operator activation/restart/rollback instructions exist | ✅ tracked runbook |
| final build and complete suite pass | ✅ 0/0, 10059 passed |
| final security checks pass | ✅ no vulnerable package |
| committed `Adapters.Mode` is `Production` | ✅ |

---

**11500-D PASS — PRODUCTION MODE ACTIVATED**
