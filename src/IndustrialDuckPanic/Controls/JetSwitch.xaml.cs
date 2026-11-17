using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using IndustrialDuckPanic.ViewModels;

namespace IndustrialDuckPanic.Controls;

/// <summary>OFF (top) when docked, ON (bottom) in space, middle on error or unknown state.</summary>
public partial class JetSwitch : UserControl
{
    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(SwitchPosition), typeof(JetSwitch),
        new PropertyMetadata(SwitchPosition.Middle, (d, _) => ((JetSwitch)d).Apply(animate: true)));

    public SwitchPosition Position
    {
        get => (SwitchPosition)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    private static readonly Brush LabelOff = new SolidColorBrush(Color.FromRgb(0x7A, 0x7F, 0x87));
    private static readonly Brush LabelOn = new SolidColorBrush(Color.FromRgb(0xEF, 0xE6, 0xD6));

    public JetSwitch()
    {
        InitializeComponent();
        Apply(animate: false);
    }

    private void Apply(bool animate)
    {
        // Lever travel: knob sits at the centre (y=56); move 34 px up or down.
        var (y, stemTop) = Position switch
        {
            SwitchPosition.Off => (-34.0, 50.0),
            SwitchPosition.On => (34.0, 26.0),
            _ => (0.0, 38.0),
        };

        if (animate)
        {
            var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };
            LeverMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TimeSpan.FromSeconds(0.35)) { EasingFunction = ease });
            Stem.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(stemTop, TimeSpan.FromSeconds(0.35)) { EasingFunction = ease });
        }
        else
        {
            LeverMove.Y = y;
            Canvas.SetTop(Stem, stemTop);
        }

        OffLabel.Foreground = Position == SwitchPosition.Off ? LabelOn : LabelOff;
        OnLabel.Foreground = Position == SwitchPosition.On ? LabelOn : LabelOff;

        var lit = Position == SwitchPosition.On;
        Led.Fill = lit ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x2A, 0x2C, 0x30));
        LedGlow.Opacity = lit ? 1 : 0;
    }
}
