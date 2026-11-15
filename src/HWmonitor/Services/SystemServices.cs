using System.Diagnostics;
using System.Management;

namespace HWmonitor.Services;

public sealed record DriverStatus(string Gpu, string Vendor, string Version, DateTime? Date, int AgeDays, bool UpdateLikely, string Url);

/// <summary>
/// Reads the installed graphics driver (WMI) and flags it when it is old enough that a newer
/// release is almost certainly available (GPU vendors publish new drivers roughly monthly).
/// </summary>
public static class DriverService
{
    public const int OutdatedAfterDays = 60;

    public static List<DriverStatus> Check()
    {
        var list = new List<DriverStatus>();
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController");
            foreach (ManagementObject m in s.Get())
            {
                string name = m["Name"]?.ToString() ?? "";
                if (name.Contains("Basic", StringComparison.OrdinalIgnoreCase) || name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Parsec", StringComparison.OrdinalIgnoreCase) || name.Contains("Remote", StringComparison.OrdinalIgnoreCase)) continue;
                string ver = m["DriverVersion"]?.ToString() ?? "";
                DateTime? date = null;
                try { if (m["DriverDate"] is string d) date = ManagementDateTimeConverter.ToDateTime(d); } catch { }

                string vendor, url;
                if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                {
                    vendor = "NVIDIA"; url = "https://www.nvidia.com/en-us/drivers/";
                    // 32.0.15.6094 → 560.94
                    var digits = ver.Replace(".", "");
                    if (digits.Length >= 5) ver = $"{digits[^5..^2]}.{digits[^2..]}";
                }
                else if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                { vendor = "AMD"; url = "https://www.amd.com/en/support/download/drivers.html"; }
                else if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                { vendor = "Intel"; url = "https://www.intel.com/content/www/us/en/support/detect.html"; }
                else { vendor = "GPU"; url = "ms-settings:windowsupdate"; }

                int age = date is { } dt ? (int)(DateTime.Now - dt).TotalDays : 0;
                list.Add(new DriverStatus(name, vendor, ver, date, age, age > OutdatedAfterDays, url));
            }
        }
        catch { }
        return list;
    }

    public static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}

/// <summary>Autostart via a scheduled task (needed because the app runs elevated).</summary>
public static class StartupService
{
    const string Task = "HWmonitor";

    public static void Set(bool enabled)
    {
        try
        {
            var exe = Environment.ProcessPath ?? "";
            var args = enabled
                ? $"/Create /F /TN \"{Task}\" /SC ONLOGON /RL HIGHEST /TR \"\\\"{exe}\\\" --minimized\""
                : $"/Delete /F /TN \"{Task}\"";
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args) { CreateNoWindow = true, UseShellExecute = false });
            p?.WaitForExit(5000);
        }
        catch { }
    }
}
