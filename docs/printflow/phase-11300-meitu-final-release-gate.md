# PrintFlow Studio — Epic 11300 Final QA and Release Gate

**Gate date:** 26 August 2026
**Workstation:** `DESKTOP-0BG8884`, local session 1, zh-CN Windows UI culture
**Repository:** `master` at `466f899`, five local commits ahead of `origin/master` at preflight
**Scope:** final QA of Epic 11300 only; Epic 11400 was not started

## 1. Final architecture

The current implementation boundary is the code at `466f899` plus the Final-QA-only correction to
the stale Production-mode startup diagnostic in `ServiceRegistration.cs`. The correction changes no
adapter registration or gate: `Adapters.Mode` remains `Fake`, Production composition still refuses
to start, and `FoundationEnvironmentGate` still refuses every Production adapter with
`EnvironmentNotVerified` until Epic 11500 supplies authoritative workstation verification.

The completed Epic 11300 slices and the latest rule carried by each are:

| Slice | Current accepted rule |
|---|---|
| 11300-A | Only Infrastructure owns guarded Meitu UI automation; process, window, foreground, signed evidence, and the global lock are mandatory. |
| 11300-B1 | A managed Working reference may be opened through the exact signed picker route; B1's inability to prove document identity was fail-closed. |
| 11300-B1.1 | B1 is superseded for identity by the signed Save-surface probe: expected basename plus `_副本`, followed by signed Cancel, is mandatory. |
| 11300-B2A | Enhancement requires the structural `AI变清晰` target, current-load Busy correlation, positive completion, and exact identity. |
| 11300-B2B | Enhancement exports only through the controlled Save/`另存为`/`#32770` route and produces validated `{Name}_HD.png`. |
| 11300-C1 | Background Removal is the exact `抠图` route; `AI换背景` is rejected; current-load Busy and positive completion are mandatory. |
| 11300-C2A | CUTOUT export requires exact dimensions, transparent pixels, visible foreground pixels, source integrity, and `{Name}_CUTOUT.png`. |
| 11300-C2B1 | Automatic selection requires explicit reviewed-content authority bound to exact RevisionId and SHA-256; Workflow owns the decision and Revision. |
| 11300-C2B2 | The operator UI exposes undecided, confirmation, authorised, and producing-attempt audit states without implying a quality guarantee. |
| 11300-D1 | Runtime failures and restarts fail closed, create no fabricated Revision, preserve retry lineage, and recover locks only for a provably dead owner. |
| 11300-D2A | Stop is phase-specific; Busy Cancel is exact, signed, operation-correlated, and invoked at most once. Take Over sends zero further Meitu input and requires explicit re-entry. |
| 11300-D2B | PrintFlow never force-terminates Meitu. Manual Meitu/Windows closure is operator-owned recovery. |

The full suite passed 8,242 tests and the focused compiled architecture suite passed 165 tests.
Those compiled tests are the primary proof that:

- Domain and Workflow reference no UI Automation assembly, App/Workflow cannot reach the UI
  automation implementation directly, and UI-automation types are confined to Infrastructure;
- ViewModels contain no `System.IO` or direct `File`, `Directory`, or `Path` calls;
- guarded seams expose no arbitrary path, coordinate, raw shortcut, free-form action, or generic
  desktop macro surface;
- Infrastructure does not create workflow Revisions (the sole `new Revision` hit in Infrastructure
  is SQLite hydration in `Mappers.cs`);
- `SessionService` remains request and Attempt authority, exact-hash approval remains Workflow-owned,
  and one persisted global automation lock remains authoritative;
- no coordinate/mouse fallback or blind production keyboard shortcut exists; and
- no force-termination capability exists in compiled product code or source.

An explicit source scan of `src` found none of `Process.Kill`, `TerminateProcess`,
`NtTerminateProcess`, `ZwTerminateProcess`, `TerminateJobObject`, `taskkill`, `Stop-Process`,
`CloseMainWindow`, `WM_CLOSE`, or `SC_CLOSE`.

## 2. Accepted preset and evidence

`appsettings.json` points to
`Baseline\workstation-v1\preset\printflow-workstation-v1.8.0.json`, version `1.8.0`, with expected
SHA-256 `DE76464F011A54F80704BB6C32A2E0D00EFF9AB24834FF7D05EF8E9CF3DB60E4`.
The exact manifest bytes recompute to that digest. The manifest is read-only.

All 18 `sourceManifestIntegrity` paths exist and all 18 exact SHA-256 values match, including the
current repository design document and the D2A `editor-busy-cancel.json` evidence. Preset/sign-off
pairs v1.0.0 through v1.5.0 remain read-only and every sign-off's recorded manifest length and hash
matches the current file. The unsigned `v1.5.0-candidate` remains distinct. No v1.6.0 or v1.7.0
manifest or sign-off exists; numeric contiguity is not required.

**Blocking finding:** no `workstation-preset-v1.8.0.json` sign-off exists anywhere under
`D:\PrintFlowStudio`. The sign-off directory stops at v1.5.0. Consequently Final QA cannot verify a
v1.8.0 sign-off or establish a signing instant against which to prove that accepted evidence was
not edited after signing. Final QA did not fabricate a sign-off or create a new preset.

## 3. Executable identity

The accepted executable exists at
`C:\Users\admin\AppData\Local\MeituApp\XiuXiu\7.8.7.5\XiuXiu.exe`.

| Check | Actual |
|---|---|
| File version | `7.8.7.5` |
| Product version | `7.8.7.5` |
| SHA-256 | `D65C6D82323275361EA0ADFBB3F6A5C0D2A5CF4CF63EA3AF1A7DDD4544B037B1` |
| Process UI culture | `zh-CN` |
| User/session | `DESKTOP-0BG8884\admin`, session 1 |

Path, version, and hash match v1.8.0 exactly. No Meitu process was running at the identity check.
Process/window ownership, exact title, handle-reuse, foreground, modal, and ambiguity rules remain
covered by the green production-foundation and guarded-driver suites. No drift was accepted.

## 4. Document identity

The production route remains:

`Working reference → signed Open path → loaded editor → signed Save identity surface → expected
basename + "_副本" → signed Cancel → exact document identity`.

Current tests cover expected A/observed A, expected A/observed B, stale A after B, empty editor,
wrong-owner Save surface, duplicate and missing filename controls, foreground loss, process exit, and
target loss immediately before Save. The Save surface is still cancelled even on an identity
mismatch when and only when its exact owner and signature remain valid. No “some document is open”
shortcut has reappeared.

## 5. Enhancement

The current Enhancement chain requires exact document identity, structural `AI变清晰` targeting,
retained-module/auto-start correlation, Busy, positive completion, identity re-probe, controlled
export, file settling/reading, PNG validation, unchanged source, and only then `AdapterOutput`.

`folderEdit` is recorded only as a disproved route and is never written by production code. `保存`
is not authorised as the controlled destination route. The complete destination is written and read
back in the owned `#32770` Save As dialog. `保存成功` identifies only the signed result surface; it
does not prove location or file success. A completion panel without current-load correlation, or a
stale retained module that never produces current-load Busy, produces no export.

## 6. Background Removal

The current chain is:

`exact CurrentArtefact review → explicit UseAutomaticSelectionForReviewedContent → exact
RevisionId+SHA-256 authority → attempt snapshot → 抠图 → current-load Busy → positive completion →
exact identity → controlled {Name}_CUTOUT.png export → exact dimensions → transparent pixels +
visible foreground pixels → unchanged source → Revision → ReviewRequired`.

`AI换背景` remains rejected as Background Removal. `Unspecified` is the default decision and is
refused before Attempt creation or adapter input; there is no implicit automatic-selection default.

## 7. Reviewed-content authority

The authority/invalidation suite verifies:

- authority A plus current A is usable;
- authority A plus current B is historical but unusable;
- mutated bytes for A fail with `RevisionIntegrityMismatch` before an Attempt or adapter call;
- a retry over byte-identical unchanged upstream content retains usable authority;
- changed upstream content requires a new decision; and
- valid authority survives restart while stale authority remains historical and unusable.

Each Background Removal attempt snapshots the authority it actually ran under. A later session
authority does not rewrite an earlier result's producing-attempt audit line.

## 8. Export and file validation

Exact names remain `{Name}_HD.png` and `{Name}_CUTOUT.png`. The producing Attempt directory supplies
uniqueness. Input and output must be distinct managed Working references and siblings in that
Attempt directory. Existing destinations fail closed without overwrite.

Enhancement requires a stable readable PNG no smaller than its source. CUTOUT additionally requires
exact canvas dimensions, at least one alpha value below 255, and at least one alpha value above 0.
Opaque, fully transparent, wrong-sized, corrupt, wrong-format, zero-byte, missing, unstable, and
source-mutating outputs are refused. Source, InputSnapshot, upstream Working content, prior Attempt
output, and Approved/Rejected artefacts are not overwritten.

## 9. Attempt and Revision invariants

For Enhancement and Background Removal, `AttemptStarted` is persisted before external processing.
Only a validated output may lead to `AttemptSucceeded`, exactly one workflow-created Revision,
exact `OutputRevisionId`, and `ReviewRequired`. Failure, cancellation, interruption, target loss,
timeout, corrupt/missing output, or restart recovery creates no usable Revision.

Retry creates a new Attempt with `RetryOfAttemptId`, a new independent Attempt directory, and an
unchanged historical Attempt. Committed successful Revisions are idempotent across restart and do
not cause a duplicate Revision or Meitu rerun.

## 10. Stop

All eight `ExternalOperationPhase` values remain exhaustively represented by the Workflow-owned
policy. During Busy, only the exact signed `LoadingMaskWidget.cancel` correlated to the exact
operation may be invoked, exactly once. The file picker's Cancel cannot match. Missing, ambiguous,
disabled, unsigned, wrong-ancestry, wrong-operation, wrong-process, modal, target-lost, or foreground-
lost cases produce no cancel input.

“Cancel invoked” is stored separately from “Meitu positively left Busy”. If Busy does not
positively end, retained state remains `OperationMayStillBeRunning`, operator action is required,
and no second cancel invocation occurs.

## 11. Take Over and re-entry

Take Over resolves to zero further Meitu input from every supported phase. A running Attempt becomes
Cancelled, the step Interrupted, the session HandedOff, and the automation lock is released. Restart
keeps the session HandedOff and performs no automatic resume or manual-output adoption.

`ReenterAutomation` creates no Attempt. It returns the session to Active/Waiting; the next explicit
Run creates a new Attempt and leaves the handed-off Attempt immutable.

## 12. Force-termination policy

The final product policy remains: **PrintFlow never force-terminates Meitu.** Stop failure, timeout,
Unknown/modal state, Take Over, and a Cancel invocation that leaves Busy do not terminate or close
Meitu. Manual Meitu/Windows closure after handoff is operator-owned. The compiled policy suite and
explicit source scan both pass.

## 13. Runtime failure and recovery

D1 coverage remains green for process exit, window loss/handle reuse, foreground refusal,
Busy-start timeout, Busy-completion timeout, Unknown state, blocking modal, missing/unstable/corrupt
output, cancellation, and Running-at-restart. Failure context is persisted and no failure creates a
Revision.

Startup recovery changes a crashed Running Attempt to Interrupted, never reconstructs a partial
file as success, and is idempotent when run twice. A stale lock is recovered only when its owner is
provably dead; live or unverifiable ownership is never stolen.

## 14. Review integrity

The UI and service regression path “Revision shown in preview → bytes changed on disk → Approve”
returns `RevisionIntegrityMismatch`. `SetBackgroundRemovalDecision` uses the same integrity guard
before it records authority. No authority or approval can cover bytes different from those reviewed.

## 15. Localisation and Revision short ids

The focused localisation/UI-rendering group passed 75 tests. en-US and zh-CN resource sets have
full parity, no empty values, and every typed accessor resolves to a real resource. Tests cover
Background Removal authority, Stop, Take Over, confirmation consequences, HandedOff, re-entry,
manual-close guidance, timeout/target-lost/operator-action-required messages, no quality guarantee,
and no wording that offers or implies force termination.

The UI's one `ShortRevision` formatter uses the trailing eight hexadecimal characters of the UUIDv7.
Artefact metadata, Background Removal authority status, and producing-attempt review audit all call
that formatter and are covered by UI tests. This is a compact operator label, not a global
collision-proof identifier.

## 16. Human WPF visual inspection

**Not completed.** Codex launched the real Debug WPF executable in Fake mode and confirmed one
targetable window titled `PrintFlow Studio`. The Windows capture helper then failed twice with
`SetIsBorderRequired failed: 不支持此接口 (0x80004002)`. Codex closed only that QA-launched PrintFlow
window normally. No Meitu or other operator application was closed.

No human/operator looked at the required A–I states in en-US and zh-CN during this gate. Automated
1000×700 rendering/binding tests remain green, but this report does not substitute those tests, an AI
observer, or screenshots for the explicitly required human inspection. Natural wording, clipping,
overlap, button distinction, short-id legibility, and zh-CN wrapping therefore remain unsigned Final
QA items.

## 17. Controlled live Meitu regression

The prior slice evidence remains internally consistent and was not rewritten:

- B2B previously ran a synthetic Enhancement through exact identity, Busy, controlled
  `{Name}_HD.png` export, three settling observations, PNG validation, and byte-identical source;
- C2A previously ran a reviewed synthetic Background Removal through current-load Busy to a
  480×360 CUTOUT containing 149,305 transparent and 55,473 visible pixels, with unchanged source;
- D2A previously ran supervised synthetic Enhancement and Background Removal Stops, each invoking
  the exact signed Cancel once and positively leaving Busy, plus a Take Over with zero further
  PrintFlow input while Meitu remained running.

Those are accepted historical slice observations, not a Final-QA rerun. The requested Final-QA live
Enhancement, Background Removal, Stop, and Take Over sequence was not run because the brief requires
the human WPF inspection to pass first, and it did not occur. No customer artwork, dangerous failure
injection, force kill, unknown modal interaction, or Task Manager automation was used.

## 18. Repository and security hygiene

Preflight began clean at `master...origin/master [ahead 5]`; the exact five local commits are the
accepted C2B2, D1, D1 report, D2A, and D2B checkpoints. `git diff cf60717..HEAD` establishes the
Epic 11300 boundary from the Epic 11200 final report through all current source, tests, migrations,
resources, and twelve slice reports.

A tracked-file scan found no synthetic image, enhanced/cutout output, runtime SQLite database, UI
dump, smoke transcript, screenshot, or external `D:\PrintFlowStudio` evidence artefact. Source/test
scans found no credential/private-key pattern and no embedded `C:\Users\admin` or workstation-name
literal. The machine-local workspace/preset root remains only in controlled configuration and
evidence documentation. `git diff --check` reports no whitespace error.

Final-QA changes are intentionally uncommitted: this report and the correction of one stale
Production-mode startup diagnostic. Nothing was pushed, staged, amended, or history-rewritten.

## 19. Reports and supersession

Historical reports remain intact:

- B1's `NOT READY` discovery is superseded by B1.1's signed Save-identity route, not erased;
- B2A's earlier close-prompt understanding is corrected by B2B's later live observation: the prompt
  did not appear after successful export, but remains an unautomated blocking modal if it does appear;
- signed v1.4.0 and v1.4.1 were safely superseded by v1.4.2 after live exercise found an icon-font
  control-name transcription error and then malformed evidence JSON; neither history nor old files
  were rewritten;
- v1.5.0 is followed directly by v1.8.0; no v1.6.0/v1.7.0 file exists and none is required;
- C2B2, D2A, and D2B honestly recorded no human visual inspection; this Final QA did not close that
  note; and
- force termination was considered and rejected by explicit policy.

The source-level stale diagnostic saying C2B authority was unimplemented was corrected during this
gate. The normal Production route remains closed for the production Photoshop work owned by Epic
11400 and workstation verification owned by Epic 11500.

## 20. Automated gates, unresolved notes, handoff, and Git state

The .NET 10 SDK is installed per-user at
`C:\Users\admin\AppData\Local\Microsoft\dotnet\dotnet.exe`. Plain `dotnet` initially resolved to the
machine-wide .NET 8 muxer and could not satisfy `global.json`; the documented per-user muxer reports
SDK `10.0.400` and was used without changing `global.json`, PATH, or the workstation.

Final gate results on the final source tree:

| Gate | Result |
|---|---|
| `dotnet restore --locked-mode` | passed; all five lock files honoured |
| `dotnet build --no-restore` | passed; 0 warnings, 0 errors |
| `dotnet test --no-build --no-restore` | 8,242 passed, 0 failed, 0 skipped |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages in all five projects |

Blocking unresolved items:

1. obtain and independently verify the missing v1.8.0 final preset sign-off without editing the
   accepted v1.8.0 manifest or evidence in place;
2. have a named human/operator inspect states A–I at at least 1000×700 in en-US and zh-CN and record
   what was actually observed; and
3. only after that visual gate passes, rerun the minimum safe synthetic controlled-seam Enhancement,
   Background Removal, Stop (if practical), and Take Over regressions, recording their current
   retained/cleanup state.

Epic 11400 must not start from this gate until those blockers are closed. The eventual handoff should
carry the controlled Meitu capability, the immutable signed evidence chain, the exact authority and
Revision invariants, fail-closed Stop/Take Over semantics, the never-force-terminate policy, and the
still-authoritative Epic 11500 Production environment gate.

EPIC 11300 NOT READY

---

## Final Gate Closure Run R2 — 26 August 2026

### R2 policy clarification

The original run treated a missing v1.8.0 sign-off as a blocker. Project policy was subsequently
clarified: a separate sign-off is not required. The accepted evidence authority is the immutable
manifest plus exact evidence and executable hashes. No sign-off was created. No v1.9.0 or other
preset version was created.

**Separate preset sign-off: NOT REQUIRED BY PROJECT POLICY.** It is not missing release evidence
and is not an R2 blocker.

### R2 evidence re-verification

Before the WPF exercise and again after it:

| Check | R2 actual | Result |
|---|---|---|
| Configured preset version | `1.8.0` | PASS |
| Configured and computed manifest SHA-256 | `DE76464F011A54F80704BB6C32A2E0D00EFF9AB24834FF7D05EF8E9CF3DB60E4` | PASS |
| `sourceManifestIntegrity` | 18 entries present; 18 exact hashes matched | PASS |
| Meitu executable | `C:\Users\admin\AppData\Local\MeituApp\XiuXiu\7.8.7.5\XiuXiu.exe` | PASS |
| Meitu file/product version | `7.8.7.5` | PASS |
| Meitu executable SHA-256 | `D65C6D82323275361EA0ADFBB3F6A5C0D2A5CF4CF63EA3AF1A7DDD4544B037B1` | PASS |

No accepted baseline or evidence changed.

### R2 preflight

The per-user .NET 10 muxer was used. Actual preflight results were:

| Gate | Result |
|---|---|
| `dotnet restore --locked-mode` | passed; all five projects restored under locked mode |
| `dotnet build` | passed; 0 warnings, 0 errors |
| `dotnet test` | 8,242 passed, 0 failed, 0 skipped |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages in all five projects |

The Final-QA-only `ServiceRegistration` diagnostic correction remained unchanged. It changes no
registration or gate. `Adapters.Mode` remained `Fake`; global Production was never enabled.

### R2 human WPF visual inspection

Codex launched the real Debug WPF application, kept `Adapters.Mode = Fake`, and maximised the
inspection window to the accepted 1920×1040 work area for the inspected states. The operator
physically inspected the visible application; accessibility text was used only to navigate the real
window and was not treated as a substitute for human judgement.

| Locale | State | R2 human result |
|---|---|---|
| zh-CN | A. Background Removal undecided | PASS |
| zh-CN | B. Automatic Selection confirmation open | PASS |
| zh-CN | C. Background Removal authorised | PASS |
| zh-CN | D. CUTOUT ReviewRequired with producing-attempt authority audit line | NOT INSPECTED — blocked before the state was produced |
| zh-CN | E. Stop available during an active external-style operation | NOT INSPECTED — blocked by the D preparation failure |
| zh-CN | F. Take Over confirmation | NOT INSPECTED — blocked by the D preparation failure |
| zh-CN | G. HandedOff/manual-recovery state | NOT INSPECTED — blocked by the D preparation failure |
| zh-CN | H. Return to automation | NOT INSPECTED — blocked by the D preparation failure |
| zh-CN | I. unresponsive/manual-close guidance | NOT INSPECTED — blocked by the D preparation failure |
| en-US | A–I | NOT INSPECTED — the R2 visual run stopped at the functional blocker |

For the three PASS observations the operator judged the visible state against the requested wording,
clipping, overlap, control clarity, consequence, short-id, wrapping, force-close and quality-guarantee
criteria. The authorised zh-CN state displayed readable Revision short id `d0defaaf` in the fresh
fixture (the earlier first presentation displayed `02996021`). No presentation defect was reported
and no wording, margin, wrapping, width, spacing or alignment fix was made.

### R2 functional blocker discovered while preparing state D

A fresh Fake-mode visual fixture was created from the known synthetic source
`D:\PrintFlowStudio\Sessions\S_20260821T054743Z_e75dbae8\Source\opaque-rgb.png`. The operator
selected the `准备设计素材` workflow; Original Confirmation passed, Enhancement was skipped, and
automatic Background Removal was explicitly authorised for the exact reviewed Revision. Invoking
the Fake Background Removal step terminated the real WPF process before CUTOUT ReviewRequired could
be shown.

Windows Application log evidence at 14:43:01 local records `.NET Runtime` event 1026 and
`Application Error` event 1000. The process ended because of an unhandled:

`System.FormatException: Input string was not in a correct format. Failure to parse near offset 1. Expected an ASCII digit.`

The stack reaches:

`OutputFileNaming.BuildProposedFileName` → `SessionService.PerformStepWorkAsync` →
`SessionService.RunProducingStepAsync` → `SessionViewModel.RunAsync`.

The accepted manifest supplies token patterns such as `{Name}_CUTOUT.png`; the current provider
passes that exact accepted value into `NamingPatternSet`, while `OutputFileNaming` calls
`string.Format` and its documented/test fixtures use positional patterns such as
`{0}_CUTOUT.png`. The current green tests exercise positional synthetic preset values and did not
catch the accepted-manifest/runtime contract mismatch.

On the single recovery relaunch, PrintFlow reported one interrupted processing attempt, one released
stale lock and one quarantined file. The quarantined partial output is
`D:\PrintFlowStudio\Quarantine\20260826T024336Z_opaque-rgb.png`, with the reason:
`Startup recovery: leftover from Interrupted attempt 01a03bf3-13d9-7d2e-81c4-780e1784fbb8, referenced by no Revision.`
No CUTOUT Revision was created. No Meitu input occurred because this was the Fake adapter route.

This is a product/runtime naming-contract defect, not one of the brief's allowed small visual
presentation defects. R2 therefore did not change product behaviour, edit the immutable manifest,
or redesign automation. The defect remains an acceptance blocker.

### R2 post-visual and controlled live regressions

The human visual gate did not pass A–I in both locales. In accordance with the ordered gate, the
post-visual controlled Meitu regressions were not started:

| Required regression | R2 result |
|---|---|
| Enhancement | NOT RUN — visual gate incomplete |
| Background Removal | NOT RUN — visual gate incomplete |
| Stop | NOT RUN — visual gate incomplete |
| Take Over | NOT RUN — visual gate incomplete |

Historical slice observations remain historical evidence only and were not presented as R2 reruns.
No customer image, process termination, Meitu force-close, global Production mode or Epic 11400 work
was used.

### R2 final retained external state

| Question | R2 retained state |
|---|---|
| Is Meitu running? | Yes; PID 30712, responding, session 1, accepted executable path |
| Is a document loaded? | Not positively re-probed; the sole minimized main window remains titled `美图秀秀-图片编辑` |
| Is any modal open? | No Meitu modal was positively identified; minimized state prevented an accessibility re-probe without disturbing the operator's window |
| Positively identified Meitu state | accepted process/path/version remains running and responding; one minimized editor-title window remains |
| Synthetic workspaces retained? | Yes: fresh session `D:\PrintFlowStudio\Sessions\S_20260826T023959Z_a06dab27` and the quarantined partial/reason files remain |
| May they safely be deleted? | Not as part of this gate. Meitu did not receive the synthetic file, but PrintFlow persistence still references the session; retain until the interrupted session is deliberately abandoned/cleaned by an authorised follow-up |

The relaunched PrintFlow application remained at Home in Fake mode with no PrintFlow file-picker
modal open. No backing file referenced by Meitu was deleted.

### R2 final automated and repository gate

After the live WPF failure, the unchanged source tree produced:

| Gate | R2 final result |
|---|---|
| `git diff --check` | passed; only the existing line-ending warning was printed |
| `dotnet restore --locked-mode` | passed |
| `dotnet build` | passed; 0 warnings, 0 errors |
| `dotnet test` | 8,242 passed, 0 failed, 0 skipped |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages in all five projects |

No synthetic image, HD/CUTOUT output, runtime database, smoke transcript, UI dump, screenshot or
external evidence file was added to Git. The test source helper `SyntheticImages.cs` is code, not a
tracked synthetic image artefact. `appsettings.json` still says `Adapters.Mode = Fake` and
`FoundationEnvironmentGate` remains the registered authoritative environment gate. No push, amend
or history rewrite occurred.

R2 closure is blocked by the unhandled accepted-manifest naming-pattern mismatch and the consequent
incomplete human visual gate. Post-visual controlled Meitu regressions remain correctly unrun.

EPIC 11300 NOT READY
