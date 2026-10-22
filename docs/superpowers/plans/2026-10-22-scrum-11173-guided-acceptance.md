# SCRUM-11173 Guided Acceptance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Native sequential execution is already authorized; the parent owns one final fresh whole-branch review.

**Goal:** Establish reproducible Q01/Q02 technical evidence and owner-ready Q03 Chinese acceptance materials without substituting synthetic evidence for human/workstation acceptance.

**Architecture:** Reuse the accepted guided App and existing synthetic fixtures. Add narrowly missing accessibility/layout assertions; change App code only for demonstrated acceptance defects, with RED→GREEN. Keep domain, workflow and infrastructure unchanged.

**Tech Stack:** .NET 10 (SDK 10.0.400), WPF offscreen UserControls, xUnit, synthetic SQLite/fake adapters, PowerShell.

**Spec:** `docs/codex/printflow-operator-ux-backlog/SCRUM-11173_JIRA_SCOPE.json`; approved v2 design and accepted E5 evidence are binding references.

## Global Constraints

- Work only in the existing `codex/scrum-11173-guided-acceptance` worktree; BASE `65c5853e0e3f4f828b7851479bb418d043b0e44e`.
- Preserve one image/task, three routes, originals/InputSnapshot, exact approval identity, invalidation, environment/automation gates, explicit W1, 300 PPI/CMYK and privacy.
- Every test command uses `output/ui/SCRUM-11173/checks/run-safe-ui.ps1`, retaining all 11 exclusions and the entire Smoke namespace.
- No real App launch, Window.Show/ShowDialog, desktop input, external apps, opt-ins, customer/production data, network/AI/uploads, push/merge/release/deploy.
- Chinese visual evidence at 1000×700, 1280×720, 1920×1080 DIP is offscreen UserControl viewport at 96 DPI; approved 1000×700 is an outer WPF window baseline, whose actual smaller client area is unmeasured; real 100/125/150% DPI, keyboard/mouse/readers and novice acceptance remain NOT RUN.
- Legacy F6/N1–N4/PNG/generic recovery and early E4 key instability retain evidence-based status. Existing broad failures may remain only with exact baseline comparison.
- Retain scratch/ledger; no per-task reviewer. Parent performs the single fresh final review.

## Review Focus

1. Offscreen/UIA-peer success cannot prove OS keyboard, screen reader or real DPI: keep explicit pending matrix and boundaries.
2. Long Chinese and multiple saved targets can crowd main actions/images: add representative geometry evidence at minimum and larger DIP sizes.
3. Hidden or stale action providers must not approve a different result: reuse identity/gesture suites and check hidden navigation surface semantics.
4. Historic failures disappearing in a narrow run do not prove a fix: compare exact names/messages in both broad runs.
5. A novice candidate must use the current guided App safely: inspect supported WorkstationEntry host, build/hash/validate only, and bind human prompts to its actual fixture/limitations.

### Task 1: Baseline and missing Q01 evidence

**Files:** create `tests/PrintFlow.Tests/Integration/Ui/GuidedAcceptanceTests.cs`, scoped evidence under `output/ui/SCRUM-11173`; modify App only after demonstrated RED.
**Interfaces:** consumes existing Guided* fixtures, WpfRendering, safe wrapper; produces baseline comparison, targeted TRX and Chinese geometry/screenshots.

- [x] Run safe broad `-RunName e6-baseline`; expected 1485 total, 1471 pass, 14 inherited failures; compare actual names/messages with E5.
- [x] Audit existing identity/gesture/layout coverage. Add smallest missing tests for accessible names, tab eligibility/hidden sections and long Chinese/multisize image/action geometry. Expected: PASS if already correct; evidence-only characterization needs no artificial RED.
- [x] For any demonstrated App defect, retain failing test/log, apply minimal fix, rerun targeted safe checks. Expected: actual assertion RED then GREEN.
- [x] Reuse existing visual fixtures for selected adverse states; inspect each final PNG and record hashes/geometry, no bitmap scaling masquerading as DPI.
- [x] Commit scoped tests/App/evidence.

### Task 2: Q02 regression and exact classification

**Files:** create `output/ui/SCRUM-11173/COVERAGE.md`, comparisons and checks.
**Interfaces:** consumes Task 1 frozen source plus inherited persistence filter; produces final raw logs/TRX and AC-to-test map.

- [x] Run final broad safe UI/Architecture once after source freeze; expected inherited 14 only plus newly added passing cases.
- [x] Run same E5 persistence selection through safe wrapper once; expected 277/277.
- [x] Compare exact baseline/E5/final failure names and raw messages. Document every unchanged/new/disappearing failure and legacy classification separately.
- [x] Map three routes, skip/reject/retry/return/correction/restart/multisize/save conflicts and invariants to exact executed tests; retain uncovered device/production boundaries.
- [x] Commit evidence and coverage.

### Task 3: Q03 owner candidate and acceptance handoff

**Files:** create `docs/printflow/guided-acceptance-maintenance.md`, `docs/printflow/guided-acceptance-walkthrough.md`, E6 RESULTS/HANDOFF, RULINGS and REVIEW_PACKAGE.
**Interfaces:** consumes frozen source, supported WorkstationEntry, Q01/Q02 evidence, readonly SCRUM-11155 sources; produces reviewable technical conclusion and explicitly incomplete human record.

- [ ] Inspect launcher/host safety and fixture support. Build isolated candidate without launch; prepare metadata and Validate only when confirmed headless. Expected: build/validation success, no run root/window/external adapter activation; document exact limitation if unavailable.
- [ ] Prepare Chinese participant confirmation, no-coaching prompts, independent completion/help/errors/verbatim response forms, actual nine-cell DPI matrix, defect severity/owner/regression register, 11155 scenario map and safe start/stop instructions.
- [ ] Verify links, hashes and candidate identity; record incomplete actual novice/workstation status and remaining conditions without fabricated timings/KPIs.
- [ ] Commit scoped deliverables; preserve scratch and send exact HEAD, package, raw results and rulings to parent for one fresh final review.


