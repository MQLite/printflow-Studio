# Epic 11100 — Part 3C3B: Dimensions, W1 and production-output UI

The session screen gains the production half of the workflow: print dimensions, the explicit
white-underbase decision, fake Photoshop output, Complete, Add Another Size, and a concise list
of the outputs a session already holds. With these, all three workflows can now be driven from
Home to `Completed` through the UI.

Nothing completed earlier was redesigned. `SessionService`, `WorkflowEngine`, `SessionView`,
Home, Workflow Selection, startup/recovery, the fake adapters, persistence, Revision integrity
and the review controls all keep their existing shape and behaviour. One pre-existing
persistence defect was found and fixed — see §8.

---

## 1. Print dimensions

`ViewModels/SessionViewModel.cs`, `Views/SessionScreenView.xaml`.

A small panel with a width and a height in millimetres, four preset shortcuts, a live preview
of the resulting pixels, and one Confirm button. It is visible exactly while the engine reports
`SetPrintDimensions` as available, so it appears on the `PrintDimensions` step and reappears
after Add Another Size without the view model restating either rule.

Confirmation goes through `WorkflowCommand.SetPrintDimensions` via `ISessionService.ExecuteAsync`.
The view model assigns nothing to the session.

**Rules stay where they were.** The screen's only contribution is turning typed text into a
number; whether that number is a usable size is the domain's answer. `PrintDimensions` gained a
non-throwing `TryFromMillimetres` for the operator-input path — a mistyped size is an ordinary
thing to type, not an exceptional condition — and it shares `IsUsableMillimetres` with the
existing throwing factory, so the two cannot diverge. A unit test asserts they refuse the same
inputs.

There is no pixel arithmetic in the view model. The preview and the persisted value are both
`PrintDimensions`' own conversion at the fixed 300 DPI, so they cannot disagree. Nothing is
rounded up, clamped, or substituted: an unusable size produces a localised notice and no
command, and the session is left untouched.

## 2. Preset shortcuts

`SizePreset` already existed but carried no millimetres anywhere in the codebase, so
`PrintDimensions.NominalMillimetres` was added beside it — the nominal ISO 216 sizes the shop's
presets are named after, in one place rather than in a view model.

A preset button types its size into the boxes and does nothing else. The operator still reads
the millimetres, may still change them, and still presses Confirm. Editing either box marks the
size `Custom`, so a size the operator typed is never recorded as having come from a preset.

No size-preset editor was built, and no automatic enlargement or resize exists anywhere in this
slice.

## 3. White-underbase branch

Three choices, in enum order, each labelled with its classification guidance:

```
0 px — fine details
1 px — ordinary artwork
2 px — solid / full rectangular artwork
```

**`SelectedWhiteUnderbaseChoice` starts null and is never given a starting value anywhere in the
codebase.** Nothing is pre-selected, nothing is marked recommended, nothing is sorted to imply
preference, and Confirm with no selection sends no command rather than falling back to a branch.
The guidance is advice to the operator; no code reads it, ranks it, or looks at the image.

Confirmation goes through `WorkflowCommand.SelectWhiteUnderbaseBranch`, carrying a stable
English justification naming the classification the operator claimed — stable rather than
localised for the same reason `Skip.DefaultReason` is: it is persisted as audit history.

## 4. Command probing

`WorkflowEngine.BuildProbe` now covers `SetPrintDimensions` and `SelectWhiteUnderbaseBranch`.
`Complete` and `AddAnotherSize` were already probed. `ReturnToStep` is deliberately still
unprobed: no screen in this slice needs it, and "may I return" depends on *which* step, so a
single stand-in target would answer a question no button is asking.

The stand-in payloads are valid by construction — an A4 size and an arbitrary branch — so the
payload check cannot be what answers the probe. What varies, and therefore what the answer
reports, is session state, current step, and whether the workflow produces a TIFF.

**No validation was weakened.** Tests assert both halves: probing reports availability truthfully,
and afterwards a zero-size `SetPrintDimensions` is still `InvalidPayload`, a blank-justification
branch selection is still `InvalidPayload`, and — the one that matters most — repeated probing
leaves `WhiteUnderbaseBranch` still null. A probe applies nothing, so no session can acquire a
branch it did not explicitly choose.

## 5. Fake Photoshop output

Run Step becomes available only once both production decisions exist, and it reaches
`FakePhotoshopOutputProcessor` through the ordinary `ISessionService` path. The UI holds no
adapter reference and calls no adapter.

The existing `FAKE PROCESSING MODE` banner is unchanged. A second, stronger sentence appears
with it whenever the workflow produces a TIFF:

> The TIFF produced here is synthetic foundation output and is NOT production-ready. No CMYK
> conversion, no W1 spot channel, no Photoshop Action and nothing prepared for Maintop has been
> performed. Do not print from it.

Localised in both resource files. It claims none of those things were done, and never claims any
of them were. A generated PNG obviously is not finished work; a file named `..._CMYK_W.tif`
looks exactly like something that could be sent to the printer, which is why it gets its own
warning.

Whether the warning applies comes from `SessionView.ProducesPrintOutput`, answered in the
workflow layer where the definition lives, so the screen does not compare step kinds.

## 6. Review, Complete and Add Another Size

Output review uses the existing Review panel unchanged. Approve remains bound to the hash of the
artefact the screen displayed; reject continues down the existing audit path. No TIFF-specific
review system was built.

Complete and Add Another Size are offered only when `AvailableCommands` contains them. There is
no shortcut that marks a step done, skips a required one, or completes around one — a test walks
a TIFF session through every stage asserting Complete stays unavailable until the terminal
artefact is approved, and that asking anyway is refused and changes nothing.

`PREPARE_ASSET` completes through its existing `ApprovedPngExport` terminal step, with neither
production decision offered at any point.

Add Another Size reopens a completed production session at `PrintDimensions` with both decisions
cleared, so the next output makes its own. Existing outputs are untouched — that is the engine's
sibling rule, not something the screen arranges.

## 7. Output list

`SessionView` gained `Outputs` (a `PrintOutputView` per production output, oldest first) and
`ProducesPrintOutput`. `SessionService.ViewOf` now takes the output rows and merges the
mutation's upsert delta by id, so a freshly approved output does not read as unreviewed until
the next reload.

Each row shows dimensions, W1 branch, review state, validity and the output file name — **no
path**. An invalidated row is dimmed and also says so in words. This is a "what have I got" list
so the operator can see Output A still exists while making Output B; it is not a history browser
and offers no way into anything.

## 8. Defect found and fixed

**Choosing a workflow left the previous workflow's step rows in the database.**

Metadata commits are upsert-only, and `ISessionRepository.LoadAsync` reads *every* `SessionStep`
row a session has. Selecting a workflow re-shapes the session onto a different step list, so the
old workflow's rows survived and came back as part of the reconstructed snapshot — a session
switched to `GENERATE_PRINT_TIFF` reloaded still waiting on `Enhancement`, a step that workflow
does not contain.

It had gone unnoticed because the in-memory `SessionView` returned by the command was always
correct, and because every existing test that selected a workflow either chose the import
default (no re-shape) or asserted only `Session.WorkflowType`. This slice hit it immediately:
the required GENERATE_PRINT_TIFF flow cannot be driven through the real Home → Workflow
Selection → Session path without it.

Fix, kept minimal:

* `SessionMutation` gained `RemoveSteps`, defaulting to empty. Stated explicitly rather than
  inferred as "whatever is not in `UpsertSteps`" — startup recovery legitimately commits with no
  steps at all, and under an inferred rule that commit would delete every step a session has.
* `SessionService` populates it by comparing the two step lists, so nothing here has to be kept
  in step with the workflow catalogue.
* `SqliteSessionRepository` deletes those rows inside the same transaction as the upserts, so
  the step list is never briefly a mixture of both workflows. Revisions, attempts and reviews
  are untouched, so the audit trail of what was actually done survives the change of workflow.

Regression test: for each of the three workflows, a re-shaped session reloads holding exactly
that workflow's steps and no others, with `CurrentStep` resolving to a step it actually has.

## 9. Tests and smoke

Full suite: **5546 passed, 0 failed, 0 skipped**, from a baseline of 5499 — 47 added. Clean
rebuild: **0 warnings, 0 errors**. Four consecutive full runs were green; the SQLite flake did
not reappear.

New coverage:

* **`DimensionsW1AndOutputTests`** (25) — dimensions entered and persisted; six invalid-input
  cases refused with nothing persisted; preset fills and stays editable; edited preset records as
  Custom; W1 offers all three with none chosen; each branch persists as chosen; Run Step withheld
  until both decisions exist, and specifically still withheld with a size but no branch; fake
  output reaches `ReviewRequired` with a real file and hash; the whole GENERATE_PRINT_TIFF flow
  to `Completed` proving no Meitu call and no Trim; Complete unavailable too early; PREPARE_ASSET
  completing without dimensions or a branch; the PREPARE_CUSTOMER_DESIGN production tail from an
  approved Trim; Add Another Size only when legal; two sizes both surviving; rejecting B leaving
  A valid and approved; the output list's metadata and bare file name; and a stage-by-stage
  agreement check between the screen's production controls and `AvailableCommands`.
* **`EnginePurityTests`** (2) — the probing contract described in §4.
* **`ValueObjectTests`** (4) — `TryFromMillimetres` agreeing with `FromMillimetres`, and the
  preset nominal sizes.
* **`HomeAndWorkflowSelectionTests`** (3) — the §8 regression.
* **`ViewRenderingTests`** (2) — the dimensions and W1 panels, and the completed state with its
  output list, both rendered for real with binding errors escalated to failures. These panels are
  collapsed on every previously covered screen, so none of their bindings had been resolved
  anywhere before — including the preset buttons' `RelativeSource` command binding.
* **`SessionSmokeTests`** (3) — the §22 smoke passes through the real composed graph.

"No Meitu attempt" is asserted at the seam: `HomeScreenHarness` now wires a
`CountingMeituProcessor` that delegates to the same fake, so the claim does not rest on the
assumption that an attempt row always precedes an adapter call.

Smoke passes, all through `ApplicationStartup`'s real container and the real `NavigationService`,
on synthetic files only:

* **A** — GENERATE_PRINT_TIFF: confirm → dimensions → W1 1px → fake output → approve → complete.
* **B** — completed → Add Another Size → new dimensions → W1 2px → fake output → approve; both
  outputs valid, approved, present on disk and displayed.
* **C** — PREPARE_CUSTOMER_DESIGN driven to an approved Trim with fake processing, then
  dimensions → W1 0px → fake output → approve → complete.

Localisation parity holds: every new string exists in both `Strings.resx` and
`Strings.zh-CN.resx`, and every typed accessor resolves to a real resource.

## 10. Architecture

The view model still performs no file-system access, issues no SQL, references no adapter and
assigns no step state. The `BannedApiEnforcementTests` scan over `ViewModels/` still passes.
Every control's availability reads `SessionView.AvailableCommands` — the engine's own answer —
so a button is offered by exactly the rule that will accept the click.

## 11. Deferred, as specified

Image preview, side-by-side comparison, checkerboard, crop, the fake-scenario selector UI, a
diagnostics or history browser, a runtime language switcher, real Photoshop, real Meitu, real
trimming, real TIFF CMYK/W1 validation, Maintop, and the Epic 11100 final QA report.

## 12. Remaining final-gate work

Not attempted in this slice, and what the Epic 11100 final gate still owes:

* the consolidated Epic 11100 QA report;
* a manual pass on a real workstation with a real window — everything here is a composed-graph
  smoke plus rendered-layout binding checks, which is not the same as somebody looking at it;
* `ReturnToStep` has no UI, so an operator cannot yet rewind a session from the screen even
  though the engine and persistence support it fully;
* the fake-scenario selector, which would let a manual tester drive the failure paths without
  writing a test.

## 13. Git state

Branch `master`, two commits ahead of `origin/master`:

* `11100: dimensions, W1 and production-output workflow UI`
* this report.

No generated TIFF, runtime database, synthetic smoke asset or log entered the repository —
workspaces and databases are created under the OS temp directory by the test fixtures and
removed on dispose.

---

`PART 3C3B PASS — READY FOR EPIC 11100 FINAL QA`
