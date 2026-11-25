using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace NenaPet
{
    // Reproduce un WAV (PCM 16 bit) con la API de audio de Windows (waveOut): arranca en milisegundos,
    // a diferencia de MediaPlayer, que tardaba unos 0,5 s en empezar a sonar.
    class WavVoice : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX { public ushort wFormatTag, nChannels; public uint nSamplesPerSec, nAvgBytesPerSec; public ushort nBlockAlign, wBitsPerSample, cbSize; }
        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR { public IntPtr lpData; public uint dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public uint dwFlags, dwLoops; public IntPtr lpNext, reserved; }
        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, int dev, ref WAVEFORMATEX f, IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, ref WAVEHDR hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr h, ref WAVEHDR hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, ref WAVEHDR hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutSetVolume(IntPtr h, uint vol);

        IntPtr h, buf; WAVEHDR hdr; int hdrSize = Marshal.SizeOf(typeof(WAVEHDR));
        public double Seconds; bool ok;

        // Lee un WAV y lo remuestrea por 'speed' (cambia tono y duración, como acelerar una cinta)
        public static WavVoice Load(string path, double speed)
        {
            try
            {
                byte[] b = File.ReadAllBytes(path);
                if (b.Length < 44 || System.Text.Encoding.ASCII.GetString(b, 0, 4) != "RIFF") return null;
                int pos = 12, ch = 1, rate = 48000, bits = 16, fmt = 1; byte[] pcm = null;
                while (pos + 8 <= b.Length)
                {
                    string id = System.Text.Encoding.ASCII.GetString(b, pos, 4); int size = BitConverter.ToInt32(b, pos + 4);
                    if (id == "fmt ") { fmt = BitConverter.ToUInt16(b, pos + 8); ch = BitConverter.ToUInt16(b, pos + 10); rate = BitConverter.ToInt32(b, pos + 12); bits = BitConverter.ToUInt16(b, pos + 22); }
                    else if (id == "data") { int n = Math.Min(size, b.Length - pos - 8); pcm = new byte[n]; Buffer.BlockCopy(b, pos + 8, pcm, 0, n); break; }
                    pos += 8 + size + (size & 1);
                }
                if (pcm == null || fmt != 1 || bits != 16 || ch < 1 || ch > 2) return null;
                if (Math.Abs(speed - 1) > 0.001)
                {
                    int frames = pcm.Length / (2 * ch), outFrames = (int)(frames / speed);
                    byte[] o = new byte[outFrames * 2 * ch];
                    for (int i = 0; i < outFrames; i++)
                    {
                        double src = i * speed; int i0 = (int)src; double t = src - i0; int i1 = Math.Min(frames - 1, i0 + 1); if (i0 >= frames) break;
                        for (int c = 0; c < ch; c++)
                        {
                            short a = BitConverter.ToInt16(pcm, (i0 * ch + c) * 2), d = BitConverter.ToInt16(pcm, (i1 * ch + c) * 2);
                            short v = (short)(a + (d - a) * t); o[(i * ch + c) * 2] = (byte)(v & 0xFF); o[(i * ch + c) * 2 + 1] = (byte)((v >> 8) & 0xFF);
                        }
                    }
                    pcm = o;
                }
                WavVoice w = new WavVoice();
                WAVEFORMATEX f = new WAVEFORMATEX();
                f.wFormatTag = 1; f.nChannels = (ushort)ch; f.nSamplesPerSec = (uint)rate; f.wBitsPerSample = 16;
                f.nBlockAlign = (ushort)(ch * 2); f.nAvgBytesPerSec = (uint)(rate * ch * 2);
                if (waveOutOpen(out w.h, -1, ref f, IntPtr.Zero, IntPtr.Zero, 0) != 0) return null;
                w.buf = Marshal.AllocHGlobal(pcm.Length); Marshal.Copy(pcm, 0, w.buf, pcm.Length);
                w.hdr = new WAVEHDR(); w.hdr.lpData = w.buf; w.hdr.dwBufferLength = (uint)pcm.Length;
                waveOutPrepareHeader(w.h, ref w.hdr, w.hdrSize);
                w.Seconds = (double)pcm.Length / (rate * ch * 2); w.ok = true;
                return w;
            }
            catch (Exception) { return null; }
        }

        public void Play(double volume)
        {
            if (!ok) return;
            waveOutReset(h);
            SetVolume(volume);
            waveOutWrite(h, ref hdr, hdrSize);
        }
        public void SetVolume(double v)
        {
            if (!ok) return;
            uint x = (uint)(Math.Max(0, Math.Min(1, v)) * 0xFFFF); waveOutSetVolume(h, x | (x << 16));
        }
        public void Stop() { if (ok) waveOutReset(h); }
        public void Dispose() { if (!ok) return; ok = false; waveOutReset(h); waveOutUnprepareHeader(h, ref hdr, hdrSize); waveOutClose(h); Marshal.FreeHGlobal(buf); }
    }

    // Maullidos y ronroneo. Usa los de serie (carpeta sounds) y, si existen, los que ponga el usuario
    // (ficheros WAV cuyo nombre empiece por miau/meow o purr/run).
    public class AudioPlayer
    {
        public bool Enabled = true;
        List<WavVoice[]> meows = new List<WavVoice[]>();       // cada maullido con 3 velocidades (tono distinto)
        List<WavVoice> purrs = new List<WavVoice>();
        WavVoice purrVoice;
        bool purring; double purrStart, purrDur; const double PurrVol = 0.6;

        public AudioPlayer(string bundledDir, string userDir)
        {
            List<string> um = new List<string>(), up = new List<string>(), bm = new List<string>(), bp = new List<string>();
            Scan(userDir, um, up); Scan(bundledDir, bm, bp);
            if (um.Count == 0) um = bm;         // los sonidos propios sustituyen a los de serie
            if (up.Count == 0) up = bp;
            foreach (string f in um)
            {
                WavVoice[] v = { WavVoice.Load(f, 0.94), WavVoice.Load(f, 1.0), WavVoice.Load(f, 1.12) };
                if (v[1] != null) { for (int i = 0; i < 3; i++) if (v[i] == null) v[i] = v[1]; meows.Add(v); }
            }
            foreach (string f in up) { WavVoice w = WavVoice.Load(f, 1.0); if (w != null) purrs.Add(w); }
        }

        static void Scan(string dir, List<string> m, List<string> p)
        {
            if (!Directory.Exists(dir)) return;
            foreach (string f in Directory.GetFiles(dir))
            {
                if (Path.GetExtension(f).ToLowerInvariant() != ".wav") continue;
                string n = Path.GetFileName(f).ToLowerInvariant();
                if (n.StartsWith("miau") || n.StartsWith("meow")) m.Add(f);
                else if (n.StartsWith("purr") || n.StartsWith("run")) p.Add(f);
            }
        }

        // Maullido real con tono/velocidad algo distintos cada vez. Devuelve la duración en segundos.
        public double PlayMeow()
        {
            if (meows.Count == 0) return 0.8;
            WavVoice[] set = meows[G.Rng.Next(meows.Count)];
            WavVoice v = set[G.Rng.Next(set.Length)];
            if (Enabled) v.Play(0.9);
            return Math.Min(2.5, v.Seconds);
        }

        // Ronroneo (run run) con entrada y salida suaves. Devuelve la duración en segundos.
        public double PlayPurr(double nowMs)
        {
            if (purrs.Count == 0) return 5.6;
            WavVoice v = purrs[G.Rng.Next(purrs.Count)];
            double dur = Math.Min(v.Seconds, 7);
            if (Enabled)
            {
                StopPurr();
                purrVoice = v; v.Play(0.0);
                purring = true; purrStart = nowMs; purrDur = dur * 1000;
            }
            return dur;
        }

        public void StopPurr() { if (purring) { purring = false; if (purrVoice != null) purrVoice.Stop(); } }

        // Hay que llamarlo cada fotograma para el fundido del ronroneo
        public void Update(double nowMs)
        {
            if (!purring) return;
            double t = nowMs - purrStart;
            if (t >= purrDur) { StopPurr(); return; }
            double v = Math.Min(1, Math.Min(t / 900.0, (purrDur - t) / 1400.0));
            purrVoice.SetVolume(PurrVol * G.Clamp(v, 0, 1));
        }

        public void SetEnabled(bool on) { Enabled = on; if (!on) StopPurr(); }
    }
}
