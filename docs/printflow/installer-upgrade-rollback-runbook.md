# PrintFlow Studio — Installation, Upgrade and Rollback Runbook

**Authority:** SCRUM-11123 (work item `11608`), parent SCRUM-11115 (`11600`).

> Create a repeatable versioned offline installer for the validated workstation with no automatic
> application update mechanism. Document installation, configuration and rollback expectations.
> Any PrintFlow, Windows, Meitu or Photoshop upgrade must require rerunning the standard test set
> before production use rather than silently changing the supported environment.

This runbook is the operator-facing half of that requirement. It is written to be followed
literally, in order, on the one validated production workstation.

---

## 0. The two rules that govern everything below

**Installation is not production approval.** A successful install means files are on disk. It says
nothing about whether this workstation may run Production. That question is answered by the
environment verification the application already performs — the workstation preset, its digest,
the Windows build, the display contract, the Meitu and Photoshop identities, the Photoshop colour
settings and the production Action — plus one further check added by SCRUM-11123: whether *this
exact installation* has been revalidated since the last thing changed.

**Any upgrade closes Production until revalidation.** PrintFlow, Windows, Meitu or Photoshop — it
makes no difference which. Each one invalidates the production revalidation record, PrintFlow
refuses Production adapters, and the only way back is Environment Readiness **and** the standard
regression set, both passing, both recorded.

> **Current state (9 September 2026).** The standard local regression set required by SCRUM-11065
> **has been built**: all seven categories exist under `D:\PrintFlowStudio\TestData\v1`, each with
> a manifest recording its expected processing path, expected properties and a fixed SHA-256, and
> `tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1` is the procedure that runs it.
> What has **not** yet happened is a complete run of all seven cases on this workstation, so no
> revalidation record exists and Production stays closed after any upgrade. This is deliberate:
> the gate is satisfied by a passing run, not by a set that exists. See §7.

---

## 1. What is installed, and what is not

| Item | Installed by the PrintFlow installer? | External workstation prerequisite? | Persists across upgrade? | Production authority? | Operator/customer data? |
|---|---|---|---|---|---|
| `PrintFlow.App.exe` and application DLLs | Yes | — | Replaced | No | No |
| .NET 10 runtime (self-contained, in the payload) | Yes | No — never downloaded | Replaced | No | No |
| `appsettings.json` (shipped default) | Yes | — | **Replaced** | No | No |
| `appsettings.local.json` (operator deviations) | **No** | — | **Preserved** | Yes — configured preset and workspace | No |
| Start Menu shortcut | Yes | — | Replaced | No | No |
| `D:\PrintFlowStudio\` workspace root | No | — | Preserved | — | Yes |
| `Data\printflow.db` (SQLite) | No | — | Preserved | No | Yes |
| `Sessions\…\InputSnapshot`, `Revisions` | No | — | Preserved | No | **Yes** |
| Approved PNGs, `PrintOutput` TIFFs | No | — | Preserved | No | **Yes** |
| `Evidence\`, diagnostic packages | No | — | Preserved | No | Yes |
| `Baseline\workstation-v1\…` preset + evidence | No | Yes — placed by the Epic 11000 baseline | Preserved | **Yes** | No |
| `Revalidation\production-revalidation.json` | **No** | — | Preserved | **Yes** | No |
| `Checkpoints\` | No — written by the checkpoint tool | — | Preserved | No | Yes (config + metadata) |
| Meitu | **No** | **Yes** | Unaffected | **Yes** | No |
| Adobe Photoshop | **No** | **Yes** | Unaffected | **Yes** | No |
| Photoshop production Action `PrintFlow-DTF-v1.atn` | No | Yes | Unaffected | **Yes** | No |
| Maintop | No | Yes | Unaffected | Downstream only | No |

The installer writes only under `%ProgramFiles%\PrintFlow Studio` and the Start Menu. Windows
Installer removes only what it installed, so **nothing in the table's "Yes" data column can be
removed by an upgrade or an uninstall**, because the package never authored any of it.

---

## 2. Fresh installation

### 2.1 Prerequisites — verify before installing

1. The workstation is the validated one (Epic 11000 baseline; `workstation-v1`).
2. Meitu is installed at its accepted path, at its accepted version.
3. Adobe Photoshop is installed at its accepted path, at its accepted version, with the accepted
   colour settings.
4. `PrintFlow-DTF-v1.atn` is loaded in Photoshop.
5. `D:\PrintFlowStudio\Baseline\workstation-v1\` holds the signed preset and its evidence.
6. You have local administrator rights (the package is per-machine).

The installer neither checks nor installs 2–4. They are the validated workstation's own
prerequisites, and PrintFlow verifies them at run time rather than at install time.

### 2.2 Install

```
PrintFlowStudio-<version>-win-x64.msi
```

Double-click it, or:

```powershell
msiexec /i "PrintFlowStudio-<version>-win-x64.msi" /qn /l*v install.log
```

No network is required or used. The .NET runtime is inside the package.

Installs to `%ProgramFiles%\PrintFlow Studio`. To install elsewhere, add
`INSTALLFOLDER="D:\Apps\PrintFlow Studio"`.

### 2.3 Configure

`appsettings.json` beside the executable is the **shipped default** and is replaced by every
upgrade. Never edit it.

Put this installation's deviations in `appsettings.local.json` in the same folder. It is never
installed, never replaced and never removed. Any top-level section it names replaces that section
wholesale:

```json
{
  "Workspace": { "Root": "D:\\PrintFlowStudio" },
  "Preset": {
    "Id": "printflow-workstation-v1",
    "Version": "1.16.0",
    "Path": "Baseline\\workstation-v1\\preset\\printflow-workstation-v1.16.0.json",
    "ExpectedSha256": "…"
  },
  "Adapters": { "Mode": "Production" }
}
```

Configuration classification:

| Value | Class | Replaced by an upgrade? |
|---|---|---|
| `appsettings.json` as shipped | Shipped default | **Yes** |
| `Workspace:Root` | Validated production configuration | Only if stated in `appsettings.json` — put it in `appsettings.local.json` |
| `Preset:*` | Validated production configuration | Same |
| `Adapters:Mode` | Validated production configuration | Same |
| `Logging:RetentionDays` | Operator preference — the persisted Settings row wins over it | Same |
| Settings rows in SQLite | Operator preference | **No** — in the workspace database |
| `Revalidation\production-revalidation.json` | Runtime/generated production state | **No** — and invalidated by an upgrade, by design |

### 2.4 Launch and verify

1. Start PrintFlow Studio from the Start Menu.
2. Open **Settings → Environment Check** and run readiness. Every blocking check must pass.
3. Run the standard regression set (§7) and confirm every case passes.
4. Record the revalidation:

```powershell
tools\installer\Set-PrintFlowProductionRevalidation.ps1 `
    -EnvironmentReadinessPassed `
    -StandardRegressionSetPath  "D:\PrintFlowStudio\TestData\v1" `
    -StandardRegressionSetResult "D:\PrintFlowStudio\TestData\v1\runs\<run>\result.json"
```

Production is available only when this script reports **"This installation is revalidated."**
It exits 2 and says **"Production remains CLOSED"** otherwise, including when the standard set
does not exist. There is no override.

---

## 3. Upgrade

There is no updater. PrintFlow never checks for, downloads or applies a new version. An upgrade
happens only when you deliberately run a newer installer.

### 3.1 Record the current state

```powershell
(Get-Item "$env:ProgramFiles\PrintFlow Studio\PrintFlow.App.exe").VersionInfo.ProductVersion
```

Write it down. Keep the current installer file — rollback needs it.

### 3.2 Close PrintFlow Studio

Completely. The checkpoint refuses to run while it is open.

### 3.3 Take a checkpoint

```powershell
tools\installer\New-PrintFlowCheckpoint.ps1 -Reason "before upgrade to <new version>"
```

It captures the SQLite database, `appsettings.json`, `appsettings.local.json`, the current
revalidation record, the installed version and the preset identity into a new timestamped folder
under `D:\PrintFlowStudio\Checkpoints\`. Nothing is ever overwritten.

**Do not proceed unless it prints `Verified :`.** "Created" means files were written; "Verified"
means they were re-hashed from disk and matched. Only the second is a checkpoint you can roll back
to.

It does **not** copy customer images, InputSnapshots, Revisions, approved PNGs, TIFFs or Evidence.
Those are never removed by an upgrade or a rollback, so duplicating them would protect nothing.

### 3.4 Run the new installer

```powershell
msiexec /i "PrintFlowStudio-<new version>-win-x64.msi" /qn /l*v upgrade.log
```

Windows Installer performs a major upgrade: the previous version's files are removed and the new
set installed. `appsettings.json` is replaced with the new release's default.
`appsettings.local.json` is not touched. The workspace is not touched.

### 3.5 Launch — and expect Production to be closed

Start the application. **Settings → Environment Check will now show
"Revalidation after upgrade" as failed**, with the message that this installation has not been
revalidated. That is correct and is the point of the whole procedure: the new binary does not
inherit the previous build's production approval.

### 3.6 Revalidate

1. Run **Environment Readiness**. Every blocking check must pass.
2. Run the standard regression set (§7). Every case must pass.
3. Record it with `Set-PrintFlowProductionRevalidation.ps1` (§2.4).

**Production may resume only after that script reports success.**

---

## 4. Rollback

### 4.1 When to roll back

- The upgraded version fails Environment Readiness for a reason the new build caused.
- The standard regression set fails on the new version and passed on the previous one.
- The new version misbehaves in a way that stops production work.

### 4.2 The database compatibility reality — read this first

PrintFlow's SQLite migrations are **forward-only**. There are no down-migrations, and none is
planned. When a newer application starts, it migrates the database to its schema. That migration
is not reversible.

Therefore: **reinstalling the previous version is not, by itself, a rollback.** The older
executable would meet a schema written by the newer one and would not understand it. A rollback is
only truthful if the database goes back too, which is what the checkpoint is for. This is also why
the installer refuses a downgrade outright rather than letting you take the shortcut that loses
data.

### 4.3 Procedure

1. **Close PrintFlow Studio.**

2. **Uninstall the current version.** Settings → Apps, or:
   ```powershell
   msiexec /x "PrintFlowStudio-<current version>-win-x64.msi" /qn
   ```
   This removes application files and registration only. The workspace, the database, customer
   files, Evidence, the preset and diagnostic packages are all untouched.

3. **Restore the pre-upgrade checkpoint.**
   ```powershell
   tools\installer\Restore-PrintFlowCheckpoint.ps1 `
       -CheckpointPath "D:\PrintFlowStudio\Checkpoints\checkpoint-<stamp>" `
       -RestoreConfiguration
   ```
   The checkpoint is re-verified against its manifest before anything is replaced. The database
   currently in place is renamed `printflow.db.replaced-<stamp>` rather than deleted, so a
   rollback is itself reversible.

4. **Install the previous version.**
   ```powershell
   msiexec /i "PrintFlowStudio-<previous version>-win-x64.msi" /qn
   ```
   The restore output names the exact file.

5. **Restore the configuration** if you did not pass `-RestoreConfiguration` in step 3.

6. **Run Environment Readiness.** It must pass.

7. **Run the standard regression set** (§7). It must pass.

8. **Record the revalidation** (§2.4). Production resumes only then.

### 4.4 What rollback does to your production files — the honest version

**Nothing is deleted.** Source images, InputSnapshots, Revisions, approved PNGs, PrintOutput
TIFFs, Evidence and diagnostic packages all stay exactly where they are.

**But the restored database does not know about work done after the checkpoint.** Those files
remain physically present on disk, and the restored metadata database has no rows for them. In
practice:

- They will **not** appear in Recent Processing.
- Their sessions cannot be resumed.
- Nothing in the application will reference or clean them up.
- They are **not lost** — they are on disk, under `D:\PrintFlowStudio`, where they were written.

There is no automatic reconciliation, and this runbook does not pretend otherwise. Before rolling
back, list what was produced after the checkpoint and decide, per job, whether to hand the output
on from the file system or to redo the work on the rolled-back version. If a job was already sent
to Maintop and printed, the physical output exists regardless of what the database says.

---

## 5. Uninstall

```powershell
msiexec /x "PrintFlowStudio-<version>-win-x64.msi" /qn
```

Removes: application files under `%ProgramFiles%\PrintFlow Studio`, the Start Menu shortcut, and
the Add/Remove Programs registration.

Does **not** remove, and offers no option to remove: the production workspace, the SQLite database
and its history, source files and InputSnapshots, Revisions, approved PNGs, production TIFFs,
Evidence, the workstation preset, checkpoints, the revalidation record, operator-created
diagnostic ZIPs, or `appsettings.local.json`.

To decommission the workstation entirely, delete `D:\PrintFlowStudio` by hand, deliberately, after
the customer work in it has been archived. No PrintFlow tool will do it for you.

---

## 6. External application upgrades

**Windows, Meitu and Photoshop upgrades are not PrintFlow events, and PrintFlow will not adapt to
them.** Selectors, Actions, digests and window contracts are never adjusted to match an
application that changed underneath them. The workstation is revalidated, or Production stays
closed.

| What changed | What PrintFlow does | What you must do |
|---|---|---|
| **Windows** feature/build update | The `OperatingSystem` check fails against the accepted baseline, and the revalidation record no longer matches the observed build. Production closes. | Confirm the new build is acceptable, update the workstation baseline and preset, run Environment Readiness, run the standard regression set, record the revalidation. |
| **Meitu** upgraded | The `MeituExecutable` digest check fails. If the preset is updated to accept the new binary, the revalidation record's recorded Meitu digest no longer matches and Production stays closed anyway. | Re-capture the Meitu baseline evidence, re-issue the preset, re-verify every Meitu automation rule against the new UI, run the standard regression set, record the revalidation. |
| **Photoshop** upgraded | The `PhotoshopExecutable` digest and version checks fail; the same second gate applies via the recorded Photoshop digest. | Re-capture the Photoshop baseline, re-verify colour settings and the production Action, re-verify every Photoshop automation rule, run the standard regression set, record the revalidation. |
| **Photoshop Action** changed | `PhotoshopActionArtifact` fails on the file digest. | Re-approve the Action, re-issue the preset, revalidate. |
| **PrintFlow** upgraded | The recorded product version no longer matches the installed one. Production closes. | §3.6. |

The second gate matters. Updating the preset to accept a new Meitu or Photoshop makes the
executable checks pass again — that is what a preset is for — but it does **not** make the
revalidation record match, because that record names the digests the tests actually ran against.
Accepting a new external application version and *testing against it* are two different acts, and
only the second reopens Production.

---

## 7. The standard regression set

### 7.1 What SCRUM-11065 requires

> Create the fixed local regression set required by the confirmed MVP design, including at least a
> normal JPG portrait, complex background with fine hair, transparent PNG, complete customer
> design, PSD with a compatible composite preview, single-page PDF and a reference production
> TIFF. Record the expected processing path and relevant expected properties for every file. Keep
> all customer-like test data local and suitable for repeatable automated, workstation and upgrade
> regression testing.

### 7.2 Where the set lives

`printflow-regression-v1`, at `D:\PrintFlowStudio\TestData\v1\`, indexed by `set.json`:

| Path | Contents |
|---|---|
| `inputs\` | The six importable assets |
| `reference\` | `FIX-REFERENCE-TIFF-001.tif` — reference only, never imported |
| `expected\` | The accepted Meitu reference outputs |
| `manifests\` | One manifest per asset — the folder `-StandardRegressionSetPath` points at |
| `runs\<run-id>\` | Per-run evidence, including `result.json` |

| Category | Asset | External applications its path needs |
|---|---|---|
| `NORMAL_JPG_PORTRAIT` | `FIX-PORTRAIT-001.jpg` | Meitu |
| `COMPLEX_BACKGROUND_FINE_HAIR` | `FIX-FINE-HAIR-001.jpg` | Meitu |
| `TRANSPARENT_PNG` | `FIX-TRANSPARENT-001.png` | none |
| `COMPLETE_CUSTOMER_DESIGN` | `FIX-CUSTOMER-DESIGN-001.jpeg` | Photoshop |
| `PSD_WITH_COMPOSITE_PREVIEW` | `FIX-PSD-001.psd` | Photoshop |
| `SINGLE_PAGE_PDF` | `FIX-PDF-001.pdf` | Photoshop |
| `REFERENCE_PRODUCTION_TIFF` | `reference\FIX-REFERENCE-TIFF-001.tif` | none |

The set is customer-like local test data. It is **not** in Git and must not be committed or
uploaded. `tools\regression\New-PrintFlowRegressionAssets.ps1`,
`New-PrintFlowRegressionPsd.ps1` and `New-PrintFlowRegressionManifests.ps1` rebuild it, and the
manifests' recorded hashes are what make a rebuild detectable rather than silent.

The unit and acceptance fixtures in `tests\PrintFlow.Tests\Fixtures\` are still not this set and
are still not a substitute for it: they never touch Meitu or Photoshop.

### 7.3 Running it

```powershell
# Layer 1 only — static, offline, opens no application. Seconds. Safe any time.
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1 -PreflightOnly

# The full fixed-workstation run. Drives real Meitu and real Photoshop.
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1

# Record the visual decisions a completed run left open, without re-running anything.
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1 -RunId <run-id> -RecordVisualReview <decisions.json>
```

**Before the full run**, put the workstation in the state the run needs, because it will refuse
otherwise rather than work around you:

1. Meitu open and on its recognised clean start page.
2. **Photoshop open, settled at its start screen, with no document open at all.** Not "no unsaved
   document" — *no document*. `PhotoshopSafeStartingState` must read
   `KnownStartScreen; No document is open.` before you start the run.
3. Nobody else using either application, and nobody using the desktop. The run drives the
   foreground, and any other application that takes it — a browser window is the usual one — will
   block the run.

The surest way to reach state 2 is to start from nothing: close Photoshop, Meitu and PrintFlow
entirely, then launch Meitu and let it settle on its start page, then launch Photoshop and leave it
on its start screen without opening anything.

**Why this is stricter than the check requires.** `PhotoshopSafeStartingState` will still *tolerate*
a saved pre-existing document — it fails only on an unsaved one or an unknown dialog, and a
tolerated document is checked for the same identity afterwards. This precondition is deliberately
tighter than that. Every observed pass of the full live phase has had a document-free Photoshop; the
9 September runs that had another person's document open failed
`PhotoshopTestImageRoundTrip` during probe cleanup. That association is recorded evidence, not a
diagnosed cause, and it has not been confirmed — see the SCRUM-11065 completion report §11.3 and its
10 September delta. Nothing in the Product was changed to accommodate it.

So this is not a workaround for a Product defect, and it should not be written up as one. It is the
cleanest workstation condition acceptance has been observed in, stated as the condition to establish
before a run that is meant to produce a revalidation record. Establishing it costs a minute; not
establishing it has cost whole acceptance attempts.

`-PreflightOnly` writes **no** run result. A set that exists is not a set that passed.

Every case must pass. The top-level status is derived from all seven required categories, so a
partial run — including one narrowed with `-Categories` — can never report `Passed`. A qualitative
visual check that nobody has decided leaves its case `Pending`, which also never reports `Passed`.

### 7.4 Recording the revalidation

Once `result.json` reads `"status": "Passed"`:

```powershell
tools\installer\Set-PrintFlowProductionRevalidation.ps1 `
  -EnvironmentReadinessPassed `
  -StandardRegressionSetPath 'D:\PrintFlowStudio\TestData\v1\manifests' `
  -StandardRegressionSetResult 'D:\PrintFlowStudio\TestData\v1\runs\<run-id>\result.json'
```

It re-verifies that all seven categories appear in a manifest under `-StandardRegressionSetPath`
before it will record a pass, and reads exactly four fields from the run result:

```json
{
  "setId": "printflow-regression-v1",
  "status": "Passed",
  "completedAtLocal": "2026-09-09T14:30:00+12:00",
  "evidencePath": "D:\\PrintFlowStudio\\TestData\\v1\\runs\\<run-id>"
}
```

Never hand-edit `Revalidation\production-revalidation.json`.

### 7.5 Conditions for Production to resume

All of the following, together:

1. Layer 1 passes — seven categories, no hash drift, no `PENDING`.
2. The live environment checks all pass.
3. All seven cases pass in one run, with every recorded visual check decided by a named reviewer.
4. `Set-PrintFlowProductionRevalidation.ps1` writes a record binding this PrintFlow version, this
   preset id/version/digest, this Windows build and the accepted Meitu and Photoshop digests, with
   `standardRegressionSet.status = Passed`.
5. PrintFlow's own verification, asked independently, reports `ProductionRevalidation` as passing.

**This is enforced, not merely documented.** There is no flag that turns "the set did not pass"
into "the set passed", and PrintFlow cannot write its own record — there is no writer for it
anywhere in the solution, and an architecture test asserts so.

---

## 8. Building an installer (release engineering)

```powershell
build\installer\Build-Installer.ps1
```

Publishes Release / self-contained / win-x64, stages the payload through
`installer\payload-policy.json` (default deny), builds the MSI, and writes
`artifacts\installer\<version>\` containing the `.msi`, `build-manifest.json` with its SHA-256 and
full file list, and `symbols\` (a release record — not installed).

The version comes from `Version.props` and nowhere else. Raising it there is the whole release
action.

To validate an artefact:

```powershell
build\installer\Invoke-InstallerSmoke.ps1 -MsiPath <n.msi> -UpgradeMsiPath <n+1.msi>
```

Nothing in either script signs anything, contacts an update service, or touches
`D:\PrintFlowStudio`.
