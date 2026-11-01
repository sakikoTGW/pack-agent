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

    public event Action<PluginRow>? UpdateRequested;
    public event Action<PluginRow>? RemoveRequested;
    public event Action<PluginRow>? OpenDirRequested;

    public PluginDetailWindow(PluginRow row)
    {
        InitializeComponent();
        _row = row;

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
        DetailShots.ItemsSource = row.PreviewImages;
        DetailShots.Visibility = row.PreviewImages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var meta = new List<string> { $"--profile {row.Profile}" };
        if (row.Latest.Length > 0) meta.Add($"npm latest {row.Latest}");
        if (row.Trial) meta.Add("试验：仅本次运行，还没固化");
        DetailMeta.Text = string.Join(" · ", meta);

        UpdateBtn.Visibility = row.ShowUpdateButton ? Visibility.Visible : Visibility.Collapsed;
        UpdateBtn.Content = row.Version.Length == 0 ? "安装" : "更新";
        DirBtn.Visibility = row.PackageDir.Length > 0 && Directory.Exists(row.PackageDir)
            ? Visibility.Visible : Visibility.Collapsed;
        RemoveBtn.Visibility = row.Trial ? Visibility.Collapsed : Visibility.Visible;
    }

    void Update_Click(object sender, RoutedEventArgs e)
    {
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
