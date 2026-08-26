# Epic 11300 — Final Gate Blocker Fix: Accepted Naming Pattern Contract

Scope: the single product/runtime blocker found by Final Gate Closure R2, and the test coverage
that was missing around it. No Final Gate visual QA was resumed, no production Meitu was run, no
preset was created or edited, and Epic 11400 was not started.

The R2 report (`phase-11300-meitu-final-release-gate.md`, commit `a9b99cc`) is unchanged.

---

## 1. R2 failure reproduction and root cause

### What happened

In Fake mode the operator prepared Background Removal, authorised the displayed Revision and
hash, and pressed Run. The WPF process terminated with an unhandled `FormatException` before
CUTOUT `ReviewRequired` was reached.

### Root cause

`OutputFileNaming.BuildProposedFileName` passed the preset's naming pattern straight to
`string.Format`:

```csharp
NamingArtifactKind.Cutout =>
    string.Format(CultureInfo.InvariantCulture, patterns.CutoutPattern, name.Value),
```

The accepted manifest supplies `{Name}_CUTOUT.png`. `string.Format` reads the text between
braces as an argument *index*, so `{Name}` is a malformed index and composite formatting throws:

```
System.FormatException : Input string was not in a correct format.
Failure to parse near offset 1. Expected an ASCII digit.
```

`SessionService.PerformStepWorkAsync` did not — and could not — convert that into a structured
failure, because the naming call had no failure channel: it returned `string` and threw. The
exception travelled out of the awaited service call in `SessionViewModel`'s Run command and
became a process-level unhandled exception.

### Reproduced

The defect was reinstated deliberately on the fixed tree (the `Cutout` arm alone restored to
`string.Format`) to confirm the new coverage catches it. Six tests failed with the exact R2
exception:

| Test | Failure |
|---|---|
| `AcceptedNamingContractTests.The_accepted_cutout_pattern_names_the_CUTOUT_output` | `FormatException` |
| `NamingContractWorkflowRegressionTests.Fake_background_removal_produces_one_CUTOUT_revision_…` | `FormatException` |
| `NamingContractCrashRegressionTests.Running_background_removal_from_the_screen_reaches_cutout_review` | `FormatException` |
| `NamingContractCrashRegressionTests.An_unrenderable_cutout_pattern_becomes_a_notice_rather_than_a_crash` | `FormatException` |
| `MeituOutputNamingTests.Background_removal_asks_for_the_cutout_name` | `FormatException` |
| `NamingContractBoundaryTests.No_source_formats_a_naming_pattern_positionally` | source-scan violation |

The tree was then restored; the reinstated defect is not present in any commit.

### Why 8,242 green tests missed it

`tests/PrintFlow.Tests/Fixtures/PresetFixture.cs` — the synthetic manifest behind
`SessionServiceHarness`, and therefore behind every SessionService, workspace and UI integration
test — declared its naming contract positionally:

```json
"enhancedPattern": "{0}_HD.png",
"cutoutPattern":   "{0}_CUTOUT.png",
```

The suite and the accepted preset were speaking different naming languages, and nothing compared
them. Every test agreed with the implementation; none agreed with production.

---

## 2. Accepted manifest syntax

`D:\PrintFlowStudio\Baseline\workstation-v1\preset\printflow-workstation-v1.8.0.json`,
`storageAndNamingContract` (read-only; not edited, not reformatted, not copied into the repo):

```json
"enhancedPattern":      "{Name}_HD.png",
"cutoutPattern":        "{Name}_CUTOUT.png",
"productionTiffPattern": "{Name}_{SizeMm}mm_CMYK_W.tif",
"collisionPattern":     "_{Sequence:00}"
```

Two of the four were already known blockers; the audit found the other two carry named tokens as
well, so `productionTiffPattern` and `collisionPattern` would have thrown the same
`FormatException` on the Photoshop TIFF route and on any output-name collision. Both are fixed by
the same change.

This is the required contract. The implementation conforms to the manifest, not the reverse.

---

## 3. `{0}` repository audit

Every accepted manifest version present on this workstation was inspected:

| Manifest | `enhancedPattern` / `cutoutPattern` / `productionTiffPattern` / `collisionPattern` |
|---|---|
| v1.0.0 | `{Name}` / `{Name}` / `{Name}`+`{SizeMm}` / `{Sequence:00}` |
| v1.1.0 | same |
| v1.2.0 | same |
| v1.3.0 | same |
| v1.4.0 | same |
| v1.4.1 | same |
| v1.4.2 | same |
| v1.5.0-candidate | same |
| v1.5.0 | same |
| **v1.8.0 (accepted)** | same |

No accepted, persisted, or runtime configuration has ever used positional syntax. `{0}` existed
in exactly two places, both inside this repository:

| Location | Kind |
|---|---|
| `src/PrintFlow.Domain/Outputs/NamingPatternSet.cs` — `DesignDefault` | source-level fallback, used only until a preset value is available |
| `tests/PrintFlow.Tests/Fixtures/PresetFixture.cs` | synthetic unit/integration fixture |
| `tests/…/Preset/WorkstationPresetProviderTests.cs` | assertions over that fixture |

### Decision — §5 case A applies

`{0}` existed **only** in synthetic fixtures and the source fallback. Both were migrated to the
accepted `{Name}` syntax. **No legacy positional form is retained.** Preserving an undocumented
second production naming language purely to keep old fixtures unchanged is precisely the
arrangement that let this defect through a green suite.

`NamingContractBoundaryTests.The_domain_fallback_patterns_are_the_accepted_syntax` pins the
fallback to the accepted contract so it cannot drift back.

---

## 4. Chosen rendering contract

One explicit authority: `PrintFlow.Domain.Files.NamingPatternRenderer`.

```csharp
public static OperationResult<string> Render(string? pattern, params NamingToken[] tokens);
```

- A `NamingToken` is a **spelling** (the exact text between the braces, matched ordinally and in
  full) and its value. The renderer does not parse format specifiers: `Sequence` and
  `Sequence:00` are two separate spellings, both supplied by `ForSequence`.
- The vocabulary is **closed and supplied per call**, so each artefact kind offers only the
  tokens it has values for.
- `string.Format` is not reachable from any naming path.

| Artefact | Pattern | Tokens offered |
|---|---|---|
| Enhanced | `EnhancedPattern` | `{Name}` |
| Cutout | `CutoutPattern` | `{Name}` |
| Production TIFF | `ProductionTiffPattern` | `{Name}`, `{SizeMm}` |
| Collision suffix | `CollisionSuffixPattern` | `{Sequence}`, `{Sequence:00}` |

`{Name}` means the already-established output name supplied by PrintFlow — the sanitised
`OutputName`, unchanged:

```
{Name}_CUTOUT.png  +  MyDesign   →   MyDesign_CUTOUT.png
{Name}_HD.png      +  MyDesign   →   MyDesign_HD.png
```

`OutputFileNaming.BuildProposedFileName` and `BuildCollisionCandidate` now return
`OperationResult<string>`. Call sites propagate the failure:

| Call site | Change |
|---|---|
| `SessionService` — Meitu Enhanced/Cutout name | failure returned as the step's failure |
| `SessionService` — Photoshop production TIFF name | failure returned as the step's failure |
| `FileWorkspace.ReserveOutput` — collision candidate | failure returned from the reservation |
| `MeituWorkstationSmoke` (opt-in, inert by default) | asserts success before use |

Programmer errors stay exceptions: a production TIFF requested without a target width, or an
unknown `NamingArtifactKind`, still throw. A bad *pattern* is data, not a programmer error, and
is reported.

---

## 5. Invalid-pattern behaviour

The renderer fails closed, deterministically, with `FailureCode.PreconditionNotMet` — an existing
code already mapped to an operator-facing message in both `Strings.resx` and
`Strings.zh-CN.resx`. No new failure code, no new UI string, no `catch (Exception)` anywhere.

| Case | Example | Result |
|---|---|---|
| Unknown token | `{Foo}_HD.png` | refused — names the token and the supported set |
| Wrong case / spacing / specifier | `{name}`, `{ Name }`, `{Name:X}` | refused |
| Positional syntax | `{0}_HD.png`, `_{0:D2}` | refused |
| Unmatched `{` | `{Name_HD.png` | refused, with position |
| Unmatched `}` | `Name}_HD.png`, `{Name}_HD.png}` | refused, with position |
| Nested / empty braces | `{{Name}_HD.png`, `{}_HD.png` | refused |
| Empty or whitespace pattern | `""`, `"   "`, `null` | refused |
| Renders to nothing | token whose value is empty | refused |
| Path-bearing render | `Sub/{Name}.png`, `../{Name}.png`, `C:{Name}.png`, `{Name}?.png` | refused — "naming patterns name files, never paths" |

### Path authority preserved (§7)

The renderer emits **bare file names only**: a rendered result containing any of
`< > : " / \ | ? *`, or any control character, is refused. It sits upstream of — and does not
replace — the workspace's own guarantees, all of which are untouched by this change:

- managed `Workspace` references and `PathGuard.ResolveWithinRoot` containment;
- Attempt-directory isolation (uniqueness still comes from the attempt directory, not the name);
- `ReserveOutput`'s `FileMode.CreateNew` atomic reservation and `_02`/`_03` collision walk;
- HD/CUTOUT sibling rules and the never-overwrite guarantee;
- `neverRenameSource`.

`NamingContractBoundaryTests.No_source_formats_a_naming_pattern_positionally` scans every `.cs`
file under `src\` and `tests\` and fails if any naming `…Pattern` is ever handed to
`string.Format` again.

---

## 6. Real-preset regression coverage (§9)

The gap R2 exposed is closed from both ends.

**The fixture now speaks the accepted language.** `PresetFixture`'s
`storageAndNamingContract` is generated from `AcceptedNamingContract`'s constants, so the entire
existing integration and UI suite — `SessionServiceHarness`, `HomeScreenHarness`,
`MeituOutputNamingTests`, `BackgroundRemovalUiTests` and the rest — now runs against the accepted
naming syntax. The identity in that fixture stays synthetic; the signed production manifest is
still never copied into the repository.

**`AcceptedNamingContractTests`** loads those values through the real
`WorkstationPresetProvider` (hash-verified, read-only) and asserts the rendered results:

| Test | Proves |
|---|---|
| `The_accepted_enhanced_pattern_names_the_HD_output` | provider returns `{Name}_HD.png` → renders `Example_HD.png` |
| `The_accepted_cutout_pattern_names_the_CUTOUT_output` | provider returns `{Name}_CUTOUT.png` → renders `Example_CUTOUT.png` |
| `The_accepted_tiff_and_collision_patterns_render_from_the_same_loaded_set` | `Example_280mm_CMYK_W.tif`, `Example_HD_02.png` |
| `The_transcribed_contract_matches_the_configured_manifest` | the constants equal the real manifest's bytes |

The last test reads the manifest at the path `appsettings.json` configures — on this workstation,
the accepted v1.8.0 file — and compares all four patterns character for character. It was
verified to actually engage that branch here: altering one transcribed constant to
`{Name}_CUT.png` failed the test against the manifest's real `{Name}_CUTOUT.png`.

**`NamingContractWorkflowRegressionTests.The_accepted_preset_drives_the_fake_workflow_to_a_cutout_review`**
goes further: where the configured manifest is present, `SessionService` is built on a real
`WorkstationPresetProvider` over those exact accepted bytes, hash-verified against the configured
digest, and the full Fake workflow is driven to `PF_ACCEPTED_HD.png` and `PF_ACCEPTED_CUTOUT.png`.
This was confirmed to take the real-manifest path on this workstation.

No second fake syntax is hard-coded anywhere in these tests.

---

## 7. Fake Enhancement regression (§10)

`NamingContractWorkflowRegressionTests.Fake_enhancement_produces_one_HD_revision_and_leaves_its_upstream_alone`
— real `SessionService`, real workspace, real SQLite, fake adapter:

| Requirement | Result |
|---|---|
| current upstream → Enhancement | PASS |
| separate `<Name>_HD.png` | `PF_NAME_HD_HD.png`, present on disk |
| `ReviewRequired` | PASS |
| no `FormatException` | command returns success |
| exactly one output Revision | 1 |
| correct Attempt directory | Revision path contains the succeeded attempt's id, area `Working` |
| source/upstream unchanged | upstream Revision SHA-256 re-hashed from disk and unchanged; operator's source file byte-identical |

---

## 8. Fake Background Removal regression (§10)

`NamingContractWorkflowRegressionTests.Fake_background_removal_produces_one_CUTOUT_revision_and_leaves_its_upstream_alone`
— the R2 crash point, through the service:

| Requirement | Result |
|---|---|
| reviewed upstream → explicit Background Removal authority → RemoveBackground | PASS |
| separate `<Name>_CUTOUT.png` | `PF_NAME_BR_CUTOUT.png`, present on disk |
| `ReviewRequired` | PASS |
| no `FormatException` | command returns success |
| exactly one output Revision | 1 |
| correct Attempt directory | Revision path contains the succeeded attempt's id, area `Working` |
| source/upstream unchanged | reviewed Revision SHA-256 re-hashed from disk and unchanged; cutout is a different path; source byte-identical |

The pre-existing `MeituOutputNamingTests` and `BackgroundRemovalUiTests` — including
`The_full_fake_mode_journey_reaches_an_approved_transparent_cutout` — now exercise the accepted
syntax too, and pass.

---

## 9. WPF crash regression (§11)

`tests/PrintFlow.Tests/Integration/Ui/NamingContractCrashRegressionTests.cs` drives the real
`SessionViewModel` over the real `SessionService`, workspace and database. `OutputFileNaming` is
never called directly.

| Test | Sequence | Expected |
|---|---|---|
| `Running_background_removal_from_the_screen_reaches_cutout_review` | Fake mode → import → confirm → run/approve Enhancement → prepare Background Removal → authorise displayed Revision/hash → **Run** | command completes; `Notice` null; `IsReviewRequired`; persisted step `ReviewRequired`; cutout named `r2-cutout_CUTOUT.png` |
| `Running_enhancement_from_the_screen_reaches_HD_review` | Fake mode → import → confirm → **Run** | HD `ReviewRequired`, `r2-hd_HD.png` |
| `An_unrenderable_cutout_pattern_becomes_a_notice_rather_than_a_crash` | as above, with a hash-verified preset supplying `{Foo}_CUTOUT.png` | step fails; screen shows a `Notice`; no Revision written; **no exception leaves the command** |

`RunStepCommand` is awaited directly, so any exception escaping it fails the test as an unhandled
exception — exactly what it did to the process at R2. `screen.IsFakeProcessing` is asserted: no
production Meitu is registered, launched or driven anywhere in this file.

The third test is what makes the fix durable rather than merely correct for today's manifest: a
future pattern that genuinely cannot render reaches the operator as a notice, not a process fault.

---

## 10. Gates

Run on the final fix tree, per-user .NET 10 muxer:

| Gate | Result |
|---|---|
| `dotnet restore --locked-mode` | passed; all five projects restored under locked mode |
| `dotnet build` | **0 warnings, 0 errors** |
| `dotnet test` | **8,298 passed, 0 failed, 0 skipped** |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages in all five projects |
| `git diff --check` | clean; no whitespace error |

Baseline before the fix was 8,242 passed / 0 failed / 0 skipped. The fix adds **56** tests and
removes none.

`Adapters.Mode` remained `Fake` throughout. No production Meitu was launched.

---

## 11. Evidence unchanged (§13)

Verified after the fix:

| Check | Actual | Result |
|---|---|---|
| Configured preset version | `1.8.0` | PASS |
| Configured manifest SHA-256 | `DE76464F011A54F80704BB6C32A2E0D00EFF9AB24834FF7D05EF8E9CF3DB60E4` | PASS |
| Computed manifest SHA-256 | `DE76464F011A54F80704BB6C32A2E0D00EFF9AB24834FF7D05EF8E9CF3DB60E4` | PASS — identical |
| `sourceManifestIntegrity` | 18 entries; **18/18 exact hashes matched** | PASS |
| Meitu executable | `C:\Users\admin\AppData\Local\MeituApp\XiuXiu\7.8.7.5\XiuXiu.exe` | PASS |
| Meitu executable SHA-256 | `D65C6D82323275361EA0ADFBB3F6A5C0D2A5CF4CF63EA3AF1A7DDD4544B037B1` | PASS |

The accepted manifest was opened read-only and never written to, reformatted, or copied into the
repository. No baseline or evidence file changed.

**No preset version change was necessary.** No workstation UI evidence changed: the fix touches
filename rendering and its failure path only. The one operator-visible surface it can reach —
`FailureCode.PreconditionNotMet` — was already present and already localised in en-US and zh-CN.

**No sign-off required by project policy** (unchanged from R2).

### Startup recovery (§12)

Not redesigned and not touched. The R2 crash proved recovery behaves correctly, and the existing
tests for `Running → Interrupted`, no fabricated Revision, lock release and quarantine remain
green in the 8,298.

---

## 12. Remaining Final Gate Closure R3 work

Not done in this task, by instruction:

1. en-US / zh-CN A–I human visual inspection of the WPF shell;
2. the post-visual controlled Meitu regressions;
3. any further R2 follow-ups the gate carries.

One Fake-mode functional confirmation was performed and no further QA was resumed: the previous
crash point now reaches CUTOUT `ReviewRequired`, proved through the real `SessionViewModel` Run
command and, separately, through `SessionService` built on the real hash-verified accepted v1.8.0
manifest.

---

## 13. Git state

- Branch `master`, no history rewritten, nothing pushed.
- `a9b99cc` (R2 report) untouched — not amended, not reordered.
- New local commits only.

### Files added

| File | Purpose |
|---|---|
| `src/PrintFlow.Domain/Files/NamingPatternRenderer.cs` | the single naming authority |
| `tests/PrintFlow.Tests/Fixtures/AcceptedNamingContract.cs` | accepted patterns + the check against the real manifest |
| `tests/PrintFlow.Tests/Fixtures/StubNamingPresetProvider.cs` | a verified preset with test-chosen patterns |
| `tests/PrintFlow.Tests/Unit/Naming/NamingPatternRendererTests.cs` | renderer contract and refusals |
| `tests/PrintFlow.Tests/Integration/Preset/AcceptedNamingContractTests.cs` | real provider → real patterns → real names |
| `tests/PrintFlow.Tests/Integration/Persistence/NamingContractWorkflowRegressionTests.cs` | Fake Enhancement and Background Removal |
| `tests/PrintFlow.Tests/Integration/Ui/NamingContractCrashRegressionTests.cs` | the R2 WPF crash regression |
| `tests/PrintFlow.Tests/Architecture/NamingContractBoundaryTests.cs` | no `string.Format` over a naming pattern |

### Files modified

| File | Change |
|---|---|
| `src/PrintFlow.Domain/Files/OutputFileNaming.cs` | renders through the renderer; returns `OperationResult<string>` |
| `src/PrintFlow.Domain/Outputs/NamingPatternSet.cs` | `DesignDefault` migrated to the accepted syntax |
| `src/PrintFlow.Workflow/Services/SessionService.cs` | propagates naming failures for Meitu and Photoshop outputs |
| `src/PrintFlow.Infrastructure/Workspace/FileWorkspace.cs` | propagates collision-candidate failures |
| `tests/PrintFlow.Tests/Fixtures/PresetFixture.cs` | fixture migrated to the accepted syntax |
| `tests/PrintFlow.Tests/Fixtures/SessionServiceHarness.cs` | optional preset provider override |
| `tests/PrintFlow.Tests/Fixtures/HomeScreenHarness.cs` | optional preset provider override |
| `tests/PrintFlow.Tests/Integration/Preset/WorkstationPresetProviderTests.cs` | assertions against the accepted patterns |
| `tests/PrintFlow.Tests/Unit/Naming/SanitiserTests.cs` | result-based naming; refusal cases |
| `tests/PrintFlow.Tests/Smoke/MeituWorkstationSmoke.cs` | asserts naming success (opt-in, inert by default) |

Not touched: `printflow-workstation-v1.8.0.json`, any baseline or evidence artefact,
`appsettings.json`, startup recovery, the workflow engine, and the R2 report.

---

## Verdict

**11300-FINAL-BLOCKER-FIX PASS — READY FOR FINAL GATE CLOSURE R3**
