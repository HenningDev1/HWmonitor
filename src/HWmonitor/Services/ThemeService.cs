using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace HWmonitor.Services;

/// <summary>Pushes the user's appearance settings into application resources (used via DynamicResource).</summary>
public static class ThemeService
{
    public static Color Parse(string hex, string fallback = "#0A84FF") =>
        Color.TryParse(hex, out var c) ? c : Color.Parse(fallback);

    public static FontFamily Font(string name) =>
        name == "Inter" ? new FontFamily("fonts:Inter#Inter") : new FontFamily(name);

    public static void Apply(AppSettings s)
    {
        var app = Application.Current;
        if (app == null) return;
        var r = app.Resources;
        bool dark = s.Theme != "Light";
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        byte a = (byte)Math.Clamp(s.WindowOpacity * 255, 120, 255);
        Color C(string hex) => Color.Parse(hex);
        Color WithA(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

        var window = dark ? C("#111113") : C("#F5F5F7");
        r["WindowBrush"] = new SolidColorBrush(WithA(window, a));
        r["SidebarBrush"] = new SolidColorBrush(dark ? C("#14FFFFFF") : C("#0A000000"));
        r["CardBrush"] = new SolidColorBrush(dark ? C("#2C2C2E") : C("#FFFFFF"));
        r["CardTopColor"] = dark ? C("#232326") : C("#FFFFFF");
        r["CardBottomColor"] = dark ? C("#1A1A1C") : C("#FAFAFC");
        r["CardAltBrush"] = new SolidColorBrush(dark ? C("#2A2A2E") : C("#F2F2F7"));
        r["TextBrush"] = new SolidColorBrush(dark ? C("#F5F5F7") : C("#1D1D1F"));
        r["SubTextBrush"] = new SolidColorBrush(dark ? C("#98989D") : C("#6E6E73"));
        r["HairlineBrush"] = new SolidColorBrush(dark ? C("#33FFFFFF") : C("#1F000000"));
        r["TrackColor"] = dark ? C("#3A3A3C") : C("#E5E5EA");

        var accent = Parse(s.Accent);
        r["AccentBrush"] = new SolidColorBrush(accent);
        r["AccentSoftBrush"] = new SolidColorBrush(WithA(accent, 40));
        r["SystemAccentColor"] = accent;
        r["SystemAccentColorLight1"] = accent;
        r["SystemAccentColorDark1"] = accent;

        r["CpuColor"] = Parse(s.CpuColor);
        r["CoreColor"] = Parse(s.CoreColor);
        r["GpuColor"] = Parse(s.GpuColor);
        r["VramColor"] = Parse(s.VramColor);
        r["RamColor"] = Parse(s.RamColor);
        r["DiskColor"] = Parse(s.DiskColor);
        r["NetColor"] = Parse(s.NetColor);
        r["FpsColor"] = Parse(s.FpsColor);
        r["TempCoolColor"] = Parse(s.TempCoolColor);
        r["TempHotColor"] = Parse(s.TempHotColor);

        r["AppFont"] = Font(s.FontFamily);
        r["AppFontSize"] = s.FontSize;
    }
}
