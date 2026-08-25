# Epic 11300 Part C1 — Meitu Background Removal action and completion

Date: 2026-08-25 (Pacific/Auckland)  
Baseline entering C1: `11300-B2B PASS — READY FOR BACKGROUND REMOVAL AUTOMATION`  
Verdict: `11300-C1 PASS WITH NOTES — READY FOR BACKGROUND REMOVAL EXPORT`

This slice establishes only the guarded Background Removal UI action, operation-specific Busy,
positive UI completion, and exact document identity before and after. It exports no cutout,
creates no `AdapterOutput` or `Revision`, and makes no alpha or image-quality claim.

## 1. Actual Background Removal feature

The live Meitu 7.8.7.5 feature corresponding to removal/cutout is the loaded editor's left-side
`抠图` page entry.

`AI换背景` is not accepted. It is a separate `高级调整` module for background replacement,
which is semantically different from producing a transparent foreground. `智能抠图` appeared in
older product prose, but is not the exact live entry used by this build.

The read-only candidate walk recorded:

| Candidate | Live structure | Finding |
|---|---|---|
| `抠图` | `CheckBox` / `PageButton`; empty AutomationId; Invoke, Value and Toggle; enabled; on-screen; owned by the accepted Meitu process | Actual cutout/background-removal entry |
| `AI换背景` | `Text` / `QLabel` marker, two ancestors below a `CheckBox` / `ModuleButton` | Background replacement; explicitly rejected |
| `AI变清晰` | Enhancement `ModuleButton` | Unrelated operation; explicitly rejected |

## 2. Structural target

The C1 rule is Background Removal-specific:

```text
exact marker 抠图
→ exact CheckBox/PageButton shape
→ fixed owner depth 0 (the marker is the action itself)
→ exactly one enabled, visible Invoke target
→ exact accepted process
```

The return to the ordinary editor uses the separately signed `调整` control with the same exact
`CheckBox` / `PageButton`, depth-zero structure. There is no substring match, sibling index,
coordinate, keystroke, first-invokable-ancestor rule, or generic `ModuleButton` selection.

## 3. Selection-mode finding

Invoking `抠图` once immediately entered the cutout page and auto-started `自动选择`. PrintFlow did
not choose a human, product, pet, icon, or manual subject type and did not send a second input.

The visible cutout/refinement choices were `自动选择`, `局部抠图`, and `手动修补`. The older signed
Epic 11000 subject-mode list (`关闭`, `人像宠物`, `商品货物`, `图标印章`) was not presented as a
required choice on this exercised entry.

The authoritative product rule remains `OPERATOR_OR_REVIEWED_CONTENT_DECISION`. The C1 foundation
therefore requires an explicit `UseAutomaticSelectionForReviewedContent` decision. `Unspecified`
fails with `PRODUCT DECISION REQUIRED` before the identity probe or any other input. The smoke's
synthetic mannequin and plain background were reviewed content, so that explicit decision
authorised the observed automatic mode for the smoke only.

## 4. Signed evidence and preset version

`v1.4.2` was not edited. C1 created and exercised candidate evidence first, then signed a new
semantic preset:

| Artifact | State / SHA-256 |
|---|---|
| `apps\meitu\editor-background-removal.json` | `CONFIRMED`; `CEBE3618B8B181E07140D2DE01776F42E3CF3678352B88DDEA8695BAF19CB1EA` |
| `preset\printflow-workstation-v1.5.0.json` | `ACCEPTED_IMMUTABLE`; `3B83BC0CB7560D9AD6F2EB9CD1BC294A744DF698FEB21289DF0299CCE5EB1738` |
| `signoff\workstation-preset-v1.5.0.json` | `CONFIRMED_FINAL`; `8FE26BD2215EE0EE8BA459FC268CB39DC95036E4C1ACD3F11D6AC8E919019013` |

The manifest and sign-off are read-only. All 16 inherited source-integrity entries were rehashed,
the new C1 entry was added, and all 17 matched. The `v1.4.2` manifest still hashes to
`B0984BDAFDB7B6C9B0403C61C8105ADCB6F4F58B2C7F86270070A0A9BAAF3225`.
`appsettings.json` now pins `v1.5.0` and its exact manifest digest.

## 5. Load and retained-state behaviour

Every exercised document began as a new opaque RGB synthetic Working copy with a unique
`PF_BACKGROUND_C1_*.png` name. The backing workspace remained alive while Meitu referenced it.

The initial supervised load opened in ordinary `调整`; Background Removal did not auto-start merely
because a document was loaded. Entering `抠图` auto-started `自动选择`. A completion panel is never
accepted at route entry: it is treated as stale and prevents invocation. Completion in a run is
accepted only after that same run positively observed Busy.

Across the later candidate and final smokes, the prior synthetic document was returned through
`调整`, closed through the signed close route to `KnownEditorEmpty`, and a fresh document opened in
the ordinary editor. No previous completion panel was accepted on the next load. The correlation
guard remains mandatory even though persistence was not observed in those repeated transitions.

## 6. Busy

The supervised live timeline was:

```text
t+1.26 s  智能识别中... + 取消
t+2.54 s  返回结果中... + 取消
t+5.92 s  图片合成中... + 取消
t+6.23 s  operation text and 取消 absent
```

The signed Busy rule requires at least two markers: one of the three cutout-specific progress
messages plus `取消`. Enhancement's `变清晰中` / `变清晰时长`, a generic editor, generic Meitu
work, or `取消` alone cannot match. Busy outranks the editor/document states; an owned modal still
outranks Busy.

The first candidate exercise failed closed because the ordinary full Qt-tree observation was too
slow and first saw completion. It exported nothing. The phase observer was then narrowed to a
read-only exact-name UIA query over only the signed Busy/completion markers. The next fresh
candidate and the final signed smoke both positively observed Busy.

## 7. Completion

Completion requires all five live result/refinement controls:

```text
自动选择
局部抠图
手动修补
反选
移除背景
```

Busy must also be absent, and Busy must already have been positively observed for the current run.
Busy disappearing on its own is not completion. The general state classifier deliberately does
not turn this panel into a safe starting state; it is a positive C1 operation phase only.

No exported pixels were inspected. The canvas appearance and cutout quality are not C1 evidence.

## 8. Post-completion identity

The guarded sequence is:

```text
exact Save-surface identity A
→ signed Cancel
→ exact editor/foreground/process reacquisition
→ one 抠图 invocation
→ Busy
→ positive completion
→ one signed 调整 invocation
→ exact editor reacquisition
→ exact Save-surface identity A again
```

Candidate pass: `PF_BACKGROUND_C1_901FE5409E25_副本` before and after.  
Final immutable-preset smoke: `PF_BACKGROUND_C1_475464A1A37F_副本` before and after.

An identity change to B, inability to open the identity surface, or inability to return to the
signed editor fails C1. It does not produce an output claim.

## 9. Safety refusals

Automated coverage proves no unintended C1 input for missing, ambiguous, near-match, wrong-depth,
wrong-process, disabled, off-screen and structurally invalid targets. It also covers:

- wrong pre-action identity;
- post-completion identity B;
- missing signed evidence;
- unspecified product/mode decision;
- already-processing state (no second invocation);
- stale completion panel (no restart or completion claim);
- Busy absent, Busy infinite, and Busy disappearing without completion;
- modal appearance without dismissal;
- cancellation while Busy;
- wrong foreground process;
- signed evidence tampering after manifest creation.

The C1 outcome type has no output, path, CUTOUT, success, export or Revision member. The route has no
value write, shortcut, coordinate or export call.

## 10. Tests and gates

The new coverage includes pure structural/phase rules, guarded driver sequences, parser and
integrity tests, architecture boundaries, generic state-classifier precedence, and retained
`ProcessAsync` refusal.

Final gates, using the installed .NET 10.0.400 SDK:

| Gate | Actual result |
|---|---|
| `dotnet restore --locked-mode` | passed |
| `dotnet build` | passed; 0 warnings, 0 errors |
| `dotnet test` | 7,074 passed; 0 failed; 0 skipped |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages in any project |

The pre-C1 baseline was 7,018 passing tests. Enhancement's production success coverage remains
green. `MeituOperation.RemoveBackground` through `ProductionMeituProcessor.ProcessAsync` still
returns the unconditional non-retryable `AdapterUnavailable` refusal before any window or input.

## 11. Live smoke

The final smoke ran only after the automated gates were green and used the immutable `v1.5.0` pin:

```text
KnownEditorEmpty
→ open PF_BACKGROUND_C1_475464A1A37F.png
→ KnownEditorWithExpectedWorkingCopy
→ identity PF_BACKGROUND_C1_475464A1A37F_副本
→ explicit reviewed-content authority for 自动选择
→ one 抠图 action
→ Busy positively observed
→ Complete (five signed controls; Busy absent)
→ one 调整 return
→ identity PF_BACKGROUND_C1_475464A1A37F_副本
→ STOP — no export, AdapterOutput or Revision
```

The synthetic document was then closed through the already-signed close route and Meitu positively
reached `KnownEditorEmpty`; no modified-document prompt appeared. Four released C1 temp workspaces
remain under the OS temp directory because the execution environment rejected the final recursive
cleanup command. They contain synthetic data only, are outside Git, and Meitu references none of
them. Smoke transcripts and the generated source image also remain local and uncommitted.

## 12. Product decision

No new global subject-mode policy was invented. The existing signed policy is sufficient only when
an operator or reviewed-content caller provides the decision. C1 encodes that authority explicitly
and refuses an unspecified caller.

This is the reason for `PASS WITH NOTES`: a future production caller must carry the reviewed-content
or operator decision into C2. `ProcessAsync` deliberately has no such parameter and remains closed.

## 13. Remaining C2 scope

C2 owns everything after the C1 identity reconfirmation:

- an evidence-backed Background Removal export route;
- a controlled `{Name}_CUTOUT.png` path with no overwrite;
- PNG existence, settling, readability and exact-path validation;
- source Working-copy hash protection;
- alpha/transparency and dimensional validation;
- an `AdapterOutput` only after validation;
- workflow success and a Background Removal `Revision` only after the output is proven;
- propagation of the explicit operator/reviewed-content mode decision.

No C2 item is implied by the C1 UI completion phase.

## 14. Git state

Before C1, the completed B1.1/B2A/B2B work was checkpointed normally without amendment:

```text
523f25a  11300: complete Meitu identity, enhancement, and export route
d51c66b  Report: Epic 11300 identity and enhancement completion
```

At report time the branch is `master`, 21 commits ahead of `origin/master`. C1 source, tests,
`appsettings.json`, and this report are uncommitted. No synthetic image, UI dump, screenshot, smoke
log, runtime database or external baseline file is in Git. Nothing was pushed and history was not
rewritten.

`11300-C1 PASS WITH NOTES — READY FOR BACKGROUND REMOVAL EXPORT`
