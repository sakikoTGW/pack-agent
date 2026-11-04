using System.Net;
using System.Runtime.InteropServices;

namespace Pad.Core;

/// <summary>
/// pid → 正在监听的 TCP 端口（IPv4）。
/// 项目页要回答"这个东西跑在哪"，端口只有系统 TCP 表知道；直接 P/Invoke 问，
/// 不 shell out 解析 netstat（输出随系统语言变，解析不可靠）。
/// </summary>
public static class Ports
{
    const int AfInet = 2;
    const int TcpTableOwnerPidListener = 3;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder,
        int ulAf, int tableClass, uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;   // 网络序，内存里是 [a,b,c,d]
        public uint LocalPort;   // 网络序，低 16 位是端口字节对
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    /// <summary>一条监听：地址 + 端口 + 所属 pid。</summary>
    public sealed record Listener(int Port, string Address, int Pid)
    {
        public bool Loopback => Address.StartsWith("127.", StringComparison.Ordinal);
        public string Label => Address + ":" + Port;
    }

    /// <summary>这些 pid 正在监听的端口（IPv4，含 127.0.0.1 / 0.0.0.0）。</summary>
    public static IReadOnlyList<Listener> Listening(IEnumerable<int> pids)
    {
        var rows = new List<Listener>();
        var want = new HashSet<int>(pids);
        if (want.Count == 0) return rows;

        var size = 0;
        var rc = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidListener, 0);
        if (rc != 0 && size <= 0) return rows;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            rc = GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidListener, 0);
            if (rc != 0) return rows;

            var count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            var at = buffer + 4;
            for (var i = 0; i < count; i++, at += rowSize)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(at);
                var pid = (int)row.OwningPid;
                if (!want.Contains(pid)) continue;

                var port = (int)(((row.LocalPort & 0xFF) << 8) | ((row.LocalPort >> 8) & 0xFF));
                var address = new IPAddress(new[]
                {
                    (byte)(row.LocalAddr & 0xFF),
                    (byte)((row.LocalAddr >> 8) & 0xFF),
                    (byte)((row.LocalAddr >> 16) & 0xFF),
                    (byte)((row.LocalAddr >> 24) & 0xFF),
                }).ToString();
                rows.Add(new Listener(port, address, pid));
            }
        }
        catch
        {
            /* TCP 表读不到：返回空，卡片照实写 — */
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return rows;
    }
}
