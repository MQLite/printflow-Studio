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

## Final Gate Closure Run R3 — 26 August 2026

R3 is a closure-only run. It did not start Epic 11400, did not enable global Production mode, and
did not modify the accepted v1.8.0 manifest. The original Final Gate NOT READY (§16) and the R2
NOT READY naming-contract discovery are preserved above exactly as written; nothing in this section
rewrites or softens the R2 `FormatException` finding.

### R3.1 Preflight

Working tree clean, `master` ahead of `origin/master` by 8 commits. No push, amend, rebase or
history rewrite occurred at any point in R3.

| Gate | R3 preflight result |
|---|---|
| `git status -sb` | `## master...origin/master [ahead 8]`, clean tree |
| `dotnet restore --locked-mode` | passed, all five projects |
| `dotnet build` | passed; **0 warnings, 0 errors** |
| `dotnet test` | **8,298 passed, 0 failed, 0 skipped** |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages in all five projects |

This matches the reported baseline exactly.

### R3.2 Naming blocker fix references and reverification

| Commit | Subject |
|---|---|
| `6f82780` | 11300: render accepted named-token naming patterns |
| `7f8d557` | Report: Epic 11300 final gate naming contract fix |

The accepted production contract is unchanged and remains the named-token form:

| Manifest property | Accepted value |
|---|---|
| `enhancedPattern` | `{Name}_HD.png` |
| `cutoutPattern` | `{Name}_CUTOUT.png` |
| `productionTiffPattern` | `{Name}_{SizeMm}mm_CMYK_W.tif` |
| `collisionPattern` | `_{Sequence:00}` |

These four values were read directly out of the accepted manifest at
`D:\PrintFlowStudio\Baseline\workstation-v1\preset\printflow-workstation-v1.8.0.json` during R3 and
match `AcceptedNamingContract` character for character.

`OutputFileNaming` and `NamingPatternRenderer` no longer pass those values to positional
`string.Format`. The only remaining occurrences of the string `string.Format` under
`src/PrintFlow.Domain/Files/` and `src/PrintFlow.Domain/Outputs/` are documentation comments that
explain why it must not be used. `OutputFileNaming.BuildProposedFileName` dispatches every pattern
through `NamingPatternRenderer.Render` with an explicitly supplied, closed token vocabulary.

The focused naming/preset/contract regression group passed **151 tests**, covering `{Name}`,
`{SizeMm}`, `{Sequence}` and `{Sequence:00}`, malformed-brace rejection, separator/drive/traversal
rejection, and the real v1.8.0 preset route. No naming behaviour was modified in R3.

### R3.3 Evidence re-verification

| Item | R3 result |
|---|---|
| Configured preset | `printflow-workstation-v1`, version `1.8.0`, status `ACCEPTED_IMMUTABLE` |
| Manifest SHA-256 | `DE76464F011A54F80704BB6C32A2E0D00EFF9AB24834FF7D05EF8E9CF3DB60E4` — **matches** |
| `sourceManifestIntegrity` | **18/18 hashes match**, 0 drift |
| Meitu path | `C:\Users\admin\AppData\Local\MeituApp\XiuXiu\7.8.7.5\XiuXiu.exe` — present |
| Meitu version | `7.8.7.5` — matches |
| Meitu executable SHA-256 | `D65C6D82323275361EA0ADFBB3F6A5C0D2A5CF4CF63EA3AF1A7DDD4544B037B1` — **matches** |

The manifest file remains read-only and was opened read-only. No separate preset sign-off record was
created; project policy does not require one.

### R3.4 Retained R2 zh-CN A–C

The naming-contract fix touched no XAML, no localisation resource and no layout. zh-CN A–C were
re-observed in passing during the R3 fixture run and their rendering did not visibly differ from R2,
so under the brief they were not re-inspected and their R2 PASS results carry forward:

| Locale | State | Result | Source |
|---|---|---|---|
| zh-CN | A. Background Removal undecided | PASS | R2, carried forward |
| zh-CN | B. Automatic Selection confirmation open | PASS | R2, carried forward |
| zh-CN | C. Background Removal authorised | PASS | R2, carried forward |

### R3.5 zh-CN D–I human results

The real Debug WPF application was launched with `Adapters.Mode = Fake` and the inspection window
placed at the accepted 1920×1040 work area. A fresh synthetic source was created for the run —
`D:\PrintFlowStudio\QA\R3\R3-VISUAL-SYNTHETIC.png`, 480×360, SHA-256
`145F14F2A9F46BCD1545C3288BE43A13A6874321F1117C159A27DCADDD425135`. No customer artwork was used.
The operator physically inspected the visible application; accessibility text was used only to
navigate and to record what was on screen, never as a substitute for human judgement.

| Locale | State | R3 human result |
|---|---|---|
| zh-CN | D. CUTOUT ReviewRequired with producing-attempt authority audit line | **PASS** |
| zh-CN | E. Stop available during an active external-style operation | **NOT INSPECTABLE — unreachable in Fake mode** |
| zh-CN | F. Take Over confirmation | **NOT INSPECTABLE — unreachable in Fake mode** |
| zh-CN | G. HandedOff / manual-recovery state | **PASS** |
| zh-CN | H. Return to automation | **NOT INSPECTABLE — unreachable in Fake mode** |
| zh-CN | I. unresponsive / manual-close guidance | **NOT INSPECTABLE — unreachable in Fake mode** |

**State D was produced successfully.** The Fake Background Removal path that terminated the WPF
process in R2 ran to completion and reached CUTOUT `待审核` without an unhandled exception. This is
the functional confirmation of the naming fix in the real WPF flow. The state displayed the
producing-attempt authority audit line `去除背景：自动选择 — 已针对已审图片版本 eb20d84f 授权`, output
`R3-VISUAL-SYNTHETIC_CUTOUT.png`, 480×360 preserved, and Revision short id `cfd20290`.

### R3.6 en-US A–I human results

`CultureInfo.CurrentUICulture` on this workstation resolves to `zh-Hans-CN` and the application has
no in-application locale switch, so en-US was obtained by launching an unmodified copy of the same
Debug output with the `zh-CN` satellite resource folder absent, which falls back to the neutral
(en-US) resources embedded in the main assembly. No product code, repository file, OS language
setting or accepted workstation `uiCulture` was changed to do this. A second fresh synthetic source
was used — `R3-VISUAL-SYNTHETIC-ENUS.png`, 480×360, SHA-256
`3BD46346C5412E2CB049FB99CD8004FB5AF0CE69E0ABC3C7D927F86DE167775C`.

| Locale | State | R3 human result |
|---|---|---|
| en-US | A. Background Removal undecided | **PASS** |
| en-US | B. Automatic Selection confirmation open | **PASS** |
| en-US | C. Background Removal authorised | **PASS** |
| en-US | D. CUTOUT ReviewRequired with producing-attempt authority audit | **PASS** |
| en-US | E. Stop available | **NOT INSPECTABLE — unreachable in Fake mode** |
| en-US | F. Take Over confirmation | **NOT INSPECTABLE — unreachable in Fake mode** |
| en-US | G. HandedOff / manual-recovery | **PASS** |
| en-US | H. Return to automation | **NOT INSPECTABLE — unreachable in Fake mode** |
| en-US | I. unresponsive / manual-close guidance | **NOT INSPECTABLE — unreachable in Fake mode** |

en-US state D produced `R3-VISUAL-SYNTHETIC-ENUS_CUTOUT.png` with authority audit line
`Background removal: Automatic Selection — authorised for reviewed Revision 67d68c3a` and Revision
short id `7caeb73f`.

One fidelity limitation is recorded rather than hidden: this launch changes only *UI resource*
resolution. `CultureInfo.CurrentCulture` remains `zh-CN`, so en-US number and date formatting was
not exercised. Every en-US judgement above therefore covers wording, clipping, overlap, control
distinction, consequence, short-id legibility and wrapping, but not locale-specific numeric or date
formatting.

### R3.7 Why E, F, H and I are not reachable in Fake mode

This is a structural finding about the gate itself, not an operator or tooling failure. The four
uninspected states are each gated on a genuinely running external-application automation:

| State | Gate | Source |
|---|---|---|
| E. Stop offered | `CanStopAutomation => State == AutomationRuntimeState.Running` | `AutomationRuntime.cs` line 77 |
| F. Take Over offered | `CanTakeOverAutomation => State == Running && DrivesExternalApplication` | `AutomationRuntime.cs` lines 89–90 |
| H. Re-enter automation | `RequiresAutomationReentry => State == HandedOff && AvailableCommands.Contains(ReenterAutomation)`, populated from `LastAutomationStop` | `SessionView.cs` lines 271–272 |
| I. Retained external state / manual-close guidance | `HasRetainedExternalState => LastAutomationStop?.OperatorActionMayBeRequired == true` | `SessionView.cs` line 265, `AutomationControl.cs` line 337 |

The shipped Fake adapter always runs `FakeAdapterScenario.Succeed`. `FakeMeituProcessor.SetScenario`
is a test-only seam, and `AdaptersConfiguration` exposes only `Mode`, so there is no supported
configuration by which a Fake operation can be made to occupy an interruptible Busy window. This was
also confirmed empirically: the Fake Background Removal was sampled every 250 ms from the instant
`执行步骤` was invoked and had already reached CUTOUT review at the first sample, so no Busy window
was ever observable. `LastAutomationStop` is populated only by a real automation Stop, which is why
the HandedOff state reached in R3 correctly offered no re-entry control.

Producing E, F, H and I therefore requires live external automation through the controlled
production seam. §9 of the closure brief defers all live Meitu work until after the human visual
gate passes, and the human visual gate cannot pass without E, F, H and I. **The brief is circular on
this point and needs a gate-level decision before Epic 11300 can close.** R3 did not resolve that
circularity by itself, did not enable global Production, and did not touch live Meitu.

### R3.8 Visual corrections

None. No wording, spacing, alignment, width, wrapping or margin defect was reported by the operator
in any of the seven states inspected across the two locales. No XAML, localisation resource or
layout file was modified in R3.

### R3.9 New defect discovered during R3 — unhandled cancellation in workspace import

While preparing the zh-CN fixture, the WPF process terminated a second time, with a **different**
unhandled exception from the R2 one. Windows Application log at 15:46:03 records `.NET Runtime`
event 1026 and `Application Error` event 1000:

`System.Threading.Tasks.TaskCanceledException: A task was canceled.`
at `PrintFlow.Infrastructure.Workspace.FileWorkspace.ImportSourceAsync` (`FileWorkspace.cs:101`).

Cause: two file pickers were open concurrently because the QA harness invoked the `ChooseFile`
command a second time through UI Automation while the first picker's modal had already disabled the
main window. The operator selected a file in both. The second command execution cancelled the first
execution's `CancellationToken`, and `ImportSourceAsync` catches only `IOException` and
`UnauthorizedAccessException` around `input.CopyToAsync(output, cancellationToken)`. The resulting
`OperationCanceledException` escaped unhandled and terminated the shell.

Scope, stated honestly in both directions:

- The **trigger** was an automation artefact. A mouse-driven operator cannot open a second picker,
  because `ShowDialog()` disables the owner window. A subsequent single, clean import through the
  same path succeeded and produced no crash.
- The **defect** is nonetheless real and is a deviation from this codebase's own established
  convention. `FakeAdapterExecution`, `FakeBackgroundRemovalPng`, `ProductionMeituProcessor`,
  `DeterministicAlphaTrimProcessor`, `WicImagePreviewDecoder`, `WicManualCropProcessor`,
  `WicMeituTransparencyInspector`, `RecycleBin` and `SessionService` all catch
  `OperationCanceledException` and convert it into a structured failure. `ImportSourceAsync` is the
  outlier, and any cancellation of an in-flight import terminates the WPF process rather than
  surfacing a `WorkspaceError` through the ordinary failure surface.

This is the same failure *family* as the R2 blocker — a producing-path exception escaping the view
model and killing the shell — and it was not fixed in R3, because §8 of the closure brief permits
only small presentation corrections during the visual phase. It is recorded here as an open defect
for a follow-up fix pass, on the same footing R2 gave the `FormatException`.

### R3.10 Post-visual automated gate

The human visual gate did not pass A–I in both locales, so this is recorded as the R3 final
automated gate rather than as a post-visual-PASS gate:

| Gate | R3 final result |
|---|---|
| `dotnet restore --locked-mode` | passed |
| `dotnet build` | passed; **0 warnings, 0 errors** |
| `dotnet test` | **8,298 passed, 0 failed, 0 skipped** |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages in all five projects |

### R3.11–14 Controlled live Meitu regressions

In accordance with the ordered gate in §9, and because the human visual gate is incomplete, none of
the controlled live regressions were started:

| Required regression | R3 result |
|---|---|
| Controlled Enhancement Final-QA | NOT RUN — visual gate incomplete |
| Controlled Background Removal Final-QA | NOT RUN — visual gate incomplete |
| Controlled Stop | NOT RUN — visual gate incomplete |
| Controlled Take Over | NOT RUN — visual gate incomplete |

No customer artwork, no global Production mode, no process termination, no Meitu force-close and no
Epic 11400 work was used or started. Historical D2A/B2B/C2A slice observations remain historical
evidence and are not presented as R3 reruns.

### R3.15 Final Meitu retained state

| Question | R3 retained state |
|---|---|
| Is Meitu running? | **No** |
| Accepted PID/path if running | n/a — not running at R3 close |
| Is a document loaded? | Unknown — no process remains to probe |
| Is any modal open? | Unknown — no process remains to probe |
| Positively identified editor state | Not positively identified at close |
| Retained synthetic workspace | Yes — see R3.16 |
| May it safely be deleted? | Not as part of this gate |

Recorded plainly: Meitu **was** running at R3 preflight as PID 30712 from the accepted path with the
accepted version and hash, and is **not** running at R3 close. PrintFlow performed no Meitu
interaction whatsoever during R3 — every operation ran through the Fake adapter — so this transition
was not caused by PrintFlow. No force termination occurred, and no force-termination capability
exists in the product to have caused it. R3 did not perform extra navigation to reach a prettier
final state, and `KnownEditorEmpty` was therefore not pursued.

### R3.16 R2 interrupted fixture and R3 fixtures

The R2 interrupted-session and quarantine files were not deleted. They remain outside Git:

- `D:\PrintFlowStudio\Quarantine\20260826T024336Z_opaque-rgb.png`
- `D:\PrintFlowStudio\Quarantine\20260826T024336Z_opaque-rgb.png.reason.txt`
- session `S_20260826T023959Z_a06dab27`

They do not block PASS: no active Meitu process references them, no Revision claims them, they are
outside Git, and their retained reason is documented here and in the R2 section. Retained reason:
they are the historical record of the R2 `FormatException` discovery.

R3 additionally left four sessions, also outside Git and retained as QA artefacts:
`S_20260826T034558Z_3ecd10fb` and `S_20260826T034603Z_e45d642a` (the two concurrent imports from the
R3.9 cancellation crash), `S_20260826T044623Z_f3e8dbff` (zh-CN visual fixture) and
`S_20260826T050246Z_c77b2824` (en-US visual fixture), plus the two synthetic sources under
`D:\PrintFlowStudio\QA\R3\`.

### R3.17 Evidence re-verification after the R3 UI work

Re-verified after all R3 application work completed:

| Item | Result |
|---|---|
| v1.8.0 manifest SHA-256 | `DE76464F…3DB60E4` — **unchanged** |
| `sourceManifestIntegrity` | **18/18 unchanged**, 0 drift |
| Meitu path / version / SHA-256 | **unchanged** |
| Preset versions on disk | v1.0.0 … v1.5.0, v1.8.0 — **no v1.9.0 created** |

### R3.18 Naming-contract proof from the R3 runs

Both produced files follow the accepted named-token contract exactly, with no positional syntax:

| Locale | Produced file | Bytes | SHA-256 (first 16) |
|---|---|---|---|
| zh-CN | `R3-VISUAL-SYNTHETIC_CUTOUT.png` | 2,533 | `04D7D41E5E954502` |
| en-US | `R3-VISUAL-SYNTHETIC-ENUS_CUTOUT.png` | 2,610 | `FAA0A88FB9FC91C5` |

Both match `{Name}_CUTOUT.png` against their session's established output name, and the SHA-256
prefixes displayed on screen (`04D7D41E5E95`, `FAA0A88FB9FC`) agree with the files on disk. Both
synthetic sources were byte-identical before and after their runs. A scan of the accepted runtime
configuration — the v1.8.0 manifest and the committed `appsettings.json` — found **no positional
`{0}`-style token anywhere**. Tests remain green for `{Name}`, `{SizeMm}`, `{Sequence}` and
`{Sequence:00}`.

This is Fake-adapter evidence produced through the real WPF flow and the real naming renderer. It is
not, and is not presented as, live-Meitu evidence; the live `<Name>_HD.png` and `<Name>_CUTOUT.png`
proofs required by §11 and §12 remain outstanding with those regressions.

No Photoshop automation was exercised.

### R3.19 Repository and security gate

| Gate | R3 result |
|---|---|
| `git status -sb` | clean tree, `ahead 8` |
| `git diff --check` | clean |
| Tracked synthetic source / HD / CUTOUT / runtime DB / transcript / UI dump / screenshot / external evidence | **none** |
| `Adapters.Mode` | `Fake` |
| `FoundationEnvironmentGate` | unchanged — last touched at `3aabb04`, untouched by every Epic 11300 commit |
| Force-termination capability | **absent** — no `Process.Kill`, `TerminateProcess`, `NtTerminateProcess`, `ZwTerminateProcess`, `TerminateJobObject` or `taskkill` in any production assembly; the only occurrences are the boundary tests that forbid them |

The two R3 synthetic sources and both CUTOUT outputs live under `D:\PrintFlowStudio\`, outside the
repository, and nothing from this run was added to Git. No commit, push, amend or history rewrite
was performed in R3.

### R3.20 Remaining notes and Epic 11400 handoff

Outstanding before Epic 11300 can close:

1. **A gate-level decision on the R3.7 circularity.** E, F, H and I cannot be produced in Fake mode,
   and §9 defers the live automation that would produce them until after the visual gate passes.
   Either the brief permits the controlled production seam to render those four states for
   inspection, or the visual gate's scope is amended to reflect what Fake mode can show.
2. **The `ImportSourceAsync` cancellation defect** (R3.9) — an unhandled
   `OperationCanceledException` terminates the shell instead of returning a structured
   `WorkspaceError`, contrary to the convention every other async production path follows.
3. **The four controlled live Meitu regressions** (R3.11–14) remain unrun, including the live
   `<Name>_HD.png` and `<Name>_CUTOUT.png` naming proofs.
4. **en-US numeric and date formatting** was not exercised (R3.6).

Epic 11400 was not started and no Epic 11400 work is claimed by this report.

### R3.21 Git state

`master`, ahead of `origin/master` by 9 commits after this report commit. R3 changed no product
source file: its single commit adds this report section only. No push, no amend, no history
rewrite.

R3 fixed nothing and hid nothing: the R2 naming defect is confirmed fixed and live-proved in the
real WPF flow, five states passed human inspection in en-US and two more in zh-CN, and a second
shell-terminating defect plus a structural gap in the visual gate itself were found and recorded.

EPIC 11300 NOT READY

## Final Gate Closure Run R4 — 27 August 2026

R4 is the final closure run requested after both Final Gate blockers were corrected. It preserves
the original NOT READY finding and the complete R2/R3 discovery history above. R4 added no feature,
did not start Epic 11400, did not enable global Production mode, and did not add force termination.

### R4.1 Blocker-fix commits and history

Both blocker corrections are present as ordinary commits in intact history:

| Blocker | Product correction | Report commit | R4 result |
|---|---|---|---|
| R2 accepted named-token patterns reached positional `string.Format` and threw `FormatException` | `6f82780` — `11300: render accepted named-token naming patterns` | `7f8d557` | PASS |
| R3 workspace import cancellation escaped and terminated the shell | `d40e823` — `11300: return workspace import cancellation as a result` | `8f029b7` | PASS |

No commit was amended, squashed or rewritten. Nothing was pushed.

### R4.2 Preflight

Preflight began on `master` at `8f029b7`, 11 local commits ahead of `origin/master`. The working
tree was clean. `git log --oneline --decorate -15` showed the R2 fix, the R3 fix, both reports and
the prior Final Gate history in normal order. The per-user .NET 10.0.400 SDK was used because the
system muxer did not expose the repository's required SDK.

| Check | R4 preflight result |
|---|---|
| `git status -sb` | clean known tree; `master...origin/master [ahead 11]` |
| `dotnet build` | PASS — **0 warnings, 0 errors** |
| Complete suite at preflight | deliberately not run; reserved for R4.18 |
| Package vulnerability audit at preflight | deliberately not repeated; reserved for R4.18 |

### R4.3 Focused blocker-fix regressions

One focused filter covered `AcceptedNamingContractTests`,
`NamingContractWorkflowRegressionTests`, `NamingContractCrashRegressionTests`,
`NamingContractBoundaryTests`, `WorkspaceImportCancellationTests`, and
`ImportCancellationShellBoundaryTests`: **21 passed, 0 failed, 0 skipped**.

This proved the accepted v1.8.0 patterns and the real Fake workflow still produce:

- `{Name}_HD.png`;
- `{Name}_CUTOUT.png` and CUTOUT `ReviewRequired`;
- `{Name}_{SizeMm}mm_CMYK_W.tif`; and
- `_{Sequence:00}`.

The producing paths use `NamingPatternRenderer`; no positional `string.Format` rendering path is
used. Import cancellation returns structured `Cancelled`, does not escape through the shell
boundary, and does not leave a partial file valid as Source.

### R4.4 Evidence gate before live work

| Item | R4 result |
|---|---|
| Configured preset | `printflow-workstation-v1`, version `1.8.0` |
| Manifest SHA-256 | `DE76464F011A54F80704BB6C32A2E0D00EFF9AB24834FF7D05EF8E9CF3DB60E4` — exact |
| Manifest attribute | read-only |
| `sourceManifestIntegrity` | **18/18 exact**, 0 missing, 0 mismatch |
| Meitu executable | `C:\Users\admin\AppData\Local\MeituApp\XiuXiu\7.8.7.5\XiuXiu.exe` |
| File/product version | `7.8.7.5` / `7.8.7.5` |
| Executable SHA-256 | `D65C6D82323275361EA0ADFBB3F6A5C0D2A5CF4CF63EA3AF1A7DDD4544B037B1` |

No separate preset sign-off was created or required. Project policy explicitly does not require
one for this closure run.

### R4.5 Gate-order clarification and controlled seam

R4 accepts the closure brief's clarification: E/F/H/I intrinsically require a genuinely running
external operation and may be rendered through the already-authorised controlled Production seam
before the complete visual gate closes. The shipped application configuration remained
`Adapters.Mode = Fake` throughout. `FoundationEnvironmentGate` was not weakened or changed.

The visual fixture was an external QA-only WPF harness under the user's temporary directory,
outside Git. It composed the real application shell and service path, replaced only the Meitu
processor and environment gate inside that harness process, and used fresh synthetic files. It did
not modify product source or an accepted runtime configuration.

### R4.6 Carried-forward visual states

Neither blocker fix changed XAML, localisation resources or layout. As authorised by the R4 brief,
the previously accepted states carry forward:

| Locale | Carried states | Result |
|---|---|---|
| zh-CN | A, B, C, D, G | PASS |
| en-US | A, B, C, D, G | PASS |

G also remained naturally visible and acceptable in the R4 handed-off screens.

### R4.7 zh-CN E/F/H/I human inspection

Fresh synthetic Enhancement attempts were driven only until the real external automation was
positively Busy. The real PrintFlow WPF window was then presented to the operator.

| State | Human result |
|---|---|
| E. Stop available while external automation is running | **PASS** |
| F. Take Over confirmation | **PASS** |
| H. Return to automation | **PASS** |
| I. retained-external-state / manual-close guidance | **PASS** |

The operator explicitly returned `zh-CN E/F PASS` and `zh-CN H/I PASS`. Wording was natural, no
clipping or overlap was reported, Stop and Take Over were visibly distinct, consequences were
understandable, and no wording implied force-close.

The technical Take Over run `zh7` reached Busy, consumed the operator Take Over signal, completed
with `HandedOff=True`, `Reenter=True`, and `Retained=True`, and displayed the Chinese operator-owned
guidance. Persistence recorded the Enhancement attempt as `CANCELLED` with `FailureCode=Cancelled`,
no output Revision id, and only the imported source Revision. Explicit re-entry was then accepted
through the ordinary `ISessionService` command path: `HandedOff -> Active`, with re-entry no longer
required. The exact retained `R4-ZH7-TAKEOVER.png` document was subsequently closed through the
signed route and Meitu reached its signed empty-editor state.

### R4.8 en-US E/F/H/I human inspection

The en-US E/F run `en1` was a separate fresh synthetic Enhancement. Its real Busy window and Take
Over confirmation were physically inspected before the operation completed naturally. The fresh
`en2` attempt then performed Take Over and presented the HandedOff state for H/I.

| State | Human result |
|---|---|
| E. Stop available while external automation is running | **PASS** |
| F. Take Over confirmation | **PASS** |
| H. Return to automation | **PASS** |
| I. retained-external-state / manual-close guidance | **PASS** |

The operator explicitly returned `en-US E/F PASS` and `en-US H/I PASS`. The `en2` status recorded
`HandedOff=True`, `Reenter=True`, `Retained=True` and the English notice that PrintFlow stopped the
attempt, sent Meitu nothing further, and created no Revision. Its database confirms a cancelled
Enhancement attempt with no output Revision and only the imported source Revision. Explicit
re-entry was accepted through the ordinary service path, after which the exact retained
`R4-EN2-TAKEOVER.png` document was closed and the signed empty-editor state was confirmed.

Final human visual status is therefore:

- **zh-CN A–I = PASS**; and
- **en-US A–I = PASS**.

### R4.9 en-US culture limitation

The accepted limitation remains a NOTE, not a blocker: en-US neutral UI resources were used while
`CurrentCulture` remained `zh-CN`. The inspection validates English wording, layout, wrapping,
control meaning and action distinction. It does not claim en-US numeric or date localisation
coverage.

### R4.10 Visual corrections

None. No wording, spacing, width, wrapping, alignment or margin defect was reported. No XAML,
resource or product source file changed in R4.

### R4.11 Controlled Enhancement final regression

The first launch was safely refused before input because an unrelated desktop window owned the
foreground. It produced no output or Revision. After the accepted Meitu editor was positively
restored to foreground, one fresh synthetic Enhancement completed through the Production adapter
seam:

| Fact | Result |
|---|---|
| Working source | `PF_BACKGROUND_C1_6E2989AC9DFA.png` |
| Controlled relative output | `Sessions/S_SMOKE/Working/A_1/PF_BACKGROUND_C1_6E2989AC9DFA_HD.png` |
| Source dimensions | `320x240` |
| Output dimensions | `1280x960` |
| Output bytes | `1,159,183` |
| Output SHA-256 | `5207F744E04267CE1AA68BEAC7C0602CF5FBA62A8D17A2EA9B3139417F643ECC` |
| Settling | 3 stable observations |
| Source before SHA-256 | `C2BFBF036791E041BAA05E229992D84816BD5F3D9DD76997CEB386187FC20E65` |
| Source after SHA-256 | same — unchanged |
| Result | validated PNG `AdapterOutput` |
| Cleanup | successful Enhancement returned the editor to signed empty; the built-in negative follow-up's exact retained document was then closed through the signed route |

The exact actual filename proves `{Name}_HD.png` on the real Meitu path. No session, persistence
repository or workflow engine was composed by this seam smoke, so it created no Revision.

### R4.12 Controlled Background Removal final regression

A separate fresh opaque RGB synthetic source was used with explicit
`UseAutomaticSelectionForReviewedContent` authority:

| Fact | Result |
|---|---|
| Working source | `PF_BACKGROUND_C2A_714FF565A5E4.png` |
| Controlled relative output | `Sessions/S_SMOKE/Working/A_1/PF_BACKGROUND_C2A_714FF565A5E4_CUTOUT.png` |
| Source/output dimensions | `480x360` / `480x360` — exact preservation |
| Source pixel format | `Bgr24` |
| Output pixel format | `Bgra32`, `HasAlpha=True` |
| Transparent pixels | `149,305 / 172,800` — present |
| Visible pixels | `55,473 / 172,800` — present |
| Output bytes | `4,788` |
| Output SHA-256 | `E9E83B1890D8CBF5963C0D5561845DD8C209F72555908D055AC41A0104ABFD99` |
| Source SHA-256 | `9C601AD12C9AE4DFF1682B63DC47E15F2150A3B42BFE171909CAAF6009EBF6D6` before and after |
| Settling | 3 stable observations |
| Cleanup | editor returned to signed empty state |

The result was a validated PNG `AdapterOutput`; no Revision was reachable from the seam smoke. The
exact actual filename proves the R2 blocker pattern `{Name}_CUTOUT.png` works through real Meitu.
No cutout-quality claim is made.

### R4.13 Controlled Stop regression

A fresh synthetic Enhancement established real Busy before the ordinary production stop registry
accepted `StopOperation`.

| Required behavior | R4 result |
|---|---|
| Exact signed control | `Button/QPushButton`, automation id containing `MaskDialog.MaskCenterWidget.LoadingMaskWidget.cancel`, exact name `取消` |
| Invocation count | **exactly one** |
| Left Busy positively | **yes** |
| Outcome | structured `Cancelled` |
| Export / AdapterOutput / Revision | none |
| Process termination | none; `forceTerminationInvoked=false` |

The post-cancel screen was conservatively classified `Unknown`, while the operation-correlated
evidence positively proved it left Busy. The exact retained synthetic document
`PF_BACKGROUND_C1_F7D8B0161F79.png` was then identified and closed through the signed route; the
editor reached its signed empty state. No duplicate Background Removal Stop was run.

### R4.14 Take Over evidence

The visual Take Over runs satisfy the Final-QA regression and were reused rather than duplicated:

- a real external Enhancement was positively Busy;
- Take Over was requested through the real runtime stop channel;
- the Take Over policy permitted **zero further PrintFlow Meitu input**;
- each attempt closed as Cancelled and the session became HandedOff;
- Meitu and the synthetic document were left operator-owned;
- no output was adopted and no output Revision was created; and
- explicit re-entry was required and later accepted through the ordinary workflow command path.

No separate duplicate Take Over smoke was run.

### R4.15 Actual HD/CUTOUT naming proof

| Artifact | Actual filename | Contract | Result |
|---|---|---|---|
| Enhancement | `PF_BACKGROUND_C1_6E2989AC9DFA_HD.png` | `{Name}_HD.png` | PASS |
| Background Removal | `PF_BACKGROUND_C2A_714FF565A5E4_CUTOUT.png` | `{Name}_CUTOUT.png` | PASS |

Both were produced by the real Meitu adapter path, not Fake. The accepted runtime JSON contains no
positional `{0}` naming syntax; `{Name}`, `{SizeMm}`, `{Sequence}` and `{Sequence:00}` remain the
named-token authority.

### R4.16 Final external Meitu state

| Question | R4 final state |
|---|---|
| Is Meitu running? | **Yes** |
| PID / path | `28736` / `C:\Users\admin\AppData\Local\MeituApp\XiuXiu\7.8.7.5\XiuXiu.exe` |
| Document loaded? | **No** — signed empty-editor state confirmed after the final exact-document close |
| Modal open? | **No** — current accessibility tree contains no `MainWindow.MaskDialog` |
| Positively identified state | `KnownEditorEmpty` |
| Process killed or force-closed? | **No** |

R4 synthetic workspaces remain outside Git under `D:\PrintFlowStudio\QA\R4\Visual` and temporary
`PrintFlowMeitu*` roots. The active editor references none of them, so the R4 visual, successful
Enhancement and Stop workspaces may safely be removed later. They were intentionally retained for
the report rather than turning Final QA into housekeeping. The successful Background Removal smoke
cleaned its controlled root. Temporary text transcripts are also outside Git and may be removed.
The historical R2/R3 artefacts remain retained for the reasons already recorded above and do not
block closure.

### R4.17 Evidence re-verification after live work

After every live operation and exact-document cleanup:

| Item | Result |
|---|---|
| v1.8.0 manifest SHA-256 | `DE76464F011A54F80704BB6C32A2E0D00EFF9AB24834FF7D05EF8E9CF3DB60E4` — unchanged |
| Manifest read-only | yes |
| `sourceManifestIntegrity` | **18/18 unchanged**, 0 drift |
| Meitu path/version/SHA-256 | unchanged and exact |
| v1.9.0 preset | **none created** |

### R4.18 Single authoritative complete automated gate

No product source changed during R4, so the complete suite was run exactly once, near the final
gate as required:

| Gate | Authoritative R4 result |
|---|---|
| `dotnet restore PrintFlowStudio.sln --locked-mode` | PASS |
| `dotnet build PrintFlowStudio.sln --no-restore` | PASS — **0 warnings, 0 errors** |
| `dotnet test PrintFlowStudio.sln --no-build --no-restore` | **8,307 passed, 0 failed, 0 skipped** in 58.4842 s |
| `dotnet list PrintFlowStudio.sln package --vulnerable --include-transitive --no-restore` | no vulnerable direct or transitive packages in all five projects |
| `git diff --check` | clean |

The initial mistyped solution name `PrintFlow.sln` was rejected before restore because that file
does not exist; the required locked restore immediately followed against the correct
`PrintFlowStudio.sln` and passed. It did not create a second suite run.

### R4.19 Repository and security hygiene

| Gate | R4 result |
|---|---|
| Tracked raster/source/output artefacts | none; 0 tracked PNG/JPEG/TIFF files |
| Tracked runtime DB / WAL / SHM | none |
| Tracked smoke transcripts / UI dumps / screenshots | none |
| Tracked external baseline/evidence files | none |
| `Adapters.Mode` | `Fake` in `appsettings.json` |
| `FoundationEnvironmentGate` | unchanged; last product change remains `3aabb04`, and Production still returns `EnvironmentNotVerified` |
| Force termination | absent from product source and forbidden by the green compiled boundary tests |
| Positional `{0}` in accepted runtime JSON | none |
| Working tree before report edit | clean |

`tests/PrintFlow.Tests/Fixtures/SyntheticImages.cs` is tracked test source code, not a synthetic
image artefact. All customer-like files and outputs used in R4 were synthetic and outside Git. No
customer artwork was used.

### R4.20 Remaining notes

There is no unresolved Epic 11300 blocker. The following non-blocking notes are retained plainly:

1. en-US numeric/date localisation was not exercised because `CurrentCulture` remained `zh-CN`;
   English resources, wording and layout did pass human inspection.
2. One Enhancement pre-attempt failed closed on foreground ownership before any input; the fresh
   retry against the positively foregrounded accepted Meitu window passed completely.
3. R2/R3 and R4 synthetic workspaces remain outside Git as documented QA evidence and are not
   claimed by active Meitu documents.

### R4.21 Epic 11400 handoff

Epic 11300's named-token blocker, import-cancellation blocker, visual A–I matrix, controlled
Enhancement, controlled Background Removal, Stop, Take Over, evidence, complete suite, dependency
and repository gates are closed. Epic 11400 may begin in a separate task. No Epic 11400 work was
started or claimed here.

### R4.22 Git state and verdict

R4 changed no product source. The only repository change in this closure run is this appended R4
report section. It is committed with an ordinary commit on `master`; after that commit the branch
is 12 local commits ahead of `origin/master`. No amend, history rewrite or push was performed.

EPIC 11300 PASS WITH NOTES — READY FOR EPIC 11400
