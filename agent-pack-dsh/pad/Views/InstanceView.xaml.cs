using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Microsoft.Win32;
using Pad.Core;
using PluginQuery = Pad.Core.PluginList;

namespace Pad.Views;

public partial class InstanceView : UserControl, IRefreshable
{
    readonly MainWindow _win;
    readonly AppState _state = AppState.Current;
    readonly Packs _packs;
    readonly Trials _trials;
    bool _instLoading;
    bool _overviewLoading;
    bool _hideHinted;
    bool _pluginLoading;
    bool _pluginBusy;
    List<PluginRow> _pluginRows = [];
    /// <summary>P1 只读事实：这个实例/profile 的包↔插件归属（插件页小标与卸/更新确认用）。</summary>
    PackPluginFacts _pluginFacts = PackPluginFacts.Empty;
    CancellationTokenSource? _pluginCts;

    public InstanceView(MainWindow win)
    {
        InitializeComponent();
        _win = win;
        _packs = new Packs(_state.Launcher);
        _trials = new Trials(_state.Launcher);
        DataContext = _state;
        ShowPane(Checked());
        HookResize();
    }

    string Checked() => new[] { NavOverview, NavSettings, NavPlugins, NavSessions, NavPresets }
        .FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? "overview";

    public void OnShown()
    {
        _win.RefreshInnerTitle();
        if (App.ShotPage == "instance-settings")
        {
            NavSettings.IsChecked = true;
            return;
        }
        if (App.ShotPage is "instance-plugins" or "instance-bundles" or "plugin-detail")
        {
            NavPlugins.IsChecked = true;
            return;
        }
        // --shot-page instance-packs：实例页「整合包」面板没有导航项（D 系列砍掉了），
        // 截图时直接把它摆出来当证据，避免为了截图加一个用户会看到的入口。
        if (App.ShotPage == "instance-packs")
        {
            ShowPane("packs");
            return;
        }
        LoadSessions();
        LoadOverview();
    }

    void LoadSessions()
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var rows = _state.Launcher.Sessions(inst);
        SessionList.ItemsSource = rows;
        SessionCount.Content = $"{rows.Count} 条";
        SessionCount.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        NoSessions.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PaneOverview is null || sender is not RadioButton { Tag: string tag }) return;
        ShowPane(tag);
    }

    void ShowPane(string tag)
    {
        PaneOverview.Visibility = tag == "overview" ? Visibility.Visible : Visibility.Collapsed;
        PaneSettings.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
        PanePlugins.Visibility = tag is "plugins" or "bundles" ? Visibility.Visible : Visibility.Collapsed;
        PanePacks.Visibility = tag == "packs" ? Visibility.Visible : Visibility.Collapsed;
        PaneSessions.Visibility = tag == "sessions" ? Visibility.Visible : Visibility.Collapsed;
        PanePresets.Visibility = tag == "presets" ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "sessions") LoadSessions();
        if (tag == "packs") { _ = LoadPacks(); _ = LoadSetList(); }
        if (tag is "plugins" or "bundles") LoadPlugins();
        if (tag == "presets") LoadPresets();
        if (tag == "settings") LoadInstSettings();
        if (tag == "overview") LoadOverview();
        if (Fx.Animated && Pane(tag) is { } pane) Fx.PageIn(pane);
    }

    UIElement? Pane(string tag) => tag switch
    {
        "settings" => PaneSettings,
        "plugins" or "bundles" => PanePlugins,
        "packs" => PanePacks,
        "sessions" => PaneSessions,
        "presets" => PanePresets,
        _ => PaneOverview,
    };

    void Rename_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var name = Prompt("修改版本名", "", inst.Name);
        if (name is null or { Length: 0 } || name == inst.Name) return;
        try
        {
            _state.Launcher.RenameInstance(inst.Id, name);
            _state.Reload();
            _win.RefreshInnerTitle();
            LoadOverview();
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    void Desc_Click(object sender, RoutedEventArgs e) => OpenDescEdit();

    void Star_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        try
        {
            var next = !(inst.Display?.Star ?? false);
            _state.Launcher.SetDisplay(inst.Id, star: next);
            _state.Reload();
            LoadOverview();
            _win.Toast(next ? "已加入收藏夹" : "已从收藏夹中移除");
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    void OpenInstanceDir_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        Reveal.Path(_state.Launcher.InstanceDir(inst.Id));
    }

    void DeleteInstance_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        if (!_win.Ask("版本删除确认",
            $"你确定要删除实例 {inst.Name} 吗？\n该实例对应的 DSH_HOME、工作区和 session 也会被一并删除。",
            warn: true)) return;
        _state.Launcher.RemoveInstance(inst.Id);
        _state.Reload();
        _win.Show("launch");
        _win.TabLaunch.IsChecked = true;
    }

    void OpenHome_Click(object sender, RoutedEventArgs e) =>
        Reveal.Path(_state.SelectedInstance?.Home);

    void OpenWorkspace_Click(object sender, RoutedEventArgs e) =>
        Reveal.Path(_state.SelectedInstance?.Workspace.Path);

    void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        if (_state.SelectedInstance is null) return;
        Reveal.Path(_state.Launcher.LogsDir(_state.SelectedInstance.Id));
    }

    void OpenProfiles_Click(object sender, RoutedEventArgs e)
    {
        if (_state.SelectedInstance is null) return;
        Reveal.Path(_state.Launcher.ProfilesDir(_state.SelectedInstance));
    }

    void Shortcut_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var profile = inst.LastProfile ?? _state.Launchables.FirstOrDefault()?.Name;
        if (profile is null)
        {
            _win.Toast("未找到可用的版本", bad: true);
            return;
        }
        try
        {
            Reveal.Path(_state.Launcher.WriteShortcut(inst, profile));
            _win.Toast("快捷方式写到了 library/shortcuts");
        }
        catch (Exception ex)
        {
            _win.Toast(PadError.Describe(ex), bad: true);
        }
    }

    void AnalyzeCrash_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        try
        {
            var text = CrashAnalyzer.Analyze(_state.Launcher, inst.Id).Render();
            MessageBox.Show(text.Length > 2000 ? text[..2000] : text, "崩溃分析",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _win.Toast(PadError.Describe(ex), bad: true);
        }
    }

    async void DumpConfig_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        try
        {
            _win.Toast("正在跑 dsh --dump-config…");
            var path = await _state.Launcher.DumpConfig(inst.Id, null, CancellationToken.None);
            _win.Toast("dump-config 写到了实例目录");
            Reveal.Path(path);
        }
        catch (Exception ex)
        {
            _win.Toast(PadError.Describe(ex), bad: true);
        }
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var dlg = new SaveFileDialog
        {
            Title = "导出实例",
            Filter = "实例包 (*.pinst.zip)|*.pinst.zip",
            FileName = $"{inst.Id}.pinst.zip",
        };
        if (dlg.ShowDialog(_win) != true) return;
        try
        {
            var path = _state.Launcher.ExportInstance(inst.Id, dlg.FileName);
            _win.Toast("导出了（凭据已剥掉）");
            Reveal.Path(path);
        }
        catch (Exception ex)
        {
            _win.Toast(PadError.Describe(ex), bad: true);
        }
    }

    // ---- session ops -----------------------------------------------------

    void DeleteSession_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SessionRow row }) return;
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        if (!_win.Ask("删除 session",
            $"你确定要删除 session {row.Id[..Math.Min(8, row.Id.Length)]} 吗？",
            warn: true)) return;
        try
        {
            _state.Launcher.DeleteSession(inst.Id, row.Id);
            LoadSessions();
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    void BackupSession_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SessionRow row }) return;
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        try
        {
            var path = _state.Launcher.BackupSessions(inst.Id, row.Id);
            _win.Toast("备份到了 " + System.IO.Path.GetFileName(path));
            Reveal.Path(path);
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    async void OpenSession_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SessionRow row }) return;
        var inst = _state.SelectedInstance;
        if (inst is null)
        {
            Reveal.Path(row.Path);
            return;
        }
        try
        {
            var plan = await Pad.Core.Jump.JumpPlanner.ExecuteAsync(_state.Launcher, inst, row.Id, null, execute: true);
            if (!plan.Executed)
            {
                Reveal.Path(row.Path);
                _win.Toast(plan.Chosen is null ? "没有可用界面，已打开会话文件" : plan.Detail ?? "已打开会话文件");
                return;
            }
            _win.Toast(plan.Degraded ? $"已聚焦窗口并复制会话 id（{plan.Pa}）" : "已打开会话");
        }
        catch (Exception ex)
        {
            Reveal.Path(row.Path);
            _win.Toast(PadError.Describe(ex), bad: true);
        }
    }

    // ---- agent-preset ----------------------------------------------------

    void LoadPresets()
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var rows = _state.Launcher.ListAgentPresets(inst.Id);
        PresetList.ItemsSource = rows;
        NoPresets.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void CopyPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AgentPresetRow row }) return;
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var toId = _win.AskInput("复制 agent-preset", $"复制 {row.Id} 为：");
        if (toId is null or { Length: 0 }) return;
        try
        {
            _state.Launcher.CopyAgentPreset(inst.Id, row.Id, toId);
            LoadPresets();
            _win.Toast($"已复制为 {toId}");
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    void RemovePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AgentPresetRow row }) return;
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        if (!_win.Ask("删除 agent-preset",
            $"你确定要删除用户层 agent-preset {row.Id} 吗？",
            warn: true)) return;
        try
        {
            _state.Launcher.RemoveAgentPreset(inst.Id, row.Id);
            LoadPresets();
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    void LoadOverview()
    {
        var inst = _state.SelectedInstance;
        if (inst is null || LogoBox is null) return;
        _overviewLoading = true;
        var line = DisplayLine(inst);
        OverviewName.Text = inst.Name;
        var custom = inst.Display?.Info?.Trim() ?? "";
        if (custom.Length > 0)
        {
            OverviewSub.Text = custom;
            OverviewPipe.Text = "  |  " + line;
            OverviewPipe.Visibility = Visibility.Visible;
        }
        else
        {
            OverviewSub.Text = line;
            OverviewPipe.Text = "";
            OverviewPipe.Visibility = Visibility.Collapsed;
        }
        LogoBox.SelectedIndex = string.IsNullOrWhiteSpace(inst.Display?.Logo) ? 0 : 1;
        var cat = inst.Display?.Category ?? "";
        TypeBox.SelectedIndex = cat switch
        {
            "hidden" or "隐藏" => 1,
            "终端" => 2,
            "网页" => 3,
            "不常用" => 4,
            _ => 0,
        };
        if (StarBtn is not null)
            StarBtn.Content = inst.Display?.Star == true ? "从收藏夹中移除" : "加入收藏夹";
        if (OverviewMark is not null)
        {
            OverviewMark.DataContext = null;
            OverviewMark.DataContext = inst;
        }
        var customLogo = !string.IsNullOrWhiteSpace(inst.Display?.Logo);
        if (LogoCustomRow is not null)
            LogoCustomRow.Visibility = customLogo ? Visibility.Visible : Visibility.Collapsed;
        if (customLogo && LogoPathBox is not null)
        {
            LogoPathBox.Text = inst.Display!.Logo;
            PaintLogoPreview(inst.Display.Logo);
        }
        var intro = inst.Display?.Intro?.Trim() ?? "";
        var readme = _state.Launcher.ReadReadme(inst.Id).Trim();
        if (OverviewIntro is not null)
        {
            OverviewIntro.Text = intro;
            OverviewIntro.Visibility = intro.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (OpenReadmeBtn is not null)
            OpenReadmeBtn.Visibility = readme.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (OverviewExtra is not null)
            OverviewExtra.Visibility = intro.Length > 0 || readme.Length > 0
                ? Visibility.Visible : Visibility.Collapsed;
        _overviewLoading = false;
    }

    string DisplayLine(Instance inst)
    {
        var profiles = _state.Launcher.Profiles(inst);
        var bits = new List<string> { inst.Dsh.Version };
        var tui = profiles.Any(p => p.HasTui);
        var web = profiles.Any(p => p.IsWeb);
        if (tui) bits.Add("终端");
        if (web) bits.Add("网页");
        if (!tui && !web) bits.Add("自定义");
        return string.Join(", ", bits);
    }

    void LogoBox_Sel(object sender, SelectionChangedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null || _overviewLoading || LogoBox is not { IsLoaded: true }) return;
        var tag = (LogoBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        try
        {
            if (tag == "custom")
            {
                LogoCustomRow.Visibility = Visibility.Visible;
                LogoPathBox.Text = inst.Display?.Logo ?? "";
                PaintLogoPreview(LogoPathBox.Text);
                return;
            }
            LogoCustomRow.Visibility = Visibility.Collapsed;
            ClearInstanceLogo(inst.Id);
            _state.Launcher.SetDisplay(inst.Id, logo: "");
            _state.Reload();
            LoadOverview();
        }
        catch (Exception ex)
        {
            _win.Toast(PadError.Describe(ex), bad: true);
            LoadOverview();
        }
    }

    string CopyInstanceLogo(string id, string src)
    {
        var ext = Path.GetExtension(src).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp"))
            ext = ".png";
        var dir = _state.Launcher.InstanceDir(id);
        Directory.CreateDirectory(dir);
        ClearInstanceLogo(id);
        var dest = Path.Combine(dir, "logo" + ext);
        File.Copy(src, dest, true);
        return dest;
    }

    void ClearInstanceLogo(string id)
    {
        var dir = _state.Launcher.InstanceDir(id);
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" })
        {
            var p = Path.Combine(dir, "logo" + ext);
            if (File.Exists(p)) File.Delete(p);
        }
    }

    public bool CanTakeLogoDrop =>
        IsLoaded && PaneOverview.Visibility == Visibility.Visible && IntroEditLayer.Visibility != Visibility.Visible;

    public bool IntroEditOpen => IntroEditLayer.Visibility == Visibility.Visible;

    public void CloseIntroEdit() => HideIntroEdit();

    public void OfferDroppedLogo(string path)
    {
        if (!CanTakeLogoDrop || !LaunchPolicy.IsImage(path) || !File.Exists(path)) return;
        if (!_win.Ask("用作图标", "要把这张图片用作当前实例的图标吗？", previewPath: path)) return;
        ApplyLogo(path);
    }

    void LogoBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择图片",
            Filter = "图片|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp|所有|*.*",
        };
        if (dlg.ShowDialog(_win) != true) return;
        LogoPathBox.Text = dlg.FileName;
        ApplyLogo(dlg.FileName);
    }

    void LogoPath_Key(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyLogo(LogoPathBox.Text);
            e.Handled = true;
        }
    }

    void LogoPath_Lost(object sender, RoutedEventArgs e)
    {
        if (_overviewLoading) return;
        var text = LogoPathBox.Text.Trim();
        if (text.Length == 0) return;
        if (File.Exists(text)) ApplyLogo(text);
        else PaintLogoPreview(text);
    }

    void ApplyLogo(string src)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        src = src.Trim().Trim('"');
        if (src.Length == 0 || !File.Exists(src))
        {
            _win.Toast("找不到这张图片", bad: true);
            return;
        }
        if (!LaunchPolicy.IsImage(src))
        {
            _win.Toast("只接受 png / jpg / gif / webp / bmp", bad: true);
            return;
        }
        try
        {
            var dest = CopyInstanceLogo(inst.Id, src);
            _state.Launcher.SetDisplay(inst.Id, logo: dest);
            _state.Reload();
            LoadOverview();
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    void PaintLogoPreview(string? path)
    {
        if (LogoLivePreview is null) return;
        path = (path ?? "").Trim().Trim('"');
        LogoLivePreview.Source = path.Length > 0 && File.Exists(path) && LaunchPolicy.IsImage(path)
            ? Skin.LoadBitmap(path)
            : null;
    }

    void OpenDescEdit()
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        InfoBox.Text = inst.Display?.Info ?? inst.Note ?? "";
        IntroBox.Text = inst.Display?.Intro ?? "";
        ReadmeBox.Text = _state.Launcher.ReadReadme(inst.Id);
        IntroEditLayer.Visibility = Visibility.Visible;
        IntroEditCard.MaxHeight = Math.Max(280, ActualHeight - 16);
        Fx.PulseIn(IntroEditCard, true);
        InfoBox.Focus();
        InfoBox.SelectAll();
    }

    void IntroSave_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        try
        {
            _state.Launcher.SetDisplay(inst.Id, info: InfoBox.Text.Trim(), intro: IntroBox.Text.Trim());
            _state.Launcher.WriteReadme(inst.Id, ReadmeBox.Text);
            _state.Reload();
            HideIntroEdit();
            LoadOverview();
            _win.Toast("描述已保存");
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    void IntroCancel_Click(object sender, RoutedEventArgs e) => HideIntroEdit();

    void IntroDim_Down(object sender, MouseButtonEventArgs e) => HideIntroEdit();

    void IntroCard_Down(object sender, MouseButtonEventArgs e) => e.Handled = true;

    void HideIntroEdit()
    {
        IntroEditLayer.Visibility = Visibility.Collapsed;
        IntroEditCard.Opacity = 1;
        IntroEditCard.RenderTransform = null;
    }

    void OpenReadme_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var md = _state.Launcher.ReadReadme(inst.Id); // instances/<id>/readme.md
        if (md.Trim().Length == 0) return;
        _win.ShowMd("详细介绍 · " + inst.Name, md);
    }

    void TypeBox_Sel(object sender, SelectionChangedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null || _overviewLoading || TypeBox is not { IsLoaded: true }) return;
        var tag = (TypeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        if (tag is "隐藏" or "hidden" && !_hideHinted)
        {
            if (!_win.Ask("隐藏版本提示",
                "确认要从版本列表中隐藏该实例吗？隐藏后它将不再出现于版本列表中。\n此后，在版本列表页面按下 F11 才可以查看被隐藏的实例。"))
            {
                LoadOverview();
                return;
            }
            _hideHinted = true;
            tag = "隐藏";
        }
        try
        {
            _state.Launcher.SetDisplay(inst.Id, category: tag);
            _state.Reload();
            LoadOverview();
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    string? Prompt(string title, string label, string defaultValue = "") =>
        _win.AskInput(title, label.Length == 0 ? null : label, defaultValue);

    void GotoGlobalLaunch_Click(object sender, RoutedEventArgs e) => _win.OpenSettingsLaunch();

    void GoLaunch_Click(object sender, RoutedEventArgs e) => _win.ShowTab("launch");

    void InstIndie_Sel(object sender, SelectionChangedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null || _instLoading || InstIndieBox is not { IsLoaded: true }) return;
        var wantIndie = (InstIndieBox.SelectedItem as ComboBoxItem)?.Tag as string != "existing";
        var nowIndie = inst.Workspace.Kind != "existing";
        if (wantIndie == nowIndie) return;
        if (!_win.Ask("工作区隔离",
            "调整工作区隔离后，要把项目文件手动迁到新目录。\n改回去就能回到原来的目录。\n$DSH_HOME 始终隔离，不会跟着切。",
            warn: true))
        {
            _instLoading = true;
            InstIndieBox.SelectedIndex = nowIndie ? 0 : 1;
            _instLoading = false;
            return;
        }
        try
        {
            if (wantIndie)
                _state.Launcher.SetWorkspaceIndie(inst.Id, true);
            else
            {
                var typed = InstWsPath.Text.Trim().Trim('"');
                if (typed.Length > 0 && Directory.Exists(typed))
                    _state.Launcher.SetWorkspacePath(inst.Id, typed);
                else
                {
                    var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "与这个目录共用工作区" };
                    if (dlg.ShowDialog() != true)
                    {
                        _instLoading = true;
                        InstIndieBox.SelectedIndex = 0;
                        _instLoading = false;
                        return;
                    }
                    _state.Launcher.SetWorkspacePath(inst.Id, dlg.FolderName);
                }
            }
            _state.Reload();
            LoadInstSettings();
        }
        catch (Exception ex)
        {
            MessageBox.Show(PadError.Describe(ex), "PAD", MessageBoxButton.OK, MessageBoxImage.Warning);
            _instLoading = true;
            InstIndieBox.SelectedIndex = nowIndie ? 0 : 1;
            _instLoading = false;
        }
    }

    void InstWsBrowse_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "工作区目录" };
        if (dlg.ShowDialog() != true) return;
        ApplyWorkspacePath(dlg.FolderName);
    }

    void InstWsPath_Key(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyWorkspacePath(InstWsPath.Text);
            e.Handled = true;
        }
    }

    void InstWsPath_Lost(object sender, RoutedEventArgs e)
    {
        if (_instLoading) return;
        var text = InstWsPath.Text.Trim();
        if (text.Length == 0) return;
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        if (LaunchPolicy.SamePath(text, inst.Workspace.Path)) return;
        ApplyWorkspacePath(text);
    }

    void ApplyWorkspacePath(string path)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        try
        {
            _state.Launcher.SetWorkspacePath(inst.Id, path);
            _state.Reload();
            LoadInstSettings();
            _win.Toast("工作区已改");
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    // ---- instance settings (launch overrides) ---------------------------

    void LoadInstSettings()
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        _instLoading = true;
        var ov = inst.Launch;
        InstTitleBox.Text = ov?.Title ?? "";
        InstEnvBox.Text = ov?.ExtraEnv ?? "";
        var nodeKind = LaunchPolicy.NodeKindTag(ov?.NodePath, instance: true);
        InstNodeKind.SelectedIndex = nodeKind switch { "auto" => 1, "custom" => 2, _ => 0 };
        InstNodeBox.Text = LaunchPolicy.NodeIsCustom(ov?.NodePath) ? ov!.NodePath : "";
        SyncInstNodeCustom();
        InstPreCommandBox.Text = ov?.PreCommand ?? "";
        InstPreWaitBox.IsChecked = ov?.PreCommandWait;
        InstPauseBox.IsChecked = ov?.PauseOnError;
        InstTelemetryBox.IsChecked = ov?.TelemetryDisabled;
        var cred = ov?.CredentialsSet ?? "";
        FillCredBox(cred);
        var after = ov?.AfterLaunch ?? "";
        InstAfterBox.SelectedIndex = after switch { "keep" => 1, "minimize" => 2, "hide" => 3, _ => 0 };
        InstIndieBox.SelectedIndex = inst.Workspace.Kind == "existing" ? 1 : 0;
        InstWsPath.Text = inst.Workspace.Path;
        var g = _state.Settings.Launch;
        InstTitleFollow.Text = string.IsNullOrWhiteSpace(ov?.Title)
            ? "跟随全局：" + (string.IsNullOrWhiteSpace(g.Title) ? "{instance} · {profile}" : g.Title)
            : "覆盖中";
        InstEnvFollow.Text = string.IsNullOrWhiteSpace(ov?.ExtraEnv)
            ? (string.IsNullOrWhiteSpace(g.ExtraEnv) ? "跟随全局：无" : "跟随全局：已有全局 extraEnv")
            : "覆盖中（追加在全局后）";
        InstPreFollow.Text = string.IsNullOrWhiteSpace(ov?.PreCommand)
            ? (string.IsNullOrWhiteSpace(g.PreCommand) ? "跟随全局：无" : "跟随全局：" + g.PreCommand)
            : "覆盖中";
        PaintInstNodeHint();
        _instLoading = false;
    }

    void FillCredBox(string current)
    {
        InstCredBox.Items.Clear();
        InstCredBox.Items.Add(new ComboBoxItem { Tag = "", Content = "跟随全局" });
        InstCredBox.Items.Add(new ComboBoxItem { Tag = "instance", Content = "本实例 .credentials.yaml" });
        InstCredBox.Items.Add(new ComboBoxItem { Tag = "global", Content = "全局 API Key" });
        InstCredBox.Items.Add(new ComboBoxItem { Tag = "none", Content = "不拷" });
        foreach (var set in _state.Launcher.ListCredentials())
        {
            if (set.Name == "global") continue;
            InstCredBox.Items.Add(new ComboBoxItem { Tag = set.Name, Content = set.Name });
        }
        var match = InstCredBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (i.Tag as string) == current);
        InstCredBox.SelectedItem = match ?? InstCredBox.Items[0];
    }

    void InstSetting_Save(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null || _instLoading || !InstAfterBox.IsLoaded) return;
        var ov = inst.Launch ??= new InstanceLaunch();
        ov.Title = BlankOr(InstTitleBox.Text);
        ov.ExtraEnv = BlankOr(InstEnvBox.Text);
        ov.NodePath = InstNodeStored();
        ov.PreCommand = BlankOr(InstPreCommandBox.Text);
        ov.PreCommandWait = InstPreWaitBox.IsChecked;
        ov.PauseOnError = InstPauseBox.IsChecked;
        ov.TelemetryDisabled = InstTelemetryBox.IsChecked;
        ov.CredentialsSet = BlankOr((InstCredBox.SelectedItem as ComboBoxItem)?.Tag as string);
        var after = (InstAfterBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        ov.AfterLaunch = after.Length == 0 ? null : after;

        if (IsEmpty(ov)) inst.Launch = null;
        _state.Launcher.SaveInstance(inst);
        PaintInstNodeHint();
        _win.Toast("实例设置已保存");
    }

    void InstNodeKind_Sel(object sender, SelectionChangedEventArgs e)
    {
        if (_instLoading || InstNodeKind is not { IsLoaded: true }) return;
        SyncInstNodeCustom();
        InstSetting_Save(sender, e);
    }

    void SyncInstNodeCustom()
    {
        var custom = (InstNodeKind.SelectedItem as ComboBoxItem)?.Tag as string == "custom";
        InstNodeCustom.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
    }

    string? InstNodeStored()
    {
        var kind = (InstNodeKind.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        return kind switch
        {
            "auto" => LaunchPolicy.NodeAuto,
            "custom" => BlankOr(InstNodeBox.Text) ?? LaunchPolicy.NodeAuto,
            _ => null,
        };
    }

    void PaintInstNodeHint()
    {
        var inst = _state.SelectedInstance;
        if (inst is null || InstNodeHint is null) return;
        var kind = (InstNodeKind.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        var ov = kind switch
        {
            "auto" => LaunchPolicy.NodeAuto,
            "custom" => BlankOr(InstNodeBox.Text) ?? ".",
            _ => null,
        };
        var exe = _state.Launcher.PreviewNode(ov, inst.Dsh.Version, _state.Settings);
        var engines = _state.Launcher.RequiredNodeVersion(inst.Dsh.Version)
            ?? LaunchPolicy.NodeFileVersion(exe);
        InstNodeHint.Text = LaunchPolicy.DescribeNodeWillUse(exe, engines);
        var warn = exe is null;
        InstNodeHintBar.Style = (Style)FindResource(warn ? "HintBarWarn" : "HintBar");
        InstNodeHint.Style = (Style)FindResource(warn ? "HintBarCopyWarn" : "HintBarCopy");
    }

    void InstNodeBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择 node.exe",
            Filter = "Node|node.exe|可执行文件|*.exe|所有|*.*",
            FileName = "node.exe",
        };
        if (dlg.ShowDialog(_win) != true) return;
        _instLoading = true;
        InstNodeKind.SelectedIndex = 2;
        InstNodeBox.Text = dlg.FileName;
        SyncInstNodeCustom();
        _instLoading = false;
        InstSetting_Save(sender, e);
    }

    void InstSetting_Sel(object sender, SelectionChangedEventArgs e)
    {
        if (InstAfterBox.IsLoaded) InstSetting_Save(sender, e);
    }

    void InstSetting_Reset(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        if (!_win.Ask("恢复跟随全局", "清掉这个实例的 launch 覆盖？\n不动 pad.json。")) return;
        inst.Launch = null;
        _state.Launcher.SaveInstance(inst);
        LoadInstSettings();
        _win.Toast("已恢复跟随全局");
    }

    static string? BlankOr(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    static bool IsEmpty(InstanceLaunch ov) =>
        ov.Title is null && ov.AfterLaunch is null && ov.ExtraEnv is null && ov.NodePath is null
        && ov.PreCommand is null && ov.PreCommandWait is null && ov.PauseOnError is null
        && ov.TelemetryDisabled is null && ov.CredentialsSet is null;

    // ---- plugin list (PCL Mod 管理) --------------------------------------

    void PluginSay(string line)
    {
        PluginLog.Visibility = Visibility.Visible;
        PluginLog.Height = 130;
        PluginLog.AppendText(line + Environment.NewLine);
        PluginLog.ScrollToEnd();
    }

    void PluginBusy(bool on)
    {
        _pluginBusy = on;
        PluginAddBtn.IsEnabled = !on;
        TrialAddBtn.IsEnabled = !on;
        PluginSpinner.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    string? SelectedPluginProfile() => (PluginProfile.SelectedItem as ProfileVm)?.Name;

    PluginQuery.Filter CurrentPluginFilter()
    {
        var tag = new[] { FilterAll, FilterUpdatable, FilterTrial, FilterFav }
            .FirstOrDefault(r => r.IsChecked == true)?.Tag as string;
        return PluginQuery.Parse(tag);
    }

    void LoadPlugins()
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        TrialEmpty.Text = "没有试验中的插件";

        var names = _state.Launchables.ToList();
        if (names.Count == 0)
        {
            // 收编进来的实例（本机 DSH）没有 Launchables 清单；profile 直接从 home/profiles 读。
            var vms = _state.Launcher.Profiles(inst).Select(p => (ProfileVm)new ProfileVm(inst, p)).ToList();
            if (vms.Count > 0)
            {
                PluginProfile.ItemsSource = vms;
                names = vms;
            }
        }
        else if (!ReferenceEquals(PluginProfile.ItemsSource, _state.Launchables))
        {
            PluginProfile.ItemsSource = _state.Launchables;
        }
        var keep = PluginProfile.SelectedItem as ProfileVm;
        var pick = keep is not null && names.Any(n => n.Name == keep.Name) ? keep
            : _state.SelectedProfile ?? names.FirstOrDefault();

        _pluginLoading = true;
        try
        {
            if (PluginProfile.SelectedItem != pick)
                PluginProfile.SelectedItem = pick;
        }
        finally { _pluginLoading = false; }

        var rows = new List<PluginRow>();
        foreach (var b in _state.Launcher.ListBundles(inst))
        {
            if (LaunchPolicy.IsProfileLayer(b.Spec)) continue;
            rows.Add(PluginQuery.FromBundle(b));
        }
        foreach (var t in _trials.Load(inst).Trials)
        {
            var spec = t.Bundles.FirstOrDefault() ?? t.Spec;
            if (LaunchPolicy.IsProfileLayer(spec)) continue;
            rows.Add(PluginQuery.FromTrial(t, inst, _state.Launcher.Root));
        }
        foreach (var r in rows)
        {
            if (r.IconPath.Length == 0) continue;
            if (Skin.LoadBitmap(r.IconPath) is null) r.IconPath = "";
        }
        var favRoot = _state.Launcher.Root;
        foreach (var r in rows) r.Favorite = Favorites.IsFavorite(favRoot, r.Spec);
        // P1：插件行的小标「来自整合包 X」。事实只读投影 + profile，不改任何安装状态。
        _pluginFacts = PackPluginFacts.Collect(inst, pick?.Name);
        foreach (var r in rows)
        {
            var origin = _pluginFacts.OriginOf(r.Spec);
            r.PackOriginText = origin?.OriginText ?? "";
            r.PackOriginTip = origin?.OriginTip ?? "";
        }
        _pluginRows = rows;
        PaintPlugins();
        if (App.ShotPage == "plugin-detail") QueueDetailShot();
        _ = FetchPluginExtras(inst);
    }

    bool _detailShotQueued;

    /// <summary>
    /// 测试钩子：`--shot-page plugin-detail` 时把插件详情窗口也离屏渲染一张。
    /// 窗口放在屏幕外、不开任务栏、不抢前台；渲染完自己关掉。
    /// </summary>
    void QueueDetailShot()
    {
        if (_detailShotQueued || App.ShotPath is not { Length: > 0 } shot) return;
        var row = _pluginRows.FirstOrDefault(r => r.Description.Length > 0) ?? _pluginRows.FirstOrDefault();
        if (row is null) return;
        _detailShotQueued = true;
        var dir = Path.GetDirectoryName(shot) ?? "";
        var path = Path.Combine(dir, Path.GetFileNameWithoutExtension(shot) + "-plugin-detail.png");
        var win = new PluginDetailWindow(row)
        {
            Owner = Window.GetWindow(this),
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Left = -20000,
            Top = -20000,
        };
        win.Loaded += (_, _) =>
        {
            win.Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    // 等仓库图片下载完（最多 10 秒），否则截出来的空页面没法看
                    await Task.WhenAny(win.ShotsLoaded, Task.Delay(TimeSpan.FromSeconds(10)));
                    win.RenderTo(path);
                }
                finally { win.Close(); }
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
        win.Show();
    }

    async Task FetchPluginExtras(Instance inst)
    {
        _pluginCts?.Cancel();
        var cts = new CancellationTokenSource();
        _pluginCts = cts;
        try
        {
            await _state.Launcher.EnsureBundleIcons(inst, cts.Token);
            var registry = _state.Settings.Download.NpmRegistry;
            foreach (var row in _pluginRows.ToList())
            {
                if (cts.IsCancellationRequested) return;
                if (row.Trial || !PluginQuery.IsNpmName(row.Spec)) continue;
                var latest = await PluginQuery.FetchLatest(registry, row.Spec, cts.Token);
                if (latest is null or { Length: 0 }) continue;
                PluginQuery.WriteCachedLatest(_state.Launcher.Root, row.Spec, latest);
                row.Latest = latest;
            }
            if (cts.IsCancellationRequested) return;
            await Dispatcher.InvokeAsync(() =>
            {
                if (!ReferenceEquals(_pluginCts, cts)) return;
                if (_state.SelectedInstance?.Id != inst.Id) return;
                foreach (var r in _pluginRows)
                {
                    if (r.IconPath.Length > 0) continue;
                    var dir = r.PackageDir;
                    var icon = BundleIcon.Resolve(_state.Launcher.Root, dir, r.Spec);
                    if (icon is not null && Skin.LoadBitmap(icon) is not null)
                        r.IconPath = icon;
                }
                PaintPlugins();
            });
        }
        catch (OperationCanceledException) { }
        catch { /* registry unreachable; 可更新 stays empty */ }
    }

    /// <summary>每页多少个按窗口高度自适应（约 17px/个），窗口越高一页越多。</summary>
    int PluginPageSize =>
        Math.Clamp((int)((Window.GetWindow(this)?.ActualHeight ?? 900) / 17), 24, 84);

    int _pluginPageSizeUsed = 54;
    System.Windows.Threading.DispatcherTimer? _resizeTimer;

    void HookResize()
    {
        SizeChanged += (_, _) =>
        {
            _resizeTimer ??= CreateResizeTimer();
            _resizeTimer.Stop();
            _resizeTimer.Start();
        };
    }

    System.Windows.Threading.DispatcherTimer CreateResizeTimer()
    {
        var t = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(220),
        };
        t.Tick += (_, _) =>
        {
            t.Stop();
            RepaintPluginsForResize();
        };
        return t;
    }

    void RepaintPluginsForResize()
    {
        if (PluginList is null || PanePlugins.Visibility != Visibility.Visible) return;
        var now = PluginPageSize;
        if (now == _pluginPageSizeUsed) return;
        var first = _pluginPage * Math.Max(1, _pluginPageSizeUsed);
        _pluginPageSizeUsed = now;
        _pluginPage = Math.Max(0, first / now);
        PaintPlugins();
    }
    int _pluginPage;

    void InstMore_Click(object sender, RoutedEventArgs e)
    {
        var show = InstMore.Visibility != Visibility.Visible;
        InstMore.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        InstMoreAngle.Angle = show ? 180 : 0;
    }

    void PluginMore_Click(object sender, RoutedEventArgs e)
    {
        var show = PluginActions.Visibility != Visibility.Visible;
        PluginActions.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PluginMoreAngle.Angle = show ? 180 : 0;
    }

    void PluginPrev_Click(object sender, RoutedEventArgs e)
    {
        if (_pluginPage <= 0) return;
        _pluginPage--;
        PaintPlugins();
    }

    void PluginNext_Click(object sender, RoutedEventArgs e)
    {
        var pick = SelectedPluginProfile();
        var inProfile = _pluginRows.Where(r => pick is null || r.Profile == pick).ToList();
        var total = PluginQuery.Apply(inProfile, PluginSearch?.Text ?? "", CurrentPluginFilter()).Count();
        var pages = Math.Max(1, (total + PluginPageSize - 1) / PluginPageSize);
        if (_pluginPage >= pages - 1) return;
        _pluginPage++;
        PaintPlugins();
    }

    void PaintPlugins()
    {
        var pick = SelectedPluginProfile();
        var inProfile = _pluginRows.Where(r => pick is null || r.Profile == pick).ToList();
        FilterAll.Content = $"全部 ({inProfile.Count})";
        FilterUpdatable.Content = $"可更新 ({inProfile.Count(r => r.Updatable)})";
        FilterTrial.Content = $"试验中 ({inProfile.Count(r => r.Trial)})";
        FilterFav.Content = $"收藏 ({inProfile.Count(r => r.Favorite)})";

        var shown = PluginQuery.Apply(inProfile, PluginSearch?.Text ?? "", CurrentPluginFilter()).ToList();
        var pageSize = PluginPageSize;
        _pluginPageSizeUsed = pageSize;
        var pages = Math.Max(1, (shown.Count + pageSize - 1) / pageSize);
        if (_pluginPage >= pages) _pluginPage = pages - 1;
        if (_pluginPage < 0) _pluginPage = 0;
        var slice = shown.Skip(_pluginPage * pageSize).Take(pageSize).ToList();
        PluginList.ItemsSource = slice;
        PluginPager.Visibility = pages > 1 ? Visibility.Visible : Visibility.Collapsed;
        PluginPageText.Text = $"{_pluginPage + 1} / {pages}";
        PluginPrevBtn.IsEnabled = _pluginPage > 0;
        PluginNextBtn.IsEnabled = _pluginPage < pages - 1;

        var empty = shown.Count == 0;
        PluginEmptyBox.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (pick is null)
            PluginEmpty.Text = "这份实例还没有 profile";
        else if (CurrentPluginFilter() == PluginQuery.Filter.Trial)
            PluginEmpty.Text = "没有试验中的插件";
        else if (CurrentPluginFilter() == PluginQuery.Filter.Updatable)
            PluginEmpty.Text = "没有可更新的插件";
        else if (CurrentPluginFilter() == PluginQuery.Filter.Favorite)
            PluginEmpty.Text = "还没有收藏的插件：点行尾的星标收藏。";
        else if ((PluginSearch?.Text ?? "").Trim().Length > 0)
            PluginEmpty.Text = "没有匹配的插件";
        else
            PluginEmpty.Text = "这个 --profile 还没有插件";
        TrialEmpty.Visibility = Visibility.Collapsed;
        PaintSelBar();
    }

    void PaintSelBar()
    {
        var n = ShownPlugins().Count(r => r.Selected);
        PluginSelBar.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        PluginSelCount.Text = $"已选 {n}";
    }

    List<PluginRow> ShownPlugins() =>
        (PluginList.ItemsSource as IEnumerable<PluginRow>)?.ToList() ?? [];

    void PluginSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (PluginList is null) return;
        _pluginPage = 0;
        PaintPlugins();
    }

    void PluginFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (PluginList is null) return;
        _pluginPage = 0;
        PaintPlugins();
    }

    void PluginProfile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_pluginLoading || PluginList is null) return;
        _pluginPage = 0;
        PaintPlugins();
    }

    void PluginTick_Click(object sender, RoutedEventArgs e) => PaintSelBar();

    void PluginSelectAll_Click(object sender, RoutedEventArgs e)
    {
        var list = ShownPlugins();
        var allOn = list.Count > 0 && list.All(r => r.Selected);
        foreach (var r in list) r.Selected = !allOn;
        PaintSelBar();
    }

    void PluginOpenDir_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var profile = SelectedPluginProfile();
        Reveal.Path(profile is { Length: > 0 }
            ? Path.Combine(inst.Home, "profiles", profile)
            : _state.Launcher.ProfilesDir(inst));
    }

    void PluginRowDir_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: PluginRow row }) return;
        if (row.PackageDir.Length > 0 && Directory.Exists(row.PackageDir))
            Reveal.Path(row.PackageDir);
        else
            PluginOpenDir_Click(sender, e);
    }

    void PluginDownload_Click(object sender, RoutedEventArgs e) =>
        _win.OpenDownloadShelf("plugin");

    void PluginFromFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "从文件安装插件",
            Filter = "插件 (package.json;*.tgz;*.tar.gz)|package.json;*.tgz;*.tar.gz|所有|*.*",
        };
        if (dlg.ShowDialog(_win) != true) return;
        var spec = dlg.FileName;
        if (string.Equals(Path.GetFileName(spec), "package.json", StringComparison.OrdinalIgnoreCase))
            spec = Path.GetDirectoryName(spec) ?? spec;
        PluginSpecBox.Text = spec;
        PluginAdd_Click(sender, e);
    }

    void PluginFav_Click(object sender, RoutedEventArgs e)
    {
        var row = PluginFrom(sender);
        if (row is null) return;
        var root = _state.Launcher.Root;
        var on = Favorites.Toggle(root, row.Spec);
        row.Favorite = on;
        if (_win is not null) _win.Toast(on ? $"已收藏 {row.Title}" : $"取消收藏 {row.Title}", bad: !on ? false : false);
        PaintPlugins();
    }

    void PluginInfo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: PluginRow row }) return;
        var win = new PluginDetailWindow(row) { Owner = Window.GetWindow(this) };
        win.UpdateRequested += r => InvokePluginHandler(PluginUpdate_Click, r);
        win.RemoveRequested += r => InvokePluginHandler(PluginRemove_Click, r);
        win.OpenDirRequested += r => InvokePluginHandler(PluginRowDir_Click, r);
        win.ShowDialog();
    }

    void InvokePluginHandler(Action<object, RoutedEventArgs> handler, PluginRow row) =>
        handler(new Border { Tag = row }, new RoutedEventArgs());

    PluginRow? PluginFrom(object sender)
    {
        if (sender is FrameworkElement { Tag: PluginRow row }) return row;
        if (sender is FrameworkElement { DataContext: PluginRow ctx }) return ctx;
        return null;
    }

    List<PluginRow> PluginTargets(object sender, bool requireSpec)
    {
        var one = PluginFrom(sender);
        if (one is not null) return [one];
        var selected = ShownPlugins().Where(r => r.Selected && !r.Trial).ToList();
        if (selected.Count > 0) return selected;
        var spec = PluginSpecBox.Text.Trim();
        var profile = SelectedPluginProfile() ?? "";
        if (requireSpec && spec.Length == 0) return [];
        if (spec.Length == 0) return [];
        return [new PluginRow { Spec = spec, Profile = profile }];
    }

    async void PluginAdd_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var spec = PluginSpecBox.Text.Trim();
        if (spec.Length == 0) { PluginSpecBox.Focus(); return; }
        var profile = SelectedPluginProfile();
        if (string.IsNullOrEmpty(profile))
        {
            PluginSay("先选一个版本。");
            return;
        }
        if (_pluginBusy) return;
        PluginBusy(true);
        try
        {
            PluginSay($"add {spec} → {profile}");
            await _state.Launcher.PluginOp(inst.Id, "add", spec,
                new Progress<string>(PluginSay), CancellationToken.None, profile);
            PluginSay("装好了。下次启动生效。");
            PluginSpecBox.Clear();
            _state.Reload();
            LoadPlugins();
        }
        catch (Exception ex) { PluginSay(PadError.Describe(ex)); _win.Toast(PadError.Describe(ex), bad: true); }
        finally { PluginBusy(false); }
    }

    async void PluginRemove_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null || _pluginBusy) return;
        var targets = PluginTargets(sender, requireSpec: true);
        if (targets.Count == 0) { PluginSpecBox.Focus(); return; }
        var profile = targets[0].Profile;
        if (string.IsNullOrEmpty(profile))
        {
            PluginSay("先选一个版本。");
            return;
        }
        // P1 提示 / P2 账本：被已装包声明（账本 owners 优先）的插件 / pack-agent 管理器，
        // 先说清后果再动手。
        var owned = targets
            .Select(t => (t.Spec, Packs: PackOwnersOf(inst, profile, t.Spec)))
            .Where(x => x.Packs.Count > 0)
            .ToList();
        var managers = targets.Where(t => PackPluginFacts.IsManagerSpec(t.Spec)).ToList();
        if (targets.Count > 1 || PluginFrom(sender) is not null || owned.Count > 0 || managers.Count > 0)
        {
            var names = string.Join("、", targets.Select(t => t.Spec));
            var lines = new List<string> { $"从 {profile} 移除 {names}？" };
            if (owned.Count > 0)
            {
                var specList = string.Join("、", owned.Select(o => o.Spec));
                var packs = string.Join("、", owned
                    .SelectMany(o => o.Packs)
                    .Distinct(StringComparer.OrdinalIgnoreCase));
                lines.Add($"「{specList}」由整合包 {packs} 要求：卸掉后包会残缺（可在包详情修复）。");
            }
            if (managers.Count > 0)
                lines.Add("PA110：pack-agent 是整合包管理器，移除后所有已装整合包的投影内容都不可见，要重新装回管理器才能恢复。");
            if (!_win.Ask("移除插件", string.Join(Environment.NewLine + Environment.NewLine, lines), warn: true)) return;
        }
        PluginBusy(true);
        try
        {
            foreach (var t in targets)
            {
                PluginSay($"remove {t.Spec} → {t.Profile}");
                await _state.Launcher.PluginOp(inst.Id, "remove", t.Spec,
                    new Progress<string>(PluginSay), CancellationToken.None, t.Profile);
            }
            // P2：账本里登记过这个插件的包 → 标 incomplete（读时对账会把缺件算出来，包行出现「修复」）。
            var marked = MarkPacksIncomplete(inst, profile, targets.Select(t => t.Spec), removed: true);
            PluginSay("移除了。下次启动生效。");
            if (marked.Count > 0)
                _win.Toast("插件已卸；整合包已标不完整：" + string.Join("、", marked) + "（可在包行点「修复」补回）");
            _state.Reload();
            LoadPlugins();
        }
        catch (Exception ex) { PluginSay(PadError.Describe(ex)); _win.Toast(PadError.Describe(ex), bad: true); }
        finally { PluginBusy(false); }
    }

    async void PluginUpdate_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null || _pluginBusy) return;
        var targets = PluginTargets(sender, requireSpec: true);
        if (targets.Count == 0) { PluginSpecBox.Focus(); return; }
        var profile = targets[0].Profile;
        if (string.IsNullOrEmpty(profile))
        {
            PluginSay("先选一个版本。");
            return;
        }
        // P1 提示 / P2 账本：更新包声明（账本 owners 优先）的插件会改掉包钉的版本 → 先确认。
        var owned = targets
            .Select(t => (t.Spec, Packs: PackOwnersOf(inst, profile, t.Spec)))
            .Where(x => x.Packs.Count > 0)
            .ToList();
        if (owned.Count > 0)
        {
            var names = string.Join("、", owned.Select(o => o.Spec));
            var packs = string.Join("、", owned
                .SelectMany(o => o.Packs)
                .Distinct(StringComparer.OrdinalIgnoreCase));
            if (!_win.Ask("更新插件",
                    $"「{names}」由整合包 {packs} 要求：更新会把版本从包钉的版本改走，包会残缺（可在包详情修复）。" +
                    Environment.NewLine + Environment.NewLine + "仍要更新？", warn: true)) return;
        }
        PluginBusy(true);
        try
        {
            foreach (var t in targets)
            {
                PluginSay($"update {t.Spec} → {t.Profile}");
                await _state.Launcher.PluginOp(inst.Id, "update", t.Spec,
                    new Progress<string>(PluginSay), CancellationToken.None, t.Profile);
            }
            // P2：版本漂移由账本读时对账算 incomplete；这里把登记的包状态写一致。
            var marked = MarkPacksIncomplete(inst, profile, targets.Select(t => t.Spec), removed: false);
            PluginSay("更新好了。下次启动生效。");
            if (marked.Count > 0)
                _win.Toast("版本已改；整合包已标不完整：" + string.Join("、", marked) + "（可在包行点「修复」补回）");
            _state.Reload();
            LoadPlugins();
        }
        catch (Exception ex) { PluginSay(PadError.Describe(ex)); _win.Toast(PadError.Describe(ex), bad: true); }
        finally { PluginBusy(false); }
    }

    void PluginUpdateSel_Click(object sender, RoutedEventArgs e) => PluginUpdate_Click(sender, e);

    void PluginRemoveSel_Click(object sender, RoutedEventArgs e) => PluginRemove_Click(sender, e);

    // ---- try-then-commit -------------------------------------------------

    void LoadTrials() => LoadPlugins();

    async void TrialAdd_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        var spec = PluginSpecBox.Text.Trim();
        if (inst is null || spec.Length == 0) { PluginSpecBox.Focus(); return; }
        var profile = SelectedPluginProfile();
        if (string.IsNullOrEmpty(profile))
        {
            PluginSay("先选一个版本。");
            return;
        }
        if (_pluginBusy) return;

        PluginBusy(true);
        try
        {
            PluginSay($"试验 {spec} -> {profile}");
            await _trials.Begin(inst, profile, spec, new Progress<string>(PluginSay),
                CancellationToken.None);
            PluginSpecBox.Clear();
            PluginSay("装好了。下次启动这个版本会带上它，只这一次。");
            LoadPlugins();
        }
        catch (Exception ex)
        {
            PluginSay(PadError.Describe(ex));
            _win.Toast(PadError.Describe(ex), bad: true);
        }
        finally { PluginBusy(false); }
    }

    TrialRecord? TrialOf(object sender)
    {
        if (sender is FrameworkElement { Tag: PluginRow { TrialRec: { } rec } }) return rec;
        if (sender is FrameworkElement { DataContext: PluginRow { TrialRec: { } ctx } }) return ctx;
        if (sender is FrameworkElement { DataContext: TrialRecord t }) return t;
        return null;
    }

    void TrialCommit_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        var rec = TrialOf(sender);
        if (inst is null || rec is null) return;
        try
        {
            _trials.Commit(inst, rec, new Progress<string>(PluginSay));
            _state.Reload();
            LoadPlugins();
        }
        catch (Exception ex)
        {
            PluginSay(PadError.Describe(ex));
            _win.Toast(PadError.Describe(ex), bad: true);
        }
    }

    async void TrialDrop_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        var rec = TrialOf(sender);
        if (inst is null || rec is null) return;
        try
        {
            await _trials.Discard(inst, rec, new Progress<string>(PluginSay), CancellationToken.None);
            LoadPlugins();
        }
        catch (Exception ex)
        {
            PluginSay(PadError.Describe(ex));
            _win.Toast(PadError.Describe(ex), bad: true);
        }
    }

    // ---- projected packs -------------------------------------------------

    void PackBusy(bool busy)
    {
        ImportBtn.IsEnabled = !busy;
        ScanBtn.IsEnabled = !busy;
        PackRefreshBtn.IsEnabled = !busy;
        PackSpinner.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        var blank = PackList.ItemsSource is null;
        PackSkeleton.Visibility = busy && blank ? Visibility.Visible : Visibility.Collapsed;
        if (busy && blank) PackEmptyBox.Visibility = Visibility.Collapsed;
    }

    void PackSay(string line)
    {
        PackLogCard.Visibility = Visibility.Visible;
        PackLog.AppendText(line + Environment.NewLine);
        PackLog.ScrollToEnd();
    }

    bool PackChainReady()
    {
        var missing = _packs.Unavailable();
        if (missing is null) return true;
        PackList.ItemsSource = null;
        Tally(0, 0);
        PackEmpty.Text = string.IsNullOrEmpty(missing.Detail)
            ? missing.Reason
            : missing.Reason + " " + missing.Detail;
        PackEmptyBox.Visibility = Visibility.Visible;
        return false;
    }

    void Tally(int on, int total)
    {
        PackCount.Content = $"{on}/{total} 启用";
        PackCount.Visibility = total == 0 ? Visibility.Collapsed : Visibility.Visible;
        PackMeter.Maximum = total;
        PackMeter.Value = on;
        PackMeter.Visibility = total == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>包行/插件行看哪个 profile：插件页当前选择 → 全局选中 → 实例 lastProfile → 第一个。</summary>
    string ProfileForFacts(Instance inst) =>
        SelectedPluginProfile()
        ?? _state.SelectedProfile?.Name
        ?? inst.LastProfile
        ?? _state.Launcher.Profiles(inst).FirstOrDefault()?.Name
        ?? "";

    /// <summary>P2：读这个实例的装包账本（旧实例没有文件 = 空账本，不炸）。</summary>
    PackLedger LedgerFor(Instance inst) => PackLedger.Load(_state.Launcher, inst);

    /// <summary>
    /// pack list 行（投影目录 id）→ 账本 packId（包文件 stem）。账本条目按
    /// <c>content.dir</c> 的目录名对上投影；对不上再按 id/标题兜底。
    /// </summary>
    static string? LedgerPackKeyFor(PackLedger ledger, PackRow row)
    {
        if (ledger.Data.Packs.Count == 0) return null;
        foreach (var kv in ledger.Data.Packs)
        {
            var dir = (kv.Value.Content.Dir ?? "").Trim().TrimEnd('/', '\\');
            var projection = dir.Length == 0 ? "" : Path.GetFileName(dir.Replace('/', Path.DirectorySeparatorChar));
            if (string.Equals(projection, row.Id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(kv.Key, row.Id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(kv.Key, row.Title, StringComparison.OrdinalIgnoreCase))
                return kv.Key;
        }
        foreach (var kv in ledger.Data.Packs)
            if (string.Equals(kv.Value.Title, row.Title, StringComparison.OrdinalIgnoreCase)) return kv.Key;
        return null;
    }

    /// <summary>包行对应的包文件（图标用）：账本登记路径优先；无账本老实例退回实例来源包（标题匹配）。</summary>
    static string? PackFileForRow(Instance inst, PackLedger ledger, string? packKey, PackRow row)
    {
        try
        {
            var fromLedger = packKey is null ? null : ledger.Get(packKey)?.Path;
            if (!string.IsNullOrWhiteSpace(fromLedger) && File.Exists(fromLedger)) return fromLedger;
            var source = inst.Source;
            if (source is null) return null;
            var matches = packKey is not null
                ? string.Equals(source.PackId, packKey, StringComparison.OrdinalIgnoreCase)
                : string.Equals(source.PackTitle, row.Title, StringComparison.OrdinalIgnoreCase);
            return matches ? source.PackPath : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>P2：要求这个插件的包名（账本 owners 优先；老实例退回 P1 的投影事实）。</summary>
    List<string> PackOwnersOf(Instance inst, string profile, string spec)
    {
        var ledger = LedgerFor(inst);
        var fromLedger = ledger.OwnersOf(profile, spec)
            .Select(id => ledger.Get(id)?.Title is { Length: > 0 } title ? title : id)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return fromLedger.Count > 0 ? fromLedger : _pluginFacts.PackTitlesOf(spec).ToList();
    }

    /// <summary>
    /// P2：插件被卸/改版本后，把要求它的已装包标成 incomplete（卸 = 从账本摘掉该条，
    /// 更新 = 只改状态；缺件由 <see cref="PackLedger.GapsFor"/> 读时对账出）。返回包名。
    /// </summary>
    List<string> MarkPacksIncomplete(Instance inst, string profile, IEnumerable<string> specs, bool removed)
    {
        var ledger = LedgerFor(inst);
        var titles = new List<string>();
        var touched = false;
        foreach (var spec in specs)
        {
            foreach (var id in ledger.OwnersOf(profile, spec))
            {
                var pack = ledger.Get(id);
                if (pack is null) continue;
                touched = true;
                if (removed && ledger.RecordPluginRemoved(id, profile, spec)) { /* 已从账本摘掉 */ }
                else ledger.SetState(id, profile, PackLedger.StateIncomplete);
                var title = pack.Title.Length > 0 ? pack.Title : id;
                if (!titles.Contains(title, StringComparer.OrdinalIgnoreCase)) titles.Add(title);
            }
        }
        if (touched) ledger.Save();
        return titles;
    }

    /// <summary>P2：装包结果不静默——成功一条 toast；冲突/失败/提醒用 Ask 列出来。</summary>
    void ShowPackApplyResult(PackApplyResult result)
    {
        var lines = new List<string> { result.Text };
        foreach (var conflict in result.Conflicts.Take(4)) lines.Add("冲突：" + conflict.Text);
        foreach (var failure in result.Failures.Take(4)) lines.Add(failure.Text);
        foreach (var warning in result.Warnings.Take(4)) lines.Add(warning);
        if (result.Ok && result.Warnings.Count == 0)
        {
            _win.Toast(result.Text);
            return;
        }
        _win.Ask(result.Ok ? "装好了（有提醒）" : "整合包 · 没有静默处理",
            string.Join(Environment.NewLine + Environment.NewLine, lines), warn: !result.Ok);
    }

    /// <summary>P3：装一次（兼容门/版本冲突以结构化结果返回，不抛）。</summary>
    Task<PackApplyResult> ApplyPackOnceAsync(Instance inst, string packPath, string profile) =>
        _packs.ApplyPackToInstance(inst, packPath, profile, new Progress<string>(PackSay),
            CancellationToken.None);

    /// <summary>
    /// P3：实例页装包的完整交互——兼容门三选一（接受风险 / 换内核 / 取消）→ 版本冲突三选一
    /// （升到包版本 / 保留现有 / 取消）→ 结果展示。重试最多 2 次（一次兼容授权、一次冲突升级），
    /// 不静默、不死循环。
    /// </summary>
    async Task ApplyPackToInstanceInteractiveAsync(Instance inst, string packPath, string profile)
    {
        PackApplyResult result;
        try
        {
            result = await ApplyPackOnceAsync(inst, packPath, profile);
        }
        catch (Exception ex)
        {
            PackSay(PadError.Describe(ex));
            if (PackCompat.FromError(ex) is { } thrownFix && await AskAndApplyCompatAsync(inst, profile, thrownFix) is { } recovered)
            {
                inst = recovered;
                try { result = await ApplyPackOnceAsync(inst, packPath, profile); }
                catch (Exception retryEx) { PackSay(PadError.Describe(retryEx)); _win.Toast(PadError.Describe(retryEx), bad: true); return; }
            }
            else
            {
                _win.Toast(PadError.Describe(ex), bad: true);
                return;
            }
        }

        var retries = 0;
        while (retries < 2)
        {
            if (PackCompat.FromApplyResult(result) is { } fix)
            {
                retries++;
                var next = await AskAndApplyCompatAsync(inst, profile, fix);
                if (next is null) return;
                inst = next;
                result = await ApplyPackOnceAsync(inst, packPath, profile);
                continue;
            }
            if (result.Conflicts.Count > 0)
            {
                retries++;
                var upgraded = await ResolveConflictsAsync(inst, profile, result.Conflicts);
                if (upgraded is null) return;
                if (upgraded == 0) break;   // 全保留现有：账本已标 incomplete，直接展示结果
                result = await ApplyPackOnceAsync(inst, packPath, profile);
                continue;
            }
            break;
        }
        ShowPackApplyResult(result);
    }

    /// <summary>
    /// P3 兼容三选一：第一问最想要的动作「接受风险安装」；否则第二问「换一个已装内核重试」。
    /// 返回重试要用的实例（换内核后是新的），取消/失败返回 null。
    /// </summary>
    async Task<Instance?> AskAndApplyCompatAsync(Instance inst, string profile, PackCompatFix fix)
    {
        var accept = _win.Ask("插件与内核不兼容 · 接受风险安装",
            $"这个整合包里的插件与它钉的内核不兼容：{fix.Text}。\n\n"
            + $"接受风险会允许 {fix.PackageVersion} 在 DSH {fix.DshVersion} 上运行；可能崩溃或损坏数据，"
            + "但这是 DSH 官方给的正门（豁免只对这两个精确版本生效）。\n\n接受风险安装？",
            warn: true, okText: "接受风险安装", cancelText: "换内核/取消…");
        if (accept)
        {
            // DSH 提示里的 profile 优先（不兼容发生在哪个 profile 就给哪个 profile 授权）。
            var target = fix.Profile.Length > 0 ? fix.Profile : profile;
            try
            {
                await _state.Launcher.AllowVersion(inst, target, fix.PackageVersion, fix.DshVersion,
                    new Progress<string>(PackSay), CancellationToken.None);
                PackSay($"已按 DSH 通道允许 {fix.PackageVersion} 在 DSH {fix.DshVersion} 上运行");
                return inst;
            }
            catch (Exception ex)
            {
                PackSay(PadError.Describe(ex));
                _win.Toast("接受风险没成功：" + PadError.Describe(ex), bad: true);
                return null;
            }
        }
        return SwitchKernel(inst);
    }

    /// <summary>
    /// P3 第二问：列出本机已装内核 → 用户输入/选一个 → PinInstance → Reload。
    /// 取消返回 null（并说明这是包内容/版本问题）。
    /// </summary>
    Instance? SwitchKernel(Instance inst)
    {
        var versions = _state.Releases.Select(r => r.Version)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (versions.Count == 0)
        {
            _win.Toast("本机没有别的已装内核可换。这是包内容/版本问题：按当前内核重新导出包，或改插件版本后重试", bad: true);
            return null;
        }
        var pick = _win.AskInput("插件与内核不兼容 · 换内核重试",
            "本机已装内核：" + string.Join("、", versions) + Environment.NewLine + Environment.NewLine
            + "输入要换的版本号（留空取消）：", inst.Dsh.Version, warn: true);
        if (string.IsNullOrWhiteSpace(pick))
        {
            _win.Toast("取消：这是包内容/版本问题——按当前内核重新导出包，或改插件版本后重试", bad: true);
            return null;
        }
        var chosen = versions.FirstOrDefault(v => string.Equals(v, pick.Trim(), StringComparison.OrdinalIgnoreCase));
        if (chosen is null)
        {
            _win.Toast("本机没装这个内核：" + pick.Trim(), bad: true);
            return null;
        }
        try
        {
            _state.Launcher.PinInstance(inst.Id, chosen);
            _state.Reload();
            var fresh = _state.Launcher.Instance(inst.Id) ?? inst;
            _state.SelectedInstance = _state.Instances.FirstOrDefault(i => i.Id == fresh.Id) ?? _state.SelectedInstance;
            PackSay($"已改钉到 {chosen}，重试装包…");
            return fresh;
        }
        catch (Exception ex)
        {
            PackSay(PadError.Describe(ex));
            _win.Toast(PadError.Describe(ex), bad: true);
            return null;
        }
    }

    /// <summary>
    /// P3 冲突三选一：升到包版本 / 保留现有（包标 incomplete，可用「修复」后补）/ 取消。
    /// 返回升级成功的个数；用户取消返回 null。
    /// </summary>
    async Task<int?> ResolveConflictsAsync(Instance inst, string profile,
        IReadOnlyList<PackPluginConflict> conflicts)
    {
        var upgraded = 0;
        foreach (var conflict in conflicts)
        {
            if (_win.Ask("插件版本冲突 · " + conflict.Spec,
                    $"包要 {conflict.Spec} 的 {conflict.Expected}，这个 profile 里是 {conflict.Actual}。\n\n"
                    + "升到包版本会改掉现有版本；保留现有则包标不完整（可用「按包修复」后补）。\n\n升到包版本？",
                    warn: true, okText: "升到包版本", cancelText: "保留现有/取消…"))
            {
                try
                {
                    var name = PackCompat.Split(conflict.Spec, out var bare, out _)
                        ? bare
                        : LaunchPolicy.BundleBareName(conflict.Spec);
                    var target = $"{name}@{conflict.Expected}";
                    PackSay($"upgrade {target} → {profile}");
                    await _state.Launcher.PluginOp(inst.Id, "add", target,
                        new Progress<string>(PackSay), CancellationToken.None, profile);
                    upgraded++;
                }
                catch (Exception ex)
                {
                    PackSay(PadError.Describe(ex));
                    _win.Toast($"升级 {conflict.Spec} 没成功：" + PadError.Describe(ex), bad: true);
                    return null;
                }
                continue;
            }
            if (!_win.Ask("插件版本冲突 · " + conflict.Spec,
                    $"保留 {conflict.Spec} 现在的 {conflict.Actual}？包会标不完整，之后可点「按包修复」重试。"
                    + Environment.NewLine + Environment.NewLine + "取消 = 不再处理剩余冲突。",
                    warn: true, okText: "保留现有", cancelText: "取消")) return null;
        }
        if (upgraded > 0) PackSay($"已把 {upgraded} 个插件升到包要求的版本，重试整包装配…");
        else _win.Toast("已保留现有版本；包标不完整，可用包行「修复」按包重试");
        return upgraded;
    }

    /// <summary>
    /// D105 UX：实例页整合包行——已启用（Enabled=true）的包行发 accent 呼吸光；
    /// 停用的行无光并整体调暗（0.85），让"发光"真的表示这个包此刻是启用的。
    /// 每行一个轻量 DropShadowEffect（只在包行，数量小；不滥用 Effect）。
    /// </summary>
    void PackRow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PackListRow row } card) return;
        if (!row.Enabled)
        {
            card.Effect = null;
            card.Opacity = 0.85;
            return;
        }
        card.Opacity = 1;
        var glow = new DropShadowEffect
        {
            Color = (Color)FindResource("C.Accent"),
            BlurRadius = 14,
            ShadowDepth = 0,
            Opacity = 0.6,
        };
        var breathe = new DoubleAnimation(0.35, 0.7, TimeSpan.FromSeconds(1.6))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        glow.BeginAnimation(DropShadowEffect.OpacityProperty, breathe);
        var blur = new DoubleAnimation(14, 24, TimeSpan.FromSeconds(1.6))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, blur);
        card.Effect = glow;
    }

    async Task LoadPacks()
    {
        var inst = _state.SelectedInstance;
        if (inst is null || !PackChainReady()) return;
        PackBusy(true);
        try
        {
            var rows = await _packs.List(inst, CancellationToken.None);
            // P1：包行副标题「带 N 个插件（M 个不在这个 profile）」来自只读事实（投影 + profile）。
            var profile = ProfileForFacts(inst);
            var facts = PackPluginFacts.Collect(inst, profile);
            var ledger = LedgerFor(inst);
            var viewRows = new List<PackListRow>();
            foreach (var r in rows)
            {
                // P2：账本条目决定「不完整 / 未登记 / 可修复」；老实例没有账本也能显示（只是标未登记）。
                var packKey = LedgerPackKeyFor(ledger, r);
                // P3 add-on（M2 发现）：catalog DB 里可能残留已删包的 allow 行——投影目录已不在且
                // 账本没有条目 → 是 catalog 残留，直接隐藏；只有目录还在的老实例才保留「未登记」。
                if (packKey is null && !string.IsNullOrWhiteSpace(r.Dir) && !Directory.Exists(r.Dir))
                    continue;
                var state = "";
                var gaps = 0;
                var missing = 0;
                var tip = new List<string>();
                if (packKey is not null)
                {
                    state = ledger.EffectiveState(profile, packKey);
                    var gapRows = ledger.GapsFor(profile, packKey);
                    gaps = gapRows.Count;
                    missing = ledger.MissingFor(profile, packKey).Count;
                    foreach (var gap in gapRows.Take(4)) tip.Add(gap.Text);
                    foreach (var conflict in ledger.VersionConflicts(profile, packKey).Take(3))
                        tip.Add(conflict.Text);
                }
                viewRows.Add(new PackListRow(r, facts.PackOf(r.Id) ?? facts.PackOf(r.Title),
                    packKey, state, gaps, missing, tip, PackFileForRow(inst, ledger, packKey, r)));
            }
            PackList.ItemsSource = viewRows;
            Tally(viewRows.Count(r => r.Enabled), viewRows.Count);
            PackEmptyBox.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            PackSay(PadError.Describe(ex));
            _win.Toast(PadError.Describe(ex), bad: true);
        }
        finally
        {
            PackBusy(false);
        }
    }

    void PackRefresh_Click(object sender, RoutedEventArgs e) => _ = LoadPacks();

    /// <summary>
    /// 反向兼容：把这个实例导出成 PackForge <c>.dspack</c>（profile 或 dshhome 自动判别），
    /// 别的启动器（HDSL / PackForge GUI / official-desktop）也能装。sessions / 凭据 / 运行数据不进包。
    /// </summary>
    async void ExportPackforge_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var dlg = new SaveFileDialog
        {
            Title = "导出为 PackForge .dspack",
            Filter = "PackForge 整合包 (*.dspack)|*.dspack|所有文件|*.*",
            FileName = $"{inst.Id}-{inst.Dsh.Version}.dspack",
        };
        if (dlg.ShowDialog(_win) != true) return;
        try
        {
            _win.Toast("正在导出 " + System.IO.Path.GetFileName(dlg.FileName));
            var json = await _packs.PackforgeExportInstance(
                inst.Home, dlg.FileName, null, inst.Id, null, inst.Dsh.Version, CancellationToken.None);
            var path = dlg.FileName;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("path", out var p) && p.GetString() is { Length: > 0 } got)
                    path = got;
            }
            catch { /* 导出已成功，路径落回对话框选择 */ }
            _win.Toast("导出好了：" + path);
            try { Reveal.Path(System.IO.Path.GetDirectoryName(path) ?? ""); } catch { /* reveal 只是便利 */ }
        }
        catch (Exception ex)
        {
            _win.Toast(PadError.Describe(ex), bad: true);
        }
    }

    /// <summary>
    /// D107 安装概念：实例页「装整合包」→ 包库选择器（不再直接弹文件框）。
    /// 选择器支持「从文件…」回首层旧的 Import_Click（保留 ApplyPackToInstance 路径）。
    /// </summary>
    void OpenPackPicker_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var profile = ProfileForFacts(inst);
        var picker = new PackPickerWindow(inst.Name, profile) { Owner = Window.GetWindow(this) };
        var fromFile = false;
        picker.FromFileRequested += () => fromFile = true;
        picker.RowActionRequested += row => { _ = PickerRowActionAsync(picker, inst, profile, row); };
        _ = picker.LoadAsync(_packs, inst, profile, _state.Launcher.Root);
        picker.ShowDialog();
        if (fromFile) Import_Click(this, new RoutedEventArgs());
    }

    /// <summary>包库列表 + 逐行后台预检：真实数据逻辑在 PackPickerWindow.LoadAsync（版本页/截图共用）。</summary>
    Task LoadPackPickerAsync(PackPickerWindow picker, Instance inst, string profile) =>
        picker.LoadAsync(_packs, inst, profile, _state.Launcher.Root);
    /// <summary>选择器里点「安装/修复」：修复直接 RepairPack；安装进预检授权窗口（与版本页同一流程）。</summary>
    async Task PickerRowActionAsync(PackPickerWindow picker, Instance inst, string profile,
        PackPickerWindow.Row row)
    {
        var packs = new Packs(_state.Launcher);
        PackCard? card;
        try
        {
            card = await packs.ResolveCard(row.Key, new Progress<string>(PackSay), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _win.Toast(PadError.Describe(ex), bad: true);
            return;
        }
        if (card is null)
        {
            _win.Toast("这个包在包库里找不到了：" + row.Title, bad: true);
            return;
        }
        if (row.Repair)
        {
            try
            {
                var preflight = await packs.PreflightPack(inst, profile, card,
                    new Progress<string>(PackSay), CancellationToken.None);
                var repaired = await packs.RepairPack(inst, preflight.PackId, profile,
                    new Progress<string>(PackSay), CancellationToken.None);
                _win.Toast(repaired.Text, repaired.StillMissing.Count > 0);
                _state.Reload();
                await LoadPacks();
                LoadPlugins();
                await picker.LoadAsync(_packs, inst, profile, _state.Launcher.Root);
            }
            catch (Exception ex)
            {
                _win.Toast(PadError.Describe(ex), bad: true);
            }
            return;
        }

        picker.Hide();
        try
        {
            await PackPreflightWindow.RunInstallFlowAsync(picker, packs, inst, profile, card,
                new Progress<string>(PackSay),
                afterApplied: () =>
                {
                    _state.Reload();
                    _ = LoadPacks();
                    LoadPlugins();
                    _ = picker.LoadAsync(_packs, inst, profile, _state.Launcher.Root);
                },
                openInstance: () =>
                {
                    _state.SelectedInstance = inst;
                    _win.ShowInner("instance", "版本设置");
                },
                reExport: () => ReExportPackFileAsync(card),
                toast: (text, bad) => _win.Toast(text, bad));
        }
        finally
        {
            picker.Show();
            picker.Activate();
        }
    }

    /// <summary>报告里的「重新导出这个包」：拷一份包文件到用户选的位置（与版本页右键导出同语义）。</summary>
    Task ReExportPackFileAsync(PackCard card)
    {
        if (!System.IO.File.Exists(card.Path))
        {
            _win.Toast("这个包的文件不见了", bad: true);
            return Task.CompletedTask;
        }
        var isZip = card.Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        var dlg = new SaveFileDialog
        {
            Title = "重新导出这个包",
            Filter = isZip ? "整合包 (*.zip)|*.zip" : "整合包 (*.pack.json)|*.pack.json",
            FileName = System.IO.Path.GetFileName(card.Path),
        };
        if (dlg.ShowDialog(_win) != true) return Task.CompletedTask;
        try
        {
            System.IO.File.Copy(card.Path, dlg.FileName, overwrite: true);
            _win.Toast($"已导出：{System.IO.Path.GetFileName(dlg.FileName)}");
        }
        catch (Exception ex)
        {
            _win.Toast("导出失败：" + ex.Message, bad: true);
        }
        return Task.CompletedTask;
    }

    async void Import_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        if (!PackChainReady())
        {
            if (_packs.Unavailable() is { } missing)
                _win.Toast($"{missing.Reason}。{missing.Detail}", bad: true);
            return;
        }

        var dlg = new OpenFileDialog
        {
            Title = "选一个整合包",
            Filter = "整合包 (*.pack.zip;*.pack.json;*.pinst.zip)|*.pack.zip;*.pack.json;*.pinst.zip|所有文件|*.*",
        };
        if (dlg.ShowDialog(_win) != true) return;

        PackBusy(true);
        var log = new Progress<string>(PackSay);
        try
        {
            var file = dlg.FileName;
            var profile = ProfileForFacts(inst);
            // .pinst.zip 是实例快照，仍走 TS import 链；整包（.pack.zip/.pack.json）走 R8 统一装配链：
            // 投影 + 内容启用 + 确保管理器 + 按 diff 装插件 + 写账本。
            if (file.EndsWith(".pinst.zip", StringComparison.OrdinalIgnoreCase))
            {
                PackSay($"import {System.IO.Path.GetFileName(file)} -> {inst.Id}");
                await _packs.Import(file, log, CancellationToken.None);
                _state.Reload();
                _win.Toast("实例包导入好了：" + System.IO.Path.GetFileName(file));
            }
            else
            {
                if (profile.Length == 0)
                {
                    _win.Toast("这份实例还没有 profile，先建一个再接整合包", bad: true);
                    return;
                }
                PackSay($"apply {System.IO.Path.GetFileName(file)} -> {inst.Id} / {profile}");
                await ApplyPackToInstanceInteractiveAsync(inst, file, profile);
                _state.Reload();
            }
            await LoadPacks();
            LoadPlugins();
        }
        catch (Exception ex)
        {
            PackSay(PadError.Describe(ex));
            _win.Toast(PadError.Describe(ex), bad: true);
        }
        finally
        {
            PackBusy(false);
        }
    }

    async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (!PackChainReady())
        {
            if (_packs.Unavailable() is { } missing)
                _win.Toast($"{missing.Reason}。{missing.Detail}", bad: true);
            return;
        }
        PackBusy(true);
        var log = new Progress<string>(PackSay);
        try
        {
            PackSay("scan-drop " + _state.Launcher.Root);
            await _packs.ScanDrop(log, CancellationToken.None);
            _state.Reload();
            await LoadPacks();
        }
        catch (Exception ex)
        {
            PackSay(PadError.Describe(ex));
            _win.Toast(PadError.Describe(ex), bad: true);
        }
        finally
        {
            PackBusy(false);
        }
    }

    async void TogglePack_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null || sender is not Button { Tag: PackListRow row }) return;
        var profile = ProfileForFacts(inst);
        if (profile.Length == 0)
        {
            _win.Toast("先选一个 profile（插件页的 profile 下拉）", bad: true);
            return;
        }

        PackBusy(true);
        var log = new Progress<string>(PackSay);
        try
        {
            var active = !row.Enabled;
            if (row.HasLedgerEntry)
            {
                // P2：内容白名单 + dsh.profile.bundles 联动（摘/加 bundle 行，dependencies 保留）。
                var ledger = LedgerFor(inst);
                var shared = ledger.SharedFor(profile, row.PackKey);
                var result = await _packs.SetPackPluginsActive(inst, row.PackKey, profile, active,
                    log, CancellationToken.None);
                var lines = new List<string> { result.Text };
                if (shared.Count > 0)
                    lines.Add((active ? "这些插件还被其他包要求，已一起加回：" : "这些插件还被其他包要求，只摘了 bundle 行、依赖保留：")
                              + string.Join("、", shared));
                foreach (var warning in result.Warnings.Take(3)) lines.Add(warning);
                _win.Toast(string.Join("；", lines), bad: result.State == PackLedger.StateIncomplete);
            }
            else
            {
                // 旧实例没有账本：只切内容白名单（P2.6 不炸；装一次包会补写账本）。
                if (active) await _packs.Allow(inst, row.ContentKey, log, CancellationToken.None);
                else await _packs.Deny(inst, row.ContentKey, log, CancellationToken.None);
                _win.Toast($"{(active ? "已启用" : "已停用")} {row.Title}（这个实例还没有装包账本，只切了内容白名单）");
            }
            await LoadPacks();
            LoadPlugins();
        }
        catch (Exception ex)
        {
            PackSay(PadError.Describe(ex));
            _win.Toast(PadError.Describe(ex), bad: true);
        }
        finally
        {
            PackBusy(false);
        }
    }

    /// <summary>
    /// P2：卸载整合包（§3.3）：删投影目录 + 只回滚「只被这个包要求」的插件（共享插件保留并提示）
    /// + 清本地插件目录 + 删账本条目。确认文案先写清删什么。
    /// </summary>
    async void UninstallPack_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null || sender is not Button { Tag: PackListRow row }) return;
        var profile = ProfileForFacts(inst);
        var ledger = LedgerFor(inst);
        var shared = row.HasLedgerEntry ? ledger.SharedFor(profile, row.PackKey) : [];
        var exclusive = row.HasLedgerEntry ? ledger.ExclusiveFor(profile, row.PackKey) : [];
        var lines = new List<string>
        {
            $"卸载整合包「{row.Title}」？",
            row.HasLedgerEntry
                ? $"会删掉工作区里的投影目录，并回滚 {exclusive.Count} 个只被它要求的插件"
                  + (exclusive.Count > 0 ? $"（{string.Join("、", exclusive)}）" : "")
                : "这个实例的账本里没有它：只删投影目录，插件一个都不动",
        };
        if (shared.Count > 0) lines.Add("这些插件还被其他包要求，会保留：" + string.Join("、", shared));
        if (!_win.Ask("卸载整合包", string.Join(Environment.NewLine, lines), warn: true)) return;

        PackBusy(true);
        var log = new Progress<string>(PackSay);
        try
        {
            var packRef = row.HasLedgerEntry ? row.PackKey : row.ContentKey;
            var result = await _packs.UninstallPack(inst, packRef, profile, log, CancellationToken.None);
            var text = result.Text;
            if (result.SkippedShared.Count > 0)
                text += "；保留共享插件：" + string.Join("、", result.SkippedShared);
            foreach (var warning in result.Warnings.Take(3)) text += "；" + warning;
            _win.Toast(text, bad: result.Warnings.Count > 0);
            _state.Reload();
            await LoadPacks();
            LoadPlugins();
        }
        catch (Exception ex)
        {
            PackSay(PadError.Describe(ex));
            _win.Toast(PadError.Describe(ex), bad: true);
        }
        finally
        {
            PackBusy(false);
        }
    }

    /// <summary>P2：按包修复（只在账本判定 incomplete / 有缺件时出现）：照账本补缺失插件。</summary>
    async void RepairPack_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null || sender is not Button { Tag: PackListRow row }) return;
        if (!row.HasLedgerEntry) return;
        var profile = ProfileForFacts(inst);
        PackBusy(true);
        var log = new Progress<string>(PackSay);
        try
        {
            PackSay($"repair {row.Title} -> {profile}");
            var result = await _packs.RepairPack(inst, row.PackKey, profile, log, CancellationToken.None);
            var text = result.Text;
            foreach (var warning in result.Warnings.Take(3)) text += "；" + warning;
            _win.Toast(text, bad: result.StillMissing.Count > 0);
            _state.Reload();
            await LoadPacks();
            LoadPlugins();
        }
        catch (Exception ex)
        {
            PackSay(PadError.Describe(ex));
            _win.Toast(PadError.Describe(ex), bad: true);
        }
        finally
        {
            PackBusy(false);
        }
    }

    // ---- named allow-set presets ----------------------------------------

    async Task LoadSetList()
    {
        var inst = _state.SelectedInstance;
        if (inst is null) { SetList.ItemsSource = null; return; }
        try
        {
            var names = await _packs.SetList(inst, CancellationToken.None);
            SetList.ItemsSource = names;
        }
        catch { SetList.ItemsSource = null; }
    }

    async void SetSave_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        var name = SetNameBox.Text.Trim();
        if (inst is null) return;
        if (name.Length == 0) { SetNameBox.Focus(); return; }
        try
        {
            PackSay($"set-save {name}");
            await _packs.SetSave(inst, name, new Progress<string>(PackSay), CancellationToken.None);
            _win.Toast($"白名单套装 {name} 已保存");
            await LoadSetList();
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    async void SetLoad_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        var name = SetNameBox.Text.Trim();
        if (inst is null) return;
        if (name.Length == 0) { SetNameBox.Focus(); return; }
        await LoadSet(inst, name);
    }

    async void SetLoadNamed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Content: string name }) return;
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        await LoadSet(inst, name);
    }

    async Task LoadSet(Instance inst, string name)
    {
        try
        {
            PackSay($"set-load {name}");
            await _packs.SetLoad(inst, name, new Progress<string>(PackSay), CancellationToken.None);
            _win.Toast($"已载入白名单套装 {name}");
            await LoadPacks();
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    void OpenProjection_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        Reveal.Path(_state.Launcher.ProjectionDir(inst));
    }

}

/// <summary>
/// 实例页整合包行：PackRow 外面包一层显示用模型。
/// P1：副标题「带 N 个插件（M 个不在这个 profile）」；
/// P2：叠账本事实——有效状态（installed/disabled/incomplete/absent = 未登记）、缺件数、
/// 「修复」是否出现；装/停用/卸载/修复都走 row 上的 <see cref="PackKey"/>。
/// </summary>
public sealed class PackListRow(PackRow row, PackFacts? facts, string? ledgerKey,
    string ledgerState, int gapCount, int missingCount, IReadOnlyList<string> ledgerTip,
    string? packPath)
{
    public PackRow Row { get; } = row;
    public string Id => Row.Id;
    public string Title => Row.Title;
    public string StateText => Row.StateText;
    public string VersionText => Row.VersionText;
    public string DescriptionText => Row.DescriptionText;
    public bool Enabled => Row.Enabled;

    /// <summary>整合包自己的 icon 文件（PackIcon 缓存/源文件）；空 = 回落「包」pill。</summary>
    public string IconPath { get; } = PackIcon.Resolve(AppState.Current.Launcher.Root, packPath) ?? "";
    public bool HasIcon => IconPath.Length > 0;

    /// <summary>allow/deny 的内容 id（投影目录名；旧实例没有账本时用它）。</summary>
    public string ContentKey => Row.Id;

    /// <summary>账本 packId（包文件 stem）；空 = 这个实例的账本里没有它（未登记）。</summary>
    public string PackKey => ledgerKey ?? "";
    public bool HasLedgerEntry => PackKey.Length > 0;
    public string LedgerState => ledgerState;
    public int GapCount => gapCount;
    public int MissingCount => missingCount;

    /// <summary>只有账本判定不完整 / 有缺件时才给「修复」入口。</summary>
    public bool CanRepair => HasLedgerEntry
        && (LedgerState == PackLedger.StateIncomplete || MissingCount > 0);

    /// <summary>不该静默的状态才响：不完整 / 投影缺失 / 未登记（正常/停用不占位）。</summary>
    public string LedgerBadge =>
        !HasLedgerEntry ? "未登记"
        : LedgerState == PackLedger.StateIncomplete || MissingCount > 0 ? "不完整"
        : LedgerState == PackLedger.StateAbsent ? "投影缺失"
        : "";

    public bool HasLedgerBadge => LedgerBadge.Length > 0;

    public string LedgerStateText => !HasLedgerEntry ? "账本：未登记（老实例；装一次包会补写）"
        : LedgerState == PackLedger.StateIncomplete ? "账本：不完整（可用「修复」补回）"
        : LedgerState == PackLedger.StateDisabled ? "账本：已停用（bundle 行已摘，依赖保留）"
        : LedgerState == PackLedger.StateAbsent ? "账本：投影缺失"
        : "账本：装好了";

    public string LedgerTip => ledgerTip.Count == 0
        ? LedgerStateText
        : LedgerStateText + Environment.NewLine + string.Join(Environment.NewLine, ledgerTip);

    /// <summary>「带 N 个插件（M 个不在这个 profile）」；包里没插件时为空（不占行）。</summary>
    public string PluginSummaryText => facts?.SummaryText ?? "";
    public string PluginSummaryTip => facts?.SummaryTip ?? "";
    public bool HasPluginSummary => PluginSummaryText.Length > 0;
}
