# Epic 11600 — Part B1

## Photoshop Owned-Document Cleanup & Signed Discard-Prompt Evidence

## 1. Verdict and scope

The product change, signed evidence, preset verification, automated tests, build, security audit,
Production Readiness check, one-job proof, and the resumed five-job Live B proof all passed. The
operator manually removed the known historical synthetic Photoshop documents before the resumed
run; PrintFlow did not inspect, close, or automate their cleanup.

B1 is accepted for the required fresh 11600-B soak. This report does not itself change the existing
Epic 11600 Part B verdict.

## 2. Manual workstation precondition

The operator confirmed the manual historical cleanup before the resumed run. PrintFlow performed
no scan-and-close, close-all, restart-based cleanup, or other historical cleanup. The resumed
read-only preflight found:

| Observation | Result |
| --- | --- |
| Accepted Photoshop processes | 1 |
| Process | PID 20848, accepted executable `D:\Adobe Photoshop CC 2019\Photoshop.exe`, responsive |
| Active title | `Adobe Photoshop CC 2019` |
| Classified state | `KnownStartScreen` |
| Main frame | enabled |
| Titled owned dialogs | 0 |
| `OWL.Document` children | 0 |
| Total child windows | 999 |
| Other tracked classes | `PSViewC 31`, `OWL.TabGroup 18`, `OWL.TabPane 4`, `Photoshop_Document 0` |

The absence of an `OWL.Document` child and the signed no-document title establish that neither a
historical `PF_11600*` / `PF_C2A_*` document nor an unrelated document was loaded. No unrelated or
customer document was enumerated beyond that bounded state classification.

## 3. Authoritative live prompt observation

The prompt was raised by requesting close on the exact synthetic Working document
`PF_C2A_20260903-112035-F47A8A0A.png`, after its TIFF had been written and validated. A read-only
native-control census observed:

| Element | Native evidence |
| --- | --- |
| Prompt | owned by accepted Photoshop PID; class `PSDialogBox`; title `Adobe Photoshop`; enabled |
| Main frame | class `Photoshop`; expected synthetic document in title; disabled while prompt open |
| Question | `Static`, control id `203`; `要在关闭之前存储对 Adobe Photoshop 文档 “PF_C2A_20260903-112035-F47A8...”的更改吗？` |
| Save | `Button`, control id `10`, text `是(Y)` as exposed by the accessibility view (`是(&Y)` in native control text) |
| Discard / do not save | `Button`, control id `11`, text `否(N)` as exposed by the accessibility view (`否(&N)` in native control text) |
| Cancel | `Button`, control id `12`, text `取消` |

The evidence-only pass pressed no prompt control. The prompt was cancelled manually with Escape
after capture so the synthetic document was preserved for implementation work.

The same observation also resolved a post-Save-As-Copy identity fact: the active document title
continued to name the original `.png`, while the signed Save As filename field carried the same
stem with the last-copy `.tif` extension. The address field still named the exact Working
directory. The accepted rule therefore uses the exact title basename as authoritative and permits
only the signed same-stem extension substitution as corroboration; it never weakens folder or
absolute-path equality.

## 4. Signed evidence and immutable preset

New read-only evidence:

```text
D:\PrintFlowStudio\Baseline\workstation-v1\apps\photoshop-2019\owned-document-cleanup.json
length  5658 bytes
SHA-256 73934881335D3A67D0B7F135B268988931FC0733E60F403D2862774B98072148
```

New read-only preset:

```text
printflow-workstation-v1 1.16.0
D:\PrintFlowStudio\Baseline\workstation-v1\preset\printflow-workstation-v1.16.0.json
length  26146 bytes
SHA-256 6396FB4EB87F69C6789304CE191453654B2B75E82A5A9AB0161F90556A6F1A80
```

It supersedes exact preset `1.15.0`, adds the owned-document cleanup evidence to the complete
integrity chain, and changes no executable, Action, OS, display, culture, or adapter-mode
acceptance.

Old preset immutability was rechecked:

```text
printflow-workstation-v1 1.15.0
length  25649 bytes
read-only true
last write UTC 2026-09-01T00:53:56.7866398Z
SHA-256 3392873ED0CA38BB410EA6725B6C4D0392F2514ECB10D9CF825B18D0DF785D16
```

All 28 source-manifest-integrity entries in `1.16.0` matched. Committed configuration points at
`1.16.0` and its full digest. `Adapters.Mode` remains `Production`. Meitu acceptance and adapter
logic were not changed; the accepted result remains `MEITU BASELINE STABLE — retain accepted
7.8.7.5`.

## 5. Closed recognition and causality contract

The new vocabulary is one `PhotoshopOwnedDocumentCleanupSignature`, not a generic dialog or
confirmation abstraction. The complete signed state requires all of the following:

1. the accepted Photoshop process and main window still own the operation;
2. no titled owned dialog exists and the main frame is enabled before close;
3. the signed Save As identity probe proves the exact expected Working absolute path;
4. the boundary is checked again after that probe is cancelled;
5. PrintFlow activates the accepted main frame and sends the named close action once;
6. exactly one newly appearing titled owned window matches the signed prompt class/title;
7. the signed question control names the document just closed, including the observed bounded
   ellipsis form;
8. the complete signed Save, discard, and Cancel controls all match id, class, and observed text;
9. the prompt itself is foreground, visible, enabled, and unchanged immediately before input;
10. only signed control id 11 is pressed, once;
11. the prompt disappears, the main frame becomes enabled, and the expected document is proved
    gone.

A prompt present before the close can never pass this sequence. Unknown, Save-for-Web, Save As,
Open, arbitrary Photoshop, multi-prompt, altered-control, target-loss, and foreground-loss states
all refuse without invoking discard.

## 6. Owned close and output lifecycle

`ProductionPhotoshopOutputProcessor.GenerateAsync` now composes the existing
`CloseExactDocumentAsync` seam only after Save As Copy has produced an independently parsed and
validated TIFF. It passes the current operation's exact approved Working path back into the close
seam; no older session or discovered filename is eligible.

The driver supports both accepted paths:

- clean close: wait until the expected document is no longer the controlled document;
- dirty close: recognise the complete signed prompt, invoke signed discard once, wait for prompt
  removal and main-frame re-enablement, then prove the expected document is gone.

The 11600-B false-close fix remains intact. A changed title or removed dirty marker is not closure.
If closing B reveals a previous A with the same filename, the driver waits, makes one signed
absolute-path probe, accepts only a different directory as evidence that B is gone, and performs
no second close or discard.

The validated TIFF candidate records hash, length, and shape before cleanup. The output factory
re-reads the file after cleanup and refuses TIFF drift. Cleanup refusal does not delete or hide a
valid TIFF: the adapter returns the valid output with an explicit `cleanup WARNING`, allowing the
normal successful Revision/PrintOutput lifecycle while making the unsafe external state visible.
The next operation still performs normal Photoshop state checks and refuses a retained modal.

## 7. Negative-case matrix

| Case | Expected and automated result |
| --- | --- |
| Exact absolute Working path | eligible for one close |
| Wrong path / customer or unowned active document | no close; discard count 0 |
| Same filename, wrong directory | no close; discard count 0 |
| Prompt already present | no close/input; discard count 0 |
| Save-for-Web after close | refusal; discard count 0 |
| Save As after close | refusal; discard count 0 |
| Arbitrary Photoshop error | refusal; discard count 0 |
| Missing or altered discard control | refusal; discard count 0 |
| Target/foreground ownership lost | refusal; discard count 0 |
| Process exited or window handle moved to another process | refusal; no further input |
| Two accepted Photoshop processes | readiness refusal as ambiguous; no input |
| Dirty exact document + complete signed prompt | discard count exactly 1 |
| Clean close with no prompt | success; discard count 0 |
| B closes and same-name A appears | one close and one discard only; A remains |
| Dirty marker changes but document remains | timeout/refusal, never false success |

The recording/integration seam also runs 12 sequential synthetic Photoshop jobs and asserts one
owned close per job with no retained-document growth. Cleanup-failure tests prove the valid output
and explicit warning survive, and a deliberate TIFF mutation during cleanup is rejected by the
post-cleanup hash check.

## 8. Automated verification

| Gate | Result |
| --- | --- |
| Focused cleanup/identity/workflow/hygiene tests | 94 passed / 0 failed |
| Broader configured Photoshop/preset safety selection | 158 passed / 0 failed |
| Post-race-fix cleanup/output/preset selection | 60 passed / 0 failed |
| Final prompt/driver/output/preset selection | 61 passed / 0 failed / 0 skipped |
| Architecture, boundary, banned-API tests | 400 passed / 0 failed / 0 skipped in 4 s |
| Build | 0 warnings / 0 errors in 0.91 s |
| NuGet vulnerability audit | no vulnerable direct or transitive packages in all five projects |
| Final complete suite | 10121 passed / 0 failed / 0 skipped / 10121 total in 2 m 13 s |

The first complete-suite attempt found one pre-existing culture-sensitive test assertion: it
looked for English `restart` while the accepted workstation correctly rendered the Simplified
Chinese restart instruction. The test now pins `en-US` for that English wording assertion. Its
isolated red/green loop passed twice after the correction (67 ms and 63 ms); the final complete
suite above is the post-fix result.

No xUnit migration or dependency change was made.

The blocked 11600-B report remains unchanged. Its exact historical Part-B complete-suite total was
not retained in that report or recoverable command evidence, so no number was inferred or added.
The current B1 final-source total is recorded above.

## 9. Production Readiness

The live verifier and the registered application gate both used committed preset `1.16.0`:

```text
PresetIntegrity      Passed — 6396FB4EB87F…
EvidenceIntegrity    Passed — 28/28 entries
blocking checks      10/10 passed
gate(Production)     ALLOWED
Production Readiness Ready
Adapters.Mode        Production
```

The existing read-only-attribute advisory remains: 16/28 integrity-referenced files are not
marked read-only, while every authoritative SHA-256 matched. The external-application UI-language
note also remains advisory. Neither is blocking.

## 10. Controlled live proof

### Live A — one successful cleanup

The final successful run used only a generated 1200×800 alpha PNG under its own QA workspace:

```text
token                 20260903-114153-F86F618B
preset                printflow-workstation-v1 1.16.0 / 6396FB4EB87F…
adapter               photoshop-cc2019-production-v1 / Production
TIFF SHA-256           4E21B3143FDAF354BDFD1B20CA4B0228B585940ACFEA6A7F5CFED31852554089
TIFF bytes             2452724
TIFF pixels            600x400
attempt                Succeeded
step                   ReviewRequired
cleanup note           signed owned-document discard completed; expected Working document gone
OWL.Document census    12 -> 12
new retained documents 0
test result            1 passed in approximately 18.7 s
```

The smoke re-read the on-disk TIFF and matched its recorded SHA-256 and byte length after discard.
No approval, rejection, promotion, or TIFF deletion occurred. The synthetic job's document was
closed; the prior synthetic document returned to the front and was not touched further.

An earlier run (`20260903-114016-090C65AF`) correctly retained its valid TIFF and recorded a
cleanup timeout warning. Investigation showed Photoshop had completed the discard but the
postcondition sampled too early. The postcondition was changed from an immediate probe to a
bounded poll, covered by targeted tests, and the final run above passed with census 12 → 12.

### Live B — five consecutive jobs

The resumed proof ran five new synthetic Photoshop Production jobs consecutively through the real
registered graph. Every job used the committed `Adapters.Mode = Production`, preset `1.16.0`, the
real `VerifiedEnvironmentGate`, and `ProductionPhotoshopOutputProcessor`. The gate returned
`ALLOWED` before each job. Photoshop remained PID 20848 throughout; it was not restarted.

| Job | TIFF SHA-256 | TIFF | `OWL.Document` before → immediate → settled | Discard / cleanup | Attempt / step | Lock |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `4883E3CF497EF049E4EEEAB6EE926FCAA636BEFCF0BF5332AC46B00BBF4E3753` | 2,452,724 bytes, 600×400 | `0 → 0 → 0` | signed discard completed once; expected Working document gone; no warning | `Succeeded` / `ReviewRequired` | free → free |
| 2 | `EB907D33D7D6FBF29B4ACABF6A9BE2D4212E4293A12C0DA584C2DA0E4A390074` | 2,452,724 bytes, 600×400 | `0 → 1 → 0` | signed discard completed once; expected Working document gone; no warning | `Succeeded` / `ReviewRequired` | free → free |
| 3 | `BDB92289312A76BCD282839C8973A3BC85E1F75D4F58DEAD0864C473E6B5C1AE` | 2,452,724 bytes, 600×400 | `0 → 1 → 0` | signed discard completed once; expected Working document gone; no warning | `Succeeded` / `ReviewRequired` | free → free |
| 4 | `2CC44F35653F4F4D9C1200B2EB7517AC3C8EBC2AE33928D128D3D033825FA62F` | 2,452,724 bytes, 600×400 | `0 → 1 → 0` | signed discard completed once; expected Working document gone; no warning | `Succeeded` / `ReviewRequired` | free → free |
| 5 | `CAEF8F5479E3EA082C8413DEFA7E62E2515BFEAECF7240276727B9713A2A7795` | 2,452,724 bytes, 600×400 | `0 → 1 → 0` | signed discard completed once; expected Working document gone; no warning | `Succeeded` / `ReviewRequired` | free → free |

The exact Working source paths re-proved for cleanup were:

| Job | Absolute Working path |
| --- | --- |
| 1 | `D:\PrintFlowStudio\Sessions\S_20260903T001037Z_be3a6c6f\Working\01a0649a-72a5-78f6-9aea-34c083c5c238\PF_11600B_20260903-121034-F19B4F91_B1_01.png` |
| 2 | `D:\PrintFlowStudio\Sessions\S_20260903T001052Z_a55f5b81\Working\01a0649a-aa77-7ef5-8883-a7f2d93154e3\PF_11600B_20260903-121034-F19B4F91_B1_02.png` |
| 3 | `D:\PrintFlowStudio\Sessions\S_20260903T001107Z_ab114d95\Working\01a0649a-e446-7011-8f6f-1c3922de5112\PF_11600B_20260903-121034-F19B4F91_B1_03.png` |
| 4 | `D:\PrintFlowStudio\Sessions\S_20260903T001122Z_1fb40113\Working\01a0649b-1dcc-70b4-8b42-220c977c9a3a\PF_11600B_20260903-121034-F19B4F91_B1_04.png` |
| 5 | `D:\PrintFlowStudio\Sessions\S_20260903T001136Z_a1d18c05\Working\01a0649b-5770-769c-9566-284c720ac06a\PF_11600B_20260903-121034-F19B4F91_B1_05.png` |

Each TIFF was independently re-read after cleanup and matched its full recorded SHA-256. Each
Working source path was the current job's exact attempt-scoped absolute path under
`D:\PrintFlowStudio\Sessions\...\Working\<attempt>\`; the cleanup success is emitted only after
that path is re-proved, the signed dirty-document prompt is recognised, the signed discard control
is invoked through the single-input seam, and the expected document is proved gone. No job retried.

For jobs 2–5, Photoshop's `OWL.Document` child outlived the already-completed close by a bounded
fraction of a second. The same accepted five-second census settle used by the existing live smoke
returned to zero every time; the no-document title and cleanup postcondition already held. An
earlier measurement-only attempt stopped at job 2 on the immediate `0 → 1` sample, sent no repair
input, and was not counted. A subsequent read-only classification found `KnownStartScreen`, so the
fresh five-job sequence above began from job 1 with no retained document.

Final Photoshop state: PID 20848, title `Adobe Photoshop CC 2019`, `OWL.Document 0`, total child
windows 999, no blocking dialog, enabled main frame. Required invariant after every successful job:

```text
retained PrintFlow Working document delta = 0
```

## 11. Customer and external-state statement

All evidence and live inputs created by this slice were synthetic and stored under dedicated QA
directories. No customer image content was captured. No unrelated/customer Photoshop document was
opened, selected, closed, saved, discarded, or modified. The product contains no
historical-document sweep. Meitu logic, accepted executable, and baseline evidence are unchanged.

The computer-use workflow influenced only the controlled evidence pass: it selected the uniquely
named synthetic tab, raised its prompt, read the visible accessible prompt/control descriptions,
and cancelled that evidence-only prompt. Product action uses the stronger signed native ids,
classes, ownership, text, foreground, and causal-transition checks.

## 12. Git state

Starting commit: `628b7e0` on `master`, clean.

New local commits:

- `fa2e29e` — signed Photoshop discard-prompt evidence contract and preset `1.16.0` wiring;
- `f4ac0b2` — exact-owned-document close/discard implementation and tests;
- `c1cb5b0` — deterministic culture pin for the English readiness wording test;
- `87e6498` — the original blocked B1 report;
- the follow-up report/evidence commit containing this resumed proof.

No commit was amended or rebased, and nothing was pushed. The existing 11600-B report and verdict
were not rewritten.

## 13. Required next action

Return immediately to the existing 11600-B plan and start a fresh 40-job soak from job 1 against
preset `1.16.0`, committed Production mode with no override, and the final B1 product source. Do
not resume the earlier 19-job run. B1 itself does not change 11600-B's blocked verdict; Part B
remains blocked until all 40 jobs, all stages, the restart checkpoint, all resource checkpoints,
and the final filesystem/session census complete.

**11600-B1 PASS — PHOTOSHOP OWNED-DOCUMENT CLEANUP READY FOR SOAK RERUN**
