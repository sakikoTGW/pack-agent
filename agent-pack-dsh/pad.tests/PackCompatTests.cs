using System.IO;
using System.Text;
using System.Text.Json;
using Pad.Core;
using Xunit;

namespace Pad.Tests;

/// <summary>
/// P3（D105 §6 P3）的纯逻辑：DSH allow-version 提示解析、命令参数、以及
/// <see cref="Launcher.MarkInstanceStatus"/> 的 JSON 保字段语义。不跑 dsh/pnpm。
/// </summary>
public sealed class PackCompatTests
{
    const string Hint =
        "dsh: to accept the risk, run: dsh plugin --profile web allow-version dsh-find-skill@0.2.1 " +
        "--dsh-version 0.2.1-rc.2 --accept-risk";

    // ---- allow-version 提示解析 -----------------------------------------

    [Fact]
    public void Parses_full_hint_line()
    {
        var fix = PackCompat.FromText(Hint);
        Assert.NotNull(fix);
        Assert.Equal("dsh-find-skill@0.2.1", fix!.PackageVersion);
        Assert.Equal("0.2.1-rc.2", fix.DshVersion);
        Assert.Equal("web", fix.Profile);
        Assert.Equal("dsh-find-skill", fix.Name);
        Assert.Equal("0.2.1", fix.Version);
    }

    [Fact]
    public void Parses_hint_inside_pad_error_render_with_crlf_and_prefix()
    {
        var rendered = "error[PA024]: dsh plugin add failed\r\n --> instance `x`\r\n  |\r\n"
                       + "  | " + Hint + "\r\n  |\r\n";
        var fix = PackCompat.FromText(rendered);
        Assert.NotNull(fix);
        Assert.Equal("web", fix!.Profile);
        Assert.Equal("dsh-find-skill@0.2.1", fix.PackageVersion);
    }

    [Fact]
    public void Parses_equals_form_of_dsh_version()
    {
        var fix = PackCompat.FromText(
            "dsh: to accept the risk, run: dsh plugin --profile dsh-tui allow-version @scope/pkg@1.2.3 --dsh-version=0.2.1");
        Assert.NotNull(fix);
        Assert.Equal("dsh-tui", fix!.Profile);
        Assert.Equal("@scope/pkg", fix.Name);
        Assert.Equal("1.2.3", fix.Version);
        Assert.Equal("0.2.1", fix.DshVersion);
    }

    [Fact]
    public void Parses_warning_line_as_fallback()
    {
        var fix = PackCompat.FromText(
            "Plugin dsh-context@0.80.0 is incompatible with dsh 0.2.1-rc.2: requirements {\"dsh\":\"^0.3.0\"}. " +
            "Update the plugin or install a plugin version compatible with this dsh runtime.");
        Assert.NotNull(fix);
        Assert.Equal("dsh-context@0.80.0", fix!.PackageVersion);
        Assert.Equal("0.2.1-rc.2", fix.DshVersion);
        Assert.Equal("", fix.Profile); // 兜底行里没有 profile
    }

    [Fact]
    public void Returns_null_when_text_has_no_compat_marker()
    {
        Assert.Null(PackCompat.FromText("error[PA028]: the import command failed"));
        Assert.Null(PackCompat.FromText(""));
        Assert.Null(PackCompat.FromText(null));
        Assert.False(PackCompat.IsIncompatible("plain pnpm error"));
    }

    [Fact]
    public void IsIncompatible_detects_markers()
    {
        Assert.True(PackCompat.IsIncompatible(Hint));
        Assert.True(PackCompat.IsIncompatible("Plugin x@1.0.0 is incompatible with dsh 0.2.1-rc.2"));
        // 光有 PA014 不够（可能是网络/包名问题）：只有真实兼容门文案才给「接受风险」。
        Assert.False(PackCompat.IsIncompatible("error[PA014]: import failed: required dsh plugin add failed"));
    }

    [Fact]
    public void Trims_trailing_punctuation_but_keeps_semver_chars()
    {
        var fix = PackCompat.FromText(
            "dsh: to accept the risk, run: dsh plugin --profile web allow-version dsh-find-skill@0.2.1 " +
            "--dsh-version 0.2.1-rc.2 --accept-risk.");
        Assert.NotNull(fix);
        Assert.Equal("0.2.1-rc.2", fix!.DshVersion);
        Assert.True(PackCompat.LooksExact("1.2.3-rc.1+build.5"));
        Assert.False(PackCompat.LooksExact("^1.2.3"));
        Assert.False(PackCompat.LooksExact("latest"));
    }

    [Fact]
    public void FromApplyResult_reads_structured_failures()
    {
        var result = new PackApplyResult
        {
            Title = "Test",
            Failures =
            [
                new PackPluginFailure { Spec = "dsh-find-skill", Required = true, Reason = "plugin add failed\n" + Hint },
            ],
        };
        var fix = PackCompat.FromApplyResult(result);
        Assert.NotNull(fix);
        Assert.Equal("web", fix!.Profile);
        Assert.Equal("dsh-find-skill@0.2.1", fix.PackageVersion);
        Assert.Null(PackCompat.FromApplyResult(new PackApplyResult()));
    }

    [Fact]
    public void Splits_scoped_and_plain_package_versions()
    {
        Assert.True(PackCompat.Split("@scope/dsh-thing@1.2.3-rc.1", out var name, out var version));
        Assert.Equal("@scope/dsh-thing", name);
        Assert.Equal("1.2.3-rc.1", version);
        Assert.True(PackCompat.Split("plain@0.1.0", out name, out version));
        Assert.Equal("plain", name);
        Assert.Equal("0.1.0", version);
        Assert.False(PackCompat.Split("no-version", out _, out _));
        Assert.False(PackCompat.Split("pkg@^1.2.3", out _, out _));
    }

    // ---- Launcher.AllowVersion 参数 -------------------------------------

    [Fact]
    public void AllowVersionArgs_match_dsh_contract()
    {
        var args = Launcher.AllowVersionArgs("web", "dsh-find-skill@0.2.1", "0.2.1-rc.2");
        Assert.Equal(
            "plugin --profile web allow-version dsh-find-skill@0.2.1 --dsh-version 0.2.1-rc.2 --accept-risk",
            args);
    }

    // ---- MarkInstanceStatus：只改 status，保住 TS 写的未知字段 -------------

    [Fact]
    public void MarkInstanceStatus_preserves_unknown_ts_fields()
    {
        var root = Path.Combine(Path.GetTempPath(), "pad-compat-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var launcher = new Launcher(root);
        var dir = launcher.InstanceDir("x");
        var home = Path.Combine(dir, "home");
        var workspace = Path.Combine(dir, "workspace");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(Path.Combine(home, "profiles", "web"));
        var record = $$"""
            {
              "schema": "pack-agent.launcher.instance/v2",
              "id": "x",
              "name": "x",
              "status": "import-failed",
              "home": {{JsonSerializer.Serialize(home)}},
              "workspace": { "kind": "owned", "path": {{JsonSerializer.Serialize(workspace)}} },
              "dsh": { "version": "0.2.1-rc.2" },
              "packs": { "allowSet": "default" },
              "plugins": { "disabled": ["a"] },
              "profile": { "name": "web" }
            }
            """;
        var file = Path.Combine(dir, "instance.json");
        File.WriteAllText(file, record, new UTF8Encoding(false));

        launcher.MarkInstanceStatus("x", "ready");

        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var rootEl = doc.RootElement;
        Assert.Equal("ready", rootEl.GetProperty("status").GetString());
        // TS 写、C# 模型不认识的字段必须原样保留（P2 的纪律：不要 C# SaveInstance）。
        Assert.Equal("default", rootEl.GetProperty("packs").GetProperty("allowSet").GetString());
        Assert.Equal("a", rootEl.GetProperty("plugins").GetProperty("disabled")[0].GetString());
        Assert.Equal("web", rootEl.GetProperty("profile").GetProperty("name").GetString());
        Assert.Equal("x", rootEl.GetProperty("id").GetString());

        Assert.Throws<PadError>(() => launcher.MarkInstanceStatus("nope", "ready"));
    }
}
