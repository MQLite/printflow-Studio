# PF-ACCEPT-A3 — Normal-App Prepare Design Asset E2E (SCRUM-11130)

## Scope and authority

Prompt 24 authorised one bounded execution: drive the SCRUM-11130 golden path through the ordinary
`PrintFlow.App.exe` of the already-qualified A2 candidate, collect the evidence, and close only A3's
evidenced scope. It did **not** authorise a build, a suite run, a new qualification or publication
cycle, a change to the frozen candidate or preset, an install, deploy, push or Jira transition.
None of those was performed.

The A2 outcome was carried forward, not re-derived. The frozen identities in prompt 24 were
re-verified read-only and all matched; no A1/A2 review, aggregation or record was re-recorded.

## Acceptance clause being tested

Resolved through the existing mapping, not by re-deriving numbers:
`docs/printflow/scrum-11130-prepare-design-asset-fixed-workstation-e2e.md` §1 quotes the complete
CSV criterion for SCRUM-11130 / CSV Work Item 11706. Prompt 24 bounded this execution to that
clause's continuous golden path:

`Home → import → workflow selection / output name → original confirmation → Meitu enhancement →
review → Meitu background removal → review → automatic trim → trim review → approved transparent
PNG export → complete session`

and explicitly excluded print dimensions, TIFF, white ink, Maintop and printing. The clause's
additional variant cases (rejected review, automatic retry, manual takeover, restart recovery,
unknown dialog, output-validation failure) were **not** re-opened here; their existing status is
recorded in that report's §5 and is unchanged by this task.

## Mapping to the current UI

`WorkflowCatalog.PrepareAsset` (src/PrintFlow.Workflow/Definitions/WorkflowCatalog.cs) defines the
six steps the clause names, and the ordinary session screen exposes exactly them:
`导入 → 原图确认 → 高清化 → 去背景 → 裁边 → 导出已审核 PNG`. Enhancement, BackgroundRemoval and Trim
are `RequiresReview: true`, so three real Operator decisions were expected. None was reached, so
none was recorded, and no A1/A2 approval was transferred.

## Fixture

`D:\PrintFlowStudio\TestData\v3\inputs\FIX-FINE-HAIR-001.jpg`, SHA-256
`5A705FE390AF87D1D48A0554D4908C425D4703A8807CA78EC73AC0E55E3C8D8E`, 312,309 bytes, 1200×1600 JPEG.
Its manifest marks it `APPROVED_LOCAL_ONLY`, synthetic, no identifiable person, `gitAllowed: false`,
`uploadAllowed: false`. It was chosen because it is the only approved JPG whose cutout produces real
transparency, which the clause's transparent-PNG ending requires.

A second approved fixture, `FIX-PORTRAIT-001.jpg` (`F4CAD2A1EC7994E4…4634`), was used for one
narrow diagnostic session after the first refusal, with the Operator's agreement.

Both originals are byte-identical after the execution. Restricted pixels stayed local: the retained
Product evidence screenshots were **not** opened by the agent and nothing was committed to Git.

## What was executed, in order

1. Read-only preparation: HEAD `058e912` verified (clean tree; `printflow-remediation-prompts/` is
   git-excluded and was preserved), A2 documents read, frozen identities re-checked.
2. Candidate resolved from the retained run's own `Binding.BuildOrigin` and receipt
   `52E6DBC5…8FBC`, then verified with the existing non-building route
   (`New-PrintFlowBuildPair.ps1 -VerifyOnly`): "Build pair verified:
   d915b1a6-4c06-4b16-9a20-5e1341d1ae9c". Its four assemblies hash-match the active record
   `E6A7D7EA…F3B9`, which was read back unchanged.
3. Explicit exclusive-use / saved-work confirmation obtained from the Operator for A3 before any
   desktop input. A2's completed window was not reused.
4. The exact candidate's `PrintFlow.App.exe` launched normally, no arguments, PID 24528.
5. Ordinary readiness run through the real UI. See HANDOFF.md for the Photoshop fault, its
   diagnosis and the Operator-assisted recovery, and for the passing gate at 14:53.
6. Session `A3-FINE-HAIR-001` created and driven through the ordinary UI to the enhancement step,
   which refused. Diagnosis, one narrow second session, and the stop decision are in HANDOFF.md.

## Verdict

**FAILED at the Meitu enhancement clause.** The golden path did not complete, so no approved
transparent PNG and no reviewed-hash binding exist, and SCRUM-11130 is **not** FULL. The failure is
a live, reproducible refusal at the Meitu document-identity probe: the signed baseline expects
Meitu's Save default to be `<basename>_副本` and Meitu supplied the bare basename, so the Product
would not claim the document and never attempted the AI enhancement. Recorded under its own time and
identity; it does not alter A2's historical pass, which remains valid for its own observation.

---

## Addendum — 21 September 2026: Prompt 24 Revision 3 rerun

Prompt 24 Revision 3 authorised one further bounded execution on top of the original scope: a
narrowly scoped Meitu save-mode setup through Meitu's ordinary UI, then one fresh continuous
ordinary A3 run. It still did **not** authorise a build, a suite run, a new qualification, a
publication or revocation, a preset/evidence edit, an install, a deploy, a push or a Jira
transition, and it explicitly did not authorise Prompt 25. None of those was performed.

The frozen identities in §2 of the prompt were re-verified read-only and all matched: pair receipt
`52E6DBC5…8FBC` (`New-PrintFlowBuildPair.ps1 -VerifyOnly` → "Build pair verified:
d915b1a6-4c06-4b16-9a20-5e1341d1ae9c"), active revalidation record `E6A7D7EA…F3B9`, preset
`printflow-workstation-v1` 1.18.0 `8484F0AA…C8E0F`, Meitu executable `9276B407…6B0B`. The
existing full suite was not rerun.

### The Operator's finding was correct

Meitu's Save panel carries a **保存路径** selector with three options. Its exact labels on this
build are **自定义**, **覆盖原图** and **桌面** — the Operator reported the wrong mode as
「覆盖原文件」, and the substance of that report is confirmed; only the wording differs.

Reproduced on a disposable synthetic setup copy, with no fixture involved:

| 保存路径 | Unedited default `fileNameEdit` |
|---|---|
| 覆盖原图 (found selected) | `PF_SETUP_A3R3_7F2C` |
| 自定义 (restored) | `PF_SETUP_A3R3_7F2C_副本` |

That is the whole of the 18 September refusal. The signed baseline's `outputBaseNameSuffix: "_副本"`
is accurate for Custom mode; Meitu had simply been left in Overwrite mode, where it offers the bare
base name. The earlier speculative readings — baseline drift, a collision-conditional suffix, or a
too-narrow comparison — are **withdrawn as the default next action**. They were honest hypotheses
recorded as untested, and nothing in them is now claimed to have been a false observation.

### Outcome

**PASS for the bounded golden path.** Session `A3-R3-FINE-HAIR-20260921` ran
`Home → import → Prepare Design Asset / output name → original confirmation → Meitu enhancement →
review → background removal → review → automatic trim → trim review → approved transparent PNG →
complete session` in one continuous run with five SUCCEEDED attempts, no failures, no retries, and
three real hash-bound Operator approvals. See HANDOFF.md for the identities, the Photoshop
installation-path blocker, the Operator actions and the remaining limitations.

This does not rewrite the two previous A3 failures, which remain FAILED and preserved, and it does
not make SCRUM-11130 FULL.
