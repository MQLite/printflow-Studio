# A2 recovery continuation — 16 September 2026

Status: offline verification complete; normal-App live result pending operator UI action.
This report appends current evidence without replacing the historical failed attempt.

## Observed state and bounded diagnosis

Photoshop PID 1488 has creation time 2026-09-15 11:03:56 local and executable
`D:\Adobe Photoshop CC 2019\Photoshop.exe`, consistent with the earlier accepted process.
Computer Use initially returned a minimized window naming `Choppers.tif`; accessibility capture
refused because it was minimized. Supported activation, window refresh and accessibility read
then returned `Adobe Photoshop CC 2019` with no document surface. This corroborates the
operator's empty-screen observation but does not constitute a full document census. No Close,
Escape or Save As input was sent. No old probe was deleted. Closure cause remains unknown.

Source and original failure agree: the round trip reached CloseGuard; the close helper invoked
ProbeDocumentIdentityAsync, requesting SaveAsProbe again before CloseActiveDocument. The owned
Save As window did not appear within 20 seconds. The previous identity probe had returned only
after CancelDialogAsync/AwaitDialogClosedAsync reported its surface gone and host enabled.
The source does not establish why the subsequent request produced no matching window. There is
no evidence that a Close request occurred. The existing filename-field settling fix is intact.
No speculative change to guards, timeouts or input dispatch has been made.

## Verification and preserved identity

The original candidate was launched through supported Computer Use and read on its normal Home
screen; no interrupted or customer operation was selected. All 29 primary evidence hashes,
four candidate Product assemblies, and 182 retained harness files match their recorded values.
A1 remains Passed for the original pair. Preset 1.18.0 and the active published record are
unchanged. There was no writer invocation, replacement pair, A1 rerun, or Product source edit.

Targeted command (installed SDK, no live smoke opt-in):

```powershell
& 'C:\Users\admin\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --no-restore --filter "FullyQualifiedName~GuardedPhotoshopUiDriverTests|FullyQualifiedName~ProductionLiveWorkstationVerifierTests|FullyQualifiedName~EnvironmentReadinessScreenTests" --verbosity quiet
```

Result: 88 passed, 0 failed, 0 skipped. Initial unqualified `dotnet` resolved to the system SDK 8
and failed SDK selection before testing; the installed per-user SDK resolved this without install
or repository configuration changes. No full suite is justified by documentation-only changes.

Canonical lease read using a read-only SQLite connection at 2026-09-16T04:44:15.953Z found the
resource row with all ownership fields null. No lease was acquired, fabricated or evicted here.
The normal Product live action must acquire its real lease when it runs.

Local raw evidence: `artifacts/pf-accept-a2/recovery-window-observations.json`,
`recovery-processes.json`, `recovery-preservation.json`, `recovery-harness-preservation.json`,
`recovery-lease-observation.json`, and `recovery-normal-app-latest.txt`.

## Pending ordinary action and honest gate result

The permitted click on Home.ShowEnvironment failed with `coordinate input geometry is unavailable`.
The operator was asked to open **生产就绪状态**, click **运行实时应用检查** once and report completion.
This is a tooling limitation, not a proved Product defect. Do not substitute an injection script
or test host for the normal gate. Reuse the already received exclusive-window confirmation.

Current fresh gate result: NOT RUN in the newly launched normal candidate. Last completed normal
gate remains the historical failed round trip; no current readiness success is claimed. Probe
absence still needs a complete successful Product document read. Historical A1 publication is
preserved and does not itself grant current admission.

An ordinary recheck button exists and requires no technical operator instructions. Whether that
action completes this recovery is still unverified. No Product recovery correction or integrated
operator-friendly recovery capability is claimed. Continue after the narrow operator action;
if the failure persists, diagnose its specific current condition and apply the authorized repair
branch rather than repeating identical attempts. A3 remains out of scope.
