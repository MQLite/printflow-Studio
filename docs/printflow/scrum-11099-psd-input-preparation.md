# SCRUM-11099 — PSD input preparation

Date: 2026-09-04. Starting branch: `master`, clean at `b186c66`. The accepted configuration remains Production, `printflow-workstation-v1` **1.16.0**. No accepted history was rewritten and nothing was pushed.

Current verdict after the accepted-workstation closure below: **SCRUM-11099 PASS — PSD INPUT PREPARATION VERIFIED**. The original implementation report and its blocked evidence are preserved below as historical findings.

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

## Final accepted-workstation verification closure — 2026-09-04

Closure ran at 13:47–13:51 NZST (01:47–01:51 UTC), starting clean at `b23936a`, with implementation `872570a` unchanged. This section supersedes the original blocked verdict above; it does not erase or reinterpret either earlier CMYK failure. The closure request explicitly confirmed that the operator had manually saved/closed unrelated Photoshop work. Initial process enumeration independently found **zero Photoshop processes**. PrintFlow then performed its normal accepted launch. No operator recovery or manual synthetic-document cleanup occurred during this run.

### Read-only preflight

The existing `PhotoshopWorkstationSmoke` ran with only its read-only enable switch; its open/close switches were unset. The real composed readiness screen and `VerifiedEnvironmentGateWorkstationSmoke` also ran. Three checks passed using the existing final-source test binaries, with `--no-build --no-restore`.

| Required observation | Closure evidence |
|---|---|
| Accepted identity | `D:\Adobe Photoshop CC 2019\Photoshop.exe`, SHA-256 `81EE8930FC1E28637B501866A8B946FA0740C376CDA4302FEA61AA82806A80C5`; signed baseline and all 28 preset evidence hashes verified |
| Process and signed main frame | Exactly one process after normal launch, PID `12764`, started `01:47:07.4483112Z`; handle `0x3113A`; class `Photoshop`; title `Adobe Photoshop CC 2019` |
| Accepted state | `KnownStartScreen`, signed marker `OWL.WelcomeScreenView`; no document marker. The committed classifier can return this state only with the main frame enabled and no titled blocking owned dialog |
| Independent document observation | Read-only attachment to the existing `Photoshop.Application.130` object: accepted installation path, version `20.0.10`, `Documents.Count = 0` at 13:48:10; therefore no unrelated or unsaved document loaded |
| Lock | Production database independently opened with SQLite `mode=ro`: `AutomationLock.SessionId`, `AcquiredAtUtc`, `ProcessId`, and `MachineName` all null. The isolated QA database starts migrated and empty |
| Production authorization | Readiness: `Workstation verified for production work.`; real `VerifiedEnvironmentGate`, `gate(Production) = ALLOWED`; `Adapters.Mode = Production`; preset `printflow-workstation-v1 1.16.0` |

The additional computer-use screenshot capture failed twice with `SetIsBorderRequired ... 0x80004002`; no screenshot is counted as evidence. State acceptance rests on the real signed guard/classifier and independent read-only document observation. No UI recovery action was taken.

### Live CMYK refusal and automatic owned-document cleanup

Ran only the `cmyk` case of the committed `PsdPreparationWorkstationSmoke`, with `PRINTFLOW_PSD_PREPARATION_SMOKE=1` and `PRINTFLOW_PSD_CASE=cmyk`. The test filter selected exactly **one test: passed**. This uses the Home import service (`ISessionService.ImportAsync`), its immutable PSD Source snapshot, `StartStep(OriginalConfirmation)`, the real registered Production environment gate and `ProductionPhotoshopOutputProcessor.PreparePsdAsync`. It is the committed preparation boundary, with an isolated QA database and purpose-built synthetic input.

| Identity or result | Observed value |
|---|---|
| QA directory | `D:\PrintFlowStudio\Evidence\SCRUM-11099-5bbfeee1e87244c2af10822735bbcb15` |
| Fixture | `PF_SCRUM11099_cmyk.psd`, 118 bytes, synthetic PSD v1, 4×3 pixels, explicit real merged composite resource |
| Session | `01a06a1b-1690-7b68-afe0-fdc864e0deca` |
| Source Revision | `01a06a1b-1709-7e6c-9b68-28a4cb951af0`, `IMPORT`, format `PSD` |
| Preparation attempt | `01a06a1b-178e-7e77-8a26-68cb44d2e86e`, 01:49:15.534–01:49:25.145 UTC |
| Adapter | `photoshop-cc2019-production-v1/psd-preparation-v1` |
| Photoshop inspection | `OriginalMode = CMYK`, `BitDepth = 8`, `PixelWidth = 4`, `PixelHeight = 3`, `HasRealMergedData = true`, Photoshop `20.0.10` |
| Channels | Four `COMPONENT` channels: `青色`, `洋红`, `黄色`, `黑色`; no alpha-mask or spot channels, no W1; visual transparency unmeasured/null |
| Structured refusal | `PsdUnsupported`, `Failure_PsdUnsupported`, `PSD was not prepared: Only RGB 8-bit PSD input is supported.`; `IsRetryable = false`; persisted failure context `{}` with no cleanup error |
| Cleanup | Real exact-owned-document cleanup completed; smoke independently required `KnownStartScreen` again. Read-only document query at 13:49:52 found zero documents in the same PID `12764` |

The incompatibility was discovered by the actual guarded Photoshop inspection. No CMYK-to-RGB conversion or flatten-and-continue operation occurred. No prepared raster Revision, PNG, or TIFF exists in this CMYK session. The synthetic Working PSD remains on disk as attempt evidence, but is **not retained as an open Photoshop document**. No unrelated document was opened, saved, discarded, closed or changed; no unknown dialog was dismissed and Photoshop was not terminated.

### Independent persistence and source-integrity readback

A separate Python process opened both QA databases using SQLite read-only URIs, without application repositories or services. It retained every table's rows and asserted the closure contract; integrity and foreign-key checks also passed.

- Exactly two CMYK attempts exist: Import `SUCCEEDED`, followed by one `PREPARE_PSD` attempt `FAILED` at `OriginalConfirmation`. `RetrySequence = 0`, `RetryOfAttemptId = null`, and preparation `OutputRevisionId = null`.
- The single Revision remains the valid PSD Import Revision. `InputSnapshot.RootRevisionId` still points to that same Source Revision. `OriginalConfirmation` is `FAILED` with one attempt; `PrintDimensions` and `PhotoshopOutput` remain `WAITING` with zero attempts. There is no Trim attempt, PrintOutput, prepared Revision or fabricated successful preparation state.
- `PsdInspection` persists CMYK/8, canvas, real-merged-data evidence and Photoshop version; four ordered `PsdChannel` rows belong to that preparation attempt. The PSD-specific failure detail is persisted separately in `ProcessingAttempt.FailureDetailJson`.
- CMYK and RGB QA locks have all ownership fields null. The production lock is also null/free and matches its preflight readback.

Before the live run, the exact deterministic bytes of the committed CMYK generator were materialized as `CMYK-reference-before.psd` and hashed at 01:48:59 UTC. The live harness generated the identical fixture; Import recorded its hash at 01:49:15.401 UTC, before preparation started. After refusal, independent byte comparisons and SHA-256 reads matched the before-reference, live fixture, Source snapshot and Working PSD:

```text
before reference SHA-256 = Import-time Source SHA-256
                        = after fixture SHA-256
                        = after Source snapshot SHA-256
                        = after Working PSD SHA-256
                        = E336D6FBD58F6F66C0DA6A1C0EC6EB1C43E517C565727DB6D4D756EE6A034D85
```

Source snapshot: `D:\PrintFlowStudio\Sessions\S_20260904T014915Z_64e0deca\Source\PF_SCRUM11099_cmyk.psd`. All compared files are 118 bytes. Only synthetic artwork was used.

### Supported-path follow-up and final Photoshop state

After CMYK cleanup and independent zero-document readback, ran only the existing `rgb` smoke: **one test passed**, using the real Production gate and adapter. Session `01a06a1b-f4f2-7a77-9b1f-5e9c8bec920e` in `D:\PrintFlowStudio\Evidence\SCRUM-11099-f213301d694645e1a64208476383e094` prepared a full-canvas 4×3 RGB/8 PNG and stopped at `ReviewRequired`.

```text
Source Revision: 01a06a1b-f585-7b73-a690-8fa3e95ade3a
Source SHA-256:  7EC057BB073977E2A61A4AAAA805B783A454F9CCD9C9FBAB98BFA2195C6E0C8B
PNG Revision:    01a06a1c-1938-7a9d-810e-cc281dc35ffb
PNG SHA-256:     1B2E72EC93EE7E3531570C5B85061FA00CD61F0E1A01F5E0094641B34DD522F3
```

Independent SQLite/file readback confirmed the two successful Import/preparation attempts, both persisted hashes and a free lock. The smoke verified `KnownStartScreen → KnownStartScreen`. Final read-only Photoshop observation at **13:51:18 NZST** confirmed the same single accepted process, version `20.0.10`, no-document main-frame title and `Documents.Count = 0`. The signed classifier's accepted final state establishes an enabled main frame without a blocking dialog. The complete observed sequence is **CMYK refusal → clean accepted state → supported RGB preparation succeeds → clean accepted state**.

### Retained closure evidence and final coverage decision

Closure evidence bundle: `D:\PrintFlowStudio\Evidence\SCRUM-11099-closure-20260904-1347`.

- `scrum-11099-closure-preflight.trx`: three read-only readiness/gate/foundation checks passed.
- `scrum-11099-closure-cmyk.trx`: one live CMYK case passed; SHA-256 `D9174B5E7528D02B6D8A946217231C519A6A956278E1ED4E0C0DB9553F8B0EB3`.
- `scrum-11099-closure-rgb-followup.trx`: one live supported follow-up passed.
- `preflight-documents.json`, `cmyk-post-documents.json`, `final-documents.json`: independent native document-count/process observations. Final observation SHA-256 `DA143A84040FBE477968A4225A10DB4B8F1C95DBA5C738015C652692BD5E0CD3`.
- `before-integrity.json`, `CMYK-reference-before.psd`, `independent-readback.json`: pre-run hash, deterministic reference and independent persisted-state/file readback. Readback SHA-256 `63D3885C66B2403FDFF1C5671D61295D8A5034AB62AF4F00D3530845197A80AA`.
- `before-integrity.py`, `independent-readback.py`, `observe-documents.ps1`: local evidence generation/readback scripts; no product source changes.

No product defect was exposed and no product source, tests, dependencies, accepted configuration or preset changed. The existing final-source **0-warning/0-error build** and **10,253 passed / 0 failed / 0 skipped** suite remain the code gate; the full suite was not rerun. This closure adds only the bounded live/readback evidence above. A new report-only commit follows the three existing SCRUM-11099 commits, with no amendment, rebase, push or attribution trailer. Local artwork, databases and evidence binaries remain outside Git.

Recorded Jira coverage delta:

```text
SCRUM-11099:
NOT_IMPLEMENTED → FULL

SCRUM-11132:
still PARTIAL
PSD prerequisite unblocked
PDF prerequisite still blocked
```

This records the coverage decision in the report; it does not claim a Jira service update or the separate Generate Print TIFF fixed-workstation E2E.

**SCRUM-11099 PASS — PSD INPUT PREPARATION VERIFIED**
