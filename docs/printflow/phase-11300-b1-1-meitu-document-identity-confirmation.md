# PrintFlow Studio — Epic 11300 Part B1.1: Meitu document identity confirmation

**Report date:** 24 August 2026  
**Runtime:** Meitu 7.8.7.5, zh-CN  
**Scope:** determine whether the loaded document can be identified through a guarded, non-writing
Save flow and, only if proven, make `KnownEditorWithExpectedWorkingCopy` reachable.  
**Not in scope:** Enhancement, `AI变清晰`, Busy, export, transformed output, workflow success, or
Revision creation.

---

## 1. Discovered Save control

A read-only UI Automation control-view walk of the loaded editor found one plausible Save marker and
its owning control:

```text
Text   id='MainWindow.centralwidget.mainStackedWidget.editPage.subBackgroundWidget.titleWidget
           .stackedWidget.editorPage.saveButton.textLabel'
       name='保存' class='QLabel' patterns=[Invoke]
  parent:
Button id='MainWindow.centralwidget.mainStackedWidget.editPage.subBackgroundWidget.titleWidget
           .stackedWidget.editorPage.saveButton'
       name='' class='OptionButton' patterns=[Invoke, Value]
```

The signed rule starts at the exact `保存` `Text/QLabel` marker and accepts its parent only when the
label id equals the owning `Button/OptionButton` id plus `.textLabel`, the owner id ends in
`.saveButton`, the owner exposes `Invoke`, both elements report the verified Meitu process, the
owner is enabled and onscreen, and exactly one candidate satisfies the complete shape. Text alone,
the first Save-like element, and a coordinate are never selection rules.

## 2. Observed Save behaviour

Invoking that unique control under a fresh process/window/foreground/element check did not save
immediately. Meitu opened one owned custom Qt tool window and disabled the editor while it was open:

```text
title       : Form
Win32 class : Qt51517QWindowToolSaveBits
relationship: owned by the verified Meitu process/editor
```

The surface exposed:

| Purpose | Observed control |
| --- | --- |
| output filename | `Edit/QLineEdit`, id containing `.SaveMaskWidget.widgetRight.wName.fileNameEdit`, `Value` |
| destination folder | `Edit/QLineEdit`, id ending `.wPath.folderEdit`, `Value` |
| format | `ComboBox/proui::NoAnimationComboBox`, id ending `.wName.formatCombo` |
| Save As | `Button/QPushButton`, name `另存为` |
| Save | `Button/QPushButton`, name `保存` |
| cancel equivalent | `Button/proui::IconFontButton`, id containing `.titleFrame.closeButton`, name U+E0E6, `Invoke` |

Only the filename value was read. Neither filename nor folder was changed, and neither Save nor Save
As was invoked.

## 3. Document identity candidate

For a source filename `X.ext`, Meitu's default output basename is exactly:

```text
X_副本
```

The accepted identity is therefore:

```csharp
Path.GetFileNameWithoutExtension(expectedWorkingCopyFileName) + "_副本"
```

compared to the signed filename control's `ValuePattern` result with
`StringComparison.OrdinalIgnoreCase`, matching Windows filename case semantics. A path, a filename
without an extension, a prefix, substring, undecorated basename, extension-bearing value, different
suffix, or previous document's value is refused.

## 4. A/B discrimination

Two independently generated PrintFlow synthetic Working copies were opened through the signed B1
picker path:

| Loaded source | Value read from the owned Save surface | Result |
| --- | --- | --- |
| `PF_IDENTITY_A_7C3A91D4E2F8.png` | `PF_IDENTITY_A_7C3A91D4E2F8_副本` | exact A accepted |
| `PF_IDENTITY_B_9E4B72C6A1D5.png` | `PF_IDENTITY_B_9E4B72C6A1D5_副本` | exact B accepted |

The values differ with the loaded source. This is document-derived identity, not a generic Save
default or recent-file label.

For each file the exact owned dialog and exact foreground were rechecked, the signed U+E0E6 close
control was invoked, the dialog closed, and controlled-directory membership remained unchanged. No
saved copy was created.

## 5. Stale-name test

A was opened and produced A's derived basename. B was then opened and produced B's derived basename;
the candidate did not retain A. The classifier tests also model previous A/current B explicitly:
observed A cannot validate expected B.

## 6. Empty-editor negative

With no document loaded, `MainWindow.OpenMaskWidget` provides the signed positive empty-editor
markers and the toolbar `saveButton` remains in the UIA tree but is disabled. The Save route therefore
cannot be invoked to expose or validate an old/default filename. An empty editor cannot establish
document identity.

## 7. Signed evidence changes

The accepted v1.0.0 and v1.1.0 manifests and sign-offs were not edited. The immutable-baseline policy
was followed by adding:

| Artifact | Status / SHA-256 |
| --- | --- |
| `apps\meitu\editor-with-working-copy.json` | `CONFIRMED`; `FDE0E991A99DF64DF48CAF7E0C607F6BE8F8C03374AE2B5137B3410FCBE87A88` |
| `preset\printflow-workstation-v1.2.0.json` | `ACCEPTED_IMMUTABLE`; `C96AEF7B2560089B1A234982076431832FF8DBAEDC0CFAEF1397A9AE0C6C4D0A` |
| `signoff\workstation-preset-v1.2.0.json` | `CONFIRMED_FINAL`; `D66EF35CD004C25E5DED84121F2B391F9AE6BD5CBB75E34C41145D447144D58A` |

All 12 inherited/source integrity entries were rechecked; none was stale. `appsettings.json` now pins
v1.2.0 and its exact digest. The v1.2.0 manifest and sign-off are read-only on disk.

## 8. Classifier and confirmation result

`KnownEditorWithExpectedWorkingCopy` is now reachable only when all of these hold together:

1. executable digest, process identity, window ownership and exact editor title remain verified;
2. the loaded editor supplies enough signed positive markers;
3. no blocking owned dialog is present and the editor is enabled;
4. the unique signed Save owner is invoked only while the verified editor is foreground;
5. exactly one signed owned Save surface appears;
6. exactly one signed filename `Value` control exposes the exact derived basename;
7. the signed Cancel control closes the surface; and
8. expected and observed identity compare exactly under `OrdinalIgnoreCase` semantics.

The normal `ProductionMeituProcessor.OpenWorkingCopyAsync` now performs that confirmation after the
picker closes. Missing evidence, wrong identity, stale identity, ambiguity, target loss, or an unknown
surface is a failure. `ProductionMeituProcessor.ProcessAsync` still cannot return workflow success,
and this slice creates no Revision.

## 9. Automated tests

The focused identity suite covers:

- exact expected A/observed A acceptance and expected A/observed B refusal;
- stale A/current B and the empty-editor negative;
- wrong-owner Save surface with no read or Cancel claimed;
- duplicate and missing filename controls failing closed;
- signed Cancel as the only dialog action;
- target loss before Save with `inputSent=false`;
- malformed/missing signed identity evidence;
- production open requiring identity confirmation; and
- the Infrastructure-only automation boundary, with no mouse primitive, coordinate path, `SendKeys`,
  unverified shortcut, or arbitrary-path identity API.

Final automated gates:

```text
dotnet restore --locked-mode                             OK
dotnet build --no-restore                                0 warnings, 0 errors
dotnet test --no-build --no-restore                      6813 passed, 0 failed, 0 skipped
dotnet list package --vulnerable --include-transitive    no vulnerable packages
```

This is 11 tests above the B1 baseline of 6802.

## 10. Live smoke

The mandatory live A/B discrimination, stale-name test and empty-editor negative all completed on
Meitu 7.8.7.5 zh-CN using unique synthetic files, guarded structural UIA and Cancel only. No customer
file, Enhancement action, export, transformed output or Revision was involved.

An additional post-implementation opt-in smoke opened
`PF_IDENTITY_FINAL_FBE193290D1E.png`, then correctly refused the Save step when a transient
Meitu-owned foreground window displaced the verified editor:

```text
MeituTargetLost
expectedWindow : 0x606BA
actualWindow   : 0x90560 (same verified Meitu process)
inputSent      : false for the refused Save action
```

A later guarded recovery confirmation also stopped before Save when Photoshop gained foreground:

```text
MeituTargetLost
foreground process : Photoshop
inputSent           : false
```

Those refusals are expected guard behaviour and were not bypassed or retried blindly. The successful
A/B observations, rather than a failed foreground race, are the live identity evidence supporting
the signed baseline.

## 11. Safety findings

- No coordinate input, mouse primitive, blind shortcut, `SendKeys`, Save, Save As, export, or
  Enhancement action exists in the identity route.
- Every UI read and invocation is rooted beneath a verified process-owned window. The editor and
  Save surface each require the appropriate exact foreground immediately before interaction.
- A wrong-owner or duplicate surface is never read, canceled or selected between. A known signed
  Save surface is canceled on filename-control ambiguity so it is not left blocking.
- The live workstation twice demonstrated target-loss refusal with zero input at the refused step.
- No saved copy or Revision was created.

One diagnostic cleanup error must be recorded: after the first post-implementation smoke refused at
Save, its unique source root was deleted while Meitu still held the already-open image. This repeated
the B1 discovery hazard the task explicitly said to avoid. Meitu remained stable and no customer file
was involved, but the cleanup action was wrong. `MeituWorkstationSmoke` is now conservative: once its
Open phase starts it retains the controlled workspace until the image has been positively closed,
and its synthetic filename is unique. Because unrelated foreground activity prevented a later
guarded close, Meitu remains running with `PF_IDENTITY_FINAL_FBE193290D1E.png` loaded in memory; no
further UI input was attempted.

## 12. Git state

Branch `master`, 19 commits ahead of `origin/master` at `4a1eb26`. Nothing was committed or pushed in
this slice, and history was not rewritten.

Repository changes are limited to the v1.2.0 preset pin, the Infrastructure-only identity signature,
exact identity rule, guarded Save/read/Cancel route, classifier/production integration, focused tests,
the conservative opt-in smoke cleanup, and this report. Synthetic images, UI dumps, screenshots,
smoke transcripts, generated saved copies, runtime DB files and external baseline artifacts are not
in Git. Temporary diagnostic test harnesses were removed.

---

Meitu exposes a document-specific value; A and B discriminate; stale A cannot validate B; the empty
editor cannot validate a previous value; comparison is exact under Windows case semantics; identity
requires no saved file; interaction remains guarded; and no transformation, workflow success or
Revision is claimed.

11300-B1.1 PASS — DOCUMENT IDENTITY CONFIRMED; READY FOR ENHANCEMENT
