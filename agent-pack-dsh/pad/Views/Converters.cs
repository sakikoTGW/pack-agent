using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Pad.Core;

namespace Pad.Views;

public sealed class BoolToVis : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var flag = value is true;
        if (Invert) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>Show a block only when a collection actually has rows.</summary>
public sealed class CountToVis : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var any = value is int n && n > 0;
        if (Invert) any = !any;
        return any ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>Idle / running / unreachable, as a pip colour.</summary>
public sealed class StatePip : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var key = value switch
        {
            int n when n < 0 => "InkDisabled",
            int n when n == 0 => "Success",
            int => "Accent",
            true => "Accent",
            _ => "InkDisabled",
        };
        return Application.Current.TryFindResource(key) as Brush
               ?? new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>The button label is the action, not the state: an enabled pack offers 停用.</summary>
public sealed class PackAction : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is true ? "停用" : "启用";

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>Picks the pill style that matches a boolean state.</summary>
public sealed class StatePill : IValueConverter
{
    public string OnKey { get; set; } = "PillSuccess";
    public string OffKey { get; set; } = "Pill";

    public object? Convert(object? value, Type t, object? p, CultureInfo c) =>
        Application.Current.TryFindResource(value is true ? OnKey : OffKey);

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>Shortens a long filesystem path in the middle so both ends stay readable.</summary>
public sealed class MiddleEllipsis : IValueConverter
{
    public int Max { get; set; } = 46;

    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var s = value?.ToString() ?? "";
        if (s.Length <= Max) return s;
        var keep = (Max - 3) / 2;
        return s[..keep] + "..." + s[^keep..];
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>Monogram text for a name that has no badge of its own.</summary>
public sealed class Initial : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var s = (value?.ToString() ?? "").Trim();
        if (s.Length == 0) return "?";
        return s.Length <= 2 ? s.ToUpperInvariant() : s[..2].ToUpperInvariant();
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>WPF Image cannot bind an SVG path; Skin rasters it on this thread.</summary>
public sealed class PathToBmp : IValueConverter
{
    public object? Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is string s && s.Length > 0 ? Skin.LoadBitmap(s) : null;

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>First eight characters of a session id — enough to tell rows apart.</summary>
public sealed class ShortId : IValueConverter
{
    public int Keep { get; set; } = 8;

    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var s = value?.ToString() ?? "";
        return s.Length <= Keep ? s : s[..Keep];
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}
