# Epic 11400 Part B1 — Photoshop W1 Action Integrity, Execution and Completion

**Verdict: 11400-B1 NOT READY**

Part B1 stopped before Action input. The accepted Action contract itself requires every W1
Action to convert the active document to CMYK, while this slice explicitly prohibits CMYK
conversion and requires colour mode to remain unchanged. Read-only live discovery confirmed the
loaded actions have that same first step. Invoking one would knowingly violate the slice rather
than test it.

No production W1 seam was added, no Action was invoked, no Photoshop document was inspected or
modified, no Action set was loaded or changed, and no file was written. The existing Part A
foundation and fail-closed `IPhotoshopOutputProcessor` remain unchanged.

## 1. Canonical `.atn` verification

The canonical file was read without modification before any runtime Action discovery.

| Fact | Accepted | Observed | Result |
| --- | --- | --- | --- |
| Path | `D:\PrintFlowStudio\Baseline\workstation-v1\actions\authoring\PrintFlow-DTF-v1.atn` | exact | PASS |
| Length | 1,636 bytes | 1,636 bytes | PASS |
| SHA-256 | `A04203EDEA623C0737D911601A3A005033789BD095130F02A5F8C04CBFCD83EE` | exact | PASS |
| Set | `PrintFlow DTF` | encoded exact in the artifact | PASS |
| Actions | `W1_0px`, `W1_1px`, `W1_2px` | encoded exact in the artifact | PASS |

The file was not edited, regenerated, loaded, exported over, or re-saved.

The artifact bytes provide an additional decisive fact. All three actions begin with the event
`convertMode`, whose target mode is `CMYM` (Photoshop's Action Manager identifier for CMYK). The
artifact records these action structures:

| Action | Steps in canonical file | Scope-relevant parameter |
| --- | --- | --- |
| `W1_0px` | convert mode; load transparency; make W1 | target mode `CMYM` |
| `W1_1px` | convert mode; load transparency; contract; make W1 | target mode `CMYM`; contract 1 px |
| `W1_2px` | convert mode; load transparency; contract; make W1 | target mode `CMYM`; contract 2 px |

This agrees with preset v1.9.0's accepted `photoshopActionContract`, which describes “Convert
mode to CMYK” as the first step of every accepted W1 action.

## 2. Runtime Action-set integrity mechanism

Runtime discovery was read-only and attached through the Running Object Table to the already
running Photoshop CC 2019 automation object. It did not activate a COM LocalServer or start a
second Photoshop. Before accepting the automation object, discovery required its application path
to be exactly `D:\Adobe Photoshop CC 2019\`.

The surrounding process identity was also reverified:

| Fact | Observed |
| --- | --- |
| Accepted process candidates | exactly 1 |
| PID | 6272 |
| Executable path | `D:\Adobe Photoshop CC 2019\Photoshop.exe` |
| Product version | `20.0` |
| File version | `20.0 (20200706.r.120 2020/07/06: 1208496)` |
| Executable SHA-256 | `81EE8930FC1E28637B501866A8B946FA0740C376CDA4302FEA61AA82806A80C5` |
| COM-reported version | `20.0.10` |

Fixed internal Action Manager discovery enumerated every loaded set by index, then every action
and command by index. Whole-string names, indices, counts and the command transcript were
captured; no prefix, substring, or first-match resolution was used.

Exactly one exact `PrintFlow DTF` set was loaded. It contained exactly one of each accepted
action:

| Runtime index | Action | Step count | Runtime command transcript |
| --- | --- | --- | --- |
| 1 | `W1_1px` | 4 | `转换模式`; `设置 选区`; `收缩`; `建立` |
| 2 | `W1_0px` | 3 | `转换模式`; `设置 选区`; `建立` |
| 3 | `W1_2px` | 4 | `转换模式`; `设置 选区`; `收缩`; `建立` |

`转换模式` is “Convert Mode.” The exact order, action counts, step counts, and step transcript
match the canonical artifact. This is materially stronger than accepting the displayed set name
alone. Photoshop CC 2019 did not expose each recorded command's complete parameter descriptor
through this read-only palette enumeration, so this evidence does not claim a byte-for-byte
runtime representation. The on-disk artifact remains the parameter authority.

The loaded set was reused for discovery only. Nothing was loaded, removed, renamed, replaced, or
saved in the operator's Action palette. Document count remained 4 before and after discovery.

## 3. Invocation route and rejected alternatives

The evaluated route was Photoshop-native automation using a fixed, operation-specific internal
Action Manager script. It can enumerate action sets/actions and, in a later conforming contract,
can synchronously call the exact accepted action and inspect the exact active document.

This route was preferred in principle because Photoshop CC 2019 exposes no usable UIA tree and the
native API avoids coordinates, mouse input, generic menu timing, and blind shortcuts.

No production invocation route was implemented because the accepted Action is outside B1's
permitted transformation boundary. The following alternatives were rejected:

- Actions-panel UIA: unavailable on this Photoshop build, as established in Part A.
- Coordinate/mouse or generic menu navigation: prohibited and unnecessary.
- Keyboard shortcuts: cannot positively establish exact set/action selection.
- `Photoshop.Application`: registered to the excluded Photoshop 2026 installation.
- COM activation of `Photoshop.Application.130`: its `LocalServer32` points at the stale,
  non-accepted `D:\ps2019\Adobe Photoshop CC 2019\Photoshop.exe`; discovery therefore attached
  only to an already-running object and reverified its reported path.
- A generic script or arbitrary action API: prohibited by the architecture boundary.

## 4. Exact branch mapping

The required closed mapping is unambiguous:

| Domain value | Accepted action |
| --- | --- |
| `WhiteUnderbaseBranch.W1_0px` | `W1_0px` |
| `WhiteUnderbaseBranch.W1_1px` | `W1_1px` |
| `WhiteUnderbaseBranch.W1_2px` | `W1_2px` |

The domain enum has no `Unspecified` member and no default. No mapping implementation was added
because no action may be invoked under the current contract. Infrastructure did not infer, rank,
or recommend a branch.

## 5. Pre-invoke document identity

No pre-invoke phase occurred. Because contract validation failed first, no synthetic Working
document was opened for B1 and the action-input boundary was never reached.

Any future implementation must still begin with Part A's
`KnownEditorWithExpectedDocument`, reverify process/start time and exact main-window ownership,
repeat the absolute-path identity probe immediately before invocation, refuse a switched document,
and check for a blocking modal. None of those controls was weakened.

## 6. Pre-existing W1 guard

No document was inspected because execution was already prohibited by the signed Action contract.
A production pre-existing-W1 guard was therefore not added or claimed. A conforming retry must
still refuse `W1`, near matches, duplicates, or evidence of a partial prior run before Action
input, and must require a fresh Working copy rather than repair an in-memory document.

## 7. Completion rule

No Action was invoked, so no completion claim exists. A future native route must treat synchronous
return as necessary but insufficient, then reverify the process/window, absence of a blocking
modal, exact active document identity, and the expected W1 result.

## 8. W1 spot-channel validation

Not exercised. No claim is made about a W1 channel. Exactly-one, spot-channel type, non-empty
content, owning-document identity, and duplicate rejection remain mandatory for a conforming B1
implementation.

## 9. Before/after document facts

No synthetic document was opened or changed, so there is no before/after document snapshot to
report. The runtime discovery observed only the application and Action palette; it did not read
the names, pixels, paths, modes, dimensions, resolution, bit depth, or channels of any open
operator document.

The contract-level expected transformation is enough to block the live run: starting with the
required non-empty RGB fixture would change colour mode to CMYK. Section 18 requires that behavior
to return `NOT READY`, and the top-level scope says not to perform the conversion in this slice.

## 10. Shared-process safety

Four documents were already open in the shared Photoshop process. Their identities and contents
were not enumerated. The count remained four before and after the read-only discovery.

No document was activated, switched, closed, saved, or modified. Photoshop was not closed or
terminated. `OtherDocumentsMayBeOpen` remains part of the unchanged Part A record, and all Part A
shared-process protections remain in force.

## 11. No-disk-output proof

No Action ran and no B1 synthetic workspace was created. Consequently:

- no Working file was overwritten;
- no TIFF, PSD, PSB, PNG, JPEG, or other output was created;
- no Save or Save As route was invoked;
- no `AdapterOutput` was returned;
- no Revision was created;
- the workflow `IPhotoshopOutputProcessor.GenerateAsync` path remains fail-closed.

## 12. Cleanup behavior

No B1 document was opened, so no cleanup action was necessary. No operator document was closed and
Photoshop was left running exactly as found. There was no unsaved-changes dialog and no discard
control was exercised or assumed.

## 13. Targeted tests and exact counts

No full-suite run was performed.

The following existing focused regressions were run because source implementation stopped before
new W1 code existed:

```text
dotnet test tests\PrintFlow.Tests\PrintFlow.Tests.csproj --no-build \
  --filter "FullyQualifiedName~Photoshop|FullyQualifiedName~AutomationBoundaryTests|FullyQualifiedName~WorkstationPresetProviderTests"
```

Result: **156 passed, 0 failed, 0 skipped**.

This covers the Part A Photoshop identity/foreground/target-loss protections, Photoshop
architecture boundaries, the directly relevant general automation boundary, and configured
preset verification. No `PhotoshopActionContractTests`, W1 mapping/execution tests, or W1 result
tests were added, because adding an invocable route for a known non-conforming Action would not be
a valid partial implementation.

Final normal solution build, using the repo-pinned user-local .NET SDK 10.0.400:

```text
dotnet build --no-restore
```

Result: **0 warnings, 0 errors**.

The first `dotnet build` through `C:\Program Files\dotnet\dotnet.exe` could not resolve the pinned
SDK because that installation contains only SDK 8.0.418. The pinned SDK was already installed at
`C:\Users\admin\AppData\Local\Microsoft\dotnet\dotnet.exe`; no repository or SDK configuration
was changed.

Dependency graph unchanged; vulnerability audit deferred to Epic 11400 Final QA.

## 14. Live results for 0px, 1px and 2px

| Branch | Runtime identity | Invocation | W1 result | Reason |
| --- | --- | --- | --- | --- |
| `W1_0px` | unique exact action; 3-step transcript confirmed | not invoked | not produced | first step converts to CMYK |
| `W1_1px` | unique exact action; 4-step transcript confirmed | not invoked | not produced | first step converts to CMYK |
| `W1_2px` | unique exact action; 4-step transcript confirmed | not invoked | not produced | first step converts to CMYK |

Three synthetic Working documents were not created. Running the live branch smoke would knowingly
perform the prohibited transformation three times and could not meet the PASS criteria.

## 15. Evidence and preset impact

Preset `printflow-workstation-v1.9.0.json` remains immutable and unchanged. Its configured digest
still matches exactly:
`0DA89F8FE4067574FD7568F1FA8D1C0F2000469E3059FEE28BFDB0C867B5FB58`.
`appsettings.json` still selects version `1.9.0`, and `Adapters.Mode` remains `Fake`.

No external evidence or preset file was created or changed. The live findings are recorded only
in this repository report:

- native Action Manager enumeration is available through the accepted running Photoshop object;
- the accepted set/actions are currently loaded uniquely;
- the runtime command transcript confirms Convert Mode is the first command in every branch;
- generic COM activation is not an acceptable production route on this workstation because its
  registrations name excluded or stale executable paths.

A new immutable preset must not be created from the present result because there is no accepted
W1-only action execution route. First the action/scope conflict requires an explicit contract
decision and new live evidence.

## 16. Remaining 11400-B2 and prerequisite scope

Before B1 can pass, choose and accept one of these product-level changes:

1. Author and validate a new canonical W1-only Action set whose actions do not resize, change
   resolution, change colour mode/bit depth, or save. Preserve the current accepted `.atn`, create
   a new immutable artifact and preset version, then rerun B1 against fresh fixtures; or
2. Explicitly move CMYK conversion into B1 and change B1's scope/PASS criteria. This couples two
   transformations and is not the contract requested here.

The first option preserves the intended seam: B1 creates only W1 in memory, while B2 owns print
dimensions, 300 ppi preparation, and CMYK conversion. TIFF export, `AdapterOutput`, workflow
success, and Revision creation remain later work.

The eventual production native route also needs a signed exact-process attachment mechanism that
does not activate the excluded Photoshop 2026 registration or the stale Photoshop 2019 path.

## 17. Git state

Preflight began on branch `master` at `2f8ae7f` (`11400: identify Photoshop and prove which
document it opened`), ahead of `origin/master` by 13 commits, with a clean working tree. This
report is the only repository change from B1. No source, project, dependency, lock, configuration,
preset, or evidence file changed.

No commit, amend, rebase, history rewrite, or push was performed. No synthetic document,
W1-modified file, screenshot, UI dump, runtime workspace, smoke transcript, or external evidence
was added to Git.

11400-B1 NOT READY
