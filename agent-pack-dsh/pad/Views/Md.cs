using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Pad.Views;

/// <summary>readme.md → FlowDocument. Headings, lists, fences, bold/italic/code/links.</summary>
public static class Md
{
    static readonly Regex Inline = new(
        @"(\*\*[^*]+\*\*|`[^`]+`|\*[^*]+\*|\[[^\]]+\]\([^)]+\))",
        RegexOptions.Compiled);

    /// <summary>
    /// ATX heading. Accepts <c>## 标题</c> and the closing-hash form <c>##2222##</c>.
    /// A bare <c>#not</c> is not a heading.
    /// </summary>
    public static bool TryHeading(string line, out int level, out string text)
    {
        level = 0;
        text = "";
        var t = (line ?? "").Trim();
        var n = 0;
        while (n < t.Length && n < 6 && t[n] == '#') n++;
        if (n == 0 || n == t.Length) return false;
        var raw = t[n..];
        var hadSpace = raw.StartsWith(' ') || raw.StartsWith('\t');
        var body = raw.Trim();
        var trail = body.TrimEnd('#');
        var hadTrail = trail.Length < body.Length;
        if (!hadSpace && !hadTrail) return false;
        text = trail.Trim();
        if (text.Length == 0) return false;
        level = n;
        return true;
    }

    public static FlowDocument Document(string text)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, sans-serif"),
            FontSize = 15,
            LineHeight = 24,
            PagePadding = new Thickness(2),
            TextAlignment = TextAlignment.Left,
            Foreground = new SolidColorBrush(Color.FromRgb(0x19, 0x22, 0x2A)),
        };
        if (string.IsNullOrWhiteSpace(text))
        {
            doc.Blocks.Add(new Paragraph(new Run("这篇详细介绍还是空的。"))
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x62, 0x6A, 0x71)),
            });
            return doc;
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            if (line.StartsWith("```"))
            {
                var code = new List<string>();
                i++;
                while (i < lines.Length && !lines[i].StartsWith("```"))
                {
                    code.Add(lines[i]);
                    i++;
                }
                if (i < lines.Length) i++;
                doc.Blocks.Add(CodeBlock(string.Join("\n", code)));
                continue;
            }
            if (TryHeading(line, out var lv, out var ht))
            {
                var size = lv switch { 1 => 26, 2 => 22, 3 => 18, _ => 16 };
                doc.Blocks.Add(Head(ht, size, lv <= 2 ? 6 : 12, 8));
                i++;
                continue;
            }
            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                var list = new List { Margin = new Thickness(8, 0, 0, 10) };
                while (i < lines.Length && (lines[i].StartsWith("- ") || lines[i].StartsWith("* ")))
                {
                    var p = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
                    Fill(p, lines[i][2..]);
                    list.ListItems.Add(new ListItem(p));
                    i++;
                }
                doc.Blocks.Add(list);
                continue;
            }
            if (line.Trim().Length == 0) { i++; continue; }

            var para = new Paragraph { Margin = new Thickness(0, 0, 0, 10) };
            Fill(para, line);
            i++;
            while (i < lines.Length && ContinuePara(lines[i]))
            {
                para.Inlines.Add(new LineBreak());
                Fill(para, lines[i]);
                i++;
            }
            doc.Blocks.Add(para);
        }
        return doc;
    }

    static bool ContinuePara(string line) =>
        line.Trim().Length > 0
        && !TryHeading(line, out _, out _)
        && !line.StartsWith("- ")
        && !line.StartsWith("* ")
        && !line.StartsWith("```");

    static Paragraph Head(string text, double size, double top, double bottom)
    {
        var p = new Paragraph
        {
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x61, 0x77)),
            Margin = new Thickness(0, top, 0, bottom),
        };
        p.Inlines.Add(new Run(text.Trim()));
        return p;
    }

    static Block CodeBlock(string code)
    {
        var p = new Paragraph
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontSize = 12,
            LineHeight = 18,
            Background = new SolidColorBrush(Color.FromRgb(0xEA, 0xEF, 0xF3)),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 12),
        };
        p.Inlines.Add(new Run(code));
        return p;
    }

    static void Fill(Paragraph p, string s)
    {
        var pos = 0;
        foreach (Match m in Inline.Matches(s))
        {
            if (m.Index > pos) p.Inlines.Add(new Run(s[pos..m.Index]));
            var t = m.Value;
            if (t.StartsWith("**"))
                p.Inlines.Add(new Run(t[2..^2]) { FontWeight = FontWeights.Bold });
            else if (t.StartsWith('`'))
                p.Inlines.Add(new Run(t[1..^1]) { FontFamily = new FontFamily("Consolas") });
            else if (t.StartsWith('['))
            {
                var mid = t.IndexOf("](", StringComparison.Ordinal);
                var label = t[1..mid];
                var href = t[(mid + 2)..^1];
                if (Uri.TryCreate(href, UriKind.Absolute, out var uri))
                {
                    var link = new Hyperlink(new Run(label)) { NavigateUri = uri };
                    p.Inlines.Add(link);
                }
                else p.Inlines.Add(new Run(label));
            }
            else
                p.Inlines.Add(new Run(t[1..^1]) { FontStyle = FontStyles.Italic });
            pos = m.Index + m.Length;
        }
        if (pos < s.Length) p.Inlines.Add(new Run(s[pos..]));
    }
}
