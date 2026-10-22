# E6 Native execution rulings

These decisions preserve the user's accepted v2 design, continuation authorization and E6 scope. They do not grant release, production or desktop authority.

| Decision | Reason | Cost or remaining uncertainty |
| --- | --- | --- |
| Continue approved plan without another approval round | User accepted E5 and requested E6 with Native execution | Human acceptance still requires actual participant/workstation evidence |
| One sequential implementer, one fresh final whole-branch review owned by controller | Explicit execution choice; no per-task reviewer or extra worker | Final reviewer must examine the full diff and evidence boundaries |
| Use the existing E6 worktree only, retain scratch and ledger | Preserve other user/worktree changes | More retained evidence; no cleanup outside this worktree |
| Every test uses the inherited safe wrapper | Whole Smoke namespace and all 11 exact exclusions stay excluded; no desktop opt-ins | Real input and excluded scenarios remain NOT RUN |
| Characterization may initially pass; product fix requires real RED | Do not invent a failure or edit product code solely to justify the task | Initial visual tests missed image collapse; this limitation is retained |
| Fix observed multi-size image collapse only in App XAML | Two-size Save image fell to 1 DIP at 1000×700 and about 34 DIP at 1280×720; stricter geometry failed 2/3 | Save/Completed output history now requires details scrolling at smaller viewports; both names and scroll reachability asserted |
| Retain one shared output template and earlier review placement | Preserve existing metadata, approved-state naming, invalidity and output facts | Existing earlier-review layout limitations are not claimed solved |
| Correct the new completion test's rendering fixture, not product threading | A model mutated after rendering retained a WPF CollectionView on a retired STA; use the established separate snapshot fixture | Raw 42/45 harness failure retained; no claim of a product threading fix |
| Approved 1000×700 DIP means the outer WPF window | MainWindow Width/Height and approved design; client area is smaller and was not measured | Offscreen 1000×700 UserControl cannot prove actual baseline-window layout; all nine real DPI/window cells remain NOT RUN |
| Build + metadata + Validate only for the owner candidate | PrepareAndSmoke constructs Application/MainWindow and exceeds current offscreen-only permission | Candidate is statically validated, not runtime-prepared or approved for novice launch |
| Keep full Interactive TIFF scenarios blocked until current-host qualification | FakePhotoshop defaults to CopyApprovedInput; headless F2/F3 ValidTiff setup is process-local | N4/F6/PNG/generic recovery and PDF fixture gaps remain explicit; no production-adapter workaround |
| Separate technical evidence from real novice/workstation acceptance | The AC requires real Chinese novices and real device observations | Q01 and Q03 remain partial/NOT RUN; do not close the Epic or invent participants/KPIs |
| Reuse final retained test evidence at task-done rather than rerun unchanged suites | New changes, new failures or unresolved concerns justify testing; repeated identical checks do not | Verifier reads counters, hashes and exact comparisons; broad regression still runs once after code freeze |
| Preserve exact inherited failure names and raw messages | A narrow pass cannot close old failures | Broad suite stays FAILED; no new/missing/changed failure is allowed to disappear from reporting |
| Use PowerShell 7 for evidence helpers | Windows PowerShell 5 default UTF-8 decoding misread Chinese JSON during a ledger check | Failed ledger invocation is a tool/encoding issue, not a product failure; corrected invocation passed |

Routing v2.4 / RouteOffset=0: Q01 UI/test/visual judgment ran in `gpt-6-astra/high`, verified by the controller at `2026-10-22T00:31:56.297Z` (session `01a16105-7e92-78e2-bf65-4a86e5f132c1`). After planning and the context boundary, separable Q02 strategy/evidence would normally use Sol High and mechanical Q03 documentation Luna Low. `MODEL_SWITCH_UNAVAILABLE`: no actual model switch interface in this continuing Native context; a stronger safe substitute was disclosed. No downgrade is claimed. The controller owns the real independence boundary for the final fresh Astra High review.

The retained detailed ledger is copied to [execution-ledger.md](execution-ledger.md) at handoff. Initial compile error, insufficient initial image assertions, actual product RED/GREEN, fixture correction and all final evidence are separately identified.


## Controller closeout rulings

User chose preparation first and real novice testing later on2026-10-22. Record DEFERRED_BY_USER/NOT RUN, not unavailable; participant remains unconfirmed. Sole fresh review:0 Critical/0 Important/1 Minor E6-M1. Keep Minor deferred under Native execution: current candidate is already built, no rebuild required; preserve it and fix candidate-specific evidence isolation/existence protection before future rebuild. Candidate README now gates the unsafe helper. No product/test/source change, broad rerun or second review for documentation-only closeout. [Review and rationale](../../../docs/codex/printflow-operator-ux-backlog/SCRUM-11173_REVIEW.md).


## User-authorized M1 follow-up

User subsequently requested the deferred rebuild helper fixed, continuing Native and deferring human tests. M1 is now RESOLVED: candidate-specific evidence directories, refusal of existing paths and no Force on evidence leaf. Five controlled filesystem cases and one real isolated Build/Validate prove preservation; original103 files unchanged. The one new scoped independent review has0 Critical/Important/Minor. Original overall review remains historical; no product regression rerun or replacement of reviewed candidate. [M1 results](../../../docs/codex/printflow-operator-ux-backlog/SCRUM-11173_M1_RESULTS.md).
