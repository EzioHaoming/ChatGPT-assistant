# ChatGPT assistant使用说明

更新：2026-10-22 · 悬浮窗恢复 / 头像撤回版 · Windows 11 x64

## 先选使用方式

| 方式 | 需要准备 | 用量来源 |
| --- | --- | --- |
| ChatGPT 授权 | 符合条件的 ChatGPT 账号、Chrome、Python、兼容 Codex App Server、本人浏览器授权 | 账号允许的 Codex / ChatGPT Work 计划额度；具体权限和超限设置以官方页面为准 |
| 自定义 API | 服务商 API Key、Base URL、支持图片的模型 ID | 服务商 API 余额或免费额度，由服务商计费 |

两种方式任选一种。仅有 ChatGPT、DeepSeek 或 Qwen 网页聊天账号，不代表已经获得本程序所需的模型调用权限。软件不附送账号、密钥或额度，不会在失败时自动切换服务。

## 解压与启动

完整解压 Windows 压缩包，双击 ChatGPT assistant.exe。请保留旁边的 DLL 和 licenses 文件夹，不要只复制 EXE，也不要在 ZIP 内运行。API 模式不需要另外安装 .NET、Python 或 Codex。

首次可能显示“模型列表加载失败”：尚未配置服务时属于预期情况，按下面任一路线完成配置即可。

## A. ChatGPT 用户：独立授权模式

此模式需要额外环境，尚未在全新电脑完成端到端验收。包内提供桥接脚本，不捆绑 Python、Chrome 或 Codex App Server。已验证的 App Server 版本是 0.163.0-alpha.11；其他版本可能存在接口差异。不要为运行本软件复制别人的授权文件。

1. 安装 Python 3.11 或更高版本、Google Chrome，以及支持官方 SIWC 通道的 Codex App Server。Codex 安装及 App Server 的官方入口见下方链接。必须找到实际可执行文件的完整路径；仅在桌面安装应用不保证 PowerShell 能识别 `codex` 命令。
2. 在解压目录打开 PowerShell，运行以下命令，建立桥接环境：

```powershell
python -m venv optional-oauth/.venv
optional-oauth/.venv/Scripts/python.exe -m pip install -r optional-oauth/requirements.txt
Copy-Item bridge-config.example.json bridge-config.json
```

3. 用记事本打开 EXE 同目录的 bridge-config.json，填写三个真实的本机绝对路径。路径中建议使用 `/`，以下只是占位示例：

```json
{
  "Python": "C:/YourProject/optional-oauth/.venv/Scripts/python.exe",
  "Script": "C:/YourProject/optional-oauth/desktop_bridge.py",
  "Codex": "C:/YourCodexInstall/codex.exe"
}
```

可在 PowerShell 用 `Test-Path '实际路径'` 分别检查，三个路径都应返回 True。如果命令 `python` 不存在，先完成 Python 安装；如果 `codex` 不在 PATH，请填写实际 EXE 路径，不要填 `.cmd` 启动脚本。

4. 打开助手的“设置与账户 → API 设置”，确保未勾选“使用自定义 API”。然后点击“ChatGPT 授权”。
5. 在 Chrome 中由本人完成 OpenAI 官方登录与计划用量授权。当前桥接保留最初验证工具名称 `codex_vision_local_test`，授权页面可能显示此名称。首次通过官方动态客户端流程注册并保存本安装的客户端信息，不需要手工填写 client secret 或单独的 OpenAI API Key。已有 Codex CLI 登录不会直接代替此次授权。
6. 回到助手，点击“刷新模型”，选择账号可用的图片模型。选择会记住；默认 gpt-6-luna。如默认模型不在账号列表中，请手动改选。

官方说明：此授权使用已有的 Codex / ChatGPT Work 计划用量，连接应用不会增加额外额度；不是普通网页聊天消息额度。请求成功或显示 tokens 均不表示免费或用量为零。账号不具备所需权限、官方预览规则变化时，可能无法使用。官方动态注册无需客户端密钥，但仍需用户授权并获得计划调用权限。

参考：[计划授权及用量](https://learn.chatgpt.com/docs/sign-in-with-chatgpt)、[官方动态注册流程](https://developers.openai.com/siwc/token-sharing-open-source/sign-in)、[Codex App Server](https://developers.openai.com/codex/app-server)。

## B. 其他用户：自定义 API 模式

ChatGPT 用户也可以选择本模式；它不使用 ChatGPT 订阅额度。

1. 到服务商开发者控制台申请自己的 API Key，确认可调用的视觉模型、兼容接口地址和计费规则。
2. 在“设置与账户 → API 设置”勾选“使用自定义 API”。
3. 填入 Base URL、模型 ID 和 API Key。Base URL 不要包含末尾 `/chat/completions`，程序会自动追加；请保留服务商要求的 `/v1` 或 `/compatible-mode/v1` 等前缀。不同地域或工作空间的地址和 Key 不要混用。
4. 点击保存，再框选一张小截图并发送问题。保存设置本身不会调用模型；第一次发送才检验真实连通性，可能计费。
5. 通过服务商控制台查看用量和余额。本程序不准确查询第三方账单，也不保证任何模型长期免费。需要仅使用免费额度时，请在服务商控制台设置对应限制。

支持的协议是 OpenAI Chat Completions 兼容接口：文本与 PNG `image_url` 输入、字符串 `message.content` 输出。必须选支持图片输入的模型；只有文字功能、只有 Responses API 或需要额外专有参数的接口不能直接使用。API 模式当前一次性显示完整回答，不流式显示。

DeepSeek、Qwen/百炼等是否能用，取决于其当前具体模型和接口兼容性，不能仅凭品牌判断。请查服务商官方控制台及文档：[DeepSeek](https://api-docs.deepseek.com/)、[Qwen/百炼视觉接口](https://help.aliyun.com/zh/model-studio/vision)。未用真实第三方 Key 验证这些服务，本软件的 API 自动测试采用本地模拟响应。

Key 由 Windows DPAPI 按当前用户加密保存。更换服务地址需重新输入 Key；留空可保留当前地址的已存 Key。不要将自己的 Key 发给软件作者或其他使用者。

## 截图、翻译与提问

- 按 Alt+Q，或点击“框选截图”，拖动选择矩形区域；Esc 或右键取消。
- 点击“翻译成中文”，或在输入框填写问题后点“发送”。例如：“解释图中的公式并说明各符号含义”。
- 当前版本每次请求独立，有截图时携带当前截图；重复提问不会自动带上之前的回答。要追问时请在问题中补充上下文。不需要截图也能发送文字问题；有截图时会附带当前图片，点击预览右上角 × 后即恢复纯文字提问。
- 回答支持 Markdown、数学公式，A−/A+ 调整字号，“复制回答”复制文字。预览右上角 × 删除当前截图，保留回答。
- 截图和问题会发送到所选服务。截图、回答默认只在内存，退出后不保留；复制会进入系统剪贴板。

## 悬浮按钮与头像

单击头像展开或收起窗口；拖动可贴边。右键可提问、框选截图或退出。右上角图钉切换置顶，关闭主窗口仅收起到头像。右键“退出”才完全结束。

“设置与账户 → 更换头像”可选择 PNG/JPG/BMP。推荐正方形图片，程序居中裁切；头像只保存在本机。“默认头像”恢复“自”图标。误点后点击“撤回头像更改”，可撤回最近一次更换或恢复默认操作，重启后也有效。重复点击默认头像不会覆盖已有撤回备份；只保留一步撤回，不是完整历史。

头像意外隐藏或跑出显示器时，程序每约两秒检查恢复；截图期间不恢复，防止截入画面。找不到入口时再双击同一版本的 EXE，会唤回已有主窗口和头像，不会重复开一个实例。该机制依赖程序正常响应，无法修复进程崩溃或系统挂起。

## 常见问题

| 问题 | 处理 |
| --- | --- |
| 快捷键无效 | 在设置中改成未被占用的组合，点“截图快捷键”；也可用头像右键截图 |
| 缺少桥接依赖 | 检查 bridge-config.json 三个绝对路径、Python 依赖和 Chrome 安装 |
| 授权失效/权限不足 | ChatGPT 模式重新点 ChatGPT 授权；API 模式检查 Key、地址、模型权限 |
| 429/限额 | 到所选服务的用量页检查，稍后重试，不要连续重复发送 |
| 图片过大/超时 | 缩小选区。失败或取消不保证服务端没有计费 |
| 模型列表加载失败 | 先配置对应通道，再刷新；API 模式的模型 ID 在 API 设置里修改 |
| 旧程序一直占用 | 右键旧头像退出再运行新版；旧版不一定支持重复启动唤回 |


## Qwen / 百炼快速配置

1. 在百炼控制台选择华北2（北京）地域，获取自己的 API Key 和兼容接口 Base URL。
2. 在 API 设置中点“填入 Qwen / 百炼预设（北京）”，默认填入 qwen3-vl-plus。
3. 将地址里的 {WorkspaceId} 换成自己的业务空间 ID，或粘贴控制台的完整 Base URL；填写自己的 Key。
4. 勾选“使用自定义 API”并保存。主界面的模型下拉会显示 qwen3-vl-plus。API 模式一次配置一个模型，修改模型 ID 请回到 API 设置，不会自动列出全部 Qwen 模型。
5. 仅用免费额度时，请在百炼确认剩余额度及有效期，并开启“用完即停/仅使用免费额度”，再发送截图。

预设只填表，不自动启用或发送请求。免费额度属于个人账号，不随软件附送。请勿混用不同地域的地址和 Key。

参考：[免费额度规则](https://help.aliyun.com/zh/model-studio/new-free-quota)、[模型价格与额度](https://help.aliyun.com/zh/model-studio/model-pricing)、[视觉接口](https://help.aliyun.com/zh/model-studio/vision)。


## 直接文字提问

不截图，直接输入问题并点击发送即可。若当前已有截图而只想发文字，先点击截图预览右上角 × 删除附件。翻译按钮仅在有截图时可用。每次请求独立，不会自动携带上一条回答。
## 自定义快捷键

在“设置与账户”中，截图、发送、换行都使用相同的输入框和按钮。点击输入框后直接按下组合键，自动识别填入，再点击右侧对应按钮应用并保存。录入时原截图快捷键暂时停用，离开输入框后恢复；Tab 可切换焦点。

- 截图默认 Alt+Q，全局生效。
- 发送默认 Enter，仅输入框内生效。
- 换行默认 Shift+Enter，仅输入框内生效。

发送和换行支持 Enter、F1–F11、Ctrl/Alt 加字母或数字（可附加 Shift），例如 Ctrl+S、Ctrl+J。不能与其他两项重复。换行改为其他组合后，原来的 Enter 组合不会再自动换行。

截图快捷键录入后，松开按键即自动应用并保存；如果被其他程序占用，会提示失败并恢复原组合。发送与换行快捷键仍点击对应按钮保存。

### 头像保存位置

头像保存在 `%USERPROFILE%\.chatgpt-assistant\avatar.png`，重启后继续使用。旧版头像会自动迁移；选择默认头像后不会重新导入旧照片。此目录是个人数据，不属于程序分发文件。


## 头像与快捷方式图标同步

更换头像、恢复默认或撤回时，窗口图标及指向当前 EXE 的桌面、任务栏固定快捷方式会一起更新。个人 ICO 只生成在本机 `%USERPROFILE%/.chatgpt-assistant/`，分享包保留默认图标。指向其他版本 EXE 的快捷方式不会修改。Windows 图标缓存可能延迟刷新；任务栏显示效果尚未完成目视验收。
