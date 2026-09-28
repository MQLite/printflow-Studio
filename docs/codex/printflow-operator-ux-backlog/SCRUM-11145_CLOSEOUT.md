# SCRUM-11145 bounded regression and evidence closeout

Task `PF-OPUX-v1-SCRUM-11145-closeout-v1`, 2026-09-25 NZ. Executes the owner-submitted `printflow-remediation-prompts/PrintFlowStudio_SCRUM11145_Bounded_Closeout_Prompt_v1.md`. Corrective continuation of the uncommitted SCRUM-11145 work only: no new feature, schema, migration, approval semantics, workflow transition, PNG approval repair, SCRUM-11147/11148, production launch, desktop input or Git mutation. Technical closeout only; not operator acceptance or production readiness.

Raw evidence: `artifacts/pf-opux-scrum11145-closeout/`. Scoped diff (19 files, measured against the pre-task working tree): `closeout-scoped.diff`, SHA-256 `424378c718ef67b7a09f299ade7431784afa33e456d1136358b42d55885a543d`. The round-1 reviewed diff is `closeout-scoped-r1.diff`; the difference between them is `correction-delta-r2.diff`.

## Baseline, route and delta plan

- `D:/Repositories/printflow-Studio`, `master`, HEAD `eea60198c5095243f0694717a7479b00c3e1cd73`, 50 dirty/untracked entries preserved. Pre-task hashes of 838 tracked/unignored files: `baseline-all-files.sha256` (2026-09-25T10:14:54+12:00). Pre-task copies of each file this task touched: `pretask/`. Final comparison `candidate-changed-files.txt`: 19 files changed or added, none removed. Everything else is byte-identical, including the faulty SCRUM-11145 readback JSON and CSV (`faulty-artifacts.sha256`).
- Route: approved local Claude adaptation v1.2 of policy v2.4, `route_offset: 0` (direct task instruction). The prompt's Sol/Astra 6.0 High map to Opus High under the standing Claude profile. NormalRoute = RequestedRoute = ExecutionTarget = Opus/High for all work. ActualRoute: host-reported model `claude-opus-5-5`; effort metadata UNVERIFIED. AdjustmentResult UNCHANGED. No Sonnet unit (nothing mechanical enough), no effort above High. Context CONTINUE; one fresh read-only independent review (FRESH_REQUIRED).
- Delta plan, executed in order: (1) reproduce and fix the label loss plus the validation gap; (2) reproduce and fix the language leak, then one combined run; (3) verify and resolve the two architecture failures; (4) reassess the P3 items against AC and design, red tests before fixes; (5) settled-source validation, review, one Jira comment, readback and export.

## 1. Label corruption — root cause, fix, proof

| Stage | Observed |
|---|---|
| Authenticated raw response `artifacts/pf-opux-scrum11145/jira-final-readback-raw.json` | 17 label arrays, 51 elements, all `System.String` (`label-loss-trace.txt`) |
| Transformer (a scratch `Build-Readback.ps1`, preserved at `original-tools/`) | `Strip` sends array items through `ForEach-Object`. `[pscustomobject]` is the `PSObject` type accelerator, so the pipeline-wrapped strings match `-is [pscustomobject]` and each is rebuilt from its only property, `{"Length": n}`. **This is the first lossy step.** Scalars passed as arguments (descriptions, timestamps) were not affected. Labels are the only scalar-bearing array path in the response. |
| Snapshot `SCRUM-11145_JIRA_READBACK.json` | 51/51 label elements are objects |
| Exporter | `[string]` cast gives `@{Length=n}`; it checked only "not empty". The unmodified exporter reports **PASS** and `VERIFIED_CURRENT` on the damaged file (`red-exporter-accepts-length-objects.log`). |
| Earlier verification | Its whole-object drift check did flag all 17 issues; the notes attributed that to a comparison artefact. It was this defect. |

Corrections:
- `Build-JiraReadback.ps1` (new, in the initiative directory; the original transformer existed only in a session scratchpad) uses exact runtime types (`PSCustomObject`, `object[]`) and no pipeline. Only `self`/`iconUrl`/`avatarUrls`/`expand`/`avatarId` are dropped. Task, phase, source and marker are parameters.
- `Export-Wave1Jira.ps1`: new `Get-LabelStrings` refuses any non-string, blank or `;`-containing label element. After writing, it checks the Labels cell against the snapshot. Task mapping added for `PF-OPUX-v1-SCRUM-11145-closeout-v1`. Schema, timestamp handling and overwrite protection are unchanged.
- `Test-JiraSnapshotFidelity.ps1` (new) is an independent oracle. It parses the raw response and the snapshot with `System.Text.Json`, never `ConvertFrom-Json`. It checks:
  - snapshot vs raw: structural equality except transport fields, with label kinds and values;
  - CSV vs raw: UTF-8 BOM and the exact quoted 23-column header; ID, key, URL, type, summary, the exact description, and AC as an exact ordinal excerpt of the raw description; priority, status, parent, inward Blocks IDs and keys, the Labels multiset, and the exact `updated`/readAt strings;
  - IDs, keys and Planning-IDs are unique.
- `Test-JiraExportIntegrity.ps1` (new) is the regression script, **19/19** (`export-integrity-03.log`; the intermediate `-01` failure was a whitespace defect in the test's own oracle):
  - synthetic fixture: the three labels stay exact strings; single-element, number, null and empty arrays survive; the original transformer is rejected (red control);
  - preserved raw response → corrected projection → exporter → oracle: PASS on 17 issues, 51 string labels, 23 columns and 34 exact timestamp strings;
  - rejections of the supplied Length-object snapshot and of a mixed string/object array, by both the exporter and the oracle;
  - CSV controls: a Length-text Labels cell, a normalised timestamp, a missing BOM, a changed ID, a changed key, a changed description, AC not taken from the description, a dropped dependency; plus an unchanged re-serialised control that must PASS.
- Reconstruction of the original readAt (evidence only, not a current export): `reconstructed-20260924T032506124Z-readback.json` and `-final.csv`. The snapshot's phase and source say "RECONSTRUCTION, NOT A FRESH READ". The CSV's `Readback_Status` column is the exporter's fixed value and describes that original read only.
- Faulty `SCRUM-11145_JIRA_READBACK.json`/`_FINAL.csv` are preserved byte-unchanged as history. **Label-integrity limitation:** their Labels are invalid; for current use they are superseded by the closeout readback and CSV below.

## 2. Language leak in combined runs

- Producers: `OperatorWave1Tests.CultureScope` and `WorkflowPurposeCardTests` captured `OperatorCulture.Current`. With no operator selection, that is the ambient fallback, and restoring it turned "follow the ambient UI culture" into an explicit en-US selection. The Wave1 focus test also calls `LocalisationService.Use`, which sets the process-wide `CultureInfo.DefaultThreadCurrentUICulture` and never restored it.
- Consumers: about 25 later tests in the same sequential `SqliteCollection` set `CurrentUICulture` and read English. This matches the original `affected-ui-01.log`.
- Fix: a shared `Fixtures/OperatorCultureScope` captures the raw `_selected` (reflection, as `FinalSaveUiTests` already did), the default UI culture and the thread UI culture, and restores all three on dispose. Both classes use it through `using` or `finally`. Nothing is forced to English. There is no collection or parallelism change: all involved classes already share the sequential collection.
- Proof: `OperatorCultureIsolationTests` runs the real producer methods in a fixed order, then a consumer check. It was red before the class change (`culture-red-01.log`: raw selection `en-US`) and green after (`culture-green-01.log`). A second test proves exact restore of a prior explicit selection on an exception path.
- One combined noninteractive process with both classes included: **415/415** (`combined-ui-02.trx`, filter `combined-ui-filter-final.txt`), including OperatorWave1Tests 21, WorkflowPurposeCardTests 6, OperatorCultureIsolationTests 2, FinalSaveUiTests 39 and the former victims (MaximumBoundsUiTests 30, TiffFinalReviewModeTests 14, HomeRecentAccessibilityTests 7). The earlier `combined-ui-01` (415/415) is preserved; it also ran one startup-based settings test, which the final run excludes.

Explicit environment exclusions, verified from source (`real-localappdata-observation.txt` shows the real `%LOCALAPPDATA%\PrintFlow Studio` lease/instance files last written on 2026-09-21/22, untouched by these runs):

| Excluded | Reason |
|---|---|
| ErrorDetailsRenderingTests | shows real windows and drives UIA; also composes `ApplicationStartup` |
| KeepOriginalExtentUiTests, ManualCropAdjustmentUiTests, PrintDimensionsPreflightUiTests, SharedReviewAuthorityTests | `Window.Show()` plus UIA on the live desktop in ordinary `[Fact]`s |
| OperatorWave1AHostTests, `Category=OperatorInteractive` | real host window and operator-interactive input |
| SessionSmokeTests, HomeAndWorkflowSelectionTests | run the production `ApplicationStartup` graph, whose workstation lease manager points at the real `%LOCALAPPDATA%` database. HomeAndWorkflowSelectionTests also drives import/session creation and a production-readiness refresh. |
| SettingsAndLocalisationTests.Composed_diagnostic_paths_render_read_only_and_copyable_in_both_languages | the only included-class method that runs `ApplicationStartup` (temp layout, fake instance guard); excluded for consistency with the startup boundary |

These are environment-safety exclusions, not failing tests; none is a language class or a final-save test.

## 3. Architecture failures

- Migration list, confirmed stale: `MaximumBoundsBoundaryTests` pins the newest embedded script by explicit name. The accepted 0018 adds three tables (`ArtifactDelivery`, `DeliveryAttempt`, `DeliveryRequestAlias`) with indexes and immutability/forward-only triggers; it has no ALTER, rebuild, backfill or cascade. The expectation now names `0018_artifact_delivery.sql` with that reason, and the method is renamed accordingly. Ordinal discovery and the explicit-name contract are kept (no count/maximum assertion). The SQL, checksums, `user_version` and schema are untouched.
- Native declarations, confirmed organizational: the rule is a real inventory boundary ("which OS calls can this application make" in one file), not a false positive. The 11 `DllImport`s and the 6 structs they marshal moved into `Automation/NativeMethods.cs`, byte-identical except `private` → `internal` (mechanical block comparison). `WindowsDeliveryNative` keeps every rule: reparse and case-sensitivity refusal, `RootDirectory` + leaf rename with `ReplaceIfExists = FALSE`, and exact held deletion. The added `GetFileInformationByHandle` overload resolves by its explicitly typed `out` at every call site. No rule was weakened and no file was whitelisted.
- Results: architecture **452/452** (`architecture-03`). Delivery backend, real-NTFS adapter (10 cases in GUID-owned temp workspaces), migration (68) and composition: **146/146** (`backend-native-03`). Temporary SQLite only.

## 4. P3 reassessment

| Finding | Observable scenario | Clause | Evidence | Disposition |
|---|---|---|---|---|
| R4 — one history row | Two unresolved saves of one result: only the newer is shown; the older's path and DeliveryId are unreachable, and after checking one the other stays hidden. The path of an older verified copy is also hidden. | Design §7.2 (unresolved attempts as "Check this save again" bound to their DeliveryId; prior deliveries rendered with destinations); prompt §6 | red `p3-red-01` | **FIXED:** a compact "Recorded saves of this result" selector, shown only for more than one record. Each record keeps its own DeliveryId/path; choosing one changes only the view. Existing guarded actions then act on that record. The default record order is unchanged. |
| T2 — collision during recheck | Reconciling a ready attempt meets a foreign file: the attempt stays unresolved, but the screen dropped Check again until a reload. | AC4, design §6 | red `p3-red-01`; scripted adapter `RacedPublicationFileSystem` over real NTFS | **FIXED:** the collision keeps DeliveryId and Check again when its record is unresolved. The foreign file is untouched; there is no new delivery. |
| T3 — save plus refresh failure | A save throws and the session reload fails: the earlier history was shown with no marker, and with no record the section claimed "Approved, but not saved to your folder yet". A failed history read also erased earlier history. | Prompt §6, design §7.3 truthfulness | red `p3-red-01` (both variants) | **FIXED:** retained history of the same result is kept and marked "could not be refreshed… may be out of date". An empty or prepublication-only stale history shows that notice instead of "not saved"; the status panel makes no unsaved claim. Drafts are unchanged; retry reuses the RequestId. |
| F12 — ineligible results | A rejected size has no save target; is the reason visible? | AC7 | new test, passes on unchanged code | **NOT_A_CONTRACT_DEFECT:** the Outputs row states Rejected / No longer valid / sent to the Recycle Bin, with no save or open action. PNG refusals appear in the section. The unreviewed-PNG path stays blocked. |
| Late progress | Progress from a finished operation arrives while another operation runs. | Design §7.2 | deterministic single-threaded test; mutation runs | **NOT_A_CONTRACT_DEFECT (coverage added):** passes on unchanged code. Removing only the id/reference filter is not observable, because late reports write to the finished operation's own state. Routing late events to the current operation fails the test (`late-progress-mutation-current-op.log`). |
| Review P3-1 — the new list's language | A runtime language switch did not refresh the new list labels. | AC8 | red `records-language-red.log` | **FIXED** in `OnOperatorLanguageChanged`. |
| Review P3-2 — collision wording after recheck | Immediately after the recheck the section says "not saved" (true: the no-replace rename was refused); a reopened screen says "may have been saved" for the same unresolved record. | §6 | review | **DEFERRED_WITH_REASON:** both statements are truthful and the recovery action is identical. Unifying them needs a new status wording, which is cosmetic. |
| Review P3-6 — chosen record persists | After choosing an older verified record, the status panel speaks about it while a newer unverified record is listed as "Not verified". | — | review | **DEFERRED_WITH_REASON:** it matches the existing "a refresh never moves the operator away from an item they chose" rule. Nothing untruthful is claimed. |
| Existing residual | A prepublication collision leaves an Intent attempt; its later Retry can report "may have been saved". | — | 11145 RESULTS | **DEFERRED_WITH_REASON:** backend (accepted SCRUM-11144) behaviour, conservative, no data risk. Not authorized here. |

Unchanged safety properties: exact revision+hash approval; no default/Enter approval or save; fresh-gesture identities (recorded actions include the DeliveryId); once-only PNG preparation; immutable requests and RequestId reuse only for an unchanged intent; the recorded destination is never silently changed; no recopy; the PNG approval gap stays blocked.

UI evidence: `rendered/` contains 18 off-screen captures (en/zh-CN, 1000×700 and 1920×1040, 96 DPI). The new `records` state was inspected: labels wrap, the section and status panel agree, the review column is unchanged. The first capture attempt failed the clipping check on long paths and was fixed with a wrapping item template (`final-save-01.log`).

## 5. Validation (settled source)

| Group | Result | File |
|---|---|---|
| Export integrity script | 19/19 | `export-integrity-03.log` |
| Final-save (coordinator, UI, composition) + language isolation | 61/61 | `final-save-05.trx` |
| Combined noninteractive UI, both language classes included | 415/415 | `combined-ui-02.trx` |
| Architecture | 452/452 | `architecture-03.trx` |
| Delivery backend + real NTFS + migrations + composition | 146/146 | `backend-native-03.trx` |

Groups overlap and are not summed. Red evidence is kept: `red-exporter-accepts-length-objects.log`, `fidelity-original-faulty.*`, `culture-red-01`, `p3-red-01`, `records-language-red`, and the two late-progress mutation logs.

Process note: `Copy-Item` keeps timestamps, so restoring a file after a temporary mutation can leave MSBuild with a stale DLL. The mutated builds were fresh writes. After each restore, a later genuine source edit (or an explicit timestamp bump) forced an App rebuild before any result used above. One confounded red/green pair for the language test was discarded and rerun.

Not run: full suite; the excluded classes above; any desktop, picker, Explorer or physical input; production App, database or customer files.

## 6. Independent review

One fresh read-only `personal-dev-reviewer` context (requested Opus High; actual model UNVERIFIED), given the prompt, AC, design, scoped diff and raw evidence only; it did not rerun tests. Report: `review.md`.
- Round 1: **CLOSED**, no P0–P2, six P3. P3-1/3/4/5 fixed; P3-2/6 dispositioned above.
- Recheck of the correction delta (same context, so a continuation rather than a new fresh review): **CLOSED**. No P0–P2 and no new P3; P3-1/3/4/5 confirmed fixed; P3-2/6 accepted as the recorded dispositions. The reviewer noted that build logs cannot show whether the App DLL was rebuilt. That limits only the filter-only mutation observation.

## 7. Jira and exports

- Two complete pre-write reads of SCRUM-11145 (issue 10875), the second immediately before posting: To Do, `updated` 2026-09-24T15:24:54.578+1200, only comment 10169, no closeout marker (`jira-prewrite-1.json`).
- One corrective comment: **[10170](https://yituoxx.atlassian.net/browse/SCRUM-11145?focusedCommentId=10170)**, marker `PF-OPUX-v1-SCRUM-11145-closeout-v1`, created `2026-09-25T11:21:37.346+1200` (`jira-comment.md`, `jira-comment-write.json`). Comment 10169 and every earlier comment are untouched. No status, AC, label, link, parent or field change, no other issue touched, and no new issue.
- Authenticated initiative readback after the comment: one page, `isLast=true`, **17 issues / 26 Blocks**, all To Do, comment containers complete. The raw response is preserved byte-identical as `artifacts/pf-opux-scrum11145-closeout/jira-closeout-readback-raw.json`. readAt **2026-09-24T23:21:49.205Z** (the host result file's write time).
- [SCRUM-11145_CLOSEOUT_JIRA_READBACK.json](SCRUM-11145_CLOSEOUT_JIRA_READBACK.json) (SHA-256 `2c93cd3b…69bc4`): built by `Build-JiraReadback.ps1`; marker found exactly once, as comment 10170.
- [SCRUM-11145_CLOSEOUT_JIRA_FINAL.csv](SCRUM-11145_CLOSEOUT_JIRA_FINAL.csv) (SHA-256 `1bdc71c0…980d82`): exact 23-column schema, UTF-8 BOM, 17 rows with 17 unique IDs, keys and Planning-IDs, 26 Blocks, 34 exact raw timestamp strings; exporter PASS (`jira-export.log`).
- Independent oracle against the raw response: **PASS** (`jira-closeout-fidelity.json`). 51/51 labels are JSON strings in both raw and snapshot. Each CSV Labels cell equals that issue's raw labels (`operator-ux;pf-opux-v1;printflow` for all 17, taken from the response, not a fixed list). IDs, keys, full descriptions and AC excerpts, parents, Blocks relationships and raw timestamp strings all match. One EVIDENCE_COMMENT_ONLY row (SCRUM-11145) and 16 READ_ONLY rows.
- Drift since the 2026-09-24T03:25:06.124Z read (`jira-drift.json`, `jira-verification-notes.md`): only SCRUM-11145 `updated` → 2026-09-25T11:21:37.346+1200 and comment 10170. The issue-level `created` field appears in this read only because it was requested this time; that is a field-selection difference, not drift.
- Earlier exports and snapshots are unchanged. The faulty 11145 pair is preserved with the label limitation recorded in §1; the closeout pair supersedes it for current use.

## 8. Remaining failures and human acceptance

- Remaining automated failures in the executed groups: none.
- Open, not unmet by this task: the PNG approval gap (KeepOriginalExtent unreviewed PNG stays ApprovalEvidenceMissing); the deferred P3 wording items above.
- **Human acceptance NOT RUN:** Explorer selecting the delivered file, real folder picker, physical keyboard/mouse (fresh gesture, held keys, double-click, stale release), bilingual layout at 1920×1080 / 96 DPI on the supported workstation, removable-drive disconnect, novice walkthrough. Wave1A post-focus-fix desktop checks and SCRUM-11146 human acceptance remain open as recorded in HANDOFF.md.
