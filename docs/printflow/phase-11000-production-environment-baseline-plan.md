# PrintFlow Studio Phase 11000 Production Environment Baseline Plan

| Field | Value |
| --- | --- |
| Baseline | Epic 11000 — Establish PrintFlow Studio Fixed Production Environment Baseline |
| Design authority | `PRINTFLOW_STUDIO_MVP_DESIGN_EN.md`, version 1.0, Confirmed, 2026-08-17 |
| Inspection date | 2026-08-17 (Pacific/Auckland) |
| Inspection mode | Initial read-only discovery followed by operator-approved local test-fixture processing, Photoshop Action authoring, TIFF export, and Maintop import validation; no customer artifact was published or added to Git |
| Status vocabulary | CONFIRMED, PARTIALLY CONFIRMED, NEEDS OPERATOR INPUT, BLOCKED, NOT APPLICABLE |

## 1. Executive summary

The repository is greenfield. It contains only the confirmed English and Chinese MVP design documents, has no commits, and has no .NET, WPF, test, prototype, or automation files.

The fixed workstation can be identified sufficiently to begin the non-automation WPF/workflow foundation: Windows 10 Pro build 19045, one detected 1920×1080 display at 100% system DPI, Simplified Chinese Windows UI, local user `admin`, and local C: and D: NTFS volumes. The operator approved `D:\PrintFlowStudio` as the PrintFlow default output root on 2026-08-17; the required external-application window policies have not been confirmed.

Meitu is installed and launchable. Runtime selection changed from the initially running 7.8.7.1 process to 7.8.7.5 after a normal operator close/reopen on 2026-08-17. The operator accepted 7.8.7.5 as the current production baseline. The uninstall registry still reports 7.5.8.0 and `Config.ini` reports 7.8.7.5, so the active executable remains authoritative. Future upgrades are allowed operationally but require proportionate environment and standard-set revalidation; unknown UI states still fail closed.

The operator corrected the production Photoshop selection during discovery. The authoritative desktop shortcut targets Photoshop CC 2019 at `D:\Adobe Photoshop CC 2019\Photoshop.exe`, file version `20.0 (20200706.r.120 2020/07/06: 1208496)`. Photoshop 2026 and a stale Start-menu Photoshop 2019 entry are not the production baseline. Trimming belongs to PrintFlow's internal deterministic module. The manual Photoshop procedure is now captured and signed under Task 11004, and the newly authored `PrintFlow DTF` Action set contains content-reviewed 0/1/2 px white-underbase variants. `W1_1px` has passed a clean-copy replay; 0 px and 2 px remain conditional on suitable content-specific fixtures.

A likely production directory contains 275 TIFF files. One recent file was inspected under the privacy-safe identifier `LOCAL-CANDIDATE-TIFF-001`. Its structure is consistent with a Photoshop-produced CMYK-plus-extra-channel TIFF, but its actual Maintop use and white-ink semantics cannot be proven from the file or directory alone. It remains a candidate, not the validated reference.

The standard regression set and 20-example manual benchmark do not yet exist as approved baseline evidence. Customer files were not copied, renamed, opened for visual review, or added to the repository.

**Decision:** Epic 11000 is complete and Epic 11100 may proceed. Tasks 11002, 11005, 11006, and 11007 are complete by explicit operator acceptance: rare Meitu interruptions are not forced, the mandatory seven-category set and 20-example benchmark are waived as completion gates, and routine manual production experience plus the demonstrated Maintop workflow is accepted for production reference. The immutable workstation preset `printflow-workstation-v1` version `1.0.0` is packaged and signed.

Final preset manifest: `D:\PrintFlowStudio\Baseline\workstation-v1\preset\printflow-workstation-v1.0.0.json`, 16,059 bytes, SHA-256 `A114B5D2B1D7BF793001DA13CFA429D84270EA816033C3A851317275918383A6`. Final preset sign-off: `D:\PrintFlowStudio\Baseline\workstation-v1\signoff\workstation-preset-v1.0.0.json`, 2,143 bytes, SHA-256 `49225D945870986CE74C7F2F3D7775D59738AE691543AE45B585072FBA9C8A7A`. All referenced accepted hashes were verified before signing. Any accepted-value change requires a new preset version and new sign-off.

## 2. Current repository state

| Item | Status | Evidence / finding |
| --- | --- | --- |
| Project maturity | CONFIRMED | Greenfield repository; no implementation structure exists. |
| Git root | CONFIRMED | `C:\Users\admin\Documents\ChatGPT\Printflow Studio` |
| Branch | CONFIRMED | `master` |
| History | CONFIRMED | No commits yet. |
| Tracked files | CONFIRMED | None. |
| Untracked project files at inspection start | CONFIRMED | `PRINTFLOW_STUDIO_MVP_DESIGN.md`; `PRINTFLOW_STUDIO_MVP_DESIGN_EN.md` |
| Existing documentation | CONFIRMED | Bilingual MVP design documents only. English document declares version 1.0 and Confirmed status. |
| .NET / WPF configuration | CONFIRMED | None: no solution, project, source, package, build, or test files. |
| Prior prototypes | CONFIRMED | None in the repository. |
| Prior automation files | CONFIRMED | None in the repository. |
| Conflicting scope | CONFIRMED | No project files implement Job/Order/Customer management, TeeNova, batching, gang-sheet/nesting, Maintop automation, RIP/printer control, cloud upload, auto-update, or a general workflow engine. |

Documentation convention did not exist. This report therefore uses the requested `docs/printflow/` location, which keeps planning evidence separate from the two root design authorities.

## 3. Task 11001 — Fixed Windows workstation contract

### Detected contract

| Contract item | Status | Detected value | Validation note |
| --- | --- | --- | --- |
| Windows edition | CONFIRMED | Windows 10 Pro, 64-bit | `Get-ComputerInfo` |
| Windows version/build | CONFIRMED | 10.0.19045; build 19045; registry-style version label 2009 | Store edition, semantic OS version, and build; do not store only “Windows 10.” |
| Workstation identity | CONFIRMED | `DESKTOP-0BG8884`, WORKGROUP, model reported as `System Product Name` | Hostname is usable; generic hardware model is not a reliable fingerprint. |
| Current Windows identity | CONFIRMED | `DESKTOP-0BG8884\admin`; user profile `C:\Users\admin` | Matches design fallback rule: application should capture current Windows username and use `Operator` only if unavailable. |
| Windows UI/culture | CONFIRMED | `zh-CN` UI culture, user culture, and system locale | `Get-WinUserLanguageList` itself failed in this session; the three independent culture values are sufficient for current UI classification. |
| Time zone | CONFIRMED | New Zealand Standard Time / Auckland-Wellington | Relevant to timestamps and benchmark evidence. |
| Active displays | CONFIRMED | One active screen, `DISPLAY1` | A virtual Oray display driver is installed, so remote-session display drift must still be detected. |
| Resolution | CONFIRMED | 1920×1080, 32 bpp; working area 1920×1040 | Record both pixel bounds and working area. |
| System display scale | CONFIRMED | 96 DPI / 100% | Current system DPI; later preset validation should also use per-window/per-monitor DPI. |
| GPU/display adapters | PARTIALLY CONFIRMED | NVIDIA GeForce GTX 1660 plus OrayIddDriver virtual display adapter | Adapter presence is evidence of possible remote-display changes, not a second active screen. |
| Local storage | CONFIRMED | C: NTFS 237.9 GB (39.1 GB free); D: NTFS 1863.0 GB (1188.9 GB free) at inspection | Capacity changes over time; store volume identity and minimum-free-space policy later. |
| Expected production/output drive | CONFIRMED | D: local NTFS volume (`New Volume`) | Operator approved a dedicated root on this volume. |
| Default PrintFlow output root | CONFIRMED | `D:\PrintFlowStudio` | Operator approved and directory created 2026-08-17. Read/write probe passed and left no probe file. At validation, D: had 1186.93 GB free. The path is outside the checked existing customer roots (`D:\Prn Files` and the current user's Desktop/Documents/Pictures/Downloads). |
| Meitu window policy | CONFIRMED | Default maximised; Meitu may already be open and may already be inside a feature such as `AI变清晰` when production begins | Window geometry is confirmed and the operator accepted the normally launched 7.8.7.5 runtime. An already-open feature page is still not automatically a clean starting state. |
| Photoshop window policy | CONFIRMED | Default maximised; extended frame rectangle `(-8,-8)` to `(1928,1048)`, 1936×1056; monitor 1920×1080, work area 1920×1040, per-window DPI 96 / 100% | Operator confirmed policy and restored/maximised the authoritative PS 2019 window on 2026-08-17. Workspace, panels, and visible clean-start evidence remain Task 11003 items rather than window-policy unknowns. |
| Remote/Oray production policy | CONFIRMED | Automation is temporarily permitted only in the normal local console session; Oray/other remote-controlled sessions are prohibited | Operator decision 2026-08-17. Current session was independently detected as active `console`, user `admin`, session ID 1, with no `CLIENTNAME`; Explorer is also in session 1. The installed Oray virtual display driver alone is not a failure, but an active remote session or display-topology mismatch must block automation. |

### A1 output-root evidence

| Evidence item | Status | Value |
| --- | --- | --- |
| Operator approval | CONFIRMED | `D:\PrintFlowStudio`, approved 2026-08-17 |
| Canonical path | CONFIRMED | `D:\PrintFlowStudio` |
| Volume | CONFIRMED | D:, `New Volume`, NTFS, serial `3401882067` |
| Capacity at validation | CONFIRMED | 1863.00 GB total; 1186.93 GB free |
| Writable | CONFIRMED | Create/write/flush test passed using a uniquely named `DeleteOnClose` probe |
| Probe cleanup | CONFIRMED | Probe did not remain after handle close |
| Separation from known customer roots | CONFIRMED | Approved root is not within `D:\Prn Files` or the current user's Desktop, Documents, Pictures, or Downloads |
| Output-name interaction | CONFIRMED | After a file is loaded, PrintFlow must provide a clearly labelled `修改名称` button. It edits the session/output name only and must never rename the source file. Operator decision 2026-08-17. |
| Default output base-name | CONFIRMED | If the operator does not use `修改名称`, default to the original filename without its extension (for example, `photo.jpg` displays `photo`). The source file itself remains unchanged. Operator decision 2026-08-17. |
| Generated suffix presentation | CONFIRMED | Enhancement: `Name_HD.png`; cutout: `Name_CUTOUT.png`; production TIFF: `Name_280mm_CMYK_W.tif`, with the actual approved physical-size value substituted for `280mm`. Operator approved 2026-08-17. |
| Name collision behaviour | CONFIRMED | Append `_02`, `_03`, and so on; never overwrite silently. Operator approved 2026-08-17, consistent with the confirmed MVP design. |

### A2 Meitu window-policy evidence

| Evidence item | Status | Value |
| --- | --- | --- |
| Operator policy | CONFIRMED | Meitu is normally/default maximised during production |
| Already-open startup condition | CONFIRMED | Production use may begin with Meitu already open and possibly inside a feature such as `AI变清晰`; later environment validation must identify the actual page and must not assume a fresh launch or clean state |
| Current top-level window count | CONFIRMED | One Meitu window observed |
| Observed titles | CONFIRMED | Earlier main window: `美图秀秀`; current already-entered editing state: `美图秀秀-图片编辑` |
| Current process image | CONFIRMED | `C:\Users\admin\AppData\Local\MeituApp\XiuXiu\7.8.7.1\XiuXiu.exe` |
| Exact maximised bounds | CONFIRMED | Extended frame rectangle `(-8,-8)` to `(1928,1048)`, 1936×1056; `IsMaximized=true`. Negative/extended borders are normal for the maximised window frame and must not be confused with client-area coordinates. |
| Active monitor / window DPI | CONFIRMED | Monitor rectangle `(0,0)` to `(1920,1080)`; work area `(0,0)` to `(1920,1040)`; per-window DPI 96 / 100% |
| Visible clean-start page | CONFIRMED | Operator closed/reopened Meitu and supplied a screenshot of the untouched start page from 7.8.7.5. Stable structure includes the logo plus `图片编辑`, `海报设计`, `批处理`, `抠图`, `AI消除`, `AI变清晰`, `证件照`, and `AI商品套图` entries. No blocking modal is visible. Theme colour may vary randomly and the upper banner is a rotating advertisement, so neither may be used as a hard recognition signal. |

### A3 Photoshop window-policy evidence

| Evidence item | Status | Value |
| --- | --- | --- |
| Operator policy | CONFIRMED | Photoshop CC 2019 is normally/default maximised during production |
| Authoritative application | CONFIRMED | Desktop-shortcut Photoshop CC 2019 at `D:\Adobe Photoshop CC 2019\Photoshop.exe` |
| Current process/title | CONFIRMED | One visible production process, file version `20.0 (20200706.r.120 2020/07/06: 1208496)`, title exactly `Adobe Photoshop CC 2019` |
| Exact maximised bounds | CONFIRMED | After operator restore: `IsMaximized=true`, extended frame rectangle `(-8,-8)` to `(1928,1048)`, 1936×1056. The earlier `(-32000,-32000)` observation is retained as evidence of the minimised state, not the baseline. |
| Active monitor / window DPI | CONFIRMED | Monitor 1920×1080 with 1920×1040 work area; per-window DPI 96 / 100% |
| Clean start | CONFIRMED | Operator screenshot shows the maximised Chinese home page with no open document and no blocking modal. Stable controls include `主页`, `新建...`, `打开...`, and the full Chinese menu bar. The Creative Cloud login message is home-page content, not a modal. |
| Clean-start evidence | CONFIRMED | Screenshot `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\photoshop-2019-clean-start-001.png`, SHA-256 `980C1E4870BD96C423470D8881387950238BA607CBCE965F2433EA5A9F523660`; manifest `D:\PrintFlowStudio\Baseline\workstation-v1\apps\photoshop-2019\clean-start.json`, SHA-256 `32CB5A276A7AD230379A9863D859FEA2298919B44092E4EDBDA329DECA69095E` |
| Document workspace | CONFIRMED | Approved cutout PNG opened unchanged at 16.7% as `RGB/8`. Visible panels include `颜色`, `属性`, `调整`, `图层`, `通道`, `路径`, `学习`, `库`, and `动作`; `通道` shows RGB/red/green/blue and no spot/alpha channel. The checked workspace is `基本功能（默认）`; it was not switched or reset. |
| Document-workspace evidence | CONFIRMED | Document screenshot `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\photoshop-2019-document-workspace-001.png`, SHA-256 `836B1774B422878DB49823E538BBED77A33FFDAE412990E5D20F4737EEC5464C`; workspace-menu screenshot `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\photoshop-2019-workspace-menu-001.png`, SHA-256 `06472094179E5264AB94FCCE693ECC545757AA60BF921BB1F0EC2E35A9BBCE88`; manifest `D:\PrintFlowStudio\Baseline\workstation-v1\apps\photoshop-2019\document-workspace.json`, SHA-256 `EC9E0EE3B9AF214BD06532CA29E42C93DB3C50289E0DFBF6E9F8174631D9DEB7` |

### A4 remote-session policy evidence

| Evidence item | Status | Value |
| --- | --- | --- |
| Operator policy | CONFIRMED | Temporarily prohibit PrintFlow automation while Oray or any remote desktop/control session is active |
| Allowed session | CONFIRMED | Normal local console display session only |
| Current session | CONFIRMED | Active `console`, user `admin`, session ID 1; no remote client name detected |
| Installed Oray driver | CONFIRMED | May remain installed; presence alone does not indicate an active permitted production session |
| Future blocking rule | CONFIRMED | Block automation when session type is not local console, a remote client is active, or monitor topology/resolution/DPI differs from the signed local preset |

### Local 11001 evidence manifests

These local files contain no customer images and are outside Git. All parsed successfully as JSON after creation.

| Local evidence file | SHA-256 |
| --- | --- |
| `D:\PrintFlowStudio\Baseline\workstation-v1\workstation.json` | `A6AC4FA1DB4EC88F0627FD12DEDFB17E56781F8B7B28FE0B64994A226A9251DC` |
| `D:\PrintFlowStudio\Baseline\workstation-v1\displays.json` | `3E662BFDDFCBED624DDCF21ABA99340F90488F0885E04D58E0B6CF672C2D4B44` |
| `D:\PrintFlowStudio\Baseline\workstation-v1\apps\meitu\window-policy.json` | `4A299A4974934AF35A95164DD1B431F67C67CBAD1AF226B4773B4664F9227966` |
| `D:\PrintFlowStudio\Baseline\workstation-v1\apps\meitu\runtime-drift-20260817.json` | `6B044F8C26F00709998EFF20507DE66DF848C6C7E331D358BF0BCC858A43F3DD` |
| `D:\PrintFlowStudio\Baseline\workstation-v1\apps\photoshop-2019\window-policy.json` | `CF912E8B633E1C0A4E2C5614CB6F9FB8A62FF27B9D00F62C72E4E6553A3080BC` |
| `D:\PrintFlowStudio\Baseline\workstation-v1\signoff\11001.json` | `19A226B6A2D4CEE7807BB150995426D9A97F12A2413CC1BAB5448A8285A3C5D6` |
| `D:\PrintFlowStudio\Baseline\workstation-v1\signoff\11001-meitu-7.8.7.5.json` | `B7D9BE8E95ADC87670DC647356E417BF3378036240FB604B9E3140F1F56F18B4` |

Task 11001 was signed by `DESKTOP-0BG8884\admin` at `2026-08-17T14:42:10.1216941+12:00` with confirmation text `确认11001`. A subsequent normal Meitu restart selected 7.8.7.5 rather than the initially observed 7.8.7.1 runtime. The original signoff is retained for audit, and the operator accepted 7.8.7.5 at `2026-08-17T15:05:00.8049729+12:00`. The live preset status is again `CONFIRMED`.

### Contract capture procedure

1. With no remote-control session changing the desktop geometry, capture Windows edition/version/build, hostname, active display list, pixel bounds, working area, and per-monitor DPI.
2. Record whether the Oray virtual display adapter is permitted during production. If it is permitted, validate its effect on active display order and coordinates.
3. Operator selects the PrintFlow output root and confirms it is not a customer-source directory. Record volume serial/identity, canonical path, ACL write test strategy, and minimum free space.
4. Operator places both production applications in the required clean starting state and selects maximised or fixed window bounds. Capture screenshot, title, process path, rectangle, monitor, DPI, and UI language.
5. Store the values as a versioned workstation preset. Any mismatch affecting coordinates, DPI, language, executable, the validated Photoshop procedure/optional Action, or colour settings must block high-risk automation.

## 4. Task 11002 — Meitu production configuration and operating steps

Operator signoff: **CONFIRMED** on 2026-08-18. Local signoff record: `D:\PrintFlowStudio\Baseline\workstation-v1\signoff\11002.json`, SHA-256 `8FE07B4F30653C8FD211BBB2AFF198CA507C15F51116BA172EA9A73A2FA039EA`. The operator accepted the demonstrated AI-sharpen and smart-cutout paths as complete and waived forced reproduction of rare login/update/tutorial/error windows and a separate filename-collision catalogue. Unknown blocking states still escalate to operator review.

| Item | Status | Finding |
| --- | --- | --- |
| Installed | CONFIRMED | Installed as 美图秀秀 under the current user profile. |
| Launcher path | CONFIRMED | `C:\Users\admin\AppData\Local\MeituApp\XiuXiu\XiuXiu.exe` |
| Initial active executable path | CONFIRMED | `C:\Users\admin\AppData\Local\MeituApp\XiuXiu\7.8.7.1\XiuXiu.exe` before the operator-led clean restart |
| Current active executable path | CONFIRMED | `C:\Users\admin\AppData\Local\MeituApp\XiuXiu\7.8.7.5\XiuXiu.exe` after normal close/reopen at 2026-08-17T14:43:18+12:00 |
| Current active executable version | CONFIRMED | Product/file version 7.8.7.5; 72,229,792 bytes; SHA-256 `D65C6D82323275361EA0ADFBB3F6A5C0D2A5CF4CF63EA3AF1A7DDD4544B037B1` |
| Version consistency | PARTIALLY CONFIRMED | Operator accepted active 7.8.7.5 as authoritative. Registry still says 7.5.8.0 and the uninstaller is 7.8.1.0, so non-runtime metadata remains inconsistent but non-blocking. Future upgrades trigger targeted environment and standard regression revalidation rather than permanent rejection. |
| UI language | CONFIRMED | Language ID 2052 plus the operator screenshot show a Simplified Chinese UI with some English subtitles such as `PhotoEditor`, `Posters`, and `Batch`. |
| Launchability | CONFIRMED | Normal operator restart produced one top-level window titled `美图秀秀` from 7.8.7.5. No additional Meitu top-level window was detected. |
| Normal startup screen | CONFIRMED | Operator reported startup complete and supplied a screenshot. It shows the main feature-entry page with no blocking modal. The theme colour is dynamic and the top banner is rotating advertising; recognise the page from multiple stable structural controls, not pixels/colour/ad text alone. |
| Expected window state | NEEDS OPERATOR INPUT | Maximised/fixed-size policy and starting page not confirmed. |
| Enhancement path | CONFIRMED | Operator demonstrated clean start → `AI变清晰` → `图片编辑`, imported the approved fixture with `高清` selected, observed automatic processing, saved deterministic PNG output, and accepted the validated result as the reference output. |
| Background-removal path | CONFIRMED | Two entry states, direct processing of an already-loaded image, per-job selection modes, progress/completion, transparent PNG export, alpha/bounds validation, and operator-accepted `人像宠物` reference output were demonstrated. |
| Export workflow | PARTIALLY CONFIRMED | AI-sharpen and smart-cutout deterministic PNG exports were demonstrated with explicit `保存成功` completion and on-disk validation. Filename-collision behaviour remains pending. |
| PNG transparency | CONFIRMED | Smart-cutout export contains real alpha: 11,875,515 fully transparent pixels, 209,033 partially transparent pixels, and non-empty subject bounds. |
| Export name/path behaviour | NEEDS OPERATOR INPUT | Must test deterministic destination, collision handling, default extension, and whether Meitu silently changes names. |
| Interruptions | PARTIALLY CONFIRMED | Rotating top-of-page advertising is confirmed but is not a modal. No login/update/tutorial/error modal is visible in this capture. Versioned runtime selection remains an observed revalidation trigger. |
| Completion states | PARTIALLY CONFIRMED | In progress: dimmed workspace plus blocking `变清晰中，请稍候...` overlay and `取消`. Processing ended when that overlay disappeared and the image remained open; no explicit success message or separate apply/confirm action was observed. Exported state remains pending. |

### B1 clean-start evidence

| Local evidence | SHA-256 | Notes |
| --- | --- | --- |
| `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\meitu-clean-start-7.8.7.5-001.png` | `277671EDC5D0FEC387C04BF65D311E5C1BA73405E833EEC3D0BEBCC209762D2B` | 995×674 screenshot supplied by operator; contains application UI/advertising only and remains outside Git |
| `D:\PrintFlowStudio\Baseline\workstation-v1\apps\meitu\clean-start.json` | `898B79DD592B8A234D7582E84D882AB2E523BBE08553279C27ABC7ACAE75ADA8` | Redacted structural observations, dynamic-region exclusions, and recognition rule |

The clean-start screen is identified by multiple structural markers plus process/version/title. Theme colour, rotating-ad content, carousel position, search history text, and recommendation ordering are explicitly excluded from hard-coded recognition.

### B2 AI-sharpen workflow evidence

| Item | Status | Finding |
| --- | --- | --- |
| Navigation to feature | CONFIRMED | Clean start → `AI变清晰` → `图片编辑` workspace; `AI工具` selected; `AI变清晰` panel expanded |
| Default quality option | CONFIRMED | `高清` selected in supplied screenshot; `AI超清` also available |
| Keep-original-size control | CONFIRMED | Visible and observed off before import |
| Primary import control | CONFIRMED | Central `打开图片` button; top `打开` control is also visible |
| Other visible imports | CONFIRMED | Drag/drop or paste, `新建画布`, and `手机导入图片` |
| Sensitive dynamic region | CONFIRMED | `最近打开` customer thumbnails are dynamic, sensitive, and excluded from recognition |
| Local screenshot | CONFIRMED | `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\meitu-ai-sharpen-pre-import-7.8.7.5-001.png`; SHA-256 `9EEEEF006B5B54CACF4BE894D615E4F7BF02CBA469C79D6994CE46F9628E2763` |
| Import behaviour | CONFIRMED | Importing `FIX-CUSTOMER-DESIGN-001` through the central `打开图片` control started `高清` processing automatically |
| Processing state | CONFIRMED | Dimmed workspace and blocking `变清晰中，请稍候...` overlay with `取消`; screenshot `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\meitu-ai-sharpen-processing-7.8.7.5-001.png`; SHA-256 `4930E84B6854993D830AFB6B552EA1879908B9ADC8AC70AA2A1C8AF3313979E8` |
| Completed editor state | CONFIRMED | Processing overlay disappeared automatically and image remained open; no explicit success message or separate apply/confirm action observed. Screenshot `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\meitu-ai-sharpen-completed-7.8.7.5-001.png`; SHA-256 `963A0394F0929042A1F53D13269C6217C3B330348329B1E898436634670ED782` |
| Initial save dialog | CONFIRMED | Default path `C:/Users/admin/Downloads`, observed default name `CUSTOMER-DESIGN-001_副本`, format `jpg`, `手动调整` quality 95, estimated 2747.48 KB. Path choices `自定义`/`覆盖原图`/`桌面`, `更改`, `另存为`, and `保存` are visible. These defaults do not satisfy the approved PrintFlow destination/name contract. Screenshot `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\meitu-ai-sharpen-save-dialog-7.8.7.5-001.png`; SHA-256 `39C158E970DE4C65ADCA52AF38E8235B00AB06AA858D1B2AE9DE40034AC02A0F` |
| Approved destination selected | CONFIRMED | Operator selected `D:\PrintFlowStudio\TestData\v1\expected`. The field visibly truncates the path due to its width, so the exact destination will also be validated from the exported file. Screenshot `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\meitu-ai-sharpen-save-path-7.8.7.5-001.png`; SHA-256 `AA96EF2F7F8F314833CBD212C4EA53A54AF77CB8F0886A1FDF44FCED932D5F85` |
| PNG/name settings | CONFIRMED | Operator entered the intended `FIX-CUSTOMER-DESIGN-001_HD` base name and selected `png`. The narrow focused name field visibly shows the tail `CUSTOMER-DESIGN-001_HD`, so the complete name will be checked on disk. PNG exposes `无压缩`/`智能压缩`/`AI变清晰`, defaults to `无压缩`, and states that PNG supports transparent backgrounds. Screenshot `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\meitu-ai-sharpen-png-settings-7.8.7.5-001.png`; SHA-256 `D7154B4A13D265F958EA02B27F408F806635FB58951913CAC441005A5A82AB59` |
| Save completion | CONFIRMED | Explicit `保存成功` modal with `打开所在文件夹` and `打开新图片`; `显示保存成功提示` is checked. Screenshot `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\meitu-ai-sharpen-save-success-7.8.7.5-001.png`; SHA-256 `E740A1F010C9A1C12A1C1837AE9B4B6C7EE4B88A3D344465FEB0B95675119DD1` |
| Exported file | CONFIRMED | `D:\PrintFlowStudio\TestData\v1\expected\FIX-CUSTOMER-DESIGN-001_HD.png`; 13,831,390 bytes; 3412×5120; approximately 96.012 DPI; SHA-256 `E90A7FE2972209744E3829CA4380574A98B75ECF02B820F2EE707843853C4903` |
| Export alpha | CONFIRMED | PNG decodes as 32-bit ARGB but all 17,469,440 pixels have alpha 255; this output has no actual transparent pixels. The UI statement that PNG supports transparency is a capability statement, not evidence of transparency in this image. |
| Source preservation | CONFIRMED | Source remains at `D:\PrintFlowStudio\TestData\v1\inputs\FIX-CUSTOMER-DESIGN-001.jpeg`, 216,162 bytes, with unchanged SHA-256 `8A7063D8F81FB72A1DB7F5633980660905E2A6C3D3971E4F94A138B5C8394879` |
| Workflow evidence manifest | CONFIRMED | `D:\PrintFlowStudio\Baseline\workstation-v1\apps\meitu\ai-sharpen-workflow.json`; SHA-256 `27099FA7146D4BF3233AA6A53AA13B31797543AAAA2D1EA2E7D2F074E87B07D0` |
| Save/export | CONFIRMED | Destination, deterministic `{Name}_HD.png`, explicit `保存成功` state, on-disk format/name/dimensions/hash, alpha content, and unchanged source were validated. Operator visual acceptance remains separate. |

### B3 smart-cutout state and navigation rule

| Item | Status | Finding |
| --- | --- | --- |
| Start-page entry | OPERATOR CONFIRMED | Select `智能抠图` from the start page to enter the cutout workflow |
| Already-loaded entry | OPERATOR CONFIRMED | If an image is still open in the editor, selecting `智能抠图` immediately starts cutout on that image; this also applies after enhancement whether or not the enhanced result was saved |
| Required state distinction | CONFIRMED | Before navigation, determine whether an image is loaded. An import prompt must not be assumed in the loaded-image state |
| Safety implication | CONFIRMED | In the loaded-image state, selecting `智能抠图` is an operation-triggering action against the current image, not harmless navigation |
| Auto-selection modes | CONFIRMED | `关闭`, `人像宠物`, `商品货物`, and `图标印章` are visible. The mode is a per-job decision and must not be globally hard-coded; this fixture completed with `人像宠物` selected |
| Processing state | CONFIRMED | Direct entry from the already-loaded enhanced image showed `智能识别中...` with `取消`; screenshot `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\meitu-smart-cutout-processing-7.8.7.5-001.png`; SHA-256 `55C0632DA5F2AE5F508D1D731E20AB818F9FDC31C1283BA34C888EA42DE27795` |
| Completed preview | CONFIRMED | Recognition overlay disappeared and the isolated person/chair appeared over a transparency checkerboard. `人像宠物` was selected; `局部抠图`, `手动修补`, `重置`, `反选`, and the right-side `背景`/`尺寸`/`特效` panel were visible. Screenshot `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\meitu-smart-cutout-completed-7.8.7.5-001.png`; SHA-256 `1F4C0253211902DD59A303B16681B08A2A21EC1629EA56EC994AB205F06127A3` |
| Save completion | CONFIRMED | Explicit `保存成功`; screenshot `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\meitu-smart-cutout-save-success-7.8.7.5-001.png`; SHA-256 `97E5B240621530D8F1B6D6F26A1CF595D4794646530A7753C7BCD1FEE8591B05` |
| Exported file | CONFIRMED | `D:\PrintFlowStudio\TestData\v1\expected\FIX-CUSTOMER-DESIGN-001_CUTOUT.png`; 3,895,181 bytes; 3412×5120; SHA-256 `A20A722DB394B8CBBAE7975CC930DD456971E913E5844C21F34B27B9C4D377E2` |
| Alpha and bounds | CONFIRMED | Alpha range 0–255; 5,384,892 opaque, 209,033 partially transparent, and 11,875,515 fully transparent pixels. Non-empty alpha bounds: x 229–2952, y 1210–4894 (2724×3685). Source hash remained unchanged. |
| Workflow evidence manifest | CONFIRMED | `D:\PrintFlowStudio\Baseline\workstation-v1\apps\meitu\smart-cutout-workflow.json`; SHA-256 `803F6E20986292975B68DFB6D976B03D238E6038AE72A03D0945FC4906706DDD` |
| Visual acceptance | CONFIRMED | Operator accepted the person-and-chair cutout edges and retained range as the `人像宠物` reference output on 2026-08-18 |
| Operator signoff | CONFIRMED | `D:\PrintFlowStudio\Baseline\workstation-v1\signoff\11002-meitu-smart-cutout.json`; SHA-256 `C393E03CF30BEEE54C44BBF76F6FDB09A953823554A6766F31DAF3F52DB041F8` |

### Approved fixture for Meitu enhancement demonstration

| Item | Status | Value |
| --- | --- | --- |
| Fixture ID | CONFIRMED | `FIX-CUSTOMER-DESIGN-001` |
| Approval | CONFIRMED | Operator approved permanent local regression use; upload and Git use prohibited |
| Local file | CONFIRMED | `D:\PrintFlowStudio\TestData\v1\inputs\FIX-CUSTOMER-DESIGN-001.jpeg` |
| File SHA-256 | CONFIRMED | `8A7063D8F81FB72A1DB7F5633980660905E2A6C3D3971E4F94A138B5C8394879` |
| Metadata | CONFIRMED | JPEG, 1024×1536, 96×96 DPI, 24-bit RGB, no alpha, 216,162 bytes |
| Privacy | CONFIRMED | Contains an identifiable person and memorial design; local-only evidence with an opaque ID |
| Fixture manifest | CONFIRMED | `D:\PrintFlowStudio\TestData\v1\manifests\FIX-CUSTOMER-DESIGN-001.json`; SHA-256 `FD319C8DE8DF85F8D5C741708963D39681799BFE3511767020C4FC4E9428FB4A` |
| Enhancement expected result | CONFIRMED | Operator accepted `D:\PrintFlowStudio\TestData\v1\expected\FIX-CUSTOMER-DESIGN-001_HD.png` (SHA-256 `E90A7FE2972209744E3829CA4380574A98B75ECF02B820F2EE707843853C4903`, 3412×5120) as the standard AI-sharpen reference output on 2026-08-18. |
| Operator signoff | CONFIRMED | `D:\PrintFlowStudio\Baseline\workstation-v1\signoff\11002-meitu-ai-sharpen.json`; SHA-256 `C87022D3C87F27AD9A20A77D5765166986226F1336926FB74A52B0920967D7FC` |

### Exact operator evidence required

Use a locally approved non-customer fixture and screen recording or timestamped screenshots. Do not use the future automation adapter.

1. Start from a fully closed Meitu, launch with the production shortcut, and show every startup/login/update/ad prompt before dismissing it manually.
2. Show the clean welcome/start page, window state, bounds, active monitor, and UI language.
3. Demonstrate enhancement from import through completed preview and export. Capture every label, dialog, progress state, completion state, export option, default extension, and final path behaviour.
4. Repeat for background removal. Include fine hair and a transparency checker. Inspect the exported PNG for alpha presence, non-empty bounds, pixel dimensions, and readable completion.
5. Demonstrate filename collisions and confirm whether Meitu overwrites, prompts, or renames. PrintFlow itself must always supply a new working path.
6. Trigger or document known login, ad, update, cloud, network, tutorial, and error dialogs. Each unknown modal must later block automation.
7. Record screenshots only in the local evidence store; repository manifests should contain redacted labels/hashes, not customer imagery.

## 5. Task 11003 — Photoshop production configuration

### Authoritative production selection

| Item | Status | Finding |
| --- | --- | --- |
| Production application | CONFIRMED | Adobe Photoshop CC 2019, as corrected by the operator. |
| Authoritative shortcut | CONFIRMED | `C:\Users\admin\Desktop\Photoshop.exe - 快捷方式.lnk` |
| Production executable | CONFIRMED | `D:\Adobe Photoshop CC 2019\Photoshop.exe` |
| Product/file version | CONFIRMED | Product 20.0; file `20.0 (20200706.r.120 2020/07/06: 1208496)` |
| Launchability | CONFIRMED | Launch produced a window titled `Adobe Photoshop CC 2019`. |
| UI language | CONFIRMED | Visible menu labels are Simplified Chinese: `文件`, `编辑`, `图像`, `图层`, `文字`, `选择`, `滤镜`, `3D`, `视图`, `窗口`, and `帮助`. |
| Clean starting state | CONFIRMED | Maximised Photoshop 2019 home page captured with no open document and no blocking modal. `主页`, `新建...`, and `打开...` are visible. Creative Cloud login text is non-blocking page content. |
| Window policy/layout | CONFIRMED | Default-maximised policy, exact bounds, visible document panels, Actions panel visibility, and checked `基本功能（默认）` workspace are confirmed. |
| TIFF save behaviour | NEEDS OPERATOR INPUT | Must be demonstrated through the actual manual post-trim Photoshop procedure on the isolated working copy. |
| Current colour settings | CONFIRMED | Visible `颜色设置` dialog shows preset `自定`; RGB `sRGB IEC61966-2.1`; CMYK `Coated FOGRA39 (ISO 12647-2:2004)`; gray/spot `Dot Gain 15%`; RGB/CMYK/gray policies `保留嵌入的配置文件`; all mismatch/missing-profile prompts off; intent `可感知`; black-point compensation, 8-bit dither, and scene-referred-profile compensation on. |
| CMYK workflow | PARTIALLY CONFIRMED | Current working CMYK and policies are visibly confirmed, consistent with the inspected `Color Settings.csf`. The actual manual conversion command, output profile assignment, and dependence on current settings remain to demonstrate. |
| Existing production Action | NOT APPLICABLE | Operator confirmed the current production flow does not use an Action. Full panel inventory showing only Adobe `默认动作` is therefore expected. The operator authorised creation of a new optional Action if it proves faster after the manual procedure is captured and validated. |
| Task 11003 operator signoff | CONFIRMED | Operator confirmed `确认11003` on 2026-08-18. Signoff: `D:\PrintFlowStudio\Baseline\workstation-v1\signoff\11003.json`, SHA-256 `A079C9A39E3D2D9D8DEECFDD3053220F2A0E06239DED306C96DFCEFD03E930AF`. This signs the Photoshop 2019 application/window/UI/workspace/visible-colour-settings baseline only; the post-trim production procedure, conversion, white channel, and TIFF behaviour remain Task 11004. |

### Non-production Photoshop installations and conflict controls

- Adobe Photoshop 2026 is installed at `D:\Adobe\Adobe Photoshop 2026\Photoshop.exe`. It is explicitly not the production baseline.
- The Start-menu Photoshop CC 2019 shortcut points to missing `D:\ps2019\Adobe Photoshop CC 2019\Photoshop.exe` and must not be used for production discovery or automation.
- PrintFlow must validate the resolved executable path and file version before automation. Display name alone is insufficient.

### Existing production-2019 artifacts suitable for later fingerprinting

| Artifact | SHA-256 at inspection | Status |
| --- | --- | --- |
| `...\Adobe Photoshop CC 2019 Settings\Actions Palette.psp` | `84154BBE1365777CC2CDF8CEC6970E332ABBCDF5659D2F09A205D900CF0D56AB` | CONFIRMED non-production evidence; the operator does not currently use an Action and only Adobe defaults are loaded |
| `...\Adobe Photoshop CC 2019 Settings\Color Settings.csf` | `2418474D01F1930505A48F64610CFB15AFE61216909F2542B5CC40C34FE55EA7` | PARTIALLY CONFIRMED; requires visible preset/policy confirmation |
| `...\Adobe Photoshop CC 2019 Settings\Adobe Photoshop CC 2019 Prefs.psp` | `E7F0B303438922A6A2CC937D98BCE660C3083519C9495949A5F19B772562235B` | PARTIALLY CONFIRMED; broad preference fingerprint only |

Visible Actions-panel inventory: `D:\PrintFlowStudio\Baseline\workstation-v1\apps\photoshop-2019\actions-panel-inventory.json`, SHA-256 `DA519C0FFB03E4DB8704A7873E16488B4F7291F0C802B2ACE60855171B338CAE`. Initial screenshot: `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\photoshop-2019-actions-panel-001.png`, SHA-256 `F762846621074A4605DBB5B1FF8701C1D38A5FCDD0E4DA55CAA20D12E9D2AB4E`. Full expanded inventory screenshot: `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\photoshop-2019-actions-panel-full-001.png`, SHA-256 `ED869FB6B4576A00AA59A32B2AAE138C35A77ED8D1D1F9FBCA9A590D927F8B2B`.

Optional Action authoring manifest: `D:\PrintFlowStudio\Baseline\workstation-v1\actions\authoring\action-authoring.json`, SHA-256 `1C876202E3559BE04837AC61207A9DA4F47FE0C0779E02F37C7C13EAC73017C3`.

Isolated working copy: `D:\PrintFlowStudio\Baseline\workstation-v1\actions\authoring\FIX-CUSTOMER-DESIGN-001_ACTION-WORKING.png`, SHA-256 `A20A722DB394B8CBBAE7975CC930DD456971E913E5844C21F34B27B9C4D377E2`. Open-state screenshot: `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\photoshop-action-working-copy-open-001.png`, SHA-256 `2041ED1C99436C07914832DB06393C876A749313FAC09B122141118817B991AC`. Image-size evidence: `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\photoshop-image-size-a3-portrait-001.png`, SHA-256 `3FA2BD9891309E2B1CF271652BB5E7801E94222FC4B7CFF409B0DFC5B7E2EFFD`. Applied-size screenshot: `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\photoshop-a3-portrait-resize-completed-001.png`, SHA-256 `9A8DFC520A8F0594C0F814A52E194FEB65B15CAA38ECE11DFDF3F2CB2388ED2C`. CMYK command screenshot: `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\photoshop-cmyk-mode-command-001.png`, SHA-256 `2CF93248D2209CD7051DEB80E0CB8220A8B34906B3DBB50C12676127817E458F`. CMYK-completed screenshot: `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\photoshop-cmyk-conversion-completed-001.png`, SHA-256 `D2548AFC8AEB2364C95A3381459B8E3C307A6BC0563092DCC312766C51590501`. CMYK-channel screenshot: `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\photoshop-cmyk-channels-001.png`, SHA-256 `6E9947A2DAA9EF52BA83DDBBDF607971EF3D71D5CE2BE5B227CAF11B2E98E06E`.

White-underbase 1 px evidence: selection/contraction state `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\photoshop-w1-selection-contract-1px-001.png`, SHA-256 `6AA1A53B0910EAF885EC93E79EF4EB60238A9078460581A65860A8370E5A3FEE`; `新建专色通道` dialog `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\photoshop-new-spot-channel-w1-dialog-001.png`, SHA-256 `FDAE13DFB6D44B94270723C4C3D025E09206491B4D9D2F833859965D075A7F15`; completed `W1` channel `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\photoshop-w1-spot-channel-completed-001.png`, SHA-256 `31869F67BAD9F9B5E79DE230F95A0D54FCAE167D180B5E6B01B0EDB402320DCD`.

Action evidence: recorded `W1_1px` `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\photoshop-action-w1-1px-recorded-001.png`, SHA-256 `F773DDB3B1572E8866489355307BB097A735A43091F8E8EEDE068F7E2F95CD52`; three variants `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\photoshop-actions-w1-variants-001.png`, SHA-256 `218A3524AF2AEF9A83BDE4D0777C343337B6A766C96E88F5B15C3D8D831D814A`; clean-copy `W1_1px` replay `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\photoshop-action-w1-1px-clean-replay-001.png`, SHA-256 `905914F2CE48B28732F67A1D36A68C2A164E2098919838E8E44F1501EA3C1711`.

Visible colour-settings evidence: `D:\PrintFlowStudio\Baseline\workstation-v1\apps\photoshop-2019\color-settings-visible.json`, SHA-256 `1822256A31EAE1DE3CC2F5779766E89F919E0E35899BA6B69EF9203D9C11A6A3`. Screenshot: `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\photoshop-2019-color-settings-001.png`, SHA-256 `B793A88EBB6BD445C33C9A372206067059C034E27326BDDFF22303999CEC84F5`.

The installed `Presets\Actions\Production.atn` is an Adobe-distributed sample dated 2020 and has the same hash as the file shipped with Photoshop 2026. Its generic filename is unrelated to the shop procedure and must not be loaded as a production Action.

## 6. Task 11004 — Capture the Photoshop production procedure and optionally author an Action

Operator signoff: **CONFIRMED** by `确认11004` on 2026-08-18. Local signoff record: `D:\PrintFlowStudio\Baseline\workstation-v1\signoff\11004.json`, SHA-256 `6A0144B08104320D06E974BA9CB3B8222B1AED5D76E41656719E0DB4BF134FF7`. This accepts the captured Photoshop procedure, the exported `PrintFlow DTF` Action set, the validated `W1_1px` replay, and Maintop's current default import/channel handling. Content-specific `W1_0px`/`W1_2px` replays may be added incrementally but are no longer an Epic 11000 completion gate under the signed 11005 acceptance. Production reference acceptance is completed under 11007.

### Current verification state

| Procedure property | Status | Required evidence |
| --- | --- | --- |
| Current Action use | CONFIRMED | None. Only Adobe `默认动作` is loaded; it is non-production. |
| Trimming boundary | CONFIRMED | Automatic trimming is performed internally by PrintFlow before Photoshop. The operator's Ctrl-click-thumbnail/crop technique is manual fallback knowledge and is excluded from any Action. |
| Optional Action authoring | W1_1PX EXPORTED AND REPLAY-VALIDATED | `PrintFlow DTF` contains stopped actions `W1_0px`, `W1_1px`, and `W1_2px`. Their visible structures are respectively conversion/selection/create, conversion/selection/1 px contraction/create, and conversion/selection/2 px contraction/create. Resize, crop, file naming, output path, and TIFF save are excluded. Canonical export: `D:\PrintFlowStudio\Baseline\workstation-v1\actions\authoring\PrintFlow-DTF-v1.atn`, 1,636 bytes, SHA-256 `A04203EDEA623C0737D911601A3A005033789BD095130F02A5F8C04CBFCD83EE`. Binary inspection confirms the exact group/action names and both 1 px and 2 px contraction values. A clean RGB copy replayed `W1_1px` without visible error and matched the manual CMYK/W1 result. The 0 px and 2 px variants await suitable content-specific replay fixtures. |
| Resize/resampling | PARTIALLY CONFIRMED | After internal trimming, resize proportionally and shrink-only at 300 ppi. A3 landscape fits within 360×280 mm; A3 portrait within 280×400 mm; A4 long edge ≤280 mm; A5 long edge ≤135 mm. PrintFlow computes dimensions outside the fixed Action. Example A3 portrait dialog shows 280×378.78 mm, 3307×4474 px, 300 ppi, resampling on, `保留细节（扩大）`, noise 0%; applied state shows 28×37.88 cm and a dirty working-copy tab. Shrink-quality/interpolation validation remains pending. |
| CMYK conversion | CONFIRMED | Operator uses `图像 → 模式 → CMYK 颜色` from RGB/8, not `转换为配置文件`. It completed without a prompt, preserved transparency and 28×37.88 cm dimensions, and produced CMYK/8 with CMYK/cyan/magenta/yellow/black channels. A validated `W1` spot channel was then added after those process channels. This is a fixed Action-step candidate. |
| White-ink creation | CONFIRMED FOR 1 PX BRANCH | Ctrl+click the current layer thumbnail to load all non-transparent pixels. Choose contraction by the complete final design: 0 px for especially fine details/fine white; 1 px for ordinary graphics; 2 px for a large solid rectangle or similar. The current cut-out fixture, if treated as a complete design, is the confirmed and demonstrated 1 px example; its uncut full rectangular design is the operator-confirmed 2 px example. The 0 px and 2 px variants still need their own clean-fixture demonstrations. |
| White-channel name/type | CONFIRMED | Photoshop 2019 dialog title is `新建专色通道`; visible fields are `名称`, `颜色`, and `密度(S)`. `W1` at 100% density was confirmed. The visible red swatch is the on-screen spot overlay colour, not the physical white-ink identifier. After confirmation, `W1` follows black in the Channels panel; the intended contracted design region is black in the W1 thumbnail and displays as a red overlay when visible. |
| Optional Action branching | CONFIRMED DESIGN DECISION | Use separate validated `W1_0px`, `W1_1px`, and `W1_2px` variants in a `PrintFlow DTF` set; sizing stays outside. The contraction branch is chosen from reviewed content and is never one global default. |
| TIFF save settings | CONFIRMED FOR MAINTOP DEFAULT WORKFLOW | The demonstrated Photoshop 2019 dialog uses image compression `无`, pixel order `隔行 (RGBRGB)`, byte order `IBM PC`, layer compression `RLE`, with image pyramid and transparency storage unchecked. The saved 116,992,344-byte TIFF is 3307×4474 at 300 dpi, separated CMYK, five interleaved 8-bit samples, no compression, and contains Photoshop `W1` spot-channel metadata. Maintop RIP v6.1 loaded it without visible error and rendered the expected transparent-outline subject on a 600×900 mm layout. The operator confirmed that current Maintop default import and channel handling, including `W1`, are correct and require no extra parameter change. Physical printing remains pending. |
| Bit depth | 8-BIT DEFAULT WORKFLOW ACCEPTED | Maintop RIP v6.1 accepted the saved five-sample 8-bit TIFF, and the operator accepted the current default workflow as correct. An earlier unrelated candidate/reference inspection suggested 16 bits per sample, but it does not override this demonstrated workflow. Physical-print evidence remains a later production-acceptance item. |
| Compression | CONFIRMED IN FILE | Photoshop `无` produced TIFF Compression 1 (none). |
| Photoshop-setting dependencies | PARTIALLY CONFIRMED | Visible current working spaces, colour policies, mismatch/missing-profile prompts, intent, and advanced conversion toggles are recorded. Save-dialog state, ruler units, interpolation preference, foreground/background-colour dependency, workspace name, and hard-coded paths remain pending. |

### Capture and optional Action-authoring procedure

1. Operator demonstrates one known-good post-trim Photoshop run on the isolated authoring copy; trimming is not repeated in Photoshop.
2. Record every explicit step, dialog value, current-setting dependency, and which values vary by order.
3. Decide which fixed repeatable steps are safe to record and how variable size/name/path inputs are supplied without hard-coding a sample value.
4. Record a new versioned Action only if it reduces time without weakening review or validation; otherwise implement the explicit UI procedure directly.
5. Export any new Action set as `.atn` into the local evidence store and record SHA-256, length, timestamp, set/action names, Photoshop version, colour-settings hash, and screenshots.
6. Run the chosen procedure twice from clean copies and compare structural metadata and intended visual output. Fingerprint the `.atn` if used plus a normalized output contract.
7. Have the operator confirm the resulting TIFF is accepted in the current Maintop production flow and complete a physical test print.
8. Create a versioned production-preset identifier, for example `ps2019-dtf-v1`, that points to hashes and expected properties rather than embedding a claim about universal Maintop compatibility.
9. Later environment checks must block Photoshop automation when executable, chosen procedure/optional Action, colour settings, required panels, or reference-output contract drift.

## 7. Task 11005 — Standard local regression image set

Operator signoff: **CONFIRMED BY ACCEPTANCE** on 2026-08-18. Local signoff record: `D:\PrintFlowStudio\Baseline\workstation-v1\signoff\11005.json`, SHA-256 `CA129A6C168F90D9DDBD530A132411BFAD128FB354CAB54F59D9C96BB6713F66`. The operator set Task 11005 complete and waived a mandatory seven-category fixture set as an Epic 11000 gate. Additional categories remain useful incremental regression assets, not required completion evidence.

### Safe inventory result

Read-only enumeration found large numbers of possible local files in the user's Desktop, Documents, Pictures, and Downloads folders: 3,101 JPG/JPEG, 2,497 PNG, 534 PSD, 522 PDF, and 2 TIFF. A separate search of likely D: production locations found 275 TIFF files. These counts prove candidates exist, not that any file is suitable, consented, non-customer, single-page, composite-preview compatible, or Maintop-proven.

No customer file was copied or added to the repository. Candidate filenames are intentionally omitted from this repository report until the operator approves test-fixture use.

| Required category | Inventory status | Existing suitable fixture | Properties/results to record |
| --- | --- | --- | --- |
| 1. Normal JPG portrait | NEEDS OPERATOR INPUT | Many format candidates exist; none approved or visually classified | Source rights/approval, SHA-256, dimensions, EXIF orientation, ICC profile, expected enhancement decision, expected cutout bounds, approved visual reference |
| 2. Complex background / fine hair | NEEDS OPERATOR INPUT | Cannot classify safely from filenames | Same as above plus expected hair preservation, holes/fringing notes, challenging edge crops, review tolerances |
| 3. Transparent PNG | NEEDS OPERATOR INPUT | Many PNG candidates exist; none approved | Colour type, alpha presence/range, transparent bounds, edge pixels, expected trim and safety margins, export alpha result |
| 4. Complete customer design | PARTIALLY CONFIRMED | `FIX-CUSTOMER-DESIGN-001` is approved local-only and fully fingerprinted; expected enhancement/background-removal/production results remain pending | Expected skip decisions, intended final bounds, target dimensions, enhanced visual reference, and expected TIFF review |
| 5. PSD with composite preview | NEEDS OPERATOR INPUT | 534 PSD candidates exist; composite-preview and fixture approval unverified | PSD version, dimensions, colour mode, bit depth, layer count where readable, composite-preview decode success, existing alpha/spot channels |
| 6. Single-page PDF | NEEDS OPERATOR INPUT | 522 PDF candidates exist; approved one-page fixture not selected | Page count=1, MediaBox/CropBox, transparency, embedded profiles, target physical size, 300-DPI rasterisation result |
| 7. Maintop-proven production TIFF | PARTIALLY CONFIRMED | `LOCAL-CANDIDATE-TIFF-001` is structurally inspectable but not proven in Maintop | All Task 11007 fields plus operator/Maintop evidence and physical-production reference |

### Proposed stable local location

The operator has approved `D:\PrintFlowStudio` as the application-owned root. `D:\PrintFlowStudio\TestData\v1\inputs\` and `manifests\` now exist and contain the first approved fixture. Create `expected\` and `maintop-reference\` only when their first approved evidence is ready.

For each approved fixture, maintain a local JSON manifest with an opaque fixture ID, original local provenance, approval date, SHA-256, byte length, format metadata, expected workflow/skip choices, expected bounds/dimensions, known acceptable visual variation, and required output checks. The repository may contain a redacted schema and non-sensitive synthetic fixtures later. Real customer images remain local and untracked by default.

## 8. Task 11006 — Manual workflow benchmark collection plan

Operator signoff: **CONFIRMED BY BUSINESS ACCEPTANCE** on 2026-08-18. Local signoff record: `D:\PrintFlowStudio\Baseline\workstation-v1\signoff\11006.json`, SHA-256 `FEF5388D54DB9314691951E91F23DEA4A94B89333301C5816595EA9434F16A0C`. The operator waived the mandatory 20-example timed benchmark because operator skill varies materially and enabling a non-expert to complete the workflow is itself accepted as a substantial improvement. Later telemetry may measure speed and quality without blocking implementation.

**Status: NEEDS OPERATOR INPUT.** No 20-example observation set was available, and no values are invented here.

### Collection protocol

1. Sample at least 20 consecutive real single-image jobs across all three intended workflow categories; avoid selecting only easy successes.
2. Assign opaque example IDs (`MB-001` through `MB-020`). Do not put customer names in the benchmark sheet.
3. Start the total timer when the operator begins preparing the source and stop when the production output or approved PNG is ready for its current real destination.
4. Use a dedicated active-time timer that runs only while the operator is interacting or making a decision. Waiting includes application launch, processing, save, reopen, and other tool latency.
5. Record overlap explicitly. Enforce `total_elapsed_seconds >= active_operator_seconds`; do not assume total equals active plus waiting if the operator performs other work concurrently.
6. Record rework as part of the same example and note each failure/retry. A restarted job is not silently replaced by its successful attempt.
7. Capture output metadata and a hash where operationally safe. Do not copy customer files into the repository.
8. After MVP runs on the same fixture/job mix, compare medians and totals. Acceptance requires at least 30% reduction in aggregate active operator time and no increase in total single-image time. Also report per-category values and outliers.

### Ready-to-use 20-example template

| Example ID | Workflow/category | Enhance Y/N | Background removal Y/N | Photoshop Y/N | Active operator s | Tool wait s | Total elapsed s | Intervention notes | Output type | Failures/rework | Observed at | Observer |
| --- | --- | --- | --- | --- | ---: | ---: | ---: | --- | --- | --- | --- | --- |
| MB-001 |  |  |  |  |  |  |  |  |  |  |  |  |
| MB-002 |  |  |  |  |  |  |  |  |  |  |  |  |
| MB-003 |  |  |  |  |  |  |  |  |  |  |  |  |
| MB-004 |  |  |  |  |  |  |  |  |  |  |  |  |
| MB-005 |  |  |  |  |  |  |  |  |  |  |  |  |
| MB-006 |  |  |  |  |  |  |  |  |  |  |  |  |
| MB-007 |  |  |  |  |  |  |  |  |  |  |  |  |
| MB-008 |  |  |  |  |  |  |  |  |  |  |  |  |
| MB-009 |  |  |  |  |  |  |  |  |  |  |  |  |
| MB-010 |  |  |  |  |  |  |  |  |  |  |  |  |
| MB-011 |  |  |  |  |  |  |  |  |  |  |  |  |
| MB-012 |  |  |  |  |  |  |  |  |  |  |  |  |
| MB-013 |  |  |  |  |  |  |  |  |  |  |  |  |
| MB-014 |  |  |  |  |  |  |  |  |  |  |  |  |
| MB-015 |  |  |  |  |  |  |  |  |  |  |  |  |
| MB-016 |  |  |  |  |  |  |  |  |  |  |  |  |
| MB-017 |  |  |  |  |  |  |  |  |  |  |  |  |
| MB-018 |  |  |  |  |  |  |  |  |  |  |  |  |
| MB-019 |  |  |  |  |  |  |  |  |  |  |  |  |
| MB-020 |  |  |  |  |  |  |  |  |  |  |  |  |

### Comparison calculations

- Active-time reduction = `(manual active total - MVP active total) / manual active total × 100`.
- Total-time change = `(MVP total elapsed - manual total elapsed) / manual total elapsed × 100`.
- Primary acceptance uses aggregate totals over the matched set; report medians, 90th percentile, and per-category results to expose skew.
- Pass condition 1: active-time reduction is at least 30%.
- Pass condition 2: MVP aggregate total single-image elapsed time does not exceed the matched manual aggregate; investigate any individual regression even if aggregate passes.

## 9. Task 11007 — Maintop reference TIFF

Operator signoff: **CONFIRMED BY ROUTINE-PRODUCTION ACCEPTANCE** on 2026-08-18. Local signoff record: `D:\PrintFlowStudio\Baseline\workstation-v1\signoff\11007.json`, SHA-256 `6D35677221CE82A74876406AFB22455E7723E23252903C3602D3E3561C489B48`. The demonstrated Photoshop/CMYK/W1/TIFF/Maintop path matches the operator's routine manual production practice. No separate physical print was produced or photographed in this validation session; that evidence boundary is explicit in the signoff.

### Local candidate finding

| Property | Status | `LOCAL-CANDIDATE-TIFF-001` finding |
| --- | --- | --- |
| Reference identifier | CONFIRMED | `LOCAL-CANDIDATE-TIFF-001`; exact customer filename is intentionally not committed to the repository |
| Source area | PARTIALLY CONFIRMED | Located under existing local `D:\Prn Files\TIF Files`; directory name alone does not prove production use |
| File size | CONFIRMED | 71,559,252 bytes |
| SHA-256 | CONFIRMED | `F296EBF8CE9DC95062E7F1C4F9E68455D3E4CE5AF6E9FDF945944E24D5419F13` |
| Pixel dimensions | CONFIRMED | 1622×2185 |
| DPI | CONFIRMED | 300×300 pixels/inch |
| Physical dimensions | CONFIRMED | Approximately 137.33×184.997 mm, calculated from pixels and DPI |
| TIFF byte order | CONFIRMED | Little-endian |
| Colour structure | PARTIALLY CONFIRMED | PhotometricInterpretation 5 (separated/CMYK), embedded ICC profile `Coated FOGRA39 (ISO 12647-2:2004)` |
| Samples/channels | PARTIALLY CONFIRMED | 5 samples per pixel: CMYK plus one `ExtraSamples=0` unspecified extra sample |
| Bit depth | CONFIRMED | 16 bits for each of the five samples |
| Compression | CONFIRMED | TIFF compression value 1 (none) |
| Planar layout | CONFIRMED | Chunky/interleaved (`PlanarConfiguration=1`) |
| Producer | CONFIRMED | `Adobe Photoshop CC 2019 (Windows)` metadata |
| White-ink channel | NEEDS OPERATOR INPUT | Fifth sample is present, but name, spot/alpha type, polarity, solidity, density, choke, and non-empty content were not safely established |
| Maintop use | NEEDS OPERATOR INPUT | Must be demonstrated in the current Maintop environment and tied to a real accepted/printed result |

### Newly authored 8-bit workflow test

`FIX-CUSTOMER-DESIGN-001_W1-1PX.tif` is a separate approved local test fixture, not a claim that `LOCAL-CANDIDATE-TIFF-001` has been validated. Its SHA-256 is `D1E69C4108D4C1D6119DB11DE036F56555CDE4A064F23AF541E24E1DAC5EA412`. Photoshop 2019 saved it as 3307×4474 pixels at 300 dpi, separated CMYK plus a named `W1` spot channel, five interleaved 8-bit samples, and no TIFF compression. 蒙泰彩色电子出版系统 RIP v6.1（平板UV个性版）loaded it without a visible error and rendered the expected transparent-outline subject on a 600×900 mm layout. Screenshot: `D:\PrintFlowStudio\Baseline\workstation-v1\screenshots\maintop-v6.1-import-w1-1px-tiff-001.png`, SHA-256 `070ED2106748300A9946D1EFC5E483329B96C82FDF3DAF751E56BF9F7888B815`.

This proves import and layout-preview compatibility for the 8-bit test TIFF. The operator also confirmed that Maintop's current default import and channel handling—including `W1`—are correct and require no additional parameter change. An accepted physical print remains pending.

### Validation and archive procedure

1. Operator selects a TIFF known to have been imported/RIPed and physically produced successfully in the current Maintop workflow. It may be this candidate or another file.
2. Record an opaque reference ID and keep the exact customer path only in the local restricted manifest.
3. Open it manually in Photoshop 2019 and show colour mode, dimensions, bit depth, Channels panel, exact white-channel properties, and TIFF save properties without saving.
4. Open/import it manually in the current Maintop workflow, capture acceptance/RIP evidence, and identify the matching physical production result or dated production record.
5. Hash the unchanged file and archive a read-only approved copy locally only after explicit customer/test-fixture approval.
6. Record the current Maintop executable/configuration fingerprint alongside it.
7. Treat this as one fixed-environment reference. Do not derive a universal Maintop specification from it.

## 10. Environment-risk register

| Risk | Detection method | Later block automation? | Operator remediation | Preset evidence |
| --- | --- | --- | --- | --- |
| Meitu UI update | Active executable path/version/hash; startup and critical-screen visual signatures; element inventory | Temporarily, until changed critical screens and outputs pass revalidation | Accept the new runtime, recapture changed states, and rerun the standard regression set before production acceptance; restore only if validation fails | Launcher and active executable hashes, versions, screen captures, completion signatures |
| Meitu version-metadata mismatch | Compare active binary against registry, config, and allowed preset | Only when the active binary is unknown; warning for known installer/registry mismatch | Treat the running binary as authoritative after operator acceptance | All version sources plus authoritative binary rule |
| Meitu login/update/ad/tutorial dialogs | Enumerate top-level windows/modals and recognised visual signatures before each action | Yes when unrecognised | Operator dismisses manually, resolves login/update policy, then restarts step from clean copy | Known dialog catalogue, screenshots, allowed dismissal policy |
| Photoshop wrong version/path | Resolve shortcut/process image path and file hash | Yes | Launch the desktop PS 2019 production shortcut; close non-production version manually | Shortcut target, executable hash/version |
| Photoshop UI update/workspace drift | Executable/panel/workspace fingerprints and known screen states | Yes | Restore validated workspace/version or rebaseline and regress | Workspace name/layout, bounds, panel screenshots |
| Photoshop procedure or optional Action change | Versioned step transcript, dialog values, preset fingerprints, and optional `.atn` hash | Yes | Restore the approved procedure/preset or formally revalidate the changed procedure/Action | Transcript, settings, optional `.atn`, screenshots, validation outputs |
| Photoshop colour-setting drift | `Color Settings.csf` hash plus visible preset/working spaces/policies | Yes | Restore confirmed settings manually or revalidate preset and prints | `.csf`, hash, screenshots, working profiles and policies |
| Resolution/scaling change | Active-monitor count, bounds, working area, per-monitor DPI at run start | Yes for coordinate/visual automation | Restore validated display state or use manual takeover | Monitor identity/order, resolution, scale, window DPI |
| Multi-monitor/remote-display change | Display topology plus Oray virtual-adapter/session state | Yes unless explicitly validated | Return to the approved topology/session and restart step | Active topology, allowed adapters/session types |
| Windows update/build drift | OS edition/version/build and pending-restart status | Yes for unvalidated build changes before automation | Complete controlled validation/regression or restore supported image | OS build, update date/ID where available, regression sign-off |
| External unsaved documents | Enumerate Photoshop documents and dirty state; detect Meitu unfinished project | Yes | Operator saves/closes or abandons manually; PrintFlow never closes unknown work | Clean-start screenshots/state checks |
| Unknown modal dialog | Top-level window inventory, ownership, title/class, screenshot recognition | Yes | Operator resolves; restart automation step from a fresh working copy | Known-modal catalogue and unknown-dialog diagnostic record |
| Maintop configuration drift | Hash/export relevant Maintop configuration and compare a reference TIFF/print smoke test | Yes for production acceptance; Maintop itself is never automated | Restore validated configuration or revalidate reference and physical print | Maintop version/config fingerprints and signed validation record |
| Low disk space/path loss | Volume identity, writable probe in PrintFlow-owned temp area, free-space threshold | Yes before export | Free space or choose approved output root | Volume/path identity and threshold |
| Customer fixture leakage | Repository ignore rules, manifest scan, approved fixture registry | Yes for test packaging/release | Remove from tracked staging through an approved recoverable process; use synthetic/redacted fixture | Fixture approval and provenance records |

## 11. Operator Inputs Required

| Required information/file/action | Why required | Jira task | Can Epic 11100 foundation proceed without it? | Meitu automation blocked? | Photoshop automation blocked? |
| --- | --- | --- | --- | --- | --- |
| Capture Meitu clean-start appearance | Maximised bounds, monitor, and DPI are now confirmed; current feature/edit page is not clean-start evidence | 11002 | Yes | Yes | No |
| Capture Photoshop CC 2019 workspace, panels, and visible clean-start appearance | Exact maximised bounds, monitor, and DPI are confirmed under 11001; the application workspace evidence remains | 11003 | Yes | No | Yes |
| Record complete Meitu startup, enhancement, cutout, and export demonstration | Labels, state transitions, dialogs, and completion signals cannot be guessed | 11002 | Yes | Yes | No |
| Supply approved non-customer Meitu fixture(s) and expected output | Required to validate alpha and visual outcomes without exposing customer data | 11002/11005 | Yes | Yes | No |
| Catalogue Meitu login/update/ad/tutorial/error dialogs | Unknown UI must fail closed | 11002 | Yes | Yes | No |
| Confirm Photoshop 2019 UI language, clean start, window layout, and workspace | Required starting-state recognition | 11003 | Yes | No | Yes |
| Show current Photoshop colour-settings dialog and export/approve `.csf` evidence | Embedded profiles alone do not prove active policies | 11003 | Yes | No | Yes |
| Demonstrate the complete manual post-trim Photoshop procedure | Required to establish resizing, CMYK, white ink, TIFF, and dependency behaviour | 11004 | Yes | No | Yes |
| Separate fixed steps from order-variable inputs and optionally author/export a new Action | Required before deciding whether direct UI steps or a new `.atn` is the safe faster implementation | 11004 | Yes | No | Yes |
| Approve seven regression fixtures or approved synthetic replacements | Test set cannot use unapproved customer files | 11005 | Yes | Yes before adapter validation | Yes before adapter validation |
| Execute and record at least 20 manual benchmark examples | Required for 30% active-time and non-regression acceptance comparison | 11006 | Yes | No for implementation; yes for acceptance | No for implementation; yes for acceptance |
| Select and confirm one actual Maintop-proven TIFF | Directory/path cannot prove it was successfully produced | 11007 | Yes | No | Yes before production acceptance |
| Demonstrate Maintop acceptance and physical-production evidence | Establishes fixed-environment result without generalising a universal contract | 11007 | Yes | No | Yes before production acceptance |
| Record current Maintop version/configuration fingerprint | Detect production-environment drift | 11007 | Yes | No | No for adapter code; yes for production acceptance |

No missing item above should be silently defaulted from the current desktop or inferred from a candidate customer file.

## 12. Implementation blockers

### Blocking now

- Meitu production automation: READY under the signed 11002 fixed-runtime/manual-path contract. Rare unknown blocking windows fail to operator review rather than being forced during baseline capture.
- Photoshop production automation: READY for the signed `PrintFlow DTF` contract with operator/review selection of the 0/1/2 px branch. `W1_1px` has clean-copy replay evidence; 0/2 content-specific replays are incremental regression work rather than Epic 11000 blockers.
- Production acceptance testing: ACCEPTED by signed operator decisions under 11005, 11006, and 11007. The seven-category set and 20-example benchmark are waived as mandatory gates, and routine manual production experience is accepted without a separately photographed print from this session.

### Not blocking the foundation

- Core WPF shell, fixed workflow state machine, persistence interfaces, SQLite schema/migrations, local file-workspace abstractions, fake Meitu/Photoshop adapters, review record model, structured failures, and unit/integration test harness can proceed from the confirmed design.
- Foundation code must not encode placeholder coordinates, labels, optional Action names, profiles, white-channel names, Maintop assumptions, output roots, or benchmark values as production defaults.

## 13. Recommended evidence/file structure

The operator approved and the validation session created only `D:\PrintFlowStudio`. The child evidence directories below remain recommended and should be created incrementally as their evidence is collected:

```text
D:\PrintFlowStudio\
  Baseline\
    workstation-v1\
      workstation.json
      displays.json
      apps\
        meitu\
        photoshop-2019\
        maintop\
      screenshots\
      actions\
      colour-settings\
      manifests\
      signoff\
  TestData\
    v1\
      inputs\
      expected\
      maintop-reference\
      manifests\
  Benchmarks\
    manual-v1\
    mvp-v1\
```

Repository structure:

```text
docs/printflow/
  phase-11000-production-environment-baseline-plan.md
  schemas/                 # later: redacted JSON/CSV schemas only
  evidence-manifests/      # later: hashes and non-sensitive metadata only
```

Rules:

- Keep customer/test pixels, screenshots containing customer work, exported Actions, Photoshop preference files, Maintop configuration, and exact private customer paths outside Git by default.
- Commit only redacted manifests, schemas, operator-approved synthetic fixtures, and stable hashes/versions.
- Every evidence item gets ID, task, capture time/time zone, operator, source path class, SHA-256, tool/method, result, and approval state.
- Preserve a write-protected baseline copy; create a new preset version rather than overwriting signed evidence.

## 14. Recommended execution order

1. Operator approves output/evidence roots, window policies, remote-display policy, and data/privacy rules (11001).
2. Capture the Meitu manual workflows, interruption catalogue, and approved Meitu fixtures (11002).
3. Capture Photoshop 2019 clean start, UI language/workspace, and visible colour settings (11003).
4. Demonstrate and transcribe the manual post-trim Photoshop procedure; then optionally author, export, hash, and validate a new Action if it is safer and faster (11004).
5. Select/approve or create the seven local fixtures and record their expected properties (11005).
6. Collect 20 consecutive real manual examples using the template (11006). This may run in parallel with fixture approval but must finish before outcome acceptance.
7. Select the Maintop-proven TIFF, demonstrate actual use, capture the Maintop fingerprint, and bind physical-production evidence (11007).
8. Review all evidence as one versioned workstation preset and run the full manual baseline checklist.
9. Begin/continue Epic 11100 foundation. Keep both production adapters disabled until their task-specific gates pass.

## 15. Epic 11000 completion checklist

| Completion item | Current status | Done when |
| --- | --- | --- |
| 11001 workstation contract | CONFIRMED | Output, display, both window contracts, local-only remote policy, naming rules, manifests, initial signoff, and operator acceptance of the active Meitu 7.8.7.5 runtime are complete |
| 11002 Meitu configuration/steps | CONFIRMED | Operator accepted the demonstrated paths as complete and waived forced reproduction of rare windows and a separate collision catalogue; unknown blockers escalate to review. |
| 11003 Photoshop configuration | CONFIRMED | PS 2019 executable/version, maximised layout, Chinese clean start, `基本功能（默认）` workspace/panels, current visible colour policies, and operator signoff are complete |
| 11004 Photoshop production procedure | CONFIRMED | Operator signed `确认11004`; manual procedure, fixed/variable boundary, three-variant Action set, W1_1px clean replay, TIFF structure, and Maintop default import/channel handling are recorded. W1_0px/W1_2px content-specific replays are incremental; production acceptance is signed under 11007. |
| 11005 regression image set | CONFIRMED BY ACCEPTANCE | Operator waived a mandatory seven-category set as the completion gate; additional fixtures are incremental. |
| 11006 manual benchmark | CONFIRMED BY BUSINESS ACCEPTANCE | Operator waived the 20-example benchmark; enabling non-expert completion is accepted as sufficient value. |
| 11007 Maintop reference TIFF | CONFIRMED BY ROUTINE-PRODUCTION ACCEPTANCE | Demonstrated workflow matches routine manual production; no separate print was captured during this session. |
| Workstation preset | CONFIRMED | Immutable preset `printflow-workstation-v1` version `1.0.0` is packaged, hash-verified and signed. |
| Epic 11000 overall | CONFIRMED COMPLETE | Tasks 11001–11007 and the final workstation preset are signed. |

## 16. Readiness decision for Epic 11100

| Area | Readiness | Basis |
| --- | --- | --- |
| Core WPF/workflow foundation | READY | Confirmed v1.0 design defines fixed workflows, state/review invariants, persistence boundaries, fake-adapter seams, and explicit exclusions. Environment-dependent values can remain validated configuration with no production defaults. |
| Meitu automation | READY UNDER OPERATOR-REVIEW FALLBACK | Task 11002 is signed; rare unknown blocking windows escalate to the operator. |
| Photoshop automation | READY UNDER SIGNED BRANCH-SELECTION CONTRACT | Task 11004 is signed. The procedure, exported Action, W1_1px replay, TIFF structure, and Maintop defaults are confirmed; 0/1/2 branch choice remains reviewed content input. |
| Production acceptance testing | ACCEPTED | Tasks 11005–11007 are signed with explicit operator waivers and evidence boundaries. |

Epic 11100 may implement the signed Meitu and Photoshop contracts behind environment checks, Action-hash checks, and operator review for unknown states/content-dependent choices. Preset identity is the exact SHA-256 of `printflow-workstation-v1.0.0.json`.

EPIC 11000 COMPLETE; READY FOR EPIC 11100
