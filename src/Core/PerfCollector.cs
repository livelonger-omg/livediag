using System;
using System.Collections.Generic;
using System.Diagnostics;
using LiveDiag.Interop;

namespace LiveDiag
{
    public class PerfInfo
    {
        public int CpuPm;
        public int MemPm;
        public int DiskQPm10 = -1;
        public int CpuStreamPm = -1;
        public int GpuEncPm = -1;
        public int Gpu3dPm = -1;
        public bool OnAc = true;
        public bool BatteryValid;
        public List<string> TopCpu = new List<string>();
    }

    /// <summary>CPU / 内存 / 磁盘队列 / 指定进程 CPU。全部用最轻的原生调用，不走 WMI。</summary>
    public class PerfCollector
    {
        private ulong _lastIdle, _lastKernel, _lastUser;
        private bool _hasSys;
        private readonly Dictionary<int, ulong> _lastProcCpu = new Dictionary<int, ulong>();
        private double _lastProcTime;
        private IntPtr _diskQuery = IntPtr.Zero;
        private IntPtr _diskCounter = IntPtr.Zero;
        private bool _diskTried;
        private IntPtr _gpuQuery = IntPtr.Zero;
        private IntPtr _gpuCounter = IntPtr.Zero;
        private double _gpuNextTry = -1;
        private double _gpuLast = -1;
        private double _diskLast = -1;
        private int _gpuEncCache = -1;
        private int _gpuAllCache = -1;
        private int _diskCache = -1;
        public string GpuDiag = "";

        public PerfInfo Read(int streamPid)
        {
            PerfInfo info = new PerfInfo();

            ulong idle, kernel, user;
            if (Native.GetSystemTimes(out idle, out kernel, out user))
            {
                if (_hasSys)
                {
                    ulong di = idle - _lastIdle;
                    ulong dk = (kernel - _lastKernel) + (user - _lastUser);
                    if (dk > 0)
                    {
                        double busy = 1.0 - (double)di / dk;
                        if (busy < 0) busy = 0;
                        if (busy > 1) busy = 1;
                        info.CpuPm = (int)Math.Round(busy * 1000);
                    }
                }
                _lastIdle = idle;
                _lastKernel = kernel;
                _lastUser = user;
                _hasSys = true;
            }

            Native.MEMORYSTATUSEX ms = new Native.MEMORYSTATUSEX();
            ms.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX));
            if (Native.GlobalMemoryStatusEx(ref ms)) info.MemPm = (int)ms.dwMemoryLoad * 10;

            Native.SYSTEM_POWER_STATUS ps = new Native.SYSTEM_POWER_STATUS();
            if (Native.GetSystemPowerStatus(ref ps))
            {
                info.BatteryValid = true;
                info.OnAc = ps.ACLineStatus == 1;
            }

            // PDH 计数（磁盘队列 / GPU）较慢，放到后台线程，避免阻塞采样 tick
            info.DiskQPm10 = _diskCache;
            info.GpuEncPm = _gpuEncCache;
            info.Gpu3dPm = _gpuAllCache;
            RefreshHeavy();
            if (streamPid > 0) info.CpuStreamPm = ReadProcCpu(streamPid);
            return info;
        }

        private int _bgBusy;

        private void RefreshHeavy()
        {
            if (System.Threading.Interlocked.CompareExchange(ref _bgBusy, 1, 0) != 0) return;
            System.Threading.Tasks.Task.Factory.StartNew(delegate
            {
                try
                {
                    double now = Mono.Now;
                    if (now - _diskLast >= 10 || _diskCache < 0)
                    {
                        _diskLast = now;
                        ReadDiskQueue();
                    }
                    ReadGpu();
                }
                catch { }
                finally { System.Threading.Interlocked.Exchange(ref _bgBusy, 0); }
            });
        }

        /// <summary>GPU 引擎利用率：总和与 VideoEncode 引擎分别统计（用于识别编码瓶颈）。</summary>
        private void ReadGpu()
        {
            try
            {
                double now = Mono.Now;
                if (now - _gpuLast < 20) return;
                _gpuLast = now;

                if (_gpuQuery == IntPtr.Zero)
                {
                    if (_gpuNextTry > 0 && now < _gpuNextTry) return;
                    _gpuNextTry = now + 60;
                    IntPtr q;
                    uint e1 = Native.PdhOpenQuery(null, IntPtr.Zero, out q);
                    if (e1 != 0) { GpuDiag = "open=" + e1; return; }
                    IntPtr counter;
                    uint e2 = Native.PdhAddEnglishCounter(q, "\\GPU Engine(*)\\Utilization Percentage", IntPtr.Zero, out counter);
                    if (e2 != 0)
                    {
                        Native.PdhCloseQuery(q);
                        GpuDiag = "add=" + e2;
                        return;
                    }
                    _gpuQuery = q;
                    _gpuCounter = counter;
                }

                uint e3 = Native.PdhCollectQueryData(_gpuQuery);
                if (e3 != 0) { GpuDiag = "collect=" + e3; return; }

                uint size = 0, count = 0;
                uint e4 = Native.PdhGetFormattedCounterArray(_gpuCounter, Native.PDH_FMT_DOUBLE, ref size, ref count, IntPtr.Zero);
                if (size == 0) { GpuDiag = "size0 get=" + e4; return; }
                IntPtr buf = System.Runtime.InteropServices.Marshal.AllocHGlobal((int)size + 64);
                try
                {
                    uint r = Native.PdhGetFormattedCounterArray(_gpuCounter, Native.PDH_FMT_DOUBLE, ref size, ref count, buf);
                    if (r != 0) { GpuDiag = "array=" + r + " size=" + size + " n=" + count; return; }
                    GpuDiag = "ok n=" + count;
                    int stride = IntPtr.Size == 8 ? 24 : 20;
                    double sumAll = 0, sumEnc = 0;
                    for (uint i = 0; i < count; i++)
                    {
                        IntPtr p = new IntPtr(buf.ToInt64() + (long)i * stride);
                        IntPtr namePtr = System.Runtime.InteropServices.Marshal.ReadIntPtr(p);
                        if (namePtr == IntPtr.Zero) continue;
                        string name = System.Runtime.InteropServices.Marshal.PtrToStringUni(namePtr);
                        long bits = System.Runtime.InteropServices.Marshal.ReadInt64(new IntPtr(p.ToInt64() + 8));
                        double v = BitConverter.Int64BitsToDouble(bits);
                        if (double.IsNaN(v) || v < 0) continue;
                        sumAll += v;
                        if (name != null && name.IndexOf("VideoEncode", StringComparison.OrdinalIgnoreCase) >= 0) sumEnc += v;
                    }
                    int enc = (int)Math.Round(sumEnc * 10);
                    int all = (int)Math.Round(sumAll * 10);
                    if (enc > 1000) enc = 1000;
                    if (all > 1000) all = 1000;
                    _gpuEncCache = enc;
                    _gpuAllCache = all;
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FreeHGlobal(buf);
                }
            }
            catch { }
        }

        private int ReadDiskQueue()
        {
            try
            {
                if (!_diskTried)
                {
                    _diskTried = true;
                    if (Native.PdhOpenQuery(null, IntPtr.Zero, out _diskQuery) == 0)
                    {
                        if (Native.PdhAddEnglishCounter(_diskQuery, "\\PhysicalDisk(_Total)\\Avg. Disk Queue Length", IntPtr.Zero, out _diskCounter) != 0)
                        {
                            Native.PdhCloseQuery(_diskQuery);
                            _diskQuery = IntPtr.Zero;
                        }
                    }
                }
                if (_diskQuery == IntPtr.Zero) return -1;
                if (Native.PdhCollectQueryData(_diskQuery) != 0) return -1;
                uint type;
                double v;
                if (Native.PdhGetFormattedCounterValue(_diskCounter, Native.PDH_FMT_DOUBLE | Native.PDH_FMT_NOCAP100, out type, out v) != 0) return -1;
                if (v < 0) v = 0;
                int pm = (int)Math.Round(v * 10);
                if (pm > 3000) pm = 3000;
                _diskCache = pm;
                return pm;
            }
            catch
            {
                return -1;
            }
        }

        private int ReadProcCpu(int pid)
        {
            IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return -1;
            try
            {
                ulong c, e, k, u;
                if (!Native.GetProcessTimes(h, out c, out e, out k, out u)) return -1;
                ulong total = k + u;
                double now = Mono.Now;
                ulong last;
                if (_lastProcCpu.TryGetValue(pid, out last) && _lastProcTime > 0)
                {
                    double dt = now - _lastProcTime;
                    if (dt > 0.001 && total >= last)
                    {
                        double pm = (total - last) / 10000000.0 / dt * 1000.0;
                        _lastProcCpu[pid] = total;
                        _lastProcTime = now;
                        if (pm > 1000) pm = 1000;
                        return (int)Math.Round(pm);
                    }
                }
                _lastProcCpu[pid] = total;
                _lastProcTime = now;
                return -1;
            }
            catch
            {
                return -1;
            }
            finally
            {
                Native.CloseHandle(h);
            }
        }

        /// <summary>全机进程 CPU Top N（每 10 秒调用一次即可）。</summary>
        public List<string> TopCpuProcesses(int top)
        {
            List<string> result = new List<string>();
            try
            {
                int[] pids = new int[2048];
                int bytes;
                if (!Native.EnumProcesses(pids, pids.Length * 4, out bytes)) return result;
                int n = bytes / 4;
                List<KeyValuePair<double, string>> list = new List<KeyValuePair<double, string>>();
                for (int i = 0; i < n; i++)
                {
                    int pid = pids[i];
                    if (pid <= 4) continue;
                    IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                    if (h == IntPtr.Zero) continue;
                    try
                    {
                        ulong c, e, k, u;
                        if (!Native.GetProcessTimes(h, out c, out e, out k, out u)) continue;
                        double sec = (k + u) / 10000000.0;
                        if (sec <= 0) continue;
                        string name;
                        try { name = Process.GetProcessById(pid).ProcessName; }
                        catch { name = "pid " + pid; }
                        list.Add(new KeyValuePair<double, string>(sec, name));
                    }
                    finally { Native.CloseHandle(h); }
                }
                list.Sort(delegate(KeyValuePair<double, string> a, KeyValuePair<double, string> b)
                {
                    return b.Key.CompareTo(a.Key);
                });
                for (int i = 0; i < list.Count && i < top; i++)
                    result.Add(list[i].Value + " " + list[i].Key.ToString("0") + "s");
            }
            catch { }
            return result;
        }
    }
}
