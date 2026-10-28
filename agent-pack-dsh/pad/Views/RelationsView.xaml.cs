using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Pad.Core;

namespace Pad.Views;

/// <summary>
/// 关系的树状架构图（从上到下）。用的是正统的整齐树布局：**子树定宽、父节点居中于子节点**，
/// 连线是从父节点底部到子节点顶部的直线——也就是组织架构图的画法（D39）。
///
/// 为什么不引库：MSAGL 只到 .NET Framework（restore 会报 NU1701），GraphX 3.0 是一整套
/// 图形控件、会推翻现有卡片语言，QuikGraph 只有数据结构没有布局。所以布局自己算，
/// 但算法是标准的，不是"摆摆看"。
/// </summary>
public partial class RelationsView : UserControl
{
    readonly AppState _state = AppState.Current;

    // 三种节点的固定尺寸：层级越深越窄，尺寸本身表达层级。
    const double WsW = 196, WsH = 54;
    const double InW = 176, InH = 56;
    const double AgW = 168, AgH = 48;
    const double SubW = 168, SubH = 44;

    const double SiblingGap = 18;    // 同级之间
    const double RootGap = 40;       // 根（工作区）之间
    const double LevelGap = 74;      // 层与层之间（留给连线）

    public RelationsView()
    {
        InitializeComponent();
        Loaded += (_, _) => Load();
    }

    /// <summary>布局用的节点：数据和几何放在一起，避免画的时候再查一次。</summary>
    sealed class Node
    {
        public required string Title;
        public string Sub = "";
        public string Foot = "";
        public string Badge = "";
        public int Kind;              // 0 工作区 · 1 实例 · 2 agent · 3 子 agent
        public bool Live;
        public Instance? Inst;
        public List<Node> Kids = [];

        public double W, H, X, Y;     // X = 中心，Y = 顶边
    }

    void Load()
    {
        try
        {
            var model = Graph.Build(_state.Launcher.Instances());
            Summary.Text =
                $"{model.Workspaces.Count} 工作区 · {model.Instances} 实例 · {model.Agents} agent · {model.Subagents} 子 agent";

            // ---- 建森林：工作区 → 实例 → agent → 子 agent ----
            var roots = new List<Node>();
            foreach (var ws in model.Workspaces)
            {
                var wsNode = new Node
                {
                    Kind = 0,
                    Title = ws.Label,
                    Sub = ws.Instances.Count + " 实例",
                    Foot = ws.Path,
                };

                foreach (var inst in ws.Instances)
                {
                    var inNode = new Node
                    {
                        Kind = 1,
                        Title = inst.Inst.Name,
                        Sub = inst.Total + " agent" + (inst.Subagents > 0 ? " / " + inst.Subagents + " 子" : ""),
                        Foot = inst.Running ? "在跑" : "未启动",
                        Live = inst.Running,
                        Inst = inst.Inst,
                    };

                    foreach (var agent in inst.Agents)
                        inNode.Kids.Add(AgentNode(agent));

                    wsNode.Kids.Add(inNode);
                }

                roots.Add(wsNode);
            }

            // ---- 布局：先量宽，再定位 ----
            foreach (var root in roots) Measure(root);
            var x = 0.0;
            foreach (var root in roots)
            {
                Place(root, x, 0);
                x += root.W + RootGap;
            }

            // ---- 画 ----
            var canvas = new Canvas();
            foreach (var root in roots) Draw(canvas, root);

            var width = roots.Count == 0 ? WsW + 40 : x - RootGap;
            var height = roots.Count == 0 ? 60 : MaxBottom(roots) + 10;
            canvas.Width = Math.Max(width, WsW + 40);
            canvas.Height = Math.Max(height, 60);

            // 布局可能比窗口宽：等比缩小到可见，而不是裁掉右边的子树。
            Host.Child = new Viewbox
            {
                Child = canvas,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(18, 12, 18, 12),
            };
        }
        catch (Exception ex)
        {
            Summary.Text = PadError.Describe(ex);
        }
    }

    Node AgentNode(Graph.Agent agent)
    {
        var isChild = agent.IsChild;
        var node = new Node
        {
            Kind = isChild ? 3 : 2,
            Title = isChild ? "└ " + agent.Title : agent.Title,
            Sub = Graph.Ago(agent.Touched),
            Badge = agent.Children.Count > 0 ? agent.Children.Count + " 子" : "",
        };
        foreach (var child in agent.Children) node.Kids.Add(AgentNode(child));
        return node;
    }

    // ------------------------------------------------------------------ 布局

    static void Size(Node n)
    {
        if (n.Kind == 0) { n.W = WsW; n.H = WsH; }
        else if (n.Kind == 1) { n.W = InW; n.H = InH; }
        else if (n.Kind == 2) { n.W = AgW; n.H = AgH; }
        else { n.W = SubW; n.H = SubH; }
    }

    /// <summary>子树定宽：叶子按自身宽，父节点按子节点总宽（不够就用自己的宽）。</summary>
    static double Measure(Node n)
    {
        Size(n);
        if (n.Kids.Count == 0) return n.W;

        var kidsW = n.Kids.Sum(Measure) + SiblingGap * (n.Kids.Count - 1);
        n.W = Math.Max(n.W, kidsW);
        return n.W;
    }

    /// <summary>父节点居中于第一个和最后一个子节点之间——这就是架构图看着"正"的原因。</summary>
    static void Place(Node n, double left, int level)
    {
        n.Y = level * (Math.Max(n.H, 56) + LevelGap);

        if (n.Kids.Count == 0)
        {
            n.X = left + n.W / 2;
            return;
        }

        // 子节点按自身宽度 + 间距依次排开，整体在父节点的宽度里居中。
        var kidsW = n.Kids.Sum(k => k.W) + SiblingGap * (n.Kids.Count - 1);
        var cursor = left + (n.W - kidsW) / 2;
        foreach (var kid in n.Kids)
        {
            Place(kid, cursor, level + 1);
            cursor += kid.W + SiblingGap;
        }

        n.X = (n.Kids[0].X + n.Kids[^1].X) / 2;
    }

    static double MaxBottom(List<Node> roots)
    {
        double Max(Node n) => n.Kids.Count == 0 ? n.Y + n.H : Math.Max(n.Y + n.H, n.Kids.Max(Max));
        return roots.Max(Max);
    }

    // ------------------------------------------------------------------ 绘制

    void Draw(Canvas canvas, Node n)
    {
        // 连线先画，卡片压在上面：父底边中点 → 子顶边中点，直线。
        foreach (var kid in n.Kids)
        {
            canvas.Children.Add(Line(
                n.X, n.Y + n.H, kid.X, kid.Y, kid.Live || n.Live));
            Draw(canvas, kid);
        }

        var card = CardImage(n);
        Canvas.SetLeft(card, n.X - n.W / 2);
        Canvas.SetTop(card, n.Y);
        canvas.Children.Add(card);
    }

    UIElement CardImage(Node n)
    {
        var stack = new StackPanel();

        if (n.Kind == 1 && n.Inst is not null)
        {
            // 实例卡带图标：自己设的 logo → 项目 logo → 鲸鱼。
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(7),
                Background = IconBrush(n.Inst) ?? Wash(),
                VerticalAlignment = VerticalAlignment.Center,
            });
            head.Children.Add(new TextBlock
            {
                Text = n.Title,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Ink(),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            stack.Children.Add(head);
        }
        else
        {
            stack.Children.Add(new TextBlock
            {
                Text = n.Title,
                FontSize = n.Kind == 0 ? 13 : n.Kind == 2 ? 11 : 10,
                FontWeight = n.Kind is 0 or 2 ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = n.Kind == 3 ? Muted() : Ink(),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 30,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 0),
        };
        if (n.Sub.Length > 0)
            line.Children.Add(new TextBlock
            {
                Text = n.Sub,
                FontSize = 9,
                Foreground = n.Live ? Accent() : Muted(),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        if (n.Badge.Length > 0)
            line.Children.Add(new TextBlock
            {
                Text = " · " + n.Badge,
                FontSize = 9,
                Foreground = Accent(),
            });
        if (line.Children.Count > 0) stack.Children.Add(line);

        if (n.Kind <= 1)
            stack.Children.Add(new TextBlock
            {
                Text = n.Foot,
                FontSize = 9,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = n.Live ? Accent() : Muted(),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = n.W - 20,
            });

        return new Border
        {
            Width = n.W,
            Height = n.H,
            Padding = new Thickness(10, 7, 10, 7),
            CornerRadius = new CornerRadius(12),
            Background = n.Kind == 3 ? Brushes.Transparent : CardBg(),
            BorderThickness = new Thickness(n.Live ? 2 : 1),
            BorderBrush = n.Live ? Accent() : n.Kind == 3 ? Wash() : Divider(),
            Child = stack,
        };
    }

    /// <summary>父子之间一根直线；在跑的路径用强调色，其余淡色。</summary>
    static Path Line(double x1, double y1, double x2, double y2, bool live)
    {
        var figure = new PathFigure { StartPoint = new Point(x1, y1), IsClosed = false };
        figure.Segments.Add(new LineSegment(new Point(x2, y2), true));
        var geo = new PathGeometry();
        geo.Figures.Add(figure);
        return new Path
        {
            Data = geo,
            Stroke = live ? Accent() : Divider(),
            StrokeThickness = live ? 1.8 : 1.2,
            StrokeDashArray = [4, 4],
            StrokeDashCap = PenLineCap.Round,
        };
    }

    static Brush Ink() => (Brush)Application.Current.Resources["TextPrimary"];
    static Brush Muted() => (Brush)Application.Current.Resources["TextMuted"];
    static Brush Accent() => (Brush)Application.Current.Resources["Accent"];
    static Brush Divider() => (Brush)Application.Current.Resources["LineBorder"];
    static Brush Wash() => (Brush)Application.Current.Resources["AccentWash"];
    static Brush CardBg() => (Brush)Application.Current.Resources["BgCard"];

    static Brush? IconBrush(Instance inst)
    {
        var candidates = new List<string?>();
        if (!string.IsNullOrWhiteSpace(inst.Display?.Logo))
            candidates.Add(Skin.Resolve(inst.Display!.Logo!));
        candidates.Add(InstIcon.MarkFile(inst));

        foreach (var path in candidates)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) continue;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path!, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
            }
            catch { /* undecodable: next */ }
        }
        return null;
    }
}
