# Prepared local native-host trial tools

These tools target one current-user browser binding at a time: `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.printflow.ai_design` for the Chrome trial, or `HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.printflow.ai_design` for Edge. They also own three registration files in `%LOCALAPPDATA%\PrintFlowStudio\AiDesignBridge`. They do not install services, startup entries or scheduled tasks. They leave requests, attachments and downloaded files intact.

The actual clean-user Chrome registration → Hello → rollback trial is **NOT RUN**. The extension has no verified Work adapter; registration and a live native connection leave Work generation unavailable. Prompt creation and manual handoff remain usable.

Build the Release native host first. When Chrome control is available, load `extensions/printflow-work` as an unpacked extension and obtain its actual 32-character ID from Chrome. Use that ID, the absolute path to the built `PrintFlow.NativeHost.exe`, the intended Workspace Root and an existing managed download directory. Do not use the synthetic ID in tests for a real trial.

Preview the exact manifest/configuration without writes:

```powershell
./build/ai-design/Register-NativeHost.ps1 -Browser Chrome -ExtensionId '<actual ID>' -HostPath '<absolute exe path>' -WorkspaceRoot '<absolute workspace>' -DownloadRoot '<absolute download directory>' -ValidateOnly
```

Registration uses the same arguments without `-ValidateOnly`; `-WhatIf` is also supported. The preview includes the manifest, local configuration and ownership record with its selected browser and registry key. Omit `-Browser` to target Edge for existing callers. Click the extension action to connect the persistent native port. Stop active requests and disconnect the extension before changing roots or rolling back. Existing registration or bindings are refused; explicitly roll back the matching owned registration before another registration.

Preview ownership checks with `./build/ai-design/Unregister-NativeHost.ps1 -ValidateOnly`. Explicit rollback uses the same command without `-ValidateOnly`. Rollback selects the registry key from the saved ownership record; no browser argument is needed. It refuses modified manifest/configuration, inconsistent browser identity or a changed binding, and deletes only the matching key and its three registration files. Idle lock/status files may remain; they establish no channel readiness.

Record the real trial separately: actual extension ID/origin, pinned version/root, Hello status, unavailable Work reason, matching rollback, absence of the exact registry binding, and absence of new startup/service entries. Current local protocol and preview tests do not substitute for this browser evidence.
