# ChatGPT assistant

[中文](README.md) · English

A floating desktop assistant for screenshot translation and text or image questions. An independent, unofficial application with no affiliation with OpenAI.

## Downloads

### [⬇️ ChatGPT assistant for Windows](https://github.com/EzioHaoming/ChatGPT-assistant/releases/download/v0.1.3-preview.20261024/ChatGPT-assistant-Windows-x64.zip)

For **Windows 11 x64**. Extract the entire ZIP and run `ChatGPT assistant.exe`.

[User guide](windows/README.en.md) · [Source code](windows/src/Desktop) · [Release notes and SHA256 checksums](https://github.com/EzioHaoming/ChatGPT-assistant/releases/tag/v0.1.3-preview.20261024)

### [⬇️ ChatGPT assistant for macOS](https://github.com/EzioHaoming/ChatGPT-assistant/releases/download/macOS-v0.1.2/ChatGPT-assistant-arm64.zip)

For **Apple Silicon Macs (M1 or later), macOS 13+**. Extract the ZIP and move `ChatGPT assistant.app` to Applications.

[User guide](docs/macos/INSTALL.en.txt) · [Source code](https://github.com/EzioHaoming/ChatGPT-assistant/releases/download/macOS-v0.1.2/ChatGPT-assistant-macOS-v0.1.2-source.zip) · [Release notes and SHA256 checksums](https://github.com/EzioHaoming/ChatGPT-assistant/releases/tag/macOS-v0.1.2)

On first launch, you may need to select Open Anyway in System Settings → Privacy & Security. This build is not notarized by Apple. See `SOURCE-BUILD.md` inside the source archive for build instructions.

## Features

- Region capture, screenshot translation, and questions with or without images.
- Markdown and math rendering, answer copying, model selection, and customizable shortcuts.
- A draggable floating avatar that opens or hides the window and supports custom images.
- Connect your own ChatGPT authorization or compatible API service.

## Accounts and data

Each user supplies their own account authorization or API Key. Requests go to the configured service; usage and charges follow that account's and provider's rules. See the platform-specific user guides for installation and model setup.

## License

Project source code in this repository is licensed under the [MIT License](LICENSE). Third-party dependencies retain their own licenses. See [windows/licenses](windows/licenses/) for Windows dependencies; macOS dependency licenses are included in its source archive.
