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

The ordering here matters and §7.6 states it in full. On a fresh installation Production is closed,
so the standard set is run under the bootstrap described in §7.6 — the normal application's live
checks come *after* the revalidation record exists, not before it.

1. Start PrintFlow Studio from the Start Menu.
2. Bring the workstation to the state §7.3 describes, and run one complete standard regression set
   run (§7) against this installation. Confirm every case passes.
3. Record the operator reviews of any qualitative checks it left open (§7.3).
4. Record the revalidation from that run:

```powershell
tools\installer\Set-PrintFlowProductionRevalidation.ps1 `
    -EnvironmentReadinessPassed `
    -StandardRegressionSetPath  "D:\PrintFlowStudio\TestData\v1" `
    -StandardRegressionSetResult "D:\PrintFlowStudio\TestData\v1\runs\<run>\result.json"
```

5. Only now open **Settings → Environment Check** and run the normal application's readiness, then
   its end-to-end work.

Production is available only when this script reports **"This installation is revalidated."**
It exits 2 and says **"Production remains CLOSED"** otherwise, including when the standard set
does not exist. There is no override.

It exits 3 and says **"THE PROPOSED REVALIDATION WAS REFUSED"** when the run result you gave it
cannot be shown to be about this installation. That is a different outcome: nothing is written and
the record you already had is left exactly as it was. See §7.4.

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

The full ordering is §7.6. In short:

1. Bring the workstation to the controlled state the run needs (§7.3), and run **one complete**
   standard regression set run against the installation you just made (§7).
2. Record the actual operator reviews of the qualitative checks that run left open (§7.3).
3. Record the revalidation from that run with `Set-PrintFlowProductionRevalidation.ps1` (§7.4).
4. Only then run the normal application's own live checks and its end-to-end work.

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

Prepare a controlled build pair from committed relevant inputs in the canonical checkout. This
generates files only; it neither installs nor launches the Product. Preserve the entire pair
directory through review and publication. A receipt is trusted local tooling evidence, not release
acceptance. Do not retrofit an old harness/candidate from matching informational versions.

```powershell
$pair = tools\regression\New-PrintFlowBuildPair.ps1 -OutputRoot artifacts\build-pairs
$receipt = Join-Path (Split-Path $pair.HarnessFolder) 'build-pair.json'
tools\regression\New-PrintFlowBuildPair.ps1 -VerifyOnly -ReceiptPath $receipt

# Read-only check inside that exact loaded harness; no Product service graph or applications.
$env:PRINTFLOW_BUILD_PAIR_PROOF_RECEIPT = $receipt
$env:PRINTFLOW_BUILD_PAIR_PROOF_CANDIDATE = $pair.CandidateFolder
try {
    & $pair.DotnetPath vstest (Join-Path $pair.HarnessFolder 'PrintFlow.Tests.dll') `
        '/TestCaseFilter:FullyQualifiedName~BuildPairVerificationSmoke' `
        "/ResultsDirectory:$(Join-Path (Split-Path $pair.HarnessFolder) 'verification-results')"
    if ($LASTEXITCODE -ne 0) { throw 'Loaded build-pair verification failed.' }
} finally {
    Remove-Item Env:PRINTFLOW_BUILD_PAIR_PROOF_RECEIPT, Env:PRINTFLOW_BUILD_PAIR_PROOF_CANDIDATE -ErrorAction SilentlyContinue
}
```

```powershell
# Layer 1 only — static, offline, opens no application. Seconds. Safe any time.
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1 -PreflightOnly

# Only after separate live authorization and workstation preparation below.
# Executes the receipt's concrete test DLL with vstest; no restore or build occurs.
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1 `
    -BuildPairReceipt $receipt -CandidateInstallFolder $pair.CandidateFolder

# Record the visual decisions a completed run left open, without re-running anything.
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1 -RunId <run-id> -RecordVisualReview <decisions.json>
```

**A run identity belongs to one execution.** If the run folder already exists the script stops and
tells you so; it does not reuse the destination and it does not delete what is there. Pick a
different `-RunId` — the default timestamp already differs. To record a review of that existing run
instead, use `-RecordVisualReview`, which is the deliberate way to update it.

**A claimed run identity is spent, including after a run you interrupted.** Nothing expires a claim.
If you stop a run part way, that `RunId` is used and the next attempt needs a new one. This is on
purpose: a claim that timed out would let a second execution overwrite the first one's evidence,
which is the thing this whole mechanism exists to prevent. The evidence the interrupted run did write
stays where it is, and remains readable.

**The run reports a pass only when its own host completed and wrote its own result.** A host that
exits nonzero is a run that did not complete, whatever is on disk; a result left by another
invocation is not this one's outcome. Both were reported as success before PF-AUDIT-R1.

**`-CandidateInstallFolder`** names the staged or installed candidate the run is testing on behalf
of; it defaults to `%ProgramFiles%\PrintFlow Studio`. Its Product bytes must match the candidate
side of `-BuildPairReceipt`. The run executes the paired harness and compares its own loaded
Product and test-host runtime artifacts with the harness side. Same labels with different bytes
are refused before operational setup. The two profiles need not match each other's bytes.

The run records evidence binding version 2 and the original receipt path, SHA-256 and pair id.
Publication checks that reference, both retained output sets, the run's identities and the target
candidate. Review preserves it unchanged and accepts no replacement receipt. Historical/unbound
runs stay readable but cannot be enriched into a publishable run. `-DiagnosticUnbound` is an
explicit nonpublishable diagnostic mode using an already-built local test project; it still drives
real applications and needs separate authorization. Review also uses `--no-build --no-restore`;
prepare the local Release test project beforehand if needed.

A documentation-only commit does not invalidate retained paired artifacts. A later Product or
harness rebuild requires a fresh pair; present-day git cleanliness cannot certify old binaries.
No receipt grants Production permission or replaces the real set, real reviews or the explicit
`-EnvironmentReadinessPassed` operator attestation.

**Before the full run**, put the workstation in the state the run needs, because it will refuse
otherwise rather than work around you:

1. Meitu open and on its recognised clean start page.
2. **Photoshop open, settled at its start screen, with no document open at all.** Not "no unsaved
   document" — *no document*. `PhotoshopSafeStartingState` must read
   `KnownStartScreen; No document is open.` before you start the run.
3. Nobody else using either application, and nobody using the desktop. The run drives the
   foreground, and any other application that takes it — a browser window is the usual one — will
   block the run.
4. **Photoshop's active tool must not be the Crop tool.** With a crop pending, every document
   Photoshop opens enters 裁剪预览 (crop preview), and in that state the options-bar controls the
   run reads are present but *not enabled*. The run then stops with *"Control 0x… is not both
   visible and enabled"*. Photoshop persists the active tool across restarts, so restarting is not
   enough — select another tool (the Move tool, `V`) once, with no document open, and let Photoshop
   exit cleanly so the choice is saved.
5. **Do not type into Photoshop before starting the run.** On this `zh-CN` workstation, typing
   engages the Simplified-Chinese IME, which creates a visible `CiceroUIWndFrame` window owned by
   the Photoshop process. The launchability check counts any titled owned window as a dialog and
   fails with *"A dialog owned by Photoshop is blocking its window"* when no Photoshop dialog
   exists. If it happens, restart Photoshop and send it no keystrokes.

The surest way to reach state 2 is to start from nothing: close Photoshop, Meitu and PrintFlow
entirely, then launch Meitu and let it settle on its start page, then launch Photoshop and leave it
on its start screen without opening anything.

Items 4 and 5 are recorded from the 10 September closure attempt, where each cost a full acceptance
run before it was identified. Both are environment conditions, not Product defects; nothing was
changed in the Product to accommodate either.

**Why this is stricter than the check requires.** `PhotoshopSafeStartingState` will still *tolerate*
a saved pre-existing document — it fails only on an unsaved one or an unknown dialog, and a
tolerated document is checked for the same identity afterwards. This precondition is deliberately
tighter than that. Every observed pass of the full live phase has had a document-free Photoshop; the
9 September runs that had another person's document open failed
`PhotoshopTestImageRoundTrip` during probe cleanup.

That question is now settled. On 10 September, with a document-free Photoshop and items 4 and 5
above satisfied, `PhotoshopTestImageRoundTrip` **passed**, `DeleteProbe` executed, and the probe file
and its scratch directory were both removed — verified independently after the run. **The
9 September lock did not reproduce.** It is an environmental-state finding, not a Product defect, and
nothing in the Product was changed to accommodate it. See the SCRUM-11065 completion report §11.3 and
its 10 September closure delta (E2).

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

It re-verifies that all seven categories appear in a manifest under `-StandardRegressionSetPath`,
and then **validates the whole proposed attestation before it writes anything**:

- the run result carries an evidence binding of a version the script knows;
- every fact the run recorded matches this installation — product version, workstation, preset id,
  version and digest, Windows build, the accepted Meitu and Photoshop digests, and adapter mode;
- the four PrintFlow assemblies the run attested are byte-for-byte the ones installed here;
- the manifests the run read are among the manifests under `-StandardRegressionSetPath`;
- the seven categories each appear exactly once, and the summary agrees with the cases underneath
  it — a `Passed` summary over an undecided visual check is a contradiction, not a pass.

**Refusal and revocation are different, and the exit code tells you which happened.**

| What you did | Exit | What is on disk afterwards |
|---|---|---|
| Supplied a valid, matching, passing run | 0 | A new record. Production may resume. |
| Supplied no `-StandardRegressionSetResult` | 2 | A record saying `NotAvailable`. This is the deliberate way to close Production. |
| Supplied a validly bound run that did not pass | 2 | A record saying `Failed`, honestly. |
| Supplied a run that cannot be bound to this installation | 3 | **Nothing written.** Any record you already had is untouched. |
| Supplied a path that does not exist (a typo in either parameter) | 3 | **Nothing written.** Any record you already had is untouched. |

The last two rows are why pointing this script at the wrong file is now safe. A result that cannot be
bound says nothing about this installation — not that it passed, and not that it failed — so
recording either would be an invention. Before PF-AUDIT-R1 the first of those cases recorded a pass,
and the second silently recorded `NotAvailable` over whatever record you already had, which is
indistinguishable from deliberately closing Production.

**So a mistyped path and a deliberate revocation are no longer the same act.** Omitting
`-StandardRegressionSetResult` closes Production on purpose and exits 2. Mistyping it is refused and
exits 3, changing nothing.

A run whose qualitative checks were concluded by a **synthetic** review — one recorded by a test of
this protocol rather than by a person — is also refused. Synthetic decisions stay synthetic and
cannot open Production.

Never hand-edit `Revalidation\production-revalidation.json`.

**Records written before PF-AUDIT-R1 are schema 1.** They stay readable as history, and PrintFlow
blocks on them with a message saying so. They are not upgraded in place and no run is re-attributed
to them: the installation simply owes a run.

### 7.5 Conditions for Production to resume

All of the following, together:

1. Layer 1 passes — seven categories, no hash drift, no `PENDING`.
2. The live environment checks all pass.
3. All seven cases pass in one run, with every recorded visual check decided by a named reviewer.
4. `Set-PrintFlowProductionRevalidation.ps1` writes a record binding this PrintFlow version, **the
   PrintFlow assembly digests actually installed**, this preset id/version/digest, this Windows
   build and the accepted Meitu and Photoshop digests, with `standardRegressionSet.status = Passed`
   and the run and invocation identity it passed in.
5. PrintFlow's own verification, asked independently, reports `ProductionRevalidation` as passing.

**This is enforced, not merely documented.** There is no flag that turns "the set did not pass"
into "the set passed", and PrintFlow cannot write its own record — there is no writer for it
anywhere in the solution, and an architecture test asserts so.

### 7.6 The order these steps go in

Getting this order wrong is not a formality — the steps have a real dependency on each other, and
two of them are commonly attempted in the wrong place.

```text
controlled standard-set bootstrap
        |
        v
one complete standard regression set run          <- the only thing that produces evidence
        |
        v
actual operator reviews of the qualitative checks  <- a person looks; nothing automates this
        |
        v
evidence-bound revalidation record                 <- Set-PrintFlowProductionRevalidation.ps1
        |
        v
normal App live checks                             <- Settings -> Environment Check
        |
        v
normal App end-to-end work
```

**Why the standard set comes before the normal application's live checks.** The normal application
refuses Production until a revalidation record exists, and the record cannot exist until the set has
run. The set's run resolves that circle once, deliberately, through the bootstrap in §7.3 — a run
that suppresses the revalidation check *and only that check*, and only when it is the sole thing
blocking, and records in its own result whether it needed to. Trying instead to run the normal
application's live checks first, or to do production work before the first revalidation, does not
work and is not a sequence anybody should reconstruct.

**Bootstrap, ordinary production use, and human review are three different authorities.** The
bootstrap exists so a first run is possible; it is not a way to run anything else. Ordinary
production use requires the record. Human review is the only thing that can conclude a qualitative
check, and no flag substitutes for it.

**A synthetic test of this protocol is not a standard-set acceptance.** The tests in
`RegressionEvidenceIntegrityTests` drive these scripts against a synthetic set, a synthetic
installation and a stand-in test host. They prove the evidence chain refuses what it should and
accepts what it should. They open no application, exercise no real image, and produce no acceptance
of anything. Only a real run on the fixed workstation, reviewed by a person, is that.

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
