using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using LiveDiag.Interop;

namespace LiveDiag
{
    /// <summary>一次逐跳探测的结果：Hops[0] 是 TTL=1（网关），Hops[1] 是 TTL=2（网关之后第一跳）。</summary>
    public class HopPath
    {
        public string TargetIp = "";
        public string[] Hops = new string[0];   // 空串表示该跳对 ICMP 无响应
        public long Tick;
        public string Error = "";

        public string HopAt(int ttl)
        {
            int i = ttl - 1;
            if (i < 0 || i >= Hops.Length) return "";
            return Hops[i];
        }

        public int MaxTtl { get { return Hops.Length; } }

        public string PathText()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < Hops.Length; i++)
            {
                if (sb.Length > 0) sb.Append(" → ");
                sb.Append(i + 1).Append(':').Append(Hops[i].Length > 0 ? Hops[i] : "*");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// 用带 TTL 的 ICMP 做路径探测与接入网质量监测。
    /// TTL=1 命中网关，TTL=2 命中的就是「网关之后第一跳」（BRAS / 汇聚交换机），
    /// 这一段属于接入网，故障概率明显高于核心网，所以单独盯它的时延与丢包。
    ///
    /// 质量探测采用"向目标发 TTL 受限的 ICMP"，让包真正经过该跳后再由它回 TTL 超时，
    /// 比直接 ping 该跳自己的地址更接近实际转发行为（有些设备不回显 ping 但会回 TTL 超时）。
    /// </summary>
    public sealed class HopProbe : IDisposable
    {
        public const uint IP_SUCCESS = 0;
        public const uint IP_DEST_NET_UNREACHABLE = 11002;
        public const uint IP_DEST_HOST_UNREACHABLE = 11003;
        public const uint IP_DEST_PROT_UNREACHABLE = 11004;
        public const uint IP_DEST_PORT_UNREACHABLE = 11005;
        public const uint IP_REQ_TIMED_OUT = 11010;
        public const uint IP_TTL_EXPIRED_TRANSIT = 11013;

        private readonly IntPtr _handle;
        private readonly byte[] _payload = new byte[32];
        private readonly byte[] _reply = new byte[512];

        public HopProbe()
        {
            _handle = Native.IcmpCreateFile();
        }

        public bool Valid { get { return _handle != IntPtr.Zero; } }

        /// <summary>逐跳扫描到 maxTtl，返回每跳的响应地址（无响应为空串）。</summary>
        public HopPath Trace(string targetIp, int maxTtl)
        {
            HopPath path = new HopPath();
            path.TargetIp = targetIp;
            path.Tick = Mono.UnixMs;

            uint dest;
            if (ToDest(targetIp, out dest) == 0)
            {
                path.Error = "目标不是 IPv4 地址";
                return path;
            }
            if (_handle == IntPtr.Zero)
            {
                path.Error = "ICMP 句柄不可用";
                return path;
            }

            if (maxTtl < 1) maxTtl = 1;
            if (maxTtl > 30) maxTtl = 30;

            List<string> hops = new List<string>();
            bool reached = false;
            for (int ttl = 1; ttl <= maxTtl && !reached; ttl++)
            {
                string hop = "";
                for (int attempt = 0; attempt < 2 && hop.Length == 0; attempt++)
                {
                    string responder;
                    int rtt;
                    uint status;
                    if (!Send(dest, ttl, 700, out responder, out rtt, out status)) continue;
                    if (status == IP_SUCCESS)
                    {
                        hop = responder;      // 目标本身就在这一跳
                        reached = true;
                    }
                    else if (status == IP_TTL_EXPIRED_TRANSIT)
                    {
                        hop = responder;      // 这一跳的路由器回了 TTL 超时
                    }
                    else if (status == IP_DEST_NET_UNREACHABLE || status == IP_DEST_HOST_UNREACHABLE)
                    {
                        hop = responder;      // 能回应"不可达"说明路由到过这里
                    }
                }
                hops.Add(hop);
            }
            path.Hops = hops.ToArray();
            return path;
        }

        /// <summary>向目标发一个 TTL 受限的 ICMP。成功（收到 TTL 超时或 echo 应答）时返回 true。</summary>
        public bool Send(uint dest, int ttl, int timeoutMs, out string responderIp, out int rttMs, out uint status)
        {
            responderIp = "";
            rttMs = -1;
            status = IP_REQ_TIMED_OUT;
            if (_handle == IntPtr.Zero) return false;

            IntPtr optPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.IP_OPTION_INFORMATION)));
            GCHandle pinned = default(GCHandle);
            try
            {
                Native.IP_OPTION_INFORMATION opt = new Native.IP_OPTION_INFORMATION();
                opt.Ttl = (byte)ttl;
                opt.Tos = 0;
                opt.Flags = 0;
                opt.OptionsSize = 0;
                opt.OptionsData = IntPtr.Zero;
                Marshal.StructureToPtr(opt, optPtr, false);

                Array.Clear(_reply, 0, _reply.Length);
                pinned = GCHandle.Alloc(_reply, GCHandleType.Pinned);
                uint n = Native.IcmpSendEcho(_handle, dest, _payload, (ushort)_payload.Length,
                    optPtr, _reply, (uint)_reply.Length, (uint)timeoutMs);
                if (n == 0) return false;

                Native.ICMP_ECHO_REPLY r = (Native.ICMP_ECHO_REPLY)Marshal.PtrToStructure(
                    pinned.AddrOfPinnedObject(), typeof(Native.ICMP_ECHO_REPLY));
                status = r.Status;
                responderIp = Native.UintToIp(r.Address);
                rttMs = (int)r.RoundTripTime;
                return true;
            }
            catch { return false; }
            finally
            {
                if (pinned.IsAllocated) pinned.Free();
                Marshal.FreeHGlobal(optPtr);
            }
        }

        public static uint ToDest(string ipText, out uint dest)
        {
            dest = 0;
            IPAddress ip;
            if (!IPAddress.TryParse(ipText, out ip)) return 0;
            if (ip.AddressFamily != AddressFamily.InterNetwork) return 0;
            dest = Native.ToNetUint(ip.GetAddressBytes());
            return dest;
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero) Native.IcmpCloseHandle(_handle);
        }
    }

    /// <summary>对某一跳（通常是第 2 跳）做周期性质检：时延、抖动、丢包。</summary>
    public sealed class HopPing : IDisposable
    {
        public readonly PingResult Result = new PingResult();
        public readonly HopProbe Probe;

        private readonly uint _dest;
        private readonly string _targetIp;
        private int _ttl;
        private int _busy;
        private volatile string _hopIp = "";

        // 路由器对 TTL 超时的 ICMP 普遍限速，单轮 3 个包很容易被判成"丢包"。
        // 丢包率改为最近 10 轮（约 50 秒）滑动窗口统计，避免一两个包被限速就触发误报。
        private const int LossWindow = 10;
        private readonly int[] _histSent = new int[LossWindow];
        private readonly int[] _histRecv = new int[LossWindow];
        private int _histIdx;
        private int _histCount;
        private int _zeroStreak;

        public string TargetIp { get { return _targetIp; } }
        public int Ttl { get { return _ttl; } }
        public string HopIp { get { return _hopIp; } }

        public HopPing(string targetIp, int ttl)
        {
            _targetIp = targetIp;
            _ttl = ttl;
            Result.Host = targetIp;
            Probe = new HopProbe();
            if (!Probe.Valid || HopProbe.ToDest(targetIp, out _dest) == 0)
                Result.Error = "目标不是 IPv4 地址";
        }

        public bool Valid { get { return Probe.Valid && _dest != 0; } }

        public void SetTtl(int ttl)
        {
            if (ttl >= 1 && ttl <= 30) _ttl = ttl;
        }

        /// <summary>探测一轮（3 个包），统计 RTT / 抖动 / 丢包。</summary>
        public void Run()
        {
            if (!Valid) return;
            if (System.Threading.Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            try
            {
                int sent = 0, recv = 0;
                int min = int.MaxValue, max = -1;
                long sum = 0;
                string lastHop = "";

                for (int i = 0; i < 3; i++)
                {
                    if (i > 0) System.Threading.Thread.Sleep(80);   // 稍微拉开间隔，降低被限速的概率
                    sent++;
                    string responder;
                    int rtt;
                    uint status;
                    if (!Probe.Send(_dest, _ttl, 700, out responder, out rtt, out status)) continue;

                    bool ok = status == HopProbe.IP_SUCCESS || status == HopProbe.IP_TTL_EXPIRED_TRANSIT;
                    if (!ok) continue;

                    recv++;
                    lastHop = responder;
                    if (rtt >= 0)
                    {
                        sum += rtt;
                        if (rtt < min) min = rtt;
                        if (rtt > max) max = rtt;
                    }
                }

                // 滑动窗口统计丢包
                _histSent[_histIdx] = sent;
                _histRecv[_histIdx] = recv;
                _histIdx = (_histIdx + 1) % LossWindow;
                if (_histCount < LossWindow) _histCount++;

                int winSent = 0, winRecv = 0;
                for (int i = 0; i < _histCount; i++) { winSent += _histSent[i]; winRecv += _histRecv[i]; }

                Result.Sent = winSent;
                Result.Recv = winRecv;
                Result.Tick = Mono.UnixMs;
                if (recv > 0)
                {
                    Result.RttMs = min == int.MaxValue ? -1 : (int)(sum / recv);
                    Result.JitMs = max >= 0 && min != int.MaxValue ? max - min : 0;
                    if (lastHop.Length > 0) _hopIp = lastHop;
                    _zeroStreak = 0;
                    Result.NoResponse = false;
                }
                else
                {
                    // 单轮全丢可能只是路由器对 ICMP 限速：连续 3 轮（约 15 秒）都没回应才判为不可达，
                    // 这样偶发的限速不会在曲线上开天窗。
                    _zeroStreak++;
                    Result.RttMs = -1;
                    Result.JitMs = 0;
                    Result.NoResponse = _zeroStreak >= 3;
                }
                // 至少累积 3 轮（约 15 秒）再给出丢包率，避免刚启动时的首轮抖动被当成丢包
                Result.LossPm = (_histCount >= 3 && winSent > 0) ? (winSent - winRecv) * 1000 / winSent : -1;
                if (Result.LossPm > 1000) Result.LossPm = 1000;
                Result.Error = "";
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

        public Task RunAsync()
        {
            return Task.Factory.StartNew(new Action(Run));
        }

        public void Dispose()
        {
            Probe.Dispose();
        }
    }
}
