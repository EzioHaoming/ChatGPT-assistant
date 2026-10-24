# ChatGPT assistant

用于截图翻译与文字提问的桌面悬浮助手。当前仓库收录 Windows 版源码。

个人开发的非官方应用，与 OpenAI 无隶属关系。Windows 开源，Mac 提供预览程序包和完整源码包。

## 🍎 Mac 下载 · v0.1.2 预览版

### [⬇️ 下载 Mac 安装包](https://github.com/EzioHaoming/ChatGPT-assistant/releases/download/macOS-v0.1.2/ChatGPT-assistant-arm64.zip)　|　[📦 下载 Mac 完整源码](https://github.com/EzioHaoming/ChatGPT-assistant/releases/download/macOS-v0.1.2/ChatGPT-assistant-macOS-v0.1.2-source.zip)

适用于 **Apple 芯片 Mac（M1 及更新芯片），macOS 13+**；不支持 Intel Mac。安装包使用默认头像，不含个人配置或授权凭据。

[安装说明](https://github.com/EzioHaoming/ChatGPT-assistant/releases/download/macOS-v0.1.2/macOS-Install-Guide.txt) · [发布说明与 SHA256 校验文件](https://github.com/EzioHaoming/ChatGPT-assistant/releases/tag/macOS-v0.1.2)

首次打开可能需要在“系统设置 → 隐私与安全性”中选择“仍要打开”（本版本尚未经过 Apple 公证）。完整源码包包含资源、依赖声明与构建脚本，构建方法见包内 `SOURCE-BUILD.md`。

## Windows

- 支持区域截图、截图翻译、带图提问和不带截图的文字提问。
- 支持 Markdown 与数学公式显示、复制回答、模型选择和自定义快捷键。
- 悬浮头像支持拖动贴边、展开收起、更换头像和撤回更改。
- 可配置独立 ChatGPT OAuth 通道，或使用自己的兼容 API 服务。

[安装、使用与模型配置说明](windows/README.md) · [更新记录](windows/CHANGELOG.md)

## 从源码构建

需要 Windows 11 和 .NET 10 SDK。在仓库根目录执行：

```powershell
dotnet publish windows/src/Desktop/ScreenshotAssistant.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/windows
```

构建结果为 `dist/windows/ChatGPT assistant.exe` 及其依赖文件。请保留同目录的依赖。

ChatGPT OAuth 模式还需要 Python、桥接依赖和对应的 Codex App Server 配置，见 Windows 使用说明。API 模式需要使用者自行提供服务地址、模型与 API Key。

## 数据与授权

每位使用者使用自己的账号授权或 API Key。请求会发送到所配置的服务，用量及费用以对应账号和服务商规则为准。

Windows 头像保存在 `%USERPROFILE%\.chatgpt-assistant\avatar.png`，可跨不同启动方式保留；旧头像自动迁移。其他设置与授权存储方式见使用说明。

## 许可证状态

本仓库已提供的项目源码采用 [MIT License](LICENSE)。第三方依赖继续遵循各自许可证，见 `windows/licenses/`。Mac v0.1.2 完整源码作为 Release 附件提供，下载入口见上方；其第三方依赖许可证随源码包提供。
