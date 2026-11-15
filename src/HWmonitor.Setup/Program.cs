using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace HWmonitorSetup;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        bool uninstall = args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
        bool quiet = args.Any(a => a.Equals("/quiet", StringComparison.OrdinalIgnoreCase));
        string dir = ArgValue(args, "/dir");

        if (uninstall)
        {
            // The uninstaller lives inside the folder it deletes, so continue from a temp copy.
            string self = Application.ExecutablePath;
            string installDir = dir ?? Installer.InstalledDir() ?? Path.GetDirectoryName(self);
            if (dir == null && self.StartsWith(installDir, StringComparison.OrdinalIgnoreCase))
            {
                string tmp = Path.Combine(Path.GetTempPath(), "HWmonitor-uninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
                File.Copy(self, tmp, true);
                Process.Start(tmp, $"/uninstall /dir \"{installDir}\"" + (quiet ? " /quiet" : ""));
                return;
            }
            if (quiet) { Installer.Uninstall(installDir, false); Installer.ScheduleSelfDelete(); return; }
            Application.Run(new SetupForm(true, installDir));
            Installer.ScheduleSelfDelete();
            return;
        }

        if (quiet) { Installer.Install(Installer.DefaultDir, true, true, null); return; }
        Application.Run(new SetupForm(false, Installer.InstalledDir() ?? Installer.DefaultDir));
    }

    static string ArgValue(string[] a, string name)
    {
        int i = Array.FindIndex(a, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < a.Length ? a[i + 1].Trim('"') : null;
    }
}

static class Installer
{
    public const string AppName = "HWmonitor";
    public const string Version = "1.0.0";
    const long Magic = 0x4857_4D4F_4E49_5452; // "HWMONITR"
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\HWmonitor";

    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);
    static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName + ".lnk");
    static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), AppName + ".lnk");

    static RegistryKey Hklm => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

    public static string InstalledDir()
    {
        using var k = Hklm.OpenSubKey(UninstallKey);
        return k?.GetValue("InstallLocation") as string;
    }

    /// <summary>Reads the app payload appended to this executable: [stub][payload][len:8][magic:8].</summary>
    static (long stubLen, long payloadLen) ReadLayout(FileStream fs)
    {
        if (fs.Length < 16) return (fs.Length, 0);
        fs.Seek(-16, SeekOrigin.End);
        var br = new BinaryReader(fs);
        long len = br.ReadInt64(); long magic = br.ReadInt64();
        if (magic != Magic || len <= 0 || len > fs.Length - 16) return (fs.Length, 0);
        return (fs.Length - 16 - len, len);
    }

    static void CopyRange(Stream src, long offset, long count, string dest)
    {
        src.Seek(offset, SeekOrigin.Begin);
        using var o = File.Create(dest);
        var buf = new byte[1 << 20];
        while (count > 0)
        {
            int n = src.Read(buf, 0, (int)Math.Min(buf.Length, count));
            if (n <= 0) throw new EndOfStreamException();
            o.Write(buf, 0, n); count -= n;
        }
    }

    static void KillRunning()
    {
        foreach (var p in Process.GetProcessesByName(AppName))
            try { p.Kill(); p.WaitForExit(5000); } catch { }
    }

    public static void Install(string dir, bool desktop, bool startMenu, IProgress<string> log, bool pawnIo = false)
    {
        log?.Report("Closing running instances…");
        KillRunning();
        Directory.CreateDirectory(dir);
        string exe = Path.Combine(dir, AppName + ".exe");
        string uninst = Path.Combine(dir, "Uninstall.exe");

        using (var fs = File.OpenRead(Application.ExecutablePath))
        {
            var (stub, payload) = ReadLayout(fs);
            if (payload == 0) throw new InvalidOperationException("This setup file does not contain the HWmonitor application (payload missing).");
            log?.Report("Copying HWmonitor…");
            CopyRange(fs, stub, payload, exe);
            log?.Report("Creating uninstaller…");
            CopyRange(fs, 0, stub, uninst);
        }

        log?.Report("Creating shortcuts…");
        if (startMenu) Shortcut(StartMenuLink, exe, dir); else TryDelete(StartMenuLink);
        if (desktop) Shortcut(DesktopLink, exe, dir); else TryDelete(DesktopLink);

        log?.Report("Registering in Windows…");
        using (var k = Hklm.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", AppName);
            k.SetValue("DisplayVersion", Version);
            k.SetValue("Publisher", AppName);
            k.SetValue("DisplayIcon", exe + ",0");
            k.SetValue("InstallLocation", dir);
            k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            k.SetValue("UninstallString", $"\"{uninst}\" /uninstall");
            k.SetValue("QuietUninstallString", $"\"{uninst}\" /uninstall /quiet");
            k.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        if (pawnIo) InstallPawnIo(log);
        log?.Report("Done");
    }

    static bool PawnIoInstalled()
    {
        using var k = Hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
        return k != null;
    }

    /// <summary>PawnIO lets the sensor library read CPU temperature and power (official winget package).</summary>
    static void InstallPawnIo(IProgress<string> log)
    {
        if (PawnIoInstalled()) return;
        log?.Report("Installing PawnIO sensor driver (CPU temperature)…");
        try
        {
            var p = Process.Start(new ProcessStartInfo("winget", "install -e --id namazso.PawnIO --silent --accept-package-agreements --accept-source-agreements")
            { CreateNoWindow = true, UseShellExecute = false });
            p?.WaitForExit(180000);
        }
        catch { }
        if (!PawnIoInstalled()) log?.Report("PawnIO could not be installed automatically — get it from pawnio.eu");
    }

    public static void Uninstall(string dir, bool removeData, IProgress<string> log = null)
    {
        log?.Report("Closing HWmonitor…");
        KillRunning();
        log?.Report("Removing autostart task…");
        try
        {
            var p = Process.Start(new ProcessStartInfo("schtasks.exe", "/Delete /F /TN \"HWmonitor\"") { CreateNoWindow = true, UseShellExecute = false });
            p?.WaitForExit(5000);
        }
        catch { }
        log?.Report("Removing shortcuts…");
        TryDelete(StartMenuLink); TryDelete(DesktopLink);
        log?.Report("Removing files…");
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            for (int i = 0; i < 5; i++)
            {
                try { Directory.Delete(dir, true); break; } catch { System.Threading.Thread.Sleep(600); }
            }
        }
        if (removeData)
        {
            log?.Report("Removing settings and session history…");
            try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName), true); } catch { }
        }
        try { Hklm.DeleteSubKeyTree(UninstallKey, false); } catch { }
        log?.Report("Done");
    }

    public static void ScheduleSelfDelete()
    {
        string self = Application.ExecutablePath;
        if (!Path.GetFileName(self).StartsWith("HWmonitor-uninstall-", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 > nul & del /f /q \"{self}\"")
            { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        }
        catch { }
    }

    static void Shortcut(string link, string target, string workDir)
    {
        var t = Type.GetTypeFromProgID("WScript.Shell");
        dynamic shell = Activator.CreateInstance(t);
        dynamic lnk = shell.CreateShortcut(link);
        lnk.TargetPath = target;
        lnk.WorkingDirectory = workDir;
        lnk.IconLocation = target + ",0";
        lnk.Description = "HWmonitor – hardware monitor";
        lnk.Save();
    }

    static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }
}

/// <summary>Clean, minimal setup window.</summary>
sealed class SetupForm : Form
{
    readonly bool _uninstall;
    readonly TextBox _dir = new();
    readonly CheckBox _desktop = new() { Text = "Create desktop shortcut", Checked = true, AutoSize = true };
    readonly CheckBox _startMenu = new() { Text = "Add to Start menu", Checked = true, AutoSize = true };
    readonly CheckBox _pawnIo = new() { Text = "Install PawnIO sensor driver (needed for CPU temperature and power)", Checked = true, AutoSize = true };
    readonly CheckBox _launch = new() { Text = "Launch HWmonitor when finished", Checked = true, AutoSize = true };
    readonly CheckBox _removeData = new() { Text = "Also delete my settings, speed tests and gaming session history", AutoSize = true };
    readonly Label _status = new() { AutoSize = false, Height = 20 };
    readonly ProgressBar _bar = new() { Style = ProgressBarStyle.Marquee, Visible = false, Height = 4, MarqueeAnimationSpeed = 25 };
    readonly Button _primary = new(), _secondary = new(), _browse = new() { Text = "Browse…" };

    static readonly Color Accent = Color.FromArgb(10, 132, 255);
    static readonly Color Bg = Color.FromArgb(245, 245, 247);

    public SetupForm(bool uninstall, string dir)
    {
        _uninstall = uninstall;
        Text = uninstall ? "Uninstall HWmonitor" : "HWmonitor Setup";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 400);
        BackColor = Bg;
        using (var s = typeof(SetupForm).Assembly.GetManifestResourceStream("icon.ico")) if (s != null) Icon = new Icon(s);

        var logo = new PictureBox { Image = Icon?.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(56, 56), Location = new Point(32, 30) };
        try { using var s = typeof(SetupForm).Assembly.GetManifestResourceStream("icon.ico"); logo.Image = new Icon(s, 128, 128).ToBitmap(); } catch { }
        var title = new Label { Text = uninstall ? "Uninstall HWmonitor" : "Install HWmonitor", Font = new Font("Segoe UI Semibold", 17f), AutoSize = true, Location = new Point(100, 30) };
        var sub = new Label
        {
            Text = uninstall ? "This removes HWmonitor, its shortcuts and its autostart entry." : $"Version {Installer.Version} · Hardware, network and gaming performance monitor",
            ForeColor = Color.FromArgb(110, 110, 115), AutoSize = true, Location = new Point(102, 64),
        };
        Controls.AddRange(new Control[] { logo, title, sub });

        int y = 120;
        if (!uninstall)
        {
            Controls.Add(new Label { Text = "Install location", AutoSize = true, Location = new Point(32, y), ForeColor = Color.FromArgb(60, 60, 67) });
            _dir.Text = dir; _dir.SetBounds(32, y + 22, 400, 26);
            _browse.SetBounds(440, y + 21, 88, 28);
            _browse.Click += (_, _) =>
            {
                using var d = new FolderBrowserDialog { SelectedPath = _dir.Text };
                if (d.ShowDialog(this) == DialogResult.OK) _dir.Text = Path.Combine(d.SelectedPath, "HWmonitor");
            };
            Controls.AddRange(new Control[] { _dir, _browse });
            _desktop.Location = new Point(32, y + 68); _startMenu.Location = new Point(32, y + 94); _pawnIo.Location = new Point(32, y + 120); _launch.Location = new Point(32, y + 146);
            Controls.AddRange(new Control[] { _desktop, _startMenu, _pawnIo, _launch });
        }
        else
        {
            Controls.Add(new Label { Text = "Installed in", AutoSize = true, Location = new Point(32, y), ForeColor = Color.FromArgb(60, 60, 67) });
            Controls.Add(new Label { Text = dir, AutoSize = true, Location = new Point(32, y + 22), Font = new Font("Segoe UI Semibold", 9.5f) });
            _removeData.Location = new Point(32, y + 64);
            Controls.Add(_removeData);
        }

        _bar.SetBounds(32, 306, 496, 4);
        _status.SetBounds(32, 316, 496, 20); _status.ForeColor = Color.FromArgb(110, 110, 115);
        Controls.AddRange(new Control[] { _bar, _status });

        Style(_primary, true); Style(_secondary, false);
        _primary.Text = uninstall ? "Uninstall" : "Install";
        _primary.SetBounds(408, 346, 120, 34);
        _secondary.SetBounds(276, 346, 120, 34);
        _secondary.Text = "Cancel";
        _secondary.Click += (_, _) => Close();
        _primary.Click += async (_, _) => await Run();
        Controls.AddRange(new Control[] { _primary, _secondary });

        // existing installation → offer uninstall straight from setup as well
        if (!uninstall && Installer.InstalledDir() is string existing)
        {
            _primary.Text = "Reinstall";
            var un = new Button { Text = "Uninstall" };
            Style(un, false);
            un.SetBounds(32, 346, 120, 34);
            un.Click += (_, _) =>
            {
                string u = Path.Combine(existing, "Uninstall.exe");
                if (File.Exists(u)) { Process.Start(u, "/uninstall"); Close(); }
                else { Installer.Uninstall(existing, false); MessageBox.Show(this, "HWmonitor was removed.", "HWmonitor"); Close(); }
            };
            Controls.Add(un);
        }
        AcceptButton = _primary;
    }

    static void Style(Button b, bool primary)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = primary ? 0 : 1;
        b.FlatAppearance.BorderColor = Color.FromArgb(210, 210, 215);
        b.BackColor = primary ? Accent : Color.White;
        b.ForeColor = primary ? Color.White : Color.FromArgb(29, 29, 31);
        b.Font = new Font("Segoe UI Semibold", 9.5f);
        b.Cursor = Cursors.Hand;
    }

    async Task Run()
    {
        _primary.Enabled = _secondary.Enabled = false;
        _bar.Visible = true;
        var progress = new Progress<string>(s => _status.Text = s);
        try
        {
            if (_uninstall)
            {
                string dir = Installer.InstalledDir() ?? Path.GetDirectoryName(Application.ExecutablePath);
                bool data = _removeData.Checked;
                await Task.Run(() => Installer.Uninstall(dir, data, progress));
                _bar.Visible = false;
                MessageBox.Show(this, "HWmonitor has been removed from your computer.", "Uninstall complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            else
            {
                string dir = _dir.Text.Trim();
                bool desk = _desktop.Checked, start = _startMenu.Checked, pawn = _pawnIo.Checked;
                await Task.Run(() => Installer.Install(dir, desk, start, progress, pawn));
                _bar.Visible = false;
                if (_launch.Checked) Process.Start(new ProcessStartInfo(Path.Combine(dir, "HWmonitor.exe")) { UseShellExecute = true, WorkingDirectory = dir });
                else MessageBox.Show(this, "HWmonitor was installed successfully.", "Setup complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
        }
        catch (Exception ex)
        {
            _bar.Visible = false;
            _status.Text = "";
            MessageBox.Show(this, ex.Message, "HWmonitor Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _primary.Enabled = _secondary.Enabled = true;
        }
    }
}
