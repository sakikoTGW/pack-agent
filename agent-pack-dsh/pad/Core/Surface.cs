using System.IO;

namespace Pad.Core;

/// <summary>
/// How a person sees one DSH instance. Orthogonal to the profile, which decides
/// what is installed: a surface decides the window, its endpoint, and its teardown.
/// See <c>docs/SURFACE.md</c>.
/// </summary>
public sealed record Surface(
    string Name,
    string Shows,
    bool Exclusive,
    bool NeedsEndpoint,
    string Requires)
{
    public static readonly Surface Tui = new("tui", "终端窗口", false, false, "dsh-tui + gateway");
    public static readonly Surface Web = new("web", "浏览器窗口", false, true, "dsh-web-app");
    public static readonly Surface App = new("app", "桌面端窗口", true, false, "DSH Desktop（Electron 壳，单实例）");

    public static IReadOnlyList<Surface> All { get; } = [Tui, Web, App];

    public static Surface? For(string name) =>
        All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The surface implied by a profile name, for profiles that were not created
    /// with an explicit one. `web` is the only name DSH itself reserves.
    /// </summary>
    public static Surface ForProfile(string profile) =>
        string.Equals(profile, "web", StringComparison.OrdinalIgnoreCase) ? Web : Tui;

    /// <summary>
    /// Refuse to start a second exclusive surface on a different instance: the
    /// desktop shell holds one lockfile and one fixed endpoint, so two of them
    /// cannot coexist. Returning the offending instance lets the caller say which.
    /// </summary>
    public static string? ExclusiveConflict(Surface surface, string wantInstance, IEnumerable<(string Id, Surface Surface)> running)
    {
        if (!surface.Exclusive) return null;
        foreach (var (id, s) in running)
            if (s.Exclusive && !string.Equals(id, wantInstance, StringComparison.OrdinalIgnoreCase))
                return id;
        return null;
    }
}
