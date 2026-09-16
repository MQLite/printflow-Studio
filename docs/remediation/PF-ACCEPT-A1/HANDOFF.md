# PF-ACCEPT-A1 — Handoff

**REGRESSION V3 IMPLEMENTED AND VERIFIED — ONE FRESH COMPLETE A1 RAN: PENDING 4/7, NO FAILED
ASSERTION — THREE OPERATOR VISUAL DECISIONS OUTSTANDING**

Started from actual HEAD `d7d8d23`; no reset. Operator option **(b)** recorded. Accepted preset
remains exactly 1.18.0 (`8484F0AA…0E8F`); no Product source, preset, image or historical result was
changed. v1/v2 manifests, runs, claims, reviews and retained pairs are untouched.

Implementation `a39846d` (tooling/tests only). v3 differs from v2 in exactly one expectation:
fine-hair `trimBoundsStrictlyInsideCanvas: true` becomes `trimMatchesAlphaBoundsAndMargins: true`,
with reason prose to match. The check recomputes, independently of the Product, the minimal
`alpha > 0` rectangle of the actual pre-Trim cutout, expands it by the attempt's recorded margins,
clamps to the canvas, compares that with the persisted ContentBounds/AppliedBounds, and requires the
decoded output to be exactly that crop of the decoded input, bound to the approved cutout and the
exported Trim artefact. Full canvas passes only when that is the correct crop; a no-op fails when a
removable border exists. v2's caller assertion and v1's refusal are unchanged. Report:
`docs/printflow/regression-v3-fine-hair-trim-contract.md`.

Offline: focused 113/0/0; clean Release build 0 warnings/0 errors; one scoped read-only review
(no blocking issues, actionable points applied before the pair); pair
`f0ac92e2-853e-4d2f-a6e4-c0af1a24e0e8` receipt
`BEBEDFFC235F40AE66554F13D0BD0354C20DE75F41FA654341621DA836BFBDC4`, loaded-pair proof 1/1, paired
focused 58/58; explicit v3 preflight PASS, no result written. `D:\PrintFlowStudio\TestData\v3`
holds 9/9 byte-identical copies of the current v2 files and no run directories. Set content digest
`75DA6EC6306B3C4E59EB1DE8644DA1C22B214AF066DBC2555533B8B6E6ACDEE8`.

A1 run `a1-v3-20260916-121334-903986e7`, invocation `cbffa3cc-ea7d-4f8b-be52-cb94d8361e17`, after
one current exclusive-desktop confirmation. Readiness `Verified: true`, every blocking check passed,
`CandidateProblems` empty, no override/diagnostic/build/restore, wrapper exit 1 on the Pending
verdict. `result.json` is **Pending, 4/7**: TRANSPARENT_PNG, PSD_WITH_COMPOSITE_PREVIEW,
SINGLE_PAGE_PDF and REFERENCE_PRODUCTION_TIFF Passed; NORMAL_JPG_PORTRAIT,
COMPLEX_BACKGROUND_FINE_HAIR and COMPLETE_CUSTOMER_DESIGN Pending on visual review. **No structural
assertion failed in any case.** `Reviews` is empty; no decision was recorded or transferred.

Fine-hair structural result (`trimMatchesAlphaBoundsAndMargins`, held): attempt
`01a0a790-ecaa-78c2-8705-88cc22b3a5a1`, margin `TightCrop` 0/0/0/0; input Revision
`01a0a790-ec35-7cca-a08d-4fbc23d2a834` (`0C2932DB…5299`, 1200x1600 Bgra32); output Revision
`01a0a790-ed7b-7ba4-b12c-87934ba0f650` (`83914805…8C7D`); independent `alpha > 0` content
`[0,0 → 1200,1600)` equals persisted ContentBounds; expected applied rectangle equals persisted
AppliedBounds; decoded output pixels equal that region exactly. Full extent was retained because
alpha-bearing content reaches all four edges — the diagnosed property of these cutouts.

### Operator decisions required (three; none may be inferred)

Local paths only; the fixture manifests forbid upload, and the agent did not view the images.

- **FINE-HAIR-VISUAL-001** — `…\a1-v3-20260916-121334-903986e7\FIX-FINE-HAIR-001-cutout.png`,
  SHA-256 `0C2932DB64BD5745FD73D467325E2385F18A91AA790FDFDB6AED8E1D1D755299`. "Are individual hair
  strands still retained at the boundary, without a hard halo, and is the foliage background fully
  removed rather than partly retained as coloured fringing?" The structural pass says the crop is
  arithmetically right; it says nothing about whether the retained edge pixels are hair or foliage.
- **PORTRAIT-VISUAL-001** — `…\FIX-PORTRAIT-001-enhanced.png`, SHA-256
  `1E41CF81E51B02A3243018F7271B0C1300DB1649F1E6945619326FE9410656FC`; the two-axis size expectation
  held at 1200x1600. "Does the enhanced export still look like a correctly enhanced portrait —
  subject sharp, skin tone unshifted, no visible artefact introduced along the hair or shoulder
  edges?"
- **CUSTOMER-DESIGN-VISUAL-001** —
  `D:\PrintFlowStudio\Sessions\S_20260916T001449Z_e131feb5\Working\01a0a790-f4ee-7f34-8a57-66bf52614e04\FIX-CUSTOMER-DESIGN-001_51mm_CMYK_W.tif`,
  SHA-256 `E53499E326EAF4A6227B32C873527BC1D37174C2542FA92993D97131FEF8C960`; validated 600x900,
  5 samples, 300 dpi, uncompressed. "Does the produced TIFF show the complete design at the
  requested size, with the W1 channel covering the intended ink region?"

Earlier artwork approvals do not transfer to these new objects. Record actual decisions only with:

```powershell
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1 `
    -RunId 'a1-v3-20260916-121334-903986e7' -RecordVisualReview '<actual-decisions-file>'
```

That is original-harness re-aggregation of this run, not a re-execution: supply no receipt, no set
root and no category filter. If the Operator reports unacceptable fine-hair quality, that is a
separate real issue; it must not be answered by redefining geometry or altering pixels.

Run evidence hashes: result `8EEAC73F93FEE8C0B46BBBF0A0F369B06E19245A865A1052975B9A75F6746613`;
readiness `7F746BBD507A4693B4499B126E6EDD861DBEB7538E448D7CD8487AB879742E52`; claim
`A1C597E6F24297FED52F61552146DC32A94AEA773AD3FF9D2A6442B9C8295058`; fine-hair case
`6BA0F08A411546CD4A3BF2A14DF640E2CE1E1A20B02D5B6E3E4332277659A66B`; regression database
`00FB392B3DEF82AD95B3E2739082F7AA5B81879751B8E5516A4F4DEDB70686A8`; live log
`049B329A31C826DB9EFC91542FE8A8092B7AF2143D55ED2B37CCE501F0184A89`; command metadata
`022B44D103E0228E445015AAFEC4A2E128EFB9E277350D0EA88C7F366BDEFDE6`. Offline evidence is under
`artifacts/pf-accept-a1/regression-v3/`.

After the run every case recorded the automation lock free, the v3 inputs still hash to their
manifests, and all three pairs (`f0ac92e2…`, `fd56e834…`, `ee8e0280…`) reverified with unchanged
receipts. Photoshop is document-free at `Adobe Photoshop CC 2019`; Meitu shows
`美图秀秀-图片编辑` and was not touched.

Stop here: the run is complete and only genuine Operator review remains. No A2/revalidation, A3,
Product refactor, preset change, image alteration, signing, install, deploy, push or Jira closure
was performed. Execution: Claude Code, Opus 5; one scoped read-only reviewer; no independent
acceptance or release approval is claimed.

**FINE-HAIR TRIM DIAGNOSED OFFLINE — PRODUCT AND CALLER BOTH CORRECT — NO REPAIRABLE MISMATCH —
STOPPED AT A FROZEN-MANIFEST REQUIREMENT ISSUE; NO CODE CHANGE, PAIR OR LIVE A1**

Started from actual HEAD `a051ce89a19bdae683ce4cf45d2596864e54480f`; no reset. Entirely offline:
no Meitu/Photoshop operation, lease, build pair, suite or standard-set run. Accepted preset 1.18.0
(`8484F0AA…0E8F`) and frozen v2 set unchanged.

Facts for incident `a1-repaired-1180-20260915-155942-a4173998` (retained files and a read-only copy
of `regression-run.db`, all rehashed unchanged):

- Trim input: Background Removal Revision `01a0a339-8596-72a7-9898-5c7a1f9111c3`, SHA-256
  `A04019741C8C3200E6F76A51B53ADA9008900E8EE48385C422CE421061ED01F6`, 1200×1600 RGBA8, approved by
  the harness (`regression`) before Trim.
- Producing attempt `01a0a339-8600-7401-85ef-2e34d380b32a`, `internal-alpha-trim-v1`, SUCCEEDED;
  margin `TIGHT_CROP` 0/0/0/0 (session and attempt).
- ContentBounds `[0,0 → 1200,1600)`; AppliedBounds `[0,0 → 1200,1600)`.
- Output Revision `01a0a339-86ae-7429-839a-145bbf7f6fe3`, SHA-256
  `E579FA70D276E69D02E3D1098AF5D51BC40EBC817D0AAB81E751724694E6B009`, 1200×1600.
- Assertion operands: `trimmedArtefact` 1200×1600 vs `produced` (cutout) 1200×1600; rule
  `width < || height <`.

Independent decode (raw PNG chunks + zlib + unfilter, no WIC) of the pre-Trim cutout:
- **alpha > 0 bounds are the whole canvas**;
- fully transparent border depth is 0 on every edge;
- the edges carry substantial, not merely stray, alpha:

| Edge | Pixels with alpha > 0 | Max alpha | Contiguous runs |
| --- | --- | --- | --- |
| Top | 1,063 of 1,200 | 254 | `[90..1102]` |
| Left | 535 | 247 | `[128..660]` |
| Right | 604 | 223 | `[103..693]` |
| Bottom | 1,129 | 255 (1,079 opaque subject pixels) | — |

Even alpha ≥ 128 spans the full canvas. Expected geometry under the recorded margin and canvas
clamp is therefore exactly the full canvas. The produced PNG is pixel-identical to the input
(RGBA and native samples), and the persisted geometry and `NoChangeRequired` derivation agree.
**The Product is correct.**

The caller faithfully implements the frozen v2 manifest property
`trimBoundsStrictlyInsideCanvas: true` (`FIX-FINE-HAIR-001`; manifest reason: "the trim found
bounds inside the canvas"). It would still reject a missed removable border. Its failure is
truthful, so **the caller is correct** and no `<`→`<=` or other change was made.

The same analysis of the operator-approved `PF-FIX-MEITU-CONFIRM` cutout of the same fixture
(Revision `01a0a248-d7eb-7d20-ad5d-389610b1287a`, SHA-256 `731BE2E0…DB19`, used only as read-only
comparison, not substituted) gives the same result:
- alpha > 0 on all four edges;
- alpha ≥ 128 spans the full canvas;
- zero transparent border.

The frozen requirement is therefore not attainable for this fixture on Meitu 7.8.8.2 without
forbidden changes (a threshold, erasing low-opacity hair, colour inference, margin changes,
substituting another cutout, or reinterpreting the manifest).

Evidence (ignored): `artifacts/pf-accept-a1/fine-hair-trim-diagnosis/`. It holds the tool
source and `SHA256SUMS.txt`, with these hashes:

| File | SHA-256 |
| --- | --- |
| pixels report | `7890B328…33F8D` |
| persistence | `C6F15E96…A454` |
| approved-cutout edges | `6E13A702…6AA6` |
| TIFF header | `E2516F2A…A50A` |

### Operator decision required (one, precise)

The v2 manifest `FIX-FINE-HAIR-001.expectedProperties.trimBoundsStrictlyInsideCanvas = true`
contradicts the actual Meitu cutouts of that fixture, which keep non-zero alpha on every canvas
edge. Choose one:

- **(a)** Keep the requirement. COMPLEX_BACKGROUND_FINE_HAIR stays Failed. A1 cannot pass on v2
  until a fixture/set revision produces a cutout with a genuine transparent border. That needs a
  new set version and hashes; it is not done here.
- **(b)** Authorise a new set version whose structural trim property is the deterministic
  contract instead: recorded ContentBounds equal independent alpha > 0 bounds, AppliedBounds
  equal their margin/clamp expansion, and output equals that crop. Then re-run A1 on that
  version. Edge quality stays FINE-HAIR-VISUAL-001.

No manifest, harness or Product change was made under this authorisation.

### Visual-review objects (unchanged, no decision recorded)

`Reviews` is empty.

- **FINE-HAIR-VISUAL-001** — `…\a1-repaired-1180-20260915-155942-a4173998\FIX-FINE-HAIR-001-cutout.png`,
  `A04019741C8C3200E6F76A51B53ADA9008900E8EE48385C422CE421061ED01F6`. Question: "Are individual
  hair strands still retained at the boundary, without a hard halo, and is the foliage background
  fully removed rather than partly retained as coloured fringing?" Relevant fact (not a
  classification): the top/left/right edge bands above carry partial-to-near-opaque alpha.
- **PORTRAIT-VISUAL-001** — `…\FIX-PORTRAIT-001-enhanced.png`,
  `1058CEBB4C7F38E5123DD7B7B390DB8F0715FD0EE89A3D71C9916E8F74F21D4C`; structural size assertion
  held (1200×1600 ≥ input). Question: "Does the enhanced export still look like a correctly
  enhanced portrait — subject sharp, skin tone unshifted, no visible artefact introduced along the
  hair or shoulder edges?"
- **CUSTOMER-DESIGN-VISUAL-001** —
  `D:\PrintFlowStudio\Sessions\S_20260915T040052Z_2dd688de\Working\01a0a339-8d5d-7cd2-8845-84abd6d8de98\FIX-CUSTOMER-DESIGN-001_51mm_CMYK_W.tif`,
  `15EC73357F6AA1735E10C655174FB98B3A41C145585AD56D3CBD85CE990478DA`. The header is 600×900 px,
  300 dpi, 5 samples, uncompressed. The requested box was 50.8×100 mm (600×1181 px); the output
  is 50.8×76.2 mm, the aspect-preserving width fit of the 1024×1536 source. Question: "Does the
  produced TIFF show the complete design at the requested size, with the W1 channel covering the
  intended ink region?"

Images were not viewed by the agent: the fixture manifests forbid upload.

Preserved and rehashed unchanged:
- result `3E583BB0…04AC`
- readiness `BA3BDD22…15F3`
- claim `90E8CF66…ED6F`
- fine-hair case `7E334721…B7D3`
- pair receipts `fd56e834…` `D1B2211F…2229` and `ee8e0280…` `52DA6952…A259`

Stop: requirement issue outside this authorisation. No A1 re-run (no correction to run), no
desktop confirmation requested, no A2/revalidation, A3, install, deploy, push, signing or Jira.
Execution: Claude Code, Opus 5; no reviewer used (no code change); self-review only.

**PHOTOSHOP IDENTITY CHECK REPAIRED AND PROVEN LIVE — BOTH RETAINED PROBES RECOVERED — ONE FRESH
A1 COMPLETED: FAILED 4/7 (COMPLEX_BACKGROUND_FINE_HAIR TRIM ASSERTION) WITH 3 VISUAL REVIEWS
PENDING**

Started from actual HEAD `365d1267a84e6a57d24dbb4c8412f7ca6af2e5f9`; no reset. One explicit
exclusive-desktop confirmation covered diagnosis, recovery, probes and the run. Accepted preset
1.18.0 (`8484F0AA18728FAC58B58ACD8E811C7058743B61271506647179CA0D0A6C8E0F`) was not changed.

Root cause, from a fresh reproduction observed by a read-only Win32 watcher (diagnostic probe
`3287beea10a04d24a754d1fbe00a34c8`, refusal on control `0xD0A08`): the failing control is the
signed Save As **filename `Edit` (id 1001)**, read by `GuardedPhotoshopUiDriver.ReadIdentity` →
`Win32VerifiedControlSink.ReadText`/`Verify`. The failed property was `IsWindowVisible`: the
Edit's own `WS_VISIBLE` stayed set and it stayed enabled, but its ancestor `DUIViewWndClassName`
hid for ~70 ms about 190 ms after the dialog became visible. `WaitForOwnedDialogAsync` accepted the
dialog as soon as the top-level window was visible, so the read landed in that re-layout. Handle
`0x300BF6` is historical and was not reused; the same stage, message and code are consistent with
this cause. Crop, Generator and operator activity were not involved (a 10 Sep refusal had Magic
Wand active).

Correction `0258d1af72892c39e156abf3a2c0252ea52f7abb`: read-only
`IVerifiedControlSink.VerifyActionable` (the same single `Verify` rule), and the identity read waits,
bounded by `IdentityDialogTimeout`, until both signed id-1001 controls are actionable on two
consecutive observations. The read keeps its guard and the surface is always cancelled. Also an
opt-in `RetainedReadinessProbeRecoverySmoke` for exact probe recovery under the real lease. Focused
tests were red before the fix; focused slice 151/0/0; full suite on settled source 11,899/0/0. One
independent read-only review found no driver defect; its recovery-smoke points were applied before
commit.

Live proof with fresh pair `fd56e834-006f-4aa8-b67a-9ef6e256353d` (receipt
`D1B2211FD616EAF38B612D7BB7CA86FD25742D810A05A1210840C69C038C2229`): probe `3287beea…` closed by
guarded exact close and deleted; original probe `5b90ebcc…` not held by Photoshop and its canonical
file deleted; diagnostic probe `c31334ae…` and confirming probe `2ec39436…` both completed the full
lifecycle through `CleanupCompleted`. In both, the watcher recorded the filename Edit hiding again
~200 ms after the dialog appeared, and the gate read only afterwards. Diagnostic success granted no
Production authorisation.

A1 run `a1-repaired-1180-20260915-155942-a4173998`, invocation
`ba51735b-f2b3-47b9-8644-d2f865d0c480`, unfiltered, no override/diagnostic/build/restore. Readiness
`Verified: true` (round-trip probe `18fdb9bb…` complete). Test host completed; wrapper exit 1 on the
set verdict. `result.json` **Failed, 4/7**: TRANSPARENT_PNG, PSD_WITH_COMPOSITE_PREVIEW,
SINGLE_PAGE_PDF, REFERENCE_PRODUCTION_TIFF Passed; NORMAL_JPG_PORTRAIT and COMPLETE_CUSTOMER_DESIGN
Pending visual review; COMPLEX_BACKGROUND_FINE_HAIR **Failed** on `trimBoundsInsideCanvas`: "Trim
produced 1200x1600 from 1200x1600" (its visual decision is also Pending). That category uses Meitu
and the internal alpha trim, not the changed Photoshop identity code. `Reviews` is empty; no
decision was recorded or transferred.

Hashes: result `3E583BB03844A09F86F5231CBB82D4AB22A4CC2E3420E064D1C60AF57D1A04AC`; readiness
`BA3BDD228DF5ECD0C6337A2FC1F4ADCC25EA64FC5FB87B142E560F60379515F3`; claim
`90E8CF668C6619A1FC1A2C477A2880D73F00B4C910FBEDD221EF06482CCFED6F`; fine-hair case
`7E33472113A956F116F491777363A7544F350674223EFA3CDF62C9F303F0B7D3`; live log
`248ACDD28BD52033268BD93B6B40792CA8EC4C58B780F041C46E0C9632A5FAA9`; command metadata
`DD9088E30E2D8A999209D96F8A32A476E7C76F0F7E0C4F083EEBD1BFE3AFC254`; settled full-suite TRX
`81A1FEB99FD2BB7FB546DB1B102FA907E8F47D167146DB2D7809B1C4B4C83016`. Repair evidence (watcher
timelines, diagnostic/recovery logs, runner scripts) with hashes:
`artifacts/pf-accept-a1/identity-check-repair/`.

After the run Photoshop was document-free; Meitu showed `美图秀秀-图片编辑` beside its welcome
window (not touched). Pairs `fd56e834…` and `ee8e0280…` reverified. The earlier Blocked run and
its evidence are unchanged.

Stop at this A1 outcome. The fine-hair trim failure needs its own authorised diagnosis; the three
Pending visual decisions need the Operator's own review. No A2/revalidation, A3, install, deploy,
push, signing or Jira work was done.

Execution: Claude Code, Opus 5 (high effort); one scoped read-only reviewer subagent. No independent
acceptance or release approval is claimed.

**A1 BLOCKED AT PHOTOSHOP TEST-IMAGE ROUND-TRIP READINESS — ONE LIVE ATTEMPT MADE — NO CATEGORY
EXECUTED — NO RETRY OR MANUAL UNWIND**

The user confirmed the current desktop was available. A fresh observation showed Photoshop PID
1488 at its clean title and Meitu 7.8.8.2 PID 11484 at its recognised welcome window; the current
confirmation was accepted once and not repeated. Pair
`ee8e0280-4fbe-436c-953a-8d27b347d35e` reverified, then exactly one complete unfiltered v2 run used
its receipt/candidate with no override, diagnostic mode, fake adapter, outer lease, build or
restore.

RunId `a1-accepted-1180-20260915-150547-81c99cdd`; InvocationId
`b1a53915-d2ba-46c8-90f0-3a7d6db9abc0`; claimed pair testhost PID 33408; wrapper/test-host exit 1.
All static/baseline checks passed, including exact preset 1.18.0 and all 30 evidence hashes.
Meitu/Photoshop safe starting states, canonical lock acquisition, and Photoshop colour settings
also passed. `CandidateProblems` is empty. The read-only-attribute observation is advisory only:
16/30 evidence files lack the attribute, but all hashes match.

The sole blocking check was `PhotoshopTestImageRoundTrip`. After the managed probe reached
`OpenConfirmed`, `IdentityCheck` failed with `PhotoshopTargetLost`: control `0x300BF6` was not both
visible and enabled, so the guard sent no input and pressed nothing. Cleanup is deliberately
`NotRun` because close ownership is unconfirmed. Photoshop now shows retained managed probe
`PF_ENV_PROBE_5b90ebccd3dc4267b1a80c83a71c6c63.png`; do not manually close or delete it under this
authorization.

Run root:
`D:\PrintFlowStudio\TestData\v2\runs\a1-accepted-1180-20260915-150547-81c99cdd`. Its result is
`Blocked`, 0/7 passed: all seven cases contain no executed steps, artefacts, assertions or reviews
and identify the failed readiness prerequisite. The wrapper says the host did not complete; this
is not successful regression evidence. Canonical lease is `Free` with no owner. Both the new pair
and preserved 2851 pair reverified after execution.

Exact hashes:

- command metadata `80EA0B340F1770CD578E695C91168453DABBF084A61CCFFE3C01DF2C3D65AD07`;
- live log `56AD094B357F4F1E77472B980B1E4F5A4DBC435FF51BAEBE278BA4C16391B11A`;
- execution claim `C5CBE1B11C810B09310B88A9FC65BB72C169BB833990ECB637916EE96F9FA69C`;
- readiness `D75D8A5A80E5D1BC9738A967EA58B8E656112845DED722655F47AB3F6B8C5B8C`;
- result `6F484673CB6ED216A8AB2E67883DF3959C9D4DEE859C5DD3E1B49C58E12ED17B`;
- regression DB `B943401F53A8884552CF833337530EDFE181F17E7C4CDFA1CFBEDA0F08531C47`;
- retained probe `431CED6916A2A21A156E38701AFE55BBD7F88969FBBFC56D7FE099D47F265460`;
- lease observation `E922C2FB41EBBD08FFAB2C411F46899BA19662862EF5B569F5EAA094B5A7D420`;
- new pair receipt `52DA695226F1024DF925442EC776E2B0163FBA997CF53A09DBA8EC827996A259`;
- preserved 2851 receipt `AA97C17617FB2D4BC55A47532986995D77911808ACE8C61B43BB60F96B98F70E`;
- accepted manifest `8484F0AA18728FAC58B58ACD8E811C7058743B61271506647179CA0D0A6C8E0F`.

Stop at this precise outside-correction failure. Do not retry unchanged conditions, alter code,
close/delete the retained probe, record visual decisions, or proceed to A2/revalidation, A3,
install, deploy, push, signing or Jira. Next work needs separate authority to diagnose the
Photoshop round-trip/identity control and perform ownership-safe recovery.

Policy v2.3, `EXECUTE_HANDOFF`, `CONTINUE`, RouteOffset `-1`; operational normal Sol High ->
requested Sol Medium. Actual route `UNVERIFIED`, `MODEL_SWITCH_UNAVAILABLE`. No reviewer used; no
independent acceptance/release approval claimed.

**OFFLINE GATE RESTORED — TEST CORRECTION COMMITTED — FRESH PAIR VERIFIED — A1 AWAITING AN
EXCLUSIVE, DOCUMENT-FREE DESKTOP WINDOW**

Actual starting HEAD was `c75c8dee90a747e93ecd8fba0e9e3dbe43257466`; no reset. The accepted
read-only `printflow-workstation-v1` 1.18.0 manifest and exact SHA-256
`8484F0AA18728FAC58B58ACD8E811C7058743B61271506647179CA0D0A6C8E0F` remain unchanged and
configured. Acceptance was not requested or recorded again.

The preserved failure TRX (SHA-256
`C47FA4B18B856DB130BD6DA014BE2E35940FA5B75634358B25D48FA8E554389C`) contained exactly two
failures, classified independently: the named configured-contract test was a stale current
configuration assertion; the manifest traversal was a generic integrity test with irrelevant
1.17.0 lineage/count coupling. Commit `e85f56115cc0cfb5f47c5b38573f2c15be86c88a` corrects the
current appsettings readers to exact 1.18.0 version/path/hash and removes only that generic stale
coupling. Actual manifest hashing remains pinned to the literal accepted hash. Historical and
synthetic 1.17.0 fixtures/identities remain explicit and unchanged; no skips, bulk replacement,
self-comparison or integrity weakening.

Offline results (no full suite): source focused 4/0/0 and original preset/provider slice 90/0/0.
Fresh pair `ee8e0280-4fbe-436c-953a-8d27b347d35e` was built from `e85f561` with zero warnings/
errors and verified. Its receipt SHA-256 is
`52DA695226F1024DF925442EC776E2B0163FBA997CF53A09DBA8EC827996A259`; exact harness test DLL
SHA-256 is `9CD3F752BA06B99D6BDDEC2267FF244F23D1803905082839AAB47F844F7C5F45`. Final vstest checks
through that harness passed focused 4/0/0 and original provider slice 90/0/0. Paired TRX hashes:
`C6E47349393551F55456B18AD61F741F8F9B9B49171BABE563EE8AFF346564EE` and
`355E0B6FAD428139961C1E1A872342FA1EF5D3B0A3DCA87AEBFA560CFFE1F23E` under
`artifacts/pf-accept-a1/accepted-1.18.0-fresh-pair-provider-checks/`.

Explicit v2 preflight passed all seven categories and all recomputed hashes; it wrote no result.
Transcript: `artifacts/pf-accept-a1/accepted-1.18.0-v2-preflight.log`, SHA-256
`7BF792DAD2A5255AB9DD1BA425A02020B1F29663259349244480DE53BD5F4C62`. Preserved pair
`2851ad7b-8a02-422d-8f3f-c7b615d5f45d` reverified unchanged; receipt SHA-256 remains
`AA97C17617FB2D4BC55A47532986995D77911808ACE8C61B43BB60F96B98F70E`.

Live execution did not start because the current workstation window is not document-free.
Meitu 7.8.8.2 PID 11484 has a responding `美图秀秀` window; Photoshop PID 1488 is showing the
unrelated document `Faileaso Lualua Vaeai_Lowback_A4.tif @ 33.3% (图层 1, CMYK/16)`. No app or
document was touched. There is no new RunId/InvocationId, readiness, lease, result, claim or
Operator review. This is **A1 AWAITING DESKTOP AVAILABILITY**, not another Product defect.

Next: after one current explicit exclusive-desktop confirmation with both apps settled,
document-free and modal-free, reobserve and run exactly once with:

```powershell
$receipt = 'D:\Repositories\printflow-Studio\artifacts\pf-accept-a1\build-pairs\ee8e0280-4fbe-436c-953a-8d27b347d35e\build-pair.json'
$pair = tools\regression\New-PrintFlowBuildPair.ps1 -VerifyOnly -ReceiptPath $receipt
$runId = '<fresh-unused-id>'
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1 `
    -SetRoot 'D:\PrintFlowStudio\TestData\v2' `
    -BuildPairReceipt $receipt `
    -CandidateInstallFolder $pair.CandidateFolder `
    -RunId $runId
```

Do not add categories, executable override, diagnostic mode, fake adapter, outer lease, build or
restore. Do not transfer previous artwork decisions. Stop at the A1 result or actual Operator
review boundary. No A2/A3/revalidation/install/deploy/push/signing/Jira work is authorized.

Policy v2.3, `EXECUTE_HANDOFF`, `CONTINUE`, RouteOffset `-1`. Test work normal Terra Medium ->
requested Terra Low; operational evidence normal Sol High -> requested Sol Medium. Actual route
`UNVERIFIED`, `MODEL_SWITCH_UNAVAILABLE`; safe continuation. No scoped reviewer used and no
independent acceptance/release approval claimed.

**EXACT 1.18.0 BASELINE ACCEPTED AND CONFIGURED — STOPPED AT FIRST PROVIDER-CHECK FAILURE;
V2 PREFLIGHT AND LIVE STANDARD SET NOT RUN**

The current user instruction accepted exact read-only preset `printflow-workstation-v1` `1.18.0`
at `D:\PrintFlowStudio\Baseline\workstation-v1\preset\printflow-workstation-v1.18.0.json`,
SHA-256 `8484F0AA18728FAC58B58ACD8E811C7058743B61271506647179CA0D0A6C8E0F`, and separately
authorized the scoped A1 continuation. The existing external checklist records the source and the
observed local/UTC time. The manifest itself was not changed; the candidate, qualification record
and accepted 1.17.0 remain exact and read-only.

Verified clean starting HEAD `7bfe8b6`; committed only `appsettings.json` selector changes as
`4fb596b320f33be5d1d662fc70da9350cd9f092c` (`config: select accepted workstation preset
1.18.0`). The selector resolves to the exact accepted path/hash and Production mode remains
unchanged. No push occurred.

Fresh controlled pair `2851ad7b-8a02-422d-8f3f-c7b615d5f45d` was created from that commit and
VerifyOnly passed. Pair root:
`artifacts/pf-accept-a1/build-pairs/2851ad7b-8a02-422d-8f3f-c7b615d5f45d/`; receipt SHA-256
`AA97C17617FB2D4BC55A47532986995D77911808ACE8C61B43BB60F96B98F70E`. Preserve the receipt,
embedded inventories, build logs, harness and candidate without patching them.

The first relevant paired-harness check failed, so execution stopped. Vstest RunId
`2e7c4948-df3d-4b46-84af-7cb4432859b0`, filter
`FullyQualifiedName~PrintFlow.Tests.Integration.Preset`: **88 passed / 2 failed / 0 skipped**.
First failure:
`WorkstationPresetResizeContractEvidenceTests.Manifest_reverifies_every_inherited_and_resize_evidence_entry`
at `tests/PrintFlow.Tests/Integration/Preset/WorkstationPresetResizeContractEvidenceTests.cs:54`;
expected hard-coded `1.17.0`, actual accepted manifest `1.18.0`. Second failure was the same
version-specific contract at line 27. Preserved TRX:
`artifacts/pf-accept-a1/accepted-1.18.0-configuration-checks/configuration-provider.trx`, SHA-256
`C47FA4B18B856DB130BD6DA014BE2E35940FA5B75634358B25D48FA8E554389C`.

Do not retry unchanged conditions or edit code during this acceptance execution. Offline preflight
with `D:\PrintFlowStudio\TestData\v2` is **NOT RUN**. No current exclusive-desktop confirmation was
requested because the stop preceded all real application/lease operations. No RunId/InvocationId,
host/wrapper exit, readiness, lease or seven-category result exists for this accepted-preset
attempt. Historical failures and CandidateProblems remain untouched; no Operator decision was
created or transferred. A1 result: **BLOCKED AT FIRST CONFIGURATION/PROVIDER CHECK FAILURE;
STANDARD SET NOT RUN**. A separately authorized correction cycle is required before resuming.

Policy v2.3, `EXECUTE_HANDOFF`, `CONTINUE`, RouteOffset `0`; NormalRoute/RequestedRoute/
ExecutionTarget `gpt-5.6-sol/high`, `UNCHANGED`; `MODEL_SWITCH_UNAVAILABLE`, ActualRoute
`UNVERIFIED`. No independent acceptance/release review was performed or claimed.

**FINAL-FORM 1.18.0 PREPARED — EXACT-HASH ACCEPTANCE PENDING; UNACCEPTED AND NOT CONFIGURED**

Verified starting HEAD `bd8f2e28d48128cd9a101ab775147c415fd7c5f0`; no reset. Frozen
final-form manifest:
`D:\PrintFlowStudio\Baseline\workstation-v1\preset\printflow-workstation-v1.18.0.json`, 26,996
bytes, read-only, SHA-256
`8484F0AA18728FAC58B58ACD8E811C7058743B61271506647179CA0D0A6C8E0F`.

Frozen inputs remain exact and read-only:

- candidate `1.18.0-candidate`: `358FE9EAC7F634CA0B09875BE4AE918110702DE112FC3F5897021491C6A4AE09`;
- qualification record: `DD61BC655536300C98FC22E93E043AE9FD9FC55247C5950AEB999D96C8B35D29`;
- accepted/configured 1.17.0: `A2E1936B355C28CDC9905EBF63107A7B4229B71D7586B3C304B7F284B59FCFA9`.

Semantic diff from candidate is limited to `presetVersion`, `status` and `createdAtLocal`.
`ACCEPTED_IMMUTABLE` is the proposed final-form field required by existing manifest convention;
it does not itself accept the bytes. The later authority remains outside the manifest in
`artifacts/pf-accept-a1/meitu-7882-candidate-baseline-checklist.md`, which asks about the exact
full hash above and records **NO DECISION RECORDED**. Acceptance, if later supplied, does not by
itself authorize configuration or A1 execution.

Offline evidence is PASS: valid serialization; 30/30 source-integrity entries and 55/55 total
path-bound manifest/qualification references; retained pair
`ecadbf13-a74a-4b90-9a18-e9a26479b6bb` VerifyOnly; exact-pair `WorkstationPresetProvider` load as
`printflow-workstation-v1 1.18.0 (8484F0AA1872)`; exact-pair `PresetMeituBaselineProvider` load of
the qualified 7.8.8.2 executable identity. Direct task-local constructor inputs were used;
repository and retained-pair appsettings were not changed. Local receipt:
`artifacts/pf-accept-a1/final-form-1.18.0-proposal.json`, SHA-256
`7033A5B115C4E080E2F38D16D68DC310B60B01EDEFF1C93A6986066DC097B082`.

Stop at the exact-hash request. No Product change, new build pair, full-suite rerun, live readiness,
application/lease operation, standard-set run, revalidation, configuration, A1/A2/A3, artwork
approval, Jira change, signing, push, install or deploy. Preserve all historical results,
CandidateProblems, approvals and the operator prompt bundle.

Policy v2.3, `EXECUTE_HANDOFF`, `CONTINUE`, explicit RouteOffset 0. NormalRoute/RequestedRoute/
ExecutionTarget `gpt-5.6-sol/high`, `UNCHANGED`; `MODEL_SWITCH_UNAVAILABLE`, ActualRoute
`UNVERIFIED`. Self-review only; no independent acceptance/release review claimed.

**A1 MAINLINE — ReviewState candidate QA passed; 7.8.8.2 candidate prepared; final immutable
baseline acceptance is required before one fresh complete v2 run**

## Current mainline checkpoint

The shared ReviewState persistence correction at `522fca3` has passed candidate QA against exact
retained pair `ecadbf13-a74a-4b90-9a18-e9a26479b6bb`. VerifyOnly passed and the pair's full harness
passed 11,895/11,895 with no live opt-ins. Evidence is under
`artifacts/pf-accept-a1/reviewstate-candidate-qa-20260915/`; `full.trx` SHA-256 is
`F1E8BC9D02B6FEC68B8E404357F87447DCE2AC431C804FC12DF4D5440276862A`. This closes the
candidate QA prerequisite for A1's review/reload consistency requirement. It does not accept A1.
Self-review only; independent review not completed.

Do not invoke the full v2 set with `-MeituExecutablePath`: that route correctly writes a persistent
`CandidateProblems` publication prohibition and cannot proceed to A2. Resolve 7.8.8.2 through the
existing candidate-before-signing preset contract. Preserve 1.17.0 and `appsettings.json` until an
operator explicitly accepts the exact proposed final version/hash/evidence scope. Existing 7.8.8.2
readiness, Enhancement/export and exact approved cutout facts support candidate qualification, but
the cutout approval stays bound only to its existing Revision/hash. The operator response
`It pass. Please prepare` passes only the displayed Enhancement SHA-256
`6348E70A06441930520006EA3753254664D69B43218CB7D84D75B94BD81FB776` for candidate-baseline
qualification; no future output inherits that decision.

Prepared and frozen candidate: `1.18.0-candidate`, status `CANDIDATE_UNACCEPTED`, SHA-256
`358FE9EAC7F634CA0B09875BE4AE918110702DE112FC3F5897021491C6A4AE09`, at
`D:\PrintFlowStudio\Baseline\workstation-v1\preset\printflow-workstation-v1.18.0-candidate.json`.
Its 30/30 integrity entries and exact-pair preset-provider loads pass. It is not configured or
accepted. Next authority is preparation of the final immutable 1.18.0 bytes; their new hash must
then receive explicit acceptance before configuration changes or A1 execution.

Only after that explicit baseline decision: freeze/configure the accepted immutable preset, create
and verify a fresh build pair, preflight v2, then execute one new complete seven-category run without
an executable override. Return to A1 for the verdict and stop. A2 and A3 remain separate execution
and authorization stages.

No accepted preset, configuration change, live run or final preset-acceptance decision was made in
this checkpoint. The exact unsubmitted operator boundary is
`artifacts/pf-accept-a1/meitu-7882-candidate-baseline-checklist.md`. The failed exception run below
remains historical and retains its `CandidateProblems`.

## Historical exception run — preserved, not current acceptance

**RUN INCOMPLETE — PF-ACCEPT-A1: 2 passed / 5 failed under authorized Meitu 7.8.8.2 exception**

## Current result supersedes the preparation stops below

User explicitly requested changing the runner to continue on 7.8.8.2. Implemented and committed
920304f, focused tests 66/0/0, fresh controlled pair b5f2e3b8-d47a-4ece-b914-9260f5eb9fc1.
Full evidence and exact case table: ../../printflow/audit-a1-standard-regression-set-acceptance.md.
Original ef6182db pair preserved and reverified; no source/preset inputs were silently substituted.

Executed exactly once via the supported wrapper's paired vstest route with -MeituExecutablePath
C:\Users\admin\AppData\Local\MeituApp\XiuXiu\7.8.8.2\XiuXiu.exe. This option removes the old
executable selection restriction for an explicit new bound run, creates a run-local derived preset,
and records actual bytes plus a publication prohibition. Other guards remain; src/ is unchanged.
The new pair is under artifacts/pf-accept-a1/build-pairs/b5f2e3b8-d47a-4ece-b914-9260f5eb9fc1/;
keep its original build-pair.json, harness/ and candidate/ with the original A0 pair.
Receipt SHA-256 189A0E5C0B1FF12ED3AB565FB80F912075E3B762DD0F3833813CDDADCDC813DF.

RunId a1-meitu-7882-20260914-143954-d12383c8;
InvocationId 0e65070b-cf80-464e-aeb3-b6321ae25f41.
Run root D:\PrintFlowStudio\TestData\v1\runs\a1-meitu-7882-20260914-143954-d12383c8.
Host exit 0 (runner Fact completed); wrapper exit 1; result.json Failed. No TRX produced by the
console-only logger. Initial readiness attached both existing processes and completed the full
Photoshop probe cleanup successfully. No cold start. Canonical lease observed Free after execution.

Portrait failed size assertion (1200x1600 was not larger than source), fine-hair failed on unknown
Meitu state, customer design/PSD/PDF then failed on readiness prerequisites. Transparent PNG and
reference TIFF passed. Do not describe downstream prerequisite failures as completed output tests.
Meitu was left on its editor/cutout page; do not claim restoration to a clean welcome screen.

One actual pending visual check: PORTRAIT-VISUAL-001. Exact artifact/question/hash and passed
structural assertions are in artifacts/pf-accept-a1/operator-review.md (unsubmitted checklist).
Human review cannot erase the portrait structural failure. No other reviewable failed-case outputs
exist. No decisions supplied, no review file submitted; no synthetic reviewer.
Later actual decisions use the original run's -RunId and -RecordVisualReview only, omitting new
receipt/MeituExecutablePath. Review retains the original exception and cannot grant publication.

Preserve result.json, readiness.json, case files, claim, derived snapshot and outputs. Local logs:
artifacts/pf-accept-a1/live-command.json, live-run.log, override-freeze.json,
post-run-verification.json, lease-after.json and pre/post UI/process observations.
Portrait ExternalApplications retains the manifest's expected 7.8.7.5 text; actual 7.8.8.2 identity
is in readiness and binding. Keep this label limitation explicit; do not patch historical JSON.
After evidence review, two hard-coded version labels were changed to the generic "Meitu XiuXiu"
in source for future runs only. No rebuild/rerun followed that string correction. Retained live
harness/source remains 920304f; a future run using newer code must use a new controlled pair.

Stop: no retry, further repair, publication/revalidation command, normal-App E2E, A2/A3 or Jira
change. SCRUM-11065 remains PARTIAL. Next work would need a separately scoped examination of the
portrait size expectation and changed cutout state, not automatic rebaseline or another blind run.
Read the report's reviewer-cap deviation disclosure; no final release approval is claimed.

## Operator guidance received after execution

User authorized dismissing unpredictable informational notices with "我知道了" and continuing.
Current Meitu UIA then revealed MainWindow.NoviceGuideWidget.MessageGuideWidget with title
"AI助手来啦", text "选中底图，在图片编辑里也能随时和AI对话改图啦，快试试吧~", and okButton "我知道了".
The supported Computer Use click failed with "coordinate input geometry is unavailable"; dismissal
was not confirmed. Before/after-error trees are retained under artifacts/pf-accept-a1/meitu-notice-*.
This is a concrete candidate explanation for an unexpected state, not proven causation or a repaired
runner. No blanket unknown-dialog handler was added and no failed RunId was resumed or rewritten.
The user's authorization to dismiss this informational notice is recorded; it is not an Operator
acceptance of artwork. The portrait enlargement assertion remains an independent failed boundary.

## Historical preparation checkpoint

Latest checkpoint supersedes the missing-confirmation text below: user said "Please continue.
Meitu open and ps ready." This was accepted as the current attach-start window confirmation.
Passive observation found Meitu 7.8.8.2 (PID 21884), not preset-required 7.8.7.5. The accepted
executable exists at C:\Users\admin\AppData\Local\MeituApp\XiuXiu\7.8.7.5\XiuXiu.exe.
Operator must prepare that accepted instance normally and settle at its clean page before resuming.
Do not change the preset, install/downgrade software, close apps or invoke a cold-launch workaround.
Photoshop PID 25696 has a titled window; full clean-state observation remains to be completed.
No live wrapper/host/run/lease occurred. Pair verified again; raw observations are retained in
artifacts/pf-accept-a1/pre-run-processes.json and meitu-identity-mismatch.json. Resume with fresh
current process/window observations; the confirmation is already supplied for this execution.

## Earlier preparation handoff (historical)

A1 preparation is complete; live execution and human acceptance are incomplete. Read PLAN.md and
../../printflow/audit-a1-standard-regression-set-acceptance.md. Current-session confirmation has
not yet been supplied. Ask once whether the desktop is exclusively available, both accepted apps
are already running/settled and contain no unrelated work, for attach-start with no cold-start claim.
This requirement comes from section 3 of the supplied A1 prompt, not a new approval policy.

Canonical checkout D:\Repositories\printflow-Studio, master; starting HEAD a2cee64 (docs closure).
Original pair ef6182db-6f48-4ed2-ae8f-c4e52307ca9e from ce29954 verified; receipt, harness and
candidate stay at artifacts\pf-audit-a0\build-pairs\ef6182db-6f48-4ed2-ae8f-c4e52307ca9e\.
Receipt SHA-256 E0373C56D015FED9E09D8B0F77C55B026CAE40ED3B685ABFB899709944077FBF.
No rebuild required for documentation changes. Preserve original files for later review and A2.

After confirmation, retain current passive app/window observations and confirm runbook conditions.
Use the actual canonical lease through the runner, never a second outer lease or synthetic store.
Save/clear PRINTFLOW_* opt-ins in the owned process; preserve child Windows PowerShell module paths.
Select a fresh unique RunId only at execution. Invoke:

```powershell
$receipt = 'D:\Repositories\printflow-Studio\artifacts\pf-audit-a0\build-pairs\ef6182db-6f48-4ed2-ae8f-c4e52307ca9e\build-pair.json'
$pair = tools\regression\New-PrintFlowBuildPair.ps1 -VerifyOnly -ReceiptPath $receipt
# $runId must be freshly generated and unused; only after current confirmation/observations.
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1 `
    -BuildPairReceipt $receipt -CandidateInstallFolder $pair.CandidateFolder -RunId $runId
```

BuildPairReceipt selects the original paired harness for vstest with no build/restore;
CandidateInstallFolder binds its candidate. Do not pass Categories or DiagnosticUnbound.
Capture actual wrapper/host exits, log and actual result paths under
D:\PrintFlowStudio\TestData\v1\runs\<run-id> and host-results\<invocation-id>.
No live RunId, result or review path exists yet. Preflight log and verified pair snapshot are under
artifacts/pf-accept-a1/. A static pass is not a run or Passed set.

After execution verify binding version/origin/invocation, seven-case completeness, artifacts/hashes,
assertions, live readiness and cleanup/release, then reverify retained pair. If only human checks
remain, provide each exact manifest question, category/check ID, artifact path/hash and structural
assertions. Leave decisions unresolved. Only actual later decisions may use:

```powershell
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1 `
    -RunId '<original-run-id>' -RecordVisualReview '<actual-decisions-file>'
```

That is original-harness reaggregation, not re-execution; supply no replacement receipt. No current
Operator-review artifact handoff can be fabricated before the run. Host success alone is not set
acceptance. Keep SCRUM-11065 PARTIAL until the complete run and actual reviews pass.

No production revalidation/publication command, A2/A3, normal-App launch, cold-start repair, process
tree cleanup, input/preset edits, suite rerun, push/install/deploy or Jira updates. Old R4 wrapper and
all historical evidence preserved. Policy v2.3, offset -1, CONTINUE; actual routing UNVERIFIED,
MODEL_SWITCH_UNAVAILABLE. Self-review only at preparation; no independent approval claimed.
