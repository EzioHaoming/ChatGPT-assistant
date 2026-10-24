# ChatGPT assistant

中文 · [English](README.en.md)

用于截图翻译与文字提问的桌面悬浮助手。个人开发的非官方应用，与 OpenAI 无隶属关系。

## 下载

### [⬇️ ChatGPT assistant for Windows](https://github.com/EzioHaoming/ChatGPT-assistant/releases/download/v0.1.3-preview.20261024/ChatGPT-assistant-Windows-x64.zip)

适用于 **Windows 11 x64**。下载 ZIP 后完整解压，运行 `ChatGPT assistant.exe`。

[使用说明](windows/README.md) · [源码](windows/src/Desktop) · [发布说明与 SHA256 校验文件](https://github.com/EzioHaoming/ChatGPT-assistant/releases/tag/v0.1.3-preview.20261024)

### [⬇️ ChatGPT assistant for macOS](https://github.com/EzioHaoming/ChatGPT-assistant/releases/download/macOS-v0.1.2/ChatGPT-assistant-arm64.zip)

适用于 **Apple 芯片 Mac（M1 及更新芯片），macOS 13+**。解压后将 `ChatGPT assistant.app` 拖入“应用程序”文件夹。

[使用说明](docs/macos/INSTALL.zh-CN.txt) · [源码](https://github.com/EzioHaoming/ChatGPT-assistant/releases/download/macOS-v0.1.2/ChatGPT-assistant-macOS-v0.1.2-source.zip) · [发布说明与 SHA256 校验文件](https://github.com/EzioHaoming/ChatGPT-assistant/releases/tag/macOS-v0.1.2)

首次打开可能需要在“系统设置 → 隐私与安全性”中选择“仍要打开”（本版本尚未经过 Apple 公证）。源码包的构建方法见包内 `SOURCE-BUILD.md`。

## 功能

- 区域截图、截图翻译、带图提问和纯文字提问。
- Markdown 与数学公式显示、复制回答、模型选择和自定义快捷键。
- 悬浮头像支持拖动、展开收起和更换。
- 使用自己的 ChatGPT 授权或兼容 API 服务。

## 账号与数据

每位使用者使用自己的账号授权或 API Key。请求会发送到所配置的服务，用量及费用以对应账号和服务商规则为准。安装与模型配置方法见各平台使用说明。

## 许可证

本仓库的项目源码采用 [MIT License](LICENSE)。第三方依赖遵循各自许可证，Windows 依赖许可见 [windows/licenses](windows/licenses/)，macOS 依赖许可随源码包提供。
