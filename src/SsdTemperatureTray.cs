using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("SSD Temperature Tray")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace SsdTemperatureTray
{
    public sealed class Settings
    {
        public string Device { get; set; }
        public string SmartctlPath { get; set; }
        public int IntervalSeconds { get; set; }
        public int WarmCelsius { get; set; }
        public int HotCelsius { get; set; }
        public Settings() { Device = "/dev/sda"; SmartctlPath = ""; IntervalSeconds = 2; WarmCelsius = 60; HotCelsius = 70; }
        public void Validate()
        {
            if (String.IsNullOrWhiteSpace(Device) || !System.Text.RegularExpressions.Regex.IsMatch(Device, @"^/dev/[a-zA-Z0-9/._-]+$"))
                throw new ArgumentException("Device must be a smartctl device such as /dev/sda or /dev/nvme0.");
            if (IntervalSeconds < 1 || IntervalSeconds > 3600) throw new ArgumentException("Interval must be 1 to 3600 seconds.");
            if (WarmCelsius < 0 || HotCelsius <= WarmCelsius || HotCelsius > 150) throw new ArgumentException("Thresholds must satisfy 0 <= warm < hot <= 150.");
        }
    }

    public sealed class Reading
    {
        public DateTime TimestampUtc { get; set; }
        public int? Celsius { get; set; }
        public string Error { get; set; }
        public int ExitCode { get; set; }
    }

    public static class Storage
    {
        public static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SsdTemperatureTray");
        public static string ConfigPath { get { return Path.Combine(DirectoryPath, "settings.json"); } }
        public static Settings Load()
        {
            if (!File.Exists(ConfigPath)) { var defaults = new Settings(); Save(defaults); return defaults; }
            var settings = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(ConfigPath));
            if (settings == null) throw new ArgumentException("Settings cannot be empty.");
            settings.Validate(); return settings;
        }
        public static void Save(Settings settings)
        {
            settings.Validate(); WriteAtomic(ConfigPath, new JavaScriptSerializer().Serialize(settings));
        }
        public static void WriteAtomic(string path, string text)
        {
            Directory.CreateDirectory(DirectoryPath);
            string temp = path + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
    }

    public static class TemperatureReader
    {
        public static string Locate(string configured)
        {
            if (!String.IsNullOrWhiteSpace(configured))
            {
                if (!Path.IsPathRooted(configured) || !File.Exists(configured)) throw new FileNotFoundException("Choose an existing absolute path to smartctl.exe.");
                return configured;
            }
            foreach (string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
            {
                string path = Path.Combine(root, "smartmontools", "bin", "smartctl.exe");
                if (File.Exists(path)) return path;
            }
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                if (String.IsNullOrWhiteSpace(dir) || !Path.IsPathRooted(dir.Trim('"'))) continue;
                try { string path = Path.Combine(dir.Trim('"'), "smartctl.exe"); if (File.Exists(path)) return path; } catch (ArgumentException) { }
            }
            throw new FileNotFoundException("smartctl.exe was not found. Install smartmontools or select its path in Settings.");
        }
        public static int Parse(string json, int exitCode)
        {
            // smartctl returns a bitmask. Health/history warning bits do not invalidate a temperature.
            if ((exitCode & 3) != 0) throw new InvalidOperationException("smartctl could not open the device or rejected its arguments (exit " + exitCode + "). Check the device and permissions.");
            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            object value;
            if (root == null || !root.TryGetValue("temperature", out value)) throw new InvalidOperationException("No temperature reported by this device.");
            var temperature = value as Dictionary<string, object>;
            if (temperature == null || !temperature.TryGetValue("current", out value) || value == null || !(value is int)) throw new InvalidOperationException("Invalid temperature in smartctl output.");
            int degrees = (int)value;
            if (degrees < -50 || degrees > 200) throw new InvalidOperationException("Temperature is outside the supported range.");
            return degrees;
        }
        public static async Task<Reading> ReadAsync(Settings settings, CancellationToken cancellation)
        {
            var result = new Reading { ExitCode = -1 };
            try
            {
                settings.Validate();
                using (var process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo(Locate(settings.SmartctlPath), "-j -A " + settings.Device) {
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                    };
                    process.Start();
                    Task<string> output = process.StandardOutput.ReadToEndAsync();
                    Task<string> error = process.StandardError.ReadToEndAsync();
                    var watch = Stopwatch.StartNew();
                    try
                    {
                        while (!process.HasExited)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            if (watch.ElapsedMilliseconds > 5000) throw new TimeoutException("smartctl timed out after 5 seconds.");
                            await Task.Delay(50, cancellation);
                        }
                        result.ExitCode = process.ExitCode;
                        string json = await output;
                        await error;
                        result.Celsius = Parse(json, result.ExitCode);
                    }
                    finally { if (!process.HasExited) { try { process.Kill(); } catch (InvalidOperationException) { } } }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { result.Error = ex.Message; }
            result.TimestampUtc = DateTime.UtcNow;
            return result;
        }
    }

    public static class TemperatureIcon
    {
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
        public static Bitmap Draw(string text, Color background, int size)
        {
            var bitmap = new Bitmap(size, size);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(background))
            using (var font = new Font("Segoe UI", size * (text.Length > 2 ? .50f : .67f), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                graphics.FillRectangle(brush, 0, 0, size, size);
                graphics.DrawString(text, font, Brushes.White, new RectangleF(-1, -1, size + 2, size + 1), format);
            }
            return bitmap;
        }
        public static Icon Create(string text, Color background)
        {
            using (Bitmap bitmap = Draw(text, background, Math.Max(16, SystemInformation.SmallIconSize.Width)))
            {
                IntPtr handle = bitmap.GetHicon();
                try { using (Icon icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
    }

    public static class Startup
    {
        private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static bool Enabled { get { using (var key = Registry.CurrentUser.OpenSubKey(Key)) return key != null && key.GetValue("SsdTemperatureTray") != null; } }
        public static void Set(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(Key))
                if (enabled) key.SetValue("SsdTemperatureTray", "\"" + Application.ExecutablePath + "\""); else key.DeleteValue("SsdTemperatureTray", false);
        }
    }

    internal sealed class TrayApplication : ApplicationContext
    {
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly ToolStripMenuItem summary = new ToolStripMenuItem("Reading temperature...");
        private readonly ToolStripMenuItem startup = new ToolStripMenuItem("Start when I sign in");
        private Settings settings;
        private Reading latest;
        private bool busy, exiting;
        private Form settingsWindow;
        public TrayApplication()
        {
            string configError = null;
            try { settings = Storage.Load(); } catch (Exception ex) { settings = new Settings(); configError = ex.Message; }
            var menu = new ContextMenuStrip();
            summary.Enabled = false; menu.Items.Add(summary);
            menu.Items.Add("Refresh now", null, async delegate { await Poll(); });
            menu.Items.Add("Details", null, delegate { ShowDetails(); });
            menu.Items.Add("Settings...", null, delegate { ShowSettings(); });
            startup.Checked = Startup.Enabled;
            startup.Click += delegate { try { Startup.Set(!Startup.Enabled); startup.Checked = Startup.Enabled; } catch (Exception ex) { MessageBox.Show(ex.Message, "Startup setting"); } };
            menu.Items.Add(startup);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitThread(); });
            tray.ContextMenuStrip = menu;
            tray.Icon = TemperatureIcon.Create("--", Color.DimGray);
            tray.Text = "SSD Temperature: reading...";
            tray.DoubleClick += delegate { ShowDetails(); };
            tray.Visible = true;
            timer.Interval = settings.IntervalSeconds * 1000;
            timer.Tick += async delegate { await Poll(); };
            timer.Start();
            if (configError != null) tray.ShowBalloonTip(10000, "Settings could not be loaded; using defaults", configError, ToolTipIcon.Warning);
            var ignored = Poll();
        }
        private async Task Poll()
        {
            if (busy || exiting) return;
            busy = true;
            try
            {
                Settings requested = settings;
                Reading reading = await TemperatureReader.ReadAsync(requested, cancellation.Token);
                if (exiting || !Object.ReferenceEquals(requested, settings)) return;
                latest = reading;
                string text = latest.Celsius.HasValue ? latest.Celsius.Value.ToString(CultureInfo.InvariantCulture) : "--";
                Color color = !latest.Celsius.HasValue ? Color.DimGray : latest.Celsius >= settings.HotCelsius ? Color.FromArgb(186, 36, 45) : latest.Celsius >= settings.WarmCelsius ? Color.FromArgb(158, 91, 0) : Color.FromArgb(0, 91, 151);
                Icon previous = tray.Icon;
                tray.Icon = TemperatureIcon.Create(text, color);
                if (previous != null) previous.Dispose();
                string tooltip = latest.Celsius.HasValue ? settings.Device + ": " + text + " °C | " + latest.TimestampUtc.ToLocalTime().ToString("HH:mm:ss") : "SSD temperature unavailable - double-click for details";
                tray.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip;
                summary.Text = latest.Celsius.HasValue ? settings.Device + "  " + text + " °C" : "Temperature unavailable";
                try { Storage.WriteAtomic(Path.Combine(Storage.DirectoryPath, "status.json"), new JavaScriptSerializer().Serialize(latest)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            catch (OperationCanceledException) { }
            finally { busy = false; }
        }
        private void ShowDetails()
        {
            string message = latest == null ? "Waiting for the first reading." : latest.Celsius.HasValue ? "Temperature: " + latest.Celsius + " °C\nRead at: " + latest.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : latest.Error;
            MessageBox.Show(message + "\n\nDevice: " + settings.Device + "\nRefresh: " + settings.IntervalSeconds + " seconds\nsmartctl exit: " + (latest == null ? "pending" : latest.ExitCode.ToString()) + "\n\nIcon colors: blue < " + settings.WarmCelsius + " °C, amber < " + settings.HotCelsius + " °C, red above.\nColors are configurable display thresholds, not drive health limits.", "SSD Temperature Tray", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        private void ShowSettings()
        {
            if (settingsWindow != null) { settingsWindow.Activate(); return; }
            var form = new Form { Text = "SSD Temperature Tray — Settings", ClientSize = new Size(520, 290), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterScreen, Font = new Font("Segoe UI", 9), AutoScaleMode = AutoScaleMode.Dpi };
            settingsWindow = form;
            var device = new TextBox { Text = settings.Device, Left = 170, Top = 20, Width = 320 };
            var path = new TextBox { Text = settings.SmartctlPath, Left = 170, Top = 60, Width = 270 };
            var browse = new Button { Text = "...", Left = 447, Top = 59, Width = 43 };
            browse.Click += delegate { using (var dialog = new OpenFileDialog { Filter = "smartctl executable|smartctl.exe", CheckFileExists = true }) if (dialog.ShowDialog(form) == DialogResult.OK) path.Text = dialog.FileName; };
            var interval = new NumericUpDown { Minimum = 1, Maximum = 3600, Value = settings.IntervalSeconds, Left = 170, Top = 110 };
            var warm = new NumericUpDown { Minimum = 0, Maximum = 149, Value = settings.WarmCelsius, Left = 170, Top = 150 };
            var hot = new NumericUpDown { Minimum = 1, Maximum = 150, Value = settings.HotCelsius, Left = 170, Top = 190 };
            string[] labels = { "Device", "smartctl.exe (auto if blank)", "Refresh (seconds)", "Amber at (°C)", "Red at (°C)" };
            int[] tops = { 23, 63, 113, 153, 193 };
            for (int i = 0; i < labels.Length; i++) form.Controls.Add(new Label { Text = labels[i], Left = 15, Top = tops[i], AutoSize = true });
            var save = new Button { Text = "Save", Left = 320, Top = 245, Width = 80 };
            var cancel = new Button { Text = "Cancel", Left = 410, Top = 245, Width = 80, DialogResult = DialogResult.Cancel };
            save.Click += async delegate
            {
                try
                {
                    var updated = new Settings { Device = device.Text.Trim(), SmartctlPath = path.Text.Trim(), IntervalSeconds = (int)interval.Value, WarmCelsius = (int)warm.Value, HotCelsius = (int)hot.Value };
                    updated.Validate(); TemperatureReader.Locate(updated.SmartctlPath); Storage.Save(updated);
                    settings = updated; timer.Interval = settings.IntervalSeconds * 1000; form.Close(); await Poll();
                }
                catch (Exception ex) { MessageBox.Show(form, ex.Message, "Cannot save settings", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            form.Controls.AddRange(new Control[] { device, path, browse, interval, warm, hot, save, cancel });
            form.AcceptButton = save; form.CancelButton = cancel;
            form.FormClosed += delegate { settingsWindow = null; form.Dispose(); };
            form.Show();
        }
        protected override void ExitThreadCore()
        {
            exiting = true; timer.Stop(); cancellation.Cancel();
            if (settingsWindow != null) settingsWindow.Close();
            tray.Visible = false;
            var icon = tray.Icon; tray.Dispose(); if (icon != null) icon.Dispose();
            timer.Dispose(); base.ExitThreadCore();
        }
    }

    internal static class Program
    {
        [STAThread] private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--probe")
            {
                Reading reading;
                try { reading = TemperatureReader.ReadAsync(Storage.Load(), CancellationToken.None).GetAwaiter().GetResult(); }
                catch (Exception ex) { reading = new Reading { TimestampUtc = DateTime.UtcNow, Error = ex.Message, ExitCode = -1 }; }
                File.WriteAllText(args[1], new JavaScriptSerializer().Serialize(reading));
                return reading.Celsius.HasValue ? 0 : 1;
            }
            bool created;
            using (var mutex = new Mutex(true, @"Local\SsdTemperatureTray", out created))
            {
                if (!created) return 0;
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApplication());
            }
            return 0;
        }
    }
}
