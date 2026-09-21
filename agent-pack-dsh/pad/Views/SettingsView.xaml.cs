using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Pad.Core;

namespace Pad.Views;

public partial class SettingsView : UserControl, IRefreshable
{
    readonly AppState _state = AppState.Current;
    bool _loading;
    string _layer = "launch";
    string? _detailRef;
    CredentialsGate.ImportedApi? _detailRow;
    CancellationTokenSource? _probeCts;

    public SettingsView()
    {
        _loading = true;
        InitializeComponent();
        DataContext = _state;
    }

    /// <summary>Switch the left rail. Used by --shot-page settings-ui|download|other|launch.</summary>
    public void OpenLayer(string id)
    {
        var btn = id switch
        {
            "api" => NavApi,
            "ui" => NavUi,
            "download" => NavDownload,
            "other" => NavOther,
            _ => NavLaunch,
        };
        btn.IsChecked = true;
        if (id == "api") ShowApiList();
    }

    public void OnShown() => Paint();

    void Paint()
    {
        _loading = true;
        var wt = Proc.Which("wt");
        OptWt.IsEnabled = wt is not null;
        WtHint.Text = wt is null ? "没有 wt.exe" : "";

        var s = _state.Settings;
        var pinned = _state.Launcher.Releases().FirstOrDefault();
        if (pinned is not null)
        {
            var bin = _state.Launcher.DshBin(pinned.Version);
            DshBinLine.Text = RelToRoot(bin);
            DshVerLine.Text = pinned.Version;
        }
        else
        {
            DshBinLine.Text = "版本库空";
            DshVerLine.Text = "";
        }
        var probe = DshSniff.Probe(s.Launch.DshHome, s.Launch.TuiPackage);
        SniffLine.Text = string.IsNullOrWhiteSpace(probe.Bin) || RelToRoot(probe.Bin) == DshBinLine.Text
            ? ""
            : "自动搜索 " + probe.Bin;
        DshTuiLine.Text = probe.Tui ?? "未找到";
        DshHomeBox.Text = string.IsNullOrWhiteSpace(s.Launch.DshHome)
            ? (probe.HomeExists ? probe.Home : "")
            : s.Launch.DshHome;
        OptWt.IsChecked = s.Kind == TerminalKind.WindowsTerminal && wt is not null;
        OptConhost.IsChecked = s.Kind == TerminalKind.Conhost
                               || (s.Kind == TerminalKind.WindowsTerminal && wt is null);
        OptCustom.IsChecked = s.Kind == TerminalKind.Custom;
        CustomBox.Text = s.TerminalCommand;
        CustomBox.IsEnabled = s.Kind == TerminalKind.Custom;

        TitleBox.Text = s.Launch.Title;
        ReuseWt.IsChecked = s.Launch.ReuseWtWindow;
        PickCombo(AfterBox, s.Launch.AfterLaunch);
        DefaultReleaseBox.Text = s.Launch.DefaultRelease;
        ExtraEnvBox.Text = s.Launch.ExtraEnv;
        PauseOnError.IsChecked = s.Launch.PauseOnError;
        TelemetryOff.IsChecked = s.Launch.TelemetryDisabled;
        PickCombo(NodeKind, LaunchPolicy.NodeKindTag(s.Launch.NodePath, instance: false));
        NodeBox.Text = LaunchPolicy.NodeIsCustom(s.Launch.NodePath) ? s.Launch.NodePath : "";
        SyncNodeCustom();
        PaintNodeHint();
        RuntimeInstallBox.IsChecked = s.Launch.RuntimeInstall;
        PickCombo(IndieDefaultBox, s.Launch.WorkspaceIndieDefault);
        SharedWorkspaceBox.Text = s.Launch.SharedWorkspace;
        PreCommandBox.Text = s.Launch.PreCommand;
        PreWaitBox.IsChecked = s.Launch.PreCommandWait;
        PickCombo(PriorityBox, s.Launch.ProcessPriority);
        DefaultProfileBox.Text = s.Launch.DefaultProfile;

        (s.Ui.Theme switch
        {
            "slate" => ThemeSlate,
            "forest" => ThemeForest,
            "amber" => ThemeAmber,
            "ink" => ThemeInk,
            "custom" => ThemeCustom,
            _ => ThemeTeal,
        }).IsChecked = true;
        CustomAccentBox.Text = s.Ui.CustomAccent;
        AnimateBox.IsChecked = s.Ui.Animate;
        BrandBox.IsChecked = s.Ui.ShowBrand;
        LogoBox.IsChecked = s.Ui.ShowLogo;
        BrandTextBox.Text = s.Ui.BrandText;
        PickCombo(AnimSpeedBox, s.Ui.AnimSpeed.ToString());
        PickCombo(ToastBox, s.Ui.ToastMs.ToString());
        PickCombo(ToastMaxBox, s.Ui.ToastMax.ToString());
        WallpaperBox.Text = s.Ui.WallpaperPath;
        LogoPathBox.Text = s.Ui.LogoPath;
        WallOpSlider.Value = s.Ui.WallpaperOpacity;
        ContentOpSlider.Value = s.Ui.ContentOpacity;
        ChromeOpSlider.Value = s.Ui.ChromeOpacity;
        PickCombo(WallFitBox, s.Ui.WallpaperFit);
        WinWBox.Text = s.Ui.WindowWidth.ToString();
        WinHBox.Text = s.Ui.WindowHeight.ToString();
        WinMarginBox.Text = s.Ui.WindowMargin.ToString();
        ChromeHBox.Text = s.Ui.ChromeHeight.ToString();
        WindowOpSlider.Value = s.Ui.WindowOpacity;
        WallpaperBlurSlider.Value = s.Ui.WallpaperBlur;
        WriteSliderLabels();
        WallpaperDirBox.Text = s.Ui.WallpaperDir;
        HideVersionEntryBox.IsChecked = s.Ui.HideVersionEntry;
        HiddenManageBox.IsChecked = s.Ui.HiddenTabs.Contains("manage");
        HiddenDownloadBox.IsChecked = s.Ui.HiddenTabs.Contains("download");

        RegistryBox.Text = s.Download.NpmRegistry;
        PickCombo(PageSizeBox, s.Download.MarketPageSize.ToString());
        MarketTimeoutBox.Text = s.Download.MarketTimeoutSec.ToString();
        InstallTimeoutBox.Text = s.Download.InstallTimeoutSec.ToString();
        GatewayTimeoutBox.Text = s.Download.GatewayTimeoutSec.ToString();
        ShelvesBox.Text = string.Join('\n',
            s.Download.Shelves.Select(x => $"{x.Id}|{x.Label}|{x.Query}"));
        PresetsBox.Text = string.Join('\n',
            s.Download.RegistryPresets.Select(x => $"{x.Label}|{x.Url}"));
        PaintRegistryButtons(s.Download.RegistryPresets);
        PnpmConcurrencyBox.Text = s.Download.PnpmNetworkConcurrency.ToString();
        CacheDirBox.Text = s.Download.CacheDir;
        AutoFetchBox.IsChecked = s.Download.AutoFetchOnOpen;

        RootLine.Text = _state.Launcher.Root;
        RootsBox.SelectedItem = _state.Launcher.Root;
        SettingsLine.Text = _state.Launcher.SettingsPath;
        ConfirmStop.IsChecked = s.Other.ConfirmStop;
        OpenLogOnError.IsChecked = s.Other.OpenLogOnError;
        BackupCorrupt.IsChecked = s.Other.BackupCorrupt;
        RefreshSecBox.Text = s.Other.RefreshSec.ToString();
        RegistriesRelBox.Text = s.Other.RegistriesRel;
        ShotDelayBox.Text = s.Other.ShotDelayMs.ToString();
        ShotMarketDelayBox.Text = s.Other.ShotMarketDelayMs.ToString();
        DebugBox.IsChecked = s.Other.Debug;
        PickCombo(UpdateChannelBox, s.Other.UpdateChannel);
        IdentifyLine.Text = s.Other.Identify;

        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        AboutLine.Text = $"PAD {ver} · schema {s.Schema}";
        if (PadUpdateLine.Text.Length == 0)
            PadUpdateLine.Text = $"PAD {ver?.ToString(3)}";
        if (DshUpdateLine.Text.Length == 0)
        {
            var installed = _state.Releases.FirstOrDefault()?.Version;
            DshUpdateLine.Text = installed is null ? "版本库是空的" : "已装 " + installed;
        }
        PaintApiReady();
        _loading = false;
    }

    void PaintRegistryButtons(IReadOnlyList<RegistryPreset> presets)
    {
        RegistryPresetRow.Children.Clear();
        foreach (var p in presets)
        {
            var btn = new Button
            {
                Style = (Style)FindResource("Btn"),
                Content = p.Label,
                Tag = p.Url,
                Margin = new Thickness(0, 0, 8, 0),
            };
            btn.Click += RegistryPreset_Click;
            RegistryPresetRow.Children.Add(btn);
        }
    }

    static void PickCombo(ComboBox box, string tag)
    {
        foreach (ComboBoxItem item in box.Items)
        {
            if ((item.Tag as string) == tag)
            {
                box.SelectedItem = item;
                return;
            }
        }
        if (box.Items.Count > 0) box.SelectedIndex = 0;
    }

    static string? ComboTag(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag as string;

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PaneLaunch is null || PaneApi is null || sender is not RadioButton { Tag: string tag }) return;
        _layer = tag;
        PaneApi.Visibility = tag == "api" ? Visibility.Visible : Visibility.Collapsed;
        PaneLaunch.Visibility = tag == "launch" ? Visibility.Visible : Visibility.Collapsed;
        PaneUi.Visibility = tag == "ui" ? Visibility.Visible : Visibility.Collapsed;
        PaneDownload.Visibility = tag == "download" ? Visibility.Visible : Visibility.Collapsed;
        PaneOther.Visibility = tag == "other" ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "api")
        {
            ShowApiList();
            PaintApiReady();
        }
        if (Fx.Animated)
        {
            var pane = tag switch
            {
                "api" => PaneApi,
                "ui" => PaneUi,
                "download" => PaneDownload,
                "other" => PaneOther,
                _ => PaneLaunch,
            };
            Fx.PageIn(pane);
        }
        ResetBtn.ToolTip = tag switch
        {
            "api" => "API Key 不能一键清空",
            "ui" => "初始化个性化",
            "download" => "初始化下载",
            "other" => "初始化其他",
            _ => "初始化启动",
        };
    }

    void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_layer == "api")
        {
            if (Window.GetWindow(this) is MainWindow w)
                w.Toast("API Key 不能一键清空", bad: true);
            return;
        }
        var s = _state.Settings;
        switch (_layer)
        {
            case "ui": s.Ui = UiPrefs.Defaults(); break;
            case "download": s.Download = DownloadPrefs.Defaults(); break;
            case "other": s.Other = OtherPrefs.Defaults(); break;
            default: s.Launch = LaunchPrefs.Defaults(); break;
        }
        Commit(s);
        Paint();
        if (Window.GetWindow(this) is MainWindow win)
            win.Toast("本页设置已恢复默认");
    }

    void Commit(PadSettings s)
    {
        _state.ApplySettings(s);
        if (Window.GetWindow(this) is MainWindow win)
        {
            win.ApplyBrand();
            win.ApplyWindowPrefs();
        }
    }

    void Term_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string tag }) return;
        var s = _state.Settings;
        s.Terminal = tag;
        CustomBox.IsEnabled = tag == "custom";
        Commit(s);
    }

    void Custom_Save(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = _state.Settings;
        s.TerminalCommand = CustomBox.Text.Trim();
        Commit(s);
    }

    void Launch_Save(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = _state.Settings;
        s.Launch.Title = TitleBox.Text.Trim();
        s.Launch.AfterLaunch = ComboTag(AfterBox) ?? "keep";
        s.Launch.ReuseWtWindow = ReuseWt.IsChecked == true;
        s.Launch.DefaultRelease = DefaultReleaseBox.Text.Trim();
        s.Launch.ExtraEnv = ExtraEnvBox.Text ?? "";
        s.Launch.PauseOnError = PauseOnError.IsChecked == true;
        s.Launch.TelemetryDisabled = TelemetryOff.IsChecked == true;
        s.Launch.DshHome = DshHomeBox.Text.Trim();
        s.Launch.NodePath = ComboTag(NodeKind) == "custom"
            ? (NodeBox.Text ?? "").Trim()
            : "";
        s.Launch.RuntimeInstall = RuntimeInstallBox.IsChecked == true;
        s.Launch.WorkspaceIndieDefault = ComboTag(IndieDefaultBox) ?? "owned";
        s.Launch.SharedWorkspace = SharedWorkspaceBox.Text.Trim();
        s.Launch.PreCommand = PreCommandBox.Text.Trim();
        s.Launch.PreCommandWait = PreWaitBox.IsChecked == true;
        s.Launch.ProcessPriority = ComboTag(PriorityBox) ?? "normal";
        s.Launch.DefaultProfile = DefaultProfileBox.Text.Trim();
        Commit(s);
        PaintNodeHint();
    }

    void Launch_Sel(object sender, SelectionChangedEventArgs e) => Launch_Save(sender, e);

    void NodeKind_Sel(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        SyncNodeCustom();
        Launch_Save(sender, e);
    }

    void SyncNodeCustom()
    {
        var custom = ComboTag(NodeKind) == "custom";
        NodeCustom.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
    }

    void PaintNodeHint()
    {
        if (NodeHint is null) return;
        var s = _state.Settings;
        var ov = LaunchPolicy.NodeIsCustom(s.Launch.NodePath) ? s.Launch.NodePath : LaunchPolicy.NodeAuto;
        var pin = _state.Launcher.Releases().FirstOrDefault();
        var ver = pin?.Version;
        var exe = _state.Launcher.PreviewNode(ov, ver, s);
        var engines = _state.Launcher.RequiredNodeVersion(ver) ?? LaunchPolicy.NodeFileVersion(exe);
        NodeHint.Text = LaunchPolicy.DescribeNodeWillUse(exe, engines);
        var warn = exe is null;
        NodeHintBar.Style = (Style)FindResource(warn ? "HintBarWarn" : "HintBar");
        NodeHint.Style = (Style)FindResource(warn ? "HintBarCopyWarn" : "HintBarCopy");
    }

    void NodeBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择 node.exe",
            Filter = "Node|node.exe|可执行文件|*.exe|所有|*.*",
            FileName = "node.exe",
        };
        if (dlg.ShowDialog() != true) return;
        _loading = true;
        PickCombo(NodeKind, "custom");
        NodeBox.Text = dlg.FileName;
        SyncNodeCustom();
        _loading = false;
        Launch_Save(sender, e);
    }

    // ---- API Key ---------------------------------------------------------

    void PaintApiReady()
    {
        if (ApiReadyList is null) return;
        var rows = CredentialsGate.ListImported(_state.Launcher, _state.Settings);
        ApiReadyList.ItemsSource = rows;
        ApiReadyList.Visibility = Visibility.Visible;
        if (ApiReadyEmpty is not null)
            ApiReadyEmpty.Visibility = Visibility.Collapsed;
        var ready = rows.Where(r => !r.RefMissing).ToList();
        if (ApiStatImported is not null)
            ApiStatImported.Text = ready.Count.ToString();
        if (ApiStatApis is not null)
            ApiStatApis.Text = rows.Count.ToString();
        var def = _state.Settings.Launch.CredentialsDefault ?? "global";
        var yaml = def == "none" ? null : _state.Launcher.GetCredentials(def);
        var have = new HashSet<string>(CredentialsFile.ListRefs(yaml), StringComparer.Ordinal);
        var ds = have.Contains(CredentialsFile.DeepseekRef);
        if (ApiStatDeepseek is not null)
            ApiStatDeepseek.Text = ds ? "已配" : "缺";
        if (ApiDeepseekBar is not null)
            ApiDeepseekBar.Visibility = ds ? Visibility.Collapsed : Visibility.Visible;
        var inst = ready
            .SelectMany(r => r.Instances.Split('、', StringSplitOptions.RemoveEmptyEntries))
            .Where(s => s != "无匹配实例")
            .Distinct()
            .Count();
        if (ApiStatInst is not null)
            ApiStatInst.Text = inst.ToString();
        if (ApiDetailStage?.Visibility == Visibility.Visible && _detailRef is not null)
        {
            var row = rows.FirstOrDefault(r => r.RefName == _detailRef);
            if (row is not null) BindApiDetail(row);
            else ShowApiList();
        }
    }

    void ShowApiList()
    {
        _probeCts?.Cancel();
        if (ApiListStage is not null) ApiListStage.Visibility = Visibility.Visible;
        if (ApiDetailStage is not null) ApiDetailStage.Visibility = Visibility.Collapsed;
    }

    void ApiRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CredentialsGate.ImportedApi row })
            OpenApiDetail(row);
    }

    void ApiDetailBack_Click(object sender, RoutedEventArgs e) => ShowApiList();

    void OpenApiDetail(CredentialsGate.ImportedApi row)
    {
        if (ApiListStage is not null) ApiListStage.Visibility = Visibility.Collapsed;
        if (ApiDetailStage is not null) ApiDetailStage.Visibility = Visibility.Visible;
        BindApiDetail(row);
        _ = ProbeDetailAsync(row);
    }

    void BindApiDetail(CredentialsGate.ImportedApi row)
    {
        _detailRow = row;
        _detailRef = row.RefName;
        if (ApiDetailCompany is not null) ApiDetailCompany.Text = row.Company;
        if (ApiDetailRef is not null) ApiDetailRef.Text = row.RefName;
        if (ApiDetailStatus is not null) ApiDetailStatus.Text = row.Status;
        if (ApiDetailInst is not null) ApiDetailInst.Text = row.Instances;
        if (ApiDetailBase is not null)
            ApiDetailBase.Text = string.IsNullOrWhiteSpace(row.BaseUrl) ? "—" : row.BaseUrl;
        if (ApiHeroBoard is not null)
            ApiHeroBoard.Text = $"{row.RefName}    {(row.RefMissing ? "—" : "已配")}";
        if (ApiDetailIcon is not null)
        {
            try
            {
                ApiDetailIcon.Source = string.IsNullOrWhiteSpace(row.Icon)
                    ? null
                    : new BitmapImage(new Uri(row.Icon, UriKind.Absolute));
            }
            catch
            {
                ApiDetailIcon.Source = null;
            }
        }
    }

    void ApiProbe_Click(object sender, RoutedEventArgs e)
    {
        if (_detailRow is not null) _ = ProbeDetailAsync(_detailRow);
    }

    async Task ProbeDetailAsync(CredentialsGate.ImportedApi row)
    {
        _probeCts?.Cancel();
        _probeCts = new CancellationTokenSource();
        var ct = _probeCts.Token;
        if (ApiLiveModels is not null)
            ApiLiveModels.Text = row.RefMissing ? "暂无 Key，导入后再检测。" : "正在检测…";
        if (row.RefMissing) return;
        try
        {
            var def = _state.Settings.Launch.CredentialsDefault ?? "global";
            var yaml = def == "none" ? null : _state.Launcher.GetCredentials(def);
            var secret = CredentialsFile.ReadRef(yaml, row.RefName);
            var (ok, detail, ids) = await ApiModels.ListAsync(row.BaseUrl, secret, ct);
            if (ct.IsCancellationRequested) return;
            if (ApiLiveModels is null) return;
            ApiLiveModels.Text = ok
                ? (ids.Count == 0 ? detail : string.Join("\n", ids))
                : detail;
        }
        catch (OperationCanceledException)
        {
            // switched row
        }
    }

    void ApiImport_Click(object sender, RoutedEventArgs e) => OpenImport(null);

    void ApiImportRow_Click(object sender, RoutedEventArgs e) =>
        OpenImport(_detailRef ?? (sender as FrameworkElement)?.Tag as string);

    void OpenImport(string? startRef)
    {
        var w = new ApiImportWindow { Owner = Window.GetWindow(this), StartRef = startRef };
        w.ShowDialog();
        PaintApiReady();
    }

    void ApiConfig_Click(object sender, RoutedEventArgs e)
    {
        var w = new ApiConfigWindow { Owner = Window.GetWindow(this) };
        w.ShowDialog();
        PaintApiReady();
    }

    void ExportPortable_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not MainWindow win) return;
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "便携目录会建在所选目录里（PAD-portable）" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var dest = Path.Combine(dlg.FolderName, "PAD-portable");
            var path = _state.Launcher.ExportPortable(dest, _state.SelectedInstance?.Id);
            win.Toast("便携目录导出了");
            Explore(path);
        }
        catch (Exception ex) { win.Toast(PadError.Describe(ex), bad: true); }
    }

    void BrowseDshHome_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "DSH_HOME" };
        if (dlg.ShowDialog() != true) return;
        DshHomeBox.Text = dlg.FolderName;
        Launch_Save(sender, e);
        Paint();
    }

    void PickSharedWs_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "新建实例共用的工作区" };
        if (dlg.ShowDialog() != true) return;
        SharedWorkspaceBox.Text = dlg.FolderName;
        Launch_Save(sender, e);
    }

    async void PinSniffed_Click(object sender, RoutedEventArgs e)
    {
        var bin = DshSniff.Bin() ?? PickBin();
        if (bin is null) return;
        await PinBin(bin);
    }

    async void PickBin_Click(object sender, RoutedEventArgs e)
    {
        var bin = PickBin();
        if (bin is null) return;
        await PinBin(bin);
    }

    void PickTui_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "dsh-tui 组合包" };
        if (dlg.ShowDialog() != true) return;
        var dir = dlg.FolderName;
        var nested = Path.Combine(dir, "@deepseek-harness-tui", "dsh-tui");
        if (Directory.Exists(nested)) dir = nested;
        var s = _state.Settings;
        s.Launch.TuiPackage = dir;
        Commit(s);
        Paint();
    }

    string? PickBin()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "dsh 命令",
            Filter = "DSH CLI|bin.js;dsh.cmd;dsh.exe|所有文件|*.*",
            FileName = "bin.js",
        };
        if (dlg.ShowDialog() != true) return null;
        var resolved = DshSniff.ResolveBin(dlg.FileName);
        if (resolved is null)
        {
            if (Window.GetWindow(this) is MainWindow win)
                win.Toast("这不是一份可运行的 dsh（需要 lib/bin.js）", bad: true);
            return null;
        }
        return resolved;
    }

    async Task PinBin(string bin)
    {
        try
        {
            await _state.Launcher.PinExisting(bin, null, CancellationToken.None);
            _state.Reload();
            Paint();
        }
        catch (Exception ex)
        {
            if (Window.GetWindow(this) is MainWindow w)
                w.Toast(PadError.Describe(ex), bad: true);
        }
    }

    void AdoptHome_Click(object sender, RoutedEventArgs e)
    {
        Launch_Save(sender, e);
        if (Window.GetWindow(this) is MainWindow win)
            win.AdoptFolder();
    }

    void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string tag }) return;
        var s = _state.Settings;
        s.Ui.Theme = tag;
        Commit(s);
    }

    void Ui_Save(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = _state.Settings;
        s.Ui.Animate = AnimateBox.IsChecked == true;
        s.Ui.ShowBrand = BrandBox.IsChecked == true;
        s.Ui.ShowLogo = LogoBox.IsChecked == true;
        s.Ui.BrandText = BrandTextBox.Text.Trim();
        if (int.TryParse(ComboTag(AnimSpeedBox), out var spd)) s.Ui.AnimSpeed = spd;
        if (int.TryParse(ComboTag(ToastBox), out var ms)) s.Ui.ToastMs = ms;
        if (int.TryParse(ComboTag(ToastMaxBox), out var max)) s.Ui.ToastMax = max;
        s.Ui.CustomAccent = CustomAccentBox.Text.Trim();
        s.Ui.WallpaperPath = WallpaperBox.Text.Trim();
        s.Ui.LogoPath = LogoPathBox.Text.Trim();
        ReadSliders(s.Ui);
        s.Ui.WallpaperFit = ComboTag(WallFitBox) ?? "cover";
        if (int.TryParse(WinWBox.Text.Trim(), out var w)) s.Ui.WindowWidth = w;
        if (int.TryParse(WinHBox.Text.Trim(), out var h)) s.Ui.WindowHeight = h;
        if (int.TryParse(WinMarginBox.Text.Trim(), out var m)) s.Ui.WindowMargin = m;
        if (int.TryParse(ChromeHBox.Text.Trim(), out var ch)) s.Ui.ChromeHeight = ch;
        s.Ui.WallpaperDir = WallpaperDirBox.Text.Trim();
        s.Ui.HideVersionEntry = HideVersionEntryBox.IsChecked == true;
        s.Ui.HiddenTabs = [];
        if (HiddenManageBox.IsChecked == true) s.Ui.HiddenTabs.Add("manage");
        if (HiddenDownloadBox.IsChecked == true) s.Ui.HiddenTabs.Add("download");
        Commit(s);
        Paint();
    }

    void WriteSliderLabels()
    {
        if (WallOpVal is null) return;
        WallOpVal.Text = (int)WallOpSlider.Value + "%";
        ContentOpVal.Text = (int)ContentOpSlider.Value + "%";
        ChromeOpVal.Text = (int)ChromeOpSlider.Value + "%";
        WindowOpVal.Text = (int)WindowOpSlider.Value + "%";
        WallpaperBlurVal.Text = WallpaperBlurSlider.Value <= 0 ? "关" : (int)WallpaperBlurSlider.Value + "px";
    }

    void ReadSliders(UiPrefs ui)
    {
        ui.WallpaperOpacity = (int)WallOpSlider.Value;
        ui.ContentOpacity = (int)ContentOpSlider.Value;
        ui.ChromeOpacity = (int)ChromeOpSlider.Value;
        ui.WindowOpacity = (int)WindowOpSlider.Value;
        ui.WallpaperBlur = (int)WallpaperBlurSlider.Value;
    }

    void Ui_Slide(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        WriteSliderLabels();
        var s = _state.Settings;
        ReadSliders(s.Ui);
        if (Window.GetWindow(this) is MainWindow win)
        {
            win.ApplySkin();
            win.Opacity = s.Ui.WindowOpacity / 100.0;
        }
    }

    void RefreshWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not MainWindow win) return;
        win.ApplySkin();
        win.Toast("已刷新壁纸");
    }

    void PickWallpaper_Click(object sender, RoutedEventArgs e)
    {
        var path = PickImage("选壁纸");
        if (path is null) return;
        WallpaperBox.Text = RelOrAbs(path);
        Ui_Save(sender, e);
    }

    void PickLogo_Click(object sender, RoutedEventArgs e)
    {
        var path = PickImage("选 logo");
        if (path is null) return;
        LogoPathBox.Text = RelOrAbs(path);
        Ui_Save(sender, e);
    }

    void ClearWallpaper_Click(object sender, RoutedEventArgs e)
    {
        WallpaperBox.Text = "";
        Ui_Save(sender, e);
    }

    void OpenWallpaperDir_Click(object sender, RoutedEventArgs e)
    {
        var dir = Path.Combine(_state.Launcher.Root, "library", "wallpaper");
        Directory.CreateDirectory(dir);
        Explore(dir);
    }

    void PickWallpaperDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "壁纸目录" };
        if (dlg.ShowDialog() != true) return;
        WallpaperDirBox.Text = RelOrAbs(dlg.FolderName);
        Ui_Save(sender, e);
    }

    void CopyIdentify_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(_state.Settings.Other.Identify);
            if (Window.GetWindow(this) is MainWindow win) win.Toast("识别码已复制");
        }
        catch { /* clipboard unavailable */ }
    }

    string? PickImage(string title)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp|所有文件|*.*",
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    string RelOrAbs(string abs)
    {
        var root = _state.Launcher.Root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        if (abs.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return abs[root.Length..].Replace('\\', '/');
        return abs;
    }

    string RelToRoot(string? abs) => string.IsNullOrWhiteSpace(abs) ? "" : RelOrAbs(abs);

    void Ui_Sel(object sender, SelectionChangedEventArgs e) => Ui_Save(sender, e);

    void Download_Save(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = _state.Settings;
        s.Download.NpmRegistry = RegistryBox.Text.Trim();
        if (int.TryParse(ComboTag(PageSizeBox), out var n)) s.Download.MarketPageSize = n;
        if (int.TryParse(MarketTimeoutBox.Text.Trim(), out var mt)) s.Download.MarketTimeoutSec = mt;
        if (int.TryParse(InstallTimeoutBox.Text.Trim(), out var it)) s.Download.InstallTimeoutSec = it;
        if (int.TryParse(GatewayTimeoutBox.Text.Trim(), out var gt)) s.Download.GatewayTimeoutSec = gt;
        s.Download.Shelves = ParseShelves(ShelvesBox.Text);
        s.Download.RegistryPresets = ParsePresets(PresetsBox.Text);
        if (int.TryParse(PnpmConcurrencyBox.Text.Trim(), out var pnc)) s.Download.PnpmNetworkConcurrency = pnc;
        s.Download.CacheDir = CacheDirBox.Text.Trim();
        s.Download.AutoFetchOnOpen = AutoFetchBox.IsChecked == true;
        Commit(s);
        Paint();
    }

    void Download_Sel(object sender, SelectionChangedEventArgs e) => Download_Save(sender, e);

    static List<ShelfPref> ParseShelves(string text)
    {
        var list = new List<ShelfPref>();
        foreach (var raw in (text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = raw.Split('|');
            if (parts.Length < 2) continue;
            var id = parts[0].Trim();
            var label = parts.Length >= 3 ? parts[1].Trim() : id;
            var query = parts.Length >= 3 ? parts[2].Trim() : parts[1].Trim();
            if (id.Length == 0 || query.Length == 0) continue;
            list.Add(new ShelfPref { Id = id, Label = label, Query = query });
        }
        return list;
    }

    static List<RegistryPreset> ParsePresets(string text)
    {
        var list = new List<RegistryPreset>();
        foreach (var raw in (text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = raw.Split('|', 2);
            if (parts.Length < 2) continue;
            var label = parts[0].Trim();
            var url = parts[1].Trim();
            if (url.Length == 0) continue;
            list.Add(new RegistryPreset
            {
                Label = label.Length == 0 ? url : label,
                Url = url,
            });
        }
        return list;
    }

    void RegistryPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string url }) return;
        RegistryBox.Text = url;
        Download_Save(sender, e);
    }

    void Other_Save(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = _state.Settings;
        s.Other.ConfirmStop = ConfirmStop.IsChecked == true;
        s.Other.OpenLogOnError = OpenLogOnError.IsChecked == true;
        s.Other.BackupCorrupt = BackupCorrupt.IsChecked == true;
        if (int.TryParse(RefreshSecBox.Text.Trim(), out var rs)) s.Other.RefreshSec = rs;
        s.Other.RegistriesRel = RegistriesRelBox.Text.Trim();
        if (int.TryParse(ShotDelayBox.Text.Trim(), out var sd)) s.Other.ShotDelayMs = sd;
        if (int.TryParse(ShotMarketDelayBox.Text.Trim(), out var sm)) s.Other.ShotMarketDelayMs = sm;
        s.Other.Debug = DebugBox.IsChecked == true;
        s.Other.UpdateChannel = ComboTag(UpdateChannelBox) ?? "release";
        Commit(s);
        Paint();
    }

    void Other_Sel(object sender, SelectionChangedEventArgs e) => Other_Save(sender, e);

    void OpenRoot_Click(object sender, RoutedEventArgs e) =>
        Explore(_state.Launcher.Root);

    // ---- multiple launcher roots ----------------------------------------

    void Roots_Sel(object sender, SelectionChangedEventArgs e) { }

    void SwitchRoot_Click(object sender, RoutedEventArgs e)
    {
        if (RootsBox.SelectedItem is not string root) return;
        if (Window.GetWindow(this) is not MainWindow win) return;
        try
        {
            _state.SwitchRoot(root);
            win.ApplyBrand();
            win.ApplyWindowPrefs();
            Paint();
            win.Toast("已切到 " + root);
        }
        catch (Exception ex) { win.Toast(PadError.Describe(ex), bad: true); }
    }

    void AddRoot_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "添加已有的启动器根（.pack-launcher）" };
        if (dlg.ShowDialog() != true) return;
        _state.AddRoot(dlg.FolderName);
        Paint();
        if (Window.GetWindow(this) is MainWindow win) win.Toast("已加进根列表");
    }

    void NewRoot_Click(object sender, RoutedEventArgs e)
    {
        var path = PromptPath("新建启动器根", "新根的绝对路径（会建 versions/ 和 instances/）：");
        if (path is null or { Length: 0 }) return;
        if (Window.GetWindow(this) is not MainWindow win) return;
        try
        {
            _state.NewRoot(path);
            win.ApplyBrand();
            win.ApplyWindowPrefs();
            Paint();
            win.Toast("已新建并切到 " + path);
        }
        catch (Exception ex) { win.Toast(PadError.Describe(ex), bad: true); }
    }

    static string? PromptPath(string title, string label)
    {
        var box = new TextBox { Width = 380 };
        var dlg = new Window
        {
            Title = title,
            Width = 460, Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(20), Children =
            {
                new TextBlock { Text = label, Margin = new Thickness(0,0,0,8), TextWrapping = TextWrapping.Wrap },
                box,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0,12,0,0), Children =
                    {
                        new Button { Content = "取消", Width = 70, Margin = new Thickness(0,0,8,0), IsCancel = true },
                        new Button { Content = "确定", Width = 70, IsDefault = true,
                            Command = System.Windows.Input.ApplicationCommands.Open },
                    } },
            } },
        };
        dlg.CommandBindings.Add(new System.Windows.Input.CommandBinding(
            System.Windows.Input.ApplicationCommands.Open,
            (_, _) => { dlg.DialogResult = true; dlg.Close(); }));
        return dlg.ShowDialog() == true ? box.Text.Trim() : null;
    }

    void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var path = _state.Launcher.SettingsPath;
        if (!File.Exists(path)) _state.ApplySettings(_state.Settings);
        Explore(path);
    }

    void OpenRegistries_Click(object sender, RoutedEventArgs e)
    {
        var rel = _state.Settings.Other.RegistriesRel.Replace('/', Path.DirectorySeparatorChar);
        var dir = Path.Combine(_state.Launcher.Root, rel);
        Directory.CreateDirectory(dir);
        Explore(dir);
    }

    static void Explore(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe",
            File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"")
        { UseShellExecute = true });

    async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_state.Settings.Other.UpdateChannel == "off")
        {
            PadUpdateLine.Text = "更新检查已关（其他 → 更新通道）";
            DshUpdateLine.Text = "";
            return;
        }
        PadUpdateLine.Text = "在查…";
        DshUpdateLine.Text = "在查…";
        try
        {
            var snap = await Updates.Check(_state.Launcher, _state.Settings.Download.NpmRegistry,
                CancellationToken.None);
            PadUpdateLine.Text = snap.PackAgentKnown
                ? $"PAD {snap.PadVersion} · pack-agent npm {snap.PackAgentLatest}"
                : $"PAD {snap.PadVersion} · npm 没查到";
            DshUpdateLine.Text = snap.DshLatest.Length == 0
                ? (snap.DshInstalled.Length == 0 ? "版本库是空的，npm 没查到" : $"已装 {snap.DshInstalled} · npm 没查到")
                : snap.DshNewer
                    ? $"已装 {(snap.DshInstalled.Length == 0 ? "无" : snap.DshInstalled)} · npm 最新 {snap.DshLatest}"
                    : $"已是 npm 最新 {snap.DshLatest}";
        }
        catch (Exception ex)
        {
            PadUpdateLine.Text = PadError.Describe(ex);
            DshUpdateLine.Text = "";
        }
    }

    void GotoDownload_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow win)
            win.TabDownload.IsChecked = true;
    }

    async void Doctor_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not MainWindow win) return;
        win.Toast("正在跑 doctor…");
        try
        {
            var report = await _state.Launcher.RunDoctor(null, CancellationToken.None);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"root: {report.Root}");
            sb.AppendLine($"node: {report.Node}");
            sb.AppendLine($"pnpm: {report.Pnpm ?? "未找到"}");
            sb.AppendLine($"writable: {report.Writable}");
            if (report.Errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("errors:");
                foreach (var err in report.Errors)
                    sb.AppendLine($"  error[{err.Code}] {err.Message} ({err.Location})");
            }
            if (report.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("warnings:");
                foreach (var w in report.Warnings)
                    sb.AppendLine($"  warning[{w.Code}] {w.Message}");
            }
            if (report.Ok) sb.AppendLine("\nOK");
            MessageBox.Show(sb.ToString(), "诊断",
                MessageBoxButton.OK,
                report.Ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex) { win.Toast(PadError.Describe(ex), bad: true); }
    }

    void ExportSettings_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not MainWindow win) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出设置",
            Filter = "JSON|*.json",
            FileName = "pad-settings.json",
        };
        if (dlg.ShowDialog(win) != true) return;
        try
        {
            System.IO.File.Copy(_state.Launcher.SettingsPath, dlg.FileName, overwrite: true);
            win.Toast("设置导出了");
        }
        catch (Exception ex) { win.Toast(PadError.Describe(ex), bad: true); }
    }

    void ImportSettings_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not MainWindow win) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入设置",
            Filter = "JSON|*.json",
        };
        if (dlg.ShowDialog(win) != true) return;
        try
        {
            var backup = _state.Launcher.SettingsPath + ".backup-" +
                DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
            System.IO.File.Copy(_state.Launcher.SettingsPath, backup, overwrite: true);
            System.IO.File.Copy(dlg.FileName, _state.Launcher.SettingsPath, overwrite: true);
            win.Toast("导入完成，旧设置备份在 " + System.IO.Path.GetFileName(backup));
            _state.ApplySettings(_state.Launcher.Settings());
            Paint();
        }
        catch (Exception ex) { win.Toast(PadError.Describe(ex), bad: true); }
    }
}
