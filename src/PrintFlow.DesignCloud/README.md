# PrintFlow 云端设计服务

配置服务后，桌面端的「OpenAI API」选项提交一个云端任务。服务用 Responses API 生图，用视觉模型对照原提示词及参考图审核。审核不通过时，服务按具体原因修正一次，然后复审。只有过审的 PNG 和 PDF 会回传。PDF 是同一张 PNG 的单页封装，保存时不会再次调用模型。

## 发布

云端需要 x64 主机和 .NET 10 ASP.NET Core Runtime。项目包含 Linux 和 Windows 所需的 Magick.NET 本机库。从仓库根目录构建 Linux 发布目录。

```sh
dotnet restore src/PrintFlow.DesignCloud/PrintFlow.DesignCloud.csproj --locked-mode
dotnet publish src/PrintFlow.DesignCloud/PrintFlow.DesignCloud.csproj -c Release -r linux-x64 --self-contained false --no-restore -o publish/design-cloud
```

在云端部署环境注入 `OPENAI_API_KEY` 和 `PRINTFLOW_DESIGN_CLOUD_TOKEN`。前者使用需要在 Platform Logs 查看记录的 OpenAI 项目密钥。后者使用至少 32 个字符的随机访问令牌，支持字母、数字、下划线和连字符。部署环境可通过 `Design__ImageModel` 指定生图模型，默认使用 `gpt-image-2.6-sunburst`。

用 HTTPS 入口转发到服务的私有监听端口。使用单个服务实例，任务及回传文件暂存在服务内存。服务最多并行执行两个任务。重启时，进行中的任务需要由操作者重新发起。HTTPS 入口需允许至少 48 MiB 的请求体并禁用缓存。

```sh
dotnet PrintFlow.DesignCloud.dll --urls http://0.0.0.0:8080
```

用 `GET /healthz` 检查启动状态。服务需要对 `api.openai.com` 的出站 HTTPS 访问。

## 桌面配置

在安装目录的 `appsettings.local.json` 中加入以下配置，保留该文件已有的其他配置节。地址填写云端 HTTPS 入口的根地址。

```json
{
  "OpenAiDesignCloud": {
    "Endpoint": "https://design.example.com"
  }
}
```

给桌面程序的进程环境设置与云端一致的 `PRINTFLOW_DESIGN_CLOUD_TOKEN`，然后重启桌面程序。密钥和令牌通过各自的环境注入。

在「AI 设计提示词」中选择「OpenAI API · 云端审核」。确认生成后，结果卡会显示审核通过轮次，提供 PNG 预览、继续处理、保存 PDF 和复制生图 Response ID。复审仍未通过时，结果卡会显示具体原因。参考图支持最多八张 PNG、JPEG 或 WebP，单张最大 20 MiB，总计最大 35 MiB。

同一任务的每轮生成和审核都启用 Responses 记录。在过审任务的 `request.json` 中，`cloudReceipt.attempts` 保存每轮 Response ID、基础模型用量和可用的生图工具用量。云端日志按任务 ID 记录各轮响应，方便核对未通过任务。Platform Logs 的记录遵循所用项目的数据保留设置。
