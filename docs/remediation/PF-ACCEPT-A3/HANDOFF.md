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
- **External apps left running.** PrintFlow PID 24528 on its normal screen; Photoshop PID 18604
  document-free; **Meitu PID 21524 left on its editor page** holding PrintFlow's working copy of the
  portrait fixture. That last state will block the next readiness run until Meitu is closed — see
  the recovery gap above. No save, discard or dismiss input was ever sent to Meitu by the agent.
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
