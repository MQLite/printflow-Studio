# SCRUM-11153 technical results

Code candidate `7b37f24175df5d6efd530683fc52876c6446750a` on `master`; baseline `1f9a93dfb4f3b90b6357b86a5fbb03dfbd75b4ec`. User-authorized bounded implementation only. No production startup, migration, shared desktop or deployment.

## Implementation

Recent's bounded list query projects the current step and an actual running current-step attempt. A second bounded metadata query derives historical Delivered counts only for an exact eligible approved result, including session and revision/output ID, hash, length, latest approval and valid source lineage. TIFF siblings remain distinct. SQL history failure yields unknown. The row applies terminal/handoff precedence, current step status and a separate qualified history line; existing rows reword on language change. The layout wraps name, metadata, status and history, and gives status an automation identity. A refresh generation prevents an older response from replacing a newer row set.

Recovery membership and correction-bound return, Resume/Details, Remove and Abandon stay on their existing commands. No sorting, filtering, bulk actions, file check, automatic processing, schema change or new status store was added.

## Verification

| Evidence | Result |
|---|---|
| `dotnet build tests/PrintFlow.Tests/PrintFlow.Tests.csproj --no-restore -v minimal` | PASS, 0 warnings, 0 errors |
| Safe affected UI/resource test filter: `RecentProcessingRecordTests|HomeRecentAccessibilityTests|HomeReadinessSummaryTests|HomeReadinessSignalCloseoutTests|FinalSaveUiTests|AddAnotherSizeTests|LocalisationResourceTests` | PASS 197/197, final rerun |
| Delivery/recovery/architecture filter: `ApprovedArtifactDeliveryServiceTests|DeliveryRepositoryTests|DeliverySchemaTests|RecoverySurfaceTests|FailureGuidanceUiTests|MaximumBoundsBoundaryTests|WorkstationVerificationBoundaryTests` | PASS 162/162 |
| Opt-in synthetic screenshot/focused set, including correction/recovery | PASS 20/20; later two small long-name capture cases PASS 2/2 |
| Final raw → snapshot → CSV fidelity oracle | PASS: 17 issues, 51 string labels, 23 columns, 26 Blocks, exact timestamps |
| Export integrity regression | PASS 18/18 cases |
| Staged code diff check; public docs whitespace/privacy scan | PASS for source and prose; exact Jira CSV retains Jira-authored Markdown trailing spaces and passed its fidelity oracle. Code commit contained 12 reviewed code/test paths. |

The test groups overlap and are not an additive total. Focused behavioral coverage includes active/current vs stale attempts, terminal/handoff precedence, exact save/no-save/unknown history, invalidated source, TIFF sibling size, both languages and existing navigation/record management. A failed initial behavior test was observed before implementation for review status and save history, with further RED before the source-validity and language fixes.

Synthetic WPF captures use GUID-owned fixtures and the existing off-screen renderer at 96 DPI. Inspected: mixed en/zh-CN at 1920×1040; mixed expanded en/zh-CN at 1000×700; correction/recovery en/zh-CN at 1920×1040; and final long-name en/zh-CN at 1000×700. The reviewer inspected ten representative final images plus the two long-name captures. Long name, status, metadata, Recovery action and row controls were readable in those shown images. The small viewport uses the existing list scrollbar and retains usable list height.

The Jira-named `HomeAndWorkflowSelectionTests` class has excluded startup/desktop paths; those original paths were NOT RUN. Required Recent assertions were covered through isolated Home/service tests. Native window/UIA, `ApplicationStartup`, broad `ProductionComposition`, real picker/Explorer, non-96-DPI and the known hanging recovery test were NOT RUN/NOT PASS. Physical keyboard/mouse, novice and final copy acceptance remain open.

## Publication and lifecycle

The code commit was pushed by normal fast-forward to `origin/master`; local HEAD, fetched remote, `ls-remote` and authenticated GitHub commit readback all agree on the exact SHA and 12 paths. Jira 11153 alone moved To Do → In Progress → In Review; comment `10188` contains one marker. Final authenticated readback and CSV passed the independent fidelity oracle. Owner acceptance remains open; `Done` was not set. The following public-safe documentation commit and local-only audit/packet are recorded separately.
