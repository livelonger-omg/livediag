using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using LiveDiag.Interop;

namespace LiveDiag
{
    /// <summary>TCP 扩展统计(ESTATS)可用性探针：定位 Get 的正确传参方式。</summary>
    internal static class EstatsProbe
    {
        public static void Run()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("== ESTATS 探针 v2 ==");
            sb.AppendLine("OS: " + Environment.OSVersion.VersionString + "  64bit=" + Environment.Is64BitProcess);
            sb.AppendLine("ROD 结构大小: " + Marshal.SizeOf(typeof(Native.TCP_ESTATS_DATA_ROD_v0)));

            Native.MIB_TCPROW_OWNER_PID row;
            int pid;
            if (!FindEstablished(out row, out pid))
            {
                sb.AppendLine("没有找到 ESTABLISHED 连接");
                Write(sb);
                return;
            }
            sb.AppendLine("样本: pid=" + pid + " " + Native.UintToIp(row.localAddr) + ":" + Native.GetPort(row.localPort) +
                " -> " + Native.UintToIp(row.remoteAddr) + ":" + Native.GetPort(row.remotePort));

            Native.MIB_TCPROW tr = new Native.MIB_TCPROW();
            tr.dwState = row.state;
            tr.dwLocalAddr = row.localAddr;
            tr.dwLocalPort = row.localPort;
            tr.dwRemoteAddr = row.remoteAddr;
            tr.dwRemotePort = row.remotePort;

            uint rodSize = (uint)Marshal.SizeOf(typeof(Native.TCP_ESTATS_DATA_ROD_v0));
            Native.TCP_ESTATS_DATA_RW_v0 rw = new Native.TCP_ESTATS_DATA_RW_v0();
            rw.EnableCollection = true;

            uint eSet = Native.SetPerTcpConnectionEStats(ref tr, Native.TcpConnectionEstatsData, ref rw, 0, 1, 0);
            sb.AppendLine("Set(Data,1) = " + eSet);
            uint eSetPath = Native.SetPerTcpConnectionEStats(ref tr, Native.TcpConnectionEstatsPath, ref rw, 0, 1, 0);
            sb.AppendLine("Set(Path,1) = " + eSetPath);

            // V1: 全 NULL，只给 ROD
            Native.TCP_ESTATS_DATA_ROD_v0 rod1 = new Native.TCP_ESTATS_DATA_ROD_v0();
            uint v1 = Native.GetPerTcpConnectionEStats(ref tr, Native.TcpConnectionEstatsData,
                IntPtr.Zero, 0, 0, 0, IntPtr.Zero, 0, 0, 0, ref rod1, 0, rodSize, 0);
            sb.AppendLine("V1 Get(null,null,rod-ref) = " + v1 + " out=" + rod1.DataBytesOut);

            // V2: 带上 RW(ref)，ROS 为 NULL
            Native.TCP_ESTATS_DATA_ROD_v0 rod2 = new Native.TCP_ESTATS_DATA_ROD_v0();
            uint v2 = Native.GetPerTcpConnectionEStatsRw(ref tr, Native.TcpConnectionEstatsData,
                ref rw, 0, 1, 0, IntPtr.Zero, 0, 0, 0, ref rod2, 0, rodSize, 0);
            sb.AppendLine("V2 Get(rw-ref,null,rod-ref) = " + v2 + " out=" + rod2.DataBytesOut);

            // V3: 全部用非托管缓冲
            IntPtr prw = Marshal.AllocHGlobal(4);
            IntPtr prod = Marshal.AllocHGlobal((int)rodSize);
            try
            {
                Marshal.WriteByte(prw, 0, 1);
                for (int i = 0; i < (int)rodSize; i++) Marshal.WriteByte(prod, i, 0);
                uint v3 = Native.GetPerTcpConnectionEStatsRaw(ref tr, Native.TcpConnectionEstatsData,
                    prw, 0, 1, 0, IntPtr.Zero, 0, 0, 0, prod, 0, rodSize, 0);
                Native.TCP_ESTATS_DATA_ROD_v0 rod3 =
                    (Native.TCP_ESTATS_DATA_ROD_v0)Marshal.PtrToStructure(prod, typeof(Native.TCP_ESTATS_DATA_ROD_v0));
                sb.AppendLine("V3 Get(rw-ptr,null,rod-ptr) = " + v3 + " out=" + rod3.DataBytesOut);
            }
            finally
            {
                Marshal.FreeHGlobal(prw);
                Marshal.FreeHGlobal(prod);
            }

            // V4: RW + ROD 都用非托管，但 RW 指针指向 4 字节
            IntPtr prw2 = Marshal.AllocHGlobal(4);
            IntPtr prod2 = Marshal.AllocHGlobal((int)rodSize);
            try
            {
                Marshal.WriteInt32(prw2, 1);
                uint v4 = Native.GetPerTcpConnectionEStatsRaw(ref tr, Native.TcpConnectionEstatsData,
                    prw2, 0, 4, 0, IntPtr.Zero, 0, 0, 0, prod2, 0, rodSize, 0);
                Native.TCP_ESTATS_DATA_ROD_v0 rod4 =
                    (Native.TCP_ESTATS_DATA_ROD_v0)Marshal.PtrToStructure(prod2, typeof(Native.TCP_ESTATS_DATA_ROD_v0));
                sb.AppendLine("V4 Get(rw-ptr4,null,rod-ptr) = " + v4 + " out=" + rod4.DataBytesOut);
            }
            finally
            {
                Marshal.FreeHGlobal(prw2);
                Marshal.FreeHGlobal(prod2);
            }

            // V5: Path 类型的 ROD（用大缓冲，只观察返回码）
            IntPtr big = Marshal.AllocHGlobal(512);
            try
            {
                uint v5 = Native.GetPerTcpConnectionEStatsRaw(ref tr, Native.TcpConnectionEstatsPath,
                    IntPtr.Zero, 0, 0, 0, IntPtr.Zero, 0, 0, 0, big, 0, 512, 0);
                sb.AppendLine("V5 Get(Path) = " + v5);
            }
            finally { Marshal.FreeHGlobal(big); }

            Write(sb);
        }

        private static bool FindEstablished(out Native.MIB_TCPROW_OWNER_PID found, out int pid)
        {
            found = new Native.MIB_TCPROW_OWNER_PID();
            pid = 0;
            IntPtr buf = IntPtr.Zero;
            Native.MIB_TCPROW_OWNER_PID loopback = new Native.MIB_TCPROW_OWNER_PID();
            int loopPid = 0;
            try
            {
                int size = 64 * 1024;
                uint ret = 0;
                for (int i = 0; i < 4; i++)
                {
                    if (buf != IntPtr.Zero) { Marshal.FreeHGlobal(buf); buf = IntPtr.Zero; }
                    buf = Marshal.AllocHGlobal(size);
                    ret = Native.GetExtendedTcpTable(buf, ref size, false, Native.AF_INET, Native.TCP_TABLE_OWNER_PID_ALL, 0);
                    if (ret != Native.ERROR_INSUFFICIENT_BUFFER) break;
                }
                if (ret != 0) return false;
                int count = Marshal.ReadInt32(buf);
                int stride = Marshal.SizeOf(typeof(Native.MIB_TCPROW_OWNER_PID));
                for (int i = 0; i < count; i++)
                {
                    IntPtr p = new IntPtr(buf.ToInt64() + 4 + (long)i * stride);
                    Native.MIB_TCPROW_OWNER_PID r =
                        (Native.MIB_TCPROW_OWNER_PID)Marshal.PtrToStructure(p, typeof(Native.MIB_TCPROW_OWNER_PID));
                    if (r.state != Native.MIB_TCP_STATE_ESTAB || r.remotePort == 0 || r.owningPid <= 4) continue;
                    string rip = Native.UintToIp(r.remoteAddr);
                    if (rip.StartsWith("127."))
                    {
                        loopback = r;
                        loopPid = (int)r.owningPid;
                        continue;
                    }
                    found = r;
                    pid = (int)r.owningPid;
                    return true;
                }
                if (loopPid > 0)
                {
                    found = loopback;
                    pid = loopPid;
                    return true;
                }
                return false;
            }
            finally
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
            }
        }

        private static void Write(StringBuilder sb)
        {
            string file = Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "estats.txt");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
        }
    }
}
