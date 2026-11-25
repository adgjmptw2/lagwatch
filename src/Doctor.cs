using System;
using System.Collections.Generic;
using System.Threading;
using static LagWatch.Lang;

namespace LagWatch
{
    internal sealed class Act { public string Label, Short, Id; public bool Main; public Act(string label, string id, bool main = false, string shortLabel = null) { Label = label; Id = id; Main = main; Short = shortLabel ?? label; } }

    /// <summary>원인 하나: 무엇 때문에, 근거는, 무엇을 할 수 있는지</summary>
    internal sealed class Cause
    {
        public string Key, Title, Short, Why, ShortWhy = "";
        public int Level;                               // 2 지금 문제, 1 손볼 것, 0 알아 둘 것, -1 해결됨
        public int Metric = -1;                         // 관계있는 자원(Snap.Cpu…)
        public bool Fail;                               // 하려다 안 된 것
        public readonly List<Act> Acts = new List<Act>();
    }

    internal struct Growth { public string Key, Label; public float First, Last; public double Hours; public float PerHour { get { return Hours > 0 ? (float)((Last - First) / Hours) : 0; } } }
    internal struct Bg { public string Key, Label; public double Avg, Max; }

    /// <summary>원인을 찾고 해결을 맡는다. 감시 스레드에서 돈다.</summary>
    internal sealed class Doctor : IDisposable
    {
        public readonly Monitor M;
        public readonly Frames Frames = new Frames();
        public readonly Kern Kern = new Kern();
        public readonly Fixer Fix;
        public readonly Temp Temp = new Temp();
        public readonly Boost Boost;
        public readonly Rounds Rounds;
        public readonly Blame Blame = new Blame();
        public readonly Hits Hits;
        public volatile List<Cause> Causes = new List<Cause>();
        public volatile List<Growth> Growing = new List<Growth>();
        public volatile List<Bg> Background = new List<Bg>();
        public Kern.Report Deep;                        // 마지막 정밀 검사
        public DateTime DeepAt;
        public string DeepText;
        public volatile bool DeepBusy;
        public double Fps, MaxMs; public int Hitch1m;
        public bool FramesOk;
        public CleanResult LastTrim; public DateTime LastTrimAt;
        public Temp.Done LastTemp; public DateTime LastTempAt;

        /// <summary>코어 화면이 보이는 동안 켠다.</summary>
        public volatile bool WantCores;
        public volatile Kern.Report Live;
        public bool CoresOk = true;

        int gamePid; double fixAt = -10, slowAt = -100, trimAt = -1000, deepAuto = -1000, coreAt;
        bool trimmedForGame;
        readonly float[] buf = new float[181], hour = new float[61]; bool lowPower;
        public Action Changed;

        public Doctor(Monitor m, string dir)
        {
            M = m;
            Fix = new Fixer(m, dir == null ? null : System.IO.Path.Combine(dir, "applied.tsv"));
            Boost = new Boost(m, dir); Rounds = new Rounds(Frames, dir, Boost.Sig) { Settle = (g, from) => { JudgeHits(M.Now, true); return Hits.Between(g, from, DateTime.Now); } }; Hits = new Hits(dir);
            if (dir != null) Temp.LogFile = System.IO.Path.Combine(dir, "clean.tsv");
            foreach (var c in Temp.Cats) { if (m.Set.TempOff.Contains(c.Key)) c.On = false; if (m.Set.TempOn.Contains(c.Key)) c.On = true; }
        }

        /// <summary>표본을 하나 받을 때마다</summary>
        public void Tick(double now)
        {
            int pid = 0, fg = 0; string gx = null; double tc, rd, dk, dv = 0; bool lowRam; lock (M.Sync) { dk = M.G.DiskPct; lowRam = Rules.RamLow(M); for (int i = 0; i < M.Lps; i++) if (M.Drv[i] > dv) dv = M.Drv[i]; if (M.Game != null) { pid = M.Game.Pid; gx = M.Game.Name; } fg = M.ForegroundPid; tc = M.TotalCpu * 100; rd = M.G.ReadyPct; }
            if (pid != gamePid)
            {
                Rounds.Close(now);
                Blame.Close(); wait.Clear();
                Frames.Close();
                if (pid != 0) Boost.Look(0);
                gamePid = pid; trimmedForGame = false;
                FramesOk = pid != 0 && Frames.Start(pid);
                if (FramesOk && M.Set.Blame && Cleaner.Elevated) Blame.Start();
            }
            if (FramesOk)
            {
                if (lowRam) tRam = now; if (dk > 0) tPage = now; if (rd >= .08 || tc >= 88) tCpu = now; if (dv >= .15) tDrv = now;
                if (Blame.Running) { int a, b; lock (M.Sync) { a = M.G.MainTid; b = M.G.SecondTid; } Blame.Watch(a, b); }
                var fresh = Frames.Drain();
                if (fresh != null) foreach (var h in fresh) wait.Add(new Pend { H = h, Now = now, At = DateTime.Now, Exe = gx });
                JudgeHits(now, false);
                Rounds.Tick(now, gx, tc, rd, dk, lowRam, dv);
            }
            if (pid != 0)
            {
                Frames.Active = fg == pid;
                Frames.Last(History.Epoch(DateTime.UtcNow), out Fps, out MaxMs, out Hitch1m);
                if (Hitch1m >= 3) lock (M.Sync) { int n = Hitch1m; double ms = MaxMs; M.Log.Report(Kind.Hitch, "", n, now - 5, now, true, () => Tx("1분간 끊김 {0}번, 최대 멈춤 {1}ms · {2}", "{0} {3} in 1 min, max freeze {1}ms · {2}", n, ms.ToString("0"), M.Rules.TopCpu(3, true), Pl(n, "stutter", "stutters"))); }
            }
            else { Fps = MaxMs = 0; Hitch1m = 0; }

            Cores(now, pid);
            if (now - fixAt >= 3) { fixAt = now; Fix.Tick(); Effects(); }
            if (now - slowAt >= 60) { slowAt = now; Slow(now); Boost.Slow(); }
            Causes = Find(now);
            Auto(now, pid);
        }

        // ---- 끊긴 까닭 ----

        sealed class Pend { public Frames.Hitch H; public double Now; public DateTime At; public string Exe; }
        readonly List<Pend> wait = new List<Pend>(); double tRam = -99, tPage = -99, tCpu = -99, tDrv = -99;

        /// <summary>윈도우 기록이 다 들어오도록 3초 뒤에 본다.</summary>
        void JudgeHits(double now, bool all)
        {
            for (int i = 0; i < wait.Count; i++)
            {
                var p = wait[i]; if (!all && now - p.Now < 3) continue; wait.RemoveAt(i--);
                try
                {
                    var h = new Hit { At = p.At, Exe = p.Exe ?? "", Ms = p.H.Ms }; Blame.Part m, s; Blame.Explain(p.H.A, p.H.B, out m, out s);
                    Func<double, bool> near = t => Math.Abs(t - p.Now) <= 3; Snap snap = null; lock (M.Sync) { h.RamLeft = M.RamAvail / 1073741824.0; h.CpuPct = M.TotalCpu * 100; }
                    h.Judge(m, s, near(tRam), near(tPage), near(tCpu), near(tDrv), Rounds.Unload(p.At));
                    if (h.Kind == Hit.Cpu && h.Measured) h.Who = Robber(m.Ready >= s.Ready ? m : s);
                    else if (h.Kind == Hit.Ram) { snap = Snap.Take(M); h.Who = Eaters(snap, 3); }
                    Hits.Add(h);
                }
                catch (Exception) { }
            }
        }
        /// <summary>게임을 밀어낸 프로그램</summary>
        string Robber(Blame.Part p)
        {
            var own = Blame.Owners(p.By.Keys); var sum = new Dictionary<string, double>();
            lock (M.Sync) foreach (var kv in p.By) { int pid; Proc q; string k = "win", l = Tx("윈도우", "Windows"); if (own.TryGetValue(kv.Key, out pid) && M.Procs.TryGetValue(pid, out q) && q.App != null) { if (q.IsGame) continue; Names.Group(q.App, out k, out l); } double v; sum.TryGetValue(l, out v); sum[l] = v + kv.Value; }
            string best = ""; double bv = 0; foreach (var kv in sum) if (kv.Value > bv) { bv = kv.Value; best = kv.Key; }
            return best;
        }
        /// <summary>게임 말고 램을 많이 쓰는 것</summary>
        static string Eaters(Snap snap, int n)
        {
            var eat = new List<Grp>(); foreach (var g in snap.Groups) if (!g.IsGame && g.Key != "win" && g.Ws > 500L << 20) eat.Add(g); eat.Sort((x, y) => y.Ws.CompareTo(x.Ws)); string who = "";
            for (int i = 0; i < eat.Count && i < n; i++) who += (i > 0 ? ", " : "") + eat[i].Label + " " + Fmt.Mem(eat[i].Ws / 1048576.0);
            return who;
        }

        // ---- 코어 ----

        void Cores(double now, int pid)
        {
            if (WantCores && !DeepBusy)
            {
                if (!Kern.Running) { CoresOk = Kern.Start(pid); coreAt = now; if (!CoresOk) WantCores = false; }
                else if (now - coreAt >= 0.9) { coreAt = now; Kern.GamePid = pid; var rp = Kern.Take(); if (rp.Core.Count > 0) Live = rp; } // 켠 직후 첫 1초는 비어 있다
            }
            else if (Kern.Running && !DeepBusy) { Kern.Close(); Live = null; }
        }

        /// <summary>정밀 검사: 잠깐 동안 누가 게임을 밀어냈는지와 드라이버 시간을 잰다.</summary>
        public void DeepScan(int seconds)
        {
            if (DeepBusy) return;
            DeepBusy = true;
            ThreadPool.QueueUserWorkItem(o =>
            {
                try
                {
                    Kern.Close(); Live = null;
                    int pid; lock (M.Sync) pid = M.Game != null ? M.Game.Pid : 0;
                    if (!Kern.Start(pid)) { CoresOk = false; DeepText = Kern.Error == 5 ? Tx("관리자 권한 필요", "Administrator required") : Tx("검사 실패 (", "Scan failed (") + Kern.Error + ")"; return; }
                    Thread.Sleep(seconds * 1000);
                    var r = Kern.Take(); Kern.Close();
                    Deep = r; DeepAt = DateTime.Now; DeepText = Explain(r, seconds);
                }
                catch (Exception) { DeepText = Tx("검사 실패", "Scan failed"); }
                finally { DeepBusy = false; if (Changed != null) Changed(); }
            });
        }

        public string Explain(Kern.Report r, int seconds)
        {
            var per = new Dictionary<string, double>(); var label = new Dictionary<string, string>();
            lock (M.Sync)
                foreach (var kv in r.Thief)
                {
                    Proc q; string k = "win", l = Tx("윈도우", "Windows");
                    if (M.Procs.TryGetValue(kv.Key, out q) && q.App != null) Names.Group(q.App, out k, out l);
                    double v; per.TryGetValue(k, out v); per[k] = v + kv.Value; label[k] = l;
                }
            string top = null; foreach (var kv in per) if (top == null || kv.Value > per[top]) top = kv.Key;
            string s = "";
            if (r.Robbed >= seconds * 10 && top != null) s = Tx("{0}초 중 게임이 CPU를 못 쓴 시간 {1} ({2} {3})", "Game waited for CPU {1} of {0}s ({2} {3})", seconds, Ms(r.Robbed), label[top], Ms(per[top]));
            else if (Pid(r)) s = Tx("{0}초 중 게임이 CPU를 못 쓴 시간 {1}, 정상", "Game waited for CPU {1} of {0}s, normal", seconds, Ms(r.Robbed));
            if (r.Drivers.Count > 0)
            {
                var d = r.Drivers[0];
                s += (s.Length > 0 ? ", " : "") + (d.Max >= 1 ? Tx("드라이버 {0} 최대 {1}ms", "Driver {0} max {1}ms", d.Name, d.Max.ToString("0.0")) : Tx("드라이버 정상 (최대 {0}ms)", "Drivers normal (max {0}ms)", d.Max.ToString("0.00")));
            }
            return s.Length == 0 ? Tx("이상 없음", "No problems found") : s;
        }
        bool Pid(Kern.Report r) { return gamePid != 0; }
        static string Ms(double v) { return v >= 100 ? v.ToString("0") + "ms" : v.ToString("0.0") + "ms"; }

        // ---- 느리게 바뀌는 것(1분마다) ----

        void Slow(double now)
        {
            var snap = Snap.Take(M);
            // 절전만. 균형 조정은 클럭이 실제로 떨어질 때(CPU 클럭 저하) 알린다
            try { string ps = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes", "ActivePowerScheme", "") as string ?? ""; lowPower = ps.StartsWith("a1841308", StringComparison.OrdinalIgnoreCase); } catch (Exception) { lowPower = false; }
            int minute = snap.Epoch / 60 - 1;
            var up = new List<Growth>(); var bg = new List<Bg>();
            var pts = new List<MPoint>();
            lock (M.Sync) M.Hist.Range(snap.Epoch - 6 * 3600, snap.Epoch, pts);
            var play = new HashSet<int>(); foreach (var p in pts) if (p.Game != 0) play.Add(p.T / 60);
            foreach (var g in snap.Groups)
            {
                if (g.Key == "lagwatch") continue;
                bool pseudo = g.Key == "win";
                if (g.Commit >= 150L << 20 && !g.IsGame && !pseudo)
                {
                    lock (M.Sync) Series.Commit(g, minute - 180, minute, buf);
                    float a, b;
                    for (int i = 0; i < 61; i++) hour[i] = buf[120 + i];
                    if (Series.Rising(buf, 181, Math.Max(60, g.Commit / 1048576f * 0.06f), out a, out b)) up.Add(new Growth { Key = g.Key, Label = g.Label, First = a, Last = b, Hours = 3 });
                    else if (Series.Rising(hour, 61, Math.Max(300, g.Commit / 1048576f * 0.15f), out a, out b)) up.Add(new Growth { Key = g.Key, Label = g.Label, First = a, Last = b, Hours = 1 }); // 켠 지 얼마 안 된 것의 빠른 누수
                }
                if (play.Count >= 10 && !g.IsGame && !pseudo)
                {
                    double s = 0, mx = 0; int n = 0, hot = 0;
                    lock (M.Sync)
                        foreach (int t in play)
                        {
                            double v = 0; float c, w, k; foreach (var app in g.Apps) if (app.H.Get(t, out c, out w, out k)) v += k;
                            s += v; n++; if (v > mx) mx = v; if (v / snap.Lps >= 0.2) hot++;
                        }
                    double avg = s / n / snap.Lps * 100;
                    if (avg >= 5 || hot >= 3) bg.Add(new Bg { Key = g.Key, Label = g.Label, Avg = avg, Max = mx / snap.Lps * 100 });
                }
            }
            up.Sort((x, y) => (y.Last - y.First).CompareTo(x.Last - x.First));
            bg.Sort((x, y) => y.Avg.CompareTo(x.Avg));
            Growing = up; Background = bg;
            if (Changed != null) Changed();
        }

        // ---- 원인 찾기 ----

        double stealSince = -1; string stealKey;

        public List<Cause> Find(double now)
        {
            var snap = Snap.Take(M);
            var r = new List<Cause>();
            var taken = new HashSet<string>();
            string game = snap.Game != null ? snap.Game.Label : null;

            // 1. 게임이 CPU를 뺏기는 중
            bool robbed = snap.Game != null && ((Hitch1m >= 3 && (snap.CpuPct >= 88 || snap.CpuPct >= 70 && snap.Wait >= 0.08)) || (snap.Wait >= 0.25 && snap.CpuPct >= 80)); // 게임이 실제로 CPU를 기다린 흔적이 있을 때만
            if (robbed)
            {
                Grp top = null;
                foreach (var g in snap.Groups) if (!g.IsGame && g.Key != "lagwatch" && (top == null || g.Cores > top.Cores)) top = g;
                if (top != null && top.Cores / Math.Max(1, snap.Lps) * 100 >= 12)
                {
                    if (stealKey != top.Key) { stealKey = top.Key; stealSince = now; }
                    if (now - stealSince >= 2)
                    {
                        bool waited = snap.Wait >= 0.08; // 게임 스레드가 CPU 차례를 기다린 게 보일 때만 '뺏는 중'
                        var c = new Cause { Key = top.Key, Level = 2, Metric = Snap.Cpu, Title = waited ? Tx("{0} {1} CPU를 뺏는 중", "{2} is taking CPU from {1}", Ga(top.Label), game, top.Label) : Tx("{0} CPU를 많이 쓰는 중", "{1} is using a lot of CPU", Ga(top.Label), top.Label), Short = waited ? Tx("{0} CPU를 뺏는 중", "{1} is taking CPU", Ga(top.Label), top.Label) : Tx("{0} CPU를 많이 씀", "{1} high CPU", Ga(top.Label), top.Label) };
                        c.Why = (Hitch1m > 0 ? Tx("최근 1분 끊김 {0}번, 최대 멈춤 {1}ms, ", "{0} {2} in last min, max freeze {1}ms, ", Hitch1m, MaxMs.ToString("0"), Pl(Hitch1m, "stutter", "stutters")) : "") + top.Label + " CPU " + snap.Val(top, Snap.Cpu).ToString("0") + "%";
                        c.ShortWhy = Hitch1m > 0 ? Tx("최근 1분 끊김 {0}번", "{0} {1} in last min", Hitch1m, Pl(Hitch1m, "stutter", "stutters")) : "CPU " + snap.Val(top, Snap.Cpu).ToString("0") + "%";
                        if (Fixer.CanTune(top)) { c.Acts.Add(new Act(Tx("코어 2개로 제한", "Limit to 2 cores"), "once:" + top.Key, true, Tx("코어 제한", "Limit cores"))); c.Acts.Add(new Act(Tx("게임할 때마다 제한", "Every game"), "always:" + top.Key)); }
                        else c.Why += top.Key == "win" ? "" : Tx(", 제한 불가", ", can't limit");
                        c.Acts.Add(new Act(Tx("자세히", "Details"), "open:" + top.Key));
                        r.Add(c); taken.Add(top.Key);
                    }
                }
            }
            else stealSince = -1;

            // 2. 가상메모리
            double vm = snap.VmGb > 0 ? 1 - snap.VmLeft / snap.VmGb : 0;
            foreach (var g in Growing)
            {
                if (taken.Contains(g.Key)) continue;
                Grp cur; snap.ByKey.TryGetValue(g.Key, out cur);
                if (cur == null || Fixed(g.Key)) continue;
                double hours = g.PerHour > 1 ? snap.VmLeft * 1024 / g.PerHour : 999;
                bool bad = (g.Last - g.First >= 1000 && hours <= 8) || (vm >= 0.85 && g.Last - g.First >= 300);
                var c = new Cause { Key = g.Key, Level = bad ? 2 : 1, Metric = Snap.Vm };
                string ft = Fmt.Mem(g.First) + " → " + Fmt.Mem(cur.Commit / 1048576.0);
                c.Title = g.Label + (bad ? Tx(" 가상메모리 급증", " commit spiking") : Tx(" 가상메모리 증가", " commit growing")); c.Short = g.Label + (bad ? Tx(" 메모리 급증", " spiking") : Tx(" 메모리 증가", " growing"));
                c.Why = Tx("최근 {0}시간 ", "Last {0}h: ", g.Hours.ToString("0")) + ft + (bad && hours < 24 ? Tx(", 약 {0} 뒤 꽉 참", ", full in about {0}", Hours(hours)) : ""); c.ShortWhy = ft;
                bool client = cur.Apps.Count > 0 && Names.IsGameClient(cur.Apps[0].Exe);
                if (Fixer.CanRestart(cur))
                {
                    bool booked = Booked(g.Key);
                    if (bad) { c.Acts.Add(new Act(Tx("재시작", "Restart"), "restart:" + g.Key, true)); if (!booked && snap.Game != null) c.Acts.Add(new Act(Tx("게임 후 재시작", "Restart after game"), "after:" + g.Key)); }
                    else if (snap.Game != null) { if (!booked) c.Acts.Add(new Act(Tx("게임 후 재시작", "Restart after game"), "after:" + g.Key, true)); else continue; }
                    else c.Acts.Add(new Act(Tx("재시작", "Restart"), "restart:" + g.Key, true));
                }
                else if (client) c.Why += Tx(", 게임을 끄면 줄어듦", ", drops when the game closes");
                r.Add(c); taken.Add(g.Key);
            }
            if (vm >= 0.85 && !r.Exists(x => x.Metric == Snap.Vm && x.Level == 2))
            {
                Grp top = null; foreach (var g in snap.Groups) if (Fixer.CanRestart(g) && (top == null || g.Commit > top.Commit)) top = g;
                var c = new Cause { Key = top != null ? top.Key : "*vm", Level = 2, Metric = Snap.Vm, Title = Tx("가상메모리 부족, {0} 남음", "Low commit memory, {0} free", Txt.Left(snap, Snap.Vm)), Short = Txt.Title(Snap.Vm) + " " + Txt.Pct(snap, Snap.Vm) };
                c.Why = Tx("{0} 사용 ({1})", "{0} used ({1})", Txt.Pct(snap, Snap.Vm), Txt.Of(snap, Snap.Vm)) + (top != null ? Tx(", 재시작 가능한 것 중 {0} 가장 큼 ({1})", ", largest restartable: {2} ({1})", Ga(top.Label), Fmt.Mem(top.Commit / 1048576.0), top.Label) : ""); c.ShortWhy = Txt.Left(snap, Snap.Vm) + Tx(" 남음", " free");
                if (top != null) { c.Acts.Add(new Act(Tx("{0} 재시작", "Restart {0}", top.Label), "restart:" + top.Key, true, Tx("재시작", "Restart"))); taken.Add(top.Key); }
                r.Add(c);
            }

            // 3. 램
            if (Rules.RamLow(M))
            {
                string who = Eaters(snap, 3);
                var c = new Cause { Key = "*ram", Level = 2, Metric = Snap.Ram, Title = Tx("램 부족, {0} 남음", "Low RAM, {0} free", Txt.Left(snap, Snap.Ram)), Short = Txt.Title(Snap.Ram) + " " + Txt.Pct(snap, Snap.Ram), Why = Tx("{0} 사용, 디스크에서 다시 읽는 중", "{0} used, reading back from disk", Txt.Pct(snap, Snap.Ram)) + (who.Length > 0 ? ", " + who : ""), ShortWhy = Txt.Left(snap, Snap.Ram) + Tx(" 남음", " free") };
                c.Acts.Add(new Act(Tx("램 청소", "Clean RAM"), "trim", true)); r.Add(c);
            }

            // 4. 드라이버, 속도
            if (Recent(Kind.Driver, 20) != null)
            {
                var e = Recent(Kind.Driver, 20);
                var c = new Cause { Key = "*drv", Level = snap.Game != null && Hitch1m >= 3 ? 2 : 1, Metric = Snap.Cpu, Title = Tx("드라이버 지연", "Driver latency"), Short = Tx("드라이버 지연", "Driver latency"), Why = DeepText != null && (DateTime.Now - DeepAt).TotalMinutes < 30 ? DeepText : e.Text, ShortWhy = e.Text };
                c.Acts.Add(new Act(DeepBusy ? Tx("검사 중", "Scanning") : Tx("드라이버 검사", "Scan drivers"), "deep", true, Tx("검사", "Scan"))); r.Add(c);
            }
            if (Recent(Kind.Slow, 2) != null) { var c = new Cause { Key = "*slow", Level = 1, Metric = Snap.Cpu, Title = Tx("CPU 클럭 저하", "CPU clock drop"), Short = Tx("CPU 클럭 저하", "CPU clock drop"), Why = Recent(Kind.Slow, 2).Text, ShortWhy = Fmt.Ghz(snap.Ghz) }; c.Acts.Add(new Act(Tx("전원 설정 열기", "Power settings"), "power", true, Tx("전원 설정", "Power"))); r.Add(c); }

            if (lowPower && snap.Game != null) { var c = new Cause { Key = "*power", Level = 1, Metric = Snap.Cpu, Title = Tx("전원이 절전 모드", "Power saver is on"), Short = Tx("전원이 절전 모드", "Power saver is on"), Why = Tx("제어판 전원 옵션", "Control Panel power options"), ShortWhy = Tx("전원 옵션", "Power options") }; c.Acts.Add(new Act(Tx("전원 설정 열기", "Power settings"), "power", true, Tx("전원 설정", "Power"))); r.Add(c); }

            // 5. 게임 중에 늘 CPU를 쓰는 것
            foreach (var b in Background)
            {
                if (taken.Contains(b.Key)) continue;
                Grp g; if (!snap.ByKey.TryGetValue(b.Key, out g)) continue;
                bool can = Fixer.CanTune(g);
                var rule = Fix.Find(b.Key);
                if (rule != null && (rule.Cores > 0 || rule.Prio > 0) || M.Set.Bg && Fixer.CanBg(g)) continue;
                if (!can && b.Avg < 6) continue;
                var c = new Cause { Key = b.Key, Level = can ? 1 : 0, Metric = Snap.Cpu, Title = Tx("게임 중 {0} CPU {1}%", "{0} CPU {1}% in game", b.Label, b.Avg.ToString("0")), Short = Tx("게임 중 {0} {1}%", "{0} {1}% in game", b.Label, b.Avg.ToString("0")) };
                c.Why = Tx("최근 6시간 게임 중 평균 {0}%, 최대 {1}%", "Last 6h in game: avg {0}%, max {1}%", b.Avg.ToString("0"), b.Max.ToString("0")) + (can ? "" : Tx(", 제한 불가", ", can't limit")); c.ShortWhy = Tx("평균 {0}%, 최대 {1}%", "avg {0}%, max {1}%", b.Avg.ToString("0"), b.Max.ToString("0"));
                if (can) c.Acts.Add(new Act(Tx("게임할 때 코어 2개로 제한", "Limit to 2 cores in game"), "always:" + b.Key, true, Tx("코어 제한", "Limit cores")));
                r.Add(c); taken.Add(b.Key);
                if (r.Count > 7) break;
            }

            // 6. 청소
            long temp = Temp.Scanned ? Temp.Total(true) : 0;
            if (temp >= 500L << 20) { var c = new Cause { Key = "*temp", Level = 1, Title = Tx("임시 파일 ", "Temp files ") + Fmt.Mem(temp / 1048576.0), Short = Tx("임시 파일 ", "Temp files ") + Fmt.Mem(temp / 1048576.0), Why = Tx("삭제 가능", "can be deleted"), ShortWhy = Tx("삭제 가능", "can be deleted") }; c.Acts.Add(new Act(Tx("청소 열기", "Open Clean"), "page:clean", true)); r.Add(c); }

            // 7. 패치로 풀린 게임 설정
            if (Boost.Boot.Reset && snap.Game == null) { var c = new Cause { Key = "*boot", Level = 1, Title = Tx("이터널 리턴 boot.config 초기화됨", "Eternal Return boot.config was reset"), Short = Tx("boot.config 초기화됨", "boot.config reset"), Why = Tx("패치로 게임 기본값이 됨", "A patch set it back to default"), ShortWhy = Tx("패치로 초기화됨", "Reset by a patch") }; c.Acts.Add(new Act(Tx("다시 적용", "Reapply"), "boot", true)); r.Add(c); }

            // 8. 방금 끝난 판
            if (snap.Game == null)
            {
                Round last = null; lock (Rounds.Items) if (Rounds.Items.Count > 0) last = Rounds.Items[Rounds.Items.Count - 1];
                if (last != null && (DateTime.Now - last.End).TotalMinutes < 40 && last.Hitch >= 5 && last.Why().Length > 0) { string lb = last.Game == Boost.Er ? Names.ErName : Names.LolName, lt = Tx("{0} 끊김 {1}번", "{0}: {1} {2}", lb, last.Hitch, Pl(last.Hitch, "stutter", "stutters")); var c = new Cause { Key = "*round", Level = 1, Title = lt, Short = lt, Why = last.Why(), ShortWhy = last.Why() }; c.Acts.Add(new Act(Tx("보기", "View"), "page:round", true)); r.Add(c); }
            }

            // 9. 방금 해결한 것
            lock (M.Sync)
                foreach (var f in Fix.Log)
                {
                    if ((DateTime.Now - f.At).TotalMinutes > 10) continue;
                    var c = new Cause { Key = f.Key, Level = -1, Fail = f.Fail, Title = f.Text, Short = f.Text, Why = f.Effect ?? "", ShortWhy = f.Effect ?? "" };
                    if (f.Undo && Fix.Find(f.Key) != null) c.Acts.Add(new Act(Tx("되돌리기", "Undo"), "undo:" + f.Key, true));
                    // 실패한 것은 원인을 지우지 않는다. 다시 누를 수 있어야 한다.
                    r.RemoveAll(x => x.Key == f.Key && (!f.Fail || x.Level == -1));
                    r.Add(c);
                }
            r.Sort((x, y) => Order(x).CompareTo(Order(y)));
            return r;
        }
        static int Order(Cause c) { return c.Level == 2 ? 0 : c.Level == -1 ? 1 : c.Level == 1 ? (c.Acts.Count > 0 ? 2 : 3) : 4; }

        bool Fixed(string key) { lock (M.Sync) foreach (var f in Fix.Log) if (f.Key == key && !f.Fail && (DateTime.Now - f.At).TotalMinutes < 10) return true; return false; }
        public bool Booked(string key) { lock (M.Sync) foreach (var a in Fix.Queue) if (a.Key == key) return true; return false; }
        Ev Recent(Kind k, int minutes)
        {
            lock (M.Sync) for (int i = M.Log.Items.Count - 1; i >= 0 && i >= M.Log.Items.Count - 30; i--) { var e = M.Log.Items[i]; if (e.Kind == k && (DateTime.Now - e.End).TotalMinutes < minutes) return e; }
            return null;
        }
        static string Hours(double h) { int m = (int)(h * 60); return m < 60 ? Math.Max(10, m / 10 * 10) + Tx("분", " min") : (m % 60 >= 30 ? m / 60 + Tx("시간 반", ".5h") : m / 60 + Tx("시간", "h")); }

        /// <summary>받침에 따라 이/가. 영어로 끝나면 흔히 읽는 소리로.</summary>
        public static string Ga(string s)
        {
            if (string.IsNullOrEmpty(s) || En) return s;
            char c = s[s.Length - 1];
            if (c >= 0xAC00 && c <= 0xD7A3) return s + ((c - 0xAC00) % 28 != 0 ? "이" : "가");
            char l = char.ToLowerInvariant(c);
            if (l == 'b' && s.EndsWith("HUB", StringComparison.OrdinalIgnoreCase)) return s + "가";
            return s + ("lmnr".IndexOf(l) >= 0 || l == '1' || l == '3' || l == '6' || l == '7' || l == '8' || l == '0' ? "이" : "가");
        }

        // ---- 해결 전후 ----

        void Effects()
        {
            lock (M.Sync)
                foreach (var f in Fix.Log)
                {
                    if (f.HitchBefore < 0 || f.Mono == 0 || M.Now - f.Mono < 62) continue;
                    f.Effect = Tx("1분간 끊김 {0}번 → {1}번", "Stutters per min {0} → {1}", f.HitchBefore, Hitch1m); f.HitchBefore = -1;
                }
        }

        // ---- 누른 것 ----

        [ThreadStatic] static bool failed;
        /// <summary>이 스레드에서 방금 한 조치가 안 됐는지</summary>
        public static bool Failed { get { return failed; } }
        static string No(string s) { failed = true; return s; }

        /// <summary>돌려주는 글은 화면에 잠깐 보여 준다. null 이면 화면이 따로 처리한다.</summary>
        public string Do(string id)
        {
            failed = false;
            int i = id.IndexOf(':'); string op = i < 0 ? id : id.Substring(0, i), key = i < 0 ? "" : id.Substring(i + 1);
            Grp g = null; if (key.Length > 0) lock (M.Sync) g = Fix.GroupOf(key);
            switch (op)
            {
                case "once":
                case "always":
                    {
                        if (!Fixer.CanTune(g)) return No(Tx("제한할 수 없는 프로그램", "This app can't be limited"));
                        var r = Fix.Find(key) ?? new Rule();
                        Fix.Set(new Rule { Key = key, Label = g.Label, Cores = 2, Prio = Math.Max(r.Prio, 1), Once = op == "once", Always = false });
                        bool game; lock (M.Sync) game = M.Game != null;
                        int n = Fix.AppliedCount(key);
                        if (game && n == 0) { Fix.Remove(key); return No(Tx("권한이 없어 적용 안 됨", "Not applied (no permission)")); }
                        if (game)
                        {
                            var f = Fix.Note(key, g.Label, g.Label + Tx(" 코어 2개, 우선순위 낮음", ": 2 cores, low priority"), op == "once" ? Tx("게임 끝나면 해제", "Removed after the game") : Tx("게임할 때마다 적용", "Applied every game"), true);
                            f.HitchBefore = Hitch1m > 0 ? Hitch1m : -1;
                        }
                        break;
                    }
                case "undo": Fix.Remove(key); lock (M.Sync) Fix.Log.RemoveAll(f => f.Key == key); break;
                case "after": if (!Fixer.CanRestart(g)) return No(Tx("재시작할 수 없는 프로그램", "This app can't be restarted")); Fix.Book(key, g.Label, true); break;
                case "unbook": Fix.Unbook(key); break;
                case "restart":
                    {
                        if (!Fixer.CanRestart(g)) return No(Tx("재시작할 수 없는 프로그램", "This app can't be restarted"));
                        double mb = g.Commit / 1048576.0; string label = g.Label, err = null;
                        ThreadPool.QueueUserWorkItem(o =>
                        {
                            bool ok = Fix.Restart(g, out err);
                            Thread.Sleep(8000);
                            Grp now; lock (M.Sync) now = Fix.GroupOf(key);
                            if (ok) Fix.Note(key, label, Tx("{0} 재시작함", "Restarted {0}", label), Txt.Title(Snap.Vm) + " " + Fmt.Mem(mb) + " → " + (now == null ? "0" : Fmt.Mem(now.Commit / 1048576.0)), false);
                            else Fix.Note(key, label, Tx("{0} 재시작 실패", "Couldn't restart {0}", label), err, false).Fail = true;
                            if (Changed != null) Changed();
                        });
                        return Tx("{0} 재시작 중", "Restarting {0}", label);
                    }
                case "end":
                    {
                        if (!Fixer.CanEnd(g)) return No(Tx("종료할 수 없는 프로그램", "This app can't be closed"));
                        double mb = g.Commit / 1048576.0; int n = Fix.End(g);
                        if (n > 0) Fix.Note(key, g.Label, Tx("{0} 종료함", "Closed {0}", g.Label), Tx("가상메모리 {0} 감소", "Commit down {0}", Fmt.Mem(mb)), false);
                        return n > 0 ? Tx("{0} 종료함", "Closed {0}", g.Label) : No(Tx("종료 실패", "Couldn't close"));
                    }
                case "trim":
                case "trimall":
                    {
                        var res = Cleaner.Trim(M, op == "trimall"); LastTrim = res; LastTrimAt = DateTime.Now; trimAt = M.Now;
                        if (Changed != null) Changed();
                        return TrimText(res);
                    }
                case "boot": { string e = Boost.Set(Boost.Boot, M.Set.Boot); if (e != null) return No(e); return Tx("boot.config 적용함", "boot.config applied"); }
                case "deep": DeepScan(8); return Tx("검사 중 (8초)", "Scanning (8s)");
                case "power": try { Sys.Open("powercfg.cpl"); } catch (Exception) { } break;
                default: return null;
            }
            Causes = Find(M.Now);
            if (Changed != null) Changed();
            return "";
        }

        /// <summary>램 청소 전후: 사용률과 늘어난 양</summary>
        public string TrimText(CleanResult r)
        {
            double t = Math.Max(1, M.RamTotal), gb = r.Gained / 1073741824.0;
            return Txt.Title(Snap.Ram) + " " + Fmt.Pct(1 - r.Before / t) + " → " + Fmt.Pct(1 - r.After / t) + (gb >= .05 ? Tx(", {0}GB 확보", ", {0}GB freed", gb.ToString("0.0")) : "");
        }

        /// <summary>권장 게임 설정을 한 번에 걸거나 되돌린다. boot.config 는 건드리지 않는다.</summary>
        public string ApplyGame(bool on)
        {
            string err = null;
            foreach (var g in Boost.Games) if (g.Installed) foreach (var k in g.Knobs) { if (k.Rec < 0 || k.Now < 0) continue; int v = on ? k.Rec : k.Home; if (k.Now == v && !(k is ClientKnob)) continue; err = Boost.Set(k, v) ?? err; }
            M.Set.Bg = on; M.Set.Save(); Fix.Tick();
            return err ?? (on ? Tx("적용함. 우선순위는 다음에 켤 때부터", "Applied. Priority takes effect next launch") : Tx("되돌림", "Reverted"));
        }

        // ---- 알아서 하는 것 ----

        void Auto(double now, int pid)
        {
            var s = M.Set;
            if (s.AutoTrimGame && pid != 0 && !trimmedForGame && now - trimAt > 120)
            {
                trimmedForGame = true; trimAt = now;
                ThreadPool.QueueUserWorkItem(o => { try { LastTrim = Cleaner.Trim(M, false); LastTrimAt = DateTime.Now; if (Changed != null) Changed(); } catch (Exception) { } });
            }
            double used; lock (M.Sync) used = M.RamTotal > 0 ? 100 - 100.0 * M.RamAvail / M.RamTotal : 0;
            if (s.AutoTrim && pid == 0 && used >= s.AutoTrimPct && now - trimAt > 900)
            {
                trimAt = now;
                ThreadPool.QueueUserWorkItem(o => { try { LastTrim = Cleaner.Trim(M, false); LastTrimAt = DateTime.Now; if (Changed != null) Changed(); } catch (Exception) { } });
            }
            if (pid != 0 && Hitch1m >= 5 && now - deepAuto > 300 && !WantCores) { deepAuto = now; DeepScan(8); }
        }

        public void Dispose()
        {
            Rounds.Close(M.Now); Blame.Close(); Frames.Close(); Kern.Close(); Fix.RestoreAll();
        }
    }
}
