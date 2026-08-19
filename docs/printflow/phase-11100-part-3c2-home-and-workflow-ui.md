# Epic 11100 — Part 3C2: Home, Recent Processing and Workflow Selection

Operator-facing entry point for PrintFlow Studio: Home, single-file import, Recent Processing
with Resume/Abandon, and Workflow Selection. No session processing controls (Part 3C3).

Part 3C1's startup design is untouched: `ISingleInstanceGuard`, `SingleInstanceGuard`,
`ApplicationStartup`, its ordering, `StartupStatus` and `StartupRecoveryService` are unchanged.
Home is reachable from exactly one place — `App.OnStartup`, after `CanShowShell` is true — so it
still cannot open before the guard is claimed, migrations applied and recovery completed.

---

## 1. Home

`ViewModels/HomeViewModel.cs`, `Views/HomeView.xaml`.

Shows the title, one line of startup-recovery status, one line of preset status, the import
panel, and Recent Processing. Nothing more: it is a functional screen, not a designed one.

Every action goes through `ISessionService`. The view model opens no database, copies no file,
computes no hash, creates no directory, constructs no domain record and never touches
`System.IO` — the closest it comes to the file system is passing a path from the dialog or the
drop handler straight into `ImportAsync`.

## 2. Import and drop

One input file, always.

* **Choose** — `IFilePicker.PickSingleFile` (`OpenFileDialogPicker`, `Multiselect = false`). The
  return type cannot express a second file, so batching cannot arrive by that route at all.
* **Drop** — the whole screen is a drop target. `HomeView.xaml.cs` reads the dropped paths and
  passes *all* of them to `HomeViewModel.DropFilesCommand`; the count rule lives in the view
  model, so it is testable without a window.
* **Two or more files** — refused with a message naming how many arrived, and no session is
  created. Deliberately not "use the first one": silently processing one of several would leave
  the operator believing work had started for files that no session exists for.

Import is `ISessionService.ImportAsync`. A session's steps *are* its workflow's steps, so import
must record some workflow; Home imports under `PREPARE_ASSET` provisionally and the next screen
confirms the real choice through `SelectWorkflow`, which is the engine's own supported path — it
re-shapes the session onto the chosen definition and carries the completed import across. The
alternative, a session with no workflow, is a state the domain deliberately does not have.

New application seam (the only one added):

```csharp
Task<OperationResult<IReadOnlyList<SessionListItem>>> ListRecentAsync(CancellationToken ct);
```

`SessionService` owns the policy (`RecentSessionLimit = 100`, `RecentSessionWindow = 30 days`)
so no view model can disagree with another about how much recent work Home shows.

## 3. Recent Processing

Up to 100 sessions from the last 30 days, newest first, straight from SQLite through the seam
above. Each row shows output name, workflow, current step, session state and last-updated time —
localised and formatted. No workspace path, no original source path, no revision, no storage
detail. `SessionListItem` gained `CurrentStep` (already a column on `ProcessingSession`).

## 4. Resume and Abandon

**Resume** loads the session with `ISessionService.LoadAsync` and shows what came back. The row
is only an identifier; nothing displayed on the session screen comes from the row's own text. A
completed or abandoned session offers *Details* instead of *Resume* and opens read-only.

**Abandon** issues `WorkflowCommand.AbandonSession` through `ExecuteAsync` and then refreshes the
list. Nothing is deleted — source snapshot, approved outputs and audit history all remain, which
the tests and the manual smoke both check. The reason recorded is stable English (persisted as
audit data, the same choice `WorkflowCommand.Skip.DefaultReason` makes), not a localised string.

Which entry actions a row offers comes from the workflow layer, not from the UI:
`SessionListItem.CanAbandon` / `.CanContinueProcessing` delegate to the new
`Engine/SessionStateRules`, which is also what `WorkflowEngine` guards its own commands with.
The rule now has one definition instead of two that could drift.

## 5. Workflow Selection

`ViewModels/WorkflowSelectionViewModel.cs`, `Views/WorkflowSelectionView.xaml`.

Exactly the three catalogue workflows, in menu order, with their steps shown so the choice is
informed. Internal values stay `PREPARE_ASSET`, `PREPARE_CUSTOMER_DESIGN`, `GENERATE_PRINT_TIFF`.
No configurable designer.

Selection is `WorkflowCommand.SelectWorkflow` and nothing else — there is no assignment to a
session's workflow anywhere in the UI. The workflow lock is not restated in the view model:
`CanSelect` reads the engine's own `AvailableCommands`, so the buttons are enabled by exactly
the rule that would accept the click, and a refusal still surfaces through the command path if
the session changed underneath.

## 6. Navigation

`Navigation/INavigationService` — three destinations, one `Current`, one changed event.
`ShellViewModel` holds `Current` and nothing else; `MainWindow.xaml` maps view model to view with
three `DataTemplate`s. No journal, no back stack, no routing, no region manager: the operator's
route is `Home → Workflow Selection → Session → Home`.

Screens are transient and resolved at the moment of navigation, so each visit starts clean and
no screen can construct another.

## 7. Localisation

All new operator text is in `Strings.resx` and `Strings.zh-CN.resx` (≈40 new keys covering Home,
Workflow Selection, the session screen, preset status, and localised `SessionState`/`StepState`
labels). `Resources/DisplayNames.cs` is the single enum-to-label mapping. No operator-visible
English is hard-coded in XAML or view models. No runtime switcher — culture follows
`CurrentUICulture`, which the smoke pass exercised for real (the workstation is zh-CN, and every
screenshot below is in Chinese).

Retired with the Part 1 shell: `Shell_Heading`, `Shell_Subheading`, `Shell_WorkflowsHeading`,
`Shell_StepsHeading`, `Shell_FoundationNotice`.

## 8. Tests and manual smoke

`dotnet restore --locked-mode` / `dotnet build` / `dotnet test`:
**5474 passed, 0 failed, 0 warnings, 0 errors** (baseline 5453; 21 added). Run five consecutive
times, green every time.

New automated coverage:

| Area | Test |
| --- | --- |
| Recent policy | asks persistence for ≤100 sessions from the last 30 days |
| Recent order | Home loads three sessions newest-first |
| Recent content | row carries operator information and no path or storage detail |
| Single file | one dropped file is accepted and starts a session |
| Multiple files | two files refused, no session created, message names the count |
| Empty drop | refused, no session created |
| Import | chosen file → real session in SQLite → Workflow Selection |
| Cancel | cancelled dialog starts nothing |
| Workflow choice | each of the three persists through `SessionService`, import survives the re-shape |
| Workflow lock | after a derived Revision: `CanSelect` false, and an attempt is refused, workflow unchanged |
| Resume | progress a session, rebuild the service, Home → Resume → real persisted state |
| Finished session | offers Details, not Resume; no Abandon; opens read-only |
| Abandon | persists Abandoned, keeps snapshot, revision, attempts, files; list refreshes |
| Real graph | `ApplicationStartup` → Home → Workflow Selection → Session → Home |
| Rendering | each view measured and arranged with WPF binding traces escalated to error, asserted empty — plus a negative control proving the listener fires |

Manual smoke, run against a throwaway installed layout under the OS temp directory with
synthetic images only (never `D:\PrintFlowStudio`, never a customer file):

* **Smoke A** — launched, Home shown, chose one image, Workflow Selection listed all three
  workflows, chose Prepare Design Asset → session screen showed `smoke-a` / 准备设计素材 / 进行中 /
  原图确认. Passed.
* **Smoke B** — returned Home, Recent Processing listed the work, Resume on `tiger-logo` opened
  the same persisted session with Import and Original Confirmation 已通过 and Enhancement current.
  Passed.
* **Smoke C** — *not* performed by hand: a synthetic OLE drag-drop is not reproducible from a
  script. The refusal is covered by the automated multi-file test above, and the drop handler
  itself contains no logic beyond forwarding the paths.
* **Smoke D** — Abandon on an active session: notice shown, list refreshed to 已放弃, the row's
  Abandon button gone, and the session's `Source\` snapshot still on disk. Passed.

Startup recovery status and preset status were both visible on Home throughout
("启动恢复未发现需要恢复的内容。" / "预设未校验" — the smoke layout has no preset manifest, which is
correctly non-blocking).

## 9. Defects found

1. **`AvailableCommands` did not report `SelectWorkflow`.** `WorkflowEngine.BuildProbe` returned
   null for it, so the command was permanently absent from the list `SessionView` documents as
   the single source of truth for button legality. Any UI trusting that list would have
   disabled workflow selection forever. Fixed by probing with the session's *current* workflow:
   both guards (session Active, no derived Revision) are checked before the payload is read, so
   the probe answers "may the workflow still be chosen?" honestly. The other payload-carrying
   commands (`SetOutputName`, `SetPrintDimensions`, `SelectWhiteUnderbaseBranch`, `ReturnToStep`)
   are still unprobed and remain a Part 3C3 concern.

2. **Pre-existing test flake, roughly 1 run in 3.** `TempDatabase`/`TempApplication` call
   `SqliteConnection.ClearAllPools()` on disposal to unlock the temp file. That call is
   process-global: it disposed pooled connections belonging to tests running concurrently, which
   then failed with `ObjectDisposedException` from inside SQLitePCL — a flake that reads as a
   persistence bug and is not one. It hit the very first baseline run of this slice. Fixed by
   putting the twelve SQLite-touching test classes in one xUnit collection so no two run at once;
   the rest of the suite still runs in parallel (5–6s, up from 3–4s).

3. **Name collision.** The session screen could not be called `SessionView` — that name is the
   workflow layer's read model. Renamed to `SessionScreenView`.

## 10. Remaining Part 3C3 work

Session processing controls: Run Step, review, Approve/Reject, Retry, Skip, Hand-off,
PrintDimensions, W1 branch, Complete, AddAnotherSize. Image preview and crop, the fake-scenario
selector, a details/diagnostics surface, the history browser, and a runtime language switcher.
Probes for the remaining payload-carrying commands, so `AvailableCommands` can drive every
button on the session screen.

## 11. Git state

Branch `master`, one implementation commit plus this report. Nothing staged but source:
no runtime database, no customer or synthetic smoke file, no logs, no preset or sign-off, no
TIFF evidence. Smoke material lived entirely under the OS temp directory.
