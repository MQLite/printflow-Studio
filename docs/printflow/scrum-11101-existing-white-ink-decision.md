# SCRUM-11101 — Handle existing white-ink spot channels explicitly

Date: 2026-09-07. Starting branch: `master`, clean at `4d18930`. Accepted configuration
`Adapters.Mode = Production`, preset `printflow-workstation-v1` **1.16.0**. No accepted history was
rewritten and nothing was pushed.

**Current verdict: SCRUM-11101 BLOCKED — EXISTING WHITE-INK DECISION NOT VERIFIED.**
The requirement authority, the pre-fix reproduction and the architectural data-flow determination
are complete and committed. Implementation of the decision path has not been carried out, and no
live Retain/Regenerate proof exists. The reasoning and the evidence gathered are recorded below so
the next slice starts from findings rather than from a re-audit.

## 1. Original Jira authority

Read before any source change from `C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv`.
The CSV carries internal Work Item IDs, not Jira keys. The row titled **Handle Existing White-Ink
Spot Channels Explicitly** is Work Item ID `11408`, Parent `11400`, Priority High, three story
points, labels `printflow,mvp,white-ink,spot-channel,photoshop,operator-choice`.

The mapping to the SCRUM keys used by this project is a constant offset of 307, corroborated on
four adjacent rows: `11406` → SCRUM-11099 *Implement PSD Input Preparation*, `11409` → SCRUM-11102
*Execute the Validated Photoshop Production Action*, `11410` → SCRUM-11103 *Validate Generated
Production TIFF*, `11411` → SCRUM-11104 *Build TIFF Final Review Mode*. `11408` is therefore
SCRUM-11101.

Exact acceptance text:

> When PSD or PDF preparation detects an existing white-ink spot channel, stop and ask the operator
> whether to retain the existing white ink or regenerate white ink using the validated production
> preset. Do not make a silent choice, and keep channel-name, polarity, density, choke and
> colour/white order as production-preset concerns rather than generic workflow assumptions.

Two things in that text shape everything below. The decision is **PSD or PDF preparation**, which
places it upstream of visual review rather than at output time. And channel name, polarity,
density, choke and colour/white order are explicitly **production-preset concerns** — so the
workflow layer must carry the operator's choice and the identity of what was observed, and must not
invent a general white-ink model of its own.

## 2. Accepted baseline as measured

`dotnet build PrintFlowStudio.sln -c Debug` succeeds with 0 warnings and 0 errors.

One complete suite was run against `4d18930` before any change:

```text
Failed:     1, Passed: 10865, Skipped: 0, Total: 10866, Duration: 3 m 25 s
```

The total matches the accepted baseline of 10,866. The single failure is
`PhotoshopPsdBoundaryTests.Production_psd_path_enforces_real_guards_and_independent_validation(variant: "malformed")`,
which expected `PsdPreparationFailed` and observed `OutputUnreadable`. Re-run in isolation the same
build passes 12/12. The test is load-sensitive rather than regressed: the malformed-output case
races the bounded settle poll in `PreparePsdAsync`, and under full-suite parallelism the poll
expires before two equal reads are obtained, which returns `OutputUnreadable` instead of reaching
`ValidatePsdRaster`. This is recorded as a pre-existing flake observed on this workstation, not as a
change introduced here, and not as an accepted 0-failure baseline.

## 3. Pre-fix reproduction

Commit `18d20c5` records the deterministic reproduction before any application source change, and
it passes against the unmodified Product. Two session-level tests in
`tests/PrintFlow.Tests/Integration/Persistence/ExistingWhiteInkPreFixReproductionTests.cs`:

1. **The refusal.** A synthetic PSD carrying exactly one existing W1 spot channel with non-empty
   content is imported and Original Confirmation is started. The Photoshop seam is actually called
   (so the refusal rests on observed facts, not on the file extension), preparation fails with
   `FailureCode.PsdUnsupported`, the attempt persists the observed inspection with `HasW1` and
   `HasSpots` true, only the import root Revision exists, the customer source is byte-identical and
   the automation lock ends free.

2. **The gap.** The step lands in `StepState.Failed` — the generic "this produced nothing" state,
   indistinguishable from a corrupt PSD or an unsupported colour mode. `AvailableCommands` offers
   `Retry` and the ordinary session-scoped commands and nothing else, and the closed `CommandKind`
   vocabulary contains no member mentioning a white-ink decision at all. The concept is absent from
   the Product, not merely unavailable in this state.

The second test asserts the absence of the concept and is expected to change when the decision path
lands. The existing live smoke `PsdPreparationWorkstationSmoke` already covers the same refusal on
the accepted workstation under `PRINTFLOW_PSD_PREPARATION_SMOKE=1`, variant `w1`.

## 4. Where the current refusal is made

Three places refuse in concert, and all three are positive checks rather than fallthroughs:

- `PhotoshopPsdProgram.Create` — the fixed JSX enumerates `source.channels`, sets `spot` for any
  `SPOTCOLOR` and `w1` for any channel named `W1`, and returns
  `'Existing W1/spot channels require an operator decision.'` before touching the document. The
  refusal message already names the missing decision.
- `ProductionPhotoshopOutputProcessor.PreparePsdAsync` — maps that unsuccessful outcome to
  `FailureCode.PsdUnsupported` while carrying `PsdInspection` on the failure so the attempt can
  persist it.
- `ValidatePsdRaster` and the `SessionService` re-check — both refuse a raster whose inspection
  reports `HasSpots` or `HasW1`, so a prepared Revision cannot appear by another route.

`PsdInspection` exposes only the two coarse predicates `HasSpots` (any channel of kind
`SPOTCOLOR`) and `HasW1` (any channel named `W1`, case-insensitive). That is sufficient to refuse
and insufficient to decide: it cannot distinguish one supported non-empty W1 from a W1 plus three
unrelated spot inks, and it carries no per-channel content facts at all.

## 5. The architectural determination (§4)

This is the finding that governs the whole slice, and it was mandatory before implementation.

### 5.1 The problem

The accepted production carrier between preparation and output is a **flattened RGB/8 PNG**. PSD
preparation merges a disposable duplicate, removes every non-component channel, and saves that as
PNG; the operator reviews the PNG; Trim, Print Dimensions and Photoshop Output all resolve the
approved raster through the step lineage. A PNG cannot represent a spot channel. So a retained W1
cannot reach the production TIFF through the artefact the operator currently approves, and §4 of
the task is right to forbid assuming otherwise.

The output path is equally closed against it. `GuardedPhotoshopW1Executor.ValidatePrepared` requires
the document to be RGB/8 with exactly three component channels and **no W1** before it will invoke
anything, and the fixed JSX repeats the check (`'A pre-existing non-component channel proves an
unsupported prior state.'`). An existing-W1 document cannot enter the accepted W1 path at all.

### 5.2 What the signed Action actually does

From `D:\PrintFlowStudio\Baseline\workstation-v1\apps\photoshop-2019\cmyk-w1-action-runtime.json`,
the accepted runtime command transcripts are:

| Branch | Runtime commands |
|---|---|
| `W1_0px` | 转换模式, 设置 选区, 建立 |
| `W1_1px` | 转换模式, 设置 选区, 收缩, 建立 |
| `W1_2px` | 转换模式, 设置 选区, 收缩, 建立 |

The RGB→CMYK conversion (转换模式) and the W1 creation (设置 选区 / 收缩 / 建立) are **one
indivisible Action**. `app.doAction` cannot run a prefix of an Action, so there is no way to obtain
the accepted CMYK conversion from the signed artefact without also generating a W1. Running it on a
Retain job would therefore regenerate exactly the channel the operator asked to keep, which §13
forbids.

This initially reads as a hard blocker requiring a new signed `.atn`.

### 5.3 Why it is not a blocker

The CMYK conversion is **not** exclusively Action-internal. The accepted preset carries it as a
first-class, hash-evidenced workstation contract:

```json
"colourSettings": {
  "rgbWorkingSpace": "sRGB IEC61966-2.1",
  "cmykWorkingSpace": "Coated FOGRA39 (ISO 12647-2:2004)",
  "grayWorkingSpace": "Dot Gain 15%",
  "spotWorkingSpace": "Dot Gain 15%",
  "conversionCommand": "图像 > 模式 > CMYK 颜色",
  "convertToProfileCommandUsed": false,
  "visibleSettingsManifestSha256": "1822256A31EAE1DE3CC2F5779766E89F919E0E35899BA6B69EF9203D9C11A6A3"
}
```

`图像 > 模式 > CMYK 颜色` is Image > Mode > CMYK Color — precisely `doc.changeMode(ChangeMode.CMYK)`
— performed under a pinned CMYK working space, explicitly **not** via Convert to Profile, with the
visible colour settings pinned by hash. The 转换模式 command inside each signed Action is that same
conversion. The accepted colour transform is a property of the verified workstation, and the Action
is one caller of it rather than its sole definition.

That distinction is what makes Retain implementable from existing auditable primitives, as §17
permits, without re-recording the signed `.atn` and without a preset-version change. The part that
is genuinely Action-owned and branch-parameterised — the selection, the 0/1/2 px contraction and
the spot-channel creation — is exactly the part Retain must skip.

### 5.4 Chosen data flow

```text
customer PSD  (immutable Source snapshot, never reopened by Production)
  → managed Working copy
  → Photoshop inspection: classify existing white ink   ← facts observed
  → OPERATOR DECISION: Retain existing / Regenerate     ← before any destructive step
  │
  ├─ Retain
  │    a) visual PNG   — merged duplicate, non-component channels removed (as today)
  │    b) W1 carrier   — managed PSD retaining the existing W1, saved into the attempt
  │                      workspace, hashed, bound to (a)
  │    → operator reviews (a) → approved raster Revision
  │    → Print Dimensions
  │    → Photoshop Output opens the CARRIER, proves its binding to the approved (a),
  │      resizes to 300 PPI, runs changeMode(CMYK) under the verified colour settings,
  │      saves TIFF. The signed W1 Action is NOT invoked.
  │
  └─ Regenerate
       a) managed copy with ONLY the supported existing W1 removed
       b) visual PNG exported from it (as today)
       → operator reviews (b) → approved raster Revision
       → Print Dimensions
       → Photoshop Output runs the ACCEPTED PATH UNCHANGED: RGB/8, no W1,
         signed W1 Action, CMYK + generated W1, TIFF.
```

The decision sits after positive observation and before every destructive step, satisfying §5. The
operator still reviews a truthful visual artefact on both branches and the decision is not visual
approval, satisfying §20. The customer file is never reopened by Production on either branch, and
the carrier is a hashed Session-workspace artefact bound to the reviewed Revision, satisfying §14
and §15.

Regenerate needs **no** change to the accepted output path: once the supported W1 is removed on the
managed copy, the document is exactly the RGB/8 three-component no-W1 state the existing guards
already require. This is why Regenerate is the branch that can be completed and proven first.

### 5.5 Open risks that only live evidence can settle

The Retain output path rests on three Photoshop behaviours that cannot be established by reading
source or preset, and that must be proven on the accepted workstation before Retain can be claimed:

1. A spot channel survives `changeMode(RGB → CMYK)` in CC 2019 with its content intact. Expected,
   because spot channels are stored independently of the composite mode, but unproven here.
2. A spot channel survives the accepted 300-PPI Image Size resize, resampled consistently with the
   colour channels, so the retained W1 stays aligned with the visual document as §13 requires.
3. `GuardedPhotoshopTiffSaver` and `ProductionTiffInspector` accept a W1 that the signed Action did
   not produce. The inspector's requirements are structural — exactly one channel named `W1` of
   Photoshop kind spot colour, five samples, `ExtraSamples=0`, CMYK/8, 300 PPI, non-empty fifth
   sample — and nothing in them refers to the Action, so acceptance is expected. `TiffSaver`'s
   pre-save guard currently requires `PhotoshopW1PreparedDocument`, which is produced only by the
   W1 executor, so a Retain result must be expressible in that type or in a sibling the saver
   accepts.

Point 3 is a code-shape question this slice must answer; points 1 and 2 are live-evidence questions
and are the real gate on §41.

## 6. Classification contract (§6) — designed, not implemented

`HasSpots`/`HasW1` are too coarse to authorise a decision. The supported-existing-W1 contract needs
per-channel facts the current inspection does not carry, and the native program must report them:

| Observation | Source | Why |
|---|---|---|
| Channel name | `channel.name` | `W1` exactly, matching the accepted production name |
| Channel kind | `channel.kind` | must be `SPOTCOLOR`; a mask or alpha named `W1` is not white ink |
| Non-empty content | `channel.histogram`, bins 0–254 | an empty W1 is not a retainable authority |
| Spot channel count | enumeration | exactly one spot channel total |
| Canvas dimensions | `doc.width/height` | binds the observation to the inspected artefact |

The closed contract: **supported existing W1** means the document contains exactly one spot channel,
that channel is named `W1`, its kind is `SPOTCOLOR`, and its content is non-empty. Anything else —
unknown spot channels, W1 plus unrelated spots, several W1 candidates, a W1 that is empty, or a W1
that is not a spot channel — remains the existing safe refusal, because the original Jira row
defines behaviour only for "an existing white-ink spot channel" and keeps polarity, density and
choke as preset concerns rather than things the workflow may infer. Polarity is deliberately **not**
inspected or normalised: the accepted `cmyk-w1-action-runtime.json` fixes the production polarity
convention, and re-deriving it from arbitrary customer content would be exactly the generic
workflow assumption the Jira row prohibits.

Every refusal in §19 stays: arbitrary spot channels, ambiguous channel sets, unsupported colour or
depth, corrupt PSD, missing composite and unknown Photoshop state are untouched. Only the
positively-classified supported case is diverted from refusal to decision.

## 7. What was not done

No Product source was changed. Specifically, none of the following exist yet: the
`ExistingWhiteInkDecision` vocabulary and its bound authority record; the workflow command, engine
legality and decision-required state; schema migration `0013` and its storage; the Session-screen
surface, its en-US/zh-CN strings and its `Session.WhiteInkDecision` / `Session.RetainExistingWhiteInk`
/ `Session.RegenerateWhiteInk` automation identities; the carrier capture and its binding; the
managed W1 removal for Regenerate; the Retain output path; branch provenance on `PrintOutput`; the
automated matrix of §38; and every live run of §40–§45.

SCRUM-11101 therefore remains **PARTIAL**. SCRUM-11104 is untouched and its boundary was not
crossed. SCRUM-11132 remains PARTIAL with the existing-white-channel prerequisite still open.

## 8. Git state

Two commits on `master`, nothing pushed, no accepted history rewritten, no branch or worktree
created:

- `18d20c5` — `test: reproduce the existing-W1 hard refusal before SCRUM-11101`
- this report

Unrelated untracked files in the worktree were left untouched.
