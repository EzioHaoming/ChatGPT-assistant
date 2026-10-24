# ChatGPT assistant — Windows user guide

Requires Windows 11 x64. Interface labels below include the Chinese labels shown in the app.

## Install and choose a service

**Using this app through ChatGPT authorization consumes your Codex / ChatGPT Work plan allowance, not the message allowance for ordinary ChatGPT web chats.** The allowance available to you is shown on your account authorization page.

Extract the entire Windows ZIP and run `ChatGPT assistant.exe`. Keep the adjacent DLLs and licenses folder; do not run the EXE inside the ZIP.

Choose either ChatGPT authorization with your own eligible account, or a compatible API service with your own API Key. Website chat accounts do not automatically grant API access. The app does not switch services automatically on failure.

API mode requires no separate .NET, Python, or Codex installation. ChatGPT mode requires Chrome, Python, and a compatible Codex App Server. A model-list error before configuration is expected.

## ChatGPT authorization

1. Install Python 3.11 or later, Google Chrome, and a Codex App Server that supports the official Sign in with ChatGPT channel. The verified App Server version is 0.163.0-alpha.11; other versions may differ. Installing the desktop app does not necessarily make `codex` available in PowerShell.
2. Open PowerShell in the extracted folder and run:

```powershell
python -m venv optional-oauth/.venv
optional-oauth/.venv/Scripts/python.exe -m pip install -r optional-oauth/requirements.txt
Copy-Item bridge-config.example.json bridge-config.json
```

3. Edit `bridge-config.json` next to the EXE. Supply actual absolute paths on your computer:

```json
{
  "Python": "C:/YourProject/optional-oauth/.venv/Scripts/python.exe",
  "Script": "C:/YourProject/optional-oauth/desktop_bridge.py",
  "Codex": "C:/YourCodexInstall/codex.exe"
}
```

These are placeholders. Check each path with `Test-Path 'actual path'`; all must return True. Use the actual Codex EXE, not a .cmd launcher. If an App Server update changes its installation path, update the Codex field.

4. Open “设置与账户 → API 设置” (Settings & account → API settings) and turn off “使用自定义 API” (Use custom API). Click “ChatGPT 授权” (ChatGPT authorization).
5. Complete sign-in and plan-usage authorization in Chrome. The authorization page may identify the app as `codex_vision_local_test`. Registration is automatic; you do not need a client secret or a separate OpenAI API Key. An existing Codex CLI sign-in does not replace this authorization. Do not copy someone else's authorization files.
6. Return to the app, click “刷新模型” (Refresh models), and choose a model your account can use. The default is gpt-6-luna. Model selection is remembered; choose another available model if needed. Screenshots require an image-capable model.

Eligibility and allowance depend on your account and OpenAI's rules. This channel uses the authorized Codex / ChatGPT Work plan allowance, not ordinary website chat-message allowance. Connecting the app does not add allowance; successful requests or token counts do not mean usage is free or zero.

References: https://learn.chatgpt.com/docs/sign-in-with-chatgpt • https://developers.openai.com/siwc/token-sharing-open-source/sign-in • https://developers.openai.com/codex/app-server

## Custom API

1. Obtain your own API Key, Base URL, and model ID from your provider. For screenshots, choose a model that supports images.
2. Open API settings and enable Use custom API.
3. Enter the Base URL, model ID, and key. Do not append `/chat/completions`; the app adds it. Retain required prefixes such as `/v1` or `/compatible-mode/v1`. Do not mix regions or workspaces.
4. Save, then send a message or a small screenshot to check connectivity. Saving alone sends no model request; sending may incur charges.
5. Check usage, billing, free allowance, and limits in your provider's console. Free tiers are provider-specific and may expire. API mode does not use your ChatGPT subscription allowance. It uses the balance, free allowance, or paid quota of the API account you configured, with usage and charges determined by that provider. For example, configuring Qwen / Bailian uses your Bailian API account allowance.

The supported protocol is OpenAI Chat Completions compatible: text and PNG `image_url` input with string `message.content` output. Responses-only or provider-specific interfaces are not supported. API answers appear when complete, without streaming.

API keys are encrypted for the current Windows user using DPAPI. Changing the service address requires its own key; leaving the field blank keeps the saved key for the same address. Never send your key to the author or other users.

DeepSeek and Qwen compatibility depends on the selected model and endpoint, not the brand alone. See https://api-docs.deepseek.com/ and https://help.aliyun.com/zh/model-studio/vision.

### Qwen / Bailian preset

1. Obtain a key and compatible Base URL for the Beijing region in the Bailian console.
2. Click the Qwen / Bailian (Beijing) preset in API settings. It fills in `qwen3-vl-plus`.
3. Replace `{WorkspaceId}` with your workspace ID, or paste the complete Base URL from your console; enter your own key.
4. Enable custom API and save. The main model selector shows the configured model. API mode configures one model at a time; change its ID in API settings rather than expecting a full model catalog.
5. If you only want free allowance, verify its balance and expiry and enable the provider's free-only/stop-when-exhausted control before sending.

The preset only fills fields; it does not enable the service or send requests. See https://help.aliyun.com/zh/model-studio/new-free-quota and https://help.aliyun.com/zh/model-studio/model-pricing.

## Capture, translate, and ask

- Press Alt+Q or click “框选截图” (Capture region), then drag a rectangle. Esc or right-click cancels.
- Click “翻译成中文” (Translate into Chinese), or type a question and click “发送” (Send).
- You can ask text-only questions without a screenshot. If a screenshot is attached, click × in its preview to remove it. Translation requires a screenshot.
- Each request is independent and includes the current screenshot, if any. Previous answers are not automatically included; add any needed context to your question.
- Answers support Markdown and formulas. A− / A+ changes text size; “复制回答” copies the answer. Removing a screenshot retains the answer.
- Screenshots and questions are sent to your selected service. Screenshots and answers stay in memory by default and disappear on exit. Copying puts content on the system clipboard.

## Floating button and avatar

Click the avatar to show or hide the window. Drag it to reposition or snap to an edge. Right-click to ask, capture, or exit. The pin toggles always-on-top. Closing the main window hides it; right-click Exit to quit completely.

In Settings & account, “更换头像” selects PNG/JPG/BMP. Square images work best; the app crops to the center. “默认头像” restores the “自” icon. “撤回头像更改” undoes the last replacement or reset, including after restart. Only one undo step is retained; repeatedly resetting does not overwrite the existing backup.

Your avatar is stored in `%USERPROFILE%/.chatgpt-assistant/avatar.png`. Older avatars migrate automatically. Selecting the default does not reimport an old photo.

Changing, resetting, or undoing the avatar also updates the window icon and desktop/taskbar pinned shortcuts that point to the current EXE. Personal ICO files are generated locally in `%USERPROFILE%/.chatgpt-assistant/`. Shortcuts pointing to another EXE version are not changed. Windows icon caching may delay refresh.

If the floating button is off-screen or hidden, the app periodically restores it except during capture. Opening the same EXE again brings the existing window forward. If the process has crashed or stopped responding, restart it.

## Keyboard shortcuts

In Settings & account, click a shortcut field and press the desired combination; it is detected automatically. Screenshot shortcut changes apply when you release the keys. For Send and New line, click the corresponding button to save. A conflict restores the previous screenshot shortcut.

Defaults: Alt+Q captures globally; Enter sends and Shift+Enter inserts a new line within the question box.

Send and New line accept Enter, F1–F11, or Ctrl/Alt with a letter or digit (optionally Shift), such as Ctrl+S or Ctrl+J. The three shortcuts must be different. When you change New line, the old Enter combination no longer inserts a line break. During recording, the old capture shortcut is temporarily disabled; leaving the field restores normal handling. Tab moves focus.

## Troubleshooting and updates

- Missing bridge dependency: check all three paths in bridge-config.json, Python dependencies, and Chrome. After App Server updates, its path may need changing.
- Shortcut unavailable: choose a combination not used by another app, or capture from the avatar menu.
- Authorization/permission error: authorize ChatGPT again, or check the API key, endpoint, and model access.
- 429/limit error: check your service's usage page and retry later; avoid repeated sends.
- Large image or timeout: capture a smaller area. Failure or cancellation does not guarantee no service-side usage.
- Model-list error: configure your service first, then refresh. Change API model IDs in API settings.
- To update, exit the old app from its avatar menu before opening the new version.
