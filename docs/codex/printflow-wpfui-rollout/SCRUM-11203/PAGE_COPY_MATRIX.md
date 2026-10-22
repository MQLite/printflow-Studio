# SCRUM-11203 页面与文案复核集合

复用U01的140行；资源集合为编辑复核范围，不能据此推断可见、资格或页面已实现。精确键列表见PAGE_COPY_MATRIX.json。

| ID | 页面 / 状态 | 文案组 | 资源数 | 后续任务 |
| --- | --- | --- | --- | --- |
| P-home | 开始任务 / normal | HOME-IMPORT | 90 | SCRUM-11204 |
| P-route | 选择路线 / normal | ROUTE | 27 | SCRUM-11204 |
| P-original | 确认原图 / normal | SOURCE | 50 | SCRUM-11204 |
| P-pdf-psd | PDF与PSD源准备 / normal | SOURCE | 50 | SCRUM-11204 |
| P-enhance-ready | 开始增强 / normal | PROCESSING | 55 | SCRUM-11204 |
| P-enhance-running | 增强处理中 / loading | PROCESSING | 55 | SCRUM-11204 |
| P-enhance-review | 增强结果审核 / review | REVIEW | 124 | SCRUM-11204 |
| P-background-ready | 选择去背景 / normal | PROCESSING | 55 | SCRUM-11204 |
| P-background-running | 去背景处理中 / loading | PROCESSING | 55 | SCRUM-11204 |
| P-review | 审核图片 / review | REVIEW | 124 | SCRUM-11204 |
| P-crop-review | 裁切结果审核 / review | REVIEW | 124 | SCRUM-11204 |
| P-crop | 调整裁切 / normal | CROP | 100 | SCRUM-11204 |
| P-dimensions | 印刷尺寸 / normal | DIMENSIONS | 65 | SCRUM-11205 |
| P-white-ink | 白墨选择 / normal | WHITE-INK | 15 | SCRUM-11205 |
| P-preflight | 生成前确认 / normal | PREFLIGHT | 36 | SCRUM-11205 |
| P-tiff-running | TIFF生成中 / loading | PROCESSING | 55 | SCRUM-11205 |
| P-tiff-review | 最终TIFF审核 / review | REVIEW | 124 | SCRUM-11205 |
| P-white-preview | TIFF白墨预览 / normal | REVIEW | 124 | SCRUM-11205 |
| P-png-final | 素材PNG收尾 / normal | SAVE | 113 | SCRUM-11206 |
| P-png-save | 保存已批准PNG / normal | SAVE | 113 | SCRUM-11206 |
| P-save | 保存结果 / normal | SAVE | 113 | SCRUM-11206 |
| P-multi-size | 多尺寸结果 / normal | SAVE | 113 | SCRUM-11206 |
| P-saved | 保存成功 / normal | SAVE | 113 | SCRUM-11206 |
| P-png-saved | PNG保存成功 / normal | SAVE | 113 | SCRUM-11206 |
| P-completed | 任务完成 / normal | COMPLETION | 29 | SCRUM-11206 |
| P-correction-prepare | 准备同事修正 / normal | COLLEAGUE | 57 | SCRUM-11206 |
| P-correction-wait | 等待修正与回导 / recovery | COLLEAGUE | 57 | SCRUM-11206 |
| P-handed-off | Photoshop已接管 / recovery | RECOVERY | 56 | SCRUM-11206 |
| P-recovery | 中断后恢复 / recovery | RECOVERY | 56 | SCRUM-11206 |
| P-failure | 处理失败与重试 / error | FAILURE | 92 | SCRUM-11206 |
| P-stopping | 正在安全停止 / recovery | STOP-TAKEOVER | 29 | SCRUM-11206 |
| P-takeover | Photoshop接管确认 / confirmation | STOP-TAKEOVER | 29 | SCRUM-11206 |
| P-save-failed | 保存失败 / error | SAVE | 113 | SCRUM-11206 |
| P-save-uncertain | 保存结果待核实 / normal | SAVE | 113 | SCRUM-11206 |
| P-collision | 同名文件冲突 / error | SAVE | 113 | SCRUM-11206 |
| P-return-confirm | 返回重做确认 / confirmation | CONFIRMATION | 77 | SCRUM-11206 |
| P-draft-leave | 离开未提交输入 / confirmation | CONFIRMATION | 77 | SCRUM-11206 |
| P-abandon | 离开与放弃确认 / confirmation | ABANDON | 9 | SCRUM-11206 |
| P-enlargement | 放大风险确认 / confirmation | PREFLIGHT | 36 | SCRUM-11205 |
| P-input-error | 导入校验失败 / error | HOME-IMPORT | 90 | SCRUM-11204 |
| P-readiness | 环境检查 / normal | ENVIRONMENT | 98 | SCRUM-11206 |
| P-settings | 设置 / normal | SETTINGS | 36 | SCRUM-11206 |
| P-recent | 最近任务 / normal | RECENT | 35 | SCRUM-11206 |
| P-error-details | 错误详情与诊断 / normal | ERROR-DETAILS | 83 | SCRUM-11206 |
| P-ai-planned | AI设计分步接入 / conditional | AI-CONDITIONAL | 0 | SCRUM-11206 |
| S-home-empty | 首页无最近任务 / empty | HOME-IMPORT | 90 | SCRUM-11204 |
| S-home-importing | 首页正在导入 / loading | HOME-IMPORT | 90 | SCRUM-11204 |
| S-home-disabled | 首页导入/继续被禁用 / disabled | HOME-IMPORT | 90 | SCRUM-11204 |
| S-recent-empty | 最近任务空列表 / empty | RECENT | 35 | SCRUM-11206 |
| S-recent-history | 最近任务有保存历史 / history | RECENT | 35 | SCRUM-11206 |
| S-recent-pending | 最近任务待处理/恢复 / recovery | RECENT | 35 | SCRUM-11206 |
| S-route-unselected | 路线尚未选择 / disabled | ROUTE | 27 | SCRUM-11204 |
| S-route-asset | 准备设计素材路线 / normal | ROUTE | 27 | SCRUM-11204 |
| S-route-customer | 处理客户设计路线 / normal | ROUTE | 27 | SCRUM-11204 |
| S-route-tiff | 生成打印TIFF路线 / normal | ROUTE | 27 | SCRUM-11204 |
| S-pdf-pending | PDF单页准备待开始 / normal | SOURCE | 50 | SCRUM-11204 |
| S-pdf-running | PDF受管副本准备中 / loading | SOURCE | 50 | SCRUM-11204 |
| S-psd-pending | PSD栅格副本待开始 / normal | SOURCE | 50 | SCRUM-11204 |
| S-source-prepared-review | PDF/PSD准备后审核 / review | SOURCE | 50 | SCRUM-11204 |
| S-source-failed | PDF/PSD准备失败 / error | SOURCE | 50 | SCRUM-11204 |
| S-enhance-skip | 增强可跳过 / normal | PROCESSING | 55 | SCRUM-11204 |
| S-background-confirm | 自动去背景确认 / confirmation | CONFIRMATION | 77 | SCRUM-11204 |
| S-background-skip | 去背景可跳过 / normal | PROCESSING | 55 | SCRUM-11204 |
| S-review-disabled | 审核资格/身份缺失 / disabled | REVIEW | 124 | SCRUM-11204 |
| S-review-history | 只读历史审核 / history | REVIEW | 124 | SCRUM-11204 |
| S-review-rejected | 审核拒绝后重试 / error | REVIEW | 124 | SCRUM-11204 |
| S-crop-invalid | 裁切输入无效 / disabled | CROP | 100 | SCRUM-11204 |
| S-crop-cancel | 取消裁切草稿 / normal | CROP | 100 | SCRUM-11204 |
| S-trim-ready | 自动裁切参数与留边 / normal | CROP | 100 | SCRUM-11204 |
| S-crop-review-return | 使用裁切后返回审核 / review | REVIEW | 124 | SCRUM-11204 |
| S-size-invalid | 印刷尺寸输入无效 / disabled | DIMENSIONS | 65 | SCRUM-11205 |
| S-size-custom | 自定义印刷尺寸 / normal | DIMENSIONS | 65 | SCRUM-11205 |
| S-white-none | 白墨尚未选择 / disabled | WHITE-INK | 15 | SCRUM-11205 |
| S-white-0 | 白墨内缩0像素及理由 / confirmation | WHITE-INK | 15 | SCRUM-11205 |
| S-white-1 | 白墨内缩1像素及理由 / confirmation | WHITE-INK | 15 | SCRUM-11205 |
| S-white-2 | 白墨内缩2像素及理由 / confirmation | WHITE-INK | 15 | SCRUM-11205 |
| S-preflight-blocked | 生成前条件未齐 / disabled | PREFLIGHT | 36 | SCRUM-11205 |
| S-tiff-failed | TIFF生成或校验失败 / error | PROCESSING | 55 | SCRUM-11205 |
| S-tiff-review-history | 旧尺寸TIFF审核历史 / history | REVIEW | 124 | SCRUM-11205 |
| S-stop-requested | 停止请求已发出 / recovery | STOP-TAKEOVER | 29 | SCRUM-11206 |
| S-stop-finished | 尝试停止后待重试 / recovery | STOP-TAKEOVER | 29 | SCRUM-11206 |
| S-takeover-requested | Photoshop接管请求处理中 / loading | STOP-TAKEOVER | 29 | SCRUM-11206 |
| S-correction-copy | 同事修正副本准备中 / loading | COLLEAGUE | 57 | SCRUM-11206 |
| S-correction-failed | 同事修正准备失败 / error | COLLEAGUE | 57 | SCRUM-11206 |
| S-correction-import-review | 修正回导后待审核 / review | COLLEAGUE | 57 | SCRUM-11206 |
| S-reentry | 自动化重新进入 / confirmation | RECOVERY | 56 | SCRUM-11206 |
| S-completed-abandoned | 任务已放弃 / history | COMPLETION | 29 | SCRUM-11206 |
| S-readiness-unknown | 环境尚未检查 / empty | ENVIRONMENT | 98 | SCRUM-11206 |
| S-readiness-running | 环境检查进行中 / loading | ENVIRONMENT | 98 | SCRUM-11206 |
| S-readiness-blocked | 环境阻塞 / error | ENVIRONMENT | 98 | SCRUM-11206 |
| S-readiness-ready | 当前环境已就绪 / normal | ENVIRONMENT | 98 | SCRUM-11206 |
| S-settings-production | 生产设置只读 / disabled | SETTINGS | 36 | SCRUM-11206 |
| S-error-empty | 无错误详情 / empty | ERROR-DETAILS | 83 | SCRUM-11206 |
| S-error-expanded | 错误技术详情展开 / error | ERROR-DETAILS | 83 | SCRUM-11206 |
| S-dialog-input-file | 选择输入文件对话框 / confirmation | HOME-IMPORT | 90 | SCRUM-11204 |
| S-dialog-output-folder | 选择保存目录对话框 / confirmation | SAVE | 113 | SCRUM-11206 |
| S-dialog-correction-file | 选择修正回导文件对话框 / confirmation | COLLEAGUE | 57 | SCRUM-11206 |
| S-dialog-diagnostic-save | 诊断包保存对话框 / confirmation | DIAGNOSTIC-DIALOG | 46 | SCRUM-11206 |
| S-dialog-automatic-selection | 自动选择授权对话框 / confirmation | CONFIRMATION | 77 | SCRUM-11204 |
| S-dialog-trim-leave | 离开裁切草稿确认 / confirmation | CROP | 100 | SCRUM-11206 |
| S-source-tiff-review | 生成打印TIFF原图设计审核 / review | SOURCE | 50 | SCRUM-11204 |
| S-meitu-takeover | 美图接管确认 / confirmation | STOP-TAKEOVER | 29 | SCRUM-11206 |
| S-meitu-takeover-requested | 美图接管请求处理中 / loading | STOP-TAKEOVER | 29 | SCRUM-11206 |
| S-source-preparation-takeover | PSD/PDF源准备接管确认 / confirmation | STOP-TAKEOVER | 29 | SCRUM-11206 |
| S-source-preparation-takeover-requested | PSD/PDF源准备接管请求处理中 / loading | STOP-TAKEOVER | 29 | SCRUM-11206 |
| F-PNG-None | PNG保存 · None / disabled | FINAL-SAVE-None | 1 | SCRUM-11206 |
| F-PNG-HistoryUnknown | PNG保存 · HistoryUnknown / history | FINAL-SAVE-HistoryUnknown | 3 | SCRUM-11206 |
| F-PNG-Pending | PNG保存 · Pending / disabled | FINAL-SAVE-Pending | 3 | SCRUM-11206 |
| F-PNG-PngPreparation | PNG保存 · PngPreparation / disabled | FINAL-SAVE-PngPreparation | 1 | SCRUM-11206 |
| F-PNG-Ineligible | PNG保存 · Ineligible / disabled | FINAL-SAVE-Ineligible | 1 | SCRUM-11206 |
| F-PNG-ApprovedNotSaved | PNG保存 · ApprovedNotSaved / normal | FINAL-SAVE-ApprovedNotSaved | 2 | SCRUM-11206 |
| F-PNG-Failed | PNG保存 · Failed / error | FINAL-SAVE-Failed | 2 | SCRUM-11206 |
| F-PNG-Collision | PNG保存 · Collision / error | FINAL-SAVE-Collision | 3 | SCRUM-11206 |
| F-PNG-Uncertain | PNG保存 · Uncertain / normal | FINAL-SAVE-Uncertain | 3 | SCRUM-11206 |
| F-PNG-Saved | PNG保存 · Saved / normal | FINAL-SAVE-Saved | 3 | SCRUM-11206 |
| F-PNG-SavedPreviously | PNG保存 · SavedPreviously / history | FINAL-SAVE-SavedPreviously | 2 | SCRUM-11206 |
| F-PNG-Missing | PNG保存 · Missing / error | FINAL-SAVE-Missing | 2 | SCRUM-11206 |
| F-PNG-Changed | PNG保存 · Changed / error | FINAL-SAVE-Changed | 2 | SCRUM-11206 |
| F-PNG-Unavailable | PNG保存 · Unavailable / error | FINAL-SAVE-Unavailable | 2 | SCRUM-11206 |
| F-PNG-Working | PNG保存 · Working / loading | FINAL-SAVE-Working | 5 | SCRUM-11206 |
| F-TIFF-None | TIFF保存 · None / disabled | FINAL-SAVE-None | 1 | SCRUM-11206 |
| F-TIFF-HistoryUnknown | TIFF保存 · HistoryUnknown / history | FINAL-SAVE-HistoryUnknown | 3 | SCRUM-11206 |
| F-TIFF-Pending | TIFF保存 · Pending / disabled | FINAL-SAVE-Pending | 3 | SCRUM-11206 |
| F-TIFF-Ineligible | TIFF保存 · Ineligible / disabled | FINAL-SAVE-Ineligible | 1 | SCRUM-11206 |
| F-TIFF-ApprovedNotSaved | TIFF保存 · ApprovedNotSaved / normal | FINAL-SAVE-ApprovedNotSaved | 2 | SCRUM-11206 |
| F-TIFF-Failed | TIFF保存 · Failed / error | FINAL-SAVE-Failed | 2 | SCRUM-11206 |
| F-TIFF-Collision | TIFF保存 · Collision / error | FINAL-SAVE-Collision | 3 | SCRUM-11206 |
| F-TIFF-Uncertain | TIFF保存 · Uncertain / normal | FINAL-SAVE-Uncertain | 3 | SCRUM-11206 |
| F-TIFF-Saved | TIFF保存 · Saved / normal | FINAL-SAVE-Saved | 3 | SCRUM-11206 |
| F-TIFF-SavedPreviously | TIFF保存 · SavedPreviously / history | FINAL-SAVE-SavedPreviously | 2 | SCRUM-11206 |
| F-TIFF-Missing | TIFF保存 · Missing / error | FINAL-SAVE-Missing | 2 | SCRUM-11206 |
| F-TIFF-Changed | TIFF保存 · Changed / error | FINAL-SAVE-Changed | 2 | SCRUM-11206 |
| F-TIFF-Unavailable | TIFF保存 · Unavailable / error | FINAL-SAVE-Unavailable | 2 | SCRUM-11206 |
| F-TIFF-Working | TIFF保存 · Working / loading | FINAL-SAVE-Working | 5 | SCRUM-11206 |
| AI-requirements | 条件AI · 需求与模板 / conditional | AI-CONDITIONAL | 0 | SCRUM-11206 |
| AI-attachments | 条件AI · 附件及实际上传清单 / conditional | AI-CONDITIONAL | 0 | SCRUM-11206 |
| AI-prompt | 条件AI · Prompt编辑与确认 / conditional | AI-CONDITIONAL | 0 | SCRUM-11206 |
| AI-generation | 条件AI · 上传/生成进度 / conditional | AI-CONDITIONAL | 0 | SCRUM-11206 |
| AI-candidate | 条件AI · 候选校验与确认 / conditional | AI-CONDITIONAL | 0 | SCRUM-11206 |
| AI-import | 条件AI · 候选导入与手动交接 / conditional | AI-CONDITIONAL | 0 | SCRUM-11206 |
