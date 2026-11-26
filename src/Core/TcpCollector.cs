using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LiveDiag.Interop;

namespace LiveDiag
{
    public class ConnInfo
    {
        public string Key = "";
        public int Pid;
        public string Proc = "";
        public string LocalIp = "";
        public int LocalPort;
        public string RemoteIp = "";
        public int RemotePort;
        public int State;
        public long BytesOut;
        public long BytesIn;
        public double UpBps;
        public double DownBps;
        public bool HasStats;
    }

    /// <summary>UDP 端点（UDP 表没有对端地址，只能记录本地端口与所属进程）。</summary>
    public class UdpEp
    {
        public int Pid;
        public string LocalIp = "";
        public int LocalPort;
    }

    public class TcpSnapshot
    {
        public int Total;
        public int Estab;
        public int SynSent;
        public int TimeWait;
        public int Listen;
        public int UdpTotal;
        public double NewPerSec;
        public List<ConnInfo> Conns = new List<ConnInfo>();
        /// <summary>IPv6 已建立连接：只用于"哪个进程占用了哪个远端端口"的判定，不参与流量统计。</summary>
        public List<ConnInfo> Conns6 = new List<ConnInfo>();
        public List<UdpEp> Udp = new List<UdpEp>();
        public Dictionary<int, double> PidUp = new Dictionary<int, double>();
        public Dictionary<int, double> PidDown = new Dictionary<int, double>();
        public Dictionary<int, string> PidName = new Dictionary<int, string>();
        public bool EstatsOk;
        public int EstatsFail;
        public int Tracked;
        public string LastError = "";

        public double UpOf(int pid)
        {
            double v;
            if (PidUp.TryGetValue(pid, out v)) return v;
            return 0;
        }

        public string NameOf(int pid)
        {
            string v;
            if (PidName.TryGetValue(pid, out v)) return v;
            return "";
        }

        /// <summary>该进程是否有远端端口落在给定集合里的连接（IPv4 + IPv6）。</summary>
        public bool HasRemotePort(int pid, HashSet<int> portSet)
        {
            foreach (ConnInfo c in Conns)
                if (c.Pid == pid && portSet.Contains(c.RemotePort)) return true;
            foreach (ConnInfo c in Conns6)
                if (c.Pid == pid && portSet.Contains(c.RemotePort)) return true;
            return false;
        }

        public bool HasRemotePort(int pid, int port)
        {
            foreach (ConnInfo c in Conns)
                if (c.Pid == pid && c.RemotePort == port) return true;
            foreach (ConnInfo c in Conns6)
                if (c.Pid == pid && c.RemotePort == port) return true;
            return false;
        }

        /// <summary>该进程是否持有端口落在给定集合里的 UDP 端点（SRT / QUIC 推流不会出现在 TCP 表）。</summary>
        public bool HasUdpPort(int pid, HashSet<int> portSet)
        {
            foreach (UdpEp u in Udp)
                if (u.Pid == pid && portSet.Contains(u.LocalPort)) return true;
            return false;
        }

        public bool HasUdp(int pid)
        {
            foreach (UdpEp u in Udp)
                if (u.Pid == pid) return true;
            return false;
        }
    }

    internal class ConnStat
    {
        public long LastOut = -1;
        public long LastIn = -1;
        public double LastTime;
        public bool Enabled;
        public int Pid;
        public long SeenMs;
    }

    /// <summary>枚举 TCP/UDP 连接，并用 TCP ESTATS(Data) 得到逐连接、逐进程的收发速率。</summary>
    public class TcpCollector
    {
        private readonly Dictionary<string, ConnStat> _stats = new Dictionary<string, ConnStat>();
        private readonly Dictionary<int, string> _nameCache = new Dictionary<int, string>();
        private readonly Dictionary<int, long> _nameTime = new Dictionary<int, long>();
        private readonly HashSet<string> _enableFailed = new HashSet<string>();
        private readonly HashSet<string> _seenThisRound = new HashSet<string>();
        private double _lastTime;
        private int _lastTotal = -1;
        private bool _estatsDisabled;
        private int _estatsErrStreak;
        public bool EstatsAvailable { get; private set; }
        public uint EstatsErrorCode { get; private set; }
        public string LastError = "";

        public TcpSnapshot Read()
        {
            TcpSnapshot snap = new TcpSnapshot();
            IntPtr buf = IntPtr.Zero;
            try
            {
                int size = 64 * 1024;
                uint ret = 0;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    if (buf != IntPtr.Zero) { Marshal.FreeHGlobal(buf); buf = IntPtr.Zero; }
                    buf = Marshal.AllocHGlobal(size);
                    ret = Native.GetExtendedTcpTable(buf, ref size, false, Native.AF_INET, Native.TCP_TABLE_OWNER_PID_ALL, 0);
                    if (ret != Native.ERROR_INSUFFICIENT_BUFFER) break;
                }
                if (ret != 0)
                {
                    LastError = "GetExtendedTcpTable 失败：" + ret;
                    return snap;
                }

                int count = Marshal.ReadInt32(buf);
                int stride = Marshal.SizeOf(typeof(Native.MIB_TCPROW_OWNER_PID));
                double now = Mono.Now;
                double dt = _lastTime > 0 ? now - _lastTime : 0;
                _seenThisRound.Clear();

                int newConns = 0;
                for (int i = 0; i < count; i++)
                {
                    IntPtr p = new IntPtr(buf.ToInt64() + 4 + (long)i * stride);
                    Native.MIB_TCPROW_OWNER_PID r =
                        (Native.MIB_TCPROW_OWNER_PID)Marshal.PtrToStructure(p, typeof(Native.MIB_TCPROW_OWNER_PID));

                    snap.Total++;
                    int state = (int)r.state;
                    if (state == Native.MIB_TCP_STATE_ESTAB) snap.Estab++;
                    else if (state == Native.MIB_TCP_STATE_SYN_SENT) snap.SynSent++;
                    else if (state == Native.MIB_TCP_STATE_TIME_WAIT) snap.TimeWait++;
                    else if (state == Native.MIB_TCP_STATE_LISTEN) snap.Listen++;

                    if (state == Native.MIB_TCP_STATE_LISTEN || r.remoteAddr == 0) continue;

                    ConnInfo c = new ConnInfo();
                    c.Pid = (int)r.owningPid;
                    c.LocalIp = Native.UintToIp(r.localAddr);
                    c.LocalPort = Native.GetPort(r.localPort);
                    c.RemoteIp = Native.UintToIp(r.remoteAddr);
                    c.RemotePort = Native.GetPort(r.remotePort);
                    c.State = state;
                    c.Key = c.Pid + "|" + c.LocalPort + "|" + c.RemoteIp + ":" + c.RemotePort;

                    ConnStat st;
                    if (!_stats.TryGetValue(c.Key, out st))
                    {
                        st = new ConnStat();
                        st.Pid = c.Pid;
                        _stats[c.Key] = st;
                        if (dt > 0) newConns++;
                    }
                    st.SeenMs = Mono.UnixMs;
                    _seenThisRound.Add(c.Key);

                    if (state == Native.MIB_TCP_STATE_ESTAB && !_enableFailed.Contains(c.Key))
                    {
                        ReadEstats(c, st, now, dt, snap);
                    }

                    // 所有已建立连接都要保留：回退模式下虽然没有字节统计，
                    // 但远端端口/远端 IP 仍然是识别推流进程的关键证据。
                    snap.Conns.Add(c);
                    if (c.UpBps > 0 || c.DownBps > 0)
                    {
                        double v;
                        if (snap.PidUp.TryGetValue(c.Pid, out v)) snap.PidUp[c.Pid] = v + c.UpBps;
                        else snap.PidUp[c.Pid] = c.UpBps;
                        if (snap.PidDown.TryGetValue(c.Pid, out v)) snap.PidDown[c.Pid] = v + c.DownBps;
                        else snap.PidDown[c.Pid] = c.DownBps;
                    }
                }

                if (dt > 0)
                {
                    snap.NewPerSec = newConns / dt;
                }
                _lastTotal = snap.Total;
                _lastTime = now;
                snap.Tracked = snap.Conns.Count;
                snap.EstatsOk = EstatsAvailable;

                // IPv6 与 UDP 只用于识别推流进程，不参与速率统计
                ReadIpv6(snap);
                ReadUdp(snap);

                // 填入进程名
                foreach (ConnInfo c in snap.Conns)
                {
                    c.Proc = GetProcName(c.Pid);
                    snap.PidName[c.Pid] = c.Proc;
                }
                foreach (ConnInfo c in snap.Conns6)
                {
                    c.Proc = GetProcName(c.Pid);
                    snap.PidName[c.Pid] = c.Proc;
                }

                // 清理过期连接
                List<string> dead = new List<string>();
                foreach (KeyValuePair<string, ConnStat> kv in _stats)
                {
                    if (!_seenThisRound.Contains(kv.Key) && Mono.UnixMs - kv.Value.SeenMs > 120000)
                        dead.Add(kv.Key);
                }
                foreach (string k in dead) { _stats.Remove(k); }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            finally
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
            }
            return snap;
        }

        private void ReadIpv6(TcpSnapshot snap)
        {
            IntPtr buf = IntPtr.Zero;
            try
            {
                int size = 64 * 1024;
                uint ret = 0;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    if (buf != IntPtr.Zero) { Marshal.FreeHGlobal(buf); buf = IntPtr.Zero; }
                    buf = Marshal.AllocHGlobal(size);
                    ret = Native.GetExtendedTcpTable(buf, ref size, false, Native.AF_INET6, Native.TCP_TABLE_OWNER_PID_ALL, 0);
                    if (ret != Native.ERROR_INSUFFICIENT_BUFFER) break;
                }
                if (ret != 0) return;

                int count = Marshal.ReadInt32(buf);
                int stride = Marshal.SizeOf(typeof(Native.MIB_TCP6ROW_OWNER_PID));
                for (int i = 0; i < count; i++)
                {
                    IntPtr p = new IntPtr(buf.ToInt64() + 4 + (long)i * stride);
                    Native.MIB_TCP6ROW_OWNER_PID r =
                        (Native.MIB_TCP6ROW_OWNER_PID)Marshal.PtrToStructure(p, typeof(Native.MIB_TCP6ROW_OWNER_PID));
                    int state = (int)r.state;
                    if (state == Native.MIB_TCP_STATE_LISTEN) continue;
                    if (r.remoteAddr == null || r.localAddr == null) continue;
                    if (IsAllZero(r.remoteAddr)) continue;

                    ConnInfo c = new ConnInfo();
                    c.Pid = (int)r.owningPid;
                    c.LocalIp = new System.Net.IPAddress(r.localAddr).ToString();
                    c.LocalPort = Native.GetPort(r.localPort);
                    c.RemoteIp = new System.Net.IPAddress(r.remoteAddr).ToString();
                    c.RemotePort = Native.GetPort(r.remotePort);
                    c.State = state;
                    c.Key = "6|" + c.Pid + "|" + c.LocalPort + "|" + c.RemoteIp + ":" + c.RemotePort;
                    snap.Conns6.Add(c);
                }
            }
            catch { }
            finally
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
            }
        }

        private void ReadUdp(TcpSnapshot snap)
        {
            ReadUdp4(snap);
            ReadUdp6(snap);
        }

        private void ReadUdp4(TcpSnapshot snap)
        {
            IntPtr buf = IntPtr.Zero;
            try
            {
                int size = 64 * 1024;
                uint ret = 0;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    if (buf != IntPtr.Zero) { Marshal.FreeHGlobal(buf); buf = IntPtr.Zero; }
                    buf = Marshal.AllocHGlobal(size);
                    ret = Native.GetExtendedUdpTable(buf, ref size, false, Native.AF_INET, Native.UDP_TABLE_OWNER_PID, 0);
                    if (ret != Native.ERROR_INSUFFICIENT_BUFFER) break;
                }
                if (ret != 0) return;

                int count = Marshal.ReadInt32(buf);
                int stride = Marshal.SizeOf(typeof(Native.MIB_UDPROW_OWNER_PID));
                for (int i = 0; i < count; i++)
                {
                    IntPtr p = new IntPtr(buf.ToInt64() + 4 + (long)i * stride);
                    Native.MIB_UDPROW_OWNER_PID r =
                        (Native.MIB_UDPROW_OWNER_PID)Marshal.PtrToStructure(p, typeof(Native.MIB_UDPROW_OWNER_PID));
                    UdpEp u = new UdpEp();
                    u.Pid = (int)r.owningPid;
                    u.LocalIp = Native.UintToIp(r.localAddr);
                    u.LocalPort = Native.GetPort(r.localPort);
                    snap.Udp.Add(u);
                }
                snap.UdpTotal += count;
            }
            catch { }
            finally
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
            }
        }

        private void ReadUdp6(TcpSnapshot snap)
        {
            IntPtr buf = IntPtr.Zero;
            try
            {
                int size = 64 * 1024;
                uint ret = 0;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    if (buf != IntPtr.Zero) { Marshal.FreeHGlobal(buf); buf = IntPtr.Zero; }
                    buf = Marshal.AllocHGlobal(size);
                    ret = Native.GetExtendedUdpTable(buf, ref size, false, Native.AF_INET6, Native.UDP_TABLE_OWNER_PID, 0);
                    if (ret != Native.ERROR_INSUFFICIENT_BUFFER) break;
                }
                if (ret != 0) return;

                int count = Marshal.ReadInt32(buf);
                int stride = Marshal.SizeOf(typeof(Native.MIB_UDP6ROW_OWNER_PID));
                for (int i = 0; i < count; i++)
                {
                    IntPtr p = new IntPtr(buf.ToInt64() + 4 + (long)i * stride);
                    Native.MIB_UDP6ROW_OWNER_PID r =
                        (Native.MIB_UDP6ROW_OWNER_PID)Marshal.PtrToStructure(p, typeof(Native.MIB_UDP6ROW_OWNER_PID));
                    if (r.localAddr == null) continue;
                    UdpEp u = new UdpEp();
                    u.Pid = (int)r.owningPid;
                    u.LocalIp = new System.Net.IPAddress(r.localAddr).ToString();
                    u.LocalPort = Native.GetPort(r.localPort);
                    snap.Udp.Add(u);
                }
                snap.UdpTotal += count;
            }
            catch { }
            finally
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
            }
        }

        private static bool IsAllZero(byte[] b)
        {
            for (int i = 0; i < b.Length; i++) if (b[i] != 0) return false;
            return true;
        }

        private void ReadEstats(ConnInfo c, ConnStat st, double now, double dt, TcpSnapshot snap)
        {
            if (_estatsDisabled) return;

            Native.MIB_TCPROW row = new Native.MIB_TCPROW();
            row.dwState = (uint)c.State;
            row.dwLocalAddr = Native.ToNetUint(ParseIp(c.LocalIp));
            row.dwLocalPort = (uint)((c.LocalPort >> 8) | ((c.LocalPort & 0xFF) << 8));
            row.dwRemoteAddr = Native.ToNetUint(ParseIp(c.RemoteIp));
            row.dwRemotePort = (uint)((c.RemotePort >> 8) | ((c.RemotePort & 0xFF) << 8));

            if (!st.Enabled)
            {
                Native.TCP_ESTATS_DATA_RW_v0 rw = new Native.TCP_ESTATS_DATA_RW_v0();
                rw.EnableCollection = true;
                uint e = Native.SetPerTcpConnectionEStats(ref row, Native.TcpConnectionEstatsData, ref rw, 0, 1, 0);
                if (e != 0)
                {
                    snap.EstatsFail++;
                    if (EstatsErrorCode == 0) EstatsErrorCode = e;
                    if (e == 87 || e == 5 || e == 1168) _enableFailed.Add(c.Key);
                    _estatsErrStreak++;
                    if (_estatsErrStreak > 40) _estatsDisabled = true;
                    return;
                }
                st.Enabled = true;
                _estatsErrStreak = 0;
            }

            Native.TCP_ESTATS_DATA_ROD_v0 rod = new Native.TCP_ESTATS_DATA_ROD_v0();
            uint err = Native.GetPerTcpConnectionEStats(ref row, Native.TcpConnectionEstatsData,
                IntPtr.Zero, 0, 0, 0, IntPtr.Zero, 0, 0, 0,
                ref rod, 0, (uint)Marshal.SizeOf(typeof(Native.TCP_ESTATS_DATA_ROD_v0)), 0);
            if (err != 0)
            {
                snap.EstatsFail++;
                if (EstatsErrorCode == 0) EstatsErrorCode = err;
                _estatsErrStreak++;
                if (_estatsErrStreak > 40) _estatsDisabled = true;
                return;
            }
            EstatsAvailable = true;
            _estatsErrStreak = 0;

            long bo = (long)rod.DataBytesOut;
            long bi = (long)rod.DataBytesIn;
            c.HasStats = true;
            c.BytesOut = bo;
            c.BytesIn = bi;
            if (st.LastOut >= 0 && dt > 0.001)
            {
                long dob = bo - st.LastOut;
                long dib = bi - st.LastIn;
                if (dob < 0) dob = 0;
                if (dib < 0) dib = 0;
                c.UpBps = dob * 8.0 / dt;
                c.DownBps = dib * 8.0 / dt;
            }
            st.LastOut = bo;
            st.LastIn = bi;
            st.LastTime = now;
        }

        private static byte[] ParseIp(string ip)
        {
            string[] parts = ip.Split('.');
            byte[] b = new byte[4];
            for (int i = 0; i < 4 && i < parts.Length; i++)
            {
                int v;
                int.TryParse(parts[i], out v);
                b[i] = (byte)v;
            }
            return b;
        }

        public string GetProcName(int pid)
        {
            return ProcName.Get(pid);
        }

        public void ResetBaseline()
        {
            _stats.Clear();
            _enableFailed.Clear();
            _lastTime = 0;
        }
    }
}
