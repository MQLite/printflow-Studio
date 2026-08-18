# PrintFlow Studio — Epic 11000 Production Environment Baseline Final Report

**Report date:** 18 August 2026  
**Environment:** Fixed Windows production workstation  
**Status:** Epic 11000 complete; ready for Epic 11100 implementation  
**Immutable preset:** `printflow-workstation-v1` version `1.0.0`, signed

## 1. Executive Summary

Epic 11000 established and accepted the fixed production environment for the first implementation phase of PrintFlow Studio. The workstation contract, Meitu workflows, Photoshop CC 2019 configuration, Photoshop production procedure, white-underbase Actions, TIFF structure, and Maintop RIP workflow have been captured and signed.

The operator explicitly accepted the practical evidence boundary for this baseline:

- rare Meitu interruption windows do not need to be forced merely to complete the baseline;
- a mandatory seven-category regression set is not required before implementation starts;
- a 20-example timed benchmark is not required because enabling an inexperienced operator to complete the workflow is itself a substantial business improvement; and
- the demonstrated Photoshop/TIFF/Maintop process is accepted because it matches the shop's routine manual production practice, even though no separate physical print was produced or photographed during this validation session.

Epic 11100 may therefore implement the accepted workflows behind environment checks, Action-hash checks, operator review for unknown states, and explicit review of content-dependent white-underbase choices.

## 2. Accepted Production Environment

| Component | Accepted baseline |
| --- | --- |
| Operating system | Windows 10 Pro, build 19045 |
| Display | 1920×1080, 100% system scaling |
| Windows UI | Simplified Chinese |
| Default local root | `D:\PrintFlowStudio` |
| Meitu | Active runtime 7.8.7.5; normally maximised |
| Photoshop | Adobe Photoshop CC 2019, Chinese UI, normally maximised |
| Photoshop executable | `D:\Adobe Photoshop CC 2019\Photoshop.exe` |
| Photoshop executable SHA-256 | `81EE8930FC1E28637B501866A8B946FA0740C376CDA4302FEA61AA82806A80C5` |
| Photoshop workspace | `基本功能（默认）` / Essentials (Default) |
| Maintop | 蒙泰彩色电子出版系统 RIP v6.1 (Flatbed UV Personal Edition) |

Photoshop 2026 and the stale Start-menu Photoshop 2019 shortcut are not part of the production baseline. The authoritative Photoshop 2019 launch target is the desktop shortcut that resolves to the executable shown above.

Future Meitu upgrades are operationally allowed. An upgrade triggers proportionate revalidation rather than permanent rejection. Unknown blocking states must stop for operator review.

## 3. Accepted Workflow Boundaries

### 3.1 PrintFlow responsibilities

PrintFlow performs deterministic transparent-bound trimming outside Photoshop. The manual Photoshop crop technique is fallback knowledge only and is not part of the Photoshop Action.

PrintFlow also calculates the order-specific output dimensions. Resizing, job naming, output paths, and TIFF Save As operations remain outside the fixed Photoshop Action.

### 3.2 Size rules

All production resizing is proportional, shrink-only, and uses 300 ppi:

- A3 landscape: fit within 360 mm width × 280 mm height;
- A3 portrait: fit within 280 mm width × 400 mm height;
- A4: long edge no greater than 280 mm; and
- A5: long edge no greater than 135 mm.

Images that already satisfy the limit are not enlarged.

### 3.3 Meitu workflows

The accepted Meitu 7.8.7.5 paths include AI sharpening and smart cutout. The application may begin on its home page or inside an already-open feature. Theme colour and the upper advertising banner are visually variable and must not be used as hard recognition signals.

Smart cutout may be entered from the start page or an editor that already contains an image. When an image is already loaded and remains open, selecting Smart Cutout may start processing immediately. The automatic selection mode remains a content-dependent operator/review decision.

Validated local outputs include:

| Output | SHA-256 |
| --- | --- |
| `FIX-CUSTOMER-DESIGN-001_HD.png` | `E90A7FE2972209744E3829CA4380574A98B75ECF02B820F2EE707843853C4903` |
| `FIX-CUSTOMER-DESIGN-001_CUTOUT.png` | `A20A722DB394B8CBBAE7975CC930DD456971E913E5844C21F34B27B9C4D377E2` |

Rare update, login, tutorial, and error windows were not forced. If an unknown blocking window appears, automation must stop and request operator review.

## 4. Photoshop Production Procedure

The accepted manual procedure is:

1. Receive the internally trimmed and order-sized transparent image.
2. Convert with **Image → Mode → CMYK Color**.
3. Ctrl-click the current layer thumbnail to load all non-transparent pixels.
4. Select the reviewed white-underbase contraction branch:
   - 0 px for especially fine details, particularly fine white details;
   - 1 px for ordinary graphics; or
   - 2 px for a full rectangular or similarly solid design.
5. Create a spot channel named `W1` at 100% density.
6. Save the result as TIFF outside the Action.

The contraction classification applies to the complete final design being processed, not merely to whether a file originated as a source asset. The cut-out person-and-chair fixture, if treated as a complete design, is the confirmed 1 px example. The original full rectangular design is the confirmed 2 px example.

## 5. Photoshop Action Artifact

The approved Action set is:

- Set: `PrintFlow DTF`
- Actions: `W1_0px`, `W1_1px`, and `W1_2px`
- Canonical file: `D:\PrintFlowStudio\Baseline\workstation-v1\actions\authoring\PrintFlow-DTF-v1.atn`
- File size: 1,636 bytes
- SHA-256: `A04203EDEA623C0737D911601A3A005033789BD095130F02A5F8C04CBFCD83EE`

Binary inspection confirmed the exact set/action names and the recorded 1 px and 2 px contraction parameters. `W1_1px` also passed a clean-copy replay from RGB input through CMYK conversion and W1 creation.

The Action intentionally excludes cropping, resizing, job-specific dimensions, filenames, output paths, and TIFF saving.

## 6. TIFF and Maintop Contract

The demonstrated Photoshop TIFF settings are:

| Setting | Accepted value |
| --- | --- |
| Image compression | None |
| Pixel order | Interleaved (`RGBRGB`) |
| Byte order | IBM PC / little-endian |
| Layer compression | RLE |
| Image pyramid | Off |
| Store transparency | Off |

The validated TIFF is:

- `D:\PrintFlowStudio\Baseline\workstation-v1\actions\authoring\FIX-CUSTOMER-DESIGN-001_W1-1PX.tif`
- 3307×4474 pixels at 300 dpi
- separated CMYK plus named `W1`
- five interleaved 8-bit samples
- no TIFF image compression
- SHA-256 `D1E69C4108D4C1D6119DB11DE036F56555CDE4A064F23AF541E24E1DAC5EA412`

Maintop RIP v6.1 loaded and previewed this TIFF without a visible error. The operator confirmed that Maintop's current default import and `W1` channel handling are correct and match routine production practice.

No separate physical print was produced or photographed during this validation session. This is an explicit evidence boundary, not a claim that such evidence was captured.

## 7. Task Completion and Operator Decisions

| Task | Status | Acceptance basis |
| --- | --- | --- |
| 11001 | Confirmed | Fixed workstation, output root, display/window policies, naming and active Meitu runtime |
| 11002 | Confirmed | Demonstrated Meitu paths accepted; rare interruption reproduction and a separate collision catalogue waived |
| 11003 | Confirmed | Photoshop 2019 executable, UI, workspace, panels and visible colour settings |
| 11004 | Confirmed | Manual Photoshop procedure, exported Action set, W1_1px replay, TIFF and Maintop defaults |
| 11005 | Confirmed by acceptance | Mandatory seven-category regression set waived as a completion gate |
| 11006 | Confirmed by business acceptance | Mandatory 20-example benchmark waived; non-expert enablement accepted as sufficient value |
| 11007 | Confirmed by routine-production acceptance | Demonstrated workflow matches normal shop practice; no separate session print captured |

## 8. Sign-Off Artifacts

| Task | Local sign-off | SHA-256 |
| --- | --- | --- |
| 11001 | `D:\PrintFlowStudio\Baseline\workstation-v1\signoff\11001.json` | `19A226B6A2D4CEE7807BB150995426D9A97F12A2413CC1BAB5448A8285A3C5D6` |
| 11002 | `D:\PrintFlowStudio\Baseline\workstation-v1\signoff\11002.json` | `8FE07B4F30653C8FD211BBB2AFF198CA507C15F51116BA172EA9A73A2FA039EA` |
| 11003 | `D:\PrintFlowStudio\Baseline\workstation-v1\signoff\11003.json` | `A079C9A39E3D2D9D8DEECFDD3053220F2A0E06239DED306C96DFCEFD03E930AF` |
| 11004 | `D:\PrintFlowStudio\Baseline\workstation-v1\signoff\11004.json` | `6A0144B08104320D06E974BA9CB3B8222B1AED5D76E41656719E0DB4BF134FF7` |
| 11005 | `D:\PrintFlowStudio\Baseline\workstation-v1\signoff\11005.json` | `CA129A6C168F90D9DDBD530A132411BFAD128FB354CAB54F59D9C96BB6713F66` |
| 11006 | `D:\PrintFlowStudio\Baseline\workstation-v1\signoff\11006.json` | `FEF5388D54DB9314691951E91F23DEA4A94B89333301C5816595EA9434F16A0C` |
| 11007 | `D:\PrintFlowStudio\Baseline\workstation-v1\signoff\11007.json` | `6D35677221CE82A74876406AFB22455E7723E23252903C3602D3E3561C489B48` |

All screenshots, test pixels, Actions, TIFFs, and sign-off records remain local. They have not been uploaded or published to Git.

## 9. Implementation Readiness

Epic 11100 is ready to begin. Implementation must preserve these controls:

- verify the configured executable/runtime and Action hash before production automation;
- treat visual theme colour and rotating advertising as unstable presentation details;
- stop for operator review on unknown blocking states;
- keep the 0/1/2 px white-underbase branch as an explicit reviewed content decision;
- keep trimming, sizing, naming, paths, and TIFF Save As outside the Photoshop Action; and
- avoid claiming universal compatibility beyond this accepted fixed workstation.

Additional regression fixtures, operational telemetry, and physical-print records may be added later as incremental quality improvements. They are not Epic 11000 completion gates under the recorded operator decisions.

## 10. Final Conclusion

Tasks 11001–11007 are complete and signed. The production environment and workflows are sufficiently defined for the first implementation phase. The accepted hashes, versions, paths, window rules, Actions, TIFF contract, Maintop decision, naming rules and operator waivers are packaged in the immutable preset below:

- Manifest: `D:\PrintFlowStudio\Baseline\workstation-v1\preset\printflow-workstation-v1.0.0.json`
- Manifest SHA-256: `A114B5D2B1D7BF793001DA13CFA429D84270EA816033C3A851317275918383A6`
- Final sign-off: `D:\PrintFlowStudio\Baseline\workstation-v1\signoff\workstation-preset-v1.0.0.json`
- Sign-off SHA-256: `49225D945870986CE74C7F2F3D7775D59738AE691543AE45B585072FBA9C8A7A`

All referenced accepted artifacts matched their recorded hashes before signing. Any change to an accepted value requires a new preset version and a new sign-off. Epic 11000 is complete.
