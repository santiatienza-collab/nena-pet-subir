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

    private var lastKey = -1e9, lastAct = -1e9, lastMove = -1e9
    private var keyIdx = 0
    private var keyTimes: [Double] = []
    private var curX = 150.0, curY = 90.0, prevX = -1.0, prevY = -1.0
    private var mode = "idle", modeStart = 0.0, modeEnd = 0.0
    private var nextPurr = 15000.0, nextBored = 20000.0, fastSince = -1.0, tired = false
    private var boredKind = "yawn", lastKind = ""
    private var gaze = "C"
    private var blinkAt = 9000.0
    // ojos en vivo sobre el fotograma sin pupilas (B01n)
    private var egx = 0.0, egy = 0.0, lidC = 0.38, lidA = 3.5, blinkUntil = -1.0, saccX = 0.0, saccY = 0.0, saccNext = 0.0, lastTick = 0.0
    private var kickAt = -1e9, offY = 0.0, scY = 1.0, relaxedNow = false, frameChangeAt = -1e9
    private let EX = [136.5, 184.0], EY = 72.0, ER = 10.2
    private let purrSeq = ["D03", "D07", "D11", "D07", "D03", "D10", "D06", "D10"]
    private let T0 = ["A00", "A01", "A02", "A03", "A01", "A02"]
    private let T1 = ["A04", "A05", "A04", "F05"]
    private let T2 = ["A09", "A06", "F09", "A08", "F06", "F13"]
    private let T3 = ["A10", "A11", "F10", "F11"]

    init() {
        if let u = Bundle.main.url(forResource: "B01n", withExtension: "png", subdirectory: "frames"), let img = NSImage(contentsOf: u), let cg = img.cgImage(forProposedRect: nil, context: nil, hints: nil) { frames["B01n"] = cg }
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
        curX = x * k; curY = y * k
        if prevX >= 0 && abs(x - prevX) + abs(y - prevY) > 2 { lastMove = now; lastAct = now }
        prevX = x; prevY = y
    }
    func keyPress(_ now: Double) {
        if mode != "type" { stopPurr() }
        lastKey = now; lastAct = now; keyIdx += 1; kickAt = now
        keyTimes.append(now); keyTimes.removeAll { now - $0 > 1000 }
        mode = "type"
    }
    func click(_ now: Double) {
        stopPurr()
        let d = audio?.playMeow() ?? 0.8
        lastAct = now; mode = "meow"; modeStart = now; modeEnd = now + max(700, d * 1000)
    }
    private func stopPurr() { audio?.stopPurr() }
    private func toIdle(_ now: Double) { mode = "idle"; nextPurr = now + 15000; nextBored = now + 20000 }
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
        func put(_ name: String, _ alpha: Double) {
            guard let img = frames[name], alpha > 0.003 else { return }
            c.saveGState()
            c.setAlpha(CGFloat(alpha))
            // la vista está invertida (y hacia abajo): se voltea la imagen para dibujarla derecha
            c.translateBy(x: rect.minX, y: rect.maxY); c.scaleBy(x: 1, y: -1)
            c.draw(img, in: CGRect(x: 0, y: 0, width: rect.width, height: rect.height))
            c.restoreGState()
        }
        c.interpolationQuality = .high
        let f = max(0, 1 - (nowT - fadeStart) / fadeLen)
        if let p = prev, f > 0 { put(p, 1); put(cur, 1 - f) } else { put(cur, 1) }
        if cur == "B01n" {
            let ea = min(1, (nowT - frameChangeAt) / 110.0)
            if ea > 0.003 { c.saveGState(); c.setAlpha(CGFloat(ea)); c.translateBy(x: rect.minX, y: rect.minY); c.scaleBy(x: k, y: k); drawEyes(c); c.restoreGState() }
        }
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
            c.setFillColor(darkC); c.fillEllipse(in: CGRect(x: px - 3.3, y: py - 6.5, width: 6.6, height: 13))
            c.setFillColor(CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 0.95)); c.fillEllipse(in: CGRect(x: px - 3.2, y: py - 5.6, width: 3.4, height: 3.8))
            let top = cy - ER, h = ER * 2
            let yl = cy + ER - (relaxedNow ? 0.14 : 0.0) * h
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

        if typing {
            mode = "type"
            if rate >= 7 { if fastSince < 0 { fastSince = now }; if now - fastSince > 2500 { tired = true } }
            else if rate < 3 { fastSince = -1 }
            let seq = rate >= 10 ? T3 : rate >= 7 ? T2 : rate >= 4 ? T1 : T0
            var n = pick(seq, keyIdx)
            if n != cur && now - frameChange < 70 { n = cur }
            let kk = max(0, 1 - (now - kickAt) / 130.0)
            offY = kk * 3.0
            show(n, 80)
            nextPurr = now + 15000; nextBored = now + 20000
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
        if sinceAct > 8000 {
            if now >= nextPurr {
                let d = audio?.playPurr(now) ?? 5.6
                mode = "purr"; modeStart = now; modeEnd = now + max(3500, min(7000, d * 1000)); return
            }
            if sinceAct > 20000 && now >= nextBored {
                let kinds = ["yawn", "up", "rest", "sleep"]
                boredKind = kinds.randomElement()!; if boredKind == lastKind { boredKind = kinds.randomElement()! }; lastKind = boredKind
                mode = "bored"; modeStart = now
                modeEnd = now + (boredKind == "yawn" ? 5400 : boredKind == "up" ? 4800 : boredKind == "rest" ? 5000 : 9000)
                return
            }
        }
        // reposo: gruñona; los ojos siguen al cursor con continuidad
        offY = sin(now / 1300) * 0.6
        let dt = min(0.05, max(0.001, (now - lastTick) / 1000.0)); lastTick = now
        let alert = now - lastMove < 3000, relaxed = sinceAct > 8000
        relaxedNow = relaxed
        let tgx = G.clamp((curX - 150) / 110.0, -1, 1), tgy = G.clamp((curY - 66) / 80.0, -1, 1)
        if now > saccNext { saccX = (G.rand() - 0.5) * 0.3; saccY = (G.rand() - 0.5) * 0.25; saccNext = now + 700 + G.rand() * 1600 }
        let sm = alert ? 0.3 : 1.0
        let k1 = min(1, dt * 16)
        egx += (G.clamp(tgx + saccX * sm, -1, 1) - egx) * k1; egy += (G.clamp(tgy + saccY * sm, -1, 1) - egy) * k1
        var cT = relaxed ? 0.62 : alert ? 0.06 : 0.40; let aT = relaxed ? 1.5 : alert ? 0.0 : 3.8
        if now >= blinkAt { blinkUntil = now + 110; blinkAt = now + 8000 + G.rand() * 9000; if G.rand() < 0.07 { blinkAt = now + 280 } }
        if now < blinkUntil { cT = 1.08 }
        let kl = min(1, dt * (now < blinkUntil ? 40 : 12))
        lidC += (cT - lidC) * kl; lidA += (aT - lidA) * min(1, dt * 8)
        show("B01n", 130)
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
