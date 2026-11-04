using System.Text.RegularExpressions;

namespace Pad.Core;

/// <summary>
/// DSH 的「精确版本豁免」提示（P3 / docs §8.3）。插件与运行时不兼容时 DSH 会打印：
/// <code>dsh: to accept the risk, run: dsh plugin --profile &lt;p&gt; allow-version
/// &lt;name&gt;@&lt;version&gt; --dsh-version &lt;exact&gt; --accept-risk</code>
/// 把它结构化成 <see cref="PackageVersion"/>/<see cref="DshVersion"/>/<see cref="Profile"/>，
/// UI 才能给「接受风险安装」按钮而不是死路。只解析文本，不执行任何命令。
/// </summary>
public sealed class PackCompatFix
{
    /// <summary>精确的 <c>name@version</c>（DSH 只接受精确版本，不接受 range）。</summary>
    public string PackageVersion { get; init; } = "";
    /// <summary>精确的 DSH 渲染版本（如 0.2.1-rc.2）。</summary>
    public string DshVersion { get; init; } = "";
    /// <summary>提示里带的 profile（解析不到为空 = 用当前 profile）。</summary>
    public string Profile { get; init; } = "";

    /// <summary>包名（去掉尾部 @version；scoped 名保留 @scope/）。</summary>
    public string Name { get; init; } = "";
    /// <summary>包版本（精确的版本段）。</summary>
    public string Version { get; init; } = "";

    public string Text => $"{PackageVersion} 需要 DSH {DshVersion}"
                          + (Profile.Length > 0 ? $"（profile {Profile}）" : "");
}

/// <summary>
/// 从 DSH 输出里解析 allow-version 豁免提示（P3）。优先解析完整提示行（带 profile），
/// 其次解析 <c>Plugin x@y is incompatible with dsh z</c> 警告作兜底；
/// 两者都没有就返回 null（UI 退回如实报错，不给假按钮）。
/// </summary>
public static class PackCompat
{
    // 包名：可选 @scope/ + name + @exactVersion（版本段必须以数字开头）。
    const string Pkg = @"(?<pkg>(?:@[a-z0-9][a-z0-9._-]*/)?[a-z0-9][a-z0-9._-]*@[0-9][^\s:;,，；。）)\]]*)";
    // DSH 版本：精确 semver（含 prerelease / build），遇到空白/标点收尾。
    const string Dsh = @"(?<dsh>[0-9][^\s:;,，；。）)\]]*)";

    static readonly Regex HintWithProfile = new(
        @"--profile\s+(?<profile>""?[^\s""]+""?)\s+allow-version\s+" + Pkg + @"[^\r\n]*?--dsh-version[\s=]+" + Dsh,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex HintNoProfile = new(
        @"allow-version\s+" + Pkg + @"[^\r\n]*?--dsh-version[\s=]+" + Dsh,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex Warning = new(
        @"Plugin\s+" + Pkg + @"\s+is\s+incompatible\s+with\s+dsh\s+" + Dsh,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>错误文本里有没有兼容门痕迹（用于决定要不要走三选一流程）。</summary>
    public static bool IsIncompatible(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains("incompatible with dsh", StringComparison.OrdinalIgnoreCase)
               || text.Contains("to accept the risk", StringComparison.OrdinalIgnoreCase)
               || text.Contains("allow-version", StringComparison.OrdinalIgnoreCase)
               || text.Contains("version exemption", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>从任意输出（含多行 render）里解析豁免提示；解析不到返回 null。</summary>
    public static PackCompatFix? FromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('|').Trim();
            if (line.Length == 0) continue;
            var hit = Match(HintWithProfile, line, withProfile: true)
                      ?? Match(HintNoProfile, line, withProfile: false)
                      ?? Match(Warning, line, withProfile: false);
            if (hit is not null) return hit;
        }
        return null;
    }

    public static PackCompatFix? FromError(Exception? ex) =>
        ex is null ? null : FromText(PadError.Describe(ex));

    public static PackCompatFix? FromFailure(string? reason) => FromText(reason);

    /// <summary>从 ApplyPackToInstance 的结构化结果找第一个兼容失败（完整 Failures.Reason 没有 TS 截断）。</summary>
    public static PackCompatFix? FromApplyResult(PackApplyResult? result)
    {
        if (result is null) return null;
        foreach (var failure in result.Failures)
            if (FromText(failure.Reason) is { } fix) return fix;
        foreach (var warning in result.Warnings)
            if (FromText(warning) is { } fix) return fix;
        return null;
    }

    static PackCompatFix? Match(Regex regex, string line, bool withProfile)
    {
        var m = regex.Match(line);
        if (!m.Success) return null;
        var packageVersion = Clean(m.Groups["pkg"].Value);
        var dshVersion = Clean(m.Groups["dsh"].Value);
        if (!Split(packageVersion, out var name, out var version)) return null;
        if (!LooksExact(dshVersion)) return null;
        var profile = withProfile ? Clean(m.Groups["profile"].Value) : "";
        return new PackCompatFix
        {
            PackageVersion = packageVersion,
            DshVersion = dshVersion,
            Profile = profile,
            Name = name,
            Version = version,
        };
    }

    /// <summary>name@version 拆包名/版本（scoped 名从最后一个 @ 拆）；版本必须是数字开头。</summary>
    public static bool Split(string? packageAtVersion, out string name, out string version)
    {
        name = "";
        version = "";
        var s = (packageAtVersion ?? "").Trim().Trim('"');
        var at = s.LastIndexOf('@');
        if (at <= 0 || at == s.Length - 1) return false;
        name = s[..at];
        version = s[(at + 1)..].Trim();
        if (version.Length == 0 || !char.IsAsciiDigit(version[0])) return false;
        return true;
    }

    /// <summary>看起来是精确 semver（数字开头，只含 semver 字符；range/前缀一律不算）。</summary>
    public static bool LooksExact(string? version)
    {
        var v = (version ?? "").Trim().Trim('"');
        if (v.Length == 0 || !char.IsAsciiDigit(v[0])) return false;
        foreach (var ch in v)
        {
            if (char.IsAsciiLetterOrDigit(ch)) continue;
            if (ch is '.' or '-' or '+') continue;
            return false;
        }
        return true;
    }

    /// <summary>去掉提示句尾的标点/引号（不碰版本里的点和连字符）。</summary>
    static string Clean(string value) => (value ?? "").Trim().Trim('"', '\'', '`')
        .TrimEnd('.', ',', ';', '，', '；', '。', ')', '）', ']', '】');
}
