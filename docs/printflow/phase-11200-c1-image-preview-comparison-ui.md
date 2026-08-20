# Epic 11200 Part C1 — Image Preview and Before/After Comparison UI

Read-only presentation slice. The operator can now see the file a review is about, beside the
file it came from, on a transparency checkerboard, with zoom and pan.

No crop handles, no manual crop, no trim-margin controls, no ReturnToStep. The Part B trim
algorithm is untouched.

---

## 1. Preview seam

Two new ports, both addressed by identity rather than by path:

```text
IArtefactPreviewService.GetPreviewAsync(SessionId, RevisionId, CancellationToken)
  → OperationResult<ImagePreview>

IImagePreviewDecoder.DecodeAsync(WorkspaceFileRef, CancellationToken)
  → OperationResult<DecodedPreview>
```

| Type | Project | Role |
| --- | --- | --- |
| `IArtefactPreviewService` / `ImagePreview` | `PrintFlow.Workflow.Services` | The UI's only route to image bytes. |
| `ArtefactPreviewService` | `PrintFlow.Workflow.Services` | Resolves the Revision inside its own session, then delegates. |
| `IImagePreviewDecoder` / `DecodedPreview` | `PrintFlow.Workflow.Ports` | The decode contract. |
| `WicImagePreviewDecoder` | `PrintFlow.Infrastructure.Imaging` | WIC decode → optional reduce → PNG re-encode. |
| `PreviewPayloadConverter` | `PrintFlow.App.Views` | Bytes → `BitmapImage` at bind time. |

`ImagePreview` carries `RevisionId`, payload pixel dimensions, **source** pixel dimensions,
`HasTransparency`, and the encoded bytes. Deliberately no hash and no path: pairing a preview
with a hash would invite a screen to treat "I displayed this" as "I verified this", and the
metadata a review is bound to already reaches the UI through `ArtefactView`.

## 2. Workspace and security boundary

The shape of the API is the guarantee. There is no `ReadAnyFile(string)` to reject an arbitrary
path with, because neither seam accepts a `string` at all — asserted in
`PreviewBoundaryTests.The_preview_seams_accept_no_string_parameter`.

| Attack | Outcome |
| --- | --- |
| Arbitrary external path | Not expressible. The service takes `SessionId` + `RevisionId`. |
| Escaped workspace path | Not expressible. The decoder takes `WorkspaceFileRef` and resolves it through `IWorkspace.ResolveAbsolute`, i.e. through `PathGuard`. |
| Session A asking for session B's Revision | `PreconditionNotMet`. |
| Unknown Revision id | `PreconditionNotMet`. |

Membership *is* the lookup rather than a check performed beside it: `ArtefactPreviewService`
searches the aggregate the repository returns for the named session, which holds that session's
Revisions and no others. "Belongs to someone else" and "does not exist" are therefore
indistinguishable from inside, which is the correct answer to give either way.

The seam writes nothing. `Previewing_writes_nothing_to_the_workspace` byte-compares every file
under the workspace root before and after six preview requests, so a cache directory, a
thumbnail beside the artefact, or a same-size rewrite in place would all fail it.

## 3. Image loading

```text
WorkspaceFileRef
  → IWorkspace.ResolveAbsolute (PathGuard)
  → FileStream, then BitmapDecoder.Create(PreservePixelFormat | IgnoreColorProfile, OnLoad)
  → HasTransparency := WicPixelFormats.HasAlpha(frame.Format) == true   (source format, not payload)
  → longest edge > 2048 ?  TransformedBitmap(ScaleTransform)  :  frame unchanged
  → FormatConvertedBitmap to Bgra32, copied into a fresh BitmapSource declared at 96 dpi
  → PngBitmapEncoder → byte[]
```

**Resolution: full up to 2048 px on the longest edge, reduced beyond it.** Reduction is
reported through `IsDownsampledForDisplay`, and the pixel figures shown to the operator stay the
*artefact's* (2600 px), never the payload's (2048 px). The underlying Revision is never
modified — nothing in this slice writes a file.

**Why 96 dpi.** The payload is explicitly a display representation. At 96 dpi one payload pixel
is one device-independent pixel, so "100%" reads as 100% rather than 32% for a 300-dpi artefact.
The artefact's real dpi is operator information and still reaches the screen through `FileFacts`,
unaltered.

**Memory.** No cache of any kind. Each call opens, decodes, encodes and releases; `OnLoad` means
the file handle is gone before the method returns, so a preview can never be the reason a later
step cannot rewrite the file it looked at. The 2048 bound caps the transient BGRA buffer at
16 MB. Panes are rebuilt wholesale on every `Show`, and cleared on Back to Home, so the bytes
become collectable immediately rather than when the screen is.

## 4. Checkerboard

A tiled `DrawingBrush` resource (`TransparencyCheckerboard`, 8 px squares on a 16 px tile) set as
the `Background` of the `Grid` behind each `Image`. Display only: the image is not flattened, its
bytes are not touched, and no checkerboard is written into any Revision. The smoke pass asserts
by reference that exactly two arranged panels carry that brush.

## 5. Before/after pairing

`SessionView` gained two members:

```text
ArtefactView? UpstreamArtefact   — resolved from CurrentArtefact.SourceRevisionId
FailureCode?  CurrentStepFailure — the current step's newest ended attempt, while it is Failed
```

`ArtefactView` gained `SourceRevisionId`, taken straight from `Revision.SourceRevisionId`. The
pairing is therefore the real derivation edge, never an inference from step order, and the view
model does not reconstruct any chain — it asks the preview seam for two ids it was handed.

`UpstreamArtefact` is populated only when the artefact on screen is the current step's **own**
result. When the screen is showing a step's *input*, that input is the only thing there is to
look at.

Verified for Enhancement, BackgroundRemoval and Trim against `Revision.SourceRevisionId` read
back out of the database — not against file names, which the two fake-adapter steps make
identical.

## 6. Zoom and pan

| Control | Behaviour |
| --- | --- |
| Zoom in / out | `×1.25` / `÷1.25` from the current scale, leaving fit mode. |
| Bounds | 10% – 800%, clamped. |
| Reset | Returns to fit-to-viewport at scale 1.0. |
| Opening state | Fit to viewport (§15), and re-fitted whenever a new artefact is shown. |

Pan is the pane's own `ScrollViewer`: scrollbars are `Disabled` while fitted (nothing is
off-screen) and `Auto` once zoomed. No custom canvas navigation.

**Synchronisation, as documented in §14:** zoom is shared — one `ZoomScale` drives both panes,
because a before and an after examined at different magnifications are not a comparison. Scroll
position is deliberately **not** shared: each pane keeps its own, so an operator can look at the
top-left of one and the bottom-right of the other.

Fit is treated as 100% for the purpose of the first zoom step, so the first press lands on a
stated, reproducible number rather than on 1.25× of however the window happened to be sized.

## 7. Trim review result

The flow the slice exists for, end to end on a real synthetic transparent PNG:

```text
12×10 PNG, alpha only in the 5×5 block (3,2)–(7,6)
  → Enhancement, BackgroundRemoval (approved)
  → deterministic Trim → ReviewRequired
  → Before: 12×10 upstream        After: 5×5 cropped Revision
```

Both halves decode; both payloads are BGRA with live alpha over the checkerboard. Asserted at
three levels: the read model (`ArtefactPreviewTests`), the view model
(`ImagePreviewControlTests`), and the pixel sizes of the bitmaps actually handed to the rendered
`Image` elements (`Smoke_G`).

**`ManualCropRequired`.** No Revision is produced, so the artefact on screen is the step's input
and there is structurally no "after" to fabricate. The screen shows the ordinary failure line
*plus* a localised notice: "Automatic trim cannot determine the crop area. Manual crop will be
available in the next workflow step." Because `CurrentStepFailure` is derived from the persisted
attempt row rather than held in view-model memory, the notice survives navigating away and
resuming. No crop control is offered.

## 8. Preview-failure behaviour

A preview that cannot be produced yields a pane carrying a sentence instead of a picture. It
does **not** fail the session, invalidate the Revision, set `Notice`, or map to `AttemptFailed`.

| Cause | Shown |
| --- | --- |
| `OutputUnreadable` (no codec, unpresentable format) | "This file cannot be displayed here. The file itself is unaffected and the review below still applies to it." |
| Anything else (missing file, wrong session) | "Preview unavailable" |

`A_preview_that_cannot_be_produced_leaves_the_review_usable` deletes the file behind the screen's
back and asserts the metadata still reads, `Notice` is still null, the session is still writable,
and Approve is still on offer bound to the hash it was always bound to.

**Hash safety.** `A_previewed_Revision_whose_bytes_change_still_refuses_approval` previews a
Revision, rewrites its bytes, then approves: the integrity re-check returns
`RevisionIntegrityMismatch` and the step does not become Approved. The preview is visual
evidence; the SHA-256 remains the authority.

## 9. Tests and manual smoke

**+43 tests, 5636 → 5679.**

| File | Covers |
| --- | --- |
| `Integration/Files/ArtefactPreviewTests.cs` (13) | §22 seam, §23 pairing, §24 trim read model, no-writes, downsampling. |
| `Integration/Ui/ImagePreviewControlTests.cs` (14) | §26 zoom state, §7/§11 panes, §17 manual crop, §18 hash safety, §19 navigation lifetime. |
| `Architecture/PreviewBoundaryTests.cs` (6) | §4/§29 seam shape: no string parameter, no mutating type, converter in the view layer. |
| `Integration/Ui/ViewRenderingTests.cs` (+5) | §25 single, before/after (fitted **and** zoomed), transparent trim, manual crop, unavailable. |
| `Integration/Ui/SessionSmokeTests.cs` (+4) | §27 Smoke E–H. |
| `Integration/Persistence/DbInvariantTests.cs` (+1) | §29 no BLOB column anywhere. |

**§27 honestly.** Interactive WPF is not available here — there is no desktop to click on — so
Smoke E–H do what §27 names as the alternative: the real composed graph from
`ApplicationStartup`, driven through the real view models, rendered for real at 1000×700, and
then *inspected*. Each manual checklist line became a machine check:

| Checklist line | How it is answered |
| --- | --- |
| Checkerboard visible | Exactly two arranged `Grid`s carry the checkerboard brush, by reference. |
| Before/After labels correct | Both headings present, Before's index before After's. |
| Trim shows a smaller canvas | Rendered bitmap sizes are `(12,10)` then `(5,5)`. |
| Zoom works | The `ScaleTransform` actually applied to the rendered `Image` is 1.0 fitted and > 1.0 after two Zoom In presses — i.e. the two `RelativeSource` bindings connect. |
| No obvious clipping at 1000×700 | The screen's `DesiredSize` fits the viewport it was given. |
| zh-CN fits reasonably | Same, with the Chinese satellite loaded and the Chinese headings asserted. |

**What is not covered, stated plainly:** nobody has looked at the result. Colour, spacing,
visual balance and legibility remain a human judgement.

A shared `Fixtures/WpfRendering.cs` replaced `ViewRenderingTests`' private STA/trace machinery,
so both suites use one implementation. Its results carry plain values, never live WPF elements —
inspection runs on the render thread because every element belongs to the STA thread that built
it.

## 10. Defects found

None in Part B. The trim algorithm, `TrimBounds`, `TrimMargin`, `ManualCropRequired`, the WIC
crop, the SessionService integration, `ReviewDecision` and exact-hash approval are all unchanged.

**One defect found and fixed in this slice's own code: stale previews from an overtaken load.**
Decoding is slow enough that a second command can land while the first load is still in flight;
the first version appended its panes unconditionally, so the older Revision's image could appear
beside the newer Revision's hash — precisely the staleness a review surface must never show.
Fixed with a generation token: `ClearPreviews` increments it, and a load publishes only if the
generation it started under is still current.

The first test written for this passed against the broken code, because the race did not
reproduce on its own. It was replaced with `GatedPreviewService`, which holds the first decode
open until the test releases it, and the replacement was verified to fail against the unguarded
version before being kept.

Two further things surfaced while building, both design choices rather than defects:

1. **Preview dpi.** A 300-dpi artefact rendered at `Stretch="None"` would appear at 32% when the
   zoom read-out said 100%. Fixed by normalising the *payload* to 96 dpi. The artefact is not
   touched; only the display representation is.
2. **`System.IO` in view models.** Building a `BitmapImage` needs a `MemoryStream`, which
   `BannedApiEnforcementTests` forbids under `ViewModels\`. Resolved by putting the one stream in
   `PreviewPayloadConverter` in the view layer, where it belongs — the view model exposes
   `ReadOnlyMemory<byte>` and never opens anything.

## 11. Deferred to C2

Unchanged from the C1 brief: crop handles, crop rectangle, manual crop, the
`OperationKind.ManualImport` UI flow, trim-margin controls, ReturnToStep, a fake-scenario
selector, image editing, annotation, colour tools, AI segmentation, real Meitu, real Photoshop.

Also deliberately not attempted here: a slider-style comparison, synchronised panning, TIFF
colour validation, and any golden-screenshot system.

## 12. Git state

Branch `master`, ahead of `origin/master`. Two commits, matching the established pattern —
implementation, then this report. No push: pushing was not authorised. No history rewritten, no
force push.

Changed (14): `ServiceRegistration.cs`, `Strings.cs`, `Strings.resx`, `Strings.zh-CN.resx`,
`SessionViewModel.cs`, `SessionScreenView.xaml`, `SessionService.cs`, `SessionView.cs`,
`HomeScreenHarness.cs`, `SessionServiceHarness.cs`, `SyntheticImages.cs`, `DbInvariantTests.cs`,
`SessionSmokeTests.cs`, `ViewRenderingTests.cs`.

Added (10): `IImagePreviewDecoder.cs`, `IArtefactPreviewService.cs`, `ArtefactPreviewService.cs`,
`WicImagePreviewDecoder.cs`, `ArtefactPreviewPane.cs`, `PreviewPayloadConverter.cs`,
`PreviewBoundaryTests.cs`, `WpfRendering.cs`, `ArtefactPreviewTests.cs`,
`ImagePreviewControlTests.cs`.

No preview cache, no committed image fixture, no screenshot, no runtime database, no generated
trim output. Every test image is generated at test time by `SyntheticImages`. No package was
added or changed, so the lock files are untouched.

## 13. Gates

```text
dotnet restore --locked-mode          OK
dotnet build                          0 warnings, 0 errors
dotnet test                           5679 passed, 0 failed, 0 skipped
dotnet list package --vulnerable      no vulnerable packages (all 5 projects)
```
