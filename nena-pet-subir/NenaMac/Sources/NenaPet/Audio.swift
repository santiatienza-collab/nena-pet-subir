import AVFoundation

// Maullidos y ronroneos reales. Los reproductores se precargan para que el sonido salga justo con la animación.
// Los sonidos propios (carpeta ~/Library/Application Support/NenaPet/sonidos) sustituyen a los de serie:
// ficheros que empiecen por "miau"/"meow" (maullidos) o "purr"/"run" (ronroneos).
final class AudioPlayer {
    var enabled = true
    private var meows: [URL] = [], purrs: [URL] = []
    private var players: [URL: AVAudioPlayer] = [:]
    private var purrPlayer: AVAudioPlayer?
    private var purring = false
    private var purrStart = 0.0, purrDur = 0.0
    private let purrVol: Float = 0.6

    init(bundled: URL?, user: URL) {
        var um: [URL] = [], up: [URL] = []
        AudioPlayer.scan(user, &um, &up)
        if let b = bundled { AudioPlayer.scan(b, &meows, &purrs) }
        if !um.isEmpty { meows = um }
        if !up.isEmpty { purrs = up }
        for u in meows + purrs { _ = pre(u) }
    }

    private func pre(_ u: URL) -> AVAudioPlayer? {
        if let p = players[u] { return p }
        guard let p = try? AVAudioPlayer(contentsOf: u) else { return nil }
        p.enableRate = true; p.volume = 0; p.prepareToPlay()
        players[u] = p
        return p
    }

    private static func scan(_ dir: URL, _ m: inout [URL], _ p: inout [URL]) {
        guard let files = try? FileManager.default.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil) else { return }
        for f in files {
            let ext = f.pathExtension.lowercased()
            if !["wav", "mp3", "m4a", "aif", "aiff", "caf"].contains(ext) { continue }
            let n = f.lastPathComponent.lowercased()
            if n.hasPrefix("miau") || n.hasPrefix("meow") { m.append(f) }
            else if n.hasPrefix("purr") || n.hasPrefix("run") { p.append(f) }
        }
    }

    // Maullido con velocidad algo distinta cada vez. Devuelve la duración en segundos.
    func playMeow() -> Double {
        guard let u = meows.randomElement(), let p = pre(u) else { return 0.8 }
        let speed = Float(G.rnd(0.92, 1.2))
        var dur = p.duration; if dur <= 0 { dur = 1.2 }
        dur = min(2.5, dur / Double(speed))
        if enabled { p.stop(); p.rate = speed; p.volume = 0.9; p.currentTime = 0; p.play() }
        return dur
    }

    // Ronroneo con entrada y salida suaves. Devuelve la duración en segundos.
    func playPurr(_ nowMs: Double) -> Double {
        guard let u = purrs.randomElement(), let p = pre(u) else { return 5.6 }
        var dur = p.duration; if dur <= 0 { dur = 5.6 }
        dur = min(dur, 7)
        if enabled {
            p.stop(); p.volume = 0; p.rate = 1; p.currentTime = 0; p.play()
            purrPlayer = p; purring = true; purrStart = nowMs; purrDur = dur * 1000
        }
        return dur
    }

    func stopPurr() {
        if purring { purring = false; purrPlayer?.stop(); purrPlayer?.volume = 0 }
    }

    func update(_ nowMs: Double) {
        guard purring, let p = purrPlayer else { return }
        let t = nowMs - purrStart
        if t >= purrDur { stopPurr(); return }
        let v = min(1, min(t / 900.0, (purrDur - t) / 1400.0))
        p.volume = purrVol * Float(G.clamp(v, 0, 1))
    }

    func setEnabled(_ on: Bool) { enabled = on; if !on { stopPurr() } }
}
