using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pad.Core;

namespace Pad.Views;

public partial class ApiImportWindow : Window
{
    readonly AppState _state = AppState.Current;
    bool _loading;

    public string? StartRef { get; set; }

    public ApiImportWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WinIcon.Apply(this);
        Loaded += (_, _) => Paint();
    }

    string TargetSet()
    {
        var def = _state.Settings.Launch.CredentialsDefault ?? "global";
        return def == "none" || string.IsNullOrWhiteSpace(def) ? "global" : def;
    }

    void Paint()
    {
        _loading = true;
        FillCompanies();
        if (!string.IsNullOrWhiteSpace(StartRef))
            SelectCompanyFor(StartRef.Trim());
        else if (ApiCompanyBox.SelectedItem is null && ApiCompanyBox.Items.Count > 0)
            ApiCompanyBox.SelectedIndex = 0;
        ApplyCompany();
        if (!string.IsNullOrWhiteSpace(StartRef))
            RefNameBox.Text = StartRef.Trim();
        _loading = false;
        PaintHint();
    }

    void FillCompanies()
    {
        ApiCompanyBox.Items.Clear();
        foreach (var c in CredentialsFile.Catalog)
            ApiCompanyBox.Items.Add(new ComboBoxItem { Tag = c.Ref, Content = CompanyChip(c.Company, c.IconFile) });
        ApiCompanyBox.Items.Add(new ComboBoxItem { Tag = "", Content = "其他" });
    }

    static StackPanel CompanyChip(string company, string iconFile)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        try
        {
            var img = new Image
            {
                Source = new BitmapImage(new Uri(CredentialsFile.IconPack(iconFile), UriKind.Absolute)),
                Width = 18,
                Height = 18,
                Margin = new Thickness(0, 0, 8, 0),
                Stretch = Stretch.Uniform,
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            row.Children.Add(img);
        }
        catch
        {
            // ComboBox still shows the company name if the pack URI fails.
        }
        row.Children.Add(new TextBlock
        {
            Text = company,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    void SelectCompanyFor(string name)
    {
        ComboBoxItem? other = null;
        foreach (var item in ApiCompanyBox.Items.OfType<ComboBoxItem>())
        {
            var tag = item.Tag as string ?? "";
            if (tag == name)
            {
                ApiCompanyBox.SelectedItem = item;
                return;
            }
            if (tag.Length == 0) other = item;
        }
        if (other is not null) ApiCompanyBox.SelectedItem = other;
    }

    void Company_Sel(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        ApplyCompany();
        PaintHint();
    }

    void ApplyCompany()
    {
        var tag = (ApiCompanyBox.SelectedItem as ComboBoxItem)?.Tag as string;
        if (!string.IsNullOrWhiteSpace(tag))
            RefNameBox.Text = tag;
        else if (string.IsNullOrWhiteSpace(RefNameBox.Text))
            RefNameBox.Text = "OPENAI_API_KEY";
    }

    void PaintHint()
    {
        var name = (RefNameBox.Text ?? "").Trim();
        var yaml = _state.Launcher.GetCredentials(TargetSet()) ?? "";
        var refs = CredentialsFile.ListRefs(yaml);
        if (name == CredentialsFile.DeepseekRef)
        {
            DeepseekKeyHint.Text = refs.Contains(name)
                ? "已保存，再写会覆盖"
                : "还没有 DEEPSEEK_API_KEY，启动后模型不会答";
            return;
        }
        DeepseekKeyHint.Text = refs.Contains(name)
            ? "这条 ref 已有值，再写会覆盖"
            : refs.Count == 0
                ? "还没有 refs"
                : "已有：" + string.Join("、", refs);
    }

    void Write_Click(object sender, RoutedEventArgs e)
    {
        var set = TargetSet();
        var name = (RefNameBox.Text ?? "").Trim();
        var secret = DeepseekKeyBox.Password?.Trim() ?? "";
        if (!CredentialsFile.IsRefName(name))
        {
            Ping("refs 名必须是环境变量样式，例如 OPENAI_API_KEY", true);
            return;
        }
        if (secret.Length == 0)
        {
            Ping("先填密钥", true);
            return;
        }
        try
        {
            var yaml = _state.Launcher.GetCredentials(set) ?? "";
            yaml = CredentialsFile.UpsertRef(yaml, name, secret);
            _state.Launcher.SetCredentials(set, yaml);
            DeepseekKeyBox.Clear();
            Ping($"ref {name} 已写入");
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Ping(PadError.Describe(ex), true);
        }
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    void Ping(string text, bool bad = false)
    {
        if (Owner is MainWindow w) w.Toast(text, bad);
    }
}
