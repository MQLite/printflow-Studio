# E6 隔离候选：静态验证完成，运行准备未执行

候选ID：`e6-20261022-review`。`dotnet build` 退出0、0警告/0错误；现有launcher的 `Validate` 退出0，返回 `STATIC_VALIDATED`、`GraphConstructed=false`；runtime run root不存在。**PrepareAndSmoke、Interactive、真实新手、实际工作站、外部软件及生产全部 NOT RUN。** 静态成功不意味着可直接启动新手走查。

## 身份与证据

| 项目 | 已记录的身份 |
| --- | --- |
| 构建源码提交 | `d21c14497697ead627386a5f0ac3f39a7bb4ea60` |
| 产品/测试冻结检查点 | `24f2b3bb18a6ae96f34fa39a572bd7a9dd9e8ebe`；到构建提交仅文档/证据差异 |
| 候选manifest SHA-256 | `9F12D97A3D15BCE05B265FA2574D9486206F71E44A08AACBC947A953800753BF` |
| 场景manifest SHA-256 | `77347307FF7BB455BA13B7B31436D71CFDD3CC7FF84A9F49A233DAD1FCDA7EB9` |
| 构建日志 SHA-256 | `0A2EE7A7FAEB82D831C623D023D24748128AC709E10760CF6BF30BB0E1E13ECD` |
| 构建工具 | .NET SDK10.0.400 / PowerShell7.6.5 / Debug |
| 绑定范围 | 809源码/资源文件 + 54构建产物，共863条文件哈希；scoped dirty state为空 |

[candidate-manifest.json](candidate-manifest.json) 保存绝对路径和所有文件哈希/MVID；[candidate-provenance.json](candidate-provenance.json) 保存源码范围、产物、构建日志及提交；[scenario-manifest.json](scenario-manifest.json) 声明Fake模式F1–F6；[build.log](build.log)、[generate.log](generate.log)、[validate.log](validate.log)、[validation-summary.json](validation-summary.json) 保存原始执行结果。二进制保留在本工作树 `artifacts/pf-opux-scrum11154-workstation-entry/build/e6-20261022-review`，不加入Git或复制到产品安装目录。构建可重复，二进制hash/MVID按实际新构建重新记录，不能承诺跨环境逐字一致。

| DLL | SHA-256 | MVID |
| --- | --- | --- |
| PrintFlow.App.dll | `1D23D6D04924BFE7131146E13FFC4EC200BCFCE75B887782DBC2F24EFCFB4782` | `7e6fe462-bda8-4aa3-8edb-1617adc5136e` |
| PrintFlow.Domain.dll | `517E8E9C1ABD46C2183D606B013F604927A8F0D49DD8F2E0257AA24494846513` | `380d626d-0731-42bb-ad8b-2d9cce133c97` |
| PrintFlow.Infrastructure.dll | `4613FA4C3D4B83F131EDF717A55464026E3CB4F91D9F07EA9CF937008DCF762C` | `59cdeb14-3ae4-4644-bbf9-1fe756b3cf3c` |
| PrintFlow.Workflow.dll | `4F7F50AB9556F18C5B5C13E7DEC885BC869D74D728335A0D64F659651F5F6330` | `f47fe163-0a6b-4125-970b-77dd65b6ddd9` |
| PrintFlow.WorkstationEntry.dll | `B3033557B41386A6DE32CFFED8A4B41D6F5093910AC3A76C8B49AC796A5A531C` | `1ea81b09-27fa-4ffa-ae55-dcdb6b5e1c9c` |

后续文档提交会推进分支HEAD；上表构建源码提交是不可变身份，不随文档HEAD改写。现有validator按manifest验证文件，不要求文档HEAD相等。源码/资源/运行二进制任何变化均须新建候选并重新验证；只改变此文档或审查记录不触发重建/重跑广泛测试。控制会话的源码冻结哈希与完整候选范围可共同复核。

## 可安全复核的静态命令

在指定E6工作树根目录，用PowerShell7执行：

```powershell
$e6Static = Get-Content -LiteralPath 'output/ui/SCRUM-11173/candidate/validation-summary.json' -Raw | ConvertFrom-Json -AsHashtable
$e6Arguments = $e6Static.Arguments
if ($e6Arguments.Mode -cne 'Validate') { throw 'Only static Validate is authorized here.' }
& ./tools/Start-WorkstationEntry.ps1 @e6Arguments
```

这复用本轮实际绝对参数：root为工作树内 `artifacts/pf-opux-scrum11154-workstation-entry/runs/e6-20261022-review`（本轮不存在），manifests位于同级 `manifests/e6-20261022-review`，host为 `build/e6-20261022-review/PrintFlow.WorkstationEntry.dll`。不创建root、不构造WPF图、不设置桌面许可。

独立审查的暂缓Minor E6-M1：[build-validate-candidate.ps1](../checks/build-validate-candidate.ps1) 虽保护build/manifest/log/run路径，却始终覆盖本目录内的证据；失败构建还可能与旧成功summary混在一起。**不要直接复用它重建。** 任何后续重建前先修正为按候选隔离的证据目录并保护已存在路径，验证旧包不变后才构建新候选。本轮保持现有候选、未再次构建；详见[审查记录](../../../../docs/codex/printflow-operator-ux-backlog/SCRUM-11173_REVIEW.md)。这不影响上述针对现有候选的只读静态校验。

## 负责人现场门槛与场景限制

[中文走查包](../../../../docs/printflow/guided-acceptance-walkthrough.md) 是负责人完整操作/停止与观察记录。另行获得隔离工作站授权后，负责人才能按它执行PrepareAndSmoke、核对ownership/typed prepared record/scenario ledger/graph smoke/startup-teardown，再在当次桌面明确许可下使用Interactive。PrepareAndSmoke虽然不Show，仍构造Application/MainWindow，因此本轮没有运行。失败/PARTIAL/BLOCKED_REQUIRED_SCENARIO或缺失匹配prepared record不得进入Interactive；不得通过编辑manifest或使用生产适配器绕过。

现有host只提供合成PNG/TIFF F1–F6，未准备PDF流程。F2/F3 headless准备显式使用ValidTiff，但Interactive是新进程，FakePhotoshop默认CopyApprovedInput仍可能产生N4的PNG字节改名TIFF；F6尺寸不一致负例和PNG KeepOriginalExtent/generic recovery缺口也未关闭。完整TIFF走查在当前Interactive生成、解码、精确批准、交付链经负责人资格验证前保持BLOCKED。F4回导须匹配当前任务/修订；预置Recent不证明参与者已完成前序步骤。没有本轮真人、实机或生产PASS。
