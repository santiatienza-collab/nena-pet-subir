import AppKit
import ApplicationServices

// ---------------------------------------------------------------- ajustes
final class Settings {
    static let d = UserDefaults.standard
    static var scale: Double { get { let v = d.double(forKey: "scale"); return v > 0 ? v : 1.0 } set { d.set(newValue, forKey: "scale") } }
    static var sound: Bool { get { d.object(forKey: "sound") == nil ? true : d.bool(forKey: "sound") } set { d.set(newValue, forKey: "sound") } }
    static var hasPos: Bool { return d.object(forKey: "px") != nil }
    static var px: Double { get { d.double(forKey: "px") } set { d.set(newValue, forKey: "px") } }
    static var py: Double { get { d.double(forKey: "py") } set { d.set(newValue, forKey: "py") } }
    static var supportDir: URL {
        let u = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("NenaPet")
        try? FileManager.default.createDirectory(at: u, withIntermediateDirectories: true)
        return u
    }
}

// ---------------------------------------------------------------- vista con la gata
final class CatView: NSView {
    let cat: Cat
    var onClick: (() -> Void)?
    var onMenu: ((NSEvent) -> Void)?
    private var downScreen = NSPoint.zero, downOrigin = NSPoint.zero, dragging = false

    init(cat: Cat) { self.cat = cat; super.init(frame: .zero) }
    required init?(coder: NSCoder) { fatalError() }
    override var isFlipped: Bool { return true }
    override var isOpaque: Bool { return false }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { return true }

    override func draw(_ dirtyRect: NSRect) {
        guard let c = NSGraphicsContext.current?.cgContext else { return }
        c.clear(bounds)
        c.setShouldAntialias(true)
        cat.draw(c, size: bounds.size)
    }

    override func mouseDown(with e: NSEvent) {
        downScreen = NSEvent.mouseLocation; downOrigin = window?.frame.origin ?? .zero; dragging = false
    }
    override func mouseDragged(with e: NSEvent) {
        let p = NSEvent.mouseLocation
        let dx = p.x - downScreen.x, dy = p.y - downScreen.y
        if !dragging && hypot(dx, dy) > 5 { dragging = true }
        if dragging { window?.setFrameOrigin(NSPoint(x: downOrigin.x + dx, y: downOrigin.y + dy)) }
    }
    override func mouseUp(with e: NSEvent) {
        if dragging, let f = window?.frame { Settings.px = Double(f.origin.x); Settings.py = Double(f.origin.y); dragging = false }
        else { onClick?() }
    }
    override func rightMouseUp(with e: NSEvent) { onMenu?(e) }
}

// ---------------------------------------------------------------- aplicación
final class AppDelegate: NSObject, NSApplicationDelegate {
    var window: NSWindow!
    var view: CatView!
    let cat = Cat()
    var audio: AudioPlayer!
    var timer: Timer?
    var statusItem: NSStatusItem!
    let t0 = Date()
    let baseW = 240.0, baseH = 135.0
    var menu = NSMenu()

    func now() -> Double { return Date().timeIntervalSince(t0) * 1000 }

    func applicationDidFinishLaunching(_ n: Notification) {
        let bundled = Bundle.main.resourceURL?.appendingPathComponent("sounds")
        audio = AudioPlayer(bundled: bundled, user: Settings.supportDir.appendingPathComponent("sonidos"))
        audio.setEnabled(Settings.sound)
        cat.audio = audio

        let sc = Settings.scale
        let w = baseW * sc, h = baseH * sc
        let vf = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1440, height: 900)
        var origin = NSPoint(x: vf.maxX - w - 8, y: vf.minY - 2)
        if Settings.hasPos {
            let p = NSPoint(x: Settings.px, y: Settings.py)
            if NSScreen.screens.contains(where: { $0.frame.insetBy(dx: -20, dy: -20).contains(NSPoint(x: p.x + w / 2, y: p.y + 10)) }) { origin = p }
        }
        window = NSWindow(contentRect: NSRect(origin: origin, size: NSSize(width: w, height: h)), styleMask: .borderless, backing: .buffered, defer: false)
        window.isOpaque = false; window.backgroundColor = .clear; window.hasShadow = false
        window.level = .floating
        window.collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary, .ignoresCycle]
        window.isReleasedWhenClosed = false
        view = CatView(cat: cat)
        view.onClick = { [weak self] in guard let s = self else { return }; s.cat.click(s.now()) }
        view.onMenu = { [weak self] e in guard let s = self else { return }; NSMenu.popUpContextMenu(s.menu, with: e, for: s.view) }
        window.contentView = view
        cat.scaleToDip = w / 300.0
        window.orderFrontRegardless()

        buildMenu()
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let b = statusItem.button {
            if #available(macOS 11.0, *) { b.image = NSImage(systemSymbolName: "pawprint.fill", accessibilityDescription: "Nena Pet") }
            else { b.title = "🐱" }
        }
        statusItem.menu = menu

        // permiso para ver las pulsaciones del teclado (Accesibilidad / Supervisión de entrada)
        let opts = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(opts)
        NSEvent.addGlobalMonitorForEvents(matching: [.keyDown]) { [weak self] e in
            guard let s = self, !e.isARepeat else { return }
            s.countKey()
        }
        NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown, .scrollWheel]) { [weak self] _ in
            guard let s = self else { return }; s.cat.activity(s.now())
        }

        timer = Timer(timeInterval: 1.0 / 60.0, repeats: true) { [weak self] _ in self?.frame() }
        RunLoop.main.add(timer!, forMode: .common)
    }

    // Cuenta una pulsación (sin duplicados entre las dos vías de detección)
    var lastKeyCountMs = -1e9
    func countKey() {
        let t = now()
        if t - lastKeyCountMs < 35 { return }
        lastKeyCountMs = t
        cat.keyPress(t)
    }

    // Segunda vía, independiente del idioma: se mira el reloj de eventos del sistema.
    // Con teclados chinos, japoneses o coreanos (editores IME) el editor consume la tecla y el monitor global no la ve,
    // pero el sistema sí registra que hubo una pulsación de teclado.
    var lastKeySecs = Double.greatestFiniteMagnitude
    func pollKeyboard() {
        let secs = CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: .keyDown)
        if secs < lastKeySecs - 0.002 { countKey() }
        lastKeySecs = secs
    }

    func frame() {
        pollKeyboard()
        let t = now()
        // cursor relativo a la ventana (origen arriba a la izquierda)
        let m = NSEvent.mouseLocation, f = window.frame
        cat.setCursor(Double(m.x - f.minX), Double(f.maxY - m.y), t)
        if cat.tick(t) { view.needsDisplay = true }
    }

    // ---------------------------------------------------------------- menú
    func buildMenu() {
        menu = NSMenu()
        let size = NSMenuItem(title: "Tamaño", action: nil, keyEquivalent: ""); let sm = NSMenu()
        for (name, v) in [("Mini", 0.7), ("Normal", 1.0), ("Grande", 1.4)] {
            let it = NSMenuItem(title: name, action: #selector(setSize(_:)), keyEquivalent: ""); it.target = self; it.tag = Int(v * 100)
            it.state = abs(Settings.scale - v) < 0.05 ? .on : .off; sm.addItem(it)
        }
        size.submenu = sm; menu.addItem(size)
        let hide = NSMenuItem(title: window.isVisible ? "Ocultar" : "Mostrar", action: #selector(toggleVisible), keyEquivalent: ""); hide.target = self; menu.addItem(hide)
        let snd = NSMenuItem(title: "Sonidos", action: #selector(toggleSound), keyEquivalent: ""); snd.target = self; snd.state = Settings.sound ? .on : .off; menu.addItem(snd)
        let auto = NSMenuItem(title: "Iniciar con el Mac", action: #selector(toggleAutostart), keyEquivalent: ""); auto.target = self; auto.state = autostartOn ? .on : .off; menu.addItem(auto)
        let folder = NSMenuItem(title: "Abrir carpeta de sonidos…", action: #selector(openSounds), keyEquivalent: ""); folder.target = self; menu.addItem(folder)
        menu.addItem(NSMenuItem.separator())
        let q = NSMenuItem(title: "Salir", action: #selector(quit), keyEquivalent: "q"); q.target = self; menu.addItem(q)
        menu.delegate = self
        statusItem?.menu = menu
    }

    @objc func setSize(_ s: NSMenuItem) {
        Settings.scale = Double(s.tag) / 100.0
        let w = baseW * Settings.scale, h = baseH * Settings.scale
        let f = window.frame
        window.setFrame(NSRect(x: f.maxX - w, y: f.minY, width: w, height: h), display: true)
        cat.scaleToDip = w / 300.0
        view.needsDisplay = true
    }
    @objc func toggleVisible() { if window.isVisible { window.orderOut(nil) } else { window.orderFrontRegardless() } }
    @objc func toggleSound() { Settings.sound.toggle(); audio.setEnabled(Settings.sound) }
    @objc func openSounds() {
        let d = Settings.supportDir.appendingPathComponent("sonidos")
        try? FileManager.default.createDirectory(at: d, withIntermediateDirectories: true)
        NSWorkspace.shared.open(d)
    }
    @objc func quit() { NSApp.terminate(nil) }

    var plistURL: URL { return FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/LaunchAgents/com.nena.pet.plist") }
    var autostartOn: Bool { return FileManager.default.fileExists(atPath: plistURL.path) }
    @objc func toggleAutostart() {
        if autostartOn { try? FileManager.default.removeItem(at: plistURL); return }
        guard let exe = Bundle.main.executablePath else { return }
        let plist: [String: Any] = ["Label": "com.nena.pet", "ProgramArguments": [exe], "RunAtLoad": true]
        try? FileManager.default.createDirectory(at: plistURL.deletingLastPathComponent(), withIntermediateDirectories: true)
        (plist as NSDictionary).write(to: plistURL, atomically: true)
    }
}

extension AppDelegate: NSMenuDelegate {
    func menuNeedsUpdate(_ menu: NSMenu) {
        for it in menu.items {
            switch it.title {
            case "Ocultar", "Mostrar": it.title = window.isVisible ? "Ocultar" : "Mostrar"
            case "Sonidos": it.state = Settings.sound ? .on : .off
            case "Iniciar con el Mac": it.state = autostartOn ? .on : .off
            default: break
            }
        }
    }
}

let app = NSApplication.shared
app.setActivationPolicy(.accessory)
let delegate = AppDelegate()
app.delegate = delegate
app.run()
