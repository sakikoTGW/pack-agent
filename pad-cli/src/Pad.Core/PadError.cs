namespace Pad.Core;

/// <summary>
/// A diagnostic in rustc's shape: a code that never changes meaning, one English
/// headline, the location it applies to, and help lines that are commands the user
/// can actually run. The UI renders these instead of raw exception text.
/// </summary>
public sealed class PadError(
    string code,
    string headline,
    string location,
    string? detail = null,
    string[]? help = null) : Exception(headline)
{
    public string Code { get; } = code;
    public string Headline { get; } = headline;
    public string Location { get; } = location;
    public string? Detail { get; } = detail;
    public string[] Help { get; } = help ?? [];

    public string Render()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"error[{Code}]: {Headline}");
        sb.AppendLine($" --> {Location}");
        if (!string.IsNullOrWhiteSpace(Detail))
        {
            sb.AppendLine("  |");
            foreach (var line in Detail.Split('\n'))
                sb.AppendLine("  | " + line.TrimEnd('\r'));
            sb.AppendLine("  |");
        }
        foreach (var h in Help) sb.AppendLine("  = help: " + h);
        return sb.ToString().TrimEnd();
    }

    public static string Describe(Exception ex) =>
        ex is PadError pad ? pad.Render() : ex.Message;
}
