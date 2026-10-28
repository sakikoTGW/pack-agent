using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Pad.Core;
using IOPath = System.IO.Path;

namespace Pad.Views;

/// <summary>
/// 制作整合包（学 PCL 的导出页）：把「现有插件配置 + 容器 + 核心」挑一挑、组一组，导出成包。
/// 一页内自己选：核心单选、容器单选、插件多选（至少一个），底部一个动作。
/// 文字只留题目；版本/来源这类细节放小字或 tooltip，不做技术说明。
/// </summary>
public partial class MakePackView : UserControl
{
    readonly AppState _state = AppState.Current;
    readonly MainWindow _win;
    readonly HashSet<string> _checked = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _pluginSource = new(StringComparer.OrdinalIgnoreCase);
    readonly List<PackMaker.LocalPlugin> _localPlugins = [];

    List<ProfileVm> _containers = [];
    Inventory? _inventory;
    string _profile = "web";
    string? _iconPath;
    string _kernelVersion = "";

    public MakePackView(MainWindow win)
    {
        _win = win;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    // ------------------------------------------------------------------ 数据

    async Task LoadAsync()
    {
        var inst = _state.SelectedInstance;
        NameBox.Text = inst is null ? "我的整合包" : $"{inst.Name}-整合包";
        VersionBox.Text = "0.1.0";
        if (inst is not null) DescBox.Text = inst.Name;
        try { _inventory = await Task.Run(() => Discover.Run(_state.Launcher, [], CancellationToken.None)); }
        catch { _inventory = null; }

        BuildKernels(inst);
        BuildContainers(inst);
        BuildPlugins(inst);
        UpdateHint();
    }

    void BuildKernels(Instance? inst)
    {
        KernelList.Items.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pick = inst?.Dsh.Version ?? "";

        Border Row(string version, string source, string tag)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = version,
                FontFamily = (FontFamily)Application.Current.Resources["FontMono"],
                FontSize = (double)Application.Current.Resources["FsSmall"],
                Foreground = (Brush)Application.Current.Resources["TextPrimary"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            stack.Children.Add(new TextBlock
            {
                Text = source,
                FontSize = (double)Application.Current.Resources["FsMicro"],
                Foreground = (Brush)Application.Current.Resources["TextMuted"],
                Margin = new Thickness(9, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            var border = new Border { Child = stack, Tag = tag };
            return border;
        }

        foreach (var release in _state.Releases)
        {
            if (string.IsNullOrWhiteSpace(release.Version) || !seen.Add(release.Version)) continue;
            KernelList.Items.Add(Row(release.Version, "版本库", "release:" + release.Version));
        }
        if (_inventory is not null)
        {
            foreach (var kernel in _inventory.Kernels)
            {
                if (string.IsNullOrWhiteSpace(kernel.Version) || !seen.Add(kernel.Version)) continue;
                KernelList.Items.Add(Row(kernel.Version, KernelSource(kernel), "kernel:" + kernel.Version));
            }
        }
        foreach (var item in KernelList.Items.OfType<Border>())
        {
            if ((string?)item.Tag != "kernel:" + pick && (string?)item.Tag != "release:" + pick) continue;
            KernelList.SelectedItem = item;
            break;
        }
        if (KernelList.SelectedItem is null && KernelList.Items.Count > 0)
            KernelList.SelectedIndex = 0;
    }

    static string KernelSource(Kernel kernel)
    {
        var source = kernel.Source ?? "";
        if (source.StartsWith("desktop bundle:", StringComparison.OrdinalIgnoreCase))
        {
            var exe = source["desktop bundle:".Length..].Trim();
            var name = IOPath.GetFileNameWithoutExtension(exe);
            return name.Length > 0 ? $"{name} 自带" : "桌面端自带";
        }
        if (source.StartsWith("launcher script:", StringComparison.OrdinalIgnoreCase)) return "启动脚本";
        if (source.Equals("pad version store", StringComparison.OrdinalIgnoreCase)) return "版本库";
        if (source.Equals("npx cache", StringComparison.OrdinalIgnoreCase)) return "npx";
        if (source.Equals("npm global", StringComparison.OrdinalIgnoreCase)) return "npm";
        return "本机";
    }

    void BuildContainers(Instance? inst)
    {
        ContainerList.Items.Clear();
        _containers = [];
        if (_inventory is not null)
        {
            foreach (var shell in _inventory.Shells)
                _containers.Add(new DetectedShellRow(shell, canAdopt: false) { Running = shell.Running });
            foreach (var tui in _inventory.Tuis)
            {
                if (string.IsNullOrWhiteSpace(tui.Package)) continue;
                _containers.Add(new DetectedTuiRow(tui, canAdopt: false) { Running = tui.Running });
            }
        }

        // 「不指定」永远在第一行：包不绑定容器也合法。
        ContainerList.Items.Add(new Border
        {
            Child = new TextBlock
            {
                Text = "不指定",
                FontSize = (double)Application.Current.Resources["FsSmall"],
                Foreground = (Brush)Application.Current.Resources["TextMuted"],
                Margin = new Thickness(2, 2, 0, 2),
            },
        });

        var instanceHome = inst?.Home ?? "";
        Border? pick = null;
        foreach (var vm in _containers)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var kind = new TextBlock
            {
                Text = vm.Kind,
                FontSize = (double)Application.Current.Resources["FsMicro"],
                Foreground = (Brush)Application.Current.Resources["TextMuted"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(kind, 0);
            grid.Children.Add(kind);

            var mark = new InstMark
            {
                Width = 26,
                Height = 26,
                VerticalAlignment = VerticalAlignment.Center,
                DataContext = vm,
            };
            Grid.SetColumn(mark, 1);
            grid.Children.Add(mark);

            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            line.Children.Add(new TextBlock
            {
                Text = vm.Name,
                FontSize = (double)Application.Current.Resources["FsBody"],
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextPrimary"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            line.Children.Add(new RunLamp
            {
                Width = 12,
                Height = 12,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Running = vm.Running,
            });
            Grid.SetColumn(line, 2);
            grid.Children.Add(line);

            var border = new Border { Child = grid, Tag = vm, ToolTip = vm.DetailText };
            ContainerList.Items.Add(border);
            if (pick is null && instanceHome.Length > 0 &&
                string.Equals(vm.DetectedHome ?? "", instanceHome, StringComparison.OrdinalIgnoreCase))
                pick = border;
        }
        ContainerList.SelectedItem = (object?)pick ?? ContainerList.Items[0];
    }

    void BuildPlugins(Instance? inst)
    {
        _pluginSource.Clear();
        void Add(string spec, string source)
        {
            spec = spec.Trim();
            if (spec.Length == 0) return;
            if (!_pluginSource.ContainsKey(spec)) _pluginSource[spec] = source;
        }

        // 现有实例的插件配置（每个 --profile 的 bundles）
        foreach (var one in _state.Instances)
        {
            List<DshProfile> profiles;
            try { profiles = _state.Launcher.Profiles(one); }
            catch { continue; }
            foreach (var profile in profiles)
                foreach (var bundle in profile.Bundles)
                    Add(bundle, profile.Name == "web" || profile.Name == "headless" ? one.Name : $"{one.Name} · {profile.Name}");
        }
        // 探测到的容器的插件配置（该 home 的 profiles）
        foreach (var vm in _containers)
        {
            var home = vm.DetectedHome;
            if (string.IsNullOrWhiteSpace(home)) continue;
            foreach (var bundle in Carriers.Bundles(home!)) Add(bundle, vm.Name);
        }

        // 预填当前实例：选了哪个实例先按它的 profile 勾上（"快速打包已有配置"）
        if (inst is not null)
        {
            var wanted = inst.LastProfile;
            try
            {
                var profile = _state.Launcher.Profiles(inst)
                    .FirstOrDefault(p => string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase))
                    ?? _state.Launcher.Profiles(inst).FirstOrDefault();
                if (profile is not null)
                    foreach (var bundle in profile.Bundles) _checked.Add(bundle);
            }
            catch { /* 读不到就留空给人选 */ }
        }
        RebuildPluginRows();
    }

    void RebuildPluginRows()
    {
        PluginList.Items.Clear();
        foreach (var (spec, source) in _pluginSource.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var box = new CheckBox
            {
                IsChecked = _checked.Contains(spec),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = spec,
            };
            box.Click += (_, _) =>
            {
                if (box.IsChecked == true) _checked.Add(spec);
                else _checked.Remove(spec);
                UpdateHint();
            };
            Grid.SetColumn(box, 0);
            grid.Children.Add(box);

            var name = new TextBlock
            {
                Text = spec,
                FontFamily = (FontFamily)Application.Current.Resources["FontMono"],
                FontSize = (double)Application.Current.Resources["FsSmall"],
                Foreground = (Brush)Application.Current.Resources["TextPrimary"],
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 10, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);

            var from = new TextBlock
            {
                Text = source,
                FontSize = (double)Application.Current.Resources["FsMicro"],
                Foreground = (Brush)Application.Current.Resources["TextMuted"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(from, 2);
            grid.Children.Add(from);

            PluginList.Items.Add(new Border { Child = grid, Margin = new Thickness(0, 0, 0, 2) });
        }
    }

    // ------------------------------------------------------------------ 交互

    void Kernel_Sel(object sender, SelectionChangedEventArgs e)
    {
        if (KernelList.SelectedItem is not Border { Tag: string tag }) return;
        _kernelVersion = tag[(tag.IndexOf(':') + 1)..];
        UpdateHint();
    }

    void Container_Sel(object sender, SelectionChangedEventArgs e)
    {
        if (ContainerList.SelectedItem is not Border { Tag: ProfileVm vm })
        {
            _profile = "web";
            UpdateHint();
            return;
        }
        _profile = vm.Kind == "TUI"
            ? (vm.Profile.Name.Replace("（本机）", "") is { Length: > 0 } p ? p : "dsh-tui")
            : "web";
        // 换了容器就把它的插件配置顺手勾上（已有的保留）
        var home = vm.DetectedHome;
        if (!string.IsNullOrWhiteSpace(home))
            foreach (var bundle in Carriers.Bundles(home!)) _checked.Add(bundle);
        RebuildPluginRows();
        UpdateHint();
    }

    void AddSpec_Click(object sender, RoutedEventArgs e)
    {
        var spec = AddSpecBox.Text.Trim();
        if (spec.Length == 0) return;
        if (!_pluginSource.ContainsKey(spec)) _pluginSource[spec] = "手动";
        _checked.Add(spec);
        AddSpecBox.Text = "";
        RebuildPluginRows();
        UpdateHint();
    }

    /// <summary>没上架 npm 的插件：选目录，整个塞进包（清单里带 local 指路）。</summary>
    void AddLocal_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "选插件文件夹（没上架 npm 的）" };
        if (dlg.ShowDialog(_win) != true) return;
        var dir = dlg.FolderName;
        var spec = LocalSpec(dir);
        if (string.IsNullOrWhiteSpace(spec)) return;
        _localPlugins.RemoveAll(p => string.Equals(p.Dir, dir, StringComparison.OrdinalIgnoreCase));
        _localPlugins.Add(new PackMaker.LocalPlugin(spec, dir));
        _pluginSource[spec] = "本地";
        _checked.Add(spec);
        RebuildPluginRows();
        UpdateHint();
    }

    /// <summary>目录里 package.json 有 name/version 就当作 spec，否则用目录名。</summary>
    static string LocalSpec(string dir)
    {
        try
        {
            var pkg = Path.Combine(dir, "package.json");
            if (File.Exists(pkg))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(pkg));
                if (doc.RootElement.TryGetProperty("name", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var name = n.GetString() ?? "";
                    var ver = doc.RootElement.TryGetProperty("version", out var v) &&
                              v.ValueKind == System.Text.Json.JsonValueKind.String
                        ? v.GetString()
                        : null;
                    if (name.Length > 0) return string.IsNullOrWhiteSpace(ver) ? name : $"{name}@{ver}";
                }
            }
            return Path.GetFileName(dir.TrimEnd('\\', '/'));
        }
        catch
        {
            return Path.GetFileName(dir.TrimEnd('\\', '/'));
        }
    }

    void PickIcon_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选整合包图标",
            Filter = "图片 (*.png;*.svg;*.ico;*.jpg)|*.png;*.svg;*.ico;*.jpg|所有文件|*.*",
        };
        if (dlg.ShowDialog(_win) != true) return;
        _iconPath = dlg.FileName;
        var bmp = Skin.LoadBitmap(_iconPath);
        IconPreview.Source = bmp;
        IconPreview.Visibility = bmp is null ? Visibility.Collapsed : Visibility.Visible;
        IconGlyph.Visibility = bmp is null ? Visibility.Visible : Visibility.Collapsed;
        if (bmp is not null) Fx.Reveal(IconPreview);
    }

    void UpdateHint()
    {
        // XAML 里 FormatList.IsChecked="True" 会在 InitializeComponent 期间触发 Checked，
        // 此时 Hint 还没构造出来；不挡住就是 NullReference（用户 2026-10-28 打开导出页直接崩）。
        if (Hint is null) return;
        if (string.IsNullOrWhiteSpace(_kernelVersion)) Hint.Text = "选一个核心";
        else if (_checked.Count == 0) Hint.Text = "至少要一个插件";
        else if ((FormatPortable?.IsChecked == true || FormatPortableFolder?.IsChecked == true)
                 && _state.SelectedInstance is null) Hint.Text = "先选一个实例";
        else Hint.Text = "";
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => _win.BackToVersions();

    async void Make_Click(object sender, RoutedEventArgs e)
    {
        var portable = FormatPortable.IsChecked == true;
        var folder = FormatPortableFolder.IsChecked == true;
        var inst = _state.SelectedInstance;
        if ((portable || folder) && inst is null)
        {
            Hint.Text = "先选一个实例";
            return;
        }
        var req = new PackMaker.Request
        {
            Name = NameBox.Text.Trim(),
            Version = VersionBox.Text.Trim(),
            Description = DescBox.Text.Trim(),
            Kernel = _kernelVersion,
            Profile = _profile,
            Plugins = _checked.Where(s => _localPlugins.All(l => !string.Equals(l.Spec, s, StringComparison.OrdinalIgnoreCase))).ToList(),
            LocalPlugins = _localPlugins.ToList(),
            IconPath = _iconPath,
        };
        var stem = SafeFile(req.Name.Length == 0 ? "整合包" : req.Name)
                   + (req.Version.Length == 0 ? "" : "-" + SafeFile(req.Version));

        MakeBtn.IsEnabled = false;
        var tmp = Path.Combine(Path.GetTempPath(), "pad-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tmp);

            // 整合包（文件夹）：不压缩，直接给目录；复制时照样报进度/速度/剩余。
            if (folder)
            {
                var pick = new OpenFolderDialog { Title = "选一个文件夹放整合包" };
                if (pick.ShowDialog(_win) != true) return;
                var dest = Path.Combine(pick.FolderName,
                    PortableExport.DefaultFolderName(req.Name, req.Version));
                _win.Progress("正在估算整合包大小…（版本库文件多，约几秒）");
                var estimate = await Task.Run(() =>
                    PortableExport.Estimate(_state.Launcher, req.Kernel, inst!.Id));
                _win.Progress($"即将打整合包（文件夹，约 {estimate / (1024 * 1024)} MB）…");
                var folderJob = await PortableExport.ExportAsync(_win, _state.Launcher, dest, inst!.Id,
                    "打整合包（文件夹）", estimate, zipPhase: false);
                _state.Launcher.FinishJob(folderJob.Id, "done");
                _win.Toast($"已导出文件夹：{dest}");
                Reveal.Path(dest);
                _win.BackToVersions();
                return;
            }

            // 整合包：PAD + 版本库 + 实例，压成可分享的单文件。
            if (portable)
            {
                var ext = PortableExport.SingleFileExtension;
                var portableDlg = new SaveFileDialog
                {
                    Title = "导出整合包",
                    Filter = $"整合包 (*{ext})|*{ext}",
                    FileName = stem + "-整合" + ext,
                    InitialDirectory = ExportHome(),
                };
                if (portableDlg.ShowDialog(_win) != true) return;
                var zipTarget = portableDlg.FileName;
                if (!zipTarget.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) zipTarget += ext;
                _win.Progress("正在估算整合包大小…");
                var estimate = await Task.Run(() =>
                    PortableExport.Estimate(_state.Launcher, req.Kernel, inst!.Id));
                _win.Progress($"即将打整合包（约 {estimate / (1024 * 1024)} MB）…");
                // 按实例固定的暂存目录：取消/中断后保留，下次续传。
                var stage = PortableExport.StageDir(inst!.Id);
                var kitDir = Path.Combine(stage, "kit");
                var job = await PortableExport.ExportAsync(_win, _state.Launcher, kitDir, inst!.Id,
                    "打整合包", estimate);
                try
                {
                    await PortableExport.PackAsync(_win, _state.Launcher, kitDir, zipTarget, estimate, job);
                    _state.Launcher.FinishJob(job.Id, "done");
                    await Task.Run(() => PortableExport.ClearStage(inst!.Id));
                }
                catch
                {
                    if (_state.Launcher.ListJobs().FirstOrDefault(j => j.Id == job.Id)?.Status == "running")
                        _state.Launcher.FinishJob(job.Id, "failed");
                    throw;
                }
                _win.Toast($"已导出：{Path.GetFileName(zipTarget)}");
                _win.BackToVersions();
                return;
            }

            // 便携包：只带清单的 .pack.zip（首次启动联网装依赖）
            var dlg = new SaveFileDialog
            {
                Title = "导出便携包",
                Filter = "便携包 (*.pack.zip)|*.pack.zip",
                FileName = stem + "-便携.pack.zip",
                InitialDirectory = ExportHome(),
            };
            if (dlg.ShowDialog(_win) != true) return;
            var target = dlg.FileName;
            if (!target.EndsWith(".pack.zip", StringComparison.OrdinalIgnoreCase)) target += ".pack.zip";
            var result = await Task.Run(() => PackMaker.WriteTo(tmp, req));
            File.Copy(result.ZipPath, target, overwrite: true);
            _win.Toast($"已导出：{Path.GetFileName(target)}");
            _win.BackToVersions();
        }
        catch (OperationCanceledException)
        {
            Hint.Text = "已取消；已复制/压缩的部分保留，下次会继续";
        }
        catch (PadError pe)
        {
            Hint.Text = pe.Message;
        }
        catch (Exception ex)
        {
            Hint.Text = PadError.Describe(ex);
        }
        finally
        {
            _win.ProgressDone(400);
            MakeBtn.IsEnabled = true;
            try { Directory.Delete(tmp, true); } catch { /* temp dir */ }
        }
    }

    void Format_Changed(object sender, RoutedEventArgs e) => UpdateHint();

    static string ExportHome()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloads = Path.Combine(home, "Downloads");
        return Directory.Exists(downloads) ? downloads : home;
    }

    static string SafeFile(string text) =>
        string.Concat(text.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
