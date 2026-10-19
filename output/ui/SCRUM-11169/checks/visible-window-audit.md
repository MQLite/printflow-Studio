# Verification boundary correction — 2026-10-19

The first E2 UI/Architecture broad command unintentionally included tests with unconditional Window.Show. Three test windows were shown. This violated the task's no-Window.Show constraint; it is not accepted desktop validation. No new visible-window runs are permitted by this task.

The original record is e2-ui-architecture-before-contract-updates.trx (1282 passed, 56 failed, 1 skipped, total1339). visible-window-path-results.json lists every matching result across all E2 TRX files, including timestamps and errors. Do not rewrite those files.

## Actually reached Window.Show

| Test suffix | Source call chain | Broad result / NZDT | Cleanup evidence |
| --- | --- | --- | --- |
| PrintDimensionsPreflightUiTests.Keyboard_traverses_the_bound_custom_size_controls_and_panel_does_not_trap_focus | test → Window.Show at line202 | Failed, 15:59:49–50; confirm.IsKeyboardFocused expectedTrue/actualFalse after moving next from millimetres | Show is inside try; finally clears Content and calls Close at lines222–225 |
| ErrorDetailsRenderingTests.Rendered_failure_entry_and_UIA_retry_preserve_history_and_only_reset_the_step | test line81 → AssertKeyboardRoute → Show line329 | Passed, 16:02:22–23; no early-return guard | helper finally clears Content and Close at lines342–344 |
| ErrorDetailsRenderingTests.Error_details_previews_the_actual_plan_then_UIA_saves_an_exact_local_package | test line201 → AssertPackageKeyboardRoute → Show line352 | Passed, 16:02:23–24; no early-return guard | helper finally clears Content and Close at lines367–369 |

These methods use WPF Focus/MoveFocus while the window exists; they do not call SendInput/keybd_event/mouse_event/SetCursorPos. ErrorDetails also invokes in-process automation peers and writes a synthetic local diagnostic package. There was no launch of the production App, no physical keyboard/mouse injection and no external application or customer data. The testhost process has exited, adding process-lifecycle evidence to the finally cleanup. No claim of real operator acceptance follows from these tests.

The keyboard test's new failure remains an unverified visible-keyboard contract issue after moving the confirmation to the fixed bar. Its source and assertions are unchanged; it is excluded, not fixed or passed. The final executed-safe-set comparison must not hide this earlier failure.

## Guarded visible paths

| Test(s) | Source | Gate / evidence |
| --- | --- | --- |
| SharedReviewAuthorityTests.Live_synthetic_review_window_is_operable_through_Windows_UIA | line58 gate; line70 Show | PRINTFLOW_SHARED_REVIEW_LIVE must equal1 |
| ManualCropAdjustmentUiTests.Live_synthetic_WPF_UIA_uniform/per_edge/cancel_keep | RunLive line167; Show184, reopened.Show301 | PRINTFLOW_MANUAL_CROP_LIVE must equal1 |
| KeepOriginalExtentUiTests.Live_synthetic_WPF_UIA_review/manual_crop_required/waiting_keyboard | RunLive line103; Show121 | PRINTFLOW_KEEP_EXTENT_LIVE must equal1 |
| OperatorWave1AHostTests.Operator_launched_visible_synthetic_host | OperatorDesktopFact and line71 second gate; Show89 | PF_OPUX_SAFE_DESKTOP=operator-confirmed, PF_OPUX_HOST_LOG required |

The seven Live_* methods returned immediately and TRX labeled them Passed; that is gate-return, not live UIA success. All three PRINTFLOW_*_LIVE gate variables were unset and not set by this task. The explicit operator host is NotExecuted in both E2 broad and g04-regression-dispatch-fixed.trx. Guarded methods are explicitly excluded going forward, even though their guards currently prevent windows.

## Audit scope and safe continuation

Reviewed all Show/ShowDialog, Application.Run/Dispatcher.Run, Window/Form constructions, DesktopAutomation/Win32 input, opt-in environment flags in Integration/Ui, Fixtures and Architecture. No additional ShowDialog, WPF Application.Run or WinForms visible host was found in that scope.

OperatorWave1Tests and GuidedComponentTests each create HwndSource(WindowStyle=0) only as a nonvisible source for synthetic routed events; no displayed window or input injection. TempApplication is a temporary configuration/workspace fixture; SmokeApplication starts the service composition with fake adapters, not a WPF Application or window. Architecture tests inspect code/type boundaries and do not start production probes.

safe-ui-exclusions.json contains exactly11 exclusions: three unconditional Show tests, seven guarded live tests, one explicit visible host. run-safe-ui.ps1 uses that list and saves the exact filter. Tests are not deleted or assertions weakened. Their later status is NOT RUN/excluded, never PASS. The safer filter executes1328 tests if no test additions/removals occur, and its result is not interchangeable with the original1339-test broad run.

The same three unconditional Show tests also exist unchanged in E1 base7fd87f6 and appear in scrum-11177-ui-fix.trx. Therefore the historical E1 broad baseline must not be described as wholly offscreen. This correction preserves historical TRX and the user's previous acceptance; it does not authorize rerunning visible tests or expanding repairs into old defects.

