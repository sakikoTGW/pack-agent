using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Pad.Views;

/// <summary>
/// The motion that cannot be expressed as a style trigger: staggered list entrances and
/// the page swap. Everything here moves opacity and a transform only, and the whole
/// module goes quiet when Windows has animations switched off.
/// </summary>
public static class Fx
{
    static bool? _forced;
    static double _speed = 1;

    /// <summary>
    /// Off when Windows says so, or when the user flipped 个性化 → 动画. A forced
    /// false always wins; a forced true still respects the OS off-switch.
    /// </summary>
    public static bool Animated =>
        SystemParameters.ClientAreaAnimation && (_forced ?? true);

    public static void SetAnimate(bool on) => _forced = on;

    /// <summary>1 = default. Above 1 stretches durations (slower), below 1 shortens them.</summary>
    public static void SetSpeed(double speed) => _speed = Math.Clamp(speed, 0.5, 2.0);

    static readonly CubicEase Out = new() { EasingMode = EasingMode.EaseOut };
    static readonly CubicEase In = new() { EasingMode = EasingMode.EaseIn };

    public static readonly DependencyProperty EnterProperty = DependencyProperty.RegisterAttached(
        "Enter", typeof(string), typeof(Fx), new PropertyMetadata(null, OnEnterChanged));

    public static void SetEnter(DependencyObject d, string value) => d.SetValue(EnterProperty, value);

    public static string? GetEnter(DependencyObject d) => (string?)d.GetValue(EnterProperty);

    static void OnEnterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        fe.Loaded -= OnLoaded;
        fe.Loaded += OnLoaded;
    }

    static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var fe = (FrameworkElement)sender;
        fe.Loaded -= OnLoaded;
        if (!Animated) return;

        var rise = GetEnter(fe) == "card" ? 10.0 : 6.0;
        // Hold the start pose before the stagger delay. BeginTime alone leaves
        // Opacity at 1 until the clock starts, so the row flashes fully opaque.
        fe.Opacity = 0;
        var shift = new TranslateTransform { Y = rise };
        fe.RenderTransform = shift;

        var delay = TimeSpan.FromMilliseconds(Math.Min(IndexOf(fe) * MsValue(36), MsValue(216)));
        fe.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(230))
        {
            BeginTime = delay,
            EasingFunction = Out,
        });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(rise, 0, Ms(280))
        {
            BeginTime = delay,
            EasingFunction = Out,
        });
    }

    /// <summary>Position of an element's container inside the list that generated it.</summary>
    static int IndexOf(DependencyObject? node)
    {
        for (var depth = 0; node is not null && depth < 64; depth++)
        {
            var owner = ItemsControl.ItemsControlFromItemContainer(node);
            if (owner is not null)
            {
                var i = owner.ItemContainerGenerator.IndexFromContainer(node);
                return i < 0 ? 0 : i;
            }
            node = VisualTreeHelper.GetParent(node);
        }
        return 0;
    }

    /// <summary>The page swap: content arrives from slightly below and settles.</summary>
    public static void PageIn(UIElement page)
    {
        if (!Animated)
        {
            page.BeginAnimation(UIElement.OpacityProperty, null);
            page.Opacity = 1;
            page.RenderTransform = Transform.Identity;
            return;
        }
        page.Opacity = 0;
        var shift = new TranslateTransform { Y = 9 };
        page.RenderTransform = shift;
        page.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(190))
        {
            EasingFunction = Out,
        });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(9, 0, Ms(280))
        {
            EasingFunction = Out,
        });
    }

    /// <summary>Crossfade between the two title-bar modes.</summary>
    public static void Reveal(UIElement element)
    {
        if (!Animated) { element.Opacity = 1; return; }
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(160))
        {
            EasingFunction = Out,
        });
    }

    public static void Slide(TranslateTransform transform, double to, int ms = 220)
    {
        if (!Animated)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = to;
            return;
        }
        transform.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(to, Ms(ms)) { EasingFunction = Out });
    }

    /// <summary>Toast leave: same edge it entered, then the caller removes the card.</summary>
    public static DoubleAnimation ToastOut(TranslateTransform shift)
    {
        shift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(8, Ms(160)) { EasingFunction = In });
        return new DoubleAnimation(0, Ms(160)) { EasingFunction = In };
    }

    static Duration Ms(int value) => new(TimeSpan.FromMilliseconds(MsValue(value)));

    static int MsValue(int value) => Math.Max(1, (int)Math.Round(value * _speed));

    /// <summary>Animate a determinate meter. PAD has real step ratios during launch.</summary>
    public static void MeterTo(ProgressBar bar, double to, int ms = 320)
    {
        to = Math.Clamp(to, bar.Minimum, bar.Maximum);
        if (!Animated)
        {
            bar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
            bar.Value = to;
            return;
        }
        bar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,
            new DoubleAnimation(to, Ms(ms)) { EasingFunction = Out });
    }

    /// <summary>The launch card arriving: fade + a short lift. pop=true for overlay cards.</summary>
    public static void PulseIn(UIElement element, bool pop = false)
    {
        if (element is FrameworkElement fe)
            fe.RenderTransformOrigin = new Point(0.5, 0.5);
        if (!Animated)
        {
            element.Opacity = 1;
            return;
        }
        element.Opacity = 0;
        var rise = pop ? 22.0 : 12.0;
        if (pop)
        {
            var scale = new ScaleTransform(0.94, 0.94);
            var shift = new TranslateTransform { Y = rise };
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(shift);
            element.RenderTransform = group;
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(280))
            {
                EasingFunction = Out,
            });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, Ms(340))
            {
                EasingFunction = Out,
            });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1, Ms(340))
            {
                EasingFunction = Out,
            });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(rise, 0, Ms(380))
            {
                EasingFunction = Out,
            });
            return;
        }
        var only = new TranslateTransform { Y = rise };
        element.RenderTransform = only;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(260))
        {
            EasingFunction = Out,
        });
        only.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(rise, 0, Ms(340))
        {
            EasingFunction = Out,
        });
    }
}
