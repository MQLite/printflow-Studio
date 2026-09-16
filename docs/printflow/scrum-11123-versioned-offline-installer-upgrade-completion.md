# SCRUM-11123 — Versioned Offline Installer, Upgrade and Rollback: Completion Report

**16 September 2026, scoped A2 update:** The supported writer published the completed A1 v3
revalidation for the original candidate, and that candidate's ordinary Product check reports
ProductionRevalidation Passed. Fresh live verification failed PhotoshopTestImageRoundTrip at
CloseGuard; production admission is still closed. See the [A2 clause reassessment](../remediation/PF-ACCEPT-A2/ASSESSMENT.md)
and [exact handoff](../remediation/PF-ACCEPT-A2/HANDOFF.md). No installer, install/upgrade/rollback,
A3 or wider release evidence was added; the earlier reports below remain historical.

**Date:** 2026-09-09
**Repository:** `D:\Repositories\printflow-Studio`, branch `master`, local commits only.
**Starting Git state:** clean `master` at `d5eb3c2` ("feat: add explicit diagnostic package export").

---

## 1. Requirement authority

The exact rows were read from `C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv`
before any Product or packaging edit.

### 1.1 Resolving the SCRUM key to the CSV

The CSV keys work items by bare number (`11000`–`11714`), not by `SCRUM-` key. The mapping is
ordinal: **SCRUM-id = 11060 + the row's zero-based position**, which the previous completion report
for SCRUM-11122 (`docs/printflow/scrum-11122-diagnostic-package-export-completion.md`) already uses
and which reproduces exactly here:

| SCRUM key | CSV work item | Row ordinal | Summary |
|---|---|---|---|
| SCRUM-11123 | `11608` | 62 | Build a Versioned Offline Installer and Upgrade Procedure |
| SCRUM-11115 | `11600` | 55 | Complete Operator UX, Localisation, Diagnostics and Offline Packaging |
| SCRUM-11065 | `11005` | 5 | Build the Standard Local Regression Image Set |

### 1.2 SCRUM-11123 (`11608`) — exact acceptance criteria

> Create a repeatable versioned offline installer for the validated workstation with no automatic
> application update mechanism. Document installation, configuration and rollback expectations.
> Any PrintFlow, Windows, Meitu or Photoshop upgrade must require rerunning the standard test set
> before production use rather than silently changing the supported environment.

Parent `11600`. 5 points. Labels: `printflow, mvp, installer, offline, versioning, upgrade, regression`.

The wording matches the task prompt's expectation verbatim.

### 1.3 SCRUM-11115 (`11600`) — parent Epic

> Complete the production-facing PrintFlow Studio experience with the confirmed Home/Drop,
> workflow, review, dimensions, TIFF review, Recent Processing, Settings/Environment Check and
> Error Details surfaces; simplified Chinese default and English switchable without restart;
> local-only logs and screenshots; explicit diagnostic-package export; and a repeatable versioned
> offline installer with no automatic updates. Operator-facing UI must use practical production
> terminology and hide internal implementation terms such as Revision, Session and Adapter while
> preserving stable internal English states and error codes.

### 1.4 SCRUM-11065 (`11005`) — the prerequisite

> Create the fixed local regression set required by the confirmed MVP design, including at least a
> normal JPG portrait, complex background with fine hair, transparent PNG, complete customer
> design, PSD with a compatible composite preview, single-page PDF and a reference production
> TIFF. Record the expected processing path and relevant expected properties for every file. Keep
> all customer-like test data local and suitable for repeatable automated, workstation and upgrade
> regression testing.

---

## 2. SCRUM-11065 prerequisite assessment — NOT IMPLEMENTED

Determined by direct inspection before any decision about the final verdict.

**What exists.** `D:\PrintFlowStudio\TestData\v1\`:

```
inputs\FIX-CUSTOMER-DESIGN-001.jpeg      216,162 bytes, 1024x1536, 24-bit RGB, no alpha
expected\FIX-CUSTOMER-DESIGN-001_HD.png       13,831,390 bytes
expected\FIX-CUSTOMER-DESIGN-001_CUTOUT.png    3,895,181 bytes
manifests\FIX-CUSTOMER-DESIGN-001.json
```

The manifest declares `"category": "COMPLETE_CUSTOMER_DESIGN"` and `"finalTiff": "PENDING"`.

**What the AC requires, and what is present:**

| Required category | Present? |
|---|---|
| Normal JPG portrait | **No** |
| Complex background with fine hair | **No** |
| Transparent PNG | **No** |
| Complete customer design | Yes — one file |
| PSD with a compatible composite preview | **No** |
| Single-page PDF | **No** |
| Reference production TIFF | **No** — the one manifest records `finalTiff: PENDING` |

**One of seven.** Machine-confirmed: `Set-PrintFlowProductionRevalidation.ps1` run against the real
`D:\PrintFlowStudio\TestData` reports exactly six missing categories
(`evidence/scrum-11123/revalidation-record-notavailable.json`).

**Repository fixtures are not this set.** `tests\PrintFlow.Tests\Fixtures\SyntheticImages.cs`,
`PdfFixtures.cs`, `SpotChannelPsdFixtures.cs`, `ProductionTiffFixture.cs` and the rest generate
images in code for specific unit assertions. They never reach Meitu or Photoshop. A regression set
whose purpose is to prove the external applications still behave after an upgrade cannot be built
from images those applications never see. Counting them would be exactly the substitution the task
brief forbids.

**Corroboration.** `docs/printflow/original-jira-functional-coverage-reaudit.md` line 115 already
records SCRUM-11065 as `NOT_IMPLEMENTED` with the same finding, and notes it blocks SCRUM-11136.

**Decision.** SCRUM-11065 was not built here — it is not this task's scope and nothing in
SCRUM-11123's wording makes building a seven-category customer-like image set unavoidable. Instead
the standard-test-set requirement is enforced structurally and **fails closed**: see §8.

---

## 3. Pre-change matrix

| SCRUM-11123 AC clause | State before this task | Existing authority | Satisfied? | Gap closed here |
|---|---|---|---|---|
| Repeatable installer | None. `Directory.Build.props` said "it ships as an offline installer"; nothing built one. | — | No | WiX 6 MSI project + one scripted build |
| Versioned | `<Version>0.1.0</Version>` stamped assemblies only | `Directory.Build.props` | Partly | `Version.props` as the single source, flowing into assemblies, MSI ProductVersion and artefact name |
| Offline | No installer, so vacuously nothing downloaded | — | No | Self-contained win-x64 payload; no bootstrapper, prerequisite or custom action |
| No automatic update mechanism | True by absence: no network client anywhere in the product | Architecture boundaries | Yes, structurally | Made explicit and guarded by `InstallerBoundaryTests` |
| Installation expectations documented | Not documented | — | No | Runbook §1–§2 |
| Configuration expectations documented | `appsettings.local.json` override existed and was documented only in a csproj comment | `PrintFlowConfiguration` | Partly | Runbook §2.3 configuration classification table |
| Rollback expectations documented | Not documented; forward-only migrations made naive rollback unsafe and unsaid | `MigrationRunner`, 17 forward migrations | No | Runbook §4, checkpoint and restore tooling |
| PrintFlow upgrade → revalidation | **No.** A new binary over a verified workstation inherited approval immediately | `VerifiedEnvironmentGate` | No | New `ProductionRevalidation` check |
| Windows upgrade → revalidation | Partly. `OperatingSystem` check fails on a changed build | `ProductionWorkstationVerifier` | Partly | Also bound into the revalidation record |
| Meitu upgrade → revalidation | Partly. Digest check fails — but passes again the moment a new preset accepts the new binary | `MeituExecutable` check | Partly | Record binds the tested digest, closing that hole |
| Photoshop upgrade → revalidation | Same gap | `PhotoshopExecutable` check | Partly | Same |
| Standard test set required before production | **No.** Nothing anywhere referenced it | — | No | `StandardRegressionSetStatus`, blocking on anything but `Passed` |

---

## 4. Installer technology and rationale

**Chosen: WiX Toolset 6.0.2, one per-machine MSI, no bootstrapper.**

Considered and rejected:

| Option | Why not |
|---|---|
| `dotnet publish` + ZIP + README | The task brief rules it out unless the AC permits it, and the AC does not: "repeatable versioned offline installer" with documented uninstall semantics is not a ZIP. |
| Inno Setup | Not installed on this machine, and installing it would put a machine-level tool dependency outside the repository between the source and the artefact. |
| WiX + Burn bootstrapper `.exe` | A bootstrapper exists to chain prerequisites and, usually, to fetch them. The payload is self-contained and has no prerequisites, so a bundle would add a component with nothing to do — and §8 of the brief forbids a custom bootstrapper ecosystem. |
| MSIX | Requires signing to install. §36 forbids introducing signing infrastructure. |

WiX arrives as a NuGet SDK (`WixToolset.Sdk/6.0.2`), so the toolchain is pinned in the repository
and restored like any other dependency. Nothing had to be installed on the workstation.

The `.msi` was chosen over an `.exe` because Windows Installer already provides, as declarative
data rather than as script: transactional install with rollback, major-upgrade sequencing,
Add/Remove Programs registration, and an uninstall that removes exactly what was installed and
nothing else. That last property is what makes §5 of this report a structural fact.

**Isolation.** `installer/` carries an empty `Directory.Build.props` and `Directory.Packages.props`
so the packaging project does not inherit the repository's C# settings (`TreatWarningsAsErrors`,
central package management, NuGet lock files), which the WiX SDK does not satisfy. The project is
deliberately **not** in `PrintFlowStudio.sln`: `dotnet build PrintFlowStudio.sln` and the Product
test suite are unaffected by the packaging toolchain.

---

## 5. Publish model — self-contained win-x64

`<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>` in `PrintFlow.App.csproj`; the build publishes
`-c Release -r win-x64 --self-contained true`.

| | Self-contained (chosen) | Framework-dependent |
|---|---|---|
| Runtime at install time | In the payload; no network, ever | Needs .NET 10 Desktop Runtime already present, or a bootstrapper that fetches it |
| Payload | 416 files, ~171 MB publish, **57.4 MB** compressed MSI | ~20 files, a few MB |
| Runtime patch | A new PrintFlow version → the revalidation gate → the standard test set | Changes underneath the validated environment with no PrintFlow event at all |

The second row is the size cost and it is real. The third row is why it was paid: the requirement
is that the supported environment must not change silently. A machine-wide .NET servicing update
would change the runtime the validated workstation runs on, and nothing in PrintFlow would notice.
Pinning the runtime into the payload converts that into a version change, which goes through the
same gate as every other upgrade. 57 MB on a fixed offline workstation is the cheaper half of that
trade.

The workstation does currently have .NET 10.0.11 installed machine-wide — so framework-dependent
would work today. That is precisely the fragility being removed.

---

## 6. Version authority

**`Version.props` at the repository root holds `<PrintFlowVersion>`, and nothing else states a
version.**

| Consumer | How it reads the version |
|---|---|
| Every assembly | Root `Directory.Build.props` imports `Version.props` and maps it to `<Version>` |
| Diagnostic package application version | Reads the running assembly (SCRUM-11122, unchanged) |
| Production revalidation check | `ProductionRevalidationEvaluator.RunningProductVersion` reads the Infrastructure assembly |
| MSI `ProductVersion` | The `.wixproj` imports `Version.props` |
| Artefact file name | `<OutputName>PrintFlowStudio-$(PrintFlowVersion)-win-x64</OutputName>` |
| Checkpoint manifest, revalidation record | Read from the installed `PrintFlow.App.exe` version resource |

`InstallerBoundaryTests` fails the build if `PrintFlowVersion` is declared more than once, if any
product assembly disagrees with it, or if the declared version appears as a literal in the
`.wixproj`, `Package.wxs` or the build script.

Current version: **0.1.0**, unchanged. Artefact: `PrintFlowStudio-0.1.0-win-x64.msi` — never a bare
`setup.msi`.

---

## 7. Payload boundary — default deny

`installer/payload-policy.json` is the boundary, and it is data rather than script.

- **`allow`** — the only paths that may ship: the four product assemblies, the executable, the two
  runtime `.json` descriptors, `appsettings.json`, `*.dll` (the self-contained runtime and WPF),
  one-level satellite resources, and `createdump.exe`. No pattern matches `.pdb`, `.cs`, `.db`,
  `.pfx`, `.psd`, `.tif`, `.zip`, or any path more than one directory deep.
- **`denyAlways`** — test assemblies, source, project files, any SQLite database, signing and
  secret material, customer and production imagery, `appsettings.local.json`, `.git` content,
  `packages.lock.json`. A match here **fails the packaging build**; it is not silently dropped.
  Symbols are the one stated exception: dropped from the payload and archived to `symbols\` beside
  the `.msi` as a release record.
- **`requiredFiles`** — eight files that must be present or the build fails.

Result on the real build: **416 staged, 4 dropped (all symbols), 0 denied, 0 outside the
allowlist.** Verified twice — once by the stager against the publish output, once by the smoke
against the extracted MSI.

---

## 8. Production revalidation — the fail-closed gate

### 8.1 The mechanism

A new `WorkstationVerificationCheck.ProductionRevalidation`, evaluated dynamically by
`ProductionWorkstationVerifier` and consumed by the existing `VerifiedEnvironmentGate`. No new
environment system: one member added to an existing closed enum, one entry in
`EvaluateCurrentState`.

It reads `D:\PrintFlowStudio\Revalidation\production-revalidation.json` and requires **all** of:

| Bound fact | Blocks when |
|---|---|
| `schemaVersion` | not 1 |
| `productVersion` | ≠ the running PrintFlow version → **PrintFlow upgraded** |
| `presetId` / `presetVersion` | ≠ the configured preset |
| `presetSha256` | ≠ the configured expected digest → preset re-issued under the same identity |
| `operatingSystemBuild` | ≠ the observed Windows build → **Windows upgraded** |
| `meituSha256` | ≠ the accepted Meitu digest → **Meitu upgraded** |
| `photoshopSha256` | ≠ the accepted Photoshop digest → **Photoshop upgraded** |
| `environmentReadinessPassed` | false |
| `standardRegressionSet.status` | anything but `Passed` — including `NotAvailable` and `Unknown` |

An absent, unreadable or malformed record is "no record", which blocks.

### 8.2 Why the external-application digests are bound here as well

`MeituExecutable` and `PhotoshopExecutable` already compare the binary on disk against the
*current* preset. Updating the preset to accept a newer Photoshop makes them pass again — that is
what a preset is for. But accepting a new external application version and *having tested against
it* are different acts, and only the second may reopen Production. Binding the tested digests into
the revalidation record closes that gap. Two tests assert exactly this: the executable check passes
while Production stays closed.

### 8.3 The application cannot approve itself

There is `IProductionRevalidationReader` and no writer, anywhere. The record is written only by
`tools\installer\Set-PrintFlowProductionRevalidation.ps1`, and an architecture test asserts that no
product source file which knows about the record performs a file write.

### 8.4 The honest blocker

`Set-PrintFlowProductionRevalidation.ps1` verifies that the set named by
`-StandardRegressionSetPath` actually contains all seven SCRUM-11065 categories before it will
record a pass, and requires a separate run-result file. Because the set does not exist, it records
`NotAvailable`, prints the six missing categories, exits 2, and says "Production remains CLOSED".
There is no switch that turns absence into a pass.

Run against the real `D:\PrintFlowStudio\TestData`, it reported six missing categories.

**Installer mechanics are complete. Production reactivation after any upgrade is blocked until the
standard regression set required by SCRUM-11065 exists and passes.**

### 8.5 A note on current behaviour

This workstation has no revalidation record today, so `Environment Check` will now report
"Revalidation after upgrade" as failed and Production adapters will be refused. That is the correct
reading of the requirement — the standard test set has never been run, because it has never
existed — and it is what §2 of the task brief instructs. `Fake` mode is unaffected, so the
application remains fully usable for troubleshooting the very workstation that is failing.

---

## 9. Configuration and data preservation

| Class | Example | Upgrade | Uninstall |
|---|---|---|---|
| Shipped immutable/default | `appsettings.json` | Replaced | Removed |
| Operator preference | Settings rows in SQLite; `Logging:RetentionDays` | Preserved | Preserved |
| Validated production configuration | `appsettings.local.json` (workspace root, preset identity and digest, adapter mode) | **Preserved** — never installer-owned | **Preserved** |
| Runtime/generated state | database, sessions, evidence, revalidation record, checkpoints | Preserved (revalidation record invalidated by comparison, not deletion) | Preserved |

The installer authors files only under `%ProgramFiles%\PrintFlow Studio` and the Start Menu folder.
Because Windows Installer removes only what it installed, production data cannot be removed by an
upgrade or uninstall — proved from the package's own `Directory`, `Component`, `File` and
`RemoveFile` tables, not from a manual observation.

There is deliberately **no** "also remove my data" uninstall option. SCRUM-11123 does not ask for
one.

---

## 10. Migration and downgrade reality

`src/PrintFlow.Infrastructure/Sqlite/Migrations/` holds 17 forward-only migrations
(`0001_initial_schema` … `0017_recent_processing_record_removal`). There are no down-migrations.

A newer application migrates the database on startup, irreversibly. The previous executable would
then meet a schema it does not understand. **Reinstalling the previous version is therefore not a
rollback**, and this report does not claim it is. `MajorUpgrade AllowDowngrades="no"` makes the
installer refuse the shortcut, with an error message that points at the runbook.

A truthful rollback is: stop → uninstall → restore the pre-upgrade checkpoint → install the
previous version → restore configuration → Environment Readiness → standard regression set →
record the revalidation. Runbook §4.

### The orphaned-file problem, stated

Restoring an older metadata database does not delete work done after the checkpoint. Those files
stay on disk; the restored database has no rows for them. They will not appear in Recent
Processing and their sessions cannot be resumed. They are **not lost**. There is no automatic
reconciliation, and none is invented here. Runbook §4.4 tells the operator to enumerate the work
produced after the checkpoint and decide per job whether to hand the output on from the file system
or to redo it — and notes that a job already printed through Maintop exists physically regardless
of what the database says.

---

## 11. Checkpoint strategy

`tools\installer\New-PrintFlowCheckpoint.ps1` captures the minimum that makes rollback truthful:
the SQLite database (with `-wal`/`-shm`), `appsettings.json`, `appsettings.local.json`, the current
revalidation record, the installed product version (three-part and raw) and the preset identity.

It does **not** copy customer images, InputSnapshots, Revisions, approved PNGs, TIFFs or Evidence:
those already have authoritative storage that no upgrade or rollback removes, so duplicating
gigabytes of customer work would create a second place for it to leak from and protect nothing.

Safety properties:

- refuses to run while `PrintFlow.App` is running;
- one timestamped folder per checkpoint, and it refuses rather than overwrite;
- **"created" and "verified" are distinct**: every captured file is re-hashed from disk and
  compared against the manifest, and the script throws "created but did NOT verify. Do not upgrade
  against it" on any mismatch.

`Restore-PrintFlowCheckpoint.ps1` re-verifies the manifest before replacing anything, renames the
current database to `.replaced-<stamp>` rather than deleting it, deliberately does **not** restore
the revalidation record (a rollback installs a different version, which must close Production), and
prints the four remaining steps including the exact previous installer file name.

---

## 12. Offline proof

| Claim | How it was established |
|---|---|
| No .NET runtime is fetched at install time | The payload contains `System.Private.CoreLib.dll`, `PresentationFramework.dll`, `hostfxr.dll`; `PrintFlow.App.runtimeconfig.json` declares no `framework` — asserted by the smoke |
| No package downloads anything | The MSI declares no `CustomAction` table at all, so there is no code path in the package that could run a downloader |
| No prerequisite chaining | No bootstrapper, no bundle, no `PackageGroupRef` |
| No Meitu or Photoshop in the payload | 416-file payload listed in `build-manifest.json`; both remain external prerequisites |
| Build-time restore is irrelevant | NuGet restore happens on the build machine; the artefact embeds its whole payload |

---

## 13. No automatic updater — proof

One proportionate architecture boundary, `InstallerBoundaryTests`:

- **`Nothing_in_the_product_or_the_packaging_can_fetch_a_new_version`** — scans every `src/**/*.cs`
  and every file under `installer/`, `build/`, `tools/` for `HttpClient`, `WebClient`,
  `WebRequest`, `System.Net.Http`, `Invoke-WebRequest`, `Start-BitsTransfer`, `DownloadFile`,
  `DownloadString`, `schtasks`, `ScheduledTask`, `Register-ScheduledTask`, `CurrentVersion\Run`,
  `winget`, `choco install`. Zero offenders.
- **`The_package_installs_nothing_that_runs_by_itself`** — `Package.wxs` declares no
  `ServiceInstall`, `ServiceControl`, `CustomAction`, `ScheduledTask`, `RunOnce`, startup folder
  entry, or bundle.

Both scans read **declarations**, with comments stripped, so the guard measures what the files do
rather than what their commentary discusses.

---

## 14. No digital signatures

No Authenticode, no signing step, no certificate, no chain validation, no timestamp service, no
signature test. `No_signing_certificate_or_timestamp_infrastructure_is_introduced` asserts it
across all packaging sources, and the smoke asserts the MSI declares no `MsiPatchCertificate` or
`DigitalSignature` table.

Existing SHA-256 and preset-manifest integrity behaviour is untouched. That is a different
mechanism — content integrity against a hash the preset states — and it is unchanged by this task.
`payload-policy.json` lists `.pfx`, `.snk`, `.p12` and `.key` on the deny list; that is the
*exclusion* of key material, not the introduction of signing.

---

## 15. Tests and smokes

### 15.1 New tests

`tests/PrintFlow.Tests/Integration/Verification/ProductionRevalidationTests.cs` — 17 tests:
verified baseline; no record; malformed record; future schema; **PrintFlow upgrade**; **Windows
upgrade**; **Meitu upgrade the preset already accepts**; **Photoshop upgrade the preset already
accepts**; preset re-issued under the same identity; regression set `NotAvailable`; regression set
`Failed`; regression set omitted; readiness not passed; the gate refuses Production while allowing
Fake; recording and removing take effect without a restart; the record lives under the workspace;
the running version comes from the built assembly.

`tests/PrintFlow.Tests/Architecture/InstallerBoundaryTests.cs` — 14 tests: single version source;
every assembly carries it; the installer derives rather than restates it; the artefact name carries
version and architecture; self-contained win-x64 publish; policy denies tests/source/data/secrets;
policy admits the application and its runtime; a denied file fails the build; the package installs
nothing into the production workspace; the local override is never installer-owned; no updater; no
self-running component; no signing; the application can read but never write a revalidation record.

**Result: 31 passed, 0 failed, 0 skipped.**

### 15.2 Installer smokes

`build\installer\Invoke-InstallerSmoke.ps1`, evidence in
`evidence/scrum-11123/installer-smoke-report.json`:

| Check | Result |
|---|---|
| Payload boundary | **PASS** — 416 files, all allowed, 0 denied, all 8 required present |
| Offline installation | **PASS** — self-contained payload; no custom action, service or signing table |
| Fresh install | **PASS** — 0.1.0 into an isolated folder; synthetic workspace untouched |
| Upgrade N → N+1 | **PASS** — 0.1.0 → 0.1.1; workspace, database, `appsettings.local.json` and revalidation record all byte-identical |
| Uninstall preservation | **PASS** — all 16 installed locations under the application or Start Menu folder; every declared removal stays inside them |

### 15.3 Tooling smokes

- `New-PrintFlowCheckpoint.ps1` — created and **verified** a 4-file checkpoint against a synthetic
  installation (`evidence/scrum-11123/checkpoint-manifest.json`).
- `Restore-PrintFlowCheckpoint.ps1` — restored the database, kept the replaced one as
  `.replaced-<stamp>`, left a post-checkpoint production TIFF on disk, did not restore the
  revalidation record, and named `PrintFlowStudio-0.1.1-win-x64.msi` as the version to reinstall.
- `Set-PrintFlowProductionRevalidation.ps1` — against the real `D:\PrintFlowStudio\TestData`,
  reported six missing categories, wrote `standardRegressionSet.status: NotAvailable`, and exited 2
  with "Production remains CLOSED".

### 15.4 The one limit, stated plainly

**A real `msiexec /i` per-machine install was attempted and refused with error 1925** ("You do not
have sufficient privileges to install this product for all users") — this session is not elevated.
Log: `evidence/scrum-11123/msiexec-per-machine-install-refused.log`.

Elevating was declined deliberately: the only machine available to elevate on is the validated
production workstation, and installing PrintFlow into its `Program Files` to demonstrate that an
installer works would change the very environment the requirement exists to protect, and is outside
this task's "nothing is deployed" boundary.

What was used instead — administrative installation (`msiexec /a`), Windows Installer's own
supported way to lay a package's payload down without installing it — exercises the real payload,
the real directory layout and the real file versions. What it does **not** exercise is the
registration half: Add/Remove Programs entry, the `MajorUpgrade` sequencing actually running, and
file removal at uninstall. Those are covered instead by reading the package's own `Directory`,
`Component`, `File` and `RemoveFile` tables, which is arguably stronger evidence than one
observed run: it proves no path outside the two permitted roots can be touched at all, rather than
that none happened to be touched once.

**Not verified by execution on this machine:** an elevated `msiexec /i` fresh install, an elevated
`msiexec /i` major upgrade over an installed product, and an elevated `msiexec /x` uninstall.

---

## 16. Product full-suite decision

The scoped testing policy says the full suite is justified only if shared Product behaviour
changes. It did: this task adds a blocking check to the **readiness/production gate**, which is
named explicitly in that list. The full suite was therefore run rather than skipped.

Two pre-existing tests needed updating, both because of the new check and neither because of a
defect:

- `Every_check_declares_whether_it_is_immutable_or_dynamic` — `ProductionRevalidation` added to the
  expected dynamic set.
- `A_workspace_root_that_is_a_file_fails_as_its_own_condition` — now deletes the fixture workspace
  recursively, because the fixture writes a revalidation record into it.

`WorkstationVerificationFixture` now writes a matching revalidation record on construction, so every
test written before this task still describes "this is the accepted workstation and nothing has
changed" and keeps asserting what it was written to assert.

**Full suite result:** see §19.

---

## 17. SCRUM-11123 reassessment, clause by clause

| Clause | Verdict | Evidence |
|---|---|---|
| Repeatable installer | **Met** | One scripted entry point; two versions built from one tree in this session, both 0 warnings / 0 errors |
| Versioned | **Met** | `Version.props` single source; `PrintFlowStudio-0.1.0-win-x64.msi`; guarded by tests |
| Offline | **Met** | Self-contained payload; no custom action, bootstrapper or prerequisite; smoke check 2 |
| For the validated workstation | **Met** | Per-machine win-x64; installs nothing into the workspace; changes no external application |
| No automatic application update mechanism | **Met** | No updater, service, task, startup entry or network client anywhere; architecture guard |
| Installation expectations documented | **Met** | Runbook §1–§2 |
| Configuration expectations documented | **Met** | Runbook §2.3; classification table; `appsettings.local.json` never installer-owned |
| Rollback expectations documented | **Met** | Runbook §4, including forward-only migrations and the orphaned-file consequence |
| PrintFlow upgrade → rerun the standard test set | **Enforced; cannot be completed** | Gate closes on version mismatch; the standard set does not exist |
| Windows upgrade → rerun the standard test set | **Enforced; cannot be completed** | Same |
| Meitu upgrade → rerun the standard test set | **Enforced; cannot be completed** | Same |
| Photoshop upgrade → rerun the standard test set | **Enforced; cannot be completed** | Same |
| Rather than silently changing the supported environment | **Met** | Nothing adapts to a changed external application; §8.2 closes the preset-reissue hole |

### Verdict: SCRUM-11123 — PARTIAL

Every clause this task owns is implemented, tested and documented. The installer is real,
repeatable, versioned, offline, updater-free, and its install/upgrade/uninstall/rollback semantics
are explicit and enforced.

The clause **"must require rerunning the standard test set before production use"** is implemented
as a hard, fail-closed gate — but it is a gate that **cannot currently be passed**, because the
standard test set required by SCRUM-11065 does not exist. The requirement is enforced; it is not
satisfiable end-to-end on this workstation today.

Marking SCRUM-11123 FULL would assert that an upgraded workstation can be returned to Production by
rerunning the standard test set. It cannot. **PARTIAL, blocked on SCRUM-11065.**

---

## 18. Parent SCRUM-11115 reassessment

Reread in full after the SCRUM-11123 reassessment, clause by clause rather than by child count.

| Parent clause | Child | State |
|---|---|---|
| Home/Drop | SCRUM-11116 | Complete |
| Workflow, review, dimensions, TIFF review | 11400/11200 epics | Complete |
| Recent Processing with resume and record management | SCRUM-11117 | Complete |
| Settings / Environment Check | SCRUM-11118 | Complete |
| Error Details | SCRUM-11120 | Complete |
| Simplified Chinese default, English switchable without restart | SCRUM-11119 | Complete |
| Local-only logs and screenshots | SCRUM-11121 | Complete |
| Explicit diagnostic-package export | SCRUM-11122 | Complete |
| **Repeatable versioned offline installer with no automatic updates** | **SCRUM-11123** | **Installer complete; revalidation gate blocked on SCRUM-11065** |
| Production terminology; internal terms hidden | SCRUM-11116–11120 | Complete |
| Stable internal English states and error codes | Throughout | Complete |

The parent's own installer clause reads "a repeatable versioned offline installer with no automatic
updates" — and that clause, taken by itself, **is** now satisfied. The standard-test-set
requirement is SCRUM-11123's wording, not the parent's.

### Verdict: SCRUM-11115 — PARTIAL → **FULL**

Every clause the Epic itself states is implemented, including the installer clause in the parent's
own words. The residual blocker belongs to SCRUM-11123's stricter revalidation wording and to
SCRUM-11065, which is a child of Epic 11000 and not of this one.

Recorded honestly: the Epic is functionally complete, and one of its children carries a documented
dependency on a different Epic's unbuilt deliverable.

---

## 19. Build, tests and Git state

**Build:** `dotnet build PrintFlowStudio.sln -c Release` — **0 warnings, 0 errors**.

**Installer build:** `Build-Installer.ps1` for 0.1.0 and 0.1.1 — **0 warnings, 0 errors** each.

**Targeted tests:** `ProductionRevalidationTests` + `InstallerBoundaryTests` — 31 passed, 0 failed,
0 skipped. Verification-adjacent suite — 328 passed, 0 failed, 0 skipped.

**Full Product suite:** run (see §16 for why). Result recorded in
`evidence/scrum-11123/full-suite.log`: **11,676 passed, 0 failed, 0 skipped** — the accepted
11,645 baseline plus the 31 new tests added here.

**Repeatability, proved from the committed state:** `artifacts\installer` deleted, then
`Build-Installer.ps1` run from scratch — 416 staged, 4 symbols archived, 0 warnings, 0 errors, a
versioned MSI with `ProductVersion` 0.1.0 and a stable `UpgradeCode`. A solution restore, a
solution build and a clean packaging build all leave the working tree untouched.

**Git:** five local commits on `master` only, `d5eb3c2` → `d8b3e57`. No branch, worktree, alternate
clone or checkout; no amend, rebase, push or deploy. No `Co-Authored-By` or other AI attribution.

```
c71d419  Establish one product version source and a win-x64 publish contract
4e2602b  Close Production after an upgrade until the environment is revalidated
277f55e  Build a versioned offline installer with upgrade and rollback procedure
9a03261  Pin the win-x64 restore targets the release publish resolves
d8b3e57  Declare the publish RID across the whole shipped project graph
```

**Files added**

```
Version.props
installer/Directory.Build.props
installer/Directory.Packages.props
installer/payload-policy.json
installer/PrintFlowStudio.Installer/PrintFlowStudio.Installer.wixproj
installer/PrintFlowStudio.Installer/Package.wxs
build/installer/Build-Installer.ps1
build/installer/Invoke-InstallerSmoke.ps1
tools/installer/New-PrintFlowCheckpoint.ps1
tools/installer/Restore-PrintFlowCheckpoint.ps1
tools/installer/Set-PrintFlowProductionRevalidation.ps1
src/PrintFlow.Infrastructure/Verification/ProductionRevalidation.cs
tests/PrintFlow.Tests/Architecture/InstallerBoundaryTests.cs
tests/PrintFlow.Tests/Integration/Verification/ProductionRevalidationTests.cs
docs/printflow/installer-upgrade-rollback-runbook.md
docs/printflow/scrum-11123-versioned-offline-installer-upgrade-completion.md
```

**Files modified**

```
Directory.Build.props                                                 imports Version.props
src/PrintFlow.App/PrintFlow.App.csproj                                win-x64 RID
src/PrintFlow.Domain/PrintFlow.Domain.csproj                          win-x64 RID
src/PrintFlow.Workflow/PrintFlow.Workflow.csproj                      win-x64 RID
src/PrintFlow.Infrastructure/PrintFlow.Infrastructure.csproj          win-x64 RID (and the new check)
src/PrintFlow.Infrastructure/Verification/WorkstationVerificationVocabulary.cs   new check member
src/PrintFlow.Infrastructure/Verification/ProductionWorkstationVerifier.cs       wires the check
src/PrintFlow.App/Resources/Strings.resx, Strings.zh-CN.resx          two operator strings each
tests/PrintFlow.Tests/Fixtures/WorkstationVerificationFixture.cs      revalidation helpers
tests/PrintFlow.Tests/Integration/Verification/ProductionWorkstationVerifierTests.cs  two assertions
tests/PrintFlow.Tests/Integration/Verification/ProductionLiveWorkstationVerifierTests.cs  one call site
src/*/packages.lock.json                                              win-x64 restore targets (all four)
docs/printflow/original-jira-functional-coverage-reaudit.md           appended only
```

---

## Addendum — 9 September 2026: the SCRUM-11065 prerequisite

*Appended, not merged. Everything above was true on 9 September when it was written and is left
exactly as it was recorded.*

The report above states that this task's clause — *"Any PrintFlow, Windows, Meitu or Photoshop
upgrade must require rerunning the standard test set before production use"* — **is enforced but
cannot be satisfied, because the standard set (SCRUM-11065) does not exist**.

The first half of that is still true. The second half is no longer.

**What changed.** SCRUM-11065 was worked later the same day. All seven required categories now
exist under `D:\PrintFlowStudio\TestData\v1`, each with a manifest recording its expected
processing path, expected properties, provenance and a fixed SHA-256, and there is one repeatable
execution procedure — `tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1`. The category
scan inside `Set-PrintFlowProductionRevalidation.ps1`, which reported six missing categories when
this report was written, now reports none missing.

**What did not change.** No revalidation record was created, and this task's clause is still not
*satisfied* on this workstation:

- Two of the seven categories passed a controlled fixed-workstation run — the two whose recorded
  path needs no external application.
- The other five drive Meitu or Photoshop in the foreground, and the workstation was in continuous
  interactive use by another person for the duration of that work. The runner requires every
  blocking live environment check to pass before it drives an external application, and reported
  `Blocked` rather than proceeding.
- `Set-PrintFlowProductionRevalidation.ps1` was therefore **not** invoked.
  `Revalidation\production-revalidation.json` does not exist, was not hand-edited, and PrintFlow's
  verification still reports `ProductionRevalidation` as failing.

**SCRUM-11123 reassessment: PARTIAL, unchanged.** Reassessed independently rather than inferred
from SCRUM-11065's label. The blocker has narrowed — from "the standard set does not exist" to
"the standard set has not completed a run on this workstation" — but it has not been cleared, and
Production is still correctly closed.

**Nothing in this task's own gate was weakened to get there.** The revalidation check was not
disabled, removed from `VerifiedEnvironmentGate`, or answered with a fabricated record. The one
seam added is `ProductionWorkstationVerifier.ForStandardRegressionRun`, which *omits* the
self-referential check for the regression run only; it is `internal`, Infrastructure grants its
internals to `PrintFlow.Tests` alone, and an architecture test asserts the shipped application
cannot reach it. The full Product suite passed 11,712 / 11,712.

Detail, evidence and the full reassessment:
[SCRUM-11065 completion report](scrum-11065-standard-local-regression-set-completion.md).
Operator procedure, now defined:
[installation, upgrade and rollback runbook](installer-upgrade-rollback-runbook.md) §7.
