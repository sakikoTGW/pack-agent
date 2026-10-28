using System.IO;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// 制作整合包。包本身由 Go 写的 <c>packmaker</c> 生成（stdlib zip + 严格校验）：
/// 至少一个插件（组合包 spec），其余结构勾了才写。PAD 只负责收集字段、调它、报错。
/// </summary>
public static class PackMaker
{
    public sealed class Request
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "0.1.0";
        public string Description { get; set; } = "";
        /// <summary>DSH 发行号（必填；npm 没有这号 import 会 PA012）。</summary>
        public string Kernel { get; set; } = "";
        public string Profile { get; set; } = "web";
        /// <summary>组合包 spec 列表；和 LocalPlugins 合计至少一个。</summary>
        public List<string> Plugins { get; set; } = [];
        /// <summary>没上架 npm 的插件：目录整个塞进包里（清单里带 local 指路）。</summary>
        public List<LocalPlugin> LocalPlugins { get; set; } = [];
        public bool Skills { get; set; }
        public bool Rules { get; set; }
        public bool Mcp { get; set; }
        public bool Commands { get; set; }
        public bool Hooks { get; set; }
        public bool Subagents { get; set; }
        public bool Settings { get; set; }
        public string? IconPath { get; set; }
    }

    public sealed record Result(string ZipPath, IReadOnlyList<string> Plugins);

    /// <summary>不能从 npm 装的插件目录。</summary>
    public sealed record LocalPlugin(string Spec, string Dir);

    /// <summary>把整合包做进指定目录（导出目标 / 临时目录都走这条）。</summary>
    public static Result WriteTo(string outDir, Request req)
    {
        var exe = Locate()
            ?? throw new PadError("PA028", "找不到 packmaker.exe",
                "packmaker", AppContext.BaseDirectory,
                ["在 packmaker 目录跑 go build -o bin/packmaker.exe .",
                 "或把 packmaker.exe 放到 PAD 旁边"]);

        var spec = new
        {
            name = req.Name,
            version = req.Version,
            description = req.Description,
            kernel = req.Kernel,
            profile = req.Profile,
            plugins = req.Plugins,
            localPlugins = req.LocalPlugins.Select(p => new { spec = p.Spec, dir = p.Dir }),
            structure = new
            {
                skills = req.Skills,
                rules = req.Rules,
                mcp = req.Mcp,
                commands = req.Commands,
                hooks = req.Hooks,
                subagents = req.Subagents,
                settings = req.Settings,
            },
            icon = req.IconPath,
        };
        var tmp = Path.Combine(Path.GetTempPath(), "packmaker-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(tmp, JsonSerializer.Serialize(spec, new JsonSerializerOptions { WriteIndented = true }));
        try
        {
            var args = $"make --spec \"{tmp}\" --out \"{outDir}\"";
            var res = Proc.Run(exe, args, AppContext.BaseDirectory, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            if (res.ExitCode != 0)
            {
                var tail = res.Tail.Trim();
                var message = tail.Length > 0 ? tail : "packmaker 失败了";
                throw new PadError(res.ExitCode == 2 ? "PA009" : "PA028", "整合包没做成", "packmaker make", message,
                    ["看这句错：校验失败会直接说缺什么（最少要一个插件）"]);
            }
            var lines = res.Tail
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToArray();
            var zip = lines.LastOrDefault() ?? "";
            if (!File.Exists(zip))
                throw new PadError("PA028", "packmaker 没返回 zip 路径", "packmaker make", res.Tail,
                    ["重跑一次；还是这样就报 bug"]);
            return new Result(zip, req.Plugins);
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* temp file */ }
        }
    }

    /// <summary>做进本启动器的整合包库（导入列表用）。</summary>
    public static Result Write(string launcherRoot, Request req) =>
        WriteTo(Path.Combine(launcherRoot, "library", "packs"), req);

    /// <summary>packmaker.exe 可能的落点：env → PAD 旁边 → 仓库 agent-pack-dsh/packmaker/bin。</summary>
    public static string? Locate()
    {
        var env = Environment.GetEnvironmentVariable("PACKMAKER_EXE");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

        var beside = Path.Combine(AppContext.BaseDirectory, "packmaker.exe");
        if (File.Exists(beside)) return beside;

        var repo = Environment.GetEnvironmentVariable("PACK_AGENT_REPO");
        if (!string.IsNullOrWhiteSpace(repo))
        {
            var p = Path.Combine(repo, "agent-pack-dsh", "packmaker", "bin", "packmaker.exe");
            if (File.Exists(p)) return p;
        }

        var walk = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && walk is not null; i++, walk = walk.Parent)
        {
            foreach (var rel in new[]
                     {
                         Path.Combine("packmaker", "bin", "packmaker.exe"),
                         Path.Combine("agent-pack-dsh", "packmaker", "bin", "packmaker.exe"),
                     })
            {
                var p = Path.Combine(walk.FullName, rel);
                if (File.Exists(p)) return p;
            }
        }
        return null;
    }
}
