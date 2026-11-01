using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Pad.Core;

namespace Pad.Views;

public partial class ManageView : UserControl, IRefreshable
{
    readonly MainWindow _win;
    readonly AppState _state = AppState.Current;
    readonly Packs _packs;
    List<WorkspaceNode> _nodes = [];
    bool _loadingBundles;
    bool _bundleBusy;
    CancellationTokenSource? _iconCts;

    /// <summary>实例列表的一行：名字/版本 + 运行状态（启动器先要能看出哪个在跑）。</summary>
    public sealed class InstRow
    {
        public required Instance Inst { get; init; }
        public bool Live { get; init; }
        public string Name => Inst.Name;
        public string Version => Inst.Dsh.Version;
        public string StateText => Live ? "在跑" : "已停";
    }

    public ManageView(MainWindow win)
    {
        InitializeComponent();
        _win = win;
        _packs = new Packs(_state.Launcher);
        DataContext = _state;
    }

    public void OnShown()
    {
        _state.RefreshRunning();
        PaintInstances();
        _ = Refresh();
    }

    /// <summary>实例列表带运行状态；状态随 RefreshRunning 变化就地重画。</summary>
    void PaintInstances()
    {
        var keep = _state.SelectedInstance;
        var rows = _state.Instances
            .Select(i => new InstRow
            {
                Inst = i,
                Live = _state.Running.Any(r => r.Entry.Instance == i.Id),
            })
            .ToList();
        InstList.ItemsSource = rows;
        if (keep is not null)
            InstList.SelectedItem = rows.FirstOrDefault(r => r.Inst.Id == keep.Id);
        else if (rows.Count > 0)
            InstList.SelectedItem = rows[0];
    }

    void SelectInstance(Instance? inst)
    {
        if (inst is null)
        {
            InstList.SelectedItem = null;
            return;
        }
        foreach (var row in InstList.Items)
        {
            if (row is InstRow r && r.Inst.Id == inst.Id)
            {
                InstList.SelectedItem = row;
                return;
            }
        }
    }

    void Inst_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (InstList.SelectedItem is InstRow row)
            _state.SelectedInstance = row.Inst;
        BundleLog.Clear();
        BundleLog.Height = 0;
        BundleLog.Visibility = Visibility.Collapsed;
        _ = Refresh();
    }

    void Probe_Click(object sender, RoutedEventArgs e) => _ = Refresh();

    void Waiting(bool busy)
    {
        ProbeSpinner.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        TreeSkeleton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy) Tree.ItemsSource = null;
    }

    void Count(string text)
    {
        TreeCount.Content = text;
        TreeCount.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    void Hint(string text)
    {
        TreeHint.Text = text;
        TreeHint.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    void KeyFaultOff()
    {
        KeyFault.Text = "";
        KeyFault.Visibility = Visibility.Collapsed;
    }

    void KeyFaultOn(string location)
    {
        KeyFault.Text = CredentialsFile.MissingError(location).Render();
        KeyFault.Visibility = Visibility.Visible;
    }

    void Filter_Click(object sender, RoutedEventArgs e) => Paint();

    void Paint()
    {
        var live = OnlyLive.IsChecked == true;
        var shown = live
            ? _nodes.Where(n => n.RunningCount > 0)
                .Select(n => new WorkspaceNode(n.Row, n.Sessions.Where(s => s.Running).ToList()))
                .ToList()
            : _nodes;

        Tree.ItemsSource = shown;
        var sessions = shown.Sum(n => n.Sessions.Count);
        Count($"{shown.Count} 个 workspace · {sessions} 条 session");

        Hint((live, sessions, _nodes.Sum(n => n.Sessions.Count)) switch
        {
            (true, 0, > 0) => "没有 agent 在跑",
            _ => "",
        });
    }

    RunVm? LiveRun()
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return null;
        return _state.Running.FirstOrDefault(r => r.Entry.Instance == inst.Id);
    }

    void PaintAlive(RunVm? run)
    {
        var on = run is not null;
        StopBtn.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        GoRunBtn.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
    }

    void BundleProfile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingBundles) return;
        PaintBundleRows();
    }

    void LoadBundles()
    {
        var inst = _state.SelectedInstance;
        if (inst is null)
        {
            _iconCts?.Cancel();
            _loadingBundles = true;
            try
            {
                BundleProfile.ItemsSource = null;
                BundleList.ItemsSource = null;
            }
            finally { _loadingBundles = false; }
            BundleEmpty.Visibility = Visibility.Collapsed;
            return;
        }

        var names = _state.Launcher.Profiles(inst).Select(p => p.Name).ToList();
        var keep = BundleProfile.SelectedItem as string;
        var pick = keep is not null && names.Contains(keep) ? keep
            : inst.LastProfile is not null && names.Contains(inst.LastProfile) ? inst.LastProfile
            : names.FirstOrDefault();

        _loadingBundles = true;
        try
        {
            BundleProfile.ItemsSource = names;
            BundleProfile.SelectedItem = pick;
        }
        finally { _loadingBundles = false; }
        PaintBundleRows();
        _ = FetchIcons(inst);
    }

    async Task FetchIcons(Instance inst)
    {
        _iconCts?.Cancel();
        var cts = new CancellationTokenSource();
        _iconCts = cts;
        try
        {
            await _state.Launcher.EnsureBundleIcons(inst, cts.Token);
            if (cts.IsCancellationRequested) return;
            await Dispatcher.InvokeAsync(() =>
            {
                if (!ReferenceEquals(_iconCts, cts)) return;
                if (_state.SelectedInstance?.Id != inst.Id) return;
                PaintBundleRows();
            });
        }
        catch (OperationCanceledException) { }
        catch { /* jsDelivr / GitHub unreachable; Kind label stays */ }
    }

    void PaintBundleRows()
    {
        var inst = _state.SelectedInstance;
        if (inst is null)
        {
            BundleList.ItemsSource = null;
            BundleEmpty.Visibility = Visibility.Collapsed;
            return;
        }

        var pick = BundleProfile.SelectedItem as string;
        var rows = _state.Launcher.ListBundles(inst)
            .Where(r => pick is null || r.Profile == pick)
            .ToList();
        foreach (var r in rows)
        {
            if (r.IconPath.Length == 0) continue;
            if (Skin.LoadBitmap(r.IconPath) is null) r.IconPath = "";
        }
        BundleList.ItemsSource = rows;
        if (pick is null)
        {
            BundleEmpty.Text = "这份实例还没有 profile";
            BundleEmpty.Visibility = Visibility.Visible;
        }
        else if (rows.Count == 0)
        {
            BundleEmpty.Text = "这个 --profile 还没有插件";
            BundleEmpty.Visibility = Visibility.Visible;
        }
        else
        {
            BundleEmpty.Visibility = Visibility.Collapsed;
        }
    }

    void BundleSay(string line)
    {
        BundleLog.Visibility = Visibility.Visible;
        BundleLog.Height = 130;
        BundleLog.AppendText(line + Environment.NewLine);
        BundleLog.ScrollToEnd();
    }

    void BundleBusy(bool on)
    {
        _bundleBusy = on;
        BundleAddBtn.IsEnabled = !on;
        BundleUpdateAllBtn.IsEnabled = !on;
        BundleSpinner.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    string? SelectedBundleProfile() => BundleProfile.SelectedItem as string;

    async Task RunPlugin(string action, string spec, string profile)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        BundleSay($"{action} {spec} → {profile}");
        await _state.Launcher.PluginOp(inst.Id, action, spec,
            new Progress<string>(BundleSay), CancellationToken.None, profile);
        BundleSay("下次启动生效。");
        LoadBundles();
    }

    async void BundleImport_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        if (_packs.Unavailable() is { } missing)
        {
            _win.Toast($"{missing.Reason}。{missing.Detail}", bad: true);
            return;
        }
        var dlg = new OpenFileDialog
        {
            Title = "选一个整合包",
            Filter = "整合包 (*.pack.zip;*.pack.json;*.pinst.zip)|*.pack.zip;*.pack.json;*.pinst.zip|所有文件|*.*",
        };
        if (dlg.ShowDialog(_win) != true) return;
        BundleBusy(true);
        var log = new Progress<string>(BundleSay);
        try
        {
            BundleSay($"装 {Path.GetFileName(dlg.FileName)} → {inst.Name}");
            await _packs.Project(inst, dlg.FileName, log, CancellationToken.None);
            LoadBundles();
        }
        catch (Exception ex)
        {
            BundleSay(PadError.Describe(ex));
            _win.Toast(PadError.Describe(ex), bad: true);
        }
        finally { BundleBusy(false); }
    }

    async void BundleAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_bundleBusy) return;
        var spec = BundleSpec.Text.Trim();
        if (spec.Length == 0) { BundleSpec.Focus(); return; }
        var profile = SelectedBundleProfile();
        if (string.IsNullOrEmpty(profile))
        {
            BundleSay("先选一个 --profile。");
            return;
        }
        BundleBusy(true);
        try
        {
            await RunPlugin("add", spec, profile);
            BundleSpec.Clear();
            _win.Toast($"add {spec} 下次启动生效");
        }
        catch (Exception ex)
        {
            BundleSay(PadError.Describe(ex));
            _win.Toast(PadError.Describe(ex), bad: true);
        }
        finally { BundleBusy(false); }
    }

    async void BundleUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_bundleBusy) return;
        if (sender is not FrameworkElement { Tag: BundleRow row }) return;
        BundleBusy(true);
        try
        {
            await RunPlugin("update", row.Spec, row.Profile);
            _win.Toast($"update {row.Spec} 下次启动生效");
        }
        catch (Exception ex)
        {
            BundleSay(PadError.Describe(ex));
            _win.Toast(PadError.Describe(ex), bad: true);
        }
        finally { BundleBusy(false); }
    }

    async void BundleRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_bundleBusy) return;
        if (sender is not FrameworkElement { Tag: BundleRow row }) return;
        var ask = MessageBox.Show(
            $"从 {row.Profile} 移除 {row.Spec}？",
            "PAD", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (ask != MessageBoxResult.OK) return;
        BundleBusy(true);
        try
        {
            await RunPlugin("remove", row.Spec, row.Profile);
            _win.Toast($"remove {row.Spec} 下次启动生效");
        }
        catch (Exception ex)
        {
            BundleSay(PadError.Describe(ex));
            _win.Toast(PadError.Describe(ex), bad: true);
        }
        finally { BundleBusy(false); }
    }

    async void BundleUpdateAll_Click(object sender, RoutedEventArgs e)
    {
        if (_bundleBusy) return;
        var inst = _state.SelectedInstance;
        var profile = SelectedBundleProfile();
        if (inst is null || string.IsNullOrEmpty(profile)) return;
        var rows = _state.Launcher.ListBundles(inst)
            .Where(r => r.Profile == profile && LaunchPolicy.IsProfileLayer(r.Spec))
            .ToList();
        if (rows.Count == 0)
        {
            BundleSay("没有可更新的插件。");
            return;
        }
        BundleBusy(true);
        try
        {
            foreach (var row in rows)
                await RunPlugin("update", row.Spec, row.Profile);
            _win.Toast($"{profile} 已全部更新，下次启动生效");
        }
        catch (Exception ex)
        {
            BundleSay(PadError.Describe(ex));
            _win.Toast(PadError.Describe(ex), bad: true);
        }
        finally { BundleBusy(false); }
    }

    /// <summary>
    /// Facts only: which surfaces PAD started for this home, on which port. No verdicts —
    /// PAD is a launcher (D18); it just shows the ledger it already keeps.
    /// </summary>
    void PaintNodes(Instance? inst)
    {
        if (inst is null || string.IsNullOrWhiteSpace(inst.Home))
        {
            NodesLine.Text = "";
            return;
        }

        var parts = new List<string>();
        try
        {
            foreach (var e in Nodes.Ledger.Load(inst.Home))
                if (Proc.Alive(e.Pid)) parts.Add($"{e.Profile}:{e.Port}");
        }
        catch
        {
            NodesLine.Text = "";
            return;
        }

        NodesLine.Text = parts.Count == 0 ? "" : "运行中 " + string.Join(" · ", parts);
    }

    async Task Refresh()
    {        LoadBundles();
        var inst = _state.SelectedInstance;
        if (inst is null)
        {
            TargetTitle.Text = "管理口";
            StateText.Text = "";
            AdBox.Content = null;
            Hint("");
            KeyFaultOff();
            Waiting(false);
            Count("");
            PaintAlive(null);
            PaintNodes(null);
            return;
        }

        var run = LiveRun();
        PaintAlive(run);
        PaintNodes(inst);
        TargetTitle.Text = run is null
            ? $"{inst.Name} · {inst.Dsh.Version}"
            : $"{run.InstanceName} · {run.Profile}";
        AdBox.Content = null;
        Hint("");
        KeyFaultOff();
        Count("");

        if (run is null)
        {
            StateText.Text = "还没启动";
            Waiting(false);
            _nodes = [];
            Tree.ItemsSource = null;
            TreeHint.Text = "这个实例还没启动。启动后这里会列出正在跑的 agent 会话。";
            TreeHint.Visibility = Visibility.Visible;
            return;
        }

        StateText.Text = "正在探测…";
        Waiting(true);

        var probe = await Gateway.Probe(inst, anyRunning: true, CancellationToken.None);
        StateText.Text = probe.Message;
        GwPip.Background = probe.State == GatewayState.Ready
            ? (Brush)Application.Current.FindResource("Accent")
            : probe.State == GatewayState.NoAd
                ? (Brush)Application.Current.FindResource("Warning")
                : (Brush)Application.Current.FindResource("InkDisabled");

        AdBox.Content = probe.Ad is null
            ? Gateway.AdPath(inst.Home)
            : $"{probe.Ad.Url}";

        if (probe.State != GatewayState.Ready || probe.Ad is null)
        {
            Waiting(false);
            _nodes = [];
            Tree.ItemsSource = null;
            TreeHint.Text = probe.State == GatewayState.NoAd
                ? "这份实例没有管理口，看不到正在聊的会话。"
                : probe.Message;
            TreeHint.Visibility = Visibility.Visible;
            run.Agents = -1;
            return;
        }

        try
        {
            _nodes = await Gateway.Tree(probe.Ad, CancellationToken.None);
            Waiting(false);
            run.Agents = _nodes.Sum(n => n.RunningCount);
            var liveKey = await Gateway.DeepseekConfigured(probe.Ad, CancellationToken.None);
            if (liveKey == false)
            {
                run.KeyMissing = true;
                KeyFaultOn(inst.Home);
            }
            else
            {
                run.KeyMissing = liveKey == true ? false : run.KeyMissing;
            }
            Paint();
        }
        catch (Exception ex)
        {
            Waiting(false);
            _nodes = [];
            Tree.ItemsSource = null;
            TreeHint.Text = PadError.Describe(ex);
            run.Agents = -1;
        }
    }

    void ShowGatewayError(Instance inst, string code, string headline, string? detail)
    {
        var text = new PadError(code, headline, inst.Home, detail, ["pad cli gateway " + inst.Id]).Render();

        var open = new Button { Content = "打开 pad-gateway.json", Width = 180, Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "关闭", Width = 70, IsDefault = true, IsCancel = true };
        var dlg = new Window
        {
            Title = "PAD",
            Width = 520, Height = 270,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock
                    {
                        Text = text,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 460,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 16, 0, 0),
                        Children = { open, close },
                    },
                },
            },
        };
        open.Click += (_, _) => { Reveal.Path(Gateway.AdPath(inst.Home)); dlg.Close(); };
        close.Click += (_, _) => dlg.Close();
        dlg.ShowDialog();
    }

    async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LiveSession s }) return;
        var inst = _state.SelectedInstance;
        if (inst is null) return;

        var ad = Gateway.ReadAd(inst.Home);
        if (ad is null) return;
        try
        {
            await Gateway.CancelSession(ad, s.SessionId, CancellationToken.None);
            await Refresh();
        }
        catch (Exception ex)
        {
            TreeHint.Text = PadError.Describe(ex);
            MessageBox.Show(PadError.Describe(ex), "PAD", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void OpenSession_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LiveSession s }) return;
        var inst = _state.SelectedInstance;
        if (inst is null) return;

        var row = _state.Launcher.Sessions(inst)
            .FirstOrDefault(r => string.Equals(r.Id, s.SessionId, StringComparison.OrdinalIgnoreCase));
        Reveal.Path(row is not null ? row.Path : System.IO.Path.Combine(inst.Home, "sessions"));
    }

    Instance? SelectedInst() => _state.SelectedInstance;

    void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (LiveRun() is not RunVm run) return;
        if (_state.Settings.Other.ConfirmStop)
        {
            var ask = MessageBox.Show(
                $"停止 {run.InstanceName} · {run.Profile}？",
                "PAD", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (ask != MessageBoxResult.OK) return;
        }
        _state.Runner.Stop(run.Entry.Instance, run.Profile);
        _state.RefreshRunning();
        _ = Refresh();
    }

    void Logs_Click(object sender, RoutedEventArgs e)
    {
        var inst = SelectedInst();
        if (inst is not null) Reveal.Path(_state.Launcher.LogsDir(inst.Id));
    }

    void OpenHome_Click(object sender, RoutedEventArgs e)
    {
        var inst = SelectedInst();
        if (inst is not null) Reveal.Path(inst.Home);
    }

    void OpenWorkspace_Click(object sender, RoutedEventArgs e)
    {
        var inst = SelectedInst();
        if (inst is not null) Reveal.Path(inst.Workspace.Path);
    }

    void GoLaunch_Click(object sender, RoutedEventArgs e) => _win.ShowTab("launch");
}
