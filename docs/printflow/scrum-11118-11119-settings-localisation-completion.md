# SCRUM-11118 / SCRUM-11119 — Settings, Production Preset Display and Runtime Localisation

| Item | Value |
| --- | --- |
| Date | 8 September 2026 |
| Repository | `d:\Repositories\printflow-Studio`, branch `master`, local commits only |
| Requirement authority | `C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv` (79 rows, original import) |
| Jira key mapping | `SCRUM key = CSV row position, starting at SCRUM-11060` — so SCRUM-11115 = CSV `11600`, SCRUM-11118 = CSV `11603`, SCRUM-11119 = CSV `11604` |
| Production authority | Signed preset `printflow-workstation-v1` `1.16.0` on `D:\PrintFlowStudio`, read through the existing workstation verifier |
| Starting baseline | Build 0 warnings / 0 errors; full suite 11,553 passed / 0 failed / 0 skipped |
| Result | Build 0 warnings / 0 errors; full suite **11,586 passed / 0 failed / 0 skipped** (+33, exactly the new cases) |

---

## 1. Exact original Jira rows

Quoted verbatim from the CSV, before any Product edit.

### SCRUM-11118 — Complete Settings and Production Preset Display (Task, parent `11600`, 3 pts)

> Provide Settings for default output root, UI language, trim safety margin, fixed production DPI
> display, validated workstation-preset details, Photoshop colour-settings confirmation and local
> log retention. Settings must not turn the MVP into a general user-configurable workflow or
> universal RIP configuration tool.

Labels: `printflow,mvp,settings,production-preset,output-root,retention`

### SCRUM-11119 — Implement Simplified Chinese and English Localisation (Task, parent `11600`, 5 pts)

> Make Simplified Chinese the first-run default and allow English selection in Settings without
> application restart. Translate operator-facing guidance, validation and error descriptions using
> print-production terminology while hiding internal terms such as Revision, Session and Adapter.
> Keep internal state names and structured error codes stable in English for diagnostics and tests.

Labels: `printflow,mvp,localisation,chinese,english,i18n`

### SCRUM-11115 — Complete Operator UX, Localisation, Diagnostics and Offline Packaging (Epic)

> Complete the production-facing PrintFlow Studio experience with the confirmed Home/Drop,
> workflow, review, dimensions, TIFF review, Recent Processing, Settings/Environment Check and
> Error Details surfaces; simplified Chinese default and English switchable without restart;
> local-only logs and screenshots; explicit diagnostic-package export; and a repeatable versioned
> offline installer with no automatic updates. Operator-facing UI must use practical production
> terminology and hide internal implementation terms such as Revision, Session and Adapter while
> preserving stable internal English states and error codes.

Labels: `printflow,mvp,phase-7,ux,localisation,diagnostics,installer,privacy`

---

## 2. Pre-change matrix

Read off the repository at `e2b9c5d`, not off historical audit prose.

| AC clause | Current Product behaviour before this slice | Existing authority | Remaining gap | Implementation needed |
| --- | --- | --- | --- | --- |
| **11118** Settings surface exists | No Settings view, view model or navigation route. `MainWindow.xaml` declared four `DataTemplate`s; `INavigationService` exposed four destinations | `INavigationService`, `MainWindow.xaml` | The whole screen | New `SettingsViewModel` + `SettingsView` + a fifth navigation route + a Home entry point |
| **11118** default output root | Resolved once at startup from `appsettings.json` `Workspace:Root`, used to construct `FileWorkspace`, the preset path and the verifier. Not visible anywhere | `appsettings.json`; preset `storageAndNamingContract.defaultOutputRoot`; verifier check `WorkspaceRoot` | Not displayed | Display read-only from a named fact the readiness report now publishes |
| **11118** UI language | Followed `CultureInfo.CurrentUICulture` — nothing set it. No selector | `Strings.cs` + `Strings.resx` / `Strings.zh-CN.resx` | No authority, no selector, no persistence | New `ILocalisationService` + `OperatorCulture` + Settings selector + persistence |
| **11118** trim safety margin | `ProcessingSession.TrimMargin` / `WorkflowSnapshot.TrimMargin` exist, persist, and start at `TrimMargin.Tight`. Changed per session via `SetTrimParameters` | `TrimMargin`, `WorkflowEngine`, `SqliteSessionRepository` | No **default** for new jobs | Persisted default read once at import; existing sessions untouched |
| **11118** fixed production DPI display | `PrintDimensions.ProductionDpi = 300` used by every calculation. Not displayed on any settings surface | `PrintDimensions.ProductionDpi`; preset `productionGeometryContract.resize.resolutionPpi = 300` | Not displayed | Display read-only from the Product constant |
| **11118** workstation-preset details | Shown on Production Readiness as `PresetIdentity`; one yes/no line on Home | `IWorkstationPresetProvider`, `EnvironmentReadinessReport.PresetIdentity` | Not on a Settings surface | Display read-only from the same report |
| **11118** Photoshop colour-settings confirmation | Verified by the `PhotoshopColourSettings` live check on Production Readiness | `IEnvironmentDiagnostics` / `VerifiedEnvironmentGate` (SCRUM-11110) | Not on a Settings surface | Display read-only status; link to the screen that runs the check |
| **11118** local log retention | `appsettings.json` `Logging:RetentionDays = 30`, parsed into `LoggingConfiguration` and read by nothing | `appsettings.json` | Not visible, not editable, no persisted value | Persist and display; enforcement stays SCRUM-11121 |
| **11118** "must not become a general configuration tool" | n/a | — | — | Four of the seven items are production facts and stay read-only |
| **11119** both locales complete | `Strings.resx` + `Strings.zh-CN.resx`, 342/342 key parity, print-production terminology | `LocalisationResourceTests` | Nothing missing | Extend both files with the Settings strings |
| **11119** zh-CN first-run default | Not enforced. Resolution followed the OS, which happens to be zh-CN on the one supported workstation | — | Product default absent | `OperatorLanguages.FirstRunDefault`, applied at startup |
| **11119** English selection in Settings | No selector anywhere | — | Whole clause | Settings language row |
| **11119** without application restart | Not met | — | Whole clause | `OperatorCulture` + one shell-wide notification |
| **11119** internal identifiers stable English | Already true — `FailureCode`, `StepKind`, `WorkflowType`, adapter ids and `SettingKey` are outside the resx | MVP design §13.4 | Nothing | Guarded by a new architecture test |

---

## 3. Authority and precedence — the decision SCRUM-11076 deferred

`SettingKey`'s own remarks recorded that "which of these the operator may actually change, and
what wins when a persisted value and `appsettings.json` or the signed preset disagree, is
SCRUM-11118's decision". It is made here, and it is deliberately **not one rule**, because the
seven items on the AC's list do not all mean the same kind of thing.

### 3.1 Operator preferences — persisted row wins

| Setting | Authoritative source | Operator-editable | Fallback chain | Restart | Effect on production |
| --- | --- | --- | --- | --- | --- |
| `UiLanguage` | SQLite `Setting` | **Yes** | row → *(no configured rung)* → `OperatorLanguages.FirstRunDefault` = zh-CN | Not required; applied immediately | None |
| `TrimSafetyMarginPixels` | SQLite `Setting` | **Yes** | row → *(no configured rung)* → `TrimMargin.Tight` (0 px) | Not required | **Prospective only** — the margin a newly imported job starts with |
| `LogRetentionDays` | SQLite `Setting` | **Yes** | row → `appsettings.json` `Logging:RetentionDays` → 30 | Not required | None in this slice — configuration only |

### 3.2 Production contract facts — the verified preset wins, and nothing is persisted

| Setting | Authoritative source | Operator-editable | Fallback | Restart | Effect on production |
| --- | --- | --- | --- | --- | --- |
| `DefaultOutputRoot` | Verified preset `storageAndNamingContract.defaultOutputRoot`, published by the gate as `EnvironmentReadinessReport.AcceptedOutputRoot` | **No — read-only** | "the approved production setup has not been confirmed" | Root changes need configuration + revalidation, not a Settings edit | It *is* the production location |
| `ProductionDpi` | `PrintDimensions.ProductionDpi` (300), matching preset `productionGeometryContract.resize.resolutionPpi` | **No — read-only** | none; the constant is always available | n/a | It *is* the print resolution |
| `WorkstationPresetDetails` | `EnvironmentReadinessReport.PresetIdentity` (id, version, short digest) | **No — read-only** | `Environment_PresetUnavailable` | n/a | Identifies the signed configuration outputs are made under |
| `PhotoshopColourSettingsConfirmed` | The colour-setup outcome on `IEnvironmentDiagnostics`, published as `EnvironmentReadinessReport.PhotoshopColourSetup` (SCRUM-11110) | **No — read-only status** | "not checked yet" | n/a | Gates production through the existing gate, not through Settings |

**No `Setting` row is ever written for any of the four.** A persisted copy would be a second
number able to disagree with the preset the outputs were actually produced under, which is the one
disagreement a fixed-workstation product must not be able to have. The enum members stay in the
vocabulary because a persisted name is never removed (MVP design §13.4), not because anything
writes them. `SettingsAuthorityTests.No_shipped_code_constructs_a_setting_entry_for_a_production_fact`
scans the whole of `src` for `SettingEntry.Text/Integer/Boolean(SettingKey.<production fact>` and
fails on any occurrence.

This is also the direct answer to the AC's own limiting sentence — *"Settings must not turn the MVP
into a general user-configurable workflow or universal RIP configuration tool."* Three preferences
are editable; four production facts are shown and cannot be typed over.

### 3.3 Why the output root is not editable

The AC lists "default output root" among the things Settings must **provide**; it does not say the
operator may reconfigure it. In this repository the output root is:

* signed into the accepted preset as `storageAndNamingContract.defaultOutputRoot` = `D:\PrintFlowStudio`;
* compared against the configured root by `ProductionWorkstationVerifier.VerifyWorkspaceRoot`, which
  **fails verification** when they disagree, when the directory is missing, when it is a file, when
  it is a reparse point, or when its file system differs from the accepted one;
* named in the preset's own `implementationGate.requiredRuntimeChecks` as "output root availability";
* resolved once at startup and used to construct `FileWorkspace`, the preset manifest path, the
  database path and the verifier — all before the shell exists.

Making it an editable preference would mean either (a) writing a value that the verifier would then
refuse, closing production, or (b) teaching the verifier to accept a root the preset does not sign.
Both are worse than the AC requires and both contradict §6/§7 of the task brief. It is therefore
displayed read-only, from the verifier, with a hint stating that no existing job is ever moved.

---

## 4. Settings persistence — the existing repository, reused

Nothing new was built for storage. The screen uses the typed seam SCRUM-11076 delivered:

* `SettingKey` — unchanged vocabulary; only its XML remarks were updated to record §3's decision.
* `SettingEntry` — `Text` for the culture name, `Integer` for the two numeric preferences.
* `ISettingsRepository` — `ReadAllAsync` on open, `UpsertAsync` on Apply.
* `SqliteSettingsRepository` — unchanged; already opens one transaction per batch.
* Migration `0001` — **unchanged**. Nothing on the AC's list needed schema the `Setting` table
  cannot represent.

No second key/value store was introduced, and no settings value became a file-system authority.

---

## 5. Language authority

### 5.1 One authority, and why it is not `CultureInfo.CurrentUICulture`

`Strings.Get` previously resolved against `CultureInfo.CurrentUICulture` at call time. That is
almost enough for a runtime switch — but not quite, and the failure mode is the worst possible one
for this AC. `CurrentUICulture` is carried by `ExecutionContext`: a value assigned inside an
`async` continuation is restored to the caller's when that continuation's context unwinds. The
Settings screen applies the language **after** awaiting a database write, so relying on the ambient
property produces a switch that appears to work in the moment and silently reverts. This was
observed directly while building the slice: with `CurrentUICulture`/`DefaultThreadCurrentUICulture`
alone, a screen opened after `RestoreAsync` still rendered English under an `en-US` ambient culture.

So the selected culture is held explicitly:

```
OperatorCulture (internal, PrintFlow.App.Resources)
  · volatile CultureInfo? _selected
  · Current => _selected ?? CultureInfo.CurrentUICulture     ← previous behaviour until selected
  · Select(culture)                                          ← only ILocalisationService calls it

Strings.Get(key) => Manager.GetString(key, OperatorCulture.Current) ?? key
```

`ILocalisationService` / `LocalisationService` owns the selection, persists it, restores it, and
raises `LanguageChanged`. It sets `DefaultThreadCurrentUICulture` and `CurrentUICulture` **as well
as**, never instead of, the explicit selection, so framework and WPF defaults agree with what the
operator reads. `CultureInfo.CurrentCulture` is deliberately untouched: number, date and path
formatting are properties of the workstation's regional configuration, and the AC asks for the
interface language.

### 5.2 First-run rule

`OperatorLanguages.FirstRunDefault = OperatorLanguage.SimplifiedChinese`, a Product constant.
`LocalisationService.RestoreAsync` answers **absent**, **unreadable** and **unrecognised** persisted
values with it — the same answer, because from the operator's side they are the same situation.
Nothing reads the operating system's UI culture; `SettingsAuthorityTests` scans the shell for
`InstalledUICulture` and `GetUserDefaultUILanguage` and fails on either.

`ApplicationStartup.RunAsync` gained step 8: restore the language after recovery and before the
shell is shown. It is deliberately **not** a stage that can refuse — a language preference must
never be the reason a production workstation will not open.

### 5.3 Runtime switching, without restart

Applying a language calls `ILocalisationService.Use`, which selects the culture and raises one
event. What that reaches:

* **the Settings screen already on the monitor** — `SettingsViewModel` raises
  `PropertyChanged(string.Empty)` once, which tells WPF every binding on the screen is stale; each
  label is a computed property over `Strings`, so all of them re-read in the new language. This is
  one notification, not a hand-written list of labels;
* **the shell** — `ShellViewModel` subscribes to `LanguageChanged` and refreshes the window title,
  the only operator-visible string the shell owns;
* **every other screen** — navigation resolves a fresh transient view model on each visit, so any
  screen opened after the switch is already in the new language.

The switch happens **on Apply**, after the batch has committed — not on selection. That is what
makes §22 of the brief satisfiable without a rollback dance: the interface can never be showing a
language the database does not hold, in either direction.

### 5.4 Stable internal identifiers

Unchanged and unchangeable: `FailureCode`, `StepKind`, `WorkflowType`, `OperationKind`, adapter ids,
`AutomationId`s and persisted `SettingKey` names. The persisted language value is itself a stable
culture name (`zh-CN` / `en-US`), following the same rule a `FailureCode` follows. The two language
names shown in the picker (`简体中文`, `English`) are endonyms and deliberately **not** resources —
a language list that translated itself would hide "English" from someone looking for it.

---

## 6. Settings screen

### 6.1 Layout

```
Settings                                            Screen.Settings

General
  Language                     [简体中文 ▾]          Settings.Language
  Default safety edge (px)     [0        ]          Settings.TrimMargin

Production   (these come from the approved production setup; not changed here)
  Output location              D:\PrintFlowStudio   Settings.OutputRoot          read-only
  Print resolution             300 PPI (fixed)      Settings.ProductionDpi       read-only
  Approved production setup    printflow-… 1.16.0   Settings.Preset              read-only
  Photoshop colour setup       Not checked yet …    Settings.PhotoshopColourSettings  read-only
                               [Production readiness]  Settings.OpenEnvironment

Local records
  Keep local records for (days)[30       ]          Settings.LogRetention

  <notice>                                          Settings.Notice
  [Apply]  [Back to Home]                           Settings.Apply / Settings.Back
```

Reachable from Home via `Home.ShowSettings`, beside the existing Production Readiness button. The
route is `INavigationService.GoToSettingsAsync`, added to the existing navigation model — no new
navigation framework, no back stack, no URI routing.

### 6.2 Immediate vs prospective effects

| Row | When it takes effect |
| --- | --- |
| Language | **Immediate**, on Apply, no restart |
| Default safety edge | **Prospective** — the margin a job imported *after* the change starts with. Never rewrites a session, an attempt's recorded `TrimParameters`, a Revision or an approved output |
| Local record retention | **Persisted configuration only.** SCRUM-11121 owns the cleanup; the screen's own hint says so, so nothing on it claims files are being deleted |
| Output location | **Read-only.** No existing session, Revision, InputSnapshot or approved output is ever moved |
| Print resolution / preset / Photoshop colour setup | **Read-only.** Displayed from the existing authorities |

### 6.3 Save behaviour

One Apply is one `UpsertAsync` batch carrying all three preferences, so they commit together or not
at all — the atomicity the AC's single action promises. The order inside `ApplyAsync` is the point:

1. both numeric fields are parsed and validated **before** anything is written, so an unusable entry
   costs nothing and writes nothing;
2. the batch is written as one call — a failure leaves the database exactly as it was;
3. only **after** a successful commit is the language applied and the notice shown.

A failed save keeps the operator on Settings, keeps their typed entries, shows one bounded bilingual
sentence carrying the stable English `FailureCode`, and leaves the visible language untouched.

All three preferences are written every time rather than only the changed ones: one Apply means
"these are my settings", and a diffing rule would make "what did Apply write" depend on state the
operator cannot see.

---

## 7. Photoshop colour-settings confirmation — SCRUM-11110 reused

Settings observes and never repairs. It resolves the same `IEnvironmentDiagnostics` the Production
Readiness screen resolves — which is the same `VerifiedEnvironmentGate` instance the workflow's
production gate is — and takes one **passive** `Read()` off the UI thread.

### 7.1 The shell names no verification check

`EnvironmentDiagnosticsBoundaryTests.The_screen_names_no_individual_check_when_classifying`
forbids any shell view model from naming a member of `WorkstationVerificationCheck` — readiness
policy lives in one place, and the shell is not it (Epic 11500 Part B §11.10). The first draft of
this screen violated it by picking the `WorkspaceRoot` and `PhotoshopColourSettings` rows out of
`Checks` by key, and the guard caught it.

The fix moved the extraction down to the layer that owns the vocabulary rather than weakening the
guard. `EnvironmentReadinessReport` gained two named facts beside the `PresetIdentity` it already
published — `AcceptedOutputRoot` and `PhotoshopColourSetup` — and `VerifiedEnvironmentGate` fills
them in from the verifier's own results as it builds the report. The Settings view model now reads
two properties and names no check at all. `SettingsAuthorityTests` asserts both halves: that the
real gate over the synthetic accepted workstation actually publishes them, and that
`SettingsViewModel.cs` contains no verification check name outside a comment.

The status maps honestly:

| `PhotoshopColourSetup` in the passive reading | Shown |
| --- | --- |
| `null` (the usual case — it is a live-application check) | "Not checked yet — run the check on the production readiness screen" |
| `Passed` | "Confirmed" |
| `Blocked` | "Not checked yet — …" |
| `Failed` / `Advisory` | "Does not match the approved colour setup" |

No "Confirmed" is ever invented for a check that did not run. Settings has no button that changes a
Photoshop colour space, rewrites the preset, installs anything or repairs drift; where a live check
is what the operator wants, `Settings.OpenEnvironment` navigates to the screen that already owns it.
`SettingsScreenHarness`'s default diagnostics seam throws on `RunLiveChecksAsync`, so a Settings
test that ever reached for one would fail.

---

## 8. Accessibility and UI Automation

* `Screen.Settings` on the view root; `Settings.<Row>` on every control the AC names; no localised
  text in any id.
* Every editable row and both actions are Tab-reachable; the read-only production values are
  focusable read-only `TextBox`es so a screen reader can read them out and an operator can copy a
  path or a digest for a support call. No focus trap: nothing on the screen sets
  `KeyboardNavigation.TabNavigation`/`ControlTabNavigation` to `Cycle` or `Contained`.
* Ordinary WPF controls and therefore ordinary UIA patterns: `ComboBox` selection for the language,
  `TextBox` value for the two numeric fields, `Button` invoke for Apply, Back and the readiness
  link. Every element carries an `AutomationProperties.Name` bound to its localised label.
* No coordinate is used anywhere in the tests.

---

## 9. Tests

Proportionate by design: cases map to distinct behaviour and authority, not to combinations. There
is no 7 settings × 2 languages × restart × validity matrix, and the pre-existing localisation
key-parity tests were not duplicated with permutations.

**`tests/PrintFlow.Tests/Integration/Ui/SettingsAndLocalisationTests.cs`** — 28 cases:

| Group | Cases |
| --- | --- |
| Persistence | edited preferences round-trip through a restart; one Apply writes one transactional batch (asserted at the repository seam); a failed save commits nothing and leaves the language alone; an unusable entry is refused and writes nothing; absent settings fall back to configuration and then to the Product constant |
| Language | first run is Simplified Chinese and does not follow Windows (run under an `en-US` ambient culture); the visible screen changes language immediately in both directions; a language switch notifies the shell exactly once; an explicit choice survives a restart in both directions; the persisted value is a stable culture name; stable identifiers do not move with the language (× 2 cultures) |
| Prospective defaults | a saved trim default applies to a newly imported job (through the real service, workspace and database, re-read after a restart); changing the default does not rewrite an existing job; with no default saved an import is still a tight crop |
| Read-only production facts | production facts are read from the verified workstation (real `VerifiedEnvironmentGate` over the synthetic accepted workstation); applying writes no row for any production fact; an unverified colour setup is reported as unverified; a checked colour setup reports what the check concluded (× passed/failed); Settings links to Production Readiness rather than verifying itself |
| Reachability / UI | Home offers a way into Settings; the screen renders keyboard-reachable with stable ids in both languages (× 2 cultures), with zero WPF binding errors; the bounded live WPF/UIA proof |

**`tests/PrintFlow.Tests/Architecture/SettingsAuthorityTests.cs`** — 9 cases: the readiness report
publishes the two production facts Settings displays, over the real gate and the synthetic accepted
workstation; `SettingsViewModel.cs` names no verification check outside a comment; no shipped code
constructs a `SettingEntry` for any of the four production facts (× 4); exactly two operator
languages with the zh-CN satellite present; the shell never derives the language from Windows.

**New fixture** `SettingsScreenHarness` — real settings repository, real culture authority, real
session service over a throwaway workspace and database; restores the process UI culture and clears
the culture selection on disposal so a language switch cannot leak into the rest of the suite.

Two existing test-side changes were needed for the same reason, and both are containment rather
than accommodation:

* `TempApplication` now restores the process UI culture and clears the selection on disposal. The
  real startup sequence selects a language before it hands back a service graph, which is correct
  for an application that starts once and poisonous in a test host that starts one dozens of
  times. The fixture that owns "an application was started" owns putting the process back. Without
  it, sixteen unrelated localisation-rendering tests failed in the full suite while each passed in
  isolation — a leak, not a regression, and one that would have flaked forever if papered over.
* `EnvironmentReadinessScreenTests` joined the existing `SQLite` collection. It opens no database,
  but it renders operator text and compares it to a resource, so it must never run beside a test
  that switches the application's language.

---

## 10. WPF / UIA proof

`A_driver_switches_language_edits_a_setting_and_both_survive_a_restart`, one bounded synthetic pass
on a real STA thread with real measure/arrange:

1. no persisted language, ambient culture `en-US` → startup restores **Simplified Chinese**;
2. Settings opens; the rendered heading `TextBlock` reads `设置`;
3. a driver selects **English** on the rendered `ComboBox`, types `6` into the safety-edge field
   through `IValueProvider.SetValue`, and presses Apply through `IInvokeProvider.Invoke`, pumping
   the dispatcher as a real desktop does;
4. the **same rendered heading** now reads `Settings` — the visible screen changed with no restart
   and no reopen;
5. the culture authority and the session service are rebuilt over the same database (a restart);
   Settings reopens in **English** with the safety edge still `6`;
6. Simplified Chinese is selected again and applied; the heading returns to `设置` immediately.

No external application is launched; the colour-settings AC needs a display, not a live Photoshop
verification, and Settings never initiates one.

The item containers of a `ComboBox` live in a `Popup`, which needs a real window to realise, so the
language is chosen on the rendered control itself rather than through an `ISelectionItemProvider`;
the `TextBox` and both `Button`s are driven through genuine UIA providers. No coordinate is used.

---

## 11. Full-suite decision

The full suite was run, once, against final source. It is justified three times over: this slice
changes **application culture resolution** (every operator-visible string now resolves through
`OperatorCulture`), **session initialisation defaults** (`SessionService.ImportAsync` now reads a
persisted trim default), and **DI/composition and navigation root behaviour** (three new
registrations, a fifth navigation destination, a new `ShellViewModel` dependency, a new startup
step).

```
Passed!  -  Failed: 0, Passed: 11586, Skipped: 0, Total: 11586, Duration: 4 m 37 s
```

Baseline 11,553 / 0 / 0 → **+33**, exactly the new cases counted with their `[Theory]` expansions
(24 in `SettingsAndLocalisationTests`, 9 in `SettingsAuthorityTests`). No existing test was changed
to pass differently; the only edits to existing tests were the two `INavigationService` doubles
gaining the new method, `SessionServiceHarness` gaining the real settings repository,
`TempApplication` restoring the process UI culture, and the collection attribute on
`EnvironmentReadinessScreenTests`.

The suite was run twice, and the first run is worth recording: it failed sixteen unrelated
localisation-rendering cases that each passed in isolation. That was the process-wide culture
selection leaking out of the startup-composition tests, and it was fixed in the fixture that
causes it rather than by weakening the product's behaviour.

---

## 12. SCRUM-11118 reassessment — NOT_IMPLEMENTED → **FULL**

| AC clause | Verdict | Evidence |
| --- | --- | --- |
| "Provide Settings for …" (a reachable operator surface) | **Met** | `SettingsView` / `SettingsViewModel`, `GoToSettingsAsync`, `Home.ShowSettings`; rendered with zero binding errors in both languages |
| default output root | **Met, read-only** | Displayed from the verifier's `WorkspaceRoot` check, which is the preset's signed `defaultOutputRoot`. §3.3 states why it is not editable |
| UI language | **Met, editable** | Persisted through `ISettingsRepository`; applied immediately; survives restart |
| trim safety margin | **Met, editable, prospective** | Persisted default; applied to newly imported jobs; existing sessions provably not rewritten |
| fixed production DPI display | **Met, read-only** | `PrintDimensions.ProductionDpi`, the same constant every calculation uses |
| validated workstation-preset details | **Met, read-only** | `EnvironmentReadinessReport.PresetIdentity` — id, version, short digest |
| Photoshop colour-settings confirmation | **Met, read-only, truthful** | Reuses SCRUM-11110's authority; says "not checked yet" rather than inventing a confirmation |
| local log retention | **Met as a setting** | Persisted and displayed, with `appsettings.json` as the configured fallback. **Enforcement is explicitly not here** — see §13 |
| "must not turn the MVP into a general user-configurable workflow or universal RIP configuration tool" | **Met** | Three preferences editable; four production facts read-only and never persisted, guarded by an architecture test |

**SCRUM-11118 → FULL.**

## 13. SCRUM-11119 reassessment — PARTIAL → **FULL**

| AC clause | Verdict | Evidence |
| --- | --- | --- |
| Simplified Chinese is the first-run default | **Met** | `OperatorLanguages.FirstRunDefault`, applied by `ApplicationStartup` step 8; proven under an `en-US` ambient culture; no OS read anywhere in the shell |
| English selectable in Settings | **Met** | `Settings.Language` |
| without application restart | **Met** | The rendered heading changes on the screen already displayed, both directions, in the UIA proof |
| operator-facing guidance, validation and error descriptions translated | **Met** | 342 → 370 keys, both files, full parity; the new Settings strings use production wording ("output location", "safety edge", "approved production setup", "local records") |
| internal terms hidden | **Met** | No new string says Revision, Session or Adapter |
| internal state names and structured error codes stable in English | **Met** | Asserted in both cultures; the persisted language value is itself a stable culture name |

**SCRUM-11119 → FULL.**

## 14. Parent SCRUM-11115 reassessment — remains **PARTIAL**

Reread clause by clause against the exact Epic Description:

| Epic clause | State after this slice |
| --- | --- |
| Home/Drop, workflow, review, dimensions, TIFF review, Recent Processing surfaces | Present from earlier Epics |
| **Settings / Environment Check surfaces** | **Now complete** — Production Readiness (SCRUM-11110) plus Settings (this slice) |
| **Error Details surface** | **Absent** — SCRUM-11120 has no page. `AutomationLogEntry` is its durable backing and now has a reader, but nothing renders it |
| **simplified Chinese default and English switchable without restart** | **Now complete** — SCRUM-11119 |
| **local-only logs and screenshots** | **Partial** — SCRUM-11121. Failure screenshots are captured and their paths are queryable, and the retention period is now visible and editable, but there is no rolling local text log, no `ILogger`/Serilog rollout, no 30-day cleanup execution and no scheduler |
| **explicit diagnostic-package export** | **Absent** — SCRUM-11122 |
| **repeatable versioned offline installer, no automatic updates** | **Absent** — SCRUM-11123 |
| practical production terminology; internal terms hidden; stable internal English states and codes | Held, and now enforced for the Settings surface too |

Remaining children and their exact gaps:

| Child | State | Exact remaining gap |
| --- | --- | --- |
| SCRUM-11116 Home / single-image drop | Present | Not reassessed by this slice; the only change to Home is one navigation button |
| SCRUM-11117 Recent Processing | Present | Not reassessed by this slice |
| SCRUM-11118 Settings and preset display | **FULL** | — |
| SCRUM-11119 zh-CN / English localisation | **FULL** | — |
| SCRUM-11120 Error Details and recovery UI | **NOT_IMPLEMENTED** | No page: no structured error code / bilingual description / screenshot / input path / expected output path / retry information / recovery actions surface |
| SCRUM-11121 Local log and screenshot retention | **PARTIAL** | Retention *setting* is now visible and editable (this slice). Still missing: the rolling local log itself, cleanup execution honouring active diagnostic references, and the operator-visible stored *locations* for logs and screenshots |
| SCRUM-11122 Diagnostic package export | **NOT_IMPLEMENTED** | No export, no contents preview, no consent step |
| SCRUM-11123 Versioned offline installer | **NOT_IMPLEMENTED** | No installer, no documented install/configure/rollback procedure |

**SCRUM-11115 stays PARTIAL.** Four of eight children are still open, and two of the Epic's own
named deliverables (diagnostic export, offline installer) have no product at all.

---

## 15. Scope boundaries honoured

Not implemented here, deliberately:

* **SCRUM-11121** — no rolling text log, no `ILogger`/Serilog rollout, no 30-day cleanup execution,
  no screenshot cleanup, no retention scheduler. Only the retention *setting* is persisted and
  displayed, and the screen's own hint says clean-up is not yet in place, so nothing claims
  otherwise. SCRUM-11121 is **not** marked FULL.
* **SCRUM-11120** — no Error Details page. The `AutomationLog` reader remains its future backing.
* **SCRUM-11122 / SCRUM-11123** — no diagnostics export, no installer work.
* **No digital-signature work** — no code signing, no certificate verification, no signing tests.
  The existing preset SHA-256 integrity checks are untouched.
* **Migration `0001` unchanged**; no second key/value store; `ISettingsRepository` reused, not
  replaced.
* No unrelated file was modified; no evidence, preset or `appsettings.json` value was changed.

---

## 16. Git state

Branch `master`, no branch, worktree or alternate clone created. Local commit(s) only — nothing
amended, rebased or pushed, and no AI-attribution trailer added.

Changed:

```
src/PrintFlow.App/Composition/ApplicationStartup.cs        startup step 8 — restore the language
src/PrintFlow.App/Composition/ServiceRegistration.cs       3 registrations + the precedence note
src/PrintFlow.App/Localisation/OperatorLanguage.cs         new — the two languages, the default
src/PrintFlow.App/Localisation/ILocalisationService.cs     new — the culture authority's seam
src/PrintFlow.App/Localisation/LocalisationService.cs      new — the authority
src/PrintFlow.App/MainWindow.xaml                          fifth DataTemplate
src/PrintFlow.App/Navigation/INavigationService.cs         GoToSettingsAsync
src/PrintFlow.App/Navigation/NavigationService.cs          GoToSettingsAsync
src/PrintFlow.App/Resources/OperatorCulture.cs             new — the selected culture
src/PrintFlow.App/Resources/Strings.cs                     30 accessors; Get resolves via OperatorCulture
src/PrintFlow.App/Resources/Strings.resx                   28 new keys
src/PrintFlow.App/Resources/Strings.zh-CN.resx             28 new keys
src/PrintFlow.App/Settings/SettingsDefaults.cs             new — the configured fallback rung
src/PrintFlow.App/ViewModels/HomeViewModel.cs              ShowSettingsCommand + label
src/PrintFlow.App/ViewModels/SettingsViewModel.cs          new — the screen
src/PrintFlow.Infrastructure/Gate/VerifiedEnvironmentGate.cs  publishes the two named production facts
src/PrintFlow.Workflow/Ports/IEnvironmentDiagnostics.cs    AcceptedOutputRoot + PhotoshopColourSetup
src/PrintFlow.App/ViewModels/ShellViewModel.cs             refreshes the title on a language change
src/PrintFlow.App/Views/HomeView.xaml                      Settings button
src/PrintFlow.App/Views/SettingsView.xaml(.cs)             new — the screen's markup
src/PrintFlow.Domain/Settings/SettingKey.cs                XML remarks only — records the precedence
src/PrintFlow.Workflow/Services/SessionService.cs          optional ISettingsRepository; prospective trim default
tests/… SettingsAndLocalisationTests.cs                    new — 28 cases
tests/… Architecture/SettingsAuthorityTests.cs             new — 8 cases
tests/… Fixtures/SettingsScreenHarness.cs                  new
tests/… Fixtures/SessionServiceHarness.cs                  exposes the real settings repository
tests/… Fixtures/HomeScreenHarness.cs                      navigation double gains GoToSettingsAsync
tests/… Fixtures/TempApplication.cs                       restores the process UI culture on disposal
tests/… Smoke/TiffFinalReviewWorkstationSmoke.cs           navigation double gains GoToSettingsAsync
tests/… Diagnostics/EnvironmentReadinessScreenTests.cs     joins the SQLite collection
docs/printflow/scrum-11118-11119-…-completion.md           new — this report
docs/printflow/original-jira-functional-coverage-reaudit.md appended delta only
```

---

**PASS — SCRUM-11118 / SCRUM-11119 SETTINGS AND LOCALISATION VERIFIED**
