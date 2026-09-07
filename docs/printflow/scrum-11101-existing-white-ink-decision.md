# SCRUM-11101 — Handle existing white-ink spot channels explicitly

Date: 2026-09-07. Starting branch: `master`, clean at `4d18930`. Accepted configuration
`Adapters.Mode = Production`, preset `printflow-workstation-v1` **1.16.0**. No accepted history was
rewritten and nothing was pushed.

> **Status as of 2026-09-07: SCRUM-11101 is SUPERSEDED_BY_DESIGN.** A business clarification defines
> PSD and PDF as visual-only design inputs; PrintFlow does not retain source white ink and does not
> ask Retain vs Regenerate. **The current verdict is in
> [SCRUM-11101 Business Contract Supersession](#scrum-11101-business-contract-supersession) at the
> end of this document.** Everything between this banner and that section — the original requirement
> determination and the SCRUM-11101-A feasibility gate — is preserved unedited as the historical
> record, including its two verdicts, which were current when written and are not current now.

**Verdict as recorded on 2026-09-07 by the requirement-authority slice: SCRUM-11101 BLOCKED —
EXISTING WHITE-INK DECISION NOT VERIFIED.**
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

---

# SCRUM-11101-A Feasibility Gate

Date: 2026-09-07. Branch `master`, starting clean at `d8d1e1c`. Accepted configuration
`Adapters.Mode = Production`, preset `printflow-workstation-v1` **1.16.0**, signed `.atn` not
re-recorded and invoked only where it belongs. No accepted history was rewritten, no branch or
worktree was created and nothing was pushed.

**Verdict: BLOCKED — SCRUM-11101 PDF WHITE-INK DETECTION AUTHORITY MISSING.**

Every Retain and Regenerate primitive this gate set out to test is verified, on the accepted
workstation. The one thing that is not verified — and cannot be, with the current authority — is the
PDF half of the requirement. The original Jira row says "PSD **or PDF** preparation", and PDF
preparation today cannot see a white-ink separation at all.

SCRUM-11101 coverage is unchanged: still **PARTIAL**. No operator can see a Retain/Regenerate choice.

## A. PDF

**Can the current PDF authority detect spot/white-ink structure: NO.**

This is not "hard" or "unreliable". There is no such information at the boundary.

### Evidence

The whole public surface of `Windows.Data.Pdf` is five types:

| Type | What it can report |
|---|---|
| `PdfDocument` | page count, password-protected, get a page |
| `PdfPage` | index, size, rotation, preferred zoom, render to a stream |
| `PdfPageDimensions` | MediaBox, CropBox, BleedBox, ArtBox, TrimBox |
| `PdfPageRenderOptions` | destination pixels, background colour, encoder, source rect |
| `PdfPageRotation` | four rotation values |

Not one member mentions a colourant, separation, colour space, ink, plate, channel or page
resource. `RenderToStreamAsync` is the only route to page content and it returns composited BGRA8
pixels. Whatever the source said about inks is resolved by the rasteriser and gone.

`ExistingWhiteInkPdfDetectionGateTests` pins this three ways:

1. **The surface itself.** The five type names are asserted exactly and a list of forbidden member
   substrings is checked. A future Windows SDK that closes this gap fails the test, which is the
   only automatic signal we would get.
2. **A spot PDF is prepared successfully today.** Synthetic single-page fixtures paint their artwork
   through `[/Separation /W1 /DeviceCMYK]` and through `[/DeviceN [/W1] /DeviceCMYK]`
   (`SpotColourantPdfFixtures`, written byte by byte in C# so the colourant name and tint transform
   are legible as evidence). Both are accepted by the accepted path, rendered, and turned into a
   prepared Revision. The spot really is honoured — the painted rectangle reaches the PNG.
3. **Spot and spot-free are indistinguishable.** The `PdfInspection` persisted for either fixture is
   **record-equal** to the one persisted for a `/DeviceRGB` control of identical geometry.

So the warning in §5 of the task is exactly right, and the situation is worse than a missing
feature: today a customer PDF carrying an existing white-ink separation is **silently rasterised**
and proceeds as an ordinary job. There is no refusal, because there is no observation to refuse on.

### What is missing, and what it would take

To satisfy the PDF half of the Jira row, PrintFlow must be able to answer, before rasterisation:

* does any page resource declare a `Separation` or `DeviceN` colour space;
* what are the colourant names, so `W1` can be matched against the production contract;
* which page each is associated with;
* is the colourant actually used by the content stream, or merely declared.

None of these is derivable from a rendered raster. Recovering them needs the PDF object graph —
cross-reference table or stream, object streams, page tree, `/Resources /ColorSpace`, and the
content stream's colour operators.

**A regex or byte scan for `/Separation`, `/DeviceN` or `/ColorSpace` is not an acceptable
substitute and was not attempted.** The repository's own `compressed-two.pdf` fixture exists because
that class of shortcut is already known to be wrong here: it stores its page dictionaries inside a
compressed object stream and plants a `/Type /Page` decoy in a content stream. A text scan
miscounts it. The same file shape would hide a `/Separation` from a scan, and an unrelated string in
a compressed stream would invent one.

Two honest options for the implementation slice, neither of which belongs in this gate:

* **A narrow structural inspector.** Feasible but genuinely not tiny: it must parse both classic
  cross-reference tables and cross-reference streams, object streams, `FlateDecode`, the page tree
  and resource dictionaries — on adversarial input, since the input is customer artwork. It would be
  a second PDF authority in a codebase whose current invariant is "Windows is the sole PDF
  authority; WIC independently validates only its PNG output".
* **A maintained library.** Removes the parser risk and adds a third-party dependency to an offline
  installer product that currently ships none for this purpose, with the supply-chain and update
  obligations that implies.

Until one is chosen, the safe interim behaviour is to **refuse** a PDF whose spot content cannot be
ruled out, rather than to keep rasterising it silently. That is a Product change and is deliberately
not made here: §17 forbids leaving Product half-wired, and refusing every PDF outright would be a
regression for the many that carry no spot at all. It is recorded as the first decision the
implementation slice must take.

## B. Retain

All four questions answered on the accepted workstation. `ExistingWhiteInkRetainPrimitiveProbe`
(`PRINTFLOW_RETAIN_PRIMITIVE_PROBE=1`, `PRINTFLOW_RETAIN_TIFF_SMOKE=1`).

The fixture matters. `SpotChannelPsdFixtures.QuadrantW1Psd` is RGB/8 with exactly one `W1` spot
channel carrying an asymmetric four-quadrant ink pattern — **0 / 96 / 192 / 255**, one quadrant with
no ink at all. "W1 still exists" would also be true of a regenerated or a shifted channel; this
pattern separates retention from regeneration, loss and misregistration.

| Question | Result |
|---|---|
| Spot survives `changeMode(ChangeMode.CMYK)` | **PASS** |
| Spot survives the accepted 300-PPI sizing | **PASS** |
| Saver accepts a truthful Retained provenance | **PASS** |
| Retained TIFF validates | **PASS** |

### Observed

```text
opened               400x300 @72  RGB/8   4 channels  W1 SPOTCOLOR   90000 ink px (75.00%)
changeMode(CMYK)     400x300 @72  CMYK/8  5 channels  W1 SPOTCOLOR   90000 ink px (75.00%)
resizeImage(300 PPI) 591x443 @300 CMYK/8  5 channels  W1 SPOTCOLOR  196618 ink px (75.10%)
```

Quadrant densities read exactly **0 / 96 / 192 / 255 at all three stages**, including after
resampling. The retained channel is transformed *with* the canvas, not merely preserved beside it.
The mode change altered no pixel of it at all.

### The CMYK contract (§9)

Not accepted merely because both commands say "CMYK". The converted document reports its profile as
**`Coated FOGRA39 (ISO 12647-2:2004)`**, which is exactly the preset's `cmykWorkingSpace`, reached
through `图像 > 模式 > CMYK 颜色` with `convertToProfileCommandUsed: false`. The probe asserts the
observed profile against the preset value rather than against a literal. No divergent
colour-management path appeared, so no preset change is proposed.

### The saver boundary (§11)

`GuardedPhotoshopTiffSaver` took a `PhotoshopW1PreparedDocument` whose `Branch`, `ActionSetName`,
`ActionName` and `ActionInvocationOccurredExactlyOnce` were true of every document the Product could
produce — so the type asserted that *all* valid W1 comes from the signed Action. SCRUM-11101 makes
that false.

Those four members are now one `Provenance` member of a closed hierarchy:

```text
PhotoshopWhiteInkProvenance
    Generated(branch, action set, action name)
    Retained(carrier path, carrier hash)
```

A private base constructor closes it. This is not an alias or a permissive base class: the
Action-specific facts still exist, confined to the case that can truthfully claim them, and an
architecture test asserts the prepared document no longer carries any of them directly.

The saver's guard changed from "the Action ran exactly once" to "the white ink has a positively
established origin", checked per case — a `Retained` naming no carrier and a `Generated` naming no
action are both refused before the single native save call. Nothing else moved, because nothing else
was ever about the Action: `PhotoshopTiffNativeBridge` and `ProductionTiffInspector` are purely
structural and mention it nowhere. Blast radius was six files, all in `Adapters/Photoshop`; no
Workflow, App or Domain source references the type.

`Retained` deliberately carries no operator decision, authority record or classification contract.
Those belong to the slice that introduces the decision itself; what this case asserts today is only
that the W1 arrived on a named managed carrier, which is what the Retain TIFF smoke needs and no
more.

### The retained TIFF

Produced from the retained carrier with the signed Action **not invoked**, and validated by the
existing inspector unchanged:

```text
591x443 @ 300x300 DPI, 5 samples of 8 bits, photometric 5
ExtraSamples [0]              one production ink, not alpha
ExtraChannelNames [W1]        Photoshop spot channel, ImageSourceData present
W1 non-white samples 196618   exactly the count Photoshop was holding
accepted PhotoshopProductionTiffSaveSettings
```

Coverage in the file is **75.10%** — recognisably the retained quadrant pattern, since one quadrant
carries no ink. A regenerated underbase covers essentially the whole canvas, as section C shows.

The managed carrier is byte-identical afterwards, exactly one file was added, the document closed
cleanly through the accepted seam and Photoshop ended clean.

### Two findings the implementation slice must carry

1. **`GuardedPhotoshopDocumentPreparer` refuses a retained W1 by design.** It requires RGB/8 with
   three component channels and rejects outright if `before.W1Exists || after.W1Exists ||
   HasSpot(...)`. The *primitive* underneath it preserves the spot perfectly — that is Gate C's
   result — but the accepted guarded sizing operation cannot be pointed at a Retain carrier as it
   stands. It needs the same kind of truthful generalisation the saver just received. This is
   scoped, small and understood; it is not a blocker, but it is a second boundary and B2 must not
   assume the saver was the only one.
2. **A Retain carrier must promote its Background layer**, exactly as the accepted path does before
   its Action. Without it the TIFF is written but the inspector rightly refuses it: required tag
   **37724** (`ImageSourceData`) is missing, because a background-only document saves no layer data.
   This cost one failed run to discover and is precisely the kind of thing this gate existed for.

## C. Regenerate

`ExistingWhiteInkRegenerateReuseProbe` (`PRINTFLOW_REGENERATE_REUSE_PROBE=1`).

| Question | Result |
|---|---|
| Supported-W1 removal on a managed copy feasible | **PASS** |
| Existing Action path reusable unchanged | **YES** |

Removing the one supported W1 leaves exactly the state the accepted path already demands:

```text
before : RGB/8 | components=3 | spots=1 | w1=yes | 400x300
after  : RGB/8 | components=3 | spots=0 | w1=no  | 400x300
```

The whole accepted chain then ran with **no modification at all**:

```text
PrepareDocumentAsync    -> 400x300 @ 300 PPI, DocumentMode.RGB
ExecuteW1Async          -> signed Action  PrintFlow DTF / W1_1px
SaveProductionTiffAsync -> 400x300 @ 300 DPI, 5 samples, W1 spot channel
```

The generated underbase covers **100.00%** of the canvas against the retained carrier's 75.10%, so
the two branches are distinguishable in the produced TIFF rather than only in intent.

This is the structural reason Regenerate is the branch to build first, and it is now demonstrated
rather than argued: the accepted preparer and W1 executor both already require RGB/8, three
component channels and no spot, which is precisely what removal produces. **No other accepted output
logic needs redesign.**

### The removal primitive (§15)

Implemented as a fixed program inside the probe, not as Product source — this is a feasibility gate,
§17 requires ordinary Product behaviour to stay the safe refusal, and nothing here is reachable from
`SessionService`.

It operates only on a PrintFlow-owned managed copy, re-proves the document's absolute path before
touching anything, and applies the closed classification: RGB/8, exactly three component channels,
exactly one non-component channel, that channel a `SPOTCOLOR` named exactly `W1`, content non-empty.
It then positively verifies the post-condition itself, so a removal that left an unexpected shape
cannot report success.

Refusals were proven live, not merely written. Against a two-spot carrier:

```text
REFUSED — Exactly one non-component channel is required; found 2.
          Ambiguous ink cannot be classified.
before  : RGB/8 | components=3 | spots=2 | w1=yes | 400x300
```

and the document was left untouched. A mask named `W1`, several candidates, an empty `W1` and any
unknown spot fall out of the same contract as refusals. Ink identity stays a production-preset
concern; nothing is guessed.

## D. Baseline

`PhotoshopPsdBoundaryTests.Production_psd_path_enforces_real_guards_and_independent_validation(variant: "malformed")`
remains the known load-sensitive case and is **not** an independently reproducible defect.

Across five targeted-matrix runs in this slice it failed **once** — on the first run after the
provenance change, when the build had just completed and the machine was busiest — with the same
signature the baseline records (expected `PsdPreparationFailed`, observed `OutputUnreadable`). The
three most recent consecutive runs were **517 / 517** each, including that case, and the class passes
**12 / 12** in isolation.

Per §19 it was not fixed and not weakened: no sleep was added, no settle timeout was widened, and
the evidence is preserved rather than tidied away. The baseline is still not deterministically green
and is not described as such.

## E. Testing performed

Targeted only, per §18. The complete 10,866-test suite was **not** run; this slice's Production
change is confined to `Adapters/Photoshop`, and the broader Photoshop/W1/TIFF/PSD/PDF matrix is the
proportionate scope.

```text
Photoshop | Tiff | W1 | Psd | WhiteInk | Pdf      517 passed / 517   (x3 consecutive)
ExistingWhiteInkPreFixReproductionTests             2 passed / 2
Live: retain primitive probe (Gates B, C)           PASS
Live: retained TIFF smoke (Gate E)                  PASS
Live: regenerate reuse probe (Gate F)               PASS
Live: ambiguous-spot refusal (§15)                  PASS
```

`dotnet build PrintFlowStudio.sln -c Debug` succeeds with 0 warnings and 0 errors.

## F. Product behaviour is unchanged (§17)

`ExistingWhiteInkPreFixReproductionTests` still passes unmodified. A PSD carrying an existing W1 is
still refused with `FailureCode.PsdUnsupported`, still lands in `StepState.Failed`, still creates no
prepared Revision, and the closed `CommandKind` vocabulary still contains no member mentioning a
white-ink decision. No `ExistingWhiteInkDecision` type, authority persistence, migration 0013,
Session control, localisation string or AutomationId was added. **No operator can see a
Retain/Regenerate choice.**

Two Photoshop documents left open by failed probe runs were closed without saving, addressed by
absolute path, leaving the operator's own unrelated document untouched.

## G. Commits

```text
b0fe7e3  test: prove PDF preparation cannot observe an existing white-ink spot
2b689f0  test: prove an existing W1 survives the CMYK and 300-PPI primitives
0b3c81b  feat: model white-ink provenance instead of assuming the Action produced it
ac93c67  test: produce a validated production TIFF from a retained-W1 carrier
54ccb5b  test: prove Regenerate reuses the accepted output path unchanged
```

Nothing pushed. `18d20c5` and `d8d1e1c` are intact.

## H. Recommended next order

Unchanged from the task's proposal, with one insertion forced by section A:

```text
B0. Decide the PDF white-ink authority, and close the silent-rasterisation hole
    in the meantime. This is the only remaining blocker, and it gates the PDF
    half of the Jira row; the PSD half is now unblocked.
B1. Regenerate backend + authority persistence
B2. Retain backend + carrier/provenance
      including the GuardedPhotoshopDocumentPreparer generalisation and the
      Background-layer promotion recorded in section B
B3. Operator decision UI + restart/invalidation
B4. Live Retain / Regenerate / ambiguous-spot acceptance
```

B1 was not started; §22 does not authorise it without explicit instruction.

---

# SCRUM-11101 Business Contract Supersession

Date: 2026-09-07. Branch `master`, starting clean at `b549802`. Accepted configuration
`Adapters.Mode = Production`, preset `printflow-workstation-v1` **1.16.0**, signed `.atn` neither
re-recorded nor modified. No accepted history was rewritten, no branch or worktree was created and
nothing was pushed. Every section above this line is preserved unchanged.

**Verdict: PASS — SCRUM-11101 SUPERSEDED BY VISUAL-ONLY INPUT BUSINESS CONTRACT.**

`SCRUM-11101: PARTIAL → SUPERSEDED_BY_DESIGN`. The **P1 blocker is removed by approved
business-design supersession**, not by delivery.

## 1. The original acceptance criterion, preserved

Unchanged from section 1 above, and repeated here so this section stands on its own. CSV Work Item
ID `11408`, *Handle Existing White-Ink Spot Channels Explicitly*, Parent `11400`, Priority High,
three story points:

> When PSD or PDF preparation detects an existing white-ink spot channel, stop and ask the operator
> whether to retain the existing white ink or regenerate white ink using the validated production
> preset. Do not make a silent choice, and keep channel-name, polarity, density, choke and
> colour/white order as production-preset concerns rather than generic workflow assumptions.

The original Jira text was not edited, and it was not wrong when it was written. What follows is an
intentional requirements change: **original requirement → later business clarification → superseded
design.**

## 2. The business clarification

```text
PSD and PDF are visual design inputs only.

Existing spot-colour / white-ink channels in customer source files
are NOT production authority.

PrintFlow does NOT retain source W1.

PrintFlow does NOT ask Retain vs Regenerate.

Production W1 is always generated by PrintFlow
from the final approved visual artwork
immediately before the production TIFF is created for Maintop.
```

The authoritative Product flow is therefore:

```text
PNG / JPEG / PSD / PDF
  → visual artwork input
  → managed visual raster Revision
  → review → trim → print dimensions
  → approved final visual raster
  → Photoshop Production → validated Production Action
  → CMYK + generated W1
  → validated TIFF → Maintop
```

W1 is a **production-output property**, not an input property.

## 3. Why B0 — PDF structural white-ink authority — is cancelled

Section A above established that `Windows.Data.Pdf` cannot report a colourant at all: five public
types, no member naming a colourant, separation, colour space, ink, plate, channel or page
resource, and `RenderToStreamAsync` returning composited BGRA8 pixels. Closing that gap needed
either a hand-written structural PDF parser — cross-reference tables *and* streams, object streams,
`FlateDecode`, the page tree and resource dictionaries, on adversarial customer input — or a
third-party dependency in an offline installer product that ships none for this purpose.

That work existed only to answer a question the Product is no longer asking. Under the visual-only
contract a source separation is a property of the *design*, and PrintFlow has no reason to look for
it before rasterising. So B0 is cancelled rather than scheduled, no `IPdfSpotColourInspector` was
written, and **no dependency was added** — `Directory.Packages.props` is untouched and the
dependency graph is unchanged.

The interim "refuse any PDF whose spot content cannot be ruled out" that section A recorded as the
implementation slice's first decision is also cancelled. Rasterising a spot-carrying PDF is not a
silent hole; it is the contract.

## 4. Why Retain is cancelled

Retain was the branch that kept the customer's own white ink through to the production TIFF. Under
the clarification, source white ink is never production authority, so the branch has no valid
input. Everything the feasibility gate built for it is dead by business definition rather than by
technical failure — sections B and E above record that the primitives all worked.

## 5. Why the Regenerate *choice* is cancelled

Regenerate as a *behaviour* is not cancelled — it is now the only behaviour, and it is not a choice.
There is nothing to ask the operator, because there is no alternative to offer. The accepted output
path already generates W1 from the approved visual artwork, so what the original AC framed as one
branch of a decision is simply what PrintFlow does.

## 6. The new visual-only input contract

| Input | Contract |
|---|---|
| PNG / JPEG | Visual-only. Unchanged. |
| PSD | Visual-only. Immutable Source snapshot → Photoshop-authoritative visual composite → managed raster Revision. Source spot channels, auxiliary alpha masks and W1 channels are observed, rasterised out with every other non-component channel, and never become production authority. |
| PDF | Visual-only. Unchanged: exactly one page, explicit multi-page refusal, `Windows.Data.Pdf` authority, page geometry, rotation, 300 PPI, full-page raster, source preserved, restart/resume, zero-render multi-page guarantee. No structural spot inspection. |

Transparency is **not** affected. It is meaningful visual input and the SCRUM-11099 semantics are
untouched: the managed raster still represents alpha truthfully and nothing is flattened onto white.
Alpha and spot are different things and the change touches only the latter.

## 7. The production W1 invariant, made structural

```text
For every PrintFlow production TIFF that requires white ink:
    W1 origin = PrintFlow validated production path
Never:
    W1 origin = customer PSD
    W1 origin = customer PDF
```

This is now enforced by the type system rather than by a rule. `PhotoshopW1PreparedDocument` is the
only input the TIFF saver accepts, it is constructed in exactly one place —
`GuardedPhotoshopW1Executor.ExecuteW1Async`, immediately after the signed Action — and it carries
`Branch`, `ActionSetName`, `ActionName` and `ActionInvocationOccurredExactlyOnce` as unconditional
members. There is no member capable of describing white ink from anywhere else, so no caller can
introduce one.

`Production_W1_can_only_originate_from_the_validated_Action_path` asserts exactly that, including
that no `Provenance`, `Retain*`, `Carrier*` or `Existing*` member exists and that no
`WhiteInkProvenance` / `ExistingWhiteInk` / `RetainExisting` type name survives in the
Infrastructure assembly.

## 8. Product source cleanup

Every change SCRUM-11101-A made only to support a future Retain branch was audited and classified.

| Change | Class | Disposition |
|---|---|---|
| `PhotoshopWhiteInkProvenance.Retained(carrier path, carrier hash)` | **C — Retain-only, now business-invalid** | Removed |
| `PhotoshopWhiteInkProvenance` closed hierarchy and its `Generated` case | **C — a one-case choice is not a choice** | Removed; the flat members restored |
| `GuardedPhotoshopTiffSaver.HasEstablishedProvenance` | **C** | Removed; the guard is again `!prepared.ActionInvocationOccurredExactlyOnce` |
| Downcasts to `Generated` in `GuardedPhotoshopW1Executor.ResultContext` | **C — safe only because one case existed** | Removed with the type |
| `PsdInspection.HasSpots` / `HasW1` | **A — required, as diagnostics** | Retained, documented as diagnostics that are never authority |

The default preference in the task was to remove dead Retain capability, and nothing here argued
against it. The generalisation did not improve correctness: it made a *true* statement — all valid
W1 comes from the signed Action — expressible as false. Restoring the flat members restores the
invariant as a structural fact, which is precisely what the clarification's production-W1 invariant
asks for. This is the smallest final Product design, not merely a revert.

`GuardedPhotoshopDocumentPreparer` was **not** generalised. Section B above recorded that it refuses
a retained W1 by design and that a Retain implementation would need to change it; with Retain
cancelled, its existing refusal is correct and it is untouched.

Searched and confirmed absent from all four Product projects (`.cs`, `.xaml`, `.resx`, excluding
`bin`/`obj`): `ExistingWhiteInkDecision`, `RetainExistingWhiteInk`, `RegenerateWhiteInk`,
`WhiteInkDecision`, `RetainedWhiteInk`, `IPdfSpotColourInspector`. No schema migration, workflow
command, `CommandKind` member, view model, AutomationId or resource string was ever added for the
decision, so none had to be removed. `PsdInspection` was not extended and `PdfInspection` was not
extended.

## 9. PSD behaviour, before and after

The refusal was made in three places acting in concert, and all three are now gone:

| Location | Before | After |
|---|---|---|
| `PhotoshopPsdProgram.Create` (fixed JSX) | Returned *"Existing W1/spot channels require an operator decision."* before touching the document, whenever any channel was `SPOTCOLOR` or was named `W1` | Channel names and kinds are still enumerated and reported; no channel gates preparation |
| `ProductionPhotoshopOutputProcessor.PreparePsdAsync` | `HasSpots` / `HasW1` in both `PsdUnsupported` tests | Removed; colour mode and depth remain |
| `ValidatePsdRaster` and the `SessionService` re-check | Refused a raster whose inspection reported spots or W1 | Removed from both |

A PSD is now refused for the same reasons it always was, and for no others:

* corrupt or unreadable PSD;
* missing required compatible composite;
* unsupported document colour mode or depth (SCRUM-11099);
* unknown Photoshop state;
* ambiguous document identity;
* preparation output validation failure;
* unsafe external-app state.

**Visual truthfulness.** The prepared PNG is unchanged by this work, because the JSX already removed
every non-component channel from its disposable duplicate before saving — the refusal sat *in front
of* that code, not inside it. The composite the operator reviews is the RGB component composite,
which is also exactly what Photoshop's own PNG export produces, since PNG has no representation of a
spot channel. Nothing visually meaningful is dropped by this change: the same bytes are produced for
a spot-bearing PSD as before, and the only difference is that they are now produced at all.

That the raster carries no production channel is structural, not asserted: PNG colour type 2 or 6 at
eight bits is RGB, or RGB with alpha, and nothing else. `ValidatePsdRaster` already pinned that
header, and it is now documented as the reason source ink cannot travel downstream.

**Spot semantics are ignored, deliberately and explicitly.** A `SPOTCOLOR` channel is never on its
own a reason to refuse, whatever it is named — `W1`, a Pantone ink or a varnish. The chosen
behaviour is tested by name rather than left implicit.

## 10. PDF behaviour

Unchanged. No source file in the PDF path was modified. `PdfInspection` was not extended. The only
PDF-facing change is operator wording (section 11).

## 11. Operator-facing wording

The smallest surface that prevents misunderstanding — no new screen, and nothing added to screens
where it would be noise. The statement appears exactly where an operator is about to prepare a PSD
or a PDF:

* `Session_PsdPending` and `Session_PdfPending` — the preparation notices, en-US and zh-CN.
* `Failure_PsdUnsupported` — corrected, because it actively stated the superseded rule
  (*"Use RGB 8-bit with no existing spot or white-ink channels. Existing ink channels require an
  operator decision."*). It now names only the refusals that still exist.

en-US: *"PSD files are treated as visual artwork inputs: existing spot-colour or white-ink channels
in the source are not preserved as production channels. PrintFlow generates production white ink
when creating the final TIFF."*

zh-CN: *"PSD 文件仅作为视觉设计稿输入：源文件中已有的专色或白墨通道不会作为生产通道保留。PrintFlow 会在生成最终
TIFF 时统一创建生产白墨通道。"*

The PDF variants say the same about separations. The technical statement of the contract lives here
and in `scrum-11099-psd-input-preparation.md`; the runbook needed no change, because it never
described the refusal.

## 12. Feasibility-only code and tests: removed or retained

| Artefact | Disposition | Why |
|---|---|---|
| `ExistingWhiteInkRetainPrimitiveProbe` (2 tests) | **Removed** | Its Retain TIFF gate was the sole thing forcing `PhotoshopWhiteInkProvenance.Retained` to exist in production. Keeping it would have kept an unnecessary production abstraction alive to serve a business-invalid branch. Its findings — spot survives the CMYK mode change, spot survives the 300-PPI resize, quadrant densities exact at all three stages, retained TIFF validates at 75.10% coverage — are preserved verbatim in section B above. |
| `ExistingWhiteInkRegenerateReuseProbe` (2 tests) | **Removed, superseded by a stronger proof** | It proved the accepted output path was reusable after removing a source W1 — a step in a cancelled design. Its evidence is preserved in section C; what replaced it proves more (section 15). |
| The `Windows.Data.Pdf` public-surface test in `ExistingWhiteInkPdfDetectionGateTests` | **Removed** | It failed the build if a future Windows SDK ever exposed colourant metadata. That was the right alarm while the PDF half of the AC was open; with the requirement cancelled it would only obstruct a harmless Windows API expansion. |
| The rest of that class (3 cases) | **Retained, reframed** | Renamed `VisualOnlyPdfInputContractTests`. The behaviour it pins — a `Separation`/`DeviceN` PDF prepares as an ordinary visual job, and its inspection is record-equal to a `DeviceRGB` control — is no longer a finding about a gap. It is the intended contract, and the exhaustive `PdfInspection` field list now guards the "do not extend PdfInspection with spot fields" rule: a spot-colour field cannot be added without this test being reconsidered. |
| `ExistingWhiteInkPreFixReproductionTests` (2 tests) | **Replaced** | It asserted the refusal, and asserted the absence of a decision *as a gap expected to close*. Under the superseding contract the refusal is gone and the absence is permanent, so the assertion survives with its meaning inverted, in `VisualOnlyPsdInputContractTests`. |
| `SpotChannelPsdFixtures`, `SpotColourantPdfFixtures` | **Retained** | Both are load-bearing for the new tests. The quadrant W1 fixture is what makes generated and retained white ink distinguishable at all. |
| `PhotoshopW1RetainedObservationSmoke` | **Untouched** | Unrelated: it observes a retained *B1B document*, not retained white ink. |

## 13. Tests

Deterministic, added:

* `VisualOnlyPsdInputContractTests` (8 cases) — a PSD with an existing W1 prepares into a reviewable
  managed raster; no channel name or kind is on its own a refusal (`W1`/`SPOTCOLOR`, a Pantone spot,
  a varnish spot, a `W1` mask); no retain/regenerate decision exists anywhere; the prepared raster
  carries no production channel; the source is unchanged and restart reuses the same managed raster
  without re-preparing.
* `PhotoshopW1BoundaryTests.Production_W1_can_only_originate_from_the_validated_Action_path` and
  `No_product_source_offers_a_retain_or_regenerate_white_ink_decision` (2 cases).

Deterministic, changed:

* `PhotoshopPsdBoundaryTests` — the `w1` variant now expects success, and asserts the inspection
  still reports `HasW1`/`HasSpots`. The `cmyk` variant is untouched.
* `PsdInspectionTests` — `ValidatePsdRaster` with a `W1`/`SPOTCOLOR` inspection now expects success.
  A `HasRealMergedData = false` refusal was added in its place so the negative case is not lost.
* `PsdPreparationWorkstationSmoke` — the live `w1` variant moved from the refusal branch to the
  success branch.
* `PhotoshopW1BoundaryTests`, `PhotoshopTiffSaveTests`, `PhotoshopW1ExecutionTests`,
  `PhotoshopW1WorkstationSmoke` — reverted to the flat members with the provenance type.

Live, added: `VisualOnlyProductionWhiteInkSmoke` (2 tests, `PRINTFLOW_VISUAL_ONLY_W1_SMOKE=1`).

## 14. Live proof — visual input

`PsdPreparationWorkstationSmoke`, `PRINTFLOW_PSD_PREPARATION_SMOKE=1`, all five variants, on the
accepted workstation through the real Product path (`ServiceRegistration` → `SessionService` →
production adapters):

```text
rgb           SUCCEEDED
transparent   SUCCEEDED
w1            SUCCEEDED     <- was PsdUnsupported
cmyk          REFUSED  PsdUnsupported       "Only RGB 8-bit PSD input is supported."
no-composite  REFUSED  PsdCompositeMissing  (refused before Photoshop was opened)
```

The `w1` run, in full:

```text
VerifiedEnvironmentGate : ALLOWED
Photoshop starting state: KnownStartScreen
Preparation             : SUCCEEDED
Automation lock         : free
Inspection              : OriginalMode=RGB, BitDepth=8, HasRealMergedData=true,
                          HasTransparency=false, PhotoshopVersion=20.0.10,
                          Channels = 红/COMPONENT, 绿/COMPONENT, 蓝/COMPONENT, W1/SPOTCOLOR
                          HasSpots=true, HasW1=true
Adapter                 : photoshop-cc2019-production-v1/psd-preparation-v1
Photoshop final state   : KnownStartScreen
```

Photoshop really did see `W1/SPOTCOLOR` and prepared the raster anyway. Source unchanged, lock free,
Photoshop returned to its starting state, and the two genuine refusals still refuse.

`VisualOnlyProductionWhiteInkSmoke.Psd_carrying_existing_W1_prepares_as_ordinary_visual_artwork`
adds the operator-facing half on the 400×300 quadrant fixture:

```text
managed raster       : 400x300 Png/Rgb
PNG colour type      : 2 at 8 bits — no spot channel exists in PNG
persisted inspection : HasW1=true, HasSpots=true (diagnostics only, no authority)
channels observed    : 红/COMPONENT, 绿/COMPONENT, 蓝/COMPONENT, W1/SPOTCOLOR
step state           : ReviewRequired
available commands   : Approve, Reject, HandOff, SelectWhiteUnderbaseBranch, AbandonSession
source SHA-256       : 05ABCE2883BE6CCEF8C002CDAA6A41DF136E114A084DF1B49742E9B816DBF5C7 (unchanged)
```

No command mentioning Retain or white ink is offered. `SelectWhiteUnderbaseBranch` is the existing
WhiteUnderbase business decision — whether PrintFlow should *generate* an underbase — and is
unrelated to what the input contained; SCRUM-11101 never touched it and neither does this.

## 15. Live proof — production W1

`VisualOnlyProductionWhiteInkSmoke.Production_TIFF_white_ink_is_generated_not_carried_over_from_the_source_psd`.
One synthetic PSD carrying a `W1` spot channel with the asymmetric quadrant pattern
**0 / 96 / 192 / 255** — one quadrant with no ink at all, so the source channel covers exactly
**75.00%** of the canvas. A W1 generated from the opaque composite covers essentially all of it.
"The TIFF has a W1" is true either way; the coverage decides which one it is.

```text
approved raster      : 400x300 Png/Rgb          (real Product PSD preparation)
source W1 coverage   : 75.00%  (quadrant pattern 0/96/192/255)
Photoshop between    : KnownStartScreen
sized for production : 400x300 @ 300 PPI, DocumentMode.RGB
validated Action     : PrintFlow DTF / W1_1px (branch W1_1px), invoked exactly once
TIFF                 : 400x300 @ 300 DPI, samples=5, names=W1, spot=True, nonWhite=120000
TIFF W1 coverage     : 100.00%
source SHA-256       : 05ABCE28…F5C7  (unchanged)
document closed      : cleanly, through the accepted seam
Photoshop after      : KnownStartScreen
```

**100.00% against the source's 75.00%.** The production white ink is PrintFlow's, generated by the
signed Action from the approved visual artwork. The source W1 is not merely absent from the TIFF —
it is absent from every artefact after the immutable Source snapshot.

A second, independent proof is structural: `GuardedPhotoshopW1Executor` refuses outright any
document that already carries a W1 or a spot channel. That the Action ran at all establishes that no
source ink reached the production document.

The TIFF was validated by `ProductionTiffInspector` unchanged — five samples, `ExtraSamples [0]`,
`ExtraChannelNames [W1]`, Photoshop spot channel, CMYK/8, 300 DPI, non-empty fifth sample — and
`PhotoshopProductionTiffSaveSettings.Accepted`.

## 16. Live proof — PDF

No new PDF structural parsing was added and none was run. Targeted regression only,
`PdfPreparationWorkstationSmoke` with `PRINTFLOW_PDF_PREPARATION_SMOKE=1`:

```text
single  1 page   192x96 pt -> 600x300 px @ 300 PPI, 50.80 x 25.40 mm
        PageSelections=1  RenderCalls=1  RevisionCount=2  LockFree  SourceUnchanged  ReviewRequired
two     2 pages  refused PdfMultiplePages
        PageSelections=0  RenderCalls=0  RevisionCount=1  LockFree  SourceUnchanged
```

The zero-render multi-page guarantee holds. Separately, `VisualOnlyPdfInputContractTests` shows
deterministically that the synthetic `/Separation /W1` and `/DeviceN [/W1]` fixtures prepare as
ordinary visual jobs, that the spot really is painted into the raster, and that their persisted
`PdfInspection` is record-equal to a `/DeviceRGB` control of identical geometry. **This is documented
as intended behaviour**, not as a gap.

## 17. Build and full suite

`dotnet build PrintFlowStudio.sln -c Debug` — **0 warnings, 0 errors.**

Targeted, before the full run:

```text
Photoshop | Tiff | W1 | Psd | WhiteInk | Pdf | VisualOnly     518 passed / 518
Localisation | Architecture | Strings | Ui                    807 passed / 807
```

One complete suite against the final source:

```text
Failed: 0, Passed: 10881, Skipped: 0, Total: 10881, Duration: 3 m 19 s
```

The population changed, so the totals are reconciled rather than compared:

```text
10,866   accepted baseline at 4d18930, before any SCRUM-11101 work
+    2   ExistingWhiteInkPreFixReproductionTests          (18d20c5)
+    4   ExistingWhiteInkPdfDetectionGateTests            (b0fe7e3)
+    2   ExistingWhiteInkRetainPrimitiveProbe             (2b689f0, ac93c67)
+    1   PhotoshopW1BoundaryTests provenance test         (0b3c81b)
+    2   ExistingWhiteInkRegenerateReuseProbe             (54ccb5b)
= 10,877  at b549802
-    7   removed here (2 pre-fix + 2 retain probe + 2 regenerate probe + 1 PDF surface test)
-    1   the reverted provenance boundary test
+   12   added here (8 VisualOnlyPsdInputContractTests, 2 smoke, 2 architecture)
= 10,881  measured
```

**The known load-sensitive flake did not occur in the full suite, but it did occur once
afterwards — on a different variant than previously recorded.**

In the complete suite above, and in every targeted run of this slice,
`PhotoshopPsdBoundaryTests.Production_psd_path_enforces_real_guards_and_independent_validation(variant: "malformed")`
passed. That is **not** a claim that it is fixed.

After the final commits, one confirmation run of a narrow filter
(`PhotoshopW1BoundaryTests | VisualOnly | PsdInspection | PhotoshopPsdBoundary`) failed **1 of 43**:

```text
失败  PhotoshopPsdBoundaryTests
      .Production_psd_path_enforces_real_guards_and_independent_validation(variant: "success", expected: null)  [77 ms]
```

That is the **`success`** variant, not `malformed`. It is the first time this case has been observed
failing, and it is recorded here rather than tidied away.

**Reproducibility: no.** Immediately re-run:

```text
PhotoshopPsdBoundaryTests in isolation                     12 passed / 12
the exact failing filter, three consecutive runs           43 / 43,  43 / 43,  43 / 43
```

The one failure came on the run that immediately followed a `dotnet build`, when the machine was
busiest — the same circumstance section D records for the `malformed` observation.

**Assessment.** This is the same defect, not a second one, and the variant is incidental. Both cases
run the same bounded settle poll in `PreparePsdAsync`, which requires two equal independent reads of
the output before it will accept the file; these boundary tests configure `TiffSettleTimeout` at
40 ms, so under load the poll can expire before two equal reads are obtained. The variant only
decides which failure code the expiry surfaces. The earlier characterisation — "the malformed case
races the bounded settle poll" — was therefore narrower than the truth: **any** variant of this test
class can lose that race, and the accepted baseline should be read as such.

Per the correction's own constraint, nothing was done about it here: no sleep was added, no settle
timeout was widened, and no test was weakened. It is not a regression introduced by this slice — the
settle poll and its 40 ms test timeout are untouched, and the `success` variant's assertions are the
ones it has always had. The baseline is still not deterministically green and is not described as
such.

## 18. Jira coverage delta

Recorded as a clearly-labelled delta section (`## H`) appended to
`docs/printflow/original-jira-functional-coverage-reaudit.md`. Nothing above that section was edited;
the original SCRUM-11101 row is preserved exactly as the 4 September audit wrote it.

```text
SCRUM-11101   previous audit: PARTIAL
              current:        SUPERSEDED_BY_DESIGN
```

Reason recorded there:

> Original Jira assumed input PSD/PDF white-ink channels could be production authority requiring
> Retain/Regenerate. Business production contract now explicitly defines PSD/PDF as visual-only
> inputs. Source white-ink/spot channels are not retained as production channels. Production W1 is
> always generated by PrintFlow immediately before final TIFF generation for Maintop.

Deliberately **not FULL**: the original Retain/Regenerate acceptance criterion was not implemented,
it was superseded. `P1 blocker removed by approved business-design supersession.`

Unchanged by this slice:

* **SCRUM-11132 — PARTIAL.** PSD input prerequisite FULL, PDF input prerequisite FULL,
  existing-white-input decision not required by the current production contract. Still pending: the
  actual fixed-workstation multi-format E2E acceptance. Not marked FULL.
* **SCRUM-11104 — unchanged.** Still owns the CMYK preview, the W1 preview, the colour-plus-white
  overlay and the final-TIFF review metadata gaps. Not closed here.

`docs/printflow/scrum-11099-psd-input-preparation.md` carries a short amendment note naming the one
superseded row in its support-contract table. Its historical record is otherwise untouched.

## 19. Git state

New local commits on `master`. Nothing amended, rebased or pushed; no branch, worktree or alternate
clone was created; unrelated untracked files were left alone. `18d20c5`, `d8d1e1c`, `b0fe7e3`,
`2b689f0`, `0b3c81b`, `ac93c67`, `54ccb5b` and `b549802` are all intact.
