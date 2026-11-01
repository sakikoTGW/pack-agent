using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pad.Core;

namespace Pad.Views;

/// <summary>
/// 插件详情窗口：按插件市场的版面渲染（图标 / 名字 / 版本 · 可更新 / 简介 / 标签 / 动作），
/// 不再是 markdown 字段列表，也不塞在页面里当浮层。
/// </summary>
public partial class PluginDetailWindow : Window
{
    readonly PluginRow _row;
    readonly Action? _install;

    public event Action<PluginRow>? UpdateRequested;
    public event Action<PluginRow>? RemoveRequested;
    public event Action<PluginRow>? OpenDirRequested;

    public PluginDetailWindow(PluginRow row, Action? install = null)
    {
        InitializeComponent();
        _row = row;
        _install = install;

        DetailIcon.IconPath = row.IconPath;
        DetailIcon.Spec = row.Spec;
        DetailIcon.Kind = row.Kind;
        DetailName.Text = row.Title;
        DetailSpec.Text = row.Spec;
        DetailVersion.Text = row.VersionText;
        DetailUpdateBadge.Visibility = row.Updatable ? Visibility.Visible : Visibility.Collapsed;
        DetailUpdateText.Text = row.UpdateText;
        DetailDesc.Text = row.Description.Length > 0 ? row.Description : "包里没写简介。";
        DetailTags.ItemsSource = row.Tags;
        if (row.PreviewImages.Count > 0)
        {
            DetailShots.ItemsSource = row.PreviewImages;
            DetailShots.Visibility = Visibility.Visible;
        }
        else
        {
            ShotsLoaded = LoadRepoShotsAsync();
        }

        InfoVersion.Text = row.VersionText;
        InfoLatest.Text = row.Latest.Length > 0 ? row.Latest : "—";
        InfoProfile.Text = row.Profile.Length > 0 ? row.Profile : "—";
        var repo = LaunchPolicy.GithubRepo(row.Repository);
        DetailMeta.Text = repo ?? (row.Trial ? "试验（仅本次运行）" : "—");

        if (_install is not null)
        {
            UpdateBtn.Visibility = Visibility.Visible;
            UpdateBtn.Content = "安装";
        }
        else
        {
            UpdateBtn.Visibility = row.ShowUpdateButton ? Visibility.Visible : Visibility.Collapsed;
            UpdateBtn.Content = row.Version.Length == 0 ? "安装" : "更新";
        }
        FavBtn.Content = row.Favorite ? "★ 已收藏" : "☆ 收藏";
    }

    /// <summary>仓库预览图加载完成（没有仓库/没有图时也完成）；--shot-page plugin-detail 会等它再截图。</summary>
    public Task ShotsLoaded { get; private set; } = Task.CompletedTask;

    async Task LoadRepoShotsAsync()
    {
        try
        {
            DetailShotHint.Visibility = Visibility.Visible;
            var root = AppState.Current.Launcher.Root;
            var shots = await RepoShots.EnsureAsync(root, _row.Spec, _row.Repository);
            if (shots.Count > 0)
            {
                DetailShots.ItemsSource = shots;
                DetailShots.Visibility = Visibility.Visible;
            }
        }
        catch { /* 没网/仓库没有图：不显示预览 */ }
        finally { DetailShotHint.Visibility = Visibility.Collapsed; }
    }

    void Fav_Click(object sender, RoutedEventArgs e)
    {
        var on = Favorites.Toggle(AppState.Current.Launcher.Root, _row.Spec);
        _row.Favorite = on;
        FavBtn.Content = on ? "★ 已收藏" : "☆ 收藏";
    }

    void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_install is { } install)
        {
            Close();
            install();
            return;
        }
        UpdateRequested?.Invoke(_row);
        Close();
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        RemoveRequested?.Invoke(_row);
        Close();
    }

    void Dir_Click(object sender, RoutedEventArgs e)
    {
        OpenDirRequested?.Invoke(_row);
        Close();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>离屏把本窗口渲染成 PNG（--shot-page plugin-detail 用；不经屏幕捕获）。</summary>
    public void RenderTo(string path)
    {
        try
        {
            UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(this);
            var w = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
            var h = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
            var rtb = new RenderTargetBitmap(w, h, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY,
                PixelFormats.Pbgra32);
            rtb.Render(this);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(path);
            png.Save(fs);
            File.WriteAllText(path + ".txt",
                $"logical {ActualWidth}x{ActualHeight}\ndpi {dpi.DpiScaleX}x{dpi.DpiScaleY}\npixels {w}x{h}\n");
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(path + ".txt", ex.ToString()); } catch { }
        }
    }
}
