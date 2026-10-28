using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pad.Core;
using IOPath = System.IO.Path;

namespace Pad.Views;

/// <summary>
/// 项目侧栏（D35）：容器在舞台层（MainWindow 持有），页面按自己的视角填充——
/// 管理页灌项目视角（实例清单），关系页灌工作区视角（层级树）。
/// 行语言统一：logo 芯片 + 名称 + 状态点 + 展开箭头。
/// 视角切换不重建：同 key 的行（工作区/实例）从旧位置滑到新位置（FLIP），新行淡入。
/// </summary>
public partial class ProjectRail : UserControl
{
    string _selected = "";
    string? _focusKey;

    public event Action<Instance>? Picked;

    /// <summary>收起请求：侧栏是舞台的，收起/展开由宿主（MainWindow）动画处理。</summary>
    public event Action? CollapseClicked;

    public ProjectRail()
    {
        InitializeComponent();
    }

    public string Selected => _selected;

    /// <summary>收起按钮平时不显示——只有把侧栏放到舞台上的页面才需要。</summary>
    public bool ShowCollapse
    {
        get => CollapseBtn.Visibility == Visibility.Visible;
        set => CollapseBtn.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    void Collapse_Click(object sender, RoutedEventArgs e) => CollapseClicked?.Invoke();

    // ------------------------------------------------------------------ 树形模式

    public sealed record Node(
        string Id, string Title, string Meta, int Kind, bool Live,
        bool Open, int Kids, string Key = "", Instance? Inst = null);

    public event Action<Node>? Toggled;
    public event Action<Node>? Activated;

    /// <summary>
    /// 树形填充：工作区 → 实例 → agent。行右侧有 ▸/▾ 那是唯一的结构操作；
    /// 点行本身是"选中/聚焦"。图里不再需要点徽标（D46）。
    /// </summary>
    public void FillTree(IReadOnlyList<Node> nodes)
    {
        Summary.Text = "展开/收起在侧栏完成";
        var items = new List<(string, FrameworkElement)>();
        foreach (var node in nodes)
            items.Add((node.Key.Length > 0 ? node.Key : node.Id, TreeRow(node)));
        ReplaceRows(items);
        ApplyFocus();
    }

    /// <summary>聚焦高亮：宿主在行点击后设置 key，行底色就地变化，不重建列表。</summary>
    public string? FocusKey
    {
        get => _focusKey;
        set { _focusKey = value; ApplyFocus(); }
    }

    void ApplyFocus()
    {
        foreach (var it in Rows.Items.OfType<Border>())
            it.Background = it.Tag as string == _focusKey
                ? (Brush)Application.Current.Resources["AccentWash"]
                : Brushes.Transparent;
    }

    Border TreeRow(Node node)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal };

        // 展开箭头：结构操作的唯一入口
        var arrow = new TextBlock
        {
            Text = node.Kids == 0 ? "" : node.Open ? "▾" : "▸",
            Width = 14,
            FontSize = 10,
            Margin = new Thickness(0, 2, 4, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Foreground = node.Kids == 0
                ? Brushes.Transparent
                : (Brush)Application.Current.Resources["TextMuted"],
            Cursor = node.Kids == 0
                ? Cursors.Arrow
                : Cursors.Hand,
        };
        if (node.Kids > 0)
        {
            arrow.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                Toggled?.Invoke(node);
            };
        }
        stack.Children.Add(arrow);

        // 实例行带 logo 芯片：和列表模式同一枚（FLIP 时元素连续）
        if (node.Kind == 1 && node.Inst is not null)
            stack.Children.Add(Chip(node.Inst, 24));

        var body = new StackPanel { Margin = new Thickness(node.Kind == 1 ? 8 : 0, 0, 0, 0) };
        body.Children.Add(new TextBlock
        {
            Text = node.Title,
            FontSize = node.Kind == 0 ? 12 : node.Kind == 1 ? 11.5 : 11,
            FontWeight = node.Kind == 0 ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = (Brush)Application.Current.Resources[node.Kind == 2 ? "TextBody" : "TextPrimary"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        body.Children.Add(new TextBlock
        {
            Text = node.Meta,
            FontSize = 9.5,
            Margin = new Thickness(0, 1, 0, 0),
            Foreground = node.Live
                ? (Brush)Application.Current.Resources["Accent"]
                : (Brush)Application.Current.Resources["TextMuted"],
        });
        stack.Children.Add(body);

        // 状态：只在在跑时亮呼吸灯；没在跑就不画点（灰点没有信息）
        if (node.Kind == 1)
        {
            stack.Children.Add(new RunLamp
            {
                Width = 12,
                Height = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Running = node.Live,
            });
        }

        var row = new Border
        {
            Child = stack,
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(node.Kind * 12, 0, 0, 1),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = node.Kind == 0 ? null : "点这一行 = 只看这一支",
        };
        // 行体 = 聚焦（看这一支）；箭头 = 展开（改结构）。两个动作不混。
        row.MouseLeftButtonUp += (_, _) => Activated?.Invoke(node);
        row.MouseEnter += (_, _) =>
            row.Background = (Brush)Application.Current.Resources["AccentWash"];
        row.MouseLeave += (_, _) =>
            row.Background = node.Key == _focusKey
                ? (Brush)Application.Current.Resources["AccentWash"]
                : Brushes.Transparent;
        Press(row);
        return row;
    }

    // ------------------------------------------------------------------ 列表模式

    public void Fill(IReadOnlyList<Instance> insts, string? selected)
    {
        _selected = selected ?? _selected;
        Summary.Text = $"{insts.Count} 个实例";

        // 同工作区的实例归一组：这是"同目录不同源"在侧栏里的表达
        static string Dir(Instance i) => (i.Workspace?.Path ?? "").TrimEnd('\\', '/').ToLowerInvariant();
        var groups = insts
            .Select((inst, idx) => (inst, idx))
            .GroupBy(p => Dir(p.inst))
            .Select(g => g.OrderBy(p => p.idx).ToList())
            .OrderBy(g => g[0].idx);

        var items = new List<(string, FrameworkElement)>();
        foreach (var group in groups)
        {
            var shared = group.Count > 1 && Dir(group[0].inst).Length > 0;
            if (shared)
                items.Add(("ws:" + Dir(group[0].inst), GroupHeader(Dir(group[0].inst), group.Count)));

            foreach (var (inst, _) in group)
                items.Add(("inst:" + inst.Id, Row(inst, shared ? 10 : 0)));
        }
        ReplaceRows(items);
    }

    Border GroupHeader(string dir, int count)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = Short(dir),
            FontSize = 10,
            Foreground = (Brush)Application.Current.Resources["TextMuted"],
        });
        row.Children.Add(new TextBlock
        {
            Text = count.ToString(),
            FontSize = 10,
            Margin = new Thickness(6, 0, 0, 0),
            Foreground = (Brush)Application.Current.Resources["TextMuted"],
        });

        return new Border
        {
            Child = row,
            Padding = new Thickness(10, 8, 10, 2),
            Background = Brushes.Transparent,
            ToolTip = dir,
        };
    }

    Border Row(Instance inst, double indent)
    {
        var selected = inst.Id == _selected;
        var live = Nodes.Ledger.Load(inst.Home).Any(e => Proc.Alive(e.Pid));

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Chip(inst));
        row.Children.Add(new TextBlock
        {
            Text = inst.Name,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        });
        row.Children.Add(new RunLamp
        {
            Width = 12,
            Height = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Running = live,
        });

        var item = new Border
        {
            Child = row,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(indent, 0, 0, 2),
            CornerRadius = new CornerRadius(9),
            Background = selected
                ? (Brush)Application.Current.Resources["AccentWash"]
                : Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = inst.Home,
        };
        item.HorizontalAlignment = HorizontalAlignment.Stretch;
        item.MouseLeftButtonUp += (_, _) =>
        {
            _selected = inst.Id;
            Fill(AppState.Current.Launcher.Instances(), _selected);
            Picked?.Invoke(inst);
        };
        item.MouseEnter += (_, _) =>
            item.Background = (Brush)Application.Current.Resources["AccentWash"];
        item.MouseLeave += (_, _) =>
            item.Background = inst.Id == _selected
                ? (Brush)Application.Current.Resources["AccentWash"]
                : Brushes.Transparent;
        Press(item);
        return item;
    }

    static string Short(string dir)
    {
        var name = IOPath.GetFileName(dir);
        if (name is "home" or "dsh-home" or "harness" or "workspace")
        {
            var parent = IOPath.GetFileName(IOPath.GetDirectoryName(dir) ?? "");
            if (!string.IsNullOrWhiteSpace(parent)) return parent + "/" + name;
        }
        return string.IsNullOrWhiteSpace(name) ? dir : name;
    }

    // ------------------------------------------------------------------ 行元素

    /// <summary>
    /// 行徽标：和卡片页/关系图同一枚——整合包图 → 载体图标 → DSH 鲸鱼。
    /// 项目 logo（可能是字母）不再进候选（D38）；不会出现空芯片。
    /// </summary>
    static FrameworkElement Chip(Instance inst, double size = 26) =>
        new InstMark
        {
            Width = size,
            Height = size,
            VerticalAlignment = VerticalAlignment.Center,
            DataContext = inst,
        };

    /// <summary>按压反馈：按下变虚，松开/移开恢复。</summary>
    static void Press(FrameworkElement row)
    {
        row.MouseLeftButtonDown += (_, _) => row.Opacity = 0.7;
        row.MouseLeftButtonUp += (_, _) => row.Opacity = 1;
        row.MouseLeave += (_, _) => row.Opacity = 1;
    }


    // ------------------------------------------------------------------ 原地重组（FLIP）

    /// <summary>
    /// 侧栏的核心动画：换内容前记下每行（按 key）的位置，换上后让同 key 的行
    /// 从旧位置滑到新位置，新行错峰淡入。列表 ↔ 树、树内展开/收起都走这一条路——
    /// 视觉上是同一批元素在重新排位，容器（分组/层级）在它们周围变化。
    /// </summary>
    void ReplaceRows(IReadOnlyList<(string Key, FrameworkElement El)> items)
    {
        var before = new Dictionary<string, Point>();
        foreach (var it in Rows.Items.OfType<FrameworkElement>())
        {
            if (it.Tag is not string k || k.Length == 0 || before.ContainsKey(k)) continue;
            try { before[k] = it.TransformToVisual(Rows).Transform(new Point(0, 0)); }
            catch { /* 尚未渲染，跳过 */ }
        }

        Rows.Items.Clear();
        foreach (var (key, el) in items)
        {
            el.Tag = key;
            Rows.Items.Add(el);
        }
        Rows.UpdateLayout();

        if (!Fx.Animated) return;
        var i = 0;
        foreach (var (key, el) in items)
        {
            var delay = TimeSpan.FromMilliseconds(Math.Min(i * 14, 140));
            i++;

            if (before.TryGetValue(key, out var from))
            {
                Point to;
                try { to = el.TransformToVisual(Rows).Transform(new Point(0, 0)); }
                catch { continue; }
                var dx = from.X - to.X;
                var dy = from.Y - to.Y;
                if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) continue;

                var tf = new TranslateTransform(dx, dy);
                el.RenderTransform = tf;
                tf.BeginAnimation(TranslateTransform.XProperty, Fx.Move(dx, 0, 340, delay));
                tf.BeginAnimation(TranslateTransform.YProperty, Fx.Move(dy, 0, 340, delay));
                el.Opacity = 0.6;
                el.BeginAnimation(OpacityProperty, Fx.Fade(0.6, 1, 280, delay));
            }
            else
            {
                el.Opacity = 0;
                el.BeginAnimation(OpacityProperty, Fx.Fade(0, 1, 240, delay));
                var shift = new TranslateTransform { Y = 7 };
                el.RenderTransform = shift;
                shift.BeginAnimation(TranslateTransform.YProperty, Fx.Move(7, 0, 300, delay));
            }
        }
    }
}
