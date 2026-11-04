using System.Text;

namespace Pad.Core;

/// <summary>汉字 → 拼音 / 首字母，用于商店搜索的拼音、首字母匹配。</summary>
public static class PinyinSearch
{
    /// <summary>把一段文本转成「全拼」（只含拼音与 ASCII 字母数字）和「首字母」。</summary>
    public static (string Full, string Initials) Convert(string text)
    {
        var full = new StringBuilder(text.Length * 2);
        var initials = new StringBuilder();
        foreach (var ch in text)
        {
            if (PinyinData.TrySyllable(ch, out var syllable))
            {
                full.Append(syllable);
                initials.Append(syllable[0]);
            }
            else if (char.IsAsciiLetterOrDigit(ch))
            {
                full.Append(char.ToLowerInvariant(ch));
            }
        }
        return (full.ToString(), initials.ToString());
    }

    /// <summary>像 pinyin/首字母查询：纯 ASCII 字母数字/空格，至少 2 个字母，且不含汉字。</summary>
    public static bool LooksLikePinyinQuery(string text)
    {
        var letters = 0;
        var hasHan = false;
        foreach (var ch in text)
        {
            if (ch > 0x2E7F) hasHan = true;
            else if (char.IsAsciiLetter(ch)) letters++;
            else if (!char.IsAsciiDigit(ch) && ch is not (' ' or '-' or '_')) return false;
        }
        return !hasHan && letters >= 2;
    }
}
