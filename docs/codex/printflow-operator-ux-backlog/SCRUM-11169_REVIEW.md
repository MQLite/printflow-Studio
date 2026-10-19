# SCRUM-11169 一次独立整体审查与处置

审查范围：`7fd87f6ef7ac349633af19aa04355def00c79b41..9fe125eaf3104049192f3c766278db60adab6ecb`，分支 `codex/scrum-11169-guided-shell`。审查者 `/root/e2_whole_branch_reviewer`，fork_turns:none，实际 gpt-6-astra/high 已从 session `01a1522f-e4d6-7560-9633-17f7ebf74092` 的运行元数据核实。只读完成，无子审查者，无工作区/索引/HEAD/分支修改；临时探针仅纯VM、合成文件、离屏WPF。

审查结论：**0 Critical / 6 Important / 1 Minor，修复前不建议合并。** 父会话按实际用户影响维持全部分级；6项Important进入原执行者的一次TDD修复，Minor仅延期。没有授权合并、推送、部署或后续Epic。

## Important

1. **重新确认同一白墨分支会卡在白墨页。** SessionViewModel.Guided.cs:77、SessionViewModel.cs:3496（审查目标行）。确认W1_1px→Preflight→修改白墨→再确认W1_1px，服务成功且持久值正确，但NavigationContext不变使_isChoosingGuidedWhiteInk保留true，page=WhiteInk/runVisible=false。成功提交必须结束编辑；失败保留草稿，不能只依赖业务值变化。
2. **草稿确认层期间顶部手动裁切仍可执行，继续编辑丢选区。** SessionScreenView.xaml:460。BeginManualCrop位于未被CanInteractWithLivePage禁用的上方行，命令也不检查模态。离屏实际provider复现：画[3,2→9,7)→Home草稿层→顶部按钮enabled/shown→Invoke→Keep，CropSelection=null。所有非紧急入口须纳入模态/历史防护，停止接管独立。
3. **已确认裁边留量仍当成草稿，Discard令UI与持久值不一致。** SessionViewModel.cs:3751、Navigation.cs:136。Tight0→Uniform2→Apply→历史，旧_cleanDraft仍Tight0；Discard后UI0，业务仍Uniform2。成功提交应推进草稿基线；普通刷新保留未提交输入，失败不推进。
4. **保留的非按钮UIA provider可以写新任务或隐藏表单。** SessionActionButton.cs:100、SessionScreenView.xaml:1586。A的TargetMillimetres旧IValueProvider在view重绑定B后仍可SetValue并改B；隐藏尺寸表单仍enabled且可写。按钮Invoke保护不能覆盖Value/Selection/Toggle等可变输入；须覆盖相关控件或安全退休整个旧编辑子树，保留合法当前输入和业务资格。
5. **保存成功后草稿基线仍停在首次编辑前。** FinalSave.cs:265、Navigation.cs:139。成功保存confirmed-output.png与folder→再编辑→历史/Discard，UI错误回keep-extent.png/nullfolder；交付事实仍正确。成功保存对应意图后推进/结束基线，后续编辑从最近已确认name/folder捕获，不能清掉事实或未决保存身份。
6. **保存A的忙碌所有权未在Open(B)时退休。** SessionViewModel.cs:2117、FinalSave.cs:703。延迟A.DeliverAsync→同VM.Open(B)，B仍IsBusy=true且确认源图被拒绝，直到A.finally清除。普通导航新VM降低暴露面，但E2明确要求同VM.Open所有权合同。保存路径必须同样代际隔离，A的实际保存/事实仍归A，旧finally不能控制B。

原始复现源码和输出已保留在 `output/ui/SCRUM-11169/review/Program.cs` 与 `probe-output.txt`；仅证据，不是产品源码/测试入口。原临时目录为 `C:/Users/admin/AppData/Local/Temp/printflow-e2-readonly-review-20261019`。

## Minor（延期）

GuidedSessionVisualEvidenceTests.cs:153：ReferenceEquals(b.Command, model.RecommendedCommand)在两者null时为true，部分处理态JSON把ReviewActualSize标为Recommended=true。实际按钮不被突出。这是证据标记错误，本次按Native只修Important规则延期；引用“推荐”字段必须结合命令非空判断，不能以该JSON独立证明实际推荐按钮。成本：证据消费者可能误读元数据。

## 五项 Review Focus

- 按住键/双击/保留provider：按钮有防护，非按钮输入缺口见4；真实设备未验证，Space波动未解释。
- A迟到结果与B：普通服务成功/失败/重载/runtime/finally有覆盖，保存遗漏见6。
- 历史只读/返回捕获：未发现历史结果被当当前审批或确认被重定向的缺陷。
- 五类草稿与刷新：基础路径有覆盖，边界见1/2/3/5。
- 小窗口/长中文/固定操作：所查离屏96DPI图片和测量支持覆盖状态下可达，不能推及全部DPI或真实窗口。

审查者查看12张PNG、抽查7份主测量JSON，未称55张全看。核对原TRX安全UI1314/14、持久化277/0、布局145/0、提交后Guided55/0，未重复广跑。父会话另独立复验52/52、核对14个故障同名同消息、31源码/55PNG/39JSON/53动作记录，并看7张代表图；这些检查在修复前HEAD，修复后须另记录。

## Declined to judge 与父会话裁定

1. **可见Tab焦点失败的最终用户影响**：确有confirm.IsKeyboardFocused=false原始失败，缺少焦点轨迹，不能区分顺序合同变化与不可达。父会话裁定：保留UNVERIFIED与原TRX；禁止再开窗口，后续离屏PASS不作为修复证据。成本：真实键盘可达性须后续人工验证，本次不能据此宣称生产就绪。
2. **Space新输入偶发未激活根因**：旧手势拒绝断言通过，但一次新激活计数0而非1。父会话裁定：保留未解释波动，不归入已修复或已证实跨任务审批。成本：偶发输入可靠性尚未证实，后续真人验证仍必要。
3. **14项继承失败的底层修复**：父会话裁定不扩修E1故障，保持精确名称/消息比对，广域集合不能称PASS。成本：遗留问题持续存在。
4. **真实UIA/物理输入/多DPI/多屏/新手中文走查**：父会话裁定均NOT RUN，合成provider和96DPI不能替代。成本：人工/设备验收仍欠缺。
5. **真实Meitu/Photoshop/客户文件/生产TIFF/CMYK/W1/发布就绪**：父会话裁定本次无生产验证依据，不执行或推断。成本：生产环境结果需要另行验收。

## 一次修复状态

2026-10-19：6项Important均交回原Astra/high执行者，以逐项RED→GREEN、一次安全广域回归及相关行为验证完成。没有第二审查或修Minor。最终修复提交、用例和结果待交回后补记。
