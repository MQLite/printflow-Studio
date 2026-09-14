# PF-CHECK-PORTRAIT-SIZE — Handoff

## Outcome

Classification C. The approved Meitu Enhancement contract predates the regression set and explicitly
accepts a decoded PNG whose width and height are each no smaller than the managed Working input.
`保持原尺寸` is present in the signed completion panel; PrintFlow invokes the exact `AI变清晰` module
and selects no dimension option. The 320 × 240 → 1280 × 960 reference is explicitly an observation,
not a multiplier contract.

The frozen v1 portrait manifest and `StandardRegressionSetWorkstationSmoke` later introduced
`enhancedOutputIsLargerThanSource`. The runner actually evaluates only
`produced.Facts.PixelWidth > 1200`; it ignores height and does not compare against source or manifest
facts. This unsupported expectation caused the retained 1200 × 1600 → 1200 × 1600 A1 output to fail
after the Product's own non-shrinking gate had accepted it and created a Revision.

Full evidence and the required compact table are in
[`meitu-portrait-enhancement-size-contract.md`](../../printflow/meitu-portrait-enhancement-size-contract.md).

## Preservation and next step

No code, generator, frozen manifest/index, preset, historical result, input/output, or build pair was
changed. The exact proposed correction is a separately approved `printflow-regression-v2` set that
retains the bytes and replaces the portrait expectation with
`enhancedOutputIsNotSmallerThanSource`, plus a runner/loader change that consumes that property and
compares actual source/output width and height. Equal, larger, and one-axis-smaller focused cases and a
Release build are required only after approval. The active v1 expectation remains unresolved for any
future execution; its A1 failure is historical fact and is not rewritten.

No live root-cause claim, Operator visual decision, notice handling, 7.8.8.2 acceptance, standard-set
rerun, production revalidation, or Jira change. Smallest next step: approve or reject the exact v2
expectation update recorded in the report; if approved, implement it as one bounded tooling/set
version change with a fresh controlled pair before any new live run.

## Execution record

Canonical `master` started at `3427d2d43d6c1274650646f3ef3e3cea9ff604de`; the untracked operator
prompt bundle was preserved. Documentation-only validation: `git diff --check`; tests/build not run.
Policy v2.3, `EXECUTE_HANDOFF`, `CONTINUE`, route_offset -1; NormalRoute gpt-5.6-sol/medium,
RequestedRoute gpt-5.6-sol/low, ActualRoute `UNVERIFIED`, `MODEL_SWITCH_UNAVAILABLE`. Self-review only;
no agent or context switch. No push/deploy.

**CONTRACT RESOLVED — SAME-SIZE ENHANCEMENT PERMITTED; EXPECTATION UPDATE STATUS EXPLICIT**
