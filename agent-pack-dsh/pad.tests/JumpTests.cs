using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pad.Core;
using Pad.Core.Jump;
using Xunit;

namespace Pad.Tests;

/// <summary>
/// M1 acceptance: every PA13x path is reachable and reproducible, registry layers merge
/// deterministically, and the browser-session cookie PAD mints is byte-identical to DSH's.
/// </summary>
public sealed class JumpTests
{
    // ---- fixtures --------------------------------------------------------

    static (Launcher Launcher, Instance Instance, string Home) TempInstance(string version = "0.2.1-rc.2")
    {
        var root = Path.Combine(Path.GetTempPath(), "pad-jump-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var launcher = new Launcher(root);
        var home = Path.Combine(root, "instances", "x", "home");
        Directory.CreateDirectory(home);
        var instance = new Instance
        {
            Id = "x",
            Name = "x",
            Home = home,
            Dsh = new InstanceDsh { Version = version },
        };
        return (launcher, instance, home);
    }

    static void WriteAd(string home)
    {
        var ad = new
        {
            schema = "pack-agent.pad-gateway/v1",
            url = "http://127.0.0.1:9/api",
            token = new string('a', 64),
            pid = 1,
            capabilities = new { jump = true, api = false },
            webUrl = "http://127.0.0.1:9",
        };
        File.WriteAllText(Path.Combine(home, "pad-gateway.json"), JsonSerializer.Serialize(ad));
    }

    static void WriteLedger(string home, int pid, string url)
    {
        var dir = Path.Combine(home, ".pad");
        Directory.CreateDirectory(dir);
        var ledger = new
        {
            version = 1,
            nodes = new[]
            {
                new
                {
                    profile = "web",
                    surface = "web",
                    port = 3080,
                    url,
                    pid,
                    startedAt = DateTimeOffset.Now.ToString("o"),
                    logPath = "",
                    verified = true,
                },
            },
        };
        File.WriteAllText(Path.Combine(dir, "nodes.json"), JsonSerializer.Serialize(ledger));
    }

    static void WriteSurface(string dir, string id, string kind, params string[] transports)
    {
        Directory.CreateDirectory(dir);
        var body = new
        {
            schema = "pad.surface/v1",
            id,
            label = id,
            kind,
            transports = transports.Select(t => new { type = t }).ToArray(),
        };
        File.WriteAllText(Path.Combine(dir, id + ".json"), JsonSerializer.Serialize(body));
    }

    // ---- SessionRef ------------------------------------------------------

    [Theory]
    [InlineData("session-02d4a5ad-9792-4569-a2de-d598e85d754a", "session-02d4a5ad-9792-4569-a2de-d598e85d754a")]
    [InlineData("02d4a5ad-9792-4569-a2de-d598e85d754a", "session-02d4a5ad-9792-4569-a2de-d598e85d754a")]
    [InlineData("02d4a5ad-9792-4569-a2de-d598e85d754a.json", "session-02d4a5ad-9792-4569-a2de-d598e85d754a")]
    public void SessionRef_NormalizesThreeForms(string raw, string expected) =>
        Assert.Equal(expected, SessionRef.Normalize(raw));

    [Theory]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("session-")]
    [InlineData("session-xyz")]
    public void SessionRef_RejectsGarbage(string raw) =>
        Assert.Null(SessionRef.Normalize(raw));

    // ---- surface registry (L0/L1/L2 merge) -------------------------------

    [Fact]
    public void Registry_BuiltinsCarryTransports()
    {
        var descriptors = SurfaceRegistry.Load(new Launcher(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))), null);
        var web = Assert.Single(descriptors, d => d.Id == "pad-web");
        Assert.True(web.HasTransport("gateway-event"));
        Assert.True(web.HasTransport("url-query"));
        Assert.True(web.HasTransport("focus-clipboard"));
        Assert.Equal("builtin", web.Source);
    }

    [Fact]
    public void Registry_MergesLayersByTransportType()
    {
        var (launcher, _, home) = TempInstance();
        WriteSurface(Path.Combine(launcher.Root, "library", "surfaces"), "pad-web", "web", "gateway-event", "custom-l1");
        WriteSurface(Path.Combine(home, ".pad", "surfaces"), "pad-web", "web", "url-query");

        var web = Assert.Single(SurfaceRegistry.Load(launcher, home), d => d.Id == "pad-web");

        Assert.Null(web.Error);
        Assert.Equal("builtin+user+instance", web.Source);
        Assert.Equal(new[] { "builtin", "user", "instance" }, web.Layers);
        // builtin transports survive; L1 adds one; L2 overrides url-query in place.
        Assert.True(web.HasTransport("gateway-event"));
        Assert.True(web.HasTransport("url-query"));
        Assert.True(web.HasTransport("custom-l1"));
        Assert.True(web.HasTransport("focus-clipboard"));
        Assert.Equal(web.Transports.Count, web.Transports.Select(t => t.Type).Distinct().Count());
        Assert.NotEmpty(web.MergeNotes);
    }

    [Fact]
    public void Registry_KindConflictMarksDescriptorUnusable()
    {
        var (launcher, _, home) = TempInstance();
        WriteSurface(Path.Combine(home, ".pad", "surfaces"), "pad-web", "app", "focus-clipboard");

        var web = Assert.Single(SurfaceRegistry.Load(launcher, home), d => d.Id == "pad-web");

        Assert.NotNull(web.Error);
        Assert.Contains("kind", web.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("instance", web.Layers);
    }

    [Fact]
    public void Registry_InvalidFileIsReportedNotFatal()
    {
        var (launcher, _, home) = TempInstance();
        var dir = Path.Combine(home, ".pad", "surfaces");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "broken.json"), """{"schema":"wrong","id":"broken"}""");

        var descriptors = SurfaceRegistry.Load(launcher, home);
        var broken = Assert.Single(descriptors, d => d.Id == "broken");
        Assert.NotNull(broken.Error);
        Assert.Contains("schema", broken.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(descriptors, d => d.Id == "pad-web"); // builtins still present
    }

    // ---- planner / PA13x -------------------------------------------------

    [Fact]
    public void Plan_InvalidId_ThrowsPA132()
    {
        var (launcher, instance, _) = TempInstance();
        var error = Assert.Throws<PadError>(() => JumpPlanner.Plan(launcher, instance, "not-a-session"));
        Assert.Equal("PA132", error.Code);
    }

    [Fact]
    public void Plan_FilteredToNothing_PA130()
    {
        var (launcher, instance, _) = TempInstance();
        var plan = JumpPlanner.Plan(launcher, instance, SessionId, surfaceFilter: "no-such-surface");
        Assert.Null(plan.Chosen);
        Assert.Equal("PA130", plan.Pa);
    }

    [Fact]
    public void Plan_NoGateway_RejectsPA136_AndFallsBackToTui()
    {
        var (launcher, instance, _) = TempInstance();
        var plan = JumpPlanner.Plan(launcher, instance, SessionId);

        Assert.Contains(plan.Rejected, r => r.Code == "PA136"); // gateway-event without ad
        Assert.Contains(plan.Rejected, r => r.Code == "PA133"); // url-query without ledger
        Assert.NotNull(plan.Chosen);
        Assert.Equal("pad-tui", plan.Chosen!.Surface);
        Assert.Equal("launch-env", plan.Chosen.Transport);
        Assert.Equal(420, plan.Chosen.Score);
        Assert.False(plan.Degraded); // launch-env is executable
        Assert.Equal(SessionId, plan.Clipboard);
    }

    [Fact]
    public void Plan_WithGatewayAd_ChoosesR0()
    {
        var (launcher, instance, home) = TempInstance();
        WriteAd(home);

        var plan = JumpPlanner.Plan(launcher, instance, SessionId);

        Assert.NotNull(plan.Chosen);
        Assert.Equal("pad-web", plan.Chosen!.Surface);
        Assert.Equal("gateway-event", plan.Chosen.Transport);
        Assert.Equal(1500, plan.Chosen.Score);
        Assert.Null(plan.Pa);
        Assert.False(plan.Degraded);
        Assert.DoesNotContain(plan.Rejected, r => r.Code == "PA136");
    }

    [Fact]
    public void Plan_WithRunningWebLedger_ChoosesR1()
    {
        var (launcher, instance, home) = TempInstance();
        WriteLedger(home, Environment.ProcessId, "http://127.0.0.1:3080/?token=abcDEF1234567890");

        var plan = JumpPlanner.Plan(launcher, instance, SessionId);

        Assert.NotNull(plan.Chosen);
        Assert.Equal("url-query", plan.Chosen!.Transport);
        Assert.Equal(800, plan.Chosen.Score);
        Assert.DoesNotContain(plan.Rejected, r => r.Code == "PA133");
    }

    [Fact]
    public void Plan_LaunchArgsOnlySurface_IsEligible()
    {
        var (launcher, instance, home) = TempInstance();
        WriteSurface(Path.Combine(home, ".pad", "surfaces"), "tui-args", "tui", "launch-args");

        var plan = JumpPlanner.Plan(launcher, instance, SessionId, surfaceFilter: "tui-args");

        Assert.NotNull(plan.Chosen);
        Assert.Equal("tui-args", plan.Chosen!.Surface);
        Assert.Equal("launch-args", plan.Chosen.Transport);
        Assert.Equal("eligible", plan.Chosen.State);
        Assert.Equal(400, plan.Chosen.Score);
    }

    [Fact]
    public void Plan_IsDeterministic()
    {
        var (launcher, instance, home) = TempInstance();
        WriteAd(home);

        var first = JumpPlanner.Plan(launcher, instance, SessionId);
        var second = JumpPlanner.Plan(launcher, instance, SessionId);

        Assert.Equal(first.Chosen!.Surface, second.Chosen!.Surface);
        Assert.Equal(first.Chosen.Transport, second.Chosen.Transport);
        Assert.Equal(first.Chosen.Score, second.Chosen.Score);
        Assert.Equal(
            first.Candidates.Select(c => $"{c.Surface}/{c.Transport}/{c.Rank}/{c.Score}/{c.State}"),
            second.Candidates.Select(c => $"{c.Surface}/{c.Transport}/{c.Rank}/{c.Score}/{c.State}"));
    }

    [Fact]
    public void Plan_AllDiagnosticsArePA13x_WithReasons()
    {
        var (launcher, instance, home) = TempInstance();
        WriteLedger(home, int.MaxValue, "http://127.0.0.1:1/"); // dead pid → PA133

        var plan = JumpPlanner.Plan(launcher, instance, SessionId);

        Assert.All(plan.Rejected, r =>
        {
            Assert.NotNull(r.Code);
            Assert.StartsWith("PA13", r.Code, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(r.Why));
        });
        if (plan.Pa is not null) Assert.Matches("^PA13[0-9]$", plan.Pa);
    }

    // ---- 0.2.x browser-session cookie -----------------------------------

    [Fact]
    public void Cookie_MintedValueMatchesDshFormatAndSignature()
    {
        var (_, _, home) = TempInstance();
        var secret = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(Path.Combine(home, ".credentials.yaml"), $"""
            version: 1
            refs:
              DEEPSEEK_API_KEY: "sk-test"
            records:
              client-connection/browser-session:
                kind: grant
                payload:
                  version: 1
                  secret: {Base64Url(secret)}
            """);

        var cookie = GatewayAuth.MintBrowserCookie(home, "http://127.0.0.1:49998");
        Assert.NotNull(cookie);

        var expectedName = "dsh-auth-" + Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes("127.0.0.1:49998")));
        var at = cookie!.IndexOf('=');
        Assert.Equal(expectedName, cookie[..at]);

        var parts = cookie[(at + 1)..].Split('.');
        Assert.Equal(3, parts.Length);
        Assert.Equal("v1", parts[0]);

        using var hmac = new HMACSHA256(secret);
        Assert.Equal(Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(parts[1]))), parts[2]);

        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlDecode(parts[1])));
        Assert.Equal(1, doc.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("127.0.0.1:49998", doc.RootElement.GetProperty("authority").GetString());
    }

    [Fact]
    public void Cookie_IsNullWithoutCredentialsFile()
    {
        var (_, _, home) = TempInstance();
        Assert.Null(GatewayAuth.MintBrowserCookie(home, "http://127.0.0.1:49998"));
    }

    // ---- credentials -----------------------------------------------------

    [Fact]
    public void Credentials_StripRefPrefixes_HandlesTopLevelAndIndented()
    {
        var topLevel = "DEEPSEEK_API_KEY: \"REF: sk-abc\"";
        Assert.Equal("DEEPSEEK_API_KEY: \"sk-abc\"", CredentialsFile.StripRefPrefixes(topLevel));

        var indented = "version: 1\nrefs:\n  DEEPSEEK_API_KEY: \"REF: sk-abc\"\nrecords: {}\n";
        var stripped = CredentialsFile.StripRefPrefixes(indented);
        Assert.Contains("DEEPSEEK_API_KEY: \"sk-abc\"", stripped);
        Assert.DoesNotContain("REF:", stripped);
        Assert.Contains("records: {}", stripped);
    }

    [Fact]
    public void Credentials_HarnessReadsRaw_OnlyFor02x()
    {
        Assert.True(CredentialsFile.HarnessReadsRaw("0.2.1-rc.2"));
        Assert.True(CredentialsFile.HarnessReadsRaw("0.2.0"));
        Assert.False(CredentialsFile.HarnessReadsRaw("0.1.0-rc.7"));
        Assert.False(CredentialsFile.HarnessReadsRaw(""));
        Assert.False(CredentialsFile.HarnessReadsRaw(null));
    }

    // ---- pnpm .modules.yaml ---------------------------------------------

    [Fact]
    public void PatchVirtualStoreDir_HandlesPnpm11JsonShape()
    {
        var json = """
            {
              "packageManager": "pnpm@11.28.5",
              "virtualStoreDir": "C:\\\\old\\\\.pnpm",
              "virtualStoreDirMaxLength": 60
            }
            """;
        var patched = LaunchPolicy.PatchVirtualStoreDir(json, @"E:\kit\profile\node_modules\.pnpm");

        Assert.StartsWith("{", patched.TrimStart());
        Assert.EndsWith("}", patched.TrimEnd());
        Assert.Single(Regex.Matches(patched, "\"virtualStoreDir\"\\s*:"));
        Assert.Contains(@"E:\\kit\\profile\\node_modules\\.pnpm", patched); // JSON-escaped
        using var doc = JsonDocument.Parse(patched); // must stay valid JSON
        Assert.Equal(@"E:\kit\profile\node_modules\.pnpm", doc.RootElement.GetProperty("virtualStoreDir").GetString());
    }

    [Fact]
    public void PatchVirtualStoreDir_AddsKeyToJsonWithoutOne()
    {
        var json = """
            {
              "packageManager": "pnpm@11.28.5",
              "virtualStoreDirMaxLength": 60
            }
            """;
        var patched = LaunchPolicy.PatchVirtualStoreDir(json, "/kit/.pnpm");
        using var doc = JsonDocument.Parse(patched);
        Assert.Equal("/kit/.pnpm", doc.RootElement.GetProperty("virtualStoreDir").GetString());
        Assert.Equal(60, doc.RootElement.GetProperty("virtualStoreDirMaxLength").GetInt32());
    }

    [Fact]
    public void PatchVirtualStoreDir_KeepsLegacyYamlShape()
    {
        var yaml = "packageManager: pnpm@10\nvirtualStoreDir: C:\\\\old\\\\.pnpm\nvirtualStoreDirMaxLength: 60\n";
        var patched = LaunchPolicy.PatchVirtualStoreDir(yaml, "/kit/.pnpm");
        Assert.Contains("virtualStoreDir: /kit/.pnpm", patched);
        Assert.DoesNotContain("C:\\\\old", patched);
        Assert.DoesNotContain("}", patched);
    }

    // ---- install recipes -------------------------------------------------

    [Fact]
    public void Recipe_WebInstallsConsumerPlugin()
    {
        var specs = Cli.Recipe("web", "0.2.1-rc.2");
        Assert.Contains("@deepseek-ai/dsh-web-app@0.2.1-rc.2", specs);
        Assert.Contains("@sakikotgw/pad-session-jump", specs);
    }

    [Fact]
    public void Recipe_TuiPicksCarrierByDshVersion()
    {
        Assert.Contains("@sakikotgw/pad-gateway-jump", Cli.Recipe("tui", "0.2.1-rc.2"));
        Assert.Contains("@sakikotgw/pad-gateway", Cli.Recipe("tui", "0.1.0-rc.7"));
    }

    // ---- helpers ---------------------------------------------------------

    const string SessionId = "session-02d4a5ad-9792-4569-a2de-d598e85d754a";

    static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static byte[] Base64UrlDecode(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            _ => padded,
        };
        return Convert.FromBase64String(padded);
    }
}
