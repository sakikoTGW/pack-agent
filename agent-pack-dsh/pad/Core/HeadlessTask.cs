using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace Pad.Core;

/// <summary>One bounded DSH headless invocation using an existing PAD instance.</summary>
public static class HeadlessTask
{
    sealed record Result(string status, int? exitCode, string output, string error, long elapsedMs);
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static async Task<int> Run(Launcher launcher, string command, string[] args)
    {
        string? Flag(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var outputPath = Flag("--out");
        var inputPath = Flag("--input");
        if (command != "run" || args.Length == 0 || args[0].StartsWith("--") || inputPath is null || outputPath is null)
        {
            Cli.Print("usage: pad cli task run <instance> --input <file> --out <file> [--timeout <seconds>]");
            return 2;
        }
        var clock = Stopwatch.StartNew();
        string secret = "", patch = "";
        Result result;
        try
        {
            var input = Path.GetFullPath(inputPath);
            var output = Path.GetFullPath(outputPath);
            if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
            {
                Cli.Print("Input and output paths must differ.");
                return 2;
            }
            var timeoutText = Flag("--timeout") ?? "120";
            if (!int.TryParse(timeoutText, out var seconds) || seconds < 1 || seconds > 3600)
                throw new InvalidOperationException("Timeout must be between 1 and 3600 seconds.");
            var inst = launcher.Instance(args[0]) ?? throw new InvalidOperationException("PAD instance not found.");
            var settings = launcher.Settings();
            var probe = CredentialsGate.Inspect(launcher, inst, settings);
            var protectedPaths = new[] { probe.Location, launcher.GlobalCredentialsPath, Path.Combine(inst.Home, ".credentials.yaml") };
            if (protectedPaths.Any(path => string.Equals(output, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)))
            {
                Cli.Print("Output path cannot be the credentials file.");
                return 2;
            }
            if (!probe.Configured) throw new InvalidOperationException("DeepSeek API key is not configured for this instance.");
            secret = CredentialsFile.ReadDeepseekKey(File.ReadAllText(probe.Location));
            if (string.IsNullOrWhiteSpace(secret) || secret.Any(char.IsWhiteSpace) || secret.Any(char.IsControl))
                throw new InvalidOperationException("DeepSeek API key has an invalid format; check PAD Settings → API Key.");
            var bin = launcher.DshBin(inst.Dsh.Version);
            if (!File.Exists(bin)) throw new InvalidOperationException("The instance's pinned DSH CLI was not found.");
            if (!Directory.Exists(inst.Workspace.Path)) throw new InvalidOperationException("The instance workspace does not exist.");
            var prompt = await File.ReadAllTextAsync(input, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(prompt)) throw new InvalidOperationException("Input file is empty.");
            patch = Path.Combine(Path.GetTempPath(), $"pad-headless-{Guid.NewGuid():N}.yml");
            await File.WriteAllTextAsync(patch, "- id: agent-default-model\n  name: '@deepseek-ai/dsh-agent-default-model'\n  config:\n    provider: deepseek-official\n    model: deepseek-flash\n", new UTF8Encoding(false));
            var start = new ProcessStartInfo
            {
                FileName = launcher.ResolveLaunchNode(inst, settings) ?? Proc.Which("node") ?? "node",
                WorkingDirectory = inst.Workspace.Path,
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            start.ArgumentList.Add(bin); start.ArgumentList.Add("headless"); start.ArgumentList.Add("--patch"); start.ArgumentList.Add(patch); start.ArgumentList.Add("-");
            start.Environment["DSH_HOME"] = inst.Home;
            start.Environment["DEEPSEEK_API_KEY"] = secret;
            start.Environment["DSH_TELEMETRY_DISABLED"] = "1";
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start DSH.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            try
            {
                await process.StandardInput.WriteAsync(prompt.AsMemory(), timeout.Token);
                process.StandardInput.Close();
                await process.WaitForExitAsync(timeout.Token);
                var text = await stdout;
                var diagnostic = await stderr;
                result = new Result(process.ExitCode == 0 ? "success" : "failed", process.ExitCode,
                    text, process.ExitCode == 0 ? "" : diagnostic, clock.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync();
                result = new Result("timeout", null, await stdout, await stderr, clock.ElapsedMilliseconds);
            }
            catch
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw;
            }
        }
        catch (Exception ex)
        {
            result = new Result("failed", null, "", ex.Message, clock.ElapsedMilliseconds);
        }
        finally
        {
            if (patch.Length > 0) try { File.Delete(patch); } catch (IOException) { }
        }
        if (secret.Length > 0) result = result with { output = result.output.Replace(secret, "[REDACTED]"), error = result.error.Replace(secret, "[REDACTED]") };
        var outFull = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outFull)!);
        await File.WriteAllTextAsync(outFull, JsonSerializer.Serialize(result, Json), new UTF8Encoding(false));
        return result.status == "success" ? 0 : 1;
    }
}
