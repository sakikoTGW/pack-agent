using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Pad.Core;

namespace Pad.Views;

/// <summary>
/// 导出 v3 整合包：填 manifest 信息、挑要带的文件与包体，交给 <see cref="PackExport"/> 写盘。
/// </summary>
public partial class PackExportWindow : Window
{
    readonly Launcher _launcher;
    readonly Instance _inst;
    readonly ObservableCollection<BundleRow> _bundles = [];
    string? _workspaceTemplate;

    /// <summary>导出成功的文件路径；null = 取消或失败。</summary>
    public string? OutputPath { get; private set; }

    public PackExportWindow(Launcher launcher, Instance inst)
    {
        InitializeComponent();
        _launcher = launcher;
        _inst = inst;

        HeaderSub.Text = $"{inst.Name} · 内核 {inst.Dsh.Version}";
        NameBox.Text = inst.Name;
        VersionBox.Text = "1.0.0";
        BundleList.ItemsSource = _bundles;

        var data = Path.Combine(inst.Workspace.Path, ".agent-pack");
        if (Directory.Exists(data))
        {
            var count = Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Count();
            DataHint.Text = $"{count} 个文件";
        }
        else
        {
            DataBox.IsChecked = false;
            DataBox.IsEnabled = false;
            DataHint.Text = "没有 .agent-pack";
        }

        foreach (var profile in launcher.Profiles(inst))
        {
            foreach (var spec in profile.Bundles.Where(b =>
                         !b.StartsWith("@deepseek-ai/", StringComparison.OrdinalIgnoreCase)))
            {
                if (_bundles.Any(b => string.Equals(b.Name, spec, StringComparison.OrdinalIgnoreCase))) continue;
                var (sizeText, sizeBytes) = SizeOf(profile.Dir, spec);
                var row = new BundleRow { Name = spec, Size = sizeText, Bytes = sizeBytes };
                row.PropertyChanged += (_, _) => UpdateTotal();
                _bundles.Add(row);
            }
        }

        if (_bundles.Count == 0)
        {
            BundledBox.IsEnabled = false;
            BundledBox.Content = "随包带插件包体（没有第三方插件）";
        }

        var outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "pack-agent");
        OutBox.Text = Path.Combine(outDir, Sanitize(inst.Name) + "-1.0.0.pack.zip");
        FooterHint.Text = "只读实例，不改任何东西。";
    }

    void Workspace_Changed(object sender, RoutedEventArgs e)
    {
        if (WorkspacePath is null) return;
        WorkspacePath.Text = _workspaceTemplate ?? "未选";
    }

    void Bundled_Changed(object sender, RoutedEventArgs e)
    {
        if (BundledPanel is null) return;
        BundledPanel.Visibility = BundledBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UpdateTotal();
    }

    void BrowseWorkspace_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "选一个工作区模板目录" };
        if (dlg.ShowDialog(this) != true) return;
        _workspaceTemplate = dlg.FolderName;
        WorkspaceBox.IsChecked = true;
        WorkspacePath.Text = _workspaceTemplate;
    }

    void BrowseOut_Click(object sender, RoutedEventArgs e)
    {
        var name = Sanitize(string.IsNullOrWhiteSpace(NameBox.Text) ? "pack" : NameBox.Text.Trim());
        var dlg = new SaveFileDialog
        {
            Title = "导出的整合包",
            FileName = $"{name}-{VersionBox.Text.Trim()}.pack.zip",
            Filter = "整合包 (*.pack.zip)|*.pack.zip|所有文件|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        OutBox.Text = dlg.FileName;
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        var name = NameBox.Text.Trim();
        var version = VersionBox.Text.Trim();
        var outPath = OutBox.Text.Trim();

        if (name.Length == 0) { ShowError("名称不能为空。"); return; }
        if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"^[0-9]+\.[0-9]+\.[0-9]+([-+][0-9A-Za-z.-]+)?$"))
        { ShowError("版本要写成 1.2.3 这种。"); return; }
        if (outPath.Length == 0) { ShowError("选个导出路径。"); return; }

        try
        {
            ExportBtn.IsEnabled = false;
            ExportBtn.Content = "导出中…";
            var opts = new PackExportOptions(
                name,
                version,
                Description: DescBox.Text.Trim(),
                Author: AuthorBox.Text.Trim(),
                KernelMode: InheritRadio.IsChecked == true ? "inherit" : "pin",
                IncludeData: DataBox.IsChecked == true,
                WorkspaceTemplate: WorkspaceBox.IsChecked == true ? _workspaceTemplate : null,
                BundledSpecs: _bundles.Where(b => b.Selected).Select(b => b.Name).ToList());

            var result = PackExport.Write(_launcher, _inst, opts, outPath);
            OutputPath = result.Path;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(PadError.Describe(ex));
            ExportBtn.IsEnabled = true;
            ExportBtn.Content = "导出";
        }
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    void UpdateTotal()
    {
        if (BundledTotal is null) return;
        var bytes = _bundles.Where(b => b.Selected).Sum(b => b.Bytes);
        BundledTotal.Text = bytes > 0 ? $"合计 +{Size(bytes)}" : "";
    }

    static (string Text, long Bytes) SizeOf(string profileDir, string spec)
    {
        try
        {
            var dir = LaunchPolicy.BundlePackageDir(profileDir, spec);
            if (!Directory.Exists(dir)) return ("", 0);
            var bytes = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Sum(file => { try { return new FileInfo(file).Length; } catch { return 0L; } });
            return (Size(bytes), bytes);
        }
        catch { return ("", 0); }
    }

    static string Size(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / 1024d / 1024 / 1024:0.0} GB",
        >= 1024L * 1024 => $"{bytes / 1024d / 1024:0.0} MB",
        >= 1024L => $"{bytes / 1024d:0} KB",
        _ => bytes + " B",
    };

    static string Sanitize(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => bad.Contains(c) ? '-' : c).ToArray()).Trim();
        return clean.Length == 0 ? "pack" : clean;
    }

    public sealed class BundleRow : INotifyPropertyChanged
    {
        bool _selected;
        public string Name { get; init; } = "";
        public string Size { get; init; } = "";
        public long Bytes { get; init; }

        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
