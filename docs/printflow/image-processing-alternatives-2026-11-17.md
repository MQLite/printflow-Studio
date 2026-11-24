# PrintFlow Studio 图像处理备选方案

核对日期：2026-11-17。优先考虑 Windows 工作站、商用出图、低调用成本和现有蒙泰兼容性。候选效果基于官方能力和仓库契约评估，尚未用同批样图跑基准或试印。

## 现有流程与输出契约

- 变清晰与去背景由美图自动化适配器执行，工作流已经有作业输入、处理结果和审核入口。自动裁切由 PrintFlow 的 `DeterministicAlphaTrimProcessor` 在本地按 Alpha 边界计算，支持边距、抗噪策略和人工调整。建议直接复用这段逻辑。 [适配器接口](../../src/PrintFlow.Workflow/Ports/AdapterPorts.cs) · [本地裁切实现](../../src/PrintFlow.Infrastructure/Imaging/DeterministicAlphaTrimProcessor.cs)
- 普通 TIFF 可定义为 RGB、灰度或四色 CMYK，按用途选择 ICC 与编码参数。当前生产 TIFF 的校验要求是小端 classic TIFF、300 PPI、CMYK 加一个名为 `W1` 的独立白墨专色、五个 8-bit 交错样本、无压缩主图、无金字塔、Photoshop 标签 `34377` 和含 RLE 图层的 `37724`。W1 样本中 255 代表无白墨，0 代表满白墨。现行流程由操作员选 0、1 或 2 px 向内缩边。 [TIFF 校验](../../src/PrintFlow.Infrastructure/Adapters/Photoshop/ProductionTiffInspector.cs) · [白墨标度](../../src/PrintFlow.Infrastructure/Adapters/Photoshop/ProductionTiffReviewDecoder.cs) · [白墨分支](../../src/PrintFlow.Domain/Outputs/WhiteUnderbaseBranch.cs)
- 在生产端复刻 Photoshop Action 时，先取同一受验工作站的目标 ICC、渲染意图、黑点补偿设置、Action 产物和样图作为基线。普通 CMYK ICC、设备实际印色、W1 分版和蒙泰导入需逐项核验。现有蒙泰对照记录说明界面预览只能显示彩色，白墨实际表现需要用分版手段或试印验证。 [MVP 输出约定](../../PRINTFLOW_STUDIO_MVP_DESIGN.md) · [蒙泰对照记录](scrum-11134-maintop-reference-comparison-fixed-workstation.md) · [Adobe TIFF 扩展规格](https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/)

## 本地组件

| 步骤 | 首选 | 可对照的组件与取舍 |
| --- | --- | --- |
| 变清晰 | Magick.NET 的重采样和轻度反锐化用于原图已清楚、细字和徽标。明显低清的照片对比 Real-ESRGAN Windows 便携程序，测试 `realesrnet-x4plus` 和 `realesrgan-x4plus`。 | SwinIR 有去噪和去 JPEG 压缩痕迹的模型。GFPGAN 用于严重退化的人像，生成式人脸修复需审核相貌。便携 Real-ESRGAN 依赖 Vulkan 驱动，需对照接缝、耗时和显存，显卡不适合时可回退传统处理。 [ImageMagick 选项](https://imagemagick.org/command-line-options/) · [Real-ESRGAN](https://github.com/xinntao/Real-ESRGAN) · [SwinIR](https://github.com/JingyunLiang/SwinIR) · [GFPGAN](https://github.com/TencentARC/GFPGAN) |
| 去背景 | C# ONNX Runtime 加 BiRefNet Lite／General。把经审核的 Alpha 蒙版应用到原尺寸图，保留原图的主体像素和坐标。人像可额外对照专用的 MODNet。 | U²-Net／U²-NetP 可作为轻量基线。多主体或自动抠错时，可评估点击和框选驱动的 SAM 2.1，再配合 Alpha 边缘处理。rembg 可用作同机进程或自托管 HTTP 试验，显式指定模型，固定权重版本和哈希。 [ONNX Runtime C#](https://onnxruntime.ai/docs/get-started/with-csharp.html) · [BiRefNet Lite](https://huggingface.co/ZhengPeng7/BiRefNet_lite) · [MODNet](https://github.com/ZHKKKe/MODNet) · [U²-Net](https://github.com/xuebinqin/U-2-Net) · [SAM 2](https://github.com/facebookresearch/sam2) · [rembg](https://github.com/danielgatis/rembg) |
| 裁切边缘 | 透明四周继续走现有 Alpha 裁切。留边、去孤立噪点、细发丝和人工调整沿用现有审核。 | 已知不透明且背景为纸白或其他均匀底色时，可评估 ImageMagick `-trim` 加颜色容差，预览后确认。它按角落颜色工作，需检查浅色设计是否被误切。 [现有实现](../../src/PrintFlow.Infrastructure/Imaging/DeterministicAlphaTrimProcessor.cs) · [ImageMagick trim](https://imagemagick.org/command-line-options/#trim) |
| RGB／Gray／CMYK 与普通 TIFF | Magick.NET 在 .NET 内转换 ICC 和编码 TIFF。使用素材的嵌入源 ICC 和实际选定的目标 ICC。源未标记时，先明确所用源配置。 | 想单独控制颜色变换时可用 Little CMS，配合 LibTIFF 或 LibTiff.NET 写 TIFF。大图批处理可对照 NetVips／libvips。 [Magick.NET](https://github.com/dlemstra/Magick.NET) · [ImageMagick 格式](https://imagemagick.org/formats/) · [Little CMS](https://www.littlecms.com/color-engine/) · [LibTIFF](https://libtiff.gitlab.io/libtiff/specification/coverage.html) · [LibTiff.NET](https://github.com/BitMiracle/libtiff.net) · [NetVips](https://github.com/kleisauke/net-vips) |
| 生产 TIFF | 已验证的 Photoshop Action 可作为迁移基线。全本地候选是 Little CMS／Magick.NET 做分色，LibTIFF／LibTiff.NET 写 CMYK 和 W1 样本及 Photoshop 扩展数据。 | 另有一条依赖印刷厂和 RIP 支持的路径，采用 RIP 原生生成白墨或单独提供白墨版。它需要独立的交付约定。 [现行生产约定](../../PRINTFLOW_STUDIO_MVP_DESIGN.md) · [Adobe TIFF 扩展规格](https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/) |

随安装包发放时按具体构件核对许可。Magick.NET 仓库为 Apache-2.0，Real-ESRGAN 主项目为 BSD-3-Clause，ncnn Vulkan 封装为 MIT。BiRefNet 作者确认代码和发布权重为 MIT，MODNet 仓库明确覆盖其代码和模型。rembg 的 MIT 许可覆盖运行器，BRIA RMBG-2.0 权重商用需要单独协议。 [Magick.NET](https://github.com/dlemstra/Magick.NET) · [Real-ESRGAN](https://github.com/xinntao/Real-ESRGAN) · [ncnn Vulkan 许可](https://github.com/xinntao/Real-ESRGAN-ncnn-vulkan/blob/master/LICENSE) · [BiRefNet 作者确认](https://github.com/ZhengPeng7/BiRefNet/issues/316) · [MODNet](https://github.com/ZHKKKe/MODNet#license) · [BRIA 模型卡](https://huggingface.co/briaai/RMBG-2.0)

## 云端 API

免费额度、账户资格和区域以控制台为准。

### OpenAI Images API

- `POST /v1/images/edits` 可用于操作员主动选择的老照片修复、去划痕和创意增强。GPT-Image-2.6 Sunburst 与 Flare 已于 2026 年 11 月 4 日发布。可提供原图、提示词与标注或遮罩。模型会生成像素，五官、服装细节、原图文字需由操作员对照检查。 [发布记录](https://developers.openai.com/api/docs/changelog) · [编辑接口](https://developers.openai.com/api/reference/resources/images/methods/edit)
- 交接可用 PNG 或 WebP，透明输出需要显式配置。常规栅格输出还支持 JPEG。色彩转换、精确边缘裁切和生产 TIFF 仍交给本地。 [输出规格](https://developers.openai.com/api/docs/guides/image-generation)
- 2.6 标准价格是文字输入 US$5、图片输入 US$8、图片输出 US$30，均按百万 token 计。实际单张成本随输入、输出尺寸与质量变化。合资格的付费 ChatGPT Pro／Business 可激活 API 促销额度，适用费用包含 Images。 [定价](https://developers.openai.com/api/docs/pricing) · [额度规则](https://help.openai.com/en/articles/20001626-api-credits-benefit-for-eligible-chatgpt-subscriptions)

### 专用服务与试用额度

| 候选 | 可承担的步骤 | 费用或接入条件 |
| --- | --- | --- |
| **withoutBG Pro API** | 去背景，直接返回 RGBA PNG，也可单独返回 alpha matte 供本地合成 | 注册送 50 次，有效期 30 天，再按量付费。厂商另提供自托管开放权重，包含不同许可的构件。官方称云端在法兰克福内存处理，响应后丢弃图片。 [接口](https://withoutbg.com/docs) · [价格](https://withoutbg.com/pro-model-api/pricing) · [处理说明](https://withoutbg.com/) · [权重许可](https://huggingface.co/withoutbg/withoutbg-openweights-onnx#license) |
| **Clipdrop / Jasper** | 超分和去背景。同步超分目标宽高各最多 4096，含透明度时返回 WebP，否则返回 JPEG | 文档列出 100 个开发调试积分，每次超分或去背景各 1 个。新申请可用性以账户为准。 [超分](https://clipdrop.co/apis/docs/image-upscaling) · [去背景](https://clipdrop.co/apis/docs/remove-background) · [Jasper 接入说明](https://clipdrop.co/apis) |
| **Photoroom** | 专用抠图 API，Image Editing API 可做超分、裁切和排版 | 两类 API 的 Sandbox 可免费调用，结果带水印。生产调用按订阅和成功处理的图片量计费。 [API 定价](https://docs.photoroom.com/getting-started/pricing) · [超分](https://www.photoroom.com/api/upscale) |
| **Pixian.AI** | 专用抠图，可用 .NET 通过 HTTP 试接，返回透明 PNG | `test=true` 可免费联调，实际处理按积分计费。 [API 文档](https://pixian.ai/api) |
| **Cloudinary** | 锐化、AI 超分、AI 去背景、`e_trim` 去边、`cs` 选 CMYK 或自定义 ICC、`f_tiff` 输出。印厂要求的 ICC、通道、分辨率标签和压缩组合需以样张核验 | 免费档 25 credits，与存储和带宽共享，普通图像最多 10 MB／25 MP。AI 去背景另计 75 次转换，AI 超分另计 10 或 100 次，取决于输入尺寸。 [变换参数](https://cloudinary.com/documentation/transformation_reference) · [计划上限](https://cloudinary.com/pricing/compare-plans) · [额度](https://cloudinary.com/documentation/developer_onboarding_faq_credits) · [计数规则](https://cloudinary.com/documentation/transformation_counts) |
| **腾讯云** | 图像处理提供清晰度增强与智能裁剪。数据万象另有通用／人像抠图和 2× 超分，需开通 COS 数据万象 | 清晰度增强和智能裁剪官方各列 1,000 次／月，开通后超额自动后付费。数据万象的商品抠图和超分各有一次性 1,000 次额度，有效两个月。人像或通用抠图的实际计费和地域按对应接口核对。 [图像处理 API](https://cloud.tencent.com/document/product/1590/81383) · [清晰度与裁切计费](https://cloud.tencent.com/document/product/1590/81379) · [抠图 API](https://cloud.tencent.com/document/product/460/106750) · [数据万象额度](https://cloud.tencent.com/document/product/460/36381) |
| **Leonardo.Ai / remove.bg** | 专用 `remove-bg` 模型，支持 PNG、JPG、WebP，可配置短期结果链接 | remove.bg 旧接口的 50 次／月是低分辨率预览，官方指示从 2026 年 12 月 1 日起迁移至 Leonardo.Ai。后者迁移文档列出 US$0.1047／图。 [预览额度与规格](https://www.remove.bg/api) · [迁移公告](https://www.remove.bg/zh/api.doc) · [迁移接口及计费](https://docs.leonardo.ai/docs/migrate-from-the-removebg-api-to-leonardoai) |
| **CloudConvert** | 可走转换 API 将 PNG 转成 TIFF。需要样张确认具体输入输出组合可设置所需 ICC、通道、PPI 和压缩 | 官网免费档面向个人、测试及业余项目，每日 10 个转换积分。可用于测试一般格式互转。 [定价](https://cloudconvert.com/pricing) · [格式支持](https://cloudconvert.com/png-to-tiff) · [转换 API](https://cloudconvert.com/api/v2/convert) |
| **Hugging Face Inference Providers** | 用于测试已有托管模型。先从模型页确认指定抠图或超分模型有可调用的提供商 | 免费账号的实验额度为 US$0.10／月，官方注明可能调整。 [额度规则](https://huggingface.co/docs/inference-providers/pricing) · [模型托管方式](https://huggingface.co/docs/hub/models-inference) |
| **Adobe Photoshop API v2** | 支持去背景、透明边裁切、ICC 色彩管理与 TIFF 输出。现有 W1 Action 等价性需以样张验证 | 官方接入指南要求包含 Firefly Services 的企业合同。 [功能](https://developer.adobe.com/firefly-services/docs/photoshop/) · [裁切](https://developer.adobe.com/firefly-services/docs/photoshop/guides/photoshop-v2-beta/v1-to-v2/document-operations-migration) · [TIFF](https://developer.adobe.com/firefly-services/docs/photoshop/guides/photoshop-v2-beta/v1-to-v2/v2-api-catalog) · [接入条件](https://developer.adobe.com/firefly-services/docs/photoshop/getting-started/) |

## 可选组合

| 路线 | 增强和去背景 | 裁切和输出 | 适用场景 |
| --- | --- | --- | --- |
| 1. 本地处理加现有生产输出 | Real-ESRGAN／传统锐化 + ONNX Runtime／BiRefNet | 现有 Alpha 裁切 + Photoshop Action | 先摆脱美图，生产端迁移范围最小 |
| 2. 云端处理加本地输出 | 腾讯云做清晰度增强，withoutBG／Photoroom 做抠图，各按适用条件使用 | 现有裁切，本地普通 TIFF 或现有 Photoshop Action | 工作站弱、单量低、已允许上传素材 |
| 3. 自托管处理服务 | 自有电脑或局域网 GPU 跑 Real-ESRGAN + rembg，桌面调用内部 HTTP | 现有裁切，工作站生成 TIFF | 多台工作站共用一台推理机器，避免每台安装模型 |
| 4. 全本地生产 | 路线 1 的模型，或路线 3 的自托管机器 | 现有裁切 + ICC 分色 + 自研 W1 TIFF 写入 | 要彻底脱离美图和 Photoshop，实施及印刷验证量最大 |
| 5. 单一托管服务 | Cloudinary 增强和去背景 | 同一服务裁切、颜色处理和普通 TIFF | 普通 TIFF、预览或打样，蒙泰 W1 仍需经过专用生产输出 |

## 优先比较顺序

1. 在同一台工作站用项目已有样图对比美图、传统锐化／Real-ESRGAN、BiRefNet／MODNet。覆盖低清人像、发丝、白色细边、文字和 Logo、多个主体，以及已有透明图。记录五官和文字变化、边缘漏切或白边、结果尺寸、耗时和峰值内存。云端优先对照能返回蒙版的接口，使用已获准上传的样图。
2. 确定处理后端时复用现有尝试、素材版本、预览和结果审核。现有端口和环境门禁带有美图／Photoshop 的执行语义，接入本地或 API 后端需要让门禁校验相应模型或服务。给普通 TIFF 独立预设，并保留可信的源配置、目标尺寸、渲染意图与输出 ICC。
3. 要替换 Photoshop 的生产输出时，先用同一批准素材和同一尺寸分别跑 0、1、2 px 白墨分支。验证目标毫米和 300 PPI、CMYK 分色和 W1 墨量及位置、标签和白墨极性，再用实际蒙泰环境导入并试印对照。PSD 光栅化和已有白墨复用也需要覆盖，因为它们进入现有生产流程。
