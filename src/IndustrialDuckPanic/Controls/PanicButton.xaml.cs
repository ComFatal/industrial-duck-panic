using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace IndustrialDuckPanic.Controls;

/// <summary>
/// Big PANIC button under a glass safety cover. Click the cover to lift it (right to left),
/// then press the dome. A radar beam sweeps around the dome after a press.
/// </summary>
public partial class PanicButton : UserControl
{
    public static readonly DependencyProperty IsArmedProperty = DependencyProperty.Register(
        nameof(IsArmed), typeof(bool), typeof(PanicButton), new PropertyMetadata(false, (d, _) => ((PanicButton)d).ApplyArmed()));

    /// <summary>True when the ship has a PANIC module (Rorqual). Otherwise the cover stays locked.</summary>
    public bool IsArmed
    {
        get => (bool)GetValue(IsArmedProperty);
        set => SetValue(IsArmedProperty, value);
    }

    public event EventHandler? Pressed;

    private readonly DispatcherTimer _closeTimer = new();
    private bool _open;

    public PanicButton()
    {
        InitializeComponent();
        _closeTimer.Tick += (_, _) => { _closeTimer.Stop(); SetOpen(false); };
        BuildBeam();
        ApplyArmed();
    }

    /// <summary>A 100° radar beam: thin wedges whose opacity grows toward the leading edge.</summary>
    private void BuildBeam()
    {
        const double c = 84, r = 130, span = 100, step = 2.5;
        var accent = Color.FromRgb(0xF0, 0xA0, 0x20);
        for (double a = 0; a < span; a += step)
        {
            var t = (a + step) / span;
            Point P(double deg) => new(c + r * Math.Sin(deg * Math.PI / 180), c - r * Math.Cos(deg * Math.PI / 180));
            var fig = new PathFigure { StartPoint = new Point(c, c), IsClosed = true };
            fig.Segments.Add(new LineSegment(P(a), false));
            fig.Segments.Add(new LineSegment(P(a + step + 0.4), false));
            SweepCanvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = new PathGeometry([fig]),
                Fill = new SolidColorBrush(Color.FromArgb((byte)(200 * t * t), accent.R, accent.G, accent.B)),
            });
        }
        // bright leading edge
        SweepCanvas.Children.Add(new System.Windows.Shapes.Line
        {
            X1 = c, Y1 = c,
            X2 = c + r * Math.Sin(span * Math.PI / 180), Y2 = c - r * Math.Cos(span * Math.PI / 180),
            Stroke = Brushes.White, StrokeThickness = 2.5,
        });
    }

    private void ApplyArmed()
    {
        Dome.IsEnabled = IsArmed;
        GlassLabel.Text = IsArmed ? "LIFT COVER" : "RORQUAL ONLY";
        Glass.Cursor = IsArmed ? Cursors.Hand : Cursors.No;
        Glass.ToolTip = IsArmed ? "Lift the cover" : "PANIC is only available in a Rorqual";
        if (!IsArmed && _open) SetOpen(false);
    }

    private void Glass_Click(object sender, MouseButtonEventArgs e) => TryOpen();

    private void Glass_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space) TryOpen();
    }

    private void TryOpen()
    {
        if (!IsArmed || _open) return;
        SetOpen(true);
        Dome.Focus();
        RestartCloseTimer(TimeSpan.FromSeconds(8));
    }

    private void SetOpen(bool open)
    {
        _open = open;
        Glass.IsHitTestVisible = !open;

        var duration = TimeSpan.FromSeconds(open ? 0.6 : 0.45);
        IEasingFunction ease = open ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 } : new CubicEase { EasingMode = EasingMode.EaseIn };
        GlassScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(open ? 0.07 : 1, duration) { EasingFunction = ease });
        GlassSkew.BeginAnimation(SkewTransform.AngleYProperty, new DoubleAnimation(open ? -16 : 0, duration) { EasingFunction = ease });
        Glass.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 0.75 : 1, duration));
    }

    /// <summary>Demo mode only: lifts the cover, then plays the sweep without raising <see cref="Pressed"/>.</summary>
    public async void PlayDemo()
    {
        await Task.Delay(800);
        SetOpen(true);
        await Task.Delay(1500);
        PlaySweep();
    }

    private void RestartCloseTimer(TimeSpan after)
    {
        _closeTimer.Stop();
        _closeTimer.Interval = after;
        _closeTimer.Start();
    }

    private void Dome_Click(object sender, RoutedEventArgs e)
    {
        if (!_open || !IsArmed) return;
        PlaySweep();
        Pressed?.Invoke(this, EventArgs.Empty);
        RestartCloseTimer(TimeSpan.FromSeconds(3));
    }

    /// <summary>Fast radar beam: five quick turns, then fades out.</summary>
    public void PlaySweep()
    {
        SweepRotate.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.4)) { RepeatBehavior = new RepeatBehavior(5) });
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.6))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.0))));
        Sweep.BeginAnimation(OpacityProperty, fade);
    }
}
