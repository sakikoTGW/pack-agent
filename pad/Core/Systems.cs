using System.IO;
using System.Text;

namespace Pad.Core;

/// <summary>
/// The on-disk skeleton of one system: layers own a DSH home, modules own a
/// workspace. Every path here is <b>derived</b> from (system, layer, module) —
/// nothing is stored as an absolute path, which is what makes a world portable:
/// installing only has to map <c>originPath</c> onto the derived path.
/// </summary>
public static class Systems
{
    public const string DefaultLayers = "collect,frontend,backend";

    public sealed record Result(
        string SystemDir,
        string PlanPath,
        IReadOnlyList<string> Layers,
        IReadOnlyList<string> Modules,
        IReadOnlyList<string> Created);

    public static string LayerDir(string systemDir, string layer) =>
        Path.Combine(systemDir, "layers", layer);

    public static string HomeDir(string systemDir, string layer) =>
        Path.Combine(LayerDir(systemDir, layer), "dsh-home");

    public static string ModuleDir(string systemDir, string layer, string module) =>
        Path.Combine(LayerDir(systemDir, layer), "modules", module);

    public static string WorkspaceDir(string systemDir, string layer, string module) =>
        Path.Combine(ModuleDir(systemDir, layer, module), "workspace");

    public static Result New(string root, string name, IReadOnlyList<string> layers,
        IReadOnlyList<string> modules, string moduleLayer)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new PadError("PA050", "system needs a name", "systems/", null, ["pad cli system new <name>"]);

        var dir = Path.GetFullPath(Path.Combine(root, name));
        if (Directory.Exists(dir))
            throw new PadError("PA051", "that system already exists", dir, null,
                ["pad cli system list", "pad cli system new <other-name>"]);

        if (layers.Count == 0) layers = DefaultLayers.Split(',');
        if (!layers.Contains(moduleLayer))
            throw new PadError("PA052", "module layer is not one of the declared layers", moduleLayer, null,
                ["pad cli system new <name> --module-layer " + layers[0]]);

        var created = new List<string>();
        Directory.CreateDirectory(dir);
        created.Add(dir);

        foreach (var layer in layers)
        {
            var hd = HomeDir(dir, layer);
            Directory.CreateDirectory(hd);
            created.Add(hd + "  (DSH_HOME)");
        }

        foreach (var module in modules)
        {
            var md = ModuleDir(dir, moduleLayer, module);
            Directory.CreateDirectory(WorkspaceDir(dir, moduleLayer, module));
            Directory.CreateDirectory(Path.Combine(md, "EVIDENCE"));
            File.WriteAllText(Path.Combine(md, "ACCEPTANCE.md"),
                $"# {module}\n\n> 断言由人写。PAD 只执行，不解读。\n\n" +
                "```powershell\n# 例：npm test\n```\n\n- [ ] 待填\n", new UTF8Encoding(false));
            created.Add(md);
        }

        var plan = Path.Combine(dir, "plan.yaml");
        File.WriteAllText(plan, Plan(name, layers, modules, moduleLayer), new UTF8Encoding(false));
        created.Add(plan);

        var sys = Path.Combine(dir, "SYSTEM.md");
        File.WriteAllText(sys, SystemDoc(name, layers), new UTF8Encoding(false));
        created.Add(sys);

        return new Result(dir, plan, layers, modules, created);
    }

    static string Plan(string name, IReadOnlyList<string> layers, IReadOnlyList<string> modules, string moduleLayer)
    {
        var sb = new StringBuilder();
        sb.AppendLine("name: " + name);
        sb.AppendLine("goal: \"\"          # 人写：这个系统交付什么");
        sb.AppendLine();
        sb.AppendLine("layers:");
        foreach (var l in layers)
        {
            sb.AppendLine($"  - id: {l}");
            sb.AppendLine($"    profile: {l}       # 一个层 = 一个实例 = 一套工具预算");
        }
        sb.AppendLine();
        sb.AppendLine("# 路径不写在这里。工作区 = layers/<layer>/modules/<module>/workspace 推导得出，");
        sb.AppendLine("# 这样地图包安装时只需把 originPath 映射到推导路径，不用存绝对路径。");
        sb.AppendLine("modules:");
        if (modules.Count == 0)
        {
            sb.AppendLine("  []");
        }
        else
        {
            foreach (var m in modules)
            {
                sb.AppendLine($"  - id: {m}");
                sb.AppendLine($"    layer: {moduleLayer}");
                sb.AppendLine("    deps: []");
            }
        }
        return sb.ToString();
    }

    static string SystemDoc(string name, IReadOnlyList<string> layers)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# " + name);
        sb.AppendLine();
        sb.AppendLine("## 目标");
        sb.AppendLine();
        sb.AppendLine("（人写：交付什么，验收在顶上那几条）");
        sb.AppendLine();
        sb.AppendLine("## 非目标");
        sb.AppendLine();
        sb.AppendLine("（人写：明确不做什么，防止范围漂移）");
        sb.AppendLine();
        sb.AppendLine("## 层");
        foreach (var l in layers)
            sb.AppendLine($"- `{l}` — 实例 home：`layers/{l}/dsh-home`");
        sb.AppendLine();
        sb.AppendLine("## 路径约定");
        sb.AppendLine();
        sb.AppendLine("| 东西 | 位置 |");
        sb.AppendLine("|---|---|");
        sb.AppendLine("| 层实例 home | `layers/<layer>/dsh-home` |");
        sb.AppendLine("| 模块工作区 | `layers/<layer>/modules/<module>/workspace` |");
        sb.AppendLine("| 模块断言 | `layers/<layer>/modules/<module>/ACCEPTANCE.md` |");
        sb.AppendLine("| 证据 | `layers/<layer>/modules/<module>/EVIDENCE/` |");
        return sb.ToString();
    }
}
