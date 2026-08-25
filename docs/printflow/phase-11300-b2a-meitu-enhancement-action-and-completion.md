# PrintFlow Studio — Epic 11300 Part B2A: Meitu Enhancement action and completion

**Report date:** 25 August 2026
**Runtime:** Meitu XiuXiu 7.8.7.5, zh-CN, accepted workstation `DESKTOP-0BG8884`
**Scope:** establish a fresh expected Working copy, locate the real `AI变清晰` action, invoke it under
guarded control, positively observe Busy, positively detect completion, and reconfirm document
identity afterwards.
**Not in scope:** export, transformed output bytes, output validation, image-quality judgement,
workflow success, Revision creation.

---

## 1. Starting identity

Part B1.1 closed with a hazard recorded against it: a synthetic workspace had been deleted while
Meitu still held that already-open image, leaving `PF_IDENTITY_FINAL_FBE193290D1E.png` loaded with no
backing file. §2 forbids enhancing that document, so this slice never used it.

The B2A sequence begins by emptying the editor and opening a new file:

```text
loaded orphan document           → classified Unknown (correctly: PrintFlow did not hand it over)
signed 关闭图片 control invoked   → editor reached KnownEditorEmpty
new synthetic Working copy       → opened through the signed B1 picker path
signed B1.1 Save probe           → exact derived basename read and cancelled
```

Live results across the supervised runs:

| Working copy | Observed Save value | Verdict |
| --- | --- | --- |
| `PF_B2A_A325C57CE6DA.png` | `PF_B2A_A325C57CE6DA_副本` | `KnownEditorWithExpectedWorkingCopy` |
| `PF_B2A_B3D43C6BAC70.png` | `PF_B2A_B3D43C6BAC70_副本` | `KnownEditorWithExpectedWorkingCopy` |
| `PF_B2A_027DF2123E74.png` | `PF_B2A_027DF2123E74_副本` | `KnownEditorWithExpectedWorkingCopy` |

Every Working file and its whole directory tree was created under the OS temp directory and kept on
disk for the entire time Meitu could reference it. No file was deleted underneath Meitu in this
slice.

Closing the orphaned document needed a route that did not exist in B1.1, so one was captured and
signed rather than guessed at — see §2 below. PrintFlow never invokes it on its own initiative.

---

## 2. Enhancement structural target

A read-only control-view walk of the loaded editor found exactly one element named `AI变清晰`, and
the control that acts on it is its **grandparent**, not its parent:

```text
Text     id='…contentStackedWidget.editorContentPage.parameterWidget.stackedWidget
             .ParameterSetWidget.scrollArea.qt_scrollarea_viewport.contentsWidget
             .ModuleButton.buttonWidget.titleLabel'
         name='AI变清晰'  class='QLabel'  patterns=[Invoke]
  parent:
Group    id='…contentsWidget.ModuleButton.buttonWidget'
         name=''  class='QWidget'  patterns=[Invoke, Value]
  parent:
CheckBox id='…contentsWidget.ModuleButton'
         name=''  class='ModuleButton'  patterns=[Invoke, Value, Toggle]
         rect=452,618 260x56  enabled=true  offscreen=false
```

This is why B1's card rule could not be reused. `MeituCardShape` encodes a one-level relationship in
which the marker's id is the owner's id plus the marker's own suffix; the editor tool list nests a
layout `QWidget` in between. Bending the card rule to fit would have meant either walking "up until
something matches" — the unbounded search §8 forbids — or accepting the intermediate `QWidget`, which
advertises `InvokePattern` and does nothing when invoked. That is the Part A defect one level higher.

`MeituOwnedControlShape` + `MeituEnhancementTargetRule` therefore record the depth as evidence and
require, for exactly one candidate:

1. marker name **exactly** `AI变清晰`, control type `Text`, class `QLabel`, id ending `.titleLabel`;
2. an owner at the signed depth of **2** — a walk that stops short is a refusal, never licence to
   climb further;
3. owner control type `CheckBox`, class `ModuleButton`, id ending `.ModuleButton`;
4. `marker.AutomationId == owner.AutomationId + ".buttonWidget.titleLabel"`, which is the tie that
   makes this one structural relation rather than two independent shape checks;
5. both elements in the verified Meitu process;
6. owner exposes `Invoke`, is enabled, is on screen, and its bounds enclose the marker's.

Two accepted candidates and zero accepted candidates are both refusals. No coordinate, sibling index,
mouse primitive, `SendKeys` or shortcut fallback exists anywhere in the route.

**Why the marker must be the anchor.** Every entry in the editor's 高级调整 list reports the
*identical* automation id for both marker and owner. The observed siblings are 消除笔, `AI变清晰`,
无痕改字 and AI换背景, so the id identifies the control only up to a four-way ambiguity that includes
"replace this background". Only the marker's name separates them.

**A name search across Meitu would be worse than useless.** The start page also contains elements
named `AI变清晰`: a start-page card, and the search box's rotating placeholder
`StartupWidget.contentWidget.titleWidget.wSearchBg.leSearch.scrollTextLabel`, whose parent is an
`Edit`/`HomeSearchLineEdit`. Resolution is always rooted beneath the one verified editor window.

### The close-document control

Captured in the same walk, and signed separately, because §2 and §28 both need it:

```text
Text   id='…titleWidget.stackedWidget.editorPage.closeButton.textLabel'
       name='关闭图片'  class='QLabel'  patterns=[Invoke]
  parent:
Button id='…titleWidget.stackedWidget.editorPage.closeButton'
       name=''  class='IconTextButton'  patterns=[Invoke, Value]
```

One level up, so `MeituCardShape` fits it directly. The editor's other `closeButton`
(`MainWindow.centralwidget.captionWidget.closeButton`, class `proui::IconFontButton`) closes the
window rather than the document and is excluded by both the name anchor and the class check.

---

## 3. Evidence version

The accepted v1.0.0, v1.1.0 and v1.2.0 manifests and their sign-offs were **not** edited. Two new
evidence files were captured and a new semantic version was created:

| Artifact | Status / SHA-256 |
| --- | --- |
| `apps\meitu\editor-close-document.json` | `CONFIRMED`; `ED3017BDD8C8FC35879771945977E954097305A6CFB46483B6CF9D74233C2C1D` |
| `apps\meitu\editor-enhancement.json` | `CONFIRMED`; `912CC4D6DFA004DCA7D376DF1F52DBB7AA43395AC3B38B2AC318F0D3AECBB74E` |
| `preset\printflow-workstation-v1.3.0.json` | `ACCEPTED_IMMUTABLE`; `6E064D921F24D9A3ADF82D81FAE71256C2E4548FA289E47FE1A7E9646115FFF7` |
| `signoff\workstation-preset-v1.3.0.json` | `CONFIRMED_FINAL` |

All 12 inherited `sourceManifestIntegrity` digests were re-verified against their files before the
new manifest was written; none was stale. `v1.2.0`'s `editor-with-working-copy.json` was left
byte-identical so that v1.2.0 itself remains verifiable. `appsettings.json` now pins v1.3.0 and its
exact digest. The v1.3.0 manifest and sign-off are read-only on disk.

Nothing in the adapter hard-codes a marker, a class, a depth or a threshold: `MeituBaseline` gained
`CloseDocument` and `Enhancement`, both nullable, and a chain that vouches for neither leaves
`Busy` unreachable and every Enhancement route refused.

---

## 4. Busy signature

Positively evidenced, from live observation only:

```json
"busy": { "requiredMarkers": ["变清晰中", "变清晰时长", "取消"], "minimumRequiredMarkers": 2 }
```

Meitu shows exactly one of two progress messages at any instant, plus an abort affordance for the
whole run:

| Phase | Visible names |
| --- | --- |
| t+0.5 s … t+2.5 s | `变清晰中，请稍候...` + `取消` |
| t+3.0 s … t+7.5 s | `变清晰时长与图片大小、网络速度有关,\n请稍等一会哦...` + `取消` |
| after | neither |

Requiring **two of three** means whichever progress phase is showing supplies its operation-specific
text *plus* the cancel affordance. Neither a generic cancel button nor a stray progress string
satisfies the rule alone, and the signature is never an absence — a signature phrased as "the editor
stopped looking normal" would be satisfied by an automation read that returned nothing.

`Busy` is classified **before every content state** in `MeituStateClassifier`. That ordering is the
point: while Meitu computes, the editor keeps its exact title, its markers and its document, so a
mid-enhancement observation otherwise satisfies `KnownEditorWithExpectedWorkingCopy` completely — and
that state is on the safe-starting-state allow-list. A blocking dialog still outranks `Busy`.

---

## 5. Completion signature

```json
"completion": {
  "requiredMarkers": ["AI超清", "高清", "保持原尺寸", "重置", "批量AI变清晰"],
  "minimumRequiredMarkers": 4,
  "requiresBusyAbsent": true
}
```

The five markers are the controls of `…ModuleButton.centerWidget.AiClarityParameterWidget`, which
exists only while the module is selected and is absent from the editor before the action is invoked.

Completion is therefore a **positive** claim — a panel Meitu created — combined with the positively
observed end of the work. `MeituEnhancementRule` evaluates Busy first and, because
`requiresBusyAbsent` is true, refuses to call an observation complete while the work is running. The
waiting order supplies the rest: `RunEnhancementAsync` will not look for completion until Busy has
been positively seen, so the gap between the invocation and the first progress paint cannot be read
as a finished run.

### Completion signals considered and rejected

| Candidate | Why not |
| --- | --- |
| "the Busy markers disappeared" | An absence. §14 forbids it as a completion test on its own, and it is the observation a crashed panel or a thin automation read would also produce. |
| `undoButton` / `redoButton` / `restoreButton` becoming enabled | Observed **disabled throughout** — before, during and after. Not a completion signal on this build. |
| `resetButton` / `btnBatchClarity` becoming enabled | Observed enabled from the first poll after the invocation, i.e. *during* processing. Enablement does not distinguish running from finished. |
| canvas zoom moving `85% → 21%` | A genuine post-processing signal — the result is upscaled — but its value depends on the image and the window, so it cannot be a signed marker. |
| `对比` (compare) | Present and enabled before the action as well as after. |

No marker that appears *only* after completion exists on this build. That is a finding, not an
omission, and it is why the signature is "the panel the action created, plus Busy gone, reached only
after Busy was seen" rather than a single label.

---

## 6. Post-completion identity

§15 is implemented as the *same* signed route, not a cheaper re-read, because the claim is the same
claim:

```text
completion positively observed
  → re-acquire the editor (activate, verify ownership + foreground, classify)
  → invoke the signed Save owner
  → read the signed filename Value control
  → compare exactly against basename + "_副本" under Windows case semantics
  → invoke the signed Cancel control, confirm the surface closed
  → re-acquire and re-verify the editor again
```

`MeituEnhancementOutcome` carries the two identity snapshots, the Busy observation and the completion
observation — and deliberately has no `OutputPath`, `Succeeded`, `Export` or `Revision` member. An
architecture test asserts that, because the temptation in B2B will be to add an output path here
rather than to a new type, and the moment this record carries one a caller can read a success from it
as "the file exists".

---

## 7. Foreground and modal behaviour

**Foreground.** Meitu displaces its own editor: both the picker closing after an open and the Save
surface closing after Cancel hand the foreground to a short-lived Meitu window rather than back to
the editor. B1.1 hit this twice and refused with nothing sent; the refusals were correct, but
requiring a caller to win a race it cannot see was not. B2A adds `ReacquireForegroundAsync`, which
asks Windows to bring the target forward and then verifies that it got there, within a bounded
budget. A window that will not come forward is still `MeituTargetLost` with nothing sent.

Observed live during this slice: a close attempt was refused because CorelDRAW took the foreground —
`MeituTargetLost`, `inputSent=false`, retried successfully afterwards. Read-only Busy polling
deliberately does **not** require the foreground: the operator may legitimately look at something
else while Meitu computes, and demanding focus in order to observe would be a side effect of its own.

**Modals.** One Meitu-owned modal was observed, and it is a genuine finding for B2B:

```text
title       : Form
Win32 class : Qt51517QWindowToolSaveBits
names       : 温馨提示 | 当前图片已修改，是否保存？ | 保存图片 | 存为作图记录 | 不用了，谢谢
```

Closing a document that Enhancement has modified raises it, and the editor and start page are both
disabled while it is present. PrintFlow classifies it `KnownModal` and stops. It does not read,
answer, dismiss or click any control on it, and it does not delete the Working file, because Meitu
may still hold it. Its structure is recorded in the signed evidence **so the stop can be explained,
not so it can be automated**; no code path in PrintFlow resolves it. It appeared three times
during this slice, always after a close attempt on a document Enhancement had modified, and
PrintFlow stopped every time with the working file left in place.

Late in the slice the operator asked for the prompt to be dismissed without them. That was treated
as an *operator* capability and not a product one: a throwaway UI Automation script outside the
repository, which identifies the dialog by owning process, window class and the exact discard
button, and refuses rather than acts on any ambiguity. It never dismissed anything — the Meitu
window class turned out to be shared by three windows, and on its one run the prompt had already
been cleared, so it reported a refusal. PrintFlow itself is unchanged: it still classifies the
prompt as `KnownModal` and stops, and contains no modal automation.

---

## 8. Tests

New and extended coverage, all against the real classifier, real rules and real guarded driver with
only the operating system faked:

**Structural target (`MeituEnhancementTargetRuleTests`, 20 tests)** — the one accepted shape; the
text marker refused as its own target; the intervening `QWidget` refused *despite* advertising
`Invoke`; two valid owners refused rather than chosen; zero candidates; one valid owner among
several; wrong process on marker and on owner; disabled; offscreen; missing pattern; bounds not
enclosing; owner id not extending to the marker id; owner id lacking the signed suffix; four
non-exact marker names; no readable ancestor at the signed depth; and all failing checks reported
together. Every refusal asserts `inputSent=false`.

**Busy and completion (`MeituEnhancementRuleTests`, 15 tests)** — signed Busy markers read as Busy;
either progress phase alone suffices; ordinary editor content does not; one marker misses the
threshold; **Busy disappearing is not completion**; too few completion markers miss the threshold;
completion markers visible during Busy still read as Busy; overlap honoured only when the evidence
records it; an empty marker list or an unsatisfiable threshold matches *nothing* rather than
everything; and markers in the window title alone do not count.

**Classifier precedence (`MeituStateClassifierTests`, +7)** — Busy outranks the expected working copy
it is running over, the signed empty editor and the signed welcome page; a blocking dialog outranks
Busy; without signed enhancement evidence Busy is unreachable; a finished enhancement is not Busy.

**The guarded route (`GuardedMeituEnhancementTests`, 24 tests)** — the whole sequence once, with each
observation asserted; Save invoked, then Cancel, then the module, in that order; nothing written
anywhere; expected A / observed B produces **zero** invocations; a Save that never presents the
signed surface stops before Enhancement; no signed evidence means not even a Save probe; foreground
loss, changed window owner and process exit each produce zero invocations; a missing control refuses
after a clean probe; an already-selected module is not toggled off; an in-flight Enhancement is not
restarted; a Busy state reports Busy rather than an identity mismatch; an action that never shows Busy
fails within budget; Busy that never ends times out rather than returning an unknown success; Busy
ending with no positive completion evidence is not treated as complete; cancellation mid-observation
sends nothing further; a modal during observation stops without touching it; identity changing after
completion fails the run; the post-completion probe cancels its own surface; and the close route
reaching the empty editor, stopping on a prompt, refusing without signed evidence, and forbidding
deletion when it never reaches the empty editor.

**Preset chain (`PresetMeituBaselineProviderTests`, +12)** — both new routes read from their signed
files; both unreachable when not vouched for; no positive Busy markers refused; no positive
completion markers refused; threshold above the marker count refused; owner depth outside 1–4
refused (0, −1, 20); a missing action field refused; an unstated Busy-overlap rule refused; an
unknown pattern refused; a close route with no marker refused; and editing signed evidence after the
manifest was written failing closed.

**Boundaries (`AutomationBoundaryTests`, +3)** — the Enhancement seam exposes no path, coordinate or
shortcut parameter and its implementation contains no `SendKeys`, mouse or coordinate reference; the
outcome record carries no output, path, revision, success or export member; and the Enhancement and
close routes write no value anywhere.

**Production seam (`ProductionMeituProcessorTests`, +5)** — Enhancement refuses `Source`, `Approved`
and `Rejected` references before touching Meitu; it re-probes identity rather than trusting the
confirmed open; and a failed Enhancement reports its own failure rather than a failed screenshot.
`ProcessAsync` still cannot return success for either operation.

### Gates

```text
dotnet restore --locked-mode                             OK
dotnet build                                             0 warnings, 0 errors
dotnet test                                              6900 passed, 0 failed, 0 skipped
dotnet list package --vulnerable --include-transitive    no vulnerable packages
```

That is 87 tests above the B1.1 baseline of 6813, measured on the final tree with the two
temporary diagnostic harnesses removed.

---

## 9. Live smoke

The mandatory §27 sequence completed in one clean run against the signed v1.3.0 chain, on
`PF_IDENTITY_FINAL_411D1411DDA3.png` — a new unique synthetic Working copy with a live backing file,
created for that run and never reused:

```text
## Phase 1 — identify and classify (read-only, no input)
state                : KnownEditorEmpty
process id           : 20788   window 0x606BA '美图秀秀-图片编辑'

## Phase 2 — open the synthetic working copy (guarded input)
state                : KnownEditorWithExpectedWorkingCopy
expected file        : PF_IDENTITY_FINAL_411D1411DDA3.png
observed identity    : PF_IDENTITY_FINAL_411D1411DDA3_副本

## Phase 3 — guarded Enhancement
signed action shape  : Text/QLabel'.titleLabel' → CheckBox/ModuleButton'.ModuleButton'
                       at depth 2, requiring Invoke
signed busy markers  : 变清晰中, 变清晰时长, 取消 (at least 2)
signed completion    : AI超清, 高清, 保持原尺寸, 重置, 批量AI变清晰 (at least 4, busy absent: True)
resolved action      : CheckBox id='…contentsWidget.ModuleButton' class='ModuleButton' pid=20788
  patterns           : Invoke, Value, Toggle
  bounds / enabled   : 452,507 260x56 / True
identity before      : KnownEditorWithExpectedWorkingCopy (PF_IDENTITY_FINAL_411D1411DDA3_副本)
busy observed        : Busy
completion observed  : KnownEditorWithExpectedWorkingCopy
identity after       : KnownEditorWithExpectedWorkingCopy
editor re-verified   : '美图秀秀-图片编辑' (process 20788)

STOP. No export, no Save, no Save As, no transformed output, no Revision.

## Phase 4 — release the working file (§28)
close                : the editor reached the signed empty state; the file is released.
```

Exactly one guarded Enhancement invocation was produced, through `IUiElementProvider.Invoke` on a
structurally resolved element. No coordinate, mouse event, `SendKeys` or shortcut was involved at any
point in the run.

### Earlier supervised runs

Three further supervised runs produced the Busy and completion evidence this slice is signed from,
each on its own unique synthetic Working copy, each with exactly one guarded invocation, and each
polled read-only at 500 ms afterwards. The observed timeline was reproducible:

```text
t+0.0 s  one guarded invocation of the resolved ModuleButton
t+0.5 s  panel appears (AI超清, 高清, 保持原尺寸, 重置, 批量AI变清晰)
         and processing begins (变清晰中，请稍候... + 取消)
t+3.0 s  progress text becomes 变清晰时长与图片大小、网络速度有关,\n请稍等一会哦...
t+8.0 s  取消 and the progress text disappear together; canvas zoom moves 85% → 21%
```

Those runs also produced the two hazards in §10 and the two defects that were fixed, and one further
correct refusal worth recording: a close attempt stopped with `MeituTargetLost` and `inputSent=false`
because CorelDRAW took the foreground mid-sequence. It succeeded on the next attempt.

### An earlier B2A smoke that correctly refused

The first attempt at the full smoke stopped in Phase 2 with `MeituUnknownState`, and it was right to:
Meitu had auto-started an enhancement when the file was opened, because the module was still
selected from a previous run. The identity probe read the exactly correct derived name and the
classifier still declined, because Busy outranks the document state. That run is what exposed defect
3 in §10 — the failure text blamed the filename. The run above was taken after the module had been
deselected.

### What was not observed

Closing the enhanced document did **not** prompt on the passing run, but *did* prompt on the earlier
observation runs, which used a larger synthetic image. The 温馨提示 save prompt is therefore recorded
as a state that can occur on close rather than one that always does; PrintFlow stops on it either
way. Nothing in this slice measured, compared or judged the enhanced pixels.

---

## 10. Defects and findings

1. **`ModuleButton` is a toggle.** The action owner is a Qt `CheckBox` that Meitu uses as its module
   selector. Invoking it while the module is already selected **deselects** it and discards the
   panel. Observed directly: a run whose invocation landed on an already-selected module turned the
   module off instead of starting work.
2. **Meitu retains the selected module across document loads.** Opening a new image through the
   signed picker while `AI变清晰` was still selected started an enhancement with **no PrintFlow input
   at all**. Both hazards are handled by the same mandatory pre-invoke guard: the signed Busy and
   completion signatures must both fail to match immediately before the single invocation. A Busy
   match means work is already running; a completion match means the module is already selected.
3. **Reporting defect, found by the first live B2A smoke and fixed.**
   `ConfirmWorkingCopyIdentityAsync` reported every non-matching final state as "the signed Save
   value did not exactly identify …". When Meitu had auto-started an enhancement, the probe read the
   exactly correct derived name and the classifier still declined — because Busy outranks the
   document state — so the failure sent the reader looking for a filename problem that did not exist.
   It now names the state that actually refused and reports the observed value alongside it, with a
   dedicated regression test.
4. **Foreground handback, fixed.** See §7. The B1.1 refusals were correct but unnecessarily brittle;
   positive reacquisition is now inside the seam rather than left to callers.
5. **No completion-only marker exists on this build.** Recorded in the signed evidence together with
   the five candidates that were considered and rejected, so a future revalidation can see what was
   already ruled out rather than rediscovering it.
6. **Closing a modified document always prompts.** This makes the enhancement smoke non-idempotent:
   after a successful run the module stays selected and the document stays modified, so the next
   open auto-enhances and the next close prompts. Handling an unrequested enhancement — waiting it
   out before confirming identity — is deliberately **not** in this slice; it belongs with B2B, which
   has to decide what to do with a result nobody asked for.

No coordinate input, mouse primitive, blind shortcut, `SendKeys`, Save, Save As, export, overwrite,
sibling output or Revision exists anywhere in the Enhancement or close routes.

---

## 11. Remaining B2B export scope

B2A proves the state transitions and nothing about the image. B2B still has to:

1. get the enhanced result out of Meitu — the observed Save surface writes `{Name}_副本` into the
   folder its own `folderEdit` names, which is not PrintFlow's output contract
   (`{Name}_HD.png` per the preset);
2. decide between `保存` and `另存为` on that surface, and set the destination through signed
   controls rather than typing;
3. validate the output on disk — existence, format, dimensions, and the upscaling the `85% → 21%`
   zoom change implies — before anything is claimed;
4. handle the 温馨提示 save prompt, or make it unreachable by exporting before closing;
5. decide what to do with an enhancement Meitu started by itself, and whether the module should be
   deselected as part of a completed attempt;
6. only then let `ProcessAsync` return success and a Revision be created.

Until all six exist, `ProductionMeituProcessor.ProcessAsync` remains an unconditional structured
refusal, and no code path creates a Revision.

---

## 12. Retained synthetic workspace

Every synthetic Working file created in this slice lived under the OS temp directory and was kept
on disk for the whole time Meitu could reference it — 15 run directories at their peak, each holding
exactly one generated PNG under `Sessions\S_*\Working\A_1\` and nothing else. Nothing was deleted
underneath Meitu at any point, which is the B1.1 mistake this slice was told not to repeat. No
customer file, image or path was involved and none of it was ever in Git.

**Final state: nothing is retained.** The passing run's Phase 4 closed the document through the
signed control and the editor reached `KnownEditorEmpty`, which is what earns the permission to
delete; the smoke deleted its own workspace at that point. Meitu was then confirmed read-only to be
holding no document — its editor shows the `打开图片` / `新建画布` empty-state overlay and no
`关闭图片` — so the permission extended to every workspace kept from the earlier runs, and all of
them were deleted:

```text
C:\Users\admin\AppData\Local\Temp\PrintFlowB2A\         removed (14 run directories, ~396 KB)
C:\Users\admin\AppData\Local\Temp\PrintFlowMeituSmoke\  removed (1 retained run directory)
```

Each held exactly one generated PNG under `Sessions\S_*\Working\A_1\` and nothing else — verified
before deletion. Meitu is left running, on its empty editor, with no document loaded and no dialog
open.

---

## 13. Git state

Branch `master`, 19 commits ahead of `origin/master` at `4a1eb26`. Nothing was committed, pushed or
rewritten in this slice.

Repository changes are limited to: the v1.3.0 preset pin in `appsettings.json`; the
Infrastructure-only enhancement and close signatures, structural action rule, Busy/completion rule,
guarded route, classifier precedence and production seam; the preset parser for the two new evidence
files; the extended test fixtures and six test files; the opt-in workstation smoke; and this report.

Not in Git, by §31: synthetic images, generated output, UI dumps, screenshots, evidence captures,
smoke transcripts, runtime DB files, and the external baseline artifacts under `D:\PrintFlowStudio`.
The two temporary diagnostic harnesses written for the read-only discovery and the supervised
observation runs were removed after their evidence was signed, and the throwaway operator scripts
described in §7 were never in the repository at all.

---

Meitu exposes one structurally identifiable Enhancement control whose actionable owner is two
levels above its text marker; a fresh backed Working copy was used; exact document identity was
confirmed immediately before the action and again after it; exactly one guarded invocation was
produced; Busy and completion were each positively evidenced from live observation and signed into
a new immutable preset version; the module-toggle and module-retention hazards are refused by a
mandatory pre-invoke guard; every bounded wait fails structurally rather than returning an unknown
success; and no export, saved copy, transformed output, workflow success or Revision is produced or
claimed.

11300-B2A PASS — READY FOR ENHANCEMENT EXPORT

