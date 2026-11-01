using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace Pad.Core;

/// <summary>Durable local FIFO queue for explicit, one-attempt headless tasks.</summary>
public static class TaskQueue
{
    sealed class Entry
    {
        public string Id { get; set; } = "";
        public string Instance { get; set; } = "";
        public string Status { get; set; } = "queued";
        public string CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
        public string UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
        public int Attempts { get; set; }
        public int TimeoutSeconds { get; set; } = 120;
        public string Transport { get; set; } = "local";
        public string? Endpoint { get; set; }
        public string? Thread { get; set; }
        public DateTimeOffset? AvailableAt { get; set; }
        public JsonElement? Result { get; set; }
    }
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    static string Dir(Launcher l) => Path.Combine(l.Root, "headless-queue");
    static string JobDir(Launcher l, string id) => Path.Combine(Dir(l), id);
    static bool ValidId(string id) => Guid.TryParseExact(id, "N", out _);
    static string? Flag(string[] a, string name) { var i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
    static FileStream Lock(string path, int waitMs = 5000)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var until = DateTime.UtcNow.AddMilliseconds(waitMs);
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < until) { Thread.Sleep(40); }
        }
    }
    static FileStream StateLock(Launcher l) => Lock(Path.Combine(Dir(l), ".state.lock"));
    static Entry? Read(string path) => File.Exists(path) ? JsonSerializer.Deserialize<Entry>(File.ReadAllText(path), Json) : null;
    static void Save(string path, Entry entry)
    {
        entry.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(entry, Json), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }
    static IEnumerable<string> Jobs(Launcher l) => Directory.EnumerateDirectories(Dir(l)).Where(p => ValidId(Path.GetFileName(p)));
    static void Emit(Launcher l, string path, object value, string? input = null)
    {
        var output = Path.GetFullPath(path);
        if (string.Equals(output, Path.GetFullPath(l.GlobalCredentialsPath), StringComparison.OrdinalIgnoreCase)
            || output.StartsWith(Path.GetFullPath(Dir(l)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || (input is not null && string.Equals(output, Path.GetFullPath(input), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Output path cannot be the input, credentials or queue storage.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(value, Json), new UTF8Encoding(false));
    }

    public static async Task<int> Run(Launcher l, string command, string[] args)
    {
        try
        {
            if (command == "submit")
            {
                var input = Flag(args, "--input"); var output = Flag(args, "--out");
                if (args.Length == 0 || input is null || output is null) throw new InvalidOperationException("submit <instance> --input <file> --out <file>");
                var inst = l.Instance(args[0]) ?? throw new InvalidOperationException("PAD instance not found.");
                var timeout = Flag(args, "--timeout") ?? "120";
                if (!int.TryParse(timeout, out var seconds) || seconds < 1 || seconds > 3600) throw new InvalidOperationException("Timeout must be between 1 and 3600 seconds.");
                var text = await File.ReadAllTextAsync(input, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Input file is empty.");
                var outputFull = Path.GetFullPath(output);
                var protectedPaths = new[] { input, l.GlobalCredentialsPath, CredentialsGate.YamlPath(l, inst, l.Settings()), Path.Combine(inst.Home, ".credentials.yaml") };
                if (outputFull.StartsWith(Path.GetFullPath(Dir(l)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || protectedPaths.Any(p => string.Equals(outputFull, Path.GetFullPath(p), StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Output path cannot be the input, credentials or queue storage.");
                var entry = new Entry { Id = Guid.NewGuid().ToString("N"), Instance = inst.Id, TimeoutSeconds = seconds };
                using (StateLock(l))
                {
                    var staging = Path.Combine(Dir(l), ".new-" + entry.Id);
                    Directory.CreateDirectory(staging);
                    File.WriteAllText(Path.Combine(staging, "input.txt"), text, new UTF8Encoding(false));
                    Save(Path.Combine(staging, "job.json"), entry);
                    Emit(l, output, entry, input);
                    Directory.Move(staging, JobDir(l, entry.Id));
                }
                return 0;
            }
            if (command == "submit-mesh")
            {
                var input = Flag(args, "--input"); var output = Flag(args, "--out"); var endpoint = Flag(args, "--url");
                if (input is null || output is null || endpoint is null) throw new InvalidOperationException("submit-mesh --url <peer-origin> --input <file> --out <file> [--thread <name>]");
                if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0)
                    throw new InvalidOperationException("Mesh URL must be an HTTP(S) origin without credentials, path, query or fragment.");
                var outputFull = Path.GetFullPath(output);
                if (string.Equals(outputFull, Path.GetFullPath(input), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(outputFull, Path.GetFullPath(l.GlobalCredentialsPath), StringComparison.OrdinalIgnoreCase)
                    || outputFull.StartsWith(Path.GetFullPath(Dir(l)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Output path cannot be the input, credentials or queue storage.");
                var text = await File.ReadAllTextAsync(input, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Input file is empty.");
                var entry = new Entry { Id = Guid.NewGuid().ToString("N"), Transport = "mesh", Endpoint = uri.GetLeftPart(UriPartial.Authority), Thread = Flag(args, "--thread") };
                using (StateLock(l))
                {
                    var staging = Path.Combine(Dir(l), ".new-" + entry.Id); Directory.CreateDirectory(staging);
                    File.WriteAllText(Path.Combine(staging, "input.txt"), text, new UTF8Encoding(false));
                    Save(Path.Combine(staging, "job.json"), entry); Emit(l, output, entry, input); Directory.Move(staging, JobDir(l, entry.Id));
                }
                return 0;
            }
            if (command == "list")
            {
                var output = Flag(args, "--out") ?? throw new InvalidOperationException("list --out <file>");
                List<Entry> entries;
                using (StateLock(l)) entries = Jobs(l).Select(d => Read(Path.Combine(d, "job.json"))).Where(e => e is not null).Cast<Entry>().OrderBy(e => e.CreatedAt).ToList();
                Emit(l, output, entries);
                return 0;
            }
            if (command is "status" or "cancel" or "retry")
            {
                var output = Flag(args, "--out");
                if (args.Length == 0 || !ValidId(args[0]) || output is null) throw new InvalidOperationException(command + " <id> --out <file>");
                Entry entry;
                using (StateLock(l))
                {
                    var file = Path.Combine(JobDir(l, args[0]), "job.json");
                    entry = Read(file) ?? throw new InvalidOperationException("Queue task not found.");
                    if (command == "cancel")
                    {
                        if (entry.Status != "queued") throw new InvalidOperationException("Only queued tasks can be cancelled.");
                        entry.Status = "cancelled"; Save(file, entry);
                    }
                    if (command == "retry")
                    {
                        if (entry.Transport == "mesh") throw new InvalidOperationException("Inspect the remote peer before submitting a new Mesh task; remote delivery may already have happened.");
                        if (entry.Status is not ("failed" or "timeout" or "interrupted")) throw new InvalidOperationException("Only failed, timed out or interrupted tasks can be retried.");
                        entry.Status = "queued"; entry.Result = null; Save(file, entry);
                    }
                }
                Emit(l, output, entry);
                return 0;
            }
            if (command == "worker")
            {
                Directory.CreateDirectory(Dir(l));
                using var owner = Lock(Path.Combine(Dir(l), ".worker.lock"), 0);
                using (StateLock(l))
                    foreach (var d in Jobs(l))
                    {
                        var path = Path.Combine(d, "job.json"); var old = Read(path);
                        if (old?.Status == "running")
                        {
                            old.Status = "interrupted";
                            var result = Path.Combine(d, $"result-{old.Attempts}.json");
                            try
                            {
                                using var document = JsonDocument.Parse(File.ReadAllText(result, Encoding.UTF8));
                                var state = document.RootElement.GetProperty("status").GetString();
                                if (state is "success" or "failed" or "timeout") { old.Status = state; old.Result = document.RootElement.Clone(); }
                            }
                            catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { }
                            Save(path, old);
                        }
                    }
                var once = args.Contains("--once", StringComparer.Ordinal);
                while (true)
                {
                    Entry? next = null; string? path = null;
                    using (StateLock(l))
                    {
                        foreach (var d in Jobs(l))
                        {
                            var candidate = Path.Combine(d, "job.json"); var e = Read(candidate);
                            if (e?.Status == "queued" && (e.AvailableAt is null || e.AvailableAt <= DateTimeOffset.UtcNow) && (next is null || string.CompareOrdinal(e.CreatedAt, next.CreatedAt) < 0)) { next = e; path = candidate; }
                        }
                        if (next is not null) { next.Status = "running"; next.Attempts++; Save(path!, next); }
                    }
                    if (next is null) { if (once) return 0; await Task.Delay(1000); continue; }
                    var job = JobDir(l, next.Id); var resultPath = Path.Combine(job, $"result-{next.Attempts}.json");
                    try
                    {
                        if (next.Transport == "mesh")
                        {
                            // Never follow redirects with a pairing token. A lost response is ambiguous:
                            // the peer may already have started work, so it must not be auto-retried.
                            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
                            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
                            bool online;
                            try
                            {
                                using var healthTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                                using var health = await client.GetAsync(new Uri(new Uri(next.Endpoint!), "/mesh/v1/health"), healthTimeout.Token);
                                using var info = JsonDocument.Parse(await health.Content.ReadAsStringAsync());
                                online = health.IsSuccessStatusCode && info.RootElement.TryGetProperty("app", out var app) && app.GetString() == "dsh-mesh";
                            }
                            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException) { online = false; }
                            if (!online)
                            {
                                // No task POST was attempted: waiting and retrying discovery is safe.
                                next.Status = "queued"; next.AvailableAt = DateTimeOffset.UtcNow.AddSeconds(30);
                                next.Result = JsonSerializer.SerializeToElement(new { status = "waiting_for_peer" }, Json);
                                using (StateLock(l)) Save(path!, next);
                                continue;
                            }
                            next.AvailableAt = null;
                            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(next.Endpoint!), "/mesh/v1/task"));
                            var token = Environment.GetEnvironmentVariable("PAD_MESH_TOKEN");
                            if (!string.IsNullOrEmpty(token)) request.Headers.TryAddWithoutValidation("X-Dsh-Mesh-Token", token);
                            var payload = JsonSerializer.Serialize(new { id = "pad-" + next.Id, from = "PAD", fromMachine = Environment.MachineName, task = await File.ReadAllTextAsync(Path.Combine(job, "input.txt"), Encoding.UTF8), thread = next.Thread ?? "" });
                            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                            using var response = await client.SendAsync(request);
                            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                            if (response.IsSuccessStatusCode && doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True && doc.RootElement.TryGetProperty("id", out var id) && id.GetString() == "pad-" + next.Id)
                            {
                                next.Status = "accepted";
                                var duplicate = doc.RootElement.TryGetProperty("duplicate", out var dup) && dup.ValueKind == JsonValueKind.True;
                                next.Result = JsonSerializer.SerializeToElement(new { status = "accepted", peerTaskId = id.GetString(), duplicate }, Json);
                            }
                            else { next.Status = "delivery_unknown"; next.Result = JsonSerializer.SerializeToElement(new { status = "delivery_unknown", error = "Peer did not confirm the task; inspect peer before retrying." }, Json); }
                        }
                        else
                        {
                            if (File.Exists(resultPath)) File.Delete(resultPath);
                            await HeadlessTask.Run(l, "run", [next.Instance, "--input", Path.Combine(job, "input.txt"), "--out", resultPath, "--timeout", next.TimeoutSeconds.ToString()]);
                            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath, Encoding.UTF8));
                            next.Result = doc.RootElement.Clone();
                            next.Status = doc.RootElement.GetProperty("status").GetString() ?? "failed";
                        }
                    }
                    catch (Exception ex)
                    {
                        next.Status = next.Transport == "mesh" ? "delivery_unknown" : "failed";
                        next.Result = JsonSerializer.SerializeToElement(new { status = next.Status, error = next.Transport == "mesh" ? "Delivery uncertain; inspect peer before retrying." : ex.Message }, Json);
                    }
                    using (StateLock(l)) Save(path!, next);
                }
            }
            throw new InvalidOperationException("queue submit|list|status|cancel|retry|worker");
        }
        catch (Exception ex) { Cli.Print("queue: " + ex.Message); return 1; }
    }
}
