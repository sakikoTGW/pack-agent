using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using Pad.Core;

namespace Pad.Views;

public partial class VersionsView : UserControl, IRefreshable
{
    readonly MainWindow _win;
    readonly AppState _state = AppState.Current;
    public bool ShowHidden { get; private set; }

    public VersionsView(MainWindow win)
    {
        InitializeComponent();
        _win = win;
        DataContext = _state;
        ((CollectionViewSource)Resources["InstSrc"]).Source = _state.Instances;
        ((CollectionViewSource)Resources["LaunchSrc"]).Source = _state.Launchables;
    }

    public void ToggleHidden()
    {
        ShowHidden = !ShowHidden;
        if (Resources["InstSrc"] is CollectionViewSource src)
            src.View?.Refresh();
    }

    void InstSrc_Filter(object sender, FilterEventArgs e)
    {
        e.Accepted = e.Item is Instance inst &&
            (ShowHidden || inst.Display?.Category is not ("隐藏" or "hidden"));
    }

    public void OnShown()
    {
        _state.Reload();
        if (Resources["InstSrc"] is CollectionViewSource src)
            src.View?.Refresh();
        SyncPinCombo();
        _ = LoadMatchPacks();
    }

    void InstanceList_Sel(object sender, SelectionChangedEventArgs e)
    {
        if (InstanceList.SelectedItem is Instance inst)
            _state.SelectedInstance = inst;
        if (PinRelease is null) return;
        SyncPinCombo();
        _ = LoadMatchPacks();
    }

    void SyncPinCombo()
    {
        var v = _state.SelectedInstance?.Dsh.Version;
        PinRelease.SelectedItem = _state.Releases.FirstOrDefault(r => r.Version == v);
    }

    async Task LoadMatchPacks()
    {
        await _state.ReloadPacks();
        if (MatchPackEmpty is null) return;
        if (_state.SelectedInstance is null)
        {
            MatchPackEmpty.Visibility = Visibility.Collapsed;
            return;
        }
        var missing = new Packs(_state.Launcher).Unavailable();
        if (missing is not null)
        {
            MatchPackEmpty.Text = missing.Reason;
            MatchPackEmpty.Visibility = Visibility.Visible;
            return;
        }
        MatchPackEmpty.Text = _state.PackRows.Count == 0 ? "没有整合包" : "";
        MatchPackEmpty.Visibility = _state.PackRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void PinApply_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        if (PinRelease.SelectedItem is not DshRelease rel) return;
        if (rel.Version == inst.Dsh.Version) return;
        try
        {
            _state.Launcher.PinInstance(inst.Id, rel.Version);
            _state.Reload();
            SyncPinCombo();
            _win.Toast($"已改钉到 {rel.Version}（组合包未验证）");
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    async void ToggleMatchPack_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null || sender is not Button { Tag: string packId } btn) return;
        if (btn.DataContext is not PackRow row) return;
        try
        {
            var packs = new Packs(_state.Launcher);
            if (row.Enabled) await packs.Deny(inst, packId, null, CancellationToken.None);
            else await packs.Allow(inst, packId, null, CancellationToken.None);
            await LoadMatchPacks();
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    void Log(string line) => Dispatcher.BeginInvoke(() =>
    {
        ProfileLog.Visibility = Visibility.Visible;
        ProfileLog.Height = 160;
        ProfileLog.AppendText(line + Environment.NewLine);
        ProfileLog.ScrollToEnd();
    });

    async void Create_Click(object sender, RoutedEventArgs e)
    {
        var name = Prompt("新建实例", "实例名：");
        if (name is null or { Length: 0 }) return;
        try
        {
            string version;
            var want = _state.Settings.Launch.DefaultRelease;
            if (_state.Releases.FirstOrDefault(r => r.Version == want) is { } pinned)
            {
                version = pinned.Version;
            }
            else if (_state.Releases.FirstOrDefault() is { } first)
            {
                version = first.Version;
            }
            else
            {
                _win.Toast("在装 DSH 发行号");
                version = await _state.Launcher.EnsureRelease(null, CancellationToken.None);
                _state.Reload();
            }
            var inst = _state.Launcher.CreateInstance(name, version);
            _state.Reload();
            _state.SelectedInstance = _state.Instances.FirstOrDefault(i => i.Id == inst.Id);
            NewProfile.Text = _state.Settings.Launch.DefaultProfile;
            _win.ShowInner("instance", "版本设置");
        }
        catch (Exception ex)
        {
            _win.Toast(PadError.Describe(ex), bad: true);
        }
    }

    void Adopt_Click(object sender, RoutedEventArgs e) => _win.AdoptFolder();

    void Download_Click(object sender, RoutedEventArgs e) => _win.ShowTab("download");

    async void ImportPack_Click(object sender, RoutedEventArgs e)
    {
        var packs = new Packs(_state.Launcher);
        if (packs.Unavailable() is { } missing)
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
        try
        {
            _win.Toast("正在装 " + Path.GetFileName(dlg.FileName));
            await packs.Import(dlg.FileName, new Progress<string>(Log), CancellationToken.None);
            _state.Reload();
            var newest = _state.Instances.OrderByDescending(i => i.Created).FirstOrDefault();
            if (newest is not null) _state.SelectedInstance = newest;
            _win.ShowInner("instance", "版本设置");
        }
        catch (Exception ex)
        {
            _win.Toast(PadError.Describe(ex), bad: true);
        }
    }

    void Pick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ProfileVm vm }) return;
        _state.SelectedInstance = vm.Instance;
        _state.SelectedProfile = _state.Launchables.FirstOrDefault(p => p.Name == vm.Name) ?? vm;
        _state.SaveSelection();
        _win.Show("launch");
        _win.TabLaunch.IsChecked = true;
    }

    void Setup_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: ProfileVm vm }) OpenSetup(vm);
    }

    void Profile_Setup(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ProfileVm vm }) return;
        e.Handled = true;
        OpenSetup(vm);
    }

    void OpenSetup(ProfileVm vm)
    {
        _state.SelectedInstance = vm.Instance;
        _state.SelectedProfile = _state.Launchables.FirstOrDefault(p => p.Name == vm.Name) ?? vm;
        _state.SaveSelection();
        _win.ShowInner("instance", "版本设置");
    }

    void Gear_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: Instance inst } fe) return;
        _state.SelectedInstance = inst;
        InstanceList.SelectedItem = inst;
        if (InstanceList.ContextMenu is { } menu)
        {
            menu.PlacementTarget = fe;
            menu.IsOpen = true;
        }
    }

    async void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var profile = NewProfile.Text.Trim();
        if (profile.Length == 0) { NewProfile.Focus(); return; }

        var kind = (ProfileKind.SelectedItem as ComboBoxItem)?.Tag as string ?? "tui";
        // One recipe shared with `pad cli profile add`, so the window and the command
        // line can never install different things.
        var specs = Cli.Recipe(kind, inst.Dsh.Version).ToList();

        AddProfileBtn.IsEnabled = false;
        AddSpinner.Visibility = Visibility.Visible;
        try
        {
            Log($"[PAD] profile {profile} ← {string.Join(", ", specs)}");
            var progress = new Progress<string>(Log);
            await _state.Launcher.AddBundles(inst, profile, specs, progress, CancellationToken.None);
            Log("[PAD] 装好了");
            _state.Reload();
            _state.SelectedProfile = _state.Launchables.FirstOrDefault(p => p.Name == profile);
            _win.Show("launch");
            _win.TabLaunch.IsChecked = true;
        }
        catch (Exception ex)
        {
            Log(PadError.Describe(ex));
            _win.Toast($"profile {profile} 没装成，看下面的输出", bad: true);
        }
        finally
        {
            AddProfileBtn.IsEnabled = true;
            AddSpinner.Visibility = Visibility.Collapsed;
        }
    }

    void OpenHome_Click(object sender, RoutedEventArgs e) => Reveal.Path(_state.SelectedInstance?.Home);

    void OpenWorkspace_Click(object sender, RoutedEventArgs e) =>
        Reveal.Path(_state.SelectedInstance?.Workspace.Path);

    void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        if (_state.SelectedInstance is null) return;
        Reveal.Path(_state.Launcher.LogsDir(_state.SelectedInstance.Id));
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
            var path = _state.Launcher.WriteShortcut(inst, profile);
            _win.Toast("快捷方式写到了 library/shortcuts");
            Reveal.Path(path);
        }
        catch (Exception ex)
        {
            _win.Toast(PadError.Describe(ex), bad: true);
        }
    }

    void InstanceList_Right(object sender, MouseButtonEventArgs e)
    {
        var src = e.OriginalSource as DependencyObject;
        var item = ItemsControl.ContainerFromElement(InstanceList, src) as ListBoxItem;
        if (item is not null) item.IsSelected = true;
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        if (!_win.Ask("版本删除确认",
            $"你确定要删除实例 {inst.Name} 吗？\n该实例对应的 DSH_HOME、工作区和 session 也会被一并删除。",
            warn: true)) return;
        _state.Launcher.RemoveInstance(inst.Id);
        _state.Reload();
    }

    void Clone_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var name = Prompt("克隆实例", $"克隆 {inst.Name} 为：");
        if (name is null or { Length: 0 }) return;
        try
        {
            var cloned = _state.Launcher.CloneInstance(inst.Id, name);
            _state.Reload();
            _state.SelectedInstance = _state.Instances.FirstOrDefault(i => i.Id == cloned.Id);
            _win.Toast($"已克隆为 {cloned.Name}");
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    void Rename_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var name = Prompt("改名", $"把 {inst.Name} 改为：", inst.Name);
        if (name is null or { Length: 0 } || name == inst.Name) return;
        try
        {
            _state.Launcher.RenameInstance(inst.Id, name);
            _state.Reload();
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    void Pin_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var releases = _state.Launcher.Releases();
        if (releases.Count == 0) { _win.Toast("版本库里还没有发行号", bad: true); return; }
        var current = inst.Dsh.Version;
        var options = releases.Select(r => r.Version).ToList();
        var pick = PickOption("改钉发行号", $"当前 {current}，改为：", options);
        if (pick is null or { Length: 0 } || pick == current) return;
        try
        {
            _state.Launcher.PinInstance(inst.Id, pick);
            _state.Reload();
            _win.Toast($"已改钉到 {pick}（组合包未验证）");
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    void Star_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        try
        {
            _state.Launcher.SetDisplay(inst.Id, star: !(inst.Display?.Star ?? false));
            _state.Reload();
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    void Category_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var cat = Prompt("设置分类", "分类名（留空取消）：", inst.Display?.Category ?? "");
        if (cat is null) return;
        try
        {
            _state.Launcher.SetDisplay(inst.Id, category: cat);
            _state.Reload();
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    void OpenProfiles_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        Reveal.Path(_state.Launcher.ProfilesDir(inst));
    }

    void OpenProjection_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        Reveal.Path(_state.Launcher.ProjectionDir(inst));
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
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
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    string? Prompt(string title, string label, string defaultValue = "") =>
        _win.AskInput(title, label, defaultValue);

    static string? PickOption(string title, string label, List<string> options)
    {
        var combo = new ComboBox { Width = 300, ItemsSource = options, SelectedIndex = 0 };
        var dlg = new Window
        {
            Title = title,
            Width = 400, Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(20), Children =
            {
                new TextBlock { Text = label, Margin = new Thickness(0,0,0,8) },
                combo,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0,12,0,0), Children =
                    {
                        new Button { Content = "取消", Width = 70, Margin = new Thickness(0,0,8,0), IsCancel = true },
                        new Button { Content = "确定", Width = 70, IsDefault = true,
                            Command = ApplicationCommands.Open },
                    } },
            } },
        };
        dlg.CommandBindings.Add(new CommandBinding(ApplicationCommands.Open,
            (_, _) => { dlg.DialogResult = true; dlg.Close(); }));
        return dlg.ShowDialog() == true ? combo.SelectedItem as string : null;
    }
}
