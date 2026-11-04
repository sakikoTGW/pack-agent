using System.IO;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// 平台自检：链接（junction / symlink）、NeedsHeal 判定、shell、进程表。
/// 不依赖任何 DSH 安装；CI 和 mac/Linux 冒烟用它验证平台层是否真的可用。
/// </summary>
public static class SelfTest
{
    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "pad-selftest-" + Guid.NewGuid().ToString("N"));
        var steps = new List<object>();
        var ok = true;

        void Step(string name, bool passed, object? detail = null)
        {
            ok &= passed;
            steps.Add(new { step = name, ok = passed, detail });
        }

        try
        {
            Directory.CreateDirectory(root);

            // 1) 建目录链接并读回
            var target = Path.Combine(root, "store", "pkg@1.0.0", "node_modules", "pkg");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "index.js"), "x");
            var nm = Path.Combine(root, "node_modules");
            var link = Path.Combine(nm, "pkg");
            PnpmHeal.Junction(link, target, isDir: true);
            var readable = File.Exists(Path.Combine(link, "index.js"));
            var linkTarget = Platform.Current.ReadLinkTarget(link);
            Step("link", readable && !string.IsNullOrWhiteSpace(linkTarget), new { linkTarget });

            // 2) 健康链接不触发重链
            Step("needs-heal.healthy", !PnpmHeal.NeedsHeal(nm));

            // 3) 真目录（拷贝来的）判定为需要重链
            var nm2 = Path.Combine(root, "node_modules2");
            Directory.CreateDirectory(Path.Combine(nm2, "pkg"));
            Directory.CreateDirectory(Path.Combine(nm2, ".pnpm", "pkg@1.0.0", "node_modules", "pkg"));
            Step("needs-heal.copied", PnpmHeal.NeedsHeal(nm2));

            // 4) shell 与进程表
            var shell = Platform.Current.Shell();
            Step("shell", !string.IsNullOrWhiteSpace(shell.Exe), new { shell.Exe, shell.ArgsPrefix });
            var procs = Platform.Current.Processes(32);
            Step("processes", procs.Count > 0, new { count = procs.Count });

            // 5) PATH 查找（找不到不算失败，只记录）
            steps.Add(new { step = "which.node", ok = Proc.Which("node") is not null, detail = Proc.Which("node") });
        }
        catch (Exception ex)
        {
            Step("exception", false, ex.Message);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* 链接目录清不掉也没关系 */ }
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            platform = Platform.Current.Name,
            os = Environment.OSVersion.VersionString,
            ok,
            steps,
        }, Pretty));

        return ok ? 0 : 1;
    }
}
