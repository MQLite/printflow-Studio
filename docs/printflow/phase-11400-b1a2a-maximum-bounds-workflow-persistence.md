# Epic 11400 Part B1A.2A — Maximum-bounds decision, source binding and persistence

**Verdict: 11400-B1A.2A PASS WITH NOTES — READY FOR MAXIMUM-BOUNDS UI INTEGRATION**

This slice makes the accepted B1A.1 fit-within-bounds contract authoritative in session state,
binds every executable plan to one exact upstream artefact, persists both, and refuses to start
Photoshop output on anything else. No Photoshop process was launched, attached to, or scripted.
No W1 Action ran, no CMYK conversion exists, no TIFF was written, and `Adapters.Mode` remains
`Fake`.

---

## 1. Accepted v1.10.0 authority

Taken as settled and not re-opened:

| Fact | Source |
|---|---|
| Configured immutable preset `printflow-workstation-v1` **v1.10.0**, SHA-256 `27B5E9…D033` | [appsettings.json](../../appsettings.json) |
| Operator unit is millimetres | B1A.1 |
| `PrintDimensions` is a **maximum fit box**, not two exact dimensions | B1A.1 |
| PrintFlow selects the limiting edge automatically; the operator never picks an axis | B1A.1 |
| Photoshop keeps proportions linked and receives **one** edge value | B1A.1 |
| Production resolution fixed at **300 PPI** | MVP design §8.3 |
| Already within bounds → resolution-only / `NONE` | B1A.1 |
| Shrink required → fixed `BICUBICSHARPER` | B1A.1 |
| Enlargement, stretching, crop and canvas extension are prohibited | B1A.1 |
| The actual Photoshop read-back is the future production authority | B1A.1 |

The preset pointer and the B1A.1 contract were uncommitted at the start of this slice and were
preserved first, without amending or rewriting anything (§30, and §14 below).

---

## 2. Legacy versus `MAX_BOUNDS_V1`

The millimetre columns did not move; what they *mean* did. A stored pair that cannot say which
reading it was written under is a pair nothing may act on, so the reading is now persisted
explicitly.

[`PrintDimensionSemantics`](../../src/PrintFlow.Domain/Outputs/PrintPreparationPlan.cs) has exactly
two members and no "unknown":

- **`LegacyExactPair`** — written before the contract. Two independently exact dimensions.
- **`MaxBoundsV1`** — a fit box under the accepted contract.

`ProcessingSession.DimensionSemantics` is null exactly when `Dimensions` is null: the reading is
half of what the pair says, not metadata about it. Migration 0005 **backfills** every existing row
that holds a size to `LEGACY_EXACT_PAIR` — stated rather than inferred, because the migration is
the last moment at which "that pair was written under the old contract" is still certain.

Nothing converts a legacy pair into a fit box, including when its numbers happen to suit the
source ratio. Which of the two was intended as the limiting edge is a question the record does not
answer, and answering it from the ratio would be the software making the operator's decision.

---

## 3. Source Revision / hash binding

[`PrintPreparationPlan`](../../src/PrintFlow.Domain/Outputs/PrintPreparationPlan.cs) carries both
halves of the artefact identity — `SourceRevisionId` and `SourceSha256` — and exposes one
predicate, `Covers(revisionId, sha256)`. This is deliberately the same arrangement
`BackgroundRemovalAuthority.Authorises` established, applied to the same problem:

| Situation | Result |
|---|---|
| Plan for Revision A, current upstream is A with the same hash | usable |
| Plan for Revision A, current upstream is B | historical, unusable |
| Plan for Revision A, bytes replaced in place | integrity failure, unusable |

`WorkflowSnapshot.UsablePrintPreparationPlan` is the single definition of "still usable", asked by
the engine before it will start the step, by `SessionService` before it snapshots a plan onto an
attempt, and by `SessionView` before it reports readiness. Invalidation is not a write: a stale
plan is retained as honest history and simply never matches again, so nothing has to hunt one down
when a Revision is superseded.

Recording bounds is itself integrity-checked. `SessionService.EnsureIntegrityAsync` now re-hashes
the upstream Revision for `SetPrintDimensions`, through the same guard `Approve` and `StartStep`
already use — no second hashing path was added. Without it, a file mutated under the screen would
only surface at `StartStep`, after a plan had been written for content nobody fitted anything to.

---

## 4. The FitWithinBounds plan

`FitWithinBounds` remains the **only** limiting-edge authority. `PrintPreparationPlan.For(...)` is
the only creator of a plan and is the calculator's only caller; nothing in Workflow, Infrastructure
or the shell calls it, and an architecture regression enforces that (§10 below).

The projected pixel calculation moved *into* the calculator —
`FitWithinBoundsResult.ProjectedPixelWidth/Height`, via the newly public
`PrintDimensions.PixelsFromMillimetres` — so §6's single-authority rule now covers rounding as
well as edge selection.

The plan records: bound Revision + SHA-256, source pixels, `MaxWidthMm`/`MaxHeightMm`, limit kind,
mode (`ResolutionOnly` | `ProportionalShrink`), automatic `LimitingEdge` (`None` | `Width` |
`Height`), limiting value in mm, projected target pixels, `ProductionDpi = 300`, and a **neutral**
resize policy (`None` | `BicubicSharper`). No Photoshop COM or DOM type reaches the Domain;
Infrastructure will map the policy to `ResampleMethod.NONE` / `ResampleMethod.BICUBICSHARPER` when
a production resize exists.

`Validated()` refuses any record whose parts contradict each other — a shrink with no edge, an edge
with no value, a resolution-only plan that claims to resample, or **any** plan projecting more
pixels than its source. The no-enlargement rule is therefore structural rather than promised.

Plan creation requires an active session, a workflow containing `PhotoshopOutput`, a current and
valid upstream Revision, and recorded source pixel dimensions. Nothing is derived from a filename,
a stale UI value, a guessed size, or `PrintDimensions.PixelWidth`/`PixelHeight`.

---

## 5. Persistence and migration

`MigrationRunner.NewestKnownVersion` was audited before naming the script: 0001–0004 existed, so
the new script is **`0005_maximum_bound_print_plan.sql`**. `NewestKnownVersion` is still derived
from the embedded scripts and remains the single version authority; no test carries a literal
schema number.

Added to `ProcessingSession`: `DimensionSemantics` plus fourteen `PrintPlan*` columns. Added to
`ProcessingAttempt`: the same fourteen `PrintPlan*` columns. The attempt copy is **self-contained**
— it carries the bounds and limit kind as well as the derived plan — so an audit row never has to
be joined back to a session that may since have changed its mind. Identical column sets on both
tables are read by one mapper, so "what a plan is" has one definition.

Fail-closed persistence (§13):

- Column CHECKs on every enumerated value, positive pixels, positive millimetres, 64-character
  SHA, and `PrintPlanProductionDpi = 300`.
- A row-level CHECK making the twelve structural plan columns **all-or-nothing**.
  `PrintPlanLimitingValueMm` is deliberately outside that group — its absence is meaningful, and
  the domain type pairs it with the mode.
- `Mappers.ToPrintPreparationPlan` requires the whole group together and rebuilds through
  `PrintPreparationPlan.Rehydrate`, so a self-contradictory row is refused on read as well.
- `ToPrintDimensionSemantics` refuses a marker this build does not understand rather than
  defaulting it.

The mapper deliberately does **not** re-run `FitWithinBounds` on read: recalculating would repair a
bad row rather than reject it, and a plan that had to be recomputed to be readable was never really
persisted.

---

## 6. Immutable Attempt snapshot

`ProcessingAttempt.PrintPreparationPlan` is written once, with the attempt's **opening**
transaction — before the working copy, before the automation lock is used, before any adapter call
— and the attempt upsert leaves the plan columns out of its `DO UPDATE` clause. This follows the
pattern already established by trim parameters and the background-removal authority, and it is what
makes "a later change of limits never relabels an earlier attempt" a property of the SQL rather
than a promise about the caller.

What is snapshotted is `UsablePrintPreparationPlan`, not the raw session field — the same predicate
the engine just validated, so an attempt can never record a stale plan that happened to be sitting
on the session.

---

## 7. Legacy fail-closed behaviour

A resumed legacy session:

- **retains** its historical dimensions, readable in the aggregate and in `SessionView.Dimensions`;
- reports `DimensionSemantics = LegacyExactPair` and `NeedsDimensionReview = true`;
- reports `PreparationMode`, `LimitingEdge`, `MaxWidthMm`/`MaxHeightMm` and the projected pair as
  **null** — a stale or legacy record never appears active;
- is refused at `StartStep(PhotoshopOutput)` with `PreconditionNotMet` and a message beginning
  `DIMENSION REVIEW REQUIRED`.

The refusal is structural, in the engine, and happens before anything is created: **no attempt row,
no Working copy, no automation lock, no adapter call.** A missing product decision is not a failed
Photoshop run, and recording one as such would put a fabricated failure in the audit history.

Nothing infers which old dimension was the limiting edge, and nothing auto-converts a pair because
it happens to fit the source ratio. The way back is the ordinary rewind:
`ReturnToStep(PrintDimensions)` clears the pair, its reading and its plan together, and recording
bounds again produces a plan calculated against the content Photoshop will actually consume.
`AddAnotherSize` clears the same three, so each output is its own decision.

---

## 8. PhotoshopRequest change

`PhotoshopRequest` gains a **non-nullable positional** `PrintPreparationPlan Preparation`. A
request that could be built without a plan is one some future call site would build without one.

`Dimensions` is retained for display, naming and audit compatibility — the output filename is built
from its millimetres and a `PrintOutput` records them — but it is explicitly **not** the executable
authority. `SessionService` builds the request from `attempt.PrintPreparationPlan`, the row the
opening transaction already wrote, and fails closed if it is absent.

An architecture regression proves the production Photoshop path cannot derive a target pair from
the legacy independent-pixel fields: `Dimensions.PixelWidth`, `Dimensions.PixelHeight`,
`Preparation.SourcePixelWidth` and `Preparation.SourcePixelHeight` are banned from executable text
in both Photoshop adapters, the Fake adapter and `SessionService`. This is worth having precisely
because those fields are still present, still populated, and reading them would compile, run, and
produce two numbers that look exactly like a size.

---

## 9. Fake compatibility

`FakePhotoshopOutputProcessor` was changed only as far as the new request shape required. It still
copies the input to the reserved output path, so the downstream validation pipeline is genuinely
exercised, and it:

- derives its adapter note **entirely** from `request.Preparation` — the semantics version, mode,
  neutral policy, limiting edge and value, and the **projected** pixel pair at 300 ppi — so the
  note is deterministic for a given plan and a Fake run demonstrably received one;
- recalculates no limiting edge, reinterprets no legacy row, and reads no independent pixel pair;
- does not mutate its input in place;
- makes no production read-back claim — the note is prefixed `fake`, says `projected`, and states
  that no Photoshop ran and nothing was resampled.

No slow-Fake configuration or unrelated failure mode was added. Every pre-existing Fake workflow
test remains green.

---

## 10. Targeted tests

Run in the required order, all green before the build gate:

| # | Scope | Filter | Result |
|---|---|---|---|
| 1 | Domain — fit calculation | `~FitWithinBounds` | 12 passed |
| 1 | Domain — preparation plan (new) | `~PrintPreparationPlanTests` | 22 passed |
| 2 | Workflow / SessionService (new) | `~MaximumBoundsPlanTests` | 12 passed |
| 2 | Workflow engine + Domain units | `~Unit.Workflow`, `~Unit.Domain` | 7178 passed |
| 3 | Migration / repository | `~MigrationTests` | 21 passed |
| 3 | Persistence integration | `~Integration.Persistence` | 149 passed |
| 4 | Fake Photoshop / request | covered by the two above | — |
| 5 | Architecture (new) | `~MaximumBoundsBoundaryTests` | 35 passed |
| 5 | Architecture + UI read model | `~Architecture`, `~Integration.Ui` | 361 passed |

Coverage against §22, by requirement:

1. wide source chooses Width — `A_wide_source_is_limited_by_width`,
   `A_source_larger_than_the_box_shrinks_on_its_limiting_edge`
2. tall source chooses Height — `A_tall_source_is_limited_by_height`
3. same ratio chooses the accepted deterministic edge —
   `An_exact_ratio_match_picks_the_accepted_deterministic_edge`
4. already within bounds → ResolutionOnly / None —
   `A_source_already_within_bounds_is_resolution_only`,
   `Recording_bounds_persists_max_bounds_semantics_and_a_source_bound_plan`
5. shrink → ProportionalShrink / BicubicSharper — `A_shrink_uses_the_fixed_resampling_policy`
6. enlargement is never produced — `No_plan_projects_more_pixels_than_its_source`,
   `A_plan_projecting_more_pixels_than_its_source_is_refused`
7. plan binds RevisionId + SHA — `A_plan_binds_the_revision_and_the_hash_it_was_calculated_from`
8. stale Revision makes plan unusable —
   `A_plan_bound_to_other_content_cannot_start_photoshop_and_creates_nothing`
9. mutated source fails integrity — `A_plan_whose_bound_hash_no_longer_matches_cannot_start_photoshop`,
   `Bounds_recorded_over_mutated_source_bytes_are_refused`
10. same-upstream retry retains plan — `A_retry_over_unchanged_upstream_content_keeps_the_plan`
11. changed upstream requires a new plan — `Reconfirming_the_limits_makes_a_legacy_session_runnable_again`
12. missing/legacy plan creates no Attempt —
    `A_legacy_exact_pair_is_retained_needs_review_and_cannot_start_photoshop`
13. request carries the typed plan — `The_photoshop_request_carries_a_typed_preparation_plan`,
    `The_producing_attempt_snapshots_the_plan_it_ran_under`
14. fake processor uses the projected plan — `The_fake_adapter_consumes_the_typed_plan_and_records_it`
15. legacy independent pixels are not executable —
    `The_photoshop_path_derives_no_target_pair_from_independent_pixels`

Migration coverage (§21): empty database → newest; pre-0005 schema → newest with rows retained;
existing sizes marked `LEGACY_EXACT_PAIR` and gaining no plan; a session that never chose a size
staying null; a complete plan round-tripping exactly; **eleven** partial-row rejections; a non-300
production resolution rejected; an unsupported semantics marker rejected; a future `user_version`
failing closed; `NewestKnownVersion` remaining the single version authority.

---

## 11. One complete-suite run, and why it was necessary

```
dotnet test PrintFlowStudio.sln --no-build --no-restore
Passed!  -  Failed: 0, Passed: 8516, Skipped: 0, Total: 8516, Duration: 57 s
```

Run **once**, at the final gate, and not repeated. It was necessary because this slice changes
three things whose blast radius is wider than any targeted filter:

1. a **forward-only SQLite migration** with a data backfill, which every persisted session reads
   through;
2. the **persisted `PrintDimensions` contract** and the `ProcessingSession` / `ProcessingAttempt`
   workflow state built on it;
3. the **`PhotoshopRequest` contract** and a new structural precondition on `StartStep`, which
   changes what the engine will accept for every TIFF-producing workflow.

That last one was the case in point: the engine precondition broke six pre-existing pure-engine
tests that drove `StartStep(PhotoshopOutput)` without a plan, and the targeted Domain and
persistence filters would not have caught them.

**Dependency graph unchanged; vulnerability audit deferred to Epic 11400 Final QA.** No package,
project-reference or lock file was touched, so per §27 no locked restore or audit was run.

---

## 12. Build

```
dotnet build
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Run at preflight (before implementation) and again at the build gate. Both clean.

---

## 13. Notes

Two items, neither affecting the PASS criteria, both recorded because they were changed and are not
strictly this slice's scope.

**N1 — a pre-existing test break was repaired to reach a green gate.**
`ValueObjectTests.Failure_codes_keep_stable_English_names_because_they_are_persisted` was already
failing on `HEAD` before this slice began; it was verified by stashing this slice's work and
re-running the test against the previous commit. Commit `2f8ae7f` ("11400: identify Photoshop and
prove which document it opened") added eight `Photoshop*` members to `FailureCode` without updating
this test's expected list. Because §26 requires the full suite at 0 failed, the list was updated to
include them. No production code was changed and the test's intent — persisted failure codes keep
stable English names — is unaltered.

**N2 — the engine-level test fixture gained a plan helper.**
`WorkflowScenario.RecordMaximumBounds(...)` was added and six pure-engine tests now call it instead
of issuing `SetPrintDimensions` alone. This is not a weakening of the new precondition: the helper
performs both halves in the order production performs them — the engine accepts the millimetres,
then the plan is calculated through `PrintPreparationPlan.For` from supplied source pixels, exactly
as `SessionService` does from the upstream Revision's `FileFacts`. A pure snapshot carries no
`FileFacts`, which is why the pixels are supplied rather than read. Even the fixture cannot invent
a limiting edge the one domain calculator would not have chosen.

**Observation for B1A.2B (not a defect).** A session already sitting at `PhotoshopOutput` with a
legacy or stale pair cannot re-issue `SetPrintDimensions` directly, because `PrintDimensions` is no
longer the current step. The structural route is the existing
`ReturnToStep(PrintDimensions)` — which is offered through `AvailableReturnTargets`, invalidates
descendants correctly, and is covered by
`Reconfirming_the_limits_makes_a_legacy_session_runnable_again`. The UI slice should surface
`NeedsDimensionReview` as a prompt to take that route rather than introducing a second path to
recording bounds.

---

## 14. Remaining scope

**B1A.2B — maximum-bounds operator UI.** Not started, deliberately. The read model exposes exactly
the minimum this slice owed it and no operator text or layout:
`DimensionSemantics`, `MaxWidthMm`, `MaxHeightMm`, `PreparationMode`, `LimitingEdge`,
`ProjectedPixelWidth`, `ProjectedPixelHeight`, `NeedsDimensionReview`, `CanSetMaximumBounds`,
`CanRunPhotoshopOutput` — every plan field sourced from the **usable** plan, so a stale plan cannot
appear active. All strings remain the shell's to localise.

**B1A.3 — production Photoshop preparation.** Still entirely absent: opening the managed Working
file for resize, mapping the neutral policy to `ResampleMethod.NONE` / `BICUBICSHARPER`, setting
300 ppi, and — the part that matters most — reading the **actual** Photoshop-returned geometry
back. This slice records a *projection* and nothing named `Actual*` or `PhotoshopResult` exists on
the plan, the session, the attempt or the view; an architecture test enforces that.

Also still out of scope and unchanged: the W1 Action, CMYK conversion, the TIFF Save As, the
production `AdapterOutput`, and global Production mode. `Adapters.Mode` remains `Fake`.

---

## 15. Git state

Branch `master`, ahead of `origin/master`. Nothing pushed, nothing amended, no history rewritten,
and the historical B1 / B1A NOT READY reports are untouched.

Preflight preserved the accepted-but-uncommitted B1A.1 work as two ordinary commits before
implementation began:

- `8fed7bb` 11400: fit source proportionally within maximum print bounds
- `530a18d` Report: Epic 11400 B1A.1 fit-within-bounds contract acceptance

This slice then adds its implementation and this report. No synthetic image, runtime database,
Photoshop file, comparison output, external preset or evidence file, screenshot or transcript was
committed.

---

**11400-B1A.2A PASS WITH NOTES — READY FOR MAXIMUM-BOUNDS UI INTEGRATION**
