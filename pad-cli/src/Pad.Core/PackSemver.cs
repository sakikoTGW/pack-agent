using System.Text.RegularExpressions;

namespace Pad.Core;

/// <summary>
/// 预检用的 npm-semver 子集（不引第三方包）。DSH 的兼容门是
/// <c>semver.satisfies(runtimeVersion, range, { includePrerelease: true })</c>
/// （见 dsh-app-boot <c>evaluatePluginCompatibility</c>），所以这里的范围解析必须按
/// includePrerelease 语义来：比较器上界写成 <c>-0</c> 哨兵（<c>&gt;=1.2.3 &lt;2.0.0-0</c>），
/// 预发布版参与比较。
///
/// 支持：精确号、<c>= ^ ~ &gt;= &gt; &lt;= &lt;</c>、<c>* x X</c> 通配、部分版本
/// （<c>1</c>/<c>1.2</c>/<c>1.2.x</c>）、连字符范围（<c>1.2.3 - 2.3.4</c>）、空格 AND、
/// <c>||</c> OR。空/非法范围一律不满足（与 DSH 的空 range 判定一致）。
/// </summary>
public static class PackSemver
{
    /// <summary>一个语义化版本；build metadata 不参与比较，只保留原样。</summary>
    public sealed record Version(
        int Major, int Minor, int Patch,
        IReadOnlyList<string> Prerelease, IReadOnlyList<string> Build)
    {
        public bool IsPrerelease => Prerelease.Count > 0;

        public override string ToString()
        {
            var text = $"{Major}.{Minor}.{Patch}";
            if (Prerelease.Count > 0) text += "-" + string.Join(".", Prerelease);
            if (Build.Count > 0) text += "+" + string.Join(".", Build);
            return text;
        }
    }

    static readonly Regex FullRe = new(
        @"^v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?(?:\+([0-9A-Za-z.-]+))?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex LooseRe = new(
        @"^v?(\d+)(?:\.(\d+|x|X|\*))?(?:\.(\d+|x|X|\*))?(?:-([0-9A-Za-z.-]+))?(?:\+([0-9A-Za-z.-]+))?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex HyphenRe = new(
        @"^(\S+)\s+-\s+(\S+)$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // ---- 解析 / 比较 ------------------------------------------------------

    /// <summary>严格解析 <c>1.2.3</c> / <c>1.2.3-rc.2+build</c>；非法返回 null。</summary>
    public static Version? Parse(string? text)
    {
        var s = (text ?? "").Trim();
        if (s.Length == 0) return null;
        var m = FullRe.Match(s);
        if (!m.Success) return null;
        return Build(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value,
            m.Groups[4].Value, m.Groups[5].Value);
    }

    /// <summary>是不是合法精确版本（不含 range 运算符 / 通配）。</summary>
    public static bool IsExact(string? version) => Parse(version) is not null;

    /// <summary>semver 排序：-1 / 0 / 1（prerelease &lt; release，数字标识符按数值比）。</summary>
    public static int Compare(Version a, Version b)
    {
        var c = a.Major.CompareTo(b.Major);
        if (c != 0) return c;
        c = a.Minor.CompareTo(b.Minor);
        if (c != 0) return c;
        c = a.Patch.CompareTo(b.Patch);
        if (c != 0) return c;
        if (a.Prerelease.Count == 0 && b.Prerelease.Count == 0) return 0;
        if (a.Prerelease.Count == 0) return 1;      // 正式版 > 同版本预发布
        if (b.Prerelease.Count == 0) return -1;
        var n = Math.Min(a.Prerelease.Count, b.Prerelease.Count);
        for (var i = 0; i < n; i++)
        {
            var id = CompareIdentifier(a.Prerelease[i], b.Prerelease[i]);
            if (id != 0) return id;
        }
        return a.Prerelease.Count.CompareTo(b.Prerelease.Count);
    }

    static int CompareIdentifier(string a, string b)
    {
        var an = IsNumeric(a);
        var bn = IsNumeric(b);
        if (an && bn)
        {
            // 数字标识符按数值比；去掉前导零避免 int 溢出（超长数字串按长度+字典序）。
            var az = a.TrimStart('0');
            var bz = b.TrimStart('0');
            if (az.Length == 0) az = "0";
            if (bz.Length == 0) bz = "0";
            var c = az.Length.CompareTo(bz.Length);
            if (c != 0) return c;
            c = string.CompareOrdinal(az, bz);
            return c;
        }
        if (an) return -1;                          // 数字标识符 < 字母标识符
        if (bn) return 1;
        return string.CompareOrdinal(a, b);
    }

    static bool IsNumeric(string value) => value.Length > 0 && value.All(char.IsAsciiDigit);

    static Version Build(string major, string minor, string patch, string pre, string build) => new(
        int.Parse(major), int.Parse(minor), int.Parse(patch),
        SplitIds(pre), SplitIds(build));

    static IReadOnlyList<string> SplitIds(string value) =>
        string.IsNullOrEmpty(value)
            ? []
            : value.Split('.', StringSplitOptions.RemoveEmptyEntries);

    // ---- 范围 -------------------------------------------------------------

    /// <summary>
    /// <paramref name="version"/> 是否满足 <paramref name="range"/>（includePrerelease:true 语义）。
    /// version 非法或 range 为空/非法时返回 false。
    /// </summary>
    public static bool Satisfies(string? version, string? range)
    {
        var v = Parse(version);
        if (v is null) return false;
        var spec = (range ?? "").Trim();
        if (spec.Length == 0) return false;
        foreach (var alternative in spec.Split("||", StringSplitOptions.None))
        {
            var set = alternative.Trim();
            if (set.Length == 0) continue;
            if (EvalSet(v, set)) return true;
        }
        return false;
    }

    static bool EvalSet(Version v, string set)
    {
        var hyphen = HyphenRe.Match(set);
        if (hyphen.Success)
        {
            var lo = ParsePartial(hyphen.Groups[1].Value);
            var hi = ParsePartial(hyphen.Groups[2].Value);
            if (lo is null || hi is null) return false;
            return MatchOp(v, ">=", lo.Lower) && MatchOp(v, "<", hi.Upper);
        }
        foreach (var token in set.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var cmps = Desugar(token);
            if (cmps is null) return false;
            foreach (var (op, ver) in cmps)
                if (!MatchOp(v, op, ver)) return false;
        }
        return true;
    }

    static bool MatchOp(Version v, string op, Version target)
    {
        var c = Compare(v, target);
        return op switch
        {
            "=" => c == 0,
            ">" => c > 0,
            ">=" => c >= 0,
            "<" => c < 0,
            "<=" => c <= 0,
            _ => false,
        };
    }

    /// <summary>一个部分版本（允许缺 minor/patch、允许 x/X/* 通配）。</summary>
    sealed record Partial(int Major, int Minor, int Patch, bool HasMinor, bool HasPatch, string Prerelease)
    {
        /// <summary>可比较的下界：缺的段按 0。</summary>
        public Version Lower => new(Major, Minor, Patch, SplitIds(Prerelease), []);

        /// <summary>上界哨兵：第一个比“这个前缀范围”大的版本（带 -0）。</summary>
        public Version Upper => HasPatch
            ? new(Major, Minor, Patch + 1, ["0"], [])
            : HasMinor
                ? new(Major, Minor + 1, 0, ["0"], [])
                : new(Major + 1, 0, 0, ["0"], []);
    }

    static Partial? ParsePartial(string raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return null;
        var m = LooseRe.Match(s);
        if (!m.Success) return null;
        var major = int.Parse(m.Groups[1].Value);
        var hasMinor = m.Groups[2].Success && m.Groups[2].Value.Length > 0;
        var hasPatch = m.Groups[3].Success && m.Groups[3].Value.Length > 0;
        // "1.x" / "1.2.x" / "1.2.*"：通配段视为缺省（npm 的 x-range）。
        var minorRaw = m.Groups[2].Value;
        var patchRaw = m.Groups[3].Value;
        if (IsWildcard(minorRaw)) hasMinor = false;
        if (IsWildcard(patchRaw)) hasPatch = false;
        if (!hasMinor && patchRaw.Length > 0) return null;      // 1.x.3 非法
        var minor = hasMinor ? int.Parse(minorRaw) : 0;
        var patch = hasPatch ? int.Parse(patchRaw) : 0;
        var pre = m.Groups[4].Value;
        return new Partial(major, minor, patch, hasMinor, hasPatch, pre);
    }

    static bool IsWildcard(string value) => value is "" or "x" or "X" or "*";

    static List<(string Op, Version V)>? Desugar(string token)
    {
        var t = (token ?? "").Trim();
        if (t.Length == 0) return null;
        if (t is "*" or "x" or "X") return [];                  // 任意版本
        var op = "";
        foreach (var candidate in new[] { "<=", ">=", "<", ">", "=", "^", "~" })
            if (t.StartsWith(candidate, StringComparison.Ordinal))
            {
                op = candidate;
                t = t[candidate.Length..].Trim();
                break;
            }
        if (t.Length == 0) return null;
        var p = ParsePartial(t);
        if (p is null) return null;
        var lower = p.Lower;
        switch (op)
        {
            case "^":
                return [(">=", lower), ("<", CaretUpper(p))];
            case "~":
                return [(">=", lower), ("<", TildeUpper(p))];
            case ">=":
                return [(">=", lower)];
            case ">":
                // >1.2.3 保留精确比较；>1.2 / >1 用“下一个前缀”的上界（includePrerelease 时带 -0）。
                return p.HasPatch ? [(">", lower)] : [(">=", p.Upper)];
            case "<":
                // <1.2 即 <1.2.0（缺省段按 0），与 npm desugar 一致。
                return [("<", lower)];
            case "<=":
                // <=1.2.3 保留精确比较；<=1.2 / <=1 用上界（<1.3.0-0 / <2.0.0-0）。
                return p.HasPatch ? [("<=", lower)] : [("<", p.Upper)];
            default:
                // 纯精确号（带预发布）就是等号；缺段或纯号按前缀范围。
                if (p.Prerelease.Length > 0 || p.HasPatch) return [("=", lower)];
                return [(">=", lower), ("<", p.Upper)];
        }
    }

    static Version CaretUpper(Partial p)
    {
        // ^1.2.3 -> <2.0.0-0；^0.2.3 -> <0.3.0-0；^0.0.3 -> <0.0.4-0；
        // ^0.0 -> <0.1.0-0；^0 -> <1.0.0-0（npm 的 replaceCaret）。
        if (p.Major > 0) return new(p.Major + 1, 0, 0, ["0"], []);
        if (!p.HasMinor) return new(1, 0, 0, ["0"], []);
        if (p.Minor > 0) return new(0, p.Minor + 1, 0, ["0"], []);
        if (!p.HasPatch) return new(0, 1, 0, ["0"], []);
        return new(0, 0, p.Patch + 1, ["0"], []);
    }

    static Version TildeUpper(Partial p) => p.HasMinor
        ? new(p.Major, p.Minor + 1, 0, ["0"], [])
        : new(p.Major + 1, 0, 0, ["0"], []);
}
