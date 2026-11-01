using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Pad.Views;

/// <summary>
/// 线性图标（D59）：Lucide 24 网格描边图标（ISC 许可，见 Theme/LineIcons.xaml 署名）。
/// 只描边不填充，颜色跟随 Foreground；几何资源的尺寸约定是 24x24，Height 即成品尺寸。
/// 和 <see cref="Ico"/>（本仓 iconfont 填充图标）是两套笔触：本页内容统一用这一套。
/// </summary>
public class LineIco : Control
{
    static LineIco()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(LineIco),
            new FrameworkPropertyMetadata(typeof(LineIco)));
    }

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(Geometry), typeof(LineIco),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Geometry? Glyph
    {
        get => (Geometry?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Button / ContentControl 上的附加形式：模板按它取图标。</summary>
    public static readonly DependencyProperty OfProperty = DependencyProperty.RegisterAttached(
        "Of", typeof(Geometry), typeof(LineIco),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static void SetOf(DependencyObject d, Geometry value) => d.SetValue(OfProperty, value);

    public static Geometry? GetOf(DependencyObject d) => (Geometry?)d.GetValue(OfProperty);

    double Side()
    {
        if (!double.IsNaN(Height) && Height > 0) return Height;
        if (!double.IsNaN(Width) && Width > 0) return Width;
        return 16;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Glyph is null) return new Size(0, 0);
        var s = Side();
        if (VisualChildrenCount > 0 && GetVisualChild(0) is UIElement child)
            child.Measure(new Size(s, s));
        return new Size(s, s);
    }

    protected override Size ArrangeOverride(Size arrangeBounds)
    {
        if (Glyph is null) return new Size(0, 0);
        var s = Side();
        if (VisualChildrenCount > 0 && GetVisualChild(0) is UIElement child)
            child.Arrange(new Rect(0, 0, s, s));
        return new Size(s, s);
    }
}
