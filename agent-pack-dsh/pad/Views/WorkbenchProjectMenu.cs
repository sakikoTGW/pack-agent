using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Pad.Core;

namespace Pad.Views;

/// <summary>
/// 项目页的「编辑」菜单与项目对话框（D66）：重命名、合并到另一个项目、取消分组。
/// 只动绑定层（project.json），实例/工作区/会话一律不碰。
/// </summary>
public partial class WorkbenchView
{
    static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    void OpenProjectMenu()
    {
        var proj = _project;
        if (proj is null) return;
        var menu = new ContextMenu { PlacementTarget = EditBtn, Placement = PlacementMode.Bottom };
        var rename = new MenuItem { Header = "重命名…" };
        rename.Click += (_, _) => RenameProject(proj);
        var merge = new MenuItem { Header = "合并到…" };
        merge.Click += (_, _) => MergeProject(proj);
        var delete = new MenuItem { Header = "取消分组（只删绑定，不动实例）" };
        delete.Click += (_, _) => RemoveProject(proj);
        menu.Items.Add(rename);
        menu.Items.Add(merge);
        menu.Items.Add(delete);
        menu.IsOpen = true;
    }

    void RenameProject(Project proj)
    {
        var name = PromptText("重命名项目", "显示名。项目 id 与绑定不变（避免把已有窗口/账本断开）。", proj.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            proj.Name = name.Trim();
            new Projects(_state.Launcher).Save(proj);
            _projects = new Projects(_state.Launcher).List();
            _project = _projects.FirstOrDefault(p => p.Id == proj.Id) ?? proj;
            _host.Toast("已重命名");
            Load();
        }
        catch (Exception ex)
        {
            _host.Toast(PadError.Describe(ex), bad: true);
        }
    }

    void MergeProject(Project source)
    {
        var others = _projects.Where(p => p.Id != source.Id).ToList();
        if (others.Count == 0)
        {
            _host.Toast("没有别的项目可合并");
            return;
        }
        var target = PromptPickProject("合并到…",
            $"把「{source.Name}」的工作区与 preset 并进另一个项目，然后删掉它。\n实例与目录都不动。", others);
        if (target is null) return;
        try
        {
            var store = new Projects(_state.Launcher);
            var dst = store.Get(target.Id) ?? target;
            foreach (var ws in source.Workspaces)
                if (!dst.Workspaces.Any(x => Projects.SamePath(x, ws)))
                    dst.Workspaces.Add(ws);
            foreach (var agent in source.Agents)
                if (!dst.Agents.Contains(agent, StringComparer.OrdinalIgnoreCase))
                    dst.Agents.Add(agent);
            store.Save(dst);
            store.Remove(source.Id);
            _projects = store.List();
            _project = _projects.FirstOrDefault(p => p.Id == dst.Id) ?? dst;
            _scope = ResolveProjectInstance(_project);
            _host.Toast($"已合并到 {_project.Name}");
            Load();
        }
        catch (Exception ex)
        {
            _host.Toast(PadError.Describe(ex), bad: true);
        }
    }

    void RemoveProject(Project proj)
    {
        var ask = MessageBox.Show(
            $"取消分组「{proj.Name}」？\n\n只删项目绑定；实例、工作区目录、会话都不动。",
            "PAD", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (ask != MessageBoxResult.OK) return;
        try
        {
            new Projects(_state.Launcher).Remove(proj.Id);
            _projects = new Projects(_state.Launcher).List();
            _project = null;
            ShowHome();
            _host.Toast("已取消分组，实例还在");
        }
        catch (Exception ex)
        {
            _host.Toast(PadError.Describe(ex), bad: true);
        }
    }

    string? PromptText(string title, string hint, string value)
    {
        var win = new Window
        {
            Title = title,
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.Height,
            Width = 420,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            Background = Res("BgCard"),
        };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = hint,
            FontSize = 11.5,
            Foreground = Res("TextMuted"),
            TextWrapping = TextWrapping.Wrap,
        });
        var box = new TextBox
        {
            Text = value,
            Height = 30,
            Margin = new Thickness(0, 10, 0, 16),
            Padding = new Thickness(8, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(box);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var ok = new Button
        {
            Content = "确定",
            Width = 76,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            Style = (Style)Application.Current.Resources["BtnPrimary"],
        };
        var cancel = new Button
        {
            Content = "取消",
            Width = 76,
            Height = 30,
            IsCancel = true,
            Style = (Style)Application.Current.Resources["BtnSm"],
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        win.Content = panel;
        ok.Click += (_, _) => win.DialogResult = true;
        box.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return win.ShowDialog() == true ? box.Text : null;
    }

    Project? PromptPickProject(string title, string hint, IReadOnlyList<Project> options)
    {
        var win = new Window
        {
            Title = title,
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.Height,
            Width = 440,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            Background = Res("BgCard"),
        };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = hint,
            FontSize = 11.5,
            Foreground = Res("TextMuted"),
            TextWrapping = TextWrapping.Wrap,
        });
        var list = new ListBox
        {
            Height = 170,
            Margin = new Thickness(0, 10, 0, 16),
            Background = Res("BgCard"),
            BorderBrush = Res("LineBorder"),
        };
        foreach (var p in options)
            list.Items.Add(new ListBoxItem
            {
                Content = $"{p.Name}   ·   {p.Workspaces.Count} 工作区 · {p.Agents.Count} preset",
                Tag = p,
                Padding = new Thickness(8, 6, 8, 6),
            });
        if (list.Items.Count > 0) list.SelectedIndex = 0;
        panel.Children.Add(list);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var ok = new Button
        {
            Content = "合并",
            Width = 76,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            Style = (Style)Application.Current.Resources["BtnPrimary"],
        };
        var cancel = new Button
        {
            Content = "取消",
            Width = 76,
            Height = 30,
            IsCancel = true,
            Style = (Style)Application.Current.Resources["BtnSm"],
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        win.Content = panel;
        ok.Click += (_, _) => win.DialogResult = true;
        return win.ShowDialog() == true && list.SelectedItem is ListBoxItem { Tag: Project picked }
            ? picked
            : null;
    }
}
