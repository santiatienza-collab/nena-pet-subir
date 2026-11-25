using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NenaSetup
{
    // Instalador de Nena Pet: lleva el programa dentro (payload.zip), instala por usuario (sin administrador),
    // crea accesos directos, se registra en "Aplicaciones instaladas" y se desinstala con /uninstall.
    public static class SetupProgram
    {
        const string AppName = "Nena Pet", Version = "2.0.0", UninstKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\NenaPet";
        static string InstallDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\NenaPet"); } }
        static string StartMenuLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"); } }
        static string DesktopLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"); } }

        [STAThread]
        public static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            bool silent = false, uninstall = false;
            foreach (string a in args) { if (a == "/uninstall") uninstall = true; if (a == "/S") silent = true; }
            if (uninstall)
            {
                if (silent || MessageBox.Show("¿Quieres desinstalar Nena Pet?", AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) Uninstall(silent);
                return 0;
            }
            if (silent) { Install(true, false, true); return 0; }
            Application.Run(new SetupForm());
            return 0;
        }

        static void KillApp()
        {
            foreach (Process p in Process.GetProcessesByName("NenaPet"))
            {
                try { p.Kill(); p.WaitForExit(3000); } catch (Exception) { }
            }
        }

        public static void Install(bool desktop, bool autostart, bool launch)
        {
            KillApp();
            Directory.CreateDirectory(InstallDir);
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream("payload.zip"))
            using (ZipArchive zip = new ZipArchive(s, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    if (string.IsNullOrEmpty(e.Name)) continue;
                    string dest = Path.Combine(InstallDir, e.FullName.Replace('/', '\\'));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    e.ExtractToFile(dest, true);
                }
            }
            string exe = Path.Combine(InstallDir, "NenaPet.exe"), unins = Path.Combine(InstallDir, "Desinstalar.exe");
            File.Copy(asm.Location, unins, true);

            MakeShortcut(StartMenuLnk, exe);
            if (desktop) MakeShortcut(DesktopLnk, exe); else TryDelete(DesktopLnk);

            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstKey))
            {
                k.SetValue("DisplayName", AppName); k.SetValue("DisplayVersion", Version); k.SetValue("Publisher", "Nena Pet");
                k.SetValue("DisplayIcon", exe); k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", "\"" + unins + "\" /uninstall"); k.SetValue("NoModify", 1, RegistryValueKind.DWord); k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            using (RegistryKey run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (autostart) run.SetValue("NenaPet", "\"" + exe + "\""); else run.DeleteValue("NenaPet", false);
            }
            if (launch) Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = InstallDir });
        }

        static void Uninstall(bool silent)
        {
            KillApp();
            TryDelete(StartMenuLnk); TryDelete(DesktopLnk);
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstKey, false); } catch (Exception) { }
            try { using (RegistryKey run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) { if (run != null) run.DeleteValue("NenaPet", false); } } catch (Exception) { }
            // el desinstalador no puede borrarse a sí mismo: lo hace un cmd oculto unos segundos después
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + InstallDir + "\"");
                psi.CreateNoWindow = true; psi.WindowStyle = ProcessWindowStyle.Hidden; psi.UseShellExecute = false;
                Process.Start(psi);
            }
            catch (Exception) { }
            if (!silent) MessageBox.Show("Nena Pet se ha desinstalado. ¡Hasta pronto!", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch (Exception) { } }

        static void MakeShortcut(string path, string target)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object sh = Activator.CreateInstance(t);
                object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { path });
                Type lt = lnk.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(target) });
                lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { target + ",0" });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Nena, la gatita de escritorio" });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch (Exception) { }
        }
    }

    public class SetupForm : Form
    {
        int imgH; CheckBox cDesk, cAuto; Button bOk, bCancel; Label info;

        public SetupForm()
        {
            Text = "Instalar Nena Pet"; StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ClientSize = new Size(430, 400); Font = new Font("Segoe UI", 9.5f);
            try { Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch (Exception) { }
            try
            {
                using (System.IO.Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream("splash.png"))
                {
                    PictureBox pb = new PictureBox(); pb.Image = Image.FromStream(st); pb.SizeMode = PictureBoxSizeMode.Zoom; pb.Left = 65; pb.Top = 10; pb.Width = 300; pb.Height = Math.Max(60, (int)(300.0 * pb.Image.Height / pb.Image.Width)); pb.BackColor = Color.Transparent; Controls.Add(pb);
                    imgH = pb.Height;
                }
            }
            catch (Exception) { imgH = 0; }
            Label title = new Label(); title.Text = "Nena Pet"; title.Font = new Font("Segoe UI", 18f, FontStyle.Bold); title.Left = 130; title.Top = 18 + imgH; title.AutoSize = true; Controls.Add(title);
            info = new Label(); info.Left = 22; info.Top = 66 + imgH; info.Width = 390; info.Height = 70;
            info.Text = "Una gatita de escritorio que patea tu teclado, te sigue con la mirada, ronronea cuando estás quieta y se enfada si tecleas a toda velocidad.\r\n\r\nSe instala solo para ti, sin permisos de administrador.";
            Controls.Add(info);
            cDesk = new CheckBox(); cDesk.Text = "Crear un acceso directo en el escritorio"; cDesk.Checked = true; cDesk.Left = 24; cDesk.Top = 150 + imgH; cDesk.Width = 380; Controls.Add(cDesk);
            cAuto = new CheckBox(); cAuto.Text = "Iniciar Nena con Windows"; cAuto.Checked = false; cAuto.Left = 24; cAuto.Top = 176 + imgH; cAuto.Width = 380; Controls.Add(cAuto);
            bOk = new Button(); bOk.Text = "Instalar"; bOk.Left = 232; bOk.Top = 212 + imgH; bOk.Width = 90; bOk.Height = 30; bOk.Click += OnInstall; Controls.Add(bOk);
            bCancel = new Button(); bCancel.Text = "Cancelar"; bCancel.Left = 328; bCancel.Top = 212 + imgH; bCancel.Width = 90; bCancel.Height = 30; bCancel.Click += delegate { Close(); }; Controls.Add(bCancel);
            AcceptButton = bOk; ClientSize = new Size(430, 256 + imgH);
        }

        void OnInstall(object s, EventArgs e)
        {
            bOk.Enabled = false; bCancel.Enabled = false; info.Text = "Instalando…";
            Application.DoEvents();
            try
            {
                SetupProgram.Install(cDesk.Checked, cAuto.Checked, true);
                MessageBox.Show("¡Nena ya está instalada! Aparecerá en la esquina inferior derecha de tu pantalla.\r\n\r\nClic derecho sobre ella para ver las opciones.", "Nena Pet", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show("No se pudo instalar:\r\n" + ex.Message, "Nena Pet", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            Close();
        }
    }
}
