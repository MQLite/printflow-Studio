# Epic 11400 Part C2A — Validated TIFF workflow output integration to ReviewRequired

**Verdict: 11400-C2A PASS WITH NOTES — READY FOR PHOTOSHOP TIFF FINAL REVIEW INTEGRATION**

C2A implements only the authority transition: a validated production TIFF candidate becomes an
`AdapterOutput`, flows through the existing `SessionService` success transaction, and leaves one
Revision with the `PhotoshopOutput` step in `ReviewRequired`. No approval, rejection, promotion or
final-review UX is added, and global Production remains closed.

---

## 1. C1 candidate authority

C1's `PhotoshopValidatedTiffCandidate` is unchanged and remains the only factual basis for
workflow success. It still carries the exact managed Working destination, SHA-256, byte length,
pixels, 300 DPI, byte order, compression, sample layout, W1 spot identity and non-empty fifth
sample, alpha/pyramid absence, RLE layer payload, the unchanged backing Working hash, and the
settling observations.

**No historical TIFF was adopted.** C2A scans no directory, and the only candidate it can act on
is the one produced by the current Attempt's own execution. The C1 smoke TIFF, the exploratory
rejected non-RLE TIFF and every other QA artefact were left untouched; the live proof (§18 below)
ran on a fresh synthetic job into a fresh workspace.

## 2. Production processor composition

`ProductionPhotoshopOutputProcessor.GenerateAsync` was a fail-closed refusal. It is now a
**composition** of the already accepted seams, and contains no automation of its own — no second
resize, no second Action invocation, no second TIFF writer:

| Stage | Seam | Slice |
|---|---|---|
| A | `EnsureReadyAsync` → `OpenManagedWorkingFileAsync` | Part A |
| B | `GuardedPhotoshopDocumentPreparer.PrepareDocumentAsync` | B1A.3 |
| C | `GuardedPhotoshopW1Executor.ExecuteW1Async` | B1B |
| D/E | `GuardedPhotoshopTiffSaver.SaveProductionTiffAsync` (save, settle, independent validation) | C1 |
| F | `PhotoshopAdapterOutputFactory.Create` | C2A |

Each stage consumes the previous stage's factual result rather than re-deriving it, so a failure
cannot be stepped over: a resize that did not happen produces no `PhotoshopPreparedDocument`, so
no Action can run against it, so there is nothing to save. Photoshop's own report that it saved
successfully is **not** an accepted result — only a candidate that survived C1's independent read
of the bytes on disk can reach stage F.

`ProductionPhotoshopOutputProcessor.cs` contains no `new AdapterOutput`.

## 3. AdapterOutput boundary

Construction lives in one new file, `PhotoshopAdapterOutputFactory`, as its own type rather than a
private helper, so the boundary is a place rather than a promise. There is no overload accepting a
path, a save return, a `PhotoshopW1PreparedDocument`, or an unvalidated candidate — the four
fabrications §8 rules out are not expressible.

`AdapterOutput` is unchanged (`ProducedFile`, `Elapsed`, `AdapterNotes`). No Photoshop COM object
or type crosses the seam.

## 4. Output reference and SHA validation

Before anything is constructed, the factory requires agreement across every measure PrintFlow has:

- `candidate.Tiff == request.ExpectedOutput` — as values, area included;
- both references resolve, through the workspace, to the same absolute path;
- the file exists at that path;
- its length equals the candidate's recorded byte length;
- its **re-read** SHA-256 equals the candidate's recorded hash.

The final re-hash is deliberate: it is the last moment before a Revision can be created, and so
the only moment at which "the file the workflow is about to record" and "the file that was
validated" can still be compared. Any mismatch fails with `OutputValidationFailed` and context
`adapterOutputConstructed=false`, `revisionCreated=false`. It never resolves in favour of
whichever file Photoshop created.

## 5. Reserved output authority and success transaction

**Reserved destination.** The TIFF name is still rendered by the existing Workflow authority —
`OutputFileNaming.BuildProposedFileName(NamingArtifactKind.ProductionTiff, …, dimensions.WidthMm)`
producing `{Name}_{SizeMm}mm_CMYK_W.tif`. The C1 SizeMm rule is unchanged. Infrastructure renders
no filename and Photoshop chooses none.

**Placement changed, and this is the one substantive workflow change in the slice.**
`SessionService` previously reserved the Photoshop TIFF in `Approved\`. It now places it as a
sibling of the working copy, in the attempt's own `Working\<attemptId>\` directory — exactly where
the Meitu adapter already puts its result. Three reasons:

1. C1's accepted saver refuses any destination that is not `Working`, is not in the same directory
   as the backing file, or already exists. `ReserveOutput` fails all three (it materialises a
   zero-byte claim file in `Approved\`), so the accepted C1 guards could not have been satisfied
   without weakening them.
2. §28 forbids promoting to `Approved`. Writing a live Photoshop save into `Approved\` before any
   operator has seen the file is exactly that.
3. The attempt-id directory makes a retry's destination new with no collision handling, and puts a
   failed attempt's TIFF where startup recovery can see and quarantine it.

**Success transaction.** Unchanged and reused. `CompleteProducingStepAsync` commits
`AttemptSucceeded`, the new Revision, the `PrintOutput`, and the step transition to
`ReviewRequired` in a single `_repository.CommitAsync` mutation. No second success transaction was
invented, and the transaction branches on no adapter identity (asserted by test).

## 6. Attempt / Revision lineage

- `Revision.SourceRevisionId` = the attempt's `InputRevisionId` — the exact upstream Revision the
  attempt consumed, never an intermediate working copy, another Photoshop output, or a historical
  size decision.
- `ProcessingAttempt.OutputRevisionId` = the new Revision's id.
- `PrintOutput.SourceRevisionId` = the same upstream Revision.
- The source Revision is left valid, present and unmodified.

## 7. TIFF Revision metadata

The Revision's `FileFacts` come from the existing `IFileInspector` (`WicFileInspector`) — the same
inspector every other file goes through. Workflow parses no TIFF. It records format, byte length,
SHA-256, and pixel/DPI metadata where WIC can decode the container.

TIFF-specific facts `FileFacts` cannot express — the W1 spot channel, compression, sample layout,
layer RLE, pyramid and alpha absence — are kept in the producing Attempt's `AdapterNotes` rather
than deforming `FileFacts` to carry them (§13). The note is a fixed-shape sentence whose length
does not scale with the TIFF; no parser output is persisted. **No schema change and no migration
were required.**

A targeted test proves `WicFileInspector` reads a production-shaped five-sample CMYK+spot TIFF
without failing — the one link nothing else exercised against real production bytes, and the one
whose failure would have broken the entire success path on the workstation and nowhere else.

## 8. No intermediate Revision

Exactly one Revision per successful attempt, asserted directly
(`Revisions.Count(r => r.Operation == PhotoshopOutput) == 1`). None for B1A.3, none for B1B, none
before TIFF validation. The intermediate stages return in-memory Infrastructure records that carry
no Revision capability — an architecture test asserts `PhotoshopValidatedTiffCandidate` has no
`AdapterOutput`/`PrintOutput`/`Revision`/`ReviewRequired`/`AttemptSucceeded` member.

## 9. Failures create no Revision

Every stage failure — identity, resize, W1, save, settling, validation — returns a structured
failure with `adapterOutputConstructed=false` and `revisionCreated=false`, and the attempt ends
Failed with no Revision and no `ReviewRequired`.

For a TIFF that was written but does not validate (the C1 non-RLE case), the run refuses **after**
the file exists: the file is retained on disk for recovery and audit, there is no hard deletion,
no automatic save retry, and no Approved promotion.

## 10. Cancellation race

- **Before the success boundary:** cancellation observed during the save returns `Cancelled` and
  no `AdapterOutput`, even though bytes finished writing. At workflow level a cancelled run leaves
  a Cancelled attempt, no Revision, and a step that is not `ReviewRequired`.
- **After it:** the established D2A rule is reused rather than a Photoshop-specific answer being
  invented. The run is unregistered before `ExecuteAsync` returns, so a late Stop is refused —
  there is no window in which it can reach a committed attempt. The Revision, the
  `OutputRevisionId` and `ReviewRequired` all stand, and `LastAutomationStop` is null.

## 11. Retry

Retry after a failed attempt produces a new `AttemptId`, a new `Working\<attemptId>\` directory and
therefore a new reserved TIFF path, with `RetryOfAttemptId` linking the old attempt. The old
invalid TIFF is neither overwritten nor deleted. Still exactly one Revision — the failed attempt
contributed none.

## 12. Restart before the success commit

Simulated with a Running attempt and a complete, valid-looking TIFF at exactly the path a
successful run would have produced. Startup recovery moves the attempt to `Interrupted`, creates
no Revision, leaves the step without a result, and **quarantines** the orphan rather than adopting
it. Recovery never reasons "there is a valid TIFF on disk, so the run worked."

## 13. Restart after the success commit

Recovery changes nothing: no duplicate Revision, no second TIFF, no Photoshop rerun. A fresh
`SessionService` instance reloads the same Revision id, the same SHA, the same succeeded attempt
and the same `ReviewRequired` artefact.

## 14. ReviewRequired read model

`SessionView.CurrentArtefact` is the TIFF Revision, with `IsCurrentStepResult = true`, the TIFF
filename, its facts, its hash and its source Revision id. `Outputs` holds one entry with
`ReviewState.NotReviewed`. Only factual information already available is exposed; no final
Approve/Reject behaviour was added.

## 15. Fake compatibility

`Adapters.Mode` remains `Fake` and the Fake Photoshop workflow is fully operational. Fake was
**not** forced to emit a real TIFF — it keeps its established deterministic behaviour, now writing
into the attempt Working directory like every other producing step.

Workflow success semantics are adapter-agnostic: there is no Photoshop-production-only Revision
path. A test extracts the success transaction's own source and asserts it references no
`AdapterExecutionMode`, adapter id or `AdapterKind`, and that `Revision.Create(` appears exactly
twice in `SessionService` (the import root, and the one producing-step path both adapters use).

## 16. EnvironmentGate remains closed

`FoundationEnvironmentGate` is **unchanged** and still refuses every `Production` adapter with
`EnvironmentNotVerified`. `appsettings.json` still selects preset v1.14.0 and `Adapters.Mode =
Fake`. The production processor still declares `AdapterExecutionMode.Production`, so the gate keeps
applying to it, and reflection asserts no type in the Photoshop adapter namespace takes, holds or
returns an `IEnvironmentGate` — a component that can consult its own permission is one that can
decide it has permission.

The controlled bypass exists only inside the test seam: one `ControlledSeamEnvironmentGate`
declared in the test project, passed as a parameter to one `SessionService` in one opt-in smoke.
Nothing in Infrastructure ships a permissive gate, and adapter registration is untouched.

**Epic 11400 PASS does not enable production deployment.** Global Production stays blocked until
Epic 11500 implements real workstation verification.

## 17. Targeted tests

| Suite | Tests | Status |
|---|---|---|
| `PhotoshopWorkflowOutputTests` (new — adapter composition, order, AdapterOutput rules) | 12 | pass |
| `PhotoshopTiffWorkflowOutputTests` (new — success transaction, lineage, retry, restart, cancellation, read model) | 17 | pass |
| `PhotoshopWorkflowOutputBoundaryTests` (new — C2A architecture) | 7 | pass |
| `PhotoshopBoundaryTests` (updated) | 23 | pass |
| `PhotoshopTiffBoundaryTests` (updated) | 5 | pass |
| `PhotoshopFoundationTests` (updated) | 17 | pass |

All 23 required behaviours in §29 are covered. Three pre-existing tests were rewritten rather than
deleted, because C2A changes what they were asserting:

- `PhotoshopBoundaryTests` — "constructs no `AdapterOutput`" became "constructs `AdapterOutput`
  **exactly once, and only from a validated candidate**", with the single construction site named
  and every path to it required to take a `PhotoshopValidatedTiffCandidate`.
- `PhotoshopTiffBoundaryTests` — dropped only the `new AdapterOutput` clause; the Revision,
  PrintOutput and review-success clauses remain and matter more now that the adapter can succeed.
- `PhotoshopFoundationTests` — the seam no longer refuses unconditionally, so the test now proves
  a **real run stops at stage A**: no keystroke sent, no TIFF anywhere, and the failure states that
  no workflow output was constructed.

Broader targeted run (Photoshop + Architecture + Persistence + Naming + Recovery): **829 passed,
0 failed.**

## 18. Controlled live end-to-end

Opt-in smoke `PhotoshopWorkflowOutputWorkstationSmoke`
(`PRINTFLOW_PHOTOSHOP_WORKFLOW_SMOKE=1`), composing the real `ProductionPhotoshopOutputProcessor`,
a real `SessionService`, a real migrated SQLite database and a real `FileWorkspace`.

**First attempt refused, honestly:** `PhotoshopTargetLost` — "Photoshop did not take the foreground
within 5s; 'chrome' holds it. No input was produced." The adapter refused to fight for focus rather
than forcing it, `inputSent=false`, and no Revision was created. This is the guard working, not a
C2A defect. Chrome's windows were minimised and Photoshop activated; the run was repeated.

**Second attempt — SUCCESS.** One fresh synthetic job, branch `W1_1px`, no customer artwork.

| Fact | Value |
|---|---|
| Controlled workspace | `D:\PrintFlowStudio\QA\Epic11400C2A\20260831-165614-2C47FCE7` |
| Preset | v1.14.0 / `F74792276C0B264C9F064D1C82CB26806F7B836A543E0AF5FC8B4E7FB1738C62` |
| Global `Adapters.Mode` | `Fake` (asserted unchanged by the smoke itself) |
| Adapter | `photoshop-cc2019-production-v1` / `Production` |
| Session | `S_20260831T045615Z_b8addd66` |
| Source | 1200×800 px @ 240 ppi synthetic PNG |
| Size decision | max 50.8 × 100 mm @ 300 ppi → 600 × 400 px |
| Attempt | `01a0562c-db70-70f6-b27e-e11dc8bcce9a` — **Succeeded** |
| Produced TIFF | `Sessions/.../Working/01a0562c-.../PF_C2A_20260831-165614-2C47FCE7_51mm_CMYK_W.tif` |
| TIFF SHA-256 | `7B6E9DDC41CCDC56ACD797D7DFC4C60A71A043B43887C7820819904B351B3CA0` |
| TIFF bytes | 2 452 724 |
| TIFF pixels | 600 × 400 @ 300 × 300 dpi |
| Revision | `01a0562d-0a0c-7064-b256-43c1e716810e` |
| Source Revision | `01a0562c-da6c-7eec-9362-0fa60284c284` (unchanged, still valid) |
| `OutputRevisionId` | `01a0562d-0a0c-7064-b256-43c1e716810e` — exact |
| Step state | **ReviewRequired** |
| Revisions / PrintOutputs | exactly 1 / exactly 1 (`ReviewState.NotReviewed`) |
| TIFF files in workspace | exactly 1; none under `Approved\` |

Recorded adapter note:

```
production Photoshop TIFF; W1_1px branch; 600x400 px @ 300x300 dpi; IBM PC / little-endian,
compression none, 5x8-bit interleaved separated; spot W1 (photoshop spot True, non-white 240000 px);
alpha False, pyramid False, layers 1 all-RLE True; 2452724 bytes; sha256 7B6E9DDC41CC;
backing unchanged 9B627484613A; save TIFFEncoding.NONE/LayerCompression.RLE/ByteOrder.IBM,
as copy True; settled over 3 observations in 2.0 s; photoshop-tiff-validation-c1-v1;
limitations: W1 sample content is proven non-empty from the fifth uncompressed interleaved sample;
the inspector does not interpret the ink's visual meaning.
```

The recorded Revision hash was re-verified independently against the file on disk
(`sha256sum` → `7b6e9ddc41cc…3ca0`, matching exactly).

**Final Photoshop state:** process 21244 still holds
`PF_C2A_20260831-165614-2C47FCE7.png @ 100% (图层 1, W1/8) *` — the synthetic source document with
unsaved in-memory CMYK+W1 changes. Cleanup was **not** automated: no signed discard route exists,
so the operator closes it manually with Don't Save. The QA TIFF, database and session workspace are
retained; the hashes above are captured.

## 19. Complete-suite run

Justified once, because this slice enables the real Photoshop workflow success path and Revision
creation. Run exactly once, after all targeted tests were green, the controlled live integration
passed, product source was final and the build was clean:

```
dotnet test PrintFlowStudio.sln --no-build --no-restore
Passed! - Failed: 0, Passed: 9722, Skipped: 0, Total: 9722, Duration: 1 m 44 s
```

Build gate: **0 warnings, 0 errors.**

## 20. Evidence and preset

**No preset change. Remains v1.14.0.** C2A discovered no new production runtime fact — it composes
already accepted runtime capabilities rather than a new Photoshop control or identifier — so no new
workstation evidence was captured and no preset was minted. Workflow wiring changing is not a
reason to mint one.

**No dependency change.** No package, project or lock file was touched, so no vulnerability audit
was run here; the dependency audit remains with Epic 11400 Final QA.

## 21. Remaining C2B / final-review scope

Explicitly **not** implemented here, and now unblocked:

1. Approve / Reject on the TIFF Revision, and promotion to `Approved`.
2. **File-level promotion out of `Working\`.** The TIFF now lives in the attempt's Working
   directory. C2B owns moving it into `Approved\` on approval and `Rejected\` on rejection.
   Note: the `WorkflowEffect.CleanupWorking` emitted on `Complete` is currently **never carried
   out** — `SessionService` has no case for it and `IWorkspace.CleanupWorking` has no production
   caller — so the retained TIFF survives completion today. C2B should decide deliberately whether
   to wire that effect up as part of promotion, rather than inheriting the gap.
3. Moving a rejected TIFF to the Recycle Bin.
4. Final-review UX for the TIFF artefact.
5. Marking the session Completed on the basis of a final review decision.
6. Epic 11500: real workstation verification, so global Production can open.

## 22. Git state

Branch `master`, based on `a5097ea` ("11400: validate controlled Photoshop TIFF saves"), which was
present and unmodified throughout. No push, no amend, no rebase, no history rewrite.

**Product source (3 files, 1 new):**

- `src/PrintFlow.Infrastructure/Adapters/Photoshop/PhotoshopAdapterOutputFactory.cs` *(new)*
- `src/PrintFlow.Infrastructure/Adapters/Photoshop/ProductionPhotoshopOutputProcessor.cs`
- `src/PrintFlow.Infrastructure/Adapters/Photoshop/PhotoshopAutomationComposition.cs`
- `src/PrintFlow.Workflow/Services/SessionService.cs`

**Tests (4 new, 4 updated).** No synthetic input, generated QA TIFF, runtime database, Photoshop
workspace, screenshot, transcript or external preset/evidence file is committed.

---

## Notes on the verdict

PASS **with notes**, for two things a reviewer should see rather than discover:

1. **The TIFF's location moved from `Approved\` to the attempt's `Working\` directory.** This was
   required — C1's accepted guards could not otherwise be satisfied, and §28 forbids putting an
   unreviewed artefact in `Approved\`. It is nonetheless a change to previously accepted workflow
   behaviour, and it hands C2B a promotion step that did not previously exist for the TIFF
   workflows (they have no `ApprovedPngExport`-style promotion node).
2. **The first live attempt was refused** because another application held the foreground. The
   refusal was correct and produced no Revision, but it means the production path depends on the
   operator's desktop state at run time — worth carrying into Epic 11500's environment
   verification rather than leaving as a surprise.

All PASS criteria are met: only an independently validated TIFF candidate becomes `AdapterOutput`;
the processor composes the accepted stages in exact order; `SessionService` remains the sole
Revision authority; one successful attempt creates exactly one Revision with exact lineage and
SHA; the step reaches `ReviewRequired` atomically; failures, cancellation and interruption create
no fabricated Revision; restart cannot adopt an orphan TIFF; retry uses a new attempt and output
path; the controlled real Photoshop E2E reached `ReviewRequired`; normal Production remains
EnvironmentGate-blocked; no Approve/Reject/Approved/Recycled behaviour was added; targeted tests
pass; the build has 0 warnings and 0 errors; and the one justified full suite is green.
