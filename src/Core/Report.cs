using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LiveDiag
{
    /// <summary>生成自包含 HTML 报告、CSV 与 ZIP 数据包（全部离线可看）。</summary>
    public static class Report
    {
        public static string Build(SessionInfo info, List<Sample> samples, List<Finding> findings, Store store, bool degraded)
        {
            try
            {
                Directory.CreateDirectory(Config.ReportDir);
                string path = Path.Combine(Config.ReportDir, info.Id + ".html");
                File.WriteAllText(path, BuildHtml(info, samples, findings, store, degraded), new UTF8Encoding(true));
                info.ReportPath = path;
                return path;
            }
            catch (Exception ex)
            {
                Engine.WriteLog("report build failed: " + ex);
                return "";
            }
        }

        private static string BuildHtml(SessionInfo info, List<Sample> samples, List<Finding> findings, Store store, bool degraded)
        {
            StringBuilder sb = new StringBuilder();
            Baseline bl = Analyzer.BuildBaseline(samples);
            int minScore = 100, maxScore = 0, scoreSum = 0, scoreCount = 0;
            foreach (Sample s in samples)
            {
                if (s.Quality < 0) continue;
                if (s.Quality < minScore) minScore = s.Quality;
                if (s.Quality > maxScore) maxScore = s.Quality;
                scoreSum += s.Quality;
                scoreCount++;
            }
            int avgScore = scoreCount > 0 ? scoreSum / scoreCount : 0;

            // 接入网第一跳地址：取窗口内最后一个有效值
            string hopIp = "";
            int hopTtl = 0, maxLossHop = 0, maxHopRtt = 0;
            foreach (Sample s in samples)
            {
                if (s.HopIp.Length > 0) { hopIp = s.HopIp; hopTtl = s.HopTtl; }
                if (s.HopLossPm > maxLossHop) maxLossHop = s.HopLossPm;
                if (s.HopRttMs > maxHopRtt) maxHopRtt = s.HopRttMs;
            }

            DateTime start = Ux.ToLocal(info.StartTs);
            DateTime end = Ux.ToLocal(Math.Max(info.EndTs, info.StartTs));

            sb.AppendLine("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
            sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            sb.AppendLine("<title>直播质量诊断报告 " + info.Id + "</title>");
            sb.AppendLine("<style>");
            sb.AppendLine(Css);
            sb.AppendLine("</style></head><body><div class=\"wrap\">");

            // 头部
            sb.AppendLine("<div class=\"head\">");
            sb.AppendLine("<div class=\"head-l\"><div class=\"kicker\">直播质量诊断报告</div>");
            sb.AppendLine("<h1>" + H(info.StreamProc.Length > 0 ? info.StreamProc : "未识别推流程序") + "</h1>");
            sb.AppendLine("<div class=\"sub\">" + start.ToString("yyyy-MM-dd HH:mm:ss") + " — " + end.ToString("HH:mm:ss") +
                " &nbsp;·&nbsp; 时长 " + info.DurationText + " &nbsp;·&nbsp; 会话 " + H(info.Id) + "</div></div>");
            sb.AppendLine("<div class=\"score " + ScoreClass(avgScore) + "\"><div class=\"num\">" + avgScore + "</div><div class=\"unit\">质量分</div></div>");
            sb.AppendLine("</div>");

            // 标签
            sb.AppendLine("<div class=\"tags\">");
            sb.AppendLine("<span class=\"tag\">结束原因：" + H(info.EndReason) + "</span>");
            if (info.Manual) sb.AppendLine("<span class=\"tag\">手动记录</span>");
            if (degraded) sb.AppendLine("<span class=\"tag warn\">估算模式（无逐连接统计）</span>");
            if (info.GapCount > 0) sb.AppendLine("<span class=\"tag bad\">断流 " + info.GapCount + " 次 / 累计 " + Fmt.ShortDur(info.GapTotalMs) + "</span>");
            else sb.AppendLine("<span class=\"tag ok\">全程无断流</span>");
            sb.AppendLine("</div>");

            // KPI
            sb.AppendLine("<div class=\"kpis\">");
            Kpi(sb, "上行峰值", Fmt.Bps((int)info.PeakUpBps));
            Kpi(sb, "上行均值", Fmt.Bps((int)info.AvgUpBps));
            Kpi(sb, "质量分区间", minScore + " ~ " + maxScore);
            Kpi(sb, "推流服务器", info.ServerIp.Length > 0 ? info.ServerIp : "-");
            Kpi(sb, "本机 IP", info.LocalIp.Length > 0 ? info.LocalIp : "-");
            Kpi(sb, "网络接口", info.AdapterName.Length > 0 ? info.AdapterName : "-");
            Kpi(sb, "网关基线 RTT", bl.GwRtt + " ms");
            Kpi(sb, "接入跳（网关后第一跳）", hopIp.Length > 0 ? hopIp + (hopTtl > 0 ? "（第" + hopTtl + "跳）" : "") : "未取到");
            Kpi(sb, "接入跳基线 RTT", bl.HopRtt > 0 ? bl.HopRtt + " ms" : "-");
            Kpi(sb, "接入跳 RTT 峰值", maxHopRtt > 0 ? maxHopRtt + " ms" : "-");
            Kpi(sb, "接入跳丢包峰值", maxLossHop <= 0 ? "0" : (maxLossHop >= 254 ? "≥" + Fmt.Pm(254) : Fmt.Pm(maxLossHop)));
            Kpi(sb, "推流节点基线 RTT", bl.SrvRtt + " ms");
            sb.AppendLine("</div>");

            // 结论
            sb.AppendLine("<h2>定界结论</h2>");
            if (findings == null || findings.Count == 0)
            {
                sb.AppendLine("<div class=\"card\"><div class=\"card-t\">本次会话未发现明显异常</div><div class=\"card-d\">网络与主机各项指标均在正常范围，若客户仍反馈卡顿，问题更可能在观众侧 CDN 或平台转码。</div></div>");
            }
            else
            {
                int idx = 0;
                foreach (Finding f in findings)
                {
                    idx++;
                    if (idx > 5) break;
                    sb.AppendLine("<div class=\"card sev" + f.Severity + "\">");
                    sb.AppendLine("<div class=\"card-h\"><span class=\"rank\">" + idx + "</span><span class=\"card-t\">" + H(f.Title) + "</span>");
                    sb.AppendLine("<span class=\"conf\">置信度 " + f.Confidence + "%</span></div>");
                    sb.AppendLine("<div class=\"card-d\">" + H(f.Detail) + "</div>");
                    if (f.Advice.Length > 0) sb.AppendLine("<div class=\"card-a\">建议：" + H(f.Advice) + "</div>");
                    sb.AppendLine("<div class=\"card-e\">证据：" + Analyzer.TimeRange(f.FromTs, f.ToTs) + "　" + H(f.Evidence) + "</div>");
                    sb.AppendLine("</div>");
                }
            }

            // 图表
            sb.AppendLine("<h2>指标曲线</h2>");
            sb.AppendLine("<div class=\"chartbox\"><div class=\"chart-t\">推流上行（bps）</div><canvas id=\"c1\" height=\"120\"></canvas></div>");
            sb.AppendLine("<div class=\"chartbox\"><div class=\"chart-t\">RTT：推流节点 / 接入跳（网关后第一跳）/ 网关 / 公网参考（ms）</div><canvas id=\"c2\" height=\"120\"></canvas></div>");
            sb.AppendLine("<div class=\"chartbox\"><div class=\"chart-t\">丢包率：接入跳（‰）</div><canvas id=\"c5\" height=\"100\"></canvas></div>");
            sb.AppendLine("<div class=\"chartbox\"><div class=\"chart-t\">丢包率（‰）与 TCP 连接数</div><canvas id=\"c3\" height=\"120\"></canvas></div>");
            sb.AppendLine("<div class=\"chartbox\"><div class=\"chart-t\">CPU % / 内存 % / 质量分</div><canvas id=\"c4\" height=\"120\"></canvas></div>");

            // 事件
            sb.AppendLine("<h2>事件时间线</h2>");
            sb.AppendLine("<table><thead><tr><th style=\"width:90px\">时间</th><th style=\"width:70px\">级别</th><th style=\"width:110px\">代码</th><th>事件</th><th>详情</th></tr></thead><tbody>");
            List<EventRecord> events = store != null
                ? store.LoadEvents(info.StartTs * 1000L - 60000, Math.Max(info.EndTs, info.StartTs) * 1000L + 60000, info.Id)
                : new List<EventRecord>();
            if (events.Count == 0)
                sb.AppendLine("<tr><td colspan=\"5\" class=\"muted\">无记录</td></tr>");
            foreach (EventRecord e in events)
            {
                string cls = e.Severity >= 2 ? "sev-bad" : (e.Severity == 1 ? "sev-warn" : "");
                sb.AppendLine("<tr class=\"" + cls + "\"><td>" + e.TimeText + "</td><td>" + e.SeverityText + "</td><td class=\"mono\">" + H(e.Code) + "</td><td>" + H(e.Title) + "</td><td>" + H(e.Detail) + (e.Evidence.Length > 0 ? "<br><span class=\"muted\">" + H(e.Evidence) + "</span>" : "") + "</td></tr>");
            }
            sb.AppendLine("</tbody></table>");

            // 数据说明
            sb.AppendLine("<h2>数据与口径说明</h2>");
            sb.AppendLine("<div class=\"note\">");
            sb.AppendLine("<p>采样间隔 " + (samples.Count > 1 ? ((samples[1].Ts - samples[0].Ts)) : 5) + " 秒，本次共 " + samples.Count + " 条记录。原始数据保存在程序 data 目录，可用“导出数据包”一并取走。</p>");
            sb.AppendLine("<p>会话界定：检测到直播上行流量开始计时；流量中断超过 5 分钟判定本次直播结束。" + (degraded ? "本次运行未取得逐连接统计权限，推流上行按整机上行估算，可能包含少量其他程序流量。" : "") + "</p>");
            sb.AppendLine("<p>本报告只记录链路与主机指标，不采集任何画面、音频或内容数据。观众侧 CDN 卡顿不在本机可观测范围。</p>");
            sb.AppendLine("</div>");

            sb.AppendLine("<div class=\"foot\">" + Program.AppName + " " + Program.Version + " &nbsp;·&nbsp; 生成于 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "</div>");
            sb.AppendLine("</div>");

            sb.AppendLine("<script>var D=" + BuildJson(samples) + ";");
            sb.AppendLine(Js);
            sb.AppendLine("</script></body></html>");
            return sb.ToString();
        }

        private static string ScoreClass(int score)
        {
            if (score >= 85) return "good";
            if (score >= 65) return "mid";
            return "bad";
        }

        private static void Kpi(StringBuilder sb, string label, string value)
        {
            sb.AppendLine("<div class=\"kpi\"><div class=\"kpi-l\">" + H(label) + "</div><div class=\"kpi-v\">" + H(value) + "</div></div>");
        }

        private static string BuildJson(List<Sample> samples)
        {
            int step = Math.Max(1, samples.Count / 900);
            StringBuilder sb = new StringBuilder("[");
            bool first = true;
            for (int i = 0; i < samples.Count; i += step)
            {
                Sample s = samples[i];
                if (!first) sb.Append(",");
                first = false;
                sb.Append("{t:" + s.Ts + ",up:" + Math.Max(0, s.StreamUpBps) + ",srv:" + s.SrvRttMs + ",gw:" + s.GwRttMs +
                    ",hop:" + s.HopRttMs + ",hl:" + s.HopLossPm +
                    ",pub:" + s.PubRttMs + ",sl:" + s.SrvLossPm + ",gl:" + s.GwLossPm + ",q:" + s.Quality +
                    ",cpu:" + s.CpuTotalPm + ",mem:" + s.MemPm + ",conn:" + s.TcpConn + ",gap:" + (((s.Flags & SampleFlags.InGap) != 0) ? 1 : 0) + "}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        public static void ExportCsv(string path, List<Sample> samples)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("时间,整机上行bps,整机下行bps,推流上行bps,网关RTTms,网关丢包‰,接入跳IP,接入跳RTTms,接入跳丢包‰,公网RTTms,公网丢包‰,推流RTTms,推流丢包‰,推流抖动ms,DNSms,TCP连接数,SYN_SENT,新建连接每秒×10,CPU‰,推流进程CPU‰,内存‰,磁盘队列×10,WiFi信号dBm,链路利用率‰,其他上行‰,质量分,状态");
            foreach (Sample s in samples)
            {
                sb.Append(Ux.ToLocal(s.Ts).ToString("yyyy-MM-dd HH:mm:ss")).Append(',');
                sb.Append(s.UpBps).Append(',').Append(s.DownBps).Append(',').Append(s.StreamUpBps).Append(',');
                sb.Append(s.GwRttMs).Append(',').Append(s.GwLossPm).Append(',');
                sb.Append(s.HopIp).Append(',').Append(s.HopRttMs).Append(',').Append(s.HopLossPm).Append(',');
                sb.Append(s.PubRttMs).Append(',').Append(s.PubLossPm).Append(',');
                sb.Append(s.SrvRttMs).Append(',').Append(s.SrvLossPm).Append(',').Append(s.SrvJitMs).Append(',').Append(s.DnsMs).Append(',');
                sb.Append(s.TcpConn).Append(',').Append(s.SynRetrans).Append(',').Append(s.NewConnPer10).Append(',');
                sb.Append(s.CpuTotalPm).Append(',').Append(s.CpuStreamPm).Append(',').Append(s.MemPm).Append(',').Append(s.DiskQPm10).Append(',');
                sb.Append(s.WifiRssi <= -1000 ? "" : s.WifiRssi.ToString()).Append(',').Append(s.LinkUtilPm).Append(',');
                sb.Append(s.OtherUpPm).Append(',').Append(s.Quality).Append(',');
                sb.Append((s.Flags & SampleFlags.InGap) != 0 ? "断流" : ((s.Flags & SampleFlags.SessionRunning) != 0 ? "直播中" : "无会话"));
                sb.AppendLine();
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        public static string ExportZip(SessionInfo info, List<Sample> samples, List<EventRecord> events)
        {
            try
            {
                Directory.CreateDirectory(Config.ReportDir);
                string path = Path.Combine(Config.ReportDir, "S" + info.Id + "_数据包.zip");
                using (ZipWriter z = new ZipWriter(path))
                {
                    if (info.ReportPath.Length > 0 && File.Exists(info.ReportPath))
                        z.AddFile("报告.html", info.ReportPath);
                    else
                        z.AddText("报告.html", "<meta charset=\"utf-8\">报告需要先生在程序中生成");

                    StringBuilder csv = new StringBuilder();
                    string tmp = Path.Combine(Path.GetTempPath(), "livediag_" + Guid.NewGuid().ToString("N") + ".csv");
                    ExportCsv(tmp, samples);
                    z.AddFile("采样数据.csv", tmp);
                    try { File.Delete(tmp); } catch { }

                    StringBuilder ev = new StringBuilder();
                    ev.AppendLine("时间\t级别\t代码\t事件\t详情\t证据");
                    foreach (EventRecord e in events)
                        ev.AppendLine(e.TimeText + "\t" + e.SeverityText + "\t" + e.Code + "\t" + e.Title + "\t" + e.Detail + "\t" + e.Evidence);
                    z.AddText("事件.csv", ev.ToString());

                    StringBuilder readme = new StringBuilder();
                    readme.AppendLine("直播诊断 数据包");
                    readme.AppendLine("会话：" + info.Id);
                    readme.AppendLine("开始：" + info.StartText);
                    readme.AppendLine("结束原因：" + info.EndReason);
                    readme.AppendLine("时长：" + info.DurationText);
                    readme.AppendLine("质量分：" + info.Score);
                    readme.AppendLine("首要原因：" + info.TopCause);
                    readme.AppendLine("推流进程：" + info.StreamProc);
                    readme.AppendLine("推流服务器：" + info.ServerIp);
                    readme.AppendLine();
                    readme.AppendLine("包含：报告.html（离线可看）、采样数据.csv（每 5 秒一条）、事件.csv");
                    z.AddText("说明.txt", readme.ToString());
                }
                return path;
            }
            catch (Exception ex)
            {
                Engine.WriteLog("export zip failed: " + ex);
                return "";
            }
        }

        private static string H(string s)
        {
            if (s == null) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private const string Css = @"
*{box-sizing:border-box}
body{margin:0;background:#f6f7f9;color:#1d2430;font:14px/1.6 -apple-system,'Segoe UI','Microsoft YaHei UI',sans-serif}
.wrap{max-width:1080px;margin:0 auto;padding:28px 20px 60px}
.head{display:flex;align-items:flex-end;justify-content:space-between;gap:20px;background:#fff;border:1px solid #e6e9ef;border-radius:14px;padding:22px 24px}
.kicker{font-size:12px;letter-spacing:2px;color:#7b8698;text-transform:uppercase}
h1{margin:6px 0 4px;font-size:22px;font-weight:600}
.sub{color:#6b7688;font-size:13px}
.score{min-width:110px;text-align:center;border-radius:12px;padding:10px 14px;color:#fff}
.score .num{font-size:34px;font-weight:700;line-height:1.1}
.score .unit{font-size:12px;opacity:.85}
.good{background:linear-gradient(135deg,#2fae7a,#1f8f63)}
.mid{background:linear-gradient(135deg,#e2a33c,#c9861f)}
.bad{background:linear-gradient(135deg,#e2603c,#c24325)}
.tags{margin:14px 0 6px;display:flex;flex-wrap:wrap;gap:8px}
.tag{background:#fff;border:1px solid #e6e9ef;border-radius:20px;padding:4px 12px;font-size:12.5px;color:#4c586c}
.tag.ok{border-color:#bfe6d5;color:#1f8f63;background:#f2fbf7}
.tag.bad{border-color:#f3c9bd;color:#c24325;background:#fdf4f1}
.tag.warn{border-color:#f0dcb4;color:#a9720f;background:#fdf9f0}
.kpis{display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:10px;margin:16px 0 6px}
.kpi{background:#fff;border:1px solid #e6e9ef;border-radius:10px;padding:12px 14px}
.kpi-l{font-size:12px;color:#7b8698}
.kpi-v{font-size:16px;font-weight:600;margin-top:2px;word-break:break-all}
h2{font-size:15px;color:#3d4859;margin:26px 0 10px;font-weight:600}
.card{background:#fff;border:1px solid #e6e9ef;border-left:4px solid #c9d1de;border-radius:12px;padding:14px 18px;margin-bottom:10px}
.card.sev2{border-left-color:#e2603c}
.card.sev1{border-left-color:#e2a33c}
.card-h{display:flex;align-items:center;gap:10px}
.rank{width:22px;height:22px;border-radius:50%;background:#eef1f6;color:#5a6577;font-size:12px;display:flex;align-items:center;justify-content:center;flex:none}
.card-t{font-weight:600;font-size:15px}
.conf{margin-left:auto;font-size:12px;color:#7b8698;background:#f3f5f8;border-radius:10px;padding:2px 10px}
.card-d{color:#4c586c;margin-top:8px}
.card-a{margin-top:8px;color:#1f6f52;background:#f2fbf7;border-radius:8px;padding:8px 12px;font-size:13px}
.card-e{margin-top:8px;color:#8b95a6;font-size:12px;font-family:ui-monospace,Consolas,monospace}
.chartbox{background:#fff;border:1px solid #e6e9ef;border-radius:12px;padding:14px 16px;margin-bottom:10px}
.chart-t{font-size:12.5px;color:#7b8698;margin-bottom:6px}
canvas{width:100%;display:block}
table{width:100%;border-collapse:collapse;background:#fff;border:1px solid #e6e9ef;border-radius:12px;overflow:hidden;font-size:13px}
th{text-align:left;background:#f3f5f8;color:#5a6577;font-weight:600;padding:9px 12px;border-bottom:1px solid #e6e9ef}
td{padding:8px 12px;border-bottom:1px solid #f0f2f6;vertical-align:top}
tr.sev-bad td{background:#fdf4f1}
tr.sev-warn td{background:#fdf9f0}
.muted{color:#8b95a6}
.mono{font-family:ui-monospace,Consolas,monospace;font-size:12px}
.note{background:#fff;border:1px solid #e6e9ef;border-radius:12px;padding:6px 18px;color:#5a6577;font-size:13px}
.foot{margin-top:28px;text-align:center;color:#9aa4b4;font-size:12px}
";

        private const string Js = @"
function fmtBps(v){if(v<=0)return '0';if(v<1e6)return (v/1e3).toFixed(0)+'K';return (v/1e6).toFixed(1)+'M';}
function fmtT(t){var d=new Date(t*1000);function p(n){return n<10?'0'+n:''+n;}return p(d.getHours())+':'+p(d.getMinutes());}
function draw(id,series,opt){
  var c=document.getElementById(id);if(!c)return;
  var dpr=window.devicePixelRatio||1;
  var w=c.clientWidth||800,h=120;
  c.width=w*dpr;c.height=h*dpr;c.style.height=h+'px';
  var g=c.getContext('2d');g.scale(dpr,dpr);g.clearRect(0,0,w,h);
  var pad={l:46,r:10,t:10,b:18};
  var iw=w-pad.l-pad.r,ih=h-pad.t-pad.b;
  var max=0,min=0;
  series.forEach(function(s){D.forEach(function(p){var v=p[s.key];if(v===undefined||v===null||v<0)return;if(v>max)max=v;if(v<min)min=v;});});
  if(opt&&opt.forceMax&&max<opt.forceMax)max=opt.forceMax;
  if(max===0)max=1;
  g.strokeStyle='#eef1f6';g.lineWidth=1;
  for(var i=0;i<=3;i++){var y=pad.t+ih*i/3;g.beginPath();g.moveTo(pad.l,y);g.lineTo(w-pad.r,y);g.stroke();}
  g.fillStyle='#9aa4b4';g.font='10px sans-serif';g.textAlign='right';
  g.fillText(opt&&opt.fmt?opt.fmt(max):(''+Math.round(max)),pad.l-6,pad.t+4);
  g.fillText('0',pad.l-6,pad.t+ih+3);
  var n=D.length;if(n<2)return;
  var x=function(i){return pad.l+iw*i/(n-1);};
  var y=function(v){return pad.t+ih-(v-min)/(max-min||1)*ih;};
  series.forEach(function(s){
    g.beginPath();var started=false;
    for(var i=0;i<n;i++){var v=D[i][s.key];if(v===undefined||v===null||v<0){started=false;continue;}
      var yy=y(v);if(!started){g.moveTo(x(i),yy);started=true;}else g.lineTo(x(i),yy);}
    g.strokeStyle=s.color;g.lineWidth=s.width||1.6;g.stroke();
  });
  g.fillStyle='#9aa4b4';g.textAlign='left';g.fillText(fmtT(D[0].t),pad.l,h-4);
  g.textAlign='right';g.fillText(fmtT(D[n-1].t),w-pad.r,h-4);
  var lx=pad.l+4;
  series.forEach(function(s){g.fillStyle=s.color;g.fillRect(lx,pad.t-4,8,3);g.fillStyle='#7b8698';g.fillText(s.name,lx+11,pad.t);lx+=g.measureText(s.name).width+30;});
  if(opt&&opt.gap){g.fillStyle='rgba(226,96,60,.08)';for(var i=0;i<n;i++){if(D[i].gap){var w2=iw/(n-1);g.fillRect(x(i)-w2/2,pad.t,Math.max(1,w2),ih);}}}
}
draw('c1',[{key:'up',color:'#2f7ce0',name:'推流上行'}],{fmt:fmtBps});
draw('c2',[{key:'srv',color:'#e2603c',name:'推流节点'},{key:'hop',color:'#d4913f',name:'接入跳'},{key:'gw',color:'#2fae7a',name:'网关'},{key:'pub',color:'#8b95a6',name:'公网参考'}],{fmt:function(v){return Math.round(v)+'ms';},forceMax:100});
draw('c3',[{key:'conn',color:'#a97bff',name:'TCP连接数'}],{});
draw('c5',[{key:'hl',color:'#d4913f',name:'接入跳丢包‰'}],{forceMax:20});
draw('c4',[{key:'cpu',color:'#e2a33c',name:'CPU‰'},{key:'q',color:'#2fae7a',name:'质量分'}],{forceMax:1000});
";
    }
}
