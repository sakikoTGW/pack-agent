using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Pad.Views;

/// <summary>
/// One icon. The geometry comes from Theme/Icons.xaml and the size is Height, so the
/// same path serves a 10px quantifier and a 16px tab. Without a square measure the
/// Path would report its 1024-unit viewbox and blow every row apart.
/// The fill follows inherited Foreground: a hover that recolours a button recolours
/// its icon too.
/// </summary>
public class Ico : Control
{
    static Ico()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Ico),
            new FrameworkPropertyMetadata(typeof(Ico)));
    }

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(Geometry), typeof(Ico),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Geometry? Glyph
    {
        get => (Geometry?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>
    /// The attached form, for a control whose own Content is already taken: a page tab
    /// carries its label as Content, so its icon has to ride alongside and be picked up
    /// by the template.
    /// </summary>
    public static readonly DependencyProperty OfProperty = DependencyProperty.RegisterAttached(
        "Of", typeof(Geometry), typeof(Ico),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static void SetOf(DependencyObject d, Geometry value) => d.SetValue(OfProperty, value);

    public static Geometry? GetOf(DependencyObject d) => (Geometry?)d.GetValue(OfProperty);

    double Side()
    {
        double raw;
        if (!double.IsNaN(Height) && Height > 0) raw = Height;
        else if (!double.IsNaN(Width) && Width > 0) raw = Width;
        else raw = 16;
        if (double.IsInfinity(raw) || raw > 256) return 16;
        return raw;
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
