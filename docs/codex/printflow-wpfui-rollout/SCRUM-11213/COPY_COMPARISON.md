# C05 中文文案前后对照

基线为 C02 改写前的工作字节；当前值取自冻结的 U09 候选。完整阶段历史见 COPY_COMPARISON.json。

后续 UI/U09 改动与 C02–C04 阶段末值不同的项目单独标注；本表不是用户理解度结论。

| 资源键 | 改写前 | 当前候选 | 阶段记录及后续变化 |
|---|---|---|---|
| Environment_Cancelled | 检查已取消。上次结果已保留，准备好后请点“安全恢复并重新检查”。 | 检查已取消，之前的结果会保留。准备好后，请点“安全恢复并重新检查”。 | SCRUM-11206 |
| Environment_Cancelling | 正在安全取消，请等待当前操作结束。 | 正在取消检查，请等待当前操作安全结束。 | SCRUM-11206 |
| Environment_EvidenceDeferred | 当前未重新观察。历史成功时间：{0}；这不是当前生产许可。 | 当前未重新观察应用状态。历史成功时间：{0}；不能据此开始生产。 | SCRUM-11206 |
| Environment_Hint | 检查本工作站是否可以开始生产。安全恢复只处理 PrintFlow 测试图像，不会更改您的工作文件或应用设置。 | 检查这台工作站能否开始生产。安全恢复只处理 PrintFlow 测试图像，不会更改你的工作文件或应用设置。 | SCRUM-11206 |
| Environment_ObservationBlocked | 部分必要检查未运行。请查看下方阻塞原因；历史结果不代表生产许可。 | 有必要检查未运行。请处理下方阻塞原因；历史结果不能作为当前生产许可。 | SCRUM-11206 |
| Environment_ObservationCurrent | 本次报告描述下方所示检查结果。每次生产请求仍会重新核验。 | 本次报告对应下方检查结果。每次生产请求仍会重新核验，不代表以后一直就绪。 | SCRUM-11206 |
| Environment_ObservationUnchecked | 尚未检查。还未读取环境报告，不能确认生产就绪。 | 尚未检查，不能确认可以生产。请先查看环境报告。 | SCRUM-11206 |
| Environment_PreviousCheckIncomplete | 上一次 Photoshop 检查未完成。请点“安全恢复并重新检查”确认当前状态。 | 上次 Photoshop 检查未完成。请点“安全恢复并重新检查”，先确认当前状态。 | SCRUM-11206 |
| Environment_RefreshScope | “刷新状态”只读取当前状态，不会恢复测试图像或执行新的应用检查。已认可的安装文件在重启 PrintFlow 前不会重新读取。 | “刷新状态”只读取状态，不会恢复测试图像，也不会重新检查应用。已认可的安装文件要重启 PrintFlow 才会重新读取。 | SCRUM-11206 |
| Environment_RunLiveChecksHint | 选择“安全恢复并重新检查”后，会检查美图秀秀和 Photoshop，安全处理已确认归属的 PrintFlow 测试图像，再进行一次新检查。 | 点“安全恢复并重新检查”，会先确认美图秀秀和 Photoshop 的状态，只处理已确认属于 PrintFlow 的测试图像，再进行新检查。 | SCRUM-11206 |
| ErrorDetails_Cancelled | 处理在结果确认之前取消。请查看可用的恢复操作。 | 处理在确认结果前取消，结果尚未确认。请查看当前可用的恢复操作。 | SCRUM-11206 |
| ErrorDetails_CodeHint | 寻求帮助时，请提供此代码。 | 需要同事帮忙时，请提供这个错误代码。 | SCRUM-11206 |
| ErrorDetails_Historical | 这是以前的故障。本次尝试的恢复操作已不可用。 | 这是以前的故障记录，不能再使用本次尝试的恢复操作。请返回任务查看当前状态。 | SCRUM-11206 |
| ErrorDetails_Interrupted | 处理在结果确认之前中断。请查看可用的恢复操作。 | 处理在确认结果前中断，结果尚未确认。请查看当前可用的恢复操作。 | SCRUM-11206 |
| FinalSave_ActionIncomplete | 此操作未完成，因此暂时无法确认此次保存的更多情况。请重试。 | 本次操作未完成，暂时无法核实更多保存信息。请查看当前状态提示；仍不确定时，请同事帮忙。 | SCRUM-11206 |
| FinalSave_ApprovalRefused | 结果尚未通过审核：屏幕上的结果已变化或无法审核。未保存任何文件。 | 未通过审核，也未保存文件：当前结果已变化或无法审核。请重新检查当前结果。 | SCRUM-11206 |
| FinalSave_ApprovalUnknown | PrintFlow 无法确认审核是否已记录。未保存任何文件。请返回主页并重新打开此任务查看当前状态。 | 暂时无法确认审核是否已记录，未保存任何文件。请返回首页，重新打开此任务查看当前状态。 | SCRUM-11206 |
| FinalSave_ApproveOnlyHint | “{0}”只记录审核结果，不会保存文件。 | 单独点“{0}”只记录审核结果，不会保存文件。 | SCRUM-11206 |
| FinalSave_ApprovedNotSaved | 已通过审核，尚未保存到你选择的文件夹。 | 这份结果已通过审核，尚未保存到你选择的文件夹。 | SCRUM-11206 |
| FinalSave_ApprovedNotSavedReason | 已通过审核，但尚未保存：{0}无需重新处理图片。 | 结果已通过审核，但尚未保存。{0}图片无需重新处理；请按提示处理后重试保存。 | SCRUM-11206 |
| FinalSave_AwaitingConfirmation | 等待你检查。请检查结果、文件名和文件夹，然后选择“{0}”。 | 请先检查图片，再核对文件名和文件夹。点“{0}”会同时确认审核并保存。 | SCRUM-11206 |
| FinalSave_ChangeLocation | 更改位置 | 选择保存文件夹 | SCRUM-11206 |
| FinalSave_Changed | 此前已保存，但 {0} 处的文件已被更改。不会覆盖它；如需再次保存，请选择其他名称或文件夹。 | 有保存记录，但 {0} 处的文件已变化，不会覆盖。如需再次保存，请换一个名称或文件夹。 | SCRUM-11206 |
| FinalSave_CheckAgain | 再次核实此次保存 | 核实这次保存 | SCRUM-11206 |
| FinalSave_FailCopy | 复制未能完成。 | 文件复制未完成。请查看当前保存状态，再按提示处理。 | SCRUM-11206 |
| FinalSave_FailNotWritable | 无法写入该文件夹。 | 无法写入所选文件夹。请检查写入权限，或选择其他文件夹。 | SCRUM-11206 |
| FinalSave_FailPersistence | 无法写入保存记录。 | 保存记录未能写入，保存结果以当前核实状态为准。请查看当前提示。 | SCRUM-11206 |
| FinalSave_FailProtected | 该文件夹属于 PrintFlow 自身存储，或该名称是导入的原始文件。请选择其他文件夹或名称。 | 不能使用 PrintFlow 自身存储文件夹，也不能覆盖导入的原始文件。请换一个文件夹或名称。 | SCRUM-11206 |
| FinalSave_FailUnavailable | 该文件夹当前不可用（驱动器可能已断开）。 | 保存文件夹当前不可用，驱动器可能已断开。请检查连接，或选择其他文件夹。 | SCRUM-11206 |
| FinalSave_FailUnsupported | 不支持此位置。请选择本机本地 NTFS 驱动器上的文件夹（U 盘须为 NTFS 格式）；不支持网络、云盘及其他类型的驱动器。 | 不能保存到此位置。请选择本机的 NTFS 文件夹；U 盘也须使用 NTFS 格式。不支持网络、云盘或其他类型的驱动器。 | SCRUM-11206 |
| FinalSave_FailVerify | 无法核实复制的文件。 | 文件复制后的校验未通过，本次尚未保存。请重试保存。 | SCRUM-11206 |
| FinalSave_HistoryNotRefreshed | 无法刷新保存记录。此处显示的是之前读取的内容，可能已过时。 | 保存记录未能更新。这里是上次读取的记录，可能已过时，不代表文件现在仍在。 | SCRUM-11206 |
| FinalSave_HistoryUnavailable | 无法读取保存记录，可能存在更早的保存。 | 暂时读不到保存记录，无法判断以前是否保存过。请稍后查看，或请同事帮忙。 | SCRUM-11206 |
| FinalSave_Missing | 此前已保存，但文件现在找不到：{0} | 有保存记录，但现在找不到文件：{0} | SCRUM-11206 |
| FinalSave_OpenChanged | 未打开文件夹：已保存的文件已被更改。 | 未打开文件夹：有保存记录，但文件已变化。 | SCRUM-11206 |
| FinalSave_OpenMissing | 未打开文件夹：已保存的文件找不到了。 | 未打开文件夹：有保存记录，但现在找不到文件。 | SCRUM-11206 |
| FinalSave_OpenUnavailable | 未打开文件夹：该位置当前无法访问。 | 未打开文件夹：目前无法访问保存位置，请检查驱动器连接。 | SCRUM-11206 |
| FinalSave_OpenUncertain | 未打开文件夹：此次保存尚未核实。 | 未打开文件夹：这次保存还没核实，请先核实保存结果。 | SCRUM-11206 |
| FinalSave_PngPreparationPending | 已通过审核；审核通过的 PNG 尚未准备好。请使用下方的步骤操作完成准备，然后再保存。图片无需重新处理。 | 结果已通过审核，但最终 PNG 还没准备好。请用下方的步骤操作完成准备，再保存；无需重新处理图片。 | SCRUM-11206 |
| FinalSave_PreferenceUnavailable | 无法读取上次使用的文件夹。保存前请选择文件夹。 | 暂时读不到上次使用的文件夹。请重新选择保存文件夹。 | SCRUM-11206 |
| FinalSave_RecordUnverified | 尚未核实：{0}，位置：{1} | 尚未核实：{0}，保存位置：{1} | SCRUM-11206 |
| FinalSave_RememberedFolder | 这是上次成功保存使用的文件夹。如果此任务应保存到别处，请更改位置。 | 这是上次成功保存使用的文件夹。要换位置，请点“选择保存文件夹”。 | SCRUM-11206 |
| FinalSave_SavedPreviously | 此前已保存：{0}，位置：{1}。尚未检查该位置。 | 有保存记录：{0}，位置：{1}。当前文件是否仍在，尚未核实。 | SCRUM-11206 |
| FinalSave_StageApproving | 正在确认此结果… | 正在记录本次审核决定，尚未开始保存… | SCRUM-11206 |
| FinalSave_TrimAdjustOpen | 请先完成或取消裁切调整，再通过或保存。 | 请先使用裁切结果，或取消本次调整，再审核或保存。 | SCRUM-11206 |
| FinalSave_Unavailable | 此前已保存，但现在无法访问 {0}。请重新连接驱动器后再次检查。 | 有保存记录，但现在无法访问 {0}。请连接好驱动器，再检查文件。 | SCRUM-11206 |
| FinalSave_Uncertain | 文件可能已保存到 {0}，但尚未核实完成。请再次核实此次保存。 | 文件可能已保存到 {0}，但结果尚未核实。请核实同一次保存，这不会重新保存或另存文件。 | SCRUM-11206 |
| FinalSave_UncertainElsewhere | 此前保存到 {0} 的操作尚未核实。使用其他名称或文件夹保存将另存一份。 | 此前保存到 {0} 的结果仍待核实。换名称或文件夹是另存一份，不会核实之前那次保存。 | SCRUM-11206 |
| Home_AbandonConfirmQuestion | 放弃“{0}”？放弃后此任务不能再继续处理。已导入的文件、已审核的输出和历史记录会保留，不会删除任何文件。 | 放弃“{0}”？此任务将不能继续处理。导入的文件、已审核结果和历史记录都会保留，不会删除任何文件。 | SCRUM-11206 |
| Home_AbandonFailed | 无法放弃该处理任务。放弃操作从不删除文件，因此它的导入文件、已审核的输出和历史记录均予保留。请在列表中查看该任务的状态；如果不确定，请展开下方的“错误详情”，并请同事或主管帮忙。 | 未能放弃此任务。导入的文件、已审核结果和历史记录都会保留，不会删除文件。请查看列表中的当前状态；仍不确定时，展开“错误详情”并请同事帮忙。 | SCRUM-11206 |
| Home_AbandonStale | “{0}”的状态已变化，未放弃。请在列表中查看它的当前状态。 | “{0}”的状态已变化，本次未放弃。请查看列表中的当前状态，再决定下一步。 | SCRUM-11206 |
| Home_ImportHeading | 开始新的处理 | 开始新任务 | SCRUM-11204 |
| Home_ImportHint | 选择一个文件，或将一个文件拖入本窗口。每个处理只接受一个文件，不支持批量导入。 | 选择一个文件，或把它拖到此处；一次只能处理一个文件。 | SCRUM-11204 |
| Home_ReadinessBlockedHint | 请选择“查看工作站检查”查看全部检查项。请熟练同事或主管帮忙。 | 请点“查看工作站检查”，查看全部阻塞原因；需要时请熟练同事或主管帮忙。 | SCRUM-11206 |
| Home_ReadinessReadyHint | 这是已记录的检查结果。每个自动步骤仍会执行自己的工作站检查。 | 这是上次记录的检查结果。每个自动步骤仍会重新检查工作站。 | SCRUM-11206 |
| Home_RecentHeading | 最近处理 | 最近任务 | SCRUM-11206 |
| Home_RecentNextContinue | 继续将打开实际当前步骤，不会自动开始处理。 | 继续任务只会打开当前步骤，不会自动开始处理。 | SCRUM-11206 |
| Home_RecentNextCorrection | 打开详情导入修正图片，之后仍需重新审核。 | 请打开详情，导入同事修正后的图片；导入后仍须审核。 | SCRUM-11206 |
| Home_RecentNextHistory | 查看处理记录。请打开详情核实当前文件；仅有保存历史无法确认。 | 请打开处理详情，核实当前文件。仅有保存记录，不能确认文件现在仍在。 | SCRUM-11206 |
| Home_RecentNextSaved | 继续查看实际步骤并核实当前文件。保存历史不代表文件仍存在。 | 请打开当前步骤，核实当前文件是否仍在。有保存记录不代表文件现在存在。 | SCRUM-11206 |
| Home_RecentPngSavedPreviously | 已批准的 PNG 曾保存过；请在任务详情中检查 | 有已审核 PNG 的保存记录，当前文件待核实；请打开任务详情 | SCRUM-11206 |
| Home_RecentSaveHistoryUnavailable | 保存记录暂不可用；请在任务详情中检查 | 暂时读不到保存记录，请打开任务详情核对 | SCRUM-11206 |
| Home_RecentTiffSavedPreviously | {0} 个已批准的 TIFF 尺寸曾保存过；请在任务详情中检查 | 有 {0} 个已审核 TIFF 尺寸的保存记录，当前文件待核实；请打开任务详情 | SCRUM-11206 |
| Home_Resume | 继续 | 继续此任务 | SCRUM-11204 |
| ReadinessRecovery_BlockedUnknownState | 暂时无法安全确认 Photoshop 的当前状态。请查看 Photoshop 是否有需要您处理的对话框，再点“安全恢复并重新检查”。 | 暂时无法确认 Photoshop 的安全状态。请查看是否有待处理的弹窗，处理后再点“安全恢复并重新检查”。 | SCRUM-11206 |
| ReadinessRecovery_BlockedUserWork | Photoshop 中还有其他工作文件。请先保存并关闭您的文件，再点“安全恢复并重新检查”。 | Photoshop 中还有你的工作文件。请自行保存并关闭，再点“安全恢复并重新检查”。 | SCRUM-11206 |
| Session_Approve | 通过 | 确认这张图通过检查 | SCRUM-11204 |
| Session_ApproveTiff | 批准此 TIFF | 确认此 TIFF 通过检查 | SCRUM-11204 |
| Session_BackgroundRemovalCancel | 取消 | 取消本次确认 | SCRUM-11204 |
| Session_BackgroundRemovalConfirm | 确认 | 确认使用自动选择 | SCRUM-11204 |
| Session_Complete | 完成 | 完成任务 | SCRUM-11206 |
| Session_CompletionCleanupPending | 处理已完成。文件清理尚未完成，PrintFlow 下次启动时将重试。已批准的输出文件会保留。 | 处理已完成，但文件清理还没完成。PrintFlow 下次启动时会重试清理，已审核的输出文件会保留。 | SCRUM-11206 |
| Session_CompletionNotice | 审核通过、保存到所选文件夹和完成任务是独立状态。内部批准文件不代表已交付。 | 通过审核、保存文件、完成任务是三件事。PrintFlow 内部的已审核文件，不代表已保存到你选的文件夹。 | SCRUM-11206 |
| Session_CompletionRecordsUnknown | (absent) | 保存记录正在读取或暂不可用，份数尚未核实。 | UI/U09 或其他后续改动 |
| Session_CompletionSaveHeading | 保存已批准的结果 | 保存已审核的结果 | SCRUM-11206 |
| Session_CompletionSavedCount | (absent) | 此结果有 {0} 份保存记录。记录不代表当前文件已核实。 | UI/U09 或其他后续改动 |
| Session_ConfirmOriginal | 确认原图 | 确认使用这张原图 | SCRUM-11204 |
| Session_ContinueWithSize | 仍按此尺寸处理 | 接受风险并授权放大 | SCRUM-11205 |
| Session_CorrectionCancel | 取消 | 取消本次准备 | SCRUM-11206 |
| Session_CorrectionIdentical | 此图片与发出的图片完全相同。 | 这张图与交给同事的图片完全相同，请核对是否选中了修正后的文件。 | SCRUM-11206 |
| Session_CorrectionMissingFiles | 部分给同事的文件已丢失。 | 给同事的文件有缺失。请重新准备文件，再自行交接。 | SCRUM-11206 |
| Session_CorrectionOpenFolderFailed | 无法打开文件夹，其路径已显示在面板中。 | 未能打开文件夹。请复制面板中的路径，自行打开。 | SCRUM-11206 |
| Session_CorrectionOtherComputer | 如果同事使用另一台电脑，请自行将这些文件复制给对方。 | 请自行交接文件。同事在另一台电脑时，请把这两个文件复制给对方；PrintFlow 不会自动发送。 | SCRUM-11206 |
| Session_CorrectionPrepare | 为同事准备文件 | 准备修正副本 | SCRUM-11206 |
| Session_CorrectionPrepareFailed | 无法为同事准备文件，未移交任何内容。{0} | 修正副本未能准备好，本次未移交任何内容。{0} | SCRUM-11206 |
| Session_CorrectionPreparing | 正在准备文件… | 正在准备修正副本，不会自动发送… | SCRUM-11206 |
| Session_CorrectionReview | 请检查修正后的图片，然后选择“通过”或“驳回”。通过后，PrintFlow 将继续进行“{0}”。不会重新去除背景。 | 请检查修正后的图片，再选择“确认这张图通过检查”或“驳回”。确认通过后，将继续进行“{0}”，不会重新去除背景。 | SCRUM-11204 |
| Session_CorrectionStatus | 已移交 — 等待同事修正后的图片 | 任务已转为同事修正，文件须自行交接；等待导入修正图片。 | SCRUM-11206 |
| Session_CorrectionStep1 | 1. 把这两个文件交给同事： | 1. 请自行把这两个文件交给同事： | SCRUM-11206 |
| Session_CorrectionStep4 | 4. 回到 PrintFlow，打开此任务并选择“导入修正后的图片”。PrintFlow 不会对导入的图片重新去除背景。 | 4. 回到 PrintFlow，打开此任务并选择“导入修正后的图片”。导入后仍须审核，不会重新去除背景。 | SCRUM-11206 |
| Session_CorrectionTryAgain | 重试 | 重试准备副本 | SCRUM-11206 |
| Session_CropDetailsLabel | (absent) | 坐标和键盘操作 | UI/U09 或其他后续改动 |
| Session_CurrentCustomTarget | 自定义{0}：{1} mm | 自定义{0}：{1} 毫米 | SCRUM-11205 |
| Session_DimensionReviewRequired | 此尺寸按旧版精确尺寸规则保存，或其所依据的图片已发生变化。继续前请重新确认最大印刷尺寸。 | 此尺寸来自旧版精确尺寸规则，或所依据的图片已变化。继续前，请重新确认最大印刷尺寸。 | SCRUM-11205 |
| Session_DraftLeaveHeading | 保留尚未提交的输入？ | 要保留本页输入吗？ | SCRUM-11206 |
| Session_DraftLeaveNotice | 当前页面有尚未提交的输入。可继续编辑，或放弃本页输入后离开；这些输入尚未确认或保存。 | 本页还有未确认或保存的输入。你可以继续编辑，或只放弃本页输入后离开；这不会放弃整个任务。 | SCRUM-11206 |
| Session_EnlargementConfirmed | 已确认按此图片和尺寸进行放大。 | 你已授权按当前图片和尺寸放大，处理后仍须审核结果。 | SCRUM-11205 |
| Session_EnlargementExplicitlyConfirmed | 已明确确认放大 | 你已明确授权放大 | SCRUM-11205 |
| Session_EnlargementWarning | 此尺寸需要在 300 PPI 下将图片放大至约 {0}%。<br>放大可能降低图片清晰度。 | 按此尺寸输出 300 PPI 图片，需要放大到原来的约 {0}%。<br>放大可能让图片变模糊。 | SCRUM-11205 |
| Session_GuidanceApprove | “{0}”会接受当前这个结果并结束本次审核。它不会把文件保存到你的文件夹。 | 点击“{0}”，确认当前这张图通过检查；此操作不会把文件保存到你的文件夹。 | SCRUM-11204 |
| Session_GuidanceApproveTiff | “{0}”只记录此 TIFF 已通过你的检查。它不检查打印质量，也不会把文件保存到你的文件夹。 | 点击“{0}”，只记录你已检查这份 TIFF；这不代表已检查打印质量，也不会把文件保存到你的文件夹。 | SCRUM-11204 |
| Session_GuidanceHelpAdjustTrim | 边缘不对？用下方“{0}”在裁切前的图片上移动边缘。新的裁切结果仍需你审核。 | 边缘不合适？点击下方“{0}”，在裁切前的图片上调整边缘；新结果仍须审核。 | SCRUM-11204 |
| Session_GuidanceHelpAskColleague | 有问题或不确定？用下方按钮请同事修正此图片。发送前可以先写备注。 | 有问题或不确定？用下方按钮准备交给同事修正的副本，准备前可以先写备注。 | SCRUM-11204 |
| Session_GuidedOriginalChecks | (absent) | 确认图片内容和方向正确，没有缺少要保留的部分。检查后，再选择下方的确认操作。 | UI/U09 或其他后续改动 |
| Session_GuidedOriginalChecksHeading | (absent) | 先检查这张原图 | UI/U09 或其他后续改动 |
| Session_GuidedPrintActual | 预计实际图片：{0:0.##} × {1:0.##} 毫米 | 预计印刷尺寸：{0:0.##} × {1:0.##} 毫米 | SCRUM-11205 |
| Session_GuidedPrintContract | 生产要求：{0} PPI · CMYK + W1。生成后仍须校验与审核。 | TIFF 打印文件：{0} PPI（每英寸像素数）· CMYK 印刷色彩 · W1 白墨通道。生成后须校验、人工审核，再保存。 | SCRUM-11205 |
| Session_GuidedPrintPixels | 输出：{0} × {1} 像素 · 保持原比例 | 预计输出：{0} × {1} 像素（图片的细小方格）· 保持原比例 | SCRUM-11205 |
| Session_GuidedPrintRequested | 当前要求：{0} | 本次尺寸设置：{0} | SCRUM-11205 |
| Session_GuidedPrintRisk | 本尺寸需要放大。确认尺寸后仍须单独授权，之后仍需审核结果。 | 此尺寸需要放大，图片可能变模糊。确认尺寸后，仍须单独授权放大，并在处理后审核结果。 | SCRUM-11205 |
| Session_GuidedPrintWhiteInk | 白墨：{0} | 白墨方案：{0} | SCRUM-11205 |
| Session_GuidedProcessingWait | 当前没有可靠的百分比，请等待。成功后仍需你检查并审核结果。 | 当前无法提供可靠的百分比，请等待；处理成功后，仍须你检查并审核结果。 | SCRUM-11204 |
| Session_GuidedWhiteInkReasonHint | 选择同时记录对应的图案分类理由，请根据本次完整成品设计判断。 | 请按完整成品设计选择图案类别：精细细节对应 0 像素，普通图案对应 1 像素，实色或整块矩形对应 2 像素。选择时会记录对应的分类理由；0 像素仍有白墨，只是不内缩。 | SCRUM-11205 |
| Session_GuidedWhiteInkSourceHint | 源 PSD/PDF 的专色或白墨通道不会沿用。本次选择用于生成生产 W1 通道。 | 不会沿用原 PSD/PDF 文件里的专色或白墨通道。本次选择用于生成打印所需的 W1 白墨通道。 | SCRUM-11205 |
| Session_HistoricalBoundsLabel | 此前保存，尚未确认为最大尺寸范围 | 此前记录的尺寸，尚未确认为最大尺寸范围 | SCRUM-11205 |
| Session_ManualCropApply | 应用裁切 | 使用此裁切 | SCRUM-11204 |
| Session_ManualCropRequiredNotice | 自动裁切无法确定裁切范围。请使用"手动裁切"自行框选。 | 未找到裁切范围。请点“手动裁切”，框出要保留的部分。 | SCRUM-11204 |
| Session_MaxBoundsHint | 请输入打印成品允许达到的最大宽度和最大高度，单位为毫米。图像会按原图比例适应最大尺寸，既不会被拉伸，也不会被放大。固定输出分辨率：300 PPI。 | 请输入成品允许的最大宽度和最大高度，单位为毫米。图片保持原比例，不拉伸，也不为填满上限而放大。像素是图片的细小方格；输出固定为 300 PPI（每英寸像素数）。 | SCRUM-11205 |
| Session_MaxBoundsInvalid | 最大宽度和最大高度都必须是大于零的毫米数值。 | 最大宽度和最大高度都要填写大于零的数值，单位为毫米。 | SCRUM-11205 |
| Session_PhotoshopTakeOverConfirmQuestion | 要在 Photoshop 中接管吗？PrintFlow 将停止控制 Photoshop，并保持其现状。它可能仍在运行；返回自动处理前，请自行检查。此步骤不支持导回手工制作的结果，包括 TIFF。 | 要在 Photoshop 中接管吗？PrintFlow 将停止控制它，但不会关闭或取消它。Photoshop 可能仍在运行，请先检查当前状态。此步骤不能导回手工结果，包括 TIFF；恢复自动处理须另行选择。 | SCRUM-11206 |
| Session_PreparationAttemptProjectionNotice | 此为计划值 — 并未运行 Photoshop。 | 这里是预计结果，尚未运行 Photoshop。 | SCRUM-11205 |
| Session_PreparationLimitingEdge | 起限制作用的边：{0} | 决定尺寸的边：{0} | SCRUM-11205 |
| Session_PreparationProportionalShrink | 图像将按原图比例缩小，以适应最大尺寸。 | 图片保持原比例，缩小到最大尺寸以内。 | SCRUM-11205 |
| Session_PreparationResolutionOnly | 图像已在最大尺寸范围之内，像素尺寸保持不变。 | 图片已在最大尺寸以内，不改变像素尺寸。 | SCRUM-11205 |
| Session_PresetHint | 预设只是填入其最大尺寸范围，确认之前仍可自行修改毫米数值；具体由哪条边起限制作用，由 PrintFlow 根据图像本身判断。 | 预设填入的是最大尺寸，确认前可以修改。图片保持原比例，由宽度或高度的上限决定是否缩小。 | SCRUM-11205 |
| Session_ReenterAutomationHint | 本会话已移交给你手动处理。恢复自动处理会以全新的工作副本开始一次新的尝试；已移交的那次尝试仍保留在历史记录中。 | 此任务已转为手动处理。恢复自动处理将使用新的工作副本和新的尝试；已移交的那次尝试仍保留在历史记录中。 | SCRUM-11206 |
| Session_ReturnConfirmQuestion | 返回该步骤会使其后生成的结果失效。已有的审核记录将予以保留。 | 返回后，后续生成的结果会失效，需要重做；已有审核记录会保留。 | SCRUM-11206 |
| Session_ReturnHint | 选择要返回的较早步骤。此处仅列出当前允许返回的步骤。 | 请选择要返回的步骤。这里只显示当前允许返回的较早步骤。 | SCRUM-11206 |
| Session_ReturnImpact | 返回“{0}”。需要重新处理：{1}。依赖这些步骤的尺寸、输出结果及审核需要重新完成；已有历史记录保留。 | 返回“{0}”后，须重新处理：{1}。相关尺寸、输出结果和审核都须重做；已有历史记录会保留。 | SCRUM-11206 |
| Session_ReviewToolsLabel | (absent) | 图片查看工具 | UI/U09 或其他后续改动 |
| Session_RunNotReady | 在确认本图片的印刷尺寸及所需放大之前，无法运行此步骤。 | 请先确认本图片的印刷尺寸；如需放大，还须单独授权。 | SCRUM-11205 |
| Session_RunReady | 可以运行此步骤：本图片的印刷尺寸已确认。 | 本图片的印刷尺寸已确认，可以运行此步骤。 | SCRUM-11205 |
| Session_SaveCurrentResult | (absent) | 当前结果预览 | UI/U09 或其他后续改动 |
| Session_SaveHideReview | (absent) | 返回结果预览 | UI/U09 或其他后续改动 |
| Session_SaveReviewResult | (absent) | 查看审核结果 | UI/U09 或其他后续改动 |
| Session_SizeDraftEnlargement | 按此尺寸需要放大图片，印刷效果可能发虚或模糊。确认此尺寸后，会请你决定是否接受。 | 此尺寸需要放大图片，印刷效果可能发虚或模糊。确认尺寸后，仍须你单独授权放大。 | SCRUM-11205 |
| Session_SizeEnlargementPlain | 按此尺寸需要放大图片，印刷效果可能发虚或模糊。可选择“{0}”改用较小的尺寸（需要的放大更少），或选择“{1}”接受这一点。此尺寸需要放大时，在你接受之前“{2}”不可用。 | 此尺寸需要放大，印刷效果可能发虚或模糊。可点“{0}”改小尺寸，减少放大；或点“{1}”。授权前，“{2}”不可用；处理后仍须审核结果。 | SCRUM-11205 |
| Session_SizeHeading | 尺寸 | 印刷尺寸 | SCRUM-11205 |
| Session_SizeHelpCustom | 某一边必须是准确尺寸时，选择“{0}”。选择要固定的边（“{1}”、“{2}”，或表示此图片较长一边的“{3}”），并输入毫米数，另一边按图片比例得出。如需放大图片，会先征求你的确认。 | 某一边需要准确尺寸时，选择“{0}”。请选择“{1}”、“{2}”或“{3}”（图片较长的一边），再输入毫米数。另一边按原比例计算；需要放大时，仍须你单独授权。 | SCRUM-11205 |
| Session_SizeHelpPreset | 需要标准尺寸时选择预设。预设是上限，不是准确的印刷尺寸：较大的图片会缩小到上限以内，较小的图片不会被放大，图片比例保持不变。 | 需要标准尺寸时，选择预设。预设是尺寸上限，不是最终印刷尺寸。大图片会缩小到上限以内，小图片不会被放大；图片保持原比例。 | SCRUM-11205 |
| Session_SizeTechnicalDetails | 技术细节（像素与 PPI） | 技术细节（像素与每英寸像素数 PPI） | SCRUM-11205 |
| Session_Skip | 跳过 | 跳过此项处理 | SCRUM-11204 |
| Session_StopHint | 安全停止本次自动操作，不会关闭美图秀秀。 | 请求停止本次自动操作，不会强制关闭美图秀秀。 | SCRUM-11206 |
| Session_StoppingNotice | 正在停止 —— PrintFlow 正在请求美图秀秀取消本次操作，请稍候。 | 正在停止：已请求美图秀秀取消本次操作，尚未确认停止。请等待。 | SCRUM-11206 |
| Session_TakeOverConfirmQuestion | 确定要在美图秀秀中手动接管吗？PrintFlow 将停止控制本次操作的美图秀秀，并且不再向它发送任何指令。美图秀秀会保持原样 —— 既不取消也不关闭，因此它可能仍在运行，你可能需要自行完成或关闭它。本次尝试不会生成版本记录；要恢复自动化，需要明确执行「恢复自动处理」。 | 要在美图秀秀中接管吗？PrintFlow 将停止发送指令，既不取消也不关闭美图，因此它可能仍在运行。请自行完成或关闭它。本次尝试不会生成版本记录；之后须明确选择“恢复自动处理”。 | SCRUM-11206 |
| Session_TakeOverHint | 停止 PrintFlow 自动化，美图秀秀保持原样交给你手动操作。PrintFlow 不会强制关闭美图秀秀。如果接管后美图秀秀无响应，请通过美图秀秀或 Windows 手动关闭后再恢复自动处理。 | 接管后，PrintFlow 会停止控制美图秀秀，不会强制关闭美图秀秀。若接管后美图秀秀无响应，请通过美图秀秀或 Windows 手动关闭，再恢复自动处理。 | SCRUM-11206；后续已变化 |
| Session_TargetEdge | 目标边 | 要设置的边 | SCRUM-11205 |
| Session_TargetSizeInvalid | 请选择宽度、高度或长边，并输入大于零的毫米数值。 | 请选择宽度、高度或长边，再输入大于零的数值，单位为毫米。 | SCRUM-11205 |
| Session_TargetSizeMm | 目标尺寸（mm） | 尺寸（毫米） | SCRUM-11205 |
| Session_TargetSizeUnavailable | 无法预览此尺寸。请核对输入和任务详情，然后重试。 | 暂时无法预览此尺寸。请核对尺寸输入和任务详情，再重试。 | SCRUM-11205 |
| Session_TiffColourLegend | 这是 CMYK 内容未经色彩校准的屏幕近似显示，不能作为印刷颜色的依据。 | CMYK 印刷色彩在屏幕上只是近似显示，未经色彩校准，不能据此判断实际打印颜色。 | SCRUM-11205 |
| Session_TiffLabelColourMode | 色彩 | 印刷色彩模式 | SCRUM-11205 |
| Session_TiffLabelPhysicalSize | 物理尺寸 | 印刷尺寸 | SCRUM-11205 |
| Session_TiffOverlayLegend | 白色叠加 = 白墨覆盖范围，并非最终印刷效果。 | 白色叠加标出白墨覆盖范围，不是最终印刷效果。 | SCRUM-11205 |
| Session_TiffReviewCaveat | 系统验证的是文件结构，而非画面或印刷质量。 | 文件结构校验不代表画面或印刷质量合格。请检查图片与白墨，再决定是否通过审核。单独审核不会保存文件。 | SCRUM-11205 |
| Session_TiffReviewHeading | 生产 TIFF | 检查打印 TIFF | SCRUM-11205 |
| Session_TiffReviewSummary | 已验证 CMYK + W1 · {0} | TIFF 文件结构已校验：CMYK + W1 · {0} | SCRUM-11205 |
| Session_TiffValueEnlargementAuthorised | 需要放大，操作员已授权 | 需要放大，你已授权；仍须检查结果 | SCRUM-11205 |
| Session_TiffValueEnlargementUnauthorised | 需要放大，但未记录操作员授权 | 需要放大，尚未记录你的授权 | SCRUM-11205 |
| Session_TiffValueWhiteInk | {0} 已验证 · {1} · {2:N0} 像素有墨 | {0} 通道已校验 · {1} · {2:N0} 像素有白墨 | SCRUM-11205 |
| Session_TiffWhiteInkLegend | 亮部 = 有白墨 · 黑色 = 无白墨。 | 亮部表示有白墨，黑色表示无白墨。请检查白墨覆盖是否正确。 | SCRUM-11205 |
| Session_TrimAdjustReady | 新的裁切已生成。请检查后再通过。 | 新的裁切已生成。请检查后，再确认这张图通过检查。 | SCRUM-11204 |
| Session_W1Confirm | 确认 W1 分支 | 确认白墨方案 | SCRUM-11205 |
| Session_W1Heading | 白墨打底（W1） | 选择白墨方案（W1） | SCRUM-11205 |
| Session_W1Hint | 请对完整的成品设计进行判断并自行选择收缩量。系统没有默认值，也绝不会通过分析图像替你决定。 | 白墨是打印时垫在彩色图案下面的白色墨层。内缩是把白墨边缘向内缩小，单位是像素（图片的细小方格）。请根据完整成品设计自行选择；没有默认选项，系统不会替你决定。 | SCRUM-11205 |
| Session_W1NotChosen | 尚未选择 | 等待你选择 | SCRUM-11205 |
| Settings_Apply | 应用 | 保存设置 | SCRUM-11206 |
| Settings_ColourSetupHint | 在生产就绪状态页面进行检查。PrintFlow 不会替您修改 Photoshop 的颜色设置。 | 请在生产就绪状态页面检查。PrintFlow 不会替你修改 Photoshop 的颜色设置。 | SCRUM-11206 |
| Settings_LoadFailed | 无法读取已保存的设置（{0}）。当前显示的是默认值。 | 暂时读不到已保存的设置（{0}），现在显示的是默认值，请先核对。 | SCRUM-11206 |
| Settings_LogRetentionHint | 诊断记录和故障截图仅保存在本机，不会上传。应用安全启动并完成恢复后，会按设置的天数限量清理符合条件的过期记录和截图。当前工作仍需使用的诊断资料会保留。更改将在下次启动时生效。 | 诊断记录和故障截图只保存在本机，不会上传。应用安全启动并完成恢复后，会按保留天数限量清理符合条件的过期内容；当前工作仍需使用的资料会保留。修改从下次启动生效。 | SCRUM-11206 |
| Settings_OutputRootHint | 成品文件的存放位置，由已认可的生产配置固定，不会移动任何已有任务的文件。 | 成品存放位置由已认可的生产配置固定。这里不会移动已有任务的文件。 | SCRUM-11206 |
| Settings_ProductionHint | 以下内容来自本机已认可的生产配置，仅供查看核对，不能在此修改。 | 这些是本机已认可的生产配置，只能查看核对，不能在这里修改。 | SCRUM-11206 |
| Settings_SaveFailed | 无法保存设置。没有做任何更改：之前的设置仍然有效，你输入的内容仍保留在屏幕上。你可以再次选择“{0}”；如果再次失败，请展开下方的“错误详情”，并请同事或主管帮忙。 | 设置未能保存，之前的设置仍有效；你输入的内容也还在。可再次点“{0}”。仍失败时，请展开“错误详情”并请同事帮忙。 | SCRUM-11206 |
| Settings_TrimMarginHint | 裁切时在图案四周保留的额外空间。仅对此后新建的任务生效；已经打开的任务保持原有取值。填 0 表示紧贴图案裁切。 | 裁切时在图案四周多留的空间，单位为像素。只影响以后新建的任务；已打开的任务不变。填 0 表示紧贴图案裁切。 | SCRUM-11206 |
| Step_Enhancement | 高清化 | 增强清晰度 | SCRUM-11204 |
| Step_OriginalConfirmation | 原图确认 | 检查原图 | SCRUM-11204 |
| Step_Trim | 裁边 | 裁去多余边缘 | SCRUM-11204 |
| W1_0px | 0 像素 — 精细细节 | 0 像素（不内缩）— 精细细节 | SCRUM-11205 |
| W1_1px | 1 像素 — 普通图案 | 内缩 1 像素 — 普通图案 | SCRUM-11205 |
| W1_2px | 2 像素 — 实色／整块矩形图案 | 内缩 2 像素 — 实色／整块矩形图案 | SCRUM-11205 |
| WorkflowSelection_GuidedChangeAllowed | 尚未开始处理；首个处理结果生成前可更换路线。返回首页会保留本次任务。 | 生成第一个处理结果前，可以更换路线；返回首页会保留此任务。 | SCRUM-11204 |
| WorkflowSelection_Heading | 选择流程 | 选择处理路线 | SCRUM-11204 |
| WorkflowSelection_Hint | 在本次处理产生第一个处理结果之前，仍可更改流程。 | 生成第一个处理结果前，可以更换路线。 | SCRUM-11204 |
| WorkflowSelection_Locked | 本次处理已产生结果，流程已锁定。如需更换流程，请结束本次处理并重新导入文件。 | 已有处理结果，路线已锁定。如需更换路线，请结束本次处理，再重新导入文件。 | SCRUM-11204 |
| WorkflowSelection_OutputNameHint | PrintFlow 生成文件时所用的基础名称。源文件不会被重命名。尺寸与油墨后缀，以及同名文件已存在时的编号后缀，都会自动添加。 | 此名称用于生成输出文件，不会改动源文件名。尺寸、油墨和同名编号后缀会自动添加。 | SCRUM-11204 |
| WorkflowSelection_OutputNameLabel | 输出名称 | 输出文件基础名称 | SCRUM-11204 |
| WorkflowSelection_PreviewNeedsPreparation | 此文件将在处理页面中准备后才能显示。请选择流程以继续。 | 文件准备好后才能显示预览。请选择处理路线，继续准备文件。 | SCRUM-11204 |
| WorkflowSelection_Refused | 无法选择该流程。没有开始任何处理，显示的输出名称已保留。你可以再次选择流程；如果仍被拒绝，请展开下方的“错误详情”，并请同事或主管帮忙。 | 无法选择此路线。未开始处理，输出名称已保留。请重新选择；若仍无法继续，请同事或主管帮忙，下方可查看错误详情。 | SCRUM-11204 |
| WorkflowSelection_Select | 选择 | 选择此路线 | SCRUM-11204 |
