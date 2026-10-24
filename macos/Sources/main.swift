import AppKit
import Carbon
import WebKit
import UniformTypeIdentifiers

let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("ChatGPT assistant")

struct Shortcut {
    let code: UInt16
    let flags: NSEvent.ModifierFlags
    let label: String
    static let mask: NSEvent.ModifierFlags = [.command, .control, .option, .shift]
    static func load(_ name: String) -> Shortcut {
        if let d = UserDefaults.standard.dictionary(forKey: "shortcut." + name), let c = d["code"] as? Int, let f = d["flags"] as? UInt, let l = d["label"] as? String { return Shortcut(code: UInt16(c), flags: NSEvent.ModifierFlags(rawValue: f), label: l) }
        if name == "capture" { return Shortcut(code: 49, flags: [.control, .shift], label: "⌃⇧Space") }
        return Shortcut(code: 36, flags: name == "newline" ? [.shift] : [], label: name == "newline" ? "⇧Enter" : "Enter")
    }
    func matches(_ e: NSEvent) -> Bool { e.keyCode == code && e.modifierFlags.intersection(Self.mask) == flags }
    func persist(_ name: String) { UserDefaults.standard.set(["code": Int(code), "flags": flags.rawValue, "label": label], forKey: "shortcut." + name) }
    var carbon: UInt32 { var v: UInt32 = 0; if flags.contains(.command) { v |= UInt32(cmdKey) }; if flags.contains(.control) { v |= UInt32(controlKey) }; if flags.contains(.option) { v |= UInt32(optionKey) }; if flags.contains(.shift) { v |= UInt32(shiftKey) }; return v }
}
final class ShortcutRecorder: NSButton {
    static var recording = false
    var name = ""
    var commit: ((Shortcut) -> Bool)?
    var monitor: Any?
    func setup(_ name: String) { self.name = name; title = Shortcut.load(name).label; target = self; action = #selector(record); bezelStyle = .rounded }
    func stop() { if let m = monitor { NSEvent.removeMonitor(m) }; monitor = nil; Self.recording = false }
    @objc func record() {
        guard !Self.recording else { return }; Self.recording = true; title = "按下快捷键（Esc 取消）"
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] e in
            guard let self = self else { return e }
            if e.keyCode == 53 { self.stop(); self.title = Shortcut.load(self.name).label; return nil }
            let flags = e.modifierFlags.intersection(Shortcut.mask)
            let key = [UInt16(36): "Enter", 76: "Enter", 49: "Space", 48: "Tab", 51: "Delete"][e.keyCode] ?? (e.charactersIgnoringModifiers ?? "").uppercased()
            let label = (flags.contains(.control) ? "⌃" : "") + (flags.contains(.option) ? "⌥" : "") + (flags.contains(.shift) ? "⇧" : "") + (flags.contains(.command) ? "⌘" : "") + key
            let shortcut = Shortcut(code: e.keyCode, flags: flags, label: label)
            let accepted = self.commit?(shortcut) ?? false
            self.stop(); self.title = accepted ? label : Shortcut.load(self.name).label
            return nil
        }
    }
    deinit { if let m = monitor { NSEvent.removeMonitor(m) } }
}
final class Composer: NSTextView {
    var submit: (() -> Void)?
    override func keyDown(with event: NSEvent) {
        guard !hasMarkedText() else { super.keyDown(with: event); return }
        if Shortcut.load("newline").matches(event) { insertNewline(nil); return }
        if Shortcut.load("send").matches(event) { if !event.isARepeat { submit?() }; return }
        super.keyDown(with: event)
    }
    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        if window?.firstResponder === self && !hasMarkedText() && (Shortcut.load("send").matches(event) || Shortcut.load("newline").matches(event)) { keyDown(with: event); return true }
        return super.performKeyEquivalent(with: event)
    }
}
// Shared drawing keeps the floating avatar, Dock and Finder icon identical.
func drawAvatar(in rect: NSRect) {
        NSColor(calibratedRed: 0.35, green: 0.40, blue: 0.74, alpha: 1).setFill()
        NSBezierPath(ovalIn: rect.insetBy(dx: rect.width / 30, dy: rect.height / 30)).fill()
        NSGraphicsContext.saveGraphicsState()
        NSBezierPath(ovalIn: rect.insetBy(dx: rect.width / 30, dy: rect.height / 30)).addClip()
        if let img = NSImage(contentsOf: support.appendingPathComponent("avatar.png")) {
            let side = min(img.size.width, img.size.height)
            img.draw(in: rect, from: NSRect(x: (img.size.width-side)/2, y: (img.size.height-side)/2, width: side, height: side), operation: .sourceOver, fraction: 1)
        } else {
            ("自" as NSString).draw(at: NSPoint(x: rect.width * 14/60, y: rect.height * 12/60), withAttributes: [.font: NSFont.systemFont(ofSize: rect.width / 2), .foregroundColor: NSColor.white])
        }
        NSGraphicsContext.restoreGraphicsState()
}
func avatarIcon() -> NSImage {
    NSImage(size: NSSize(width: 512, height: 512), flipped: false) { rect in
        drawAvatar(in: rect)
        return true
    }
}
final class AvatarView: NSView {
    weak var owner: Assistant?
    var down = NSPoint.zero
    var origin = NSPoint.zero
    var moved = false
    override func draw(_ dirtyRect: NSRect) {
        drawAvatar(in: bounds)
    }
    override func mouseDown(with event: NSEvent) { down = NSEvent.mouseLocation; origin = window!.frame.origin; moved = false }
    override func mouseDragged(with event: NSEvent) {
        let p = NSEvent.mouseLocation
        if hypot(p.x-down.x, p.y-down.y) > 4 { moved = true }
        if moved { window?.setFrameOrigin(NSPoint(x: origin.x+p.x-down.x, y: origin.y+p.y-down.y)) }
    }
    override func mouseUp(with event: NSEvent) {
        if !moved { owner?.toggle() } else { owner?.snap() }
    }
}
final class Assistant: NSObject, NSApplicationDelegate, NSWindowDelegate, WKNavigationDelegate {
    var window: NSWindow!
    var avatar: NSPanel!
    var avatarView: AvatarView!
    var item: NSStatusItem!
    var hotkey: EventHotKeyRef?
    var eventHandler: EventHandlerRef?
    var screenshot: Data?
    var worker: Process?
    var screenshotProcess: Process?
    var requestID = UUID()
    var timeout: DispatchWorkItem?
    var settingsWindow: NSWindow?
    var apiWindow: NSWindow?
    var shortcutRecorders: [ShortcutRecorder] = []
    let settingsStatus = NSTextField(labelWithString: "")
    let renderedAnswer = WKWebView()
    let answerScroll = NSScrollView()
    var editingAnswer = false
    let answerMode = NSButton(title: "编辑源码", target: nil, action: nil)
    let answer = NSTextView(frame: NSRect(x: 0, y: 0, width: 600, height: 220))
    let composer = Composer(frame: NSRect(x: 0, y: 0, width: 600, height: 76))
    let status = NSTextField(labelWithString: "就绪 · Enter 发送，Shift+Enter 换行")
    let preview = NSImageView()
    let previewBar = NSStackView()
    let settingsArea = NSStackView()
    let settingsToggle = NSButton(title: "▸ 设置与账户", target: nil, action: nil)
    let modelPicker = NSPopUpButton()
    var modelRows: [[String: Any]] = []
    let capability = NSTextField(labelWithString: "图片能力未知 · 请刷新模型")
    var zoom = 1.0
    let zoomLabel = NSTextField(labelWithString: "100%")
    var imageWindow: NSWindow?
    let model = NSComboBox()
    let apiModelStatus = NSTextField(wrappingLabelWithString: "可选择列表中的模型，也可直接输入模型 ID。")
    let providerPicker = NSPopUpButton()
    let url = NSTextField(string: "")
    let key = NSSecureTextField(string: "")
    let api = NSButton(checkboxWithTitle: "使用自定义 API", target: nil, action: nil)
    var busy: Bool { worker != nil || screenshotProcess != nil }
    let accent = NSColor(calibratedRed: 0.36, green: 0.37, blue: 0.73, alpha: 1)
    let canvasColor = NSColor(calibratedRed: 0.90, green: 0.92, blue: 0.96, alpha: 1)
    func styleButton(_ b: NSButton, primary: Bool = false) {
        b.isBordered = false; b.wantsLayer = true; b.layer?.cornerRadius = 9
        b.layer?.backgroundColor = (primary ? accent : .white).cgColor
        b.layer?.borderWidth = primary ? 0 : 0.5; b.layer?.borderColor = NSColor(calibratedWhite: 0.81, alpha: 1).cgColor
        b.font = .systemFont(ofSize: 12, weight: .medium); b.contentTintColor = primary ? .white : NSColor(calibratedRed: 0.27, green: 0.30, blue: 0.39, alpha: 1)
        b.heightAnchor.constraint(equalToConstant: 29).isActive = true
        b.widthAnchor.constraint(greaterThanOrEqualToConstant: CGFloat(b.title.count * 12 + 24)).isActive = true
    }
    func button(_ text: String, _ selector: Selector) -> NSButton { let b = NSButton(title: text, target: self, action: selector); styleButton(b, primary: text == "发送" || text == "翻译成中文"); return b }
    func row(_ views: [NSView]) -> NSStackView { let r = NSStackView(views: views); r.orientation = .horizontal; r.spacing = 8; r.alignment = .centerY; return r }
    func spacer() -> NSView { let v = NSView(); v.setContentHuggingPriority(.defaultLow, for: .horizontal); return v }
    func roundCard(_ v: NSView) { v.wantsLayer = true; v.layer?.cornerRadius = 12; v.layer?.masksToBounds = true; v.layer?.backgroundColor = NSColor.white.cgColor }
    @objc func toggleSettingsArea() { settingsArea.isHidden.toggle(); settingsToggle.title = settingsArea.isHidden ? "▸ 设置与账户" : "▾ 设置与账户" }
    func applicationDidFinishLaunching(_ notification: Notification) {
        try? FileManager.default.createDirectory(at: support, withIntermediateDirectories: true)
        let menu = NSMenu(); let appMenu = NSMenu(); let root = NSMenuItem(); root.submenu = appMenu; menu.addItem(root)
        appMenu.addItem(withTitle: "退出 ChatGPT assistant", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        let edit = NSMenuItem(title: "编辑", action: nil, keyEquivalent: ""); let editMenu = NSMenu(); edit.submenu = editMenu; menu.addItem(edit)
        for (title, action, shortcut) in [("复制", "copy:", "c"), ("粘贴", "paste:", "v"), ("剪切", "cut:", "x"), ("全选", "selectAll:", "a")] { editMenu.addItem(withTitle: title, action: Selector(action), keyEquivalent: shortcut) }
        NSApp.mainMenu = menu
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 540, height: 760), styleMask: [.titled, .closable, .resizable, .miniaturizable], backing: .buffered, defer: false)
        window.title = "ChatGPT assistant"; window.delegate = self; window.isReleasedWhenClosed = false; window.minSize = NSSize(width: 500, height: 620); window.center()
        window.appearance = NSAppearance(named: .aqua); window.titlebarAppearsTransparent = true; window.backgroundColor = canvasColor
        let rootView = NSView(); window.contentView = rootView
        let stack = NSStackView(); stack.orientation = .vertical; stack.spacing = 9; stack.alignment = .leading
        rootView.addSubview(stack); stack.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([stack.leadingAnchor.constraint(equalTo: rootView.leadingAnchor, constant: 18), stack.trailingAnchor.constraint(equalTo: rootView.trailingAnchor, constant: -18), stack.topAnchor.constraint(equalTo: rootView.topAnchor, constant: 10), stack.bottomAnchor.constraint(equalTo: rootView.bottomAnchor, constant: -14)])
        func add(_ v: NSView) { stack.addArrangedSubview(v); v.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true }
        let title = NSTextField(labelWithString: "很高兴为您服务！"); title.font = .boldSystemFont(ofSize: 17); title.textColor = NSColor(calibratedRed: 0.18, green: 0.21, blue: 0.29, alpha: 1)
        add(row([title, spacer(), button("置顶", #selector(pin))]))
        add(row([button("框选截图", #selector(capture)), button("翻译成中文", #selector(translate)), button("取消请求", #selector(cancel)), spacer()]))
        modelPicker.target = self; modelPicker.action = #selector(selectModel); modelPicker.font = .systemFont(ofSize: 12)
        modelPicker.widthAnchor.constraint(equalToConstant: 230).isActive = true
        let modelLabel = NSTextField(labelWithString: "模型"); modelLabel.font = .systemFont(ofSize: 12)
        add(row([modelLabel, modelPicker, spacer(), button("刷新模型", #selector(refreshModels))]))
        capability.font = .systemFont(ofSize: 11); capability.textColor = .secondaryLabelColor; add(capability)
        loadModels(); status.stringValue = "发送：" + Shortcut.load("send").label + " · 换行：" + Shortcut.load("newline").label
        preview.imageScaling = .scaleProportionallyUpOrDown; preview.heightAnchor.constraint(equalToConstant: 64).isActive = true; preview.widthAnchor.constraint(equalToConstant: 100).isActive = true
        previewBar.orientation = .horizontal; previewBar.spacing = 8; previewBar.alignment = .centerY
        for v in [preview, button("放大截图", #selector(enlargeImage)), button("删除截图", #selector(clearImage)), spacer()] { previewBar.addArrangedSubview(v) }
        add(previewBar); previewBar.isHidden = true
        answer.isEditable = true; answer.isRichText = false; answer.font = .systemFont(ofSize: 17); answer.isVerticallyResizable = true; answer.autoresizingMask = [.width]; answer.textContainer?.widthTracksTextView = true; answer.textContainerInset = NSSize(width: 12, height: 12)
        let scroll = NSView(); roundCard(scroll); add(scroll)
        answerScroll.hasVerticalScroller = true; answerScroll.documentView = answer
        for view in [renderedAnswer, answerScroll] { scroll.addSubview(view); view.translatesAutoresizingMaskIntoConstraints = false; NSLayoutConstraint.activate([view.leadingAnchor.constraint(equalTo: scroll.leadingAnchor), view.trailingAnchor.constraint(equalTo: scroll.trailingAnchor), view.topAnchor.constraint(equalTo: scroll.topAnchor), view.bottomAnchor.constraint(equalTo: scroll.bottomAnchor)]) }
        answerScroll.isHidden = true; renderedAnswer.navigationDelegate = self
        if let page = Bundle.main.resourceURL?.appendingPathComponent("renderer/index.html") { renderedAnswer.loadFileURL(page, allowingReadAccessTo: page.deletingLastPathComponent()) }
        answerMode.target = self; answerMode.action = #selector(toggleAnswerMode); styleButton(answerMode)
        scroll.heightAnchor.constraint(greaterThanOrEqualToConstant: 220).isActive = true
        zoomLabel.font = .monospacedDigitSystemFont(ofSize: 11, weight: .regular); zoomLabel.textColor = .secondaryLabelColor
        add(row([button("复制回答", #selector(copyAnswer)), answerMode, spacer(), button("A−", #selector(zoomOut)), zoomLabel, button("A+", #selector(zoomIn)), button("重置", #selector(resetZoom))]))
        zoom = UserDefaults.standard.object(forKey: "answerZoom") as? Double ?? 1; applyZoom()
        composer.font = .systemFont(ofSize: 14); composer.isRichText = false; composer.submit = { [weak self] in self?.send() }; composer.autoresizingMask = [.width]; composer.textContainer?.widthTracksTextView = true; composer.textContainerInset = NSSize(width: 8, height: 8)
        let input = NSScrollView(); input.hasVerticalScroller = true; input.documentView = composer; roundCard(input); input.heightAnchor.constraint(equalToConstant: 64).isActive = true
        input.widthAnchor.constraint(greaterThanOrEqualToConstant: 280).isActive = true
        let sendButton = button("发送", #selector(send)); add(row([input, sendButton]))
        settingsToggle.target = self; settingsToggle.action = #selector(toggleSettingsArea); settingsToggle.isBordered = false; settingsToggle.font = .systemFont(ofSize: 12, weight: .medium); settingsToggle.contentTintColor = .secondaryLabelColor
        add(row([settingsToggle, spacer()]))
        settingsArea.orientation = .vertical; settingsArea.alignment = .leading; settingsArea.spacing = 8
        settingsArea.addArrangedSubview(row([button("快捷键设置", #selector(settings)), button("API 设置", #selector(apiSettings)), button("更换头像", #selector(changeAvatar)), button("默认头像", #selector(resetAvatar))]))
        settingsArea.addArrangedSubview(row([button("ChatGPT 授权", #selector(login)), button("撤回头像", #selector(undoAvatar))]))
        add(settingsArea); settingsArea.isHidden = true
        if config().isEmpty {
            settingsArea.isHidden = false; settingsToggle.title = "▾ 设置与账户"
            status.stringValue = "首次使用：点击 ChatGPT 授权，或配置自己的 API。"
        }
        status.font = .systemFont(ofSize: 11); status.textColor = .secondaryLabelColor; status.lineBreakMode = .byTruncatingTail; status.maximumNumberOfLines = 1; add(status)
        avatar = NSPanel(contentRect: NSRect(x: 50, y: 200, width: 60, height: 60), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        avatar.isOpaque = false; avatar.backgroundColor = .clear; avatar.level = .floating; avatar.hidesOnDeactivate = false; avatar.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        avatarView = AvatarView(); avatarView.owner = self; avatar.contentView = avatarView
        let context = NSMenu(); context.addItem(withTitle: "展开 / 收起", action: #selector(toggle), keyEquivalent: "").target = self
        context.addItem(withTitle: "截图", action: #selector(capture), keyEquivalent: "").target = self
        context.addItem(withTitle: "退出", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        avatarView.menu = context
        item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength); item.button?.title = "自"; item.menu = context.copy() as? NSMenu
        if let x = UserDefaults.standard.object(forKey: "avatarX") as? Double, let y = UserDefaults.standard.object(forKey: "avatarY") as? Double { avatar.setFrameOrigin(NSPoint(x: x, y: y)) }
        snap(); avatar.orderFrontRegardless(); reloadAvatar()
        NotificationCenter.default.addObserver(forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main) { [weak self] _ in self?.snap() }
        var kind = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, _, pointer in
            guard let pointer = pointer else { return OSStatus(eventNotHandledErr) }
            let app = Unmanaged<Assistant>.fromOpaque(pointer).takeUnretainedValue()
            DispatchQueue.main.async { if !ShortcutRecorder.recording { app.capture() } }; return noErr
        }, 1, &kind, Unmanaged.passUnretained(self).toOpaque(), &eventHandler)
        if !registerCapture(Shortcut.load("capture")) { status.stringValue = "截图快捷键被占用，请在设置中更换" }
        show()
    }
    func windowShouldClose(_ sender: NSWindow) -> Bool { for r in shortcutRecorders { r.stop(); r.title = Shortcut.load(r.name).label }; sender.orderOut(nil); return false }
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool { show(); avatar.orderFrontRegardless(); return true }
    func applicationWillTerminate(_ notification: Notification) { if worker?.isRunning == true { worker?.terminate() }; if screenshotProcess?.isRunning == true { screenshotProcess?.terminate() }; if let h = hotkey { UnregisterEventHotKey(h) } }
    func show() { window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true) }
    @objc func toggle() { if window.isVisible { window.orderOut(nil) } else { show() } }
    @objc func pin() { window.level = window.level == .floating ? .normal : .floating }
    func snap() {
        let screen = NSScreen.screens.first(where: { $0.frame.intersects(avatar.frame) }) ?? NSScreen.main
        guard let frame = screen?.visibleFrame else { return }
        let x = avatar.frame.midX < frame.midX ? frame.minX + 8 : frame.maxX - 68
        let y = max(frame.minY + 8, min(avatar.frame.minY, frame.maxY - 68)); avatar.setFrameOrigin(NSPoint(x: x, y: y))
        UserDefaults.standard.set(x, forKey: "avatarX"); UserDefaults.standard.set(y, forKey: "avatarY")
    }
    @objc func capture() {
        guard !busy else { return }
        guard CGPreflightScreenCaptureAccess() else {
            if !UserDefaults.standard.bool(forKey: "screenCapturePermissionRequested") {
                UserDefaults.standard.set(true, forKey: "screenCapturePermissionRequested")
                _ = CGRequestScreenCaptureAccess()
            }
            status.stringValue = "截图权限尚未生效。请在系统设置的屏幕录制中重新添加当前应用，允许后用菜单退出并重新打开（关闭窗口不等于退出）。"
            return
        }
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        do { try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: false, attributes: [.posixPermissions: 0o700]) } catch { status.stringValue = "无法创建临时截图目录"; return }
        let file = dir.appendingPathComponent("selection.png")
        window.orderOut(nil); avatar.orderOut(nil)
        let process = Process(); process.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture"); process.arguments = ["-i", "-s", "-x", "-t", "png", file.path]; process.standardError = FileHandle.nullDevice
        screenshotProcess = process
        process.terminationHandler = { [weak self] p in
            let data = p.terminationStatus == 0 ? (try? Data(contentsOf: file)) : nil
            try? FileManager.default.removeItem(at: dir)
            DispatchQueue.main.async {
                guard let self = self else { return }; self.screenshotProcess = nil; self.show(); self.avatar.orderFrontRegardless()
                if let data = data, data.count <= 20_000_000, let image = NSImage(data: data) { self.screenshot = data; self.preview.image = image; self.previewBar.isHidden = false; self.status.stringValue = "已截图，尚未发送" }
                else { self.status.stringValue = "截图已取消、失败或图片超过 20 MB；原截图保留" }
            }
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.18) {
            do { try process.run() } catch { self.screenshotProcess = nil; try? FileManager.default.removeItem(at: dir); self.show(); self.avatar.orderFrontRegardless(); self.status.stringValue = "无法启动系统截图" }
        }
    }
    @objc func clearImage() { guard !busy else { return }; screenshot = nil; preview.image = nil; previewBar.isHidden = true; status.stringValue = "已删除截图，可直接文字提问" }
    @objc func translate() { guard screenshot != nil else { status.stringValue = "请先截图"; return }; run(["action": "ask", "question": "将图片中的英文翻译为中文，尽可能保留数学公式、代码和原文结构。", "png": screenshot!.base64EncodedString()]) }
    @objc func send() { let text = composer.string.trimmingCharacters(in: .whitespacesAndNewlines); guard !text.isEmpty else { return }; run(["action": "ask", "question": text, "png": screenshot?.base64EncodedString() ?? ""]) }
    func config() -> [String: Any] {
        guard let d = try? Data(contentsOf: support.appendingPathComponent("settings.json")), let c = try? JSONSerialization.jsonObject(with: d) as? [String: Any] else { return [:] }; return c
    }
    func loadModels() {
        let c = config(); let selected = c["model"] as? String ?? "gpt-6-luna"
        let cacheKey = "modelCatalog:" + ((c["api"] as? Bool == true) ? (c["url"] as? String ?? "api") : "chatgpt")
        modelRows = UserDefaults.standard.array(forKey: cacheKey) as? [[String: Any]] ?? []
        if !modelRows.contains(where: { $0["id"] as? String == selected }) { modelRows.insert(["id": selected], at: 0) }
        modelPicker.removeAllItems()
        for r in modelRows { modelPicker.addItem(withTitle: r["id"] as? String ?? "") }
        if let i = modelRows.firstIndex(where: { $0["id"] as? String == selected }) { modelPicker.selectItem(at: i) }
        updateCapability()
    }
    func updateCapability() {
        guard modelRows.indices.contains(modelPicker.indexOfSelectedItem) else { return }
        let supported = modelRows[modelPicker.indexOfSelectedItem]["image"] as? Bool
        capability.stringValue = supported.map { $0 ? "支持图片与文字" : "仅支持文字：发送前请删除截图或切换模型" } ?? "图片能力未知，请确认服务商支持情况"
    }
    @objc func refreshModels() { run(["action": "models"]) }
    @objc func selectModel() {
        guard !busy, modelRows.indices.contains(modelPicker.indexOfSelectedItem), let id = modelRows[modelPicker.indexOfSelectedItem]["id"] as? String else { loadModels(); return }
        updateCapability(); run(["action": "select_model", "model": id])
    }
    func applyZoom() {
        zoom = max(0.5, min(2.5, zoom)); renderedAnswer.pageZoom = zoom
        answer.font = .systemFont(ofSize: 17 * zoom); zoomLabel.stringValue = "\(Int((zoom * 100).rounded()))%"
        UserDefaults.standard.set(zoom, forKey: "answerZoom")
    }
    @objc func zoomIn() { zoom += 0.1; applyZoom() }
    @objc func zoomOut() { zoom -= 0.1; applyZoom() }
    @objc func resetZoom() { zoom = 1; applyZoom() }
    @objc func enlargeImage() {
        guard let data = screenshot, let image = NSImage(data: data) else { status.stringValue = "请先截图"; return }
        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 850, height: 650), styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
        w.title = "截图预览 · 双指缩放"; w.isReleasedWhenClosed = false
        let scroll = NSScrollView(); scroll.hasVerticalScroller = true; scroll.hasHorizontalScroller = true; scroll.allowsMagnification = true; scroll.minMagnification = 0.25; scroll.maxMagnification = 4
        let v = NSImageView(frame: NSRect(origin: .zero, size: image.size)); v.image = image; scroll.documentView = v; w.contentView = scroll
        imageWindow = w; w.center(); w.makeKeyAndOrderFront(nil)
    }
    func renderAnswer() {
        guard !editingAnswer, let data = try? JSONSerialization.data(withJSONObject: [answer.string]), let json = String(data: data, encoding: .utf8) else { return }
        renderedAnswer.evaluateJavaScript("window.renderAnswer && window.renderAnswer(" + json + "[0])", completionHandler: nil)
    }
    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) { renderAnswer() }
    @objc func toggleAnswerMode() {
        guard !busy else { status.stringValue = "请等待回答完成后编辑"; return }
        editingAnswer.toggle(); answerScroll.isHidden = !editingAnswer; renderedAnswer.isHidden = editingAnswer
        answerMode.title = editingAnswer ? "预览公式" : "编辑源码"
        if editingAnswer { window.makeFirstResponder(answer) } else { renderAnswer() }
    }
    @objc func copyAnswer() { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(answer.string, forType: .string) }
    @objc func cancel() { requestID = UUID(); timeout?.cancel(); if worker?.isRunning == true { worker?.terminate() }; worker = nil; status.stringValue = "已取消；已提交请求可能计入用量" }
    func run(_ request: [String: Any]) {
        guard !busy else { return }
        if request["action"] as? String == "ask", !(request["png"] as? String ?? "").isEmpty, modelRows.indices.contains(modelPicker.indexOfSelectedItem), modelRows[modelPicker.indexOfSelectedItem]["image"] as? Bool == false { status.stringValue = "当前模型仅支持文字，请删除截图或切换支持图片的模型"; return }
        let bundledPython = Bundle.main.resourceURL!.appendingPathComponent("python/bin/python3")
        let python = FileManager.default.isExecutableFile(atPath: bundledPython.path) ? bundledPython : support.appendingPathComponent("venv/bin/python3")
        guard FileManager.default.isExecutableFile(atPath: python.path) else { status.stringValue = "请先在 Mac 执行 setup.command 安装运行环境"; return }
        guard let script = Bundle.main.resourceURL?.appendingPathComponent("bridge/desktop_bridge.py") else { return }
        let process = Process(); process.executableURL = python; process.arguments = ["-u", script.path]
        var environment = ProcessInfo.processInfo.environment; environment.removeValue(forKey: "OPENAI_API_KEY"); environment.removeValue(forKey: "OPENAI_BASE_URL"); environment.removeValue(forKey: "PYTHONHOME"); environment.removeValue(forKey: "PYTHONPATH"); environment["PYTHONDONTWRITEBYTECODE"] = "1"; environment["PYTHONIOENCODING"] = "utf-8"; process.environment = environment
        let input = Pipe(); let output = Pipe(); process.standardInput = input; process.standardOutput = output; process.standardError = FileHandle.nullDevice
        let id = UUID(); requestID = id; worker = process; status.stringValue = "正在处理…"; let asking = request["action"] as? String == "ask"
        if asking { editingAnswer = false; answerScroll.isHidden = true; renderedAnswer.isHidden = false; answerMode.title = "编辑源码"; answer.string = ""; renderAnswer() }
        let deadline = DispatchWorkItem { [weak self] in guard self?.requestID == id, self?.worker != nil else { return }; self?.cancel(); self?.status.stringValue = "请求超时" }
        timeout = deadline; DispatchQueue.main.asyncAfter(deadline: .now() + (request["action"] as? String == "login" ? 930 : 240), execute: deadline)
        do { try process.run() } catch { worker = nil; timeout?.cancel(); status.stringValue = "无法启动 Python 桥接"; return }
        DispatchQueue.global(qos: .userInitiated).async {
            var done = false
            do {
                var bytes = try JSONSerialization.data(withJSONObject: request); bytes.append(10)
                try input.fileHandleForWriting.write(contentsOf: bytes); try input.fileHandleForWriting.close()
                var buffer = Data()
                while true {
                    let chunk = output.fileHandleForReading.availableData; if chunk.isEmpty { break }; buffer.append(chunk)
                    while let newline = buffer.firstIndex(of: 10) {
                        let line = buffer.prefix(upTo: newline); buffer.removeSubrange(...newline)
                        guard let event = try JSONSerialization.jsonObject(with: line) as? [String: Any] else { continue }
                        let type = event["type"] as? String ?? ""
                        if type == "done" || type == "error" { done = true }
                        DispatchQueue.main.async {
                            guard self.requestID == id else { return }
                            if request["action"] as? String == "api_models" {
                                guard self.url.stringValue == request["url"] as? String, self.key.stringValue == request["key"] as? String else { self.apiModelStatus.stringValue = "地址或密钥已改变，请重新刷新。"; return }
                                if let rows = event["api_models"] as? [[String: Any]] {
                                    let typed = self.model.stringValue
                                    self.model.removeAllItems(); self.model.addItems(withObjectValues: rows.compactMap { $0["id"] as? String }); self.model.stringValue = typed
                                }
                                self.apiModelStatus.stringValue = event["message"] as? String ?? event["answer"] as? String ?? "刷新完成"
                                return
                            }
                            if type == "delta" { self.answer.string += event["text"] as? String ?? "" }
                            if type == "done" { if asking { self.answer.string = event["answer"] as? String ?? "" }; self.status.stringValue = asking ? "完成" : (event["answer"] as? String ?? "完成") }
                            if type == "done" {
                                if let rows = event["models"] as? [[String: Any]] {
                                    let c = self.config(); let k = "modelCatalog:" + ((c["api"] as? Bool == true) ? (c["url"] as? String ?? "api") : "chatgpt")
                                    let safeRows = rows.map { r -> [String: Any] in var v: [String: Any] = ["id": r["id"] as? String ?? ""]; if let b = r["image"] as? Bool { v["image"] = b }; return v }
                                    UserDefaults.standard.set(safeRows, forKey: k)
                                }
                                if !asking { self.loadModels() }
                            }
                            if type == "error", request["action"] as? String == "select_model" { self.loadModels() }
                            self.renderAnswer()
                            if type == "error" { if asking { self.answer.string = "" }; self.status.stringValue = event["message"] as? String ?? "请求失败"; self.renderAnswer() }
                        }
                    }
                }
                process.waitUntilExit()
            } catch { if process.isRunning { process.terminate() } }
            let completed = done
            DispatchQueue.main.async { guard self.requestID == id else { return }; self.worker = nil; self.timeout?.cancel(); if !completed { if asking { self.answer.string = "" }; self.status.stringValue = "请求失败或桥接进程异常退出，请检查设置" } }
        }
    }
    func registerCapture(_ shortcut: Shortcut) -> Bool {
        if let h = hotkey { UnregisterEventHotKey(h); hotkey = nil }
        return RegisterEventHotKey(UInt32(shortcut.code), shortcut.carbon, EventHotKeyID(signature: 0x43474153, id: 1), GetApplicationEventTarget(), 0, &hotkey) == noErr
    }
    func saveShortcut(_ name: String, _ value: Shortcut) -> Bool {
        if (value.flags.isEmpty || value.flags == [.shift]) && (name == "capture" || ![UInt16(36), 76].contains(value.code)) { settingsStatus.stringValue = "请加入 ⌘、⌃ 或 ⌥ 修饰键"; return false }
        if value.code == 12 && value.flags.contains(.command) { settingsStatus.stringValue = "请勿使用系统退出或注销快捷键"; return false }
        for other in ["capture", "send", "newline"] where other != name {
            let v = Shortcut.load(other); if v.code == value.code && v.flags == value.flags { settingsStatus.stringValue = "快捷键与其他操作重复"; return false }
        }
        if name == "capture" && !registerCapture(value) { _ = registerCapture(Shortcut.load("capture")); settingsStatus.stringValue = "快捷键被占用，已保留原设置"; return false }
        value.persist(name); settingsStatus.stringValue = "快捷键已保存"; status.stringValue = "发送：" + Shortcut.load("send").label + " · 换行：" + Shortcut.load("newline").label; return true
    }
    @objc func settings() {
        if settingsWindow == nil {
            let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 480, height: 260), styleMask: [.titled, .closable], backing: .buffered, defer: false)
            w.appearance = NSAppearance(named: .aqua); w.backgroundColor = canvasColor; w.title = "快捷键设置"; w.isReleasedWhenClosed = false; w.delegate = self; settingsWindow = w
            let s = NSStackView(); s.orientation = .vertical; s.spacing = 18; s.edgeInsets = NSEdgeInsets(top: 24, left: 24, bottom: 24, right: 24); w.contentView = s
            let title = NSTextField(labelWithString: "快捷键"); title.font = .boldSystemFont(ofSize: 16); s.addArrangedSubview(title)
            for (name, label) in [("capture", "截图"), ("send", "发送"), ("newline", "换行")] {
                let recorder = ShortcutRecorder(); recorder.setup(name); recorder.widthAnchor.constraint(equalToConstant: 260).isActive = true
                recorder.commit = { [weak self] value in self?.saveShortcut(name, value) ?? false }; shortcutRecorders.append(recorder)
                s.addArrangedSubview(row([NSTextField(labelWithString: label), recorder]))
            }
            s.addArrangedSubview(settingsStatus)
        }
        settingsWindow?.center(); settingsWindow?.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true)
    }
    @objc func apiSettings() {
        if apiWindow == nil {
            let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 520, height: 335), styleMask: [.titled, .closable], backing: .buffered, defer: false)
            w.appearance = NSAppearance(named: .aqua); w.backgroundColor = canvasColor; w.title = "API 设置"; w.isReleasedWhenClosed = false; apiWindow = w
            let root = NSView(); w.contentView = root
            let s = NSStackView(); s.orientation = .vertical; s.alignment = .leading; s.spacing = 16
            root.addSubview(s); s.translatesAutoresizingMaskIntoConstraints = false
            NSLayoutConstraint.activate([s.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 22), s.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -22), s.topAnchor.constraint(equalTo: root.topAnchor, constant: 24)])
            providerPicker.addItems(withTitles: ["自定义（兼容接口）", "OpenAI", "Qwen / 百炼"])
            providerPicker.target = self; providerPicker.action = #selector(providerChanged)
            url.placeholderString = "https://服务地址/v1"; key.placeholderString = "留空保留当前地址的密钥"; model.placeholderString = "选择或输入模型 ID"; model.isEditable = true; model.completes = true; model.numberOfVisibleItems = 10
            for (label, field) in [("服务商", providerPicker as NSView), ("服务地址", url as NSView), ("API Key", key as NSView), ("API 模型", model as NSView)] {
                let text = NSTextField(labelWithString: label); text.font = .systemFont(ofSize: 12, weight: .medium); text.alignment = .left; text.widthAnchor.constraint(equalToConstant: 72).isActive = true
                let r = row([text, field]); s.addArrangedSubview(r); r.widthAnchor.constraint(equalTo: s.widthAnchor).isActive = true
                field.widthAnchor.constraint(equalToConstant: 390).isActive = true
            }
            let refresh = row([spacer(), button("刷新模型", #selector(refreshAPIModels))]); s.addArrangedSubview(refresh); refresh.widthAnchor.constraint(equalTo: s.widthAnchor).isActive = true
            apiModelStatus.font = .systemFont(ofSize: 11); apiModelStatus.textColor = .secondaryLabelColor
            s.addArrangedSubview(apiModelStatus); apiModelStatus.widthAnchor.constraint(equalTo: s.widthAnchor).isActive = true
            let actions = row([spacer(), button("取消", #selector(closeAPI)), button("保存并启用", #selector(save))]); s.addArrangedSubview(actions); actions.widthAnchor.constraint(equalTo: s.widthAnchor).isActive = true
        }
        model.removeAllItems(); apiModelStatus.stringValue = "可选择列表中的模型，也可直接输入模型 ID。"
        let c = config(); url.stringValue = c["url"] as? String ?? ""
        model.stringValue = c["api_model"] as? String ?? (c["api"] as? Bool == true ? (c["model"] as? String ?? "") : "")
        let provider = c["provider"] as? String ?? "custom"; providerPicker.selectItem(at: provider == "openai" ? 1 : provider == "qwen" ? 2 : 0); key.stringValue = ""
        apiWindow?.center(); apiWindow?.makeKeyAndOrderFront(nil)
    }
    @objc func refreshAPIModels() {
        guard !busy else { apiModelStatus.stringValue = "请等待当前请求完成后再刷新。"; return }
        apiModelStatus.stringValue = "正在获取模型…"
        run(["action": "api_models", "url": url.stringValue, "key": key.stringValue])
    }
    @objc func closeAPI() { key.stringValue = ""; apiWindow?.orderOut(nil) }
    @objc func providerChanged() {
        key.stringValue = ""; model.stringValue = ""; model.removeAllItems()
        switch providerPicker.indexOfSelectedItem {
        case 1: url.stringValue = "https://api.openai.com/v1"
        case 2: url.stringValue = "https://{WorkspaceId}.cn-beijing.maas.aliyuncs.com/compatible-mode/v1"; model.stringValue = "qwen3-vl-plus"
        default: url.stringValue = ""
        }
    }
    @objc func save() {
        guard !busy else { return }; let c = config()
        if c["api"] as? Bool != true { UserDefaults.standard.set(c["model"] as? String ?? "gpt-6-luna", forKey: "lastChatGPTModel") }
        run(["action": "configure", "api": true, "provider": ["custom", "openai", "qwen"][providerPicker.indexOfSelectedItem], "model": model.stringValue, "url": url.stringValue, "key": key.stringValue])
        key.stringValue = ""; apiWindow?.orderOut(nil); settingsWindow?.orderOut(nil); show()
    }
    @objc func login() {
        settingsWindow?.orderOut(nil); show(); let c = config()
        let remembered = c["chatgpt_model"] as? String ?? (c["api"] as? Bool == true ? (UserDefaults.standard.string(forKey: "lastChatGPTModel") ?? "gpt-6-luna") : (c["model"] as? String ?? "gpt-6-luna"))
        run(["action": "login", "model": remembered])
    }
    func reloadAvatar() {
        avatarView?.needsDisplay = true
        let icon = avatarIcon()
        NSApp.applicationIconImage = icon
        // Keep the signed app bundle immutable; personalize only the floating window and Dock.
    }

    func backupAvatar() throws { let data = (try? Data(contentsOf: support.appendingPathComponent("avatar.png"))) ?? Data(); try data.write(to: support.appendingPathComponent("avatar-undo.bin"), options: .atomic) }
    @objc func changeAvatar() {
        let picker = NSOpenPanel(); picker.allowedContentTypes = [.png, .jpeg]; picker.allowsMultipleSelection = false
        guard picker.runModal() == .OK, let file = picker.url, let data = try? Data(contentsOf: file), data.count < 20_000_000, let image = NSImage(data: data), let tiff = image.tiffRepresentation, let bitmap = NSBitmapImageRep(data: tiff), let png = bitmap.representation(using: .png, properties: [:]) else { return }
        do { try backupAvatar(); try png.write(to: support.appendingPathComponent("avatar.png"), options: .atomic); reloadAvatar() } catch { status.stringValue = "头像保存失败" }
    }
    @objc func resetAvatar() { let file = support.appendingPathComponent("avatar.png"); if FileManager.default.fileExists(atPath: file.path) { do { try backupAvatar(); try FileManager.default.removeItem(at: file); reloadAvatar() } catch { status.stringValue = "头像重置失败" } } }
    @objc func undoAvatar() { let backup = support.appendingPathComponent("avatar-undo.bin"); guard let data = try? Data(contentsOf: backup) else { return }; do { let target = support.appendingPathComponent("avatar.png"); if data.isEmpty { if FileManager.default.fileExists(atPath: target.path) { try FileManager.default.removeItem(at: target) } } else { try data.write(to: target, options: .atomic) }; try FileManager.default.removeItem(at: backup); reloadAvatar() } catch { status.stringValue = "撤回失败" } }
}
let app = NSApplication.shared
let delegate = Assistant()
app.delegate = delegate
app.setActivationPolicy(.regular)
app.run()
