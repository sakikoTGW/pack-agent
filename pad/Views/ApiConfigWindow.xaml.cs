using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pad.Core;

namespace Pad.Views;

public partial class ApiConfigWindow : Window
{
    readonly AppState _state = AppState.Current;
    bool _loading;
    string? _credName;
    List<LlmProviderRecipe> _providers = [];
    int _providerIndex = -1;

    public ApiConfigWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WinIcon.Apply(this);
        Loaded += (_, _) => Paint();
    }

    void Paint()
    {
        _loading = true;
        PaintCreds();
        PaintLoadSets();
        PaintMatches();
        _loading = false;
    }

    void Ping(string text, bool bad = false)
    {
        if (Owner is MainWindow w) w.Toast(text, bad);
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

    void PaintLoadSets()
    {
        if (CredentialsDefaultBox is null) return;
        var keep = _state.Settings.Launch.CredentialsDefault ?? "global";
        var painting = _loading;
        _loading = true;
        CredentialsDefaultBox.Items.Clear();
        foreach (var set in _state.Launcher.ListCredentials())
            CredentialsDefaultBox.Items.Add(new ComboBoxItem
            {
                Tag = set.Name,
                Content = set.Name == "global" ? "library 全局" : set.Name,
            });
        CredentialsDefaultBox.Items.Add(new ComboBoxItem { Tag = "none", Content = "不拷贝" });
        PickCombo(CredentialsDefaultBox, keep);
        _loading = painting;
    }

    void PaintMatches()
    {
        if (ApiMatchList is null) return;
        ApiMatchList.ItemsSource = CredentialsGate.ListMatches(_state.Launcher, _state.Settings);
    }

    void Load_Sel(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var s = _state.Settings;
        s.Launch.CredentialsDefault = ComboTag(CredentialsDefaultBox) ?? "global";
        _state.ApplySettings(s);
        PaintMatches();
    }

    void PaintCreds()
    {
        var names = _state.Launcher.ListCredentials().Select(s => s.Name).ToList();
        CredList.ItemsSource = names;
        var cur = _credName;
        if (cur is null || !names.Contains(cur)) _credName = names.FirstOrDefault();
        CredList.SelectedItem = _credName;
        CredLoad(_credName);
        PaintProviders();
    }

    void CredLoad(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            CredEditor.Text = "";
            CredPathLine.Text = "";
            return;
        }
        CredEditor.Text = _state.Launcher.GetCredentials(name) ?? "";
        CredPathLine.Text = _state.Launcher.NamedCredentialsPath(name);
    }

    void Cred_Sel(object sender, SelectionChangedEventArgs e)
    {
        _credName = CredList.SelectedItem as string;
        CredLoad(_credName);
        if (!_loading) PaintProviders();
    }

    void CredSave_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || _credName is null) return;
        _state.Launcher.SetCredentials(_credName, CredEditor.Text ?? "");
        CredLoad(_credName);
        PaintProviders();
        PaintLoadSets();
        PaintMatches();
        Ping($"配置 {_credName} 已保存");
    }

    void CredNew_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptPath("新建配置", "名称（小写字母 / 数字 / -，如 work）：");
        if (name is null or { Length: 0 }) return;
        name = name.Trim();
        _state.Launcher.SetCredentials(name, "");
        _credName = name;
        PaintCreds();
        PaintLoadSets();
        PaintMatches();
        CredEditor.Focus();
    }

    void CredDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_credName is null) return;
        if (_credName == "global")
        {
            Ping("global 是默认配置，不能删", true);
            return;
        }
        var ask = MessageBox.Show($"删除配置 {_credName}？", "PAD",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (ask != MessageBoxResult.OK) return;
        _state.Launcher.RemoveCredentials(_credName);
        _credName = null;
        PaintCreds();
        PaintLoadSets();
        PaintMatches();
    }

    void PaintProviders()
    {
        if (ProviderRoster is null) return;
        FillProviderPresets();
        _providers = _state.Launcher.LoadLlmProviders();
        var refs = CurrentRefs();
        var keep = (ProviderRoster.SelectedItem as ProviderRow)?.Recipe.Route;
        var rows = _providers.Select(p => LlmProviders.ToRow(p, refs)).ToList();
        var painting = _loading;
        _loading = true;
        ProviderRoster.ItemsSource = rows;
        var empty = rows.Count == 0;
        ProviderEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ProviderSplit.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        if (keep is not null)
        {
            var match = rows.FirstOrDefault(r => r.Recipe.Route == keep);
            if (match is not null) ProviderRoster.SelectedItem = match;
            else if (rows.Count > 0) ProviderRoster.SelectedIndex = 0;
        }
        else if (rows.Count > 0)
            ProviderRoster.SelectedIndex = 0;
        else
        {
            _providerIndex = -1;
            ClearProviderFields();
        }
        _loading = painting;
        ShowProvider((ProviderRoster.SelectedItem as ProviderRow)?.Recipe.Route);
    }

    IReadOnlyList<string> CurrentRefs()
    {
        if (_credName is null) return [];
        return CredentialsFile.ListRefs(_state.Launcher.GetCredentials(_credName) ?? "");
    }

    void FillProviderPresets()
    {
        if (ProviderPreset is null || ProviderPreset.Items.Count > 0) return;
        foreach (var p in LlmProviders.CatalogPresets())
            ProviderPreset.Items.Add(new ComboBoxItem
            {
                Content = LlmProviders.RowTitle(p) + " · " + p.Route,
                Tag = p.Route,
            });
        ProviderPreset.SelectedIndex = 0;
    }

    void FillProviderEnvChoices(string current)
    {
        var refs = CurrentRefs().ToList();
        if (current.Length > 0 && !refs.Contains(current)) refs.Insert(0, current);
        ProviderEnvBox.ItemsSource = refs;
        ProviderEnvBox.Text = current;
    }

    void ShowProvider(string? route)
    {
        route ??= "";
        _providerIndex = _providers.FindIndex(p => p.Route == route);
        if (_providerIndex < 0)
        {
            if (route.Length == 0) ClearProviderFields();
            PaintProviderHint();
            return;
        }
        var p = _providers[_providerIndex];
        ProviderRouteBox.Text = p.Route;
        ProviderNameBox.Text = p.DisplayName;
        ProviderUrlBox.Text = p.BaseUrl;
        FillProviderEnvChoices(p.ApiKeyEnv);
        ProviderModelsBox.Text = string.Join(Environment.NewLine, p.Models);
        ProviderInputText.IsChecked = p.Input.Contains("text");
        ProviderInputImage.IsChecked = p.Input.Contains("image");
        PaintProviderHint();
    }

    void ClearProviderFields()
    {
        ProviderRouteBox.Text = "";
        ProviderNameBox.Text = "";
        ProviderUrlBox.Text = "";
        FillProviderEnvChoices("");
        ProviderModelsBox.Text = "";
        ProviderInputText.IsChecked = true;
        ProviderInputImage.IsChecked = false;
        PaintProviderHint();
    }

    void PaintProviderHint()
    {
        if (ProviderHint is null) return;
        var rec = ReadProviderFields();
        ProviderHint.Text = LlmProviders.DescribeWillList(rec);
        var warn = rec.Models.All(m => m.Trim().Length == 0);
        ProviderHintBar.Style = (Style)FindResource(warn ? "HintBarWarn" : "HintBar");
        ProviderHint.Style = (Style)FindResource(warn ? "HintBarCopyWarn" : "HintBarCopy");
    }

    void ProviderModels_Lost(object sender, RoutedEventArgs e) => PaintProviderHint();

    void Provider_Sel(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderRoster is null || _loading) return;
        ShowProvider((ProviderRoster.SelectedItem as ProviderRow)?.Recipe.Route);
    }

    LlmProviderRecipe ReadProviderFields()
    {
        var input = new List<string>();
        if (ProviderInputText.IsChecked == true) input.Add("text");
        if (ProviderInputImage.IsChecked == true) input.Add("image");
        if (input.Count == 0) input.Add("text");
        return new LlmProviderRecipe
        {
            Route = ProviderRouteBox.Text.Trim(),
            DisplayName = ProviderNameBox.Text.Trim(),
            BaseUrl = ProviderUrlBox.Text.Trim(),
            ApiKeyEnv = (ProviderEnvBox.Text ?? "").Trim(),
            Api = "openai-completions",
            Models = ProviderModelsBox.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToList(),
            Input = input,
        };
    }

    void ProviderSave_Click(object sender, RoutedEventArgs e)
    {
        var rec = ReadProviderFields();
        if (rec.Route.Length == 0)
        {
            Ping("先填路由名", true);
            return;
        }
        if (rec.ApiKeyEnv.Length > 0 && !CredentialsFile.IsRefName(rec.ApiKeyEnv))
        {
            Ping("apiKeyEnv 必须是 refs 名，例如 OPENAI_API_KEY", true);
            return;
        }
        var i = _providerIndex;
        if (i >= 0 && i < _providers.Count) _providers[i] = rec;
        else
        {
            var existing = _providers.FindIndex(p => p.Route == rec.Route);
            if (existing >= 0) _providers[existing] = rec;
            else _providers.Add(rec);
        }
        _state.Launcher.SaveLlmProviders(_providers);
        PaintProviders();
        SelectProviderRoute(rec.Route);
        Ping("配方已写入各实例 settings.yaml，下次启动进 session.models");
    }

    void SelectProviderRoute(string route)
    {
        foreach (ProviderRow row in ProviderRoster.Items)
        {
            if (row.Recipe.Route == route)
            {
                ProviderRoster.SelectedItem = row;
                return;
            }
        }
    }

    string UniqueRoute(string stem)
    {
        stem = string.IsNullOrWhiteSpace(stem) ? "gateway" : stem.Trim();
        var n = stem;
        var i = 2;
        while (_providers.Any(p => p.Route == n)) n = stem + "-" + i++;
        return n;
    }

    void ProviderNew_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptPath("新建供应商", "路由名（小写字母 / 数字 / -，如 acme-gateway）：");
        if (name is null or { Length: 0 }) return;
        name = UniqueRoute(name.Trim());
        _providers.Add(new LlmProviderRecipe { Route = name, ApiKeyEnv = "OPENAI_API_KEY", Input = ["text"] });
        _state.Launcher.SaveLlmProviders(_providers);
        PaintProviders();
        SelectProviderRoute(name);
    }

    void ProviderPreset_Click(object sender, RoutedEventArgs e)
    {
        var route = (sender as FrameworkElement)?.Tag as string
            ?? (ProviderPreset.SelectedItem as ComboBoxItem)?.Tag as string;
        var preset = LlmProviders.CatalogPresets().FirstOrDefault(p => p.Route == route);
        if (preset is null) return;
        var copy = new LlmProviderRecipe
        {
            Route = UniqueRoute(preset.Route),
            DisplayName = preset.DisplayName,
            ApiKeyEnv = preset.ApiKeyEnv,
            Api = preset.Api,
            BaseUrl = preset.BaseUrl,
            Models = [.. preset.Models],
            Input = [.. preset.Input],
        };
        _providers.Add(copy);
        _state.Launcher.SaveLlmProviders(_providers);
        PaintProviders();
        SelectProviderRoute(copy.Route);
        Ping("已套用 " + preset.Route + "，改 baseURL 和模型后保存");
    }

    void ProviderDelete_Click(object sender, RoutedEventArgs e)
    {
        var route = (ProviderRoster.SelectedItem as ProviderRow)?.Recipe.Route;
        if (string.IsNullOrWhiteSpace(route)) return;
        _providers.RemoveAll(p => p.Route == route);
        _state.Launcher.SaveLlmProviders(_providers);
        PaintProviders();
    }

    void ProviderUp_Click(object sender, RoutedEventArgs e) => MoveProvider(-1);

    void ProviderDown_Click(object sender, RoutedEventArgs e) => MoveProvider(1);

    void MoveProvider(int delta)
    {
        var i = _providerIndex;
        var j = i + delta;
        if (i < 0 || j < 0 || j >= _providers.Count) return;
        (_providers[i], _providers[j]) = (_providers[j], _providers[i]);
        var route = _providers[j].Route;
        _state.Launcher.SaveLlmProviders(_providers);
        PaintProviders();
        SelectProviderRoute(route);
    }

    string? PromptPath(string title, string label)
    {
        var box = new TextBox { Width = 380 };
        var dlg = new Window
        {
            Title = title,
            Width = 460,
            Height = 170,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap },
                    box,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 12, 0, 0),
                        Children =
                        {
                            new Button { Content = "取消", Width = 70, Margin = new Thickness(0, 0, 8, 0), IsCancel = true },
                            new Button
                            {
                                Content = "确定", Width = 70, IsDefault = true,
                                Command = ApplicationCommands.Open,
                            },
                        },
                    },
                },
            },
        };
        dlg.CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Open,
            (_, _) => { dlg.DialogResult = true; dlg.Close(); }));
        return dlg.ShowDialog() == true ? box.Text.Trim() : null;
    }
}
