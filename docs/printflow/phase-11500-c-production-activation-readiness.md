# Epic 11500 Part C — Production Activation Readiness & Operator Diagnostics

**Verdict: 11500-C PASS — PRODUCTION ACTIVATION READY**

Part A established what this workstation is. Part B made that establishment the authority behind
production permission. Part C makes the answer legible to the person standing in front of the
machine, and resolves the one security question Part B left open in writing rather than in code.

`Adapters.Mode` is still `Fake`. `appsettings.json` is not in the change set. No production
adapter was enabled, no external application was launched, and no operator switch was added.

---

## 1. What this slice added

Two things, and deliberately nothing else:

1. **A read-only Production Readiness screen** in the existing WPF shell, reached from Home,
   fed by `IEnvironmentDiagnostics` and by nothing else.
2. **An explicit, documented, tested trust model for immutable baseline drift** (§7 below) — the
   question Part B §9 raised, answered from the actual execution architecture.

## 2. Files

**Added**

| File | What it is |
|---|---|
| `src/PrintFlow.App/ViewModels/EnvironmentReadinessViewModel.cs` | `EnvironmentCheckRow` and the screen's view model |
| `src/PrintFlow.App/Views/EnvironmentReadinessView.xaml` | the screen |
| `src/PrintFlow.App/Views/EnvironmentReadinessView.xaml.cs` | `InitializeComponent`, nothing more |
| `tests/PrintFlow.Tests/Architecture/EnvironmentDiagnosticsBoundaryTests.cs` | §11 structural assertions |
| `tests/PrintFlow.Tests/Integration/Diagnostics/EnvironmentReadinessScreenTests.cs` | §12 diagnostics and refresh |
| `tests/PrintFlow.Tests/Integration/Verification/ImmutableBaselineTrustModelTests.cs` | §7 / §12 trust-model proofs |
| `docs/printflow/phase-11500-c-production-activation-readiness.md` | this report |

**Modified**

| File | Change |
|---|---|
| `src/PrintFlow.App/Composition/ServiceRegistration.cs` | registers `EnvironmentReadinessViewModel` (transient) |
| `src/PrintFlow.App/Navigation/INavigationService.cs` | one destination added: `GoToEnvironmentReadinessAsync` |
| `src/PrintFlow.App/Navigation/NavigationService.cs` | resolves the screen and takes its first reading |
| `src/PrintFlow.App/MainWindow.xaml` | one `DataTemplate` |
| `src/PrintFlow.App/ViewModels/HomeViewModel.cs` | `EnvironmentLabel` and `ShowEnvironmentCommand` |
| `src/PrintFlow.App/Views/HomeView.xaml` | one button |
| `src/PrintFlow.App/Resources/Strings.resx`, `Strings.zh-CN.resx` | 30 keys each |
| `src/PrintFlow.App/Resources/Strings.cs` | 22 typed accessors (18 new keys plus the four Part B keys that had none) |
| `tests/PrintFlow.Tests/Fixtures/HomeScreenHarness.cs` | `RecordingNavigation` gained the new destination |
| `tests/PrintFlow.Tests/Architecture/LocalisationResourceTests.cs` | 4 tests |
| `tests/PrintFlow.Tests/Architecture/WorkstationVerificationBoundaryTests.cs` | doc and assertion message updated to Part C reality |
| `tests/PrintFlow.Tests/Integration/Startup/EnvironmentGateCompositionTests.cs` | 4 tests |
| `tests/PrintFlow.Tests/Integration/Ui/ViewRenderingTests.cs` | 2 render tests |
| `tests/PrintFlow.Tests/Integration/Ui/HomeAndWorkflowSelectionTests.cs` | 1 real-graph test |
| `tests/PrintFlow.Tests/Integration/Automation/PhotoshopFoundationTests.cs` | 1 operation-time drift test |
| `tests/PrintFlow.Tests/Integration/Automation/ProductionMeituProcessorTests.cs` | 1 operation-time drift test |
| `tests/PrintFlow.Tests/Integration/Automation/PhotoshopW1ExecutionTests.cs` | 1 operation-time drift test |
| `tests/PrintFlow.Tests/Smoke/VerifiedEnvironmentGateWorkstationSmoke.cs` | the live readiness-screen proof |

**Removed** — nothing.

No `.csproj`, `Directory.Packages.props`, `packages.lock.json`, `nuget.config`, `global.json` or
`appsettings.json` was touched. No preset, manifest, evidence file, machine snapshot, registry
dump, screenshot, database or generated artefact is included.

## 3. Diagnostics architecture

The authorisation path is untouched:

```text
SessionService / Workflow
    → IEnvironmentGate                  (the only authorisation seam)
        → IProductionWorkstationVerifier
```

The diagnostics path is new, parallel, and observational:

```text
HomeView  → HomeViewModel.ShowEnvironmentCommand
          → INavigationService.GoToEnvironmentReadinessAsync
          → EnvironmentReadinessViewModel
              → IEnvironmentDiagnostics.Read()
                  ↳ the same VerifiedEnvironmentGate object
```

`VerifiedEnvironmentGate` implements both interfaces and is registered as one concrete singleton
that both resolve to (Part B §14, unchanged). That is the whole reason the screen cannot disagree
with the workflow: there is one object in the process that consults the workstation, and the
screen is looking at its answer rather than forming its own.

The view model holds two fields — `IEnvironmentDiagnostics` and `INavigationService` — and no
gate, no verifier, no fact reader and no configuration. Asserted, not intended
(`The_readiness_screen_holds_diagnostics_and_no_authorisation_seam`).

**No second authorisation path.** The shell never names `IEnvironmentGate`, the verifier, or any
verification type; source scans over `ViewModels`, `Views` and `Navigation` enforce it, comment-
aware in the established convention. The composition-root exemption is Part B's and is unchanged.

## 4. UI behaviour

One screen, reached by one button on Home, left by one button back to Home.

It shows:

* overall state — "Workstation verified for production work." / "Production workstation
  verification failed." — derived from `EnvironmentReadinessReport.Verified` alone;
* the advisory summary beside it, never instead of it;
* the accepted preset's id, version and short digest — or, when the preset itself did not verify,
  a sentence saying it states nothing;
* the observation time, in workstation local time;
* the blocking failures, **all of them**, on their own;
* the advisories, separately;
* every check that ran, in evaluation order, each with a localised subject, the stable English
  key beside it for support to quote, its status, its Blocking/Advisory classification, and the
  report's bounded detail.

It has exactly two commands — `RefreshCommand` and `BackToHomeCommand` — asserted by enumerating
the view model's command properties. There is no enable, continue-anyway, ignore, override,
retry-as-production, repair, install, preset selection, evidence repair, display configuration or
adapter switch, on the screen or on the seam it reads.

**Two families of wording, and why.** Part B's `EnvironmentCheck_*` strings are written as the
problem — *"Display configuration does not match the verified workstation."* — because they are
what a refused operator reads. Beside the word "Passed" that sentence says the opposite of what
happened. Part C adds `EnvironmentCheckName_*`: a neutral subject — *"Display configuration"* —
shown on every row whatever the outcome. The failure sentence is shown only on failures and
advisories, which is exactly §6's rule that operator wording comes from the failing check's
`MessageKey`.

**What is not shown**, deliberately: manifest JSON, the evidence chain, registry state, machine
inventories, accepted/observed digest pairs, secrets, customer data, or any workspace path. The
detail on each row is the report's own 200-character-capped English sentence, and a test asserts
every row stays inside that cap and carries no JSON.

**Reading it enabled nothing.** `Reading_readiness_changes_no_adapter` resolves the real graph's
`IMeituProcessor` and `IPhotoshopOutputProcessor` before and after opening and refreshing the
screen: same instances, `Mode` still `Fake`.

## 5. Localisation

30 new keys in each of `Strings.resx` and `Strings.zh-CN.resx`: 12 `EnvironmentCheckName_*` and
18 `Environment_*` screen strings. Both files complete; the existing
`Every_English_string_has_a_zh_CN_translation` and its converse still pass.

New tests:

* `Every_workstation_check_has_a_neutral_subject_in_both_languages` — driven off
  `WorkstationVerificationCheck`, so a check added to the accepted contract cannot reach a listed
  row as a bare enum name in either language.
* `The_readiness_screen_has_wording_in_both_languages`.
* `The_restart_requirement_names_what_it_is_about` — the §8 sentence must name Photoshop, Meitu,
  the action file and the word restart, in both languages. A vague "restart if something changes"
  would be a sentence nobody acts on.
* `The_refresh_wording_distinguishes_re_observing_from_re_reading`.

Runtime resolution is proved too, from the built satellite:
`The_screen_resolves_its_wording_in_zh_CN` asserts nothing on the screen is still a resource key,
and `The_readiness_screen_fits_the_window_with_the_Chinese_resources` renders it at the
1000×700 viewport the operator screens are signed off against, with zero WPF binding errors.

Part B's `No_environment_string_offers_a_way_past_the_gate` scans every key beginning
`Environment` in both languages for override wording; the 30 new keys are inside its scope and
pass.

## 6. Refresh behaviour

`Refresh` calls `IEnvironmentDiagnostics.Read()` afresh, off the UI thread (the first reading of
a run hashes two installations and 27 evidence files). The previous reading is discarded and
replaced, so a repaired check stops being listed rather than accumulating.

There is **no UI-level cache**. Proved by counting at the fact reader:
`Every_refresh_re_observes_the_dynamic_facts` shows three readings produce exactly three times
the first reading's dynamic-read count.

Both §4 journeys are tested end to end in one process, one screen instance, no restart:

```text
second display attached → Not ready → display restored  → Check again → Ready
secure desktop          → Not ready → desktop restored  → Check again → Ready
```

`ObservedAt` is displayed and is never a criterion:
`The_observation_time_follows_the_reading_and_authorises_nothing` advances the clock, breaks the
display, refreshes, and asserts the newer timestamp did not make anything ready.

## 7. Blocking versus advisory presentation

`EnvironmentCheckRow.IsBlocking` is the flag the report carried. `BlockingFailures` and
`Advisories` are the report's own projections. The screen re-derives nothing.

The exact §5 pairing is one test:
`An_advisory_only_workstation_is_ready_with_the_advisory_shown` asserts Ready **and** "Advisories:
present" **and** an empty blocking list, together — split into three smaller tests, a future
change that suppressed Ready on an advisory, or one that swallowed the advisory, would each still
pass two of them.

`Readiness_comes_from_the_report_and_is_not_re_derived_from_the_rows` feeds the screen a report
that is `Verified` while carrying an advisory. A screen that counted rows would report Not Ready;
this one does not, because readiness is the verifier's verdict and not the shell's arithmetic.

`The_screen_names_no_individual_check_when_classifying` scans the shell for every
`WorkstationVerificationCheck` member name. Hard-coding which checks "really" matter would
compile, pass every behavioural test on today's preset, and silently disagree with the gate the
day a check is added.

## 8. Failure wording

Unchanged from Part B. The workflow failure code is the single stable
`FailureCode.EnvironmentNotVerified`; operator wording comes from the failing check's
`MessageKey`; no workstation sub-check became a new workflow failure-code vocabulary. The screen
shows all failed checks — which is the surface where the whole list belongs, because the
workflow's single message would otherwise send an operator to fix the display, restart, and
discover the session was wrong too — and manufactures no parallel authorization semantics.

---

## 9. Immutable baseline drift — the audit

### A. Can a replaced baseline artefact affect a new Production operation?

Audited against the actual production execution lifecycle, artefact by artefact.

**The cache, precisely.** `ProductionWorkstationVerifier` caches two things for the life of the
process: `_rootOfTrust` (manifest + evidence-chain checks, behind a `Lazy<>`) and `_baselineChecks`
(OS identity, both executables, the Action artefact). `PresetPhotoshopBaselineProvider` and
`PresetMeituBaselineProvider` each cache the parsed, hash-verified manifest the same way. Session,
display, culture and workspace availability are cached nowhere.

**1. An executable launched after verification.** The bytes at the accepted path can be replaced
under a running PrintFlow, and the *gate* will not notice until restart. But the gate is not what
stands between those bytes and an operation. `ProductionPhotoshopOutputProcessor.EnsureReadyAsync`
calls `PhotoshopExecutableIdentityRule.Verify` — existence, product version, file version and a
full SHA-256 of the file on disk — at the start of **every** run, before `Launch` and before any
input, and `GenerateAsync` begins with `EnsureReadyAsync`. `ProductionMeituProcessor` does the
same through its own `VerifyExecutableIdentity`. Neither reads a cached digest of the file; both
re-hash it. A replaced executable therefore closes the operation with `PhotoshopNotInstalled` /
`MeituNotInstalled` and `inputSent=false`, whatever the gate still believes.

**2. An already-running executable.** Replacing the file on disk does not change a process already
mapped from it, and no hashing scheme in PrintFlow can. What PrintFlow does check at operation
time is that the process it is driving was started from the accepted path
(`FindProcessesByExecutable`, the launch-path re-identification, and the COM bridges' comparison
of Photoshop's reported `Path` against the accepted directory). The residual case — an attacker
replaces the binary, someone restarts Photoshop by hand, then the original bytes are restored
before PrintFlow's next hash — requires write access to the installation directory plus control
of the operator's timing. It is out of reach of any file-hash design and is recorded here rather
than papered over.

**3. The Photoshop Action artefact consumed after verification.**
`GuardedPhotoshopW1Executor.VerifyArtifact` re-hashes the canonical `.atn` on disk immediately
before every Action invocation, from the accepted contract's digest. A replaced Action file closes
the run with `EnvironmentNotVerified` and `actionInvocationCount=0`. Separately, and already true
from Epic 11400: what actually executes is the Action set loaded inside Photoshop, which the
runtime evidence check compares against the accepted set, action and command transcript. Neither
of those is affected by the gate's cache.

**4. The manifest and evidence, used only by the verifier.** These are the artefacts nothing
outside verification consumes. Caching them for the life of the process is not merely harmless
here — it is the *safe* direction. The cached values are the ones that were hash-verified against
the digest in `appsettings.json`. A manifest rewritten under a running process cannot inject a new
accepted path, digest, display geometry or culture into this run, because nothing re-reads it;
the stale answer is the previously *proved* answer. On restart the rewritten document fails its
hash and Production is refused. `A_manifest_rewritten_under_a_running_process_cannot_widen_what_is_accepted`
pins that direction: after the manifest is corrupted, a wrong display still refuses, because the
values being compared against are still the accepted ones.

### B. Is process-lifetime trust adequate for each artefact?

Yes, and not because "an attacker who can write those files is already privileged" — that
argument is true and insufficient on its own. It is adequate because for every artefact whose
bytes can reach a production operation, a second, uncached, operation-time check already stands in
front of that operation:

| Cached artefact | Gate sees drift | Operation-time re-read | Effect of drift on a production run |
|---|---|---|---|
| Photoshop executable | on restart | every run, full SHA-256 | refused before launch, `inputSent=false` |
| Meitu executable | on restart | every run, full SHA-256 | refused before launch |
| Photoshop Action `.atn` | on restart | before every invocation | refused, zero invocations |
| Workstation manifest | on restart | — (verifier only) | none: cannot widen what is accepted |
| Evidence files | on restart | — (verifier only) | none: cannot widen what is accepted |

The gate's process-lifetime cache is therefore a *reporting* staleness, not an authorisation hole.

### C. The chosen model

**Model 1 — restart-bound immutable baseline — is retained, deliberately.**

Not because it is the incumbent. Model 2's cost is real and its benefit here is nil: re-hashing
two application binaries on every production step would add seconds of I/O per operation, and the
operation it would protect is already protected by an uncached hash of the same bytes taken at the
same moment. A filesystem watcher was explicitly not introduced (Part B §20 stands). Nothing in
the verifier or the gate changed.

What the retained model obliges, and what Part C delivers:

* **the restart requirement is documented** — here, and on the operator's screen;
* **it is exposed to the operator**, in both languages, always visible rather than only after a
  refusal, because an operator who first meets the rule when production is already closed has met
  it too late;
* **the diagnostics wording does not imply a re-hash that did not happen** — "Check again" says in
  so many words which facts it re-reads and that the accepted files are not among them;
* **the restart boundary is unmistakable in tests** (§11 below).

### Restart-required operator wording (§8)

en-US:

> If an accepted PrintFlow workstation record, the Photoshop installation, the Meitu installation
> or the verified action file is replaced or updated, restart PrintFlow before using production
> mode.

zh-CN:

> 如果已认可的 PrintFlow 工作站记录、Photoshop 安装、美图秀秀安装或已校验的动作文件被替换或更新，
> 请先重启 PrintFlow，然后再使用生产模式。

And, separately and adjacently, what Refresh actually does:

> Check again re-reads what can change while PrintFlow runs: the desktop session, the display
> configuration, the Windows display language and the working folder. The accepted files listed
> above are read once per run and are not re-read here.

The two sentences are deliberately distinct. Dynamic-fact refresh and immutable-baseline restart
semantics are different guarantees, and collapsing them is the one misunderstanding that could put
a replaced installation into a production run.

---

## 10. Tests added

### Structural — `EnvironmentDiagnosticsBoundaryTests` (41 cases)

Against §11's list:

1. UI/App consumes `IEnvironmentDiagnostics`, not the verifier ✓ (constructor + field types, plus
   source scans over `ViewModels`, `Views`, `Navigation`)
2. no adapter consumes `IEnvironmentDiagnostics` ✓
3. no adapter consumes `IEnvironmentGate` ✓
4. no adapter consumes `IProductionWorkstationVerifier` ✓
5. exactly one product `IEnvironmentGate` ✓ — and the only `IEnvironmentDiagnostics`
   implementation is that same type ✓
6. diagnostics expose no mutation/bypass API ✓ — one method named `Read`, no parameters, returning
   a report; and no member of `IEnvironmentDiagnostics`, `EnvironmentReadinessReport` or
   `EnvironmentCheckReport` contains Enable / Override / Ignore / Continue / Bypass / Skip /
   Force / Authorise / Authorize; and the screen offers exactly `RefreshCommand` and
   `BackToHomeCommand`
7. no Production enable/override vocabulary ships ✓ — `VerifyAndEnable`, `ContinueAnyway`,
   `RetryAsProduction`, `EnableProduction`, `IgnoreEnvironment`, `OverrideReadiness`,
   `SkipReadiness` across all four `src` projects, on top of Part B's seven
8. `Adapters.Mode` remains `Fake` ✓ (Part B's assertion against the committed file, still green)
9. the Production adapter-registration guard remains fail-closed ✓ — and now proved by behaviour
   as well: `Composing_for_production_still_refuses_rather_than_substituting_a_fake` builds the
   real graph with `Adapters.Mode = "Production"` and asserts `NotSupportedException`
10. no duplicate verification-policy implementation in UI code ✓ — the verification vocabulary is
    absent from the shell, and so is every individual check name

Plus: no shell screen can choose an adapter mode (`AdapterExecutionMode`, `Adapters:Mode`,
`Adapters.Mode` all absent from `ViewModels`, `Views`, `Navigation`).

Every scan skips comment lines, following `WorkstationVerificationBoundaryTests.IsComment` — the
convention Part B §18 established so that documentation promising the absence of a behaviour is
not read as the behaviour.

### Behavioural — `EnvironmentReadinessScreenTests` (20 cases)

Against §12's diagnostics list: verified → Ready ✓; one blocking failure → Not Ready ✓; multiple
blocking failures all visible ✓; advisory-only → Ready + advisory visible ✓; blocking + advisory →
Not Ready with both classifications and no overlap ✓; preset identity displayed ✓ (and the
unverified-preset sentence ✓); observation timestamp displayed ✓; bounded details displayed ✓;
en-US resolves ✓; zh-CN resolves ✓. Plus: a passing check shows its subject and not a failure
sentence; no row reaches the operator as a bare resource key; the only way off the screen is Home.

Against §12's refresh list: display fail → refresh after restore → Ready ✓; session fail → refresh
after restore → Ready ✓; every refresh re-observes (counted) ✓; a repaired check stops being
listed ✓; the timestamp authorises nothing ✓.

Most cases drive the screen against the *real* `VerifiedEnvironmentGate` over
`WorkstationVerificationFixture`'s synthetic workstation — a machine that exists nowhere — so what
the screen shows is what the one authority actually concluded. Two stub-backed cases cover shapes
the accepted preset never produces.

### Immutable-baseline decision — `ImmutableBaselineTrustModelTests` (10 cases)

* `A_replaced_baseline_artefact_is_restart_bound_at_the_gate` — a theory over **all five** cached
  artefacts (manifest, evidence, Meitu binary, Photoshop binary, Action file): the running process
  keeps authorising, a newly constructed graph refuses. Part B proved this for the Photoshop
  binary alone; widened here, because "which artefacts are restart-bound" is precisely what an
  operator has to be told, and a single-artefact proof leaves it open whether the rest were ever
  considered.
* `Dynamic_facts_move_under_a_running_process_and_cached_artefacts_do_not` — both halves in one
  process, so the distinction cannot quietly collapse in either direction.
* `Diagnostics_show_the_cached_conclusion_and_state_the_restart_requirement` — after a replaced
  binary the row still reads Passed, and the restart sentence is on the same screen. This is the
  §7 wording obligation, pinned.
* `A_refresh_re_observes_the_workstation_without_re_reading_the_cached_files` — counted.
* `After_a_restart_the_screen_names_the_replaced_artefact` — the other end of the instruction: an
  operator who follows it must actually see the problem afterwards.
* `A_manifest_rewritten_under_a_running_process_cannot_widen_what_is_accepted`.

### Operation-time defence — beside the adapters that own it

Three tests, each in the suite for the adapter under test, each proving the same shape: a passing
run, then the artefact replaced on disk, then the next run in the **same process** refused.

* `PhotoshopFoundationTests.A_binary_replaced_after_a_passing_run_is_refused_on_the_next_run` —
  `PhotoshopNotInstalled`, `inputSent=false`, `LaunchCount` unchanged.
* `ProductionMeituProcessorTests.A_binary_replaced_after_a_passing_run_is_refused_on_the_next_run` —
  `MeituNotInstalled`, nothing launched, no input sent.
* `PhotoshopW1ExecutionTests.An_atn_replaced_after_a_successful_run_refuses_the_next_run` —
  `EnvironmentNotVerified`, `actionInvocationCount=0`, `ExecuteCount` still 1.

These are the factual basis for §9's conclusion, and they live beside the code they constrain so
that changing that code fails them.

### Composition, rendering and the real graph

* `EnvironmentGateCompositionTests` — the screen resolves; its diagnostics seam is
  reference-equal to the gate; reading readiness changes no adapter; Fake stays usable when
  readiness fails; production composition still throws.
* `ViewRenderingTests` — the screen renders in both states with zero WPF binding errors (a
  mistyped binding path is silent everywhere else), and fits the 1000×700 viewport in zh-CN.
* `HomeAndWorkflowSelectionTests.The_real_graph_reaches_production_readiness_from_home_and_back` —
  real `ApplicationStartup`, real `ServiceRegistration`, real navigation: Home → Readiness →
  refresh → Home, with `IMeituProcessor` the same instance and still `Fake`. A screen nobody can
  reach is not a diagnostics surface.

## 11. Results

**Targeted run** — the readiness screen, the diagnostics boundaries, the trust model, the gate,
composition, verification boundaries, the verifier, localisation, view rendering, Home, the three
adapter suites, and the dependency/scope/banned-API rules:

```
Passed!  -  Failed: 0, Passed: 405, Skipped: 0, Total: 405, Duration: 14 s
```

**Complete suite.** Run once, after the targeted tests, the live proof and a clean build. Justified
under §15: this slice changes the composition root and adds a destination to `INavigationService`,
which every screen depends on — an architecture boundary rather than a local change.

```
dotnet test PrintFlowStudio.sln --no-build --no-restore
Passed!  -  Failed: 0, Passed: 9997, Skipped: 0, Total: 9997, Duration: 2 m 1 s
```

Part B finished at 9911; the 86 additional cases are this slice's.

**Build:** `dotnet build PrintFlowStudio.sln` — **0 warnings, 0 errors** (`TreatWarningsAsErrors`,
`WarningLevel 9999`).

**Dependencies and security.** No `.csproj`, `Directory.Packages.props`, `packages.lock.json`,
`nuget.config` or `global.json` changed. No package was added, removed or moved. Per §15, the Epic
11400 Final QA vulnerability audit was not repeated: there is nothing new to audit.

## 12. Live-read proof (§14)

`VerifiedEnvironmentGateWorkstationSmoke.Read_the_production_readiness_screen_on_this_workstation`,
opt-in behind `PRINTFLOW_WORKSTATION_VERIFY=1`, builds the graph with the **real
`ServiceRegistration`** from the **real committed `appsettings.json`** and the real workspace root,
resolves the **real `EnvironmentReadinessViewModel`** from it, and prints what an operator would
read. The one substitution is a throwaway database, which readiness does not consult.

```
Production readiness
Workstation verified for production work.
Advisories: present
Verified workstation preset: printflow-workstation-v1 1.15.0 (3392873ED0CA)
Checked at: 2026/9/2 11:05

Stopping production work
  Nothing is stopping production work.

Notes (these do not stop production work)
  Read-only marking of accepted files [FilesystemReadOnlyPolicyAdvisory] — Note
    Some accepted files are no longer marked read-only. Their signatures still match, so this
    does not stop production work.
  Meitu and Photoshop interface language [ExternalApplicationUiLanguage] — Note
    The recorded interface language of Meitu or Photoshop, for reference.

Checks
  [Passed  ] Required  Workstation preset integrity   [PresetIntegrity]
  [Passed  ] Required  Accepted workstation records   [EvidenceIntegrity]
  [Note    ] Advisory  Read-only marking of accepted files [FilesystemReadOnlyPolicyAdvisory]
  [Passed  ] Required  Windows version                [OperatingSystem]
  [Passed  ] Required  Meitu installation             [MeituExecutable]
  [Passed  ] Required  Photoshop installation         [PhotoshopExecutable]
  [Passed  ] Required  Photoshop action file          [PhotoshopActionArtifact]
  [Passed  ] Required  Working folder                 [WorkspaceRoot]
  [Passed  ] Required  Desktop session                [InteractiveSession]
  [Passed  ] Required  Display configuration          [DisplayConfiguration]
  [Passed  ] Required  Windows display language       [UiCulture]
  [Note    ] Advisory  Meitu and Photoshop interface language [ExternalApplicationUiLanguage]

after refresh     : Workstation verified for production work.
```

**10 blocking checks passed, 2 advisories, and the screen reads Ready with advisories present** —
the state §14 predicted. The same run asserts the composed application still resolves
`IMeituProcessor.Mode` and `IPhotoshopOutputProcessor.Mode` as `Fake`, and that
`configuration.Adapters.Mode` is `"Fake"`.

The companion gate proof in the same file was re-run alongside it and still reports
`gate(Production) : ALLOWED`, `gate(Fake) : ALLOWED`, `verified : True`.

**No external application was touched.** Nothing in this slice launched Photoshop or Meitu, sent
automation input, ran a production workflow, modified a workstation setting, or dismissed a
dialog. The readiness read is a file-and-Win32 question, which is the whole point of a gate that
answers without bringing an application onto the operator's screen.

## 13. §10 — the retained Photoshop document

At Part C preflight, **neither Photoshop nor Meitu was running**: a process check found no
`Photoshop`, `XiuXiu` or `Meitu` process. The `Elaine Rudolph_A4.tif` document 11500-B left open
was therefore not present to report on, and nothing in this slice closed it — this slice launched,
attached to and terminated nothing. No Photoshop discard dialog was automated, and no Photoshop
lifecycle behaviour was added.

## 14. Git state

Branch `master`, ahead of `origin/master`. No push, no amend, no rebase, no history rewrite.
11500-A (`3e1c320`, `593f5f8`) and 11500-B (`f847c6a`) are intact and untouched.

Part C is three new local commits:

| Commit | Subject |
|---|---|
| `890f6f4` | 11500: let the operator see why production is or is not ready |
| `1423541` | 11500: decide what a replaced baseline file may do to a running process |
| this one | Report: Epic 11500 Part C production activation readiness and operator diagnostics |

## 15. `Adapters.Mode` status

`appsettings.json` is **unmodified** and is not in the change set.

```json
"Preset":   { "Id": "printflow-workstation-v1", "Version": "1.15.0", ... },
"Adapters": { "Mode": "Fake" }
```

`The_shipped_configuration_still_runs_fake_against_the_accepted_preset` asserts both against the
committed file. `RegisterAdapters` still throws for `Mode: "Production"`, and that is now asserted
by composing the real graph and catching it rather than only by reading the source.

## 16. What is still outstanding before Production can be activated

Part C closes the readiness gap. What remains is a decision and its release process, not code:

1. **`ServiceRegistration.RegisterAdapters` still throws for `Mode: "Production"`.** Its message
   names Epic 11400's Photoshop adapter and Epic 11500's gate; both are now complete, and the
   slice that opens the branch should say so in its own report. Failing closed remains right until
   then.
2. **A production activation decision**, with whatever release, training and rollback process the
   shop requires. Nothing in this slice constitutes one.
3. **Operator documentation** should carry the §8 restart requirement alongside the screen that
   states it, so it survives a workstation rebuild.

The immutable-baseline question is **no longer outstanding**: it is decided, documented and tested.

---

## Verdict

**11500-C PASS — PRODUCTION ACTIVATION READY**

- an operator can see why production work is or is not available, on a screen in the shipped shell ✓
- blocking failures and advisories are visually and semantically distinct ✓
- an advisory-only workstation reads Ready with advisories present ✓
- all blocking failures are listed, not only the first ✓
- refreshing re-observes restored dynamic facts without a restart ✓
- the UI can neither authorize nor bypass anything — no enable, override, ignore, continue-anyway,
  retry-as-production, repair or adapter switch exists ✓
- the single verifier → gate authority is intact, and the screen reads the same object ✓
- immutable-baseline drift has an explicit, audited, tested security model: **Model 1,
  restart-bound**, safe because every artefact whose bytes can reach a production operation is
  re-hashed at operation time by the adapter that uses it ✓
- the restart requirement is visible on the screen and localised in en-US and zh-CN ✓
- refresh wording never claims to re-read what it does not ✓
- no production adapter is activated; the Production registration branch still fails closed ✓
- committed `Adapters.Mode` remains `Fake` ✓
- targeted tests pass (405) ✓
- the justified complete suite passes (9997 / 0 failed / 0 skipped) ✓
- build has 0 warnings / 0 errors ✓
- dependencies unchanged ✓
- the controlled live read produces Ready + 2 advisories on the accepted workstation, with the
  application's adapters still resolving as Fake ✓
- no external application was launched, driven or closed ✓

Production mode is not enabled in this slice.
