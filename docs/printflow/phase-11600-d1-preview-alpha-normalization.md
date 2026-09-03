# EPIC 11600 — Part D1: Production TIFF Preview Alpha-Normalization Remediation

Date: 2026-09-03 (Pacific/Auckland). Scope: the display-preview boundary only. The Production
TIFF, its inspector, the Photoshop Action, the preset and the accepted-artefact contract were not
touched, and Part D remains blocked.

## Defect reproduction

The Part D retry produced a Production TIFF that passes the authoritative contract in full —
945 × 945 px, 300 × 300 DPI, five 8-bit interleaved separated samples, CMYK, one non-empty
Photoshop W1 spot channel, no alpha, no pyramid, one accepted layer — and the review screen drew
nothing at all.

Re-measured on the live artefact before this change, through the real
`WicImagePreviewDecoder` → `ArtefactPreviewService` → `PreviewPayloadConverter` path:

```text
frame                 945 x 945, 300x300 dpi, WIC format = Default (40 bpp), no palette
preview payload       945 x 945 Bgra32
total pixels          893,025
alpha == 0            893,025
```

The artefact was never at fault. Nothing failed: the file resolved, WIC decoded it, the payload
encoded, `BitmapImage` constructed. Every pixel simply carried alpha 0, so WPF correctly drew an
invisible image.

## Exact WIC observation

`BitmapDecoder` reports the five-sample separated frame as `PixelFormats.Default` at 40 bpp — a
format PrintFlow cannot name. `WicPixelFormats.HasAlpha` therefore answers `null`, which is the
honest answer and which the decoder already reported truthfully as
`DecodedPreview.HasTransparency = false`.

The damage happened one step later. `FormatConvertedBitmap(frame, Bgra32, …)` converts the frame
for display and places the **fifth sample — the W1 spot channel, which is ink — into the BGRA
alpha byte**. The W1 channel of this artefact is full ink across all 893,025 pixels, so the
conversion produced alpha 0 everywhere while leaving the colour bytes intact.

That is not a WIC bug to work around with a channel-count rule. It is the ordinary consequence of
asking a converter to express a five-channel separated frame in a four-channel display format:
the fourth position has to hold something, and nothing in the frame says what it means.

## Minimal 2 × 2 reproduction

`ProductionTiffFixture` already builds a 2 × 2 accepted CMYK+W1 TIFF. Two options were added so it
can carry distinguishable colour and a W1 channel that is full ink in *every* pixel
(`CmykSamples`, `FifthSampleEverywhere`); the inspector-facing default zeroes a single sample,
which makes the channel non-empty but leaves three of four pixels opaque after conversion — not
the defect the live artefact showed.

With `CmykSamples: [0x10, 0x40, 0x80, 0x20]` and `FifthSampleEverywhere: 0`:

| | before the fix | after the fix |
| --- | --- | --- |
| `WicPixelFormats.SourceAlpha(frame)` | `null` | `null` |
| payload alpha bytes | `0, 0, 0, 0` | `255, 255, 255, 255` |
| payload B/G/R bytes | unchanged from WIC's conversion | unchanged from WIC's conversion |
| `HasTransparency` | `false` | `false` |

Verified as a real red/green transition: with the normalisation call temporarily forced to
"preserve", `A_CMYK_W1_TIFF_previews_opaque_with_its_colour_bytes_untouched` and
`A_CMYK_W1_TIFF_Revision_previews_visibly_through_the_preview_service` both fail on alpha; with
the fix in place all eight cases pass.

## Source-alpha decision rule

The rule is about provenance, not channel count:

```text
source alpha positively identified   -> preserve every decoded alpha value
otherwise                            -> set display alpha to 255
```

"Positively identified" is answered by the new `WicPixelFormats.SourceAlpha(BitmapSource)`, which
extends the existing three-valued `HasAlpha(PixelFormat)` rather than replacing it:

- a format that names alpha (`Bgra32`, `Pbgra32`, `Rgba64`, …) → `true`;
- a format that names none (`Bgr24`, `Cmyk32`, `Gray8`, …) → `false`;
- an indexed format → decided by its **palette**: any entry with `A < 255` → `true`, an entirely
  opaque palette → `false`. This turns an unknown into a positive fact from source information,
  and is what keeps a transparent GIF/indexed preview transparent;
- anything else, including the 40-bpp separated frame → `null`, and `null` is never read as
  transparency.

`HasAlpha` itself is unchanged, so `WicFileInspector` and `DeterministicAlphaTrimProcessor` keep
the exact semantics they had.

## Normalization location

`WicImagePreviewDecoder.ToDisplayBgra` — the narrowest layer that knows both whether source alpha
was positively established and the final display BGRA bytes. It already copied the converted
pixels into a fresh 96-dpi BGRA32 bitmap for the dpi normalisation, so the fix is a single pass
over that in-memory buffer:

```csharp
if (!sourceAlphaIsPositivelyIdentified)
{
    for (int alpha = AlphaByteOffset; alpha < pixels.Length; alpha += BytesPerBgra32Pixel)
    {
        pixels[alpha] = byte.MaxValue;
    }
}
```

No new image library, no WPF-side special case, no artefact-type or customer-specific branch. The
view layer (`PreviewPayloadConverter`) is untouched and still knows nothing about TIFF semantics.

## Real-alpha preservation

- **Transparent PNG.** A `Bgra32` PNG with a fully transparent, a partially transparent
  (`0x7F`) and an opaque pixel previews with alpha exactly `0x00, 0x7F, 0xFF`. The partial value
  is the load-bearing one: forcing opacity would flatten it just as it flattens the transparent
  pixel.
- **Indexed image with a transparent palette entry.** Previews with alpha `0x00, 0xFF`. Covered
  end-to-end as a GIF: WIC's own PNG encoder writes no `tRNS` chunk, so an indexed PNG round-trips
  as a fully opaque palette and cannot carry the fixture. WIC's converter honours palette alpha,
  which is why the palette branch is a real preservation rule and not decoration.
- **Opaque images.** Opaque RGB PNG and JPEG preview opaque at their own dimensions, with
  `HasTransparency = false` and no change to payload creation.
- No new supported file contract was invented for any of this.

## Colour-byte preservation

Asserted on both the synthetic fixture and the live artefact by recording WIC's own conversion
first and comparing it to the payload the operator sees. On the 945 × 945 customer TIFF:

```text
unnormalised alpha == 0   893,025
pixels colour changed          0
pixels alpha changed     893,025
```

Only the alpha byte moves. B, G and R are byte-for-byte what WIC produced.

## Customer TIFF read-only local proof

`Sessions/S_20260903T023453Z_43e8af85/Working/01a06557-1975-73c2-a47b-19d1174a42a4/…_80mm_CMYK_W.tif`
was used as read-only diagnostic input from its existing workspace location. It was not copied
into Git and no automated test depends on it.

```text
tiff length before/after   7,863,396 / 7,863,396
tiff sha256 before/after   8771725E958DA79DAB941557AC09D17B9A971D1FF262401B86F1C0BDA74AD78A (both)
last-write time            2026-09-03T03:37:16.4037731Z, unchanged
frame                      945 x 945, 300x300 dpi, Default 40 bpp
decoder payload            945 x 945 Bgra32, 1,522,207 bytes
service payload            945 x 945 Bgra32, identical
BitmapImage                945 x 945 Bgra32
HasTransparency            False
alpha == 0                 0
alpha == 255               893,025
non-white colour pixels    893,025
```

The file was opened for reading only, and the run was repeated with PrintFlow live against the
same workspace and database with identical results.

## Files changed

| File | Change |
| --- | --- |
| `src/PrintFlow.Infrastructure/Imaging/WicPixelFormats.cs` | new `SourceAlpha(BitmapSource)`; `HasAlpha` untouched |
| `src/PrintFlow.Infrastructure/Imaging/WicImagePreviewDecoder.cs` | reads three-valued source alpha; `ToDisplayBgra` normalises the alpha byte when it is not positively identified |
| `tests/PrintFlow.Tests/Fixtures/ProductionTiffFixture.cs` | `CmykSamples` / `FifthSampleEverywhere` options; existing defaults unchanged |
| `tests/PrintFlow.Tests/Fixtures/SyntheticImages.cs` | `IndexedWithTransparentEntry`, `DecodePayloadBgra` |
| `tests/PrintFlow.Tests/Integration/Files/PreviewAlphaNormalizationTests.cs` | new — the whole D1 regression set |
| `docs/printflow/phase-11600-d1-preview-alpha-normalization.md` | this report |

Not changed: `ProductionTiffInspector`, TIFF save behaviour, Photoshop output format, W1
generation, spot-channel structure, TIFF validation, the Photoshop Action, the preset/evidence,
approval semantics, `PreviewPayloadConverter`, and every UI surface.

## Tests

`PreviewAlphaNormalizationTests` — 8 cases, all passing:

- `WICs_own_conversion_of_a_CMYK_W1_frame_reports_every_pixel_transparent` — the standing repro;
- `A_CMYK_W1_TIFF_previews_opaque_with_its_colour_bytes_untouched`;
- `Previewing_a_CMYK_W1_TIFF_does_not_write_to_it`;
- `A_transparent_PNG_keeps_its_alpha_exactly`;
- `An_indexed_image_with_a_transparent_palette_entry_keeps_that_transparency`;
- `An_image_with_no_alpha_channel_previews_opaque_at_its_own_size` (PNG and JPEG);
- `A_CMYK_W1_TIFF_Revision_previews_visibly_through_the_preview_service` — real workspace, real
  SQLite repository, real `ArtefactPreviewService`, real WIC decoder, nothing doubled.

Every assertion inspects actual pixel data. None depends on UI appearance.

## Build

.NET SDK 10.0.400 (per-user install), `dotnet build PrintFlowStudio.sln`: **0 warnings, 0 errors**.

## Complete-suite result

Run once against final source: **10,146 passed / 0 failed / 0 skipped** (2 m 15 s). The previous
recorded baseline was 10,138; the difference is the eight new cases.

## Dependency / security status

No dependency state changed. `Directory.Packages.props`, `Directory.Build.props` and
`nuget.config` are untouched and no image library was added — the existing WIC path expresses the
fix. The dependency/security check was therefore not re-run.

## Production TIFF bytes were never modified

Confirmed three ways: the decoder opens every file `FileAccess.Read`/`FileShare.Read` and writes
nothing; `Previewing_a_CMYK_W1_TIFF_does_not_write_to_it` compares the fixture's full bytes before
and after a preview; and the live customer TIFF's length, SHA-256 and last-write timestamp are
identical before and after every run above. Normalization exists only in the in-memory preview
payload — never in the Revision hash, source snapshot, Working copy, exported files, Photoshop
input or approval hash.

## Preview metadata truth

Audited per §11. `DecodedPreview.HasTransparency` / `ImagePreview.HasTransparency` were already
derived from the source format rather than the BGRA payload, and now read
`SourceAlpha(frame) == true`, so the CMYK+W1 artefact reports `false` — agreeing with the
Production TIFF inspector's authoritative statement that this output has no alpha. Nothing in the
UI labels W1 as alpha or binds `HasTransparency`; the review screen's transparency checkerboard is
a static backdrop. No metadata correction was needed and the inspector was not changed.

## Live bounded preview proof

1. No PrintFlow process was running beforehand (0).
2. Final source rebuilt: 0 warnings, 0 errors. `PrintFlow.App.dll`
   `B4ED9D9E94E2C735544BBABF0970386713AD631DE060D04B1E8F576686A274E6`,
   `PrintFlow.Infrastructure.dll`
   `8565583DD175949FE032516647C7032C82CC88D76C4C3864C76E8C71E8A68B15`.
3. New PrintFlow process PID 5592 started from those binaries at 2026-09-03 16:12:24
   Pacific/Auckland, responding, window "PrintFlow Studio".
4. No customer Production adapter attempt was executed. No session was opened in the UI.
5. Read-only verification against the already-produced TIFF: preview service succeeded, preview
   945 × 945, payload not all alpha 0 (0 pixels at alpha 0, 893,025 at 255), colour retained,
   `BitmapImage` constructed at 945 × 945 Bgra32.
6. No approval or rejection was performed.

Censuses across the whole exercise: session directories 103 → 103, Comparison files 70 → 70,
Quarantine files 2 → 2.

The pixel-level proof was taken through the same production code path the shell uses
(`FileWorkspace` → `WicImagePreviewDecoder` → `SqliteSessionRepository` → `ArtefactPreviewService`
→ the converter's `BitmapImage` construction) rather than by driving the GUI, because desktop
coordinate automation is unavailable on this build and navigating the review screen would have
meant opening the historical customer session. That is the one part of §15 taken by equivalent
read-only means rather than by on-screen observation.

## Historical customer session was not changed

Read back after the fix, still exactly as Part D recorded it:

```text
session state   Active, step PhotoshopOutput
steps           Import=Approved, OriginalConfirmation=Approved,
                PrintDimensions=Approved, PhotoshopOutput=ReviewRequired
revisions       2
attempts        3
                01a0651e-8125-7b5a-be86-43601434c7ae Import           Succeeded retrySeq=0
                01a06530-f227-710e-ad3c-4677458a2b53 PhotoshopOutput  Failed    retrySeq=0
                01a06557-1975-73c2-a47b-19d1174a42a4 PhotoshopOutput  Succeeded retrySeq=1
reviews         1 (OriginalConfirmation, approved)
outputs         1, NotReviewed
```

The first failed Background-layer attempt and the successful `RetrySequence = 1` attempt are both
present. Nothing was approved, rejected, retried or deleted.

## Git state

Two new local commits, no rebase, no amend, no push. The first records the previously uncommitted
Part D evidence — the Background-layer adapter fix, its regression coverage and the blocked Part D
report — so that evidence is preserved in history rather than left loose in the working tree. The
second is this D1 change. Nothing earlier was rewritten.

## Verdict

D1's definition of done is met: the minimal CMYK+W1 TIFF reproduces the transparent-preview defect
before the fix, normalization is based on source-alpha semantics rather than channel count, the
non-alpha CMYK+W1 preview is now opaque and visible, real transparency survives, colour samples
are unchanged, the Production TIFF's bytes and hash are untouched, its validation is unchanged, the
shared preview tests and the complete suite pass, the build is clean, the historical customer
session is untouched, and a restarted PrintFlow built from this source renders the already-produced
TIFF correctly.

This does **not** pass 11600-D, which remains **BLOCKED — REAL PRODUCTION ORDER NOT VERIFIED**. The
successful TIFF still belongs to a retried session (`RetrySequence = 1`). Closing Part D requires a
restart from these binaries, a Production Readiness preflight, a completely new customer session
run from Import forward with exactly one successful Photoshop Production attempt, an operator
visual review of the now-correct preview, an explicit approval, and the final persistence checks.

**11600-D1 PASS WITH NOTES — PRODUCTION TIFF PREVIEW FIX READY FOR FRESH ORDER RERUN**
