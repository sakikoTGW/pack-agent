using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pad.Views;

/// <summary>
/// 一个插件的小图标（插件市场式）：优先用包里/缓存的真实图标；
/// 没有就按插件类型给一枚彩色字形方块，列表里不会出现空图标。
/// </summary>
public partial class PluginMark : UserControl
{
    public static readonly DependencyProperty IconPathProperty =
        DependencyProperty.Register(nameof(IconPath), typeof(string), typeof(PluginMark),
            new PropertyMetadata("", (d, _) => ((PluginMark)d).Update()));

    public static readonly DependencyProperty SpecProperty =
        DependencyProperty.Register(nameof(Spec), typeof(string), typeof(PluginMark),
            new PropertyMetadata("", (d, _) => ((PluginMark)d).Update()));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(double), typeof(PluginMark),
            new PropertyMetadata(28.0, (d, _) => ((PluginMark)d).Update()));

    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(string), typeof(PluginMark),
            new PropertyMetadata("", (d, _) => ((PluginMark)d).Update()));

    public string IconPath { get => (string)GetValue(IconPathProperty); set => SetValue(IconPathProperty, value); }
    public string Spec { get => (string)GetValue(SpecProperty); set => SetValue(SpecProperty, value); }
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    public PluginMark()
    {
        InitializeComponent();
        Update();
    }

    void Update()
    {
        if (Pic is null || Tile is null || GlyphIcon is null) return;

        var size = Size <= 0 ? 28 : Size;
        Root.Width = size;
        Root.Height = size;
        Tile.CornerRadius = new CornerRadius(size * 0.28);
        GlyphIcon.Width = size * 0.54;
        GlyphIcon.Height = size * 0.54;

        if (!string.IsNullOrWhiteSpace(IconPath) && File.Exists(IconPath))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(IconPath, UriKind.Absolute);
                bmp.EndInit();
                Pic.Source = bmp;
                Pic.Visibility = Visibility.Visible;
                Tile.Visibility = Visibility.Collapsed;
                return;
            }
            catch { /* 坏图就退回字形 */ }
        }

        Pic.Visibility = Visibility.Collapsed;
        Tile.Visibility = Visibility.Visible;
        var (key, bg, fg) = Look(Spec, Kind);
        Tile.Background = new SolidColorBrush(bg);
        GlyphIcon.Foreground = new SolidColorBrush(fg);
        if (Application.Current.TryFindResource(key) is Geometry g) GlyphIcon.Glyph = g;
    }

    /// <summary>按插件种类/名字选字形和颜色：管理口、TUI、网页端、基础、通用。</summary>
    static (string Key, Color Bg, Color Fg) Look(string spec, string kind)
    {
        var s = (spec ?? "").ToLowerInvariant();
        var k = kind ?? "";
        if (k == "管理口" || s.Contains("gateway") || s.Contains("apiproxy"))
            return ("I.Plug", Color.FromRgb(0xFB, 0xF1, 0xE2), Color.FromRgb(0xB0, 0x7A, 0x1E));
        if (k == "TUI" || s.Contains("dsh-tui"))
            return ("I.Terminal", Color.FromRgb(0xE8, 0xF2, 0xF6), Color.FromRgb(0x1D, 0x6F, 0x8C));
        if (s.Contains("web-app") || s.Contains("dsh-web"))
            return ("I.Globe", Color.FromRgb(0xE8, 0xF2, 0xF6), Color.FromRgb(0x1D, 0x6F, 0x8C));
        if (s.Contains("dsh-base") || s.Contains("@deepseek-ai/"))
            return ("I.Layers", Color.FromRgb(0xEA, 0xEE, 0xF3), Color.FromRgb(0x4A, 0x5A, 0x6A));
        return ("I.Mod", Color.FromRgb(0xEA, 0xEE, 0xF3), Color.FromRgb(0x4A, 0x5A, 0x6A));
    }
}
