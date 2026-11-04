using System.Security.Cryptography;
using System.Text;

namespace Pad.Core;

/// <summary>
/// Identity of a DSH: the facts that make it *this* one and not another. Two entries with the
/// same fingerprint are the same thing wearing two names, and PAD must say so instead of
/// letting them look distinct.
///
/// The material is deliberately boring — home, workspace, version, declared bundles — because
/// a fingerprint is only useful if it stays stable while names and icons change.
/// </summary>
public static class Fingerprint
{
    public sealed record Parts(
        string Home,
        string Workspace,
        string Version,
        IReadOnlyList<string> Bundles,
        string Code);

    public static Parts Of(Instance inst)
    {
        var home = Norm(inst.Home);
        var work = Norm(inst.Workspace?.Path);
        var version = (inst.Dsh?.Version ?? "").Trim();
        var bundles = Carriers.Bundles(home)
            .OrderBy(b => b, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var material = string.Join("|", home, work, version, string.Join(",", bundles));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var code = Convert.ToHexString(hash)[..8].ToLowerInvariant();
        return new Parts(home, work, version, bundles, code);
    }

    /// <summary>Same path means the same place; case and trailing separators do not matter.</summary>
    static string Norm(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? ""
            : path!.TrimEnd('\\', '/').ToLowerInvariant();
}
