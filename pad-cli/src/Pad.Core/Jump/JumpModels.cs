using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Pad.Core.Jump;

/// <summary>
/// One session address: a DSH home plus the canonical session id. Surface is
/// deliberately not part of identity — the planner picks a surface later.
/// </summary>
public sealed record SessionRef(string Home, string Id, string? Workspace = null, string? Profile = null)
{
    static readonly Regex Canonical = new("^session-[A-Za-z0-9][A-Za-z0-9-]{7,63}$", RegexOptions.Compiled);
    static readonly Regex Uuid = new(
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        RegexOptions.Compiled);

    /// <summary>Accept session-&lt;uuid&gt;, a raw uuid, or a cache file name. Null when unknown.</summary>
    public static string? Normalize(string? raw)
    {
        var v = raw?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (Canonical.IsMatch(v)) return v;
        if (v.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) v = v[..^5];
        if (Uuid.IsMatch(v)) return "session-" + v;
        return null;
    }

    /// <summary>Clipboard form: canonical id (a raw uuid is normalized first).</summary>
    public static string ClipboardText(string raw) => Normalize(raw) ?? raw;
}

/// <summary>One transport a surface advertises (see docs/session-jump.md §5).</summary>
public sealed class SurfaceTransport
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("sessionParam")] public string? SessionParam { get; set; }
    [JsonPropertyName("envName")] public string? EnvName { get; set; }
    [JsonPropertyName("placement")] public string? Placement { get; set; }
}

/// <summary>A surface capability descriptor (built-in, user or instance level).</summary>
public sealed class SurfaceDescriptor
{
    [JsonPropertyName("schema")] public string Schema { get; set; } = "pad.surface/v1";
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "custom";
    [JsonPropertyName("transports")] public List<SurfaceTransport> Transports { get; set; } = [];
    [JsonIgnore] public string Source { get; set; } = "builtin";
    [JsonIgnore] public string? Error { get; set; }
    /// <summary>Layers this descriptor came from, lowest first (builtin / user / instance).</summary>
    [JsonIgnore] public List<string> Layers { get; set; } = [];
    /// <summary>Human-readable merge decisions/conflicts for <c>surface validate</c>.</summary>
    [JsonIgnore] public List<string> MergeNotes { get; set; } = [];

    public bool HasTransport(string type) =>
        Transports.Any(t => string.Equals(t.Type, type, StringComparison.OrdinalIgnoreCase));

    public static SurfaceDescriptor Builtin(string id, string label, string kind, params string[] transports) => new()
    {
        Id = id,
        Label = label,
        Kind = kind,
        Source = "builtin",
        Transports = transports.Select(t => new SurfaceTransport { Type = t }).ToList(),
    };
}

/// <summary>
/// Loads surface descriptors as three layers (design §5): L0 built-ins, L1
/// <c>&lt;dataRoot&gt;/library/surfaces/*.json</c> (user), L2 <c>&lt;home&gt;/.pad/surfaces/*.json</c>
/// (instance). Same id across layers merges instead of replacing: higher layer wins
/// label/kind, transports merge by <c>type</c> (higher layer's fields win). A kind
/// mismatch is a conflict and marks the descriptor unusable. Bad files are reported,
/// never fatal.
/// </summary>
public static class SurfaceRegistry
{
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static List<SurfaceDescriptor> Builtins() =>
    [
        SurfaceDescriptor.Builtin("pad-web", "Web（浏览器 / Edge app 窗）", "web", "gateway-event", "url-query", "focus-clipboard"),
        SurfaceDescriptor.Builtin("pad-tui", "TUI（终端）", "tui", "launch-env", "launch-args", "focus-clipboard"),
        SurfaceDescriptor.Builtin("pad-app", "官方桌面端", "app", "focus-clipboard"),
        SurfaceDescriptor.Builtin("pad-tauri", "Tauri 桌面端（单实例壳）", "tauri", "focus-clipboard"),
    ];

    /// <summary>Descriptor list with per-file parse errors kept for <c>surface validate</c>.</summary>
    public static List<SurfaceDescriptor> Load(Launcher launcher, string? home)
    {
        var user = new List<SurfaceDescriptor>();
        LoadDir(Path.Combine(launcher.Root, "library", "surfaces"), "user", user);
        var instance = new List<SurfaceDescriptor>();
        if (!string.IsNullOrWhiteSpace(home))
            LoadDir(Path.Combine(home, ".pad", "surfaces"), "instance", instance);

        var merged = new List<SurfaceDescriptor>();
        var byId = new Dictionary<string, SurfaceDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var (layer, items) in new (string Layer, List<SurfaceDescriptor> Items)[]
                 {
                     ("builtin", Builtins()),
                     ("user", user),
                     ("instance", instance),
                 })
        {
            foreach (var item in items)
            {
                item.Layers = [layer];
                if (!byId.TryGetValue(item.Id, out var target))
                {
                    byId[item.Id] = item;
                    merged.Add(item);
                    continue;
                }
                MergeInto(target, item, layer);
            }
        }

        foreach (var descriptor in merged)
        {
            descriptor.Source = descriptor.Layers.Count == 0
                ? descriptor.Source
                : string.Join("+", descriptor.Layers.Distinct(StringComparer.OrdinalIgnoreCase));
        }
        return merged;
    }

    /// <summary>Fold one higher-layer descriptor into the accumulated lower-layer one.</summary>
    static void MergeInto(SurfaceDescriptor target, SurfaceDescriptor overlay, string layer)
    {
        if (overlay.Error is not null)
        {
            target.MergeNotes.Add($"{layer}/{overlay.Id}: 文件无效（{overlay.Error}），保留低层定义");
            if (target.Error is null) target.Error = overlay.Error;
            return;
        }
        if (target.Error is not null) return;

        if (!string.IsNullOrWhiteSpace(overlay.Kind)
            && !string.Equals(overlay.Kind, target.Kind, StringComparison.OrdinalIgnoreCase))
        {
            target.Error = $"kind 冲突：{target.Source}={target.Kind} vs {layer}={overlay.Kind}";
            target.MergeNotes.Add(target.Error);
            target.Layers.Add(layer);
            return;
        }

        if (!string.IsNullOrWhiteSpace(overlay.Label)) target.Label = overlay.Label;
        if (!string.IsNullOrWhiteSpace(overlay.Kind)) target.Kind = overlay.Kind;

        foreach (var incoming in overlay.Transports)
        {
            var existing = target.Transports.FirstOrDefault(t =>
                string.Equals(t.Type, incoming.Type, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                target.Transports.Add(incoming);
                target.MergeNotes.Add($"{layer}: 增加 transport {incoming.Type}");
                continue;
            }
            if (!string.IsNullOrWhiteSpace(incoming.SessionParam)) existing.SessionParam = incoming.SessionParam;
            if (!string.IsNullOrWhiteSpace(incoming.EnvName)) existing.EnvName = incoming.EnvName;
            if (!string.IsNullOrWhiteSpace(incoming.Placement)) existing.Placement = incoming.Placement;
            target.MergeNotes.Add($"{layer}: 覆盖 transport {incoming.Type}");
        }
        target.Layers.Add(layer);
    }

    static void LoadDir(string dir, string source, List<SurfaceDescriptor> sink)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var text = File.ReadAllText(file);
                var d = JsonSerializer.Deserialize<SurfaceDescriptor>(text, Json);
                if (d is null) throw new InvalidDataException("empty descriptor");
                if (!string.Equals(d.Schema, "pad.surface/v1", StringComparison.Ordinal))
                    throw new InvalidDataException($"schema must be pad.surface/v1, got `{d.Schema}`");
                if (string.IsNullOrWhiteSpace(d.Id) || !Regex.IsMatch(d.Id, "^[a-z0-9][a-z0-9-]{1,40}$"))
                    throw new InvalidDataException("id must match ^[a-z0-9][a-z0-9-]{1,40}$");
                d.Source = source;
                d.Error = null;
                sink.Add(d);
            }
            catch (Exception ex)
            {
                sink.Add(new SurfaceDescriptor
                {
                    Id = Path.GetFileNameWithoutExtension(file),
                    Label = file,
                    Kind = "invalid",
                    Source = source,
                    Error = ex.Message,
                });
            }
        }
    }
}

/// <summary>The pad-gateway ad written by the in-instance carrier.</summary>
public sealed class GatewayAd
{
    [JsonPropertyName("schema")] public string Schema { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    [JsonPropertyName("pid")] public int Pid { get; set; }

    public bool Valid =>
        Schema == "pack-agent.pad-gateway/v1"
        && Uri.TryCreate(Url, UriKind.Absolute, out var u)
        && u.Scheme == "http"
        && (u.Host is "127.0.0.1" or "localhost")
        && Token.Length >= 16;

    public static GatewayAd? Read(string home)
    {
        try
        {
            var path = Path.Combine(home, "pad-gateway.json");
            if (!File.Exists(path)) return null;
            var ad = JsonSerializer.Deserialize<GatewayAd>(File.ReadAllText(path));
            return ad is { Valid: true } ? ad : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Small Win32 helpers for the universal fallback: focus a window + clipboard.</summary>
public static class FocusClipboard
{
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr param);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int max);

    delegate bool EnumProc(IntPtr hwnd, IntPtr param);

    /// <summary>
    /// Focus the window owned by <paramref name="pid" />; when that process has no
    /// top-level window (browser shells), fall back to title hints. Ambiguity is
    /// reported instead of guessing silently.
    /// </summary>
    public static (bool Focused, bool Ambiguous, string Detail) TryFocus(int? pid, params string[] titleHints)
    {
        // user32 只在 Windows 上有；其它平台没有"抢窗口"这回事。
        if (!OperatingSystem.IsWindows())
            return (false, false, "windows-only focus helper");

        var windows = new List<(IntPtr Hwnd, int Pid, string Title)>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            GetWindowThreadProcessId(hwnd, out var owner);
            var len = GetWindowTextLength(hwnd);
            var sb = new System.Text.StringBuilder(Math.Max(1, len + 1));
            GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();
            if (title.Length > 0) windows.Add((hwnd, owner, title));
            return true;
        }, IntPtr.Zero);

        if (pid is { } p && p > 0)
        {
            var own = windows.Where(w => w.Pid == p).ToList();
            if (own.Count == 1) return (Activate(own[0].Hwnd), false, $"pid {p}");
            if (own.Count > 1) return (Activate(own[0].Hwnd), true, $"pid {p}: {own.Count} windows");
        }

        var hints = titleHints.Where(h => !string.IsNullOrWhiteSpace(h)).ToArray();
        if (hints.Length > 0)
        {
            var matched = windows
                .Where(w => hints.Any(h => w.Title.Contains(h, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (matched.Count == 1) return (Activate(matched[0].Hwnd), false, $"title `{matched[0].Title}`");
            if (matched.Count > 1) return (Activate(matched[0].Hwnd), true, $"{matched.Count} windows match title");
        }
        return (false, false, "no matching window");
    }

    static bool Activate(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, 9);
        return SetForegroundWindow(hwnd);
    }


    /// <summary>CLI: 不写剪贴板（窗口版才需要）。</summary>
    public static void SetClipboard(string text) { }

    /// <summary>Process name for a pid, or null when it exited.</summary>
    public static string? ProcessName(int pid)
    {
        try { return Process.GetProcessById(pid).ProcessName; }
        catch { return null; }
    }
}
