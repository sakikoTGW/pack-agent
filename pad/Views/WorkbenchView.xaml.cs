using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Pad.Core;
using IOPath = System.IO.Path;

namespace Pad.Views;

/// <summary>
/// 管理首页。左边是可收起的项目侧栏，右边是可由用户自排的卡片区（D28）。
/// 卡片 = 数据源 × 表现形；偏好存在 pad.json#ui.cards，没有隐藏状态。
/// </summary>
public partial class WorkbenchView : UserControl
{
    readonly AppState _state = AppState.Current;
    readonly MainWindow _host;

    Inventory? _inventory;
    int[] _activity = new int[7];
    (string Tool, int Calls, long Tokens)[] _tools = [];
    bool _editing;
    CardPref? _open;
    string _selected = "";

    /// <summary>舞台侧栏（MainWindow 持有）：本页只负责喂数据，不拥有它。</summary>
    ProjectRail? _rail;
    IReadOnlyList<Instance>? _lastInsts;

    static readonly (string Type, string Label, string View, int Cols)[] Catalog =
    [
        ("running", "在运行", "ring", 1),
        ("inventory", "本机 DSH", "count", 1),
        ("context", "上下文", "bar", 1),
        ("activity", "近 7 天", "bars", 2),
        ("tools", "工具调用", "bars", 2),
        ("project", "项目卡", "dots", 1),
        ("note", "便签", "", 1),
    ];

    public WorkbenchView(MainWindow host)
    {
        InitializeComponent();
        _host = host;
        if (App.ShotPage == "manage-edit")
        {
            _editing = true;
            EditBtn.Content = "完成";
        }
        if (App.ShotPage == "manage-open")
        {
            // Prove the expanded face without a pointer: open the inventory card for the shot.
            Loaded += (_, _) =>
            {
                // Prefs() also materialises the defaults, so ask it before picking one.
                _open = Prefs().FirstOrDefault(c => c.Type == "inventory");
                if (_open is not null) Load();
            };
        }
        // Loaded fires inside the layout pass, and Load enumerates the process table, home
        // directories and session files. Running that here froze the first frame of every
        // switch into 管理 — the one hitch that survived the motion work. The scan now runs
        // off the UI thread and paints when it has data.
        Loaded += (_, _) => Load();
        BuildAddMenu();
    }

    void OpenBundles_Click(object sender, RoutedEventArgs e) =>
        _host.ShowInner("bundles", "实例与组合包", "manage");

    void OpenRelations_Click(object sender, RoutedEventArgs e) =>
        _host.ShowRelations();

    // ------------------------------------------------------------------ 加载

    /// <summary>
    /// Prewarm. Run the scan before the page is ever shown so the first switch has cards to
    /// stagger in instead of an empty grid (the window unfold no longer waits for data).
    /// </summary>
    public void Prepare() => Load();

    void Load() => _ = LoadAsync();

    int _loadGen;

    /// <summary>
    /// Nothing in the first half touches WPF: process enumeration, the known-location walk,
    /// session mtimes and the token-usage cache all read the disk/machine on a worker. The
    /// page is already on screen when this starts, so the switch has no stall to hide.
    /// </summary>
    async Task LoadAsync()
    {
        var gen = ++_loadGen;
        try
        {
            var launcher = _state.Launcher;
            var insts = await Task.Run(() => launcher.Instances());
            if (gen != _loadGen) return;
            if (_selected.Length == 0)
                _selected = _state.SelectedInstance?.Id ?? insts.FirstOrDefault()?.Id ?? "";

            var inventory = await Task.Run(() => Discover.Run(launcher, [], CancellationToken.None));
            if (gen != _loadGen) return;
            _inventory = inventory;

            var liveHome = inventory.Homes.FirstOrDefault(h => h.Running)?.Path
                           ?? inventory.Shells.FirstOrDefault(sh => !string.IsNullOrWhiteSpace(sh.Home))?.Home
                           ?? DesktopHome();
            var scan = await Task.Run(() => (Activity: ScanActivity(liveHome), Tools: ScanTools(liveHome)));
            if (gen != _loadGen) return;
            _activity = scan.Activity;
            _tools = scan.Tools;

            PaintProjects(insts);
            PaintCards(insts);
            Footnote.Text = "";
        }
        catch (Exception ex)
        {
            Footnote.Text = PadError.Describe(ex);
        }
    }

    static string DesktopHome() => IOPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dsh-desktop", "harness");

    /// <summary>Logo lookups hit the disk, so remember the answer per workspace.</summary>
    readonly Dictionary<string, string?> _marks = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string?> _shots = new(StringComparer.OrdinalIgnoreCase);

    string? MarkFor(Instance inst) => Cached(_marks, inst, _ => InstIcon.PackImage(inst));

    /// <summary>
    /// Icon priority: what the user set → the instance's pack image → the carrier's icon
    /// (TUI/desktop bundle) → the DSH whale. A letter is never drawn.
    /// </summary>
    string? IconFor(Instance inst)
    {
        if (!string.IsNullOrWhiteSpace(inst.Display?.Logo))
        {
            var set = Skin.Resolve(inst.Display!.Logo!);
            if (System.IO.File.Exists(set)) return set;
        }

        var mark = MarkFor(inst);
        if (mark is not null) return mark;

        // Carriers are plugins, and there can be several TUIs: ask the packages themselves,
        // then the carrier database, before settling for the whale.
        var carrier = Carriers.Resolve(inst.Home, _state.Launcher.Root, Carriers.Bundles(inst.Home));
        if (carrier is not null) return carrier;

        return WhaleIcon();
    }

    /// <summary>The DSH app icon that ships beside the desktop shell.</summary>
    string? WhaleIcon()
    {
        if (_whaleLoaded) return _whale;
        _whaleLoaded = true;
        var exe = _inventory?.Shells.FirstOrDefault()?.Exe;
        if (string.IsNullOrWhiteSpace(exe)) return null;
        var at = IOPath.Combine(IOPath.GetDirectoryName(exe) ?? "", "resources", "icon.png");
        if (System.IO.File.Exists(at)) _whale = at;
        return _whale;
    }

    bool _whaleLoaded;
    string? _whale;

    string? ShotFor(Instance inst) => Cached(_shots, inst, Logos.Ui);

    static string? Cached(Dictionary<string, string?> cache, Instance inst, Func<string?, string?> find)
    {
        var dir = inst.Workspace?.Path;
        if (string.IsNullOrWhiteSpace(dir)) return null;
        if (cache.TryGetValue(dir, out var known)) return known;
        var found = find(dir);
        cache[dir] = found;
        return found;
    }

    /// <summary>An ImageBrush for a local image; null when it cannot be decoded.</summary>
    static ImageBrush? ImageBrushFor(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>How many sessions touched this home on each of the last seven days.</summary>
    static int[] ScanActivity(string home)
    {
        var activity = new int[7];
        var dir = IOPath.Combine(home, "storages", "session_projcache", "sessions");
        if (!System.IO.Directory.Exists(dir)) return activity;

        var today = DateTime.Now.Date;
        try
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(dir, "*.json"))
            {
                var days = (today - System.IO.File.GetLastWriteTime(file).Date).Days;
                if (days is >= 0 and < 7) activity[6 - days]++;
            }
        }
        catch { /* unreadable dir: leave zeros */ }
        return activity;
    }

    /// <summary>Top five tools by call count; the report already reads them from the cache.</summary>
    static (string Tool, int Calls, long Tokens)[] ScanTools(string home)
    {
        try
        {
            return Usage.Measure(home).Tools
                .OrderByDescending(t => t.Calls)
                .Take(5)
                .Select(t => (t.Tool, t.Calls, t.Tokens))
                .ToArray();
        }
        catch
        {
            /* no cache yet */
            return [];
        }
    }

    /// <summary>
    /// The rail is shared with the relations page, so switching pages keeps it still.
    /// This method only feeds it and reacts to a pick.
    /// </summary>
    /// <summary>挂接舞台侧栏。页面是缓存的：再次上台时把上次的项目列表原位放回去。</summary>
    public void AttachRail(ProjectRail rail)
    {
        _rail = rail;
        if (_lastInsts is not null) _rail.Fill(_lastInsts, _selected);
    }

    void PaintProjects(IReadOnlyList<Instance> insts)
    {
        _lastInsts = insts;
        _rail?.Fill(insts, _selected);
    }


    // ------------------------------------------------------------------ 卡片

    List<CardPref> Prefs()
    {
        var cards = _state.Settings.Ui.Cards;
        if (cards.Count == 0)
        {
            cards.Add(new CardPref { Type = "running", View = "ring" });
            cards.Add(new CardPref { Type = "inventory", View = "count" });
            cards.Add(new CardPref { Type = "context", View = "bar" });
            // Wide cards sit next to a square one so each row still fills three columns.
            cards.Add(new CardPref { Type = "activity", View = "bars", Cols = 2 });
            cards.Add(new CardPref { Type = "project", View = "dots" });
            cards.Add(new CardPref { Type = "tools", View = "bars", Cols = 2 });
            cards.Add(new CardPref { Type = "note", View = "" });
        }
        return cards;
    }

    void PaintCards(IReadOnlyList<Instance> insts)
    {
        CardHost.Children.Clear();
        foreach (var pref in Prefs())
            CardHost.Children.Add(Card(pref, insts));
        if (_editing) CardHost.Children.Add(AddTile());
    }

    UIElement Card(CardPref pref, IReadOnlyList<Instance> insts)
    {
        var wide = pref.Cols >= 2;
        var open = ReferenceEquals(_open, pref);
        var body = open
            ? ExpandBody(pref)
            : pref.Type switch
            {
                "running" => RingCard(),
                "inventory" => InventoryCard(),
                "context" => ContextCard(),
                "activity" => ActivityCard(),
                "tools" => ToolsCard(),
                "project" => ProjectCard(pref, insts),
                "note" => NoteCard(pref),
                _ => new TextBlock { Text = pref.Type },
            };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = Title(pref, insts),
            Style = (Style)Resources["WbLabel"],
            Foreground = (Brush)Application.Current.Resources["TextMuted"],
            Margin = new Thickness(0, 0, 0, 14),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        stack.Children.Add(body);

        var inner = new Grid();
        inner.Children.Add(stack);
        if (_editing)
        {
            var del = new Button
            {
                Content = "×",
                Style = (Style)Application.Current.Resources["BtnGhost"],
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -6, -6, 0),
                ToolTip = "移除这张卡片",
            };
            del.Click += (_, _) =>
            {
                var cards = _state.Settings.Ui.Cards;
                var idx = cards.IndexOf(pref);
                if (idx >= 0) cards.RemoveAt(idx);
                _state.SaveSettings();
                Load();
            };
            inner.Children.Add(del);
        }

        var card = new Border
        {
            Width = wide ? 576 : 280,
            Height = open ? 372 : 226,
            Margin = new Thickness(0, 0, 16, 16),
            Child = inner,
        };
        card.Style = (Style)Resources["WbCard"];
        // Tagged so the page-level entrance can cascade the cards instead of the panel
        // arriving as one block (Fx.EnterPage).
        Fx.SetEnter(card, "card");
        if (!_editing)
        {
            card.Cursor = System.Windows.Input.Cursors.Hand;
            card.ToolTip = open ? "收起" : "展开";
            card.MouseLeftButtonUp += (_, _) =>
            {
                _open = open ? null : pref;
                Load();
            };
        }
        // No per-card DropShadowEffect: a blurred surface per card is re-rendered on
        // every frame of the entrance stagger and on every hover. The card style's
        // border carries the separation instead.
        return card;
    }

    /// <summary>
    /// The expanded face of a card: the same data, but every row instead of the aggregate.
    /// Interaction is where extra content comes from; the closed card stays a shape (D31).
    /// </summary>
    UIElement ExpandBody(CardPref pref)
    {
        var stack = new StackPanel();
        var homes = _inventory?.Homes ?? [];

        switch (pref.Type)
        {
            case "inventory":
            {
                // 展开不引入新数据源：把「本机 DSH」的载体逐行列出来（类型 · 版本/名 · home/路径 · 在跑）。
                foreach (var c in CarrierRows(_inventory))
                    stack.Children.Add(CarrierLine(c));
                break;
            }
            case "running":
            {
                var max = Math.Max(homes.Count == 0 ? 0 : homes.Max(h => h.Sessions), 1);
                foreach (var h in homes)
                    stack.Children.Add(DetailRow(ShortHome(h.Path), h.Sessions, max, h.Running));
                break;
            }
            case "tools":
            {
                var max = Math.Max(_tools.Length == 0 ? 0 : _tools.Max(t => t.Calls), 1);
                foreach (var (tool, calls, tokens) in _tools)
                    stack.Children.Add(DetailRow($"{tool} · {(tokens / 1000)}k", calls, max, false));
                break;
            }
            case "activity":
            {
                var max = Math.Max(_activity.DefaultIfEmpty(0).Max(), 1);
                for (var i = 0; i < _activity.Length; i++)
                    stack.Children.Add(DetailRow(DayLabel(6 - i), _activity[i], max, false));
                break;
            }
            default:
                return new TextBlock
                {
                    Text = "这张卡没有更多明细",
                    Style = (Style)Resources["WbLabel"],
                };
        }
        return stack;
    }

    static string ShortHome(string path)
    {
        var full = path.TrimEnd('\\', '/');
        var name = IOPath.GetFileName(full);
        if (string.IsNullOrWhiteSpace(name)) name = full;
        // Every PAD instance ends in a folder called "home"; the parent is what differs.
        if (name is "home" or "dsh-home" or "harness" or "workspace")
        {
            var parent = IOPath.GetFileName(IOPath.GetDirectoryName(full) ?? "");
            if (!string.IsNullOrWhiteSpace(parent)) name = parent + "/" + name;
        }
        return name.Length > 18 ? name[..18] : name;
    }

    /// <summary>One detail line: name, a bar for the amount, and the number itself.</summary>
    UIElement DetailRow(string label, int value, int max, bool lit)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8), Height = 20 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TextBody"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(name, 0);
        row.Children.Add(name);

        var bar = new Border
        {
            Height = 8,
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Width = Math.Max(4, 210.0 * value / max),
            Background = lit
                ? (Brush)Application.Current.Resources["Accent"]
                : (Brush)Application.Current.Resources["AccentSoft"],
        };
        Grid.SetColumn(bar, 1);
        row.Children.Add(bar);

        var num = new TextBlock
        {
            Text = value.ToString(),
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TextMuted"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        Grid.SetColumn(num, 2);
        row.Children.Add(num);

        return row;
    }

    string Title(CardPref pref, IReadOnlyList<Instance> insts)    {
        if (pref.Type != "project") return Catalog.FirstOrDefault(c => c.Type == pref.Type).Label ?? pref.Type;
        var id = string.IsNullOrWhiteSpace(pref.Source) ? _selected : pref.Source;
        return insts.FirstOrDefault(i => i.Id == id)?.Name ?? "未绑定";
    }

    UIElement RingCard()
    {
        var homes = _inventory?.Homes ?? [];
        var running = homes.Count(h => h.Running);
        const double r = 44;
        var grid = new Grid { Width = 104, Height = 104, HorizontalAlignment = HorizontalAlignment.Left };

        grid.Children.Add(new Path
        {
            Stroke = (Brush)Application.Current.Resources["LineDivider"],
            StrokeThickness = 9,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = new EllipseGeometry(new Point(52, 52), r, r),
        });
        grid.Children.Add(new Path
        {
            Stroke = (Brush)Application.Current.Resources["Accent"],
            StrokeThickness = 9,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = Arc(52, 52, r, homes.Count <= 0 ? 0 : running / (double)homes.Count),
        });
        grid.Children.Add(new TextBlock
        {
            Text = running.ToString(),
            FontSize = 40,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        // 第二层：每个 home 一根细柱，高度=会话数，实心=在跑。
        var strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = 34,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var maxSessions = Math.Max(homes.Count == 0 ? 0 : homes.Max(h => h.Sessions), 1);
        var accent = (Brush)Application.Current.Resources["Accent"];
        var idle = (Brush)Application.Current.Resources["LineBorder"];

        foreach (var h in homes.Take(10))
        {
            // +1 so an idle home still shows a stub: absence is information too.
            var barH = Math.Max(10, 32.0 * (h.Sessions + 1) / (maxSessions + 1));
            strip.Children.Add(new Border
            {
                Width = 12,
                Height = barH,
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = h.Running ? accent : idle,
            });
        }

        var stack = new StackPanel();
        stack.Children.Add(grid);
        stack.Children.Add(strip);
        return stack;
    }

    UIElement InventoryCard()
    {
        var inv = _inventory;
        var carriers = CarrierRows(inv);

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = carriers.Count.ToString(),
            FontSize = 40,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
        });

        // 第二层：一个载具一个方块。实心 = 在跑，浅色 = 装了没跑，灰 = home。
        var grid = new UniformGrid
        {
            Columns = 8,
            Rows = 2,
            Height = 64,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var accent = (Brush)Application.Current.Resources["Accent"];
        var soft = (Brush)Application.Current.Resources["AccentLight"];
        var pale = (Brush)Application.Current.Resources["LineDivider"];
        if (carriers.Count == 0)
            grid.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 6, 6),
                Background = pale,
            });
        foreach (var c in carriers.Take(16))
            grid.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 6, 6),
                Background = c.Running ? accent : soft,
            });
        stack.Children.Add(grid);
        stack.Children.Add(new TextBlock
        {
            Text = $"内核 {(inv?.Kernels.Count ?? 0)} · home {(inv?.Homes.Count ?? 0)}",
            Style = (Style)Application.Current.Resources["Muted"],
            Margin = new Thickness(0, 8, 0, 0),
        });
        return stack;
    }

    /// <summary>一张卡片行：载具类型 · 版本/名 · home/路径 · 在跑。</summary>
    sealed record CarrierRow(string Kind, string Label, string Detail, bool Running);

    /// <summary>载体 = 桌面端 + TUI + CLI。按类型排，同内核算一组由数据本身区分（D36）。</summary>
    static List<CarrierRow> CarrierRows(Inventory? inv)
    {
        var rows = new List<CarrierRow>();
        if (inv is null) return rows;
        foreach (var s in inv.Shells)
            rows.Add(new CarrierRow("shell", $"{s.Version ?? "?"} · {IOPath.GetFileName(s.Exe)}",
                s.Home is null ? "" : "home " + ShortHome(s.Home), s.Running));
        foreach (var t in inv.Tuis)
            rows.Add(new CarrierRow("tui", $"{t.Package} {(t.Version ?? "")}".Trim(),
                t.Home is null ? "" : "home " + ShortHome(t.Home), t.Running));
        foreach (var c in inv.Clis)
            rows.Add(new CarrierRow("cli", c.Version ?? "?",
                c.Running ? "在跑" : ShortHome(c.Bin), c.Running));
        return rows;
    }

    UIElement CarrierLine(CarrierRow row)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6), Height = 18 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var kind = new TextBlock
        {
            Text = row.Kind switch { "shell" => "桌面端", "tui" => "TUI", _ => "CLI" },
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources[row.Running ? "Accent" : "TextMuted"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(kind, 0);
        grid.Children.Add(kind);

        var label = new TextBlock
        {
            Text = row.Label,
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TextBody"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        var detail = new TextBlock
        {
            Text = row.Running ? "在跑" : row.Detail,
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources[row.Running ? "Accent" : "TextMuted"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        Grid.SetColumn(detail, 2);
        grid.Children.Add(detail);
        return grid;
    }

    /// <summary>Lerp pale → accent by activity, so one glance ranks every home.</summary>
    static Brush Heat(int value, int max, bool running)
    {
        var accent = ((SolidColorBrush)Application.Current.Resources["Accent"]).Color;
        var pale = ((SolidColorBrush)Application.Current.Resources["LineBorder"]).Color;
        if (!running)
            return new SolidColorBrush(Color.FromArgb(255, pale.R, pale.G, pale.B));

        var t = max <= 0 ? 0 : Math.Min(1.0, value / (double)max);
        t = 0.35 + 0.65 * t; // 即使在跑但没会话，也要看得见
        return new SolidColorBrush(Color.FromArgb(
            255,
            (byte)(pale.R + (accent.R - pale.R) * t),
            (byte)(pale.G + (accent.G - pale.G) * t),
            (byte)(pale.B + (accent.B - pale.B) * t)));
    }

    Border KindTick(int count, double width) => new()
    {
        Width = Math.Max(4, width),
        Height = 5,
        CornerRadius = new CornerRadius(3),
        Margin = new Thickness(0, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Background = count > 0
            ? (Brush)Application.Current.Resources["Accent"]
            : (Brush)Application.Current.Resources["LineDivider"],
    };

    UIElement ContextCard()
    {
        var home = _inventory?.Homes.FirstOrDefault(h => h.Running)?.Path
                   ?? IOPath.Combine(
                       Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                       "dsh-desktop", "harness");
        var tokens = 0L;
        var window = 1_000_000L;
        var schema = 0L;
        try
        {
            var report = Inspect.Measure(home);
            tokens = report.ContextTokens;
            schema = report.ToolsTokens;
            if (report.ContextWindow > 0) window = report.ContextWindow;
        }
        catch { /* home missing */ }

        var pct = Math.Round(tokens * 100.0 / window);

        var bar = new Grid { Width = 20, Height = 124, VerticalAlignment = VerticalAlignment.Top };
        bar.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = (Brush)Application.Current.Resources["LineDivider"],
        });
        bar.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = (Brush)Application.Current.Resources["Accent"],
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = Math.Max(4, 124 * Math.Min(1.0, tokens / (double)window)),
        });

        var right = new StackPanel { Margin = new Thickness(16, 6, 0, 0) };
        right.Children.Add(new TextBlock
        {
            Text = pct + "%",
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
        });
        right.Children.Add(new TextBlock
        {
            Text = $"{tokens / 1000}k / {window / 1000}k",
            Style = (Style)Application.Current.Resources["Muted"],
        });

        // 第二层：上下文里有多少是工具定义（深）与其余部分（浅）。
        var mix = new Grid { Width = 120, Height = 8, Margin = new Thickness(0, 12, 0, 0) };
        mix.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(4),
            Background = (Brush)Application.Current.Resources["LineDivider"],
        });
        mix.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = tokens <= 0 ? 0 : Math.Max(3, 120.0 * Math.Min(schema, tokens) / tokens),
            Background = (Brush)Application.Current.Resources["Accent"],
        });
        right.Children.Add(mix);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(bar);
        row.Children.Add(right);
        return row;
    }

    /// <summary>近 7 天会话数：每根柱子是一天的会话落盘数。</summary>
    UIElement ActivityCard()
    {
        var max = Math.Max(_activity.DefaultIfEmpty(0).Max(), 1);
        var bars = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = 92,
        };

        var accent = (Brush)Application.Current.Resources["Accent"];
        var pale = (Brush)Application.Current.Resources["LineDivider"];
        var labels = new StackPanel { Orientation = Orientation.Horizontal };

        for (var i = 0; i < _activity.Length; i++)
        {
            var v = _activity[i];
            var h = v <= 0 ? 5 : Math.Max(9, 84.0 * v / max);
            var col = new StackPanel { Width = 66, VerticalAlignment = VerticalAlignment.Bottom };
            col.Children.Add(new Border
            {
                Width = 40,
                Height = h,
                CornerRadius = new CornerRadius(4),
                Background = v <= 0 ? pale : accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
            });
            bars.Children.Add(col);

            labels.Children.Add(new TextBlock
            {
                Text = DayLabel(6 - i),
                Width = 66,
                TextAlignment = TextAlignment.Center,
                Style = (Style)Application.Current.Resources["Muted"],
                FontSize = 10,
                Margin = new Thickness(0, 5, 0, 0),
            });
        }

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = _activity.Sum().ToString(),
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            Margin = new Thickness(0, 0, 0, 4),
        });
        stack.Children.Add(bars);
        stack.Children.Add(labels);
        return stack;
    }

    static string DayLabel(int daysAgo) => daysAgo switch
    {
        0 => "今天",
        1 => "昨天",
        _ => "-" + daysAgo + "天",
    };

    /// <summary>工具调用分布：前五名各一根横条，长度按调用次数。</summary>
    UIElement ToolsCard()
    {
        var stack = new StackPanel();
        var total = _tools.Sum(t => t.Calls);
        stack.Children.Add(new TextBlock
        {
            Text = total == 0 ? "—" : total.ToString(),
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            Margin = new Thickness(0, 0, 0, 10),
        });

        if (_tools.Length == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "这个 home 还没有工具调用记录",
                Style = (Style)Application.Current.Resources["Muted"],
            });
            return stack;
        }

        var max = Math.Max(_tools.Max(t => t.Calls), 1);
        var maxTokens = Math.Max(_tools.Max(t => t.Tokens), 1);
        var accent = (Brush)Application.Current.Resources["Accent"];
        var accentSoft = (Brush)Application.Current.Resources["AccentLight"];
        var pale = (Brush)Application.Current.Resources["LineDivider"];

        foreach (var (tool, calls, tokens) in _tools)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4), Height = 18 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new TextBlock
            {
                Text = tool,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextBody"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(name, 0);
            row.Children.Add(name);

            // 两层：上=调用次数，下=该工具吃掉的 token。同一条柱的两个维度。
            var bars = new StackPanel { Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            bars.Children.Add(new Border
            {
                Height = 7,
                CornerRadius = new CornerRadius(4),
                Background = accent,
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(4, 240.0 * calls / max),
            });
            bars.Children.Add(new Border
            {
                Height = 4,
                Margin = new Thickness(0, 3, 0, 0),
                CornerRadius = new CornerRadius(2),
                Background = accentSoft,
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(3, 240.0 * tokens / maxTokens),
            });
            Grid.SetColumn(bars, 1);
            row.Children.Add(bars);

            var num = new TextBlock
            {
                Text = calls.ToString(),
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextMuted"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(num, 2);
            row.Children.Add(num);

            stack.Children.Add(row);
        }
        return stack;
    }

    UIElement ProjectCard(CardPref pref, IReadOnlyList<Instance> insts)
    {
        var id = string.IsNullOrWhiteSpace(pref.Source) ? _selected : pref.Source;
        var inst = insts.FirstOrDefault(i => i.Id == id) ?? insts.FirstOrDefault();
        var stack = new StackPanel();

        var sessions = 0;
        var live = false;
        if (inst is not null)
        {
            try { sessions = Inspect.Measure(inst.Home).Sessions; } catch { }
            live = Nodes.Ledger.Load(inst.Home).Any(e => Proc.Alive(e.Pid));
        }

        stack.Children.Add(new TextBlock
        {
            Text = sessions.ToString(),
            FontSize = 32,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
        });

        var dots = new UniformGrid { Columns = 8, Rows = 1, Height = 26, Margin = new Thickness(0, 10, 0, 0) };
        PaintDots(dots, Math.Max(sessions, 1), Math.Min(sessions, 8));
        stack.Children.Add(dots);
        stack.Children.Add(new TextBlock
        {
            Text = live ? "在跑" : "未启动",
            Foreground = live
                ? (Brush)Application.Current.Resources["Accent"]
                : (Brush)Application.Current.Resources["TextMuted"],
            FontSize = 11,
            Margin = new Thickness(0, 12, 0, 0),
        });

        // A project that ships a UI shot shows it: the card stops being a blank number.
        var shot = inst is null ? null : ShotFor(inst);
        var brush = shot is null ? null : ImageBrushFor(shot);
        if (brush is null) return stack;

        var cover = new Border
        {
            Height = 92,
            CornerRadius = new CornerRadius(14),
            Margin = new Thickness(0, 0, 0, 10),
            Background = brush,
        };

        // With a cover the card is shared space, so the rows below it get compact.
        var compact = new StackPanel { Orientation = Orientation.Horizontal };
        compact.Children.Add(new TextBlock
        {
            Text = sessions.ToString(),
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
        });
        compact.Children.Add(new TextBlock
        {
            Text = live ? "在跑" : "未启动",
            Foreground = live
                ? (Brush)Application.Current.Resources["Accent"]
                : (Brush)Application.Current.Resources["TextMuted"],
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(10, 0, 0, 4),
        });

        var withCover = new StackPanel();
        withCover.Children.Add(cover);
        withCover.Children.Add(compact);
        return withCover;
    }

    UIElement NoteCard(CardPref pref)
    {
        // View mode stays a plain block: a TextBox drags the shared field style along and a
        // note is not a form field. Editing happens in edit mode, where a field is honest.
        if (!_editing)
            return new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(pref.Text) ? "（空便签）" : pref.Text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextBody"],
                VerticalAlignment = VerticalAlignment.Top,
            };

        var box = new TextBox
        {
            Text = pref.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = (Brush)Application.Current.Resources["TextBody"],
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        box.ClearValue(StyleProperty);
        box.TextChanged += (_, _) =>
        {
            if (pref.Text == box.Text) return;
            pref.Text = box.Text;
        };
        box.LostFocus += (_, _) => _state.SaveSettings();
        return box;
    }

    UIElement AddTile()
    {
        var tile = new Border
        {
            Width = 288,
            Height = 214,
            Margin = new Thickness(0, 0, 14, 14),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(2),
            BorderBrush = (Brush)Application.Current.Resources["LineDivider"],
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = new TextBlock
            {
                Text = "+",
                FontSize = 34,
                Foreground = (Brush)Application.Current.Resources["LineDivider"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        tile.MouseLeftButtonUp += (_, _) => AddPop.IsOpen = true;
        return tile;
    }

    static TextBlock Caption(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["Muted"],
        Margin = new Thickness(0, 10, 0, 0),
    };

    static void PaintDots(UniformGrid host, int total, int lit)
    {
        host.Children.Clear();
        var accent = (Brush)Application.Current.Resources["Accent"];
        var pale = (Brush)Application.Current.Resources["LineDivider"];
        for (var i = 0; i < total; i++)
            host.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 5, 5),
                Background = i < lit ? accent : pale,
            });
    }

    static Geometry Arc(double cx, double cy, double r, double fraction)
    {
        if (fraction <= 0.0001) return Geometry.Empty;
        var start = -90 * Math.PI / 180;
        var sweep = Math.Min(359.9, 360 * fraction);
        var end = start + sweep * Math.PI / 180;
        var figure = new PathFigure
        {
            StartPoint = new Point(cx + r * Math.Cos(start), cy + r * Math.Sin(start)),
            IsClosed = false,
        };
        figure.Segments.Add(new ArcSegment(
            new Point(cx + r * Math.Cos(end), cy + r * Math.Sin(end)),
            new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise, true));
        var geo = new PathGeometry();
        geo.Figures.Add(figure);
        return geo;
    }

    // ------------------------------------------------------------------ 交互

    void BuildAddMenu()
    {
        AddMenu.Children.Clear();
        foreach (var (type, label, view, cols) in Catalog)
        {
            var btn = new Button
            {
                Content = label,
                Style = (Style)Application.Current.Resources["BtnGhost"],
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 2),
            };
            btn.Click += (_, _) =>
            {
                _state.Settings.Ui.Cards.Add(new CardPref
                {
                    Type = type,
                    View = view,
                    Cols = cols,
                    Source = type == "project" ? _selected : "",
                });
                _state.SaveSettings();
                AddPop.IsOpen = false;
                Load();
            };
            AddMenu.Children.Add(btn);
        }
    }

    void Add_Click(object sender, RoutedEventArgs e) => AddPop.IsOpen = true;

    void Edit_Click(object sender, RoutedEventArgs e)
    {
        _editing = !_editing;
        EditBtn.Content = _editing ? "完成" : "编辑";
        Load();
    }
}
