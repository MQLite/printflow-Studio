# PF-CHECK-PORTRAIT-SIZE — Meitu portrait Enhancement size contract

**Disposition:** Classification C. The approved Enhancement contract permits a same-size output.
The frozen v1 portrait manifest and its runner assertion later imposed a larger-only expectation
without requirement or preset authority. This report does not change the frozen set, accepted preset,
Product adapter, historical run, or any acceptance status.

## Compact plan and authority

Verify local Git and retained-file identities; read only the named Jira rows, approved Meitu contract,
accepted preset/evidence, portrait manifest, generator, runner, and direct output-validation chain;
identify the assertion's origin and operands; classify the conflict before considering a correction;
record the versioned expectation change required to keep the active set and runner aligned. No desktop
interaction, lease operation, external application, notice work, or acceptance rerun is part of this
plan.

Local `master` started at `3427d2d43d6c1274650646f3ef3e3cea9ff604de`, 24 commits ahead of
`origin/master`. The operator-owned `printflow-remediation-prompts/` directory was the only untracked
item and remains unchanged. The source CSV was read from the already-established local authority at
`C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv`. Its IDs are not Jira keys:
the repository's ordinal mapping makes CSV Work Item 11005 / sixth data row SCRUM-11065, and CSV Work
Item 11303 / twenty-ninth data row SCRUM-11088.

## Requirement and evidence table

| Source | Exact statement | Required or observed | Scope | Consequence for this portrait |
|---|---|---|---|---|
| Original CSV, Work Item 11005 → SCRUM-11065 | Record the expected processing path and relevant expected properties for each of seven fixed categories, including a normal JPG portrait. | Required | Regression-set authoring; it does not state an enlargement rule. | The set must record a justified portrait expectation, but this row supplies no larger-only expectation. |
| Original CSV, Work Item 11303 → SCRUM-11088 | Start Enhancement, wait for observable completion, export to a predetermined Working path, and validate before success; unknown screens stop. | Required | The exact Meitu AI Enhancement automation. | Successful invocation/completion/export is required; enlargement is not named. |
| Original CSV, Work Item 11305 → SCRUM-11090 | The expected file must exist, settle, fully decode/read, have reasonable format and dimensions, and hash before a Revision is created. | Required | Meitu export validation for any Revision. | A non-shrinking decoded PNG satisfies the approved concrete interpretation; “reasonable” does not mean “strictly larger.” |
| Accepted preset 1.17.0 and `apps/meitu/editor-enhancement.json` | Route `acceptedWorkflows.aiSharpen.featureLabel = AI变清晰`; the completion panel includes `高清` and `保持原尺寸`; the route invokes the signed `AI变清晰` ModuleButton and selects no dimension option. | Required route plus observed UI | Preset hash `A2E1936B…FCFA9`; evidence hash `912CC4D6…BB74E`. | PrintFlow requests Enhancement, not a separately configured enlargement or super-resolution mode. The signed route contains no promise that every input grows. |
| Approved Part B2B contract, §§16–18; final Epic 11300 gate §8; `MeituEnhancementOutputRule.Validate` | `output.Width >= source.Width` and `output.Height >= source.Height`; “not smaller”, not a multiplier. The panel's `保持原尺寸` makes unchanged dimensions legitimate. | Required | Decoded exported PNG compared with the managed Working input inspected before Meitu. | 1200 × 1600 → 1200 × 1600 is dimensionally valid. A smaller width or height remains invalid. |
| Part B2B retained reference | One 320 × 240 synthetic input produced 1280 × 960; the report says the 4× scale is one observation, not a contract. | Observed | One different PNG, settings, and accepted 7.8.7.5 run. | It cannot turn this portrait into a larger-only requirement or establish a controlled 7.8.8.2 comparison. |
| Frozen local `FIX-PORTRAIT-001` v1 manifest and generator, introduced by `ae3f090` | `enhancedOutputIsLargerThanSource = true`; prose calls “larger than the source” structural. | Later authoring choice | Normal-portrait fixture only. Commit `523f25a` containing the non-shrinking contract is its ancestor. | This is the unsupported conflicting expectation. The manifest is immutable historical authority for v1, so it is not edited in place. |
| `StandardRegressionSetWorkstationSmoke.RunEnhancementCaseAsync`, also introduced by `ae3f090` | `produced.Facts.PixelWidth > 1200`; height appears only in the message. | Implemented assertion | Post-Enhancement `SessionView.CurrentArtefact`. It does not read the source facts or the manifest's property. | The assertion fails equal width, ignores height as an operand, and hard-codes this fixture. It does not implement either the Product's two-axis non-shrinking rule or a general larger-canvas rule. |
| Retained A1 result | Output PNG 1200 × 1600, size assertion false; Enhancement review, PNG, promoted Revision, source preservation, and lock assertions true. | Observed | Run `a1-meitu-7882-20260914-143954-d12383c8` under a nonpublishable 7.8.8.2 exception. | The recorded case failure is real under frozen v1, but it is not evidence that Enhancement had no visual effect or that the wrong control was used. |

## Actual comparison path and operands

`SessionService` creates an attempt-owned managed Working copy and passes that `WorkspaceFileRef` to
`ProductionMeituProcessor`. Before Meitu receives it, `WicFileInspector` reads the Working copy's
hash and `PixelWidth`/`PixelHeight`. After the signed Enhancement and export, the output must settle;
the same inspector decodes the exported PNG. `MeituEnhancementOutputRule` compares both decoded pixel
axes against that pre-operation Working input and refuses either axis when smaller. This occurs before
adapter success. `SessionService` then independently inspects the returned output and carries those
facts into the Enhancement Revision and `SessionView.CurrentArtefact`.

The regression assertion runs after `StartStep(Enhancement)` returns `ReviewRequired`. Its left
operand is the Enhancement Revision's decoded `produced.Facts.PixelWidth`; its right operand is the
literal `1200`. It does not compare the manifest source, imported Revision, managed Working copy,
height, DPI, preview, thumbnail, promoted Revision, or screenshot. Promotion happens later and copies
the approved bytes unchanged. All dimension rules here are pixel-canvas rules; portrait orientation is
represented by width 1200 and height 1600. DPI is metadata and is not a comparison operand.

## Retained file measurements

The files named by the original run were inspected read-only through the retained A1 harness's
`WicFileInspector`; this composes no workspace, lease manager, UI Automation, COM application, or live
verifier.

| File | SHA-256 / bytes | Decoded facts |
|---|---|---|
| `D:\PrintFlowStudio\TestData\v1\inputs\FIX-PORTRAIT-001.jpg` | `F4CAD2A1EC7994E42E2A77CC6F30E29DE91D344821E4EE7AC01C7E712D9A4634` / 130,111 | JPEG, 1200 × 1600 px, 300 × 300 DPI, RGB, no alpha |
| `D:\PrintFlowStudio\TestData\v1\runs\a1-meitu-7882-20260914-143954-d12383c8\FIX-PORTRAIT-001-enhanced.png` | `6348E70A06441930520006EA3753254664D69B43218CB7D84D75B94BD81FB776` / 772,859 | PNG, 1200 × 1600 px, approximately 96.012 × 96.012 DPI, RGB, alpha-capable |
| Run `result.json` | `F614FB7904F6C182A784E140FDFA6E1E90C2F52B7D4FD949F117F1F1E358D047` / 21,415 | Records the same dimensions and assertion outcomes above. |

The input hash matches its frozen manifest and the output/result hashes match the A1 report. A separate
read-only WIC decode to BGRA32 found that the two 1200 × 1600 rasters are not pixel-identical. That
distinguishes the export from a pixel-identical copy; it does not establish visual improvement, explain
the changed pixels, or answer `PORTRAIT-VISUAL-001`. Different encodings, DPI metadata, and alpha
capability likewise prove no Enhancement quality.

## Disposition and bounded correction

**Classification C — the approved contract explicitly permits same-size Enhancement, and the
larger-only regression expectation is an unsupported later assumption.** The live root cause is a
separate question: this disposition does not establish why 7.8.8.2 returned the same canvas, whether a
particular option was selected by Meitu, or whether the result looks acceptable.

No runner or generator correction is applied in this slice. Changing the hard-coded runner to pass
same-size output while the active, content-bound v1 manifest still says “larger” would make code and
the frozen set silently disagree. Editing that manifest or `set.json` in place would rewrite accepted
historical authority, which is explicitly prohibited.

The exact proposed, approval-dependent correction is a new versioned set, not a v1 mutation:

1. Create `D:\PrintFlowStudio\TestData\v2` with `setId = printflow-regression-v2`, `setVersion = v2`,
   and portrait `fixtureSetVersion = v2`; copy the seven accepted input/reference bytes unchanged and
   recompute/bind their existing hashes.
2. In only the v2 portrait manifest replace
   `enhancedOutputIsLargerThanSource: true` with
   `enhancedOutputIsNotSmallerThanSource: true`. Replace the comparison prose with the two-axis rule
   from approved Part B2B §§16–18 / final gate §8 and retain the PNG, Revision, source-preservation,
   lock, and Operator visual checks unchanged.
3. Update `New-PrintFlowRegressionManifests.ps1` to emit that v2 expectation. Update the runner/loader
   together so the runner requires and consumes the named manifest property, compares the actual
   managed source facts with the decoded Enhancement Revision facts on both axes (`output width >=
   source width` and `output height >= source height`), and records assertion name
   `enhancedOutputIsNotSmallerThanSource`. Missing, legacy, or contradictory expectation data must
   fail preflight rather than default to green.
4. Prove the future caller boundary with synthetic equal-size and larger outputs that pass, and a
   one-axis-smaller output that fails. Then run only the affected regression tests and required Release
   build. Any live use of changed harness code requires a fresh controlled build pair and a new RunId.

Until that versioned change is explicitly approved and completed, the active v1 expectation is
**UNRESOLVED FOR FUTURE EXECUTION**: its historical A1 failure remains unchanged, and no new v1 run
should be represented as applying the corrected contract. This is an expectation-governance update,
not a Product adapter change or Production acceptance.

## Validation, routing, and stop

Documentation only; Product code, tests, generator, local manifests/index, preset, retained output,
and both build pairs are unchanged. No build or test was required or run. Scoped validation is
`git diff --check`; self-review only. No standard set, external application, desktop input, lease,
production revalidation, version acceptance, Jira update, install, deploy, or push occurred.

Policy v2.3, `EXECUTE_HANDOFF`, `CONTINUE`. NormalRoute was gpt-5.6-sol/medium for bounded contract
diagnosis; explicit RouteOffset -1 requested gpt-5.6-sol/low. The current context exposes no real
in-thread model switch or verified execution metadata: `MODEL_SWITCH_UNAVAILABLE`, ActualRoute
`UNVERIFIED`; the available context remained safe for read-only analysis and documentation.

**CONTRACT RESOLVED — SAME-SIZE ENHANCEMENT PERMITTED; EXPECTATION UPDATE STATUS EXPLICIT**

## 14 September 2026 — approved v2 correction completed

The separately authorized correction is complete at code commit `df162aa`. A fresh
`D:\PrintFlowStudio\TestData\v2` preserves the current seven category bytes and two required fixed
reference outputs, while its portrait manifest alone adopts
`enhancedOutputIsNotSmallerThanSource: true`. Both preflight layers and the actual runner consume
that property and compare the managed input and Enhancement Revision on width and height. Focused
tests, Release build, v2 static preflight and one new controlled pair passed. v1 and its historical
A1 failure/review semantics remain unchanged; no live acceptance or Production revalidation ran.
See [the completion report](regression-v2-portrait-expectation-correction.md).
