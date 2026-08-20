# Epic 11200 Part A — Dependency Cleanup and Trim Seam Audit

Preparation slice. No trimming algorithm and no image-review UI was implemented.

---

## 1. Dependency change

| | Before | After |
| --- | --- | --- |
| `Microsoft.Data.Sqlite` | 10.0.0 | **10.0.11** |
| `SQLitePCLRaw.*` (transitive) | 2.1.11 | **2.1.12** |

Patch-level bump in `Directory.Packages.props` only, with all three `packages.lock.json`
files regenerated (`dotnet restore --force-evaluate`). Nothing else moved: .NET stayed on
10.0.400, and Dapper, xUnit, Shouldly, CommunityToolkit and the Microsoft.Extensions
packages are untouched.

The obsolete `NuGetAuditSuppress` for `GHSA-2m69-gcr7-jv3q` was deleted from
`Directory.Build.props`. Its stated rationale — "NuGet's advisory record lists no patched
SQLitePCLRaw version" — is no longer true, so the suppression was removed outright rather
than rewritten or narrowed. NuGet auditing remains fully enabled; no global
`NuGetAudit`/`NuGetAuditLevel` override was introduced, and `TreatWarningsAsErrors` is
unchanged.

## 2. Vulnerability audit result

Verified in this repository before and after the change.

**Before** — `dotnet list package --vulnerable --include-transitive`:

```text
PrintFlow.Infrastructure  > SQLitePCLRaw.lib.e_sqlite3  2.1.11  High  GHSA-2m69-gcr7-jv3q
PrintFlow.App             > SQLitePCLRaw.lib.e_sqlite3  2.1.11  High  GHSA-2m69-gcr7-jv3q
PrintFlow.Tests           > SQLitePCLRaw.lib.e_sqlite3  2.1.11  High  GHSA-2m69-gcr7-jv3q
```

**After**:

```text
The given project `PrintFlow.Domain` has no vulnerable packages given the current sources.
The given project `PrintFlow.Workflow` has no vulnerable packages given the current sources.
The given project `PrintFlow.Infrastructure` has no vulnerable packages given the current sources.
The given project `PrintFlow.App` has no vulnerable packages given the current sources.
The given project `PrintFlow.Tests` has no vulnerable packages given the current sources.
```

This audit is independent of the suppression: `dotnet list package --vulnerable` reported
the advisory while the suppression was still in place, so the clean result reflects the
resolved graph and not a silenced warning. The subsequent `dotnet build` — which *does*
honour audit suppressions and had none — also emitted no `NU1903`.

No new advisory of any severity was introduced.

## 3. Regression result

| Gate | Result |
| --- | --- |
| `dotnet restore --locked-mode` | restored, lock files consistent |
| `dotnet build` | **0 warnings, 0 errors** |
| `dotnet test` | **5546 passed, 0 failed, 0 skipped** |

Identical to the Epic 11100 release-gate baseline. Epic 11100 invariants are covered by
the existing suite and all remain green — migrations (`MigrationTests`,
`DbInvariantTests`), restart/resume and startup recovery (`StartupRecoveryTests`,
`ApplicationStartupTests`, `RecoveryAndBranchTests`), session persistence
(`SessionServiceTests`, `RetryAndReviewTests`), automation lock
(`SingleInstanceGuardTests`, `ProcessLivenessTests`, `EnvironmentGateTests`), source
preservation (`WorkspaceTests`, `FileInspectorTests`), workflow E2E
(`FakeAdapterScenarioTests`, `SessionSmokeTests`) and `AddAnotherSizeTests`. No concrete
gap appeared, so no test was added.

---

## 4. Current Trim architecture

Trim is fully defined as a *shape* and entirely absent as *behaviour*.

**Declared today**

- `StepKind.Trim` — [SessionEnums.cs](../../src/PrintFlow.Domain/Sessions/SessionEnums.cs)
- `OperationKind.Trim` — [RevisionEnums.cs](../../src/PrintFlow.Domain/Revisions/RevisionEnums.cs)
- `AdapterKind.Internal` — [StepDefinition.cs](../../src/PrintFlow.Workflow/Definitions/StepDefinition.cs);
  `IsAdapterBacked` is deliberately **false** for it, so the environment gate does not treat
  trimming as external automation.
- Ordinal 4 in both `PrepareAsset` and `PrepareCustomerDesign`, with
  `IsSkippable: false, RequiresReview: true, ProducesRevision: true` —
  [WorkflowCatalog.cs:44-47](../../src/PrintFlow.Workflow/Definitions/WorkflowCatalog.cs#L44-L47).
  `GeneratePrintTiff` has no Trim step.
- Persistence round-trip `OperationKind.Trim ⇄ "TRIM"` —
  [Mappers.cs:105](../../src/PrintFlow.Infrastructure/Sqlite/Mappers.cs#L105).
- Display name `Strings.Step_Trim` — [DisplayNames.cs:39](../../src/PrintFlow.App/Resources/DisplayNames.cs#L39).

**The single behavioural hole**

[SessionService.cs:602](../../src/PrintFlow.Workflow/Services/SessionService.cs#L602),
`case AdapterKind.Internal:` — currently `return await InspectAsync(workingCopy.Value, …)`,
i.e. the working copy passes through unchanged. It claims no cropping behaviour and exists
only to exercise the attempt → validation → revision → review pipeline. **This one case is
where Epic 11200 plugs in, and it is the only production line that has to change.**

### Required flow, mapped onto existing types

```text
approved upstream Revision
  WorkflowSnapshot.UpstreamRevisionOf(StepKind.Trim) → RevisionId
  (falls through skipped Enhancement/BackgroundRemoval with no special case)
        │
        ▼
Trim operation
  SessionService.RunAdapterAsync, case AdapterKind.Internal
  IWorkspace.CreateWorkingCopyAsync(session, attemptId, source) → fresh Working\<attemptId>\
  → ITrimProcessor.TrimAsync(TrimRequest)  ← NEW, the only new port
        │
        ▼
output candidate
  WorkspaceFileRef in WorkspaceArea.Working
        │
        ▼
validation / hash
  IFileInspector.InspectAsync(absolutePath) → FileFacts { Format, ByteLength, Sha256,
  PixelWidth/Height, DpiX/Y, ColourMode, HasAlpha }
  One read: hashing IS the readability proof. Failure ⇒ WorkflowCommand.System.AttemptFailed,
  no Revision created.
        │
        ▼
ReviewRequired
  Revision.Create(..., OperationKind.Trim, file, facts, now) with SourceRevisionId = upstream
  → WorkflowCommand.System.AttemptSucceeded(attemptId, StepKind.Trim, revId, hash)
  → StepDefinition.RequiresReview: true ⇒ StepState.ReviewRequired
  → SessionView.CurrentArtefact = ArtefactView{ IsCurrentStepResult = true }
        │
        ▼
Approve / Reject
  WorkflowCommand.Approve(StepKind.Trim, ReviewedHash)  → StepState.Approved
  WorkflowCommand.Reject (StepKind.Trim, ReviewedHash, RejectionReason)
      → WorkflowEffect.InvalidateDescendants(subject, InvalidationReason.Rejected)
      → StepState.RetryRequired; IWorkspace.MoveToRejectedAsync retains the bytes
  ReviewDecision is append-only and bound to ReviewedSha256.
```

## 5. Reusable existing seams

Everything below already exists and needs **no change** for the first Trim slice:

| Need | Existing seam |
| --- | --- |
| Upstream input resolution | `WorkflowSnapshot.UpstreamRevisionOf(StepKind)` — already falls through skipped steps |
| Clean per-attempt input | `IWorkspace.CreateWorkingCopyAsync` — a fresh `Working\<attemptId>\` per retry, invariant 8 |
| Path handling | `WorkspaceFileRef` / `WorkspaceDirRef`; `ResolveAbsolute` is the only path join |
| Output validation + hash | `IFileInspector.InspectAsync` → `FileFacts`, one read for hash *and* metadata |
| Alpha metadata | `FileFacts.HasAlpha` (`bool?`), derived from the WIC `PixelFormat` in `WicFileInspector.InferHasAlpha`; `null` = unknown, never guessed |
| Derivation chain | `Revision.SourceRevisionId` + `OperationKind.Trim` |
| Review lifecycle | `StepState.ReviewRequired/Approved/RetryRequired`, `ReviewDecision`, `Approve`/`Reject`/`Retry` commands |
| Rejected-result retention | `IWorkspace.MoveToRejectedAsync` → `WorkspaceArea.Rejected` |
| Invalidation | `WorkflowEffect.InvalidateDescendants` + `SessionService.ComputeDescendantInvalidations` |
| Going back | `WorkflowCommand.ReturnToStep` + `CommandKind.ReturnToStep`, fully implemented in the engine ([WorkflowEngine.cs:208](../../src/PrintFlow.Workflow/Engine/WorkflowEngine.cs#L208)) |
| Manual result re-entry | `OperationKind.ManualImport` already exists |
| Failure reporting | `OperationResult<T>` / `OperationFailure` / `FailureCode`; `HandOff` for "operator, take over" |
| Determinism in tests | `CommandContext` supplies clock and ids; `SyntheticImages` builds real images through WIC encoders |
| Host for pixel work | `PrintFlow.Infrastructure` already sets `UseWPF` for WIC, so no new package is needed |

`ScopeGuardTests` / `DependencyRuleTests` / `BannedApiEnforcementTests` already forbid
`System.IO` in `PrintFlow.Domain`, `PrintFlow.Workflow` and `PrintFlow.App\ViewModels` —
the trim *port* must live in `Workflow/Ports`, the *implementation* in `Infrastructure`.

## 6. Missing seams

Genuinely absent today:

1. **`ITrimProcessor` port and its request/result types.** No trim port exists;
   `AdapterPorts.cs` covers Meitu and Photoshop only. This is the one new seam required.
2. **`SessionService` cannot construct a trim result.** Its constructor takes `IMeituProcessor`
   and `IPhotoshopOutputProcessor`; `AdapterKind.Internal` is handled inline with no
   collaborator. An `ITrimProcessor` parameter and DI registration in
   `ServiceRegistration.cs` are needed.
3. **No "manual crop required" outcome.** There is no way to express *"this file has no usable
   alpha, a human must crop it"*. `HandOff` is the nearest existing route, but it ends automated
   progression for the whole session, which is heavier than this case warrants.
4. **No image bytes reach the UI.** `ArtefactView` carries `RevisionId`, `FileName`, `FileFacts`
   and `IsCurrentStepResult` — deliberately no path. The session screen renders metadata text
   only. Any preview needs a new, explicitly-scoped read seam; a view model must not open files.
5. **No before/after pairing in the read model.** `SessionView.CurrentArtefact` is a single
   artefact. Comparison needs the upstream Revision alongside the current result.
6. **No trim parameters are persisted.** `Revision` records the *output*, not the margin/mode
   that produced it. Re-running a trim with a different margin is representable only as a new
   attempt.
7. **`ReturnToStep` has no UI surface.** Engine-complete and command-available, but no button
   and no view-model command exist ([WorkflowEngine.cs:971](../../src/PrintFlow.Workflow/Engine/WorkflowEngine.cs#L971) notes this explicitly).

Nothing above was added in this task.

## 7. Recommended Part B boundary

**Part B should be domain + infrastructure only. No UI.**

New types, all small:

```text
PrintFlow.Domain/Trimming/
  TrimBounds     — Left, Top, Right, Bottom in pixels; the content box, plus IsEmpty
  TrimMargin     — uniform or per-edge (Top/Right/Bottom/Left) safety margin in pixels
  TrimMode       — TightCrop | UniformMargin | EdgeSpecificMargin
  TrimOutcome    — Trimmed | NoChangeRequired | ManualCropRequired

PrintFlow.Workflow/Ports/
  ITrimProcessor — Task<OperationResult<TrimResult>> TrimAsync(TrimRequest, CancellationToken)
  TrimRequest    — WorkspaceFileRef Input, WorkspaceFileRef ExpectedOutput,
                   WorkspaceDirRef WorkingDirectory, TrimMode Mode, TrimMargin Margin
  TrimResult     — TrimOutcome, WorkspaceFileRef? ProducedFile, TrimBounds? ContentBounds,
                   original and resulting pixel size, TimeSpan Elapsed

PrintFlow.Infrastructure/Imaging/
  DeterministicAlphaTrimProcessor — WIC pixel read, alpha bounds, crop, encode
```

Bounds computation itself is pure integer work over a pixel buffer and should sit behind a
testable pure function so the algorithm can be tested without touching a file.

Suggested Part B order:

1. `TrimBounds` / `TrimMargin` / `TrimMode` / `TrimOutcome` value types + unit tests.
2. Pure alpha-bounds scan (buffer in, `TrimBounds` out) + unit tests over synthetic buffers.
3. `ITrimProcessor` port and `DeterministicAlphaTrimProcessor` over WIC, using
   `SyntheticImages`-style real PNGs.
4. Wire `SessionService`'s `AdapterKind.Internal` case to the port; register in DI; replace
   the pass-through placeholder and its comment.
5. Decide and implement the `ManualCropRequired` representation (see risks).

Explicitly **out** of Part B: image preview, checkerboard, zoom/pan, before/after comparison,
manual crop UI, `ReturnToStep` button. Those are a later, UI-only slice.

## 8. Deterministic Trim boundary — confirmed supportable

The intended behaviour, checked against the current seams:

| Behaviour | Supported by current architecture? |
| --- | --- |
| Alpha-based bounds only | Yes. `FileFacts.HasAlpha` gates it; pixel access belongs in Infrastructure. |
| Every pixel with `Alpha > 0` contributes | Yes. Pure function over the pixel buffer; no seam needed. |
| Safety margin | Yes, via `TrimRequest`. |
| Tight crop | Yes. |
| Uniform margin | Yes. |
| Edge-specific margin | Yes. |
| Cancel / no change | Yes. `TrimOutcome.NoChangeRequired` still produces a Revision, so the chain stays complete — matching how `PromoteApproved` records unchanged bytes. |
| No useful alpha ⇒ do not guess a background | Yes, and it is enforced by omission: nothing in the domain models "background colour", and the processor is never handed one. |
| No-alpha ⇒ Manual Crop Required | **Partially.** Detection is fine (`HasAlpha` is `false`/`null`); the *representation* of the outcome is the one genuine gap. See risks. |

No architectural violation is required for any of these.

## 9. Excluded from Epic 11200 — recorded boundary

Deterministic Trim must **not**:

- infer a white background;
- infer a black background;
- perform AI segmentation;
- call Meitu (that is `IMeituProcessor`, Epic 11300);
- call Photoshop (that is `IPhotoshopOutputProcessor`, Epic 11400);
- perform colour correction;
- generate W1 / white underbase (`SelectWhiteUnderbaseBranch` stays an explicit operator decision);
- change print dimensions (`SetPrintDimensions` stays a separate step).

Trim changes the canvas extent and nothing else. It reads alpha, computes an integer
rectangle, and writes the cropped result. Colour data is copied, never interpreted.

`AdapterKind.Internal` having `IsAdapterBacked == false` keeps this structural: the
environment gate that guards external automation does not apply to trimming, and trimming
has no route to an external application.

## 10. Review UI scope (audit only — nothing implemented)

| Later need | Status today |
| --- | --- |
| Image preview | **Missing.** `ArtefactView` carries no path or bytes, by design. |
| Transparent checkerboard | **Missing.** Pure XAML once a preview exists; no model change. |
| Before/after comparison | **Missing.** `SessionView.CurrentArtefact` is singular; the upstream Revision is not exposed alongside it. |
| Zoom / pan | **Missing.** Pure view concern; no model change. |
| Current-result review | **Sufficient.** `ArtefactView.IsCurrentStepResult`, `Sha256`, `Facts` and the `Approve`/`Reject` commands already work and are hash-bound. |
| Manual crop | **Missing.** Needs both the outcome representation (§6.3) and a UI. |
| `ReturnToStep` operator action | **Model sufficient, UI missing.** The command, its `CommandKind` and the full engine transition exist; no button does. |

Already sufficient: `RevisionId`, `FileName`, `Facts` (format, pixels, DPI, `HasAlpha`),
`Sha256`, `IsCurrentStepResult`, `AvailableCommands`, `Steps`, `CurrentStep`.

Missing and needed later: a way to obtain displayable bytes for a Revision, and the
upstream artefact next to the current one. **No field was added in this task** — neither is
needed for correctness today, and adding them before the UI slice would be speculative.

## 11. Risks and open points

1. **`ManualCropRequired` representation (decide in Part B).** A no-alpha file cannot be
   trimmed deterministically. Three options: (a) reuse `HandOff` — honest but ends automated
   progression for the whole session; (b) `AttemptFailed` with a dedicated `FailureCode` —
   lands in `RetryRequired`, but retrying the same deterministic operation will fail
   identically, which is misleading; (c) a new `TrimOutcome.ManualCropRequired` that leads to
   a `ManualImport` Revision. **(c) is recommended** — `OperationKind.ManualImport` already
   exists for exactly "the operator produced this by hand". It needs no new step kind.
2. **Trim parameters are not persisted.** Margin and mode are attempt inputs but appear
   nowhere on the `Revision`. Acceptable for Part B (the output hash is still the reviewed
   identity), but "why is this result 12px wider" is unanswerable from the record. Worth an
   explicit decision before the UI slice.
3. **WIC pixel-format breadth.** `InferHasAlpha` returns `null` for formats it does not
   recognise. `null` must be treated as *unknown*, not as *no alpha* — treating it as no-alpha
   would silently route valid files to manual crop. Needs an explicit test.
4. **Premultiplied alpha.** `Pbgra32` / `Prgba64` are in the recognised-alpha list. `Alpha > 0`
   is well-defined for both, but the processor must normalise the format on read rather than
   assume straight alpha.
5. **Large-image memory.** A full-resolution production PNG decoded to `Bgra32` is
   substantial. A single sequential scan is fine; caching decoded frames would not be.
6. **`GeneratePrintTiff` has no Trim step** — correct and intentional. Part B must not add one.
7. **SQLite bump risk: none observed.** No test changed behaviour, and migrations,
   restart/resume, persistence, the automation lock, source preservation, workflow E2E,
   `AddAnotherSize` and startup recovery all pass unchanged.

## 12. Git state

Branch `master`, ahead of `origin/master`. History linear; nothing rewritten, nothing
force-pushed.

```text
<this report>  Report: Epic 11200 Part A dependency cleanup and Trim seam audit
d64786f        11200: update SQLite dependency and remove obsolete audit suppression
14abc00        Report: Epic 11100 final release gate
```

The code commit touches five files only — `Directory.Packages.props`,
`Directory.Build.props` and the three `packages.lock.json` files. No production source
file, no generated image and no test was modified.
