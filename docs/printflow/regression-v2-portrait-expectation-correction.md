# PF-FIX-REGRESSION-V2 — Portrait expectation correction

**Complete.** The separate local `printflow-regression-v2` set now implements the approved
Enhancement contract: decoded output width is no smaller than the exact managed pre-Enhancement
Working input width **and** decoded output height is no smaller than that input height. This is a
tooling/set-expectation correction only. No artwork-quality decision, live acceptance, Meitu
7.8.8.2 acceptance, Production authorization, revalidation or Jira status change occurred.

## Plan and implementation

The executed plan was to preserve v1, make v2 generation explicit, enforce the new expectation in
both preflight layers, consume it in the existing portrait caller using persisted Revision facts,
prove the caller/result boundary with focused tests, materialize and preflight v2, then commit the
reviewed code before producing one new controlled pair. No second runner or Product seam was added.

Implementation commit: `df162aae64dc8a18a7e671a27f11c20fea8419e2`.

- `New-PrintFlowRegressionManifests.ps1` accepts explicit `-SetVersion v1|v2`. Its default and v1
  output retain the historical larger-only property; v2 emits only
  `enhancedOutputIsNotSmallerThanSource: true`, `setId = printflow-regression-v2`, and
  `fixtureSetVersion/setVersion = v2`. Schema version remains 2 and preset version is untouched.
- `Invoke-PrintFlowStandardRegressionSet.ps1` keeps `-SetRoot` as the selection mechanism. A new
  run must explicitly use `-SetRoot D:\PrintFlowStudio\TestData\v2`. The PowerShell preflight and
  the C# loader refuse a missing, false, non-boolean, legacy-only or conflicting v2 portrait
  expectation before operational setup. New v1 execution is refused with a v2-selection message;
  v1 remains readable and the existing review-only route retains its original semantics.
- `StandardRegressionSetWorkstationSmoke.RunEnhancementCaseAsync` resolves the workflow's exact
  upstream Revision before Enhancement, then loads the decoded Enhancement Revision produced by
  that attempt. Its named assertion records all four dimensions and evaluates
  `outputWidth >= inputWidth && outputHeight >= inputHeight`. Missing/non-positive decoded facts
  fail the assertion. The PNG, promoted Revision, source-byte, automation-lock and Operator checks
  are unchanged. A passing size assertion still leaves the portrait Pending until its actual
  Operator review is decided.

## v1-to-v2 semantic and byte comparison

Normalized comparison of all seven manifests found only the derived version/path identity changes
on every manifest, plus the approved portrait changes below. All other category expectations,
workflow paths, external-application text, comparison policies, privacy rules, reference facts and
manual questions are unchanged.

| Area | v1 | v2 |
|---|---|---|
| Set identity | `printflow-regression-v1`, `v1` | `printflow-regression-v2`, `v2` |
| Local paths | `D:\PrintFlowStudio\TestData\v1\...` | `D:\PrintFlowStudio\TestData\v2\...` |
| Portrait property | `enhancedOutputIsLargerThanSource: true` | `enhancedOutputIsNotSmallerThanSource: true` |
| Portrait size prose | strictly larger structural statement | decoded output width and height each at least the managed input axis |

The fresh destination contains seven category files and the two fixed reference outputs only; it
contains no `runs` or `host-results` folder. Each destination hash equals its current v1 source:

| Copied file | Bytes | SHA-256 (v1 = v2) |
|---|---:|---|
| `inputs/FIX-PORTRAIT-001.jpg` | 130,111 | `F4CAD2A1EC7994E42E2A77CC6F30E29DE91D344821E4EE7AC01C7E712D9A4634` |
| `inputs/FIX-FINE-HAIR-001.jpg` | 312,309 | `5A705FE390AF87D1D48A0554D4908C425D4703A8807CA78EC73AC0E55E3C8D8E` |
| `inputs/FIX-TRANSPARENT-001.png` | 3,895,181 | `A20A722DB394B8CBBAE7975CC930DD456971E913E5844C21F34B27B9C4D377E2` |
| `inputs/FIX-CUSTOMER-DESIGN-001.jpeg` | 216,162 | `8A7063D8F81FB72A1DB7F5633980660905E2A6C3D3971E4F94A138B5C8394879` |
| `inputs/FIX-PSD-001.psd` | 2,757,782 | `68476CBB1EFF66D01882A6AE6C95F225F68AAE76C8E96E84DE17F7DD23BA64E8` |
| `inputs/FIX-PDF-001.pdf` | 130,800 | `12FF373ED02F6E3622F854EC0BC820EAADFE29E89CC26089074BFBEF09F2BE3E` |
| `reference/FIX-REFERENCE-TIFF-001.tif` | 116,992,344 | `D1E69C4108D4C1D6119DB11DE036F56555CDE4A064F23AF541E24E1DAC5EA412` |
| `expected/FIX-CUSTOMER-DESIGN-001_HD.png` | 13,831,390 | `E90A7FE2972209744E3829CA4380574A98B75ECF02B820F2EE707843853C4903` |
| `expected/FIX-CUSTOMER-DESIGN-001_CUTOUT.png` | 3,895,181 | `A20A722DB394B8CBBAE7975CC930DD456971E913E5844C21F34B27B9C4D377E2` |

## Validation and controlled pair

- Actual non-live v2 preflight: **Passed**, seven categories, all manifests readable and every file
  hash recomputed. Set identity `printflow-regression-v2`; content digest
  `21AF322BE9172B406DD1B12716C2CBC8977A6B057D63A39571A446ECE86232F0`.
- Focused tests: **79 passed / 0 failed / 0 skipped**. The parameterized caller cases use a
  713 × 997 input and cover equal axes, both larger, one equal/one larger, smaller width, and
  smaller height. Loader/type/conflict refusal, explicit v2 root propagation, seven-category
  content binding, v1 preservation, result serialization and unresolved Operator review are also
  covered. Durable result: `artifacts/pf-fix-regression-v2/test-results/focused.trx`.
- Required Release solution build: exit 0, **0 warnings / 0 errors**. No full suite was run.
- One post-commit controlled pair: `a4237f41-17d2-47f8-affd-8d829d98fd5f`, source
  `df162aae64dc8a18a7e671a27f11c20fea8419e2`, 603 committed inputs, input digest
  `B40780B6BEF7967F49CE03A71C2650A754D0FCD7ACF12F46B616EBDA26F60621`.
  Receipt SHA-256:
  `BC4D3084E4CBB9440778E06940634A761B418AA9E009EFD7C5171D3682E317C4`.
  Receipt verification passed; the actual read-only loaded-harness proof passed **1/1** at
  `artifacts/pf-fix-regression-v2/build-pairs/a4237f41-17d2-47f8-affd-8d829d98fd5f/verification-results/loaded-pair.trx`.

The original A0 and A1 receipts remain at their original paths and hashes
`E0373C56D015FED9E09D8B0F77C55B026CAE40ED3B685ABFB899709944077FBF` and
`189A0E5C0B1FF12ED3AB565FB80F912075E3B762DD0F3833813CDDADCDC813DF`.
The retained A1 result remains
`F614FB7904F6C182A784E140FDFA6E1E90C2F52B7D4FD949F117F1F1E358D047`.

## Boundaries and routing

Self-review only; no independent approval is claimed. Product source/adapters, accepted presets,
the 7.8.8.2 `CandidateProblems` publication prohibition, R1/R2/R3/A0 semantics, v1 manifests and all
historical results were untouched. No application, desktop diagnostic, readiness check, standard
set body, real lease, production database/revalidation, visual decision, install, push, publish or
deploy was run. SCRUM-11065, SCRUM-11123 and SCRUM-11130 acceptance statuses remain unchanged.

Installed routing policy v2.3, `EXECUTE_HANDOFF`, `CONTINUE`. NormalRoute was
`gpt-5.6-sol/medium`; explicit RouteOffset -1 requested `gpt-5.6-sol/low`. No real in-thread model
switch or execution metadata was available: `MODEL_SWITCH_UNAVAILABLE`, ActualRoute `UNVERIFIED`;
the available context remained safe for the bounded tooling/test change.

**PASS — PF-FIX-REGRESSION-V2 EXPECTATION AND RUNNER VERIFIED; LIVE ACCEPTANCE NOT RUN**
