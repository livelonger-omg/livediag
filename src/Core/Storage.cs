using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LiveDiag.Interop;

namespace LiveDiag
{
    /// <summary>定长采样记录（v2=72 字节，兼容旧版 60 字节）+ 事件流 + 会话索引 + 学习库。全部为纯文本/二进制，无外部依赖。</summary>
    public class Store : IDisposable
    {
        public const int RecordSize = 72;
        /// <summary>历史版本记录长度：第 59 字节为保留位（恒 0），可用来区分新旧格式。</summary>
        public const int LegacyRecordSize = 60;
        /// <summary>记录格式版本号，写在记录第 59 字节。</summary>
        public const byte RecordVersion = 2;

        private FileStream _smp;
        private BinaryWriter _smpWriter;
        private string _smpDay = "";
        private FileStream _evt;
        private StreamWriter _evtWriter;
        private string _evtDay = "";

        // ---------------- 采样 ----------------

        public void Append(Sample s)
        {
            try
            {
                string day = Ux.ToLocal(s.Ts).ToString("yyyy-MM-dd");
                if (_smp == null || day != _smpDay)
                {
                    CloseSampleFile();
                    Directory.CreateDirectory(Config.SampleDir);
                    // 新格式单独成文件：避免"旧版本已经写了 60 字节记录、升级后往同一文件追加 72 字节"导致格式混排
                    string path = Path.Combine(Config.SampleDir, day + ".v2.smp");
                    _smp = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 8192);
                    _smpWriter = new BinaryWriter(_smp);
                    _smpDay = day;
                }

                byte[] buf = new byte[RecordSize];
                int o = 0;
                WriteInt64(buf, ref o, s.Ts);
                WriteInt32(buf, ref o, (int)s.Flags);
                WriteInt32(buf, ref o, s.UpBps);
                WriteInt32(buf, ref o, s.DownBps);
                WriteInt32(buf, ref o, s.StreamUpBps);
                WriteInt16(buf, ref o, ClampShort(s.GwRttMs));
                WriteInt16(buf, ref o, ClampShort(s.GwJitMs));
                WriteByte(buf, ref o, ClampByte(s.GwLossPm));
                WriteInt16(buf, ref o, ClampShort(s.PubRttMs));
                WriteByte(buf, ref o, ClampByte(s.PubLossPm));
                WriteInt16(buf, ref o, ClampShort(s.SrvRttMs));
                WriteInt16(buf, ref o, ClampShort(s.SrvJitMs));
                WriteByte(buf, ref o, ClampByte(s.SrvLossPm));
                WriteInt16(buf, ref o, ClampShort(s.DnsMs));
                WriteInt16(buf, ref o, ClampShort(s.RetransPm));
                WriteInt16(buf, ref o, ClampShort(s.CwndKb));
                WriteByte(buf, ref o, ClampByte(s.RtoCount));
                WriteByte(buf, ref o, ClampByte(s.SynRetrans));
                WriteInt16(buf, ref o, ClampShort(s.TcpConn));
                WriteByte(buf, ref o, ClampByte(s.NewConnPer10));
                WriteByte(buf, ref o, ClampByte(s.CpuTotalPm / 10));
                WriteByte(buf, ref o, ClampByte(s.CpuStreamPm < 0 ? -1 : s.CpuStreamPm / 10));
                WriteByte(buf, ref o, ClampByte(s.GpuEncPm < 0 ? -1 : s.GpuEncPm / 10));
                WriteByte(buf, ref o, ClampByte(s.Gpu3dPm < 0 ? -1 : s.Gpu3dPm / 10));
                WriteByte(buf, ref o, ClampByte(s.MemPm / 10));
                WriteByte(buf, ref o, ClampByte(s.DiskQPm10 < 0 ? -1 : s.DiskQPm10 / 10));
                WriteByte(buf, ref o, (byte)(s.WifiRssi <= -1000 ? 200 : (s.WifiRssi < 0 ? 100 + (s.WifiRssi + 100) : 100)));
                WriteByte(buf, ref o, ClampByte(s.WifiTx10 < 0 ? -1 : s.WifiTx10 / 10));
                WriteByte(buf, ref o, ClampByte(s.LinkUtilPm));
                WriteByte(buf, ref o, ClampByte(s.OtherUpPm < 0 ? -1 : s.OtherUpPm / 10));
                WriteByte(buf, ref o, ClampByte(s.Quality));
                WriteByte(buf, ref o, RecordVersion);          // 59：格式版本，旧格式此处恒为 0
                WriteUInt32(buf, ref o, HopIpToUint(s.HopIp)); // 60：接入跳地址
                WriteInt16(buf, ref o, ClampShort(s.HopRttMs));// 64：接入跳 RTT
                WriteInt16(buf, ref o, ClampShort(s.HopJitMs));// 66：接入跳抖动
                WriteByte(buf, ref o, ClampByte(s.HopLossPm)); // 68：接入跳丢包
                WriteByte(buf, ref o, ClampByte(s.HopTtl));    // 69：跳序号
                WriteInt16(buf, ref o, 0);                     // 70：保留

                // 每条采样立即落盘：每分钟仅 12 条 60 字节，代价可忽略，
                // 但能保证断电/强杀后"可溯源记录"不丢数据。
                _smpWriter.Write(buf);
                _smpWriter.Flush();
                _smp.Flush(true);
            }
            catch { }
        }

        private void CloseSampleFile()
        {
            try
            {
                if (_smpWriter != null) { _smpWriter.Flush(); _smpWriter.Dispose(); }
                if (_smp != null) _smp.Dispose();
            }
            catch { }
            _smpWriter = null;
            _smp = null;
        }

        public List<Sample> LoadSamples(long fromMs, long toMs)
        {
            List<Sample> list = new List<Sample>();
            try
            {
                CloseSampleFile();
                if (!Directory.Exists(Config.SampleDir)) return list;
                DateTime from = Ux.ToLocalMs(fromMs).Date.AddDays(-1);
                DateTime to = Ux.ToLocalMs(toMs).Date.AddDays(1);
                foreach (string f in Directory.GetFiles(Config.SampleDir, "*.smp"))
                {
                    // 文件名形如 2026-11-26.smp（旧格式）或 2026-11-26.v2.smp（新格式）
                    string name = Path.GetFileNameWithoutExtension(f);
                    int dot = name.IndexOf('.');
                    if (dot > 0) name = name.Substring(0, dot);
                    DateTime day;
                    if (!DateTime.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)) continue;
                    if (day < from || day > to) continue;

                    byte[] all = File.ReadAllBytes(f);
                    int rs = DetectRecordSize(all, f.IndexOf(".v2.", StringComparison.OrdinalIgnoreCase) >= 0);
                    int n = all.Length / rs;
                    for (int i = 0; i < n; i++)
                    {
                        Sample s = Unpack(all, i * rs, rs);
                        if (!PlausibleTs(s.Ts)) continue;   // 兜底：跳过格式错位导致的垃圾记录
                        if (s.Ts * 1000L >= fromMs - 60000 && s.Ts * 1000L <= toMs + 60000) list.Add(s);
                    }
                }
                list.Sort(delegate(Sample a, Sample b) { return a.Ts.CompareTo(b.Ts); });
            }
            catch { }
            return list;
        }

        /// <summary>判断记录长度：新格式文件名带 .v2 且第 59 字节写版本号 2，旧格式为 60 字节。</summary>
        private static int DetectRecordSize(byte[] all, bool v2Name)
        {
            if (v2Name && all.Length >= RecordSize && all.Length % RecordSize == 0) return RecordSize;
            bool v2 = all.Length >= RecordSize && all.Length % RecordSize == 0 && all[59] == RecordVersion;
            if (v2) return RecordSize;
            if (all.Length >= LegacyRecordSize && all.Length % LegacyRecordSize == 0) return LegacyRecordSize;
            return RecordSize;
        }

        /// <summary>时间戳合理性检查（2000-01-01 ~ 当前+1 天），用于丢弃格式错位产生的垃圾记录。</summary>
        private static bool PlausibleTs(long ts)
        {
            if (ts < 946684800L) return false;
            if (ts > Ux.Now() + 86400L) return false;
            return true;
        }

        private static Sample Unpack(byte[] b, int o, int recordSize)
        {
            Sample s = new Sample();
            s.Ts = ReadInt64(b, o);
            s.Flags = (SampleFlags)BitConverter.ToInt32(b, o + 8);
            s.UpBps = BitConverter.ToInt32(b, o + 12);
            s.DownBps = BitConverter.ToInt32(b, o + 16);
            s.StreamUpBps = BitConverter.ToInt32(b, o + 20);
            s.GwRttMs = BitConverter.ToInt16(b, o + 24);
            s.GwJitMs = BitConverter.ToInt16(b, o + 26);
            s.GwLossPm = b[o + 28];
            s.PubRttMs = BitConverter.ToInt16(b, o + 29);
            s.PubLossPm = b[o + 31];
            s.SrvRttMs = BitConverter.ToInt16(b, o + 32);
            s.SrvJitMs = BitConverter.ToInt16(b, o + 34);
            s.SrvLossPm = b[o + 36];
            s.DnsMs = BitConverter.ToInt16(b, o + 37);
            s.RetransPm = BitConverter.ToInt16(b, o + 39);
            s.CwndKb = BitConverter.ToInt16(b, o + 41);
            s.RtoCount = b[o + 43];
            s.SynRetrans = b[o + 44];
            s.TcpConn = BitConverter.ToInt16(b, o + 45);
            s.NewConnPer10 = b[o + 47];
            s.CpuTotalPm = b[o + 48] * 10;
            int cs = b[o + 49];
            s.CpuStreamPm = cs == 255 ? -1 : cs * 10;
            int ge = b[o + 50];
            s.GpuEncPm = ge == 255 ? -1 : ge * 10;
            int g3 = b[o + 51];
            s.Gpu3dPm = g3 == 255 ? -1 : g3 * 10;
            s.MemPm = b[o + 52] * 10;
            int dq = b[o + 53];
            s.DiskQPm10 = dq == 255 ? -1 : dq * 10;
            int wr = b[o + 54];
            s.WifiRssi = wr == 200 ? -1000 : (wr >= 100 ? wr - 200 : 100 - wr);
            int tx = b[o + 55];
            s.WifiTx10 = tx == 255 ? -1 : tx * 10;
            s.LinkUtilPm = b[o + 56];
            int ou = b[o + 57];
            s.OtherUpPm = ou == 255 ? -1 : ou * 10;
            s.Quality = b[o + 58];
            if (recordSize >= RecordSize)
            {
                uint hop = BitConverter.ToUInt32(b, o + 60);
                s.HopIp = hop == 0 ? "" : Native.UintToIp(hop);
                s.HopRttMs = BitConverter.ToInt16(b, o + 64);
                s.HopJitMs = BitConverter.ToInt16(b, o + 66);
                s.HopLossPm = b[o + 68];
                s.HopTtl = b[o + 69];
            }
            return s;
        }

        private static uint HopIpToUint(string ip)
        {
            System.Net.IPAddress parsed;
            if (string.IsNullOrEmpty(ip)) return 0;
            if (!System.Net.IPAddress.TryParse(ip, out parsed)) return 0;
            if (parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return 0;
            return Native.ToNetUint(parsed.GetAddressBytes());
        }

        // ---------------- 事件 ----------------

        public void AppendEvent(EventRecord e)
        {
            try
            {
                string day = Ux.ToLocal(e.Ts).ToString("yyyy-MM-dd");
                if (_evt == null || day != _evtDay)
                {
                    CloseEventFile();
                    Directory.CreateDirectory(Config.EventDir);
                    _evt = new FileStream(Path.Combine(Config.EventDir, day + ".evt"), FileMode.Append, FileAccess.Write, FileShare.Read);
                    _evtWriter = new StreamWriter(_evt, new UTF8Encoding(false));
                    _evtDay = day;
                }
                _evtWriter.WriteLine(string.Join("\t", new string[] {
                    e.Ts.ToString(), Clean(e.SessionId), Clean(e.Code), e.Severity.ToString(),
                    Clean(e.Title), Clean(e.Detail), Clean(e.Evidence) }));
                _evtWriter.Flush();
                if (_evt != null) _evt.Flush();
            }
            catch { }
        }

        private void CloseEventFile()
        {
            try
            {
                if (_evtWriter != null) { _evtWriter.Flush(); _evtWriter.Dispose(); }
                if (_evt != null) _evt.Dispose();
            }
            catch { }
            _evtWriter = null;
            _evt = null;
        }

        public List<EventRecord> LoadEvents(long fromMs, long toMs, string sessionId)
        {
            List<EventRecord> list = new List<EventRecord>();
            try
            {
                CloseEventFile();
                if (!Directory.Exists(Config.EventDir)) return list;
                DateTime from = Ux.ToLocalMs(fromMs).Date.AddDays(-1);
                DateTime to = Ux.ToLocalMs(toMs).Date.AddDays(1);
                foreach (string f in Directory.GetFiles(Config.EventDir, "*.evt"))
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    DateTime day;
                    if (!DateTime.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)) continue;
                    if (day < from || day > to) continue;
                    foreach (string line in File.ReadAllLines(f, Encoding.UTF8))
                    {
                        if (line.Length == 0) continue;
                        string[] p = line.Split('\t');
                        if (p.Length < 6) continue;
                        EventRecord e = new EventRecord();
                        long ts;
                        long.TryParse(p[0], out ts);
                        e.Ts = ts;
                        e.SessionId = p[1];
                        e.Code = p[2];
                        int sev;
                        int.TryParse(p[3], out sev);
                        e.Severity = sev;
                        e.Title = p[4];
                        e.Detail = p[5];
                        e.Evidence = p.Length > 6 ? p[6] : "";
                        if (sessionId != null && sessionId.Length > 0 && e.SessionId != sessionId) continue;
                        if (ts * 1000L < fromMs - 60000 || ts * 1000L > toMs + 60000) continue;
                        list.Add(e);
                    }
                }
                list.Sort(delegate(EventRecord a, EventRecord b) { return a.Ts.CompareTo(b.Ts); });
            }
            catch { }
            return list;
        }

        // ---------------- 会话索引 ----------------

        public List<SessionInfo> LoadSessions()
        {
            List<SessionInfo> list = new List<SessionInfo>();
            try
            {
                if (!File.Exists(Config.SessionIndexPath)) return list;
                foreach (string line in File.ReadAllLines(Config.SessionIndexPath, Encoding.UTF8))
                {
                    if (line.Trim().Length == 0 || line.StartsWith("#")) continue;
                    string[] p = line.Split('\t');
                    if (p.Length < 12) continue;
                    SessionInfo s = new SessionInfo();
                    s.Id = p[0];
                    long.TryParse(p[1], out s.StartTs);
                    long.TryParse(p[2], out s.EndTs);
                    s.EndReason = p[3];
                    int.TryParse(p[4], out s.Score);
                    s.TopCause = p[5];
                    s.StreamProc = p[6];
                    s.ServerIp = p[7];
                    s.LocalIp = p[8];
                    s.AdapterName = p[9];
                    s.Manual = p[10] == "1";
                    long peak;
                    long.TryParse(p[11], out peak);
                    s.PeakUpBps = peak;
                    if (p.Length > 12) int.TryParse(p[12], out s.SampleCount);
                    if (p.Length > 13) int.TryParse(p[13], out s.GapCount);
                    if (p.Length > 14) long.TryParse(p[14], out s.GapTotalMs);
                    if (p.Length > 15) long.TryParse(p[15], out s.AvgUpBps);
                    if (p.Length > 16) s.ReportPath = p[16];
                    if (p.Length > 17) s.Note = p[17];
                    list.Add(s);
                }
                list.Sort(delegate(SessionInfo a, SessionInfo b) { return b.StartTs.CompareTo(a.StartTs); });
            }
            catch { }
            return list;
        }

        public void SaveSessions(List<SessionInfo> list)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# id\tstartTs\tendTs\tendReason\tscore\ttopCause\tstreamProc\tserverIp\tlocalIp\tadapter\tmanual\tpeakUp\tsampleCount\tgapCount\tgapTotalMs\tavgUp\treportPath\tnote");
                foreach (SessionInfo s in list)
                {
                    sb.AppendLine(string.Join("\t", new string[] {
                        s.Id, s.StartTs.ToString(), s.EndTs.ToString(), Clean(s.EndReason), s.Score.ToString(),
                        Clean(s.TopCause), Clean(s.StreamProc), Clean(s.ServerIp), Clean(s.LocalIp), Clean(s.AdapterName),
                        s.Manual ? "1" : "0", s.PeakUpBps.ToString(), s.SampleCount.ToString(), s.GapCount.ToString(),
                        s.GapTotalMs.ToString(), s.AvgUpBps.ToString(), Clean(s.ReportPath), Clean(s.Note) }));
                }
                File.WriteAllText(Config.SessionIndexPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        public void UpsertSession(SessionInfo info)
        {
            List<SessionInfo> list = LoadSessions();
            bool found = false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id == info.Id) { list[i] = info; found = true; break; }
            }
            if (!found) list.Add(info);
            SaveSessions(list);
        }

        // ---------------- 学习库 ----------------

        public Dictionary<string, int> LoadLearning()
        {
            Dictionary<string, int> dic = new Dictionary<string, int>();
            try
            {
                if (!File.Exists(Config.LearningPath)) return dic;
                foreach (string line in File.ReadAllLines(Config.LearningPath, Encoding.UTF8))
                {
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    string[] p = line.Split('\t');
                    if (p.Length < 2) continue;
                    int v;
                    int.TryParse(p[1], out v);
                    dic[p[0]] = v;
                }
            }
            catch { }
            return dic;
        }

        public void SaveLearning(Dictionary<string, int> dic)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# 直播识别学习库：进程|判定(1直播/0非直播)\t判定值");
                foreach (KeyValuePair<string, int> kv in dic) sb.AppendLine(Clean(kv.Key) + "\t" + kv.Value);
                File.WriteAllText(Config.LearningPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        // ---------------- 清理 ----------------

        public void Cleanup(Config cfg)
        {
            try
            {
                CloseSampleFile();
                CloseEventFile();
                DateTime rawLimit = DateTime.Now.Date.AddDays(-cfg.RawRetentionDays);
                foreach (string f in Directory.GetFiles(Config.SampleDir, "*.smp"))
                    if (File.GetLastWriteTime(f) < rawLimit) TryDelete(f);
                foreach (string f in Directory.GetFiles(Config.EventDir, "*.evt"))
                    if (File.GetLastWriteTime(f) < rawLimit) TryDelete(f);
                if (Directory.Exists(Config.ReportDir))
                {
                    DateTime repLimit = DateTime.Now.Date.AddDays(-cfg.ReportRetentionDays);
                    foreach (string f in Directory.GetFiles(Config.ReportDir, "*.html"))
                        if (File.GetLastWriteTime(f) < repLimit) TryDelete(f);
                }
            }
            catch { }
        }

        private static void TryDelete(string f)
        {
            try { File.Delete(f); } catch { }
        }

        public void Dispose()
        {
            CloseSampleFile();
            CloseEventFile();
        }

        // ---------------- 工具 ----------------

        private static string Clean(string s)
        {
            if (s == null) return "";
            return s.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
        }

        private static int ClampShort(int v)
        {
            if (v < short.MinValue) return short.MinValue;
            if (v > short.MaxValue) return short.MaxValue;
            return v;
        }

        private static byte ClampByte(int v)
        {
            if (v < 0) return 0;
            if (v > 254) return 254;
            return (byte)v;
        }

        private static void WriteInt64(byte[] b, ref int o, long v) { BitConverter.GetBytes(v).CopyTo(b, o); o += 8; }
        private static void WriteInt32(byte[] b, ref int o, int v) { BitConverter.GetBytes(v).CopyTo(b, o); o += 4; }
        private static void WriteUInt32(byte[] b, ref int o, uint v) { BitConverter.GetBytes(v).CopyTo(b, o); o += 4; }
        private static void WriteInt16(byte[] b, ref int o, int v) { BitConverter.GetBytes((short)v).CopyTo(b, o); o += 2; }
        private static void WriteByte(byte[] b, ref int o, int v) { b[o] = (byte)v; o += 1; }
        private static long ReadInt64(byte[] b, int o) { return BitConverter.ToInt64(b, o); }
    }
}
