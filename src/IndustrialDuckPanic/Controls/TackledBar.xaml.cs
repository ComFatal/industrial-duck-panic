using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IndustrialDuckPanic.Controls;

public partial class TackledBar : UserControl
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(TackledBar), new PropertyMetadata(false, (d, _) => ((TackledBar)d).Apply()));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private static readonly Color IdleTop = Color.FromRgb(0x45, 0x49, 0x4F), IdleBottom = Color.FromRgb(0x32, 0x35, 0x39);
    private static readonly Color HotTop = Color.FromRgb(0xE8, 0x8E, 0x1E), HotBottom = Color.FromRgb(0xA8, 0x5A, 0x06);
    private static readonly Color IdleText = Color.FromRgb(0x8A, 0x8E, 0x94), HotText = Color.FromRgb(0xFF, 0xF4, 0xE0);

    public TackledBar()
    {
        InitializeComponent();
    }

    private void Apply()
    {
        LeftLed.IsActive = IsActive;
        RightLed.IsActive = IsActive;

        if (IsActive)
        {
            // Slow pulse, like an old power-plant warning light.
            var duration = TimeSpan.FromSeconds(1.3);
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            Pulse(BarTop, GradientStop.ColorProperty, IdleTop, HotTop, duration, ease);
            Pulse(BarBottom, GradientStop.ColorProperty, IdleBottom, HotBottom, duration, ease);
            Pulse(BarTextBrush, SolidColorBrush.ColorProperty, IdleText, HotText, duration, ease);
            BarGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,
                new DoubleAnimation(0, 0.9, duration) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
        }
        else
        {
            BarTop.BeginAnimation(GradientStop.ColorProperty, null);
            BarBottom.BeginAnimation(GradientStop.ColorProperty, null);
            BarTextBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            BarGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, null);
            BarTop.Color = IdleTop;
            BarBottom.Color = IdleBottom;
            BarTextBrush.Color = IdleText;
            BarGlow.Opacity = 0;
        }
    }

    private static void Pulse(Animatable target, DependencyProperty prop, Color from, Color to, TimeSpan d, IEasingFunction ease) =>
        target.BeginAnimation(prop, new ColorAnimation(from, to, d) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
}
