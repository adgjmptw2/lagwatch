using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using static LagWatch.Lang;

namespace LagWatch
{
    /// <summary>게임에서 가장 바쁜 스레드 두 개의 상태</summary>
    internal sealed class GameStat
    {
        public const int N = 20;
        const byte Run = 1, Ready = 2, Disk = 3;
        readonly byte[] a = new byte[N], b = new byte[N];
        int pos;
        public int Samples;
        public int MainTid, SecondTid;
        public double Main, Second;          // 코어 하나를 쓴 비율
        public double ReadyPct, DiskPct;     // CPU 차례를 기다림 / 디스크를 기다림

        public void Reset() { pos = Samples = 0; MainTid = SecondTid = 0; Main = Second = ReadyPct = DiskPct = 0; }

        static byte Class(uint state, uint reason)
        {
            // KTHREAD_STATE: 1 Ready, 2 Running, 3 Standby, 5 Waiting, 6 Transition, 7 DeferredReady
            if (state == 2) return Run;
            if (state == 1 || state == 3 || state == 7) return Ready;
            if (state == 6) return Disk;
            if (state == 5 && Blame.MemWait(reason)) return Disk;
            return 0;
        }

        public void Push(uint s1, uint r1, uint s2, uint r2)
        {
            a[pos] = Class(s1, r1); b[pos] = Class(s2, r2);
            pos = (pos + 1) % N;
            if (Samples < N) Samples++;
            int ra = 0, rb = 0, da = 0, db = 0;
            for (int i = 0; i < Samples; i++)
            {
                if (a[i] == Ready) ra++; else if (a[i] == Disk) da++;
                if (b[i] == Ready) rb++; else if (b[i] == Disk) db++;
            }
            ReadyPct = (double)Math.Max(ra, rb) / Samples;
            DiskPct = (double)Math.Max(da, db) / Samples;
        }
    }

    internal sealed unsafe class Monitor
    {
        public readonly object Sync = new object();
        public readonly Settings Set;
        public readonly EventLog Log;
        public readonly Rules Rules;

        // 기계
        public int Lps, Phys;
        public int[] CoreOf;
        public float[] Busy, Drv;
        public double TotalCpu, BaseGhz, Ghz, LimitPct = 100;
        public long Commit, CommitLimit, CommitPeak, RamTotal, RamAvail, PagedPool, NonPagedPool;
        public double PageReads;
        public readonly History Hist;
        public int Epoch { get { return History.Epoch(DateTime.UtcNow); } }

        // 프로그램
        public readonly Dictionary<int, Proc> Procs = new Dictionary<int, Proc>(512);
        public readonly Dictionary<string, App> Apps = new Dictionary<string, App>(256);
        public Proc Game;
        public readonly GameStat G = new GameStat();
        public int ForegroundPid;
        public double Now, StartedAt;

        public volatile bool Visible;
        public Action<double> AfterStep;
        public double LastStepMs;

        byte* buf; int bufLen = 640 * 1024;
        byte* small;
        readonly Stopwatch sw = Stopwatch.StartNew();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly List<int> gone = new List<int>();
        readonly Clock clock = new Clock();
        long[] pIdle, pTot, pDrv;
        uint prevPageReads;
        double tscHz, lastAt = -1, fiveAt, clockAt = -10;
        int stamp;
        bool stop;
        Thread thread;

        public Monitor(Settings set, EventLog log, History hist = null)
        {
            Set = set; Log = log; Hist = hist ?? new History(null);
            Rules = new Rules(this);
            small = (byte*)Marshal.AllocHGlobal(64 * 1024);
            buf = (byte*)Marshal.AllocHGlobal(bufLen);
            Lps = Environment.ProcessorCount;
            Busy = new float[Lps]; Drv = new float[Lps];
            pIdle = new long[Lps]; pTot = new long[Lps]; pDrv = new long[Lps];
            CoreOf = new int[Lps];
            ReadTopology();
            double mhz = 0;
            try
            {
                object v = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "~MHz", 0);
                if (v is int) mhz = (int)v;
            }
            catch (Exception) { }
            if (mhz < 500) mhz = 3000;
            BaseGhz = mhz / 1000; tscHz = mhz * 1e6;
        }

        void ReadTopology()
        {
            int len = 60 * 1024, core = 0;
            if (Native.GetLogicalProcessorInformation(small, ref len))
            {
                // SYSTEM_LOGICAL_PROCESSOR_INFORMATION x64 = 32바이트, Relationship 0 = 실제 코어
                for (int o = 0; o + 32 <= len; o += 32)
                {
                    if (*(int*)(small + o + 8) != 0) continue;
                    ulong mask = *(ulong*)(small + o);
                    for (int i = 0; i < Lps && i < 64; i++) if ((mask & (1UL << i)) != 0) CoreOf[i] = core;
                    core++;
                }
            }
            if (core == 0) { for (int i = 0; i < Lps; i++) CoreOf[i] = i; core = Lps; }
            Phys = core;
        }

        public void Start()
        {
            thread = new Thread(Loop) { IsBackground = true, Name = "sampler" };
            thread.Start();
        }

        public void Stop()
        {
            stop = true; wake.Set();
            if (thread != null) thread.Join(2000);
            lock (Sync) { Log.Save(Now); Hist.Save(); }
        }

        public void Wake() { wake.Set(); }

        void Loop()
        {
            while (!stop)
            {
                double t0 = sw.Elapsed.TotalSeconds;
                try { Step(); }
                catch (Exception) { }
                double used = sw.Elapsed.TotalSeconds - t0;
                LastStepMs = used * 1000;
                int ms = Game != null || Visible ? 1000 : 5000;
                wake.WaitOne(Math.Max(100, ms - (int)(used * 1000)));
            }
        }

        // ---------------------------------------------------------------

        public void Step()
        {
            int ret;
            uint st = Native.NtQuerySystemInformation(Native.SystemProcessInformation, buf, bufLen, out ret);
            while (st == Native.STATUS_INFO_LENGTH_MISMATCH)
            {
                bufLen = Math.Max(bufLen, ret) + 96 * 1024;
                buf = (byte*)Marshal.ReAllocHGlobal((IntPtr)buf, (IntPtr)bufLen);
                st = Native.NtQuerySystemInformation(Native.SystemProcessInformation, buf, bufLen, out ret);
            }
            if (st != 0) return;
            double now = sw.Elapsed.TotalSeconds;
            double dt = lastAt < 0 ? 0 : now - lastAt;
            if (dt > 0 && dt < 0.08) return; // 너무 가까운 표본은 버림
            bool gap = dt > 30;              // 절전에서 깨어남
            lastAt = now;

            lock (Sync)
            {
                Now = now;
                if (StartedAt == 0) { StartedAt = now; fiveAt = now; }
                uint fpid; Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out fpid);
                ForegroundPid = (int)fpid;
                ReadCores(dt, gap);
                ReadMemory(dt, gap);
                ReadProcs(now, dt, gap);
                ReadClock(now);
                Aggregate();
                if (gap) { Rules.Reset(); fiveAt = now; }
                else if (dt > 0) { Rules.Eval(now, dt); Record(dt); }
                if (now - fiveAt >= 300) { fiveAt += Math.Floor((now - fiveAt) / 300) * 300; FiveTick(now); }
                Log.Tick(now);
            }
            if (AfterStep != null) AfterStep(now);
        }

        static float Ema(double prev, double v, double dt, double tau)
        {
            double k = 1 - Math.Exp(-dt / tau);
            return (float)(prev + (v - prev) * k);
        }

        void ReadCores(double dt, bool gap)
        {
            int ret;
            if (Native.NtQuerySystemInformation(Native.SystemProcessorPerformanceInformation, small, Lps * 48, out ret) != 0) return;
            double sum = 0;
            for (int i = 0; i < Lps; i++)
            {
                byte* e = small + i * 48;
                long idle = *(long*)e, tot = *(long*)(e + 8) + *(long*)(e + 16), drv = *(long*)(e + 24) + *(long*)(e + 32);
                long di = idle - pIdle[i], dtot = tot - pTot[i], dd = drv - pDrv[i];
                bool first = pTot[i] == 0;
                pIdle[i] = idle; pTot[i] = tot; pDrv[i] = drv;
                if (first || gap || dtot <= 0 || dt <= 0) continue;
                double busy = Math.Max(0, Math.Min(1, 1 - (double)di / dtot));
                double d = Math.Max(0, Math.Min(1, (double)dd / dtot));
                Busy[i] = Ema(Busy[i], busy, dt, 0.7);
                Drv[i] = Ema(Drv[i], d, dt, 1.5);
                sum += busy;
            }
            if (dt > 0 && !gap)
            {
                TotalCpu = Ema(TotalCpu, sum / Lps, dt, 0.7);
            }
        }

        void ReadMemory(double dt, bool gap)
        {
            int ret;
            if (Native.NtQuerySystemInformation(2, small, 4096, out ret) != 0) return;
            const long pg = 4096;
            RamAvail = *(uint*)(small + 0x2C) * pg;
            Commit = *(uint*)(small + 0x30) * pg;
            CommitLimit = *(uint*)(small + 0x34) * pg;
            CommitPeak = *(uint*)(small + 0x38) * pg;
            PagedPool = *(uint*)(small + 0x70) * pg;
            NonPagedPool = *(uint*)(small + 0x74) * pg;
            uint pr = *(uint*)(small + 0x50);
            if (dt > 0 && !gap) PageReads = Ema(PageReads, unchecked(pr - prevPageReads) / dt, dt, 2);
            prevPageReads = pr;
            if (RamTotal == 0)
            {
                Native.PERFORMANCE_INFORMATION pi;
                if (Native.GetPerformanceInfo(out pi, (uint)Marshal.SizeOf(typeof(Native.PERFORMANCE_INFORMATION))))
                    RamTotal = (long)pi.PhysicalTotal.ToUInt64() * pg;
            }
        }

        void ReadClock(double now)
        {
            if (now - clockAt < 2) return;
            if (!Visible && Game == null && TotalCpu < 0.25) { Ghz = 0; return; }
            clockAt = now;
            double lim, r = clock.Read(out lim);
            Ghz = r > 0 ? r * BaseGhz : 0;
            LimitPct = lim;
        }

        void ReadProcs(double now, double dt, bool gap)
        {
            stamp++;
            Proc game = null;
            long privSum = 0;
            byte* p = buf;
            while (true)
            {
                int next = *(int*)p, pid = (int)*(long*)(p + 0x50);
                if (pid != 0)
                {
                    long create = *(long*)(p + 0x20);
                    Proc q;
                    if (!Procs.TryGetValue(pid, out q) || q.Create != create)
                    {
                        if (q != null) Drop(q);
                        q = NewProc(p, pid, create, now);
                        Procs[pid] = q;
                    }
                    q.Stamp = stamp;
                    Update(q, p, now, dt, gap);
                    privSum += q.Commit;
                    if (q.IsGame && (game == null || q.Commit > game.Commit)) game = q;
                }
                if (next == 0) break;
                p += next;
            }
            Pseudo(-1, "paged", Tx("윈도우 커널 (페이징 풀)", "Windows kernel (paged pool)"), PagedPool, now);
            Pseudo(-2, "nonpaged", Tx("윈도우 커널 (비페이징 풀)", "Windows kernel (nonpaged pool)"), NonPagedPool, now);
            Pseudo(-3, "other", Tx("기타 (그래픽·공유 메모리)", "Other (graphics, shared memory)"), Math.Max(0, Commit - privSum - PagedPool - NonPagedPool), now);

            gone.Clear();
            foreach (var kv in Procs) if (kv.Value.Stamp != stamp) gone.Add(kv.Key);
            for (int i = 0; i < gone.Count; i++) { Drop(Procs[gone[i]]); Procs.Remove(gone[i]); }

            if (game != Game)
            {
                if (Game != null) Rules.GameEnd(now);
                Game = game;
                G.Reset();
                if (game != null) Rules.GameStart(now);
            }
        }

        void Drop(Proc q) { q.App = null; q.Threads = null; }

        Proc NewProc(byte* p, int pid, long create, double now)
        {
            int nl = *(ushort*)(p + 0x38);
            char* np = (char*)*(IntPtr*)(p + 0x40);
            string name = nl == 0 || np == null ? "PID " + pid : new string(np, 0, nl / 2);
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            var q = new Proc
            {
                Pid = pid, Create = create, Name = name, ParentPid = (int)*(long*)(p + 0x58), Session = *(int*)(p + 0x64),
                FirstSeen = now, IsGame = Set.Games.Contains(name), Cycles = *(long*)(p + 0x18), HardFaults = *(uint*)(p + 0x10)
            };
            string key, label; bool script, service;
            Names.Classify(q, out key, out label, out script, out service);
            App a;
            if (!Apps.TryGetValue(key, out a))
            {
                a = new App { Key = key, Label = label, Exe = name, Script = script, Service = service, IsGame = q.IsGame,
                    Protected = Names.IsGuarded(name), Ignored = Set.Ignore.Contains(key) };
                if (q.IsGame) a.Label = name == "EternalReturn" ? Names.ErName : name;
                Apps[key] = a;
                Prefill(a);
            }
            q.App = a;
            return q;
        }

        Dictionary<string, List<KeyValuePair<int, AppAt>>> lastDay;

        void Prefill(App a)
        {
            if (lastDay == null) { try { lastDay = Hist.LastDay(Epoch); } catch (Exception) { lastDay = new Dictionary<string, List<KeyValuePair<int, AppAt>>>(); } }
            List<KeyValuePair<int, AppAt>> v;
            if (!lastDay.TryGetValue(a.Key, out v)) return;
            // 5분 묶음은 그 앞 5분을 대표한다
            foreach (var b in v) for (int j = -4; j <= 0; j++) a.H.Set(b.Key + j, b.Value.Commit, b.Value.Ws, b.Value.Cores);
            lastDay.Remove(a.Key);
        }

        void Pseudo(int pid, string key, string label, long bytes, double now)
        {
            Proc q;
            if (!Procs.TryGetValue(pid, out q))
            {
                var a = new App { Key = "*" + key, Label = label, Exe = "", Pseudo = true, InfoDone = true, Ignored = Set.Ignore.Contains("*" + key) };
                Apps[a.Key] = a;
                Prefill(a);
                q = new Proc { Pid = pid, Name = label, App = a, Pseudo = true, FirstSeen = now };
                Procs[pid] = q;
            }
            q.Stamp = stamp;
            q.Commit = bytes;
            if (bytes < q.BucketMin) q.BucketMin = bytes;
        }

        void Update(Proc q, byte* p, double now, double dt, bool gap)
        {
            long cyc = *(long*)(p + 0x18);
            uint hf = *(uint*)(p + 0x10);
            q.Commit = *(long*)(p + 0xC8);
            q.Ws = *(long*)(p + 0x90);
            q.Handles = *(int*)(p + 0x60);
            q.ThreadCount = *(int*)(p + 4);
            if (q.Commit < q.BucketMin) q.BucketMin = q.Commit;
            if (q.Handles < q.HandleMin) q.HandleMin = q.Handles;

            if (dt > 0 && !gap)
            {
                double cores = Math.Max(0, (cyc - q.Cycles) / (tscHz * dt));
                if (cores > Lps * 1.5) cores = 0;
                q.Cores = Ema(q.Cores, cores, dt, 0.7);
                q.FaultsPerSec = Ema(q.FaultsPerSec, unchecked(hf - q.HardFaults) / dt, dt, 1.5);
            }
            q.Cycles = cyc; q.HardFaults = hf;

            if (q.IsGame || q.Cores >= 0.6 || (q.Threads != null && q.Cores >= 0.4)) TrackThreads(q, p, now, gap);
            else if (q.Threads != null) { q.Threads = null; q.ThreadsAt = -1; q.Hot = 0; q.HotSince = -1; }
            else q.Hot = q.Cores;
        }

        void TrackThreads(Proc q, byte* p, double now, bool gap)
        {
            int n = q.ThreadCount;
            byte* t = p + 0x100;
            bool first = q.Threads == null;
            if (first) q.Threads = new Dictionary<int, ThreadTrack>(n + 8);
            double dt = now - q.ThreadsAt;
            q.ThreadsAt = now;
            double top = 0, second = 0, cur = 0; int topTid = 0, secondTid = 0;
            for (int i = 0; i < n; i++, t += 80)
            {
                int tid = (int)*(long*)(t + 0x30);
                long time = *(long*)t + *(long*)(t + 8);
                ThreadTrack k;
                if (!q.Threads.TryGetValue(tid, out k)) { q.Threads[tid] = k = new ThreadTrack { Time = time }; }
                else if (!first && !gap && dt > 0)
                    k.Load = Ema(k.Load, Math.Max(0, Math.Min(1.2, (time - k.Time) / 1e7 / dt)), dt, q.IsGame ? 2 : 1.5);
                k.Time = time; k.Stamp = stamp;
                if (k.Load > top) { second = top; secondTid = topTid; top = k.Load; topTid = tid; }
                else if (k.Load > second) { second = k.Load; secondTid = tid; }
                if (q.IsGame && tid == G.MainTid) cur = k.Load;
            }
            if (q.Threads.Count > n * 2 + 32)
            {
                var dead = new List<int>();
                foreach (var kv in q.Threads) if (kv.Value.Stamp != stamp) dead.Add(kv.Key);
                foreach (int d in dead) q.Threads.Remove(d);
            }
            q.Hot = Math.Min(1, top);
            if (q.Hot >= 0.9) { if (q.HotSince < 0) q.HotSince = now; } else if (q.Hot < 0.8) q.HotSince = -1;

            if (!q.IsGame || (Game != null && q != Game)) return;
            // 게임의 바쁜 스레드 두 개. 엎치락뒤치락하지 않게 10%p 넘게 앞설 때만 바꾼다.
            if (G.MainTid == 0 || (topTid != G.MainTid && top > cur + 0.10)) G.MainTid = topTid;
            G.SecondTid = G.MainTid == topTid ? secondTid : topTid;
            uint s1 = 5, r1 = 0, s2 = 5, r2 = 0;
            t = p + 0x100;
            double ml = 0, sl = 0;
            for (int i = 0; i < n; i++, t += 80)
            {
                int tid = (int)*(long*)(t + 0x30);
                if (tid == G.MainTid) { s1 = *(uint*)(t + 0x44); r1 = *(uint*)(t + 0x48); ml = q.Threads[tid].Load; }
                else if (tid == G.SecondTid) { s2 = *(uint*)(t + 0x44); r2 = *(uint*)(t + 0x48); sl = q.Threads[tid].Load; }
            }
            G.Main = Math.Min(1, ml); G.Second = Math.Min(1, sl);
            if (!first && topTid != 0) G.Push(s1, r1, s2, r2);
        }

        void Aggregate()
        {
            foreach (var a in Apps.Values)
            {
                a.Count = 0; a.Cores = 0; a.Hot = 0; a.Ws = 0; a.Commit = 0; a.Leaking = false; a.LeakGrow = 0; a.LeakMinutes = 0;
            }
            foreach (var q in Procs.Values)
            {
                var a = q.App;
                if (a == null) continue;
                a.Count++; a.Cores += q.Cores; a.Ws += q.Ws; a.Commit += q.Commit;
                if (q.Hot > a.Hot) a.Hot = q.Hot;
                if (q.Leaking)
                {
                    a.Leaking = true; a.LeakGrow += q.LeakGrow;
                    if (q.LeakMinutes > a.LeakMinutes) a.LeakMinutes = q.LeakMinutes;
                }
                a.LastSeen = Now;
            }
        }

        /// <summary>그래프에 남길 값을 쌓는다. 1분마다 프로그램별 기록, 5분마다 파일에 덧붙인다.</summary>
        void Record(double dt)
        {
            float core = 0, drv = 0;
            for (int i = 0; i < Lps; i++) { if (Busy[i] > core) core = Busy[i]; if (Drv[i] > drv) drv = Drv[i]; }
            var p = new MPoint
            {
                T = Epoch, Cpu = (float)TotalCpu, Core = core, Drv = drv, Ghz = (float)Ghz,
                Ram = RamTotal > 0 ? (float)(1 - (double)RamAvail / RamTotal) : 0,
                Commit = CommitLimit > 0 ? (float)((double)Commit / CommitLimit) : 0, CommitGb = (float)(Commit / 1073741824.0)
            };
            if (Game != null && Game.App != null)
            {
                p.Game = Hist.Id(Game.App.Key, Game.App.Label);
                if (G.Samples >= 6) { p.GMain = (float)G.Main; p.GReady = (float)G.ReadyPct; p.GDisk = (float)G.DiskPct; }
            }
            if (Hist.Add(p, dt)) MinuteTick(p.T / 60 - 1);
            foreach (var a in Apps.Values) if (a.Count > 0) { a.Acc += a.Cores * dt; a.AccT += dt; }
        }

        void MinuteTick(int minute)
        {
            if (lastDay != null && lastDay.Count > 0 && Now - StartedAt > 1800) lastDay.Clear();
            List<string> dead = null;
            bool block = minute % 5 == 4;
            var top = block ? new List<App>(128) : null;
            foreach (var kv in Apps)
            {
                var a = kv.Value;
                if (a.Count > 0)
                {
                    double c = a.AccT > 0 ? a.Acc / a.AccT : a.Cores;
                    a.H.Set(minute, a.Commit / 1048576.0, a.Ws / 1048576.0, c);
                    a.FiveCores += c; a.FiveN++;
                    if (block && (a.Commit >= 100L << 20 || a.FiveCores / a.FiveN >= 0.05)) top.Add(a);
                }
                else if (Now - a.LastSeen > (a.H.Peak() >= 200 || a.H.PeakCores() >= 0.3 ? 86400 : 1800)) { if (dead == null) dead = new List<string>(); dead.Add(kv.Key); }
                a.Acc = a.AccT = 0;
            }
            if (block)
            {
                top.Sort((x, y) => (y.Commit / 1048576.0 + y.FiveCores / y.FiveN * 2000).CompareTo(x.Commit / 1048576.0 + x.FiveCores / x.FiveN * 2000));
                var b = new Block { Minute = minute, Apps = new AppAt[Math.Min(History.TopApps, top.Count)] };
                for (int i = 0; i < b.Apps.Length; i++)
                {
                    var a = top[i];
                    EnsureInfo(a);
                    b.Apps[i] = new AppAt { Id = Hist.Id(a.Key, a.Label), Commit = a.Commit / 1048576f, Ws = a.Ws / 1048576f, Cores = (float)(a.FiveCores / a.FiveN) };
                }
                Hist.AddBlock(b);
                Hist.Save();
                foreach (var a in Apps.Values) { a.FiveCores = 0; a.FiveN = 0; }
            }
            if (dead != null) foreach (var k in dead) Apps.Remove(k);
        }

        void FiveTick(double now)
        {
            long ft = DateTime.UtcNow.ToFileTimeUtc();
            foreach (var q in Procs.Values)
            {
                double age = q.Pseudo ? now - q.FirstSeen : (ft - q.Create) / 1e7;
                if (age >= 300 && q.BucketMin != long.MaxValue)
                {
                    if (q.Lows == null) q.Lows = new FloatRing(288);
                    q.Lows.Add(q.BucketMin / 1048576f);
                    if (q.HandleMin != int.MaxValue && (q.HandleMin >= 3000 || q.HandleLows != null))
                    {
                        if (q.HandleLows == null) q.HandleLows = new FloatRing(144);
                        q.HandleLows.Add(q.HandleMin);
                    }
                }
                q.BucketMin = q.Commit; q.HandleMin = q.Pseudo ? int.MaxValue : q.Handles;
            }
            foreach (var a in Apps.Values)
            {
                if (a.Count == 0) continue;
                if (a.Counts == null) { if (a.Count < 4) continue; a.Counts = new FloatRing(144); }
                a.Counts.Add(a.Count);
            }
            Rules.Leaks(now);
        }

        public void EnsureInfo(App a)
        {
            if (a.InfoDone) return;
            foreach (var q in Procs.Values)
            {
                if (q.App != a) continue;
                Names.FillInfo(a, q.Pid);
                // 이름이 같은 프로그램이 또 있으면 파일 이름을 붙여 구분
                foreach (var o in Apps.Values)
                {
                    if (o == a || !o.InfoDone || o.Pseudo || o.Label != a.Label || o.Exe == a.Exe) continue;
                    if (!o.IsGame) o.Label += " (" + o.Exe + ")";
                    if (!a.IsGame) a.Label += " (" + a.Exe + ")";
                    break;
                }
                return;
            }
        }

        /// <summary>게임으로 볼지 바꾼다.</summary>
        public void SetGame(App a, bool on)
        {
            lock (Sync)
            {
                if (on) Set.Games.Add(a.Exe); else Set.Games.Remove(a.Exe);
                a.IsGame = on;
                foreach (var q in Procs.Values) if (q.App == a) q.IsGame = on;
                Set.Save();
            }
        }
    }
}
