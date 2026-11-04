using Pad.Core;

namespace Pad.Cli;

/// <summary>macOS / Linux 的 PAD CLI 入口。Windows 上也能跑，便于和 PAD 对比行为。</summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            return await global::Pad.Core.Cli.Run(args);
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"pad: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }
}
