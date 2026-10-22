# SCRUM-11173 whole-branch review package

Review BASE `65c5853e0e3f4f828b7851479bb418d043b0e44e` through `e850e73359ffc0451f69193376b584753bcdf903`. Worktree: `C:/Users/admin/.codex/worktrees/scrum-11173-guided-acceptance/printflow-Studio`. Branch: `codex/scrum-11173-guided-acceptance`. The sole fresh independent [review](../../../docs/codex/printflow-operator-ux-backlog/SCRUM-11173_REVIEW.md) is complete: 0 Critical, 0 Important, 1 deferred Minor E6-M1. The sections below remain the reviewed implementation evidence; final disposition is in that review record.

## Spec and scope

- [Original Epic and Q01–Q03 AC](../../../docs/codex/printflow-operator-ux-backlog/SCRUM-11173_JIRA_SCOPE.json), [approved execution plan](../../../docs/superpowers/plans/2026-10-22-scrum-11173-guided-acceptance.md), [results](../../../docs/codex/printflow-operator-ux-backlog/SCRUM-11173_RESULTS.md).
- User accepted E5 and requested E6 in the approved v2 style with Native sequential execution. Only necessary App-layer acceptance fixes are permitted. Domain/Workflow/Infrastructure, production, releases and other worktrees are outside scope.
- One product XAML change: shared output-history template; Save/Completed use the existing details scroll to protect the image. One new test file with 9 cases; one optional capture-state filter in an existing visual fixture. No VM or business/host changes.
- Code/tests frozen at `24f2b3bb18a6ae96f34fa39a572bd7a9dd9e8ebe`; later documentation/evidence commits do not change the candidate's scoped source. Controller freeze evidence is [controller-source-freeze.json](checks/controller-source-freeze.json).

## What warrants attention

1. Verify output-history facts and identities survive extraction/repositioning, there is exactly one materialized `Session.OutputList`, earlier review placement is retained, and new history scroll does not remove the image or current main actions. Inspect both Save and Completed at all three viewports.
2. Assess whether the new tests provide useful acceptance evidence without overstating peer names/candidate Tab sets as OS input, actual screen readers, DPI or outer-window client geometry. The approved 1000×700 refers to outer WPF Width/Height; actual smaller client area remains unmeasured.
3. Keep inherited failures and old defects separate from the App layout fix. Final broad is FAILED with 14 exact inherited names/messages; narrow success is not causal proof that N1–N4/F6/PNG/generic recovery or early E4 key anomalies are fixed.
4. Check candidate identity and safety: build + metadata + STATIC_VALIDATED/GraphConstructed=false only; no runtime root. PrepareAndSmoke and Interactive are NOT RUN. Full Interactive fake-TIFF path remains subject to N4/F6 qualification; no production-adapter fallback.
5. Check the Chinese owner material is executable once its explicit gates are met: neutral A01–A12 task cards, source/fixture limits, candidate validation, preparation/stop paths, anonymous independent-completion/help/error/verbatim fields, actual nine-cell outer-window/DPI matrix, owner visual signoff and defect/regression record.
6. Preserve actual partial status: user now explicitly deferred human testing (“先准备走查材料，真人测试稍后安排”); participant not confirmed, novice/workstation observations NOT RUN. Q01/Q03 and Epic acceptance are incomplete. Technical readiness is not release, production or old Epic closure.

## Evidence to inspect

| Evidence | Meaning |
| --- | --- |
| [Actual RED](checks/e6-multisize-red.trx) and [GREEN](checks/e6-multisize-green.trx) | Image collapsed to 1 DIP / about 34 DIP; 2 failures then 39/39 related checks |
| [Final Q01](checks/e6-q01-final.trx) | 45/45, including all 9 new cases and selected adverse-state visual tests |
| [Final broad](checks/e6-ui-architecture-final.trx) | 1494 total / 1480 pass / 14 fail; never a blanket PASS |
| [E5 comparison](checks/e6-final-vs-e5.json), [baseline comparison](checks/e6-final-vs-baseline.json) | Exact names and raw messages; new/missing/changed=0 |
| [Final persistence](checks/e6-workflow-persistence-final.trx) | 277/277; exactly the same E5 filter; actual counters in RESULTS and TRX |
| [AC map](COVERAGE.md), [executed test map](checks/coverage-map.json), [old failure list](inherited-failures.md) | Exact parameterized cases, source TRX hashes and limits; overlapping groups must not be added as unique tests |
| [Visual index](index.html), [inspection](visual-inspection.md), [manifest](visual-manifest.json) | 17 individually inspected final PNGs + 17 geometry files at offscreen 96 DPI |
| [Pre-fix images](visuals-before-fix/multisize-long-name-1000x700.png) | Initial tests were insufficient; preserved evidence must not be mistaken for final output |
| [Candidate](candidate/README.md) | Immutable build-source commit, source/binary/MVID identity, static validation and explicit runtime limitations |
| [Walkthrough](../../../docs/printflow/guided-acceptance-walkthrough.md), [maintenance](../../../docs/printflow/guided-acceptance-maintenance.md) | Actual owner/novice work is unexecuted; explicit future gates |
| [Rulings](RULINGS.md), [ledger](execution-ledger.md) | Scope decisions, actual RED versus compile/fixture corrections, routing limitations |

The initial 9/9 and 15/15 runs were characterization with insufficient multi-size image assertions. `e6-q01-initial.log` is a test compile error; `e6-q01-final-visual.trx` has 3 fixture cross-STA failures (42/45). They were corrected in the test fixture, using the established E5 snapshot pattern; there was no product threading fix. Final source has not changed since successful final Q01 evidence.

## Verification boundary

Use PowerShell 7 from the E6 root. `& ./output/ui/SCRUM-11173/checks/verify-evidence.ps1 -Phase Final` reads counters, comparisons, hash manifests and candidate identity without running desktop tests. Any additional justified test must use `run-safe-ui.ps1`; preserve all 11 exclusions and the whole `PrintFlow.Tests.Smoke.` namespace. Never use unfiltered `dotnet test`, PRINTFLOW opt-ins, Window.Show/ShowDialog, real App, desktop input, external apps or production data. Do not run PrepareAndSmoke to expand review coverage.

The controller will record the independent review, any justified minimal fix pass, its verification and final Jira disposition separately. Preserve existing controller files, scratch, raw evidence and all protected worktrees. Do not push or merge.
