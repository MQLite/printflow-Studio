# Epic 11600 — Part B1

## Photoshop Owned-Document Cleanup & Signed Discard-Prompt Evidence

## 1. Verdict and scope

The product change, signed evidence, preset verification, automated tests, build, security audit,
Production Readiness check, and one-job live cleanup proof all passed. B1 is nevertheless blocked:
the operator-only historical-document cleanup in §0 was not performed, so the required five-job
live proof was not run.

This does not change the existing Epic 11600 Part B verdict. The 40-job soak was not rerun.

## 2. Manual workstation precondition

The required manual preflight cleanup was **not performed or confirmed by the operator** during
this slice. PrintFlow did not automate it.

Photoshop was not running at the initial process check. When the first controlled synthetic proof
started Photoshop, CC 2019 restored its prior tabs. A final read-only Win32 census found:

| Observation | Result |
| --- | --- |
| Accepted Photoshop processes | 1 |
| Process | PID 20848, responsive |
| Active title | `PF_C2A_20260903-112035-F47A8A0A.png @ 100% (图层 1, W1/8) *` |
| `OWL.Document` children | 12 |
| Total child windows | 1073 |

These are historical/synthetic QA documents. No scan-and-close, close-all, restart-based cleanup,
or other historical cleanup was introduced or run. This unmet operator precondition is the reason
Live B and the PASS verdict are withheld.

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

**Not run.** The operator-only historical synthetic-document cleanup remained unconfirmed and the
read-only census still showed 12 documents. Running the repetition anyway would violate the
explicit evidence precondition and could not support a PASS verdict.

## 11. Customer and external-state statement

All evidence and live inputs created by this slice were synthetic and stored under dedicated QA
directories. No customer image content was captured. No unrelated/customer Photoshop document was
closed, saved, discarded, or modified. The product contains no historical-document sweep. Meitu
logic, accepted executable, and baseline evidence are unchanged.

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
- the commit containing this report.

No commit was amended or rebased, and nothing was pushed. The existing 11600-B report and verdict
were not rewritten.

## 13. Required next action

The operator must manually discard the known historical `PF_11600*` and synthetic `PF_C2A_*` QA
documents while leaving unrelated documents untouched. Then rerun Production Readiness and Live B:
five consecutive synthetic Photoshop Production jobs in the same accepted Photoshop process, each
with a valid unchanged TIFF, `ReviewRequired`, free automation lock, no cleanup warning, and zero
retained-document growth. Only after that passes may B1 receive a PASS/PASS WITH NOTES verdict.
The next step after a B1 PASS is a fresh 40-job 11600-B soak from job 1; B1 itself must not change
11600-B's blocked verdict.

**11600-B1 BLOCKED — PHOTOSHOP CLEANUP NOT SAFE**
