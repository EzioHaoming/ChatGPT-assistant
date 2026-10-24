# macOS v0.1.2 源码与构建说明

此归档包含构建 v0.1.2 的 Swift/Python 源码、网页渲染资源、默认图标、测试、依赖声明和构建/打包脚本，并附上使用过的 Python 3.12.14 arm64 二进制运行环境。第三方运行环境不是本项目的源代码；其许可证保留在 runtime 中。前端 vendored 资源保留其许可证文件或头部声明。

## 环境与构建

要求 Apple 芯片 Mac、macOS 13+、Xcode Command Line Tools。若没有编译工具，先执行 `xcode-select --install` 并按系统提示安装。

在解压后的项目目录执行：

```sh
# 运行自动化测试，无需登录个人账号
runtime/bin/python3 -B -m unittest discover -s tests -v

# 仅在没有本项目签名身份的新开发机器上执行一次
runtime/bin/python3 -B scripts/create-signing-identity.py

# 编译 .app，随后打包并检查私人文件
bash build.command
runtime/bin/python3 -B scripts/package-release.py
```

首次签名可能需要允许 codesign 访问你创建的钥匙串密钥。此包不包含作者证书、签名私钥、钥匙串数据、API Key、OAuth 令牌、个人头像或个人设置。不得为方便构建读取或复制其他应用的账号凭据。

已有签名身份时不要重建证书：build.command 会从开发者自己机器上的 Application Support/ChatGPT assistant/Signing/identity.sha1 读取指纹，私钥由钥匙串保存。更换签名身份会影响截图权限的延续；第三方自行构建不保证继承作者版本的权限。签名结果也不会与原发布包逐字节一致。

输出位于 dist/ChatGPT assistant.app 和 dist/ChatGPT-assistant-arm64.zip。仅从干净构建目录打包，不要复制自己的已安装头像版。GitHub 版没有 Apple 公证；可选 scripts/notarize-release.py 需要开发者自己的 Developer ID 和公证凭据，本次不属于必需构建步骤。

## 依赖与许可

`bridge/requirements.txt` 为直接依赖，`bridge/requirements-lock.txt` 为内置环境版本；`runtime` 包含可重定位运行时。项目采用 MIT 许可，第三方完整许可保留在 `docs/macos/licenses`、`runtime` 和 `Resources/renderer`。

构建脚本从全新 dist 应用目录生成，清除有源文件可重建的 Python 缓存后签名。打包脚本拒绝缓存或个人路径，解压校验全部签名，并生成安装包、源码包及 SHA256.txt。请使用 `-B` 运行项目脚本，禁止从个人安装版打包。
