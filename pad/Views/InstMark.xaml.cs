using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pad.Core;

namespace Pad.Views;

public partial class InstMark : UserControl
{
    bool _watching;

    public InstMark()
    {
        InitializeComponent();
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

    void Paint()
    {
        if (!IsLoaded) return;
        try
        {
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
            if (kind == InstIcon.Kind.Pack && pack is not null)
            {
                Pic.Source = Skin.LoadBitmap(pack);
                Pic.Visibility = Visibility.Visible;
                Glyph.Visibility = Visibility.Collapsed;
                return;
            }

            var bundle = BundleIcon.First(AppState.Current.Launcher.Root, profiles);
            if (bundle is not null)
            {
                var bmp = Skin.LoadBitmap(bundle);
                if (bmp is not null)
                {
                    Pic.Source = bmp;
                    Pic.Visibility = Visibility.Visible;
                    Glyph.Visibility = Visibility.Collapsed;
                    return;
                }
            }

            Pic.Source = null;
            Pic.Visibility = Visibility.Collapsed;
            Glyph.Visibility = Visibility.Visible;
            if (kind == InstIcon.Kind.Ds)
            {
                Pic.Source = Skin.LoadBitmap(null, new Uri("pack://application:,,,/Assets/pad-logo.png"));
                Pic.Visibility = Visibility.Visible;
                Glyph.Visibility = Visibility.Collapsed;
                return;
            }

            var key = kind == InstIcon.Kind.Tui ? "I.Terminal" : "I.Window";
            Glyph.Glyph = TryFindResource(key) as Geometry
                ?? Application.Current?.TryFindResource(key) as Geometry;
        }
        catch
        {
            Pic.Source = Skin.LoadBitmap(null, new Uri("pack://application:,,,/Assets/pad-logo.png"));
            Pic.Visibility = Visibility.Visible;
            Glyph.Visibility = Visibility.Collapsed;
        }
    }
}
