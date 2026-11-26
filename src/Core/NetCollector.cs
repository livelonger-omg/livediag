using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LiveDiag.Interop;

namespace LiveDiag
{
    internal static class Mono
    {
        private static readonly Stopwatch Sw = Stopwatch.StartNew();
        public static double Now { get { return Sw.Elapsed.TotalSeconds; } }
        public static long UnixMs { get { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; } }
    }

    public class AdapterInfo
    {
        public string Name = "";
        public string Description = "";
        public string Ip = "";
        public string Gateway = "";
        public List<string> Dns = new List<string>();
        public int IfIndex;
        public long LinkBps;
        public bool Wired;
        public uint IfType;
        public bool Up;

        public string DnsText
        {
            get { return Dns.Count == 0 ? "-" : string.Join(", ", Dns.ToArray()); }
        }

        public string LinkText
        {
            get
            {
                if (LinkBps <= 0) return "-";
                return (LinkBps / 1000000.0).ToString("0.#") + " Mbps";
            }
        }
    }

    public class TrafficInfo
    {
        public double UpBps;
        public double DownBps;
        public int LinkUtilPm = -1;
        public bool Valid;
    }

    /// <summary>网卡信息与接口流量计数器。</summary>
    public class NetCollector
    {
        private readonly AdapterInfo _adapter = new AdapterInfo();
        private uint _lastIn;
        private uint _lastOut;
        private double _lastTime;
        private bool _hasLast;
        private uint _tableSize;
        public string LastError = "";

        public AdapterInfo Adapter { get { return _adapter; } }

        public AdapterInfo RefreshAdapter()
        {
            IntPtr buf = IntPtr.Zero;
            try
            {
                uint best = 0;
                Native.GetBestInterface(Native.ToNetUint(new byte[] { 223, 5, 5, 5 }), out best);

                uint size = 16 * 1024;
                uint ret = 0;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    if (buf != IntPtr.Zero) { Marshal.FreeHGlobal(buf); buf = IntPtr.Zero; }
                    buf = Marshal.AllocHGlobal((int)size);
                    ret = Native.GetAdaptersAddresses(Native.AF_INET,
                        Native.GAA_FLAG_SKIP_ANYCAST | Native.GAA_FLAG_SKIP_MULTICAST | Native.GAA_FLAG_INCLUDE_GATEWAYS,
                        IntPtr.Zero, buf, ref size);
                    if (ret != Native.ERROR_BUFFER_OVERFLOW) break;
                }
                if (ret != 0)
                {
                    LastError = "GetAdaptersAddresses 失败：" + ret;
                    _adapter.Up = false;
                    return _adapter;
                }

                AdapterInfo bestMatch = null;
                AdapterInfo fallback = null;
                IntPtr cur = buf;
                while (cur != IntPtr.Zero)
                {
                    Native.IP_ADAPTER_ADDRESSES a =
                        (Native.IP_ADAPTER_ADDRESSES)Marshal.PtrToStructure(cur, typeof(Native.IP_ADAPTER_ADDRESSES));

                    AdapterInfo info = new AdapterInfo();
                    info.Name = Marshal.PtrToStringUni(a.FriendlyName);
                    info.Description = Marshal.PtrToStringUni(a.Description);
                    info.IfIndex = (int)((ulong)a.AlignLength >> 32);
                    info.IfType = a.IfType;
                    info.Up = a.OperStatus == 1;
                    info.LinkBps = (long)a.TransmitLinkSpeed;
                    info.Wired = a.IfType == 6 || a.IfType == 71 ? a.IfType == 6 : false;
                    if (a.IfType == 71) info.Wired = false;

                    IntPtr u = a.FirstUnicastAddress;
                    while (u != IntPtr.Zero)
                    {
                        Native.IP_ADAPTER_SOCKADDR_NODE n =
                            (Native.IP_ADAPTER_SOCKADDR_NODE)Marshal.PtrToStructure(u, typeof(Native.IP_ADAPTER_SOCKADDR_NODE));
                        string ip = Native.SockaddrToString(n.lpSockaddr, n.iSockaddrLength);
                        if (ip != null && ip != "0.0.0.0" && !ip.StartsWith("169.254.") && ip.IndexOf(':') < 0)
                        {
                            info.Ip = ip;
                            break;
                        }
                        u = n.Next;
                    }

                    IntPtr g = a.FirstGatewayAddress;
                    while (g != IntPtr.Zero)
                    {
                        Native.IP_ADAPTER_SOCKADDR_NODE n =
                            (Native.IP_ADAPTER_SOCKADDR_NODE)Marshal.PtrToStructure(g, typeof(Native.IP_ADAPTER_SOCKADDR_NODE));
                        string ip = Native.SockaddrToString(n.lpSockaddr, n.iSockaddrLength);
                        if (ip != null && ip != "0.0.0.0")
                        {
                            info.Gateway = ip;
                            break;
                        }
                        g = n.Next;
                    }

                    IntPtr d = a.FirstDnsServerAddress;
                    while (d != IntPtr.Zero && info.Dns.Count < 4)
                    {
                        Native.IP_ADAPTER_SOCKADDR_NODE n =
                            (Native.IP_ADAPTER_SOCKADDR_NODE)Marshal.PtrToStructure(d, typeof(Native.IP_ADAPTER_SOCKADDR_NODE));
                        string ip = Native.SockaddrToString(n.lpSockaddr, n.iSockaddrLength);
                        if (ip != null && ip != "0.0.0.0" && ip.IndexOf(':') < 0 && !info.Dns.Contains(ip)) info.Dns.Add(ip);
                        d = n.Next;
                    }

                    if (info.Up && info.Ip.Length > 0)
                    {
                        if (info.IfIndex == (int)best && info.Gateway.Length > 0) bestMatch = info;
                        if (fallback == null && info.Gateway.Length > 0) fallback = info;
                    }

                    cur = a.Next;
                }

                AdapterInfo chosen = bestMatch != null ? bestMatch : fallback;
                if (chosen == null)
                {
                    LastError = "未找到已连接的网络适配器";
                    _adapter.Up = false;
                    return _adapter;
                }

                bool changed = chosen.IfIndex != _adapter.IfIndex || chosen.Ip != _adapter.Ip;
                _adapter.Name = chosen.Name;
                _adapter.Description = chosen.Description;
                _adapter.Ip = chosen.Ip;
                _adapter.Gateway = chosen.Gateway;
                _adapter.Dns = chosen.Dns;
                _adapter.IfIndex = chosen.IfIndex;
                _adapter.LinkBps = chosen.LinkBps;
                _adapter.IfType = chosen.IfType;
                _adapter.Wired = chosen.Wired;
                _adapter.Up = chosen.Up;
                if (changed) { _hasLast = false; }
                LastError = "";
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            finally
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
            }
            return _adapter;
        }

        public TrafficInfo ReadTraffic()
        {
            TrafficInfo t = new TrafficInfo();
            IntPtr buf = IntPtr.Zero;
            try
            {
                uint size = _tableSize;
                if (size == 0)
                {
                    Native.GetIfTable(IntPtr.Zero, ref size, true);
                    if (size == 0) return t;
                    _tableSize = size;
                }
                buf = Marshal.AllocHGlobal((int)size + 8192);
                uint ret = Native.GetIfTable(buf, ref size, true);
                if (ret != 0)
                {
                    _tableSize = 0;
                    return t;
                }

                int count = Marshal.ReadInt32(buf);
                int stride = Marshal.SizeOf(typeof(Native.MIB_IFROW));
                uint inOct = 0, outOct = 0;
                bool found = false;
                // MIB_IFROW 固定布局：dwIndex@512、dwInOctets@552、dwOutOctets@576
                // 直接读裸内存，避免每行都做字符串封送（实测可从 20ms 降到 <1ms）
                for (int i = 0; i < count; i++)
                {
                    IntPtr row = new IntPtr(buf.ToInt64() + 4 + (long)i * stride);
                    if (Marshal.ReadInt32(row, 512) == _adapter.IfIndex)
                    {
                        inOct = (uint)Marshal.ReadInt32(row, 552);
                        outOct = (uint)Marshal.ReadInt32(row, 576);
                        found = true;
                        break;
                    }
                }
                if (!found) return t;

                double now = Mono.Now;
                if (_hasLast)
                {
                    uint dIn = unchecked(inOct - _lastIn);
                    uint dOut = unchecked(outOct - _lastOut);
                    double dt = now - _lastTime;
                    if (dt <= 0.001) dt = 0.001;
                    if (dIn < 2000000000u && dOut < 2000000000u)
                    {
                        t.DownBps = dIn * 8.0 / dt;
                        t.UpBps = dOut * 8.0 / dt;
                        t.Valid = true;
                        if (_adapter.LinkBps > 0)
                        {
                            double util = (dOut * 8.0 / dt) / _adapter.LinkBps * 1000.0;
                            t.LinkUtilPm = (int)Math.Min(999, Math.Round(util));
                        }
                    }
                }
                _lastIn = inOct;
                _lastOut = outOct;
                _lastTime = now;
                _hasLast = true;
            }
            catch { }
            finally
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
            }
            return t;
        }
    }

    public class PingResult
    {
        public string Host = "";
        public int RttMs = -1;
        public int JitMs;
        public int LossPm = -1;
        public int Sent;
        public int Recv;
        public long Tick;
        public string Error = "";
        public bool NoResponse;

        public bool HasValue { get { return Tick > 0; } }
    }

    /// <summary>ICMP 探测者：每次取 3 个回包，统计 RTT / 抖动 / 丢包。</summary>
    public class PingWorker : IDisposable
    {
        public readonly string Host;
        private readonly uint _dest;
        private readonly IntPtr _handle = IntPtr.Zero;
        private readonly byte[] _payload = new byte[32];
        public readonly PingResult Result = new PingResult();
        private int _busy;
        private int _zeroStreak;

        public bool Valid { get { return _handle != IntPtr.Zero; } }

        public PingWorker(string host)
        {
            Host = host;
            Result.Host = host;
            IPAddress ip;
            if (IPAddress.TryParse(host, out ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                _dest = Native.ToNetUint(ip.GetAddressBytes());
                _handle = Native.IcmpCreateFile();
            }
            else
            {
                Result.Error = "目标不是 IPv4 地址";
            }
        }

        public void Run()
        {
            if (_handle == IntPtr.Zero) return;
            if (System.Threading.Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            try
            {
                int sent = 0, recv = 0;
                int min = int.MaxValue, max = -1;
                long sum = 0;
                byte[] reply = new byte[512];
                for (int i = 0; i < 3; i++)
                {
                    sent++;
                    uint n = Native.IcmpSendEcho(_handle, _dest, _payload, (ushort)_payload.Length,
                        IntPtr.Zero, reply, (uint)reply.Length, 900);
                    if (n == 0) continue;
                    GCHandle h = GCHandle.Alloc(reply, GCHandleType.Pinned);
                    try
                    {
                        Native.ICMP_ECHO_REPLY r = (Native.ICMP_ECHO_REPLY)Marshal.PtrToStructure(
                            h.AddrOfPinnedObject(), typeof(Native.ICMP_ECHO_REPLY));
                        if (r.Status != 0) continue;
                        recv++;
                        int rtt = (int)r.RoundTripTime;
                        sum += rtt;
                        if (rtt < min) min = rtt;
                        if (rtt > max) max = rtt;
                    }
                    finally { h.Free(); }
                }

                Result.Sent = sent;
                Result.Recv = recv;
                Result.Tick = Mono.UnixMs;
                if (recv > 0)
                {
                    Result.RttMs = (int)(sum / recv);
                    Result.JitMs = max - min;
                }
                else
                {
                    Result.RttMs = -1;
                    Result.JitMs = 0;
                }
                Result.LossPm = sent > 0 ? (sent - recv) * 1000 / sent : -1;
                Result.Error = "";
                if (recv == 0)
                {
                    _zeroStreak++;
                    if (_zeroStreak >= 3)
                    {
                        Result.NoResponse = true;
                        Result.RttMs = -1;
                        Result.LossPm = -1;
                    }
                }
                else
                {
                    _zeroStreak = 0;
                    Result.NoResponse = false;
                }
            }
            catch (Exception ex)
            {
                Result.Error = ex.Message;
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _busy, 0);
            }
        }

        /// <summary>异步跑一轮，避免阻塞采样线程。</summary>
        public Task RunAsync()
        {
            return Task.Factory.StartNew(new Action(Run));
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero) Native.IcmpCloseHandle(_handle);
        }
    }
}
