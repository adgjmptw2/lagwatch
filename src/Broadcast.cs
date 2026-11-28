using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace LagWatch
{
    /// <summary>방송·녹화 중 1초마다 OBS 숫자와 PC 상태를 한 줄씩 남긴다. 판정은 아직 하지 않는다(1주차는 기록만).
    /// 기록: dir\yyyyMMdd_HHmmss.tsv (줄마다 1초), dir\streams.tsv (방송 하나당 한 줄). 35일 지나면 지운다.</summary>
    internal sealed class Broadcast : IDisposable
    {
        public const int KeepDays = 35;
        public const string Head = "t\tclock\tstream\trec\trenderSkip\tencSkip\tnetSkip\trendered\tencoded\tsent\trenderMs\tfps\tobsCpu\tcongestion\tkbps\tobsEnc\tobs3d\tobsCuda\tgame3d\tgpuBusy\tgpuEnc\tupKbps\tretrans\tcpu\tramFreeMB\tgameFps\ttop\ttopCores";
        static readonly string[] Q = { "GetStats", "GetStreamStatus", "GetRecordStatus" };

        readonly Monitor m; readonly string dir; readonly Func<double> gameFps;
        readonly Obs obs = new Obs(); readonly Gpu gpu = new Gpu(); readonly Net net = new Net();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly Stopwatch sw = Stopwatch.StartNew();
        Thread th; volatile bool stop;

        /// <summary>off: OBS 꺼짐 · disabled: 웹소켓 꺼짐 · auth: 비밀번호 틀림 · wait: 연결 시도 중 · on: 연결됨 · live: 방송·녹화 기록 중</summary>
        public volatile string State = "off";
        public volatile ObsStats Last;
        public volatile ObsEncoder Enc;
        public volatile string FilePath;
        public long Rows;
        public string ObsVersion { get { return obs.ObsVersion; } }
        /// <summary>지금 방송의 판정(방송 중이 아니면 null)과 마지막으로 끝난 방송</summary>
        public volatile StreamDoctor Live, LastSession;
        /// <summary>트레이 알림. 같은 원인은 5분에 한 번만.</summary>
        public Action<string, string> Notify;
        readonly System.Collections.Generic.Dictionary<string, DateTime> told = new System.Collections.Generic.Dictionary<string, DateTime>();

        sealed class Rec
        {
            public StreamWriter W; public DateTime Start; public double T0, PrevAt; public ObsStats First, Prev; public int ObsPid; public ObsEncoder Enc; public string File; public long Rows; public StreamDoctor Doc; public double Grid;
        }
        Rec rec;
        DateTime lost;

        public Broadcast(Monitor m, string dir, Func<double> gameFps) { this.m = m; this.dir = dir; this.gameFps = gameFps; }

        public void Start() { if (th != null) return; th = new Thread(Loop) { IsBackground = true, Name = "broadcast", Priority = ThreadPriority.BelowNormal }; th.Start(); }
        public void Dispose() { stop = true; wake.Set(); if (th != null) th.Join(3000); }

        void Nap(int ms) { wake.WaitOne(ms); }

        int ObsPid()
        {
            lock (m.Sync) foreach (var q in m.Procs.Values) if (string.Equals(q.Name, "obs64", StringComparison.OrdinalIgnoreCase) || string.Equals(q.Name, "obs64.exe", StringComparison.OrdinalIgnoreCase)) return q.Pid;
            return 0;
        }

        void Loop()
        {
            int backoff = 0;
            if (LastSession == null && dir != null) try { LastSession = LoadLatest(dir); } catch (Exception ex) { Sys.Note(ex); } // 다시 켜도 지난 방송 리포트가 보이게
            while (!stop)
            {
                int pid = ObsPid();
                if (pid == 0) { End(); obs.Close(); State = "off"; Nap(5000); continue; }
                if (!obs.Open)
                {
                    var c = ObsConfig.Read();
                    if (!c.Found || !c.Enabled) { State = "disabled"; Nap(10000); continue; }
                    State = "wait";
                    if (!obs.Connect(c, 3000))
                    {
                        State = obs.Fail == "인증" ? "auth" : "wait";
                        if (rec != null) { if ((DateTime.Now - lost).TotalSeconds > 30) End(); Nap(1000); continue; } // 방송 중 끊김: 30초까지는 곧바로 다시 붙어 본다
                        backoff = backoff == 0 ? 5000 : Math.Min(60000, backoff * 2); Nap(backoff); continue;
                    }
                    backoff = 0; State = rec != null ? "live" : "on"; obs.OutputChanged = true;
                }
                try
                {
                    if (rec == null)
                    {
                        if (obs.OutputChanged)
                        {
                            obs.OutputChanged = false;
                            var r = obs.Batch(Q, 3000); var s = Obs.Stats(r[0], r[1], r[2]); Last = s;
                            if (s.Streaming || s.Recording) Begin(s, pid);
                        }
                        if (rec == null) { obs.Pump(1000); continue; } // 방송을 시작하면 이벤트가 온다
                    }
                    int left = (int)((rec.Grid + rec.Rows + 1 - sw.Elapsed.TotalSeconds) * 1000); // 시작부터 정확히 1초 간격(밀리지 않게)
                    if (left < -2000) { rec.Grid = sw.Elapsed.TotalSeconds - rec.Rows - 1; left = 0; } // 끊겼다 다시 붙었으면 밀린 줄을 몰아 쓰지 않고 지금부터
                    if (left > 10) obs.Pump(Math.Min(1000, left));
                    if (stop) break;
                    var res = obs.Batch(Q, 3000); var st = Obs.Stats(res[0], res[1], res[2]); Last = st;
                    Row(st); obs.OutputChanged = false;
                    if (!st.Streaming && !st.Recording) End();
                }
                catch (IOException) // OBS를 끄거나 연결이 끊김. 방송 중이고 OBS가 살아 있으면 기록을 닫지 않고 다시 붙는다
                {
                    obs.Close(); State = "wait";
                    if (rec != null && ObsPid() != 0) { lost = DateTime.Now; Nap(1000); } else { End(); Nap(2000); }
                }
                catch (Exception ex) { Sys.Note(ex); End(); obs.Close(); State = "wait"; Nap(2000); }
            }
            End(); obs.Close(); gpu.Dispose(); net.Dispose();
        }

        void Begin(ObsStats s, int pid)
        {
            if (dir == null) return;
            Directory.CreateDirectory(dir); Prune();
            var r = new Rec { Start = DateTime.Now, T0 = sw.Elapsed.TotalSeconds, First = s, Prev = s, ObsPid = pid };
            r.PrevAt = r.Grid = r.T0;
            try { r.Enc = obs.Encoder(3000); } catch (IOException) { throw; } catch (Exception) { r.Enc = new ObsEncoder(); }
            Enc = r.Enc;
            r.File = Path.Combine(dir, r.Start.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".tsv");
            r.W = new StreamWriter(r.File, false, new UTF8Encoding(false));
            r.W.WriteLine("# 렉 감시 방송 기록 · 시작 " + r.Start.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            r.W.WriteLine("# obs " + obs.ObsVersion + " · websocket " + obs.WsVersion + " · mode " + r.Enc.Mode + " · encoder " + r.Enc.Encoder + " · kbps " + r.Enc.Kbps + " · preset " + r.Enc.Preset);
            r.W.WriteLine("# hags " + Reg(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode") + " · gamemode " + Reg(@"HKEY_CURRENT_USER\Software\Microsoft\GameBar", "AutoGameModeEnabled") + " · obsElevated " + Elevated(pid));
            r.W.WriteLine(Head);
            gpu.Start(); net.Start();
            Cpu(); // 기준점
            var rr = r; r.Doc = new StreamDoctor { Enc = r.Enc, Start = r.Start, Decided = d => Told(d), Closed = d => Log(rr, d) };
            rec = r; Live = r.Doc; FilePath = r.File; State = "live";
        }

        void Row(ObsStats s)
        {
            var r = rec; double now = sw.Elapsed.TotalSeconds, dt = Math.Max(0.05, now - r.PrevAt); var p = r.Prev;
            int gamePid = 0; string top = null; float topC = 0;
            lock (m.Sync)
            {
                if (m.Game != null) gamePid = m.Game.Pid;
                foreach (var a in m.Apps.Values) // 게임·OBS·렉 감시를 빼고 CPU를 가장 많이 쓰는 프로그램. 같은 프로그램의 프로세스는 합친 값(감시 주기대로라 몇 초 늦을 수 있음)
                {
                    if (a.IsGame || a.Pseudo || a.Cores <= topC || a.Exe == null) continue;
                    if (a.Exe.StartsWith("obs64", StringComparison.OrdinalIgnoreCase) || a.Exe.StartsWith("LagWatch", StringComparison.OrdinalIgnoreCase)) continue;
                    topC = (float)a.Cores; top = string.IsNullOrEmpty(a.Label) ? a.Exe : a.Label;
                }
            }
            double cpu = Cpu(); long free = Free(); // 감시 주기(평소 5초)를 당기지 않고 두 숫자만 직접 읽는다
            var g = gpu.Sample(r.ObsPid, gamePid); var n = net.Sample(); double fps = gameFps != null ? gameFps() : double.NaN;
            var b = new StringBuilder(220); var ic = CultureInfo.InvariantCulture;
            Action<object> add = v => b.Append('\t').Append(v is double ? ((double)v).ToString("0.##", ic) : v is float ? ((float)v).ToString("0.#", ic) : Convert.ToString(v, ic));
            b.Append((now - r.T0).ToString("0.0", ic));
            add(DateTime.Now.ToString("HH:mm:ss", ic)); add(s.Streaming ? 1 : 0); add(s.Recording ? 1 : 0);
            add(D(s.RenderSkipped, p.RenderSkipped)); add(D(s.EncSkipped, p.EncSkipped)); add(D(s.NetSkipped, p.NetSkipped));
            add(D(s.RenderTotal, p.RenderTotal)); add(D(s.EncTotal, p.EncTotal)); add(D(s.NetTotal, p.NetTotal));
            add(s.AvgRenderMs); add(s.Fps); add(s.ObsCpu); add(s.Congestion); add(D(s.Bytes, p.Bytes) * 8 / 1000.0 / dt);
            add(g.ObsEnc); add(g.Obs3D); add(g.ObsCuda); add(g.Game3D); add(g.Busy); add(g.Enc);
            add(n.UpKbps); add(n.Retrans); add(cpu); add(free >> 20); add(double.IsNaN(fps) ? "" : (object)fps); add(top ?? ""); add(topC);
            r.W.WriteLine(b.ToString());
            r.Doc.Add(new Sec
            {
                At = DateTime.Now, Render = (int)D(s.RenderSkipped, p.RenderSkipped), Enc = (int)D(s.EncSkipped, p.EncSkipped), Net = (int)D(s.NetSkipped, p.NetSkipped),
                RenderMs = (float)s.AvgRenderMs, Fps = (float)s.Fps, ObsCpu = (float)s.ObsCpu, Cong = (float)s.Congestion, Kbps = (float)(D(s.Bytes, p.Bytes) * 8 / 1000.0 / dt),
                ObsEnc = g.ObsEnc, Obs3D = g.Obs3D, ObsCuda = g.ObsCuda, Game3D = g.Game3D, Busy = g.Busy, GpuEnc = g.Enc, Up = n.UpKbps, Retrans = n.Retrans,
                Cpu = (float)cpu, FreeMB = free >> 20, GameFps = double.IsNaN(fps) ? 0 : (float)fps, Top = top, TopCores = topC
            });
            r.Prev = s; r.PrevAt = now; r.Rows++; Interlocked.Increment(ref Rows);
            if (r.Rows % 10 == 0) r.W.Flush();
        }

        /// <summary>누적값 차이. OBS가 새로 시작해 값이 줄면 새 값을 그대로 쓴다.</summary>
        static long D(long now, long before) { return now >= before ? now - before : now; }

        void End()
        {
            var r = rec; if (r == null) return; rec = null;
            try { r.Doc.Finish(); } catch (Exception ex) { Sys.Note(ex); }
            LastSession = r.Doc; Live = null; FilePath = null; if (State == "live") State = obs.Open ? "on" : "wait";
            int nd = r.Doc.Drops.Count; string main = r.Doc.Main();
            if (nd > 0 && Notify != null && m.Set.Notify) Notify(Lang.Tx("방송 끝: 끊김 {0}번", "Stream ended: {0} drops", nd), main != null && main.Length > 0 ? Lang.Tx("대부분 {0}", "Mostly {0}", StreamDoctor.Name(main)) : Lang.Tx("원인 모름", "Cause unknown"));
            try
            {
                var p = r.Prev; var f = r.First; var ic = CultureInfo.InvariantCulture;
                string sum = string.Join("\t", r.Start.ToString("yyyy-MM-dd HH:mm:ss", ic), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", ic), ((int)(r.PrevAt - r.T0)).ToString(ic),
                    r.Enc.Encoder, r.Enc.Kbps.ToString(ic), D(p.RenderSkipped, f.RenderSkipped).ToString(ic), D(p.EncSkipped, f.EncSkipped).ToString(ic), p.NetSkipped.ToString(ic), p.NetTotal.ToString(ic), r.Doc.Drops.Count.ToString(ic), r.Doc.Causes(), Path.GetFileName(r.File));
                r.W.WriteLine("# end " + sum.Replace('\t', ' '));
                r.W.Dispose();
                string idx = Path.Combine(dir, "streams.tsv"); bool fresh = !File.Exists(idx);
                File.AppendAllText(idx, (fresh ? "start\tend\tseconds\tencoder\tkbps\trenderSkip\tencSkip\tnetSkip\tnetTotal\tdrops\tcauses\tfile\n" : "") + sum + "\n", new UTF8Encoding(false));
            }
            catch (Exception ex) { Sys.Note(ex); }
            gpu.Dispose(); net.Dispose();
        }

        /// <summary>원인을 처음 정했을 때: 기록 파일에 한 줄, 트레이 알림(같은 원인은 5분에 한 번)</summary>
        void Log(Rec r, Drop d)
        {
            var ic = CultureInfo.InvariantCulture;
            try { r.W.WriteLine("# drop " + d.From.ToString("HH:mm:ss", ic) + "-" + d.To.ToString("HH:mm:ss", ic) + " render " + d.Render + " enc " + d.Enc + " net " + d.Net + " cause " + (d.Cause.Length == 0 ? "?" : d.Cause) + " also " + string.Join(",", d.Also.ToArray()) + " · " + d.Why); } catch (Exception) { }
        }

        void Told(Drop d)
        {
            if (Notify == null || !m.Set.Notify) return;
            string key = d.Cause.Length == 0 ? "?" : d.Cause; DateTime last;
            if (told.TryGetValue(key, out last) && (DateTime.Now - last).TotalMinutes < 5) return;
            told[key] = DateTime.Now;
            string fix = StreamDoctor.Fix(d.Cause);
            Notify(Lang.Tx("방송 끊김: {0}", "Stream drops: {0}", StreamDoctor.Name(d.Cause)), d.Why + (fix.Length > 0 ? "\n" + fix : ""));
        }

        /// <summary>가장 최근 방송 기록 파일을 다시 읽어 판정까지 되살린다(알림은 안 함)</summary>
        public static StreamDoctor LoadLatest(string dir)
        {
            if (!Directory.Exists(dir)) return null;
            var files = Directory.GetFiles(dir, "????????_??????.tsv"); if (files.Length == 0) return null;
            Array.Sort(files, StringComparer.Ordinal); return Load(files[files.Length - 1]);
        }

        public static StreamDoctor Load(string file)
        {
            var ic = CultureInfo.InvariantCulture; var sd = new StreamDoctor(); DateTime start = DateTime.MinValue; string[] head = null; DateTime last = DateTime.MinValue;
            foreach (var line in File.ReadLines(file, Encoding.UTF8))
            {
                if (line.StartsWith("# 렉 감시 방송 기록")) { DateTime t; if (DateTime.TryParseExact(line.Substring(line.Length - 19), "yyyy-MM-dd HH:mm:ss", ic, DateTimeStyles.None, out t)) start = sd.Start = t; continue; }
                if (line.StartsWith("# obs "))
                {
                    var e = new ObsEncoder(); var parts = line.Split(new[] { " · " }, StringSplitOptions.None);
                    foreach (var p in parts) { var kv = p.Trim().Split(new[] { ' ' }, 2); if (kv.Length < 2) continue; if (kv[0] == "mode") e.Mode = kv[1]; else if (kv[0] == "encoder") e.Encoder = kv[1]; else if (kv[0] == "preset") e.Preset = kv[1]; else if (kv[0] == "kbps") int.TryParse(kv[1], out e.Kbps); }
                    sd.Enc = e; continue;
                }
                if (line.StartsWith("t\t")) { head = line.Split('\t'); continue; }
                if (line.Length == 0 || line[0] == '#' || head == null || start == DateTime.MinValue) continue;
                var c = line.Split('\t'); Func<string, float> f = name => { int i = Array.IndexOf(head, name); float v; return i >= 0 && i < c.Length && float.TryParse(c[i], NumberStyles.Float, ic, out v) ? v : 0; };
                Func<string, string> str = name => { int i = Array.IndexOf(head, name); return i >= 0 && i < c.Length && c[i].Length > 0 ? c[i] : null; };
                var s = new Sec
                {
                    At = start.AddSeconds(Math.Round(f("t"))), Render = (int)f("renderSkip"), Enc = (int)f("encSkip"), Net = (int)f("netSkip"), RenderMs = f("renderMs"), Fps = f("fps"), ObsCpu = f("obsCpu"), Cong = f("congestion"), Kbps = f("kbps"),
                    ObsEnc = f("obsEnc"), Obs3D = f("obs3d"), ObsCuda = f("obsCuda"), Game3D = f("game3d"), Busy = f("gpuBusy"), GpuEnc = f("gpuEnc"), Up = f("upKbps"), Retrans = f("retrans"), Cpu = f("cpu"), FreeMB = f("ramFreeMB"), GameFps = f("gameFps"), Top = str("top"), TopCores = f("topCores")
                };
                sd.Add(s); last = s.At;
            }
            if (last == DateTime.MinValue) return null;
            sd.Finish(); sd.End = last; return sd;
        }

        void Prune()
        {
            try
            {
                var old = DateTime.Now.AddDays(-KeepDays);
                foreach (var f in Directory.GetFiles(dir, "????????_??????.tsv")) if (File.GetLastWriteTime(f) < old) File.Delete(f);
            }
            catch (Exception) { }
        }

        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [StructLayout(LayoutKind.Sequential)] struct MemStat { public uint Len, Load; public ulong Total, Avail, PageTotal, PageAvail, VirtTotal, VirtAvail, ExtAvail; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemStat m);
        long pIdle, pBusy;
        /// <summary>앞 호출 뒤로 PC 전체 CPU %(커널 시간에 쉰 시간이 들어 있어서 뺀다)</summary>
        double Cpu()
        {
            long i, k, u; if (!GetSystemTimes(out i, out k, out u)) return double.NaN;
            long tot = k + u, di = i - pIdle, dt = tot - pBusy; pIdle = i; pBusy = tot;
            return dt > 0 ? Math.Max(0, Math.Min(100, 100.0 * (dt - di) / dt)) : double.NaN;
        }
        static long Free() { var s = new MemStat { Len = (uint)Marshal.SizeOf(typeof(MemStat)) }; return GlobalMemoryStatusEx(ref s) ? (long)s.Avail : 0; }

        static string Reg(string key, string name) { try { var v = Microsoft.Win32.Registry.GetValue(key, name, null); return v == null ? "-" : v.ToString(); } catch (Exception) { return "?"; } }

        [DllImport("advapi32.dll")] static extern bool OpenProcessToken(IntPtr h, int acc, out IntPtr tok);
        [DllImport("advapi32.dll")] static extern bool GetTokenInformation(IntPtr t, int cls, out int v, int len, out int ret);
        /// <summary>OBS가 관리자 권한으로 도는지. OBS는 게임이 아니라 열어도 된다. UAC가 꺼진 PC에서는 늘 1이다.</summary>
        static string Elevated(int pid)
        {
            IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid); if (h == IntPtr.Zero) return "?";
            try { IntPtr t; if (!OpenProcessToken(h, 8, out t)) return "?"; int v, n; bool ok = GetTokenInformation(t, 20, out v, 4, out n); Native.CloseHandle(t); return ok ? (v != 0 ? "1" : "0") : "?"; }
            finally { Native.CloseHandle(h); }
        }
    }
}
