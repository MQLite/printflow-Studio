# Epic 11400 B1A.2E — Flexible Size Operator UI and Enlargement Confirmation

## Verdict

**11400-B1A.2E PASS — READY FOR PRODUCTION PHOTOSHOP SIZE PREPARATION**

## Scope and preflight

This slice is limited to WPF, ViewModel, the UI-safe service confirmation seam, read-model audit
projection, localisation, and focused Fake verification. It adds no migration and changes no
persisted sizing or workflow-engine semantics.

Preflight found a clean `master` at `c6ee407`, ahead of `origin/master` by 24 commits, with the
B1A.2D implementation and report commits present. The repository-pinned .NET SDK 10.0.400 was
selected through the established per-user muxer. The preflight build completed with 0 warnings
and 0 errors. The 9,000+ complete suite was intentionally not run.

`appsettings.json` still selects immutable workstation preset v1.11.0 and `Adapters.Mode` remains
`Fake`. The production Photoshop output processor remains fail-closed: no resize, W1 execution,
CMYK conversion, TIFF Save As, or process launch was added.

## Operator sizing paths

### Simple preset path

The Print Dimensions step now presents the verified preset recommendations as the primary size
cards. Every card is rebuilt from `SessionView.Sizing.PresetRecommendations`; neither XAML nor the
ViewModel states production dimensions. A3 Landscape and A3 Portrait show configured recommended
maximum boxes, while A4 and A5 show their configured recommended long edges. A4 displays 280 mm,
not nominal ISO A4's 297 mm.

Choosing a named card sends `SetPresetFitSize(SizePreset)`. It asks for no millimetres and keeps
the ordinary proportional-fit route the shortest operator path.

### Custom target-edge path

Custom Size opens one selector containing exactly Width, Height, and Long Edge, plus one
culture-aware millimetre text box. The ViewModel parses directly to `decimal`, requires a positive
value for friendly immediate feedback, and sends `SetCustomTargetEdgeSize`; it performs no pixel,
scale, limiting-edge, fit, or enlargement calculation.

The authoritative service/domain path remains responsible for exact decimal acceptance and the
projected plan. The screen shows the resulting Resolution only, Shrink, or enlargement state from
`SessionView.Sizing` and states the fixed 300 PPI without offering an editable DPI.

### Preset-adjustment path

An active named preset offers Adjust size. The action returns through the established
`ReturnToStep(PrintDimensions)` contract, then opens the one-edge form with the selected preset as
context. Confirmation sends the custom edge and millimetres with the named preset, preserving the
auditable preset override. The UI shows “Based on …” and the configured recommendation.

## Preset limit and enlargement

`PresetLimitExceeded` produces a non-blocking notice naming the preset. It does not request a
second confirmation. Focused coverage proves an A4 300 mm long-edge override can exceed the
configured 280 mm recommendation while remaining supported by source pixels and runnable.

`NeedsEnlargementAuthority` instead presents a warning with the authoritative projected scale and
300 PPI, explains that clarity may be reduced, disables run readiness, and offers Change size or
Continue with this size. No resampling/interpolation vocabulary is operator-facing.

Continue uses `ISessionService.AuthoriseCurrentEnlargementAsync`. The displayed warning carries an
opaque single-use offer ID only. SessionService retains the exact hidden Revision, SHA-256, edge,
millimetres, pixels, and scale binding, re-verifies integrity, and submits the exact existing
workflow command. A changed source or target therefore refuses the displayed offer and forces a
refresh; the shell receives no Revision/hash and cannot construct `EnlargementAuthority` or
`WorkflowCommand.AuthoriseEnlargement`.

After success the screen refreshes from `SessionView`: `NeedsEnlargementAuthority` is false,
`HasUsableEnlargementAuthority` is true, and the image-and-size-specific confirmation note appears.
Run readiness remains `SessionView.CanRunPhotoshopOutput`.

## Stale, retry, and Add Another Size behavior

- Changing a target goes back through the workflow size step. The workflow clears the prior target
  and authority; a new enlarging target receives a new warning and offer.
- A displayed offer is refused if another persisted target replaces it before the click.
- Reject/retry with the same source and target retains the matching authority and does not ask for
  enlargement confirmation again.
- Add Another Size uses the existing command and opens a fresh sizing panel with no preset,
  custom target, preset override, or enlargement authority. Completed sibling output audit remains
  listed.
- Upstream/source staleness continues to be determined by Workflow/SessionView. No App comparison
  of Revision or SHA was introduced.

## Producing-attempt audit

The Review Required panel reads only the producing Attempt's immutable preparation projection.

For PresetFit it shows the named preset, its recorded configured recommendation, proportional fit,
300 PPI/projected pixels, and the Fake qualification.

For TargetEdgeV1 it shows the requested custom edge and millimetres, “Based on …” plus the recorded
recommendation when it was a preset adjustment, explicit preset override, projected direction,
and explicit enlargement confirmation when present. It does not display a source hash, DOM enum,
or scale rational.

Fake output is always qualified as “Projected plan — Photoshop was not run.” No wording claims an
actual or final Photoshop result.

## Localisation and rendering

Natural en-US and zh-CN resources were added for preset use, custom sizing, target edges,
millimetres, preset adjustment, recommendation forms, limit notices, projected directions,
enlargement warning/actions/confirmation, attempt override, projected plan, and Photoshop-not-run
qualification. Resource parity, non-empty values, typed accessors, warning semantics, and the ban
on operator-facing resampling vocabulary are tested.

Real WPF measure/arrange passes at the established 1000×700 viewport cover both locales for:

1. normal preset selection;
2. selected A4 configured recommendation;
3. pure Custom Size;
4. Adjust selected preset;
5. custom shrink;
6. preset exceeded without enlargement;
7. enlargement warning with both actions visible;
8. enlargement authorised;
9. Fake ReviewRequired — PresetFit;
10. Fake ReviewRequired — TargetEdge plus enlargement;
11. Add Another Size fresh state.

All bindings resolve, the rendered width stays within the supported viewport, and no screenshot or
golden-image system was added.

## Focused Fake workflow verification

The focused tests use real `SessionService`, SQLite persistence, workspace files, WPF ViewModels
and layouts, and Fake adapters.

- Custom shrink reaches ReviewRequired with immutable projected attempt audit and no enlargement
  confirmation.
- A4 adjustment beyond 280 mm but within source capacity shows only the preset notice and retains
  the A4 override in the attempt audit.
- Small-source enlargement blocks Run, requires the opaque exact-offer confirmation, then reaches
  ReviewRequired with explicit enlargement confirmation and no Photoshop process.
- Target replacement invalidates the old authority; same-content retry retains it; Add Another
  Size starts clean.

The final focused command selected flexible-size workflow/UI/rendering, prior maximum-bound UI and
rendering compatibility, Dimensions/W1 output UI, Add Another Size, Fake adapter scenarios, session
smokes, startup composition, localisation, Photoshop/maximum-bound architecture boundaries, and
directly relevant persistence coverage.

Result: **270 passed, 0 failed, 0 skipped**. The complete suite was not run, as required.

## Architecture and dependency checks

- ViewModels do not call `FitWithinBounds`, `ScaleToTargetEdge`, or `EnlargementAuthority.For`.
- The App cannot construct the enlargement workflow command and receives no Revision/SHA sizing
  binding.
- ViewModels contain no `System.IO`; no Photoshop COM/DOM/UIA was added to App.
- The operator screen contains no resampling selector or Bicubic/Preserve Details vocabulary.
- Production Photoshop remains fail-closed; no W1, CMYK, TIFF, resize, process termination, or
  Photoshop launch was added.
- Migration scripts still end at `0006_flexible_size_and_enlargement_authority.sql`.
- No package, project, or lock file changed. Dependency graph unchanged; vulnerability audit
  deferred to Epic 11400 Final QA.
- No preset, baseline, or evidence file changed; no workstation evidence or 23/23 rehash was
  required.

## Remaining B1A.3 production scope

B1A.3 still owns the real Photoshop preparation operation: opening/validating the production
document under its signed baseline, applying the selected proportional resolution/shrink/enlarge
policy, executing W1, converting to CMYK, saving/exporting TIFF, and recording actual Photoshop
read-back. None of that production scope is enabled here.

## Build and Git state

The final `dotnet build` completed successfully with **0 warnings and 0 errors**.
Implementation and report changes remain local and uncommitted for review on `master`, which is
still ahead of `origin/master` by the pre-existing 24 commits. No amend, rebase,
history rewrite, push, runtime database, synthetic image, Fake output, screenshot, smoke workspace,
external preset/evidence, or Photoshop file is included.
