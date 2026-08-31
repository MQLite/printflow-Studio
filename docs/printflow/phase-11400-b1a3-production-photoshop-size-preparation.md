# Epic 11400 Part B1A.3 — Production Photoshop Size Preparation and Actual Read-Back

**Verdict: 11400-B1A.3 PASS — READY FOR ACCEPTED CMYK + W1 ACTION**

This slice implements only the real in-memory Photoshop size/resolution preparation and factual
read-back. It does not execute W1, change colour mode, save, write TIFF, construct `AdapterOutput`
or `PrintOutput`, create a Revision, complete the Photoshop workflow step, or enable global
Production mode.

## 1. Accepted authority and evidence impact

Work began under immutable preset v1.11.0, SHA-256
`A6E5DC172817F2F992114A1FDE0DCBAACC80D9CADD148C37D25CA3F816AC8AD1`. That preset remains
read-only and byte-identical.

Live integration discovered one new runtime fact: Photoshop CC 2019 ExtendScript has no global
`JSON` object (`Error 2: JSON is undefined`). Production therefore cannot rely on
`JSON.stringify` for factual return data. The accepted replacement is the fixed `PF-B1A3-1`
six-line protocol with `encodeURIComponent` strings and invariant numeric fields. It was safely
exercised across the complete live matrix.

The new immutable evidence is:

```text
D:\PrintFlowStudio\Baseline\workstation-v1\apps\photoshop-2019\production-preparation-runtime.json
SHA-256 31CF04CD9F9306018C981DD753659C8D044AAC45AEAC68BC0E7B27C2539E3A5D
```

Immutable preset v1.12.0 supersedes v1.11.0, carries all 23 inherited entries plus that evidence,
and reverified all 24 entries exactly:

```text
D:\PrintFlowStudio\Baseline\workstation-v1\preset\printflow-workstation-v1.12.0.json
SHA-256 7EAC531AC3464DEBBB447CC68D8226CB43CA653FD1345174E885396B6C83D20F
```

Both new external files are read-only. `appsettings.json` selects v1.12.0 by that exact digest and
`Adapters.Mode` remains `Fake`.

## 2. Part A safety reuse

Immediately before every mutation the preparation operation re-runs the accepted executable
path/product version/file version/SHA check, requires exactly one accepted process, compares PID
and start time to the opening record, rechecks liveness, requires the exact accepted visible main
window handle/owner/class, refuses a disabled frame or visible titled owned dialog, classifies the
editor, and repeats the Part A absolute-path identity probe. Basename, tab position and document
index never establish identity.

The native program independently compares `app.activeDocument.fullName.fsName` to that same exact
path and compares source pixels before reaching `resizeImage`. The Running Object Table route gets
only the already-running `Photoshop.Application.130` object, never activates the registered
LocalServer, and requires the automation object's reported application directory to equal the
accepted executable directory.

Photoshop was shared throughout. The live cases observed document counts 5, 7, 8 and 10. No
operator-document path, pixels, channels or content were enumerated; only counts and the exact
managed synthetic active document were observed. Photoshop and all operator documents remained
open and untouched.

## 3. Closed production preparation seam

`IPhotoshopPreparationAutomation.PrepareDocumentAsync` accepts only a
`PhotoshopOpenedDocument`, the closed `PhotoshopPreparation` union, and cancellation. It is an
Infrastructure-only seam beside the Part A foundation. It exposes no arbitrary script,
Action Manager descriptor, action name, resampling enum, generic width/height pair, save, export or
workflow output capability.

`ProductionPhotoshopOutputProcessor.GenerateAsync` remains an unconditional structured refusal
before Photoshop is touched. Resize success cannot construct `AdapterOutput`, create a Revision,
complete `PhotoshopOutput`, or create `ReviewRequired`.

The operation reads no Session state, preset selection, operator override, source Revision lookup,
target calculation or enlargement confirmation. Its sole geometry input is the already-authorised
immutable `PhotoshopPreparation`. Production Infrastructure neither references nor constructs
`EnlargementAuthority`.

## 4. Neutral policy and supported variants

The closed Infrastructure-only mapping is exact:

| Neutral policy | Photoshop CC 2019 identifier |
| --- | --- |
| `None` | `ResampleMethod.NONE` |
| `BicubicSharper` | `ResampleMethod.BICUBICSHARPER` |
| `PreserveDetails` | `ResampleMethod.PRESERVEDETAILS` |

An unsupported enum value is a precondition failure with `fallbackUsed=false`. No Automatic,
remembered preference, Bicubic Smoother or content-dependent fallback exists.

The implementation accepts exactly `FitWithinBoundsPreparation` and `TargetEdgePreparation`.
ResolutionOnly sends no physical edge. Resize sends only the already-resolved `Width` or `Height`
and its millimetres at 300 PPI; projected pixels are not present on the native command. `LongEdge`
cannot cross the boundary because the preparation exposes the resolved `LimitingEdge` type.

## 5. Native operations

The three fixed operation forms are:

```text
ResolutionOnly: resizeImage(undefined, undefined, 300, NONE)
Width:          resizeImage(UnitValue(mm, 'mm'), undefined, 300, fixedMethod)
Height:         resizeImage(undefined, UnitValue(mm, 'mm'), 300, fixedMethod)
```

The native program captures the exact pre-operation facts, rejects wrong path/source pixels,
pre-existing W1, non-RGB, non-8-bit, non-three-component RGB and pre-existing spot channels, then
invokes `resizeImage` exactly once. It captures actual facts synchronously afterwards. There is no
retry, alternative method or corrective second resize.

## 6. Factual read-back and validation

`PhotoshopPreparedDocument` contains factual `Before` and `Actual` observations: exact full path,
pixel pair, PPI, physical millimetres, colour mode, bit depth, ordered channel names/types, W1
presence, document count, applied direction/policy, commanded edge,
`OtherDocumentsMayBeOpen`, and the unchanged backing SHA. It is not an `AdapterOutput`, Revision or
print artifact.

Acceptance requires actual pixels to equal the immutable projected pair exactly; no ±1 tolerance
exists. Resolution must equal 300 within `1e-7` PPI. The commanded physical edge must be within
one half of a 300-PPI integer pixel plus `1e-6` mm API noise. Integer geometry must be the nearest
proportional result. Path, mode, bit depth, ordered channels, W1 absence and document count must be
unchanged.

The accepted normal input boundary is RGB/8 with exactly three component channels and no spot
channel. Existing supported alpha channels are preserved by exact ordered channel comparison.
Unsupported state fails inside the fixed native program before mutation where possible; it is
never normalised.

## 7. Disk and output proof

Before native input, the backing Working SHA must equal the immutable preparation source SHA. The
same file is hashed again after the call and must be identical. The containing managed directory's
file set is compared before/after. The successful live cases retained identical SHA values and one
source file only. No PSD, PSB, PNG/JPEG derivative, TIFF or other output was created.

There is no `doAction`, `PrintFlow DTF`, colour-mode conversion, Save, Save As, TIFF naming,
`AdapterOutput`, `PrintOutput` or Revision construction in the preparation path. The accepted W1
Action remains untouched.

## 8. Cancellation, target loss and modal behavior

Cancellation observed before the synchronous native call returns structured `Cancelled`, invokes
zero mutation and leaves the backing file unchanged. Once the call begins, cancellation does not
claim interruption or rollback: safe post-observation finishes, the backing file remains unsaved,
and the failure context says an in-memory prepared result may be retained.

Process/window/document loss after mutation never becomes success and never selects another
document. The result is structured target-lost/unknown retained state, with no save or later input.
A visible titled owned dialog or disabled main window before mutation prevents resize. After
mutation it stops post-input immediately. Untitled Photoshop `OWL` floating chrome is handled by
the established Part A rule and is not misclassified as a modal.

## 9. Live matrix

Every case used a separate fresh synthetic managed Working PNG. No customer artwork was used.

| Case | Before | Command | Projected | Photoshop actual | Preservation |
| --- | --- | --- | --- | --- | --- |
| ResolutionOnly | 1200×800, 240 PPI | no edge, NONE | 1200×800 | 1200×800, 300 PPI, 101.6×67.7333333333333 mm | RGB/8/channels unchanged; no W1; SHA exact |
| ProportionalShrink | 1200×800, 240 PPI | Width 50.8 mm, BICUBICSHARPER | 600×400 | 600×400, 300 PPI, 50.8×33.8666666666667 mm | RGB/8/channels unchanged; no W1; SHA exact |
| AuthorisedEnlarge | 1200×800, 240 PPI | Height 135.4666666667 mm, PRESERVEDETAILS | 2400×1600 | 2400×1600, 300 PPI, 203.2×135.466666666667 mm | RGB/8/channels unchanged; no W1; SHA exact |
| Exact midpoint | 2000×1000, 240 PPI | Width 84.709 mm, BICUBICSHARPER | 1001×501 | 1001×501, 300 PPI, 84.7513333333333×42.418 mm | RGB/8/channels unchanged; no W1; SHA exact |

The midpoint proves Photoshop agrees with Domain midpoint-away arithmetic exactly: 1000.5 becomes
1001 on the commanded edge and the proportional 500.5 becomes 501. No tolerance was added.

The live route also demonstrated safe refusals: unsupported `JSON.stringify` produced no retry or
success claim; transient signed identity-control loss stopped before mutation and later attempts
used new Working files. No new identity control/signature was accepted.

## 10. Cleanup

Nine exact synthetic documents from discovery, safe refusals and accepted cases remain open in
Photoshop. The final live document count was ten including pre-existing operator work. Before any
cleanup the exact synthetic identity would have to be re-proved. Because the accepted close route
does not include a signed unsaved-changes discard control, PrintFlow did not press Don't Save,
close a modified document, close Photoshop or delete the retained workspaces. Operator cleanup is
required.

No synthetic Working file, runtime workspace, screenshot, smoke transcript or external
evidence/preset file is added to Git.

## 11. Focused verification

The complete 9,000+ suite was not run. The exact focused groups were:

| Focus | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| B1A.3 preparation, mapping and architecture | 22 | 0 | 0 |
| Part A identity/window/shared-automation plus directly affected Meitu boundary | 176 | 0 | 0 |
| preparation plans, target-edge midpoint/rehydration, request/workflow boundary | 135 | 0 | 0 |
| configured preset/provider/runtime evidence | 12 | 0 | 0 |
| **Total targeted** | **345** | **0** | **0** |

The native-method inventory gained only ROT retrieval declarations; therefore the directly
affected Meitu automation boundary and UI-driver tests were included. No workflow state machine,
persistence/migration, Session/Attempt contract or broad shared architecture changed, so the
test scope was not widened to the full suite.

Final normal solution build, using the repository-pinned user-local .NET SDK 10.0.400:

```text
dotnet build
0 warnings, 0 errors
```

The machine-wide `dotnet.exe` exposes only SDK 8.0.418; the pinned SDK is already installed at
`C:\Users\admin\AppData\Local\Microsoft\dotnet\dotnet.exe`. No SDK configuration changed.

No PackageReference, central package version or lock file changed. Dependency graph unchanged;
vulnerability audit deferred to Epic 11400 Final QA.

## 12. Remaining B1B / Part C scope

B1B still owns the accepted CMYK + W1 Action and its factual result validation. Part C still owns
TIFF Save As, TIFF naming and validation, `AdapterOutput`, workflow success and Revision creation.
B1A.3 performs none of them and does not make the Photoshop output workflow step successful.

## 13. Git state

Preflight started on `master` at `054822b`, ahead of `origin/master` by 26 commits, after preserving
B1A.2E as normal commits `dbd7612` and `054822b`. The dependency graph is unchanged. B1A.3 source,
focused tests and the v1.12 appsettings pointer were preserved in normal local commit `d960a03`.
This report is the only subsequent repository change before its own normal report commit. No
amend, rebase, history rewrite or push was performed.

11400-B1A.3 PASS — READY FOR ACCEPTED CMYK + W1 ACTION
