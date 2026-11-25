using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NenaPet
{
    // Ajustes guardados en %APPDATA%\NenaPet\settings.ini
    public class Settings
    {
        public double Scale = 1.0;
        public double X = double.NaN, Y = double.NaN;
        public bool Sound = true;
        static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }
        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NenaPet"); } }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (File.Exists(FilePath))
                    foreach (string line in File.ReadAllLines(FilePath))
                    {
                        int i = line.IndexOf('='); if (i < 0) continue;
                        string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                        double d;
                        if (k == "scale" && double.TryParse(v, System.Globalization.NumberStyles.Float, G.Inv, out d)) s.Scale = d;
                        else if (k == "x" && double.TryParse(v, System.Globalization.NumberStyles.Float, G.Inv, out d)) s.X = d;
                        else if (k == "y" && double.TryParse(v, System.Globalization.NumberStyles.Float, G.Inv, out d)) s.Y = d;
                        else if (k == "sound") s.Sound = v != "0";
                    }
            }
            catch (Exception) { }
            return s;
        }
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                List<string> l = new List<string>();
                l.Add("scale=" + Scale.ToString(G.Inv)); l.Add("sound=" + (Sound ? "1" : "0"));
                if (!double.IsNaN(X)) { l.Add("x=" + X.ToString(G.Inv)); l.Add("y=" + Y.ToString(G.Inv)); }
                File.WriteAllLines(FilePath, l.ToArray());
            }
            catch (Exception) { }
        }
    }

    public class PetWindow : Window
    {
        const double BASE_W = 240, BASE_H = 135;
        CatView cat; Settings st; InputPoller input; AudioPlayer audio;
        Stopwatch clock = Stopwatch.StartNew();
        System.Windows.Forms.NotifyIcon tray;
        // paseo
        int walkDir; double walkSpeed, walkX; double lastFrameMs;
        // arrastrar
        bool pressed, dragging; Point pressScreen; double pressLeft, pressTop;
        Point lastCursorRel = new Point(-1, -1);

        public PetWindow(Settings s)
        {
            st = s;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize; ShowActivated = false;
            UseLayoutRounding = false; SnapsToDevicePixels = false;
            Title = "Nena Pet";
            ApplySize();

            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            audio = new AudioPlayer(Path.Combine(exeDir, "sounds"), Path.Combine(Settings.Dir, "sonidos"));
            audio.SetEnabled(st.Sound);
            cat = new CatView(); cat.Audio = audio;
            Viewbox vb = new Viewbox(); vb.Stretch = Stretch.Uniform; vb.Child = cat;
            Content = vb;
            cat.WalkCommand = delegate(int dir, double speed) { walkDir = dir; walkSpeed = speed; walkX = Left; };

            // posición inicial: esquina inferior derecha del área de trabajo
            Rect wa = SystemParameters.WorkArea;
            if (!double.IsNaN(st.X) && st.X > wa.Left - Width / 2 && st.X < wa.Right - Width / 2 && st.Y > wa.Top && st.Y < wa.Bottom - 20) { Left = st.X; Top = st.Y; }
            else { Left = wa.Right - Width - 8; Top = wa.Bottom - Height + 2; }

            SourceInitialized += delegate { Native.MakeNoActivate(new WindowInteropHelper(this).Handle); };
            Loaded += delegate { StartInput(); BuildTray(); CompositionTarget.Rendering += OnFrame; };
            Closed += delegate { if (tray != null) { tray.Visible = false; tray.Dispose(); } };

            MouseLeftButtonDown += OnDown; MouseMove += OnMove; MouseLeftButtonUp += OnUp;
            MouseRightButtonUp += delegate { ShowMenu(); };
        }

        void ApplySize()
        {
            double right = Left + Width, bottom = Top + Height;
            Width = BASE_W * st.Scale; Height = BASE_H * st.Scale;
            if (cat != null) { cat.ScaleToDip = Width / 300.0; }
            if (IsLoaded) { Left = right - Width; Top = bottom - Height; }
        }

        double NowMs() { return clock.Elapsed.TotalMilliseconds; }

        void StartInput()
        {
            input = new InputPoller();
            input.KeyPressed += delegate(uint vk) { cat.KeyPress(NowMs()); };
            input.Activity += delegate { cat.Activity(NowMs()); };
        }

        // ---------------------------------------------------------------- bucle de fotogramas
        void OnFrame(object sender, EventArgs e)
        {
            double now = NowMs(), dt = Math.Min(0.1, (now - lastFrameMs) / 1000.0); lastFrameMs = now;
            if (input != null) input.Poll();

            // cursor (en DIP relativos a la ventana)
            Native.POINT p; Native.GetCursorPos(out p);
            PresentationSource src = PresentationSource.FromVisual(this);
            if (src != null && src.CompositionTarget != null)
            {
                Point dip = src.CompositionTarget.TransformFromDevice.Transform(new Point(p.X, p.Y));
                Point rel = new Point(dip.X - Left, dip.Y - Top);
                if (rel != lastCursorRel) { lastCursorRel = rel; cat.SetCursor(rel.X, rel.Y, now); }
            }

            // paseo: la ventana avanza por el borde inferior y da la vuelta en los bordes
            if (walkDir != 0 && !dragging)
            {
                Rect wa = SystemParameters.WorkArea;
                walkX += walkDir * walkSpeed * dt;
                double min = wa.Left + 4, max = wa.Right - Width - 4;
                if (walkX < min || walkX > max) { walkX = Math.Max(min, Math.Min(max, walkX)); walkDir = -walkDir; cat.WalkDirFromHost(walkDir); }
                Left = walkX;
            }
            cat.Tick(now);
        }

        // ---------------------------------------------------------------- ratón: arrastrar, clic y menú
        void OnDown(object s, MouseButtonEventArgs e)
        {
            pressed = true; dragging = false;
            System.Drawing.Point sp = System.Windows.Forms.Cursor.Position; pressScreen = new Point(sp.X, sp.Y);
            pressLeft = Left; pressTop = Top;
            CaptureMouse();
        }
        void OnMove(object s, MouseEventArgs e)
        {
            if (!pressed) return;
            System.Drawing.Point sp = System.Windows.Forms.Cursor.Position;
            PresentationSource src = PresentationSource.FromVisual(this);
            double k = src != null ? src.CompositionTarget.TransformFromDevice.M11 : 1;
            double dx = (sp.X - pressScreen.X) * k, dy = (sp.Y - pressScreen.Y) * k;
            if (!dragging && Math.Sqrt(dx * dx + dy * dy) > 5) { dragging = true; walkDir = 0; }
            if (dragging) { Left = pressLeft + dx; Top = pressTop + dy; }
        }
        void OnUp(object s, MouseButtonEventArgs e)
        {
            if (!pressed) return;
            pressed = false; ReleaseMouseCapture();
            if (dragging) { st.X = Left; st.Y = Top; st.Save(); dragging = false; }
            else cat.Click(NowMs());
        }

        // ---------------------------------------------------------------- menú y bandeja
        void SetScale(double sc) { st.Scale = sc; st.Save(); ApplySize(); }
        void ToggleVisible() { if (IsVisible) Hide(); else Show(); }
        static bool Autostart
        {
            get { using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) { return k != null && k.GetValue("NenaPet") != null; } }
            set
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (k == null) return;
                    if (value) k.SetValue("NenaPet", "\"" + Assembly.GetExecutingAssembly().Location + "\""); else k.DeleteValue("NenaPet", false);
                }
            }
        }
        void OpenSoundsFolder()
        {
            string d = Path.Combine(Settings.Dir, "sonidos"); Directory.CreateDirectory(d); Process.Start("explorer.exe", "\"" + d + "\"");
        }
        void Quit() { Close(); Application.Current.Shutdown(); }

        void ShowMenu()
        {
            ContextMenu m = new ContextMenu();
            MenuItem title = new MenuItem(); title.Header = "Nena Pet 🐱"; title.IsEnabled = false; m.Items.Add(title);
            m.Items.Add(new Separator());
            MenuItem size = new MenuItem(); size.Header = "Tamaño";
            string[] names = { "Mini", "Normal", "Grande" }; double[] vals = { 0.8, 1.0, 1.5 };
            for (int i = 0; i < 3; i++)
            {
                MenuItem it = new MenuItem(); it.Header = names[i]; it.IsCheckable = true; it.IsChecked = Math.Abs(st.Scale - vals[i]) < 0.01;
                double v = vals[i]; it.Click += delegate { SetScale(v); }; size.Items.Add(it);
            }
            m.Items.Add(size);
            MenuItem hide = new MenuItem(); hide.Header = "Esconder a Nena"; hide.Click += delegate { Hide(); }; m.Items.Add(hide);
            MenuItem auto = new MenuItem(); auto.Header = "Iniciar con Windows"; auto.IsCheckable = true; auto.IsChecked = Autostart;
            auto.Click += delegate { Autostart = auto.IsChecked; }; m.Items.Add(auto);
            MenuItem snd = new MenuItem(); snd.Header = "Sonidos (miau / run run)"; snd.IsCheckable = true; snd.IsChecked = st.Sound;
            snd.Click += delegate { st.Sound = snd.IsChecked; st.Save(); audio.SetEnabled(st.Sound); }; m.Items.Add(snd);
            MenuItem folder = new MenuItem(); folder.Header = "Abrir carpeta de sonidos propios…"; folder.Click += delegate { OpenSoundsFolder(); }; m.Items.Add(folder);
            m.Items.Add(new Separator());
            MenuItem quit = new MenuItem(); quit.Header = "Salir"; quit.Click += delegate { Quit(); }; m.Items.Add(quit);
            m.IsOpen = true;
        }

        void BuildTray()
        {
            try
            {
                tray = new System.Windows.Forms.NotifyIcon();
                tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
                tray.Text = "Nena Pet";
                System.Windows.Forms.ContextMenuStrip cm = new System.Windows.Forms.ContextMenuStrip();
                cm.Items.Add("Mostrar / esconder a Nena", null, delegate { ToggleVisible(); });
                System.Windows.Forms.ToolStripMenuItem snd = new System.Windows.Forms.ToolStripMenuItem("Sonidos"); snd.CheckOnClick = true; snd.Checked = st.Sound;
                snd.Click += delegate { st.Sound = snd.Checked; st.Save(); audio.SetEnabled(st.Sound); }; cm.Items.Add(snd);
                cm.Items.Add("Abrir carpeta de sonidos propios…", null, delegate { OpenSoundsFolder(); });
                cm.Items.Add("-");
                cm.Items.Add("Salir", null, delegate { Quit(); });
                tray.ContextMenuStrip = cm;
                tray.MouseClick += delegate(object o, System.Windows.Forms.MouseEventArgs a) { if (a.Button == System.Windows.Forms.MouseButtons.Left) ToggleVisible(); };
                tray.Visible = true;
            }
            catch (Exception) { }
        }
    }

    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {

            bool created;
            Mutex mutex = new Mutex(true, "NenaPet.SingleInstance", out created);
            if (!created) return 0;                                   // ya hay una Nena viva

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Settings st = Settings.Load();
            PetWindow w = new PetWindow(st);
            w.Show();
            app.Run();
            GC.KeepAlive(mutex);
            return 0;
        }
    }

}
