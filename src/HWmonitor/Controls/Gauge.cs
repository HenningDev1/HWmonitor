using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HWmonitor.Controls;

/// <summary>
/// Half-circle "tick" gauge: many radial lines, the ones up to the value lit in the gauge colour,
/// big value with a small unit, a caption and a soft glow underneath. Drawn directly (cheap).
/// </summary>
public sealed class Gauge : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<Gauge, double>(nameof(Value));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<Gauge, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<Gauge, string?>(nameof(Text));
    public static readonly StyledProperty<string?> UnitProperty = AvaloniaProperty.Register<Gauge, string?>(nameof(Unit), "%");
    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<Gauge, string?>(nameof(Label));
    public static readonly StyledProperty<Color> GaugeColorProperty = AvaloniaProperty.Register<Gauge, Color>(nameof(GaugeColor), Colors.DodgerBlue);
    public static readonly StyledProperty<Color> TrackColorProperty = AvaloniaProperty.Register<Gauge, Color>(nameof(TrackColor), Color.Parse("#3A3A3C"));
    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<Gauge, IBrush?>(nameof(Foreground), Brushes.White);
    public static readonly StyledProperty<IBrush?> SubForegroundProperty = AvaloniaProperty.Register<Gauge, IBrush?>(nameof(SubForeground), Brushes.Gray);
    public static readonly StyledProperty<FontFamily> FontFamilyProperty = Avalonia.Controls.Documents.TextElement.FontFamilyProperty.AddOwner<Gauge>();
    public static readonly StyledProperty<int> TicksProperty = AvaloniaProperty.Register<Gauge, int>(nameof(Ticks), 32);
    public static readonly StyledProperty<bool> GlowProperty = AvaloniaProperty.Register<Gauge, bool>(nameof(Glow), true);

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string? Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public Color GaugeColor { get => GetValue(GaugeColorProperty); set => SetValue(GaugeColorProperty, value); }
    public Color TrackColor { get => GetValue(TrackColorProperty); set => SetValue(TrackColorProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public IBrush? SubForeground { get => GetValue(SubForegroundProperty); set => SetValue(SubForegroundProperty, value); }
    public FontFamily FontFamily { get => GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public int Ticks { get => GetValue(TicksProperty); set => SetValue(TicksProperty, value); }
    public bool Glow { get => GetValue(GlowProperty); set => SetValue(GlowProperty, value); }

    static Gauge()
    {
        AffectsRender<Gauge>(ValueProperty, MaximumProperty, TextProperty, UnitProperty, LabelProperty, GaugeColorProperty,
            TrackColorProperty, ForegroundProperty, SubForegroundProperty, FontFamilyProperty, TicksProperty, GlowProperty);
    }

    static Point P(Point c, double r, double deg)
    {
        double a = deg * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
    }

    static Color Mix(Color a, Color b, double t) => Color.FromArgb(255,
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height;
        double r = Math.Min(w / 2 - 2, h - 4);
        if (r < 20) return;
        var c = new Point(w / 2, (h + r) / 2 + 2); // arc centre sits on the bottom edge of the drawing
        double frac = Maximum > 0 ? Math.Clamp(Value / Maximum, 0, 1) : 0;
        var col = GaugeColor;

        using (dc.PushClip(new Rect(0, 0, w, c.Y + 1)))
        {
            if (Glow && frac > 0)
            {
                var glow = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb((byte)(70 + 90 * frac), col.R, col.G, col.B), 0),
                        new GradientStop(Color.FromArgb(0, col.R, col.G, col.B), 1),
                    },
                };
                dc.DrawEllipse(glow, null, c, r * 0.62, r * 0.42);
            }

            int n = Math.Max(8, Ticks);
            double thick = Math.Max(1.5, r * 0.028);
            double outer = r, inner = r * 0.76;
            var track = new Pen(new SolidColorBrush(TrackColor), thick, lineCap: PenLineCap.Round);
            var dim = Color.FromArgb(255, (byte)(col.R * 0.55), (byte)(col.G * 0.55), (byte)(col.B * 0.55));
            int lit = (int)Math.Round(frac * n);
            for (int i = 0; i < n; i++)
            {
                double deg = 180 + 180.0 * i / (n - 1);
                Pen pen = track;
                if (i < lit) pen = new Pen(new SolidColorBrush(Mix(dim, col, lit <= 1 ? 1 : (double)i / (lit - 1))), thick, lineCap: PenLineCap.Round);
                dc.DrawLine(pen, P(c, inner, deg), P(c, outer, deg));
            }
        }

        // value + unit
        var bold = new Typeface(FontFamily, FontStyle.Normal, FontWeight.Bold);
        var text = Text ?? $"{Value:0}";
        var vt = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold, r * 0.34, Foreground);
        FormattedText? ut = string.IsNullOrEmpty(Unit) ? null
            : new FormattedText(Unit, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily), r * 0.13, SubForeground);
        double total = vt.Width + (ut != null ? ut.Width + 2 : 0);
        double vy = c.Y - r * 0.62;
        dc.DrawText(vt, new Point(c.X - total / 2, vy));
        if (ut != null) dc.DrawText(ut, new Point(c.X - total / 2 + vt.Width + 2, vy + r * 0.04));

        if (!string.IsNullOrEmpty(Label))
        {
            var lt = new FormattedText(Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily), Math.Max(9, r * 0.12), SubForeground);
            dc.DrawText(lt, new Point(c.X - lt.Width / 2, vy + vt.Height - r * 0.02));
        }
    }
}
