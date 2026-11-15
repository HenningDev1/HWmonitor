using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HWmonitor.Controls;

/// <summary>0–100 °C bar; fill colour blends from cool to hot with the temperature.</summary>
public sealed class TempBar : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<TempBar, double>(nameof(Value));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<TempBar, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<Color> CoolColorProperty = AvaloniaProperty.Register<TempBar, Color>(nameof(CoolColor), Colors.LimeGreen);
    public static readonly StyledProperty<Color> HotColorProperty = AvaloniaProperty.Register<TempBar, Color>(nameof(HotColor), Colors.Red);
    public static readonly StyledProperty<Color> TrackColorProperty = AvaloniaProperty.Register<TempBar, Color>(nameof(TrackColor), Color.Parse("#3A3A3C"));

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public Color CoolColor { get => GetValue(CoolColorProperty); set => SetValue(CoolColorProperty, value); }
    public Color HotColor { get => GetValue(HotColorProperty); set => SetValue(HotColorProperty, value); }
    public Color TrackColor { get => GetValue(TrackColorProperty); set => SetValue(TrackColorProperty, value); }

    static TempBar()
    {
        AffectsRender<TempBar>(ValueProperty, MaximumProperty, CoolColorProperty, HotColorProperty, TrackColorProperty);
        HeightProperty.OverrideDefaultValue<TempBar>(8);
    }

    static Color Lerp(Color a, Color b, double t) => Color.FromArgb(255,
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public override void Render(DrawingContext dc)
    {
        var b = new Rect(Bounds.Size);
        double rad = b.Height / 2;
        dc.DrawRectangle(new SolidColorBrush(TrackColor), null, b, rad, rad);
        double frac = Math.Clamp(Value / Maximum, 0, 1);
        if (frac <= 0) return;
        // cool until 40 °C, fully hot at 90 °C
        double heat = Math.Clamp((Value - 40) / 50, 0, 1);
        var end = Lerp(CoolColor, HotColor, heat);
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(CoolColor, 0), new GradientStop(end, 1) },
        };
        dc.DrawRectangle(brush, null, new Rect(0, 0, Math.Max(b.Height, b.Width * frac), b.Height), rad, rad);
    }
}

/// <summary>Tiny line chart for frame-time / speed history.</summary>
public sealed class Sparkline : Control
{
    public static readonly StyledProperty<double[]?> ValuesProperty = AvaloniaProperty.Register<Sparkline, double[]?>(nameof(Values));
    public static readonly StyledProperty<Color> LineColorProperty = AvaloniaProperty.Register<Sparkline, Color>(nameof(LineColor), Colors.DodgerBlue);

    public double[]? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public Color LineColor { get => GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }

    static Sparkline() => AffectsRender<Sparkline>(ValuesProperty, LineColorProperty);

    public override void Render(DrawingContext dc)
    {
        var v = Values;
        if (v == null || v.Length < 2) return;
        double max = Math.Max(1, v.Max() * 1.15), w = Bounds.Width, h = Bounds.Height;
        var line = new StreamGeometry();
        var fill = new StreamGeometry();
        using (var l = line.Open())
        using (var f = fill.Open())
        {
            f.BeginFigure(new Point(0, h), true);
            for (int i = 0; i < v.Length; i++)
            {
                var p = new Point(w * i / (v.Length - 1), h - h * v[i] / max);
                if (i == 0) l.BeginFigure(p, false); else l.LineTo(p);
                f.LineTo(p);
            }
            f.LineTo(new Point(w, h));
            f.EndFigure(true);
            l.EndFigure(false);
        }
        var c = LineColor;
        dc.DrawGeometry(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(90, c.R, c.G, c.B), 0), new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1) },
        }, null, fill);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(c), 1.6, lineJoin: PenLineJoin.Round), line);
    }
}

/// <summary>Thin rounded utilisation bar in a single colour.</summary>
public sealed class ValueBar : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<ValueBar, double>(nameof(Value));
    public static readonly StyledProperty<Color> BarColorProperty = AvaloniaProperty.Register<ValueBar, Color>(nameof(BarColor), Colors.DodgerBlue);
    public static readonly StyledProperty<Color> TrackColorProperty = AvaloniaProperty.Register<ValueBar, Color>(nameof(TrackColor), Color.Parse("#3A3A3C"));
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Color BarColor { get => GetValue(BarColorProperty); set => SetValue(BarColorProperty, value); }
    public Color TrackColor { get => GetValue(TrackColorProperty); set => SetValue(TrackColorProperty, value); }
    static ValueBar()
    {
        AffectsRender<ValueBar>(ValueProperty, BarColorProperty, TrackColorProperty);
        HeightProperty.OverrideDefaultValue<ValueBar>(6);
    }
    public override void Render(DrawingContext dc)
    {
        var b = new Rect(Bounds.Size); double rad = b.Height / 2;
        dc.DrawRectangle(new SolidColorBrush(TrackColor), null, b, rad, rad);
        double f = Math.Clamp(Value / 100, 0, 1);
        if (f > 0) dc.DrawRectangle(new SolidColorBrush(BarColor), null, new Rect(0, 0, Math.Max(b.Height, b.Width * f), b.Height), rad, rad);
    }
}
