# Epic 11400 Part B1B — Accepted CMYK + W1 Action Execution and Factual Validation

**Verdict: 11400-B1B PASS WITH NOTES — READY FOR TIFF SAVE AND OUTPUT VALIDATION**

Part B1B adds one closed Infrastructure-only operation that consumes an already accepted B1A.3
`PhotoshopPreparedDocument` plus `WhiteUnderbaseBranch`, invokes the exact canonical Photoshop
Action once, and accepts success only after factual CMYK/channel/geometry/backing-file validation.
It does not save, write TIFF, return `AdapterOutput`/`PrintOutput`, create a Revision, complete the
PhotoshopOutput workflow step, or enable global Production mode.

## 1. Scope change from historical B1

Historical B1 stopped because every accepted W1 Action begins with Convert Mode to CMYK while the
old slice prohibited a mode transition. B1B explicitly accepts that canonical transformation:

```text
prepared RGB/8 + final pixels + 300 PPI + no W1
  -> exact accepted Action once
  -> CMYK/8 + four process components + exactly one non-empty W1 spot channel
```

CMYK conversion remains owned by the Action. No separate conversion was added. B1B calls no
resize operation and performs no corrective resize or mode normalization.

## 2. Preflight and B1A.3 checkpoint

Initial visual preflight found Photoshop CC 2019 not running: zero documents and no operator
documents. No cleanup action was taken. After the first controlled refusal, the one known
synthetic document was closed manually with **Don't Save** before the final matrix. The accepted
process then remained running with zero documents.

Normal commits `d960a03` and `bfc894e` are present and unchanged. The task began on `master` at
`bfc894e`, ahead of `origin/master` by 28 commits, with a clean working tree.

The initial normal build used the repository-pinned per-user SDK at
`C:\Users\admin\AppData\Local\Microsoft\dotnet\dotnet.exe` and passed with **0 warnings, 0
errors**. The machine-wide muxer exposes only SDK 8.0.418; `global.json` was not changed.

## 3. Canonical `.atn` verification

The canonical artifact remained exact and was never repaired, reloaded, or modified:

| Fact | Accepted/observed |
| --- | --- |
| Path | `D:\PrintFlowStudio\Baseline\workstation-v1\actions\authoring\PrintFlow-DTF-v1.atn` |
| Bytes | 1,636 |
| SHA-256 | `A04203EDEA623C0737D911601A3A005033789BD095130F02A5F8C04CBFCD83EE` |
| Set | `PrintFlow DTF` |
| Actions | `W1_0px`, `W1_1px`, `W1_2px` |

The production operation hashes the artifact before native Action input and fails closed on a
missing or mismatched file.

## 4. Runtime set/action integrity

Immediately before each invocation the fixed native program enumerates loaded Action sets,
actions, and commands by index. It requires exactly one exact `PrintFlow DTF` set, exactly three
actions, exactly one whole-string match for each accepted action, and the exact transcripts:

| Action | Runtime command transcript |
| --- | --- |
| `W1_0px` | `转换模式`; `设置 选区`; `建立` |
| `W1_1px` | `转换模式`; `设置 选区`; `收缩`; `建立` |
| `W1_2px` | `转换模式`; `设置 选区`; `收缩`; `建立` |

No substring, prefix, nearest-name, duplicate, missing-action, unexpected-action-count, or
transcript fallback is accepted. The `.atn` hash remains parameter authority; runtime enumeration
corroborates the loaded palette and does not claim complete descriptor byte equality.

## 5. Closed branch mapping and invocation route

`IPhotoshopW1Automation.ExecuteW1Async` accepts the opened/prepared factual records, one typed
`WhiteUnderbaseBranch`, and cancellation. It accepts no set string, action string, script,
descriptor, or output path.

| Typed branch | Exact Action |
| --- | --- |
| `WhiteUnderbaseBranch.W1_0px` | `W1_0px` |
| `WhiteUnderbaseBranch.W1_1px` | `W1_1px` |
| `WhiteUnderbaseBranch.W1_2px` | `W1_2px` |

The fixed internal route attaches through the already-running `Photoshop.Application.130` object,
requires its reported application directory to match the independently accepted executable, and
calls `app.doAction(exactActionName, exactSetName)` once. The invocation counter is advanced at
the input boundary, before the synchronous call, so a throw/partial return is retained unknown
state and can never be misreported as zero input. There is no retry or fallback.

## 6. Prepared-document preconditions and exact identity

Before Action input B1B requires:

- accepted executable hash/version and exactly one accepted PID/start-time identity;
- the same accepted main-window handle, ownership, class, and no titled blocking modal;
- recognised editor title for the expected managed filename;
- the Part A absolute-path identity probe matching the exact managed Working path;
- prepared actual pixels greater than zero and unchanged from B1A.3 authority;
- exactly 300 PPI, `DocumentMode.RGB`, `BitsPerChannelType.EIGHT`;
- exactly three component channels and no W1/non-component channel;
- current backing SHA equal to `PhotoshopPreparedDocument.BackingWorkingSha256`;
- canonical artifact and runtime Action integrity.

Wrong geometry, resolution, mode, bit depth, path, document, channel state, artifact, runtime set,
action, or transcript fails before `doAction`. B1B never selects a document by index.

## 7. CMYK transition and geometry preservation

Every final live case factually returned:

```text
before: DocumentMode.RGB / BitsPerChannelType.EIGHT
after:  DocumentMode.CMYK / BitsPerChannelType.EIGHT
```

All remained exactly 600×400 pixels at 300 PPI and
50.8×33.8666666666667 mm. Production validation compares pixels, resolution, physical
representation, path, and bit depth before/after and performs no correction when any differs.

## 8. Post-Action identity route

One controlled development run established a factual representation boundary. The Action ran,
then the Part A Save As identity probe could not reproduce the original `.png` filename because a
CMYK document with a spot channel is not representable as PNG and Photoshop proposed a compatible
save format. The run was not accepted; the retained synthetic was manually discarded without
saving.

The final route keeps the full Part A absolute-path probe immediately before Action input. After
the Action it reverifies process, main window, recognised expected-title state, and absence of a
modal, then requires the fixed native result's read-only
`app.activeDocument.fullName.fsName` to equal the managed Working path exactly. This is the loaded
document's factual original path and does not invoke Save or Save As for output.

## 9. CMYK process-channel validation

The retained read-only UTF-8 observation recorded Photoshop CC 2019's factual channel
representation:

| Index | Localized name | DOM kind | Direct COM kind |
| --- | --- | --- | --- |
| 1 | `青色` | `ChannelType.COMPONENT` | 1 |
| 2 | `洋红` | `ChannelType.COMPONENT` | 1 |
| 3 | `黄色` | `ChannelType.COMPONENT` | 1 |
| 4 | `黑色` | `ChannelType.COMPONENT` | 1 |

Production authority is four `ChannelType.COMPONENT` values in `DocumentMode.CMYK`; localized
English names are not invented or hard-coded. W1 must not occur in `componentChannels`.

## 10. W1 spot-channel and non-empty validation

After Action completion the all-channel collection must contain exactly five channels: the four
components above plus exactly one exact `W1`. W1 must report `ChannelType.SPOTCOLOR` (direct COM
kind 4), not masked/selected alpha, layer, selection, or component.

The fixed read-only validation reads W1's histogram and sums bins 0 through 254. A positive total
is required for the accepted spot-channel polarity and controlled fixtures; existence alone is
not accepted. All live branches returned a positive diagnostic count of 240,000. The retained
observation recorded 100% solidity and preview RGB 255,0,0, agreeing with the accepted Action
contract. Production acceptance relies on name, kind, uniqueness, and non-empty content; it does
not convert an unrecorded display default into new authority.

## 11. Branch-specific result

The exact Action invoked is branch authority. The full-canvas controlled fixture returned the
same histogram diagnostic for all three Actions. No claim is made that 2 px must always contain
fewer W1 samples than 1 px, and no content-quality classifier was added.

## 12. Backing-file and no-output proof

The controlled fixture backing SHA was identical before and after every branch:

`A2B882C373C88CBE1F8A3AD129871B18B921E7EAD0EECE00F4EE7BCFB7EAB4A5`

The top-level managed directory file set was also identical before/after each call. No Save, Save
As for output, TIFF, PSD/PSB/PNG derivative, `AdapterOutput`, `PrintOutput`, ReviewRequired,
Revision, or workflow completion occurred. `IPhotoshopOutputProcessor.GenerateAsync` remains
fail-closed until Part C TIFF save and output validation exist.

## 13. Cancellation, target loss, modal, and ambiguous return

- Cancellation observed before native input returns structured Cancelled with zero Action input.
- Cancellation after the synchronous call begins completes safe factual observation where
  possible, records retained CMYK/W1 state, saves nothing, and runs no later operation.
- Process/window/document loss after input cannot become success and triggers no second Action.
- A post-Action modal is recorded and not dismissed, clicked through, escaped, or confirmed.
- A throwing/partial Action is one ambiguous invocation with retained state, never a retryable
  precondition and never retried.

No process is terminated and no rollback is claimed.

## 14. Shared-process safety

The first final case started with one document (the active managed synthetic) and no unrelated
documents. The second and third cases observed counts two and three because earlier successful
synthetics were deliberately retained. Counts remained unchanged across each Action. The adapter
records `OtherDocumentsMayBeOpen`, never enumerates operator document paths, never activates a
document by index, never closes all documents or Photoshop, and never mutates the Action palette.

## 15. Cleanup

The three successful final documents remain open, modified in memory, and unsaved:

- `PF_B1B_W1_0px_35A80BE6A740.png`
- `PF_B1B_W1_1px_DAF39590576F.png`
- `PF_B1B_W1_2px_76FA10887F5F.png`

Their retained workspace root is
`C:\Users\admin\AppData\Local\Temp\PrintFlowPhotoshopW1Smoke\1e72215e6f294fd7933ae42239fe8f68`.
It must not be deleted while the documents remain open. Cleanup is operator-owned: close only
these known synthetics and choose **Don't Save**. Photoshop itself was left running.

## 16. W1_0px live result

`W1_0px` ran once on a fresh prepared document. RGB/8 became CMYK/8; 600×400 pixels, 300 PPI and
physical geometry were unchanged; four component channels plus one non-empty W1 spot channel were
validated; backing SHA and directory file set were unchanged; no output was written. PASS.

## 17. W1_1px live result

`W1_1px` ran once on a separate fresh prepared document with the same required factual outcome
and invariants. The earlier W1_0px synthetic remained open and untouched. PASS.

## 18. W1_2px live result

`W1_2px` ran once on a third fresh prepared document with the same required factual outcome and
invariants. Both earlier synthetic documents remained open and untouched. PASS.

## 19. Targeted tests and build

No full-suite run was performed. Final focused command selected W1 contract/invocation/result,
B1A.3 preparation, Part A foundation/driver/shared-process, Photoshop boundary, general automation
boundary, production adapter gate, and workstation preset tests.

| Gate | Result |
| --- | --- |
| Final focused automated suite | **179 passed, 0 failed, 0 skipped** |
| Final three-branch workstation smoke | **1 passed** (all three branches) |
| Retained read-only channel observation | **1 passed** |
| Final normal solution build | **0 warnings, 0 errors** |

The complete 9,000+ suite remains deferred to Epic 11400 Final QA, as required.

## 20. Evidence and preset v1.13.0

New immutable external evidence:

`D:\PrintFlowStudio\Baseline\workstation-v1\apps\photoshop-2019\cmyk-w1-action-runtime.json`

SHA-256:
`E1738B4FF54CC1602C1052615A04B94C06CE06C0C0F423BFCB505DE270ECDB94`

New immutable preset:

`D:\PrintFlowStudio\Baseline\workstation-v1\preset\printflow-workstation-v1.13.0.json`

SHA-256:
`67525D6E9BF6A60438BC530B9E41FDFE65919473061A5D28772377211923A7CD`

All 24 inherited integrity entries and the new entry rehashed exactly: 25/25, zero mismatch. Both
new files are read-only. v1.12.0 remains read-only and exact. `appsettings.json` selects v1.13.0
by the exact digest and `Adapters.Mode` remains `Fake`.

## 21. Architecture and dependency result

`WhiteUnderbaseBranch` is the only caller-selected W1 input. Generic Action names, sets, scripts,
ExtendScript, descriptors, save paths, resize, TIFF, process termination, `AdapterOutput`, and
Revision capabilities do not enter Workflow/App. The fixed native bridge and factual result are
Infrastructure-only. ViewModels still contain no `System.IO`.

No dependency or lock file changed. Dependency graph unchanged; vulnerability audit deferred to
Epic 11400 Final QA.

## 22. Remaining Part C and Git state

Part C still owns TIFF Save As, TIFF structure/on-disk validation, `AdapterOutput`, workflow
PhotoshopOutput completion, ReviewRequired behavior, and Revision creation. B1B alone cannot
declare workflow output success.

At report preparation, `master` remained based on accepted `bfc894e`, ahead of `origin/master` by
28 commits. Repository changes are limited to the B1B Infrastructure seam, focused tests/smokes,
configured v1.13.0 pointer, and this report. External evidence/preset files, retained synthetic
documents, runtime workspaces, screenshots, smoke transcripts, and observations are not committed.
No amend, rebase, history rewrite, or push was performed.

11400-B1B PASS WITH NOTES — READY FOR TIFF SAVE AND OUTPUT VALIDATION
