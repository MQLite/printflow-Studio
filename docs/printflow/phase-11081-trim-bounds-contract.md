# SCRUM-11081 — Trim bounds contract: persist and expose the crop geometry

Date: 2026-09-04 (Pacific/Auckland). Scope: the two rectangles a successful deterministic trim
establishes — what the alpha scan detected, and what was actually cropped out. Nothing about how
trimming decides those rectangles was changed.

Closes the **P0-3** gap in `original-jira-functional-coverage-reaudit.md`: SCRUM-11081's stated
acceptance criterion *"return original and final bounds"*. Unblocks SCRUM-11094 (display of
detected graphic bounds) and closes the one failing row of SCRUM-11126's acceptance matrix
(*metadata persistence*).

---

## 1. The gap this closes

`DeterministicAlphaTrimProcessor` has always computed both rectangles and has always returned
them on `TrimResult`. `SessionService` then dropped them:

```csharp
// before
return await InspectAsync(result.ProducedFile!.Value, cancellationToken);
```

The tuple that line returns carries the file, the inspected facts and the adapter notes — and
nothing else. `result.ContentBounds` and `result.AppliedBounds` went out of scope one line later.
A search for either name outside `ITrimProcessor.cs` found test files only, and no migration held
a bounds column.

The consequence was not cosmetic. A Trim Revision could say how large it is, and nothing anywhere
could say **which part of the source it came from**. The margin was persisted (migration 0002),
the produced file was hashed into a Revision, and the one fact connecting the two — the rectangle
the crop was taken at — existed for the duration of one method call.

## 2. Scope and non-goals

**In scope.** Carry both rectangles from the processor to the closing transaction; persist them
on `ProcessingAttempt`; expose them on `SessionView`; show them on the Trim review in both
languages.

**Not in scope, deliberately.**

* The trim algorithm. `AlphaBounds.Compute`, the `alpha > 0` rule, `TrimBounds.Expand` and the
  canvas clamp are untouched. This slice records what they already decided.
* A workstation-configurable default safety margin (the second half of the audit's finding on
  SCRUM-11081 B). That belongs to SCRUM-11118 (Settings), which does not exist yet.
* Persisting the **manual** crop rectangle. A human-drawn rectangle is a different fact with a
  different provenance; §11 states why it stays out rather than being folded in here.
* Backfilling historical attempts (§21).

## 3. The value: `TrimGeometry`

A new domain record in `PrintFlow.Domain/Trimming`, beside the types it is made of:

```csharp
public sealed record TrimGeometry(TrimBounds ContentBounds, TrimBounds AppliedBounds)
```

One value rather than two loose nullable properties, because neither half means anything alone.
*The applied rectangle with no record of what was detected* cannot answer how much safety margin a
result carries; *the detected rectangle with no record of what was applied* cannot be checked
against the file that was produced. A trim establishes both or establishes neither.

Both rectangles are in the **source image's** pixel coordinates, in `TrimBounds`'s half-open
`[left, top → right, bottom)` convention, unchanged and unconverted. No second coordinate system
is introduced anywhere in this slice — not for storage, not for display. Every conversion is a
place for a missed `+1` to silently clip a column of the operator's artwork, and the product
already has exactly one convention.

It lives in the domain rather than the workflow layer for the reason every other name in
`TrimBoundaryTests`'s domain list does: it is geometry. It touches no file, no repository and no
clock. `TrimBoundaryTests` now asserts it into that list.

## 4. `ContentBounds` — what the alpha scan found

The smallest rectangle containing every source pixel whose alpha is greater than zero, **before**
any operator safety margin — exactly what `AlphaBounds.Compute` returned. Never touched by
margins, by display scaling or by print sizing.

This is the number SCRUM-11094 asks to be shown, and the reason it is stored rather than derived:
it cannot be recovered from the trimmed file. The output PNG's own alpha bounds are its own
canvas, which for a margined crop is a strictly larger rectangle at a different origin.

## 5. `AppliedBounds` — what was cropped, and the invariant

`ContentBounds` grown by this attempt's `TrimMargin` and clamped to the source canvas. The
produced PNG is `Width × Height` of this rectangle by construction rather than by coincidence
(§18).

`TrimGeometry.Create` refuses a pair that could not have been produced by margin expansion:

```text
applied.Left   <= content.Left
applied.Top    <= content.Top
applied.Right  >= content.Right
applied.Bottom >= content.Bottom
```

A margin expands the crop and the clamp only ever stops it at the canvas edge, so an applied
rectangle that cuts *inside* the detected content is not a trim this product performed. Storing
one would put a row in the history that no reader can honestly interpret — the same reason
migration 0002 refuses a negative margin. Both rectangles must also be non-empty.

A margin larger than the border records the **clamped** rectangle, not the arithmetic: what was
cropped, not what was asked for.

## 6. Where the geometry lives: the attempt, not the session

On `ProcessingAttempt`, next to `TrimParameters`, and for the same reason migration 0002 put the
margin there (Part C3 §15).

The session already records the margin *the next run would use*. Geometry is not a pending
decision but a fact about one produced file. An operator who rejects a trim and re-runs at a wider
margin produces a second attempt with the **same** content rectangle and a **different** applied
one; a session-level record would have retrospectively relabelled what the first attempt cropped.
Attempt rows are written once, so both readings stay side by side and each Revision keeps the
geometry that actually produced it.

## 7. Migration `0009_trim_bounds.sql`

Eight nullable integer columns on `ProcessingAttempt` — four edges per rectangle — added by plain
`ALTER TABLE ADD COLUMN`, so no table is rebuilt and no existing constraint is touched.

```text
TrimContentLeft  TrimContentTop  TrimContentRight  TrimContentBottom
TrimAppliedLeft  TrimAppliedTop  TrimAppliedRight  TrimAppliedBottom
```

Typed integer columns rather than a JSON blob, following 0002. Four edges is what a support query
wants to read directly, and burying operator-visible geometry in an opaque string would make
*"which pixels were kept?"* unanswerable without a parser.

Per-column `CHECK`s state what a column alone can state (coordinates are pixel indices, never
negative). Three triggers state what it cannot:

| Trigger | Refuses |
| --- | --- |
| `..._Coherent_Insert` | a half-written pair; an empty rectangle; an applied rectangle smaller than its content |
| `..._Coherent_Update` | the same, on update |
| `..._TrimBounds_Immutable` | any edit to a geometry already recorded (§17) |

Triggers rather than a table `CHECK` because SQLite cannot add one to an existing table, and
rebuilding `ProcessingAttempt` to gain a constraint a `BEFORE INSERT/UPDATE` trigger states
exactly as well would mean copying every attempt row and re-establishing four foreign keys for no
additional guarantee.

`MaximumBoundsBoundaryTests` asserts the migration set now ends at `0009`. That assertion exists
so adding a script is a deliberate, stated act rather than a silent one.

## 8. What NULL means

**"This attempt established no trim geometry."** Never *"the whole canvas was kept"* — the whole
canvas is itself a rectangle these columns can state, so the two readings must not collide.

All eight are null together. The mapper refuses a partial row rather than patching it up with
zeros: a missing edge is not a rectangle that starts at the canvas corner, and defaulting it would
put a fabricated crop into an operator-visible audit line. The 0009 triggers refuse to write such
a row by any route, so reaching that throw means the file was edited outside PrintFlow.

## 9. Carrying the rectangles out of the processor

`SessionService`'s three-element result tuple became a named record:

```csharp
private sealed record StepWork(
    WorkspaceFileRef Output,
    FileFacts Facts,
    string? AdapterNotes = null,
    TrimGeometry? TrimGeometry = null);
```

A fourth anonymous slot on a value threaded through twenty return statements is a slot that gets
filled in the wrong order eventually. Naming it also states the rule that matters: everything on
it is **measured**, carried from whatever performed the work rather than recomputed afterwards.

The trim branch now passes the processor's own rectangles through:

```csharp
return await InspectAsync(
    result.ProducedFile!.Value,
    cancellationToken,
    trimGeometry: TrimGeometry.Create(result.ContentBounds!.Value, result.AppliedBounds!.Value));
```

`InspectAsync` passes `trimGeometry` straight through rather than deriving it from the file. The
inspector can say how large the cropped PNG is; it cannot say where in the source that rectangle
sat, and re-running the alpha scan over an already-cropped image answers a different question —
the trimmed file's own content bounds, which for a margined crop are not the source's. That would
silently turn every margined crop into a tight one.

The `!` on both rectangles is safe by construction: `TrimResult.Produced` is the only way to build
a produced result, and the `ManualCropRequired` outcome returns earlier (§11).

## 10. The closing transaction

The geometry joins the attempt in the same transaction as its status, its Revision and the step's
move to `ReviewRequired`:

```csharp
ProcessingAttempt succeededAttempt = runningAttempt.Succeed(revisionId, context.NowUtc, produced.AdapterNotes);
if (produced.TrimGeometry is { } geometry)
{
    succeededAttempt = succeededAttempt.WithTrimGeometry(geometry);
}
```

Never a later best-effort update. If that transaction fails there is no successful attempt, no
Revision and no bounds; the three cannot come apart.

This is the one audit group written by the **closing** transaction rather than the opening one,
which is why it appears in the attempt upsert's `DO UPDATE` clause as well as its insert. The
margin, the background-removal authority and the preparation plan are decisions the attempt was
*started* with, so their absence from that clause is what makes them unrewritable. Geometry is a
result: it does not exist until the pixel work has run, and the row that opened the attempt
necessarily carried nulls. The `..._Immutable` trigger closes the difference — the clause can fill
nulls exactly once and can never edit or erase what was recorded (§17).

## 11. What records nothing

Null geometry, on purpose, for:

| Case | Why |
| --- | --- |
| A trim that ended `ManualCropRequired` | No content was detected and nothing was cropped. The step fails with its stable code; a fabricated rectangle would describe work that never happened |
| A **manual** crop | The rectangle a human drew is not an alpha detection. Recording it in these columns would make "detected graphic bounds" mean two different things in two rows |
| Every adapter call — Meitu, Photoshop | No trim ran |
| A promotion | No trim ran |
| Every attempt written before 0009 | §21 |

## 12. The read model

`SessionView` gains `ArtefactTrimGeometry`, resolved through `ProcessingAttempt.OutputRevisionId`
exactly as `CurrentTrimParameters` is, and for the same reason: after a reject-and-re-run at a
different margin the history holds two trim attempts, and *"the geometry of whatever ran last"*
would label the file on screen with a crop that produced a different file.

Two readings sit on top of it:

| Member | Scope | Question it answers |
| --- | --- | --- |
| `CurrentTrimGeometry` / `HasTrimGeometry` | the step's own result | "How was **this** file made?" — the Trim review line (§13) |
| `ArtefactTrimGeometry` / `HasDetectedGraphicBounds` | the artefact, wherever it is | "Where did the artwork sit in the original?" (§15) |

The domain `TrimGeometry` is exposed directly rather than flattened, because unlike a preparation
plan it holds nothing a screen must not see: two rectangles in source pixels, no Revision id and
no hash. One shared shape also means the review panel and any later display read the same detected
extent rather than each deriving one.

## 13. The operator surface

The Trim review panel gains a two-column block: **Detected graphic bounds** beside **Applied trim
bounds**, each stating origin, extent and size on matching lines so the operator reads straight
across and sees what the margin added.

Every number is the value the processor measured and the closing transaction stored, formatted.
The view model computes no rectangle, reads no file and scales nothing.

The block is **collapsed entirely** when there is no geometry — a manual crop, a non-trim artefact,
or a trim recorded before 0009 — rather than showing zeroes. `Left 0 px` would be a measurement
nobody took, and the shell must not fill the gap from the output's pixel dimensions: those give
the applied rectangle's size but never its origin, and never the detected extent.

A tight trim shows two identical rectangles rather than hiding one. "Detected and applied are the
same" is the useful statement there.

## 14. Localisation

Six new keys in both `Strings.resx` and `Strings.zh-CN.resx`, checked by
`LocalisationResourceTests`. Neutral wording: no resource key, no enum name and no internal type
name reaches the operator.

The four edge numbers are shown exactly as recorded rather than converted to an inclusive
convention, because converted they stop agreeing with each other — `980 - 120` is the `860 px` the
size line reports, while an inclusive `979` would leave the operator with numbers that do not add
up. The caption states the convention once, in words:

> Measured in the original image. Right and bottom are the first pixel outside the rectangle, so
> the size is the difference between the edges.

## 15. One step downstream — what SCRUM-11094 needs

`HasDetectedGraphicBounds` stays true after Trim hands the file on. At Print Dimensions the
trimmed file is the step's **input**, so a result-only reading would report nothing — and "where
did the artwork sit in the original" is exactly what a print size is being decided against.

Only the detected extent widens like this. The crop *parameters* of an upstream step are not a
legitimate thing to state beside a later step's review, which is why `CurrentTrimParameters` keeps
its narrower scoping.

This slice provides the value and the predicate. Drawing the SCRUM-11094 display is that task's
work, and it now has something true to draw.

## 16. Restart

The rectangles are read back from SQLite by a service that never saw the run, unchanged and
without recomputation. `Mappers.ToTrimGeometry` rebuilds both through `TrimBounds.FromEdges` and
`TrimGeometry.Create`, so a stored pair that could not have come from a margin expansion **fails
to load** rather than loading as a crop that never happened.

## 17. Immutability

Geometry, once written, is as immutable as the rest of an attempt's history. The
`ProcessingAttempt_TrimBounds_Immutable` trigger aborts any update that changes a geometry already
present.

So a return upstream that supersedes an attempt leaves that attempt's rectangles exactly as they
were, and a retry at a different margin has to be a new attempt rather than an edit to this one.
Two attempts at different margins keep independent applied rectangles and share a content one.

## 18. Metadata, artefact and downstream sizing agree

The applied rectangle's `Width × Height` is the trimmed Revision's pixel size — asserted for every
margin shape, including clamped ones — and that Revision is what `UpstreamRevisionOf(PrintDimensions)`
resolves to. The sizing plan is calculated from its pixel dimensions and bound to its hash, so the
recorded metadata, the file on disk and the print calculation are three views of one rectangle
rather than three independent numbers that happen to agree.

## 19. Return upstream and re-run

Returning to Trim and running again moves the reported bounds to the new attempt's rectangles
while leaving the superseded attempt's own row untouched. The operator sees the geometry of the
file in front of them; the history keeps the geometry of the file that was rejected.

## 20. Tight versus margined

`IsTightToContent` is `ContentBounds == AppliedBounds` — true for a tight trim, and true for a
margin clamped away entirely. Both are honest readings of "the margin added nothing to what was
cut", which is the question the flag exists to answer.

## 21. Historical rows

Not backfilled. Every attempt written before 0009 reads NULL.

Their geometry is genuinely unrecoverable: the output PNG's dimensions give the applied
rectangle's **size** but not its origin, and nothing records where in the source the content sat.
Inferring an origin from the size difference and the session's *current* margin would invent a
rectangle nobody measured — and the current margin may not be the one that attempt ran with. A
NULL that reads "not recorded" is worth more than a number that reads "measured" and was not.

## 22. Files changed

**New**

```text
src/PrintFlow.Domain/Trimming/TrimGeometry.cs
src/PrintFlow.Infrastructure/Sqlite/Migrations/0009_trim_bounds.sql
tests/PrintFlow.Tests/Unit/Trimming/TrimGeometryTests.cs
tests/PrintFlow.Tests/Integration/Persistence/TrimBoundsPersistenceTests.cs
```

**Modified**

| File | Change |
| --- | --- |
| `Domain/Attempts/ProcessingAttempt.cs` | `TrimGeometry?` property and `WithTrimGeometry` |
| `Workflow/Services/SessionService.cs` | tuple → `StepWork`; trim branch carries the rectangles; closing transaction records them |
| `Workflow/Services/SessionView.cs` | `ArtefactTrimGeometry`, `CurrentTrimGeometry`, `HasTrimGeometry`, `HasDetectedGraphicBounds`, `ResolveTrimGeometry` |
| `Infrastructure/Sqlite/SessionRows.cs` | eight nullable edge columns on `AttemptRow` |
| `Infrastructure/Sqlite/Mappers.cs` | `ToTrimGeometry`; write and read both directions |
| `Infrastructure/Sqlite/SqliteSessionRepository.cs` | upsert insert list, value list and `DO UPDATE` clause |
| `App/ViewModels/SessionViewModel.cs` | eight bound members and their change notifications |
| `App/Views/SessionScreenView.xaml` | the two-column bounds block |
| `App/Resources/Strings.cs`, `Strings.resx`, `Strings.zh-CN.resx` | six keys, both languages |
| `App/Resources/DisplayNames.cs` | origin / extent / size formatters |

## 23. Tests

| File | Covers |
| --- | --- |
| `Unit/Trimming/TrimGeometryTests.cs` | the pair and its invariant (§3–§5) |
| `Integration/Persistence/TrimBoundsPersistenceTests.cs` | the rectangles through the real service and real SQLite: recorded, survive restart, equal the output's size, independent per attempt, absent where nothing was measured, exposed by the read model, still readable downstream (§4–§6, §8, §11, §12, §15–§20) |
| `Integration/Persistence/MigrationTests.cs` | 0009 applies to a pre-0009 database, backfills nothing, refuses incoherent geometry, refuses a rewrite (§7, §8, §17, §19, §21) |
| `Integration/Ui/ReturnAndTrimControlsUiTests.cs` | both rectangles in localized words; differ by the margin; identical when tight; absent for a manual crop; tracked across a reject-and-re-run (§11, §13, §17) |
| `Integration/Ui/ViewRenderingTests.cs` | the block renders in the review panel with no binding errors, and renders under the zh-CN resources inside the signed-off review viewport (§13, §14) |
| `Architecture/LocalisationResourceTests.cs` | the six keys exist in both languages with their placeholders intact, and name no internal type or jargon (§13, §14) |
| `Architecture/TrimBoundaryTests.cs` | `TrimGeometry` is a domain trim type and stays one (§3) |
| `Architecture/MaximumBoundsBoundaryTests.cs` | the migration set ends at 0009 (§7) |

One pre-existing test was corrected rather than extended:
`PhotoshopTiffWorkflowOutputTests.The_success_transaction_branches_on_no_adapter_identity` scans
`SessionService.cs` source between two literal method signatures, and the second of them is
`PerformStepWorkAsync`'s. Renaming its return type to `StepWork` (§9) moved that anchor. The
assertion — that the success transaction contains no adapter identity — is unchanged; only the
string that locates its end was updated.

## 24. Results

```text
Passed!  -  Failed: 0, Passed: 10204, Skipped: 0, Total: 10204, Duration: 2 m 33 s
```

46 tests added against the 10,158 recorded at the SCRUM-11137 prerequisite attempt-timing gate.
Build: 0 warnings, 0 errors. No dependency added.

Against the source before this slice the geometry tests cannot compile — `TrimGeometry`,
`SessionView.CurrentTrimGeometry` and the eight columns did not exist — so "they fail without the
fix" is structural here rather than observed as a red run. The one assertion that *was*
observable both ways is the migration-set boundary, which read `0008` before and `0009` after.

## 25. Product boundaries

Unchanged by this slice: the trim algorithm and its `alpha > 0` rule; the `FailureCode`
vocabulary; the `ManualCropRequired` routing; attempt containment and lock atomicity; recovery's
fail-closed rules; the naming contract; every adapter. No dependency was added.

The one structural change outside persistence is `SessionService`'s internal result tuple becoming
`StepWork` (§9) — a private nested record, invisible outside the class.

## 26. Git state

Branch `master`, clean before the task, no push. Epic 11600 commits were not amended, rebased or
rewritten. One local commit, containing the migration, the source, the tests and this document,
following the convention of every preceding slice:

```text
src/PrintFlow.Domain/Trimming/TrimGeometry.cs                            new
src/PrintFlow.Infrastructure/Sqlite/Migrations/0009_trim_bounds.sql      new
tests/PrintFlow.Tests/Unit/Trimming/TrimGeometryTests.cs                 new
tests/PrintFlow.Tests/Integration/Persistence/TrimBoundsPersistenceTests.cs  new
docs/printflow/phase-11081-trim-bounds-contract.md                       new

src/PrintFlow.Domain/Attempts/ProcessingAttempt.cs                       modified
src/PrintFlow.Workflow/Services/SessionService.cs                        modified
src/PrintFlow.Workflow/Services/SessionView.cs                           modified
src/PrintFlow.Infrastructure/Sqlite/SessionRows.cs                       modified
src/PrintFlow.Infrastructure/Sqlite/Mappers.cs                           modified
src/PrintFlow.Infrastructure/Sqlite/SqliteSessionRepository.cs           modified
src/PrintFlow.App/ViewModels/SessionViewModel.cs                         modified
src/PrintFlow.App/Views/SessionScreenView.xaml                           modified
src/PrintFlow.App/Resources/Strings.cs                                   modified
src/PrintFlow.App/Resources/Strings.resx                                 modified
src/PrintFlow.App/Resources/Strings.zh-CN.resx                           modified
src/PrintFlow.App/Resources/DisplayNames.cs                              modified
tests/PrintFlow.Tests/Architecture/TrimBoundaryTests.cs                  modified
tests/PrintFlow.Tests/Architecture/MaximumBoundsBoundaryTests.cs         modified
tests/PrintFlow.Tests/Architecture/LocalisationResourceTests.cs          modified
tests/PrintFlow.Tests/Integration/Persistence/MigrationTests.cs          modified
tests/PrintFlow.Tests/Integration/Persistence/PhotoshopTiffWorkflowOutputTests.cs  modified
tests/PrintFlow.Tests/Integration/Ui/ReturnAndTrimControlsUiTests.cs     modified
tests/PrintFlow.Tests/Integration/Ui/ViewRenderingTests.cs               modified
```

---

**PASS — SCRUM-11081 TRIM BOUNDS PERSISTED, EXPOSED AND SHOWN**
