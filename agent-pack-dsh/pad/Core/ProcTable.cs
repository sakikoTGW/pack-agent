using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Pad.Core;

/// <summary>
/// 一个进程的现场事实：从内核结构（PEB）读出来的命令行与环境变量，不是猜的。
///
/// 作用：桌面壳的 <c>--user-data-dir</c>、子进程的 <c>DSH_HOME</c>、
/// TUI/纯 dsh 的 <c>--profile</c> 都只能从进程现场拿到；只看文件永远判不出"在跑"。
/// </summary>
public sealed record ProcInfo(
    int Pid,
    int ParentPid,
    string? Exe,
    string? CommandLine,
    /// <summary>环境变量 DSH_HOME（进程自己认的 home，最硬的归属证据）。</summary>
    string? DshHome,
    /// <summary>环境变量 DSH_PROFILE。</summary>
    string? DshProfile,
    /// <summary>命令行里的 --user-data-dir（Electron 壳的 userData 真值）。</summary>
    string? UserDataDir,
    /// <summary>命令行里的 --profile。</summary>
    string? ProfileArg);

/// <summary>
/// 进程表快照 + PEB 读取。纯 P/Invoke，无外部包、不 shell out。
///
/// 布局（x64）：PEB.ProcessParameters @0x20；RTL_USER_PROCESS_PARAMETERS 里
/// CommandLine @0x70、Environment @0x80，都是 UNICODE_STRING。
/// 32 位（WOW64）目标进程的偏移不同，本机 DSH 全是 x64，遇到就跳过而不是猜。
/// </summary>
public static class ProcTable
{
    const uint ProcessQueryLimitedInformation = 0x1000;
    const uint ProcessQueryInformation = 0x0400;
    const uint ProcessVmRead = 0x0010;

    const int PebProcessParametersOffset = 0x20;   // PEB -> RTL_USER_PROCESS_PARAMETERS*
    const int ProcessParametersCommandLine = 0x70;
    const int ProcessParametersEnvironment = 0x80;

    const int MaxCommandLineChars = 4096;
    const int MaxEnvironmentBytes = 256 * 1024;
    const int EnvironmentChunkBytes = 4096;

    static readonly string[] ArgvNames = ["node", "bun", "electron", "deno", "dsh", "dsh-cli"];

    /// <summary>
    /// 本机进程现场（只留和 DSH 有关的：node/bun/electron、名字或路径里带 dsh/deepseek/harness 的）。
    /// 读不到内存的进程（系统/提权进程）跳过，不抛异常。调用方在 worker 线程上用。
    /// </summary>
    public static IReadOnlyList<ProcInfo> Snapshot(int cap = 900, CancellationToken ct = default)
    {
        var found = new List<ProcInfo>();
        Process[] all;
        try { all = Process.GetProcesses(); }
        catch { return found; }

        try
        {
            // 第一遍：只问 exe + 父进程（轻），筛出和 DSH 可能有关的进程。
            // 名字就带 dsh/deepseek/harness 的排前面：240 个读取名额优先给硬证据。
            var prelim = new List<(int Pid, int Parent, string? Exe, bool Strong)>();
            var count = Math.Min(all.Length, cap);
            for (var i = 0; i < count; i++)
            {
                if (i % 50 == 0) ct.ThrowIfCancellationRequested();
                int pid;
                try { pid = all[i].Id; }
                catch { continue; }
                var basic = ReadBasic(pid);
                if (basic is null) continue;
                if (PreliminaryRelevant(basic.Value.Exe, out var strong))
                    prelim.Add((basic.Value.Pid, basic.Value.Parent, basic.Value.Exe, strong));
            }

            // 第二遍：给候选读命令行 + 环境（PEB）。
            var reads = 0;
            foreach (var (pid, _, _, _) in prelim.OrderByDescending(p => p.Strong))
            {
                if (++reads > 240) break;
                if (reads % 25 == 0) ct.ThrowIfCancellationRequested();
                try
                {
                    var info = ReadPid(pid, wantEnvironment: true);
                    if (info is not null && IsRelevant(info)) found.Add(info);
                }
                catch { /* 一个进程读不到不影响整张表 */ }
            }
        }
        finally
        {
            foreach (var proc in all)
            {
                try { proc.Dispose(); } catch { /* best effort */ }
            }
        }

        return found;
    }

    static bool PreliminaryRelevant(string? exe, out bool strong)
    {
        strong = false;
        if (string.IsNullOrWhiteSpace(exe)) return false;
        var name = Path.GetFileNameWithoutExtension(exe);
        if (ArgvNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))) return true;
        strong = exe.Contains("dsh", StringComparison.OrdinalIgnoreCase)
            || exe.Contains("deepseek", StringComparison.OrdinalIgnoreCase)
            || exe.Contains("harness", StringComparison.OrdinalIgnoreCase);
        return strong;
    }


    /// <summary>读单个进程；读不到返回 null。</summary>
    public static ProcInfo? Read(int pid) => ReadPid(pid, wantEnvironment: true);

    /// <summary>命令行（读不到 null）。</summary>
    public static string? ReadCommandLine(int pid) => ReadPid(pid, wantEnvironment: false)?.CommandLine;

    /// <summary>环境变量（读不到 null）。</summary>
    public static IReadOnlyDictionary<string, string>? ReadEnvironment(int pid) =>
        ReadEnvironmentBlock(pid);

    // ---- 进程读取 ----------------------------------------------------------

    static (int Pid, int Parent, string? Exe)? ReadBasic(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            return (pid, ParentPid(handle), ImagePath(handle));
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    static ProcInfo? ReadPid(int pid, bool wantEnvironment)
    {
        if (pid <= 0) return null;
        var handle = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, pid);
        if (handle == IntPtr.Zero)
            handle = OpenProcess(ProcessQueryLimitedInformation | ProcessVmRead, false, pid);
        if (handle == IntPtr.Zero)
            handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return null;

        try
        {
            var wow64 = false;
            try { if (IsWow64Process(handle, out wow64) && wow64) return null; } catch { /* 新系统可能没有 */ }
            if (wow64) return null;

            var exe = ImagePath(handle);
            var parent = ParentPid(handle);
            string? cmdline = null;
            IReadOnlyDictionary<string, string>? env = null;

            if (TryPebPointers(handle, out var commandLinePtr, out var environmentPtr))
            {
                cmdline = ReadUnicodeString(handle, commandLinePtr, MaxCommandLineChars);
                if (wantEnvironment && environmentPtr != IntPtr.Zero)
                    env = ReadEnvironmentPointer(handle, environmentPtr);
            }

            if (cmdline is null && exe is null) return null;
            if (exe is null && cmdline is null && env is null) return null;

            env ??= Empty;
            env.TryGetValue("DSH_HOME", out var dshHome);
            env.TryGetValue("DSH_PROFILE", out var dshProfile);

            return new ProcInfo(
                pid, parent, exe, cmdline,
                Clean(dshHome), Clean(dshProfile),
                ArgValue(cmdline, "--user-data-dir"),
                ArgValue(cmdline, "--profile"));
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    static string? ImagePath(IntPtr process)
    {
        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            if (QueryFullProcessImageName(process, 0, sb, ref size)) return Clean(sb.ToString());
        }
        catch { /* ignore */ }
        return null;
    }

    static int ParentPid(IntPtr process)
    {
        try
        {
            var pbi = new ProcessBasicInformation();
            var status = NtQueryInformationProcess(process, 0, ref pbi,
                Marshal.SizeOf<ProcessBasicInformation>(), out _);
            if (status == 0) return (int)pbi.InheritedFromUniqueProcessId.ToInt64();
        }
        catch { /* ignore */ }
        return 0;
    }

    static bool TryPebPointers(IntPtr process, out IntPtr commandLine, out IntPtr environment)
    {
        commandLine = IntPtr.Zero;
        environment = IntPtr.Zero;
        // 下面的偏移是 x64 布局；PAD 以 x86 构建时读到的会错，直接不读更安全。
        if (IntPtr.Size != 8) return false;
        try
        {
            var pbi = new ProcessBasicInformation();
            var status = NtQueryInformationProcess(process, 0, ref pbi,
                Marshal.SizeOf<ProcessBasicInformation>(), out _);
            if (status != 0 || pbi.PebBaseAddress == IntPtr.Zero) return false;

            if (!ReadIntPtr(process, pbi.PebBaseAddress + PebProcessParametersOffset, out var parameters))
                return false;
            if (parameters == IntPtr.Zero) return false;

            commandLine = parameters + ProcessParametersCommandLine;
            if (ReadIntPtr(process, parameters + ProcessParametersEnvironment, out var envPtr))
                environment = envPtr;
            return true;
        }
        catch
        {
            return false;
        }
    }

    static string? ReadUnicodeString(IntPtr process, IntPtr unicodeString, int maxChars)
    {
        try
        {
            // UNICODE_STRING (x64): Length u16 @0, MaximumLength u16 @2, Buffer* @8
            var header = new byte[16];
            if (!ReadProcessMemory(process, unicodeString, header, header.Length, out var read) || read < 16)
                return null;
            var length = BitConverter.ToUInt16(header, 0);
            var buffer = (IntPtr)BitConverter.ToInt64(header, 8);
            if (buffer == IntPtr.Zero || length == 0) return null;
            var want = Math.Min(length, maxChars * 2);
            var bytes = new byte[want];
            if (!ReadProcessMemory(process, buffer, bytes, bytes.Length, out read) || read == 0)
                return null;
            var text = Encoding.Unicode.GetString(bytes, 0, (int)Math.Min(read, bytes.Length));
            var end = text.IndexOf('\0');
            if (end >= 0) text = text[..end];
            // 命令行本身是带引号的（"exe" args）：不能像路径那样 Trim('"')，
            // 否则首尾引号不配对，后面按引号切参数会整段粘成一个 token。
            return TrimCommandLine(text);
        }
        catch
        {
            return null;
        }
    }

    static IReadOnlyDictionary<string, string>? ReadEnvironmentPointer(IntPtr process, IntPtr block)
    {
        if (block == IntPtr.Zero) return null;
        var bytes = new List<byte>(16 * 1024);
        for (var offset = 0; offset < MaxEnvironmentBytes; offset += EnvironmentChunkBytes)
        {
            var chunk = new byte[EnvironmentChunkBytes];
            if (!ReadProcessMemory(process, block + offset, chunk, chunk.Length, out var read) || read == 0)
                break;
            bytes.AddRange(read == chunk.Length ? chunk : chunk[..(int)read]);
            if (HasDoubleNull(bytes, 2)) break;
            if (read < chunk.Length) break;
        }
        if (bytes.Count == 0) return null;
        var text = Encoding.Unicode.GetString(bytes.ToArray());
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in text.Split('\0'))
        {
            if (item.Length == 0) break;
            var at = item.IndexOf('=');
            if (at <= 0) continue;
            map[item[..at]] = item[(at + 1)..];
        }
        return map.Count == 0 ? null : map;
    }

    static IReadOnlyDictionary<string, string>? ReadEnvironmentBlock(int pid)
    {
        var handle = OpenProcess(ProcessQueryInformation | ProcessQueryLimitedInformation | ProcessVmRead, false, pid);
        if (handle == IntPtr.Zero) handle = OpenProcess(ProcessQueryLimitedInformation | ProcessVmRead, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var wow = false;
            if (IsWow64Process(handle, out wow) && wow) return null;
            if (!TryPebPointers(handle, out _, out var env) || env == IntPtr.Zero) return null;
            return ReadEnvironmentPointer(handle, env);
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    static bool HasDoubleNull(List<byte> bytes, int align)
    {
        // UTF-16 里的 \0\0 是一个空字符串；环境块以它结尾。
        for (var i = align; i < bytes.Count; i += 2)
            if (bytes[i - 2] == 0 && bytes[i - 1] == 0 && i + 1 < bytes.Count &&
                bytes[i] == 0 && bytes[i + 1] == 0)
                return true;
        return false;
    }

    // ---- 判定与参数 --------------------------------------------------------

    /// <summary>这条进程现场和 DSH 有没有关系（决定要不要进快照）。</summary>
    public static bool IsRelevant(ProcInfo info)
    {
        if (info.DshHome is { Length: > 0 }) return true;
        if (info.ProfileArg is { Length: > 0 } && LooksDsh(info)) return true;
        // 别的 Electron 应用也带 --user-data-dir：只有路径/命令行像 DSH 的才算
        if (info.UserDataDir is { Length: > 0 } && LooksDsh(info)) return true;
        var exe = info.Exe;
        if (string.IsNullOrWhiteSpace(exe)) return false;
        return LooksDsh(info);
    }

    static bool LooksDsh(ProcInfo info)
    {
        var text = (info.Exe ?? "") + "\n" + (info.CommandLine ?? "");
        return text.Contains("dsh", StringComparison.OrdinalIgnoreCase)
            || text.Contains("deepseek", StringComparison.OrdinalIgnoreCase)
            || text.Contains("harness", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>命令行里 <c>--name value</c> 或 <c>--name=value</c> 的值（去引号）。</summary>
    public static string? ArgValue(string? commandLine, string name)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;
        var tokens = SplitArgs(commandLine!);
        for (var i = 0; i < tokens.Count; i++)
        {
            var arg = tokens[i];
            if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                return Clean(arg[(name.Length + 1)..]);
            if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count)
                return Clean(tokens[i + 1]);
        }
        return null;
    }

    /// <summary>
    /// 把命令行切成参数（处理双引号，保留空格路径）。
    /// </summary>
    public static IReadOnlyList<string> SplitArgs(string commandLine)
    {
        var list = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        foreach (var ch in commandLine)
        {
            if (ch == '"') { quoted = !quoted; continue; }
            if (!quoted && (ch == ' ' || ch == '\t'))
            {
                if (current.Length > 0) { list.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(ch);
        }
        if (current.Length > 0) list.Add(current.ToString());
        return list;
    }

    /// <summary>命令行里第一个指向 <c>.js</c> 的路径（TUI/CLI 的入口）。</summary>
    public static string? ScriptArg(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;
        foreach (var arg in SplitArgs(commandLine!))
            if (arg.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                && (arg.Contains('\\') || arg.Contains('/')))
                return Clean(arg);
        return null;
    }

    static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim().Trim('"').Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>命令行只去尾部空白/NUL，保留引号（参数切分依赖引号配对）。</summary>
    static string? TrimCommandLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.TrimEnd('\0').TrimEnd();
        return text.Length == 0 ? null : text;
    }

    // ---- P/Invoke ---------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool ReadProcessMemory(IntPtr process, IntPtr baseAddress, byte[] buffer,
        int size, out IntPtr bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool ReadProcessMemory(IntPtr process, IntPtr baseAddress, IntPtr buffer,
        int size, out IntPtr bytesRead);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name,
        ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool IsWow64Process(IntPtr process, out bool wow64);

    [DllImport("ntdll.dll")]
    static extern int NtQueryInformationProcess(IntPtr process, int infoClass,
        ref ProcessBasicInformation info, int infoLength, out int returnLength);

    static bool ReadIntPtr(IntPtr process, IntPtr address, out IntPtr value)
    {
        value = IntPtr.Zero;
        var bytes = new byte[IntPtr.Size];
        if (!ReadProcessMemory(process, address, bytes, bytes.Length, out var read) || read < bytes.Length)
            return false;
        value = IntPtr.Size == 8 ? (IntPtr)BitConverter.ToInt64(bytes, 0) : (IntPtr)BitConverter.ToInt32(bytes, 0);
        return value != IntPtr.Zero;
    }
}
