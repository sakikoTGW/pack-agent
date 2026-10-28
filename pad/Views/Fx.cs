using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Pad.Views;

/// <summary>
/// Motion for what a style trigger cannot express: page entrances, staggered lists, panes
/// and overlays. Two rules hold everywhere in this file:
///
/// 1. Only opacity and a Transform move. Nothing here animates layout, so no frame inside
///    a motion reflows the window.
/// 2. Entrances settle like a critically damped spring: fast start, long tail, no
///    overshoot. Bounce is reserved for things a gesture threw, and nothing in PAD's
///    chrome qualifies — see .agents/skills/emotion-design.
/// </summary>
public static class Fx
{
    static bool? _forced;
    static double _speed = 1;

    /// <summary>
    /// Off when Windows says so, or when the user flipped 个性化 → 动画. A forced false
    /// always wins; a forced true still respects the OS off-switch.
    /// </summary>
    public static bool Animated =>
        SystemParameters.ClientAreaAnimation && (_forced ?? true);

    /// <summary>
    /// Windows reduced motion but the user did not switch animation off: travel is dropped,
    /// comprehension-grade cross-fades stay (Apple: gentler, not nothing).
    /// </summary>
    static bool Reduced => !SystemParameters.ClientAreaAnimation && (_forced ?? true);

    public static void SetAnimate(bool on) => _forced = on;

    /// <summary>1 = default. Above 1 stretches durations (slower), below 1 shortens them.</summary>
    public static void SetSpeed(double speed) => _speed = Math.Clamp(speed, 0.5, 2.0);

    static long _busyUntil;

    /// <summary>
    /// True while a page swap is animating. Background refreshes (process table, disk
    /// scans) check this and skip a beat instead of dropping frames inside the motion.
    /// </summary>
    public static bool Busy => Environment.TickCount64 < _busyUntil;

    public static void BusyFor(int ms) => _busyUntil = Environment.TickCount64 + Math.Max(0, ms);

    // WPF has no spring easing. A steep exponential PowerEase is the closest stand-in for a
    // critically damped settle: the same immediate departure from rest, the same long tail
    // into the target, and no overshoot. The cubic built-ins start too gently to read as
    // "direct" — that is the difference between an interface that responds and one that
    // merely finishes.
    static readonly PowerEase Settle = new() { Power = 4.6, EasingMode = EasingMode.EaseOut };
    static readonly PowerEase Exit = new() { Power = 2.4, EasingMode = EasingMode.EaseIn };
    static readonly PowerEase FadeEase = new() { Power = 2.4, EasingMode = EasingMode.EaseOut };

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
        if (!Animated)
        {
            CrossFade(fe);
            return;
        }

        var rise = GetEnter(fe) == "card" ? 10.0 : 8.0;
        // Hold the start pose before the stagger delay. BeginTime alone leaves Opacity at 1
        // until the clock starts, so the row flashes fully opaque.
        fe.Opacity = 0;
        var shift = new TranslateTransform { Y = rise };
        fe.RenderTransform = shift;

        // 30ms of stagger between rows, capped at 210ms: enough to read as a cascade,
        // short enough that the last row is not waiting a third of a second.
        var delay = TimeSpan.FromMilliseconds(Math.Min(IndexOf(fe) * MsValue(30), MsValue(210)));
        fe.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(180))
        {
            BeginTime = delay,
            EasingFunction = FadeEase,
        });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(rise, 0, Ms(300))
        {
            BeginTime = delay,
            EasingFunction = Settle,
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

    // ------------------------------------------------------------------ page switch

    /// <summary>
    /// Entrance choreography for a whole page. The container is visible at once — a page
    /// switch must never leave a blank rectangle — and the motion belongs to its parts.
    /// Every page picks a different shape, so switching never feels templated.
    /// </summary>
    public static void EnterPage(UIElement page, PageMotion motion)
    {
        page.BeginAnimation(UIElement.OpacityProperty, null);
        page.Opacity = 1;
        page.RenderTransform = Transform.Identity;
        if (!Animated || page is not DependencyObject root) return;

        var targets = StaggerTargets(root);
        if (motion == PageMotion.Wave) OrderByPosition(page, targets);
        for (var i = 0; i < targets.Count; i++)
            Pose(targets[i], motion, i);
    }

    /// <summary>Tagged elements (Fx.Enter) first; a page without tags gets its sections.</summary>
    static List<FrameworkElement> StaggerTargets(DependencyObject root)
    {
        var tagged = new List<FrameworkElement>();
        CollectTagged(root, 0, tagged);
        if (tagged.Count > 0) return tagged;

        var content = Descend(root, 0);
        var sections = new List<FrameworkElement>();
        if (content is null) return sections;
        VisibleChildren(content, sections);
        // A rail + one pane would stagger two elements and read as nothing. Open the wrappers
        // a couple of levels so the parts the user actually recognises are what arrives.
        for (var pass = 0; pass < 3 && sections.Count is > 0 and < 4; pass++)
            sections = Expand(sections);
        if (sections.Count > 14) sections.RemoveRange(14, sections.Count - 14);
        return sections;
    }

    static void VisibleChildren(DependencyObject node, List<FrameworkElement> into)
    {
        foreach (var child in Children(node))
        {
            if (child is FrameworkElement fe && fe.Visibility == Visibility.Visible && fe.ActualWidth > 1)
                into.Add(fe);
        }
    }

    static List<FrameworkElement> Expand(List<FrameworkElement> current)
    {
        var next = new List<FrameworkElement>();
        foreach (var fe in current)
        {
            var kids = new List<FrameworkElement>();
            VisibleChildren(fe, kids);
            if (kids.Count == 0) next.Add(fe);
            else next.AddRange(kids);
        }
        return next;
    }

    static void CollectTagged(DependencyObject node, int depth, List<FrameworkElement> into)
    {
        if (depth > 14 || into.Count >= 14) return;
        if (node is FrameworkElement fe && GetEnter(fe) is not null
            && fe.Visibility == Visibility.Visible && fe.ActualWidth > 1)
        {
            into.Add(fe);
            return;
        }
        foreach (var child in Children(node)) CollectTagged(child, depth + 1, into);
    }

    /// <summary>A wave travels across the screen, not through the tree.</summary>
    static void OrderByPosition(UIElement page, List<FrameworkElement> items)
    {
        var keyed = new List<(double Key, FrameworkElement Fe)>(items.Count);
        foreach (var fe in items)
        {
            var key = 0.0;
            try
            {
                var p = fe.TranslatePoint(new Point(0, 0), page);
                key = p.Y * 3 + p.X * 0.08;
            }
            catch { /* not measured yet: keep tree order */ }
            keyed.Add((key, fe));
        }
        keyed.Sort((a, b) => a.Key.CompareTo(b.Key));
        items.Clear();
        foreach (var (_, fe) in keyed) items.Add(fe);
    }

    /// <summary>Skip single-child wrappers (UserControl -> Border -> Grid) down to the layout root.</summary>
    static DependencyObject? Descend(DependencyObject node, int depth)
    {
        if (depth > 8) return null;
        var kids = Children(node);
        if (kids.Count > 1) return node;
        if (kids.Count == 0) return node;
        return Descend(kids[0], depth + 1);
    }

    static List<DependencyObject> Children(DependencyObject node)
    {
        var list = new List<DependencyObject>();
        var n = VisualTreeHelper.GetChildrenCount(node);
        if (n > 0)
        {
            for (var i = 0; i < n; i++) list.Add(VisualTreeHelper.GetChild(node, i));
            return list;
        }
        foreach (var child in LogicalTreeHelper.GetChildren(node))
            if (child is DependencyObject d) list.Add(d);
        return list;
    }

    /// <summary>
    /// One element's arrival. The shape of the motion — distance, scale, slide, stagger —
    /// is what makes 设置 read as one-by-one and 管理 read as a wave.
    /// </summary>
    static void Pose(FrameworkElement fe, PageMotion motion, int index)
    {
        double rise, scale, slide;
        int dur, fade;
        TimeSpan delay;
        switch (motion)
        {
            case PageMotion.Rise:
                (rise, scale, slide, dur, fade) = (14, 1, 0, 300, 180);
                delay = At(index, 36, 240);
                break;
            case PageMotion.Cards:
                (rise, scale, slide, dur, fade) = (8, 0.96, 0, 320, 180);
                delay = At(index, 30, 240);
                break;
            case PageMotion.Rows:
                (rise, scale, slide, dur, fade) = (6, 1, 0, 240, 140);
                delay = At(index, 16, 200);
                break;
            case PageMotion.Slide:
                (rise, scale, slide, dur, fade) = (4, 1, -12, 260, 170);
                delay = At(index, 24, 220);
                break;
            case PageMotion.Sequential:
                // Deliberately slow stagger: the page assembles itself, part by part.
                (rise, scale, slide, dur, fade) = (10, 1, 0, 280, 160);
                delay = At(index, 58, 520);
                break;
            case PageMotion.Wave:
                (rise, scale, slide, dur, fade) = (6, 0.97, 0, 300, 170);
                delay = At(index, 26, 300);
                break;
            case PageMotion.Combo:
                // Two beats at once: the lead block travels long and slow, the rest cascade
                // underneath it on their own offset.
                if (index == 0)
                {
                    (rise, scale, slide, dur, fade) = (20, 1, 0, 420, 220);
                    delay = TimeSpan.Zero;
                }
                else
                {
                    (rise, scale, slide, dur, fade) = (8, 0.96, 0, 300, 170);
                    delay = TimeSpan.FromMilliseconds(120 + Math.Min((index - 1) * 34, 220));
                }
                break;
            default: // Drift
                (rise, scale, slide, dur, fade) = (8, 0.985, 0, 300, 180);
                delay = At(index, 26, 240);
                break;
        }

        var group = new TransformGroup();
        var s = new ScaleTransform(scale, scale);
        var t = new TranslateTransform { X = slide, Y = rise };
        group.Children.Add(s);
        group.Children.Add(t);
        fe.RenderTransform = group;
        fe.RenderTransformOrigin = new Point(0.5, 0.5);
        fe.Opacity = 0;

        fe.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(fade))
        {
            BeginTime = delay,
            EasingFunction = FadeEase,
        });
        if (rise != 0)
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(rise, 0, Ms(dur))
            {
                BeginTime = delay,
                EasingFunction = Settle,
            });
        if (slide != 0)
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(slide, 0, Ms(dur))
            {
                BeginTime = delay,
                EasingFunction = Settle,
            });
        if (scale != 1)
        {
            s.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, 1, Ms(dur))
            {
                BeginTime = delay,
                EasingFunction = Settle,
            });
            s.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, 1, Ms(dur))
            {
                BeginTime = delay,
                EasingFunction = Settle,
            });
        }
    }

    static TimeSpan At(int index, int stagger, int cap) =>
        TimeSpan.FromMilliseconds(Math.Min(index * stagger, cap));

    /// <summary>
    /// The page swap for a pane inside one page. One composed entrance — fade, a 12px rise,
    /// a barely-there 0.988 scale — so the pane reads as a surface arriving.
    /// </summary>
    public static void PageIn(UIElement page)
    {
        if (!Animated)
        {
            CrossFade(page);
            return;
        }
        page.Opacity = 0;
        var shift = new TranslateTransform { Y = 12 };
        var scale = new ScaleTransform(0.988, 0.988);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(shift);
        page.RenderTransform = group;
        page.RenderTransformOrigin = new Point(0.5, 0.5);

        page.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(170))
        {
            EasingFunction = FadeEase,
        });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, Ms(320))
        {
            EasingFunction = Settle,
        });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.988, 1, Ms(320))
        {
            EasingFunction = Settle,
        });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.988, 1, Ms(320))
        {
            EasingFunction = Settle,
        });
    }

    /// <summary>Reduced motion: no travel, no scale, just a short comprehension-grade fade.</summary>
    static void CrossFade(UIElement element)
    {
        element.RenderTransform = Transform.Identity;
        if (!Reduced)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            return;
        }
        element.Opacity = 0;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(120))
        {
            EasingFunction = FadeEase,
        });
    }

    /// <summary>
    /// One beat of a staged window resize, driven per frame with DEVICE-PIXEL snapping.
    ///
    /// Animating Width/Height with a DoubleAnimation feeds fractions of a DIP into the
    /// window every frame. At 150% scaling that lands on half pixels, and with layout
    /// rounding on, every child snaps to a different pixel each frame — that is the
    /// twitching inside the window. Snapping the bounds to whole physical pixels per frame
    /// makes the content move by a stable integer step.
    ///
    /// Null bounds are left where they are, so the caller can widen first and drop the
    /// bottom edge second — two readable edges instead of one diagonal jump.
    /// </summary>
    public static void StepBounds(Window win, double? left, double? top, double? width, double? height,
        int ms, Action? done = null)
    {
        StopBounds();
        if (!Animated)
        {
            if (left is { } l0) win.Left = l0;
            if (top is { } t0) win.Top = t0;
            if (width is { } w0) win.Width = w0;
            if (height is { } h0) win.Height = h0;
            done?.Invoke();
            return;
        }
        _bounds = new BoundsDriver(win, left, top, width, height, ms, done);
        _bounds.Start();
    }

    /// <summary>Cancel any in-flight bounds travel (a settings apply, a rebuild).</summary>
    public static void StopBounds()
    {
        _bounds?.Stop();
        _bounds = null;
    }

    static BoundsDriver? _bounds;

    /// <summary>Frame-synced, pixel-snapped window bounds driver (one at a time).</summary>
    sealed class BoundsDriver
    {
        readonly Window _win;
        readonly double? _left, _top, _width, _height;
        readonly double _l0, _t0, _w0, _h0;
        readonly int _ms;
        readonly Action? _done;
        readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        bool _running;

        public BoundsDriver(Window win, double? left, double? top, double? width, double? height,
            int ms, Action? done)
        {
            _win = win;
            _left = left;
            _top = top;
            _width = width;
            _height = height;
            _ms = Math.Max(1, ms);
            _done = done;
            _l0 = win.Left;
            _t0 = win.Top;
            _w0 = win.Width;
            _h0 = win.Height;
        }

        public void Start()
        {
            _running = true;
            CompositionTarget.Rendering += OnFrame;
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            CompositionTarget.Rendering -= OnFrame;
        }

        void OnFrame(object? sender, EventArgs e)
        {
            if (!_running) return;
            var t = Math.Min(1.0, _clock.Elapsed.TotalMilliseconds / _ms);
            // Same exponential settle as Settle/PowerEase(4.6).
            var k = 1 - Math.Pow(1 - t, 4.6);
            var dpi = VisualTreeHelper.GetDpi(_win);
            var x = Snap(Lerp(_l0, _left ?? _l0, k), dpi.DpiScaleX);
            var y = Snap(Lerp(_t0, _top ?? _t0, k), dpi.DpiScaleY);
            var w = Snap(Lerp(_w0, _width ?? _w0, k), dpi.DpiScaleX);
            var h = Snap(Lerp(_h0, _height ?? _h0, k), dpi.DpiScaleY);
            if (w > 1) _win.Width = w;
            if (h > 1) _win.Height = h;
            _win.Left = x;
            _win.Top = y;

            if (t < 1) return;
            Stop();
            if (_width is { } fw) _win.Width = Snap(fw, dpi.DpiScaleX);
            if (_height is { } fh) _win.Height = Snap(fh, dpi.DpiScaleY);
            if (_left is { } fl) _win.Left = Snap(fl, dpi.DpiScaleX);
            if (_top is { } ft) _win.Top = Snap(ft, dpi.DpiScaleY);
            _done?.Invoke();
        }

        static double Lerp(double from, double to, double k) => from + (to - from) * k;

        /// <summary>Whole physical pixel, expressed back in DIPs.</summary>
        static double Snap(double dip, double scale) =>
            scale <= 0 ? dip : Math.Round(dip * scale) / scale;
    }

    /// <summary>
    /// The 1px edge ring cannot stay crisp while the window is between two sizes: the line
    /// lands on fractional pixels and blinks. Fade it out for the bounds travel and back in
    /// when they land, so the edge reads as part of the motion instead of a flicker.
    /// </summary>
    public static void Edge(FrameworkElement ring, bool visible, int ms = 130)
    {
        ring.BeginAnimation(UIElement.OpacityProperty, null);
        if (!Animated)
        {
            ring.Opacity = visible ? 1 : 0;
            return;
        }
        ring.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(visible ? 1 : 0, Ms(ms))
        {
            EasingFunction = visible ? FadeEase : Exit,
        });
    }

    /// <summary>Crossfade between the two title-bar modes.</summary>
    public static void Reveal(UIElement element)
    {
        if (!Animated) { CrossFade(element); return; }
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(150))
        {
            EasingFunction = Settle,
        });
    }

    /// <summary>
    /// 内容在原位重组：不挪位置、不闪白，新排布从半透明轻轻落定。
    /// 整块换内容的容器用这个；要「同一批元素换位」用 Relayout 思路（见 ProjectRail）。
    /// </summary>
    public static void Morph(UIElement element)
    {
        if (!Animated) { CrossFade(element); return; }
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.4, 1, Ms(200))
        {
            EasingFunction = Settle,
        });
    }

    /// <summary>位移动画：FLIP 重组用。begin 用来做错峰。</summary>
    public static DoubleAnimation Move(double from, double to, int ms = 300, TimeSpan? begin = null)
    {
        var a = new DoubleAnimation(from, to, Ms(ms)) { EasingFunction = Settle };
        if (begin is not null) a.BeginTime = begin;
        return a;
    }

    /// <summary>透明度动画：错峰淡入用。</summary>
    public static DoubleAnimation Fade(double from, double to, int ms = 240, TimeSpan? begin = null)
    {
        var a = new DoubleAnimation(from, to, Ms(ms)) { EasingFunction = Settle };
        if (begin is not null) a.BeginTime = begin;
        return a;
    }

    /// <summary>
    /// 舞台元素的宽度：收起/展开走动画，旁边的内容跟着连续重排——
    /// 「元素在舞台上重新组成」靠的就是这种连续性，而不是跳变。
    /// 只在人手动收放侧栏时用；换页时宽度必须瞬间到位（见 MainWindow.ShowRail）。
    /// </summary>
    public static void WidthTo(FrameworkElement element, double to, int ms = 260)
    {
        if (!Animated)
        {
            element.BeginAnimation(FrameworkElement.WidthProperty, null);
            element.Width = to;
            return;
        }
        element.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(to, Ms(ms))
        {
            EasingFunction = Settle,
        });
    }

    /// <summary>
    /// A To-only animation: WPF starts it from the live presentation value, so a second
    /// click retargets the pill mid-flight instead of jumping to a fixed origin.
    /// </summary>
    public static void Slide(TranslateTransform transform, double to, int ms = 240)
    {
        if (!Animated)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = to;
            return;
        }
        transform.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(to, Ms(ms)) { EasingFunction = Settle });
    }

    /// <summary>Toast leave: same edge it entered, then the caller removes the card.</summary>
    public static DoubleAnimation ToastOut(TranslateTransform shift)
    {
        shift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(8, Ms(160)) { EasingFunction = Exit });
        return new DoubleAnimation(0, Ms(160)) { EasingFunction = Exit };
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
            new DoubleAnimation(to, Ms(ms)) { EasingFunction = Settle });
    }

    /// <summary>The launch card arriving: fade + a short lift. pop=true for overlay cards.</summary>
    public static void PulseIn(UIElement element, bool pop = false)
    {
        if (element is FrameworkElement fe)
            fe.RenderTransformOrigin = new Point(0.5, 0.5);
        if (!Animated)
        {
            CrossFade(element);
            return;
        }
        element.Opacity = 0;
        var rise = pop ? 22.0 : 12.0;
        if (pop)
        {
            // Overlay cards are centred, not anchored to a trigger — Apple exempts modals
            // from origin-awareness. 0.94 is the floor: nothing appears from nothing.
            var scale = new ScaleTransform(0.94, 0.94);
            var shift = new TranslateTransform { Y = rise };
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(shift);
            element.RenderTransform = group;
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(200))
            {
                EasingFunction = FadeEase,
            });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, Ms(320))
            {
                EasingFunction = Settle,
            });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1, Ms(320))
            {
                EasingFunction = Settle,
            });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(rise, 0, Ms(360))
            {
                EasingFunction = Settle,
            });
            return;
        }
        var only = new TranslateTransform { Y = rise };
        element.RenderTransform = only;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(180))
        {
            EasingFunction = FadeEase,
        });
        only.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(rise, 0, Ms(340))
        {
            EasingFunction = Settle,
        });
    }
}

/// <summary>How a page's parts arrive. One per page, so a switch never feels templated.</summary>
public enum PageMotion
{
    /// <summary>Long rise from below — the launch page's single hero gesture.</summary>
    Rise,

    /// <summary>Scale + lift — card grids.</summary>
    Cards,

    /// <summary>Tight rise — dense form rows.</summary>
    Rows,

    /// <summary>Slide in from the left — list and tree pages.</summary>
    Slide,

    /// <summary>Gentle drift — pages built around a large canvas.</summary>
    Drift,

    /// <summary>One after another with a deliberate gap: the page assembles itself.</summary>
    Sequential,

    /// <summary>A wave across the layout, ordered by screen position rather than tree order.</summary>
    Wave,

    /// <summary>Two beats at once: the lead block travels slowly while the rest cascade under it.</summary>
    Combo,
}
