using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using Pad.Core;

namespace Pad.Views;

/// <summary>
/// v3 整合包导入：第一页选目标实例，第二页（只在有冲突时）逐条选替换/保留/跳过。
/// 真正的落盘交给 <see cref="PackInstall"/>，窗口只负责收决策与显示结果。
/// </summary>
public partial class PackImportWindow : Window
{
    readonly Launcher _launcher;
    readonly string _packPath;
    readonly PackSpec _spec;
    readonly ObservableCollection<ConflictRow> _rows = [];
    bool _conflictsShown;

    /// <summary>装完的实例；null = 没装（取消或失败）。</summary>
    public Instance? Installed { get; private set; }

    /// <summary>最后一行结果摘要（给调用方 toast）。</summary>
    public string Summary { get; private set; } = "";

    public PackImportWindow(Launcher launcher, string packPath, PackSpec spec, Instance? preferred = null)
    {
        InitializeComponent();
        _launcher = launcher;
        _packPath = packPath;
        _spec = spec;

        HeaderSub.Text = $"{spec.Name} {spec.Version}";
        PackTitle.Text = spec.Name;
        PackVersion.Text = spec.Version;
        PackMeta.Text = $"内核 {spec.Kernel.Mode}{(spec.Kernel.Version.Length > 0 ? " " + spec.Kernel.Version : "")}"
            + $" · {spec.Profiles.Count} 个 profile · {spec.Integrity.Files.Count} 个文件";
        SchemaBadge.Text = spec.WasLegacy ? "旧格式 · 兼容" : "v3";

        var instances = launcher.Instances();
        InstanceCombo.ItemsSource = instances;
        InstanceCombo.DisplayMemberPath = "Name";
        InstanceCombo.SelectedItem = preferred ?? instances.FirstOrDefault();

        NewNameBox.Text = spec.Name;
        var hasExisting = instances.Count > 0;
        ExistingRadio.IsChecked = hasExisting;
        NewRadio.IsChecked = !hasExisting;
        InstanceCombo.IsEnabled = hasExisting;
        NewNameBox.IsEnabled = !hasExisting;
        TargetHint.Text = spec.Kernel.Mode == "pin" && spec.Kernel.Version.Length > 0
            ? $"新建实例会钉到内核 {spec.Kernel.Version}（没装的话先去下载页装）。"
            : "工作区按包里的 workspace 模式创建。";

        ConflictList.ItemsSource = _rows;
        FooterHint.Text = "只装这个包；实例里其它内容不动。";
    }

    void ExistingRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (InstanceCombo is null) return;
        InstanceCombo.IsEnabled = true;
        NewNameBox.IsEnabled = false;
    }

    void NewRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (InstanceCombo is null) return;
        InstanceCombo.IsEnabled = false;
        NewNameBox.IsEnabled = true;
    }

    void Bulk_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (BulkCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
        var choice = item.Content?.ToString() ?? "保留";
        foreach (var row in _rows) row.Choice = choice;
    }

    async void Start_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        try
        {
            var inst = ResolveTarget();
            if (inst is null) return;

            if (!_conflictsShown)
            {
                var conflicts = PackInstall.Conflicts(_spec, inst, _packPath);
                if (conflicts.Count > 0)
                {
                    ShowConflicts(conflicts);
                    _conflictsShown = true;
                    return;
                }
            }

            var decisions = _rows.Count > 0
                ? _rows.ToDictionary(r => r.Path, r => r.Choice switch
                {
                    "替换" => "replace",
                    "跳过" => "skip",
                    _ => "keep",
                })
                : null;

            StartBtn.IsEnabled = false;
            StartBtn.Content = "导入中…";
            var opt = new PackImportOptions("keep", InstallBundles: true, decisions);
            var res = await Task.Run(() => PackInstall.Install(_launcher, inst, _packPath, opt));

            Installed = inst;
            Summary = res.Messages.FirstOrDefault(m => m.StartsWith("files ", StringComparison.Ordinal))
                ?? res.Messages.LastOrDefault() ?? "";
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = PadError.Describe(ex);
            ErrorText.Visibility = Visibility.Visible;
            StartBtn.IsEnabled = true;
            StartBtn.Content = "开始导入";
        }
    }

    Instance? ResolveTarget()
    {
        if (ExistingRadio.IsChecked == true)
        {
            if (InstanceCombo.SelectedItem is Instance existing) return existing;
            ShowError("先选一个现有实例。");
            return null;
        }

        var name = NewNameBox.Text.Trim();
        if (name.Length == 0) { ShowError("给新实例起个名字。"); return null; }
        try
        {
            var inst = _launcher.CreateInstance(name, _spec.Kernel.Version);
            return inst;
        }
        catch (Exception ex)
        {
            ShowError(PadError.Describe(ex) + "（新实例需要内核已安装；也可以装进现有实例）");
            return null;
        }
    }

    void ShowConflicts(List<PackConflict> conflicts)
    {
        foreach (var c in conflicts)
        {
            _rows.Add(new ConflictRow
            {
                Path = c.Path,
                Times = $"你的 {c.LocalWrite:MM-dd HH:mm}  ·  包内 {c.PackWrite:MM-dd HH:mm}",
            });
        }

        ConflictTitle.Text = $"{conflicts.Count} 个文件已被修改";
        ConflictPanel.Visibility = Visibility.Visible;
        StartBtn.Content = "开始导入";
        FooterHint.Text = "保留 = 你的文件不动，新版存成 .pack-new；替换前会备份你的版本。";
    }

    void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>冲突清单的一行。</summary>
    public sealed class ConflictRow : INotifyPropertyChanged
    {
        string _choice = "保留";

        public string Path { get; init; } = "";
        public string Times { get; init; } = "";
        public string[] Choices { get; } = ["保留", "替换", "跳过"];

        public string Choice
        {
            get => _choice;
            set
            {
                if (_choice == value) return;
                _choice = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Choice)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
