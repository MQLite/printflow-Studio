# Epic 11200 Part C3 — Return and Trim controls

The last implementation slice before the Epic 11200 final QA gate. Two operator surfaces were
missing: a way back to an earlier step, and a way to say how much canvas an automatic trim
should keep. Both are added here, together with the persistence that makes the second
auditable. No other image-editing capability was implemented.

---

## 1. ReturnToStep UI

A **Return to an earlier step** panel on the session screen: a destination selector, a Return
button, and a confirmation that stands between the two.

The view model issues `WorkflowCommand.ReturnToStep(target)` through
`ISessionService.ExecuteAsync` and touches nothing else — no step state is assigned, no
Revision is invalidated, no file is named. `SessionViewModel` gained three commands
(`BeginReturn`, `CancelReturn`, `ConfirmReturn`) and two pieces of screen state
(`SelectedReturnTarget`, `IsConfirmingReturn`), neither persisted.

`BeginReturn` and `CancelReturn` reach no service at all, so "cancelling changes nothing" is a
property of what the code *can* do rather than of what it happens to do.

## 2. Legal target authority

Destinations come from the workflow layer, never from WPF:

```
IWorkflowEngine.AvailableReturnTargets(WorkflowSnapshot) -> IReadOnlyList<StepKind>
SessionView.ReturnTargets                                -> IReadOnlyList<ReturnTargetView>
ReturnTargetView(StepKind Step, int Ordinal)
```

`AvailableReturnTargets` is implemented by **applying the real `ReturnToStep(target)` command**
for each step of the definition and keeping the accepted ones. There is therefore no second
statement of the rule to drift: whatever `ReturnToStep`'s preconditions are today or become
later, the offered list is exactly the set that satisfies them (§4, §8).

`ReturnTargetView` carries the stable `StepKind` and no display text — the label is built in
the shell by `DisplayNames.Step`, as every other step name on the screen is, so zh-CN parity
holds. `ReturnTargetRow`'s only constructor is internal and takes a `ReturnTargetView`, so a
destination cannot be invented from a bare `StepKind`.

`ReturnToStep` remains deliberately absent from `AvailableCommands`: its legality depends on
which step, so a single stand-in probe would answer a question no button asks (§8).

## 3. Invalidation behaviour

Not rewritten. The Part 3A rules were verified through the new operator path:

| | |
|---|---|
| later workflow state | reopened to `Waiting` |
| descendant Revisions | invalidated |
| dependent PrintOutputs | invalidated |
| Review history | retained |
| Attempts | retained, including failed ones |
| files | retained on disk |

Print dimensions and the W1 branch are cleared when the destination is at or above
`PrintDimensions` — they are decisions attached to the run being rewound and must be made
again explicitly.

**Sibling outputs (§7) — a finding.** Under the existing rules there is no return that
invalidates exactly one of two siblings, and this slice adds none. Siblings share a source by
construction, so every destination at or above that source reaches both, and every destination
below it reaches neither. `No_offered_return_target_invalidates_exactly_one_of_two_siblings`
asserts that over every offered target. The path that retires one size while its twin stands is
**rejection**, not return, and is covered by
`AddAnotherSizeTests.Rejecting_the_second_size_leaves_the_first_output_untouched`.

## 4. Return confirmation

Shown before the command is issued, en-US and zh-CN:

> Returning to this step will invalidate later derived results. Existing audit history will be
> retained.

> 返回该步骤会使其后生成的结果失效。已有的审核记录将予以保留。

It says nothing about files, because `ReturnToStep` deletes none. Changing the destination
retracts a standing confirmation — the warning names no step, so a confirmation carried across
a change would be a confirmation of something the operator did not read.

## 5. Trim margin UI

A **Trim margin** panel offering exactly the three existing modes, using the existing
`TrimMode` and `TrimMargin` — no second margin model was created.

- **Tight crop** — no input; zero on all four edges by construction.
- **Uniform margin** — one non-negative pixel box.
- **Edge-specific margin** — Top / Right / Bottom / Left, compact, no drag handles.

Validation goes through the domain factories (`TrimMargin.Uniform`, `TrimMargin.PerEdge`),
which refuse a negative value outright. The screen parses with `NumberStyles.None`, so a minus
sign, a decimal, an empty box and a word all fail the same way: an invalid-margin notice and
nothing recorded. A negative margin is never read as "crop further in".

**Default (§10).** Tight is pre-selected and is the initial persisted value. That is deliberate
and is the opposite of the W1 rule: a trim margin is an operational parameter of a
deterministic algorithm whose zero is meaningful, whereas a W1 branch is a classification of
the artwork that only a human can make, so a pre-selected branch would be that judgement made
by the software (MVP design §12). Nothing anywhere adds a non-zero safety margin.

**Visibility (§9, §17).** The panel appears only while `SessionView.CanSetTrimParameters` is
true — the engine's answer (Trim is current and between attempts) combined with the attempt
history that says whether this file is on the manual-crop path. A `ManualCropRequired` outcome
withdraws the margin controls and offers the crop tool instead: adding pixels cannot rescue an
image with no alpha to measure from. The Manual Crop path is unchanged C2 behaviour.

## 6. Trim parameter command and persistence

The view model never touches `ITrimProcessor`. A new command carries the decision:

```
WorkflowCommand.SetTrimParameters(TrimMargin)   -> WorkflowEffect.PersistTrimParameters
```

Accepting one starts nothing — no attempt, no file, no Revision. `StartStep(Trim)` then reads
the persisted value, so what runs is what was last recorded rather than whatever a screen was
holding. It survives a restart.

Legality: session active, workflow contains Trim, Trim is the current step, and its state is
one of `Waiting | RetryRequired | Failed | Interrupted`. Setting a margin against a result
already awaiting review is refused, because it would leave the session claiming one margin and
the attempt row another.

**Persistence closes the Part A gap.** Migration `0002_trim_parameters.sql` adds typed columns
(no JSON) to two tables, answering two different questions:

| table | question | lifetime |
|---|---|---|
| `ProcessingSession` | what will the **next** trim run use? | changes whenever the operator changes their mind |
| `ProcessingAttempt` | how was **this** Trim Revision produced? | written once, never updated |

Both carry `TrimMode` plus `TrimMarginTop/Right/Bottom/Left`, with `CHECK` constraints for the
mode vocabulary and for non-negativity. The attempt columns are deliberately absent from
`UpsertAttemptAsync`'s `DO UPDATE` clause, so "a retry never rewrites the first attempt's
settings" is a property of the SQL rather than a promise about the caller.

`ProcessingAttempt.TrimParameters` is `init`-only and is **null** for anything that is not a
deterministic trim — an adapter call, a promotion, and specifically a manual crop. Null reads
as "this attempt had no trim margin", never as "it used the default". Rows written before the
migration read back as `Tight` at session level, which is what they actually ran with.

## 7. Retry parameter history

```
Trim @ Uniform 4  ->  Reject  ->  RetryRequired  ->  margin changed to 1/2/3/4  ->  Run
```

Attempt A keeps `Uniform 4`; attempt B records `EdgeSpecific 1/2/3/4`; both stay auditable;
Revision B is the active result; both files remain on disk; the rejection stays as a
`ReviewDecision`. `NoChangeRequired` is unaffected — a full-canvas result still produces a Trim
Revision and still goes to review, never an auto-skip.

## 8. Review metadata

For a deterministic Trim Revision the review panel states, concisely:

```
Trim: Tight            /  裁切：紧贴
Trim: Uniform 8 px     /  裁切：统一 8 像素
Trim: T 4 / R 8 / B 4 / L 8 px
```

Resolved from the attempt whose `OutputRevisionId` is the Revision on screen — not from the
newest Trim attempt, which after a reject-and-re-run would label the result with settings that
produced a different file. Empty (and collapsed) for anything that is not a deterministic trim.
A uniform 0 keeps its mode rather than collapsing to Tight: the two produce the same rectangle
and mean different things. No general processing-history screen was built.

Before/After is untouched: changing a margin produces a new Trim Revision and C1 pairs it
through `Revision.SourceRevisionId` with no special case.

## 9. Tests and smoke

**Baseline 6039 → 6659 passed, 0 failed.** (288 of the increase is the `StepState × CommandKind`
matrix widening for the new command kind; the rest is new coverage.)

| suite | covers |
|---|---|
| `Unit/Workflow/ReturnTargetTests` (279) | §8 both directions, exhaustive over workflow × step × state: every offered target accepted, every unoffered target refused; ordering; empty for first-step and non-progressing sessions; query mutates nothing |
| `Integration/Persistence/TrimParameterTests` (13) | §20 Tight / Uniform / Edge-specific / clamped, asserted on inspected pixel sizes; §16 full-canvas still reviewed; §21 retry parameter history; restart survival; §17 manual-crop path |
| `Integration/Persistence/ReturnToStepTests` (11) | §22 PREPARE_ASSET, PREPARE_CUSTOMER_DESIGN → PrintOutput invalidated, AddAnotherSize siblings; §23 return after ManualImport; §7; §8 at service level |
| `Integration/Ui/ReturnAndTrimControlsUiTests` (19) | §24 — control visibility, real targets, confirmation-before-mutation, cancellation, three modes, invalid margins, absence during ManualCropRequired, review summary |
| `Integration/Ui/ViewRenderingTests` (+4) | §25 — return selector (open and confirming), all three margin states, trim review summary, zh-CN fit at 1000×700 |
| `Integration/Ui/SessionSmokeTests` (+2) | §26 — Smoke J and Smoke K through the real composed graph |
| `Architecture/TrimBoundaryTests` (+4) | §27 — one command carries a margin; `TrimParameters` is init-only; return targets come from the engine and rows cannot be invented; the read model carries no attempt history |

**Human smoke: none.** §26 asks for an interactive visual pass and this build has no
interactive desktop. What stands in for it is stated rather than implied — Smoke J walks
*margin → review → return upstream → run again at a different margin* through the real
`ApplicationStartup` graph, with each review state measured and arranged for real at 1000×700
and failing on any binding error, asserting the drawn bitmap sizes (12×10 → 9×9, then
12×10 → 11×8). Smoke K confirms the margin panel is gone and the crop tool present on a
`ManualCropRequired` outcome. **Nobody has looked at the result**; colour, spacing and the
readability of the zh-CN wording remain human judgements that have not been made.

## 10. Defects

None found in the existing invalidation, trim, manual-crop or review behaviour.

Two corrections were made to assumptions, not to code:

1. **Sibling survival on return does not exist** (§3 above). The spec's §7 second clause has no
   corresponding behaviour under the Part 3A rules; the honest property is asserted instead.
2. Three tests asserted the literal schema version `1`. Rather than bumping a literal that would
   go stale again, `MigrationRunner.NewestKnownVersion` was exposed and the tests now assert
   against it.

## 11. Remaining final-gate scope

Not implemented, and explicitly deferred (§28): resize handles, freeform crop, rotation,
annotation, colour tools, image filters, AI segmentation, fake-scenario UI, real Meitu, real
Photoshop, a runtime language switcher, a generic history browser.

Carried into the final QA gate:

- human visual sign-off of the two new panels, in both languages, on a real desktop;
- `AvailableReturnTargets` applies the real command once per step per view build — negligible
  at six steps and a pure engine, but worth a note if a workflow ever grows large;
- `SetOutputName` still has no screen (unchanged from Part 3C3B).

## 12. Git state

Branch `master`, 12 commits ahead of `origin/master` before this slice. Working tree contains
source, tests, one migration script and this report only — no runtime database, generated
images, screenshots, preview cache or synthetic smoke output. **Not pushed**; no force push.

---

`11200-C3 PASS — READY FOR EPIC 11200 FINAL QA`
