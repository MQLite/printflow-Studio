# Epic 11400 Part B1A — Photoshop Print Dimensions and 300 PPI Preparation

**Verdict: 11400-B1A NOT READY**

B1A stopped before implementation and before any Photoshop document mutation because the accepted
authority does not select a resampling method for shrink operations. The design requires one fixed,
validated method; the signed baseline records the method as unresolved. Choosing a Photoshop
default would change image quality without product acceptance.

No resize seam was added, no Photoshop-native preparation call was made, no document was opened,
resized, saved or closed, and no workflow output or Revision was created.

## 1. Revised operation order

The B1 discovery report established that all three canonical W1 Actions begin with CMYK
conversion. The accepted implementation order is therefore:

1. **B1A:** establish final print dimensions and 300 PPI while preserving the current colour mode;
2. **B1B:** run the accepted W1 Action, which owns CMYK conversion and W1 creation;
3. **C:** save and validate the production TIFF, then allow workflow Revision creation.

This report is the current reporting authority for that revised order. The historical
`phase-11400-b1-photoshop-w1-action-execution.md` report remains unchanged and is the evidence for
the revision. The canonical `.atn` was neither changed nor executed. No replacement Action was
created.

## 2. Existing PrintDimensions contract

`PrintFlow.Domain.Outputs.PrintDimensions` is the numeric authority already passed through the
workflow in a typed `PhotoshopRequest`.

| Fact | Existing authority |
| --- | --- |
| width and height in millimetres | `PrintDimensions.WidthMm` and `HeightMm`; each must be finite and greater than zero |
| production resolution | fixed `PrintDimensions.ProductionDpi = 300` |
| target pixels | `millimetres / 25.4 * 300` |
| rounding | `Math.Round(..., MidpointRounding.AwayFromZero)` independently for width and height |
| named nominal sizes | `PrintDimensions.NominalMillimetres`; named values are operator-editable starting points, not preset limit enforcement |
| Session persistence | the full value is carried by `WorkflowCommand.SetPrintDimensions` / `WorkflowEffect.PersistPrintDimensions`; SQLite stores millimetres, pixels and preset and reconstructs through the Domain factory |
| adapter request | `PhotoshopRequest.Dimensions` is the same typed value; Infrastructure must not repeat the conversion arithmetic |

The current value object checks internal numeric consistency only. It does not receive source image
dimensions and therefore cannot prove proportionality, shrink-only behaviour, preset limits, or
effective-DPI sufficiency. Those checks remain necessary at the preparation boundary before any
Photoshop operation.

## 3. Resampling and aspect-ratio authority

### Resampling — blocking

The design says the validated Photoshop procedure uses a fixed, validated resampling method, but it
does not name one. Preset v1.9.0 says only `proportional: true`, `shrinkOnly: true`, 300 PPI and the
physical limits. It contains no interpolation/resampling-method value.

The Epic 11000 baseline is explicit:

- `Resize/resampling` is **PARTIALLY CONFIRMED**;
- the observed Image Size dialog showed `保留细节（扩大）` (“Preserve Details (Enlargement)”),
  with the evidence record saying it was not confirmed before applying;
- `validate shrink resampling quality and interpolation choice` remains pending;
- the Photoshop interpolation preference remains pending.

That observation cannot be promoted into a shrink-quality policy, and Photoshop's remembered
setting cannot be used. No Bicubic, Bicubic Sharper, Bicubic Smoother, Preserve Details, Nearest
Neighbour, or other method was selected by this slice.

**Exact product decision required:** choose and accept the one Photoshop CC 2019 resampling method
to use for proportional shrink operations at 300 PPI, after quality validation on representative
production artwork. Record its exact Photoshop-native identifier and the behavior for a
resolution-only/no-pixel-change case in immutable accepted evidence before production code invokes
it.

### Aspect ratio — rule accepted, executable decision incomplete

The design and preset do establish the high-level rule: aspect ratio is locked, the operator enters
one dimension and PrintFlow calculates the other, resizing is proportional and shrink-only, and
non-proportional stretching is forbidden.

The current Domain/UI model nevertheless accepts and persists two independently supplied physical
dimensions. It does not bind them to the source document's pixel aspect ratio. A safe future seam
can refuse a mismatching pair after reading source pixels, but exact target width and height can be
simultaneously required only when the persisted pair was derived from that source ratio.

Before end-to-end production enablement, the authority must be deepened so one dimension is
authoritative and the other is derived from the source ratio using an accepted rounding rule, or
an equivalent exact ratio-compatibility rule must be documented. B1A must never stretch, crop, add
canvas, or guess which dimension wins.

## 4. Intended Photoshop-native preparation route

No route was implemented because the resampling gate failed first. The smallest acceptable future
route remains an Infrastructure-only, operation-specific seam taking
`PhotoshopOpenedDocument`, typed `PrintDimensions`, and a cancellation token. It must not expose
arbitrary JavaScript, command ids, scripts, or generic resize values to App or Workflow.

The evaluated native direction is attachment through the Running Object Table to the already
accepted Photoshop CC 2019 object, followed by a fixed Action Manager/Image Size operation whose
resampling identifier comes from newly accepted evidence. It must never activate Photoshop 2026,
the stale 2019 COM registration, or another Photoshop process.

## 5. Exact document identity boundary

Part A remains unchanged and is the required pre-operation boundary:

- accepted executable path, process id/start time and main-window ownership reverified;
- no blocking modal;
- title plus read-only Save As folder probe repeated immediately before mutation;
- observed absolute path exactly matches the managed Working file;
- active document remains the same target.

Filename-only, tab-index, and same-basename/different-folder selection remain prohibited. Because
the resampling contract failed before a document was opened, no B1A identity probe or mutation was
attempted.

## 6. Before/after dimensions and resolution

Not observed. No synthetic Working image was opened and no live resize occurred. Consequently
there is no claimed before/after pixel size, physical size, or PPI result.

A future PASS must read the expected document before and after, require exactly the Domain-owned
target pixel width and height, and require 300 PPI within a separately justified Photoshop numeric
tolerance. An API return value alone is not completion evidence.

## 7. Colour mode, bit depth and channel preservation

No Photoshop document was inspected or changed in this slice. No claim is made from fabricated
facts. The future B1A operation must capture and compare mode, bit depth, channel names/types and
W1 presence before and after, refuse any pre-existing W1, and require:

- colour mode unchanged (RGB before and after for the normal synthetic fixture);
- bit depth unchanged;
- no W1 before or after;
- no new alpha, spot, process or other unexpected channel.

CMYK conversion and W1 creation remain exclusively in B1B's accepted Action.

## 8. No-disk-output proof

No B1A workspace, synthetic file, output reservation, TIFF, AdapterOutput or Revision was created.
No Save or Save As confirmation was invoked. The existing production
`IPhotoshopOutputProcessor.GenerateAsync` remains fail-closed before Photoshop is touched.

Because no live Working file existed, there is no before/after Working SHA-256 to claim. The
canonical Action artifact was rehashed read-only at
`A04203EDEA623C0737D911601A3A005033789BD095130F02A5F8C04CBFCD83EE`.

## 9. Shared-process safety and cleanup

Photoshop was not activated or mutated for B1A. Existing operator documents were not enumerated,
activated, switched, saved, closed or changed. Photoshop was not closed or terminated, and no
global Photoshop preference or colour setting was changed.

`PhotoshopOpenedDocument.OtherDocumentsMayBeOpen` and Part A's exact-one-document cleanup policy
remain unchanged. No B1A document was opened, so no cleanup was needed and no directory remains
referenced because of this slice.

## 10. Targeted verification

The complete suite was not run.

```text
dotnet test tests\PrintFlow.Tests\PrintFlow.Tests.csproj --no-build \
  --filter "FullyQualifiedName~PrintDimensions"
```

Result: **19 passed, 0 failed, 0 skipped**. This covers the Domain conversion, fixed 300 DPI,
rounding results, value validation, workflow decision handling and related persistence paths whose
test names contain `PrintDimensions`.

```text
dotnet test tests\PrintFlow.Tests\PrintFlow.Tests.csproj --no-build \
  --filter "FullyQualifiedName~Photoshop|FullyQualifiedName~AutomationBoundaryTests|FullyQualifiedName~WorkstationPresetProviderTests"
```

Result: **156 passed, 0 failed, 0 skipped**. This covers Part A identity and shared-process safety,
Photoshop state/driver/foundation behavior, relevant architecture boundaries and preset selection.

No Photoshop-preparation tests were added because no preparation behavior may be implemented while
the required resampling authority is absent. Encoding a guessed method in a test would turn the
guess into code authority.

Final normal solution build used the repository-pinned user-local .NET SDK 10.0.400. Result:
**0 warnings, 0 errors**. The system-wide .NET installation contains only SDK 8.0.418 and cannot
resolve `global.json`; no repository configuration was changed. Dependency files are unchanged,
so vulnerability audit remains deferred to Epic 11400 Final QA.

## 11. Live smoke

Not run. The brief explicitly prohibits the live resize when resampling or aspect-ratio authority
is unresolved. No Photoshop Action was executed and no document was modified in memory.

## 12. Evidence and preset impact

`appsettings.json` still selects accepted immutable preset v1.9.0, whose manifest SHA-256 remains
`0DA89F8FE4067574FD7568F1FA8D1C0F2000469E3059FEE28BFDB0C867B5FB58`.
`Adapters.Mode` remains `Fake`.

Preset v1.9.0, its evidence files and the canonical `.atn` are unchanged. No new preset was
created: the missing resampling rule requires new quality evidence and product acceptance, not an
assumption recorded as a manifest value.

## 13. Remaining B1B scope

After B1A eventually passes, B1B may execute the exact accepted `W1_0px`, `W1_1px` or `W1_2px`
Action selected by the existing explicit Domain branch. That Action owns CMYK conversion and W1
creation. B1B must not resize, choose dimensions, save a TIFF, or create a Revision. TIFF save,
validation, AdapterOutput and Revision creation remain Part C.

## 14. Git state

Preflight began on `master` at `2f8ae7f`, ahead of `origin/master` by 13 commits, with only the
historical B1 NOT READY report untracked. That report was committed unchanged as standalone
checkpoint `005c97e` (`Report: Epic 11400 B1 W1 action discovery checkpoint`); no amend, rebase,
history rewrite or push occurred.

This B1A report is the only B1A repository change. No source, test, project, dependency, lock,
configuration or preset file changed. It is left uncommitted because the slice's ordinary
implementation/report commit policy permits a commit only after PASS. No synthetic files,
screenshots, workspaces, outputs or external evidence were added to Git.

11400-B1A NOT READY
