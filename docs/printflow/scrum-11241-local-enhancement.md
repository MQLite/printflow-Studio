# SCRUM-11241 · 本地轻度锐化与 2× 超分

## 运行规格

| 方法 | 运行时与算法 | 支持范围 |
| --- | --- | --- |
| 轻度锐化 | Magick.NET Q16 x64 15.1.0，反锐化半径自动、σ 0.7、增益 0.45、阈值 0.015 | RGB 或灰度 PNG／JPEG，每通道最高 16 位。每边 ≤ 8,192 像素，总计 ≤ 2,400 万像素，文件 ≤ 128 MiB。按 EXIF 方向摆正后保持显示尺寸，保留受支持的 ICC 和原始 Alpha 样本。 |
| AI 超分 | RealESRGAN_x2plus、ONNX Runtime 1.32.0 CPU 执行提供程序 | RGB 或灰度 PNG／JPEG，每通道最高 8 位。每边 ≤ 1,024 像素且总计 ≤ 262,144 像素，输出宽高各翻倍。Windows x64 和至少 1.5 GiB 可用系统内存，单次推理最多使用四个 CPU 线程，无显卡要求。同一进程一次运行一个超分。 |

超分将 EXIF 方向应用到像素，生成的 PNG 不携带原始 EXIF 其他字段。带 ICC 的输入转换到 sRGB 后推理，输出转换回输入的 ICC。完全透明区的 RGB 由邻近可见像素延伸填充，Alpha 独立作双线性放大。两种方法输出候选 PNG，由原工作流审核。尝试记录方法、算法版本、输入哈希和处理前后像素尺寸。

## 开发与验证所需模型

将模型资产放在待运行的 `PrintFlow.App.exe` 旁的 `Models\real_esrgan_x2.onnx`。应用从该路径读取并校验，不自动下载。测试可以通过环境变量 `PRINTFLOW_B1_MODEL` 指向任意位置的同一文件。安装包纳入模型和干净工作站的离线验证属于 [SCRUM-11244](https://yituoxx.atlassian.net/browse/SCRUM-11244)。

- 模型是原生 2× 的 `RealESRGAN_x2plus`，原权重发布于 v0.2.1。固定 SceneWorks ONNX 导出版本 `09f741bac80a246b407da3ee902bf5f3291b602f`，ONNX opset 17，FP32。输入和输出均为 NCHW RGB，输入归一化到 [0,1]。 [上游模型定义](https://github.com/xinntao/Real-ESRGAN/blob/master/docs/model_zoo.md) · [ONNX 导出说明](https://huggingface.co/SceneWorks/real-esrgan-onnx) · [固定版本下载](https://huggingface.co/SceneWorks/real-esrgan-onnx/resolve/09f741bac80a246b407da3ee902bf5f3291b602f/real_esrgan_x2.onnx)
- 文件大小为 67,073,434 字节。SHA-256 为 `7115ba92e8a1bfa63d68558ef006ef3d91273a068d321b1439f8bb1c9179002c`。上游权重和导出方声明采用 BSD-3-Clause。 [上游许可](https://github.com/xinntao/Real-ESRGAN/blob/master/LICENSE)
- 模型缺失、身份校验不通过、系统内存不足或输入超限时，可选择轻度锐化或跳过。首版超分的 8 位限制会提示更换方式，16 位输入可用轻度锐化保留位深。

## 样图对照

| 样图 | 原图／锐化 | 2× 超分 | 观察 |
| --- | --- | --- | --- |
| 低清人像 | 68 × 74 | 136 × 148 | 超分使眼睛和发丝更清晰，也重新刻画了眼部和牙齿细节。 |
| 透明底细字 Logo | 168 × 88 | 336 × 176 | 锐化保留字样。超分让大字边缘更平滑，小字 `Fine lettering` 的部分笔画发生变化。 |

人像是 NASA Eileen Collins 公共领域照片的降采样裁切，Logo 是测试生成图。两种方法使用同一对输入样图。 [人像来源](https://scikit-image.org/docs/0.20.x/api/skimage.data.html#skimage.data.astronaut)

运行 `LocalEnhancementTests` 复核工作流、输入限制和保真行为。设置 `PRINTFLOW_B1_MODEL` 后运行 `LocalEnhancementModelSmoke` 进行真实 CPU 推理、取消及回退验证。可选 `PRINTFLOW_B1_SAMPLES` 指向包含 `portrait.png` 和 `logo.png` 的目录，将两种方法的输出保存到同目录用于对照。
