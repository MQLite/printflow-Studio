# Prepared local native-host trial tools

These tools target one current-user browser binding at a time: `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.printflow.ai_design` for the Chrome trial, or `HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.printflow.ai_design` for Edge. They also own three registration files in `%LOCALAPPDATA%\PrintFlowStudio\AiDesignBridge`. They do not install services, startup entries or scheduled tasks. They leave requests, attachments and downloaded files intact.

Ordinary registration leaves Work generation unavailable. See the opt-in Chrome trial below for the isolated handshake and Studio submission flow.

Build the Release native host first. When Chrome control is available, load `extensions/printflow-work` as an unpacked extension and obtain its actual 32-character ID from Chrome. Use that ID, the absolute path to the built `PrintFlow.NativeHost.exe`, the intended Workspace Root and an existing managed download directory. Do not use the synthetic ID in tests for a real trial.

Preview the exact manifest/configuration without writes:

```powershell
./build/ai-design/Register-NativeHost.ps1 -Browser Chrome -ExtensionId '<actual ID>' -HostPath '<absolute exe path>' -WorkspaceRoot '<absolute workspace>' -DownloadRoot '<absolute download directory>' -ValidateOnly
```

Registration uses the same arguments without `-ValidateOnly`; `-WhatIf` is also supported. The preview includes the manifest, local configuration and ownership record with its selected browser and registry key. Omit `-Browser` to target Edge for existing callers. Click the extension action to connect the persistent native port. Stop active requests and disconnect the extension before changing roots or rolling back. Existing registration or bindings are refused; explicitly roll back the matching owned registration before another registration.

Preview ownership checks with `./build/ai-design/Unregister-NativeHost.ps1 -ValidateOnly`. Explicit rollback uses the same command without `-ValidateOnly`. Rollback selects the registry key from the saved ownership record; no browser argument is needed. It refuses modified manifest/configuration, inconsistent browser identity or a changed binding, and deletes only the matching key and its three registration files. Idle lock/status files may remain; they establish no channel readiness.

Record the real trial separately: actual extension ID/origin, pinned version/root, Hello status, unavailable Work reason, matching rollback, absence of the exact registry binding, and absence of new startup/service entries. Current local protocol and preview tests do not substitute for this browser evidence.

## Studio cold-start Work submission trial

The local Chrome 157 registration, real native handshake and rollback were verified in an isolated profile on 2026-11-09. The trial build has a separate Studio action named **自动生图 / Generate image**. It opens the registered Chrome profile, waits for the native handshake, then stages the saved Workbench prompt in a new tab. The profile must be signed in. If the new tab has an empty Chat composer, the bridge switches it to Work and verifies that Work is active before entering the prompt. When the prompt references images, the bridge sends the verified snapshots one at a time and waits for each ready file chip before the host permits one submit.

For the opted-in trial only, use the actual unpacked extension ID and the profile-directory basename shown by Chrome (for example, `Default` or `Profile 2`). Use the same isolated Workspace Root as the running Studio build:

```powershell
./build/ai-design/Register-NativeHost.ps1 -Browser Chrome -ExtensionId '<actual ID>' -HostPath '<absolute trial native host exe>' -WorkspaceRoot '<absolute trial workspace>' -DownloadRoot '<existing isolated directory>' -EnableTextSubmissionTrial -ChromeProfileDirectory '<actual profile directory>' -ValidateOnly
```

After successful preview, use the same command without `-ValidateOnly` to register. Load or reload the corresponding unpacked extension before exiting Chrome. For an attachment trial, open a saved Studio draft with a synthetic reference image. Verify that Chrome has exited and click **自动生图** once. Observe the ready file chip, one exact prompt in a new Work conversation, and the submitted status in Studio. Automatic final PDF return requires the receiver setup below and a matching native host build.

The ordinary registration keeps the Work contract unverified. Trial mode uses a distinct contract hash and fixed extension launch page. Remove the trial's matching registration with the documented rollback after Chrome has exited.

See the [Studio final PDF return trial](../../docs/codex/printflow-ai-design-backlog/WORK_RESULT_TRIAL.md) for local setup and acceptance.
