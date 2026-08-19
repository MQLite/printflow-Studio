# Epic 11100 — Part 3C3A: Session processing and review controls

The Session placeholder becomes a working processing screen: step list, current-artefact
metadata, review panel, and the Confirm / Run / Approve / Reject / Retry / Skip / Hand-off
actions. Print dimensions, the W1 branch, Complete, AddAnotherSize, the fake-scenario selector
and image preview remain deferred to Part 3C3B and Epic 11200.

Nothing completed earlier was redesigned. Home, Recent Processing, Workflow Selection, the
startup guard, `StartupRecoveryService`, `SessionService`, the `WorkflowEngine` architecture,
the SQLite/workspace/hash logic and the fake-adapter scenario harness are all unchanged in
behaviour.

---

## 1. Session UI

`ViewModels/SessionViewModel.cs`, `Views/SessionScreenView.xaml`.

* **Identity** — output name, localised workflow, localised session state, localised current
  step.
* **Step list** — every step of the workflow with its localised name and `StepState`; the
  current step is emphasised. `IsCurrent` comes from `SessionView.CurrentStep`, so the screen
  never restates the "first step that is neither Approved nor Skipped" rule.
* **Current artefact** — file name, format, pixel dimensions, DPI, SHA-256 short form and a
  short revision identifier. The panel says explicitly when what it is showing is the step's
  *input* rather than its result.
* **Fake-mode banner** — `FAKE PROCESSING MODE`, localised, shown whenever the wired adapters
  are doubles.
* **Error area** — one localised sentence plus the stable failure code.
* **Deferred-scope notice** — kept, retargeted to what is genuinely still missing.

The artefact carries a workspace file *name*, never a path. The operator's original lives
outside the workspace and its location is not operator information; neither is the workspace
layout. A test asserts the displayed name contains no separator, drive letter or colon.

## 2. Command availability

Every button is bound to a property that reads `SessionView.AvailableCommands`:

```csharp
private bool Allows(CommandKind kind) => _session?.AvailableCommands.Contains(kind) == true;
```

There is no `if (state == ReviewRequired)` anywhere in the view model. Even the review panel's
visibility is `CanApprove || CanReject` rather than a step-state test, so the panel cannot drift
from the buttons inside it. A button is enabled by exactly the rule that will accept the click.

`WorkflowEngine.BuildProbe` already covered every command this slice needs — `ConfirmOriginal`,
`StartStep`, `Retry`, `Skip`, `HandOff`, `Approve`, `Reject` — and probes `Approve`/`Reject`
with the step's **own current hash**, never a synthesised one. Two changes were made, both to
make the existing contract explicit rather than to alter it:

* the `"probe"` placeholder reason for `HandOff`/`AbandonSession` is now a documented named
  constant, stating the §5 separation: the probe answers *may this command category be
  attempted here*, while the real command still has to satisfy its non-empty-reason guard;
* two unit tests pin the contract down — `Approve` availability tracks the step actually having
  a result, a wrongly-hashed `Approve` is still rejected, and `HandOff` with a blank reason is
  still `InvalidPayload`.

No command validation was weakened. `SetPrintDimensions`, `SelectWhiteUnderbaseBranch` and
`ReturnToStep` probing remains for 3C3B.

## 3. Confirm and Run

**Confirm Original** issues `WorkflowCommand.ConfirmOriginal` through `ISessionService`. It does
not set the step to Approved: whether confirmation is a bare acknowledgement or a hash-bound
design-readiness review depends on the workflow definition, and only the engine knows which.

**Run Step** issues `WorkflowCommand.StartStep(currentStep)`. The environment gate, the
automation lock, the adapter call, output validation, hashing, and both metadata transactions
all happen behind `ExecuteAsync`. The UI never touches `FakeMeituProcessor`,
`FakePhotoshopOutputProcessor` or `WorkflowEngine`.

Both refresh from the `SessionView` the service returned, which is rebuilt from what was
persisted. There is no local "what I think happened" state to drift out of step with SQLite.

## 4. Approve and Reject

**Approve** sends the hash of the artefact the screen actually displayed, and only when that
artefact is the current step's own result:

```csharp
private Sha256? ReviewedHash =>
    _session?.CurrentArtefact is { IsCurrentStepResult: true } artefact ? artefact.Sha256 : null;
```

`ArtefactView.IsCurrentStepResult` exists for exactly this: it distinguishes "the result under
review" from "the upstream file this step will consume", so an approval cannot be bound to
something that was merely on screen. If the file changed after display, `SessionService`'s
integrity re-check refuses with `RevisionIntegrityMismatch`, invalidates the Revision, and the
session does not advance. There is no automatic re-approval on any path.

**Reject** offers all seven MVP quick reasons as stable enum values behind localised labels,
plus optional notes. `ReviewRequired → RetryRequired`, the `ReviewDecision` is persisted, and the
judged Revision stays on record — only descendants of a rejected result are invalidated, and the
step simply stops offering it downstream.

## 5. Retry and Skip

**Retry** is deliberately *not* fused with Run Step. It moves the step to `Waiting` and stops
there, so the operator sees the progression. A subsequent run produces a new `AttemptId` and
therefore a fresh `Working\<attemptId>\` directory; the existing invariant is asserted again
here from the UI path.

**Skip** is offered only when `AvailableCommands` contains it, and records the stable default
reason. No Revision is created. Trim is not skippable and the button does not appear for it —
asserted directly.

## 6. Hand-off

`WorkflowCommand.HandOff` through `ISessionService`, with a stable English reason (persisted as
audit history, so it is not localised — the same choice `Skip.DefaultReason` and Home's abandon
reason make). Result: `SessionState = HandedOff`, automation lock released, every run action
withdrawn, and one localised sentence — *"Automated processing has ended. Continue manually."*

Nothing launches Photoshop or Meitu, watches a folder, or resumes automation later. Home still
lists the session and still offers Abandon: hand-off ended automation, not the record.

## 7. Read-model change

`SessionView` gained two members, both supplied by `SessionService`:

```csharp
ArtefactView? CurrentArtefact,
AdapterExecutionMode ProcessingMode
```

`CurrentArtefact` resolves to the current step's own Revision when it has one, otherwise the
upstream Revision it would consume — reusing `WorkflowSnapshot.UpstreamRevisionOf`, which
already defines the fall-through past skipped steps.

`ProcessingMode` is derived from the adapters actually wired into the graph, reported as `Fake`
if *either* is a double. Putting it here rather than letting a view model read configuration is
what lets the screen warn about synthetic output while still never referencing an adapter.

All five `SessionView` construction sites now go through one private `ViewOf` helper, so what
the screen knows cannot drift between the import path, the command path and the reload path.

## 8. Tests and smoke

`dotnet restore --locked-mode`, `dotnet build`, `dotnet test`: **5499 passed, 0 failed,
0 warnings, 0 errors**, from a 5474 baseline. The suite was run five times consecutively at the
implementation point and three times after the final additions — all green, so the SQLite
parallelism flake fixed in 3C2 remains gone.

**Controls** (`Integration/Ui/SessionControlsTests.cs`, real service, workspace, fake adapters
and SQLite; every outcome read back from the repository, not from the view model):

| Case | Asserts |
| --- | --- |
| Confirm | `OriginalConfirmation → Approved`, current step advances, no Revision created |
| Run Step | `Waiting → ReviewRequired`, real attempt, real Revision, file on disk |
| Artefact metadata | format/pixels/hash/revision shown, no path or separator in the name |
| Fake mode | reported for a fake-adapter session |
| Approve | `ReviewRequired → Approved`, decision bound to the displayed hash |
| Stale hash | file mutated after display → `RevisionIntegrityMismatch`, no progression, no review |
| Reject | `→ RetryRequired`, reason and trimmed notes persisted, audit intact |
| Retry | `→ Waiting`, then a new run with a distinct `AttemptId` and working directory |
| Skip | `Enhancement → Skipped`, no Revision; Skip absent for Trim |
| Hand-off | `→ HandedOff`, lock released, run actions withdrawn |
| Availability | button state matches `AvailableCommands` in all six states this slice reaches |
| Navigation | Session → Home mutates nothing; resume reopens the persisted state and hash |

**Smoke** (`Integration/Ui/SessionSmokeTests.cs`) — the four §21 journeys, driven through the
**real composed application graph**: `ApplicationStartup` performs the real configuration load,
directory creation, migration run, preset verification and crash recovery, and the real
`NavigationService` resolves real screens from the container. Only the single-instance guard and
the modal file dialog are stood in for. Synthetic PNGs only, written under the OS temp
directory.

* **A — success**: import → PREPARE_ASSET → Confirm → Run Enhancement → Review → Approve.
* **B — reject/retry**: Run BackgroundRemoval → Reject → Retry → Run → Approve; two attempts,
  two review decisions.
* **C — skip**: fresh session, Confirm → Skip Enhancement → Skip BackgroundRemoval; one Revision
  in total (the root), no attempts for either step, session sitting on Trim.
* **D — hand-off**: `HandedOff`, lock released, run actions gone, still listed on Home.

**Rendering** — `ViewRenderingTests` gained a case that drives the session to `ReviewRequired`
through the view model's own commands before rendering, so the artefact grid and review panel
bindings are actually exercised rather than collapsed. Binding errors remain test failures, and
the negative control still proves the listener works.

**Guards added** — `Shell_view_models_contain_no_System_IO_usage` (§18), and
`LocalisationResourceTests`: English/zh-CN key parity in both directions, every typed accessor
resolves to a real resource, no empty values. A missing translation is otherwise silent — the
accessor falls back to the key and shows `Session_Approve` on a button.

## 9. Defects found

1. **`SessionView` could not describe what the operator was looking at.** It carried steps and
   hashes but no file facts, so a review screen had no honest way to show format, pixels, DPI or
   a file name without reaching past the read model. Fixed by adding `ArtefactView`, resolved in
   the workflow layer from the Revisions the caller already holds.

2. **No UI-safe way to know processing was fake.** The adapter mode was known only to the
   composition root and the adapters themselves. A view model reading configuration to find out
   could have disagreed with what actually ran; `SessionView.ProcessingMode` reports what was
   really wired.

3. **`SessionView` was constructed at five sites with slightly different inputs.** Not yet a bug,
   but the shape that produces one. Consolidated into `ViewOf`.

4. **One incorrect assumption in a draft test**, corrected rather than accommodated: a rejected
   Revision stays valid — only its descendants are invalidated (plan §10.4) — and it is the
   step's `CurrentRevisionId` being cleared that stops it being offered downstream.

No defect was found in `BuildProbe`; §5's requirement was already met by the 3C2 work, and this
slice added the tests and documentation that pin it down.

## 10. Remaining Part 3C3B work

PrintDimensions input, W1 branch selection, the PhotoshopOutput operator flow, Complete,
AddAnotherSize, and probing for `SetPrintDimensions` / `SelectWhiteUnderbaseBranch` /
`ReturnToStep`. Then: the fake-scenario selector, image preview and comparison, crop, the
diagnostics/history surface, the runtime language switcher, real adapters, and the Epic 11100
final gate.

## 11. Git state

Branch `master`. One implementation commit (`11100: Session processing and review controls`)
plus this report. Source only: no runtime database, no synthetic smoke file, no logs, no
generated adapter output. Every smoke and test artefact lived under the OS temp directory and
was cleaned up on dispose. No force push.
