# PrintFlow Studio — Epic 11400 Part C1 Photoshop TIFF Save Validation

## 1. Result and scope

Part C1 now has a closed Infrastructure-only operation that consumes the exact factual B1B result
and an already-rendered managed Working reference, invokes one fixed Photoshop CC 2019 TIFF Save
As Copy, settles the returned file, and independently validates the on-disk production structure.
The result is a `PhotoshopValidatedTiffCandidate`; it is not workflow output success.

This slice creates no `AdapterOutput`, `PrintOutput`, `Revision`, `ReviewRequired`, or
`AttemptSucceeded`, performs no Approved/Rejected promotion, and leaves `Adapters.Mode` as `Fake`.
C2 retains every workflow-integration and final-review decision.

## 2. Accepted TIFF baseline

The read-only reference remains:

`D:\PrintFlowStudio\Baseline\workstation-v1\actions\authoring\FIX-CUSTOMER-DESIGN-001_W1-1PX.tif`

SHA-256: `D1E69C4108D4C1D6119DB11DE036F56555CDE4A064F23AF541E24E1DAC5EA412`

Independent inspection established 116,992,344 bytes, little-endian classic TIFF, 3307×4474,
300×300 DPI, five 8-bit interleaved samples, PhotometricInterpretation 5 (separated), image
Compression 1 (none), one ExtraSamples value 0, one IFD, no SubIFD/pyramid, and Photoshop image
resources naming W1 as display-info kind 2. The uncompressed fifth sample contained 8,228,624
values other than 255. Photoshop ImageSourceData contained one layer whose channel compression
fields were all 1 (RLE). The baseline was never modified.

Adobe distinguishes TIFF image compression from layer-pixel compression and documents the pixel
order, byte-order, image-pyramid, transparency, and layer options in its
[TIFF save guidance](https://helpx.adobe.com/uk/photoshop/using/saving-files-graphics-formats.html).
The Photoshop resource/layer parser follows the structures described by Adobe's
[Photoshop File Formats Specification](https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/).

## 3. Preserved B1B pre-save authority

The save seam accepts only a `PhotoshopW1PreparedDocument` paired with its original
`PhotoshopOpenedDocument`. Before the one save call it requires:

- the accepted executable hash/process/start time and accepted main window;
- no blocking modal and the exact active managed backing path;
- unchanged final pixel geometry and 300 PPI;
- `DocumentMode.CMYK`, `BitsPerChannelType.EIGHT`, four component channels, and exactly one
  non-empty `W1` `ChannelType.SPOTCOLOR`;
- proof that the canonical W1 Action already occurred exactly once; and
- a backing Working SHA-256 equal to the B1B result.

The save operation performs no resize, colour conversion, W1 action, or source save.

## 4. Destination and no-overwrite authority

The caller supplies a `WorkspaceFileRef`; `IWorkspace.ResolveAbsolute` remains the only path
authority. The saver requires `WorkspaceArea.Working`, a `.tif` name, and the same attempt-specific
directory as the B1B backing file. Source, Approved, Rejected, Logs, arbitrary absolute paths, and
other directories are refused before the native call.

The exact destination must not exist. It is never deleted, replaced, collision-renamed, or retried.
A partial or invalid TIFF is retained as a Working artefact and produces no candidate.

## 5. Production filename and SizeMm audit

No Photoshop-specific renderer was added. `SessionService` already supplies
`dimensions.WidthMm` to `OutputFileNaming.BuildProposedFileName`, which delegates to the existing
`NamingPatternRenderer`. `ForSizeMm` rounds that target width to a whole millimetre using
`MidpointRounding.AwayFromZero`. This is the existing unambiguous Workflow authority for
PresetFit and custom sizing; Infrastructure consumes only the finished filename.

The live requested width was 50.8 mm and the existing renderer produced `51mm` in:

`PF_C1_W1_1PX_20260831160634F574D8AC_51mm_CMYK_W.tif`

Focused naming tests pin both the rounding rule and the Workflow call site.

## 6. Exact CC 2019 save route and as-copy proof

The fixed native bridge attaches only to `Photoshop.Application.130`, verifies the automation
object's directory against the accepted executable, and executes one operation-specific program:

`doc.saveAs(output, options, true, Extension.LOWERCASE)`

There is one source-level `doc.saveAs` call and no retry or fallback. No mouse, coordinate, dialog,
remembered default, generic format, arbitrary script, or caller-selected save option crosses the
seam.

The successful live run observed the identical absolute source path immediately before and after
Save As Copy. The generated TIFF appeared at the exact separate Working destination, so CC 2019's
`asCopy=true` route is accepted for this contract.

## 7. Fixed option mapping and independent representation

| Accepted setting | CC 2019 DOM value | Independent on-disk proof |
| --- | --- | --- |
| Image compression None | `imageCompression = TIFFEncoding.NONE` | TIFF tag 259 = 1 |
| Interleaved | `interleaveChannels = true` | TIFF tag 284 = 1 |
| IBM PC/little-endian | `byteOrder = ByteOrder.IBM` | `II` TIFF header |
| Layer compression RLE | `layerCompression = LayerCompression.RLE` | every Photoshop Layr channel compression field = 1 |
| Image pyramid Off | `saveImagePyramid = false` | one IFD and no SubIFD tag 330 |
| Store transparency Off | `transparency = false` | no alpha/transparency ExtraSamples value; W1 separately proven spot |
| Preserve W1 | `spotColors = true` | Photoshop resources 1006/1045 name W1; resource 1077 kind = 2 |
| Do not save alpha | `alphaChannels = false` | exactly five samples; sole extra sample is the named spot ink |
| Preserve baseline layer payload | `layers = true` | tag 37724 contains one Layr record |
| Embed accepted colour profile | `embedColorProfile = true` | option read-back; not used as proof of CMYK sample structure |

`annotations=false` is also fixed. The program reads every controllable value back before saving
and refuses if CC 2019 does not expose and retain the exact values. Factual TIFF tags/resources,
not the option object alone, authorize the candidate.

One discovery TIFF used a high-frequency synthetic pixel pattern. Although the RLE option object
read back correctly, its Photoshop layer channel records were raw (`0`), not the accepted RLE
representation (`1`). The inspector rejected and retained that Working TIFF; it was not retried or
deleted and produced no candidate. The accepted live fixture used compressible synthetic pixels.
The production validator remains strict, so any future content that CC 2019 stores without RLE
will fail closed rather than silently weaken the baseline.

## 8. File settling

After the synchronous save returns, the saver polls for at most 60 seconds at 500 ms intervals.
Every observation records existence, length, bytes read, complete readability, and elapsed time.
Acceptance requires three consecutive equal non-zero lengths and a complete read of the final
length. Cancellation after the save begins does not interrupt settling/inspection; cancellation
remains authoritative and returns failure even if the retained bytes later validate.

The accepted live TIFF settled in three observations over 2.0379751 seconds.

## 9. Inspector implementation decision

No imaging dependency was added. `ProductionTiffInspector` is a small bounded parser dedicated to
classic TIFF plus the Photoshop resources required by this production contract. It validates
offsets/counts, limits IFD traversal, rejects unsupported or malformed structures, and reads only
the exact tag/resource/layer facts C1 needs. This avoids broadening PNG/JPEG inspection and avoids
a dependency/lock-file change.

The parser accepts the immutable baseline and a deterministic generated fixture. Negative tests
cover byte order, compression, sample count/bits, planar order, alpha, DPI, W1 name/type/content,
pyramid, Photoshop layer compression, and malformed bytes.

## 10. W1 and channel facts

The inspector does not trust the in-memory B1B fact as on-disk proof. It requires:

- five and only five 8-bit samples;
- separated photometric model 5, four process samples plus one non-alpha extra sample;
- matching W1 names in Photoshop resources 1006 and 1045;
- DisplayInfo resource 1077 kind 2 for the spot channel; and
- at least one fifth-sample value other than 255.

Because image compression is None and pixel order is interleaved, the fifth-sample scan is direct
and deterministic. It proves that the W1 plane is not wholly white; it does not interpret the
artwork's visual or printing semantics. A Photoshop re-open was unnecessary and was not used as a
substitute for disk inspection.

## 11. Successful live generated TIFF

Controlled external workspace:

`D:\PrintFlowStudio\QA\Epic11400C1\20260831-160634-F574D8AC`

Relative managed output:

`Sessions/S_C1/Working/A_20260831-160634-F574D8AC/PF_C1_W1_1PX_20260831160634F574D8AC_51mm_CMYK_W.tif`

| Fact | Observed value |
| --- | --- |
| Bytes | 2,452,724 |
| SHA-256 | `C6A6B6FD3844DC3297190F2BD3D33C7E501F899C430D1B7E7A6C27FD403940D0` |
| Pixels | 600×400 |
| Resolution | 300×300 DPI |
| Byte order | IBM PC / little-endian |
| Image compression | 1 / None |
| Photometric model | 5 / separated |
| Samples and bits | 5 / 8,8,8,8,8 |
| Pixel order | PlanarConfiguration 1 / interleaved |
| W1 | exact name, DisplayInfo kind 2, 240,000 non-white fifth samples |
| Alpha/transparency | false |
| Layers | one; every layer channel RLE |
| Pyramid | false; one IFD |

The B1A.3 → B1B W1_1px → Save As Copy → settle → inspect chain passed once on this fresh path.
The TIFF and synthetic source remain outside Git. Photoshop was not closed and the synthetic
document was not automatically discarded.

## 12. Backing and directory integrity

The successful source Working SHA-256 was
`9B627484613A5869872F35E1EB1B8946BEC5067C3D75D5F914E733417FD52FD3` before preparation/W1/save
and the same afterward. The final attempt directory contained exactly the original PNG plus the
one expected TIFF. There was no PSD, PSB, second TIFF, backup, sidecar, or other output.

## 13. Failure, cancellation, target-loss, and modal behavior

Pre-save cancellation, invalid B1B facts, changed backing bytes, non-Working/wrong-directory
destinations, an existing destination, target loss, executable/window mismatch, or a blocking
modal produces structured failure with zero save calls. Once the synchronous save begins there is
no retry. Exceptions and incomplete native observations are ambiguous retained-artefact failures.
Unreadable, unstable, malformed, geometrically wrong, non-RLE, extra-output, backing-mutation, or
post-save target-loss results remain Working-only and create no candidate.

The first live discovery output demonstrated the non-RLE rejection. A later environment attempt
opened a new source while Photoshop retained an active crop overlay; UI identity verification
failed before preparation/save and created no TIFF. These fail-closed observations are retained
outside Git and are not preset integrity artefacts.

## 14. Automated verification

No full-suite run was performed. Final focused commands selected TIFF save/parser/naming,
Photoshop B1B/Part A/preparation/UI regressions, production gate, Photoshop/automation/dependency
architecture, workstation preset evidence/provider, naming authority, `WorkspaceTests`, and
`ValueObjectTests`.

| Gate | Result |
| --- | --- |
| Main focused group | 280 passed, 0 failed, 0 skipped |
| Workspace/path/value-object group | 61 passed, 0 failed, 0 skipped |
| Successful live W1_1px TIFF smoke | 1 passed |
| Normal solution build | 0 warnings, 0 errors |

The final focused non-live total is 341 passed. The complete 9,000+ suite remains deferred to Epic
11400 Final QA as requested.

## 15. Dependency result

No NuGet package, package reference, or lock file changed. Locked restore and vulnerability audit
were therefore not required. The dependency graph is unchanged.

## 16. Immutable evidence and preset v1.14.0

New read-only external evidence:

`D:\PrintFlowStudio\Baseline\workstation-v1\apps\photoshop-2019\production-tiff-save-runtime.json`

SHA-256: `D0E2562C88F4CE0CD018D83D2BA4F1EF5813E95CFAACDF14F3C7CFF5B500FD90`

New read-only preset:

`D:\PrintFlowStudio\Baseline\workstation-v1\preset\printflow-workstation-v1.14.0.json`

SHA-256: `F74792276C0B264C9F064D1C82CB26806F7B836A543E0AF5FC8B4E7FB1738C62`

All 25 inherited integrity entries and the new entry rehashed exactly: 26/26, zero mismatch. The
accepted v1.13.0 manifest remains read-only and byte-exact at
`67525D6E9BF6A60438BC530B9E41FDFE65919473061A5D28772377211923A7CD`.
`appsettings.json` selects v1.14.0 by exact digest and `Adapters.Mode` remains `Fake`.

## 17. Remaining C2 scope

C2 still owns conversion of a validated candidate into `AdapterOutput`/`PrintOutput`, workflow
success, Revision creation, ReviewRequired/final review, Approved promotion, rejection/recycle
behavior, persistence, and production-mode enablement. C1 neither anticipates nor performs those
decisions.

## 18. Git and retention state

The repository contains only source, focused tests/smoke, the configured v1.14 pointer, and this
report. Generated TIFFs, synthetic sources, runtime QA workspaces, screenshots, transcripts, and
external evidence/preset files are not committed. No amend, rebase, history rewrite, or push was
performed. Normal local commit is permitted only after the final focused verification remains
green.

11400-C1 PASS WITH NOTES — READY FOR TIFF WORKFLOW OUTPUT INTEGRATION
