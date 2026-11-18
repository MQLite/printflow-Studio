# SCRUM-11242 · 本地 BiRefNet 去背景

## 安装与使用

将固定模型 `birefnet-lite-1024.onnx` 放到运行中的 `PrintFlow.App.exe` 所在目录下的 `Models` 子目录。模型就绪后，可在「设置 → 图像处理」设为去背景默认方法，也可在任务的去背景步骤中更换方法。应用只从本机加载模型。安装包集成与干净工作站回归属于 [SCRUM-11244](https://yituoxx.atlassian.net/browse/SCRUM-11244)。

- 权重为 BiRefNet Lite `aa62cd87eafb9cc43056d08ef3615a14628b831d`。采用 AssetFurnace 的 ONNX opset 19 导出 `7d8f1d2b2bb8ece3c6feeefc1a10682fd5de53b0`，使用标准 `DeformConv`。上游权重和导出均采用 MIT 许可。 [上游权重](https://huggingface.co/ZhengPeng7/BiRefNet_lite/tree/aa62cd87eafb9cc43056d08ef3615a14628b831d) · [导出模型卡](https://huggingface.co/AssetFurnace/birefnet-lite-onnx) · [固定版本下载](https://huggingface.co/AssetFurnace/birefnet-lite-onnx/resolve/7d8f1d2b2bb8ece3c6feeefc1a10682fd5de53b0/birefnet-lite-1024.onnx)
- 文件大小 180,666,557 字节。SHA-256 为 `a97176abb6fe55525b11113bf5bead5a9b5866b2c0d4ac058186cabb86cadfff`。每次推理验证实际传给 ONNX Runtime 的文件内容。尝试记录算法版本 `birefnet-lite-a97176ab-ort-1.32.0-cpu-v1`。
- 使用 Windows x64、ONNX Runtime 1.32.0 CPU 执行提供程序和 Magick.NET Q16。开始推理时至少需要 4 GiB 可用物理内存。每个进程同时处理一项 BiRefNet 去背景，推理使用最多四个 CPU 线程。支持 RGB 或灰度 PNG、JPEG，每通道最高 16 位，每边最多 8,192 像素，总计最多 2,400 万像素，文件最多 128 MiB。

## 像素和审核契约

1. 按 EXIF 方向摆正推理副本并转换为 sRGB。已有透明区域在推理副本上合成到中性灰。双线性缩放到 1,024 × 1,024，张量为 FP32 `input_image[1,3,1024,1024]`。RGB 先缩放到 0 至 1，再按 ImageNet 均值 `[0.485,0.456,0.406]` 和标准差 `[0.229,0.224,0.225]` 归一化。
2. 检查 FP32 `output_image[1,1,1024,1024]` 的形状与有限性。应用 sigmoid 后，以像素中心对齐的双线性采样逆映射到原始画布。每个输出 Alpha 为原 Alpha 乘蒙版覆盖率后量化。输出保留编码宽高、解码所得颜色样本、ICC 和 EXIF 方向。8 位及更低位深输出为 8 位，16 位输入输出为 16 位。
3. 模型无效、蒙版尺寸或张量形状不符、原可见区域上蒙版为常量、结果全透明、已有 Alpha 完全未变或 PNG 不可解码时，会拒绝候选并显示处理状态。已选择 BiRefNet 的任务可以切换方法，或走「转为手工处理」和「提交手工结果」。运行前的确认绑定输入 Revision 和 SHA-256，输出沿用现有候选 PNG 校验和审核。

## 验证

`LocalBackgroundMatteTests` 使用可控蒙版覆盖像素坐标、位深、ICC、EXIF、已有透明度、无效蒙版、取消和手工接续。在设置 `PRINTFLOW_B2_MODEL` 为上述模型路径、`PRINTFLOW_B2_SAMPLES` 为样图目录后，`LocalBackgroundMatteModelSmoke` 运行真实 CPU 推理。样图目录包含 `portrait.png` 和 `general.png`，测试还会从人像生成含已有半透明区域的 16 位样图。

- 人像使用 scikit-image 的 NASA Eileen Collins 公共领域照片。 [样图说明](https://scikit-image.org/docs/0.25.x/api/skimage.data.html#skimage.data.astronaut)
- 通用样图使用 Rachel Michetti 的 CC0 咖啡杯照片。 [样图说明](https://scikit-image.org/docs/0.25.x/api/skimage.data.html#skimage.data.coffee)

```powershell
dotnet build PrintFlowStudio.sln -c Release
dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj -c Release --filter 'FullyQualifiedName~LocalBackgroundMatte'
```

### 2026-11-18 本机验证

Windows x64 上 Release 构建通过，0 警告、0 错误。B2 专项通过 26 项，其中 3 项运行固定权重。以下耗时含模型加载，三个输出的宽高与解码 RGB 均与输入完全一致。

| 样图 | 输入与输出 | 耗时 | Alpha |
| --- | --- | --- | --- |
| 人像 | 512 × 512，8 位 | 7.92 s | 4,553 个半透明像素 |
| 咖啡杯 | 600 × 400，8 位 | 7.66 s | 3,364 个半透明像素 |
| 含原始 Alpha 的人像 | 512 × 512，16 位 | 8.23 s | 原透明区域保持透明，1,436 个半透明像素保留原值 |

独立代码复审已完成。像素回归覆盖 7 种非默认 EXIF 方向，确认 PNG 保留原始宽高及方向，蒙版与像素位置一致。
