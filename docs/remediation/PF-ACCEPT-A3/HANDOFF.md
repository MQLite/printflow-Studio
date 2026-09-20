# PF-ACCEPT-A3 final handoff — 18 September 2026

Read [PLAN.md](PLAN.md) first for scope, the acceptance clause and the UI mapping.

**Verdict: FAILED at the Meitu enhancement clause.** The ordinary gate opened, one continuous
session reached the first Meitu operation, and that operation refused deterministically. No
approved PNG, no review decision and no output Revision exist. SCRUM-11130 remains **not FULL**.

Raw local evidence (ignored, not committed): `artifacts/pf-accept-a3/`.

## Identities

| Fact | Value |
|---|---|
| HEAD at start | `058e91283a141d55964f1c083d16542615d358dc`, working tree clean |
| Candidate | `artifacts/pf-accept-a2/build-pairs/d915b1a6-4c06-4b16-9a20-5e1341d1ae9c/candidate` |
| Pair receipt | `52E6DBC5E894EA95CB859CF9CF0BB0D6D42533B17AE26D90DAC0EB41E8DE8FBC` |
| Active revalidation record | `E6A7D7EAA9C370AFACF0769B927B4D5AD735ADAC8203B614F3EE9320D975F3B9`, unchanged |
| Candidate fingerprint as the app reported it | `13A73BEFAE17…` |
| Preset | `printflow-workstation-v1` 1.18.0, `8484F0AA…C8E0F`, unchanged |
| Normal app process | `PrintFlow.App.exe` PID 24528, no arguments, started 14:42:57 |
| Meitu executable | `…\MeituApp\XiuXiu\7.8.8.2\XiuXiu.exe`, `9276B407…6B0B` — byte-identical to the accepted record |
| Photoshop | `D:\Adobe Photoshop CC 2019\Photoshop.exe`, `81EE8930…` |

## The ordinary gate: a natural recurrence of the 18 September Photoshop fault

The first ordinary live run (14:44:59) failed at `PhotoshopSafeStartingState`. The displayed row
again blamed unsaved work, an unknown state or a blocking dialog; that is again not what happened.
Independent read-only observation: Photoshop PID 25116, launched by the Product itself at 14:45:01,
Responding, title exactly `Adobe Photoshop CC 2019` with no document, and a typed enumeration of the
COM Running Object Table returned 16 entries with **zero** Photoshop entries. Evidence:
`a3-live-failure-diagnosis.json`, `a3-rot-after-failure.txt`.

The action was **not** retried unchanged. The Operator was asked for the plain ordinary action and
reopened Photoshop (PID 18604, 14:50:20). Verification mattered: their first report of having done
so was checked and the process identity showed the same instance, so the check was withheld until a
genuinely new instance existed. The reopened instance also registered nothing in the ROT, which
matches A2's finding that this agent's ROT view does not predict the Product's own attach.

With that changed precondition the ordinary action was run once more (14:51:29): six of seven live
checks passed, and `PhotoshopTestImageRoundTrip` reported that the *previous* test image had not
completed and asked the operator to press the same recovery action so PrintFlow could confirm
Photoshop's current state first. That is the Product's designed recovery route, not a blind retry,
and Photoshop was at that moment genuinely holding PrintFlow's own probe
(`PF_ENV_PROBE_75b4eba8…`). It was invoked once (14:52:46) and the gate passed:

**本工作站已通过生产环境校验。** — 7/7 live checks, 11/11 blocking automatic checks, nothing
blocking, `ProductionRevalidation` 通过 recognising candidate `13A73BEFAE17…`. The retained probe
was safely closed and its directory removed, leaving the nine historical probe directories
untouched, and the canonical lease read all owner fields null. Evidence:
`a3-live-result-3-passed.txt`, `a3-lease-after-gate.json`.

The root cause and recovery mechanism of the Photoshop registration fault remain **unresolved**.
It recurred naturally today on a Product-launched instance and cleared after an Operator-opened one
plus the ordinary recovery action. No permanent repair is claimed and no Product-side cause is
categorically excluded.

## The business run and the refusal

Session `A3-FINE-HAIR-001`, SessionId `01a0b276-733f-7ce3-856d-2a5c8d760a2a`, folder
`D:\PrintFlowStudio\Sessions\S_20260918T030142Z_8d760a2a`.

Import and original confirmation completed normally through visible ordinary controls; the app
displayed the fixture's own hash `5A705FE390AF…`. Then every enhancement attempt refused:

| Attempt | Local time | Meitu precondition | Result |
|---|---|---|---|
| `01a0b278-0fbf…` | 15:03:27 | instance running since 11:33, left on its edit page | `MeituUnknownState` |
| `01a0b281-06cb…` | 15:13:15 | fresh instance launched by the Product's own readiness check, start page recognised | `MeituUnknownState` |
| `01a0b295-43a4…` (portrait) | 15:35:21 | fresh instance, gate passing, clean start page | `MeituUnknownState` |

Every attempt recorded the same technical detail, differing only in the file name:

> The signed Save value did not exactly identify 'FIX-FINE-HAIR-001.jpg'. The dialog was canceled
> and no document identity is claimed.

with context `observedIdentity: "FIX-FINE-HAIR-001"`, `expectedFile: "FIX-FINE-HAIR-001.jpg"`,
`inputSent: "cancel-only"`, and `IsRetryable: false`. The portrait session
(`01a0b290-f215-724d-b139-4e3ff8cc3f89`) reproduced it identically as `FIX-PORTRAIT-001`, which
establishes the refusal is **not** fixture-specific.

### What actually mismatched: the signed suffix, not the extension

The failure message names the expected *file*, which invites the wrong reading. The comparison the
code performs is a derived-value one — `MeituDocumentIdentityRule.ExpectedSaveBaseName` is
`Path.GetFileNameWithoutExtension(workingCopyFileName) + signature.OutputBaseNameSuffix`, compared
for exact case-insensitive equality. The signed baseline evidence
`D:\PrintFlowStudio\Baseline\workstation-v1\apps\meitu\editor-with-working-copy.json`
(`FDE0E991…7A88`) declares `outputBaseNameSuffix: "_副本"`. So:

| | Expected | Observed |
|---|---|---|
| fine hair | `FIX-FINE-HAIR-001_副本` | `FIX-FINE-HAIR-001` |
| portrait | `FIX-PORTRAIT-001_副本` | `FIX-PORTRAIT-001` |

**Meitu's Save surface supplied the bare document base name with no `_副本` suffix**, which the
signed evidence says it appends. The extension plays no part. Evidence:
`a3-identity-suffix-analysis.json`.

That reframes the finding: this is most likely **baseline drift** — the signed observation of Meitu's
Save-default naming no longer matches Meitu's current behaviour — rather than a defect in the
comparison. A plausible and untested mechanism is that Meitu appends `_副本` only when the pre-filled
name would collide with an existing file in its remembered save directory, which would make the
suffix conditional on state the signature treats as constant. **This was not tested**, because
confirming it requires driving Meitu's Save panel directly, which this task's bounds do not permit.

### Why no enhancement was ever attempted

Worth stating plainly, because an observer watching the screen sees Meitu open the image and then
close without the AI 变清晰 control ever being clicked. `ProductionMeituProcessor` opens the working
copy, observes the load, and then calls `ConfirmWorkingCopyIdentityAsync` **before** `EnhanceAsync`.
Identity confirmation raises the signed Save surface purely as a read-only probe, reads its value
control and always cancels — Save, Save As and every editable field stay untouched. Because the
derived-value comparison failed, the open never returned a confirmed `MeituOpenedWorkingCopy`, so
`EnhanceAsync` was never called. The Product refused to run an irreversible AI operation on a
document it could not prove was its own working copy. That is the intended boundary working, not a
missed click.

The Product behaved correctly throughout: it cancelled the dialog, claimed no document identity,
sent no further input, wrote no Meitu output, produced no Revision and left both sources and working
copies byte-identical. Evidence: `a3-enhancement-attempt1-failure.json`,
`a3-enhancement-defect-observation.json`, `a3-attempts-final.json`, `a3-session-steps-final.json`.

### What is established, and what is not

The same Product bytes completed this same operation roughly 24 hours earlier. A2's own record for
v3 run `a2-v3-20260917-154736-d915b1a6` shows enhancement attempt `01a0ad7a-63a0…` **SUCCEEDED**
for `FIX-PORTRAIT-001.jpg`, producing `…_HD.png` `F580164E…`, with the trace "editor returned to its
signed empty state". The Meitu executable is byte-identical to the accepted record and no file under
its install folder has changed since 17 September 18:00, so **a Meitu upgrade or local content
update is not the explanation and is not claimed.** Why Meitu supplied the `_副本` suffix through the
qualification harness yesterday and not through the ordinary app today is **not established by this
task's evidence.** That gap is the first thing the next execution should close.

### A second, separate gap: the Product cannot recover Meitu

After a refused attempt the Product leaves Meitu on its unrecognised editor page, and it cannot
recover that state itself:

- 15:11:08 — the next step attempt refused with `EnvironmentNotVerified`
  ("无法安全启动或连接到美图秀秀"), correct behaviour once Meitu's state no longer held.
- 15:32:56 — the ordinary 安全恢复并重新检查 action was run with Meitu on its editor page. It failed
  at `美图秀秀启动能力` ("无法安全启动或连接到美图秀秀"), leaving the whole gate closed. Its safe
  recovery handles PrintFlow-owned **Photoshop** test images only.
- 15:34:42 — after the Operator closed Meitu, the same action passed again and relaunched Meitu at
  its start page.

So an ordinary operator who hits the refusal cannot restore production readiness from inside
PrintFlow; they must close Meitu by hand. Evidence: `a3-portrait-attempt1-envnotverified.txt`,
`a3-recheck-with-meitu-editor.txt`.

## Smallest evidence-supported correction plan

Not authorised by prompt 24 and **not** performed. No Product, preset or configuration bytes were
changed, and the frozen candidate was not patched.

1. **Establish when Meitu appends `_副本` and when it does not.** Observe the actual Save surface
   under both conditions — a pre-filled name that would collide with an existing file in the
   remembered save directory, and one that would not — and compare the harness path with the ordinary
   app path for the same fixture. Until that is known, both a code change and an evidence change are
   guesses.
2. **If Meitu's behaviour has genuinely changed**, the correction is to the **signed baseline
   evidence**, not to the comparison: re-observe and re-sign `editor-with-working-copy.json` for the
   current behaviour. That is a signed-evidence and preset-version change and needs its own
   authorisation — it must not be edited quietly to make a run pass.
3. **If the suffix is conditional**, then the signature is too narrow to express Meitu's real
   contract, and the smallest honest fix is to let it declare both permitted derived values
   (with and without the suffix) while every other value still refuses. Add a red-first regression
   reproducing the exact observed shape (value `FIX-PORTRAIT-001`, signed suffix `_副本`) plus
   negative cases, in the style of the retained-welcome regression already in this suite.
4. **Also fix the recovery gap:** extend the existing safe-recovery action to close a positively
   identified, PrintFlow-owned Meitu document the same way it already handles Photoshop probes.
   Unknown or unidentified documents must keep refusing.
5. Only then a new build pair, the standard set, and a **separately authorised** qualification and
   publication cycle. Nothing here justifies publishing over the accepted record.

## Final state, assistance and limitations

- **Sessions.** Both A3 sessions are preserved as interrupted work at `Enhancement FAILED`, with
  full lineage: 5 attempts, 2 succeeded imports, 2 Revisions, **0 review decisions**, 0 output
  Revisions. Nothing was abandoned, because abandoning them is a business decision and would destroy
  the evidence. The two pre-existing interrupted tasks from August were never opened or touched.
- **Sources.** `FIX-FINE-HAIR-001.jpg` `5A705FE3…8D8E` and `FIX-PORTRAIT-001.jpg` `F4CAD2A1…4634`
  are byte-identical to their manifests after the run.
- **Gate.** Last observed state: passed at 15:34, admission OPEN **for that observation only**.
- **Lease.** Canonical `workstation-automation-v1.db`, resource
  `printflow-studio.external-automation.v1`: all owner fields null at 03:37:59Z. No synthetic lease
  manager and no outer lease was used.
- **Probes.** Nine historical probe directories, untouched; the probe created today was cleaned up
  by the Product itself.
- **External apps handed back safe.** Meitu was left holding PrintFlow's own working copy of the
  portrait fixture on its editor page, beside its signed start page. Because the open document was
  positively tied to this session's own
  `Sessions\S_20260918T033038Z_f8cc3f89\Working\01a0b295-…\FIX-PORTRAIT-001.jpg` (byte-identical to
  the untouched fixture), no PrintFlow automation was in flight and the lease was free, the editor
  window was closed through its own caption control. It closed with no save prompt — PrintFlow had
  never modified the document — leaving the signed start page, which is the state the Product
  recognises. Save, discard and dismiss inputs were never used.
- **Final state, 15:48.** One more ordinary readiness run was made so the workstation is handed back
  verified: **本工作站已通过生产环境校验。**, "没有任何项目阻止生产处理。" PrintFlow PID 24528 on the
  readiness screen; Photoshop PID 18604 document-free; Meitu PID 21524 at its start page; nine probe
  directories (the run's own probe cleaned up again); canonical lease all owner fields null at
  03:48:51Z. Evidence: `a3-final-gate.txt`, `a3-lease-handback.json`. This also confirms the recovery
  gap above is exactly the Meitu editor state and nothing more: once that window is gone, the
  ordinary action restores readiness by itself.
- **Operator assistance (this was not an unattended run).** The Operator gave the exclusive-use
  confirmation; selected both fixtures in the native file dialog, which exposes no UIA patterns and
  cannot be driven by the permitted tools; closed and reopened Photoshop; and closed Meitu twice.
  All business transitions were driven through the real WPF controls by native UI Automation
  patterns. No harness, bootstrap, Fake mode, direct service call, database write, hidden entrypoint
  or injected readiness was used, and no alternate input channel was substituted when a tool was
  unavailable.
- **Restricted pixels.** The Product retained three `open-unconfirmed` evidence screenshots under
  `D:\PrintFlowStudio\Evidence\`. They were left local and were deliberately **not** opened by the
  agent; nothing from the approved-local-only fixtures entered Git.
- **Limitations.** The cause of the identity-comparison difference between yesterday's harness run
  and today's app run is unresolved. The Photoshop ROT registration fault is unresolved. The
  clause's variant cases were out of this task's bounds and keep their existing status. The three
  required review decisions were never reached, so the reviewed-hash binding the clause demands does
  not exist.

## Next boundary

Close the harness-versus-app difference read-only, then the bounded correction above under its own
authorisation. Do not re-run A1/A2, do not publish over the accepted record, and do not claim
SCRUM-11130 FULL until one continuous ordinary run produces an approved transparent PNG bound to a
real reviewed hash.

---

# PF-ACCEPT-A3 rerun handoff — 21 September 2026 (Prompt 24 Revision 3)

**Verdict: PASS for the bounded SCRUM-11130 golden path.** One continuous ordinary run produced an
approved transparent PNG bound to a real reviewed hash. The 18 September failures above are
unchanged and preserved.

Raw local evidence (git-ignored, not committed): `artifacts/pf-accept-a3/rev3-20260921/`.

## Execution record

| Fact | Value |
|---|---|
| Executor | Claude Code, VS Code extension host |
| Policy | `C:\Users\admin\.claude\workflows\development-routing.md` v1.1 (2026-09-18), loaded via the managed `PERSONAL_DEV_ROUTING` entry in `~/.claude/CLAUDE.md` |
| Requested / ExecutionTarget | Opus High |
| ActualRoute | Opus High — session model reported as Opus 5 (`claude-opus-5`); per-request effort metadata is not exposed, so the effort component is `UNVERIFIED`. No Sonnet delegation was made and no switch was attempted, so `MODEL_SWITCH_UNAVAILABLE` does not apply. |
| RouteOffset | 0 (none supplied, none inherited) |
| Context | CONTINUE |
| HEAD at start | `23bf90c3bcf0bd425d9330be83dbe15439e33cac`, working tree clean |
| Candidate | `artifacts/pf-accept-a2/build-pairs/d915b1a6-4c06-4b16-9a20-5e1341d1ae9c/candidate`, receipt `52E6DBC5…8FBC` re-verified with `-VerifyOnly` |
| Active revalidation record | `E6A7D7EA…F3B9`, unchanged |
| Preset | `printflow-workstation-v1` 1.18.0, `8484F0AA…C8E0F`, unchanged; the app reported `printflow-workstation-v1 1.18.0 (8484F0AA1872)` |
| Normal app process | `PrintFlow.App.exe` PID 17840, no arguments, started 10:31:29 |
| Meitu | PID 8368, `…\MeituApp\XiuXiu\7.8.8.2\XiuXiu.exe`, `9276B407…6B0B` — byte-identical to the accepted record |
| Photoshop | PID 8008, `D:\Adobe Photoshop CC 2019\Photoshop.exe`, `81EE8930…A80C5` |
| Input channel | Native UI Automation `InvokePattern` / `ValuePattern` / `ExpandCollapsePattern` on visible controls only. No SendKeys, no synthetic mouse, no harness, bootstrap, Fake, direct service call, database write or injected readiness. |

## Meitu save-mode setup (authorised setting change, not business evidence)

The Save panel's **保存路径** row exposes `btnCustomSavePath` 自定义, `btnCoverSavePath` 覆盖原图 and
`btnDesktopSavePath` 桌面 as toggleable `QPushButton`s inside
`MainWindow.MaskDialog.MaskCenterWidget.SaveMaskWidget.widgetRight.wPath`. The exact label for the
wrong mode is **覆盖原图**, not 覆盖原文件 as reported; the substance of the Operator's finding is
confirmed and only the wording differs.

A disposable synthetic 480×320 JPEG (`PF_SETUP_A3R3_7F2C.jpg`, `527BAABF…DBBD`, generated for this
setup only, kept in the session scratchpad outside the repository and outside `D:\PrintFlowStudio`)
was opened in the accepted instance. No customer document, no fixture and no historical
failed-session artefact was used.

1. **Found:** `btnCoverSavePath` = On. Unedited `fileNameEdit` = `PF_SETUP_A3R3_7F2C` — the bare
   base name, exactly the shape that refused on 18 September.
2. **Restored:** `btnCustomSavePath` invoked once. Read back: `btnCustomSavePath` = On,
   `btnCoverSavePath` = Off, `folderEdit` = `C:/Users/admin/Downloads`, and the unedited
   `fileNameEdit` became **`PF_SETUP_A3R3_7F2C_副本`**. Nothing was typed into the name field; the
   suffix was supplied by Meitu.
3. **Persistence:** the panel was cancelled through the signed
   `…widgetRight.titleFrame.closeButton`, then raised once more from the editor `saveButton`.
   Custom was still selected and the default name still carried the suffix. **No setup save was
   needed**, so none was performed — zero images were written, the setup file's hash is unchanged
   and `C:\Users\admin\Downloads` contains no `PF_SETUP*` file.
4. **Cleared:** the setup document was closed through 关闭图片. Meitu returned to
   `MainWindow.OpenMaskWidget`, the signed empty editor. **自定义 was left selected.**

Save was never pressed while 覆盖原图 was selected. No application settings file was edited and no
other remembered preference was changed.

The three attributions stay distinct: the **Operator reported** the historical cause; the
**executor observed** the current setting and restored it; the **Product then succeeded** at its own
identity probe during the measured run, which is the only thing that proves the fix.

## The ordinary gate: a new, different Photoshop blocker

First ordinary run (10:32:50, report 10:34) **failed** at `Photoshop 启动能力`:

> Process 7308 owns no visible top-level window of class 'Photoshop', so there is no target any
> input could be addressed to.

Read-only diagnosis: the preset's accepted executable is `D:\Adobe Photoshop CC 2019\Photoshop.exe`
(the authoritative Desktop shortcut resolves there), but the instance the Operator had running was
PID 14792 from **`C:\ps2019\Adobe Photoshop CC 2019\Photoshop.exe`** — byte-identical
(`81EE8930…A80C5`) but a path the preset does not register, and not on its `excludedInstallations`
list either. Photoshop is single-instance, so PrintFlow's launch of the accepted executable handed
off to that instance and exited without ever owning a window. The Product refused rather than
adopting an unaccepted instance. Evidence: `gate-2-photoshop-launchability-failed.txt`,
`gate-2-diagnosis.json`.

**This is not the unresolved 18 September ROT / `MK_E_UNAVAILABLE` fault.** That one failed later,
at `PhotoshopSafeStartingState`, with a Product-launched instance present and no explanation.
Today's failure is earlier, at launchability, and has a concrete observed cause. The ROT fault did
not recur today and remains unresolved and unclaimed.

The action was **not** retried unchanged. Starting and stopping Photoshop is outside this agent's
permitted actions, so the Operator was asked once for the precise ordinary action and closed
PID 14792 (document-free, title exactly `Adobe Photoshop CC 2019`) then started Photoshop from the
accepted Desktop shortcut. The new instance was independently verified before the check: PID 8008,
`D:\Adobe Photoshop CC 2019\Photoshop.exe`, started 10:42:40, responding, no document.

With that changed precondition the ordinary action was invoked once more (10:45:44) and the gate
passed at 10:45:

**本工作站已通过生产环境校验。** — 没有任何项目阻止生产处理。 Verified preset
`printflow-workstation-v1 1.18.0 (8484F0AA1872)`, two informational-only notices (read-only markers
on accepted files, whose signatures still match; and the preset's recorded UI languages). The Meitu
checks — 应用自动化可用性, 美图秀秀启动能力, 美图秀秀初始状态 — stopped appearing as blocking, i.e.
they passed under the restored Custom mode. Canonical lease `workstation-automation-v1.db`,
resource `printflow-studio.external-automation.v1`: all owner fields null. Evidence:
`gate-3-passed.txt`, `gate-3-passed.json`, `lease-after-gate.json`.

## The business run

Session **`A3-R3-FINE-HAIR-20260921`**, SessionId `01a0c106-1035-77b2-98ae-432229bec71b`, workspace
`Sessions/S_20260920T225315Z_29bec71b`, `WorkflowType = PREPARE_ASSET`, created 22:53:15Z,
`State = COMPLETED` at 23:29:00Z. A new unique name was used; neither historical A3 session was
resumed, reused or stitched in.

Fixture `D:\PrintFlowStudio\TestData\v3\inputs\FIX-FINE-HAIR-001.jpg`, `5A705FE3…8D8E`, 312,309
bytes. The session's own `Source` snapshot hashes identically.

| # | Step | Adapter | Result | Started → ended (UTC) |
|---|---|---|---|---|
| 1 | Import | `internal-import-v1` | SUCCEEDED | 22:53:15.189 → 22:53:15.635 |
| 2 | Enhancement | `meitu-xiuxiu-production-v1` | **SUCCEEDED** | 22:55:29.926 → 22:55:49.649 |
| 3 | BackgroundRemoval | `meitu-xiuxiu-production-v1` | SUCCEEDED | 23:11:26.416 → 23:11:52.867 |
| 4 | Trim | `internal-alpha-trim-v1` | SUCCEEDED | 23:19:48.406 → 23:19:48.633 |
| 5 | ApprovedPngExport | `internal-promote-v1` | SUCCEEDED | 23:25:23.142 → 23:25:23.206 |

Five attempts, **zero failures, zero retries**. The Meitu document-identity probe that refused three
times on 18 September did not refuse once.

### Revisions and the reviewed-hash binding

| Operation | Revision | Artefact | SHA-256 | Bytes |
|---|---|---|---|---|
| IMPORT | `…693dd6b76f80` | `Source/FIX-FINE-HAIR-001.jpg` | `5A705FE3…8D8E` | 312,309 |
| ENHANCE | `…5a1904964ab9` | `A3-R3-FINE-HAIR-20260921_HD.png` | `98F0136C…9F6C` | 1,556,380 |
| REMOVE_BACKGROUND | `…abbc12aad5d8` | `A3-R3-FINE-HAIR-20260921_CUTOUT.png` | `A52512A5…10DC` | 1,587,578 |
| TRIM | `…1fa86089ff92` | `trimmed.png` | `8D94D320…80C1` | 1,600,764 |
| PROMOTE_APPROVED | `…f8f3f7354e30` | `Approved/A3-R3-FINE-HAIR-20260921.png` | `8D94D320…80C1` | 1,600,764 |

Each step's input is the previous step's reviewed revision, and the exported deliverable carries the
**identical** SHA-256 of the reviewed Trim revision, so the approved PNG is bound to reviewed
content rather than to a re-derived artefact.

### The three real review decisions

All three were the Operator's own decision for this session's own revision and hash, requested with
the exact file, pixels, revision short id and SHA-256 on screen, and recorded through the ordinary
review control. No A1/A2 approval was transferred and nothing was pre-approved.

| Step | Subject revision | Reviewed SHA-256 | Operator | Decision | Decided (UTC) |
|---|---|---|---|---|---|
| Enhancement | `01a0c108-6b92-7dd8-82c1-5a1904964ab9` | `98F0136C…9F6C` | admin | APPROVED | 23:08:39.725 |
| BackgroundRemoval | `01a0c117-1e23-7da6-b376-abbc12aad5d8` | `A52512A5…10DC` | admin | APPROVED | 23:19:31.727 |
| Trim | `01a0c11e-6099-790e-baee-1fa86089ff92` | `8D94D320…80C1` | admin | APPROVED | 23:25:08.107 |

### Executor actions inside the flow, disclosed separately

- **Background-removal automatic-selection authorisation.** Step 4 refuses to run until
  对此图片使用自动选择 is confirmed for the current artefact. The executor invoked that control and
  its 确认, immediately after the Operator approved that exact revision. The session records
  `BackgroundRemovalDecision = USE_AUTOMATIC_SELECTION_FOR_REVIEWED_CONTENT` bound to revision
  `01a0c108-…04964ab9` / `98F0136C…9F6C`. This is an in-flow ordinary control, **not** one of the
  three required Operator review decisions, and it is reported here as executor assistance.
- **Output name.** `A3-R3-FINE-HAIR-20260921` was typed into `WorkflowSelection.OutputName` through
  `ValuePattern`, set and verified twice.
- Every other business transition was a single `InvokePattern.Invoke()` on a visible, enabled,
  on-screen control resolved by exact `AutomationId`.

### Trim kept the full canvas, and that is the expected shape here

`internal-alpha-trim-v1` reported detected content bounds `0, 0, 1200, 1600` and applied bounds
`0, 0, 1200, 1600` under `TrimMode = TIGHT_CROP`: the cutout's non-transparent pixels reach all four
edges, so a tight crop is a no-op and `trimmed.png` has the same dimensions as its input. The
Operator noticed mid-run that this fixture cannot show a visible trim difference. That observation
is correct, and the outcome is the one the prompt explicitly permits — trim may keep the full canvas
when the alpha bounds justify it. **The fixture was not changed**, because this prompt names
`FIX-FINE-HAIR-001.jpg` for the business session and forbids inventing additional variant testing.
Demonstrating a non-trivial crop needs a fixture whose subject does not touch the frame; that is
separate, separately authorised work and is **not** claimed here.

### Final deliverable

`D:\PrintFlowStudio\Sessions\S_20260920T225315Z_29bec71b\Approved\A3-R3-FINE-HAIR-20260921.png`

SHA-256 `8D94D3207333F172D3F4F2CEB2D84F8F4ABC6B2B845556D5C7372BE013C280C1`, 1,600,764 bytes, PNG,
1200 × 1600, `Format32bppArgb`, 96.012 DPI, alpha channel present. An independent read-only sample
of every 7th pixel found 7,839 fully transparent and 22,674 fully opaque samples, so the
transparency is real and not a nominal alpha channel. The destination did not exist before the run
and nothing was overwritten.

## Final state, assistance and limitations

- **Handback gate.** Read-only 刷新状态 at 11:32: **本工作站已通过生产环境校验。**,
  没有任何项目阻止生产处理。 No extra live application actions were taken for the handback.
  Evidence: `gate-4-handback.txt`.
- **Lease.** Canonical `workstation-automation-v1.db`, resource
  `printflow-studio.external-automation.v1`: all owner fields null at 23:29:43Z and at handback. No
  synthetic lease manager and no outer competing lease were used.
- **Probes.** Nine `EnvironmentVerification` directories, newest last written 16 September — all
  historical. This run's own probe was created and cleaned up by the Product itself.
- **Sessions preserved.** `S_20260918T030142Z_8d760a2a` and `S_20260918T033038Z_f8cc3f89` are
  untouched, still interrupted at `Enhancement FAILED` with zero review decisions. The two August
  interrupted tasks were never opened.
- **Sources.** `FIX-FINE-HAIR-001.jpg` `5A705FE3…8D8E` and `FIX-PORTRAIT-001.jpg` `F4CAD2A1…4634`
  are byte-identical to their manifests after the run.
- **External apps handed back safe.** Meitu PID 8368 at its signed empty editor with 自定义 still
  selected; Photoshop PID 8008 at the accepted path, document-free; PrintFlow PID 17840 on the
  readiness screen. Nothing was force-closed or killed.
- **Restricted pixels.** No Product evidence screenshot was opened by the agent, and no fixture,
  working-copy or deliverable pixels entered Git. `D:\PrintFlowStudio\Evidence\` gained nothing
  today.
- **Operator assistance (this was not an unattended run).** The Operator gave the exclusive-use
  confirmation and closed a real customer document in Photoshop before any desktop input; closed the
  `C:\ps2019` Photoshop instance and started the accepted one; selected the fixture in the native
  选择文件… dialog, which exposes no UIA patterns and cannot be driven by the permitted tools; and
  gave all three review decisions.
- **Limitations that remain.**
  1. The Photoshop ROT / `MK_E_UNAVAILABLE` registration fault of 18 September is still
     **unresolved**. It did not recur today; today's blocker was a different, explained one.
  2. Today's blocker is itself unresolved as a configuration question: an unregistered `C:\ps2019`
     copy of the accepted Photoshop build exists on this workstation and will block the gate again
     whenever it is the running instance. Deciding whether to remove it, register it or add it to
     `excludedInstallations` is a workstation-configuration change and was **not** made.
  3. The **Meitu recovery gap** recorded on 18 September was not exercised today, because no refusal
     occurred. It is neither reproduced nor fixed, and remains an open gap.
  4. Meitu's save mode is a user-level application preference with no Product-side guard. Nothing
     prevents it being switched back to 覆盖原图, which would reproduce the 18 September refusal.
     The Product's refusal is correct behaviour, but its message names the expected *file* rather
     than the derived Save value, which is what made this cause hard to read. No code change is
     proposed or authorised here.
  5. The clause's variant cases (rejected review, automatic retry, manual takeover, restart
     recovery, unknown dialog, output-validation failure) were out of scope and keep their existing
     statuses.
  6. **SELF-REVIEW ONLY / INDEPENDENT REVIEW NOT COMPLETED.** No independent reviewer was engaged;
     this prompt did not add one.

## Next boundary

The golden-path clause now has real evidence. SCRUM-11130 is **not** FULL: its variant cases still
rest on harness-labelled and synthetic-live supporting evidence, and independent review is not
completed. Prompt 25 was **not** executed and none of its proposed repairs is needed for the
identity refusal, whose cause is now known and was a Meitu setting rather than baseline drift, a
conditional suffix or a defective comparison. Do not rebuild, requalify or republish on the strength
of this run.
