# Epic 11600 — Part B

## Production Multi-job Soak & Resource Stability

---

## 0. Resolution — fresh post-B1 soak

The B1 owned-document cleanup gate passed in commit `95cb24e`. A completely fresh soak then ran
from job 1 on 2026-09-03; it did not resume either historical partial run described below. The
configuration and registered graph were:

```text
committed Adapters.Mode : Production
override                : none
preset                  : printflow-workstation-v1 1.16.0
preset SHA-256          : 6396FB4EB87F69C6789304CE191453654B2B75E82A5A9AB0161F90556A6F1A80
gate                    : VerifiedEnvironmentGate / Production ALLOWED
Photoshop adapter       : photoshop-cc2019-production-v1 / Production
Meitu adapter           : meitu-xiuxiu-production-v1 / Production
run token               : 20260903-121445-E3477306
QA database             : D:\PrintFlowStudio\QA\Epic11600B\20260903-121445-E3477306\printflow-soak.db
elapsed                 : 11.1055 minutes
```

### 0.1 Completed structure

| Stage | Required sequence | Result |
| --- | --- | --- |
| A | 8 mixed jobs, `P M P M P M P M` | 8/8 succeeded |
| B | 12 consecutive Photoshop jobs | 12/12 succeeded |
| C | 12 consecutive Meitu jobs | 12/12 succeeded |
| Restart checkpoint | rebuild the PrintFlow graph while leaving Photoshop and Meitu running | passed; lock free, 0 interrupted attempts, 0 unexpected quarantine, 32 prior outputs re-hashed unchanged |
| D | 8 post-restart jobs, `P M P M P M P M` | 8/8 succeeded |

All six resource checkpoints were reached in their defined order: before job 1, after job 8,
after job 20, after job 32, after the PrintFlow restart, and after job 40. No threshold was invented
or applied. The completed harness reached its checkpoint table and all final assertions; the
earlier report's statement that four checkpoints were not taken applies only to the preserved
historical run below.

The post-run bounded observation found Photoshop PID 20848 still on the same process, title
`Adobe Photoshop CC 2019`, signed `KnownStartScreen`, no operator work loaded, and no blocking
dialog. The soak's final window census was `OWL.Document 0`, `OWL.TabPane 4`,
`Photoshop_Document 0`. A later read-only resource sample found Photoshop at 821 MB working set,
2,069 MB private, 1,883 handles and 75 threads; Meitu at 410 MB working set, 416 MB private,
1,185 handles and 42 threads. These are observations, not leak thresholds.

### 0.2 Job and output result

The QA database was reopened read-only after completion. It contained 40 sessions and 80 successful
attempt rows: 40 import attempts plus exactly 40 adapter-backed step attempts. There were zero
failed attempts, zero retries, zero cleanup warnings, 40 `ReviewRequired` step states, and a free
automation lock. The adapter split was 20 Photoshop TIFF jobs and 20 Meitu enhancement jobs.

All Meitu outputs were independently recorded as 1280×960 PNG, 1,159,183 bytes, SHA-256
`5207F744E04267CE1AA68BEAC7C0602CF5FBA62A8D17A2EA9B3139417F643ECC`. The 20 Photoshop
outputs were independently validated 600×400 TIFFs:

| Job | TIFF SHA-256 |
| --- | --- |
| 1 | `1D60A969957E8A6EACA0C296743D3F170129700BCCD8FD70A947104AC2708F87` |
| 3 | `022E4C89717D651A4CBCBEF31A28AB862D2D61BCB5756FD6FEB1711809CC7ED5` |
| 5 | `86F6F5711F5BB995639601B73DBBE2B7FA5FD8BD4FC987DB40A38C1F4090416B` |
| 7 | `C36A8E3A965DFFA64B7DC92524A409E3FE62EE2B16E1C2AB985DC4635EB535C0` |
| 9 | `6AD0B6113C5A18E8477C5D5AE8A901685D2E651FCBD8BAB87975913745850F9A` |
| 10 | `46E3C442186BB23302F4971B1BC35EA0E8356CAD35B4DFE49402BA27A5114CC5` |
| 11 | `0DF6BECFBCEE00573CC9DE50BA6C4A6F0170EC1A8F20718BBDECA5577A7747CB` |
| 12 | `CFF73FB24E16D9DF2FD8B96015DED6C8A1E002FCD7713CF91311506ECD9157DC` |
| 13 | `112463913D82F3368E33AA68C82184C797942FACE9EF5D78DFE739E41D2C2248` |
| 14 | `057EBEB5194F84692D902460900C15E8F63C5D5133ACC2AE2CFD6BA3BA62BD40` |
| 15 | `074BDE75DB1DB67C6A92FFAE3A7DCFC62F66CE7BF3FC7FFC72B05DE445D08E0D` |
| 16 | `6BEE404397E21EF12013F89C168B92BDCC44958441639BDB7663EE8900C576CB` |
| 17 | `326EE5F4A3F4E20A885D5B242352D8D5E8BEFB4FA132A5E9A7FE5E75CB88F4D7` |
| 18 | `BBA67EDB3828F1F3A4C2ADF698750EB65C009CAAA3EA7B2C34D483C5D4161507` |
| 19 | `5A3F44DD32C5DB1380E1220CACF2C95E8B698BE5EC47ECAA2C64070A2C9080A3` |
| 20 | `8D4B42E9A51BA774EF2CB56D81FC2B37EDC033E6F571C56D51B1E3FD816AE8E6` |
| 33 | `396C27772F8C75AA4CAB26E0E9D83C1020047962C89E3075362D89B7523C5E22` |
| 35 | `0186E5E3D057080D956E82E265B9A4BEF428DABFEF721AA5BAE49CF7FBEFF8CA` |
| 37 | `A14D93197A738EC5D2EE3E06A00C5DC39CA372F988FB39791B874F51A248ED72` |
| 39 | `94D49CFD511E26CF4B910B3524750D905C6D5B7D9E2389E445A8B30D1FE18C18` |

Nineteen TIFFs were 2,452,724 bytes; job 13 was 2,452,720 bytes and passed the same independent
structural validation. Every Photoshop job recorded signed owned-document cleanup success and no
warning, and the final retained Working-document delta was zero.

### 0.3 Final filesystem/session census and customer safety

```text
sessions before/after    : 54 / 94
new session directories : 40
expected from this run   : 40
session directories lost: 0
Comparison files         : 70 (unchanged)
Quarantine files         : 2 (unchanged)
QA directories           : 9 (unchanged by the measured interval)
prior-session sample     : 5/5 metadata fingerprints unchanged
Meitu running instances  : 1, accepted 7.8.7.5 executable only
```

All run inputs were synthetic and isolated under the run's QA directory and its 40 new session
directories. No customer artifact was opened, selected, saved, closed, discarded, overwritten,
moved, renamed, or deleted. The full 10,121-test final-source suite was not rerun because this
resumed proof changed no product source; the already-passed B1 suite remains the code gate.

The remainder of this document preserves the original partial-run evidence and the defect that B1
subsequently remediated. Those historical `NOT COMPLETED` statements do not override this fresh
post-B1 result.

**11600-B PASS — PRODUCTION MULTI-JOB SOAK AND RESOURCE CHECKPOINTS COMPLETE**

---

## 1. Preflight

Starting point: Epic 11600 Part A accepted, `master` clean at `e752ca3`.

| Check | Result |
| --- | --- |
| Working tree | clean |
| Build (`PrintFlowStudio.sln`) | **0 warnings / 0 errors** |
| Committed `Adapters.Mode` | `Production` |
| Override | **none** |
| Configured preset | `printflow-workstation-v1 1.15.0` (`3392873ED0CA…`) |
| Baseline suite (Part A) | 10088 passed / 0 failed / 0 skipped |

Live workstation verification before anything was changed — read-only, through the accepted
`ProductionWorkstationVerifier`:

```text
Workstation verified — preset printflow-workstation-v1 1.15.0, 10/10 checks passed
  [Passed  ] Immutable MeituExecutable      …\7.8.7.5\XiuXiu.exe (D65C6D823232)
  [Passed  ] Immutable PhotoshopExecutable  D:\Adobe Photoshop CC 2019\Photoshop.exe (81EE8930FC1E)
  [Passed  ] Immutable PhotoshopActionArtifact  …\PrintFlow-DTF-v1.atn (A04203EDEA62)
  [Passed  ] Immutable EvidenceIntegrity    27/27 entries verified
  [Advisory] FilesystemReadOnlyPolicyAdvisory  16/27 files not marked read-only
  [Advisory] ExternalApplicationUiLanguage  Meitu zh-CN, Photoshop Simplified Chinese
```

Both advisories are pre-existing and are the ones Epic 11500 accepted; SHA-256 remains the
authority and every digest matched.

---

## 2. Phase 0 — the Meitu version drift, resolved

### 2.1 What was actually on the workstation

Audited before any decision was taken.

| Observation | Value |
| --- | --- |
| Accepted executable (from the signed preset) | `C:\Users\admin\AppData\Local\MeituApp\XiuXiu\7.8.7.5\XiuXiu.exe` |
| Accepted digest | `D65C6D82…B037B1` — **matches the preset exactly** |
| Second installed version | `…\XiuXiu\7.8.8.0\XiuXiu.exe`, digest `53E73026…C74407`, written 2026-09-02 12:15 |
| Running at audit time | one process, from **7.8.7.5** |
| Operator entry point | Start Menu `美图秀秀.lnk` → `…\MeituApp\XiuXiu\XiuXiu.exe` (a 392 KB launcher stub, `FileVersion 1.0.0.1`, `OriginalFilename launcher.rc`) |
| Stub's configured target | `…\MeituApp\XiuXiu\Config.ini` → `Version=7.8.7.5` |

Two stale `*_tmp` directories (`7.6.0.2_tmp`, `7.6.2.6_tmp`) contain no executable and are not
installed versions.

### 2.2 Why 7.8.8.0 appeared

There is **no** Meitu updater service, scheduled task, or `Run`-key autostart on this machine —
all three were enumerated and all three are empty of anything Meitu. The updater is **in-process**:
it runs only while Meitu itself is running, and its preference lives in Meitu's own per-user
configuration:

```text
C:\Users\admin\AppData\Local\Meitu\XiuXiu\Config.json
    "silentUpgrade": true
    "lastUpgradeTipTime": 1784161511385
```

That is the root cause, and it explains the Part A timeline precisely: Meitu was left running
across the workstation session, upgraded itself silently into a new sibling version directory at
12:15, and the newer build then took the single-instance slot.

The launcher stub resolves through `Config.ini`'s `Version=`, and that key is rewritten by
whichever build actually starts. Because PrintFlow launches the accepted binary **by absolute
path**, every ordinary production launch re-points the operator's own shortcut back at 7.8.7.5 —
which is why the stub reads `7.8.7.5` today. That is a side effect worth knowing about, not a
control, and it is not relied on below.

### 2.3 The decision: Option A — retain accepted 7.8.7.5

Chosen for reasons that are about evidence rather than convenience:

* the accepted binary is **present and digest-exact**, and the workstation verifies 10/10 against
  preset `1.15.0` as it stands;
* Option B is not a version bump. The Meitu half of the accepted evidence chain is twelve signed
  UI artefacts — `clean-start`, `editor-empty`, `editor-document-identity`,
  `editor-with-working-copy`, `editor-close-document`, `editor-enhancement`, `editor-export`,
  `editor-background-removal`, `editor-busy-cancel`, `start-page-card-target`, `open-file-dialog`,
  `window-policy` — every one of which would have to be re-observed against 7.8.8.0, re-hashed
  into a new immutable preset, and revalidated end to end. That is a slice of its own;
* §2 is explicit that a new version must not be accepted merely because one enhancement happens
  to work, and one enhancement working is the entire evidence 7.8.8.0 currently has.

**No installed version was deleted, renamed or modified.** 7.8.8.0 is still on disk, untouched.

### 2.4 What actually prevents 7.8.8.0 becoming the running Production instance

Stated as three separate claims, because they have very different strengths and conflating them
would be the dishonest part.

**Verified — PrintFlow cannot select it.** `Win32ExternalAppWindowLocator.FindProcessesByExecutable`
matches a running process by its **absolute module path**, and `EnsureReadyAsync` verifies the
binary's SHA-256 before it looks for a process at all. A 7.8.8.0 process is therefore not a
candidate: not attached to, not counted as an instance, not a reason to skip the launch. Proved
synthetically by the two new tests in §7, and observed live at every soak checkpoint.

**Verified — a newer instance holding the slot is a refusal, not a fallback.** With 7.8.8.0
running, launching the accepted binary hands off through Meitu's own single-instance mechanism and
exits without presenting a window; PrintFlow reports `MeituLaunchFailed`, creates no Revision and
releases the automation lock. The cost is availability, and it is the right cost. Reproduced
synthetically (§7) and observed live in Part A.

**Not verified — that auto-update is disabled.** It is not. `silentUpgrade` is still `true` on this
workstation. Turning it off is a change to a third-party application's own settings on a live
production machine, and it is the workstation owner's call, not something this slice should make
silently — so it is recorded as the recommended operator action in §11 and nothing was edited.
Nothing in this report claims Meitu will not update itself again.

**The standing control that was actually exercised** is therefore observation, not prevention: the
soak reads the whole Meitu version topology — installed version directories, the launcher stub's
configured target, and the module path of every running instance — before job 1, after every
single job, and at every checkpoint, and stops the run if anything but the accepted binary is
running. §5's "Meitu is running from an unaccepted executable" is an assertion in the harness, not
a note in the log.

### 2.5 Phase 0 evidence

**Cold-start lifecycle.** Meitu was closed cleanly from its neutral state (editor → start page →
exit; no prompt appeared and none was answered), leaving nothing running. The gate was then run
against that cold workstation:

```text
installed versions      : 7.8.7.5, 7.8.8.0
launcher stub resolves  : 7.8.7.5
running instances       : 0
only accepted running   : True
Production Readiness    : Ready

[OK ] job  1 M   24.2s  apps 1->2 LAUNCHED  attempts 1  Succeeded/ReviewRequired  lock free->free
         out  Sessions/S_20260902T215654Z_85651ae1/Working/01a06420-…/…_P0_HD.png
         sha  5207F744E04267CE…  1280x960 Png 1159183 bytes
         cleanup the editor was returned to its signed empty state

running instances       : 1
  C:\Users\admin\AppData\Local\MeituApp\XiuXiu\7.8.7.5\XiuXiu.exe
```

From nothing running, PrintFlow launched the accepted binary by absolute path and produced a valid
output, then returned the editor to its signed empty state. Corroborated independently from the
operating system — `ProcessId 7484`, created `2026/9/3 9:56:55`, command line
`"C:\Users\admin\AppData\Local\MeituApp\XiuXiu\7.8.7.5\XiuXiu.exe"`.

**That run was reported as a test failure, and the failure was the harness's.** The gate asserted
that the whole version topology — including the set of running instances — was unchanged across
the enhancement. A cold start legitimately takes that set from none to one, so the assertion made
the cold-start case fail the very gate that exists to prove it works.

Fixed: the must-not-change comparison now covers the installed versions and the launcher's
configured target, and "only the accepted binary is running" is asserted separately — which is the
question that actually matters, and which held throughout. **Re-run from cold and confirmed:**

```text
running instances       : 0
Production Readiness    : Ready

[OK ] job  1 M   44.0s  apps 1->2 LAUNCHED  attempts 1  Succeeded/ReviewRequired  lock free->free
         sha  5207F744E04267CE…  1280x960 Png 1159183 bytes
         cleanup the editor was returned to its signed empty state

MEITU BASELINE STABLE — retain accepted 7.8.7.5
```

Three cold or warm starts of this gate produced byte-identical output — the same digest
`5207F744…` at 1 159 183 bytes each time — from three different sessions, which is the
determinism claim rather than a contradiction of the isolation one.

**Phase 0 gate, second run (warm, attaching to the instance the cold start left):**

```text
=== Phase 0: Meitu version topology ===
accepted executable     : C:\Users\admin\AppData\Local\MeituApp\XiuXiu\7.8.7.5\XiuXiu.exe
accepted version/digest : 7.8.7.5 / D65C6D823232
accepted binary present : True
installed versions      : 7.8.7.5, 7.8.8.0
launcher stub resolves  : 7.8.7.5
running instances       : 1
  C:\Users\admin\AppData\Local\MeituApp\XiuXiu\7.8.7.5\XiuXiu.exe
only accepted running   : True

gate                    : VerifiedEnvironmentGate
Meitu adapter           : meitu-xiuxiu-production-v1 / Production
Photoshop adapter       : photoshop-cc2019-production-v1 / Production
preset identity         : printflow-workstation-v1 1.15.0 (3392873ED0CA)
verified                : True   blocking failures: 0
Production Readiness    : Ready

[OK ] job  1 M   28.0s  apps 2->2 reused  attempts 1  Succeeded/ReviewRequired  lock free->free
         out  Sessions/S_20260902T220835Z_29aab3a8/Working/01a0642a-…/…_P0_HD.png
         sha  5207F744E04267CE…  1280x960 Png 1159183 bytes
         cleanup the editor was returned to its signed empty state
```

The topology fingerprint was identical before and after the enhancement.

### Phase 0 result

```text
MEITU BASELINE STABLE — retain accepted 7.8.7.5
```

No workstation change was made. No preset was created, edited or re-signed. No executable identity
check was weakened.

---

## 3. Soak structure as run, and where it stopped

Two soak runs were made against the committed Production configuration. Neither reached forty
jobs. What follows is what they did reach, and what stopped them.

```text
committed mode : Production
override       : none
```

| | Run 1 (10:10:19) | Run 2 (10:15:13) |
| --- | --- | --- |
| Stage A — mixed warm-up | job 1 only | **8 / 8 complete** |
| Stage B — Photoshop accumulation | — | stopped at job 9 of 12 |
| Stage C — Meitu sustained reuse | — | not reached |
| Restart checkpoint | — | not reached |
| Stage D — restart boundary | — | not reached |
| **Jobs completed** | 1 | **19** |

Run 1 stopped on a Meitu failure whose retry was mishandled by the harness rather than by the
product — `WorkflowCommand.Retry` returns a failed step to `Waiting` and deliberately does not
re-run it, so the harness recorded a step sitting at `Waiting` as a failed retry. Fixed in the
harness; run 2 exercised the corrected path and its one natural failure retried cleanly.

### 3.1 Per-job record, run 2

| # | k | dur | apps | attempts | terminal | lock | outcome |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | P | 13.0 s | 2→2 reused | 1 | Succeeded / ReviewRequired | free→free | OK |
| 2 | M | 14.6 s | 2→2 reused | 1 | Succeeded / ReviewRequired | free→free | OK |
| 3 | P | 4.0 s | 2→2 reused | 1 | Failed / Failed | free→free | **natural failure** |
| 3r | P | 12.1 s | 2→2 reused | 2 | Succeeded / ReviewRequired | free→free | OK on retry |
| 4 | M | 38.8 s | 2→2 reused | 1 | Succeeded / ReviewRequired | free→free | OK |
| 5 | P | 12.4 s | 2→2 reused | 1 | Succeeded / ReviewRequired | free→free | OK |
| 6 | M | 13.3 s | 2→2 reused | 1 | Succeeded / ReviewRequired | free→free | OK |
| 7 | P | 12.4 s | 2→2 reused | 1 | Succeeded / ReviewRequired | free→free | OK |
| 8 | M | 12.7 s | 2→2 reused | 1 | Succeeded / ReviewRequired | free→free | OK |
| 9 | P | 27.0 s | 2→2 reused | 1 | Failed / Failed | free→free | **stopped the soak** |
| 9r | P | 1.0 s | 2→2 reused | 2 | Failed / Failed | free→free | retry also refused |

Every job, successful or not, released the automation lock. No job reused another session's output
path. No external application was launched at any point — 2 → 2 throughout, every job attaching to
the instance the previous one left running.

Every successful job produced a validated output at its own path under its own session directory,
with dimensions and format read back independently: Photoshop jobs 600×400 TIFF, 2 452 724 bytes;
Meitu jobs 1280×960 PNG, 1 159 183 bytes. Every successful Meitu job recorded
`cleanup the editor was returned to its signed empty state` — **no cleanup warnings at all**.

### 3.2 The two natural failures (§15)

**Job 3 — `PhotoshopTargetLost`**, "Control 0x291F52 is not both visible and enabled, so it is not
something PrintFlow may read or drive. Nothing was written or pressed." A transient: the retry
succeeded twelve seconds later against the same session. Recorded, not erased; the failed attempt
stays in history and the retry wrote to its own attempt folder.

**Job 9 — `PhotoshopUnknownState`**, "No window owned by Photoshop matching the signed `另存为`
signature appeared within 20 s. Nothing further was sent." The retry one second later found
`PhotoshopBlockingDialog`. This one is not transient, and §4 below is about why.

Both failed closed: no Revision, no `PrintOutput`, `ReviewRequired` never reached, lock released,
dialog untouched.

### 3.3 Timing

Photoshop and Meitu kept apart, as §12 requires. The sample is too small for a meaningful p95 and
none is quoted.

| | n | min | median | mean | max |
| --- | --- | --- | --- | --- | --- |
| Photoshop (successful) | 4 | 12.1 s | 12.4 s | 12.5 s | 13.0 s |
| Meitu (successful) | 4 | 12.7 s | 14.0 s | 19.9 s | 38.8 s |

Photoshop's successful jobs are flat to within a second across Stage A — there is no warm-up effect
and no progressive drift in the *successful* population. The degradation in §4 does not show up as
slower successes; it shows up as a job that stops succeeding.

Meitu's single 38.8 s outlier (job 4) is roughly three times its neighbours. It is not a launch —
the process was reused, as every job was. Meitu's own variance on this operation is simply wide:
the Phase 0 gate ran the identical synthetic input three times and took **24.2 s** (cold launch),
**28.0 s** (warm, attached) and **44.0 s** (cold launch), producing byte-identical output every
time. A 12.7–38.8 s spread across the soak sits inside that. Four samples cannot separate adapter
variance from anything else, and nothing is claimed about it.

### 3.4 Resource checkpoints, run 2

Absolute values and the delta from the first checkpoint, as §8 requires. Only two of the six
checkpoints were reached.

| | | before job 1 | after job 8 | Δ |
| --- | --- | --- | --- | --- |
| **PrintFlow** | working set | 212 MB | 302 MB | +90 MB |
| | private | 161 MB | 245 MB | +84 MB |
| | handles | 407 | 611 | +204 |
| | threads | 22 | 22 | 0 |
| **Photoshop** | working set | 767 MB | 820 MB | +53 MB |
| | private | 3 817 MB | 3 929 MB | +112 MB |
| | handles | 2 098 | 2 227 | +129 |
| | threads | 92 | 98 | +6 |
| **Meitu** | working set | 379 MB | 395 MB | +16 MB |
| | private | 386 MB | 401 MB | +15 MB |
| | handles | 1 445 | 1 528 | +83 |
| | threads | 42 | 57 | +15 |

No threshold is applied to any of these and none is called a leak: two checkpoints over eight jobs
cannot distinguish growth from warm-up, and §8 forbids inventing a number to make it look like it
can. Photoshop's private commitment was already 3.8 GB before the soak began, on a process that had
been running since the previous day.

What §8 *does* name as a failure regardless of size is met, and it is not any of the memory rows:
**the next job required manual cleanup, and a later job failed because earlier PrintFlow-owned
state had accumulated.** That is §4.

---

## 4. The finding: Photoshop document accumulation is not survivable

### 4.1 Documents were measurable after all

§7 asks for Photoshop's open-document count "if determinable through an already accepted safe
inspection seam". There is no such seam — `IPhotoshopUiDriver` classifies a screen and probes one
document's identity, and Photoshop CC 2019 draws its own tabs, so a UI Automation walk finds no
`TabItem` at all. So the soak recorded a bounded read-only census of Photoshop's child windows by
class instead, and let the run say which class, if any, tracked.

One did, exactly:

| Observation | `OWL.Document` | `PSViewC` | change |
| --- | --- | --- | --- |
| before run 1 | 7 | 38 | baseline (Part A's residual document) |
| after run 1 job 1 | 8 | 39 | +1 |
| before run 2 | 8 | 39 | — |
| after run 2 job 1 | 9 | 40 | +1 |
| after job 3 (failed) | 10 | 41 | +1 |
| after job 3r | 11 | 42 | +1 |
| after job 5 | 12 | 43 | +1 |
| after job 7 | 13 | 44 | +1 |
| after job 9 | 14 | 45 | +1 |

Nine consecutive observations, +1 per Photoshop job, never −1. Note job 3: a job that **failed**
still left its document open, because the document is opened before the stage that failed.

**What is measured here is the delta, not the absolute count.** Both classes carry a fixed offset
that this run did not establish — Photoshop draws other document-shaped child windows — so
`OWL.Document 14` is not "fourteen documents". The count of *PrintFlow* documents is read off the
job record instead, where every one is accounted for: Part A's residual document, run 1's single
Photoshop job, and run 2's six (jobs 1, 3, 3r, 5, 7 and 9) — **eight open documents, none ever
closed**. The two agree on the only thing that matters, which is that the population grows by one
per Photoshop job and never shrinks.

### 4.2 What accumulation cost

* **Documents:** unbounded, one per Photoshop job including failed ones.
* **Ownership:** never ambiguous. Every one of them remained identifiable by absolute path, and
  every successful job proved its own document through the signed identity probe. Policy A's safety
  argument held completely — what failed was not safety.
* **Latency of successful jobs:** flat (§3.3). Accumulation did not make successful work slower.
* **Job 9, with eight PrintFlow documents open (class count 14):** the signed `另存为` surface did not appear within
  its 20-second timeout. The retry, one second later, found a modal standing that PrintFlow had not
  raised — a `PSExport_WindowClass` window titled `存储为 Web 所用格式 (100%)`, Photoshop's
  Save-for-Web dialog, with the main window disabled behind it.
* **The next job required manual cleanup**, which is §8's stated failure condition.

On the Save-for-Web window: PrintFlow sends only `Ctrl+O`, `Escape`, `Ctrl+Shift+S` and `Ctrl+W`,
and never presses Alt — `Win32ScopedInputSink` touches `VK_CONTROL` and `VK_SHIFT` and nothing
else. Save-for-Web is `Ctrl+Shift+Alt+S`. No modifier was stuck when the workstation was checked
afterwards. How a modifier reached Photoshop is therefore **not established**, and nothing is
claimed about it beyond the two facts that are: the dialog appeared, and PrintFlow refused to touch
it.

Throughout, PrintFlow behaved exactly as it should: it refused, created no Revision, released the
lock, and left the dialog for an operator. The defect is not in how it failed. It is that Policy A
guarantees this state will eventually be reached.

### 4.3 Photoshop Policy A — the decision

**Policy A is not operationally acceptable.** §10's Outcome A required, among other things, that
resource growth show no concerning unbounded pattern within the soak evidence and that no dialog
storm occur during normal operation. The document count grows without bound, one per job, and the
run ended in exactly the state Outcome A rules out.

---

## 5. §10 Outcome B: implemented, tested against the live application, and withdrawn

Outcome B was implemented as the smallest change the section allows: after the TIFF is written and
independently validated, close the working document this operation opened, through the existing
`CloseExactDocumentAsync` seam, recording the result on the attempt and never failing the operation
— exactly the shape Meitu's accepted `ReturnToNeutralStateAsync` already has.

Every §10 safety rule is satisfied by the seam rather than by the new code: it re-proves the
absolute path through the signed identity probe immediately before acting, refuses without sending
anything when the observed path is not the expected one, closes the active document only, never
answers a prompt, and never terminates Photoshop. The validated TIFF is a separate file that has
already been read back and hashed.

**It was then tested against the real Photoshop, and it does not work.**

`PhotoshopOwnedDocumentCleanupProbe`, driving only the accepted seam and synthesising no input of
its own, established two things.

**First, a defect in the seam itself** (§6 below): nine consecutive `CloseExactDocumentAsync` calls
returned **success** while the `OWL.Document` census stayed at 15 and the window title still named
the document each call claimed to have closed.

**Second, and decisive: the close raises Photoshop's unsaved-changes prompt.** With the seam fixed,
a close attempt left a `PSDialogBox` titled `Adobe Photoshop` standing, with Photoshop's main
window disabled behind it — the discard prompt, refused as `PhotoshopBlockingDialog` and left
untouched, exactly as the rules require.

This is not an edge case, it is the normal case. The working document is modified **by
construction**: the resize and the W1 Action are in-memory edits and the TIFF is a *Save As Copy*,
so the document is dirty on every successful job. Composing the cleanup would therefore leave a
blocking modal after essentially every job — trading an accumulating spare document for an
immediately blocked next job, which is worse.

**The composition was reverted.** `GenerateAsync` closes nothing, the structural test that pins
that still asserts what it always asserted, and its rationale now records a measured reason rather
than a predicted one. Part A's original reasoning for Policy A — that closing would raise a prompt
PrintFlow may not answer — is **confirmed by observation**.

### What would actually close the gap

Bounded cleanup needs PrintFlow to be able to recognise Photoshop's discard prompt as *its own*
prompt, on a document whose absolute path it has just proved, and dismiss it through a positively
identified control. That means signed UI evidence for the prompt, which means a new immutable
preset — a preset change, which §18 keeps out of this slice, and which deserves its own.

Until then the two available policies are both unacceptable, and that is the honest state of it:
Policy A accumulates until the workstation stops, and Policy B cannot complete without answering a
prompt.

---

## 6. Product change: the close seam reported success without closing

The one product change in this slice, and it is a correctness fix rather than a policy change.

`GuardedPhotoshopUiDriver.AwaitDocumentClosedAsync` decided the document had closed when the main
window title was no longer **equal** to the title recorded during the identity probe. Photoshop's
title carries more than the document's name: it also carries the unsaved-changes marker. So a
document that merely stopped being dirty produced a different title and was reported as closed
while it was still loaded.

Observed live, nine times in a row, with the document count unmoved and the same file still named
in the title. A seam whose entire purpose is to close exactly one proved document was returning
success for having closed nothing.

**The fix** asks about the document rather than about the string: the wait ends when the title no
longer names the closed document, tested through the same
`PhotoshopDocumentIdentityRule.TitleNamesExpectedDocument` the classifier already uses. The name
test is used in the negative direction only, which is the safe one — a title that no longer names
this file cannot be this file, whereas "the title changed" says nothing about which document is in
front. Without the signed identity signature the wait now refuses rather than assuming.

Confirmed live: the same call that previously reported nine false successes now closes a document
(census 15 → 14) and, where two loaded documents share a file name — which a retry produces —
reports `Timeout` ("the document may still be loaded") rather than claiming a success it cannot
prove. A false "still open" is a warning; a false "closed" is a lie.

Files changed:

| File | Reason |
| --- | --- |
| `src/PrintFlow.Infrastructure/Adapters/Photoshop/GuardedPhotoshopUiDriver.cs` | the fix above |
| `src/PrintFlow.Infrastructure/Adapters/Photoshop/ProductionPhotoshopOutputProcessor.cs` | comment only — records why there is no cleanup stage |

No `FailureCode` was added. No preset, evidence file or executable check was touched.
`appsettings.json` is unchanged and `Adapters.Mode` remains `Production`.

---

## 7. Tests

### Added — `GuardedPhotoshopUiDriverTests` (2)

* a document that only stopped being dirty is **not** reported as closed — the §6 regression;
* the previous document coming to the front **does** complete the close.

### Added — `ExternalStateHygieneTests` (4)

* Meitu never selects a newer installed version — the run lands on the accepted binary even when a
  process from a newer sibling directory is running;
* a newer Meitu holding the single-instance slot is refused (`MeituLaunchFailed`), not adopted, with
  nothing sent to it;
* Meitu re-enters through the accepted neutral state on every one of twelve repetitions, refuses a
  foreign loaded asset, and is ready again as soon as the editor is empty;
* Photoshop ownership stays absolute-path based after twelve prior PrintFlow documents — the
  thirteenth open, answered with the right file name from the wrong folder, is refused.

### Added — `SessionHygieneAndRecoveryTests` (2)

* twelve sequential Photoshop jobs each hold only their own output, with every earlier output still
  byte-identical afterwards;
* a restart with eight historical `ReviewRequired` jobs mutates none of them, and the whole-workspace
  file list is identical across it.

### Changed — `ExternalStateHygieneTests`, `PhotoshopWorkflowOutputTests`

The structural policy test still asserts that the composed operation closes no document; it now
strips comments first, because the source explains at length why there is no close there. The
composed-run stub records a close instead of throwing on one, so a close reappearing fails the test
that is about the composition rather than surfacing as a stub exception.

### Added — smoke infrastructure (inert by default)

* `ProductionMultiJobSoakSmoke` — the Phase 0 gate (`PRINTFLOW_SOAK_PHASE0=1`) and the forty-job
  staged soak (`PRINTFLOW_SOAK_SMOKE=1`);
* `PhotoshopOwnedDocumentCleanupProbe` — the §10 question, asked through the accepted seam
  (`PRINTFLOW_PS_CLEANUP_PROBE=1`);
* `WorkstationObservation` — bounded read-only process vitals, the window-class census and the
  Meitu version topology, all derived from the verified preset rather than from constants.

No monitoring package was added; every counter comes from `System.Diagnostics.Process` and two
`user32` calls.

---

## 8. Results

| Gate | Result |
| --- | --- |
| Build (`PrintFlowStudio.sln`) | **0 warnings / 0 errors** |
| Targeted — driver, hygiene, boundary, persistence, workflow-output | **90 passed / 0 failed** |
| Phase 0 live gate | **PASS** |
| 40-job soak | **NOT COMPLETED — 19 jobs** |

A complete suite is required by §20 and was run, because product source changed in shared adapter
infrastructure: `GuardedPhotoshopUiDriver` is common to every Photoshop route, and the accepted
policy test changed with it.

---

## 9. Session and filesystem census

Before run 2: 35 sessions, 70 Comparison files, 2 Quarantine files, 9 QA directories.

Read directly from the workstation afterwards:

```text
sessions        : 47   (14 created by this slice)
Comparison files: 70   (unchanged)
Quarantine files:  2   (unchanged)
```

The fourteen break down exactly as the job record predicts — 3 from the Phase 0 gate (its two
cold starts and one warm run), 2 from soak run 1 (jobs 1 and 2), and 9 from soak run 2 (jobs 1
through 9, a retry sharing its original's session). Every one carries the `PF_11600B_` prefix. Alongside them are QA directories under `D:\PrintFlowStudio\QA\Epic11600B\`, each holding
its own throwaway SQLite database: **no row was written to the operator's installation database.**

No session directory was lost, and Comparison and Quarantine are byte-for-byte the counts they
were before the slice began.

The soak's own census assertions — 0 sessions lost, Comparison and Quarantine unchanged, only this
run's own directories added, and a metadata fingerprint of five pre-existing sessions unchanged —
sit at the end of the harness and were **not reached**, because the run stopped at job 9. The
counts above are read directly and are what can be claimed.

Sessions from the interrupted runs are left in place. §18 forbids introducing cleanup to tidy a
test count, and §15 forbids erasing a failure from the record.

---

## 10. What was not reached

Stated plainly, because the Definition of Done turns on it:

* Stage B did not complete; Stages C and D did not start.
* The restart checkpoint was not exercised.
* Four of the six resource checkpoints were not taken.
* Meitu received five live jobs, not the twelve Stage C asks for, so the sustained-neutral-state
  claim rests on synthetic evidence plus those five.
* The session and filesystem census assertions did not run.
* No claim is made about long-run resource trends. Two checkpoints is not a trend.

---

## 11. Workstation state, and what it needs

**Photoshop needs operator attention before the next production run.** It is left holding eight
accumulated PrintFlow synthetic working documents, and its main window is currently **disabled**:
the Save-for-Web modal and the later discard prompt were each dismissed with a `WM_CLOSE` message —
the safe, non-destructive route, which on a save-changes prompt means Cancel — but Photoshop did
not re-enable its main window afterwards, and it cannot be re-enabled from here. Synthetic
keystrokes were not available to this session, so the remaining recovery is an operator's:

1. click Photoshop, or restart it;
2. discard the eight `PF_11600*` working documents — every one of them has a validated TIFF
   already written as a separate file, so discarding is always correct;
3. no customer document is among them (see §12).

Meitu is untouched and healthy: one instance, the accepted 7.8.7.5 binary, in its signed empty
state, with every soak job having returned it there.

**Recommended operator action on Meitu auto-update**, from §2.2 and deliberately not applied here:
set `silentUpgrade` to `false` in `%LOCALAPPDATA%\Meitu\XiuXiu\Config.json`, or turn auto-upgrade
off in Meitu's own settings. That is a change to a third-party application on a live production
workstation and is the workstation owner's call. Its effect has not been verified and nothing in
this report claims Meitu will not update itself again.



---

## 12. Customer safety

**No customer artefact was opened, selected, saved, closed, overwritten, moved, renamed or
deleted.**

Every input was a PNG generated at run time inside this run's own QA directory. The only files
written outside it are this run's own session directories under `Sessions\`.

Two unrelated operator applications were on the workstation throughout and neither was touched:

* **Photoshop** was holding PrintFlow's own residual synthetic document from Part A
  (`PF_11600A_…_E.png`), which is the Policy A leftover, not customer work;
* **CorelDRAW** (pid 7968, running since 2026-09-02 11:41) was holding a real customer file,
  `D:\2工作Disk\…\O le Vaalau.cdr`. PrintFlow drives only Photoshop and Meitu; CorelDRAW was
  observed once, in a read-only window enumeration, and was never activated, focused or sent
  anything. It held the foreground for part of the run and that changed nothing: both adapters
  acquire and re-verify the foreground for the window they are about to drive.

Pre-existing sessions were sampled by **metadata only** — relative path, byte length and last
write time — never by reading content, as §13 requires. Digests were computed only for this run's
own outputs.

---

## 14. Dependency and security

`dotnet list package --vulnerable --include-transitive`: **no vulnerable packages** in any of the
five projects.

`--deprecated`: `xunit 2.9.3` (Legacy → `xunit.v3`), test project only. Unchanged and deliberately
out of scope — §21 keeps the xunit.v3 migration in its own maintenance lane. No monitoring package
was introduced for the §7 counters.

## 15. Git

Branch `master`, based on `e752ca3`. Epic 11500 and 11600-A history is untouched: no amend, no
rebase, no rewrite, no push. Phase 0 required no workstation or preset change, so there is nothing
to keep separable from the product fix; the slice is one local commit.
