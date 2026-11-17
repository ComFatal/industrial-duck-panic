using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace IndustrialDuckPanic.Controls;

/// <summary>Orange rotating beacon light. Off: dark amber glass. On: lit, with a beam turning around.</summary>
public sealed class Beacon : Grid
{
    private const double Size = 28, Inner = 22;
    private readonly GradientStop _domeInner = new(Color.FromRgb(0x5A, 0x3E, 0x1A), 0);
    private readonly GradientStop _domeOuter = new(Color.FromRgb(0x22, 0x15, 0x07), 1);
    private readonly Path _beam;
    private readonly RotateTransform _rotate = new(0, Inner / 2, Inner / 2);
    private readonly DropShadowEffect _glow = new() { Color = Color.FromRgb(0xFF, 0x9A, 0x1F), BlurRadius = 16, ShadowDepth = 0, Opacity = 0 };

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(Beacon), new PropertyMetadata(false, (d, _) => ((Beacon)d).Apply()));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public Beacon()
    {
        Width = Size;
        Height = Size;

        Children.Add(new Ellipse { Fill = new SolidColorBrush(Color.FromRgb(0x0B, 0x0A, 0x09)) });

        var dome = new Ellipse
        {
            Width = Inner, Height = Inner,
            Fill = new RadialGradientBrush { GradientOrigin = new Point(0.4, 0.35), GradientStops = { _domeInner, _domeOuter } },
            Effect = _glow,
        };
        Children.Add(dome);

        var r = Inner / 2;
        _beam = new Path
        {
            Opacity = 0,
            // 70° wedge from the centre (built in code: geometry strings break with a decimal comma locale)
            Data = new PathGeometry([new PathFigure(new Point(r, r),
            [
                new LineSegment(new Point(r, -r), false),
                new ArcSegment(new Point(r + r * 2 * Math.Sin(Math.PI * 70 / 180), r - r * 2 * Math.Cos(Math.PI * 70 / 180)),
                    new Size(r * 2, r * 2), 0, false, SweepDirection.Clockwise, false),
            ], closed: true)]),
            Fill = new LinearGradientBrush(
                [new GradientStop(Color.FromArgb(0, 0xFF, 0x9A, 0x1F), 0), new GradientStop(Color.FromRgb(0xFF, 0xF1, 0xC8), 0.6), new GradientStop(Color.FromRgb(0xFF, 0x9A, 0x1F), 1)],
                new Point(0, 0.5), new Point(1, 0.5)),
            RenderTransform = _rotate,
        };
        var canvas = new Canvas
        {
            Width = Inner, Height = Inner,
            Clip = new EllipseGeometry(new Point(r, r), r, r),
        };
        canvas.Children.Add(_beam);
        Children.Add(canvas);

        // glass highlight
        Children.Add(new Ellipse
        {
            Width = Inner, Height = Inner, Stroke = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), StrokeThickness = 1,
            Fill = new RadialGradientBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF), Color.FromArgb(0, 0xFF, 0xFF, 0xFF))
            { GradientOrigin = new Point(0.35, 0.25), Center = new Point(0.4, 0.3), RadiusX = 0.55, RadiusY = 0.55 },
        });
    }

    private void Apply()
    {
        if (IsActive)
        {
            _domeInner.Color = Color.FromRgb(0xFF, 0xB3, 0x47);
            _domeOuter.Color = Color.FromRgb(0xB3, 0x5A, 0x00);
            _beam.Opacity = 1;
            _rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.75)) { RepeatBehavior = RepeatBehavior.Forever });
            _glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.5, 1, TimeSpan.FromSeconds(0.375)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            _domeInner.Color = Color.FromRgb(0x5A, 0x3E, 0x1A);
            _domeOuter.Color = Color.FromRgb(0x22, 0x15, 0x07);
            _beam.Opacity = 0;
            _rotate.BeginAnimation(RotateTransform.AngleProperty, null);
            _glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
            _glow.Opacity = 0;
        }
    }
}
