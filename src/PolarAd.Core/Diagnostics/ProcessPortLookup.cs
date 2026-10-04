using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace PolarAd.Core.Diagnostics;

/// <summary>
/// Maps a local UDP port to the owning process, so the block log can show which
/// browser/app made a given DNS query. Uses the IP Helper API (no admin rights needed
/// to read the table of the current session's processes).
/// </summary>
public sealed class ProcessPortLookup
{
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable, ref int dwOutBufLen, bool sort, int ipVersion, UDP_TABLE_CLASS tableClass, uint reserved);

    private enum UDP_TABLE_CLASS
    {
        UDP_TABLE_BASIC = 0,
        UDP_TABLE_OWNER_PID,
        UDP_TABLE_OWNER_MODULE,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDPROW_OWNER_PID
    {
        public uint LocalAddr;
        public uint LocalPort; // stored big-endian in the low 16 bits
        public uint OwningPid;
    }

    private const int AF_INET = 2;
    private readonly Dictionary<int, string> _processNameCache = new();

    /// <summary>
    /// Returns (appName, pid) for the process currently owning the given local UDP port,
    /// or ("Unknown", 0) if it can't be determined.
    /// </summary>
    public (string AppName, int ProcessId) ResolveOwner(int localPort)
    {
        try
        {
            var pid = FindPidForPort(localPort);
            if (pid <= 0)
            {
                return ("Unknown", 0);
            }

            if (!_processNameCache.TryGetValue(pid, out var name))
            {
                name = TryGetProcessName(pid);
                _processNameCache[pid] = name;
            }

            return (name, pid);
        }
        catch
        {
            return ("Unknown", 0);
        }
    }

    private static int FindPidForPort(int localPort)
    {
        int bufferSize = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);
        if (bufferSize <= 0)
        {
            return -1;
        }

        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            uint result = GetExtendedUdpTable(buffer, ref bufferSize, true, AF_INET, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);
            if (result != 0)
            {
                return -1;
            }

            int rowCount = Marshal.ReadInt32(buffer);
            IntPtr rowPtr = IntPtr.Add(buffer, 4);
            int rowSize = Marshal.SizeOf<MIB_UDPROW_OWNER_PID>();

            for (int i = 0; i < rowCount; i++)
            {
                var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(IntPtr.Add(rowPtr, i * rowSize));
                int port = ExtractPort(row.LocalPort);
                if (port == localPort)
                {
                    return (int)row.OwningPid;
                }
            }

            return -1;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int ExtractPort(uint rawPort)
    {
        // The port occupies the low 16 bits in network byte order.
        ushort networkOrder = (ushort)(rawPort & 0xFFFF);
        return IPAddress.NetworkToHostOrder((short)networkOrder) & 0xFFFF;
    }

    private static string TryGetProcessName(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            return proc.ProcessName + ".exe";
        }
        catch
        {
            return $"PID {pid}";
        }
    }
}
