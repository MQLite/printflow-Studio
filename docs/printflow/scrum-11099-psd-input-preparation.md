# SCRUM-11099 — PSD input preparation

Date: 2026-09-04. Starting branch: `master`, clean at `b186c66`. The accepted configuration remains Production, `printflow-workstation-v1` **1.16.0**. No accepted history was rewritten and nothing was pushed.

## Original Jira authority

Read before source changes from `C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv`, row titled **Implement PSD Input Preparation**. The CSV uses internal Work Item ID `11406`, Parent `11400`, Priority High, five story points. Those internal numbers are not the Jira key; this implementation is SCRUM-11099.

Original acceptance text:

> Support PSD only when a Photoshop-compatible composite preview is available. Detect relevant colour mode, Alpha channels and spot channels, use Photoshop to export a flattened managed working copy for the production Action and preserve the original source. Unsupported or unreadable PSDs must fail clearly rather than being partially processed.

## Pre-fix reproduction

Commit `968847e` records the deterministic reproduction before any application source change. One integration test passed against the old implementation:

1. The Home picker advertises `*.psd` and accepts a synthetic 4×3 PSD v1 with real merged RGB/8 image data.
2. Home creates a byte-identical Source snapshot and an Import Revision, labelled PSD, with null pixel dimensions.
3. Workflow selection and Original Confirmation succeed.
4. Print Dimensions becomes current and fails with `PreconditionNotMet` because no raster dimensions exist.

The final version of the test asserts the corrected contract: the Source is still preserved and advertised, but confirmation/downstream sizing cannot bypass PSD preparation. An adapter without PSD capability refuses specifically at preparation. The original pre-fix assertion remains inspectable in its commit.

## Support contract

| Input state | Behaviour |
|---|---|
| PSD v1, positive real-merged-data evidence, Photoshop-openable RGB/8, three component channels | Prepare full-canvas PNG and require raster review |
| Same, with visible transparency | Preserve transparency, including partially transparent pixels |
| Extra alpha-mask channels | Inspect and record channel names/types; PNG represents visual transparency, not auxiliary masks |
| Existing spot channel or a channel named W1 | Refuse; retain inspection metadata; no retain/regenerate decision is made |
| CMYK, grayscale, bitmap, other modes; non-8-bit depth | Refuse; no arbitrary colour/depth conversion |
| Missing/false/ambiguous compatibility evidence | `PsdCompositeMissing` or `PsdUnreadable`; no Photoshop open |
| Corrupt/truncated envelope or unreadable document | Structured preparation/open failure; no prepared Revision |
| Unknown Photoshop state, modal, lost identity/target, unavailable executable | Existing guarded failure; no generic dismissal or automatic retry |
| Missing/malformed/wrong-size/wrong-alpha raster | Refuse independent validation; retain attempt history, no prepared Revision |

PSD support is deliberately constrained to the existing Production input contract, RGB/8. The original colour mode and depth are observations, never inferred from the `.psd` extension. Unsupported observations may carry null visual-transparency evidence because no merged duplicate was made; null does not mean opaque.

## Composite-preview behaviour

`PsdCompositeProbe` reads the bounded PSD envelope and Adobe Image Resource **1057**, version 1, `hasRealMergedData`. It requires an explicit true flag and a present composite image section. Missing and false flags are refused. Opening in Photoshop is not used as compatibility evidence.

This is a small structural metadata reader, not a PSD pixel decoder or rendering engine. It interprets no layers, blend modes, effects, masks or compressed pixels. The file layout authority is the [Adobe Photoshop File Formats Specification](https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/). The standard Photoshop document observation does not establish the on-disk real-merged-data flag, so this narrow read supplies the missing fact without a third-party dependency.

The positive compatibility fact is persisted with `PsdInspection`. A pre-open refusal is persisted as its typed failure code and detail. Actual interpretation and raster production remain Photoshop's responsibility. Header evidence alone never creates a raster Revision.

## Workflow integration and operator review

Home performs only the existing import/snapshot operation. Workflow selection remains available before any derived Revision exists.

For a PSD source, the existing **Original Confirmation** step gains its existing producing/review lifecycle: Waiting → Processing → ReviewRequired → Approved. The operator explicitly selects **Prepare PSD for review**. No new step kind or hidden ViewModel automation was added. `WorkflowCatalog` selects this fixed capability from the persisted root format; `SessionService` orchestrates it using the existing Photoshop gate, automation lock, attempt and workspace machinery.

`ConfirmOriginal` cannot acknowledge an opaque PSD. Approval is bound to the prepared PNG Revision/hash through the ordinary review command. Downstream Trim, sizing, enhancement and output resolve the approved raster through the existing step lineage. Production never reaches back to the customer path. No trim or resize occurs during PSD preparation; `TrimGeometry` remains the later Trim operation's authority.

The read model reports the original source format separately. English and Chinese notices distinguish the unchanged PSD source from its Photoshop-prepared PNG representation. The PSD itself is never sent to the general preview decoder; the managed PNG uses the existing preview pipeline. A PSD-to-PNG pair is not presented as two directly decodable images.

Historical PSD sessions acquire no fabricated metadata. If an old session already acknowledged an opaque PSD, downstream producing/sizing commands refuse with `PsdPreparationFailed` and require returning to Original Confirmation.

## Source preservation and Photoshop boundary

The selected customer file and immutable Source snapshot retain their existing import/hash semantics. Photoshop receives only a new attempt-scoped Working copy. Input/output paths must be distinct siblings in that attempt, and an existing output cannot be overwritten.

The Production adapter reuses accepted executable identity/hash, process identity, window/dialog classification, exact-document identity, and owned-document cleanup. Its closed native bridge attaches only to the running accepted Photoshop CC 2019 automation object and checks its installation directory. It accepts typed paths, never caller-provided script.

Photoshop observes dimensions, mode, bit depth, component/alpha/spot channels, W1 and application version. After refusing unsupported states, it merges a disposable duplicate and saves that duplicate as PNG. It does not flatten the original document, add a background, change its mode, resize or trim. Auxiliary alpha masks are recorded before the duplicate's PNG export. Visual transparency is measured on the duplicate and independently compared with PNG pixel alpha.

The Working PSD is rehashed after the native call. The source document is closed only through the accepted exact-owned-document seam. Unknown dialogs are never dismissed. A changed external target stops input. On a stop arriving during the synchronous native call, that call cannot be recalled; after it returns, no subsequent external input or cleanup is sent.

## Raster validation and provenance

The raster must settle across two equal independent file observations, reopen as RGB PNG, have 8-bit samples and the exact full PSD canvas, and match the observed transparency. A `PhotoshopValidatedPsdCandidate` is then passed to the existing sole Photoshop `AdapterOutput` factory, which compares destination, byte length and SHA-256 again. `SessionService` independently inspects the returned file before creating the Revision.

The provenance chain remains:

```text
InputSnapshot.RootRevisionId
  → Import Revision: exact PSD Source, original hash/format
  → PreparePsd Revision: managed PNG, its own id/hash/dimensions/colour/alpha
       SourceRevisionId = PSD Import Revision
       producing ProcessingAttempt.OutputRevisionId = PNG Revision
```

Attempt adapter identity is `photoshop-cc2019-production-v1/psd-preparation-v1`; inspection separately records Photoshop's observed version. Output colour mode is the raster Revision's typed `FileFacts.ColourMode`. The fixed verified output depth is 8 bits.

Migration **0010** widens the `Revision.Operation` CHECK by a transactional table rebuild, preserving existing columns, rows, indexes and the immutable-identity trigger. It adds typed `PsdInspection` and ordered `PsdChannel` tables linked to the producing attempt. Essential inspection facts are not stored only in opaque JSON. Failed unsupported attempts retain their inspection in the same closing transaction. Historical attempts are not backfilled. Existing migration tests exercise upgrades with prior revisions/attempts and preserved foreign-key relationships.

## Restart, retry, cancellation and failures

Rehydration reconstructs the PSD capability from the root format and the prepared state from persisted step/revision/attempt records. Loading performs no preparation. Integration tests rebuild services over the same database; a separate live test-host process also restored the previously prepared transparent PSD with identical IDs/hashes/dimensions and exactly two unchanged attempts (Import and preparation).

The existing Retry command returns a failed step to Waiting; Start creates the next attempt. Tests prove Failed attempt 1, Succeeded attempt 2, `RetrySequence = 1`, linked retry history, distinct destinations, and an output Revision belonging only to attempt 2. The previous partial output remains intact.

Operator Stop and Take Over are tested through the runtime signal: the attempt becomes Cancelled, produces no Revision and releases the lock. Take Over retains the existing HandedOff semantics. Cancellation-token failures close on an uncancelled metadata transaction under existing failure semantics. Launch/open/state/identity/native/output/target-loss cases are contained, with no hidden retry. Startup recovery continues to interrupt an unfinished persisted attempt.

If an unsupported observation is followed by cleanup failure, `PsdUnsupported` remains the primary failure; the cleanup code/detail and unknown retained state are recorded alongside it. This prevents an incidental cleanup error from hiding the PSD incompatibility.

## Automated verification

- Pre-fix reproduction: **1 passed**, preserved in `968847e`.
- PSD/workflow/persistence, migration, Home, Production gate and Photoshop guard set: **205 passed / 0 failed / 0 skipped** before the final factory refinement.
- Focused PSD, architecture, failure vocabulary and trim UI locale verification: **197 passed / 0 failed / 0 skipped**.
- Final clean build: **0 warnings / 0 errors**, repository-pinned .NET SDK 10.0.400 at `C:\Users\admin\AppData\Local\Microsoft\dotnet\dotnet.exe`.
- Final complete suite: **10,253 passed / 0 failed / 0 skipped**, 2m35s, using `DOTNET_CLI_UI_LANGUAGE=en-US`; retained result `tests/PrintFlow.Tests/TestResults/scrum-11099-full-suite-final.trx`.

The first complete run reported 10,246 passed and seven failures: four assertions still fixed to the pre-PSD migration/failure/save/output vocabulary, plus three existing trim UI tests expecting English while the test host used the workstation's zh-CN culture. The vocabulary assertions were extended explicitly for the new closed PSD path; TIFF's save/candidate constraints remain enforced. `DOTNET_CLI_UI_LANGUAGE=en-US` makes the existing English assertions deterministic without changing product localization or those trim tests. The final complete rerun passed after these corrections.

Tests distinguish evidence sources: scripted native/adapter tests prove control flow, validation and persistence; they do not claim Photoshop actually opened those states. The following live cases supply the actual Photoshop observations.

## Controlled accepted-workstation smoke

All cases use purpose-built synthetic PSDs, the committed Production configuration, the real `VerifiedEnvironmentGate` (ALLOWED), accepted Photoshop 20.0.10, and isolated QA databases. No customer production order or TIFF generation was run.

| Case | Evidence and result |
|---|---|
| RGB/8 opaque composite | PASS. Session `01a069f5-b8b4-7303-88ab-988bb59a818e`; 4×3 PNG, source unchanged, ReviewRequired, lock free, KnownStartScreen → KnownStartScreen |
| RGB/8 transparent layered PSD | PASS. Session `01a069f7-a7a2-768e-936a-16f01fa6ad72`; Photoshop observed transparency; independent PNG validation passed, full 4×3 canvas retained, source unchanged, lock free, clean start screen restored |
| Existing W1 spot | PASS safe refusal. Session `01a069f9-f5e3-7227-afd7-168a6cfe96fa`; Photoshop observed `W1/SPOTCOLOR`; persisted `PsdUnsupported`, no raster Revision, source unchanged, lock free, clean state restored |
| No compatible composite | PASS safe refusal through the Production session path. Session `01a06a02-82b2-7d93-86c8-567568ff85f5`; `PsdCompositeMissing` before any Photoshop call, source unchanged, no raster Revision, lock free |
| Separate-process resume | PASS. Process 912 restored the transparent session; same Source and PNG IDs/hashes/dimensions; two attempts unchanged; lock free |
| CMYK | Final clean refusal verification remains incomplete. First run stopped before Open with a guarded timeout; captured screen was the empty Photoshop start screen. A separate controlled rerun reached inspection but timed out during exact-document cleanup. No raster Revision was created and the lock was released. Subsequent read-only observation showed an unrelated document with unsaved changes; further interactive automation was stopped. No causal claim about the first timeout is made. |

Transparent live identities:

```text
Source Revision: 01a069f7-a82c-72df-95b7-024a24edfc23
SHA-256: 8F69A84F7DEF0C33C1EEE1261D45B789484261F452FAF1A4D31257E8EBCD0624
PNG Revision: 01a069f7-cadd-7d55-ba1f-03cdc08ceb23
SHA-256: 320D937528D70E13EEBC4D69A2389C0FEF82A66F0F0CC1A9C5523753E6C5BE3F
```

Retained local evidence:

- `D:\PrintFlowStudio\Evidence\SCRUM-11099-2ac0850f9d7d4f879f94bf6acdb3f237` — opaque success.
- `D:\PrintFlowStudio\Evidence\SCRUM-11099-2ad2a4d0fce442ba92d46b62cb4b83cd` — transparent success and resume expectation.
- `D:\PrintFlowStudio\Evidence\SCRUM-11099-fe30731c544d40d4b4339d63cebcc605` — W1 refusal.
- `D:\PrintFlowStudio\Evidence\SCRUM-11099-no-composite-f5d933609b2a45c189a16397be08648b` — pre-open composite refusal.
- `D:\PrintFlowStudio\Evidence\SCRUM-11099-b73860cc79fd45bd961e237046860445` and `SCRUM-11099-5de17edb8fbf4872a85bb2569eeeb303` — retained CMYK attempt history.
- `tests/PrintFlow.Tests/TestResults/psd-live-*.trx` and `scrum-11099-full-suite-final.trx` — ignored local test results.

No artwork, PSD/PNG output, screenshot, database or signed baseline was added to Git.

## Dependencies, Git and coverage decision

No package or lockfile changed; no new PSD rendering engine or third-party parsing dependency was introduced. No dependency/security audit was required for a dependency change. The accepted preset, executable trust configuration and Production mode remain unchanged.

New local commits: `968847e` records the pre-fix reproduction; `872570a` contains the implementation and final tests. This report is delivered in a subsequent documentation commit on `master`. The final working tree is clean. Nothing was pushed, no accepted commit was amended/rebased, and no attribution trailer was added.

SCRUM-11099 implementation is present, including the advertised-PSD early-boundary fix. **NOT_IMPLEMENTED → FULL is withheld** until the remaining accepted-workstation verification is complete. No other Jira classification was modified.

**SCRUM-11132 remains PARTIAL.** The PSD prerequisite has working, live-proved supported paths, but final acceptance is not declared unblocked while this report is blocked. The PDF prerequisite remains blocked. This task has not performed or claimed the Generate Print TIFF fixed-workstation E2E.

To complete verification, the operator must save/close unrelated Photoshop work and leave an accepted empty state. Then rerun the controlled final-source PSD smoke, including a clean CMYK refusal, and confirm Photoshop/lock cleanup. The requirement comes from the user's owned-document/unknown-dialog safety rules and the accepted Photoshop guards; PrintFlow must not close unrelated work to obtain a passing smoke.

**SCRUM-11099 BLOCKED — PSD INPUT PREPARATION NOT VERIFIED**
