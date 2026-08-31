# Epic 11400 — Photoshop production TIFF automation: final QA and release gate

**Verdict: EPIC 11400 PASS WITH NOTES — READY FOR EPIC 11500**

Epic 11400 gives PrintFlow Studio the ability to turn an approved design into a validated
production CMYK + W1 TIFF using a real Adobe Photoshop CC 2019, to review that TIFF against its
exact bytes, and to promote or dispose of it. This gate verified the finished stack against the
configured immutable preset, a live Photoshop, a live Maintop, and the operator's own eyes in both
locales.

**Epic 11400 passing does not mean production mode is deployed.** `Adapters.Mode` remains `Fake`
and `FoundationEnvironmentGate` still refuses every Production adapter through normal composition.
Opening that is Epic 11500's work.

---

## 1. Final architecture

The finished shape, and the boundaries that hold it in place:

| Layer | What it may do | What it cannot do |
|---|---|---|
| `PrintFlow.Domain` | Records, geometry, naming, hashes | No I/O |
| `PrintFlow.Workflow` | The pure engine, `SessionService`, exact-hash review, Revision authority, transaction ordering | Parses no TIFF, drives no application |
| `PrintFlow.Infrastructure` | Photoshop/Meitu automation, TIFF parsing, workspace, Recycle Bin, SQLite | Constructs no Revision, no `PrintOutput`, no `ReviewDecision` |
| `PrintFlow.App` | Screens and commands | No `System.IO`, no hashing, no file movement, no Recycle Bin |

Verified this gate, by 287 passing architecture tests plus direct source inspection:

- **Photoshop automation is Infrastructure-only.** `DoJavaScript` appears in exactly three files,
  all in `Infrastructure/Adapters/Photoshop/`, and each call passes a program built by a fixed
  builder from a **typed command record** — `PhotoshopPreparationProgram.Create(command)`,
  `PhotoshopProductionTiffProgram.Create(command)`, `PhotoshopW1Program.Create(command)`. There is
  no `ExecuteScript(string)` and no `ExecuteAction(string, …)`: an arbitrary script surface is not
  expressible, not merely unused.
- **`WhiteUnderbaseBranch` is the only W1 selector.** `PhotoshopW1Program.ActionName` maps the
  branch through the *verified preset contract* and refuses any branch the contract does not map
  exactly once. No caller supplies an action name.
- **No coordinate or mouse automation**, and no arbitrary filesystem path reaches Photoshop: the
  seams take `WorkspaceFileRef` and managed commands.
- **No Save As / TIFF options surface.** The save is one fixed call with fixed options.
- **`SessionService` is the sole Revision authority.** `Revision.Create(`/`new Revision(` appear
  only in the Domain definition, in `SessionService`, and in the SQLite mapper that rehydrates
  stored rows.
- **Workflow and App parse no TIFF.** No `BinaryReader`, `ReadUInt16/32`, photometric or
  samples-per-pixel handling exists outside Infrastructure.
- **Exact-hash review is Workflow-owned** (`RevisionIntegrityGuard`, reached through
  `SessionService.EnsureIntegrityAsync`).
- **No ViewModel references `System.IO`.**
- **No process-termination capability.** `Process.Kill`, `TerminateProcess`, `taskkill` and
  `CloseMainWindow` are absent from product source, asserted at IL level by
  `ForceTerminationPolicyBoundaryTests` (15 tests).
- **`File.Delete(` appears once in product source**, in `FakeAdapterExecution`'s
  `ProduceMissingFile` scenario — a deterministic double that makes an output vanish so failure
  handling can be tested. It never touches a Revision, an approved file or a rejected one, and is
  exempted by name so a second exemption is a visible edit.
- **The EnvironmentGate cannot be bypassed by production components.** No type in the Photoshop
  adapter namespace takes, holds or returns an `IEnvironmentGate`; only the composition root names
  it, to register the one real implementation.

## 2. Accepted v1.14.0 evidence

Verified before the live work and **re-verified unchanged afterwards** (§30):

| Item | Expected | Result |
|---|---|---|
| Manifest SHA-256 | `F74792276C0B264C9F064D1C82CB26806F7B836A543E0AF5FC8B4E7FB1738C62` | **exact** |
| `sourceManifestIntegrity` entries | 26 | **26/26 exact, 0 mismatch, 0 missing** |
| Manifest file mode | read-only | `-r--r--r--` |

Every entry was recomputed from the file on disk, not read back from the manifest.

**One honest observation, non-blocking.** 10 of the 26 referenced evidence files carry the
read-only attribute; 16 do not — the older Epic 11000/11300-era baseline files
(`workstation.json`, `displays.json`, the Meitu editor evidence, both `clean-start.json` and
`window-policy.json` pairs, `document-workspace.json`) and the repository's
`PRINTFLOW_STUDIO_MVP_DESIGN_EN.md`. Every Photoshop-era file added during Epic 11400 *is*
read-only. All 26 hash exact, and the preset's own `immutability.filesystemPolicy` states
"SHA-256 remains authoritative" — so nothing has changed and nothing is unverifiable. Recommended
as an operator tidy-up, not a release blocker: marking the remaining 16 read-only changes no bytes
and no hash.

**No new preset was minted.** This gate discovered no runtime fact product code must rely on; it
re-exercised accepted routes. v1.14.0 stands.

## 3. Photoshop identity

| Item | Preset value | Observed |
|---|---|---|
| Path | `D:\Adobe Photoshop CC 2019\Photoshop.exe` | exact |
| `productVersion` | `20.0` | `20.0` |
| `fileVersion` | `20.0 (20200706.r.120 2020/07/06: 1208496)` | identical |
| SHA-256 | `81EE8930FC1E28637B501866A8B946FA0740C376CDA4302FEA61AA82806A80C5` | **exact** |

The binary is unchanged and no Photoshop update was accepted. A note on the brief's wording: the
accepted evidence records `20.0` plus that build string; **`20.0.10` appears nowhere in the preset
or on the binary**, and the authority is the SHA-256, which matches exactly.

**Canonical Action:**

| Item | Expected | Observed |
|---|---|---|
| Path | `…\actions\authoring\PrintFlow-DTF-v1.atn` | exact |
| Bytes | 1636 | 1636 |
| SHA-256 | `A04203EDEA623C0737D911601A3A005033789BD095130F02A5F8C04CBFCD83EE` | **exact** |
| Set name | `PrintFlow DTF` | as recorded |
| Actions | `W1_0px`, `W1_1px`, `W1_2px` | as recorded |

The runtime Action transcript is the accepted `cmyk-w1-action-runtime.json`, which is integrity
entry 2 and hashed exact. Both live runs this gate drove real Actions through that contract
(`W1_2px` and `W1_1px`) and the adapter's own identity rule accepted them. The operator's Action
set was not modified, reloaded or replaced.

## 4. Flexible sizing

Both accepted modes verified — 49 flexible-size tests, 106 maximum-bounds tests, 104 preset tests,
all passing, with the preset suite reading the **real configured v1.14.0 manifest** rather than a
fixture.

**PresetFit.** Recommendations come from v1.14 inheritance. The manifest's `limitsMillimetres`
gives `A3_LANDSCAPE` and `A3_PORTRAIT` a two-sided box (360×280, 280×400) and `A4`/`A5` a
long-edge-only recommendation (280 mm, 135 mm), asserted directly against the shipped file.
Nominal ISO millimetres are not executable authority: `SetPresetFitSize` carries the preset and
nothing else, and `SessionService` resolves the recommendation.

**CustomTargetEdge.** Width, Height and LongEdge; preset override; ResolutionOnly, Shrink, and
Enlarge only under explicit separate authority. Projection is exact rational arithmetic
(`round-half-away-from-zero(mm × 1500 / 127)`) at a fixed 300 PPI. A stale source or target
invalidates the authority; a same-content retry retains it; `AddAnotherSize` clears the plan,
selection, authority and branch so the second output is its own decision.

**No operator resampling-method selector exists** anywhere in the shell — asserted, and confirmed
by the visual review.

Both modes were also exercised live this gate: Job A took A5's configured recommendation, Job B a
custom 50.8 mm Width overriding A4 (§7).

## 5. B1A.3 — preparation

Real Photoshop geometry at 300 PPI with factual read-back, unchanged and re-exercised by both live
jobs. Job A's trimmed 2000×1300 source became **1594×1036 px @ 300 DPI** under A5's 135 mm long
edge; Job B's 1200×800 @ 240 ppi source became **600×400 px @ 300 DPI** under a 50.8 mm width. Both
shrank; neither needed enlargement authority.

## 6. B1B — CMYK + W1

The accepted Action ran in both jobs with factual validation. Job A used `W1_2px` and produced a
spot channel with 1 651 384 non-white samples; Job B used `W1_1px` with 240 000. Both were verified
as genuine Photoshop spot channels named exactly `W1`.

## 7. C1 — TIFF structural validation of the current outputs

**Not inherited from the C1 slice.** Both TIFFs produced this gate were validated by the C1 parser
at save time, and the approved deliverable was then **re-read and re-validated after promotion**:

```
re-validated TIFF    : …\Approved\PF_FG_20260901-103804-0000B7E5_135mm_CMYK_W.tif
byte order           : IBM PC / little-endian
pixels               : 1594x1036
resolution           : 300x300 (unit 2)
samples              : 5 x 8/8/8/8/8 bit
photometric          : 5   compression 1   planar 1
extra channels       : W1
W1 spot / non-white  : True / 1651384
alpha / pyramid      : False / False
layers / all RLE     : 1 / True
bytes                : 12879148
```

Every fact §14 names: TIFF, little-endian, exact pixels, 300 DPI, image compression None
(`Compression = 1`), interleaved (`PlanarConfiguration = 1`), five 8-bit samples, separated CMYK
(`PhotometricInterpretation = 5`), exact `W1` identity as a Photoshop spot channel, non-empty W1
fifth sample, no alpha, no image pyramid, one IFD, one wholly RLE-compressed layer, and the
expected byte length and SHA-256.

Job B's TIFF carried the same structure (600×400, 2 452 724 bytes, SHA `4173C22C…7B9B`) before it
was rejected.

## 8. C2A — Revision and ReviewRequired

Unchanged. Each successful attempt left exactly one Revision, one `PrintOutput` at
`ReviewState.NotReviewed`, a succeeded attempt pointing at it, and the step in `ReviewRequired`,
with the TIFF in the attempt's own `Working\<attemptId>\` directory. Both live jobs reproduced this
exactly.

## 9. C2B — approval and rejection lifecycle

Unchanged, and re-proved live.

**Approval** (Job A): reserve the `Approved` destination atomically → persist the reservation →
copy the bytes → independently re-hash → commit the review, move `PrintOutput.File` and mark the
step Approved. Result: one Approved file, byte-identical to the reviewed TIFF, one content
Revision, one `ReviewDecision`, and `Session.State = Completed`.

**Rejection** (Job B): verify the hash → send the exact reviewed TIFF to the Windows Recycle Bin →
commit the decision, `RecycledAtUtc` and `RetryRequired`. Result verified against the **real**
Recycle Bin:

```
Name             : PF_FG_20260901-103851-FB933082_51mm_CMYK_W.tif
OriginalLocation : D:\PrintFlowStudio\QA\Epic11400FinalGate\B-print-tiff-…\Working\01a059f9-…
DateDeleted      : 2026/9/1 10:39
Size             : 2.33 MB
```

Restorable, not deleted. Retry then produced a fresh attempt with the size decision and W1 branch
retained and no reconfirmation demanded.

**Review-integrity regression (§16):** a TIFF mutated in `Working` cannot be approved — refused
with `RevisionIntegrityMismatch`, the Revision invalidated, nothing promoted, no Approved review —
and cannot be rejected either, with the Recycle Bin never called. Both asserted in
`PhotoshopTiffFinalReviewTests`, against Fake-produced bytes; the real Final-QA TIFFs were not
mutated.

**Collision and idempotency (§17):** an existing Approved filename is never overwritten; approval
takes `{Name}_02.tif` and the persisted `PrintOutput.File` is the file actually written. A replayed
approval is refused by the existing transition table and creates no second copy, review or
Revision.

## 10. Operator UI — human visual inspection

Real WPF application at the accepted 1920×1080 viewport, driven by a person through all fourteen
states A–N in both locales, against the Fake adapter.

| Locale | States A–N | Result |
|---|---|---|
| en-US | preset cards, A4/A5 recommendations, custom Width/Height/Long Edge, preset override without enlargement, enlargement warning, enlargement authorised, ReviewRequired TIFF, Approve, Reject + reason, Approved/Completed, Rejected/RetryRequired, Add Another Size | **PASS** |
| zh-CN | same fourteen | **PASS** |

Checked in both: wording natural; no clipping or overlap; actions understandable; **no resampling
terminology exposed**; the enlargement warning promises no quality outcome; the TIFF review states
structural validation only; and the Working / Approved / Recycle Bin location text is truthful.

**A finding recorded and then corrected.** Mid-gate this workstation appeared to be locked to
en-US: `CultureInfo.CurrentUICulture` resolved `en-US` for every newly started process while
formats and the system locale were `zh-CN`, which would have meant the zh-CN satellite never loads.
That reading was wrong. The persisted setting (`HKCU\Control Panel\Desktop\PreferredUILanguages`)
was **already** `zh-CN`; only the running session predated it. Refreshing the session made the
application render Chinese correctly, and the operator confirmed it visually. **There is therefore
no en-US CurrentCulture limitation to carry into Epic 11500**, and the note §34 offers for it does
not apply. No persisted setting was changed: the language registry state after this gate is
byte-identical to the pre-work probe (`PreferredUILanguages = zh-CN`,
`MuiCached\MachinePreferredUILanguages = en-US`, `WinSystemLocale = zh-CN`). Newly launched
applications now honour the machine's own configured zh-CN preference, as they would after the
operator's next sign-in.

The runtime database was backed up before the review
(`Data\printflow.db.gatebackup-20260901-104508`). The review's synthetic sessions remain in the
operator's workspace and can be removed at will.

## 11. Controlled Production workflow matrix

Synthetic data only. Global Production was **not** enabled: both jobs ran through the controlled
seam, with `Adapters.Mode = Fake` asserted by the smoke itself and a
`ControlledSeamEnvironmentGate` declared in the test project and passed to those two
`SessionService` instances.

| | Job A | Job B |
|---|---|---|
| Workflow | **PREPARE_CUSTOMER_DESIGN** | **GENERATE_PRINT_TIFF** |
| Source | 2400×1600 @ 300 ppi, transparent border | 1200×800 @ 240 ppi |
| Upstream | skipped Meitu; real deterministic Trim → 2000×1300 | confirmed original |
| Sizing | preset fit, A5 configured recommendation (shrink) | custom Width 50.8 mm overriding A4 (shrink) |
| W1 branch | `W1_2px` | `W1_1px` |
| Produced | 1594×1036, 12 879 148 B, SHA `DD02D164…AD60` | 600×400, 2 452 724 B, SHA `4173C22C…7B9B` |
| Decision | **Approve** → Approved + Completed | **Reject** (`WhiteInkIssue`) → Recycle Bin + RetryRequired → Retry |
| Workspace | `QA\Epic11400FinalGate\A-customer-design-20260901-103804-0000B7E5` | `QA\Epic11400FinalGate\B-print-tiff-20260901-103851-FB933082` |

Together they cover both workflows, a preset fit and a custom override, two W1 branches, and both
review outcomes. The B1A.3 geometry matrix, all three W1 branches and the C1 parser matrix were
deliberately **not** repeated: those slices accepted them, and this gate consumes them.

**Approval end-to-end (§11), independently verified outside PrintFlow:**

```
sha256sum Approved/*.tif Working/*/*.tif
dd02d16440ff0b1a709f14049d5c871e5da9dd2086f39aff4c9c98b5b760ad60  Approved/…_135mm_CMYK_W.tif
dd02d16440ff0b1a709f14049d5c871e5da9dd2086f39aff4c9c98b5b760ad60  Working/01a059f9-…/…_135mm_CMYK_W.tif
```

Exactly one TIFF Revision, exactly one `PrintOutput`, exact hash preserved Working → Approved, no
second Revision from promotion, one `ReviewDecision`, one file under `Approved\`, session
`Completed`, and the Working Revision still integrity-readable.

**Rejection/retry end-to-end (§12):** performed for real, against the real Recycle Bin, as above.
It stops once the fresh retry attempt is established. Producing a second real TIFF would re-prove
C1's save and validation contract and add nothing about the rejection lifecycle — which §12
permits, and which the deterministic C2B tests already cover including the failure and both crash
points.

**PREPARE_ASSET regression (§13):** unaffected. Its final approved-PNG path still works under Fake
mode, it requires no `PhotoshopOutput`, and no TIFF lifecycle leaks into it — in particular a
rejected intermediate PNG is **retained for comparison**, never sent to the Recycle Bin, which is
the MVP design §10 rule for Meitu-derived files. Asserted by
`PREPARE_ASSET_review_and_completion_are_unchanged`.

## 12. Maintop compatibility — current generated TIFF

Performed manually by the operator on **Maintop v6.1**, using a TIFF generated *by this gate*
rather than the historical Epic 11000 baseline. Maintop was not automated and no production job was
printed.

| Item | Result |
|---|---|
| File | `…\Epic11400FinalGate\A-customer-design-…\Approved\PF_FG_20260901-103804-0000B7E5_135mm_CMYK_W.tif` |
| Loads | **yes**, no format rejection or error |
| Preview renders | **yes** |
| Reported dimensions / channels | correct — 1594×1036 @ 300 DPI, CMYK + `W1` spot |

**PASS.** A current production TIFF from the finished Epic 11400 pipeline is accepted by the RIP.

## 13. Failure and recovery

Focused regressions re-run this gate, all passing:

| Area | Suite | Tests |
|---|---|---|
| Photoshop identity, document, window, modal, target loss, resize/W1/save failures | `Automation` | 698 |
| Photoshop foundation, preparation, W1, TIFF, workflow output, final review | `Photoshop` | 294 |
| Architecture boundaries | `Architecture` | 287 |
| Stop / take-over | `StopAndTakeOver` | 52 |
| Force-termination policy | `ForceTermination` | 15 |
| Final-review lifecycle and faults | `PhotoshopTiffFinalReviewTests` | 24 |
| Startup recovery | `StartupRecovery` | 9 |
| Database invariants | `DbInvariant` | 9 |

Covered: wrong executable; multiple process candidates; wrong or stale document; same basename in a
different folder; blocking modal; target/window loss; cancelled resize; W1 Action failure; invalid
or missing W1; TIFF save failure; non-RLE TIFF; invalid TIFF structure; TIFF SHA mismatch;
promotion failure; recycle failure. **No failure fabricates `ReviewRequired`, `Approved` or
`Completed`** — every one returns a structured failure and creates no Revision.

**Promotion crash/recovery (§18)** — crash before reservation or copy; crash after the persisted
reservation and copy but before the review commit; restart after a completed approval; copy
failure; promoted-hash mismatch. In every case: no duplicate approved bytes, no false Approved
state, no second Revision, deterministic recoverable state. The persisted reservation is what makes
the second case resume into the same file instead of claiming `_02`.

**Rejection crash/recovery (§19)** — recycle failure; crash before recycle; crash after recycle
before the commit; restart after a completed rejection. All deterministic.

**Restart and idempotency (§24):** a Running attempt before TIFF success closes as `Interrupted`
with no fabricated Revision; a valid TIFF on disk before the success commit is never adopted, it is
quarantined; a `ReviewRequired` restart reloads the same Revision; an Approved restart reloads the
same Approved file, review and session state; a `RetryRequired` restart reloads the same decision.
No successful restart reruns Photoshop.

## 14. Migration chain

Newest migration: **`0007_print_output_promotion.sql`**. 59 migration tests pass, including two
added at this gate to close a coverage gap C2B left — its schema change had no dedicated upgrade or
trigger test.

| Requirement | Covered by |
|---|---|
| Empty database → newest | `Empty_database_migrates_successfully` |
| Older databases → newest | pre-0002, pre-0003, pre-0005, pre-0006, **pre-0007** |
| 0005 → 0006 → 0007 in order | pre-0005 and pre-0006 replay through to newest |
| Sessions/revisions/attempts/**outputs** survive | `A_pre_0007_database_upgrades_and_keeps_its_outputs` *(new)* |
| Historical output gains no promotion in flight | same — `PromotionReservedPath` reads NULL |
| PrintOutput identity trigger | `A_PrintOutputs_location_may_move_but_its_identity_columns_may_not` *(new)* |
| Promotion reservation round-trips and clears | same |
| Future unsupported schema fails closed | `A_database_user_version_ahead_of_this_build_fails_closed` |

The trigger is the file-location model stated as a database rule: a `PrintOutput`'s `RelativePath`
may move, because approval promotes the deliverable; its `Sha256`, `ByteLength`, `SourceRevisionId`
and `CreatedAtUtc` may not, because approval copies bytes that were already validated. Its
counterpart `Revision_Immutable_Update` forbids a Revision's path from changing at all. No
production database was rewritten by hand.

## 15. `CleanupWorking` — deliberately unwired (NOTE)

`WorkflowEffect.CleanupWorking` is emitted by `WorkflowEngine.Complete` for every workflow and
still has **no production interpreter**; `IWorkspace.CleanupWorking` still has **no production
caller**. Verified by `CleanupWorking_still_has_no_production_interpreter_or_caller`, which fails
the moment either changes.

It is not safe to wire as it stands. `FileWorkspace.CleanupWorking` deletes the whole `Working\`
tree recursively, and since C2A the production TIFF's Revision names a file inside that tree — as
do every Meitu-derived Revision, trim and manual-crop result. Executing it would destroy the
artefacts immutable records point at and break their integrity guard. "Safe to remove" has no
definition anywhere in the system, which is precisely the missing contract.

**Current behaviour, recorded rather than changed:** Working artefacts remain after completion;
their Revision integrity stays valid (asserted by
`Completing_the_session_does_not_delete_the_Working_artefacts`); disk usage grows over the life of
a workspace. This is **deferred workspace-retention work**, not a hidden cleanup feature. It did
not block this gate. It needs its own design task defining reachability against attempt status and
Revision reachability — not a recursive delete bolted onto a release.

## 16. Rejection crash-after-recycle window (NOTE)

Unchanged from C2B, re-reviewed and still acceptable:

- no false rejection state is recorded — the decision simply does not land;
- the disposed bytes are **not** accidentally approvable — the next decision on that step is
  refused with `RevisionIntegrityMismatch` because the artefact cannot be re-read;
- the Revision invalidation is deterministic;
- restoration remains possible from the Windows Recycle Bin, because nothing was hard-deleted.

The alternative ordering — record the rejection first, dispose afterwards — closes this window and
opens a worse one: a Recycle Bin *failure* would then leave a completed rejection over a file still
sitting on disk. This ordering never claims something untrue. **Non-blocking known limitation.**

## 17. Empty Approved reservation residue (NOTE)

`ReserveOutput` claims the destination with `FileMode.CreateNew` before the reservation commit. A
hard process death in that window would leave a zero-byte file in `Approved\`. Reviewed against
§20's criteria:

- the residue contains **no customer TIFF bytes** — it is zero-length, and bytes are copied only
  after the reservation commit lands;
- **no database record points at it** — the commit that would have recorded it never happened;
- the next approval **collision-numbers around it** through the established `_02` contract;
- **no duplicate deliverable bytes** can result.

When the commit merely *fails* rather than the process dying, the reservation is quarantined out of
`Approved\` immediately. **No such residue exists anywhere in the workspace** — a scan of every
`Approved\` directory found zero zero-byte files. No startup cleanup was added, and Final QA was
not expanded into filesystem scavenging. **Operational note only.**

## 18. Foreground dependency (Epic 11500 handoff)

Preserved exactly. If another application owns the foreground when Photoshop requires guarded
input, PrintFlow fails closed with `PhotoshopTargetLost` and `inputSent=false`. Photoshop is never
made to fight for focus, and the deterministic regressions for this remain in
`GuardedPhotoshopUiDriverTests`.

This is a **workstation/operator-readiness requirement for Epic 11500**: the production path
depends on the operator's desktop state at run time. C2A's first live attempt was refused for this
reason; every run since — including both jobs this gate — took the foreground first time. The
refusal is the guard working, not a defect, but Epic 11500's environment verification should state
it as a precondition rather than let an operator discover it.

## 19. Repository and security hygiene

- `git diff --check` — clean.
- **No tracked** synthetic source, generated TIFF, runtime SQLite database (or `-wal`/`-shm`), QA
  workspace, screenshot, smoke transcript, or external preset/evidence file. No tracked `.png`,
  `.jpg`, `.atn` or `.exe`.
- **No secrets or credentials.** Every `token` match in the scan is a local variable
  (`CancellationToken`, a QA name token, an IL token).
- **No machine-specific transient paths in product source.** The only absolute paths in the
  repository are in `appsettings.json` — the workstation's configured workspace root and preset
  pointer, which is the accepted configuration shape, not a leak.
- **No banned process-termination APIs**, asserted at IL level.
- **No direct `File.Delete` for a rejected TIFF** — the single occurrence is the deterministic
  fake's missing-output scenario.
- **No arbitrary Photoshop script or action surface** — every program is built from a typed
  command record.

## 20. Dependency / vulnerability audit

```
dotnet restore PrintFlowStudio.sln --locked-mode
  Restored PrintFlow.Domain, PrintFlow.Workflow, PrintFlow.Infrastructure,
           PrintFlow.App, PrintFlow.Tests

dotnet list PrintFlowStudio.sln package --vulnerable --include-transitive --no-restore
  PrintFlow.Domain          has no vulnerable packages
  PrintFlow.Workflow        has no vulnerable packages
  PrintFlow.Infrastructure  has no vulnerable packages
  PrintFlow.App             has no vulnerable packages
  PrintFlow.Tests           has no vulnerable packages
```

All lock files honoured under `--locked-mode`. **No vulnerable direct or transitive packages.** No
findings, so no disposition is required. This closes the audit deferred from every slice since
B1A.

## 21. Complete suite

Run **once**, after the human visual review, both controlled live workflows, the Maintop check, the
targeted fault and recovery regressions, and after product source was final:

```
dotnet build PrintFlowStudio.sln --no-restore
  0 Warning(s)   0 Error(s)

dotnet test PrintFlowStudio.sln --no-build --no-restore
  Passed! - Failed: 0, Passed: 9763, Skipped: 0, Total: 9763, Duration: 1 m 51 s
```

**0 warnings, 0 errors, 0 failed, 0 skipped. Final test count: 9763.**

Product source was not changed after this run. Five test methods were added during this gate — two
migration tests (§14) and three opt-in workstation-smoke facts (§11, §7) — and no product code was
modified, which is why the gate needed no second full run.

## 22. Epic 11500 handoff

What Epic 11500 receives, and what it must do:

1. **Global Production is closed.** `Adapters.Mode = Fake`; `FoundationEnvironmentGate` refuses
   every Production adapter through normal composition, returning `EnvironmentNotVerified`. The
   controlled seam exists only as a `ControlledSeamEnvironmentGate` declared in the test project.
   Epic 11500 owns real workstation verification.
2. **Foreground ownership** must become part of environment readiness (§18).
3. **Workspace retention.** `CleanupWorking` is unwired and `Working\` grows; this needs its own
   design task (§15).
4. **The immutable evidence chain** — v1.14.0, 26 entries, the Photoshop binary and the canonical
   `.atn` — is what a verified workstation must re-establish. 16 of the 26 evidence files would
   benefit from the read-only attribute the protocol describes (§2).
5. **Locale is not a constraint.** Both en-US and zh-CN render correctly and both have visual
   sign-off (§10).

## 23. Git state

Branch `master`, based on `4f4db5f` ("Report: Epic 11400 C2B Photoshop TIFF final review and file
lifecycle"), present and unmodified throughout. No push, no amend, no rebase, no history rewrite.
No old slice commit was touched.

**Product source: unchanged by this gate.** Final QA discovered no blocking defect requiring a
correction to product code.

**Tests (1 new file, 1 updated):**

- `tests/PrintFlow.Tests/Smoke/PhotoshopFinalGateWorkstationSmoke.cs` *(new — the controlled
  workflow matrix and the post-promotion C1 re-validation)*
- `tests/PrintFlow.Tests/Integration/Persistence/MigrationTests.cs` *(the pre-0007 upgrade and the
  PrintOutput identity trigger)*

**Docs:** this report.

No synthetic input, generated QA TIFF, runtime database, Photoshop workspace, screenshot,
transcript or external preset/evidence file is committed.

## 24. Final external state

| Item | State |
|---|---|
| Photoshop running | **yes**, PID 21244, `D:\Adobe Photoshop CC 2019\Photoshop.exe` |
| Foreground document | `PF_FG_20260901-103851-FB933082.png @ 100% (图层 1, W1/8) *` — Job B's synthetic source, unsaved |
| Synthetic documents open | expected **two** (Job A's and Job B's), both synthetic, both with unsaved in-memory CMYK+W1 changes |
| Operator documents open | **none** |
| Modal state | none observed; Photoshop idle |
| PrintFlow Studio running | no — closed after the visual review |
| Maintop running | no — closed after the compatibility check |
| Retained QA workspaces | `QA\Epic11400FinalGate\{A-customer-design-…, B-print-tiff-…20260901-103821-28783754, B-print-tiff-…20260901-103851-FB933082, ui-review}` plus the earlier `QA\Epic11400C1`, `C2A`, `C2B` |
| Approved QA TIFF | **1** — Job A's, 12 879 148 B, retained |
| Recycled QA TIFF | **1** — Job B's, in the Windows Recycle Bin, 2.33 MB, restorable |
| Runtime database | backed up to `Data\printflow.db.gatebackup-20260901-104508` before the UI review |

**All of the above may safely be cleaned later.** Nothing here is customer artwork and nothing is
manifest integrity evidence. The synthetic Photoshop documents must be closed **manually with
Don't Save** — no signed discard route exists and none was automated. The
`B-print-tiff-…-28783754` workspace is from the aborted first attempt at Job B (a test-authoring
error, not a product failure) and holds no TIFF.

## 25. Slice history and supersession

Historical reports are **not** rewritten. Their verdicts stand as the record of what was true when
each was written; current source, the configured immutable preset, the accepted evidence and the
current tests are the authority now.

| Slice | Verdict as written | Status at this gate |
|---|---|---|
| 11400-A | PASS | in force — Photoshop/process/window/document identity |
| 11400-B1 | **NOT READY** | **superseded.** The W1 Action could not be executed safely under the contract as it then stood; B1B replaced that approach with the accepted preset-bound Action contract |
| 11400-B1A | **NOT READY** | **superseded.** Print dimensions at 300 PPI were unresolved; B1A.1–B1A.2E replaced the exact-pair model with the accepted fit-within-bounds and target-edge contracts |
| 11400-B1A.1 | **NOT READY** — resampling/dimension contract unresolved | **superseded** by B1A.2C, which accepted the flexible-size and limit-override contract and removed the operator resampling question entirely |
| 11400-B1A.2A | PASS WITH NOTES | in force — maximum-bounds workflow and persistence |
| 11400-B1A.2B | PASS WITH NOTES | in force — maximum-bounds operator UI |
| 11400-B1A.2C | PASS — contract accepted | in force — flexible size and limit override |
| 11400-B1A.2D | PASS | in force — flexible-size workflow and persistence |
| 11400-B1A.2E | PASS | in force — flexible-size operator UI |
| 11400-B1A.3 | PASS | in force — real Photoshop preparation and read-back |
| 11400-B1B | PASS WITH NOTES | in force — accepted CMYK + W1 Action |
| 11400-C1 | PASS WITH NOTES | in force — fixed TIFF save and independent validation |
| 11400-C2A | PASS WITH NOTES | in force — AdapterOutput, one Revision, ReviewRequired |
| 11400-C2B | PASS | in force — exact-hash review, promotion, disposal, completion |

Two of C2A's notes were resolved rather than inherited: the TIFF's move from `Approved\` to the
attempt's `Working\` directory is now the accepted placement with promotion on approval, and the
`PrintOutput` area read-model defect it introduced was fixed in C2B.

---

## Notes on the verdict

**PASS WITH NOTES**, for three things that are documented, demonstrated non-blocking, and each
guarded by a test that fails if it silently changes:

1. **`CleanupWorking` is intentionally unwired** (§15) — `Working\` is retained after completion
   and disk usage grows. Deferred workspace-retention work with its own design task.
2. **The rejection crash-after-recycle window** (§16) — deterministic, records nothing untrue, and
   leaves the file restorable from the Recycle Bin.
3. **The zero-byte Approved reservation residue after a hard process death** (§17) — cannot contain
   customer bytes, cannot duplicate a deliverable, and does not currently exist anywhere.

A fourth candidate note is explicitly **withdrawn**: the en-US CurrentCulture limitation is not
real, and both locales have visual sign-off.

A fifth item is recorded as an **operator tidy-up, not a note against the release**: 16 of the 26
integrity-referenced evidence files lack the read-only attribute the baseline protocol describes.
All 26 hash exact, and the preset states SHA-256 is authoritative.

All PASS criteria are met: the final en-US and zh-CN human visual review passed; both controlled
Production Photoshop workflows passed; the current generated TIFF passes C1 structural validation
*after promotion*; the current generated TIFF is accepted by Maintop v6.1; the exact-hash approval
and rejection lifecycle is correct; approval produces one byte-identical Approved TIFF and one
content Revision; rejection uses the real Recycle Bin with no hard-delete fallback anywhere;
restart and fault paths are deterministic; the migration chain passes including two new tests for
0007; v1.14.0 and all 26 evidence entries remain exact after the live work, as do the Photoshop
binary and the canonical `.atn`; the dependency and vulnerability audit passes with no findings;
the complete suite passes 9763/9763 with 0 skipped; the EnvironmentGate remains closed; and no
repository or security blocker exists.

**Epic 11500 is required before production mode may be enabled, and is not started here.**
