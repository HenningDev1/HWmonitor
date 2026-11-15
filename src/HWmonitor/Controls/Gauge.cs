using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HWmonitor.Controls;

/// <summary>Speedometer-style gauge, drawn directly (no templates) to stay cheap.</summary>
public sealed class Gauge : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<Gauge, double>(nameof(Value));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<Gauge, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<Gauge, string?>(nameof(Text));
    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<Gauge, string?>(nameof(Label));
    public static readonly StyledProperty<Color> GaugeColorProperty = AvaloniaProperty.Register<Gauge, Color>(nameof(GaugeColor), Colors.DodgerBlue);
    public static readonly StyledProperty<Color> TrackColorProperty = AvaloniaProperty.Register<Gauge, Color>(nameof(TrackColor), Color.Parse("#3A3A3C"));
    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<Gauge, IBrush?>(nameof(Foreground), Brushes.White);
    public static readonly StyledProperty<IBrush?> SubForegroundProperty = AvaloniaProperty.Register<Gauge, IBrush?>(nameof(SubForeground), Brushes.Gray);
    public static readonly StyledProperty<FontFamily> FontFamilyProperty = Avalonia.Controls.Documents.TextElement.FontFamilyProperty.AddOwner<Gauge>();
    public static readonly StyledProperty<bool> ShowTicksProperty = AvaloniaProperty.Register<Gauge, bool>(nameof(ShowTicks), true);

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public Color GaugeColor { get => GetValue(GaugeColorProperty); set => SetValue(GaugeColorProperty, value); }
    public Color TrackColor { get => GetValue(TrackColorProperty); set => SetValue(TrackColorProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public IBrush? SubForeground { get => GetValue(SubForegroundProperty); set => SetValue(SubForegroundProperty, value); }
    public FontFamily FontFamily { get => GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public bool ShowTicks { get => GetValue(ShowTicksProperty); set => SetValue(ShowTicksProperty, value); }

    static Gauge()
    {
        AffectsRender<Gauge>(ValueProperty, MaximumProperty, TextProperty, LabelProperty, GaugeColorProperty,
            TrackColorProperty, ForegroundProperty, SubForegroundProperty, FontFamilyProperty, ShowTicksProperty);
    }

    const double StartDeg = 135, SweepDeg = 270;

    static Point P(Point c, double r, double deg)
    {
        double a = deg * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
    }

    static StreamGeometry Arc(Point c, double r, double fromDeg, double sweep)
    {
        var g = new StreamGeometry();
        using var ctx = g.Open();
        ctx.BeginFigure(P(c, r, fromDeg), false);
        ctx.ArcTo(P(c, r, fromDeg + sweep), new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise);
        ctx.EndFigure(false);
        return g;
    }

    public override void Render(DrawingContext dc)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size < 10) return;
        double thick = Math.Max(4, size * 0.075);
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2 + size * 0.04);
        double r = size / 2 - thick / 2 - 1;
        double frac = Maximum > 0 ? Math.Clamp(Value / Maximum, 0, 1) : 0;

        dc.DrawGeometry(null, new Pen(new SolidColorBrush(TrackColor), thick, lineCap: PenLineCap.Round), Arc(c, r, StartDeg, SweepDeg));
        if (frac > 0.003)
        {
            var col = GaugeColor;
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(170, col.R, col.G, col.B), 0), new GradientStop(col, 1) },
            };
            dc.DrawGeometry(null, new Pen(brush, thick, lineCap: PenLineCap.Round), Arc(c, r, StartDeg, SweepDeg * frac));
        }

        var sub = SubForeground ?? Brushes.Gray;
        if (ShowTicks && size >= 90)
        {
            var tickPen = new Pen(sub, Math.Max(1, size * 0.008));
            for (int i = 0; i <= 10; i++)
            {
                double deg = StartDeg + SweepDeg * i / 10;
                double inner = r - thick / 2 - size * (i % 5 == 0 ? 0.075 : 0.045);
                dc.DrawLine(tickPen, P(c, r - thick / 2 - size * 0.02, deg), P(c, inner, deg));
            }
        }

        // needle
        if (size >= 120)
        {
            double nd = StartDeg + SweepDeg * frac;
            var needlePen = new Pen(new SolidColorBrush(GaugeColor), Math.Max(2, size * 0.022), lineCap: PenLineCap.Round);
            dc.DrawLine(needlePen, P(c, size * 0.05, nd), P(c, r - thick - size * 0.06, nd));
            dc.DrawEllipse(Foreground, null, c, size * 0.035, size * 0.035);
        }

        var tf = new Typeface(FontFamily, FontStyle.Normal, FontWeight.SemiBold);
        var text = Text ?? $"{Value:0}%";
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, size * (size >= 120 ? 0.15 : 0.2), Foreground);
        double ty = size >= 120 ? c.Y + r * 0.38 : c.Y - ft.Height * 0.62;
        dc.DrawText(ft, new Point(c.X - ft.Width / 2, ty));

        if (!string.IsNullOrEmpty(Label))
        {
            var lt = new FormattedText(Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily, FontStyle.Normal, FontWeight.Medium), Math.Max(9, size * 0.075), sub);
            dc.DrawText(lt, new Point(c.X - lt.Width / 2, size >= 120 ? ty + ft.Height : ty + ft.Height - 2));
        }
    }
}
