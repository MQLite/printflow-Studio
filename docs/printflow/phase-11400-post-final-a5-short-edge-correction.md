# PrintFlow Studio — Epic 11400 Post-Final A5 Short-Edge Correction

**Correction status: PASS WITH NOTES. Epic 11400 Final QA status is preserved and the product is
ready for Epic 11500.**

This report records only the post-Final-QA A5 correction. It does not rewrite
`phase-11400-photoshop-final-release-gate.md`, and it does not imply that the superseded A5 rule
was never accepted. All other Epic 11400 Final QA evidence remains accepted and was not re-run
unnecessarily.

## 1. Reason for the correction

Epic 11400 Final QA truthfully accepted A5 as `MaximumLongEdge 135 mm`. The product decision was
subsequently corrected: A5 is a maximum **short** edge, not a maximum long edge and not a
135 × 135 mm box. A3, A4, Photoshop identity, W1, TIFF, review, Maintop compatibility, and the
environment gate were explicitly outside the semantic correction.

## 2. Superseded and current A5 contracts

The historical v1.14.0 contract remains:

- A5: `MaximumLongEdge 135 mm`.

The current v1.15.0 contract is:

- A5: `MaximumShortEdge 135 mm`;
- A4: unchanged `MaximumLongEdge 280 mm`;
- A3 landscape: unchanged `MaximumBox 360 × 280 mm`;
- A3 portrait: unchanged `MaximumBox 280 × 400 mm`.

Ordinary A5 PresetFit remains shrink-only. It never enlarges a source merely to reach 135 mm.

## 3. Domain recommendation kind and calculation

`PresetRecommendationKind.MaximumShortEdge` is a first-class Domain value. The recommendation
owns both operations that depend on its kind:

- `Fit` delegates to the single `FitWithinBounds` sizing authority;
- `Covers` classifies projected geometry against box, long-edge, or short-edge semantics.

For A5, a landscape source resolves to concrete `Height`, a portrait source resolves to concrete
`Width`, and a square source deterministically ties to `Width`. A source whose natural short edge
is already at or below 135 mm is `ResolutionOnly` with unchanged pixels. A shrink constrains only
the short edge; the long edge runs on proportionally.

The final projection also mirrors Photoshop's integer operation order. The commanded millimetre
edge is converted to whole pixels first, then the other edge is derived from that integer and the
immutable source ratio. The live seam exposed the importance of this rule: independently converting
180 × 135 mm predicted 2126 × 1594, while the one-edge Photoshop operation truthfully returns
2125 × 1594.

## 4. Preset override classification

An operator's typed scalar is no longer compared directly with a single configured number.
`TargetEdgePrintPreparationPlan` asks `PresetPrintRecommendation.Covers` about the projected pixel
pair:

- a maximum box checks both axes;
- a maximum long edge checks the projected long edge;
- a maximum short edge checks the projected short edge and does not cap the long edge.

`SourceCapacityExceeded` remains a separate classification. Tests cover a scalar above 135 mm
whose projection remains inside A5, a projected short edge above A5, and source-capacity decisions
independent of the preset classification.

## 5. Historical plans, reconfirmation, and immutable attempts

Completed historical A5 Attempts retain their recorded `MaximumLongEdge 135 mm` snapshot and the
matching long-edge audit wording. No migration or read path relabels them.

A current/pending A5 plan is executable only when its persisted recommendation matches the current
configured recommendation. An old `MaximumLongEdge 135 mm` A5 plan therefore becomes unusable,
blocks Photoshop work before an Attempt, working-state transition, or automation lock, and requires
operator review/reconfirmation. Reconfirmation creates a new `MaximumShortEdge 135 mm` plan. New
Attempts snapshot the recommendation kind/value, resolved concrete edge, projected geometry,
300 PPI, and resize policy that actually ran.

## 6. Persistence and migration

Migration `0008_maximum_short_edge_recommendation.sql` widens the recommendation-kind CHECK in both
`ProcessingSession` and `ProcessingAttempt` to admit `MAXIMUM_SHORT_EDGE`. It rebuilds the tables
without rewriting existing values.

The upgrade test starts from a v7 database containing flexible-size session state, authority,
steps, snapshot, revision, a historical A5 long-edge Attempt and retry, review, output, automation
lock, and automation log. After migration, every session column and child row remains intact, the
historical recommendation is still `MAXIMUM_LONG_EDGE`, and the widened CHECK accepts a new
`MAXIMUM_SHORT_EDGE` row.

## 7. Read model and UI wording

Current preset context is derived from v1.15.0. Historical Attempt wording is derived from the
immutable Attempt snapshot.

Affected operator wording is:

- en-US A5: `Recommended short edge: 135 mm`;
- zh-CN A5: `推荐短边：135 mm`;
- en-US A4 remains `Recommended long edge: 280 mm`;
- zh-CN A4 remains `推荐长边：280 mm`.

The A5 adjustment state retains `Based on A5` / `基于 A5` together with the same short-edge
recommendation. UI code renders the Domain/read-model result; it does not calculate a short edge.

## 8. Fake workflow coverage

The global `Adapters.Mode` remains `Fake`. Fake workflow tests cover landscape A5 shrinking,
already-within-limit resolution-only behavior, panoramic long-edge run-on, Attempt snapshot truth,
historical audit truth, active old-plan blocking, and reconfirmation. They do not invoke real
Photoshop.

## 9. Narrow live Photoshop result

One controlled synthetic A5 landscape job ran through the existing Production preparation seam
and stopped after B1A.3 factual read-back:

- controlled workspace:
  `C:\Users\admin\AppData\Local\Temp\PrintFlowPhotoshopPreparationSmoke\0cce3474057441f2967926a0765b6f9f`;
- source: 2400 × 1800 px at 240 PPI;
- recommendation: A5 `MaximumShortEdge 135 mm`;
- resolved operation: `Height`, 135 mm, `BICUBICSHARPER`;
- projected and actual: 2125 × 1594 px;
- actual resolution: 300 PPI;
- actual physical result: approximately 179.9167 × 134.9587 mm;
- aspect ratio remained proportional within whole-pixel rounding;
- RGB/8 component channels remained unchanged and W1 remained absent;
- backing SHA remained
  `630996F530D0534B8535A96B599CBD50975161CD32994683E7BBBD5F68BAD6E8`;
- no save, output, W1 action, corrective resize, or retry occurred.

The smoke intentionally retained the modified synthetic document in memory and retained its
controlled workspace. Discarding that document is an operator cleanup action, not part of the
production proof or product acceptance.

## 10. Narrow human visual result

Four real WPF layout captures were inspected at the signed 1000 × 700 viewport:

- `en-US-preset-cards.png`;
- `en-US-adjust-a5.png`;
- `zh-CN-preset-cards.png`;
- `zh-CN-adjust-a5.png`.

The captures are retained outside Git under
`C:\Users\admin\.codex\visualizations\2026\09\01\01a05a6a-f4ef-7033-8f4f-034735c21699\a5-correction`.
Both A5 states use the correct short-edge wording without clipping or harmful wrapping. A4 still
uses long-edge wording, and the A3 labels and values are unchanged. The broader 14-state matrix was
not manually repeated.

## 11. Targeted tests and clean build

The final targeted correction filter passed **309 tests, 0 failed, 0 skipped**. It includes the
Domain kind and geometry, preset authority, A3/A4 non-regression, projected override
classification, source capacity, workflow/reconfirmation, historical and current persistence,
migration/child rows, read models, localisation/rendering, preset/evidence integrity, Photoshop
preparation boundary, and architecture checks.

A fresh Debug clean/build of `PrintFlowStudio.sln` completed with **0 warnings and 0 errors**.

## 12. One complete-suite run

After targeted tests, the live proof, the affected-state visual check, final product source, and
the clean build all passed, the required command was run exactly once:

```text
dotnet test PrintFlowStudio.sln --no-build --no-restore
```

Result: **9,788 passed, 0 failed, 0 skipped**, duration **1 minute 58 seconds**. Product source was
not changed after this run and the suite was not rerun.

## 13. v1.15 evidence and preset

The immutable correction evidence is:

- `D:\PrintFlowStudio\Baseline\workstation-v1\apps\photoshop-2019\a5-short-edge-contract.json`;
- SHA-256 `FA2CC94FA2C4377CC9232664461FE7AF3B2FBEB8F3399D51EBFB41EE5525494A`.

The immutable current preset is:

- `D:\PrintFlowStudio\Baseline\workstation-v1\preset\printflow-workstation-v1.15.0.json`;
- SHA-256 `3392873ED0CA38BB410EA6725B6C4D0392F2514ECB10D9CF825B18D0DF785D16`;
- status `ACCEPTED_IMMUTABLE` and filesystem read-only;
- supersedes v1.14.0 by exact hash
  `F74792276C0B264C9F064D1C82CB26806F7B836A543E0AF5FC8B4E7FB1738C62`;
- all **27/27** source-integrity entries rehashed exact.

The evidence file is also filesystem read-only. `appsettings.json` points to v1.15.0 and its exact
manifest hash while `Adapters.Mode` remains `Fake`. The external baseline and visual captures are
not Git artefacts and were not committed.

## 14. Unchanged runtime authorities

No Photoshop runtime contract was changed. Infrastructure still receives only the resolved
`Width`, `Height`, or `None` edge; no `ShortEdge` COM concept or arbitrary resize API was added.
The accepted resampling identifiers remain `NONE`, `BICUBICSHARPER`, and `PRESERVEDETAILS`.

The following runtime authorities remain exact:

- Photoshop executable SHA-256:
  `81EE8930FC1E28637B501866A8B946FA0740C376CDA4302FEA61AA82806A80C5`;
- canonical `PrintFlow-DTF-v1.atn` SHA-256:
  `A04203EDEA623C0737D911601A3A005033789BD095130F02A5F8C04CBFCD83EE`.

W1 generation, TIFF structure/save validation, review/approval lifecycle, failure handling,
Maintop compatibility, process-termination policy, and Photoshop identity were not changed or
unnecessarily re-proved.

## 15. Environment, dependencies, and security

`src/PrintFlow.Infrastructure/Gate/FoundationEnvironmentGate.cs` is unchanged. The directly
relevant architecture coverage confirms that sizing remains Domain-owned, UI and Workflow do not
duplicate short-edge arithmetic, Infrastructure receives a concrete edge, nominal paper sizes are
non-executable, historical Attempt audit is immutable, and no process-termination/arbitrary-resize
surface was introduced.

No project, package, lock, props, or targets file changed. In accordance with the correction scope,
the already-passed Epic 11400 Final QA dependency/vulnerability audit was not repeated.

## 16. Final QA status and Git state

The previously accepted status remains **EPIC 11400 PASS WITH NOTES — READY FOR EPIC 11500**.
This correction adds no blocking product note. The operational note is only that the successful
live smoke deliberately left one unsaved synthetic Photoshop document and its controlled workspace
for explicit operator cleanup.

Product changes were committed locally on `master` as:

- `7f37986 Implement A5 maximum short-edge correction`.

At report authoring time the product commit was `HEAD`, the working tree was clean, and `master`
was 37 commits ahead of `origin/master`. This report is the required second normal local commit;
history was not amended and nothing was pushed.
