using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using LiveDiag.Interop;

namespace LiveDiag
{
    /// <summary>进程名查询：走原生 QueryFullProcessImageName，比 Process.GetProcessById 快一个数量级。
    /// 目标进程权限更高（如以管理员身份运行的直播软件）打不开时，回退到 Toolhelp 快照取名字。</summary>
    public static class ProcName
    {
        private static readonly Dictionary<int, string> Names = new Dictionary<int, string>();
        private static readonly Dictionary<int, long> Times = new Dictionary<int, long>();
        private static Dictionary<int, string> _snapshot;
        private static long _snapshotMs;

        public static string Get(int pid)
        {
            string name;
            long now = Mono.UnixMs;
            if (Names.TryGetValue(pid, out name))
            {
                long t;
                if (Times.TryGetValue(pid, out t) && now - t < 60000) return name;
            }
            name = Query(pid);
            if (name == null) name = FromSnapshot(pid);
            if (name == null) name = "pid" + pid;
            Names[pid] = name;
            Times[pid] = now;
            return name;
        }

        public static string Query(int pid)
        {
            IntPtr h = Native.OpenProcess(0x1000, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                StringBuilder sb = new StringBuilder(600);
                uint size = 600;
                if (!Native.QueryFullProcessImageName(h, 0, sb, ref size)) return null;
                return TrimExe(sb.ToString());
            }
            catch { return null; }
            finally { Native.CloseHandle(h); }
        }

        private static string TrimExe(string full)
        {
            int i = full.LastIndexOf('\\');
            string file = i >= 0 ? full.Substring(i + 1) : full;
            int dot = file.LastIndexOf('.');
            return dot > 0 ? file.Substring(0, dot) : file;
        }

        /// <summary>Toolhelp 快照：不要求目标进程权限，能拿到所有进程的可执行文件名。</summary>
        private static string FromSnapshot(int pid)
        {
            Dictionary<int, string> map = Snapshot();
            string v;
            if (map.TryGetValue(pid, out v)) return v;
            return null;
        }

        private static Dictionary<int, string> Snapshot()
        {
            long now = Mono.UnixMs;
            if (_snapshot != null && now - _snapshotMs < 5000) return _snapshot;
            Dictionary<int, string> map = new Dictionary<int, string>();
            IntPtr h = Native.CreateToolhelp32Snapshot(Native.TH32CS_SNAPPROCESS, 0);
            if (h == IntPtr.Zero || h.ToInt64() == -1)
            {
                _snapshot = map;
                _snapshotMs = now;
                return map;
            }
            try
            {
                Native.PROCESSENTRY32W e = new Native.PROCESSENTRY32W();
                e.dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.PROCESSENTRY32W));
                if (Native.Process32First(h, ref e))
                {
                    do
                    {
                        if (e.szExeFile != null && e.szExeFile.Length > 0)
                            map[(int)e.th32ProcessID] = TrimExe(e.szExeFile);
                    }
                    while (Native.Process32Next(h, ref e));
                }
            }
            catch { }
            finally { Native.CloseHandle(h); }
            _snapshot = map;
            _snapshotMs = now;
            return map;
        }
    }

    public class ProcStat
    {
        public int Pid;
        public string Name = "";
        public double CpuPm;        // 占全机 CPU 千分比
        public double WriteBps;     // 进程写出字节速率（网络+磁盘，估算用）
        public double ReadBps;
        public bool Accessible;
    }

    /// <summary>进程级 CPU 与 IO 写出量。用于在无 ESTATS 权限时估算"谁在上行"。</summary>
    public class ProcessCollector
    {
        private readonly Dictionary<int, ulong> _lastWrite = new Dictionary<int, ulong>();
        private readonly Dictionary<int, ulong> _lastRead = new Dictionary<int, ulong>();
        private readonly Dictionary<int, ulong> _lastCpu = new Dictionary<int, ulong>();
        private readonly Dictionary<int, string> _names = new Dictionary<int, string>();
        private readonly Dictionary<int, long> _nameTime = new Dictionary<int, long>();
        private double _lastTime;
        private readonly int _cores = Environment.ProcessorCount;

        public List<ProcStat> Read()
        {
            List<ProcStat> list = new List<ProcStat>();
            try
            {
                int[] pids = new int[4096];
                int bytes;
                if (!Native.EnumProcesses(pids, pids.Length * 4, out bytes)) return list;
                int n = bytes / 4;
                double now = Mono.Now;
                double dt = _lastTime > 0 ? now - _lastTime : 0;

                for (int i = 0; i < n; i++)
                {
                    int pid = pids[i];
                    if (pid == 0 || pid == 4) continue;
                    IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                    if (h == IntPtr.Zero) continue;
                    try
                    {
                        ulong c, e, k, u;
                        Native.IO_COUNTERS io;
                        if (!Native.GetProcessTimes(h, out c, out e, out k, out u)) continue;
                        if (!Native.GetProcessIoCounters(h, out io)) continue;

                        ProcStat st = new ProcStat();
                        st.Pid = pid;
                        st.Name = ProcName.Get(pid);
                        st.Accessible = true;
                        ulong cpu = k + u;
                        ulong wr = io.WriteTransferCount;
                        ulong rd = io.ReadTransferCount;

                        ulong lastCpu, lastW, lastR;
                        if (dt > 0.05 && _lastCpu.TryGetValue(pid, out lastCpu) && cpu >= lastCpu)
                        {
                            st.CpuPm = (cpu - lastCpu) / 10000000.0 / dt * 1000.0 / _cores;
                            if (_lastWrite.TryGetValue(pid, out lastW) && wr >= lastW) st.WriteBps = (wr - lastW) * 8.0 / dt;
                            if (_lastRead.TryGetValue(pid, out lastR) && rd >= lastR) st.ReadBps = (rd - lastR) * 8.0 / dt;
                        }
                        _lastCpu[pid] = cpu;
                        _lastWrite[pid] = wr;
                        _lastRead[pid] = rd;
                        list.Add(st);
                    }
                    catch { }
                    finally { Native.CloseHandle(h); }
                }
                _lastTime = now;
            }
            catch { }
            return list;
        }

        public string NameOf(int pid)
        {
            return ProcName.Get(pid);
        }
    }
}
