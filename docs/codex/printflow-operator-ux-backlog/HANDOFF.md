# SCRUM-11154 — isolated entry implementation verified

2026-09-30. Task `PF-OPUX-v1-SCRUM-11154-workstation-entry-impl-v1`. Required noninteractive implementation and isolation gates passed on candidate `51123588…9EFD15`; final independent conformance PASS; code publication and Jira/export verified; final documentation SHA and packet results are recorded locally after publication. The owner approved the final reviewed design and five choices. Approved design/review bytes and all eight earlier audit residues remain unchanged; the historical records below retain their original meaning.

The actual launcher is `tools/Start-WorkstationEntry.ps1`; [runbook](SCRUM-11154_WORKSTATION_ENTRY_RUNBOOK.md) contains tested commands. Test-only real MainWindow/ShellViewModel/NavigationService and real isolated SQLite/NTFS services passed F1–F6, mapped onto existing W01–W10. Final build clean; 46 entry checks and 7 runtime boundary checks passed, with 474 unchanged architecture checks from the prior affected run. PrepareAndSmoke and the exact owned-child restart exited 0, with settled teardown and no native dispatch. The same independent reviewer found no unresolved isolation issue. See [implementation](SCRUM-11154_WORKSTATION_ENTRY_IMPLEMENTATION.md), [review](SCRUM-11154_WORKSTATION_ENTRY_IMPLEMENTATION_REVIEW.md), [plan](SCRUM-11154_WORKSTATION_ENTRY_IMPL_PLAN.md), and [current audit](SCRUM-11154_WORKSTATION_ENTRY_IMPL_PUBLICATION_STATUS_AUDIT.json).

Production remains denied; processing/readiness/recycling simulated. F6 maximum-box versus actual TIFF size disagreement remains negative evidence. Writer mid-copy/adjacent/final-publication fault injections and the historical generic recovery hang remain unverified. Physical keyboard AC2, workstation screenshots AC3, owner/novice acceptance and print quality remain OPEN/NOT RUN. The consumed negative-test root is not a valid future Interactive root. No visible application, dialogs, Explorer, capture/input, live processors or SCRUM-11155 work occurred.

Actual baseline master/origin `25a93f551f27f9e918fdcbbec701a1371ccc23d0`. Code `a39b3bd94c541e4faadfeafbaffa69e0f35525a4` was pushed directly to origin/master and exact remote tip verified. Only SCRUM-11154 moved In Review → In Progress (21) → In Review (31), with one implementation comment `10191`. Authenticated readback at 2026-09-30T06:02:00Z: 17 issues, 26 Blocks, 51 string labels, 31 complete comments; exact 23-column BOM CSV and independent fidelity/drift checks PASS. Final documentation SHA is recorded locally after its push, without a self-reference loop; complete owner packet/ZIP verification follows. Comments 10189/10190 and other issues remain unchanged. Policy v2.4, route_offset 0; composition/reviewer requested Astra High, separable scenarios Sol High; actual routes UNVERIFIED, root switching unavailable. No global settings changed.

---
# SCRUM-11154 — isolated workstation entry design handoff

2026-09-30. Task `PF-OPUX-v1-SCRUM-11154-workstation-entry-design-v1`. **AWAITING_OWNER_REVIEW.** Design-only investigation of actual master `5e6adcf3251bded0ce3df2d127d933eecde71969`; historical product candidate `e7852f00311d5bc914eae12174e0ae84196bcc23`. SCRUM-11154 remains In Review; AC1–3 and earlier owner decisions remain open. No implementation or SCRUM-11155 work.

- Recommendation: a separate test-only composition using the real MainWindow, navigation, product views/commands/services and isolated SQLite/NTFS behavior. Fake mode and one-screen Wave1A do not prove whole-app isolation. Bind every root/lease/diagnostic destination before construction; production processors and authority remain unreachable.
- Publication: design/review/current handoff/audit commit `a4a896b11023e7e2a89993d81436ce591f262b61` verified on origin/master; one design comment `10190`, no Jira transition. Authenticated read at `2026-09-30T03:08:39Z`: 17 issues, 30 complete comments, 26 Blocks links, 51 string labels; [23-column UTF-8 BOM CSV](SCRUM-11154_WORKSTATION_ENTRY_JIRA_FINAL.csv) and independent fidelity oracle PASS. Other 16 issues and the prior comment are unchanged.
- [Design and owner decisions](SCRUM-11154_WORKSTATION_ENTRY_DESIGN.md), [independent review](SCRUM-11154_WORKSTATION_ENTRY_DESIGN_REVIEW.md), [publication/Jira audit](SCRUM-11154_WORKSTATION_ENTRY_AUDIT.json). Publication/comment/export results are recorded in the audit; the final local audit and owner packet record the last documentation SHA without a self-referential commit.
- Source-traced TIFF discrepancy: the 6×5 fixture at 300 DPI produces 0.508×0.423 mm from its preparation, while the output row displays retained 200×150 maximum-box dimensions. No product/fixture fix; this frame is negative size-consistency evidence. Exact review/save identity and known PNG approval/recovery/diagnostic gaps are preserved.
- Validation in this task is static source/document/privacy/hash/link and publication/export verification only. No builds, product tests, DI, migrations, app launch, native/physical input, new screenshot, real processor, production or novice run. Seven pre-existing audit residues remain untouched and unstaged.
- Next boundary: owner decides on the concrete design and bounded implementation sequence; implementation and a later safe-desktop slot require separate authorization. Continue from the next actual master and verify source/binary identity before any future run. Do not run historical launcher commands from this handoff.

Policy 2.4, route_offset 0. Design and fresh independent safety reviewer requested `gpt-6-astra/high`; runtime metadata UNVERIFIED. Root live switch unavailable (MODEL_SWITCH_UNAVAILABLE); no paper downgrade. Local Windows host, source-only work. Current branch master; scoped documentation only is authorized for direct publication. Other issues stay read-only.

---
# PF-OPUX-v1 — SCRUM-11154 integration handoff

2026-09-30 NZ. Task `PF-OPUX-v1-SCRUM-11154-integration-v1`. **Bounded noninteractive integration, focused verification, one independent review, direct master code publication and SCRUM-11154-only Jira synchronization complete. SCRUM-11154 is In Review. AC1 whole-journey/owner acceptance is OPEN; AC2 physical keyboard-only and AC3 workstation screenshot criteria are NOT RUN; AC4 findings/fixes are recorded. Stop at SCRUM-11154; SCRUM-11155 was not started.**

Across Home, Recent, Recovery, routes, reviews, trim, colleague correction, print dimensions, final save and error details, the single [integration matrix](SCRUM-11154_INTEGRATION.md) maps state, exact identity, legal action and evidence. It preserves Approved versus Saved, Completed versus Delivered, historical saved metadata versus current file existence, and last-observed readiness versus production authority. Existing PNG approval/backfill, legacy Trim Reject exact binding, generic import/recovery, diagnostic navigation and physical usability gaps remain in the owner open-items record. No approval, file, production, gate or recovery contract changed.

- Fixes: final TIFF Reject label now says only “Reject this TIFF” / “驳回此 TIFF”; custom-size current-preview failure shows a neutral adjacent bilingual hint; eight Chinese resources consistently name physical print size “印刷尺寸”. Recent stopped, abandoned and actual running-current-attempt states gained a bilingual render check. [Affected final copy](SCRUM-11154_COPY_REVIEW.md) awaits owner judgment.
- Code: [e7852f00311d5bc914eae12174e0ae84196bcc23](https://github.com/MQLite/printflow-Studio/commit/e7852f00311d5bc914eae12174e0ae84196bcc23), nine scoped source/test/terminology paths, fast-forward to `origin/master`; Git remote ref and authenticated GitHub commit/file readback agree. Six pre-existing modified earlier audit files were not staged.
- Verification: nonincremental build zero warnings/errors; final affected safe set **453/453**, architecture **474/474** (overlap); synthetic 96-DPI off-screen bilingual renders at 1000×700 and 1920×1040. One fresh independent reviewer found a P2 initial over-specific size hint, then confirmed the neutral correction and non-size failure regression; no remaining actionable P0/P1/P2 in the scoped delta. Review/test details are in local `artifacts/pf-opux-scrum11154/`.
- Jira: only SCRUM-11154 To Do → In Progress → In Review (transition IDs 21, 31), one comment `10189` with marker `PF-OPUX-v1-SCRUM-11154-integration-v1`. Post-write authenticated readback at `2026-09-30T01:46:22.488Z`: 17 issues, 29 complete comments, 26 Blocks links, 51 string labels; exact 23-column UTF-8 BOM [CSV](SCRUM-11154_JIRA_FINAL.csv), exporter and independent fidelity oracle PASS. Other 16 issues were read-only; compared with the prior readback, only 11154's updated timestamp/status/comment count changed.
- Next boundary: [one workstation checklist](SCRUM-11154_WORKSTATION_CHECKLIST.md) for a later separately authorized safe session with verified isolation, native keyboard and actual screenshot evidence. A one-Screen Wave1A host is not a verified whole-app host. No shared desktop or production was run now. Owner copy and earlier Tasks remain open. The local owner packet and final audit record the later documentation publication SHA without a self-referential commit.

Routing policy 2.4, `route_offset: 0`: requested Astra High UI/review context; actual runtime model/effort UNVERIFIED, root model switch unavailable, no paper downgrade represented as a switch. No global rule update.

No deployment, production migration/startup, feature branch/PR, force push, protection bypass, other-issue writes or SCRUM-11155 work.

---

# PF-OPUX-v1 — SCRUM-11153 Recent status handoff

2026-09-30 NZ. Task `PF-OPUX-v1-SCRUM-11153-impl-v1`. **Bounded implementation, safe technical verification, independent review, direct master code publication and Jira synchronization complete. SCRUM-11153 is In Review. AC1–4 pass technical checks; AC5 has synthetic layout evidence and awaits physical/human acceptance. Stop at SCRUM-11153.**

Recent now shows the current step's review/stopped/input/actually running status, with handed-off/completed/abandoned precedence. A separate historical line reports only exact approved Delivered PNG or counted TIFF size records, qualified as previously saved; a failed history read is unknown. The row does not claim destination-file availability. Existing Recovery/correction-bound return, Resume/Details, Remove and Abandon remain intact. No new processing, file check, sorting or status store.

- Code: [7b37f24175df5d6efd530683fc52876c6446750a](https://github.com/MQLite/printflow-Studio/commit/7b37f24175df5d6efd530683fc52876c6446750a), 12 source/test paths, normal fast-forward verified by fetch/ls-remote and authenticated GitHub commit readback.
- Verification: final safe affected 197/197, delivery/recovery/architecture 162/162, synthetic screenshot/focused 20/20 plus 2/2 long-name captures, build 0 warnings/errors; groups overlap. Off-screen en/zh-CN at 1000×700 and 1920×1040, 96 DPI. Excluded startup/native desktop and known hanging recovery paths remain NOT RUN/NOT PASS.
- Independent review: one fresh reviewer found three P2 defects, then rechecked fixes and representative images with no remaining actionable code finding. It did not run tests; runtime model/effort unverified.
- Jira: To Do → In Progress → In Review, transitions 21 and 31; one evidence comment `10188`. Authenticated final readback at `2026-09-30T00:16:56.330Z`: 17 issues, 26 Blocks, 51 string labels, complete comments, exact 23-column CSV, fidelity PASS. Only 11153 received writes.
- Review: [plan](SCRUM-11153_PLAN.md), [final copy](SCRUM-11153_COPY_REVIEW.md), [results](SCRUM-11153_RESULTS.md), [independent review](SCRUM-11153_INDEPENDENT_REVIEW.md), [CSV](SCRUM-11153_JIRA_FINAL.csv). Owner copy, physical input/non-96-DPI and Jira human acceptance remain OPEN. The local audit and complete packet record the final docs SHA separately.

No deployment, production startup/migration, shared-desktop operation, feature branch/PR, force-push or other-issue writes. The prior handoff remains below as dated history.

---

# PF-OPUX-v1 — SCRUM-11152 signal closeout handoff

2026-09-30 NZ. Task `PF-OPUX-v1-SCRUM-11152-signal-closeout-v1`. **Implementation and technical independent review PASS; code MASTER_PUSH_VERIFIED; Jira In Review. AC1–4 pass technical/automated verification. AC5 PARTIAL; owner copy acceptance and human workstation checks remain OPEN/NOT RUN. Stop at SCRUM-11152; do not begin SCRUM-11153.**

Home receives existing authoritative observations from Production Readiness passive/live, Settings, per-step full gate verification (including owned lease), and diagnostic-package reads. No extra checks or permission changes. Monotonic generation withdraws prior Ready on later starts/failures/incomplete checks; cancelled results stay unconfirmed. Internal-work subset success cannot establish full Ready. Loaded Home updates on its dispatcher without refreshing jobs, clearing notices or changing focus. Diagnostic-package outcome does not classify readiness.

Approved Home copy directions are implemented in both locales, including “View workstation checks”, exact check identifier/state, experienced-colleague/supervisor help, startup-only preset wording, and a Home-only cleanup notice that does not imply processing may proceed. Final copy acceptance is open.

- Code: [11df50f7143faa8cc33ec537eaf5f7a162a69d08](https://github.com/MQLite/printflow-Studio/commit/11df50f7143faa8cc33ec537eaf5f7a162a69d08), 16 source/test paths; direct fast-forward, authenticated GitHub commit/tree/parent/path verification plus fetch/ls-remote.
- Validation: final focused 155/155, architecture 473/473, affected safe UI/resources 225/225 (overlap); 4/4 render cases, 48 final synthetic PNGs, en/zh-CN, 1000×700 and 1920×1040, 96 DPI. Same independent reviewer performed precheck and final source/evidence/visual review, no actionable findings; reviewer ran no tests.
- Jira: In Review; new deduplicated comment 10187, original 10186 unchanged. Final authenticated read `2026-09-29T22:58:30.117Z`; 17 issues, 26 Blocks, 51 string labels, 34 exact timestamp strings; complete parent/comments, exact 23-column CSV and independent fidelity PASS; other issues read-only with no unexpected drift.
- Public decision documents: [closeout contract and evidence](SCRUM-11152_CLOSEOUT.md), [final bilingual copy](SCRUM-11152_CLOSEOUT_COPY_REVIEW.md), [complete results](SCRUM-11152_CLOSEOUT_RESULTS.md), [independent review](SCRUM-11152_CLOSEOUT_INDEPENDENT_REVIEW.md), [CSV](SCRUM-11152_CLOSEOUT_JIRA_FINAL.csv), [publication audit](SCRUM-11152_CLOSEOUT_PUBLICATION_STATUS_AUDIT.json).
- The following privacy-safe docs commit is verified separately; its SHA is recorded only in the local final audit and new immutable packet. Packet/ZIP follow the existing `doc/owner-review/` convention and stay ignored/local. Old packet, original evidence, prior exports and all four audit residues are preserved.
- Remaining: AC5 physical input/other scaling/Jira human check NOT RUN; final copy acceptance OPEN; prior 11151 copy/navigation and 11150 residuals unchanged. Unsafe real-window/UIA/startup/composition paths excluded; known hanging recovery test remains NOT PASS.
- Routing policy 2.4, route_offset 0; supported native Astra High implementation and independent-review targets, actual model/effort UNVERIFIED. Coordinator live switch unavailable; no fabricated downgrade. No global configuration changes.

No deployment, production migration/startup, desktop control, feature branch/PR, force-push or other-issue writes. Prior handoff bytes remain verbatim below as dated history.

---

# PF-OPUX-v1 — SCRUM-11152 current handoff

2026-09-30 NZ. Task `PF-OPUX-v1-SCRUM-11152-impl-v1`. **Implementation, verification, independent review (two rounds), code MASTER_PUSH_VERIFIED and Jira synchronization complete. SCRUM-11152 is In Review. AC1 and AC5 are PARTIAL, the owner copy review is OPEN, and the Jira human check is NOT RUN. Stop at 11152; SCRUM-11153 not started.**

**Delivered.** Home now shows one readiness summary built from what Production Readiness last observed in this run:
- **Not checked yet**: nothing observed since PrintFlow started. An earlier run's pass or the startup preset check never counts.
- **Checking**: a reading has started but has no result yet.
- **Ready to process when last checked**: shown with that reading's own `ObservedAt`, in local time with the date.
- **Automatic processing is blocked**: gives the report's first blocking reason in the readiness screen's wording. A live check that has not run reads "no current result… choose Safe recovery and recheck".
- **Readiness is not confirmed**: the last check threw or was cancelled, or its report named no reason.

It has one button, the existing Production readiness screen. Startup recovery counts are under a collapsed "Startup details / 启动详情". The preset-not-verified, recovery-not-run and retention warnings stay visible. The Recovery list, its actions, correction navigation and the 11151 notice + Error details are unchanged. The part above the lists scrolls on its own once it would crowd them.

**How.**
- `ReadinessObservationAccessor` is an app-lifetime, in-memory singleton that is never persisted and starts empty each run.
- It uses latest-ticket-wins semantics: a started reading hides the older report, a thrown or cancelled reading withdraws it, and a late earlier completion is ignored.
- It is fed by `EnvironmentReadinessViewModel` (passive and live readings) and `SettingsViewModel` (the same passive `Read`).
- `HomeReadinessSummary` restates no rule, and Home reads memory only.

No new check, gate, persistence, production authorisation or processor activity.

**Publication and verification**
- Code: [a71927fcfe09f3644be74bbc9718321a220e670c](https://github.com/MQLite/printflow-Studio/commit/a71927fcfe09f3644be74bbc9718321a220e670c), 25 paths, normal fast-forward from `d017fdb`. Fetch, `ls-remote` and a GitHub REST readback agree. The `gh` CLI is not installed; the REST readback was an unauthenticated GET of the public repository.
- Settled candidate (clean `--no-incremental` build, 0 warnings/errors). Counts overlap:
  - focused 155/155;
  - architecture 473/473;
  - safe combined UI 711/711;
  - final test class 30/30 after the test-only follow-ups.
- Test-first evidence:
  - behavioural RED 16/27 before wiring;
  - RED 5/30 against the reverted review fixes;
  - three mutations caught.
- Renders: 32 final synthetic 96-DPI renders, 8 capture contexts × en/zh-CN × 1000×700 and 1920×1040. Materially different states were inspected.
- Independent review: one fresh read-only reviewer over two rounds; no blocking defect remains, and the reviewer ran no tests.
  - Round 1 found F1 (a false "prerequisite failed" reason) and F2 (unrecorded gate re-verification) at P1, plus F3 (cancel shown as a fault) and F4 (lists squeezed to 1px) at P2.
  - F1, F3, F4 and F5 are fixed. F2 is dispositioned: Ready is worded as a past result and AC1 stays PARTIAL.
- This docs commit follows the code commit. Its verified SHA and the final tip are recorded only in the local final audit, the owner-review packet and the execution report.

**Jira and export**
- Transitions: To Do → In Progress (21) at implementation start, then In Progress → In Review (31) after verified code publication.
- One evidence comment, `10186`, with marker `PF-OPUX-v1-SCRUM-11152-impl-v1`.
- Final read `2026-09-29T21:57:25.846Z`: 17 issues, 26 Blocks links, 51 string labels, 34 exact timestamps, complete comments, one marker. Only this issue and its embedded link status on 11154/11155 changed. A first readback taken without the `parent` field was set aside as NOT A RESULT.
- [CSV](SCRUM-11152_JIRA_FINAL.csv): unchanged 23-column schema, UTF-8 BOM. Both the exporter and the independent fidelity oracle PASS. The other 16 issues are READ_ONLY.

**Open acceptance and limits**
- **AC1 PARTIAL.** Two readings are not recorded, so after a later refusal Home can still show an earlier "Ready to process when last checked":
  - the gate's own re-verification before each production step (`VerifiedEnvironmentGate.AuthoriseProduction`);
  - `DiagnosticPackageService` readings.

  The owner option to downgrade Home on `EnvironmentNotVerified` is documented and not implemented.
- **AC5 PARTIAL.** Only synthetic 96 DPI renders exist. Display scaling, real keyboard traversal and the Jira human check are NOT RUN.
- The owner must review the [bilingual copy](SCRUM-11152_COPY_REVIEW.md). No acceptance is inferred from the implementation brief.
- Accepted residuals (see [plan](SCRUM-11152_PLAN.md)):
  - Home refreshes its snapshot on open and on Refresh only;
  - cancelling right after a real failure shows "not confirmed";
  - Settings moved to the title row;
  - `Preset_*` keys are unused.
- Excluded and NOT RUN: real-window/UIA classes, ApplicationStartup and ProductionComposition paths (including `ApplicationStartupTests`, which reads `StartupSummary`, and `HomeAndWorkflowSelectionTests`). The known hanging recovery test remains excluded and NOT PASS.
- All earlier open items are still open: 11151 owner copy review, AC1 diagnostic-navigation gap and legacy attempt wording; 11150 inline-validation residual, PNG approval and physical-input checks.

**Owner-review packet (new convention).** Every task now finishes with one self-contained packet and ZIP per issue per run. The layout and rules are in [`doc/owner-review/README.md`](../../../doc/owner-review/README.md). Packets, ZIPs and `LATEST.md` stay local and ignored by Git; only the README is tracked. This run's packet includes the still-pending 11151 copy review under `related-pending/`, with its originals untouched.

**Evidence and routing**

Public: [plan, source-to-display matrix and AC map](SCRUM-11152_PLAN.md), [copy review](SCRUM-11152_COPY_REVIEW.md), the CSV and the audit.
Local only:
- `artifacts/pf-opux-scrum11152/`: RESULTS, both review records, raw Jira responses, logs/TRX and renders;
- `SCRUM-11152_JIRA_READBACK.json` (account metadata; gitignored);
- `doc/owner-review/SCRUM-11152/`.

Claude adaptation v1.2 of policy v2.4, `route_offset: 0`, Opus High. The host model is `claude-opus-5-5`; effort is UNVERIFIED. No Sonnet unit. One `personal-dev-reviewer` subagent (definition `opus`) reported `claude-opus-5-5` from its context. No global configuration changed.

Prior 11149/11150/11151 audit residues are preserved byte-for-byte and never staged. After the docs publication, one deliberate local 11152 final-audit residue will record the docs SHA.

Prior handoff bytes are preserved verbatim below as dated history.

---

# PF-OPUX-v1 — SCRUM-11151 current handoff

2026-09-29 NZ. Task `PF-OPUX-v1-SCRUM-11151-impl-v1`. **Implementation, final verification, independent review, code MASTER_PUSH_VERIFIED and Jira synchronization complete. SCRUM-11151 is In Review; owner bilingual copy review remains OPEN. Stop at 11151.**

Delivered: all 16 scoped failure keys in en/zh-CN, four helper strings and 13 notice-only specialised variants. Main notices describe known/uncertain outcomes without unconditional Retry; exact originating codes sit in adjacent read-only Error details disclosures. Trim/language retention, correction prepare/import, alternate actions and successive Error Details failures are covered. Guidance uses the existing right scroller; preview/crop dimensions and business commands remain unchanged.

**Publication and verification**

- Code: [924281d22974f1693900052851f05c7ddfda750a](https://github.com/MQLite/printflow-Studio/commit/924281d22974f1693900052851f05c7ddfda750a), 30 paths, normal fast-forward push from `91a21ea`; fetch, ls-remote and authenticated GitHub verification agree.
- Final clean build: 0 warnings/errors; architecture 473/473; corrected safe UI/resource set 681/681; 0 failures/skips. Focused/intermediate counts overlap.
- 36 synthetic off-screen renders: 9 contexts × both languages × 1000×700/1920×1040, 96 DPI. Every materially different family inspected in both languages. Expanded/collapsed disclosures retain baseline preview geometry.
- Fresh independent read-only review and same-context fix recheck: closed for this bounded implementation. Latest P2 (old package failure masking the next failure) fixed after six behavioral RED cases; affected suite 42/42. Reviewer reran no tests.
- This six-path docs commit follows the code commit. Its verified SHA/final tip is recorded only in the local final audit and execution report.

**Jira and export**

- Original Claude session moved To Do → In Progress; continuation moved In Progress → In Review after verified code publication.
- Single evidence comment `10185`; marker `PF-OPUX-v1-SCRUM-11151-impl-v1`.
- Final read `2026-09-29T05:11:42.773Z`: 17 issues, 26 Blocks, 51 string labels, 34 exact timestamps. Complete comments, one marker. Only this issue and its nested linked status changed; prior comments and relationships preserved.
- [CSV](SCRUM-11151_JIRA_FINAL.csv): exact 23-column schema, UTF-8 BOM; exporter and independent fidelity oracle PASS. Other 16 issues READ_ONLY.

**Open acceptance and limits**

- Owner must review the actual [bilingual copy table](SCRUM-11151_COPY_REVIEW.md); no acceptance/waiver inferred from the implementation brief.
- Adjacent disclosures show the exact code; they do not navigate to the attempt-bound full diagnostic page. This precise AC1 gap remains documented.
- Full specialised attempt Description retains legacy advice/claims outside the bounded notice variants. This is not claimed fixed or waived.
- No live workstation/scaling or physical-input acceptance. Real-window/UIA, ApplicationStartup and ProductionComposition stay excluded. The known hanging recovery test remains excluded and NOT PASS.
- Nonblocking English button-label quotes, Home refresh/notice ordering and retained disclosure state remain. Earlier 11150 Domain-invalid sizing inline-hint residual and all earlier open checks remain.
- No deployment, production startup/data changes, desktop interaction, other-issue writes or 11152+ work.

**Evidence and routing**

Public: [source/claim matrix and AC map](SCRUM-11151_PLAN.md), [copy review](SCRUM-11151_COPY_REVIEW.md), CSV and [audit](SCRUM-11151_PUBLICATION_STATUS_AUDIT.json).
Local only: `artifacts/pf-opux-scrum11151/` (RESULTS, raw responses, review records, logs/TRX, layout/renders); `SCRUM-11151_JIRA_READBACK.json` (account metadata, ignored).

Policy 2.4, route_offset 0. Approved Claude plan continued. UI-fix and independent-review native subagents requested/accepted Astra High; actual runtime metadata UNVERIFIED. Coordinator normal route Sol Medium; MODEL_SWITCH_UNAVAILABLE, no claimed live downgrade. No global configuration changed.

Prior 11149/11150 audit residues are preserved byte-for-byte and never staged. After docs publication, one deliberate local 11151 final-audit residue records the docs SHA; do not fold these residues into later work.

Prior handoff bytes are preserved verbatim below as dated history.

---

# PF-OPUX-v1 — SCRUM-11150 current handoff

2026-09-29 NZ. Task `PF-OPUX-v1-SCRUM-11150-impl-v1`. **Implementation, verification, independent review, MASTER_PUSH_VERIFIED and Jira synchronization COMPLETE. SCRUM-11150 is In Review; human acceptance NOT RUN. Stop; SCRUM-11151 not started.**

**Delivered.** Plain print-size guidance on the two TIFF routes, in the right-hand details column:
- a "choose this when" line beside each existing choice (a preset is a maximum and is never enlarged; Custom size fixes one chosen side, Long edge = the longer side of this picture);
- a live sentence in "Print size check / 印刷尺寸核对": the whole picture's approximate millimetres, what decided them and that proportions are kept, formatted from the shown preflight; drafts say "would";
- the enlargement offer in plain words beside the same buttons and gating;
- the existing custom-size validation under the input;
- unchanged pixel, PPI, edge and scale facts under collapsed "Technical details".

The deciding edge comes from a new read-only projection on `PrintDimensionsPreflight` (`Governor`, `GoverningEdge`, `SelectedTargetEdge`), copied in `SessionService.PreflightFrom` from the plan it already projects. No second calculator, no persistence, no change to fitting, 300 PPI, offers or gating.

**Git**
- Before: local = `origin/master` = `fe38160796e39af5dd511cacf676778e9c3bee16`; only the SCRUM-11149 local audit residue was modified, and it stays unstaged.
- Code commit `03da756f6b021cce809b8591229b0c605ebe1f55` (12 paths), fast-forward push, verified by fetch, `ls-remote` and the GitHub API.
- A docs-only commit follows with this HANDOFF, the plan, the CSV, the audit and the one-mapping exporter change. Its SHA is recorded in the local audit copy and in the final report, not here.

**Verification**
- New tests 47/47 (tests written alongside the code; two deliberate mutations caught and restored).
- Affected sizing/preflight/workflow/resource suites 264/264, incl. MaximumBoundsUiTests and FlexibleSizeUiTests.
- Settled candidate: clean build 0/0; architecture 452/452; combined UI 601/601; workflow/persistence 11531/11531. Counts overlap.
- Exclusions and the NOT PASS baseline recovery hang are unchanged.
- 20 off-screen layouts at 96 DPI: the preview area is identical to the baseline in all 20.

**Independent review.** One fresh read-only reviewer, two rounds, CLOSED with no P0–P2. Residuals:
- a number that parses but that the Domain refuses shows no inline hint;
- scaling other than 96 DPI was not run;
- zh-CN 打印尺寸/打印准备 remain in older labels on this screen.

**Jira**
- SCRUM-11150 transitions 21 (→ In Progress) and 31 (→ In Review); comment 10184.
- Final readback 2026-09-29T01:57:19.864Z: 17 issues, 26 Blocks, 51 labels, 34 timestamps; expected drift only.
- [CSV](SCRUM-11150_JIRA_FINAL.csv) (23 columns, BOM, exporter and oracle PASS, integrity 18/18) and [audit](SCRUM-11150_PUBLICATION_STATUS_AUDIT.json).
- Other issues were read-only.

**Remaining acceptance**
- Human bilingual workstation check at its own scaling (AC6) and physical input; checklist in [SCRUM-11150_PLAN.md](SCRUM-11150_PLAN.md).
- Novice walkthrough (SCRUM-11155).
- All earlier open checks listed below remain open.

**Where things are**
- Public: [plan and AC map](SCRUM-11150_PLAN.md), CSV, audit, code commit.
- Local only:
  - `artifacts/pf-opux-scrum11150/` (RESULTS.md, review record, logs/TRX, renders, raw readback);
  - `SCRUM-11150_JIRA_READBACK.json` (account metadata; gitignored).

**Routing.** Claude adaptation v1.2 of policy v2.4, `route_offset: 0`, Opus High. Host model `claude-opus-5-5`, effort UNVERIFIED. No Sonnet unit; one reviewer subagent (definition `opus`, runtime UNVERIFIED).

Prior handoff bytes are preserved verbatim below as dated history.

---

# PF-OPUX-v1 — SCRUM-11149 current handoff

2026-09-29 NZ. Task `PF-OPUX-v1-SCRUM-11149-impl-v1`. **Implementation, verification, independent review, MASTER_PUSH_VERIFIED and Jira synchronization COMPLETE. SCRUM-11149 is In Review; human acceptance NOT RUN. Stop; SCRUM-11150 not started.**

**Delivered.** A read-only "What to check / 检查要点" section in the session screen's details column for the Enhancement, background-removal, Trim and production-TIFF reviews. Each shows 3–4 checks naming existing tools, an Approve line (never "saved"), a Reject line and a help line. The help buttons are the existing Ask-a-colleague and Adjust-trim-edges commands with their own eligibility. The TIFF Reject copy follows the code: Recycle Bin first, a failed recycle records nothing, and no new TIFF until Run step (Retry alone makes none).

**Git**
- Before: local = `origin/master` = `fddf2434d26d927a798f942678c79072ab3afc99`, clean.
- Code commit `6cbde817a2da7f3565cf52860ca26d79a3e71242` (11 paths), fast-forward push, verified by fetch, `ls-remote` and the GitHub API.
- A docs-only commit follows with this HANDOFF, the plan, the CSV, the audit and the one-mapping exporter change. Its SHA is recorded in the local audit copy and in the final report, not here.

**Verification**
- Tests written first (red build recorded).
- Targeted 36/36.
- Settled candidate: clean build 0/0; architecture 452/452; combined UI 554/554 with the SCRUM-11148 corrected filter. Counts overlap.
- Exclusions and the NOT PASS baseline recovery hang are unchanged. Workflow/persistence and backend groups were not rerun, because no Workflow, Domain or Infrastructure file changed.
- 16 off-screen renders at 96 DPI: the picture area is identical to the baseline in all 16.

**Independent review.** One fresh read-only reviewer, two rounds, no open P0–P2. Two P2 findings were fixed: TIFF wording against the existing label, and the untested R2 review. Residuals:
- the Trim/TIFF final-review lines need scrolling at 1000×700;
- the Trim/TIFF fallback wording is untested;
- focus returns to the bar button;
- the real-window review-authority class and other DPI scaling were not run.

**Jira**
- SCRUM-11149 transitions 21 (→ In Progress) and 31 (→ In Review); comment 10183.
- Final readback 2026-09-28T23:32:17.755Z: 17 issues, 26 Blocks, 51 labels, 34 timestamps; expected drift only.
- [CSV](SCRUM-11149_JIRA_FINAL.csv) (23 columns, BOM, exporter and oracle PASS) and [audit](SCRUM-11149_PUBLICATION_STATUS_AUDIT.json).
- Other issues were read-only.

**Remaining acceptance**
- Human bilingual workstation check (AC1/AC6), other scaling and physical input.
- Real colleague round trip from the guidance button.
- Novice walkthrough (SCRUM-11155). AC4 applies only if the walkthrough refutes a prompt; the first candidate is the unchanged label "Reject and make another TIFF".
- All earlier open checks listed below remain open.

**Where things are**
- Public: [plan and AC map](SCRUM-11149_PLAN.md), CSV, audit, code commit.
- Local only:
  - `artifacts/pf-opux-scrum11149/` (RESULTS.md, review record, logs/TRX, renders, raw readback);
  - `SCRUM-11149_JIRA_READBACK.json` (account metadata; gitignored).

**Routing.** Claude adaptation v1.2 of policy v2.4, `route_offset: 0`, Opus High. Host model `claude-opus-5-5`, effort UNVERIFIED. No Sonnet unit; one reviewer subagent (definition `opus`, runtime UNVERIFIED).

Prior handoff bytes are preserved verbatim below as dated history.

---

# PF-OPUX-v1 — direct master publication and Jira status current handoff

2026-09-29 NZ. Task `PF-OPUX-v1-master-publication-v1`. **MASTER_PUSH_VERIFIED; Jira status synchronization and final readback COMPLETE. Stop; SCRUM-11149 not started.** Full record: [PUBLICATION_STATUS_REPORT.md](PUBLICATION_STATUS_REPORT.md) and [PUBLICATION_STATUS_AUDIT.json](PUBLICATION_STATUS_AUDIT.json).

**Authorization.** The user authorized direct publication to `master` for this batch, with no feature branch or PR. That supersedes this batch's earlier no-commit/no-push/no-status-change instructions. The historical statements below remain true for their own dates.

**Git**
- Local HEAD before: `eea60198c5095243f0694717a7479b00c3e1cd73`. `origin/master` before: `cb552eefad00682c2e2f868206d248715dfeb6d3`; it had not advanced.
- New commits:
  - `cb875b2896481abd7915f52fba833b939aec22f4`: feat: operator UX for PF-OPUX-v1 through SCRUM-11148 (103 source/test/migration paths)
  - `4ed9b52a448339e4041e5bbaf937312fee51aced`: docs: PF-OPUX-v1 plans, designs, Jira exports and tooling through SCRUM-11148 (44 paths)
- The fast-forward push also published the seven earlier local docs commits.
- Final `origin/master`: `4ed9b52a448339e4041e5bbaf937312fee51aced`. Fetch, ls-remote and the GitHub API agree, and the published paths equal the manifest.

**Not committed**
- The two `docs/printflow` waiver files (pre-existing user work).
- Five `SCRUM-*_JIRA_READBACK.json` snapshots, plus the new publication readback. They embed the Atlassian accountId, and two also embed the owner email. The repository is public. This is an owner decision.
- This section and the PUBLICATION_STATUS_* outputs, which were written after the push.

**Verification**
- Settled source equals the SCRUM-11148 final candidate; the assemblies are byte-identical.
- Clean build: 0 warnings, 0 errors.
- Architecture 452, UI 518, workflow/persistence 11531 and delivery backend 124 all passed, with 0 failed or skipped. The counts overlap.
- Export integrity 18/18.
- Migrations 0001–0017 unchanged; 0018 and 0019 match their accepted hashes.
- The exclusions are unchanged. The baseline recovery hang remains NOT PASS.

**Jira**
- Transitions: To Do 11, In Progress 21, In Review 31, Done 41.
- Status changes:
  - SCRUM-11139 Epic and SCRUM-11141 → In Progress.
  - SCRUM-11140 and SCRUM-11142–11148 → In Review.
  - None → Done.
- One marker comment `PF-OPUX-v1-master-publication-v1` per issue, comments 10173–10182.
- Final authenticated readback 2026-09-28T22:29:11.826Z: 17 issues, 26 Blocks, 51 string labels, 34 timestamps. Only the expected drift occurred.
- CSV: 23 columns with a UTF-8 BOM. Exporter PASS; independent oracle PASS.
- SCRUM-11149–11155 were read only and remain To Do.

**Still awaiting acceptance**
- **Owner review:** 11140 delivery terms; 11144 KeepOriginalExtent PNG approval contract and asset-route acceptance.
- **Human-only:** the 11141 walkthrough.
- **Human/native checks:**
  - 11142/11143/11146 workstation bilingual visuals;
  - 11143 post-focus-fix native and physical input;
  - 11145 Explorer selection and real picker;
  - 11147 human synthetic-image, drag and key checks;
  - 11148 human check and real colleague round trip.
- **Epic:** novice validation (SCRUM-11155).
- Residuals preserved below are unchanged.

**Not performed.** No deployment, production migration or startup, customer files, force-push, history rewrite or SCRUM-11149+ work.

**Routing.** Claude adaptation v1.2 of policy v2.4, `route_offset: 0`, Opus High. Host model `claude-opus-5-5`, effort UNVERIFIED. No Sonnet unit or subagent.

Prior handoff bytes are preserved verbatim below as dated history.

---

# PF-OPUX-v1 — SCRUM-11148 current handoff

2026-09-28 NZ. **Bounded local implementation, verification, independent review and authorized Jira reporting COMPLETE. Human/native acceptance NOT RUN. Stop at SCRUM-11148.**

Continued Claude session `58c23d0a-0ac1-44c6-a1c9-3a728d6d9436` after its quota interruption. Request-bound colleague correction now covers migration0019, verified independent U/R copies, explicit PNG preflight/import, distinct R2 with exact human review and Trim next, Home navigation plus both generic guards, post-opening/persisted bindings, atomic AssertBound unfinished closures, no post-success handoff and foreign-lock preservation. Approved base/addendum/D1–D5/C1/C2 unchanged.

The continuation corrected the stale delivery-schema version expectation and three independently found P2 defects: partial-file overwrite, linked-final acceptance and stale same-session asynchronous UI responses. Fresh CreateNew staging leaves old partials untouched; held handles reject reparse/multi-link finals. Correction-only target ownership covers responses/reloads/picker/focus/busy state. T9 real closing seams supplement pure-builder evidence.

- Reviewed50-file scoped diff SHA256 `80323da44bb3998dc55ac24342aabcc0af5ae623cf93e89973c3be0e25ac7622`; manifest/assembly hashes under artifacts/pf-opux-scrum11148. Four designs/reviews plus833 unrelated baseline files remain unchanged.
- Build0 warnings/errors. Targeted379/379; final UI518, workflow/persistence11531, architecture452, backend/delivery124 all PASS with0 failed/skipped. Counts overlap. Export integrity18/18; final raw/snapshot/CSV fidelity PASS. Failed/intermediate evidence preserved.
- Independent fresh native reviewer, then same-context recheck: R1/R2/R3 CLOSED, no remaining P0–P2. Read-only review, no tests executed by reviewer. Requested Astra High; actual runtime model/effort UNVERIFIED.
- Fourteen new off-screen bilingual captures,1000x700/1920x1040 at96DPI, with representative inspection. English small handoff panel needs scrolling. These are not native input/workstation acceptance.
- Scope deviation: inherited historical backend filter ran22 ProductionCompositionTests cases against temp layouts without activating adapters, contrary to production-DI prohibition. Disclosed; final03 stopped and final04 excludes that class. No production startup/customer migration/files/visible desktop operations. Pre-existing baseline recovery hang remains excluded and NOT PASS.
- Real colleague round trip, picker/Explorer, physical input/focus, supported-workstation visuals and novice usability remain NOT RUN. Previous Wave1A/11145/11146/11147 checks, generic unbound Stop/TakeOver, KeepOriginalExtent PNG gap, startup invalid-binding asymmetry and trim/small-viewport residuals remain open. Stop does not immediately interrupt the importer; successful imports remain successful.

Jira: sole evidence comment [10172](https://yituoxx.atlassian.net/browse/SCRUM-11148?focusedCommentId=10172), marker PF-OPUX-v1-SCRUM-11148-impl-v1. Authenticated final readback2026-09-28T04:36:30.035Z:17 issues/26 Blocks/51 string labels/34 raw timestamps; all comments complete, marker once, no unexpected field drift. [Actual JSON](SCRUM-11148_JIRA_READBACK.json) and [23-column UTF-8-BOM CSV](SCRUM-11148_JIRA_FINAL.csv) validated independently; no status/field/AC/label/relationship changes.

[Plan](SCRUM-11148_PLAN.md), [results and AC/T1–T9 evidence](../../../artifacts/pf-opux-scrum11148/RESULTS.md), [independent review](../../../artifacts/pf-opux-scrum11148/independent-review.md). Codex policy2.4, offset0; package Sol High/UI Astra High at independent correction boundary; native isolated review. Runtime telemetry UNVERIFIED, no claimed in-place switch. Root switch unavailable recorded.

Repository D:/Repositories/printflow-Studio, master at eea60198c5095243f0694717a7479b00c3e1cd73. All accumulated work uncommitted; no commit, push, deployment, global rule/memory update, SCRUM-11149 or later task. No further work scheduled. Prior HANDOFF bytes are preserved verbatim below.

---

# PF-OPUX-v1 — SCRUM-11148 design-boundary closeout current handoff

2026-09-28 NZ. Task `PF-OPUX-v1-SCRUM-11148-design-boundary-closeout-v1` (PLAN_ONLY / REVIEW_ONLY). **AWAITING_OWNER_REVIEW.** This task produced a design addendum and its review only; nothing is authorized for implementation or migration.
- **Base design:** unchanged, SHA-256 `8d4edaec9c9b02d94bde75ac18d85f7a1bbf609bdcd667dc2df21e0c316bd9b2`. Its review is also unchanged.
- **[Addendum](SCRUM-11148_DESIGN_ADDENDUM.md):** 288 lines, SHA-256 `90f5d61a2b51f48188766d81d9dd5a652f11494bc576ae2aed643d6559ec5f28`.
- **[Addendum review](SCRUM-11148_DESIGN_ADDENDUM_REVIEW.md).**
- **Candidate specification:** the base design plus the addendum. The addendum's §5 lists the exact base clauses it supersedes.

**C1 — Home recovery import (confirmed base-design defect, runtime not tested).**
- **The defect.** The base design kept Home's generic "Import manual result" for a correction job. A replacement import started there has no request binding. If it is stopped and then fails, or crashes, the job ends `Active` + `Interrupted`, where Run restarts background removal.
- **The fix: Home navigates instead.** For a job with an eligible request, the Home card offers "Open job to import corrected image" (read-only load and navigation) and no generic import.
- **Two service guards.** `RecoveryOf` omits ManualResult, and `ExecuteCoreAsync` refuses a generic `SubmitManualResult` while an eligible request exists without a `CorrectionContext`.
- **Legacy and F19 are unchanged.** No request is inferred from text or filenames.

**C2 — bound closing scope (confirmed document inconsistency).**
- **The inconsistency.** Base §6.4 L293 and review N1 applied `LockChange = null` to every manual-import Stop.
- **Source shows this is unnecessary.** Success and failure closes of manual imports are already lock-neutral today. The addendum defines one explicit predicate, `BoundCorrectionClose` (session, BackgroundRemoval, READY, `LastImportAttemptId`, the attempt being closed, input = U), sourced from `afterStart` or the persisted row.
- **Bound closes.** A bound unfinished close re-hands off with the request's reason, touches no lock, and asserts the request row in the same transaction. A bound success does not hand off.
- **Unbound closes are unchanged.** Generic Stop/TakeOver lock release is recorded as an observation only.

**Review.** One fresh read-only `personal-dev-reviewer`, because the original context was unavailable. Round 1: CLOSED with 8 × P3, all fixed. Recheck in the same context: CLOSED, with one wording-only P3 applied verbatim.

**Owner decisions.** D1–D5 remain pending; D5 is now scoped by the predicate (addendum §9). No new decision is proposed. Generic manual-import lock neutrality would need its own decision and task.

**Not run.** Builds, tests, the application, desktop, migrations, Jira (neither read nor written) and CSV regeneration. Future cases T1–T9 are NOT RUN.

**Routing.** Claude adaptation v1.2 of policy v2.4, `route_offset: 0`. Opus High; host model `claude-opus-5-5`, effort UNVERIFIED. No Sonnet unit.

**Repository.** `D:/Repositories/printflow-Studio`, `master`, HEAD `eea60198c5095243f0694717a7479b00c3e1cd73`. Only the two addendum documents and this section were added. No commit, push or deploy.

**Stop.** Implementation requires owner approval of the base design, the addendum and D1–D5, plus a separate authorization.

---

# PF-OPUX-v1 — SCRUM-11148 colleague-correction design current handoff

2026-09-28 NZ. Task `PF-OPUX-v1-SCRUM-11148-design-v1` (PLAN_ONLY / REVIEW_ONLY). **AWAITING_OWNER_REVIEW.** Design and independent review only; nothing is authorized for implementation or migration.
- [Design](SCRUM-11148_COLLEAGUE_HANDOFF_DESIGN.md): SHA-256 `8d4edaec9c9b02d94bde75ac18d85f7a1bbf609bdcd667dc2df21e0c316bd9b2`.
- [Review](SCRUM-11148_COLLEAGUE_HANDOFF_DESIGN_REVIEW.md).

**Requirement source.** Authenticated read-only read of SCRUM-11148 (id 10878): To Do, updated 2026-09-22T14:27:26.992+1200, 0 comments, parent SCRUM-11139, label `pf-opux-v1`. All eight AC are mapped in design §10.

**Current-source findings**
- Today a background-removal review handed off via `HandOff` is a dead end, except for Reject or Abandon. The session is `HandedOff` with the step still `ReviewRequired`; `CanSubmit`, `ReenterAutomation` and Approve all refuse.
- Operator `HandOff` creates **no file**: its `CreateWorkingCopy` and `OpenForManualWork` effects are never realized.
- A stopped or crash-interrupted manual import leaves the session `Active`.
- A lock release by a non-holder rolls back the whole commit.

**Recommendation: a dedicated exact-target pair.**
- `RequestColleagueCorrection` (default reason when there is no note) and `ImportCorrectedImage`.
- Both are bound to a persisted request id with R and U id + hash.
- Both are reachable only through dedicated service entries; `ExecuteAsync` refuses them.
- The generic `HandOff`, `CanSubmit` and the F19 test are unchanged.

**Files.**
- Reference = U, the exact input of background removal. Working copy = R, the result under review.
- Verified copies go into `Sessions\…\Correction\…`. They are never links, and PrintFlow never overwrites or deletes the colleague's files.
- "Handed off" is committed only after the files are verified.
- This is not delivery: no delivery records, and no remembered destination is used.

**Return.**
- An explicit picker, then a read-only preflight using the importer's own limits. A refusal writes nothing and keeps the handoff.
- The import produces a new Revision R2 (source U) awaiting review. Approve and Reject become exact (id + hash).
- The R2 review names the next step (Trim).
- A failed, stopped or crash-recovered correction import re-hands off in its closing commit.
- There is no watcher and no automatic re-run.

**Review.** One fresh read-only `personal-dev-reviewer` context (Opus High requested; it reported `claude-opus-5-5`, effort UNVERIFIED).
- Round 1: OPEN, 2 × P1, 4 × P2, 6 × P3.
- Round 2: CLOSED, plus three P3 items, which were folded in.
- Round 3: OPEN, one P2 (bound-attempt lookup), fixed.
- Round 4: **CLOSED**.

**Owner decisions (design §13).**
- D1: additive migration 0019 `CorrectionRequest`.
- D2: correction folder inside the managed workspace.
- D3: reference = U, not the uploaded original.
- D4: hide the generic "Hand off manually" on background-removal review.
- D5: the narrow closing-seam re-handoff for correction-bound imports.

**Not run.** Builds, tests, application, picker, Explorer, desktop, migrations, Jira writes and CSV regeneration. All design §11 evidence is future work. The SCRUM-11147 open items and the earlier human checks preserved below remain open.

**Routing.** Claude adaptation v1.2 of policy v2.4, `route_offset: 0`. Opus High throughout; host model `claude-opus-5-5`, effort UNVERIFIED. No Sonnet unit.

**Repository.** `D:/Repositories/printflow-Studio`, `master`, HEAD `eea60198c5095243f0694717a7479b00c3e1cd73`.
- The accumulated uncommitted work is untouched.
- This task added only the two SCRUM-11148 documents and this section.
- No checkout, stash, commit, push or deploy.

**Stop.** Implementation needs owner approval of the design (with D1–D5) and a separate authorization. Do not start it from this handoff alone.

The complete previous handoff is preserved verbatim below as dated history.

---

# PF-OPUX-v1 — SCRUM-11147 implementation current handoff

2026-09-25/28 NZ. Task `PF-OPUX-v1-SCRUM-11147-impl-v1`, run under the owner's approval of the reviewed design (SHA-256 `be4d7491…9cb7`, unchanged). **The bounded implementation, validation, independent review and authorized Jira synchronization are complete. STOP; no autonomous continuation.** This is technical completion on synthetic data, not operator acceptance or production readiness. Documents: [plan](SCRUM-11147_PLAN.md), [results](../../../artifacts/pf-opux-scrum11147/RESULTS.md), [review](../../../artifacts/pf-opux-scrum11147/independent-review.md), [scoped diff](../../../artifacts/pf-opux-scrum11147/scrum11147-scoped.diff).

**Implemented**
- **Command.** The exact-target `AdjustTrimFromReview` names R1 and U by id + hash and is legal only from Trim ReviewRequired. It emits only the existing manual-crop effects with a tight margin.
- **R1.** Only leaves the step: no review, rejection or invalidation is written, and it stays valid, unreviewed history even if the crop fails. The new result R2 has U as its source and waits for its own review.
- **Eligibility and service.** Workflow-layer eligibility checks three-way U lineage, R1/U validity and retention, a raster source, geometry, and no descendants. The service re-checks eligibility, U integrity and that the crop fits U under one gate.
- **Editor.** Draws on U, never R1: inside-anchored handles, displacement drag with the system threshold, key nudges and border snap, dimming with an inset two-tone outline, Compare, and exact Restore from the stored automatic bounds. Use this trim and Cancel.
- **Around the editor.** Consequential actions are suppressed while editing, except read-only Open and Check saved. Plain Trim Approve goes through the exact entry. The R2 review, save target and focus follow correctly.
- **Unchanged:** the fallback manual crop; schema and migrations.

**Evidence** (settled candidate, isolated synthetic fixtures)
- Combined UI 477/477; workflow/trim 10228/10228; architecture 452/452; delivery backend 146/146; Unit UI 62/62; renders 16/16 (16 inspected PNGs); export-integrity 19/19.
- Six red mutations, each restored byte-identically.
- Design deviations are recorded in RESULTS §1: fitted Compare, toggle placement, 印刷尺寸, Tab order, the fits-U pre-check, member renames.

**Review.** One fresh read-only `personal-dev-reviewer` context (Opus High requested; the reviewer reported `claude-opus-5-5`, effort UNVERIFIED). It inspected but did not rerun tests. Round 1 was OPEN on one P2 (missing gesture/focus evidence), which was fixed; the same-context recheck was CLOSED. Its later P3 test gap was also fixed.

**Jira**
- One comment, [10171](https://yituoxx.atlassian.net/browse/SCRUM-11147?focusedCommentId=10171), marker `PF-OPUX-v1-SCRUM-11147-impl-v1`.
- Authenticated readback at **2026-09-27T21:29:59.658Z**: 17 issues, 26 Blocks, all To Do. Current exports are [SCRUM-11147_JIRA_READBACK.json](SCRUM-11147_JIRA_READBACK.json) and [SCRUM-11147_JIRA_FINAL.csv](SCRUM-11147_JIRA_FINAL.csv). Exporter and oracle both PASS (51 string labels, 34 raw timestamps).
- Drift: only SCRUM-11147 `updated` and comment 10171.
- The exporter gained only a narrow task mapping.

**Still open**
- **NOT RUN (native/human):** physical drag, Esc and lost capture; arrow keys versus scrolling; held or repeated keys onto Use this trim and then Approve; supported-workstation bilingual layout; novice walkthrough. A synthetic checklist is in RESULTS §5.
- **Residuals:** the KeepOriginalExtent PNG approval gap; pre-existing Trim Reject/KeepOriginalExtent exact binding without a fresh-gesture guard; inert Reject reason/notes while editing. At 1000×700 the adjust surface is small, which is the screen's existing height budget. Wave1A, SCRUM-11146 and SCRUM-11145 human checks remain open, as preserved below.

**Routing.** Claude adaptation v1.2 of policy v2.4, `route_offset: 0`. Opus High throughout; host model `claude-opus-5-5`, effort UNVERIFIED. No Sonnet unit.

**Repository.** `D:/Repositories/printflow-Studio`, `master`, HEAD `eea60198c5095243f0694717a7479b00c3e1cd73`. All work is uncommitted.
- 32 files changed against the pre-task tree (`artifacts/pf-opux-scrum11147/candidate-changed-files.txt`). Added afterwards: this handoff, the plan execution record, the two Jira outputs and the artifacts.
- Everything else is byte-identical to `baseline-all-files.sha256`.
- No checkout, stash, commit, push or deploy; no production app, data, migration or customer files; no desktop input or capture.
- Not started: SCRUM-11148, the PNG repair, any other Task.

**Stop at SCRUM-11147.**

The complete previous handoff is preserved verbatim below as dated history.

---

# PF-OPUX-v1 — SCRUM-11147 trim design current handoff

2026-09-25 NZ. Task `PF-OPUX-v1-SCRUM-11147-design-v1` (PLAN_ONLY). **AWAITING_OWNER_REVIEW.** This handoff covers a design and its independent review only. Nothing is authorized for implementation. Documents: [design](SCRUM-11147_TRIM_DESIGN.md), [review](SCRUM-11147_TRIM_DESIGN_REVIEW.md).

**Requirement source.** Authenticated read of SCRUM-11147 (issue 10877): To Do, updated 2026-09-22T14:27:25.050+1200, no comments. It matches the closeout snapshot. Jira was read only; no CSV was produced.

**Recommended interaction.** The trim review offers "Adjust trim edges", which opens the existing crop surface over the **pre-trim source U**, never the cropped result R1.
- The editor shows the current boundary with inside-anchored edge and corner handles, dims the area outside it, and reuses the existing zoom.
- Keyboard: arrow keys move the focused handle; Shift moves 10 px; Ctrl moves to the picture border.
- "Compare" shows the current result beside a display-only proposed crop.
- "Restore automatic suggestion" returns to the stored `TrimGeometry.AppliedBounds` of the automatic attempt on U, with margin Tight. Nothing is recomputed.
- "Use this trim" submits once. "Cancel adjustment" discards the draft.
- Nothing persists before "Use this trim".

**Persistent transition.** A new exact-target command, `AdjustTrimFromReview(R1 id+hash, U id+hash, crop)`, legal only from Trim `ReviewRequired`.
- It emits only the existing manual-crop effects (working copy, `ManualImport` attempt, `RunManualCrop` with margin Tight).
- R1 is taken off the step with no review decision and no invalidation, as `KeepOriginalExtent` does.
- R2 then waits for its own review.
- Eligibility lives in the workflow layer: geometry-bearing R1, three-way agreement on U, valid unreleased U and R1, no descendants of R1.
- Integrity is re-checked on U. The whole command runs under one gate acquisition inside `ExecuteAsync`, and stale revision ids are refused even when hashes match.
- The existing Failed/RetryRequired manual crop is unchanged. There is no Reject wrapper, no schema change and no migration.

**Saving and the PNG approval gap.**
- While the editor is open, approval, reject, save and the other consequential actions are hidden. The commit-time exact checks remain the authority.
- Plain Trim Approve is routed through the existing exact entry, `ApproveExactReviewAsync`.
- After R2 appears, the pending save target, `ConfirmAndSaveIdentity`, non-activating focus and the fresh-gesture guards all move to R2. "Use this trim" is itself fresh-gesture guarded.
- Delivered copies and delivery records are untouched.
- There is no dependency on the separate KeepOriginalExtent PNG approval gap, which remains open and unrepaired.
- Residual recorded for the owner: Trim Reject and KeepOriginalExtent do not bind the exact revision.

**Review.** One fresh read-only `personal-dev-reviewer` context (requested Opus High; actual model UNVERIFIED), rechecked twice in the same context.
- Round 1: OPEN, with 2 × P2 (R1 falsely marked Superseded; handles clipped at the picture border) and 7 × P3.
- Round 2: those fixed; one new P2 (a handle grab-offset jump) and 2 × P3.
- Round 3: **CLOSED**.
- One reviewer run ended early on a host rate limit and was resumed once.

**Owner decisions.** None blocks the design. The owner is asked to approve or revise it as a whole. The final wording is subject to the novice walkthrough.

**Routing.** Claude adaptation v1.2 of policy v2.4, `route_offset: 0`. Opus High throughout; host model `claude-opus-5-5`, effort UNVERIFIED. No Sonnet unit.

**Not run.** Builds, tests, the application, desktop, input, capture and migrations. All future evidence in design §12 is NOT RUN. Closeout exclusions and the human acceptance items preserved below remain open.

**Repository.** `D:/Repositories/printflow-Studio`, `master`, HEAD `eea60198c5095243f0694717a7479b00c3e1cd73`. The accumulated uncommitted work is preserved and untouched. This task changed only this handoff and added the two SCRUM-11147 documents. There was no product/test/tool/config change and no checkout, stash, commit, push or deploy.

**Stop.** A future, separately authorized task may implement design §13 slices 1–5 after owner approval. It must not start from this handoff alone.

The complete previous handoff is preserved verbatim below as dated history.

---

# PF-OPUX-v1 — SCRUM-11145 closeout current handoff

2026-09-25 NZ. Task `PF-OPUX-v1-SCRUM-11145-closeout-v1`. **The bounded regression and evidence closeout is complete. STOP; no autonomous continuation.** This is technical completion only, not operator acceptance or production readiness. [Closeout record](SCRUM-11145_CLOSEOUT.md), [review](../../../artifacts/pf-opux-scrum11145-closeout/review.md), evidence under `artifacts/pf-opux-scrum11145-closeout/`.

- **Label defect, corrected.** The scratch transformer's `-is [pscustomobject]` test matched pipeline-wrapped label strings, which were rebuilt as `{"Length": n}`; the exporter then accepted them. New `Build-JiraReadback.ps1`. The exporter refuses non-string labels and checks the Labels column after writing. New independent oracle `Test-JiraSnapshotFidelity.ps1` (System.Text.Json) and regression script `Test-JiraExportIntegrity.ps1` (19/19).
  - **Label-integrity limitation:** the preserved [SCRUM-11145_JIRA_READBACK.json](SCRUM-11145_JIRA_READBACK.json) and [SCRUM-11145_JIRA_FINAL.csv](SCRUM-11145_JIRA_FINAL.csv) have invalid Labels. They are byte-unchanged history and are superseded for current use by the closeout pair below.
- **Language leak, corrected.** The two test classes restored the resolved fallback (and the process default UI culture) instead of the raw state. The shared `Fixtures/OperatorCultureScope` restores the exact prior state, and an order-sensitive reproducer was red before the fix and green after. One combined noninteractive run including both classes: 415/415. Excluded classes and their reasons are in the closeout §2.
- **Architecture, corrected.** The migration-list expectation names the accepted 0018 (SQL untouched). The delivery P/Invokes moved verbatim into `NativeMethods.cs`. Architecture 452/452; backend, real NTFS and migrations 146/146.
- **P3 items against AC/design.**
  - Fixed: recorded-saves selector (R4); collision during a recheck keeps Check again (T2); dated history after a thrown save plus a failed refresh (T3); the list's language refresh.
  - Not a defect: F12, with a confirming test. Late progress got a deterministic test with mutation evidence.
  - Deferred with reason: two cosmetic wording/selection items and the backend Intent-collision residual.
  - Final-save and isolation suites: 61/61. en/zh-CN off-screen renders inspected.
- **Independent review.** Fresh read-only `personal-dev-reviewer` (Opus High requested, actual UNVERIFIED): CLOSED (no P0–P2). Its P3 notes were fixed or recorded; the same-context recheck was CLOSED.
- **Jira.** One comment, [10170](https://yituoxx.atlassian.net/browse/SCRUM-11145?focusedCommentId=10170), marker `PF-OPUX-v1-SCRUM-11145-closeout-v1`; 10169 untouched. Authenticated readback **2026-09-24T23:21:49.205Z**: 17 issues / 26 Blocks, all To Do.
  - Current exports: [SCRUM-11145_CLOSEOUT_JIRA_READBACK.json](SCRUM-11145_CLOSEOUT_JIRA_READBACK.json) and [SCRUM-11145_CLOSEOUT_JIRA_FINAL.csv](SCRUM-11145_CLOSEOUT_JIRA_FINAL.csv). Exporter PASS; oracle PASS against the raw response (51 string labels, IDs, full descriptions/AC, relationships, 34 exact timestamp strings).
  - Only drift: SCRUM-11145 `updated` and comment 10170.
- **Still NOT RUN (human acceptance):** Explorer selection, real picker, physical input, supported-workstation bilingual layout, removable-drive disconnect, novice walkthrough. The PNG approval gap (KeepOriginalExtent) remains open. Wave1A post-focus-fix desktop checks and SCRUM-11146 human acceptance remain open as preserved below.
- **Routing:** approved Claude adaptation v1.2 of policy v2.4, `route_offset: 0`; Opus High throughout. Host model `claude-opus-5-5`, effort UNVERIFIED; no Sonnet unit, no rules/config edits.
- **Repository:** `D:/Repositories/printflow-Studio`, `master`, HEAD `eea60198c5095243f0694717a7479b00c3e1cd73`. All work is uncommitted and preserved: 19 source/test/tool files plus these closeout documents changed against the pre-task tree; nothing else. No checkout/stash/clean/commit/push/deploy, production startup/migration, customer files or desktop input. No SCRUM-11147/11148, PNG repair or other Task.
- **Stop here.** A future separately authorized task should read this checkpoint and SCRUM-11145_CLOSEOUT.md, and preserve dirty work and historical evidence.

The complete previous handoff is preserved verbatim below as dated history.

---

# PF-OPUX-v1 — SCRUM-11145 final save UI current handoff

2026-09-24 NZ. **SCRUM-11145 bounded implementation, validation, independent review and authorized Jira synchronization complete. STOP; no autonomous continuation.** Technical completion only — not operator acceptance or production readiness.

- Built on the accepted, uncommitted SCRUM-11144 backend without changing its publication, recovery, schema or alias contract. Added: exact revision+hash approval and once-only PNG promotion entries on the session service (single gate acquisition, existing lawful cores), read-only destination preference and draft check, `FinalSaveCoordinator`, App folder picker and Infrastructure shell-selection ports, and a compact bilingual final-save section on the Session screen with truthful approval/delivery states, retries, reconciliation, history and Open containing folder. [Plan](SCRUM-11145_PLAN.md), [results](../../../artifacts/pf-opux-scrum11145/RESULTS.md), [review](../../../artifacts/pf-opux-scrum11145/independent-review.md), [final manifest](../../../artifacts/pf-opux-scrum11145/final-candidate-manifest.json).
- Final evidence: final-save suites **49/49**, affected UI **377/377**, delivery backend + composition **78/78**, isolated Wave1/cards 21/21 + 6/6, architecture 450/452 (the 2 failures read the unchanged 11144 files `WindowsDeliveryNative.cs` and migration 0018). Synthetic GUID data, temporary SQLite and NTFS folders, fake processors/picker/shell. Off-screen renders (en/zh-CN, 1000×700 and 1920×1040) inspected. A pre-existing combined-run culture leak in `OperatorWave1Tests`/`WorkflowPurposeCardTests` is documented, not changed.
- Independent review (fresh read-only Opus High context, actual model UNVERIFIED): rounds 1–2 found 6 P2 and several P3; all P2 fixed with regression tests (red evidence kept); round 3 **CLOSED**; T1 then fixed and confirmed. Open P3: single displayed history record, collision during a recheck, double-failure refresh, ineligible outputs not listed, no dedicated late-progress test.
- NOT RUN: Explorer visibly selecting the file, real folder picker, physical keyboard/mouse (fresh gesture, held key, double-click, stale release), bilingual layout on the supported workstation, removable-drive disconnect, novice walkthrough; UI classes that show real windows or run `ApplicationStartup`. Checklist in RESULTS.md. The unreviewed-PNG (KeepOriginalExtent) gap remains open. Wave1A post-focus-fix desktop checks and SCRUM-11146 human acceptance remain open as preserved below.
- Jira: one comment [10169](https://yituoxx.atlassian.net/browse/SCRUM-11145?focusedCommentId=10169), marker `PF-OPUX-v1-SCRUM-11145-ui-v1`. Final authenticated readback **2026-09-24T03:25:06.124Z**, 17 issues / 26 Blocks, all To Do; [JSON](SCRUM-11145_JIRA_READBACK.json) built from the preserved raw response, [CSV](SCRUM-11145_JIRA_FINAL.csv) from the existing exporter (exact 23 columns, UTF-8 BOM, 34 raw timestamps, PASS). Only drift: SCRUM-11145 updated/comment. Exporter received a narrow task-marker mapping. Earlier exports unchanged.
- Routing: approved Claude adaptation v1.2 of policy v2.4, `route_offset: 0`; Astra/Sol High map to Opus High; host model `claude-opus-5-5`, effort UNVERIFIED; no rules/config edits.
- Repository `D:/Repositories/printflow-Studio`, `master`, HEAD `eea60198c5095243f0694717a7479b00c3e1cd73`; all work uncommitted and preserved. No checkout/stash/clean/commit/push/deploy, production startup/migration, customer files or desktop input. No PNG approval repair, SCRUM-11147, SCRUM-11148 or other Task.
- **Stop at SCRUM-11145.** A future separately authorized task should read this checkpoint and RESULTS.md, and preserve dirty work and historical evidence.

The complete previous handoff is preserved verbatim below as dated history.

---

# PF-OPUX-v1 — SCRUM-11144 backend current handoff

2026-09-23 NZ. **SCRUM-11144 backend implementation, bounded verification and authorized reporting complete within the accepted local-NTFS/approval envelope. STOP; no autonomous continuation.** This is not product/operator acceptance or production readiness.

- The newly submitted owner request adopted existing SQLite delivery/attempt records with atomic ordered destination preference, the reviewed local fixed/removable drive-letter NTFS envelope, and temporary `ApprovalEvidenceMissing` for PNG without valid human approval. The historical design/review remain unchanged; approval repair/backfill and complete asset-route acceptance remain open.
- Backend now covers exact reviewed PNG/TIFF authority and dimensions, additive migration 0018, immutable RequestId/alias binding, session coordination, held-handle verified staging/no-replace publication, durable restart/unknown-ack reconciliation, availability, explicit successor-first replacement, identity-bearing progress and a disposable verified-selection lease. App composition only; no 11145 UI/coordinator/Explorer.
- Corrected affected verification: **138 PASS / 0 FAIL / 0 SKIP**, exit 0, including **10 real local NTFS adapter cases**. Synthetic access/disconnect/copy faults are separately labeled. Temporary databases and GUID-owned storage only; no production migration or App launch. Initial 131-case candidate, red regressions and failed native probes are preserved.
- One fresh independent **Astra 6.0 High** review found R1–R4 and E1; the same context rechecked all corrections and raw results: **CLOSED, no unresolved actionable finding**. It inspected source/assertions/hashes/TRX and did not independently rerun tests. [Plan](SCRUM-11144_PLAN.md), [results](../../../artifacts/pf-opux-scrum11144/RESULTS.md), [review](../../../artifacts/pf-opux-scrum11144/independent-review.md), [corrected manifest](../../../artifacts/pf-opux-scrum11144/corrected-candidate-manifest.json), [corrected diff](../../../artifacts/pf-opux-scrum11144/corrected-scoped-candidate.diff).
- Physical removable disconnect, genuine read-only media, power-loss durability, live reparse/mode-substitution probes and human/UI acceptance are unperformed. Excluded network/non-NTFS/path forms are unsupported. Wave1A post-focus-fix desktop checks and SCRUM-11146 human acceptance remain open as preserved below; do not retry their desktop probes from this handoff.
- Jira: one authorized [comment 10168](https://yituoxx.atlassian.net/browse/SCRUM-11144?focusedCommentId=10168), marker `PF-OPUX-v1-SCRUM-11144-backend-v1`, read back exactly once with identical body. Current authenticated snapshot **2026-09-23T07:04:26.141Z**, **17 issues / 26 Blocks**, all To Do. [JSON](SCRUM-11144_JIRA_READBACK.json) and [CSV](SCRUM-11144_JIRA_FINAL.csv): exact 23-column historical schema, UTF-8 BOM, full descriptions/AC/relationships, 34 unchanged raw timestamp strings. No unexpected Jira field drift; no status/AC/other field writes. Earlier exports remain unchanged.
- Routing: approved **v2.4**, `route_offset: 0`; actual backend **gpt-6-sol/high**, bounded native escalation **gpt-6-astra/high**, fresh independent review **gpt-6-astra/high**, runtime-verified. Root coordination/export remains pre-existing **gpt-6-astra/xhigh**, normal Sol Medium route unavailable in-place (`MODEL_SWITCH_UNAVAILABLE`); no claimed downgrade, silent 5.6 fallback or rules/configuration edits. [Routes](../../../artifacts/pf-opux-scrum11144/routes.json).
- Repository remains `D:/Repositories/printflow-Studio`, `master`, HEAD `eea60198c5095243f0694717a7479b00c3e1cd73`. Accumulated work is uncommitted and preserved. No checkout/stash/clean/commit/push/deploy, customer work, production migration/startup, desktop capture/input, PNG approval repair, trim transition or colleague re-import.
- **SCRUM-11145 is the next candidate, not authorized by this task.** A future separately authorized task should read this checkpoint, plan/design/review, actual current Jira requirements and relevant backend contracts; preserve dirty work and historical evidence. Do not execute historical commands merely because they appear below.

The complete previous handoff is preserved verbatim below as dated history. Its earlier owner-approval and routing statements describe those checkpoints, not the current authorization.

---


# PF-OPUX-v1 — delivery design handoff

2026-09-23 NZ. Task `PF-OPUX-v1-delivery-design-v1`. **PROPOSED / AWAITING_OWNER_REVIEW**; design only. No implementation authorization.

- [DELIVERY_DESIGN.md](DELIVERY_DESIGN.md) covers exact approved PNG/TIFF identity, proposed delivery/attempt persistence and destination preference, no-overwrite staging/publication, crash reconciliation, idempotency, cancellation and the SCRUM-11145 final-review/save/Open contract. [Independent review](DELIVERY_DESIGN_REVIEW.md): technically ready for owner review; both P2 findings resolved and affected corrections rechecked. Added restart delivery discovery and explicit missing-copy replacement, including successor-first idempotency. Owner approval remains pending.
- Current authenticated full Jira reads: 11144 updated `2026-09-22T14:27:19.196+1200`; 11145 updated `2026-09-22T15:00:19.787+1200`; both To Do with zero comments, read 2026-09-23 NZ. Amended 11145 approval-before-export applies. Jira remained read-only; no CSV was regenerated.
- Recommended: existing SQLite with bounded delivery and attempt records; remember a distinct last-successful external destination atomically with delivery. Exact source/approval is revalidated; a held, verified temporary file is published without replacement; final-file verification plus durable evidence precedes Saved. Retry reconciles publication before copying again.
- Owner decisions: approve/revise the proposed storage/recovery design; accept or expand the proposed local NTFS destination envelope; decide the asset-path compatibility gap. Current KeepOriginalExtent can lead from an unreviewed import to an internally Approved PNG without a human ReviewDecision. Proposed delivery refuses that case; a new approval contract needs separate authorization. Normal reviewed PNG and valid sibling TIFF sizes remain in the design.
- Next, only after owner review and separate implementation authorization: bounded implementation plan, then **11144 backend/persistence (Sol High)**, then **11145 UI/interaction (Astra High)**. No automatic continuation, migration or Jira mutation.
- Policy: approved local v2.3; explicit `ROUTE_PROFILE.route_offset: 0`. Root runtime is `gpt-6-astra/xhigh`, pre-existing; intended design route Astra High. **MODEL_SWITCH_UNAVAILABLE** disclosed; no claim that High or a later Sol downgrade actually ran. Fresh review used `fork_turns=none`; actual reviewer `gpt-6-astra/high` is runtime-verified.
- Repository: `D:/Repositories/printflow-Studio`, `master`, HEAD `eea60198c5095243f0694717a7479b00c3e1cd73`. Existing source/test/configuration work remains uncommitted and untouched. Design verification is document/source/AC inspection plus file integrity; builds, tests, migration, application/native probes and customer work are **NOT RUN**. No commit, push, deployment or scheduled continuation.
- Change scope: only the two new design/review Markdown files and this handoff. The other 802 existing tracked/unignored files have identical before/after path/content hashes; prior handoff text is retained below. Final reviewed design SHA-256: `FF4AEA4EDAAB78DCDB8C0A44D34AE51B41DBA33F5F5FB373830758531B930ACB`.
- Future startup: read this handoff, the design/review and approved global policy; run `git status --short`, `git branch --show-current`, `git rev-parse HEAD`; re-read current 11144/11145 requirements and only the relevant code. Preserve uncommitted work. Do not execute historical native-test commands merely because they are recorded below.

The previous handoff is preserved verbatim below as a **dated historical checkpoint preceding this design task**. Its evidence remains historical; its pending acceptance has not been run or closed by this task.

---

# PF-OPUX-v1 — SCRUM-11146 and Wave1A current handoff

2026-09-23 NZ. **SCRUM-11146 bounded implementation/reporting complete; human/native acceptance remains open.** Stop here. No autonomous continuation.

## SCRUM-11146

- Three existing cards now explain purpose, approved PNG/print-TIFF result and print-size requirements in en/zh-CN. Enhancement/background removal remain optional; no delivery promise. Same route order, steps, Choose/lock behavior/message, preview/output name and focus/approval source.
- Localized read-model properties and wrapping XAML; existing localization event refreshes the same card objects. Source delta: WorkflowSelectionViewModel.cs, WorkflowSelectionView.xaml, Strings.cs/.resx/.zh-CN.resx. New WorkflowPurposeCardTests.cs. Terminology reference updated narrowly.
- Targeted final evidence: **24 PASS / 0 FAIL / 0 SKIP** (6 new + 18 affected), successful build. Red: four missing-copy failures before implementation; raw intermediate test-authoring errors retained. No full suite, timestamp regression or production-startup/crop smoke.
- Existing off-screen WPF renderer produced eight PNGs (six distinct) at 1000x700 and 1920x1040, 96 DPI, en/zh-CN top/bottom. Astra High inspected them: purpose/result/size content visible, lower cards scroll-accessible. Synthetic layout/binding/Tab-stop/UIA evidence is separate from native/human acceptance.
- Actual OS input, physical keyboard/mouse, human supported-workstation visuals and novice validation **NOT RUN**. Original bilingual human visual AC remains open.
- One genuinely fresh isolated independent code review: **no actionable findings**. Requested gpt-6-sol/high; actual route **UNVERIFIED** because reviewer runtime metadata was unavailable. No correction/re-review needed; unchanged Wave1A code was not re-reviewed.
- [Plan](SCRUM-11146_PLAN.md), [full results](../../../artifacts/pf-opux-scrum11146/RESULTS.md), [independent review](../../../artifacts/pf-opux-scrum11146/independent-review.md), [scoped diff](../../../artifacts/pf-opux-scrum11146/scrum11146.diff), candidate hashes/TRX/logs/rendered PNGs under artifacts/pf-opux-scrum11146.

## Wave1A pending acceptance — preserved

- Bounded checkpoint for this request: **AWAITING_SAFE_DESKTOP**. No current operator confirmation and current native computer controls disabled. No launch/input/capture probe, no postfix run directory. All 14 source/launcher + 2 assembly hashes matched the focus-fix manifest before SCRUM-11146 edits; comparison is artifacts/pf-opux-scrum11146/wave1a-candidate-check.json.
- Earlier four en/zh-CN native Enter/Space approvals, ordinary controls, Tab navigation and F6/F7 focus stability **belong to the pre-focus-fix candidate only**.
- The initial visible-entry /Window focus defect was separately reproduced/fixed. Fixed candidate has historical **22 PASS / 0 FAIL / 1 explicit interactive SKIP** and independent fix review with no actionable findings. Its **native post-fix acceptance remains NOT RUN**. New SCRUM-11146 assemblies do not inherit historical input PASS.
- Pending fixed-candidate native checks: first attached review gets non-activating status/preview focus (not action/Window), zero unintended approvals; ordinary-button positive controls; en/zh-CN fresh Enter/Space on actual Approve with exact displayed job/step/revision/hash and persisted counts; subsequent review unaffected by prior gesture; same-target refresh/language changes preserve valid focus without approval.
- Pending physical cases: ordinary-button/fresh single-click, unrelated click, double-click remainder, genuinely held/repeating keys and stale releases across jobs/revisions, followed by valid fresh activation. Earlier screenshot errors (0x80004002), unavailable mouse geometry and unsupported held-key API are known limitations, not new product defects. The two mouse events during the historical pause have UNVERIFIED source/hit target and establish no acceptance.
- Pending visuals: bilingual review, Photoshop guidance and crop at supported workstation conditions. HUMAN_PHYSICAL_INPUT, HUMAN_WORKSTATION_VISUALS and HUMAN_NOVICE_VALIDATION remain NOT RUN. A person doing device/layout checks need not be a novice; novice usability remains separate.
- Preserve [WAVE1A_VERIFICATION.md](WAVE1A_VERIFICATION.md), [historical RESULTS](../../../artifacts/pf-opux-wave1a/interactive-20260923/RESULTS.md), raw host logs, FAIL/PASS and focus-fix evidence. The unchanged startup-based crop smoke remains excluded because its shared LocalAppData lease is not isolated.

Only after a currently safe idle operator-controlled desktop and supported input are established, reuse the existing launcher and [operator checklist](../../../artifacts/pf-opux-wave1a/ui-operator-checklist.md):

```powershell
& 'D:\Repositories\printflow-Studio\docs\codex\printflow-operator-ux-backlog\Start-Wave1AHost.ps1' -SafeDesktopConfirmed
```

Record the actual source/assembly candidate, display/work area/DPI/viewport, initial post-fix focus, physical input source, exact target/counts, repeat/stale/double-click results and bilingual visuals. Baseline requirement: 1920x1080 display, 1920x1040 work area, 100%/96 DPI. Historical host measured display1920x1080/DPI96 and viewport1184x686.8; work area/visuals were not verified. Those values are not current observations. No production launch, display changes, new capture/input framework or repeated failed probes. A new focus defect requires separately scoped fixing authorization.

## Jira synchronization and actual exports

Only SCRUM-11146 received one comment: [10167](https://yituoxx.atlassian.net/browse/SCRUM-11146?focusedCommentId=10167), marker PF-OPUX-v1-SCRUM-11146-dev-v1. Complete pre-write reconciliation found no comments; final readback verifies the exact body once. No issue fields/status/AC/assignment/parent/links changed.

Final authenticated initiative readback: **2026-09-22T23:12:09.631Z**, one complete page, 17 issues/26 Blocks, all To Do. [SCRUM-11146_JIRA_READBACK.json](SCRUM-11146_JIRA_READBACK.json) and [SCRUM-11146_JIRA_FINAL.csv](SCRUM-11146_JIRA_FINAL.csv) are the actual new files. Existing exporter and reparse checks pass: exact 23 columns, UTF-8 BOM, full descriptions/AC, identity/parents/relationships, 34 exact raw timestamp strings. One EVIDENCE_COMMENT_ONLY and sixteen READ_ONLY rows; no observed external field/count/link drift. Narrow exporter task/marker mapping added; no interface/schema/timestamp algorithm change.

Original [WAVE1A_JIRA_READBACK.json](WAVE1A_JIRA_READBACK.json)/[WAVE1A_JIRA_FINAL.csv](WAVE1A_JIRA_FINAL.csv) remain the preserved **2026-09-22T05:45:29.544Z** historical checkpoint; original comments 10165/10166 unchanged. Historical exports/import evidence remain immutable.

## Route, Git and stopping boundary

Codex policy v2.3, route_offset0. Root/UI actual gpt-6-astra/high verified from runtime turn_context (artifacts/pf-opux-scrum11146/route.json). Documentation/export re-evaluated to normal Sol Medium; in-place switch unavailable (**MODEL_SWITCH_UNAVAILABLE**), disclosed safe Astra fallback without claiming downgrade. Independent review used fork_turns=none with only requirements/scoped diff/direct evidence; actual reviewer route UNVERIFIED.

D:/Repositories/printflow-Studio, master at eea60198c5095243f0694717a7479b00c3e1cd73; all work uncommitted. Integrity checks preserve 136 historical files, unrelated starting changes, existing resource values and focus/approval source. No checkout/stash/clean/commit/push/deploy/status transition/production/customer work. No delivery SCRUM-11144/11145, trim transition SCRUM-11147, correction re-import SCRUM-11148 or other Epic implementation. Stop; no further work or follow-up scheduled.
