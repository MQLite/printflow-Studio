# SCRUM-11173 Native handoff

User authorization: “SCRUM-11172 通过。请继续推进 SCRUM-11173，沿用已批准的视觉风格和 Native 执行方式。” Only E6 is authorized. Do not push, merge, deploy, publish, operate production, start a later Epic, or change other worktrees.

Worktree: `C:/Users/admin/.codex/worktrees/scrum-11173-guided-acceptance/printflow-Studio`; branch `codex/scrum-11173-guided-acceptance`; BASE `65c5853e0e3f4f828b7851479bb418d043b0e44e`. Code/test freeze `24f2b3bb18a6ae96f34fa39a572bd7a9dd9e8ebe`; later commits contain evidence/documentation only. Candidate build commit and scoped hash identity are in [candidate README](../../../output/ui/SCRUM-11173/candidate/README.md). Documentation-only HEAD advancement does not alter those source/resource or DLL identities.

## Current result

Jira final readback: SCRUM-11172 Done; SCRUM-11196 In Review for technical evidence; SCRUM-11173/11195/11197 In Progress. User deferred actual novice testing. No merge/push/deploy or other-Epic closure. Receipt: `output/ui/SCRUM-11173/checks/jira-final-status.json`.

One Native implementer completed the technical preparation; the sole fresh whole-branch [review](SCRUM-11173_REVIEW.md) is complete: 0 Critical, 0 Important, 1 deferred Minor (candidate helper overwrites fixed evidence on rebuild). The partial technical handoff is acceptable; fix evidence-path preservation before any later rebuild. [RESULTS](SCRUM-11173_RESULTS.md), [coverage](../../../output/ui/SCRUM-11173/COVERAGE.md), [visual index](../../../output/ui/SCRUM-11173/index.html) and [review package](../../../output/ui/SCRUM-11173/REVIEW_PACKAGE.md) are the entry points.

The only product change is a shared output-history template in `SessionScreenView.xaml`: Save/Completed show the existing output facts inside the details scroll so two output rows cannot collapse the image. Earlier review placement stays unchanged. Domain, workflow, infrastructure and host implementation are unchanged. Nine new parameterized cases cover peer names/candidate tab eligibility, long Chinese and two-size Save/Completed geometry/reachability. An existing visual fixture gains screenshot-state selection without filtering assertions.

Q01 final targeted is 45/45; actual product RED was 2/3, followed by 39/39 GREEN. Initial 9/9 and 15/15 were incomplete assertions, and the subsequent 42/45 fixture failure is separately retained. All 17 final PNGs were individually inspected; 34 image/geometry manifest entries match. Read the final broad/persistence counters and exact inherited comparisons in RESULTS, not a success-only summary.

## Boundaries that must survive review

- Broad failures must remain explicitly FAILED and compared by exact parameterized name and raw message with both E5 and E6 baseline. No added exclusions: entire `PrintFlow.Tests.Smoke.` plus the same 11 exclusions.
- Offscreen 1000×700 is a UserControl viewport, while approved 1000×700 is an outer WPF window. Actual smaller client area and the real 100/125/150% matrix are NOT RUN. Direct peers/routed events do not prove physical keyboard, mouse, Windows UIA clients or screen readers.
- User explicitly chose “先准备走查材料，真人测试稍后安排” on 2026-10-22. Human testing is DEFERRED_BY_USER; participant not yet confirmed; observations and owner visual signoff NOT RUN. Use the [walkthrough](../../printflow/guided-acceptance-walkthrough.md) forms; developer/synthetic evidence cannot fill them. Do not mark E6/Q01/Q03 fully accepted.
- Candidate build/manifest/Validate is static-only. PrepareAndSmoke constructs Application/MainWindow and was not run; Interactive requires fresh desktop permission and matching prepared evidence. Current fake TIFF runtime needs qualification; headless F2/F3 setup does not carry into a new Interactive process.
- N1–N4, F6, PNG KeepOriginalExtent approval/binding, generic recovery/history hang, early E4 key anomalies and E5 M1 remain separately classified. No old Epic closure or production certification.
- Keep scratch, raw failures, pre-fix screenshots, controller protection/route evidence and Jira receipts. The controller owns external status changes; this implementer sent none.

## Controller completion

The single fresh Astra High review is complete; do not dispatch another review merely to resume this handoff. Minor E6-M1 is deferred and recorded in REVIEW/RULINGS: preserve the existing candidate and correct the helper's evidence-path isolation before any rebuild. Run only justified safe verification; do not repeat broad tests or rebuild the candidate for documentation-only commits. The existing candidate validator checks file hashes; preserve the immutable build-source commit separately from the final docs HEAD.

Any actual host preparation or novice/workstation session needs the documented owner gate. If unavailable or unexecuted, preserve partial status and explicit conditions rather than inventing completion. [Maintenance](../../printflow/guided-acceptance-maintenance.md), [rulings](../../../output/ui/SCRUM-11173/RULINGS.md) and retained ledger explain reproducibility and routing. Q02 strategy/docs stayed in the verified stronger Native context with `MODEL_SWITCH_UNAVAILABLE`; no actual downgrade is claimed.
