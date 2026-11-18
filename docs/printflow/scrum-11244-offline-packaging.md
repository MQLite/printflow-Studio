# SCRUM-11244 离线打包

Windows x64 MSI 内含两个固定模型、ONNX Runtime CPU、Magick.NET Q16 和本地 VC++ 运行库。启动后从程序旁的 `Models` 读取模型。已有美图、Photoshop 和蒙泰仍使用各自的工作站安装。

## 准备构建输入

在持有适用 Visual Studio 分发许可的构建环境中准备下列目录。模型和 DLL 均按 `installer/payload-policy.json` 中的 SHA‑256 校验。安装目录内的 `Licenses` 保存模型与运行时的许可及第三方声明。

```text
offline-assets/
  Models/
    real_esrgan_x2.onnx
    birefnet-lite-1024.onnx
  msvcp140.dll
  msvcp140_1.dll
  vcruntime140.dll
  vcruntime140_1.dll
```

- Real-ESRGAN x2plus 使用 SceneWorks 固定导出 `09f741bac80a246b407da3ee902bf5f3291b602f`，BSD-3-Clause。[模型卡](https://huggingface.co/SceneWorks/real-esrgan-onnx/blob/09f741bac80a246b407da3ee902bf5f3291b602f/README.md) · [固定权重](https://huggingface.co/SceneWorks/real-esrgan-onnx/resolve/09f741bac80a246b407da3ee902bf5f3291b602f/real_esrgan_x2.onnx) · [原始许可](https://github.com/xinntao/Real-ESRGAN/blob/v0.2.1/LICENSE)
- BiRefNet Lite 使用 AssetFurnace 固定导出 `7d8f1d2b2bb8ece3c6feeefc1a10682fd5de53b0`，MIT。[固定权重](https://huggingface.co/AssetFurnace/birefnet-lite-onnx/resolve/7d8f1d2b2bb8ece3c6feeefc1a10682fd5de53b0/birefnet-lite-1024.onnx) · [导出许可](https://huggingface.co/AssetFurnace/birefnet-lite-onnx/blob/7d8f1d2b2bb8ece3c6feeefc1a10682fd5de53b0/LICENSE) · [原始模型](https://huggingface.co/ZhengPeng7/BiRefNet_lite/tree/aa62cd87eafb9cc43056d08ef3615a14628b831d)
- VC++ 使用 Visual Studio 2026 REDIST 中的 14.51.36247.0 x64 文件。官方 VSIX SHA‑256 为 `24A1C8DBA0376D9B008AC2BA430D13C06664FC6F163511FD7866485030E289AF`。在包内的 `Contents/VC/Redist/MSVC/<版本>/x64/Microsoft.VC145.CRT` 取上述四个文件。[微软 REDIST 清单](https://learn.microsoft.com/en-us/visualstudio/releases/2026/redistribution) · [固定来源包](https://download.visualstudio.microsoft.com/download/pr/3984a6ef-2ffc-4c07-962e-bf9e9b862819/24a1c8dba0376d9b008ac2ba430d13c06664fc6f163511fd7866485030e289af/Microsoft.VC.14.51.CRT.Redist.X64.base.vsix)
- ONNX Runtime 1.32.0 与 Magick.NET 15.1.0 由现有 NuGet 引用提供。发布时从还原的包复制许可与第三方声明，并将 Magick.NET 的 Apache-2.0 许可收入包内。[ONNX Runtime](https://onnxruntime.ai/docs/install/) · [Magick.NET 15.1.0 许可](https://github.com/dlemstra/Magick.NET/blob/15.1.0/License.txt)

```powershell
.\build\installer\Build-Installer.ps1 -OfflineAssetsDirectory 'D:\offline-assets'
```

每次构建都要提供资产目录，包括使用 `-SkipPublish` 时。构建清单记录 MSI SHA‑256、全部打包路径和 6 个外部资产的 SHA‑256。

## 工作站验收

1. 在干净的 Windows x64 系统断网后安装，验证本地轻度锐化、2× 超分、BiRefNet 和三种裁切方式。验收 BiRefNet 时确认至少有 4 GiB 可用物理内存。
2. 在独立测试安装中移出一个模型，再重启并验证对应方法的提示与替代路径。分别验证超分可改用轻度锐化或跳过，以及 BiRefNet 可切换方法或转为手工处理。移出 ONNX Runtime 的独立试验应显示运行库提示。恢复原文件或使用 MSI 修复后重新确认就绪。
3. 在有已验证外部应用的工作站用原标准回归脚本分批跑，并保存每批结果。用经授权的同批样图对比本地与美图，每类至少三张。保存原图、产物、裁切几何、耗时、峰值内存及五官、文字和透明边观察。
