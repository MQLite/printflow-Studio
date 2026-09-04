# SCRUM-11100 — Single-page PDF input preparation

Date: 2026-09-04. Started on `master`, clean at `24ff6b1`. Accepted configuration: `Adapters.Mode = Production`, `printflow-workstation-v1 1.16.0`. The accepted configuration, preset and Photoshop implementation are unchanged.

## Original Jira authority

Read before Product source changes from `C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv`, selecting the exact title **Implement Single-Page PDF Input Preparation**. This export uses internal Work Item ID **11407**, Parent **11400**, Priority High, five story points. Internal export IDs and historical repository phase numbers are not Jira keys; this work is **SCRUM-11100**.

Original acceptance text:

> Support single-page PDF by confirming the page and target physical dimensions, then rasterising at production DPI for the Photoshop output flow. Reject multi-page PDFs explicitly and never silently select the first page. Preserve the original PDF and keep all rasterised working files within the managed Session workspace.

The supplied implementation contract establishes the detailed sequence: full-page preparation at 300 PPI, operator review of the prepared page and its physical dimensions, then the existing print-sizing decision. Preparation never applies a requested production size, trim, white ink or TIFF action.

## Previous gap

PDF import could record an opaque Source without reliable pixel dimensions. There was no structural page-count authority, supported raster preparation, persisted PDF inspection, or explicit multi-page preparation refusal. The supported picker did not advertise PDF. A generic image decoder or a Photoshop default import page could not establish the required safety invariant.

## Authority audit and dependency decision

The existing Windows installation supplies **Windows.Data.Pdf**, which successfully loaded and rendered synthetic fixtures in a standalone probe before integration. The same API exposes document page count, password protection, page geometry, rotation and rasterisation. The existing accepted Photoshop integration supplies no structural PDF page-count authority. Photoshop need not participate in PDF preparation.

`WindowsPdfDocumentAuthority` holds one `PdfDocument` on one read-only stream throughout inspection and rendering. The stream denies other writers and deletion. `PageCount` is read before `GetPage(0)`, `RenderToStreamAsync`, or output creation. Only an accepted count of exactly one reaches page selection. There is no PDF byte scan, regular expression, partial parser, second PDF renderer, default import dialog or externally supplied script.

Microsoft documents the structural [PdfDocument API](https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf.pdfdocument?view=winrt-26100), the [page size calculation](https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf.pdfpage.size?view=winrt-28000), and [render options](https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf.pdfpagerenderoptions?view=winrt-28000). The actual workstation probe additionally established 600×300 output for a 144×72-point page, 300×600 for its 90° rotation, and 450×225 for a smaller CropBox.

No third-party PDF parser or renderer package was introduced. Infrastructure, App and Tests now target `net10.0-windows10.0.19041.0`, with **Microsoft.Windows.SDK.NET.Ref 10.0.19041.57** explicitly pinned through `WindowsSdkPackageVersion`. Domain and Workflow remain platform-neutral. This is Microsoft's [documented desktop WinRT integration mechanism](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps), not an additional PDF engine. The existing NuGet package graph and versions are unchanged; three lockfiles acquire the explicit Windows framework key.

The targeting pack supplies Microsoft.Windows.SDK.NET plus WinRT.Runtime **2.2.0.48161** and its build-time projection generator. Its nuspec declares no package dependencies. The Microsoft author signature and NuGet repository signature passed `dotnet nuget verify --all`. The SDK uses Microsoft's [Windows SDK licence](https://download.microsoft.com/download/0/F/F/0FF2B061-47DD-4F55-89B6-FD1D8C44F14D/sdk_license.rtf), including conditional object-code redistribution rights. This is suitable for the existing Windows desktop application under those terms; no new copyleft PDF dependency is introduced. No installer was published in this task.

`dotnet list PrintFlowStudio.sln package --vulnerable --include-transitive --format json` reported no vulnerable packages. Because framework targeting packs are not listed as ordinary PackageReferences, the NuGet vulnerability feeds were also checked explicitly for Microsoft.Windows.SDK.NET.Ref, Microsoft.Windows.CsWinRT and WinRT.Runtime: no published entries were returned. These are dependency-advisory checks, not a claim that the installed operating system has undergone a new security certification.

## Supported contract and refusals

| Source observation | Preparation result |
|---|---|
| Windows-readable, unencrypted, exactly one page, supported geometry | Full visible-page RGB/8 PNG at 300 PPI, followed by required review |
| More than one page | Non-retryable `PdfMultiplePages`; exact count persisted; zero page selections, zero renders, zero prepared Revisions |
| Zero pages reported by authority | `PdfUnreadable` before page selection |
| Zero-page invalid tree rejected during native load | `PdfUnreadable`; count remains unknown/null rather than fabricated as zero |
| Password required or native `IsPasswordProtected` true | Non-retryable `PdfEncrypted`; no prompt, password storage, supplied/default password attempt or bypass |
| Corrupt/unreadable document | Non-retryable `PdfUnreadable`; Source remains; failed preparation attempt; no downstream work |
| Rendering, settling or independent validation failure | `PdfPreparationFailed`; no prepared Revision; retryable operational failures use a new attempt |
| Unsupported geometry/resource size | Refuse without reducing DPI or silently resizing |

The native encrypted fixture fails load with HRESULT `8007052B`; the corrupt and zero-page fixtures fail with native PDF errors. Inspection preserves `IsReadable`, nullable encryption status and nullable page count. Unknown facts are not converted to false or zero. The platform is the PDF readability authority; PrintFlow does not claim a separate strict ISO-conformance or repair-warning detector.

Output is bounded to 32,768 pixels per edge and 80 million pixels total. Documents beyond this limit fail preparation; there is no adaptive resolution. Preparation uses Windows' visual RGB representation of PDF content, not preservation of PDF CMYK/spot separations. It does not invoke a Photoshop colour conversion or production Action.

## Geometry, rotation, DPI and transparency

The authoritative visible page is `PdfPage.Size`, which Windows derives from MediaBox, CropBox and rotation. Both original boxes, their offsets and dimensions, the resulting visible width/height and rotation are persisted. Geometry units are **1/96 inch**. Windows reports rotations as a closed quarter-turn enum, recorded as 0, 90, 180 or 270 degrees. PrintFlow does not crop to artwork bounds or independently reinterpret boxes.

For each already-rotated visible edge:

```text
requested pixels = round(visible edge in DIPs × 300 / 96, midpoint away from zero)
```

Both resulting dimensions are explicitly supplied to Windows. The renderer's returned dimensions must agree. The fractional-size fixture locks the rounding rule: 144.12 points produces 601 pixels. A PNG pixel transfer writes physical-resolution metadata at 300 PPI without another PDF interpretation, colour management, crop or resize. Independent readback allows 0.02 PPI solely for PNG's integer pixels-per-metre metadata quantisation; pixel dimensions must match exactly.

Rendering explicitly sets a transparent background, PNG encoding and high-contrast override, so remembered display/import settings cannot determine production pixels. The native raster's actual alpha values are observed; WIC independently decodes every output row and compares visual transparency. Tests include transparent and opaque page content. Alpha-channel presence and actual transparent pixels remain distinct facts. No corner-colour inference or white background is used.

## Workflow, UI and provenance

The existing Original Confirmation step becomes producing/review-required for PDF, using the same lifecycle established for PSD: Waiting → Processing → ReviewRequired → Approved. `IPdfPreparationProcessor` is the narrow Workflow port; Windows technology stays in Infrastructure. Home only imports and snapshots. ViewModels display data and issue existing commands; they never parse or render PDF.

`ConfirmOriginal` cannot approve an opaque PDF. Preparation is explicit, labelled **Prepare PDF for review**. The review notice identifies the unchanged PDF Source, prepared PNG, page **1**, 300 PPI, pixel dimensions and physical dimensions. It is confined to Original Confirmation, so it cannot mislabel a later trimmed raster as the original full-page representation. English and Chinese labels/refusals are present and localisation parity passes. The picker advertises `*.pdf`; Home-to-review tests prove that this advertised capability reaches the real preparation path. Retry is hidden and refused for unchanged multi-page, encrypted and unreadable inputs.

The PDF Source never enters the WIC preview decoder. Only the validated managed raster is previewed. Before/after comparison does not pretend the PDF and PNG are equivalent decodable image files. Approval binds to the prepared Revision's exact hash. Existing Trim and Print Dimensions consume that approved raster, including the persisted print plan's source Revision ID.

```text
InputSnapshot.RootRevisionId
  → Import Revision: exact immutable PDF, original SHA-256 and PDF format
  → PreparePdf attempt + typed PdfInspection
  → PreparePdf Revision: attempt-owned PNG, own ID/hash/dimensions/colour/alpha
       SourceRevisionId = PDF Import Revision ID
       attempt.OutputRevisionId = prepared PNG Revision ID
```

Only a new sibling PNG inside the managed Working attempt directory is permitted. The source stream is read-only, a post-render hash is compared, and live proof also hashes the original synthetic customer-input analogue and immutable Source before/after. Output must settle across independent observations, reopen fully as RGB/8 PNG, match the geometry/DPI/alpha contract, and match the adapter's validated hash again at the workflow boundary. An adapter success alone cannot create a Revision. A same-size PNG changed after adapter validation is explicitly tested and refused.

Migration **0011** transactionally widens Revision.Operation to `PREPARE_PDF`, retaining historical rows, foreign keys, indexes and immutable-identity trigger, and adds immutable typed `PdfInspection` rows linked to attempts. Essential provenance is not confined to failure JSON. Failed source refusals retain their inspection alongside the failure. Historical sessions receive no backfilled PDF facts. Existing legacy opaque sessions cannot bypass preparation through later sizing/producing commands.

## Restart, retry, cancellation and failure

Rehydration restores capability from the root format and reads attempts, inspection and Revisions from SQLite. It neither selects nor renders a page. Integration tests restore identical IDs, hashes and inspection through a fresh service, then approve and enter existing sizing/Trim. The separate live test-host process described below proves persistence-only resume across processes.

Retry retains the failed attempt and any partial file; the next Start creates a new AttemptId and directory, `RetrySequence = 1`, and `RetryOfAttemptId` pointing to the failure. Only the successful second attempt owns the raster Revision. Tests cover renderer exceptions, missing output, partial PNG, wrong dimensions/format, wrong alpha evidence, unsettled output and changed output after validation. There is no hidden retry.

Stop, Take Over and cancellation-token tests stop before rendering, retain observed count, create no Revision and release the automation lock. The existing uncancelled closing transaction handles cancellation safely. This processor uses the Production environment gate and shared attempt/lock lifecycle but controls no external application. Photoshop executable/document/dialog/target-loss rules are unchanged and are not bypassed; no Photoshop document is opened for PDF preparation.

## Automated verification

Committed fixtures are synthetic and small. The conventional documents were generated with the bundled QA-only **pypdf 6.10.0**; the regeneration script is retained. A compressed fixture explicitly contains a compressed catalog, nested page tree and page dictionaries in an object stream, plus a cross-reference stream and a misleading cleartext page token inside content. An incremental-update fixture changes a former single-page file to two pages. Windows itself reads these fixtures in the tests; no mocked page count is substituted for the structural refusal proof.

- Targeted PDF, architecture, migration, localisation and failure-vocabulary gate: **552 passed / 0 failed / 0 skipped**.
- Clean solution build: **0 warnings / 0 errors**, repository-pinned .NET SDK **10.0.400**.
- Final complete suite: **10,284 passed / 0 failed / 0 skipped**, 2m52s, using `DOTNET_CLI_UI_LANGUAGE=en-US`. The final source also passed a clean rebuild before the live smoke.
- The first complete run also passed 10,284 tests. Final review then identified and fixed the scope of the preparation notice, added the downstream-notice assertion, and reran the suite against final source. The earlier evidence is retained as `scrum-11100-full-suite-before-notice-scope.trx`.
- Required side-effect proof: ordinary, compressed/nested and incremental two-page PDFs persist PageCount 2, with **zero page selections, zero renders, zero PNGs, and Source-only Revision history**.
- Source byte preservation, review/approval, all three workflows, downstream sizing/Trim, fresh-service resume, retry isolation, cancellation, production gating and en-US/zh-CN UI wording pass.

Local code/dependency evidence: `D:\PrintFlowStudio\Evidence\SCRUM-11100-api-probe`. Test results: `tests/PrintFlow.Tests/TestResults/scrum-11100-targeted-final.trx` and `scrum-11100-full-suite-final.trx`. Workstation smoke tests are opt-in and inert during the ordinary full suite; their actual enabled runs are reported separately below.

## Controlled accepted-workstation smoke

The real registered Production service ran at approximately **14:29 NZST**, with an isolated QA database and the committed configuration. `IEnvironmentDiagnostics` reported verified readiness; the real `VerifiedEnvironmentGate` allowed Production. The registered `IPdfPreparationProcessor` was the real Windows implementation. Native authority counters were read on that same registered instance, not substituted by a test renderer.

Evidence directory: `D:\PrintFlowStudio\Evidence\SCRUM-11100-1d8a0cb0485145df9f8936c64b79642e`.

| Live case | Result |
|---|---|
| Single-page PDF | PASS. Session `01a06a40-0fcc-747d-b68d-48cf3922cfa7`; count 1; page number 1; 600×300 RGB/8 PNG at 300 PPI; 50.8×25.4 mm full page; transparent background; exactly one selection/render; ReviewRequired; two Revisions |
| Two-page PDF | PASS safe refusal. Session `01a06a40-15c8-75f0-8831-719605f01743`; persisted count 2; no selected/prepared page; `PdfMultiplePages`; **zero native page selections, zero native renders, zero PNGs, one Source Revision**, no output Revision |
| Separate-process resume | PASS. Initial process **21932**, resumed process **15776**. Same Source and raster IDs/hashes/dimensions/inspection; ReviewRequired; two unchanged attempts; zero native page selections/renders |
| Source and lock checks | Both original synthetic PDFs and their immutable Source snapshots retain their pre-preparation SHA-256. Both QA attempts finish with the shared lock free. Independent read-only inspection also finds the production database lock free |

Live rendering provider: `Windows.Data.Pdf/10.0.19041.4522 (WinBuild.160101.0800)`. Neither case invokes Photoshop, opens an external document, dismisses a dialog, or generates a TIFF. Existing operator documents are outside this preparation path. No customer order or SCRUM-11132 E2E was run.

Supported identities:

```text
Source Revision:   01a06a40-10c6-7bd9-8715-b1edb7c26d14
Source SHA-256:    5663822B2A087960E8C52FE09C5E6D39352E6CAD01BE5F7A743F15A82BCA3D7B
Prepared Revision: 01a06a40-157c-7f85-aa19-4f0291fda364
Prepared SHA-256:  3DAAD65522A3BBC035484C8372EEAFE28EA071B8382DD62B872CB8429D10223B

Two-page Source SHA-256 before = after = Source snapshot:
2F1FE7091AED81C995FF92ADD7E1DDA57AA2C6C7ACAE63C16E030FAEC3EB1CE4
```

A separate Python process opened SQLite with `mode=ro` and independently read the persisted rows and source/raster files at **14:31 NZST**. Integrity check returned `ok`; foreign-key check returned no violations. It found two sessions, four attempts and exactly three Revisions in total. Pillow reopened the prepared PNG as 600×300, 299.9994 PPI, with transparent page area and the expected solid red synthetic content. Its artwork bounds were `[42,133,208,258]`, proving the 600×300 raster retains the full page rather than trimming to content. The rendered image was also visually inspected.

Retained evidence:

- `readiness.json`, `single-before.sha256`, `two-before.sha256`, `single-result.json`, `two-result.json` — readiness, before hashes, native counters and preparation outcomes.
- `pdf-smoke.db`, `resume.json` — persisted state and cross-process expectation.
- `independent-readback.py`, `independent-readback.json` — independent read-only assertions and rows. Readback SHA-256: `7199E3DB9650350E0F8926790E78BE7FD21239ACCF9431F95F15F061DC1AF510`.
- `tests/PrintFlow.Tests/TestResults/scrum-11100-live-preparation.trx` — exactly one enabled test covering Live A and B: passed.
- `tests/PrintFlow.Tests/TestResults/scrum-11100-live-resume.trx` — exactly one enabled separate-process resume test: passed.
- Final complete-suite TRX SHA-256: `EAAA119895EE9EBA31CFA91755BB5D68AE7AB062D8F2F2AF14364FFFC05A4659`.

No local smoke artwork, session database or accepted baseline file is committed. The only committed PDF binaries are the eleven deliberately synthetic regression fixtures. A narrow ignore exception includes those files, and Git treats PDF as binary so newline conversion cannot corrupt xref offsets; all eleven staged blobs were checked byte-identical to the fixtures exercised by the tests.

## Git and coverage decision

Implementation and tests are in new local commit **`30b92a0`** (`feat: prepare single-page PDF inputs with authoritative Windows inspection`). This report is delivered in a subsequent documentation commit. The final working tree is clean. Accepted history is preserved; nothing was amended, rebased or pushed, and no attribution trailers were added.

Recorded coverage delta, based on the original Jira acceptance text and the verification above:

```text
SCRUM-11100:
NOT_IMPLEMENTED → FULL

P0 core blockers:
0 remaining

SCRUM-11132:
still PARTIAL
PSD prerequisite = unblocked
PDF prerequisite = unblocked
fixed-workstation multi-format E2E still pending
```

This is the report's coverage decision, not a claim that the Jira service was updated. SCRUM-11132 is not marked FULL.

**SCRUM-11100 PASS — SINGLE-PAGE PDF INPUT PREPARATION VERIFIED**
