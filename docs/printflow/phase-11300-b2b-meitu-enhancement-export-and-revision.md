# PrintFlow Studio — Epic 11300 Part B2B: Meitu Enhancement export and Revision

**Report date:** 25 August 2026
**Runtime:** Meitu XiuXiu 7.8.7.5, zh-CN, accepted workstation `DESKTOP-0BG8884`
**Scope:** get the enhanced result out of Meitu onto a PrintFlow-controlled path, validate what
lands there, let production Enhancement return success, and handle Meitu's retained-module
behaviour.
**Not in scope:** Background Removal, image-quality judgement.

---

## 1. Export UI structure

Invoking the signed Save control on an enhanced document raises an owned surface — not a Windows
dialog, and not a window PrintFlow had seen before:

```text
Form / Qt51517QWindowToolSaveBits, owned by the verified Meitu process
  MainWindow.MaskDialog.MaskCenterWidget.SaveMaskWidget
    …widgetRight.titleFrame.titleLabel      Text     '保存图片'
    …widgetRight.titleFrame.closeButton     Button   proui::IconFontButton
    …widgetRight.wPath.saveLabel            Text     '保存路径'
    …widgetRight.wPath.btnCustomSavePath    CheckBox '自定义'
    …widgetRight.wPath.btnCoverSavePath     CheckBox '覆盖原图'      ← overwrites the input
    …widgetRight.wPath.btnDesktopSavePath   CheckBox '桌面'
    …widgetRight.wPath.folderEdit           Edit     QLineEdit  value='C:/Users/admin/Downloads'
    …widgetRight.wPath.selectFolderButton   Button   '更改'
    …widgetRight.wName.fileNameEdit         Edit     QLineEdit  value='{Name}_副本'
    …widgetRight.wName.formatCombo          ComboBox value='png'   items=[jpg, png, bmp, webp, pdf]
    …widgetRight.qualityFrame…AIButton      CheckBox 'AI变清晰'      ← a third name collision
    …widgetRight.saveAsButton               Button   '另存为'
    …widgetRight.saveButton                 Button   '保存'
    …widgetRight.saveToPhoneButton          Button   '点击保存图片至手机'
```

`folderEdit` opens on `C:/Users/admin/Downloads` — Meitu's remembered last-used folder, which is
exactly the destination §10 forbids relying on.

After a save, Meitu replaces the surface with a second one:

```text
Form / Qt51517QWindowToolSaveBits          ← the same class and the same title
  MainWindow.MaskDialog.MaskCenterWidget.SaveResultMaskWidget
    …titleFrame.closeButton   Button   proui::IconFontButton   name = U+E0E6
    …successLabel             Text     '保存成功'
    …openFolderButton         Button   '打开所在文件夹'
    …openNewButton            Button   '打开新图片'
    …showSaveSuccessRadio     CheckBox MSwitchButton
```

**The two surfaces are indistinguishable by window shape.** Same class, same title, same owner.
PrintFlow tells them apart by contents: the Save surface is the one carrying the signed
`fileNameEdit`; the result surface is the one showing `保存成功` and `打开所在文件夹` and carrying a
close control whose id names `SaveResultMaskWidget`. A wait or a dismissal that matched on class
and title alone would act on whichever happened to be up.

---

## 2. Save versus Save As, and the route that looked right and was not

`保存` and `另存为` were each exercised live, on the same enhanced document, each followed by a
filesystem check of the controlled attempt directory *and* of every folder the surface names.

| Route | File name | Format | Destination |
| --- | --- | --- | --- |
| `保存` | honours `fileNameEdit` | honours `formatCombo` | **ignores `folderEdit`** — writes to Meitu's remembered folder |
| `另存为` | carries `fileNameEdit` + `formatCombo` into a `#32770` dialog | same | wherever that dialog's file-name field names |

The `保存` row is the finding of this slice. Writing the controlled attempt directory into
`folderEdit` **succeeds**, and **reads back exactly**:

```text
folder read-back : 'C:/Users/admin/AppData/Local/Temp/PrintFlowB2B/…/Working/A_1'
name read-back   : 'PF_B2B_C4477309D42A_HD'
format read-back : 'png'
→ 保存 → 保存成功
→ the file appeared in C:\Users\admin\Downloads
```

Meitu showed its success panel while the export landed in the operator's Downloads folder, under
the correct name and format, with the field on screen still displaying the controlled path. The
stray file was removed immediately; it is recorded here because the observation is the reason for
everything below it.

**The consequence is the part worth keeping.** §8 requires every written value to be read back
before it is acted on, and that rule is necessary — it catches the lost write, and this slice's
own tests exercise that. What this disproves is that read-back is *sufficient*. For one control on
this surface, a value can be accepted, echoed back character for character, and change nothing.
No amount of care in the code would have caught it; only looking at the file system did.

So PrintFlow does not name a destination on this surface at all. It writes `fileNameEdit` and
`formatCombo` — both of which a value write genuinely controls — and names the destination in the
dialog `另存为` raises:

```text
#32770  '图片另存为'
  Edit   id='1001'  '文件名:'  ← pre-filled '{fileNameEdit}.{formatCombo}'
  Button id='1'     '保存(S)'
  Button id='2'     '取消'
```

The same shape, and the same delivery mechanism, as the open picker Part B1 already drives. §6
says to prefer `另存为` if it is the route that explicitly respects destination and name; on this
build it is the only one that does.

`folderEdit`, `selectFolderButton`, `btnCustomSavePath`, `btnDesktopSavePath` and `btnCoverSavePath`
are named by no signature, written by no code path, and asserted absent from the driver source as
string literals. `覆盖原图` in particular overwrites the input working copy — the one outcome this
slice exists to make impossible.

---

## 3. Preset and evidence version

One new evidence file, and three preset versions — which needs explaining rather than glossing.

| Artifact | Status / SHA-256 |
| --- | --- |
| `apps\meitu\editor-export.json` | `CONFIRMED`; `574DE5FD437CA50C13EBC925404A0A7FB003235DC45E98CE08884306E214CF8E` |
| `preset\printflow-workstation-v1.4.2.json` | `ACCEPTED_IMMUTABLE`; `B0984BDAFDB7B6C9B0403C61C8105ADCB6F4F58B2C7F86270070A0A9BAAF3225` |
| `signoff\workstation-preset-v1.4.2.json` | `CONFIRMED_FINAL` |

v1.3.0 and every earlier version are byte-identical on disk. All 15 inherited
`sourceManifestIntegrity` digests were re-verified against their files before each manifest was
written; none was stale at any point.

**v1.4.0 and v1.4.1 were signed and then superseded, both times because the evidence had not been
exercised before it was signed.**

1. **v1.4.0** recorded the result surface's close control with an empty automation name,
   transcribed from a read-only UI dump. The real name is the icon-font private-use codepoint
   `U+E0E6`, which renders as nothing. The signature matched no element, so the post-save surface
   could not be dismissed. The live smoke found it, and the rule's own rejection list named the
   one candidate it should have accepted with the single reason `name is '', not exactly ''` —
   two strings that print identically and are not equal. A diagnostic printing the name as
   codepoints supplied the answer directly.
2. **v1.4.1** carried the corrected codepoint, but the explanatory note added alongside it
   contained unescaped quotation marks. The file was not valid JSON, and the next run refused the
   entire baseline with `EnvironmentNotVerified`, naming the line and byte position.

Neither fault could have produced a wrong output, and that is the point. The first arrived after
the file had already been exported and validated, and was kept apart from it by §26's split
between processing success and cleanup warning. The second refused the whole chain, which is the
strongest failure available and the correct one. Both were superseded rather than edited, because
the preset's own immutability policy says so and a rule that bends when it is inconvenient is not
a rule.

The corrective is an ordering one and costs nothing: **run the route against candidate evidence
first, sign the version that survived.** It is recorded in the v1.4.2 sign-off as a process
finding.

---

## 4. Target name, folder and format

The output contract is the existing naming authority, not a new one. `SessionService` builds the
name the same way it builds the approved deliverable's:

```csharp
OutputFileNaming.BuildProposedFileName(NamingArtifactKind.Enhanced, session.OutputName, patterns)
→ "{Name}_HD.png"
```

and passes it as a **sibling of the working copy inside the producing attempt's own directory**.

Until this slice the workflow named the working copy as its own expected output — the fake adapter
processes in place, and nothing downstream minded. A real Meitu minds: exporting over the input
would destroy the bytes the attempt validates its result against, and would do it silently,
because the file would still exist, still be a readable PNG, and still hash to something. The
adapter now refuses that request shape before it touches a window.

Uniqueness comes from the attempt directory rather than from the name. Two attempts on one session
both produce `{Name}_HD.png`, in different directories, neither overwriting the other — which is
what lets a rejected result and the retry that replaces it both survive for comparison.

Enforcement, in order:

| Step | Check | On mismatch |
| --- | --- | --- |
| before Meitu | destination is fully qualified, has a directory and a base name | refuse; nothing invoked |
| before Meitu | extension equals the signed `requiredFormatValue` | refuse; nothing invoked |
| before Meitu | destination does not already exist | refuse; nothing invoked (§33) |
| before Meitu | input and output are both `Working`, and are not the same file | refuse; nothing invoked |
| on the surface | `formatCombo` set, read back, equals `png` exactly | cancel the surface; no Save As |
| on the surface | `fileNameEdit` set, read back, equals the base name exactly | cancel the surface; no Save As |
| in the dialog | `1001` set to the full path, read back, equals it (case-insensitively) | cancel the dialog; no confirm |

The format is confirmed from the selector, never inferred from the extension — §11 — and the
extension is checked against the signed format anyway, because the two decide different things: the
selector decides what Meitu encodes, the name decides what the file is called, and a destination
where they disagreed would produce a file named `.png` containing something else.

---

## 5. Output stability and validation

A dialog closing is not an output. After the single confirm, the screen stops being consulted.

**Appearance and stability** (§14, §15) is a rule over observations rather than a sleep:

```text
the same non-zero length, three consecutive observations in a row,
with the file openable for reading (FileShare.Read) on the last of them
```

polled every 250 ms against a two-minute budget. Three rather than two: two equal lengths is one
interval of quiet, which a writer flushing in chunks produces routinely. The readability probe is
the other half — a slow writer can pause at a stable length, and hashing then would produce a
perfectly reproducible digest of a partial file. Live, the 1.1 MB export settled in **3
observations**, so the margin costs nothing.

Nothing about elapsed time appears in the rule. A slower machine takes more polls, not a different
answer.

**Validation** runs the existing pipeline. `IFileInspector` — the same `WicFileInspector` the
workflow uses — reads the file once and returns hash and metadata from the same bytes. No second
image-inspection implementation exists in the adapter; `MeituEnhancementOutputRule` decides what
that inspector's answer has to look like:

* non-zero;
* format `Png`, read from magic bytes rather than the extension;
* readable end to end (hashing is the readability proof);
* pixel dimensions present, on both the output **and** the source;
* `output.Width >= source.Width` and `output.Height >= source.Height`;
* SHA-256 computed.

The dimension rule is "not smaller", not a multiplier. Meitu was observed upscaling 4×, but that
factor belongs to one image and one set of module settings — the same panel offers `高清` and
`保持原尺寸`, and `保持原尺寸` means exactly what it says. A 4× rule would reject a legitimate result
the moment an operator or a future build chose differently, while catching nothing that "not
smaller" does not already catch. An unmeasurable *source* refuses rather than waives the
comparison: "the source could not be measured, so anything is acceptable" is how a shrunk result
gets through.

**Source safety** (§19). The working copy is inspected before Meitu is given it, and again after
the export, and the digests must match. The customer's original is protected further up by the
Working-copy boundary; what is left to establish here is that the attempt's own input survived its
own attempt. The check exists because `覆盖原图` exists: no code path can reach it, but that is a
claim about the code, and this is a check on the world.

---

## 6. Auto-started and stale enhancements

Part B2A recorded that Meitu retains its selected module across document loads. B2B found the
sharper half of that hazard:

**The module's parameter panel — which *is* the signed completion signature — outlives the document
it belonged to.** After exporting a result and closing the document, the panel was still on screen
and the signed completion signature still matched, on an editor holding **no document at all**:

```text
state before open    : Unknown          (no document)
phase before open    : Complete         (the panel from the previous document)
```

So "the completion markers are showing" says nothing whatever about the document now in front of
PrintFlow. The only evidence that ties an enhancement to a particular load is having positively
watched Busy happen between the open and the claim — and that moment cannot be reconstructed
afterwards.

`ObserveLoadedDocumentAsync` therefore runs **immediately after the open and before the identity
probe**, read-only, and the ordering is required rather than convenient: while Meitu computes, the
editor is disabled and the Save surface the identity probe needs cannot be raised, so probing first
would fail on a run proceeding perfectly normally.

Its first reading decides how long it keeps looking, and the asymmetry is the design:

| First reading | What it means | What happens |
| --- | --- | --- |
| `Unobserved` | no module panel, so nothing can auto-start | return at once; normal path |
| `Busy` | work already in flight on the just-opened document | wait for completion; do not invoke |
| `Complete` | module selected; work may be about to begin | watch up to 5 s for Busy |
| `Complete`, and Busy never appears | stale panel from the previous document | report no auto-start; the pre-invoke guard refuses |

Observed live, opening a document into a retained module:

```text
t+0.0 s   open completes                          (a transient owned pop-up; tolerated, not a stop)
t+0.5 s   Busy                                    ← no PrintFlow input at all
t+8.5 s   Busy ends
t+9.0 s   completion panel
```

The identity probe then runs and confirms the document exactly. That closes §22's correlation:
PrintFlow handed this file over through the signed picker, Busy started after that open and ended
before the probe, and the probe says the editor is holding exactly that file. The enhancement is
provably this attempt's, and the module is **not** invoked — invoking it would toggle it off and
discard the result.

The stale case takes the normal path and is refused by B2A's pre-invoke guard, naming the reason
that actually applies: the module is already selected, so invoking its control would deselect it
rather than start work. That refusal is what stops an unenhanced image being exported under an
enhanced name.

---

## 7. Modal and cleanup behaviour

Exporting before closing makes B2A's `温馨提示 / 当前图片已修改，是否保存？` prompt **unreachable on the
normal path**. Observed directly: with the result exported through `另存为`, invoking the signed
`关闭图片` control reached the signed empty editor, no prompt. Meitu no longer considers the document
to have unsaved changes.

PrintFlow's position on that prompt is unchanged. It is classified `KnownModal`, it stops the run,
and no signature names any control on it. Where an *unexported* modified document raised it during
this slice's discovery work, it was cleared by a throwaway operator step outside the product —
identified by the verified process, the window class, the exact `EnsureSaveMaskWidget` title
marker and exactly one discard button with the exact name, refusing on any ambiguity — and that
step was removed with the rest of the discovery harnesses.

One Meitu-owned surface *is* dismissed: the post-save `保存成功` panel, through its own signed close
control. It earns that exemption by being positively identified from signed markers **and** a
signed close control, which the save prompt has neither of. It has to be dismissed because it
disables the editor while it is up.

**A finding from getting the order wrong.** The first cleanup implementation invoked the close
control and returned immediately. The surface was still disappearing when the document close ran,
which found a blocking modal and reported the document as still loaded — for a close that had in
fact succeeded. The dismissal now waits for the surface to actually go away before returning, and
two tests pin it: one that the wait happens, one that a surface which never goes is a cleanup
failure rather than a silent success.

---

## 8. The `ProcessAsync` success boundary

`ProductionMeituProcessor.ProcessAsync` can return success for the first time. What it takes:

```text
operation is Enhance                       (RemoveBackground refuses before anything is touched)
→ input and output are both Working, and are different files
→ input inspected: dimensions and digest, taken before Meitu sees it
→ Meitu identified, safe state, working copy opened
→ load observed: did Meitu start anything by itself?
→ identity confirmed exactly
→ enhancement — invoked once, or Meitu's own, watched from start to finish
→ destination does not already exist
→ format and file name set on the signed surface and read back exactly
→ full path named in the signed dialog and read back exactly
→ confirm control invoked once
→ a file appears at the controlled path
→ its length stops changing across three observations and it opens for reading
→ it inspects as a PNG no smaller than the working copy
→ the working copy is byte-for-byte what it was
→ AdapterOutput
```

None of the following is a terminal condition anywhere in that list, and each is something a
shorter implementation would have been happy to stop at: the file opened, the module was clicked,
Busy ended, the Save surface closed, `保存成功` appeared. The last is worth naming — it appears for
`保存` and `另存为` alike, names no path, and appeared on the run where the file landed in Downloads.

Cleanup runs after the output is validated and **cannot take it away** (§26). It returns a
description, not a result, because there is no caller-visible decision to make: the file exists and
is valid, and the only question is what to write down. A Meitu that could not be returned to a
neutral state produces a `WARNING:` in the adapter notes saying the next attempt must not blindly
proceed.

Nothing in the adapter creates a Revision or a database row.

---

## 9. Workflow Attempt and Revision integration

The 11100 architecture is unchanged:

```text
SessionService → attempt started → adapter → real output → FileInspector → SHA-256
              → attempt succeeded → Enhancement Revision → ReviewRequired
```

The only workflow change is the request: `MeituRequest.ExpectedOutput` is now a distinct
preset-named sibling rather than the working copy. `SessionService` still creates the attempt row
first, still inspects the produced file itself, and still owns Revision creation.

Verified end to end against a real database, real files and the real service:

* the Enhancement Revision's file is `{Name}_HD.png`, in `Working`, inside the succeeded attempt's
  own directory, and exists on disk;
* the working copy is still there beside it — the attempt directory holds exactly two files;
* Background Removal asks for `{Name}_CUTOUT.png`, not the Enhancement name.

`ProductionMeituProcessor` gained `IFileInspector` and an output probe, so the adapter's own
validation gate uses the same inspection implementation the workflow does rather than a second one.

---

## 10. Reject and retry

The Epic 11100 reject/retry coverage passes unchanged against the new output contract, and B2B adds
the filesystem half of it:

```text
Enhancement A → ReviewRequired → Reject → Retry → Enhancement B
```

* A's Revision and A's ReviewDecision are both retained;
* B is a new attempt with its own working directory;
* both Revisions name `{Name}_HD.png`, at **different paths**, and both files exist;
* neither overwrites the other, because uniqueness is the attempt directory rather than the name;
* B becomes the active result.

A failed attempt still creates no Revision, and its `OutputRevisionId` stays null.

---

## 11. Integrity regression

Unchanged and still passing: an enhanced output that is previewed, mutated on disk and then
Approved fails with `RevisionIntegrityMismatch`. Production Meitu weakens nothing about review
integrity — the approval is bound to the hash `FileInspector` recorded, and the adapter has no way
to influence that binding.

---

## 12. Tests

**7018 passed, 0 failed, 0 skipped** — 118 above the B2A baseline of 6900, measured on the final
tree with every temporary diagnostic harness removed.

**Export rules (`MeituExportRuleTests`, 21)** — a controlled destination taken apart into the pieces
each control needs; the base name excludes the extension the selector supplies; empty, relative and
directory-only destinations refused; four extensions disagreeing with the signed format refused and
two casings accepted; missing signed format refuses the export; the result surface recognised from
its markers; the Save surface's contents *not* read as the result surface; an empty observation, an
empty marker list and three unsatisfiable thresholds all matching nothing; one marker seen three
times still not two markers.

**Output validation (`MeituOutputValidationTests`, 31)** — three equal readable observations settle;
growth to the last moment does not; a stable size that cannot be opened does not; zero bytes never
settle however stable; a file that never appeared does not; fewer observations than required are
"not yet"; earlier growth does not prevent a later settle; a gap inside the window does not settle;
the timeout description names the part that failed. Then the output contract: an upscale accepted,
an unchanged size accepted, empty refused, three wrong formats refused on content, three
smaller-in-either-direction refused, unreadable output dimensions refused, an unmeasurable source
refusing the comparison rather than waiving it; and source safety, including a same-length file with
different bytes.

**The guarded export route (`GuardedMeituExportTests`, 25)** — the whole route once, with the confirm
invoked exactly once; the base name to the surface and the full path to the dialog, asserted
separately; fields set and read back before Save As is invoked; **no value ever written to a folder
field**; a destination disagreeing with the signed format never raising the surface; a relative
destination never raising it; a format that will not read back stopping before Save As and
cancelling; a file name that will not read back doing the same; a destination path that will not
read back cancelling instead of confirming; a path echoed back in different casing accepted; a Save
control presenting no surface; a Save As presenting no dialog; a dialog that will not close
cancelled and never reconfirmed; an editor not showing the expected document; a process that exits
mid-export; a foreground change before the confirm; no signed evidence invoking nothing;
cancellation; the result surface dismissed through its own control, waited for, and reported as a
failure when it never goes; nothing to dismiss reported as success; **the Save surface not mistaken
for the result surface**; and **the modified-document prompt not dismissed**.

**Load correlation (`MeituLoadObservationTests`, 8)** — a quiet load reporting nothing and sending
nothing; Busy at the moment of opening waited out and reported as auto-started; an enhancement
starting just after the open still observed; **a stale module panel not reported as an enhancement
of this document**; Busy that never ends failing rather than claiming; a transient owned window
right after the open not ending the observation; no signed evidence refusing; cancellation.

**The production adapter end to end (`ProductionMeituExportTests`, 11)** — a complete run succeeding
with a validated file and an unchanged input; the notes carrying the facts a report needs; the
result surface dismissed and the document closed; a dialog that closes without producing a file
failing; zero-byte, corrupt, wrong-content-format and shrunk exports each failing on their own
reason; an existing output path failing closed **without overwriting it**; an auto-started
enhancement exported with zero module invocations; a stale panel producing no export and no file.

**Output naming through the workflow (`MeituOutputNamingTests`, 4)** — the Revision pointing at
`{Name}_HD.png` inside its attempt's directory; the working copy surviving beside it; a retry
writing a second output without overwriting the first; Background Removal asking for the cutout
name.

**Preset chain (`PresetMeituBaselineProviderTests`, +9)** — the export route read from the signed
file; unreachable when not vouched for; no required format value refused; no destination dialog
refused; **a destination dialog with no cancel control refused**; no accepted title refused; no
positive result markers refused; no result surface refused; an unknown pattern refused; an
incomplete control description refused; and editing signed export evidence after the manifest was
written failing closed.

**Boundaries (`AutomationBoundaryTests`, +3, 1 tightened)** — the Enhancement route still writes no
value anywhere, with the region now bounded at the export route; the adapter writes values in
exactly three places and **names no folder-field id as a string literal**, with no folder or
directory member on the export signature either; the export evidence record carrying no success,
size or hash surface; the export route exposing no coordinate, keystroke or free-form control name.

**Adapter refusals (`ProductionMeituProcessorTests`, rewritten +2)** — Background Removal refused
outright before any window is touched; an Enhancement whose expected output is its own working copy
refused; three non-Working output areas refused.

---

## 13. Workstation smoke

The §35 sequence completed in one clean run against the signed v1.4.2 chain, on a new unique
synthetic Working copy:

```text
## Phase 1 — identify and classify (read-only)
state                : KnownEditorEmpty          process 20788, window 0x606BA

## Phase 2 — open the synthetic working copy
state                : KnownEditorWithExpectedWorkingCopy
expected / observed  : PF_IDENTITY_FINAL_0ED2996F1147.png / …_副本

## Phase 3 — guarded Enhancement
busy observed        : Busy
identity after       : KnownEditorWithExpectedWorkingCopy
enhancement origin   : Meitu auto-started it on this load; PrintFlow waited it out and invoked nothing

## Phase 4 — export to the controlled attempt path
controlled output    : Sessions/S_SMOKE/Working/A_1/PF_IDENTITY_FINAL_0ED2996F1147_HD.png
source facts         : Png 320x240, 4363 bytes
confirmed format     : png
destination dialog   : '图片另存为'
observations to settle: 3
output facts         : Png 1280x960, 1159183 bytes
output DPI           : 96.01 x 96.01
output colour/alpha  : Rgb / True
output SHA-256       : 5207F744E04267CE1AA68BEAC7C0602CF5FBA62A8D17A2EA9B3139417F643ECC
source unchanged     : verified byte-for-byte

## Phase 5 — release the working file
result surface       : dismissed through its signed close control
close                : the editor reached the signed empty state; the file is released.
```

That run happened to take the auto-start path, which is the realistic second-and-later attempt; an
earlier run on the same code took the normal path (`PrintFlow invoked the signed control once`) and
produced a byte-identical output from the same synthetic input.

**The production seam** (§37) was then exercised separately — one real `IMeituProcessor.ProcessAsync`
against the real application:

```text
adapter id           : meitu-xiuxiu-production-v1
adapter mode         : Production
RESULT               : SUCCESS
produced file        : Sessions/S_SMOKE/Working/A_1/PF_IDENTITY_FINAL_834D73885FD3_HD.png
elapsed              : 13.9 s
adapter notes        : meitu:enhance;
                       source Png 320x240 4363 bytes sha256=C2BFBF03…;
                       output Png 1280x960 1159183 bytes sha256=5207F744…;
                       format png; settled after 3 observation(s);
                       enhancement auto-started by Meitu and waited out;
                       cleanup the editor was returned to its signed empty state

background removal   : REFUSED — AdapterUnavailable
```

`MeituAutomationComposition.CreateProductionProcessor` composes the adapter behind the same
interface `SessionService` uses, with **no session, no repository, no workflow engine and no
registration** — so a caller there can run a real Enhancement and get a real `AdapterOutput`, and
cannot start a session, record an attempt or create a Revision. `IEnvironmentGate` is untouched and
still refuses every Production adapter in the application composition; `appsettings.json` still
reads `"Adapters": { "Mode": "Fake" }`. Epic 11500 is not weakened, and §37's "prove it through the
controlled seam" is what was done.

### Output inspection evidence

| | |
| --- | --- |
| source dimensions | 320 × 240 |
| output dimensions | 1280 × 960 (4×, alpha preserved) |
| pixel format | 8-bit RGBA; `ColourMode.Rgb`, `HasAlpha = true` |
| DPI | 96.01 × 96.01 |
| source size | 4,363 bytes |
| output size | 1,159,183 bytes |
| output SHA-256 | `5207F744E04267CE1AA68BEAC7C0602CF5FBA62A8D17A2EA9B3139417F643ECC` |
| controlled relative path | `Sessions/S_SMOKE/Working/A_1/{Name}_HD.png` |
| observations to settle | 3 (≈0.75 s at the 250 ms poll) |

The 4× scale is one observation on one synthetic image and is recorded as an observation, not a
contract. **Nothing here is a quality judgement** (§18): dimensions and a valid PNG header say the
export produced a real image of at least the right size, and say nothing about whether the
enhancement improved anything. That belongs to later QA.

---

## 14. Defects and findings

1. **`folderEdit` accepts a value, reads it back exactly, and does not move the export.** The most
   consequential finding in the slice: a read-back check that passes while the behaviour differs.
   Recorded in the signed evidence with the run that disproved it, and enforced by an architecture
   test that forbids the control ids as string literals — because nothing in the code could catch
   this, only looking at the file system did.
2. **The Save surface and the result surface share a window class and a window title.** Neither is
   identifiable by shape. Both are identified by contents instead.
3. **Icon-font control names are not empty.** Both title-bar close controls carry `U+E0E6`, which
   renders as nothing in a UI dump. Evidence transcribed from such a dump recorded an empty name and
   matched no element. Fixed; the test fake now carries the real glyph, so a blank-name fake cannot
   let the mistake pass again.
4. **Evidence signed before it was exercised cost two preset versions.** Both faults were of a kind
   only a live run finds. Sign after exercising, not before.
5. **The Save surface closes by itself when Meitu loses the foreground.** An export must raise it and
   drive it to the destination dialog inside one continuous guarded sequence.
6. **The completion signature outlives its document.** It matched an editor holding nothing at all.
   Correlation now rests on having positively observed Busy for the load in question.
7. **Dismissing the result surface must wait for it to go.** Returning as soon as the close control
   was invoked made a successful close report itself as blocked.
8. **A third `AI变清晰` name collision.** The Save surface's quality panel carries one, alongside the
   start-page card and the rotating search placeholder. The signed action rule refuses it on control
   type, class and id suffix.
9. **`保存成功` is not location evidence.** It appears for both terminal controls, names no path, and
   appeared on the run that wrote to Downloads.
10. **`覆盖原图` exists and would overwrite the input.** Recorded as a negative case; no signature
    names it and no code path reaches it. The byte-for-byte source check is the belt to that braces.
11. **Cosmetic, in the auto-start path only.** The smoke prints `completion observed : Unknown`,
    because the load observation's completion snapshot carries no document identity — Meitu is busy
    at that point and the Save surface the identity probe needs cannot be raised. The *phase* was
    `Complete`; the state classification is `Unknown` for want of an identity that had not yet been
    established. Injecting one would be worse than the confusing label.

---

## 15. Remaining Background Removal scope

`MeituOperation.RemoveBackground` refuses unconditionally, before any window is touched, and the
refusal is asserted in two places. The route below it is specific to Enhancement in every part — its
module, its Busy and completion signatures, and a dimension rule that would be wrong for a cut-out.

Still to do:

1. structurally target `智能抠图` / `AI换背景` and sign it, with the same marker-anchored walk and the
   same care about the four-way id ambiguity in the editor's tool list;
2. observe and sign its Busy and completion signatures — they will not be Enhancement's;
3. decide the automatic-selection-mode question the Epic 11000 preset already flags as an
   `OPERATOR_OR_REVIEWED_CONTENT_DECISION`, which is a product decision before it is an automation
   one;
4. an output contract for a cut-out: `{Name}_CUTOUT.png`, alpha required, and a dimension rule that
   is almost certainly *not* "no smaller" — a cut-out may legitimately be the same size, and
   trimming is Epic 11200's job rather than Meitu's;
5. the load-correlation work carries over unchanged, and so does the export route: the Save surface,
   the destination dialog and the result surface are format-neutral and already signed.

The export machinery, the stability rule, the source-safety check, the collision rule and the
workflow naming are all shared and need nothing further.

---

## 16. Git state

Branch `master`, 19 commits ahead of `origin/master` at `4a1eb26`. Nothing was committed, pushed or
rewritten in this slice.

Repository changes: the v1.4.2 preset pin in `appsettings.json`; the export signature types, parser,
rules, guarded route, load observation and production success in Infrastructure; the distinct
expected-output reference in `SessionService`; a fake Meitu that now produces a real separate file;
the extended fixtures and six new test files; the export and cleanup phases and the production-seam
leg of the opt-in workstation smoke; and this report.

Not in Git: synthetic images, exported outputs, UI dumps, screenshots, evidence captures, smoke
transcripts, runtime database files, and the external baseline artifacts under `D:\PrintFlowStudio`.
The six temporary diagnostic harnesses written for the read-only discovery, the export-route
comparison, the auto-start observation and the operator's modal dismissal were removed after their
evidence was signed; the last of those never resolved a prompt PrintFlow itself is allowed to touch.

Every synthetic Working file lived under the OS temp directory and was kept on disk for as long as
Meitu could reference it. All of it was deleted once Meitu was confirmed read-only to be holding no
document; Meitu is left running on its empty editor with no dialog open. One export landed outside a
controlled path — the `保存` run that disproved `folderEdit` — and that file was removed immediately.
No customer file, image or path was involved at any point.

### Gates

```text
dotnet restore --locked-mode                             OK
dotnet build                                             0 warnings, 0 errors
dotnet test                                              7018 passed, 0 failed, 0 skipped
dotnet list package --vulnerable --include-transitive    no vulnerable packages
```

---

Meitu's Save surface honours a written file name and format and silently ignores a written
destination, so PrintFlow names the destination in the Windows dialog `另存为` raises and never
touches the folder field; the enhanced result lands on the producing attempt's own
`{Name}_HD.png`; it is claimed only after it appears, stops changing across three observations,
opens for reading, inspects as a PNG no smaller than its source, and its working copy is confirmed
byte-for-byte unchanged; an enhancement Meitu starts by itself is used rather than repeated, and
only when Busy was positively watched for that load, because the module panel that would otherwise
vouch for it survives the document it belonged to; the post-save surface is dismissed through its
own signed control and the editor returns to its signed empty state, leaving the modified-document
prompt unreachable and unautomated; production Enhancement can now succeed through the existing
adapter seam while Background Removal still refuses and the Epic 11500 gate still refuses every
Production adapter in the application; and no image-quality claim is made anywhere.

11300-B2B PASS — READY FOR BACKGROUND REMOVAL AUTOMATION
