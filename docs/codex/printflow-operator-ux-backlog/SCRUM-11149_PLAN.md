# SCRUM-11149 — review guidance: implementation checklist

Task `PF-OPUX-v1-SCRUM-11149-impl-v1`, Planning-ID `PF-OPUX-v1-review-step-guidance`, 2026-09-29 NZ. Executes `printflow-remediation-prompts/PrintFlowStudio_SCRUM11149_Review_Guidance_Implementation_Prompt_v1.md` (operator-owned, untracked). The owner approved its bounded interaction brief and authorized implementation, verification, independent review, in-scope fixes, direct `master` publication and SCRUM-11149 Jira updates only.

## Baseline

- `master` = `origin/master` = `fddf2434d26d927a798f942678c79072ab3afc99` (fetch and `ls-remote` agree); clean worktree. The prior cumulative publication (through SCRUM-11148) was not repeated.
- Jira pre-read: SCRUM-11149 (id 10879) To Do, 0 comments, parent SCRUM-11139 (In Progress), blocks 11154/11155, no inward Blocks. Transitions discovered live: To Do 11, In Progress 21, In Review 31, Done 41. Moved To Do → In Progress (21) before implementation; read back In Progress.
- Hooks/CI: no `.github/workflows`, no active git hooks. A `master` push triggers no deployment or migration.

## Route

Claude adaptation v1.2 of policy v2.4, `route_offset: 0`. Opus/High for all work; no Sonnet unit. Host model `claude-opus-5-5`, effort UNVERIFIED. Context `CONTINUE`; one fresh read-only `personal-dev-reviewer` (Opus) at the review boundary.

## What the code actually does (copy is written from this, not from the Jira sentences)

| Step | Approve | Reject | Help that exists |
|---|---|---|---|
| Enhancement | Accepts the exact hash; next step waits | `RetryRequired`; nothing runs until the operator chooses | Generic Hand off only; no colleague request |
| Background removal (incl. returned R2) | Exact revision+hash entry; R2 approval continues with the next step, no BR rerun (11148 status line) | Exact revision+hash entry; `RetryRequired` | `Ask a colleague to correct this image` while `SessionView.CanAskColleague` |
| Trim | Exact revision+hash entry; asset route is a final review (save section) | `RetryRequired` (Retry / manual crop offered) | `Adjust trim edges` while `SessionView.TrimAdjustment` exists |
| PhotoshopOutput TIFF | Promotes inside the workspace; not a save, not a quality check | **Recycle Bin first**, then the rejection is recorded; a failed recycle records nothing and leaves the TIFF. Then `RetryRequired` with **both Retry and Run step offered**; no new TIFF until Run step (Retry alone only resets to Waiting) | No import of a manually changed TIFF; Return to an earlier step when a PrintDimensions target is offered |

The Jira draft sentence "Reject makes a new TIFF" and the first implementation draft ("choose Retry, then Run step") were both inaccurate; the test `Tiff_reject_wording_matches_recycle_first_and_no_new_tiff_until_run_step` exposed the second (Run step is offered directly after Reject). Independent review F1 then showed that quoting the existing label "Reject and make another TIFF" before "no new TIFF yet" read as a contradiction. Final copy: "Reject and make another TIFF does not make the new TIFF straight away. It first moves this TIFF to the Recycle Bin, then records the reason you choose below. The new TIFF is made only when you choose Run step; Retry alone does not make one. If the TIFF cannot be moved to the Recycle Bin, nothing is rejected and it stays where it is." The label itself is unchanged (no global rename) and is the first candidate for AC4 walkthrough feedback.

## Implementation

- `SessionViewModel.ReviewGuidance.cs` (new): read-only projection `ReviewGuidance` (None unless `ReviewTargetIdentity` is set and no Ask panel / trim editor / crop is open), 3–4 checks per step with tool names filled from the existing label resources, Approve/Reject/help lines, and `ShowsGuidanceAskColleague` / `ShowsGuidanceAdjustTrim` from the workflow layer's own eligibility. Notified from `NotifyOperatorStatusChanged` and `OnIsCroppingChanged`; language change uses the existing `OnPropertyChanged(string.Empty)`.
- `SessionScreenView.xaml`: one bordered section in the existing right-hand details column, after the final-save section and before "Current file". The two buttons bind the existing `BeginAskColleagueCommand` / `BeginTrimAdjustCommand` with `IsEnabled` = `CanAskColleague` / `CanAdjustTrim`, plain (non-default, unguarded like their bottom-bar twins, which only open a mode).
- `Strings.resx` / `Strings.zh-CN.resx` / `Strings.cs`: 25 `Session_Guidance*` keys in both languages.
- `docs/printflow/operator-ux-terminology.md`: "What to check / 检查要点" row.
- No engine, service, persistence, delivery, correction-protocol, migration, preset or approval-binding change.

## AC → evidence map

| AC | Evidence | Remaining |
|---|---|---|
| 1 checks/explanation/help next to the review, both languages | `Each_review_shows_its_checks_decision_explanation_and_help_in_the_operator_language` (4 steps × en/zh-CN); layout test (16 renders); language-switch test; R2 (returned correction) test; no-colleague fallback test | Human bilingual visual check |
| 2 TIFF Reject text matches behaviour | TIFF reject test: failed recycle records nothing; recycle → RetryRequired, 0 new attempts; Retry alone 0; Run step +1 | — |
| 3 BR help offers colleague correction | BR case asserts the Ask button and help line; help button = same command instance as the bar button, opens the Ask panel, writes nothing | Real colleague round trip (11148) |
| 4 walkthrough-driven revision | Conditional; no walkthrough has happened | Future, via 11155 |
| 5 Approve/Reject behaviour and binding unchanged | No binding/service change in the diff; busy-state test (identities and guarded Reject unchanged); combined UI 554/554 on the settled candidate 02 | Real-window review-authority class not run (shared desktop) |
| 6 not clipped, image not hidden | Image area identical before/after in all 16 cases (tolerance 0.01 px); no over-wide text; section inside the details column; heading visible without scrolling | Workstation DPI/scaling check (human) |

## Human-check addendum (NOT RUN)

Add to the existing human checks, with synthetic images on the supported workstation:

1. At each of the four reviews, in English and 简体中文, confirm "What to check" appears in the right-hand column, reads naturally, and each check names a control that is actually on screen.
2. Background-removal review: press the section's "Ask a colleague to correct this image"; the Ask panel opens with the note box focused and nothing is sent. Cancel.
3. Trim review: press the section's "Adjust trim edges"; the editor opens; Cancel returns to the same review.
4. TIFF review: Reject with the Recycle Bin available; confirm the TIFF is in the Recycle Bin, no new TIFF exists until Run step is chosen.
5. At 1000×700 and the workstation's own scaling, confirm the picture is not smaller than before and the section can be scrolled to its help line.

## Verification

- Red: new tests failed to compile before the implementation (`build-01-red.log`).
- Targeted: 36/36 (`ReviewGuidance*`).
- Settled candidate 02, clean `--no-incremental` build 0 warnings/0 errors; architecture 452/452; combined UI 554/554 (SCRUM-11148 final filter, which keeps `ProductionCompositionTests` and the real-window/UIA/startup classes out). Counts overlap. The baseline recovery hang stays excluded and NOT PASS; workflow/persistence and backend/delivery groups were not rerun because no Workflow, Domain or Infrastructure file changed.
- Renders: 16 off-screen captures at 1000×700 and 1920×1040, 96 DPI; picture area identical to the baseline in all 16.
- Independent review: one fresh read-only reviewer, two rounds; no open P0–P2.

Full evidence: `artifacts/pf-opux-scrum11149/RESULTS.md` (local, not published).

## Publication and Jira

- Code commit `6cbde817a2da7f3565cf52860ca26d79a3e71242` pushed to `master` (fast-forward from `fddf243`). Fetch, `ls-remote` and the GitHub API agree: MASTER_PUSH_VERIFIED.
- SCRUM-11149 transitions: 21 To Do → In Progress (before implementation); 31 In Progress → In Review (after the verified push). Evidence comment 10183 carries marker `PF-OPUX-v1-SCRUM-11149-impl-v1`. No other initiative issue was written.
- Not Done: the issue's Validation needs the human check and walkthrough confirmation, and no acceptance or waiver was given.
- Final authenticated readback: 17 issues, 26 Blocks, 51 string labels. The only drift is on SCRUM-11149 and the embedded 11149 status inside SCRUM-11154/11155 links.
- Outputs:
  - [SCRUM-11149_JIRA_FINAL.csv](SCRUM-11149_JIRA_FINAL.csv): 23 columns, UTF-8 BOM, exporter PASS, independent oracle PASS.
  - [SCRUM-11149_PUBLICATION_STATUS_AUDIT.json](SCRUM-11149_PUBLICATION_STATUS_AUDIT.json).
  - `SCRUM-11149_JIRA_READBACK.json` stays local; it is gitignored because it carries account metadata.
