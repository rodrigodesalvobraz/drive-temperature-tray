using System;
using System.Drawing;
using SsdTemperatureTray;
using System.IO;
using System.Threading;
using System.Diagnostics;

class Tests
{
    static int checks;
    static void Assert(bool condition, string label) { if (!condition) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
    static void Reject(Action action, string label) { bool rejected = false; try { action(); } catch { rejected = true; } Assert(rejected, label); }
    static void Main()
    {
        Assert(TemperatureReader.Parse("{\"temperature\":{\"current\":72}}", 0) == 72, "NVMe unified temperature");
        Assert(TemperatureReader.Parse("{\"temperature\":{\"current\":41},\"ata_smart_attributes\":{}}", 0) == 41, "ATA unified temperature");
        Assert(TemperatureReader.Parse("{\"temperature\":{\"current\":0}}", 0) == 0, "zero Celsius is valid");
        Assert(TemperatureReader.Parse("{\"temperature\":{\"current\":72}}", 8 | 64) == 72, "health and history flags preserve valid readings");
        Reject(delegate { TemperatureReader.Parse("{\"temperature\":{\"current\":72}}", 2); }, "open-device errors reject stale/partial readings");
        Reject(delegate { TemperatureReader.Parse("{}", 0); }, "missing sensor");
        Reject(delegate { TemperatureReader.Parse("{\"temperature\":{\"current\":null}}", 0); }, "null sensor");
        Reject(delegate { TemperatureReader.Parse("{\"temperature\":{\"current\":\"72\"}}", 0); }, "non-numeric sensor");
        Reject(delegate { TemperatureReader.Parse("{\"temperature\":{\"current\":999}}", 0); }, "impossible temperature");
        Reject(delegate { TemperatureReader.Parse("not json", 0); }, "malformed output");
        Reject(delegate { new Settings { Device = "/dev/sda --other" }.Validate(); }, "argument injection rejected");
        Reject(delegate { new Settings { IntervalSeconds = 0 }.Validate(); }, "zero interval rejected");
        Reject(delegate { new Settings { WarmCelsius = 70, HotCelsius = 60 }.Validate(); }, "reversed thresholds rejected");
        Reject(delegate { TemperatureReader.Locate("relative.exe"); }, "explicit executable must be absolute");
        using (var icon = TemperatureIcon.Create("72", Color.Firebrick)) Assert(icon.Width >= 16, "native icon creation");
        using (var bitmap = TemperatureIcon.Draw("72", Color.Firebrick, 128)) bitmap.Save("test-results/icon-preview.png");
        var settings = new Settings { SmartctlPath = Path.GetFullPath("build/FakeSmartctl.exe") };
        var reading = TemperatureReader.ReadAsync(settings, CancellationToken.None).GetAwaiter().GetResult();
        Assert(reading.Celsius == 47 && reading.ExitCode == 8, "process reads large stdout/stderr without deadlock");
        settings.Device = "/dev/fail";
        reading = TemperatureReader.ReadAsync(settings, CancellationToken.None).GetAwaiter().GetResult();
        Assert(!reading.Celsius.HasValue && reading.Error != null, "process failures clear temperature");
        settings.Device = "/dev/timeout";
        var watch = Stopwatch.StartNew();
        reading = TemperatureReader.ReadAsync(settings, CancellationToken.None).GetAwaiter().GetResult();
        Assert(!reading.Celsius.HasValue && reading.Error.Contains("timed out") && watch.Elapsed.TotalSeconds < 10, "hung process is bounded by timeout");
        using (var cancellation = new CancellationTokenSource(150))
            Reject(delegate { TemperatureReader.ReadAsync(settings, cancellation.Token).GetAwaiter().GetResult(); }, "shutdown cancels pending reads");
        Console.WriteLine(checks + " checks passed.");
    }
}
