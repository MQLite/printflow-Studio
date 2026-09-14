# PF-FIX-MEITU-CONFIRM — execution plan

## Objective and evidence boundary

Recover the already-processed `FIX-FINE-HAIR-001` cutout from the retained Meitu 7.8.8.2
window, export it through the guarded route, validate and hash the decoded PNG, and register only
that exact output against its original A1 session. Preserve the failed attempt as historical fact.
The operator's acceptance applies to this observed cutout only. The completed portrait Enhancement
is execution evidence only and its quality remains unapproved.

Meitu 7.8.8.2 evidence remains nonpublishable. This task does not change an accepted preset,
`CandidateProblems`, production revalidation, prior run artefacts, or any release/deploy state.

## Correlation already established

- A1 session `01a09dc9-d68f-7f44-8f03-b1bd33cd06d1` owns source SHA-256
  `5A705FE390AF87D1D48A0554D4908C425D4703A8807CA78EC73AC0E55E3C8D8E` and attempt
  `01a09dc9-d787-7e7a-a96e-a9f10557911e`.
- The A1 busy captures show that distinctive fine-hair image in Background Removal. The retained
  completion capture shows the same object on Meitu's checkerboard cutout-result screen.
- The later v2 session without a BackgroundRemoval attempt is not the execution context for this
  retained result and is not used to deny the A1 observation.

## Stages and acceptance criteria

1. Preserve and inspect
   - Do not reset, close, navigate, or re-run either Meitu operation before export.
   - Confirm the A1 database state and canonical shared lease without mutation.
2. Reproduce at the actual caller
   - Add a tight regression showing that exact Save identity on a signed cutout-result screen is
     rejected by the generic loaded-editor predicate before Save is used as an identity probe.
   - Keep unknown screens and wrong identities fail-closed.
3. Correct the phase boundary
   - Permit the identity probe and export reacquisition from an exact signed Enhancement-result or
     cutout-result state, while retaining exact process/window, ownership, identity, modal and
     destination guards.
   - Report Save's role explicitly as identity probe or guarded export rather than inferring a
     processing phase from the helper name.
4. Verify locally
   - Run the focused identity/result/export tests first, then the affected Meitu suite and a Release
     build if scope warrants. Decide against a full suite unless the diff expands beyond this seam.
5. Recover the live cutout
   - Use a fresh paired local build and the canonical shared lease.
   - Confirm the current signed cutout-result state and exact `FIX-FINE-HAIR-001_副本` Save identity.
   - Export once without invoking Enhancement or Background Removal.
6. Validate and register
   - Decode the output as PNG, require the source canvas size and the existing real-transparency
     rule, verify the source hash is unchanged, and record the exported SHA-256.
   - Hand off the failed A1 session if required by the existing recovery contract; submit the exact
     PNG through manual-result import so a new attempt/Revision and review-required state are
     recorded without rewriting the failed attempt.
   - Apply the supplied operator acceptance only to that exact BackgroundRemoval Revision/hash.
     Do not approve the Enhancement result.
7. Cleanup and handoff
   - Verify export/result-surface cleanup, shared lease release, persisted Revision/review state,
     and readiness for the next ordinary operation without resetting Meitu solely for evidence.
   - Commit only the scoped repository changes locally; no push/install/deploy/publish.

## Routing

- Policy: personal development routing v2.3, execute supplied handoff.
- Normal/requested/execution route: `gpt-5.6-sol/high`; RouteOffset `0` (`UNCHANGED`).
- ActualRoute: `UNVERIFIED`; `MODEL_SWITCH_UNAVAILABLE`; continue in the current context.
- Independent agent review is unavailable because this task did not authorize delegation; use
  focused automated checks plus self-review.
