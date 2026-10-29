# SCRUM-11155 — remaining Epic work and preparation result

2026-10-05 (Pacific/Auckland). Task `PF-OPUX-v1-SCRUM-11155-preparation-v1`. **PREPARATION VERIFIED / HUMAN WALKTHROUGH NOT RUN — PARTIALLY COMPLETE overall.** This record contains no new human, filesystem-processing, application or production acceptance evidence. Documentation verification and the independent preparation review are complete; SCRUM-11155 and the Epic are not complete.

## Current authenticated Jira state

The current session read Epic SCRUM-11139, SCRUM-11154, SCRUM-11155 and a complete one-page list of all 16 children. The parent search returned `isLast=true`. Privacy-minimized requirement evidence: `artifacts/pf-opux-scrum11155/jira-requirements.redacted.json`. No Jira mutation has been performed.

| Issue(s) | Jira status observed | Remaining assessment / source |
|---|---|---|
| SCRUM-11139 | In Progress | Every child Done or explicitly descoped; genuine novice acceptance run; production safeguards retained. None of these is inferred from this preparation. |
| SCRUM-11140 | In Review | Terminology reference exists and is nonblocking. Assess any remaining semantic decision against [reference](../../printflow/operator-ux-terminology.md); this record does not grant owner acceptance. |
| SCRUM-11141 | In Progress | [Discovery script](../../printflow/operator-ux-novice-walkthrough.md) is prepared, with no actual participant record. A later acceptance run cannot retroactively supply a pre-change baseline. |
| SCRUM-11142–11148 | In Review | Existing implementation/verification records remain historical evidence. Assess original AC and remaining owner/physical acceptance; do not redo completed implementation merely because Jira has not reached Done. See dated [HANDOFF](HANDOFF.md), delivery/correction/trim records. |
| SCRUM-11149 | In Review | Guidance implementation has technical evidence; recorded human check and novice confirmation remain open. [Plan](SCRUM-11149_PLAN.md). |
| SCRUM-11150 | In Review | Size guidance has technical evidence; AC6 workstation/scaling and human assessment remain. F6 and N3 also affect sizing observations. [Plan](SCRUM-11150_PLAN.md). |
| SCRUM-11151 | In Review | Failure-copy implementation has technical evidence; owner copy and recorded diagnostic-navigation/legacy-detail limits remain open. [Plan](SCRUM-11151_PLAN.md). |
| SCRUM-11152 | In Review | Technical signal closeout complete historically; AC5/owner physical acceptance remain. [Closeout](SCRUM-11152_CLOSEOUT.md). |
| SCRUM-11153 | In Review | Technical Recent status evidence exists; physical/human acceptance remains and N1 contradicts Interrupted-state consistency. [Results](SCRUM-11153_RESULTS.md). |
| SCRUM-11154 | In Review | Latest recheck: AC1/AC3 OPEN, AC2 PARTIAL, AC4 RECORDED. N1–N4 and observations need disposition; B4 FAIL; partial/blocked checks retained. [Latest direct record](SCRUM-11154_ZH_RECHECK_RESULTS.md). |
| SCRUM-11155 | To Do | Real novice acceptance walkthrough not run; depends on implementation Tasks and 11154. This preparation does not satisfy the human completion criteria. |

Only SCRUM-11155 is numbered after 11154 within this Epic. This document interprets the current request as remaining work in the same Epic, pending scope clarification; no later Epic was started.

## Prerequisite findings retained

| Finding | Verified predecessor evidence, not re-observed now | Next work / route when selected |
|---|---|---|
| N1, P2 | Recent shows unknown for Interrupted whereas Session groups it with stopped/failed | Diagnose the Recent projection and add a focused regression; separable read model Sol Medium, any UI/visual work Astra High. |
| N2, P3 | Diagnostic preview items expose the CLR record representation as accessible names | Diagnose and correct accessible item naming; UI unit Astra High. |
| N3, P3 | Output size text displays an unrounded long millimetre value | Preserve exact stored/technical values; diagnose presentation formatting; UI unit Astra High. |
| N4, P1 pending diagnosis | Interactive fake processor copies PNG bytes into a `.tif`; UI claims verified TIFF, approval occurs, delivery then refuses | Investigate synthetic processor versus pre-review validation authority. No safe TIFF acceptance claim until addressed. Sol High for separable host/validation logic; Astra High for mixed UI or critical safety review. |
| F6, known negative | 200×150 mm maximum-box metadata disagrees with an actual 6×5 TIFF | Keep FAIL/known-negative evidence; diagnose before claiming print-size or full TIFF success. Sol High or Astra High according to real cause/risk. |
| Recovery / approval / binding | Missing current Recovery fixture; PNG KeepOriginalExtent approval gap, legacy binding/generic recovery and historical recovery hang remain | Separate safe diagnosis and sufficient contract tests; do not unexclude a hanging/unsafe test or claim a skipped path passed. |

User authorization covers automatic work within the selected scope, subject to Route Rules. The prior handoff's stop point is historical; it is not a request to stop the current task. Scope/participant questions remain pending. No finding has been fixed or waived by this preparation, and no visible current-run authority is inferred.

## Original SCRUM-11155 AC assessment

| AC | Current result | Evidence / limit |
|---|---|---|
| 1 Per-scenario independent completion, help, wrong actions and discovery comparison | NOT RUN | [Extended script and empty record](../../printflow/operator-ux-acceptance-walkthrough.md) provide fields; every participant observation is UNOBSERVED. No discovery comparison data exists in the current record. |
| 2 Failed scenarios retained with proposed follow-up | NOT RUN for novice results | Predecessor findings above are retained distinctly. The human finding table is empty; no new novice failure or PASS is invented. |
| 3 No operator available → NOT RUN, Epic not operator-validated | NOT RUN; participant availability not yet confirmed | The question is pending. This record makes no claim that a suitable person is unavailable and no operator-validation claim. |
| 4 No customer content or identifying screenshots | Preparation scope only | New documents use synthetic identifiers and anonymous placeholders; no new image/screenshot captured. Final human record will need its own privacy check. |

## Verification and route

- Preparation scope: issue-specific plan, Chinese facilitator script, anonymized observation/results template, current remaining-work map and initiative handoff checkpoint.
- Fresh documentation verification: all local links resolve; scope snapshot has 16 children and exact parent/issue identity; 12 scenario prompts and 12 NOT RUN/UNOBSERVED rows; AC1–4 assessment present; privacy-minimized snapshot contains no user/account metadata fields; `git diff --check` passed. The initial privacy text scan matched the legitimate word `DisplayNames` in original requirements; the corrected check inspects quoted metadata field names and passes.
- One [independent preparation review](SCRUM-11155_PREPARATION_REVIEW.md) found no actionable defect. This is document/requirement review only. Build, product tests, fresh isolated preparation and UI session: NOT RUN (Markdown-only preparation does not require a product build/test run).
- Routing v2.4, route_offset 0. Documentation NormalRoute/RequestedRoute Sol 6.0 Medium; root in-place switching unavailable (`MODEL_SWITCH_UNAVAILABLE`); ActualRoute UNVERIFIED, no claim of a real switch. Fresh native reviewer requested `gpt-6-sol`/high, `fork_turns=none`, with no parent history; reviewer ActualRoute UNVERIFIED because runtime model/effort metadata was not exposed. Mechanism: isolated native subagent, not a new main chat.
- Git baseline: master @ `de7bca6e0a8462584b4d6bf4d16307d1335e2fb3`; twelve pre-existing modified audit files preserved. No commit, push, deployment, normal App startup, production change, Jira transition/closure or external message performed.
- Integrity: all 1,013 existing tracked files verified byte-identical before the checkpoint. Only the existing HANDOFF receives a dated prefix; its complete previous bytes are checked separately. Four new Markdown documents contain this plan, script, results and independent-review summary. Source/test/configuration paths are unchanged.
- Execution host: local Windows workspace `D:/Repositories/printflow-Studio`; no host transfer. Re-evaluation after preparation selects Sol High for requirements review and retains the human validation boundary; no UI implementation unit or critical repair was executed. Original design and goals were reused; the extension updates the old script's historical FUTURE scenarios without rewriting its observations.

Next: confirm intended scope and participant availability, resolve/qualify prerequisites, then conduct the genuine human walkthrough. If the required participant or qualification is absent, report Partially Complete with human acceptance NOT RUN; do not close the issue or Epic from this document.
