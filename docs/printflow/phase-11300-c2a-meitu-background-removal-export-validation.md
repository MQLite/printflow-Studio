# Epic 11300 Part C2A — Meitu Background Removal export and validation

Date: 2026-08-25 (Pacific/Auckland)

Verdict: `11300-C2A PASS WITH NOTES — READY FOR BACKGROUND REMOVAL WORKFLOW INTEGRATION`

This slice makes a real, validated Background Removal `AdapterOutput` available only through the
controlled production `IMeituProcessor` seam. It does not add the decision to the normal Session UI,
does not enable global Production mode, and creates no Revision in Infrastructure.

## 1. Explicit decision and request contract

`MeituRequest` now carries the typed `BackgroundRemovalDecision`:

```text
Unspecified
UseAutomaticSelectionForReviewedContent
```

The production `RemoveBackground` route checks this before inspecting a window, locating Meitu or
producing any input. `Unspecified` returns `PreconditionNotMet` with `inputSent=false`; it is never
mapped or defaulted to automatic selection. The ordinary `SessionService` explicitly constructs
`Unspecified`, so C2A cannot enable the production workflow by accident. C2B owns the real caller
that may supply reviewed-content/operator authority.

The deterministic fake does not translate the decision to automatic mode: it drives no Meitu mode.
For opaque RGB inputs its success branch creates a separate same-canvas PNG with deterministic alpha;
for existing-alpha fixtures it preserves that alpha exactly, retaining Epic 11200 trim behaviour.

## 2. Export route reuse

The real C2A smoke positively exercised the completed cutout before reuse was accepted. It found the
same signed chain already used by Enhancement:

```text
signed Save surface
→ format field set/read back as png
→ filename field set/read back
→ 另存为
→ owned #32770 destination dialog
→ full controlled path set/read back
→ one confirm invocation
→ existing signed result surface
```

No structural difference appeared. Background Removal calls the same `ExportResultAsync` driver and
the same result-surface dismissal. There is no second file-dialog implementation, no `folderEdit`, no
`保存` route, and no coordinate, mouse or shortcut path.

## 3. Preset and evidence

The inherited `printflow-workstation-v1` preset `v1.5.0` and its existing export evidence were
accepted unchanged. Because the live cutout exposed the identical surface/control structure, no new
UI evidence or semantic preset version was created. `v1.5.0` was not edited.

## 4. CUTOUT naming and path

Production Background Removal accepts only a non-empty exact `{Name}_CUTOUT.png` filename. The input
and output must be distinct `WorkspaceArea.Working` references and siblings in the same producing
Attempt directory. The adapter resolves only the managed output reference; no arbitrary caller path
reaches the UI driver.

The exact destination must not already exist. A collision fails closed with `exportInvoked=false`;
there is no overwrite and no collision suffix.

## 5. Stability

Both operations use the existing B2B filesystem rule:

```text
non-zero length
same length for 3 consecutive observations
250 ms production polling
readable on the final observation
bounded timeout
```

Dialog closure and the result surface are never treated as file success. The real cutout settled in
three observations.

## 6. Actual alpha validation

Generic file facts still come from the existing `IFileInspector`/`WicFileInspector` pipeline: content
format, dimensions, DPI/colour metadata, `HasAlpha`, full readability and SHA-256. A narrow internal
WIC transparency inspector then decodes the already-proven PNG to BGRA32 and reads every alpha byte.

The pure rule is exact:

```text
alpha < 255 → some transparency
alpha > 0   → visible foreground
```

Both must occur. There is no 128 threshold or other opacity cutoff. Unit coverage pins
`0, 1, 127, 128, 254, 255`: five values count as transparent and five count as visible. Opaque and
fully transparent PNGs are both refused.

## 7. Dimensions

The supervised source and cutout were both `480×360`. C2A therefore makes exact canvas preservation
the production contract:

```text
output Width == source Width
output Height == source Height
```

Background Removal does not reuse Enhancement's “not smaller” rule.

## 8. Source Working safety

The source Working copy is inspected before Meitu interaction and again after export. Success requires
equal byte length and exact SHA-256. The live source remained
`9C601AD12C9AE4DFF1682B63DC47E15F2150A3B42BFE171909CAAF6009EBF6D6` before and after. The cutout was a
new sibling file; no overwrite-original control is reachable.

## 9. Production AdapterOutput boundary

`MeituOperation.RemoveBackground` now returns success only after:

```text
reviewed-content decision
→ managed-reference and CUTOUT validation
→ source inspection/hash
→ exact Working document open and identity
→ current-run Background Removal Busy
→ positive five-control completion
→ exact post-completion identity
→ inherited controlled export
→ exact path and stable readable file
→ content-inspected PNG
→ exact dimensions
→ real transparency and visible foreground
→ unchanged source Working hash
→ cleanup attempt
→ AdapterOutput
```

The outcome correlation is rechecked before export, so a stale completion panel without current-load
Busy cannot become export authority. Infrastructure references no repository and creates no Revision.
Enhancement continues through its existing operation-specific validation unchanged.

## 10. Failure coverage

The combined inherited and C2A suites cover:

- unspecified decision with zero Meitu input;
- wrong CUTOUT name/extension and different Attempt directory;
- existing destination without overwrite;
- missing or mismatched Save surface controls, format/name/path read-back mismatch, missing destination
  dialog, and foreground loss before confirm, with no confirm invocation for every pre-Save refusal;
- result-surface discrimination from the structurally similar Save surface;
- missing, zero-byte, corrupt, wrong-content-format and never-settling output;
- wrong dimensions, opaque PNG, fully transparent PNG and mutated source;
- stale Background Removal completion without current-load Busy;
- deterministic fake output as a separate PNG.

## 11. Automated tests and gates

Preflight, before C2A, matched the handoff exactly:

```text
7074 passed
0 failed
0 warnings
0 errors
no vulnerable packages
```

After C2A the full automated suite reports:

```text
locked restore succeeded
7098 passed
0 failed
0 skipped
0 warnings
0 errors
no vulnerable packages (including transitive dependencies)
```

## 12. Workstation smoke

The opt-in controlled-seam smoke used Meitu `7.8.7.5`, immutable preset `v1.5.0`, and a new synthetic
opaque RGB foreground/background image. No customer artwork was read.

```text
KnownEditorEmpty
→ open controlled Working copy
→ exact identity
→ UseAutomaticSelectionForReviewedContent
→ 抠图
→ current-run Busy
→ positive completion
→ exact identity
→ inherited Save / png / 另存为 route
→ PF_BACKGROUND_C2A_CCA8D0E49AED_CUTOUT.png
→ 3 settling observations
→ PNG / alpha / dimension / source-hash validation
→ AdapterOutput
→ signed result dismissal and empty-editor cleanup
```

Observed facts:

| Fact | Observation |
|---|---|
| Source dimensions | `480×360` |
| Output dimensions | `480×360` |
| Source pixel format | `Bgr24` |
| Output pixel format | `Bgra32` |
| Content format | `Png` |
| `HasAlpha` | `True` |
| Transparent pixels | `149305 / 172800` (`True`) |
| Visible pixels | `55473 / 172800` (`True`) |
| File size | `4788` bytes |
| Output SHA-256 | `E9E83B1890D8CBF5963C0D5561845DD8C209F72555908D055AC41A0104ABFD99` |
| Controlled relative path | `Sessions/S_SMOKE/Working/A_1/PF_BACKGROUND_C2A_CCA8D0E49AED_CUTOUT.png` |
| Settling | `3` observations |
| Source unchanged | `True` |
| Cleanup | signed result dismissed; signed empty editor reached |

No cutout-quality judgement was made.

## 13. Cleanup and retained state

The successful C2A smoke returned Meitu to the signed empty editor and removed its GUID-scoped
synthetic workspace/output. Its text transcript remains in the OS temp directory and is uncommitted.

The four released C1 synthetic workspaces were resolved exactly beneath
`C:\Users\admin\AppData\Local\Temp\PrintFlowMeituSmoke`. A deletion attempt was made only against
those four explicit paths, but the execution policy rejected the command before it ran. No C1
directory was deleted. They remain outside Git, released by Meitu and housekeeping-only. Older
identity-diagnostic directories were intentionally not targeted.

## 14. Remaining C2B scope

C2B still owns:

```text
SessionService / UI
→ explicit operator or reviewed-content decision
→ Attempt
→ production adapter
→ Revision
→ ReviewRequired
```

C2A did not add that caller, database row or Revision path. `Adapters:Mode=Production` remains refused
by application composition and the existing environment gate remains authoritative. Enhancement was
not redesigned.

## 15. Git state

C1 was preserved in checkpoint commit `eedf87c` (`11300: complete guarded background removal action`).
C2A implementation and tests are checkpointed in `ffae82c` (`11300: export and validate background
cutouts`). At the implementation checkpoint the branch was `master...origin/master [ahead 23]` and
clean. This report is the next normal commit. No history was amended or rewritten, nothing was pushed,
and no synthetic image, cutout, screenshot, UI dump, smoke transcript, runtime database or external
baseline file is tracked.

`11300-C2A PASS WITH NOTES — READY FOR BACKGROUND REMOVAL WORKFLOW INTEGRATION`
