using System;
using System.Collections.Generic;
using System.Text;
using static LagWatch.Lang;

namespace LagWatch
{
    /// <summary>무엇을 문제로 볼지. 기준값은 여기 한곳에만 둔다.</summary>
    internal sealed class Rules
    {
        const double CpuFullAt = 0.92, CpuFullSec = 3;
        const double WaitAt = 0.15;
        const double OthersLoad = 0.55; // 전체의 절반을 넘으면 하이퍼스레딩 코어를 나눠 쓰기 시작
        const double OneCoreSec = 20;
        const double DriverAt = 0.15, DriverSec = 3;
        const double SlowAt = 0.92, SlowLoad = 0.25, SlowSec = 5;
        const double CommitWarn = 0.85, CommitBad = 0.93, CommitSec = 10;
        const double RamSec = 5;
        /// <summary>램이 모자라 디스크에서 다시 읽는 중인지. 화면과 기록이 같은 기준을 쓴다.</summary>
        public static bool RamLow(Monitor m) { return m.RamTotal > 0 && m.RamAvail < Math.Max(1L << 30, (long)(m.RamTotal * 0.08)) && m.PageReads >= 1000; }
        const double GameFaults = 300, GameFaultSec = 3;

        readonly Monitor m;
        readonly Hold cpuFull = new Hold(), others = new Hold(), slow = new Hold(), commit = new Hold(), commitBad = new Hold(), ram = new Hold(), gameDisk = new Hold();
        Hold[] driver;

        /// <summary>제목, 내용</summary>
        public Action<string, string> Notify;

        // 게임 한 번 켠 동안
        double gameFrom, gameAcc, gameMainFull;
        string gameLabel = "";
        double othersCores;
        bool commitBadTold;

        public Rules(Monitor m) { this.m = m; }

        public void Reset()
        {
            cpuFull.Reset(); others.Reset(); slow.Reset(); commit.Reset(); commitBad.Reset(); ram.Reset(); gameDisk.Reset();
            if (driver != null) foreach (var h in driver) h.Reset();
        }

        public double OthersLimit { get { return Math.Max(2.0, m.Phys * 0.4); } }

        public void Eval(double now, double dt)
        {
            bool inGame = m.Game != null;
            var log = m.Log;

            if (cpuFull.On(m.TotalCpu >= CpuFullAt, now, CpuFullSec))
                log.Report(Kind.CpuFull, "", m.TotalCpu, now - CpuFullSec, now, inGame, () => "CPU " + Fmt.Pct(m.TotalCpu) + " · " + TopCpu(3, false));

            if (inGame)
            {
                othersCores = 0;
                foreach (var a in m.Apps.Values) if (!a.IsGame && !a.Pseudo) othersCores += a.Cores;
                if (others.On(othersCores >= OthersLimit && m.TotalCpu >= OthersLoad, now, 5))
                    log.Report(Kind.Others, "", othersCores, now - 5, now, true, () => Tx("다른 프로그램 ", "Other apps ") + Fmt.Cores(othersCores) + " · " + TopCpu(3, true));

                if (m.G.Samples >= GameStat.N && m.G.ReadyPct >= WaitAt)
                    log.Report(Kind.GameWait, "", m.G.ReadyPct, now - 5, now, true, () => m.Game.App.Label + Tx(" · CPU 대기 ", " · CPU wait ") + Fmt.Pct(m.G.ReadyPct) + " · " + TopCpu(3, true));

                if (gameDisk.On(m.Game.FaultsPerSec >= GameFaults && m.RamAvail < m.RamTotal * 0.15, now, GameFaultSec))
                    log.Report(Kind.Ram, "game", m.Game.FaultsPerSec, now - GameFaultSec, now, true,
                        () => m.Game.App.Label + Tx(" · 램 부족으로 디스크 읽기 초당 {0}번 · 램 상위 ", " · low RAM, {0} disk reads/s · top RAM: ", m.Game.FaultsPerSec.ToString("0")) + TopMem(2, true));

                gameAcc += dt;
                if (m.G.Main >= 0.95) gameMainFull += dt;
            }
            else others.Reset();

            if (driver == null) { driver = new Hold[m.Lps]; for (int i = 0; i < driver.Length; i++) driver[i] = new Hold(); }
            for (int i = 0; i < m.Lps; i++)
            {
                if (driver[i].On(m.Drv[i] >= DriverAt, now, DriverSec)) ReportDriver(i, now, inGame);
            }

            if (slow.On(m.Ghz > 0 && m.Ghz < m.BaseGhz * SlowAt && m.TotalCpu >= SlowLoad, now, SlowSec))
                log.Report(Kind.Slow, "", m.BaseGhz / m.Ghz, now - SlowSec, now, inGame,
                    () => Tx("CPU 클럭 ", "CPU clock ") + Fmt.Ghz(m.Ghz) + Tx(" (기본 ", " (base ") + Fmt.Ghz(m.BaseGhz) + (m.LimitPct < 99.5 ? Tx(", 제한 ", ", limit ") + m.LimitPct.ToString("0") + "%" : "") + ")");

            double cu = m.CommitLimit > 0 ? (double)m.Commit / m.CommitLimit : 0;
            if (commit.On(cu >= CommitWarn, now, CommitSec))
            {
                if (log.Report(Kind.Commit, "", cu, now - CommitSec, now, inGame, CommitText)) Tell(Txt.Title(Snap.Vm) + " " + Fmt.Pct(cu), TopMem(3, false));
            }
            if (commitBad.On(cu >= CommitBad, now, 3))
            {
                if (!commitBadTold) { commitBadTold = true; Tell(Tx("가상메모리 부족 ({0})", "Low commit memory ({0})", Fmt.Pct(cu)), TopMem(3, false)); }
            }
            else if (cu < CommitWarn) commitBadTold = false;

            if (ram.On(RamLow(m), now, RamSec))
                log.Report(Kind.Ram, "", m.PageReads, now - RamSec, now, inGame,
                    () => Tx("램 부족 ({0}GB 남음) · ", "Low RAM ({0}GB free) · ", Fmt.Gb(m.RamAvail)) + TopMem(3, false, true));

            foreach (var q in m.Procs.Values)
            {
                if (q.HotSince < 0 || q.IsGame || q.App == null || q.App.Ignored) continue;
                if (q.Pid == m.ForegroundPid || now - q.HotSince < OneCoreSec) continue;
                ReportOneCore(q, now, inGame);
            }
        }

        void ReportDriver(int lp, double now, bool inGame)
        {
            m.Log.Report(Kind.Driver, "c" + m.CoreOf[lp], m.Drv[lp], now - DriverSec, now, inGame,
                () => Tx("{0}번 코어 드라이버 처리 {1}", "Core {0} driver time {1}", m.CoreOf[lp] + 1, Fmt.Pct(m.Drv[lp])));
        }

        void ReportOneCore(Proc q, double now, bool inGame)
        {
            m.EnsureInfo(q.App);
            m.Log.Report(Kind.OneCore, q.App.Key, now - q.HotSince, q.HotSince, now, inGame,
                () => q.App.Label + Tx(" 스레드 하나가 코어 1개를 100% 사용", ": one thread at 100% of a core"));
        }

        string CommitText()
        {
            return Txt.Title(Snap.Vm) + " " + Fmt.Pct((double)m.Commit / m.CommitLimit) + " (" + Fmt.Gb(m.Commit) + " / " + Fmt.Gb(m.CommitLimit) + "GB) · " + TopMem(3, false);
        }

        // ---- 5분마다 ----

        public void Leaks(double now)
        {
            foreach (var q in m.Procs.Values)
            {
                if (q.App == null) continue;
                float g; int min;
                bool skip = q.IsGame || q.App.Ignored || q.Name == "Memory Compression";
                q.Leaking = Leak.Find(skip ? null : q.Lows, out g, out min);
                q.LeakGrow = q.Leaking ? g : 0;
                q.LeakMinutes = q.Leaking ? min : 0;
                if (q.Leaking)
                {
                    float cur = q.Commit / 1048576f;
                    if (q.AlertedAt == 0 || cur >= q.AlertedAt + Math.Max(500, q.AlertedAt * 0.5f))
                    {
                        q.AlertedAt = cur;
                        m.EnsureInfo(q.App);
                        string t = Tx("{0} 동안 {1} 증가 (현재 {2})", "+{1} in {0} (now {2})", Fmt.Dur(min * 60), Fmt.Mem(g), Fmt.Mem(cur));
                        m.Log.Add(Kind.Leak, q.App.Key, q.App.Label + " · " + t, now, m.Game != null);
                        Tell(q.App.Label, t);
                    }
                }
                float hg;
                if (!skip && q.Handles >= 10000 && Leak.Check(q.HandleLows, 12, 4000, 0.25f, out hg) && q.Handles >= q.HandleAlertedAt + Math.Max(5000, q.HandleAlertedAt / 2))
                {
                    q.HandleAlertedAt = q.Handles;
                    m.EnsureInfo(q.App);
                    m.Log.Add(Kind.Leak, q.App.Key, q.App.Label + Tx(" · 핸들 1시간 동안 {0}개 증가 (현재 {1}개)", " · handles +{0} in 1h (now {1})", hg.ToString("#,0"), q.Handles.ToString("#,0")), now, m.Game != null);
                }
            }
            foreach (var a in m.Apps.Values)
            {
                float g;
                if (a.Ignored || a.Service || a.Count < 12 || !Leak.Check(a.Counts, 36, 8, 0.5f, out g)) continue;
                if (a.Count < a.CountAlertedAt * 1.5) continue;
                a.CountAlertedAt = a.Count;
                m.EnsureInfo(a);
                m.Log.Add(Kind.Procs, a.Key, a.Label + Tx(" · 프로세스 3시간 동안 {0}개 증가 (현재 {1}개)", " · processes +{0} in 3h (now {1})", g.ToString("0"), a.Count), now, m.Game != null);
            }
        }

        // ---- 게임 ----

        public void GameStart(double now)
        {
            gameFrom = now; gameAcc = 0; gameMainFull = 0;
            gameLabel = m.Game.App.Label;
            Started = m.Log.Now();
        }
        public DateTime Started;

        public void GameEnd(double now)
        {
            double sec = now - gameFrom;
            string label = gameLabel;
            string sum = GameSummary();
            if (sec >= 180)
            {
                string text = label + " " + Fmt.Dur(sec) + " · " + (sum.Length == 0 ? Tx("문제 없음", "no issues") : sum);
                if (gameAcc > 60 && gameMainFull / gameAcc >= 0.05) text += Tx(" · 메인 스레드 100% 구간 ", " · main thread at 100%: ") + Fmt.Pct(gameMainFull / gameAcc);
                m.Log.Add(Kind.Game, "", text, now, false);
            }
        }

        string GameSummary()
        {
            var n = new int[Enum.GetValues(typeof(Kind)).Length];
            foreach (var e in m.Log.Items)
                if (e.InGame && e.End >= Started && e.Kind != Kind.Game) n[(int)e.Kind]++;
            var sb = new StringBuilder();
            for (int i = 0; i < n.Length; i++)
            {
                if (n[i] == 0) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(Kinds.Label((Kind)i)).Append(' ').Append(En ? "×" : "").Append(n[i]).Append(Tx("번", ""));
            }
            return sb.ToString();
        }

        // ---- 알림: 게임 중에는 띄우지 않는다(그래프와 기록에만 남김) ----

        void Tell(string title, string text)
        {
            if (Notify == null || !m.Set.Notify || m.Game != null) return;
            Notify(title, text);
        }

        // ---- 많이 쓰는 것 ----

        public App TopCpuApp()
        {
            App t = null;
            foreach (var a in m.Apps.Values) if (!a.Pseudo && !a.IsGame && (t == null || a.Cores > t.Cores)) t = a;
            return t;
        }

        public string TopCpu(int n, bool noGame)
        {
            var top = new List<App>();
            foreach (var a in m.Apps.Values)
            {
                if (a.Pseudo || a.Cores < 0.1 || (noGame && a.IsGame)) continue;
                int i = top.Count;
                while (i > 0 && top[i - 1].Cores < a.Cores) i--;
                if (i < n) { top.Insert(i, a); if (top.Count > n) top.RemoveAt(n); }
            }
            var sb = new StringBuilder();
            foreach (var a in top)
            {
                m.EnsureInfo(a);
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(a.Label).Append(' ').Append(Fmt.Cores(a.Cores));
            }
            return sb.Length == 0 ? Tx("사용량 높은 프로그램 없음", "No busy apps") : sb.ToString();
        }

        public string TopMem(int n, bool noGame, bool ws = false)
        {
            var top = new List<App>();
            foreach (var a in m.Apps.Values)
            {
                long v = ws ? a.Ws : a.Commit;
                if (v == 0 || (noGame && a.IsGame) || a.Key == "*paged" || a.Key == "*nonpaged") continue;
                int i = top.Count;
                while (i > 0 && (ws ? top[i - 1].Ws : top[i - 1].Commit) < v) i--;
                if (i < n) { top.Insert(i, a); if (top.Count > n) top.RemoveAt(n); }
            }
            var sb = new StringBuilder();
            foreach (var a in top)
            {
                m.EnsureInfo(a);
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(a.Label).Append(' ').Append(Fmt.Mem((ws ? a.Ws : a.Commit) / 1048576.0));
            }
            return sb.ToString();
        }
    }
}
