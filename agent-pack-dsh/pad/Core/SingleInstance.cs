using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Pad.Core;

/// <summary>
/// 每个启动器根（.pack-launcher）同时只允许一个窗口实例写。
/// 第二个实例不再开窗，而是通过命名管道让第一个实例把主窗口显出来，然后自己退出。
/// CLI（<c>pad cli …</c>）不参与，脚本/自动化照旧可以无窗口操作。
/// </summary>
public sealed class SingleInstance : IDisposable
{
    readonly Mutex _mutex;
    readonly string _pipeName;
    readonly string _lockFile;
    CancellationTokenSource? _cts;
    bool _disposed;

    SingleInstance(Mutex mutex, string pipeName, string lockFile)
    {
        _mutex = mutex;
        _pipeName = pipeName;
        _lockFile = lockFile;
    }

    /// <summary>同一路径的大小写/尾斜杠差异要落到同一把锁上。</summary>
    static string KeyFor(string root)
    {
        var canon = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canon));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    public static SingleInstance? TryAcquire(string root)
    {
        var key = KeyFor(root);
        var mutex = new Mutex(initiallyOwned: true, name: $@"Local\PAD-launcher-{key}", out var created);
        if (!created)
        {
            mutex.Dispose();
            return null;
        }
        var lockFile = Path.Combine(root, "pad.lock");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(lockFile,
                $"{Environment.ProcessId}{Environment.NewLine}{Environment.ProcessPath}{Environment.NewLine}" +
                $"{DateTimeOffset.Now:o}{Environment.NewLine}", new UTF8Encoding(false));
        }
        catch { /* 锁文件只是给人看的，mutex 才是权威 */ }
        return new SingleInstance(mutex, $@"PAD-launcher-{key}", lockFile);
    }

    /// <summary>起一个后台管道监听：第二个实例发 "show" 时回调（在调用方线程上）。</summary>
    public void Listen(Action show)
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (string.Equals(line, "show", StringComparison.OrdinalIgnoreCase))
                        show();
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    try { await Task.Delay(200, ct).ConfigureAwait(false); }
                    catch { return; }
                }
            }
        }, ct);
    }

    /// <summary>锁文件里记的持有者（pid, exe）；读不到返回 (null, null)。</summary>
    public static (int? Pid, string? Exe) LockOwner(string root)
    {
        try
        {
            var path = Path.Combine(root, "pad.lock");
            if (!File.Exists(path)) return (null, null);
            var lines = File.ReadAllLines(path);
            int? pid = lines.Length > 0 && int.TryParse(lines[0], out var value) && value > 0 ? value : null;
            var exe = lines.Length > 1 && lines[1].Length > 0 ? lines[1] : null;
            return (pid, exe);
        }
        catch { return (null, null); }
    }
    /// <summary>第二个实例调用：让已运行的 PAD 显形。失败返回 false（对方刚退出等）。</summary>
    public static bool SignalExisting(string root)
    {
        var key = KeyFor(root);
        var pipeName = $@"PAD-launcher-{key}";
        for (var attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                client.Connect(120);
                using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
                writer.WriteLine("show");
                return true;
            }
            catch
            {
                Thread.Sleep(120);
            }
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts?.Cancel(); } catch { }
        try
        {
            // 只删自己写的锁文件（别把下一个实例的文件删了）。
            if (File.Exists(_lockFile) && File.ReadAllText(_lockFile).StartsWith(Environment.ProcessId.ToString()))
                File.Delete(_lockFile);
        }
        catch { }
        try { _mutex.ReleaseMutex(); } catch { }
        try { _mutex.Dispose(); } catch { }
    }
}
