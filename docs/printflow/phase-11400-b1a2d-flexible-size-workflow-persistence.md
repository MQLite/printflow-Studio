# Epic 11400 Part B1A.2D — Flexible Size Workflow, Preset Authority and Persistence

Workflow, persistence and adapter-seam integration for the accepted flexible-size contract. No
operator UI for custom target edges, no Photoshop launch, no production resize, no W1, no CMYK, no
TIFF. `Adapters.Mode` remains `Fake`.

---

## 1. v1.11.0 authority

`appsettings.json` still selects the accepted preset and was not edited in this slice:

```
Preset.Id              printflow-workstation-v1
Preset.Version         1.11.0
Preset.Path            Baseline\workstation-v1\preset\printflow-workstation-v1.11.0.json
Preset.ExpectedSha256  A6E5DC172817F2F992114A1FDE0DCBAACC80D9CADD148C37D25CA3F816AC8AD1
Adapters.Mode          Fake
```

Nothing under `D:\PrintFlowStudio\Baseline` was opened for writing, and no external evidence file
was modified. The live `PRESERVEDETAILS` evidence accepted in v1.11.0 stands unchanged, so no
rehash was performed.

The manifest's `productionGeometryContract.resize.limitsMillimetres` is now *read* by the
application for the first time:

| Named size | Configured form | Configured value |
| --- | --- | --- |
| A3 landscape | maximum box | 360 × 280 mm |
| A3 portrait | maximum box | 280 × 400 mm |
| A4 | maximum long edge | 280 mm |
| A5 | maximum long edge | 135 mm |

## 2. PresetFit resolution

`IWorkstationPresetProvider.GetPrintSizeRecommendations()` is the single route from a preset name
to millimetres. `WorkstationPresetProvider` reads the geometry contract from the hash-verified
manifest and maps each entry to `PresetPrintRecommendation` — `MaximumBox` when the manifest states
`maxWidth`/`maxHeight`, `MaximumLongEdge` when it states `maxLongEdge`.

There is deliberately **no fallback**, unlike the naming patterns beside it. A naming pattern has a
documented design default; a print limit does not, and a paper standard is not one. A manifest that
configures nothing yields an empty set and no named size is offered.
`ConfiguredPresetProvider` — which never opens the manifest — fails closed for the same reason: it
holds a verified *reference*, and configured limits are *contents*.

The operator route is `WorkflowCommand.SetPresetFitSize(SizePreset)`. It carries the preset and
nothing else, because the millimetres are not the caller's to supply. `SessionService.PlanSize`
resolves the recommendation, turns it into a fit box through
`PresetPrintRecommendation.AsFitBounds()`, and hands it to `PrintPreparationPlan.For` — so ordinary
preset use keeps exactly the sizing authority the accepted B1A.1 contract gave it.
`ScaleToTargetEdge` is never called for it.

A maximum long edge of *L* is the square box *L × L*: fitting proportionally inside a square
constrains whichever source edge is longer and lets Photoshop derive the other, which is the
definition of a long-edge limit. `FitWithinBounds` therefore owns both configured forms, and no
second limiting-edge rule was added.

## 3. Nominal versus executable preset values

These are two different numbers that share a name, and the slice keeps them apart:

* `PrintDimensions.NominalMillimetres` answers *how big is a sheet of A4* — 210 × 297 mm. It
  remains in the Domain as descriptive metadata and is never persisted as an executable limit.
* The configured recommendation answers *how big does this shop print A4* — a 280 mm maximum long
  edge.

The gap is 17 mm on the long edge: invisible on screen, on every A4 job, and delivered to the
customer. Three things now prevent it.

1. `SessionViewModel.SizePresets` is rebuilt from `SessionView.Sizing.PresetRecommendations` on
   every state change. It previously came from `NominalMillimetres` once, in the constructor.
2. `WorkflowCommand.SetPrintDimensions` **refuses** a named preset outright. A preset arriving with
   millimetres attached is a caller having decided what that preset means. It is refused rather
   than corrected: silently substituting the configured value would accept a command that said
   something else.
3. An architecture test bans `NominalMillimetres(` from `PrintFlow.App` and
   `PrintFlow.Infrastructure` entirely.

## 4. TargetEdgeV1 semantics

`PrintDimensionSemantics` now distinguishes three readings, and none is a default:

| Reading | Meaning | Executable |
| --- | --- | --- |
| `LegacyExactPair` | two independently exact dimensions, pre-contract | no — the limits are reconfirmed |
| `MaxBoundsV1` | a fit box; `FitWithinBounds` selects the one edge | yes |
| `TargetEdgeV1` | one exact operator-selected physical edge | yes, enlargement authority permitting |

A `TargetEdgeV1` plan carries the source binding, the source pixels, the selection, the concrete
Photoshop edge, the projected pixel pair, the reduced integer scale, the direction and the neutral
policy. `LongEdge` is not a third edge Photoshop understands: it is resolved against the source's
own pixels into `Width` or `Height`, and a stored plan whose resolution contradicts its pixels is
refused.

An existing `MaxBoundsV1` record is never re-read as a target edge. The two live in separate
columns and a row holding both is refused by the database and by the mapper.

## 5. Preset override

`SetCustomTargetEdgeSize(TargetEdge, decimal, SizePreset?)` records one exact edge, optionally as an
explicit override of a named recommendation. The recommendation is retained beside the override and
never replaced by it, so "the operator went past A5's 135 mm" stays readable afterwards.

An override is **not** permission to enlarge, and the two classifications are reported separately:

| Case | `PresetLimitExceeded` | `SourceCapacityExceeded` | Authority needed |
| --- | --- | --- | --- |
| A5 (135 mm) → long edge 160 mm, 2000 px source | true | false | no |
| A5 (135 mm) → long edge 200 mm, 2000 px source | true | true | yes |

The first shrinks. Asking the operator to authorise adding pixels to a job that removes them would
be the software misreading its own state.

`RecommendedLimitMm` — the value an override is measured against — is the configured long edge for
a long-edge form and the larger of the two bounds for a box, so "past the recommendation" means the
same thing in both forms.

## 6. Enlargement authority lifecycle

`EnlargementAuthority` is not a bool. It binds the source `RevisionId`, the source SHA-256, the
sizing mode, the selected target edge, the exact requested millimetres, the exact projected scale
and the exact projected pixel pair. `Authorises` compares all eight.

| Situation | Outcome |
| --- | --- |
| Enlarge, no authority | not executable; `StartStep` refuses |
| Enlarge, exact matching authority | executable |
| changed source Revision | authority no longer applies |
| same Revision, changed SHA | authority no longer applies |
| changed requested millimetres | authority no longer applies |
| changed Width/Height/LongEdge selection | authority no longer applies |
| changed projected pixel pair | authority no longer applies |
| Shrink / ResolutionOnly | no authority required, and one cannot be attached |

Recording a size grants nothing. `WorkflowCommand.AuthoriseEnlargement` is a separate command
carrying what the operator was looking at — Revision, hash, edge and millimetres — and is accepted
only when all four still describe the plan on offer. The authority itself is built by
`EnlargementAuthority.For(plan)` from the plan rather than from the payload, which is what binds it
to the projected scale and pixel pair the operator was shown.

Invalidation is never a write. An authority stops applying because the exact thing it names is no
longer on offer, so nothing hunts it down and nothing revokes it.

## 7. Source Revision and hash binding

All four sizing and enlargement commands go through the existing `RevisionIntegrityGuard` before
anything is calculated, on the same route `Approve` and `SetBackgroundRemovalDecision` already
take. `ResolveSizingSource` then requires: an upstream result for `PhotoshopOutput`, a loaded and
valid Revision, agreement between the step row's hash and the Revision's own, and recorded pixel
dimensions.

The pixels come from the Revision's validated `FileFacts` and nowhere else — never a filename,
never a screen value, never `PrintDimensions.PixelWidth` (the independent millimetre conversion).
No second hashing implementation was added.

## 8. Migration

`MigrationRunner.NewestKnownVersion` was 5; this slice adds exactly one script,
`0006_flexible_size_and_enlargement_authority.sql`, and an architecture test fixes that.

**`ProcessingSession` is rebuilt.** 0005 added `DimensionSemantics` with
`CHECK (... IN ('LEGACY_EXACT_PAIR', 'MAX_BOUNDS_V1'))`, and SQLite cannot alter or drop a CHECK
constraint. The choice was between recreating the table and adding a second semantics column beside
the first. A second column was rejected: two columns answering "what does this size mean" is two
sources of truth, and the old one would go stale the moment a session recorded a target edge,
leaving a schema whose honest answer and readable answer are different columns.

Every column, type and CHECK is reproduced verbatim from 0001, 0002, 0003 and 0005. The only
differences are the widened `DimensionSemantics` CHECK and the new columns appended at the end.
`ProcessingAttempt` needs no rebuild — 0005 gave it plan columns but no semantics column — so it
takes ordinary `ALTER TABLE ADD COLUMN` statements.

`MigrationRunner` now suspends foreign keys for the migration pass and restores them afterwards, on
both the success and the failure path. This is not a convenience: with enforcement on, the
rebuild's `DROP TABLE` performs an implicit delete that fires every `ON DELETE CASCADE` pointing at
`ProcessingSession` and would take the sessions' steps, revisions, attempts and outputs with it.
The pragma cannot be set inside a transaction, and every migration script runs in one, so it is set
around the pass. Per-script atomicity is unchanged.

**Backfill policy.** None. Every new column is NULL for every existing row. `LegacyExactPair` stays
legacy, `MaxBoundsV1` stays `MaxBoundsV1`, no historical row acquires a preset override or an
enlargement authority, and no valid old session is upgraded to `TargetEdgeV1` because v1.11.0 is
now configured.

**Fail-closed constraints.** Valid sizing modes, target edges, recommendation kinds, directions and
policies are closed sets. Pixels and scale numerator/denominator are positive. `ProductionDpi = 300`.
Millimetres are TEXT for exactness and still constrained positive by `CAST(... AS REAL) > 0` — a
validity check on the stored text, never the arithmetic. The selection, the plan and the authority
are three all-or-nothing groups; the direction fixes the policy; a row cannot hold both sizing
contracts; and an authority exists only beside an `ENLARGE` target-edge plan with an enlarging
ratio.

## 9. Session persistence

`ProcessingSession` gains `SizeSelection`, `TargetEdgePlan` and `EnlargementAuthority`, all pending
state for the *next* run. They are written, replaced and cleared together with the dimensions they
belong to.

**Exact decimals.** The requested millimetres are stored as TEXT via
`decimal.ToString(InvariantCulture)`. A SQLite `REAL` is a binary double, and the accepted
target-edge calculation is exact — it converts the operator's decimal to a rational and rounds on
integer remainders. A stored 84.709 mm returning as 84.708999999999996 would decide a midpoint case
somewhere other than where the contract decides it, and would stop matching the enlargement
authority granted for it. The exact-decimal round trip is proved against the accepted midpoint case
84.709 mm → 1000.5 px → 1001 px. No second arithmetic implementation exists in persistence.

**Exact scale.** The projected scale is stored as the reduced integer numerator and denominator the
contract compares, never as a percentage. `ResizeScale.FromReduced` refuses an unreduced stored
pair rather than reducing it: 2002/4000 and 1001/2000 are the same number and not the same record,
and the authority comparison is exact.

The requested millimetres live once, on the selection; the plan is rehydrated with the selection it
belongs to. `PresetLimitExceeded` and `SourceCapacityExceeded` are likewise not persisted — both
are exact functions of what is stored, and a second copy would be a row that could contradict
itself.

Rehydration goes through `FlexibleSizeSelection.Rehydrate`,
`TargetEdgePrintPreparationPlan.Rehydrate` and `EnlargementAuthority.Rehydrate`, which refuse and
never repair. It deliberately does not recalculate: a plan that had to be recomputed to be readable
was never really persisted.

## 10. Immutable Attempt snapshot

`ProcessingAttempt.PrintPreparationPlan` is replaced by `ProcessingAttempt.Preparation`, the closed
`PhotoshopPreparation` union — so an attempt made under either contract is audited in the shape it
was actually made, enlargement authority included.

It is written once with the attempt's opening transaction, from
`WorkflowSnapshot.UsablePhotoshopPreparation` — the same predicate the engine just applied, not a
second read of the session. The attempt upsert leaves every one of these columns out of its
`DO UPDATE` clause, so a later size change or a later enlargement decision cannot relabel an
earlier attempt as something it was not. The row is self-contained: the selection, the
recommendation and the override facts travel with it rather than being joined back to a session
that can still change.

## 11. StartStep preconditions

`StartStep(PhotoshopOutput)` refuses before the attempt row, the working copy, the automation lock
and the adapter call, with three distinct messages:

* a legacy exact pair — the limits must be reconfirmed; nothing infers which edge was intended;
* an enlargement with no matching authority — `ENLARGEMENT NOT AUTHORISED`, a missing product
  decision rather than a failed Photoshop run;
* a missing or stale plan — a plan calculated from different or since-changed content does not
  carry over.

No attempt row is created in any of the three cases, so no fabricated external failure enters the
history.

`WorkflowSnapshot.UsablePhotoshopPreparation` is the single authority behind this, and behind the
attempt snapshot and `SessionView` readiness. It answers with the preparation rather than a
boolean, so the value a caller receives *is* the thing that was validated.

`NeedsDimensionReview` and `NeedsEnlargementAuthority` are deliberately separate and mutually
exclusive: the first says a size cannot be executed and must be chosen again, the second says a
size is exactly right and needs confirming. Telling an operator to redo a size they meant would be
the software losing their decision.

## 12. PhotoshopRequest union

`PhotoshopRequest.Preparation` changes from `PrintPreparationPlan` to the closed
`PhotoshopPreparation` union, with exactly two cases — `FitWithinBoundsPreparation` and
`TargetEdgePreparation` — closed by a `private protected` constructor so no assembly outside the
Domain can add a third.

`TargetEdgePreparation` validates its authority at construction. An enlargement without a matching
authority throws, and a shrink carrying one throws. So an unauthorised enlargement is not a case
Infrastructure has to detect — it is a value that cannot be built, and the request the adapter
receives is a fully resolved operation. The adapter decides nothing: not whether an override was
allowed, not whether an enlargement was authorised, not which recommendation applies.

The request carries the neutral Domain policy only — `None`, `BicubicSharper`, `PreserveDetails`.
No `ResampleMethod` identifier exists outside Infrastructure, and the mapping to `NONE`,
`BICUBICSHARPER` and `PRESERVEDETAILS` is B1A.3's.

## 13. Fake integration

`FakePhotoshopOutputProcessor` consumes both variants and reports each in its own vocabulary rather
than flattening them. For a target-edge run the note records the direction, the neutral policy, the
requested edge and millimetres, the resolved concrete edge, the reduced scale, `PresetLimitExceeded`,
`SourceCapacityExceeded`, and whether enlargement authority was required and present.

It stays prefixed `fake`, says `projected`, and ends `no Photoshop ran and nothing was resampled`.
A `PreserveDetails` policy is reported as the policy the run *would* have used. The Fake never
claims Preserve Details executed.

## 14. Read-model seam

`SessionView.Sizing` (`FlexibleSizeView`) is the seam the next UI slice binds to:
`PresetRecommendations`, `SizingMode`, `Preset`, `RecommendationKind`,
`RecommendationMaxWidthMm`/`MaxHeightMm`, `PresetOverride`, `RequestedTargetEdge`,
`RequestedMillimetres`, `ResolvedLimitingEdge`, `ProjectedPixelWidth`/`Height`, `ResizeDirection`,
`ProjectedScalePercent`, `PresetLimitExceeded`, `SourceCapacityExceeded`,
`NeedsEnlargementAuthority`, `HasUsableEnlargementAuthority`, `CanAuthoriseEnlargement`,
`CanSetPresetFitSize`, `CanSetCustomTargetEdgeSize`. `CanRunPhotoshopOutput` stays on the view.

Every value reflects the *usable* decision, never a raw stored one, and the three readiness flags
come from the engine's own command probe. What is deliberately absent: the source SHA-256, the
Revision binding, any Photoshop DOM identifier, and the rational arithmetic behind the projection.

`PrintPreparationAttemptView` — the audit line for the result on screen — now covers both contracts,
with the maximum-bound fields nullable and target-edge fields beside them.

## 15. Retry, ReturnToStep and AddAnotherSize

* **Same-content retry.** Same Revision, same SHA, same requested target: the plan and its
  enlargement authority both remain usable, and a second run starts without a second confirmation.
  A rejected Photoshop result is not a reason to ask again.
* **ReturnToStep(PrintDimensions).** Clears the dimensions, the semantics, both plans, the
  selection and the enlargement authority together. The operator went back to choose a different
  size, so a confirmation of the old one has nothing to apply to.
* **AddAnotherSize.** Starts with no target, no override and no authority. Completed siblings keep
  their own immutable audit.

## 16. Targeted test counts

Run in the prescribed order, all green:

| Filter | Passed |
| --- | --- |
| Flexible-size Domain (`FlexibleSizeSelectionTests`, `ScaleToTargetEdgeTests`, `EnlargementAuthorityTests`, `TargetEdgeRehydrationTests`) | 40 |
| Workflow engine and transition matrix (`Unit.Workflow`) | 7 962 |
| `FlexibleSizeWorkflowTests` (service, persistence, attempt snapshot, Fake, read model) | 30 |
| `MigrationTests` + `MaximumBoundsPlanTests` | 69 |
| Maximum-bounds and dimensions UI | 55 |
| Architecture | 261 |

New in this slice: `FlexibleSizeWorkflowTests` (30), `TargetEdgeRehydrationTests` (14), eight
migration tests including the 0005→0006 upgrade, and four architecture guards — the target-edge
calculator ban, the `EnlargementAuthority.For` ban, the nominal-paper-size ban, and the closed-union
assertion on `PhotoshopRequest`.

## 17. One complete-suite run

```
dotnet test PrintFlowStudio.sln --no-build --no-restore
Passed! - Failed: 0, Passed: 9559, Skipped: 0, Total: 9559
```

Run exactly once, at the final gate, after every targeted filter was green and the build was clean.
It was necessary because this slice changes a forward-only database migration that **rebuilds a
table**, the persisted Session and Attempt semantics, the `StartStep` readiness predicate, and the
`PhotoshopRequest` shape. Those four reach code no targeted filter names — the rebuild in
particular touches every row of every session ever written, and the `Preparation` union changes a
type every Photoshop path passes through.

## 18. Build

```
dotnet build
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## 19. Remaining scope

**Flexible-size operator UI (next slice).** Custom target-edge entry, the Width/Height/LongEdge
selector, the override control, and the enlargement warning and second confirmation. The existing
maximum-bounds screen is unchanged apart from two things §3 required: its named-size shortcuts now
show the configured recommendation, and confirming a still-selected named preset routes through
`SetPresetFitSize`. No XAML changed and no flexible-size control was partially added.

**B1A.3 production scope.** The real Photoshop resize; the mapping of `None`, `BicubicSharper` and
`PreserveDetails` onto `ResampleMethod.NONE`, `BICUBICSHARPER` and `PRESERVEDETAILS`; reading the
actual returned geometry back; W1; CMYK; the TIFF save; and production mode. None of it exists yet,
and the architecture tests still refuse it.

## 20. Git state

Branch `master`, not pushed. Two commits precede this slice's work, checkpointing the accepted
B1A.2C contract:

```
1bdba53 Report: Epic 11400 B1A.2C flexible size and limit override acceptance
0f56dcf 11400: calculate one exact operator-chosen print edge
```

No previous commit was amended and no history was rewritten. `B1 NOT READY`, `B1A NOT READY`,
`B1A.1`, `B1A.2A` and `B1A.2B` are untouched.

**Dependency graph unchanged; vulnerability audit deferred to Epic 11400 Final QA.** No package,
project or lock file was modified.

Nothing runtime, synthetic or external was committed: no database, no images, no Photoshop files,
no preset or evidence files, no transcripts, no screenshots.

---

**11400-B1A.2D PASS — READY FOR FLEXIBLE-SIZE OPERATOR UI**
