# SCRUM-11171 independent whole-diff review

- Review BASE: 5c061dd15f23d2cc33a7e62dca9e130296b14a8a
- Reviewed HEAD: 685b8d5ee59877498bef45a51c6bf6fc91d49f8b
- One fresh reviewer: /root/e4_overall_review; fork_turns=none; session_meta forked_from_id=null.
- Actual runtime: gpt-6-astra / high, turn_context 2026-10-19T21:23:36.318Z, session 01a1560c-5fc3-76a1-a054-691bd554f25e.
- Read-only review of all six product files and seven changed/new test files, original E4/P01-P05 AC, approved v2 design, raw test evidence and 18 representative final screenshots with geometry.
- Original verdict: CHANGES REQUESTED. Critical 0; Important 1; Minor 0.

## R1 — Important: crop command failure feedback is hidden

SessionScreenView.xaml:1106 collapses SessionDetailsScroll on Crop, including Session.ActionFailure. A refused UseThisTrim command can preserve the same result, editor and draft; the top generic Notice suppresses coded failures (XAML:340), and crop StatusReason is empty. The operator remains in the editor with no visible reason or error-code disclosure.

Evidence: RunServiceAsync sets coded failure and refreshes (SessionViewModel.cs:3390-3401); Show preserves the same-target editor (3527-3545). Processing has an explicit replacement failure surface; Crop does not.

Required correction: expose the existing coded failure surface in the crop workspace or outside the collapsed column. Meaningful offscreen regression must cover refused crop preserving its draft, including reload failure. Keep existing failure semantics, exact identities and business rules.

Parent verified the collapsed-column placement and accepts this as an in-scope Important regression. Disposition: RETURNED TO ORIGINAL NATIVE EXECUTOR for receiving-code-review/TDD correction. Final verification pending.

## Remaining AC and evidence assessment

Processing, common exact-result review, crop cancel/apply identity, dimensions/raw values/enlargement authority, both-route explicit white ink and preflight gates align with E4. Material PNG remains excluded. No business engine, approval control, Domain/Workflow/Infrastructure changes.

Disclosure test adaptations preserve substantive geometry, command identity and authority assertions. Final-save comparison appropriately reloads current session. Programmatic disclosure expansion does not prove physical keyboard reachability.

Reviewer independently compared final broad raw TRX with E3: 1424 pass / 14 fail; all 14 exact name/message matches. Persistence 277/277, contracts 76/76, visual 52/52 reviewed. Two additional first-broad approval failures expected fresh activation and received zero; subsequent targeted/final broad runs passed unchanged. Cause UNRESOLVED; neither fixed nor proven inherited. Relevant gesture code/tests unchanged.

18 final Chinese PNGs inspected including minimum review/crop/compare/dimensions/white/preflight/running/stopping/takeover/recovery/invalid/enlargement and representative larger views. Correct final stopping is screens-runtime; all evidence is synthetic offscreen 96 DPI.

## Declined to judge

No newly identified regression declined as out of scope. Existing F6 and the 14 matching E3 failures remain existing work. Intermittent approval failures remain validation uncertainty.

Live keyboard/UIA-device, high-DPI, workstation, novice and production acceptance: NOT RUN. No application launches, desktop input, source changes or tests performed by reviewer. Checkout remained clean at reviewed HEAD.


## R1 executor correction disposition

The same Native executor independently reproduced R1 through UseThisTrim and the real SessionService, failing the first repository commit with the existing FaultingRepository. Four zh-CN/en × successful/failed reload cases preserved the editor, crop draft, exact result and persisted records; each failed before the fix because the visible failure notice count was zero. No direct Notice injection was used.

Correction: 21 XAML lines in SessionScreenView place the existing coded failure text and a read-only code disclosure inside the crop workspace, with unique Session.Crop.* IDs. ViewModel, service, crop geometry, exact approval controls and recovery behavior are unchanged. Two Chinese 1000×700 captures show the message and expanded code alongside the preserved draft and required controls; both were individually inspected and hash-verified.

Executor verification: r1-crop-failure-red-behavior 0 passed/4 expected failures; r1-crop-failure-green 4/4; r1-crop-failure-affected 251/251, zero failures/skips. Initial compile nullability typo is separately retained and is not behavioral RED. All 30 final screenshot hashes match. The pre-correction full broad 1424/14 and persistence 277/277 are retained as pre-correction evidence, not claimed rerun. Early intermittent activation failures remain unresolved.

Disposition: **R1 CORRECTED AND VERIFIED**. Original executor affected regression 251/251; parent final source verification 34/34 at 62afc10f5ddd32d0f37812179735e96cd52d3f1a, including the crop failure regression and unchanged activation guards. Parent inspected the 21-line XAML correction and both Chinese crop-error screenshots; all 30 final PNG hashes match. This records correction verification and does not claim a second independent whole-diff review. Jira E4 and five stories are In Review, pending user acceptance.
