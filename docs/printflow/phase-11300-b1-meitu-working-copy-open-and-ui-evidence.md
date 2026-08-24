# PrintFlow Studio — Epic 11300 Part B1: Meitu working-copy open and UI evidence

**Report date:** 24 August 2026
**Scope:** resolve the real `图片编辑` CardButton structurally, open a PrintFlow working copy, and
sign evidence for the UI states that turn up along the way.
**Not in scope:** Enhancement, Background Removal, completion detection, export, Revision creation.

---

## 1. The CardButton structural target

Part A's defect was precise: the element matching the signed marker `图片编辑` is
`ControlType.Text` with automation id `…functionWidget.CardButton.titleLabel` — the card's *title
label*. It advertises `InvokePattern`, so invoking it reports success and does nothing.

A read-only control-view walk of the live start page established the ancestry:

```
[0] Text     id='…wStartup.funcArea.functionWidget.CardButton.titleLabel'
             name='图片编辑'  class='QLabel'     patterns=[Invoke]
[1] CheckBox id='…wStartup.funcArea.functionWidget.CardButton'
             name=''          class='CardButton' patterns=[Invoke, Value, Toggle]
[2] Group    id='…wStartup.funcArea.functionWidget'  class='StartupFunctionWidget'
…
[n] Window   id='StartupWidget'  class='StartupWidget'
```

Two facts from that walk decide the rule.

**The automation id is not an address.** All eight start-page cards carry the *identical* id
`…wStartup.funcArea.functionWidget.CardButton`. It identifies a card only up to an eight-way
ambiguity that includes 海报设计, 批处理, 抠图, AI消除, AI变清晰, 证件照 and AI商品套图. Only the
child `titleLabel`'s name separates them, so the signed marker text has to be the *anchor* and the
id is a shape check.

**"The first invokable ancestor" is not a rule.** In Meitu's Qt tree every element from the label up
to the top-level window advertises `InvokePattern`, including the `QLabel` itself. Pattern support is
a necessary condition here and never a selection criterion.

The signed rule (`start-page-card-target.json`, read by `MeituCardTargetRule.SelectOwningCard`) walks
*up* from the marker and accepts the parent only if all of:

| Check | Value |
| --- | --- |
| marker name | exactly `图片编辑` — not a prefix, not a substring |
| marker control type / class | `Text` / `QLabel` |
| marker id suffix | `.titleLabel` |
| card control type / class | `CheckBox` / `CardButton` |
| card id suffix | `.CardButton` |
| **id relation** | marker id **==** card id **+** `.titleLabel` |
| pattern | card exposes `Invoke` |
| process | both elements report the verified Meitu pid |
| geometry | card's rectangle encloses the marker's |
| count | **exactly one** accepted candidate |

The id relation is what makes this structural rather than two independent shape checks: Meitu's Qt
ids encode the widget path, so a card that merely has the right class cannot satisfy it against
*this* marker.

Nothing is hard-coded in the adapter. Every field above comes from signed workstation evidence, and
the bounds are recorded as evidence — nothing turns them back into a coordinate.

Confirmed on the workstation, read-only, before anything was invoked:

```
signed card shape : Text/QLabel'.titleLabel' → CheckBox/CardButton'.CardButton' requiring Invoke
resolved card     : CheckBox id='StartupWidget.contentWidget.stackedWidget.startupPage.saStartup
                    .qt_scrollarea_viewport.wStartup.funcArea.functionWidget.CardButton'
                    name='' class='CardButton' pid=26860
  patterns        : Invoke, Value, Toggle
  bounds/enabled  : 812,520 196x70 / True
```

## 2. What the CardButton actually opens

Not a file dialog. **Invoking `图片编辑` creates a second top-level window** — the photo editor, UIA
`AutomationId` `MainWindow`, class `MainWindow`, Win32 title `美图秀秀-图片编辑` — in its *empty*
state. The picker comes from a control on that window, not from the start page.

Both windows report the same Win32 class (`Qt51517QWindowIcon`), so the Win32 class does not
distinguish them and is not used to.

This invalidated the Part A model in two ways worth stating plainly: the start page has no Open
dialog to wait for, and **the window PrintFlow ends on is not the window it started from**.
`IMeituUiDriver.OpenWorkingCopyAsync` therefore returns the target it finished on, and the adapter
confirms against that — polling the start page would watch a screen the file was never going to
appear on.

## 3. Picker / dialog evidence

The empty editor's overlay (`MainWindow.OpenMaskWidget`) carries the open control:

```
Button id='MainWindow.OpenMaskWidget.backgroundWidget.openWidget.widget_2.openButton'
       name='打开图片'  class='QPushButton'  patterns=[Invoke, Value]
```

Invoking it raises a **Windows common dialog** after all — but owned by Meitu and appearing
*asynchronously*: observed absent immediately after the invoke and present shortly after, which is
why the open path polls with a bounded timeout rather than reading once. Part A's `#32770`
assumption turned out to be right, and is now recorded because it was observed rather than assumed.

```
Window   id=''     name='打开图片'     class='#32770'
ComboBox id='1148' name='文件名(N):'   class='ComboBox'
Edit     id='1148' name='文件名(N):'   class='Edit'    patterns=[Value, Text]
Button   id='1'    name='打开(O)'      class='Button'  patterns=[Invoke]
Button   id='2'    name='取消'         class='Button'
```

**Automation id 1148 is not unique** — the file-name `Edit` is nested inside a `ComboBox` and both
report it. A lookup taking the first matching descendant returns the ComboBox. The signed control
type selects the `Edit`, and the resolution must be unique before anything is written. (The item
view also contains `ListItem`s with ids `0`, `1`, `3`… — the Open button's id `1` collides with a
*file in the listing*, which the control-type filter excludes.)

Evidence files record control identities only. No directory listing, file name, path or thumbnail
from the operator's machine appears in any of them.

## 4. Signed evidence changes

The accepted preset's own clause is explicit: *"Any accepted-value change requires a new semantic
preset version and a new sign-off; this file and the manifest must not be edited in place."* So
`printflow-workstation-v1.0.0.json` and its sign-off are **untouched on disk**, and a new
`printflow-workstation-v1.1.0.json` was created. All eight inherited `sourceManifestIntegrity`
digests were re-verified before it was written; none was stale. The diff between the two manifests is
a version bump, a `supersedes` provenance block, and four appended integrity entries — nothing else.

| New evidence | Makes reachable |
| --- | --- |
| `apps\meitu\start-page-card-target.json` | the structural CardButton rule |
| `apps\meitu\editor-empty.json` | `KnownEditorEmpty`, and the `打开图片` open control |
| `apps\meitu\open-file-dialog.json` | the picker and its two usable controls |
| `apps\meitu\editor-document-identity.json` | **nothing** — a signed *negative* result (§6 below) |

`appsettings.json` now names v1.1.0 and its digest. `Adapters:Mode` is still `Fake`.

Optional evidence has two different absences, and the difference is the fail-closed mechanism: a file
the preset **does not list** yields `null` and leaves its state unreachable; a file the preset
**lists** but which does not verify fails the entire baseline. Recognition literals live only in
signed files — `MeituAutomationOptions` no longer carries the dialog class or control ids at all.

## 5. Editor recognition

`KnownEditorEmpty` is now reachable and evidence-backed. It requires the exact title
`美图秀秀-图片编辑` plus at least three of the signed `OpenMaskWidget` markers — `打开图片`,
`试试图片拖放/粘贴至此处打开`, `新建画布`, `手机导入图片`, `最近打开`. All five belong to an overlay
Meitu shows *only* while no document is loaded, so the recognition is positive. Defining emptiness as
"the expected file name is absent" was rejected: that is satisfied equally well by a read that
returned nothing at all.

The classifier was tightened in three ways beyond Part A:

* the exact editor title is required, so a feature-suffixed title cannot carry an editor recognition
  in either direction;
* an editor signature with no positive markers is refused at load time, because a title is the one
  property of a window anything can claim;
* **the empty editor is only considered when nothing has been handed over.** This closes a trap:
  `KnownEditorEmpty` is a *safe* starting state, so a loose empty-editor signature would have turned
  "PrintFlow expected A.png and the editor is showing B.png" into a green light.

Where a document name must appear is itself signed (`MeituFileNameLocation`), because file names turn
up in places that prove nothing — the empty editor's own `最近打开` recent-files list being the
obvious one.

## 6. Positive working-copy confirmation — the blocking finding

**Meitu 7.8.7.5 does not expose which document is open, anywhere a UI Automation client can read.**

A PrintFlow working copy named `PRINTFLOW-SMOKE.png` was opened into a freshly launched Meitu through
the signed picker, under guarded control. The editor window was then walked read-only to depth 30:

```
elements read                     : 141
occurrences of the file name      : 0
occurrences of any file extension : 0
window title with a document open : 美图秀秀-图片编辑   (identical to the empty editor's)
only numeric readout              : 100%  (canvas zoom, not image dimensions)
```

What *is* observable is that **a** document is open: the `关闭图片` control appears and the
`OpenMaskWidget` overlay disappears. What is not observable is **which**. The layers panel was also
opened as an operator-supervised check and exposed no document identity either.

§14 rules out exactly this substitution — "Never treat 'some document is open' as sufficient" — and
§10 requires failing closed rather than inventing evidence. So:

* `editor-with-working-copy.json` **was not created**;
* `KnownEditorWithExpectedWorkingCopy` remains **unreachable**;
* the adapter, having handed the file over and having watched the picker close, **refuses to claim
  the file is open**.

Rejected substitutes, each recorded in the signed negative-result file: the overlay disappearing; the
`关闭图片` control appearing; the `最近打开` list (it names files opened at some point, including ones
PrintFlow did not choose, and persists after a document is closed); image dimensions (not exposed,
and not identity even if they were); and the picker having closed.

The one candidate route left is Epic 11000's recorded observation that Meitu's save flow defaults the
output base name to the original file name. Reading it means invoking Save, which Part B1 does not do.

## 7. Wrong-target safety

Every refusal below is asserted as *"the recorder is empty"*, not merely as a returned failure.

| Situation | Result |
| --- | --- |
| the `titleLabel` itself, `InvokePattern` and all | refused |
| an invokable layout container (`StartupFunctionWidget`, `QWidget`) above the card | refused |
| two structurally valid cards | refused — never chosen between |
| a correctly-shaped card that does not own *this* marker | refused |
| no reachable owner / no marker at all | refused |
| marker or card in another process | refused |
| a name that merely *starts with* the signed marker | refused |
| card without `Invoke`, disabled, offscreen, or not enclosing its marker | refused |
| the editor's *other* `openButton` (toolbar, unnamed) | refused |
| a `#32770` dialog owned by another process | never typed into |
| a picker control reporting another process | refused |
| two picker controls matching the signed identity | refused |
| a file-name field that silently drops the write | **Open is not invoked** |
| Explorer takes the foreground mid-sequence | stops, nothing written |

Two hardening changes came out of this slice:

**The `Ctrl+O` fallback was removed from the open path.** Part A dropped through to a verified
shortcut when the element lookup failed, which was reasonable while that meant "no element of that
name is here". It now means the structural rule *refused*, and §5 requires those to end with no input
at all. `SendVerifiedShortcutAsync` remains on the seam, guarded as before; nothing in the open path
calls it.

**The picker phase now verifies before input.** Part A's fill-and-confirm called the element provider
directly, so no check ran between the open control being invoked and the path being written. The
picker is a different window from the editor, so the editor-foreground check would refuse every
legitimate open; the correct check is that the picker still exists, still belongs to the verified
Meitu process, and that the foreground still belongs to *that process*. It runs twice — before the
write, and again immediately before Open, the irreversible half.

## 8. Automated tests

**6802 pass, up from the 6752 baseline (+50).** 0 failed, 0 skipped.

| Area | Cases |
| --- | --- |
| `MeituCardTargetRuleTests` (new, 19) | the whole of §7 above as pure-function cases over records |
| `GuardedMeituUiDriverTests` (+13) | unsigned card / picker / empty-editor each stop with nothing invoked; picker never appears; missing file-name field; missing Open control; dropped write; wrong-process control; duplicate controls; cancellation; unrecognised screen; foreground moves away |
| `ProductionMeituProcessorTests` (+2) | an open that cannot be confirmed by identity is refused, not claimed; a confirmation that never arrives is a failure |
| `PresetMeituBaselineProviderTests` (+13) | unlisted evidence leaves its state unreachable *without failing*; each new file read from the signed chain; edited bytes → `PresetHashMismatch`; missing file; no digest; markerless signature; more markers demanded than listed; unknown pattern; unknown file-name location; incomplete card and picker signatures |
| `MeituStateClassifierTests` (+5) | file name without editor markers → not accepted; editor markers without the file name → not accepted; a title extending the signed one → not accepted; an editor showing another document is never `KnownEditorEmpty` |

The `RecordingUiElementProvider` fake now models a small parented tree, so "there are two of these"
and "this ancestor is wrong" are constructible, and an `OnInvoke` hook lets a test model what
invoking *causes* — the sequence start page → editor → picker → loaded document is exercised in
order, rather than asserted against a screen that already looked right.

## 9. Workstation smoke

Opt-in and inert by default, run through the §22 composition seam. `FoundationEnvironmentGate` was
not weakened. Synthetic 320×240 PNG under `%TEMP%`; `D:\PrintFlowStudio` was read (the signed preset,
read-only) and never written; Documents, Desktop and Downloads were never navigated or touched.

Final run — PrintFlow launched Meitu itself:

```
state                : KnownWelcome            (9 of 10 signed markers)
resolved card        : CheckBox …functionWidget.CardButton  class='CardButton'  pid=26860
→ card invoked → editor window 0x15150A '美图秀秀-图片编辑' created
→ 打开图片 invoked → '#32770' picker owned by Meitu appeared
→ path written through ValuePattern and read back and matched
→ Open invoked → document loaded (关闭图片 present, OpenMaskWidget gone)

RESULT : MeituUnknownState
detail : 'PRINTFLOW-SMOKE.png' was handed to Meitu, but the verified evidence chain carries no
         signature that identifies which document the editor is showing, so PrintFlow cannot
         confirm the right file is open and will not claim that it is.
  missingEvidence : editor-with-working-copy
```

No Enhancement, no Background Removal, no export, no Revision. No customer or source file was read or
modified. No dialog was dismissed by PrintFlow.

**An unplanned §25 demonstration.** Two earlier runs were interrupted by the workstation itself:

```
RESULT : MeituTargetLost
detail : Meitu did not come to the foreground within the activation timeout.
         Expected 0x1A17D2; foreground is 0x4192C owned by 'CorelDRW' (process 2832).
  inputSent : false
```

and the same with `chrome`. Windows refused the foreground change, PrintFlow polled, disagreed with
its own activation call, and stopped having sent nothing — which is the invariant, observed in the
wild rather than staged.

## 10. Defects and findings

1. **A timed-out confirmation was reported as success (fixed).** `PollForStateAsync` returned its
   last observation as `Ok` when the deadline passed. The launch path checks safety afterwards so it
   was unaffected, but the *confirmation* path did not — an open that never produced the expected
   screen came back as a success carrying `Unknown`. That is the "opened successfully means processing
   succeeded" conflation §24 exists to rule out, latent since Part A and only reachable once the open
   path got far enough to time out. Split into `ConfirmStateAsync` (fails closed) and
   `PollForStateAsync` (returns what it saw), named so rather than one method with a flag.

2. **The picker phase produced input without re-verifying (fixed).** See §7.

3. **A lost value write would have been followed by Open (fixed).** The path is now read back and
   compared before Open is invoked. Without it, a write that silently failed would leave Open acting
   on whatever the dialog already had selected — in a dialog that opens in the operator's last-used
   folder, a file PrintFlow did not choose.

4. **`Ctrl+O` after a structural refusal (fixed).** See §7.

5. **Automation ids are not unique, twice over.** Eight start-page cards share one id; the picker's
   file-name `Edit` and its parent `ComboBox` share another. Both are handled by requiring a unique
   match after the full signed signature, not by taking the first hit.

6. **Meitu does not expose the open document's identity (open, blocking).** §6 above.

7. **Meitu terminated when a panel was opened on a document whose file had been deleted.** Observed
   during operator-supervised discovery, after the smoke's cleanup removed the synthetic working copy
   while Meitu still held it. Discovery-tooling behaviour, not a PrintFlow path, but it argues for
   leaving the working copy in place while Meitu has it.

8. **Part A defect 6 confirmed and worked around.** A Meitu launched from inside a `dotnet test` host
   dies when that host's job object closes. Launching through WMI (`Win32_Process.Create`) escapes the
   job and was used for the discovery runs. This does not apply to the WPF application.

9. **Meitu is not running.** PrintFlow launched the final instance from inside the test host, so it
   was terminated with the host. Nothing force-terminates Meitu by design.

## 11. Remaining B2 scope

* A confirmation route for which document is loaded — the save-dialog default name is the one
  candidate. Until then `KnownEditorWithExpectedWorkingCopy` stays unreachable and the open path
  refuses to claim success.
* `Busy` remains unreachable. Part B1 did not automate Enhancement to produce it, per §16, and the
  Epic 11000 processing-overlay captures are still outside the verified chain. B2's first supervised
  Enhancement run should capture it.
* Enhancement action, completion detection and export; Background Removal and cutout; Meitu-specific
  retry; Stop / force-termination UX; the success benchmark; Photoshop; Maintop.
* Full workstation verification remains Epic 11500.

## 12. Git state

Branch `master`, 18 commits ahead of `origin/master`. Nothing pushed, no history rewritten, no force
push.

Modified: `appsettings.json`, `GuardedMeituUiDriver.cs`, `IMeituUiDriver.cs`,
`MeituAutomationOptions.cs`, `MeituBaseline.cs`, `MeituStateClassifier.cs`,
`PresetMeituBaselineProvider.cs`, `ProductionMeituProcessor.cs`, `IUiElementProvider.cs`,
`UiaElementProvider.cs`, and five test files.

Added: `MeituCardTargetRule.cs`, `MeituCardTargetRuleTests.cs`.

Not committed, and not in the repository at all: the signed preset and evidence files (they live
under `D:\PrintFlowStudio\Baseline\`), screenshots, window captures, the synthetic working image,
smoke transcripts, UI dumps, the runtime database, logs. Smoke transcripts, evidence captures and the
synthetic workspace were deleted. No screenshot exists in the repository and none left the
workstation.

### Gates

```
dotnet restore --locked-mode                            OK
dotnet build                                            0 warnings, 0 errors
dotnet test                                             6802 passed, 0 failed, 0 skipped
dotnet list package --vulnerable --include-transitive    no vulnerable packages
```

---

## Verdict

The correct `图片编辑` CardButton is targeted structurally and confirmed on the live workstation; what
it opens is now known rather than assumed, and is evidence-backed through a properly versioned preset
that leaves the accepted v1.0.0 artifacts untouched; a synthetic Working copy is opened reliably
end to end; no arbitrary or blind input was introduced, and two paths that could have produced input
without a preceding check were closed; unknown UI fails closed; Production mode remains disabled; and
no transformation or Revision is claimed.

One PASS requirement is not met, and cannot be met on this Meitu build: **expected-file identity is
not positively confirmed**, because Meitu 7.8.7.5 exposes the open document's identity nowhere a UI
Automation client can read. This is a property of the application, not an incomplete implementation —
the classifier, the evidence chain and the open path are all ready to accept a confirmation rule the
moment one exists, and every substitute that would have let PrintFlow claim success without one has
been considered and rejected in writing.

Because §31 makes that confirmation a condition of PASS, and because the honest position is that
PrintFlow can open the working copy but cannot yet prove it is looking at it:

**11300-B1 NOT READY**
