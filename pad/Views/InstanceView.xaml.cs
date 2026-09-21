using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    CancellationTokenSource? _pluginCts;

    public InstanceView(MainWindow win)
    {
        InitializeComponent();
        _win = win;
        _packs = new Packs(_state.Launcher);
        _trials = new Trials(_state.Launcher);
        DataContext = _state;
        ShowPane(Checked());
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
        if (App.ShotPage is "instance-plugins" or "instance-bundles")
        {
            NavPlugins.IsChecked = true;
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

    void OpenSession_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SessionRow row }) return;
        Reveal.Path(row.Path);
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
        var tag = new[] { FilterAll, FilterUpdatable, FilterTrial }
            .FirstOrDefault(r => r.IsChecked == true)?.Tag as string;
        return PluginQuery.Parse(tag);
    }

    void LoadPlugins()
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        TrialEmpty.Text = "没有试验中的插件";

        var names = _state.Launchables.ToList();
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
        _pluginRows = rows;
        PaintPlugins();
        _ = FetchPluginExtras(inst);
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

    void PaintPlugins()
    {
        var pick = SelectedPluginProfile();
        var inProfile = _pluginRows.Where(r => pick is null || r.Profile == pick).ToList();
        FilterAll.Content = $"全部 ({inProfile.Count})";
        FilterUpdatable.Content = $"可更新 ({inProfile.Count(r => r.Updatable)})";
        FilterTrial.Content = $"试验中 ({inProfile.Count(r => r.Trial)})";

        var shown = PluginQuery.Apply(inProfile, PluginSearch?.Text ?? "", CurrentPluginFilter()).ToList();
        PluginList.ItemsSource = shown;

        var empty = shown.Count == 0;
        PluginEmptyBox.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (pick is null)
            PluginEmpty.Text = "这份实例还没有 profile";
        else if (CurrentPluginFilter() == PluginQuery.Filter.Trial)
            PluginEmpty.Text = "没有试验中的插件";
        else if (CurrentPluginFilter() == PluginQuery.Filter.Updatable)
            PluginEmpty.Text = "没有可更新的插件";
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
        PaintPlugins();
    }

    void PluginFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (PluginList is null) return;
        PaintPlugins();
    }

    void PluginProfile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_pluginLoading || PluginList is null) return;
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

    void PluginInfo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: PluginRow row }) return;
        var md = $"# {row.Spec}\n\n"
            + $"- 版本：{row.VersionText}\n"
            + $"- `--profile`：{row.Profile}\n"
            + (row.Latest.Length > 0 ? $"- npm latest：{row.Latest}\n" : "")
            + (row.Keywords.Count > 0 ? $"- 标签：{string.Join("、", row.Keywords)}\n" : "")
            + (row.PackageDir.Length > 0 ? $"- 目录：`{row.PackageDir}`\n" : "")
            + (row.Trial ? "- 试验：仅本次运行，还没固化\n" : "")
            + (row.Description.Length > 0 ? $"\n{row.Description}\n" : "\n包里没写描述。\n");
        _win.ShowMd(row.Spec, md);
    }

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
        if (targets.Count > 1 || PluginFrom(sender) is not null)
        {
            var names = string.Join("、", targets.Select(t => t.Spec));
            if (!_win.Ask("移除插件", $"从 {profile} 移除 {names}？", warn: true)) return;
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
            PluginSay("移除了。下次启动生效。");
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
        PluginBusy(true);
        try
        {
            foreach (var t in targets)
            {
                PluginSay($"update {t.Spec} → {t.Profile}");
                await _state.Launcher.PluginOp(inst.Id, "update", t.Spec,
                    new Progress<string>(PluginSay), CancellationToken.None, t.Profile);
            }
            PluginSay("更新好了。下次启动生效。");
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

    async Task LoadPacks()
    {
        var inst = _state.SelectedInstance;
        if (inst is null || !PackChainReady()) return;
        PackBusy(true);
        try
        {
            var rows = await _packs.List(inst, CancellationToken.None);
            PackList.ItemsSource = rows;
            Tally(rows.Count(r => r.Enabled), rows.Count);
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
            PackSay($"project {System.IO.Path.GetFileName(dlg.FileName)} -> {inst.Id}");
            await _packs.Project(inst, dlg.FileName, log, CancellationToken.None);
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
        if (inst is null || sender is not Button { Tag: string packId } btn) return;
        if (btn.DataContext is not PackRow row) return;

        PackBusy(true);
        var log = new Progress<string>(PackSay);
        try
        {
            if (row.Enabled) await _packs.Deny(inst, packId, log, CancellationToken.None);
            else await _packs.Allow(inst, packId, log, CancellationToken.None);
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
