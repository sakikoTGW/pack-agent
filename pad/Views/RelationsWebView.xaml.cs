using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Pad.Core;
using IOPath = System.IO.Path;

namespace Pad.Views;

/// <summary>
/// 关系页：C# 只提供**数据集**，视图长什么样由 JSON 定义（Core/Views.cs）决定。
/// 加一个视图 = 加一份 JSON；加一种数据 = 这里加一个数据集（D41）。
/// </summary>
public partial class RelationsWebView : UserControl
{
    readonly AppState _state = AppState.Current;
    UiState _ui = new();
    string _selectedInstance = "";
    readonly Dictionary<string, (string ws, string inst)> _ids = new(StringComparer.OrdinalIgnoreCase);
    bool _ready;
    Graph.Model? _model;
    string _viewId = "sessions";

    /// <summary>舞台侧栏（MainWindow 持有）：挂接进来，本页不拥有它。</summary>
    ProjectRail? _rail;

    /// <summary>返回入口由宿主给：回管理卡片页也是舞台内的一次重组。</summary>
    public Action? GoBack { get; set; }

    public RelationsWebView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await Start();
    }

    /// <summary>
    /// 挂接舞台侧栏。页面是缓存的：重复上台不重复订阅；若数据已就绪，
    /// 立刻把树放回侧栏——侧栏还是那个侧栏，内容在列表和树之间重组。
    /// </summary>
    public void AttachRail(ProjectRail rail)
    {
        if (ReferenceEquals(_rail, rail))
        {
            if (_model is not null) PaintSidebar(_model);
            return;
        }
        if (_rail is not null)
        {
            _rail.Toggled -= OnToggle;
            _rail.Activated -= OnFocus;
            _rail.Picked -= OnPicked;
        }
        _rail = rail;
        _rail.Toggled += OnToggle;
        _rail.Activated += OnFocus;
        _rail.Picked += OnPicked;
        if (_model is not null) PaintSidebar(_model);
    }

    void Back_Click(object sender, RoutedEventArgs e) => GoBack?.Invoke();

    async Task Start()
    {
        try
        {
            var profile = IOPath.Combine(_state.Launcher.Root, "webview");
            Directory.CreateDirectory(profile);

            var env = await CoreWebView2Environment.CreateAsync(null, profile);
            await Web.EnsureCoreWebView2Async(env);

            Web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Web.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (!e.Uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
            };

            var page = IOPath.Combine(AppContext.BaseDirectory, "Assets", "web", "page.html");
            if (!File.Exists(page))
            {
                Summary.Text = "缺少 Assets/web/page.html";
                return;
            }

            _ui = UiState.Load(_state.Launcher.Root);

            // 页面把展开状态推回来，落盘；不打断页面自身。
            // focus 一并收：图卡片点击聚焦是页面发起的，落盘后刷新还在。
            Web.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("active", out var a) && a.ValueKind == JsonValueKind.String)
                        _ui.Active = a.GetString() ?? "";
                    if (root.TryGetProperty("focus", out var f) && f.ValueKind == JsonValueKind.String)
                        _ui.Focus[_viewId] = f.GetString() ?? "root";
                    if (root.TryGetProperty("view", out var v) && v.ValueKind == JsonValueKind.String &&
                        root.TryGetProperty("ids", out var ids) && ids.ValueKind == JsonValueKind.Array)
                    {
                        _ui.Set(v.GetString() ?? "", ids.EnumerateArray().Select(x => x.GetString() ?? ""));
                    }
                    _ui.Save(_state.Launcher.Root);
                }
                catch { /* 坏消息忽略 */ }
            };

            Web.CoreWebView2.NavigationCompleted += async (_, _) =>
            {
                _ready = true;
                await Push();
            };
            Web.CoreWebView2.Navigate(new Uri(page).AbsoluteUri);
        }
        catch (Exception ex)
        {
            Summary.Text = PadError.Describe(ex);
        }
    }

    async Task Push()
    {
        if (!_ready) return;
        try
        {
            var model = Graph.Build(_state.Launcher.Instances());
            Summary.Text =
                $"{model.Workspaces.Count} 工作区 · {model.Instances} 实例 · {model.Agents} agent · {model.Subagents} 子 agent";

            // 同指纹分组：Tree() 里的 twin 字段从这里查
            TwinNames.Clear();
            foreach (var g in model.Workspaces.SelectMany(w => w.Instances)
                         .GroupBy(i => i.Code).Where(g => g.Count() > 1 && !string.IsNullOrWhiteSpace(g.Key)))
                TwinNames[g.Key] = string.Join("、", g.Select(x => x.Inst.Name));

            PaintSidebar(model);

            var specs = ViewCatalog.Load(_state.Launcher.Root);

            var tables = specs
                .Select(spec => new
                {
                    id = spec.Id,
                    title = spec.Title,
                    orientation = spec.Orientation,
                    renderer = spec.Renderer,
                    note = spec.Note,
                    columns = spec.Columns.Select(c => new
                    {
                        key = c.Key,
                        label = c.Label,
                        width = c.Width,
                        align = c.Align,
                    }).ToArray(),
                    rows = Rows(spec.Source, model),
                    tree = Tree(spec.Source, model),
                })
                .ToArray();

            // shot 页点名视图时优先（自证要确定性）；平时优先用上次的选择
            var forced = App.ShotPage == "relations-icicle" ? "sessions-icicle"
                : App.ShotPage.StartsWith("relations-sessions") ? "sessions"
                : App.ShotPage == "relations" ? "projects"
                : "";
            var active = forced != "" && tables.Any(t => t.id == forced)
                ? forced
                : !string.IsNullOrWhiteSpace(_ui.Active) && tables.Any(t => t.id == _ui.Active)
                    ? _ui.Active
                    : tables.FirstOrDefault()?.id;

            var payload = new
            {
                active,
                expanded = _ui.Views.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()),
                focus = _ui.Focus.TryGetValue(_viewId, out var savedFocus) ? savedFocus : "root",
                tables,
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
            await Web.CoreWebView2.ExecuteScriptAsync($"render({json});");

            // 验证用：按页面参数控制展开到第几层
            switch (App.ShotPage)
            {
                case "relations-sessions-expanded":
                    await Web.CoreWebView2.ExecuteScriptAsync("expandAll();");
                    break;
                case "relations-sessions-one":
                    // 只展开 dsh/workspace 这一层（工作区 -> 实例）
                    await Web.CoreWebView2.ExecuteScriptAsync("expandIds(['root.1']);");
                    break;
                case "relations-sessions-two":
                    // 再展开 本机 DSH 这一层（实例 -> agent）
                    await Web.CoreWebView2.ExecuteScriptAsync("expandIds(['root.1','root.1.0']);");
                    break;
                case "relations-sessions-search":
                    await Web.CoreWebView2.ExecuteScriptAsync("setQuery(\"dsh\");");
                    break;
            }

            await CaptureForShot();
        }
        catch (Exception ex)
        {
            Summary.Text = PadError.Describe(ex);
        }
    }

    /// <summary>
    /// The rail is the same control the card page uses, and it also carries the
    /// expand/collapse actions: the sidebar is the operator, the graph only reflects it.
    /// Key 用稳定标识（工作区路径 / 实例 id / 会话 id）：切模式、展开收起时，
    /// 侧栏按 key 做 FLIP 换位——同一批元素在重新排位，不是重建。
    /// </summary>
    void PaintSidebar(Graph.Model model)
    {
        _model = model;
        _ids.Clear();

        var open = Open(_viewId);
        var nodes = new List<ProjectRail.Node>();
        var i = 0;

        foreach (var ws in model.Workspaces)
        {
            var wsId = "root." + i;
            var wsOpen = open.Contains(wsId);
            nodes.Add(new ProjectRail.Node(wsId, ws.Label,
                ws.Instances.Count + " 实例 · " + ws.Instances.Sum(x => x.Total) + " agent",
                0, ws.Instances.Any(x => x.Running), wsOpen, ws.Instances.Count,
                Key: "ws:" + ws.Path.ToLowerInvariant()));

            var j = 0;
            foreach (var inst in ws.Instances)
            {
                var inId = wsId + "." + j;
                var inOpen = open.Contains(inId);
                _ids[inst.Inst.Id] = (wsId, inId);
                nodes.Add(new ProjectRail.Node(inId, inst.Inst.Name,
                    inst.Total + " agent" + (inst.Subagents > 0 ? " / " + inst.Subagents + " 子" : ""),
                    1, inst.Running, inOpen, inst.Agents.Count,
                    Key: "inst:" + inst.Inst.Id, Inst: inst.Inst));

                if (inOpen)
                {
                    var k = 0;
                    foreach (var agent in inst.Agents)
                    {
                        nodes.Add(new ProjectRail.Node(inId + "." + k, agent.Title,
                            Graph.Ago(agent.Touched) + (agent.Children.Count > 0 ? " · " + agent.Children.Count + " 子" : ""),
                            2, false, false, 0,
                            Key: "agent:" + agent.Id));
                        k++;
                    }
                }
                j++;
            }
            i++;
        }

        _rail?.FillTree(nodes);
    }

    /// <summary>当前视图的展开集合；宿主与页面共用同一份（写在 ui-state.json）。</summary>
    HashSet<string> Open(string viewId)
    {
        if (!_ui.Views.TryGetValue(viewId, out var set))
        {
            set = new HashSet<string>(StringComparer.Ordinal) { "root" };
            _ui.Views[viewId] = set;
        }
        set.Add("root");
        return set;
    }

    /// <summary>侧栏点箭头：改状态 → 落盘 → 推给页面（页面不自己决定展开什么）。</summary>
    async void OnToggle(ProjectRail.Node node)    {
        var open = Open(_viewId);
        if (!open.Remove(node.Id)) open.Add(node.Id);
        _ui.Save(_state.Launcher.Root);

        if (_model is not null) PaintSidebar(_model);

        if (!_ready) return;
        try
        {
            var ids = "[" + string.Join(",", open
                .Where(id => id != "root")
                .Select(id => "\"" + id + "\"")) + "]";
            await Web.CoreWebView2.ExecuteScriptAsync("setOpen(" + ids + ");");
        }
        catch { /* page not ready */ }
    }

    /// <summary>侧栏点行选实例：图里把它的两层展开。</summary>
    async void OnPicked(Instance inst)
    {
        _selectedInstance = inst.Id;
        if (!_ready || !_ids.TryGetValue(inst.Id, out var ids)) return;
        try
        {
            var args = "[\"" + ids.ws + "\",\"" + ids.inst + "\"]";
            await Web.CoreWebView2.ExecuteScriptAsync("expandIds(" + args + ");");
        }
        catch { /* page not ready */ }
    }

    /// <summary>Rail row click: focus the graph on that subtree and remember it.
    /// 侧栏行就地高亮（FocusKey），不重建列表。</summary>
    async void OnFocus(ProjectRail.Node node)
    {
        _ui.Focus[_viewId] = node.Id;
        _ui.Save(_state.Launcher.Root);
        if (_rail is not null)
            _rail.FocusKey = node.Key.Length > 0 ? node.Key : node.Id;
        if (!_ready) return;
        try
        {
            await Web.CoreWebView2.ExecuteScriptAsync("setFocus(\"" + node.Id + "\");");
        }
        catch { /* page not ready */ }
    }

    /// <summary>数据集：视图 JSON 里的 source 指向这里。加一种数据才需要改代码。</summary>
    static IReadOnlyList<Dictionary<string, object?>> Rows(string source, Graph.Model model) =>
        source switch
        {
            "sessions" => SessionRows(model),
            _ => ProjectRows(model),
        };

    /// <summary>图数据：同一个 source，换个镜头就是另一棵树。
    /// 卡片上只放得下的信息用视觉通道表达（图标/热度/幽灵态/规模条），路径、指纹这类长事实走 tooltip，
    /// 所以每个节点都带全量字段，页面自己决定画哪些（D47）。</summary>
    static object Tree(string source, Graph.Model model) => new
    {
        name = source == "sessions" ? "全部" : "本机",
        kind = -1,
        meta = "",
        badge = "",
        live = false,
        path = "",
        fp = "",
        twin = "",
        icon = "",
        ageMin = -1d,
        ctx = 0L,
        win = 0L,
        comp = 0,
        children = model.Workspaces.Select(ws => new
        {
            name = ws.Label,
            kind = 0,
            meta = ws.Instances.Count + " 实例 · " + ws.Instances.Sum(i => i.Total) + " agent",
            badge = ws.Instances.Count(i => i.Running) is var r && r > 0 ? r + " 在跑" : "",
            live = ws.Instances.Any(i => i.Running),
            path = ws.Path,
            fp = "",
            twin = "",
            icon = "",
            ageMin = -1d,
            ctx = ws.Instances.Sum(i => i.Agents.Sum(CtxOf)),
            win = 0L,
            comp = 0,
            children = ws.Instances.Select(inst => new
            {
                name = inst.Inst.Name,
                kind = 1,
                meta = inst.Total + " agent",
                badge = inst.Subagents > 0 ? inst.Subagents + " 子" : "",
                live = inst.Running,
                path = inst.Inst.Home,
                fp = inst.Code,
                twin = Twins(inst.Code),
                icon = IconDataUrl(inst.Inst),
                ageMin = -1d,
                ctx = inst.Agents.Sum(CtxOf),
                win = 0L,
                comp = 0,
                // 按项目视角只到实例；按会话视角继续展开到 agent 与子 agent
                children = source == "sessions"
                    ? inst.Agents.Select(AgentNode).ToArray()
                    : [],
            }).ToArray(),
        }).ToArray(),
    };

    static readonly Dictionary<string, string> TwinNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>同指纹 = 同一份 DSH（不变量 4）。给 tooltip 一句"同源"，图面不加字。</summary>
    static string Twins(string code) =>
        string.IsNullOrWhiteSpace(code) ? "" : TwinNames.TryGetValue(code, out var names) ? names : "";

    /// <summary>子树上下文合计：图上实例/工作区的规模条用的是这个聚合值。</summary>
    static long CtxOf(Graph.Agent a) => a.Ctx + a.Children.Sum(CtxOf);

    static object AgentNode(Graph.Agent agent) => new
    {
        name = agent.Title,
        kind = agent.IsChild ? 3 : 2,
        meta = Graph.Ago(agent.Touched),
        badge = agent.Children.Count > 0 ? agent.Children.Count + " 子" : "",
        live = false,
        path = "",
        fp = "",
        twin = "",
        icon = "",
        ageMin = Graph.AgeMinutes(agent.Touched),
        ctx = agent.Ctx,
        win = agent.Win,
        comp = agent.Compactions,
        children = agent.Children.Select(AgentNode).ToArray(),
    };

    /// <summary>
    /// 图里的实例图标，编码成 data URL（页面在 file:// 下，引用外部路径不可靠）。
    /// 优先级和侧栏一致：自己设的 → 整合包图 / 载体图标 → 空（空就画状态点）。
    /// </summary>
    static string IconDataUrl(Instance inst)
    {
        var path = !string.IsNullOrWhiteSpace(inst.Display?.Logo)
            ? Skin.Resolve(inst.Display!.Logo!)
            : "";
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            path = InstIcon.MarkFile(inst) ?? "";
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "";

        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length is 0 or > 512_000) return "";
            var mime = IOPath.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                _ => "",
            };
            return mime == "" ? "" : $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }
        catch { return ""; }
    }

    static IReadOnlyList<Dictionary<string, object?>> ProjectRows(Graph.Model model)
    {
        var rows = new List<Dictionary<string, object?>>();
        foreach (var ws in model.Workspaces)
        {
            rows.Add(new Dictionary<string, object?>
            {
                ["label"] = ws.Label,
                ["instances"] = ws.Instances.Count,
                ["agents"] = ws.Instances.Sum(i => i.Total),
                ["running"] = ws.Instances.Count(i => i.Running),
                ["path"] = ws.Path,
            });
        }
        return rows;
    }

    static IReadOnlyList<Dictionary<string, object?>> SessionRows(Graph.Model model)
    {
        var rows = new List<Dictionary<string, object?>>();

        foreach (var ws in model.Workspaces)
        foreach (var inst in ws.Instances)
            Walk(inst.Agents, ws.Label, inst.Inst.Name, 0);

        return rows;

        void Walk(IReadOnlyList<Graph.Agent> agents, string project, string instance, int depth)
        {
            foreach (var agent in agents)
            {
                rows.Add(new Dictionary<string, object?>
                {
                    ["title"] = agent.Title,
                    ["project"] = project,
                    ["instance"] = instance,
                    ["state"] = agent.IsChild ? "子 agent" : "主 agent",
                    ["when"] = Graph.Ago(agent.Touched),
                    // 缩进和热度都由页面按这两个字段画：空格在 HTML 里会被折叠，靠不住
                    ["depth"] = depth,
                    ["ageMin"] = Graph.AgeMinutes(agent.Touched),
                });
                Walk(agent.Children, project, instance, depth + 1);
            }
        }
    }

    /// <summary>
    /// WebView2 是独立合成层，不在 RenderTargetBitmap 里 —— `--shot` 拍到的永远是空白。
    /// 自证时用它自己的截图另存一张。
    /// </summary>
    /// <summary>Shot-only: pad an opaque background so CapturePreview is not black.</summary>
    const string PadWhite = "document.body.style.background='#fafcfd';";

    const string PadClear = "document.body.style.background='transparent';";

    async Task CaptureForShot()
    {
        if (App.ShotPath is null) return;
        try
        {
            // CapturePreview 会把透明背景拍成黑色：先垫一层白，再拍，再恢复。
            await Web.CoreWebView2.ExecuteScriptAsync(PadWhite);
            await Task.Delay(1200);
            var file = IOPath.Combine(
                IOPath.GetDirectoryName(App.ShotPath) ?? ".",
                IOPath.GetFileNameWithoutExtension(App.ShotPath) + ".web.png");
            await using (var stream = File.Create(file))
            {
                await Web.CoreWebView2.CapturePreviewAsync(
                    CoreWebView2CapturePreviewImageFormat.Png, stream);
            }
            await Web.CoreWebView2.ExecuteScriptAsync(PadClear);
            Summary.Text += $"  ·  web 截图已存 {IOPath.GetFileName(file)}";
        }
        catch (Exception ex)
        {
            Summary.Text += "  ·  web 截图失败：" + ex.Message;
        }
    }
}
