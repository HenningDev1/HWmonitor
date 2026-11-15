using System.Text.Json;

namespace HWmonitor.Services;

public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";          // Dark | Light
    public string FontFamily { get; set; } = "Inter";
    public double FontSize { get; set; } = 13;
    public double WindowOpacity { get; set; } = 1.0;
    public string Accent { get; set; } = "#0A84FF";

    public string CpuColor { get; set; } = "#0A84FF";
    public string CoreColor { get; set; } = "#64D2FF";
    public string GpuColor { get; set; } = "#30D158";
    public string VramColor { get; set; } = "#66D4CF";
    public string RamColor { get; set; } = "#BF5AF2";
    public string DiskColor { get; set; } = "#FF9F0A";
    public string NetColor { get; set; } = "#5E5CE6";
    public string FpsColor { get; set; } = "#FF375F";
    public string TempCoolColor { get; set; } = "#30D158";
    public string TempHotColor { get; set; } = "#FF453A";

    public int UpdateIntervalMs { get; set; } = 1000;
    public bool StartWithWindows { get; set; }
    public bool TrackGames { get; set; } = true;
    public List<string> IgnoredProcesses { get; set; } = [];
    public List<string> ExtraGameProcesses { get; set; } = [];

    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HWmonitor");

    static string FilePath => Path.Combine(DataDir, "settings.json");
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch { /* corrupt file → defaults */ }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch { }
    }
}
