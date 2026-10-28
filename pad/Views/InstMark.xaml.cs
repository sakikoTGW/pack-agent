using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pad.Core;

namespace Pad.Views;

public partial class InstMark : UserControl
{
    bool _watching;
    System.Windows.Media.Brush? _tileBg;

    /// <summary>直接给一个图标文件（任务行用：任务创建时记下当时的整合包/实例图标）。</summary>
    public static readonly DependencyProperty IconPathProperty = DependencyProperty.Register(
        nameof(IconPath), typeof(string), typeof(InstMark),
        new PropertyMetadata(null, (d, _) => ((InstMark)d).Paint()));

    public string? IconPath
    {
        get => (string?)GetValue(IconPathProperty);
        set => SetValue(IconPathProperty, value);
    }

    public InstMark()
    {
        InitializeComponent();
        _tileBg = Tile.Background;
        DataContextChanged += (_, _) => Paint();
        Loaded += (_, _) =>
        {
            if (!_watching)
            {
                BundleIcon.Changed += OnBundleIcon;
                _watching = true;
            }
            Paint();
        };
        Unloaded += (_, _) =>
        {
            if (!_watching) return;
            BundleIcon.Changed -= OnBundleIcon;
            _watching = false;
        };
        SizeChanged += (_, _) =>
        {
            var s = Math.Max(10, Math.Min(ActualHeight, ActualWidth) * 0.42);
            if (!double.IsNaN(s) && s > 0) Glyph.Height = s;
        };
    }

    void OnBundleIcon() => Dispatcher.BeginInvoke(Paint);

    /// <summary>临时诊断：PAD_ICON_DEBUG=&lt;file&gt; 时记录每个 tile 走了哪条分支。</summary>
    static void Dbg(string line)
    {
        var path = Environment.GetEnvironmentVariable("PAD_ICON_DEBUG");
        if (string.IsNullOrWhiteSpace(path)) return;
        try { System.IO.File.AppendAllText(path!, DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + Environment.NewLine); }
        catch { /* debug only */ }
    }

    void Paint()
    {
        if (!IsLoaded) return;
        try
        {
            // 图标图像自带黑底/透明底，不再套一层深色底板（官方 DSH 本来就是黑的，保留它自己的黑）。
            Tile.Background = System.Windows.Media.Brushes.Transparent;
            // 任务行直接传图标文件（整合包/实例自己的图，不画状态图标）。
            if (!string.IsNullOrWhiteSpace(IconPath))
            {
                var direct = Skin.LoadBitmap(IconPath);
                if (direct is not null)
                {
                    Pic.Stretch = Stretch.UniformToFill;
                    Pic.Source = direct;
                    Pic.Visibility = Visibility.Visible;
                    Glyph.Visibility = Visibility.Collapsed;
                    Dbg($"  -> JOB image {IconPath}");
                    return;
                }
            }

            // 探测行自带的图标（桌面端 resources\icon.png）：官方小黑鲸 / 第三方自己的图。
            if (DataContext is ProfileVm { IconPath: { Length: > 0 } rowIcon })
            {
                var custom = Skin.LoadBitmap(rowIcon);
                if (custom is not null)
                {
                    Pic.Stretch = Stretch.UniformToFill;
                    Pic.Source = custom;
                    Pic.Visibility = Visibility.Visible;
                    Glyph.Visibility = Visibility.Collapsed;
                    Dbg($"  -> ROW image {rowIcon}");
                    return;
                }
            }

            Instance? inst = DataContext as Instance;
            DshProfile? one = null;
            if (DataContext is ProfileVm vm)
            {
                inst = vm.Instance;
                one = vm.Profile;
            }
            var pack = inst is null ? null : InstIcon.PackImage(inst);
            IEnumerable<DshProfile> profiles = [];
            bool tui, web;
            if (one is not null)
            {
                tui = one.HasTui;
                web = one.IsWeb;
                profiles = [one];
            }
            else if (inst is not null)
            {
                profiles = AppState.Current.Launcher.Profiles(inst);
                tui = profiles.Any(p => p.HasTui);
                web = profiles.Any(p => p.IsWeb);
            }
            else
            {
                tui = web = false;
            }

            var kind = InstIcon.Pick(pack is not null, tui, web);
            Dbg($"ctx={DataContext?.GetType().Name} inst={inst?.Id ?? "-"} one={one?.Name ?? "-"} " +
                $"pack={pack ?? "-"} kind={kind} tui={tui} web={web}");
            if (kind == InstIcon.Kind.Pack && pack is not null)
            {
                Pic.Stretch = Stretch.UniformToFill;
                Pic.Source = Skin.LoadBitmap(pack);
                Pic.Visibility = Visibility.Visible;
                Glyph.Visibility = Visibility.Collapsed;
                Dbg($"  -> PACK image {pack} loaded={Pic.Source is not null}");
                return;
            }

            var bundle = BundleIcon.First(AppState.Current.Launcher.Root, profiles);
            if (bundle is not null)
            {
                var bmp = Skin.LoadBitmap(bundle);
                if (bmp is not null)
                {
                    // 载体图标可能是一横条 wordmark：留完整图，不裁。
                    Pic.Stretch = Stretch.Uniform;
                    Pic.Source = bmp;
                    Pic.Visibility = Visibility.Visible;
                    Glyph.Visibility = Visibility.Collapsed;
                    Dbg($"  -> BUNDLE image {bundle}");
                    return;
                }
                Dbg($"  bundle icon unreadable: {bundle}");
            }
            else
            {
                Dbg("  no bundle icon");
            }

            Pic.Source = null;
            Pic.Visibility = Visibility.Collapsed;
            Glyph.Visibility = Visibility.Visible;
            if (kind == InstIcon.Kind.Ds)
            {
                // 最后兜底：DeepSeek 蓝色鲸鱼。绝不画名字首字母，也不拿 PAD 自己的 logo 顶。
                Pic.Stretch = Stretch.Uniform;
                Pic.Source = Skin.LoadBitmap(null, new Uri("pack://application:,,,/Assets/api/deepseek.png"));
                Pic.Visibility = Visibility.Visible;
                Glyph.Visibility = Visibility.Collapsed;
                Dbg("  -> DS fallback deepseek whale");
                return;
            }

            Tile.Background = _tileBg;
            var key = kind == InstIcon.Kind.Tui ? "I.Terminal" : "I.Window";
            Glyph.Glyph = TryFindResource(key) as Geometry
                ?? Application.Current?.TryFindResource(key) as Geometry;
            Dbg($"  -> GLYPH {key} geometry={Glyph.Glyph is not null}");
        }
        catch
        {
            Pic.Stretch = Stretch.Uniform;
            Pic.Source = Skin.LoadBitmap(null, new Uri("pack://application:,,,/Assets/api/deepseek.png"));
            Pic.Visibility = Visibility.Visible;
            Glyph.Visibility = Visibility.Collapsed;
            Dbg("  -> EXCEPTION fallback deepseek whale");
        }
    }
}
