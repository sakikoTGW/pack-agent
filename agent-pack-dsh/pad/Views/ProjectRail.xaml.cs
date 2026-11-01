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

    /// <summary>在跑/启动中的行：行尾灯 + 呼吸底色。按实例 id 记着，状态变了就地改，不重建列表。</summary>
    readonly Dictionary<string, LiveRow> _live = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>一行里跟状态有关的两件东西。</summary>
    sealed class LiveRow(RunLamp lamp, Border wash)
    {
        public RunLamp Lamp { get; } = lamp;
        public Border Wash { get; } = wash;
        public RunPhase? Phase { get; set; }
    }

    public event Action<Instance>? Picked;

    /// <summary>项目视角（D66）：点项目行，宿主用 id 换右栏（项目页）。</summary>
    public event Action<string>? ProjectPicked;

    /// <summary>侧栏项目行要的数据：由宿主算好（它知道怎么把项目映射到实例/状态）。</summary>
    public sealed record ProjectRowData(string Id, string Name, string Meta, bool Live, string Right = "");
    List<ProjectRowData> _projectRows = [];
    bool _projectMode;

    /// <summary>D69（对齐原型 v4）：项目 / 实例 两条表示 + 底部收编入口。</summary>
    public event Action<bool>? SegmentChanged;
    public event Action? AdoptClicked;
    public event Action? ImportClicked;
    bool _segInst;

    /// <summary>管理中枢模式：显示 项目/实例 分段和底部两个入口。</summary>
    public void ShowHub(bool on, bool instances)
    {
        _segInst = instances;
        SegPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        FootPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        RailTitle.Text = instances ? "实例" : "项目";
        SegProj.Style = (Style)Application.Current.Resources[instances ? "BtnSm" : "BtnPrimary"];
        SegInst.Style = (Style)Application.Current.Resources[instances ? "BtnPrimary" : "BtnSm"];
        SearchHint.Text = instances ? "搜索实例 / home" : "搜索项目 / 目录";
        SearchBox.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        SearchHint.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    void SegProj_Click(object sender, RoutedEventArgs e)
    {
        if (!_segInst) return;
        _segInst = false;
        ShowHub(true, false);
        SegmentChanged?.Invoke(false);
    }

    void SegInst_Click(object sender, RoutedEventArgs e)
    {
        if (_segInst) return;
        _segInst = true;
        ShowHub(true, true);
        SegmentChanged?.Invoke(true);
    }

    void Adopt_Click(object sender, RoutedEventArgs e) => AdoptClicked?.Invoke();
    void Import_Click(object sender, RoutedEventArgs e) => ImportClicked?.Invoke();
    string _filter = "";

    /// <summary>点了固定的第一行「主页」：右栏回到本机概览（D54）。</summary>
    public event Action? HomePicked;

    /// <summary>收起请求：侧栏是舞台的，收起/展开由宿主（MainWindow）动画处理。</summary>
    public event Action? CollapseClicked;

    public ProjectRail()
    {
        InitializeComponent();
        // 灯是活的，不是一次性快照：AppState 每 2s 换一次状态快照，这里只改灯和行的呼吸（D53）。
        AppState.Current.StatusChanged += ApplyStatus;
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
        _projectMode = false;
        ShowHub(false, false);
        Summary.Text = "展开/收起在侧栏完成";
        _live.Clear();
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
        _projectMode = false;
        ShowHub(false, false);
        _selected = selected ?? _selected;
        Summary.Text = $"{insts.Count} 个实例";
        _live.Clear();

        // 同工作区的实例归一组：这是"同目录不同源"在侧栏里的表达
        static string Dir(Instance i) => (i.Workspace?.Path ?? "").TrimEnd('\\', '/').ToLowerInvariant();
        var groups = insts
            .Select((inst, idx) => (inst, idx))
            .GroupBy(p => Dir(p.inst))
            .Select(g => g.OrderBy(p => p.idx).ToList())
            .OrderBy(g => g[0].idx);

        // 第一行永远是主页：右栏看的是「本机」还是「某个项目」，由这一行和项目行共同决定。
        var items = new List<(string, FrameworkElement)> { ("home", HomeRow()) };
        foreach (var group in groups)
        {
            var shared = group.Count > 1 && Dir(group[0].inst).Length > 0;
            if (shared)
                items.Add(("ws:" + Dir(group[0].inst), GroupHeader(Dir(group[0].inst), group.Count)));

            foreach (var (inst, _) in group)
                items.Add(("inst:" + inst.Id, Row(inst, shared ? 10 : 0)));
        }
        ReplaceRows(items);
        // 行建好就把当前状态贴上；之后只随 StatusChanged 就地改灯，不再重建（FLIP 不跳）。
        ApplyStatus();
    }

    /// <summary>
    /// 项目视角（D66）：主页/项目页的侧栏。按「在跑 / 已停」分组，行 = 项目名 + 工作区/preset
    /// 事实 + 运行灯；顶部搜索框按名字过滤。行数据由宿主算（项目 → 实例 → 状态）。
    /// </summary>
    public void FillProjects(IReadOnlyList<ProjectRowData> rows, string? selected)
    {
        _projectMode = true;
        ShowHub(true, false);
        _projectRows = [.. rows];
        _selected = selected ?? _selected;
        SearchBox.Visibility = Visibility.Visible;
        SearchHint.Visibility = Visibility.Visible;
        Summary.Text = $"{rows.Count} 个项目 · {rows.Count(r => r.Live)} 个在跑";
        _live.Clear();

        var items = new List<(string, FrameworkElement)> { ("home", HomeRow()) };
        var filtered = rows
            .Where(r => _filter.Length == 0
                        || r.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                        || r.Id.Contains(_filter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Live)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var live = filtered.Where(r => r.Live).ToList();
        var stopped = filtered.Where(r => !r.Live).ToList();
        if (live.Count > 0)
        {
            items.Add(("ph:live", GroupLabel("在跑")));
            foreach (var r in live) items.Add(("proj:" + r.Id, ProjectRow(r)));
        }
        if (stopped.Count > 0)
        {
            items.Add(("ph:stop", GroupLabel("已停")));
            foreach (var r in stopped) items.Add(("proj:" + r.Id, ProjectRow(r)));
        }
        if (filtered.Count == 0)
            items.Add(("ph:none", GroupLabel("没有匹配的项目")));
        ReplaceRows(items);
    }

    void Search_Changed(object sender, TextChangedEventArgs e)
    {
        _filter = SearchBox.Text.Trim();
        if (_instRowsV4 is not null) FillInstancesV4(_instRowsV4, _selected);
        else if (_projectMode) FillProjects(_projectRows, _selected);
    }

    // ------------------------------------------------------------------ 实例视角（D69）

    List<Instance>? _instRowsV4;

    /// <summary>实例视角：运行体清单（对齐原型 v4）——名字 + home + 在跑/已停。</summary>
    public void FillInstancesV4(IReadOnlyList<Instance> insts, string? selected)
    {
        _projectMode = false;
        _instRowsV4 = [.. insts];
        ShowHub(true, true);
        _selected = selected ?? _selected;
        Summary.Text = $"{insts.Count} 个实例 · {insts.Count(i => AppState.Current.StatusOf(i.Id).Live)} 个在跑";
        _live.Clear();

        var items = new List<(string, FrameworkElement)> { ("home", HomeRow()) };
        items.Add(("ph:inst", GroupLabel("实例（运行体）")));
        foreach (var inst in insts
                     .Where(i => _filter.Length == 0
                                 || i.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                                 || i.Home.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(i => AppState.Current.StatusOf(i.Id).Live)
                     .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            items.Add(("inst:" + inst.Id, InstRowV4(inst)));
        if (items.Count == 2) items.Add(("ph:none", GroupLabel("没有匹配的实例")));
        ReplaceRows(items);
        ApplyStatus();
    }

    Border InstRowV4(Instance inst)
    {
        var selected = inst.Id == _selected;
        var phase = AppState.Current.StatusOf(inst.Id);
        var chip = Chip(inst, 26);
        Grid.SetColumn(chip, 0);

        var body = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(new TextBlock
        {
            Text = inst.Name,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        body.Children.Add(new TextBlock
        {
            Text = ShortHome(inst.Home),
            FontSize = 9.5,
            Margin = new Thickness(0, 1, 0, 0),
            Foreground = (Brush)Application.Current.Resources["TextMuted"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(body, 1);

        var state = new TextBlock
        {
            Text = phase.Live ? "在跑" : "已停",
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = phase.Live
                ? (Brush)Application.Current.Resources["Accent"]
                : (Brush)Application.Current.Resources["TextMuted"],
        };
        Grid.SetColumn(state, 2);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(chip);
        content.Children.Add(body);
        content.Children.Add(state);

        var item = new Border
        {
            Child = content,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 2),
            CornerRadius = new CornerRadius(9),
            Background = selected
                ? (Brush)Application.Current.Resources["AccentWash"]
                : Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = inst.Home,
        };
        item.MouseLeftButtonUp += (_, _) =>
        {
            _selected = inst.Id;
            FillInstancesV4(_instRowsV4 ?? [inst], _selected);
            Picked?.Invoke(inst);
        };
        item.MouseEnter += (_, _) => item.Background = (Brush)Application.Current.Resources["AccentWash"];
        item.MouseLeave += (_, _) => item.Background = inst.Id == _selected
            ? (Brush)Application.Current.Resources["AccentWash"]
            : Brushes.Transparent;
        Press(item);
        return item;
    }

    static string ShortHome(string home)
    {
        var norm = (home ?? "").TrimEnd('\\', '/');
        var idx = norm.IndexOf("instances\\", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) return "…\\" + norm[idx..];
        var leaf = IOPath.GetFileName(norm);
        return leaf.Length == 0 ? norm : leaf;
    }

    Border GroupLabel(string text) => new()
    {
        Child = new TextBlock
        {
            Text = text,
            FontSize = 10,
            Foreground = (Brush)Application.Current.Resources["TextMuted"],
        },
        Padding = new Thickness(10, 10, 10, 2),
        Background = Brushes.Transparent,
    };

    Border ProjectRow(ProjectRowData data)
    {
        var selected = data.Id == _selected;
        var wash = new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = (Brush)Application.Current.Resources["AccentWash"],
            Opacity = 0,
            IsHitTestVisible = false,
        };

        var chip = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = VerticalAlignment.Center,
            Background = selected
                ? (Brush)Application.Current.Resources["Accent"]
                : (Brush)Application.Current.Resources["AccentWash"],
            Child = new Ico
            {
                Glyph = (Geometry)Application.Current.Resources["I.Data"],
                Width = 13,
                Height = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = selected
                    ? (Brush)Application.Current.Resources["TextOnAccent"]
                    : (Brush)Application.Current.Resources["Accent"],
            },
        };
        Grid.SetColumn(chip, 0);

        var body = new StackPanel
        {
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        body.Children.Add(new TextBlock
        {
            Text = data.Name,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        body.Children.Add(new TextBlock
        {
            Text = data.Meta,
            FontSize = 9.5,
            Margin = new Thickness(0, 1, 0, 0),
            Foreground = data.Live
                ? (Brush)Application.Current.Resources["Accent"]
                : (Brush)Application.Current.Resources["TextMuted"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(body, 1);

        var right = new TextBlock
        {
            Text = data.Right,
            FontSize = 9.5,
            Foreground = (Brush)Application.Current.Resources["TextMuted"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        Grid.SetColumn(right, 2);

        var lamp = new RunLamp
        {
            Width = 12,
            Height = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Running = data.Live,
        };
        Grid.SetColumn(lamp, 3);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(chip);
        content.Children.Add(body);
        content.Children.Add(right);
        content.Children.Add(lamp);

        var layer = new Grid();
        layer.Children.Add(wash);
        layer.Children.Add(content);

        var item = new Border
        {
            Child = layer,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 2),
            CornerRadius = new CornerRadius(9),
            Background = selected
                ? (Brush)Application.Current.Resources["AccentWash"]
                : Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = data.Meta,
        };
        item.HorizontalAlignment = HorizontalAlignment.Stretch;
        item.MouseLeftButtonUp += (_, _) =>
        {
            _selected = data.Id;
            FillProjects(_projectRows, _selected);
            ProjectPicked?.Invoke(data.Id);
        };
        item.MouseEnter += (_, _) =>
            item.Background = (Brush)Application.Current.Resources["AccentWash"];
        item.MouseLeave += (_, _) =>
            item.Background = data.Id == _selected
                ? (Brush)Application.Current.Resources["AccentWash"]
                : Brushes.Transparent;
        Press(item);
        return item;
    }

    /// <summary>
    /// 固定的主页行（D54）：管理页的右栏默认是「本机」概览，它不属于任何实例，
    /// 但必须能选中 / 能回来——否则切到项目页就没有出口。
    /// </summary>
    Border HomeRow()
    {
        var selected = _selected.Length == 0;
        var row = new StackPanel { Orientation = Orientation.Horizontal };

        var chip = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = VerticalAlignment.Center,
            Background = selected
                ? (Brush)Application.Current.Resources["Accent"]
                : (Brush)Application.Current.Resources["AccentWash"],
            Child = new Ico
            {
                Glyph = (Geometry)Application.Current.Resources["I.Data"],
                Width = 14,
                Height = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = selected
                    ? (Brush)Application.Current.Resources["TextOnAccent"]
                    : (Brush)Application.Current.Resources["Accent"],
            },
        };
        row.Children.Add(chip);

        var body = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(new TextBlock
        {
            Text = "主页",
            FontSize = 11.5,
            FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
        });
        body.Children.Add(new TextBlock
        {
            Text = "本机概览",
            FontSize = 9.5,
            Margin = new Thickness(0, 1, 0, 0),
            Foreground = (Brush)Application.Current.Resources["TextMuted"],
        });
        row.Children.Add(body);

        var item = new Border
        {
            Child = row,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 6),
            CornerRadius = new CornerRadius(9),
            Background = selected
                ? (Brush)Application.Current.Resources["AccentWash"]
                : Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = "管理主页：本机概览（所有实例的机器级数据）",
        };
        item.HorizontalAlignment = HorizontalAlignment.Stretch;
        item.MouseLeftButtonUp += (_, _) =>
        {
            _selected = "";
            HomePicked?.Invoke();
        };
        item.MouseEnter += (_, _) =>
            item.Background = (Brush)Application.Current.Resources["AccentWash"];
        item.MouseLeave += (_, _) =>
            item.Background = _selected.Length == 0
                ? (Brush)Application.Current.Resources["AccentWash"]
                : Brushes.Transparent;
        Press(item);
        return item;
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
        var run = AppState.Current.StatusOf(inst.Id);

        // 呼吸底色：在跑的行自己会呼吸。单独一层放在内容下面，动的是透明度，
        // 不和 hover / 选中的背景抢同一个属性。
        var wash = new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = (Brush)Application.Current.Resources["AccentWash"],
            Opacity = 0,
            IsHitTestVisible = false,
        };

        var chip = Chip(inst);
        Grid.SetColumn(chip, 0);

        var name = new TextBlock
        {
            Text = inst.Name,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        Grid.SetColumn(name, 1);

        // 灯在行右端：用户点名的位置（"右边出现绿灯"）；没在跑就整盏收起（灰点没有信息）。
        var lamp = new RunLamp
        {
            Width = 12,
            Height = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Running = run.Live,
            ToolTip = run.Live ? run.Detail : null,
        };
        Grid.SetColumn(lamp, 2);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(chip);
        content.Children.Add(name);
        content.Children.Add(lamp);

        var body = new Grid();
        body.Children.Add(wash);
        body.Children.Add(content);

        var item = new Border
        {
            Child = body,
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
        _live[inst.Id] = new LiveRow(lamp, wash);
        return item;
    }

    /// <summary>
    /// 状态快照变了：只改行尾灯和这一行的呼吸，不重建列表——列表一重建，
    /// 指针位置、选中底色和 FLIP 连续性都会跳。没变的行连动画都不重启动。
    /// </summary>
    void ApplyStatus()
    {
        foreach (var (id, row) in _live)
        {
            var run = AppState.Current.StatusOf(id);
            if (row.Phase == run.Phase) continue;
            row.Phase = run.Phase;
            row.Lamp.Running = run.Live;
            row.Lamp.ToolTip = run.Live ? run.Detail : null;
            if (run.Live)
                Fx.Breathe(row.Wash, ms: run.Phase == RunPhase.Starting ? 1100 : 2400);
            else
                Fx.Calm(row.Wash);
        }
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
