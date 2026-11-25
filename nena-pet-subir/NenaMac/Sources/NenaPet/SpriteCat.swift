import AppKit

// Utilidades matemáticas
enum G {
    static func clamp(_ v: Double, _ a: Double, _ b: Double) -> Double { return max(a, min(b, v)) }
    static func rnd(_ a: Double, _ b: Double) -> Double { return a + Double.random(in: 0..<1) * (b - a) }
    static func rand() -> Double { return Double.random(in: 0..<1) }
}

// Nena con animación de sprites (6 hojas de 16 fotogramas, todos alineados con la misma cámara: no hay saltos).
// Funciones: teclear (golpes que suben de intensidad con el ritmo, enfado/explosión al teclear muy rápido, cansancio después),
// ojos que siguen al cursor, ojos entornados + run run cada 15 s, miau al clic, aburrimiento (bostezo, mirar arriba, dormir...).
final class Cat {
    static let W = 327.0, H = 184.0            // tamaño de los fotogramas
    var audio: AudioPlayer?
    var scaleToDip = 0.8                        // ancho de ventana / 300

    private var frames: [String: CGImage] = [:]
    private var cur = "B01n", prev: String? = nil
    private var fadeStart = -1e9, fadeLen = 100.0, frameChange = -1e9
    private var nowT = 0.0

    private var lastKey = -1e9, lastAct = 0.0, lastMove = -1e9
    private var keyIdx = 0
    private var keyTimes: [Double] = []
    private var curX = 150.0, curY = 90.0, prevX = -1.0, prevY = -1.0, havePrev = false
    private let PS = 1.0, MX0 = 221.0, MY0 = 152.0
    private var mouseTX = 0.0, mouseTY = 0.0, mox = 0.0, moy = 0.0, armK = 0.0, onMouse = false
    private var loNow = 0.0, mouthK = 0.0, mouthT = 0.0, relaxBase = 0, relaxOff = 0.0, wasRelaxed = false
    private var hitStr = [0.0, 0.0], attK = 0.0, pupilS = 1.0, purrOn = false, earPerk = 0.0, stSx = 1.0
    private var mode = "idle", modeStart = 0.0, modeEnd = 0.0
    private var nextPurr = 15000.0, nextBored = 20000.0, fastSince = -1.0, tired = false
    private var boredKind = "yawn", lastKind = ""
    private var gaze = "C"
    private var blinkAt = 9000.0
    // ojos en vivo sobre el fotograma sin pupilas (B01n)
    private var egx = 0.0, egy = 0.0, lidC = 0.38, lidA = 3.5, blinkUntil = -1.0, saccX = 0.0, saccY = 0.0, saccNext = 0.0, lastTick = 0.0
    private var hitAt = [-1e9, -1e9], hitRest = [-11.0, -11.0], hitTau = [100.0, 100.0], hitJx = [0.0, 0.0], hitRotA = [0.0, 0.0], hitScale = [1.0, 1.0]
    private var stageX = 0.0, puppetX = 0.0, puppetKick = -1e9, puppetDir = 1.0, stTx = 0.0, stTy = 0.0, stRot = 0.0, stSy = 1.0
    private var earNext = 4000.0, earStart = -1e9, earSide = 0, earDouble = false, earAng = [0.0, 0.0]
    private var prevKeyT = -1e9, burst = 0, clumsy = 0.0
    private var hitDepth = [2.0, 2.0], hitCl = [0.0, 0.0]
    private var veinK = 0.0, veinFrame = "", veinNow = 0.0
    private var kickAt = -1e9, offY = 0.0, scY = 1.0, relaxedNow = false, frameChangeAt = -1e9
    private let EX = [136.5, 184.0], EY = 72.0, ER = 10.2
    private let purrSeq = ["D03", "D07", "D11", "D07", "D03", "D10", "D06", "D10"]
    private let T0 = ["A05", "A05m"]
    private let T1 = ["F05", "F05m"]
    private let T2 = ["F05", "A06", "F05m", "A08"]
    private let T3 = ["A10", "A11", "F10", "F11"]

    init() {
        if let u = Bundle.main.url(forResource: "B01n", withExtension: "png", subdirectory: "frames"), let img = NSImage(contentsOf: u), let cg = img.cgImage(forProposedRect: nil, context: nil, hints: nil) { frames["B01n"] = cg }
        for ex in ["A05m", "F05m", "P_base", "P_L", "P_R", "E_L", "E_R"] {
            if let u = Bundle.main.url(forResource: ex, withExtension: "png", subdirectory: "frames"), let img = NSImage(contentsOf: u), let cg = img.cgImage(forProposedRect: nil, context: nil, hints: nil) { frames[ex] = cg }
        }
        for L in "ABCDEF" {
            for i in 0..<16 {
                let n = String(L) + String(format: "%02d", i)
                if let u = Bundle.main.url(forResource: n, withExtension: "png", subdirectory: "frames"),
                   let img = NSImage(contentsOf: u), let cg = img.cgImage(forProposedRect: nil, context: nil, hints: nil) {
                    frames[n] = cg
                }
            }
        }
    }

    // ---------------------------------------------------------------- API
    func activity(_ now: Double) { lastAct = now }
    func setCursor(_ x: Double, _ y: Double, _ now: Double) {
        let k = scaleToDip > 0 ? 1.0 / scaleToDip : 1.25
        let nx = x * k / PS, ny = (y * k - 169.0 * (1 - PS)) / PS
        if havePrev { mouseTX = G.clamp(mouseTX + (nx - curX) * 0.4, -5, 9); mouseTY = G.clamp(mouseTY + (ny - curY) * 0.4, -4, 4) }
        curX = nx; curY = ny
        if havePrev && abs(x - prevX) + abs(y - prevY) > 2 { lastMove = now; lastAct = now }
        prevX = x; prevY = y; havePrev = true
    }
    func keyPress(_ now: Double) {
        if mode != "type" { stopPurr() }
        lastKey = now; lastAct = now; keyIdx += 1; kickAt = now
        if now - prevKeyT > 1500 { burst = 0 }
        prevKeyT = now; burst += 1
        do {
            // los primeros golpes de una racha son torpes: patita entera, más arriba, algo torcida y a destiempo; luego se afina
            // los primeros golpes de una racha: como si un humano la sujetara por detrás y le aporreara el teclado con las patas;
            // luego va tomando el control ella sola
            let p = keyIdx % 2, cl = max(0, 1 - Double(burst - 1) / 10.0); clumsy = cl
            let both = cl > 0.2 && G.rand() < 0.55 * cl
            for q in 0..<2 where both || q == p {
                hitAt[q] = now; hitRest[q] = -(11 + 14 * cl); hitTau[q] = 100 - 30 * cl; hitCl[q] = 0
                hitJx[q] = (G.rand() * 2 - 1) * (5 + 5 * G.rand()); hitRotA[q] = (G.rand() * 2 - 1) * 3; hitScale[q] = 1.12 + G.rand() * 0.2; hitStr[q] = 0.28 + G.rand() * 0.32; hitDepth[q] = 2 + 2 * cl
            }
            puppetKick = now; puppetDir = G.rand() < 0.5 ? -1 : 1; puppetX = (G.rand() * 2 - 1) * 2.5 * cl
        }
        keyTimes.append(now); keyTimes.removeAll { now - $0 > 1000 }
        mode = "type"
    }
    func click(_ now: Double) {
        stopPurr()
        let d = audio?.playMeow() ?? 0.8
        lastAct = now; mode = "meow"; modeStart = now; modeEnd = now + max(700, d * 1000)
    }
    private func stopPurr() { audio?.stopPurr() }
    private func toIdle(_ now: Double) { mode = "idle"; nextPurr = now + 6300; nextBored = now + 20000 }
    private func pick(_ a: [String], _ i: Int) -> String { return a[((i % a.count) + a.count) % a.count] }

    private func show(_ name: String, _ len: Double = 100) {
        guard frames[name] != nil else { return }
        if name != cur { prev = cur; cur = name; fadeStart = nowT; fadeLen = len; frameChange = nowT; frameChangeAt = nowT }
    }

    // ---------------------------------------------------------------- dibujo (coordenadas con origen arriba a la izquierda)
    func draw(_ c: CGContext, size: CGSize) {
        let k = Double(size.width) / Cat.W
        let w = Cat.W * k, h = Cat.H * k
        let rect = CGRect(x: 0, y: Double(size.height) - h + offY * k * 0.917, width: w, height: h)
        func put(_ name: String, _ alpha: Double, _ dy: Double = 0, _ dx: Double = 0, _ rot: Double = 0, _ sc: Double = 1, _ pcx: Double = 0, _ pcy: Double = 0, _ syk: Double = 1) {
            guard let img = frames[name], alpha > 0.003 else { return }
            c.saveGState()
            c.setAlpha(CGFloat(alpha))
            // la vista está invertida (y hacia abajo): se voltea la imagen para dibujarla derecha
            c.translateBy(x: rect.minX + dx * k, y: rect.maxY + dy * k); c.scaleBy(x: 1, y: -1)
            if rot != 0 || sc != 1 || syk != 1 {
                let ox = pcx * k, oy = (Cat.H - pcy) * k
                c.translateBy(x: ox, y: oy); c.rotate(by: CGFloat(-rot * Double.pi / 180)); c.scaleBy(x: CGFloat(sc), y: CGFloat(sc * syk)); c.translateBy(x: -ox, y: -oy)
            }
            c.draw(img, in: CGRect(x: 0, y: 0, width: rect.width, height: rect.height))
            c.restoreGState()
        }
        c.interpolationQuality = .high
        c.saveGState()   // pet: gata y teclado a menor tamaño, con sitio a la derecha para el ratón
        c.translateBy(x: 0, y: size.height); c.scaleBy(x: CGFloat(PS), y: CGFloat(PS)); c.translateBy(x: 0, y: -size.height)

        c.saveGState()
        do {
            c.translateBy(x: CGFloat(rect.midX + stTx * k), y: CGFloat(rect.maxY + stTy * k))
            c.rotate(by: CGFloat(stRot * Double.pi / 180)); c.scaleBy(x: CGFloat(stSx), y: CGFloat(stSy))
            c.translateBy(x: CGFloat(-rect.midX), y: CGFloat(-rect.maxY))
        }
        let f = max(0, 1 - (nowT - fadeStart) / fadeLen)
        if let p = prev, f > 0 { put(p, 1); put(cur, 1 - f) } else { put(cur, 1) }
        if cur == "P_base" {
            let fade = min(1, (nowT - frameChangeAt) / 90.0)
            let sxs = [126.0, 201.0]
            if onMouse {
                put("P_L", min(1, armK * 1.3), 0)      // la derecha está en el ratón
            } else {
                for i in 0..<2 {
                    let t = max(0, nowT - hitAt[i])
                    let ex = exp(-t / (hitTau[i] * 0.9))
                    let yy = nowT < hitAt[i] ? hitRest[i] : hitRest[i] + (hitDepth[i] - hitRest[i]) * exp(-t / hitTau[i])
                    put(i == 0 ? "P_L" : "P_R", fade, yy, hitJx[i] * ex, hitRotA[i] * ex, 1 + (hitScale[i] - 1) * ex, sxs[i], 140.0, 1 + hitStr[i] * ex)
                    if nowT >= hitAt[i] && t < 170 {
                        c.saveGState(); c.setAlpha(CGFloat((1 - t / 170.0) * 0.6))
                        c.translateBy(x: rect.minX, y: rect.minY); c.scaleBy(x: k, y: k)
                        c.setFillColor(CGColor(srgbRed: 228/255, green: 228/255, blue: 238/255, alpha: 1))
                        let bx = (sxs[i] + hitJx[i] * 0.8) / 0.917, by = 170.0 / 0.917
                        for off in [-21.0, -6.0, 9.0] { c.fill(CGRect(x: bx + off / 0.917, y: by - 3 / 0.917, width: 12 / 0.917, height: 7 / 0.917)) }
                        c.restoreGState()
                    }
                }
            }
        }
        if veinK > 0.01 {
            let mirror = veinFrame.hasSuffix("m")
            let vx = mirror ? 327.0 - 204.0 : 204.0, vy = 40.0 + offY
            let pulse = 1 + 0.22 * max(0, sin(veinNow / 1000.0 * 14)), pop = 0.55 + 0.45 * veinK
            let sc = 0.62 * pulse * pop
            c.saveGState(); c.setAlpha(CGFloat(min(1, veinK * 1.5)))
            c.translateBy(x: rect.minX, y: rect.minY); c.scaleBy(x: k, y: k)
            c.translateBy(x: vx, y: vy); c.rotate(by: CGFloat(sin(veinNow / 1000.0 * 9) * 4 * Double.pi / 180)); c.scaleBy(x: sc, y: sc)
            let red = CGColor(srgbRed: 238/255, green: 28/255, blue: 37/255, alpha: 1), edge = CGColor(srgbRed: 107/255, green: 0, blue: 8/255, alpha: 1)
            for i in 0..<4 {
                c.saveGState(); c.rotate(by: CGFloat(Double(i) * Double.pi / 2))
                c.beginPath(); c.move(to: CGPoint(x: 3, y: -3)); c.addLine(to: CGPoint(x: 2.5, y: -19)); c.addQuadCurve(to: CGPoint(x: 19, y: -2.5), control: CGPoint(x: 6, y: -7)); c.closePath()
                c.setFillColor(red); c.setStrokeColor(edge); c.setLineWidth(1.8); c.setLineJoin(.round); c.drawPath(using: .fillStroke)
                c.restoreGState()
            }
            c.restoreGState()
        }
        if cur == "B01n" || cur == "P_base" {
            let ea = min(1, (nowT - frameChangeAt) / 110.0)
            if ea > 0.003 {
                put("E_L", ea, 0, 0, earAng[0], 1, 126, 48); put("E_R", ea, 0, 0, earAng[1], 1, 194, 48)
                c.saveGState(); c.setAlpha(CGFloat(ea)); c.translateBy(x: rect.minX, y: rect.minY); c.scaleBy(x: k, y: k); drawEyes(c); c.restoreGState()
                if mouthK > 0.01 {
                    // boca neutra: se tapa la boca seria y se dibuja una línea tranquila
                    c.saveGState(); c.setAlpha(CGFloat(ea * mouthK)); c.translateBy(x: rect.minX, y: rect.minY); c.scaleBy(x: k, y: k)
                    c.setFillColor(CGColor(srgbRed: 252/255, green: 249/255, blue: 244/255, alpha: 1))
                    c.addPath(CGPath(roundedRect: CGRect(x: 148, y: 89, width: 26, height: 15), cornerWidth: 5, cornerHeight: 5, transform: nil)); c.fillPath()
                    c.setStrokeColor(CGColor(srgbRed: 30/255, green: 18/255, blue: 14/255, alpha: 1)); c.setLineWidth(1.6); c.setLineCap(.round)
                    c.beginPath(); c.move(to: CGPoint(x: 153, y: 98.4)); c.addQuadCurve(to: CGPoint(x: 169, y: 98.4), control: CGPoint(x: 161, y: 96.4))
                    c.move(to: CGPoint(x: 161, y: 89)); c.addLine(to: CGPoint(x: 161, y: 95)); c.strokePath()
                    c.beginPath(); c.move(to: CGPoint(x: 154.2, y: 97.4)); c.addLine(to: CGPoint(x: 158.6, y: 98)); c.addLine(to: CGPoint(x: 156.2, y: 104.6)); c.closePath()
                    c.setFillColor(CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 1)); c.setLineWidth(0.9); c.setLineJoin(.round); c.drawPath(using: .fillStroke); c.restoreGState()
                }
            }
        }
        c.restoreGState()

        c.restoreGState()
    }

    // Teclado pequeño (queda detrás de la gata, con el ratón pegado a su derecha)
    private func drawKeyboard(_ c: CGContext, _ size: CGSize) {
        let u = Double(size.width) / 300.0
        let edge = CGColor(srgbRed: 20/255, green: 20/255, blue: 26/255, alpha: 1)
        c.saveGState(); c.scaleBy(x: CGFloat(u), y: CGFloat(u))
        c.setStrokeColor(edge); c.setLineJoin(.round)
        c.setFillColor(CGColor(srgbRed: 58/255, green: 60/255, blue: 70/255, alpha: 1)); c.setLineWidth(2)
        c.beginPath(); c.move(to: CGPoint(x: 84, y: 139)); c.addLine(to: CGPoint(x: 190, y: 139)); c.addLine(to: CGPoint(x: 201, y: 163)); c.addLine(to: CGPoint(x: 73, y: 163)); c.closePath(); c.drawPath(using: .fillStroke)
        c.setFillColor(CGColor(srgbRed: 38/255, green: 39/255, blue: 46/255, alpha: 1)); c.setLineWidth(1.2)
        c.beginPath(); c.move(to: CGPoint(x: 73, y: 163)); c.addLine(to: CGPoint(x: 201, y: 163)); c.addLine(to: CGPoint(x: 203, y: 167)); c.addLine(to: CGPoint(x: 71, y: 167)); c.closePath(); c.drawPath(using: .fillStroke)
        c.setFillColor(CGColor(srgbRed: 30/255, green: 31/255, blue: 38/255, alpha: 1)); c.setStrokeColor(CGColor(srgbRed: 92/255, green: 95/255, blue: 108/255, alpha: 1)); c.setLineWidth(0.5)
        let counts = [9, 8, 8, 6]
        for r in 0..<4 {
            let y = 141.5 + Double(r) * 5.5, t = (y - 139) / 24.0
            let xl = 84 - 11 * t + 4, xr = 190 + 11 * t - 4, w = (xr - xl) / Double(counts[r])
            for col in 0..<counts[r] {
                if r == 3 && (col == 3 || col == 4) { continue }
                let space = r == 3 && col == 2
                let rect = CGRect(x: xl + Double(col) * w + 0.75, y: y, width: (space ? w * 3 : w) - 1.5, height: 4.0)
                c.addPath(CGPath(roundedRect: rect, cornerWidth: 1, cornerHeight: 1, transform: nil)); c.drawPath(using: .fillStroke)
            }
        }
        c.restoreGState()
    }

    // El ratón, el brazo que se estira hasta él y la patita que lo maneja
    private func drawMouse(_ c: CGContext, _ size: CGSize) {
        let u = Double(size.width) / 300.0
        let mx = MX0 + mox, my = MY0 + moy
        let edge = CGColor(srgbRed: 22/255, green: 22/255, blue: 28/255, alpha: 1)
        c.saveGState(); c.scaleBy(x: CGFloat(u), y: CGFloat(u)); c.translateBy(x: CGFloat(mx), y: CGFloat(my)); c.scaleBy(x: 0.85, y: 0.85)
        c.setFillColor(CGColor(srgbRed: 0, green: 0, blue: 0, alpha: 0.31)); c.fillEllipse(in: CGRect(x: -15, y: 13, width: 30, height: 9))
        c.setStrokeColor(edge); c.setLineWidth(1.8); c.setLineCap(.round)
        c.beginPath(); c.move(to: CGPoint(x: 0, y: 15)); c.addCurve(to: CGPoint(x: 15, y: 21), control1: CGPoint(x: 2, y: 19), control2: CGPoint(x: 8, y: 20)); c.strokePath()
        c.setLineJoin(.round); c.setLineWidth(2)
        func body(_ fill: CGColor, _ y0: Double) {
            c.setFillColor(fill); c.beginPath(); c.move(to: CGPoint(x: -9, y: y0))
            c.addQuadCurve(to: CGPoint(x: 9, y: y0), control: CGPoint(x: 0, y: y0 - 3)); c.addQuadCurve(to: CGPoint(x: 13, y: y0 + 3), control: CGPoint(x: 12, y: y0 + 3))
            c.addQuadCurve(to: CGPoint(x: 13, y: y0 + 19), control: CGPoint(x: 13, y: y0 + 16)); c.addQuadCurve(to: CGPoint(x: 0, y: y0 + 33), control: CGPoint(x: 13, y: y0 + 31))
            c.addQuadCurve(to: CGPoint(x: -13, y: y0 + 19), control: CGPoint(x: -13, y: y0 + 31)); c.addQuadCurve(to: CGPoint(x: -9, y: y0), control: CGPoint(x: -13, y: y0 + 3))
            c.closePath(); c.drawPath(using: .fillStroke)
        }
        body(CGColor(srgbRed: 40/255, green: 43/255, blue: 52/255, alpha: 1), -13)
        body(CGColor(srgbRed: 78/255, green: 82/255, blue: 96/255, alpha: 1), -17)
        c.setLineWidth(1.4); c.beginPath(); c.move(to: CGPoint(x: -12, y: 2)); c.addQuadCurve(to: CGPoint(x: 12, y: 2), control: CGPoint(x: 0, y: 5)); c.move(to: CGPoint(x: 0, y: 15)); c.addLine(to: CGPoint(x: 0, y: 3.5)); c.strokePath()
        c.setFillColor(CGColor(srgbRed: 214/255, green: 218/255, blue: 226/255, alpha: 1)); c.fill(CGRect(x: -1.8, y: 3.5, width: 3.6, height: 7))
        c.setFillColor(CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 0.27)); c.fillEllipse(in: CGRect(x: -5.5, y: -13, width: 11, height: 6))
        c.restoreGState()
        if armK > 0.01 {
            let px = mx - 1, py = my - 4
            c.saveGState(); c.scaleBy(x: CGFloat(u), y: CGFloat(u)); c.setAlpha(CGFloat(armK))
            c.setStrokeColor(CGColor(srgbRed: 44/255, green: 29/255, blue: 23/255, alpha: 1)); c.setLineWidth(15); c.setLineCap(.round)
            c.beginPath(); c.move(to: CGPoint(x: 184, y: 143)); c.addQuadCurve(to: CGPoint(x: px - 3, y: py - 1), control: CGPoint(x: (184 + px) / 2, y: max(143, py) + 5)); c.strokePath()
            c.restoreGState()
            if let img = frames["P_R"] {
                let kf = Double(size.width) / Cat.W, w = Cat.W * kf, h = Cat.H * kf, top = Double(size.height) - h
                c.saveGState(); c.setAlpha(CGFloat(armK))
                c.translateBy(x: CGFloat((px - 184.0) * u), y: CGFloat(top + h + (py - 140.0) * u)); c.scaleBy(x: 1, y: -1)
                let ox = 201.0 * kf, oy = (Cat.H - 154.0) * kf
                c.translateBy(x: CGFloat(ox), y: CGFloat(oy)); c.rotate(by: CGFloat(-9 * armK * Double.pi / 180)); c.scaleBy(x: CGFloat(1 + 0.08 * armK), y: CGFloat(1 + 0.12 * armK)); c.translateBy(x: CGFloat(-ox), y: CGFloat(-oy))
                c.draw(img, in: CGRect(x: 0, y: 0, width: w, height: h)); c.restoreGState()
            }
        }
    }

    // Al mover el cursor la gata estira la pata derecha hasta el ratón y lo arrastra; al teclear lo suelta
    private func updateMouse(_ now: Double, _ dt: Double, _ typing: Bool) {
        let want = !typing && now - lastMove < 1400
        armK += ((want ? 1.0 : 0.0) - armK) * min(1, dt * 7)
        mox += (mouseTX - mox) * min(1, dt * 14); moy += (mouseTY - moy) * min(1, dt * 14)
        let dec = pow(0.25, dt); mouseTX *= dec; mouseTY *= dec
    }

    // De vez en cuando mueve un poquito las orejas (una, la otra o las dos; a veces dos sacudidas seguidas)
    private func updateEars(_ now: Double) {
        if now >= earNext { earStart = now; earSide = Int.random(in: 0..<3); earDouble = G.rand() < 0.35; earNext = now + 3500 + G.rand() * 6500 }
        var f = 0.0; let u = (now - earStart) / 200.0, u2 = (now - earStart - 270) / 200.0
        if u >= 0 && u <= 1 { f = sin(Double.pi * u) } else if earDouble && u2 >= 0 && u2 <= 1 { f = sin(Double.pi * u2) * 0.8 }
        for i in 0..<2 { earAng[i] = ((earSide == 2 || earSide == i) ? (i == 0 ? -1.0 : 1.0) * 4.2 * f : 0) + (i == 0 ? -1.0 : 1.0) * earPerk }
    }

    // El cuerpo entero se mueve cuando "lo maneja un humano"; vuelve a su sitio al dejar de teclear
    private func stageStep(_ now: Double, _ dt: Double, _ cl: Double) {
        let pulse = exp(-(now - puppetKick) / 120.0)
        stageX += (puppetX - stageX) * min(1, dt * 10)
        stTx = stageX; stTy = cl * 1.2 * pulse
        stRot = cl * (puppetDir * 0.9 * pulse + stageX * 0.08); stSy = 1 - cl * 0.012 * pulse
    }

    private func drawEyes(_ c: CGContext) {
        let lidBrown = CGColor(srgbRed: 42/255, green: 28/255, blue: 22/255, alpha: 1)
        let lineC = CGColor(srgbRed: 30/255, green: 18/255, blue: 14/255, alpha: 1)
        let darkC = CGColor(srgbRed: 20/255, green: 12/255, blue: 14/255, alpha: 1)
        for i in 0..<2 {
            let cx = EX[i], cy = EY, dirIn: Double = i == 0 ? 1 : -1
            c.saveGState()
            c.addEllipse(in: CGRect(x: cx - (ER - 0.3), y: cy - (ER - 0.3), width: (ER - 0.3) * 2, height: (ER - 0.3) * 2)); c.clip()
            let px = cx + egx * 5.0, py = cy + egy * 3.0
            c.setFillColor(darkC); c.fillEllipse(in: CGRect(x: px - 3.3 * pupilS, y: py - 6.5 * pupilS, width: 6.6 * pupilS, height: 13 * pupilS))
            c.setFillColor(CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 0.95)); c.fillEllipse(in: CGRect(x: px - 3.2, y: py - 5.6, width: 3.4, height: 3.8))
            let top = cy - ER, h = ER * 2
            let yl = cy + ER - loNow * h
            c.setFillColor(lidBrown)
            c.fill(CGRect(x: cx - ER - 1, y: yl, width: 2 * ER + 2, height: ER + 4))
            let yOut = top + lidC * h - lidA * 0.5, yIn = top + lidC * h + lidA
            let xo = cx - dirIn * (ER + 1), xi = cx + dirIn * (ER + 1)
            c.beginPath(); c.move(to: CGPoint(x: xo, y: top - 3)); c.addLine(to: CGPoint(x: xi, y: top - 3)); c.addLine(to: CGPoint(x: xi, y: yIn)); c.addLine(to: CGPoint(x: xo, y: yOut)); c.closePath(); c.fillPath()
            c.setStrokeColor(lineC); c.setLineWidth(1.7); c.setLineJoin(.round)
            c.beginPath(); c.move(to: CGPoint(x: xo, y: yOut)); c.addLine(to: CGPoint(x: xi, y: yIn)); c.strokePath()
            c.restoreGState()
        }
    }

    // ---------------------------------------------------------------- bucle. Devuelve true si hay que redibujar.
    @discardableResult
    func tick(_ now: Double) -> Bool {
        nowT = now
        updateEars(now)
        if now - lastKey >= 650 { veinK = 0 }
        audio?.update(now)
        let wasFading = (now - fadeStart) < fadeLen + 20
        let before = cur
        step(now)
        return true
    }

    private func step(_ now: Double) {
        keyTimes.removeAll { now - $0 > 1000 }
        let rate = Double(keyTimes.count)
        let typing = now - lastKey < 650
        let sinceAct = now - lastAct
        let dt = min(0.05, max(0.001, (now - lastTick) / 1000.0)); lastTick = now
        if now - lastKey >= 650 { puppetX = 0; clumsy = 0; stageStep(now, dt, 0); stSx = 1 }

        if typing || mode != "idle" { onMouse = false }

        if typing {
            mode = "type"
            if rate >= 7 { if fastSince < 0 { fastSince = now }; if now - fastSince > 2500 { tired = true } }
            else if rate < 3 { fastSince = -1 }
            if rate >= 10 {
                var n = pick(T3, keyIdx)
                if n != cur && now - frameChange < 70 { n = cur }
                offY = 0
                show(n, 80)
                puppetX = 0; stageStep(now, dt, 0)
                veinK = 1; veinFrame = cur; veinNow = now
            } else {
                // el cuerpo y la cabeza no se mueven: solo las patas (alternando) y los ojos/ceño
                offY = 0
                show("P_base", 80)
                let lvl = G.clamp((rate - 3) / 6.0, 0, 1)
                eyesStep(now, dt, sin(now / 420.0) * 0.5 * clumsy, 0.9 * (1 - clumsy) - 0.1 * clumsy, (0.30 + 0.12 * lvl) * (1 - clumsy) + 0.03 * clumsy, (4.0 + 4.5 * lvl) * (1 - clumsy) - 1.5 * clumsy, 0, 0.3)
                stageStep(now, dt, clumsy)
                veinK = G.clamp((rate - 5.5) / 2.0, 0, 1); veinFrame = "P_base"; veinNow = now
            }
            nextPurr = now + 6300; nextBored = now + 20000
            return
        }
        if mode == "type" {
            if tired { mode = "tired"; modeStart = now; modeEnd = now + 2600; tired = false; fastSince = -1 } else { toIdle(now) }
        }
        if mode == "tired" {
            if now >= modeEnd { toIdle(now) }
            else { let t = now - modeStart; show("A14", 160); return }
        }
        if mode == "meow" {
            if now >= modeEnd { toIdle(now) }
            else {
                let u = (now - modeStart) / max(1, modeEnd - modeStart)
                let seq = ["E01", "E02", "E03", "E04", "E05", "E06", "E05", "E04", "E03", "E01"]
                show(seq[min(seq.count - 1, Int(u * Double(seq.count)))], 80)
                return
            }
        }
        if mode == "purr" {
            if now >= modeEnd || sinceAct < 1200 { stopPurr(); toIdle(now) }
            else {
                let t = now - modeStart
                let n = t < 350 ? "D00" : t < 700 ? "D01" : t < 1050 ? "D02" : pick(purrSeq, Int((t - 1050) / 420))
                show(n, 130)
                return
            }
        }
        if mode == "bored" {
            if now >= modeEnd || sinceAct < 1500 { toIdle(now); nextBored = now + 9000 + G.rand() * 6000 }
            else { bored(now); return }
        }
        // reposo: gruñona; los ojos siguen al cursor con continuidad
        offY = 0
        let alert = now - lastMove < 3000, relaxed = sinceAct > 6000
        var tgx = G.clamp((curX - 150) / 110.0, -1, 1), tgy = G.clamp((curY - 66) / 80.0, -1, 1)
        var hT = 0.0, cT = alert ? 0.06 : 0.40, aT = alert ? 0.0 : 3.8, lo = 0.0, phase = -1
        if relaxed {
            // sin tocar nada, cada 5 s cambia de modo: 0 relajada (ojos entornados + run run), 1 grumpy, 2 extremadamente atenta
            if !wasRelaxed { relaxOff = Double.random(in: 0..<17500) }      // el ciclo ya no empieza siempre por "relajada": cada vez arranca por un modo al azar
            wasRelaxed = true
            let cyc = (relaxOff + (now - (lastAct + 6000))).truncatingRemainder(dividingBy: 17500)   // relajada 7,5 s, grumpy 2,5 s (3 veces menos), atenta 7,5 s
            phase = cyc < 7500 ? 0 : cyc < 10000 ? 1 : 2
            hT = phase == 2 ? 1 : 0; mouthT = phase == 0 ? 1 : 0     // boca neutra solo en el modo relajado
            if phase == 0 && now >= nextPurr { _ = audio?.playPurr(now); purrOn = true; nextPurr = now + 15000 }
            cT = phase == 0 ? 0.55 : phase == 2 ? -0.06 : 0.45; aT = phase == 0 ? 1.0 : phase == 2 ? -1.4 : 4.5; lo = phase == 0 ? 0.0 : 0
        } else { if purrOn { stopPurr(); purrOn = false }; wasRelaxed = false }
        if !relaxed { mouthT = 0 }
        mouthK += (mouthT - mouthK) * min(1, dt * 4)
        if phase == 0 { tgx = tgx * 0.5; tgy = 0.15 + 0.2 * tgy }
        onMouse = armK > 0.05
        var baseName = onMouse ? "P_base" : "B01n"
        if !onMouse && phase == 1 { let gdx = curX - 150; baseName = gdx < -70 ? "B09" : gdx > 70 ? "B11" : "B13" }   // grumpy: la cara gruñona de la hoja, mirando al cursor
        show(baseName, 160)
        eyesStep(now, dt, tgx, tgy, cT, aT, lo, (alert ? 0.3 : 1.0) * (1 - 0.85 * hT))
        attK += (hT - attK) * min(1, dt * 4); pupilS += ((1 + 0.7 * attK) - pupilS) * min(1, dt * 4)
        earPerk = 5.0 * attK - 4.0 * mouthK; stSx = 1 + 0.05 * attK; stSy = 1 + 0.05 * attK; stTy = -2.5 * attK + 1.5 * mouthK + 0.5 * sin(now / 1000.0 * 55) * mouthK; stTx = 0.35 * sin(now / 1000.0 * 47 + 1) * mouthK; stRot += egx * 2.2 * attK
        if attK > 0.5 && earNext - now > 1800 { earNext = now + 700 + G.rand() * 1100 }
    }

    private func eyesStep(_ now: Double, _ dt: Double, _ tgx: Double, _ tgy: Double, _ cT0: Double, _ aT: Double, _ lo: Double, _ sm: Double) {
        relaxedNow = lo > 0; loNow += (lo - loNow) * min(1, dt * 6)
        if now > saccNext { saccX = (G.rand() - 0.5) * 0.3; saccY = (G.rand() - 0.5) * 0.25; saccNext = now + 700 + G.rand() * 1600 }
        let k1 = min(1, dt * (16 + 26 * attK))
        egx += (G.clamp(tgx + saccX * sm, -1, 1) - egx) * k1; egy += (G.clamp(tgy + saccY * sm, -1, 1) - egy) * k1
        var cT = cT0
        if now >= blinkAt { blinkUntil = now + 110; blinkAt = now + 8000 + G.rand() * 9000; if G.rand() < 0.07 { blinkAt = now + 280 } }
        if now < blinkUntil { cT = 1.08 }
        let kl = min(1, dt * (now < blinkUntil ? 40 : 12))
        lidC += (cT - lidC) * kl; lidA += (aT - lidA) * min(1, dt * 8)
    }

    private func bored(_ now: Double) {
        let t = now - modeStart; var n: String
        switch boredKind {
        case "yawn": let s = ["C01", "C03", "C04", "C05", "C04", "C06", "C07", "C00"]; n = s[min(s.count - 1, Int(t / 680))]
        case "up": let s = ["C08", "C09", "C08", "C10", "C10", "C07"]; n = s[min(s.count - 1, Int(t / 800))]
        case "rest": let s = ["C00", "C02", "C02", "C07", "C02"]; n = s[min(s.count - 1, Int(t / 1000))]
        default: let s = ["C07", "C14", "C15", "C14", "C15", "C14", "C15", "C14", "C15"]; n = s[min(s.count - 1, Int(t / 1000))]
        }
        show(n, 160)
    }
}
