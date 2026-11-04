using System.IO;

namespace Pad.Core;

/// <summary>
/// 一种「怎么看见」的实现（产品级）。与实例、home、版本都无关：
/// 版本是某个安装的属性，能不能用于某个实例由 <see cref="Requires"/> 这份 Recipe 决定。
/// 真正的可用性判断在 <see cref="BundleCatalog"/>。
/// </summary>
public sealed record Bundle(
    string Id,
    string Kind,
    string Label,
    string Shows,
    bool Exclusive,
    bool NeedsEndpoint,
    string Requires)
{
    public static readonly Bundle Tui = new(
        "dsh-tui", "tui", "终端（dsh-tui）", "终端窗口", false, false,
        "profile 里装 @deepseek-harness-tui/dsh-tui");

    public static readonly Bundle Web = new(
        "dsh-web-app", "web", "浏览器（dsh-web-app）", "浏览器窗口", false, true,
        "profile 里装 @deepseek-ai/dsh-web-app");

    public static readonly Bundle Official = new(
        "official-electron", "app", "官方桌面端", "桌面端窗口", true, false,
        "官方 Electron 壳 exe");

    public static readonly Bundle Tauri = new(
        "tauri", "tauri", "Tauri 桌面端", "桌面端窗口", true, false,
        "Tauri 壳 exe");

    /// <summary>内置的产品级 surface。壳类实现（第三方 Electron）由 <see cref="ForShell"/> 动态生成。</summary>
    public static IReadOnlyList<Bundle> Builtins { get; } = [Tui, Web, Official, Tauri];

    /// <summary>按 id 或 kind 找内置 surface（CLI 的 --surface 兼容旧的 tui/web/app/tauri）。</summary>
    public static Bundle? For(string? name)
    {
        var key = (name ?? "").Trim();
        if (key.Length == 0) return null;
        return Builtins.FirstOrDefault(s =>
            string.Equals(s.Id, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(s.Kind, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>把本机探测到的一个桌面壳变成一个 surface：每个壳是它自己的一种「怎么看见」。</summary>
    public static Bundle ForShell(Shell shell)
    {
        var exe = shell.Exe ?? "";
        var name = string.IsNullOrWhiteSpace(exe) ? "桌面端" : Path.GetFileNameWithoutExtension(exe);
        var isTauri = name.Contains("tauri", StringComparison.OrdinalIgnoreCase);
        return new Bundle(
            "electron:" + (string.IsNullOrWhiteSpace(exe) ? name : exe),
            isTauri ? "tauri" : "app",
            isTauri ? "Tauri 桌面端 · " + name : "桌面端 · " + name,
            "桌面端窗口",
            true,
            false,
            "exe: " + exe);
    }
}

/// <summary>
/// 某个实例能不能用某种 surface 打开：只做 Recipe 检查，不改 surface 本身。
/// <see cref="Profile"/> 非空 = 由这个 profile 满足（tui/web）；
/// 壳类没有 profile，满足与否看 exe 在不在。
/// </summary>
public sealed record BundleOption(
    Bundle Bundle,
    bool Available,
    string Detail,
    string? Profile = null,
    string Version = "",
    string? Exe = null);

/// <summary>把「本机装了什么」翻译成「这个实例可以用哪些 surface 打开」。</summary>
public static class BundleCatalog
{
    /// <summary>
    /// 一个实例的全部候选 surface：先是两个 bundle 类（tui / web），再是壳类。
    /// <paramref name="shells"/> 为 null 时只列 bundle 类（调用方没有做本机探测）。
    /// </summary>
    public static IReadOnlyList<BundleOption> ForInstance(
        Launcher launcher, Instance inst, IReadOnlyList<Shell>? shells = null)
    {
        var profiles = launcher.Profiles(inst);
        var list = new List<BundleOption>(6);

        var tui = profiles.FirstOrDefault(p => p.UiSurfaces.Contains("tui"));
        list.Add(new BundleOption(
            Bundle.Tui,
            tui is not null,
            tui is null ? "缺 " + Bundle.Tui.Requires : $"profile {tui.Name} · {VersionOf(tui, "dsh-tui")}",
            tui?.Name,
            tui is null ? "" : VersionOf(tui, "dsh-tui")));

        var web = profiles.FirstOrDefault(p => p.UiSurfaces.Contains("web"));
        list.Add(new BundleOption(
            Bundle.Web,
            web is not null,
            web is null ? "缺 " + Bundle.Web.Requires : $"profile {web.Name} · {VersionOf(web, "web-app")}",
            web?.Name,
            web is null ? "" : VersionOf(web, "web-app")));

        // 壳类：每个探测到的桌面壳是一种 surface；没有装官方壳时也留一行“缺”。
        var official = shells?.FirstOrDefault(s => IsOfficial(s.Exe));
        list.Add(new BundleOption(
            Bundle.Official,
            official is not null,
            official is null ? "本机没探测到官方壳" : official.Exe ?? "",
            Exe: official?.Exe));

        foreach (var shell in shells ?? [])
        {
            if (IsOfficial(shell.Exe)) continue;
            var surface = Bundle.ForShell(shell);
            list.Add(new BundleOption(surface, true, shell.Exe ?? "", Exe: shell.Exe));
        }

        return list;
    }

    /// <summary>profile 里某个 UI bundle 的版本：从 <c>name@version</c> 形式的 bundle 声明里取。</summary>
    static string VersionOf(DshProfile profile, string bundleHint) =>
        profile.Bundles
            .FirstOrDefault(b => b.Contains(bundleHint, StringComparison.OrdinalIgnoreCase))
            ?.Split('@').LastOrDefault() ?? "";

    /// <summary>官方壳 = exe 名或产品名带 DeepSeek（和容器行同一条判据）。</summary>
    public static bool IsOfficial(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) return false;
        try
        {
            var trimmed = exe.Trim().Trim('"');
            var name = Path.GetFileNameWithoutExtension(trimmed);
            if (name.Contains("DeepSeek", StringComparison.OrdinalIgnoreCase)) return true;
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(trimmed);
            return (info.ProductName ?? "").Contains("DeepSeek", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
