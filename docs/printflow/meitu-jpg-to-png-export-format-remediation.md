# Meitu JPG-to-PNG export-format remediation

**Date:** 10 September 2026  
**Work item:** SCRUM-11065  
**Repository start:** `master` at `967fc2ba5fb2f195e031a2f6fbcec161c67ecb13`, clean  
**Scope:** signed Meitu evidence, workstation preset reissue, Infrastructure adapter repair,
tests, and fixed-workstation acceptance. SCRUM-11136 was not executed.

## Defect reproduction

The production failure was reproduced with the controlled `FIX-PORTRAIT-001.jpg` fixture. On the
signed Meitu Save surface, `formatCombo` initially read `jpg`. `ValuePattern.SetValue("png")`
returned normally but a fresh read still returned `jpg`. PrintFlow then stopped before Save or
Save As, so the existing read-back safety contract worked; the missing behavior was a signed,
deterministic route that could actually change the format.

The historical immutable export evidence (`editor-export.json`, SHA-256 `574DE5FD…214CF8E`) was
captured from a PNG/RGBA source and described only the Save surface and a `Value`-pattern format
control already reading `png`. It did not describe the JPG default, Qt popup, or its `png` item.

## Live popup hierarchy and interaction result

Evidence capture used only `FIX-PORTRAIT-001.jpg`, the accepted XiuXiu 7.8.7.5 executable, and the
already-signed Editor Save control. Save and Save As were not invoked during inspection.

| Object | Stable accepted identity | Diagnostic-only observation |
|---|---|---|
| Format combo | empty Name; AutomationId contains `.SaveMaskWidget.widgetRight.wName.formatCombo`; `ComboBox`; `proui::NoAnimationComboBox`; `Invoke` + `Value`; same accepted process | runtime id `42.1710804.4.-2147480684`; 1280,414 64×28; initial `jpg` |
| Popup | Win32 title `XiuXiu`; Win32 class `Qt51517QWindowPopupSaveBits`; UIA Name `XiuXiu`; UIA class `QComboBoxPrivateContainer`; `Window`; `Invoke` + `Value` + `Window`; visible/enabled; same accepted process; signed Save surface remains exact foreground | runtime id `42.1182400`; 1280,441 72×134 |
| PNG item | Name `png`; empty AutomationId/class; `ListItem`; visible/enabled; same accepted process; parent `List`/`QListView`; next ancestor `ComboBox`/`proui::NoAnimationComboBox` | runtime id `42.1710804.4.-2147480655`; 1287,472 58×24; clickable point 1316,484 |

`SelectionItem.Select()` and `InvokePattern.Invoke()` both returned normally while leaving the
popup open and the value at `jpg`. Only a pointer click at the item's freshly obtained UIA
clickable point changed the value to `png`; the popup then disappeared and the same format control
freshly read `png`. Recorded runtime ids, bounds, the process id, and the point are observations,
not recognition values or stored input coordinates.

## Accepted recognition contract

The reissued authority permits exactly this route:

1. Read the unique signed format control. If it already reads `png`, do not open a popup.
2. If and only if it reads the signed initial value `jpg`, verify the signed Save surface and the
   same unique combo exposing both `Invoke` and `Value`.
3. Refuse any pre-existing unknown visible same-process top-level window.
4. Invoke the combo, require exactly one new popup matching both its current Win32 and UIA
   identities, and require the signed Save surface to remain the exact foreground window.
5. Require exactly one enabled, onscreen `png` item with the signed two-level ancestry.
6. Re-verify the accepted process instance (PID and start time), Save surface, combo, popup,
   foreground, live item bounds, live clickable point, and point hit-test immediately before input.
7. Send one pointer sequence derived inside the provider from that current clickable point. The
   caller cannot supply a point or coordinate.
8. Positively observe the popup absent, then re-verify the Save surface and freshly read `png`.
   Only that read-back permits the existing Save As path.

The explicit runtime-derived pointer exception is narrower and newer than the historical phase
11300 statement that the earlier route contained no coordinate path. It is authorized by the
SCRUM-11065 requirement after the UIA Selection and Invoke mechanisms were proven inert. No fixed
coordinate, caller-supplied point, generic arbitrary popup driver, or non-Infrastructure GUI detail
was introduced.

## Negative recognition and stale-target behavior

Every one of these observations stops before Save As and destination confirmation:

- no popup, more than one new window, an unknown new popup, or a pre-existing unknown visible
  same-process top-level window;
- wrong Win32 class, wrong UIA class/pattern set, wrong process, lost exact foreground, missing
  popup, or a replaced/unreadable popup;
- no unique `png` item, disabled/offscreen/empty-bounds item, or wrong item ancestry;
- a format control split across separate partial `Value` and `Invoke` matches;
- process restart/PID reuse, replaced Save surface/control/popup/item, stale clickable point, or a
  hit test resolving to a different element;
- failure to positively observe popup disappearance, popup replacement while settling, an unknown
  window appearing after input, or fresh read-back remaining `jpg`.

Observation failure is not treated as disappearance. Retries are bounded by the existing dialog
deadline, and the click is never retried after input may have been sent.

## Preset reissue

Preset 1.16.0 and its evidence remain byte-identical historical authority. The next legitimate
immutable version was issued outside Git through the existing workstation-baseline system:

| Authority | State |
|---|---|
| `apps\meitu\editor-export-format-popup.json` | read-only; 6,174 bytes; SHA-256 `DB6E8D69283719B7137E0E4E9B75FDE02B308C9482E3894273A3248053F19997` |
| `preset\printflow-workstation-v1.17.0.json` | `ACCEPTED_IMMUTABLE`, read-only; 26,853 bytes; SHA-256 `A2E1936B355C28CDC9905EBF63107A7B4229B71D7586B3C304B7F284B59FCFA9` |
| Superseded authority | 1.16.0, SHA-256 `6396FB4EB87F69C6789304CE191453654B2B75E82A5A9AB0161F90556A6F1A80` |
| Integrity | all 28 inherited entries retained and rehashed; the new evidence is entry 29 |

`appsettings.json` now binds 1.17.0 and its exact digest. The provider verifies the supplemental
file through the preset integrity list and refuses an incomplete contract or one that disagrees
with the historical Save-surface control and required `png` value.

## Product change and automated verification

The change is confined to `PrintFlow.Infrastructure` and its tests: the optional Meitu signature,
strict preset parser, guarded popup route, and narrow UIA provider operation. Domain, Workflow, and
App UI remain unaware of Qt classes, HWNDs, popup selectors, and pointer details. The dead
`SetValue("png")` attempt was removed; read-back remains authoritative.

- Focused/affected Meitu, preset, automation-boundary, Photoshop-boundary, and workstation tests:
  **563 passed, 0 failed, 0 skipped**.
- Static regression-set preflight: **Passed** — seven categories, all manifests readable, all
  hashes recomputed and matching.
- Clean Release solution build: **Passed**, 0 warnings, 0 errors.
- First full Product run after stability: **11,734 passed, 1 failed, 0 skipped** of 11,735. The sole
  failure was the pre-existing timing-sensitive
  `ProductionMeituExportTests.A_reviewed_content_cutout_succeeds_through_the_production_seam`, whose
  synthetic output remained changing inside its 400 ms settle deadline. It immediately passed in
  isolation in 463 ms. No Product rule or timeout was weakened.
- One proportionate full-suite retry after the final safety review: **11,735 passed, 0 failed,
  0 skipped** in 5 minutes 18 seconds.

Because Product source/tests changed, these closure counts are diagnostic results and do not
rewrite the previously accepted 11,712/0/0 Product baseline.

## Live proof and acceptance state

The final implementation's portrait and fine-hair live proofs have not yet run. After the latest
foreground correction and safety review, Photoshop contained unrelated operator work
(`When God Made Me.tif` at the last non-invasive check), so it was neither closed, restarted,
dismissed, nor modified. The documented acceptance environment requires Photoshop
`KnownStartScreen` with zero documents, and the verifier correctly blocks before driving cases
while that prerequisite is false.

Accordingly, no complete seven-category run, Operator visual-review handoff, Production
revalidation record, or normal Product-gate result is claimed here yet. SCRUM-11065 and
SCRUM-11123 remain PARTIAL; SCRUM-11136 remains PARTIAL/not executed. This section will receive a
dated addendum if the operator workstation becomes safely available for the remaining live steps.

## Git state

The work began clean on `master` at `967fc2ba5fb2f195e031a2f6fbcec161c67ecb13`. The accepted
authority/configuration change is local commit `579c41cfd27a9ea79bdc38e8933f0badaf0db562`; the Product
adapter and tests are local commit `378c8483e018a67f635169eb368d93f2d7a6cc70`; this report and
append-only audits are committed separately as the final documentation commit. No branch,
worktree, clone, alternate checkout, amend, rebase, push, deploy, co-author trailer, or AI
attribution was used.
