using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Pad.Core;

namespace Pad.Views;

/// <summary>
/// D69：按原型 v4 补的两页——
///   现在（等你输入 / 在跑 / 刚完成，按 agent 排；只读 PAD 项目实例的会话）
///   实例（运行体清单，和项目并列）
/// 项目页的 v4 卡片（智能体团队 / 任务 / 能力 / 最近会话 / 配置与覆盖）也在这一份。
/// </summary>
public partial class WorkbenchView
{
    // ---- 状态 ----------------------------------------------------------

    bool _instMode;

    sealed record NowItem(
        string ProjectId, string ProjectName, string Title, string Detail,
        string State, DateTimeOffset? At, string SessionId, string Preset,
        string Draft, string? Tool, int Turns, string Cwd, string Home);

    List<NowItem> _nowItems = [];
    List<JobRecord> _hubJobs = [];

    const string StateWait = "等你输入";
    const string StateRun = "在跑";
    const string StateDone = "刚完成";

    bool InNowPage => _project is null && _scope is null && !_instMode;

    // ---- 切换 ----------------------------------------------------------

    void ShowHubNow()
    {
        _project = null;
        _scope = null;
        _scopeRun = null;
        _instMode = false;
        _selected = "";
        _open = null;
        PaintHeader();
        PaintTransition();
        if (_lastInsts is not null) FillRail(_lastInsts);
        CardScroll?.ScrollToTop();
        Load();
    }

    void ShowInstances()
    {
        _project = null;
        _scope = null;
        _scopeRun = null;
        _instMode = true;
        _selected = "";
        PaintHeader();
        PaintTransition();
        if (_lastInsts is not null) FillRail(_lastInsts);
        CardScroll?.ScrollToTop();
        Load();
    }

    void BackToNow_Click(object sender, RoutedEventArgs e) => ShowHubNow();

    void RailImport() => _host.ShowInner("bundles", "实例与组合包", "manage");

    void SegmentChanged(bool instances)
    {
        if (instances) ShowInstances();
        else ShowHubNow();
    }

    void ImportPack_Click(object sender, RoutedEventArgs e) =>
        _host.ShowInner("bundles", "实例与组合包", "manage");

    // ---- 首页：现在（D69）---------------------------------------------

    async Task LoadNowAsync(IReadOnlyList<Instance> insts, int gen)
    {
        var launcher = _state.Launcher;
        var settings = _state.Settings;
        var projects = new Projects(launcher).List();

        var rows = await Task.Run(() =>
        {
            var list = new List<NowItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(NowSession s, string home, Project bound)
            {
                if (!seen.Add(s.Id)) return;
                var hot = s.LastAt is { } at && (DateTimeOffset.Now - at).TotalSeconds < 120;
                var state = s.Waiting && !s.Open ? StateWait : (s.Open || hot ? StateRun : StateDone);
                var leaf = string.IsNullOrWhiteSpace(s.Cwd)
                    ? ""
                    : Path.GetFileName(s.Cwd.TrimEnd('\\', '/'));
                var what = s.Tool is { Length: > 0 } tool
                    ? "正在运行 " + tool
                    : !string.IsNullOrWhiteSpace(s.Draft)
                        ? ShortDraft(s.Draft)
                        : s.Open ? "正在改文件 / 跑测试" : "已收尾";
                list.Add(new NowItem(
                    bound.Id, bound.Name, s.Title,
                    $"{leaf} · {s.Preset} · {what}", state, s.LastAt,
                    s.Id, s.Preset, s.Draft, s.Tool, s.Turns, s.Cwd, home));
            }

            // 现在页只读 PAD 项目实例的会话：项目 = 一个实例/home，隔离由 home 提供。
            foreach (var p in projects)
            {
                var inst = ResolveProjectInstance(p);
                if (inst is null) continue;
                foreach (var s in NowProbe.Read(inst.Home, 40))
                {
                    // 只留落在项目工作区里的会话；实例 home 里别处的会话不算这个项目。
                    var mine = p.Workspaces.Any(w =>
                        !string.IsNullOrWhiteSpace(s.Cwd)
                        && s.Cwd.StartsWith(w, StringComparison.OrdinalIgnoreCase));
                    if (mine) Add(s, inst.Home, p);
                }
            }
            return list;
        });
        if (gen != _loadGen) return;
        _projects = projects;
        _nowItems = rows;

        IReadOnlyList<JobRecord> jobs = [];
        try { jobs = launcher.ListJobs(); } catch { }
        _hubJobs = [.. jobs.Where(j => j.Status is "queued" or "running").Take(20)];
    }

    void ShowProjectFromNow(NowItem item)
    {
        var proj = _projects.FirstOrDefault(p => p.Id == item.ProjectId)
                   ?? new Projects(_state.Launcher).Get(item.ProjectId);
        if (proj is not null) ShowProject(proj);
    }

    // ---- 现在：页头 / 卡片 --------------------------------------------

    void PaintNowHeader()
    {
        var live = _nowItems.Count(i => i.State == StateRun);
        var wait = _nowItems.Count(i => i.State == StateWait);
        PageTitle.Text = "现在";
        ScopeMeta.Visibility = Visibility.Visible;
        ScopeMeta.Text = $"{_projects.Count} 个项目 · {live} 个 agent 在跑 · {wait} 等你输入";
        ScopeMeta.ToolTip = "按 agent 状态排队：谁在等我、谁在跑、谁刚完成";
        ScopeLogoHost.Visibility = Visibility.Collapsed;
        ScopeLamp.Visibility = Visibility.Collapsed;
        AddBtn.Visibility = Visibility.Collapsed;
        EditBtn.Visibility = Visibility.Collapsed;
        BackHomeBtn.Visibility = Visibility.Collapsed;
        RunBtn.Visibility = Visibility.Collapsed;
        VersionsBtn.Visibility = Visibility.Collapsed;
    }

    void PaintNowCards()
    {
        CardHost.Children.Add(NowCountStrip());
        CardHost.Children.Add(NowSection(StateWait, "等你输入"));
        CardHost.Children.Add(NowSection(StateRun, "在跑"));
        CardHost.Children.Add(NowSection(StateDone, "刚完成"));
    }

    /// <summary>三格计数：等你输入 / 在跑 / 刚完成（对齐原型 v4 顶部三卡）。</summary>
    UIElement NowCountStrip()
    {
        var grid = new UniformGrid { Columns = 3 };
        grid.Children.Add(NowCount(StateWait, "等你输入", "agent 停了在等你", "L.Mail"));
        grid.Children.Add(NowCount(StateRun, "在跑", "正在改文件 / 跑测试", "L.Play"));
        grid.Children.Add(NowCount(StateDone, "刚完成", "可以验收", "L.Check"));
        var card = new Border
        {
            Width = 864,
            Margin = new Thickness(0, 0, 16, 16),
            Padding = new Thickness(18, 14, 18, 14),
            Child = grid,
        };
        card.Style = (Style)Resources["WbCard"];
        Fx.SetEnter(card, "card");
        return card;
    }

    UIElement NowCount(string state, string label, string hint, string iconKey)
    {
        var n = _nowItems.Count(i => i.State == state);
        var stack = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(Icon(iconKey, 12, Muted));
        head.Children.Add(new TextBlock
        {
            Text = label, FontSize = 11, Foreground = Muted,
            Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(head);
        stack.Children.Add(new TextBlock
        {
            Text = n.ToString(),
            FontSize = 30,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources[state == StateRun ? "Accent" : "TextPrimary"],
            Margin = new Thickness(0, 2, 0, 0),
        });
        stack.Children.Add(new TextBlock { Text = hint, FontSize = 9.5, Foreground = Muted });
        return stack;
    }

    UIElement NowSection(string state, string title)
    {
        var items = _nowItems
            .Where(i => i.State == state)
            .OrderByDescending(i => i.At ?? DateTimeOffset.MinValue)
            .ToList();
        var stack = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(new TextBlock
        {
            Text = title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Primary,
        });
        head.Children.Add(new TextBlock
        {
            Text = items.Count.ToString(), FontSize = 10, Foreground = Muted,
            Margin = new Thickness(8, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(head);

        if (items.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = state switch
                {
                    StateWait => "没有人在等你输入。",
                    StateRun => "现在没有 agent 在跑。",
                    _ => "最近没有刚完成的对话。",
                },
                FontSize = 10.5, Foreground = Muted,
            });
        }
        else
        {
            foreach (var item in items.Take(6)) stack.Children.Add(NowRow(item));
            if (items.Count > 8)
                stack.Children.Add(new TextBlock
                {
                    Text = $"+{items.Count - 8} 条", FontSize = 9.5, Foreground = Muted,
                    Margin = new Thickness(2, 4, 0, 0),
                });
        }

        var card = new Border
        {
            Width = 864,
            Margin = new Thickness(0, 0, 16, 16),
            Padding = new Thickness(18, 14, 18, 14),
            Child = stack,
        };
        card.Style = (Style)Resources["WbCard"];
        Fx.SetEnter(card, "card");
        return card;
    }

    UIElement NowRow(NowItem item)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // v4：每行左边是项目色块（首字），不是灰点。
        var tile = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(7),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ProjectColor(item.ProjectName))),
            Margin = new Thickness(0, 0, 9, 0),
            Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(item.ProjectName) ? "?" : item.ProjectName[..1],
                FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        grid.Children.Add(tile);

        var body = new StackPanel { Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(new TextBlock
        {
            Text = item.Title, FontSize = 12, Foreground = Primary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        body.Children.Add(new TextBlock
        {
            Text = $"{item.ProjectName} · {item.Preset} · {item.Detail}",
            FontSize = 9.5, Foreground = Muted,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        var badge = new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(7, 2, 7, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Background = (Brush)Application.Current.Resources[item.State == StateWait ? "AccentWash" : "AccentWash"],
            Margin = new Thickness(6, 0, 6, 0),
            Child = new TextBlock
            {
                Text = item.State, FontSize = 9.5,
                Foreground = (Brush)Application.Current.Resources[item.State == StateRun ? "Accent" : "TextMuted"],
            },
        };
        Grid.SetColumn(badge, 2);
        grid.Children.Add(badge);

        var at = new TextBlock
        {
            Text = AgoText(item.At),
            FontSize = 9.5,
            Foreground = Muted,
            Width = 54,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(at, 3);
        grid.Children.Add(at);

        var row = new Border
        {
            Child = grid,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 2, 0, 0),
            Background = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
        };

        Popup? preview = null;
        row.MouseEnter += (_, _) =>
        {
            row.Background = (Brush)Application.Current.Resources["AccentWash"];
            if (preview is not null) return;
            preview = new Popup
            {
                PlacementTarget = row,
                Placement = PlacementMode.Bottom,
                HorizontalOffset = 8,
                VerticalOffset = 4,
                StaysOpen = true,
                AllowsTransparency = true,
                IsHitTestVisible = false,
                Child = NowHoverCard(item),
            };
            preview.IsOpen = true;
        };
        row.MouseLeave += (_, _) =>
        {
            row.Background = Brushes.Transparent;
            if (preview is { IsOpen: true }) preview.IsOpen = false;
        };
        row.MouseLeftButtonUp += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(item.ProjectId)) { ShowProjectFromNow(item); return; }
            _host.Toast("这条会话不在项目工作区里");
        };
        return row;
    }

    /// <summary>悬停预览（v4）：标题 + 最近一轮输出 + 工具调用 + “点一下切窗口”。</summary>
    UIElement NowHoverCard(NowItem item)
    {
        var stack = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new Border
        {
            Width = 10, Height = 10, CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ProjectColor(item.ProjectName))),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
        };
        head.Children.Add(dot);
        head.Children.Add(new TextBlock
        {
            Text = item.Title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Primary,
        });
        stack.Children.Add(head);
        stack.Children.Add(new TextBlock
        {
            Text = $"{item.ProjectName} · {item.Preset} · {item.Turns} 轮",
            FontSize = 9.5, Foreground = Muted, Margin = new Thickness(0, 2, 0, 8),
        });

        if (!string.IsNullOrWhiteSpace(item.Draft))
        {
            stack.Children.Add(new TextBlock
            {
                Text = "最近一轮", FontSize = 9, Foreground = Muted, Margin = new Thickness(0, 0, 0, 4),
            });
            var bubble = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8, 10, 8),
                Background = (Brush)Application.Current.Resources["BgPage"],
                Child = new TextBlock
                {
                    Text = item.Draft, FontSize = 10.5, Foreground = Primary,
                    TextWrapping = TextWrapping.Wrap, MaxWidth = 370, MaxHeight = 92,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };
            stack.Children.Add(bubble);
        }
        if (!string.IsNullOrWhiteSpace(item.Tool))
        {
            stack.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = (Brush)Application.Current.Resources["AccentWash"],
                Child = new TextBlock
                {
                    Text = "⟳ 运行 " + item.Tool,
                    FontSize = 9.5,
                    Foreground = (Brush)Application.Current.Resources["Accent"],
                },
            });
        }
        stack.Children.Add(new TextBlock
        {
            Text = "点一下 → PAD 把这窗切到对这个对话",
            FontSize = 9.5, Foreground = Muted, Margin = new Thickness(0, 8, 0, 0),
        });

        var card = new Border
        {
            Width = 400,
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 4, 0, 0),
            CornerRadius = new CornerRadius(13),
            Background = (Brush)Application.Current.Resources["BgCard"],
            BorderBrush = (Brush)Application.Current.Resources["LineBorder"],
            BorderThickness = new Thickness(1),
            Child = stack,
            Effect = new DropShadowEffect
            {
                BlurRadius = 24, ShadowDepth = 6, Direction = 270,
                Opacity = 0.16, Color = Color.FromRgb(0x20, 0x30, 0x3F),
            },
            Opacity = 0.98,
        };
        return card;
    }

    static readonly string[] ProjectPalette =
    [
        "#1d5d78", "#22855a", "#7c55b8", "#b4762a",
        "#b42318", "#4a6fa5", "#2f8f83", "#8a6211",
    ];

    static string ProjectColor(string key)
    {
        var h = 0;
        foreach (var ch in key ?? "") h = (h * 31 + ch) & 0x7fffffff;
        return ProjectPalette[h % ProjectPalette.Length];
    }

    static string ShortDraft(string text)
    {
        var t = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return t.Length <= 26 ? t : t[..26] + "…";
    }

    static string AgoText(DateTimeOffset? at)
    {
        if (at is not { } t) return "—";
        var d = DateTimeOffset.Now - t;
        if (d.TotalMinutes < 1) return "刚刚";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} 分钟前";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} 小时前";
        if (d.TotalDays < 8) return $"{(int)d.TotalDays} 天前";
        return t.LocalDateTime.ToString("MM-dd");
    }

    // ---- 实例页（D69）------------------------------------------------

    void PaintInstancesHeader()
    {
        PageTitle.Text = "实例";
        ScopeMeta.Visibility = Visibility.Visible;
        ScopeMeta.Text = "运行体 · 与项目并列";
        ScopeLogoHost.Visibility = Visibility.Collapsed;
        ScopeLamp.Visibility = Visibility.Collapsed;
        AddBtn.Visibility = Visibility.Collapsed;
        EditBtn.Visibility = Visibility.Collapsed;
        BackHomeBtn.Visibility = Visibility.Collapsed;
        RunBtn.Visibility = Visibility.Collapsed;
        VersionsBtn.Visibility = Visibility.Collapsed;
    }

    void PaintInstanceCards(IReadOnlyList<Instance> insts)
    {
        var stack = new StackPanel();
        if (insts.Count == 0)
            stack.Children.Add(new TextBlock { Text = "这份 PAD 还没有实例。", FontSize = 11, Foreground = Muted });
        foreach (var inst in insts.OrderByDescending(i => _state.StatusOf(i.Id).Live).ThenBy(i => i.Name))
        {
            var phase = _state.StatusOf(inst.Id);
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var chip = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(8),
                Background = (Brush)Application.Current.Resources["AccentWash"],
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                Child = Icon("L.Hash", 14, AccentBrush),
            };
            row.Children.Add(chip);
            var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            body.Children.Add(new TextBlock
            {
                Text = inst.Name, FontSize = 12, Foreground = Primary,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            body.Children.Add(new TextBlock
            {
                Text = inst.Home, FontSize = 9.5, Foreground = Muted,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            Grid.SetColumn(body, 1);
            row.Children.Add(body);
            var state = new TextBlock
            {
                Text = phase.Say, FontSize = 10,
                Foreground = (Brush)Application.Current.Resources[phase.Live ? "Accent" : "TextMuted"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(state, 2);
            row.Children.Add(state);

            var item = new Border
            {
                Child = row,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 2, 0, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = inst.Home,
            };
            item.MouseLeftButtonUp += (_, _) => ShowScope(inst);
            item.MouseEnter += (_, _) => item.Background = (Brush)Application.Current.Resources["AccentWash"];
            item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
            stack.Children.Add(item);
        }
        CardHost.Children.Add(FullCard("这份 PAD 管的所有实例", stack, "L.Hash"));

        var axes = new StackPanel();
        axes.Children.Add(AxisLine("现在", "按 agent 状态排队：谁在等我、谁在跑、谁刚完成"));
        axes.Children.Add(AxisLine("项目", "一个项目 = 一个实例/home + 属于它的工作区"));
        axes.Children.Add(AxisLine("实例", "运行体：一份 home + 一个工作区目录"));
        CardHost.Children.Add(FullCard("两条轴的分工", axes, "L.Layers"));
    }

    UIElement AxisLine(string name, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        row.Children.Add(new TextBlock
        {
            Text = name, Width = 52, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = AccentBrush,
        });
        row.Children.Add(new TextBlock { Text = text, FontSize = 10.5, Foreground = Muted });
        return row;
    }

    // ---- 项目页 v4 卡片（D69）-----------------------------------------

    UIElement FullCard(string title, UIElement body, string iconKey)
    {
        var stack = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        head.Children.Add(Icon(iconKey, 13, Muted));
        head.Children.Add(new TextBlock
        {
            Text = title, Style = (Style)Resources["WbLabel"],
            Foreground = (Brush)Application.Current.Resources["TextMuted"],
            Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(head);
        stack.Children.Add(body);
        var card = new Border
        {
            Width = 864,
            Margin = new Thickness(0, 0, 16, 16),
            Padding = new Thickness(18, 14, 18, 14),
            Child = stack,
        };
        card.Style = (Style)Resources["WbCard"];
        Fx.SetEnter(card, "card");
        return card;
    }

    /// <summary>项目页顶部的「Edge 网页窗口」状态条（对齐原型 v4）。</summary>
    UIElement ProjectStatusStrip()
    {
        var inst = _scope;
        var facts = _scopeFacts;
        var phase = inst is null ? new ProjectRun(_project!.Id, RunPhase.Stopped, "") : _state.StatusOf(inst.Id);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = Icon("L.External", 18, phase.Live ? AccentBrush : Muted);
        icon.Margin = new Thickness(0, 0, 10, 0);
        grid.Children.Add(icon);

        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = facts?.AddressText is { } addr && addr != "—"
                ? $"Edge 网页窗口 · {addr}"
                : $"Edge 网页窗口 · {(_project!.Dsh.Profile ?? "web")}",
            FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Primary,
        });
        var bits = new List<string>();
        if (facts?.UptimeText is { } up && up != "—") bits.Add($"已运行 {up}");
        if (facts?.Pid is { } pid) bits.Add($"pid {pid}");
        if (inst is not null) bits.Add($"home {inst.Home}");
        body.Children.Add(new TextBlock
        {
            Text = bits.Count == 0 ? "还没在跑：点右上「打开窗口」启动"
                : string.Join(" · ", bits),
            FontSize = 9.5, Foreground = Muted, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(MiniButton("打开窗口", "L.External", (_, _) => OpenProjectWindow()));
        if (inst is not null)
        {
            buttons.Children.Add(MiniButton("日志", "L.File", (_, _) => Reveal.Path(_state.Launcher.LogsDir(inst.Id))));
            buttons.Children.Add(MiniButton("目录", "L.Folder", (_, _) => Reveal.Path(inst.Home)));
        }
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        var card = new Border
        {
            Width = 864,
            Margin = new Thickness(0, 0, 16, 16),
            Padding = new Thickness(16, 12, 16, 12),
            Child = grid,
            Background = (Brush)Application.Current.Resources["BgCard"],
            CornerRadius = new CornerRadius(14),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        card.MouseLeftButtonUp += (_, _) => OpenProjectWindow();
        return card;
    }

    void OpenProjectWindow()
    {
        var inst = _scope;
        if (inst is null) { _host.Toast("项目还没有实例", bad: true); return; }
        if (_scopeFacts?.PadUrl is { } url) { Reveal.Url(url); return; }
        if (LatestWebUrl(inst) is { } logUrl) { Reveal.Url(logUrl); return; }
        if (_scopeFacts?.Pids is { Count: > 0 } pids && ShellActivate.Try(pids)) return;
        _host.Toast("还没在跑：先点启动", bad: true);
    }

    /// <summary>智能体团队：项目绑的 preset，谁在干活；点一行换默认，+ 从 preset 名里绑。</summary>
    UIElement ProjectTeamCard()
    {
        var proj = _project!;
        var stack = new StackPanel();
        var busy = _scopeDialogs.Count(d => d.StepOpen);
        stack.Children.Add(new TextBlock
        {
            Text = proj.Agents.Count == 0 ? "还没绑 agent"
                : $"{proj.Agents.Count} 个 agent · {busy} 在干活",
            FontSize = 9.5, Foreground = Muted, Margin = new Thickness(0, 0, 0, 8),
        });
        if (proj.Agents.Count == 0)
            stack.Children.Add(new TextBlock
            {
                Text = "点下面「绑定 preset」把发行号里的 agent 挂到这个项目上。",
                FontSize = 10.5, Foreground = Muted, TextWrapping = TextWrapping.Wrap,
            });
        foreach (var id in proj.Agents)
        {
            var isDefault = string.Equals(id, proj.Agents[0], StringComparison.OrdinalIgnoreCase);
            var working = _scopeDialogs.Any(d => d.StepOpen
                && string.Equals(d.Preset, id, StringComparison.OrdinalIgnoreCase));
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var dot = new Border
            {
                Width = 9, Height = 9, CornerRadius = new CornerRadius(5),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 8, 0),
                Background = (Brush)Application.Current.Resources[working ? "Accent" : "LineDivider"],
            };
            row.Children.Add(dot);
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = id, FontSize = 11.5, Foreground = Primary });
            body.Children.Add(new TextBlock
            {
                Text = (isDefault ? "默认 · " : "") + (working ? "在干活" : "空闲"),
                FontSize = 9.5, Foreground = working ? AccentBrush : Muted,
            });
            Grid.SetColumn(body, 1);
            row.Children.Add(body);
            var bind = new Button
            {
                Content = isDefault ? "默认" : "设为默认",
                Height = 24, Padding = new Thickness(8, 0, 8, 0),
                Style = (Style)Application.Current.Resources[isDefault ? "BtnPrimary" : "BtnSm"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            var preset = id;
            bind.Click += (_, _) => UsePreset(proj, preset);
            Grid.SetColumn(bind, 2);
            row.Children.Add(bind);
            stack.Children.Add(row);
        }
        var add = new Button
        {
            Content = BtnContent(proj.Agents.Count == 0 ? "绑定 preset" : "＋ 从该实例 preset 名里绑定", "L.Plus"),
            Height = 26, Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Style = (Style)Application.Current.Resources["BtnSm"],
        };
        add.Click += (_, _) => AddPreset(proj);
        stack.Children.Add(add);
        return stack;
    }

    /// <summary>任务：PAD 的排队 / 在跑作业（headless 队列）。没有任务给空态。</summary>
    UIElement ProjectTasksCard()
    {
        var stack = new StackPanel();
        var running = _hubJobs.Count(j => j.Status == "running");
        stack.Children.Add(new TextBlock
        {
            Text = _hubJobs.Count == 0 ? "还没有排队或运行中的任务"
                : $"运行中 {running} · 待办 {_hubJobs.Count - running}",
            FontSize = 9.5, Foreground = Muted, Margin = new Thickness(0, 0, 0, 8),
        });
        foreach (var job in _hubJobs.Take(5))
        {
            var row = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock
            {
                Text = job.Label ?? job.Kind,
                FontSize = 11, Foreground = Primary,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            row.Children.Add(new TextBlock
            {
                Text = (job.Instance ?? "本机") + " · " + job.Status,
                FontSize = 9.5,
                Foreground = job.Status == "running" ? AccentBrush : Muted,
            });
            stack.Children.Add(row);
        }
        if (_hubJobs.Count == 0)
            stack.Children.Add(new TextBlock
            {
                Text = "任务来自 PAD 的作业队列（headless / 整合包安装）。",
                FontSize = 10, Foreground = Muted, TextWrapping = TextWrapping.Wrap,
            });
        return stack;
    }

    /// <summary>能力：这个实例的插件（组合包 spec）。</summary>
    UIElement ProjectCapsCard()
    {
        var stack = new StackPanel();
        var bundles = _scopeBundles;
        stack.Children.Add(new TextBlock
        {
            Text = bundles.Count == 0 ? "这个实例还没装组合包" : $"插件 {bundles.Count} · 整合包 {_scopePacks.Count}",
            FontSize = 9.5, Foreground = Muted, Margin = new Thickness(0, 0, 0, 8),
        });
        var wrap = new WrapPanel();
        foreach (var b in bundles.Take(8))
        {
            var chip = new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 6, 6),
                Background = (Brush)Application.Current.Resources["AccentWash"],
                Child = new TextBlock
                {
                    Text = b.Spec,
                    FontSize = 10,
                    Foreground = (Brush)Application.Current.Resources["Accent"],
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 220,
                },
            };
            wrap.Children.Add(chip);
        }
        stack.Children.Add(wrap);
        stack.Children.Add(new TextBlock
        {
            Text = "插件与整合包在「设置 → 插件」里管理",
            FontSize = 9.5, Foreground = Muted, Margin = new Thickness(0, 2, 0, 0),
        });
        return stack;
    }

    /// <summary>最近会话：点一条跳到那个对话（复用 D67 的打开对话）。</summary>
    UIElement ProjectRecentCard()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = _scopeDialogs.Count == 0
                ? "这个 home 还没有会话"
                : $"本项目 {_scopeDialogs.Count} 条 · 点一条跳到那个对话",
            FontSize = 9.5, Foreground = Muted, Margin = new Thickness(0, 0, 0, 8),
        });
        foreach (var d in _scopeDialogs.Take(3)) stack.Children.Add(ProjectDialogLine(d));
        if (_scopeDialogs.Count == 0)
            stack.Children.Add(new TextBlock
            {
                Text = "启动一次 DSH，这里会列出这个项目 home 的会话。",
                FontSize = 10, Foreground = Muted, TextWrapping = TextWrapping.Wrap,
            });
        return stack;
    }

    /// <summary>配置与覆盖：工作区 N · 智能体 M · profile · 端口 + 编辑。</summary>
    UIElement ProjectConfigStrip()
    {
        var proj = _project!;
        var inst = _scope;
        var facts = _scopeFacts;
        var bits = new List<string>
        {
            $"工作区 {proj.Workspaces.Count}",
            $"智能体 {proj.Agents.Count}",
            $"profile {proj.Dsh.Profile ?? inst?.LastProfile ?? "web"}",
        };
        if (facts?.PortText is { } port && port != "—") bits.Add($"端口 {port}");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock
        {
            Text = string.Join(" · ", bits),
            FontSize = 10.5, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center,
        });
        var edit = new Button
        {
            Content = "编辑",
            Height = 26, Padding = new Thickness(12, 0, 12, 0),
            Style = (Style)Application.Current.Resources["BtnSm"],
        };
        edit.Click += (_, _) => OpenProjectMenu();
        Grid.SetColumn(edit, 1);
        grid.Children.Add(edit);
        return grid;
    }
}
