using System;
using System.Collections.Generic;
using static LagWatch.Lang;

namespace LagWatch
{
    /// <summary>화면에서 한 이름으로 보이는 프로그램 묶음</summary>
    internal sealed class Grp
    {
        public string Key, Label;
        public readonly List<App> Apps = new List<App>();
        public double Cores;
        public long Ws, Commit;
        public int Count;
        public bool IsGame;
        public void Add(App a) { Apps.Add(a); Cores += a.Cores; Ws += a.Ws; Commit += a.Commit; Count += a.Count; if (a.IsGame) IsGame = true; }
        public App Main { get { App t = null; foreach (var a in Apps) if (!a.Pseudo && (t == null || a.Commit > t.Commit)) t = a; return t ?? (Apps.Count > 0 ? Apps[0] : null); } }
    }

    internal sealed class Part
    {
        public string Key, Label; public Grp G; public double V;
        public int Role;    // 0 게임, 1 기타, 2 윈도우, 3 프로그램, 4 문제
        public int Rank;    // 프로그램 중 몇 번째로 큰지
        public int N;
    }

    /// <summary>화면 한 장을 그릴 값. 잠금 안에서 한 번에 베낀다.</summary>
    internal sealed class Snap
    {
        public const int Cpu = 0, Ram = 1, Vm = 2;
        public readonly List<Grp> Groups = new List<Grp>();
        public readonly Dictionary<string, Grp> ByKey = new Dictionary<string, Grp>();
        public Grp Game;
        public double CpuPct, RamGb, RamLeft, VmGb, VmLeft, VmPeak, Ghz, GameSec, Wait, Main;
        public double[] Core, Drv;
        public int Phys, Lps, Programs, Epoch;
        double scale = 1;

        public static Snap Take(Monitor m)
        {
            var s = new Snap();
            lock (m.Sync) s.Fill(m);
            return s;
        }

        void Fill(Monitor m)
        {
            Phys = m.Phys; Lps = m.Lps; Epoch = m.Epoch;
            foreach (var a in m.Apps.Values)
            {
                if (a.Count == 0) continue;
                string k, l; Names.Group(a, out k, out l);
                Grp g; if (!ByKey.TryGetValue(k, out g)) { ByKey[k] = g = new Grp { Key = k, Label = l }; Groups.Add(g); }
                g.Add(a);
                if (!a.Pseudo) Programs++;
            }
            foreach (var g in Groups)
            {
                if (g.Apps.Count == 1 && !g.Apps[0].InfoDone && (g.Commit > 200L << 20 || g.Cores > 0.1)) m.EnsureInfo(g.Apps[0]);
                if (g.Apps.Count == 1) { string k, l; Names.Group(g.Apps[0], out k, out l); g.Label = l; }
                if (g.IsGame && (Game == null || g.Commit > Game.Commit)) Game = g;
            }
            double sum = 0; foreach (var g in Groups) sum += g.Cores;
            CpuPct = m.TotalCpu * 100;
            scale = sum > 0.05 ? Math.Max(0.5, Math.Min(1.2, m.TotalCpu * Lps / sum)) : 1;
            RamGb = m.RamTotal / 1073741824.0; RamLeft = m.RamAvail / 1073741824.0;
            VmGb = m.CommitLimit / 1073741824.0; VmLeft = Math.Max(0, m.CommitLimit - m.Commit) / 1073741824.0; VmPeak = m.CommitPeak / 1073741824.0;
            Ghz = m.Ghz; Wait = m.Game != null && m.G.Samples >= 6 ? m.G.ReadyPct : 0; Main = m.Game != null ? m.G.Main : 0;
            GameSec = m.Game != null ? m.Now - m.Game.FirstSeen : 0;
            if (m.Game != null && m.Game.Create > 0) GameSec = (DateTime.UtcNow.ToFileTimeUtc() - m.Game.Create) / 1e7;
            Core = new double[Phys]; Drv = new double[Phys]; var n = new int[Phys];
            for (int i = 0; i < Lps; i++) { int c = m.CoreOf[i]; Core[c] += m.Busy[i] * 100; Drv[c] += m.Drv[i] * 100; n[c]++; }
            for (int i = 0; i < Phys; i++) if (n[i] > 0) { Core[i] /= n[i]; Drv[i] /= n[i]; }
        }

        public double Cap(int m) { return m == Cpu ? 100 : m == Ram ? RamGb : VmGb; }
        public double Used(int m) { return m == Cpu ? CpuPct : m == Ram ? RamGb - RamLeft : VmGb - VmLeft; }
        public double Left(int m) { return Math.Max(0, Cap(m) - Used(m)); }
        public double Val(Grp g, int m) { return m == Cpu ? g.Cores / Lps * 100 * scale : m == Ram ? g.Ws / 1073741824.0 : g.Commit / 1073741824.0; }

        /// <summary>아래에서 위로: 게임 → 기타 → 윈도우 → 작은 것 → 큰 것 → 문제. 합은 쓴 양과 같다.</summary>
        public List<Part> Parts(int m, int named, string culprit)
        {
            double cap = Cap(m), used = Used(m);
            var big = new List<Grp>();
            Grp bad = null, win = null;
            foreach (var g in Groups)
            {
                if (g == Game) continue;
                if (g.Key == "win") { win = g; continue; }
                if (culprit != null && g.Key == culprit) { bad = g; continue; }
                if (Val(g, m) / cap >= 0.02) big.Add(g);
            }
            big.Sort((x, y) => Val(y, m).CompareTo(Val(x, m)));
            int keep = bad != null ? named - 1 : named;
            if (big.Count > keep) big.RemoveRange(keep, big.Count - keep);
            bool showWin = win != null && Val(win, m) / cap >= 0.04;
            var r = new List<Part>();
            double sum = 0; int shown = 0;
            if (Game != null) { r.Add(new Part { Key = Game.Key, Label = Game.Label, G = Game, V = Val(Game, m), Role = 0 }); sum += r[0].V; shown++; }
            var rest = new Part { Key = "*rest", Label = Tx("기타", "Other"), Role = 1 }; r.Add(rest);
            if (showWin) { r.Add(new Part { Key = "win", Label = Tx("윈도우", "Windows"), G = win, V = Val(win, m), Role = 2 }); sum += Val(win, m); shown += win.Apps.Count; }
            for (int i = big.Count - 1; i >= 0; i--) { double v = Val(big[i], m); r.Add(new Part { Key = big[i].Key, Label = big[i].Label, G = big[i], V = v, Role = 3, Rank = i }); sum += v; shown += big[i].Apps.Count; }
            if (bad != null) { double v = Val(bad, m); r.Add(new Part { Key = bad.Key, Label = bad.Label, G = bad, V = v, Role = 4 }); sum += v; shown += bad.Apps.Count; }
            // 이름 있는 것의 합이 더 크면(공유 메모리를 겹쳐 셈) 비율로 맞춘다
            if (sum > used && sum > 0) { double k = used / sum; foreach (var p in r) p.V *= k; sum = used; }
            rest.V = Math.Max(0, used - sum); rest.N = Math.Max(0, Programs - shown);
            rest.Label = rest.N > 0 ? Tx("기타 {0}개", "Other ({0})", rest.N) : Tx("기타", "Other");
            return r;
        }
    }

    /// <summary>시간 흐름</summary>
    internal static class Series
    {
        /// <summary>묶음의 분 단위 커밋(MB). 없던 때는 -1</summary>
        public static void Commit(Grp g, int from, int to, float[] v)
        {
            for (int t = from; t <= to; t++)
            {
                double s = 0; bool any = false; float c, w, k;
                foreach (var a in g.Apps) if (a.H.Get(t, out c, out w, out k)) { s += c; any = true; }
                v[t - from] = any ? (float)s : -1;
            }
        }

        /// <summary>렉 감시를 다시 켜는 사이처럼 잠깐 빈 곳을 앞 값으로 잇는다.</summary>
        public static void Bridge(float[] v, int n, int max)
        {
            int i = 0; while (i < n && v[i] < 0) i++;
            while (i < n)
            {
                if (v[i] >= 0) { i++; continue; }
                int a = i; while (i < n && v[i] < 0) i++;
                if (i - a <= max) for (int k = a; k < i; k++) v[k] = v[a - 1];
            }
        }

        /// <summary>
        /// 세 토막의 평균이 차례로 오르고, 마지막 토막에서도 오르고 있고, 한 번에 뛴 것이 아니어야 늘어나는 중.
        /// </summary>
        public static bool Rising(float[] v, int n, float minGrow, out float first, out float last)
        {
            first = last = 0;
            int a = 0; while (a < n && v[a] < 0) a++;
            int len = n - a;
            if (len < 60 || len < n * 0.8) return false;
            for (int i = a; i < n; i++) if (v[i] < 0) v[i] = i > a ? v[i - 1] : 0;
            int t = len / 3;
            double x = 0, y = 0, z = 0;
            for (int i = 0; i < t; i++) { x += v[a + i]; y += v[a + t + i]; z += v[n - t + i]; }
            x /= t; y /= t; z /= t;
            double grow = z - x;
            if (!(y > x && z > y) || grow < minGrow || z - y < 0.3 * grow) return false;
            // 다섯 칸 평균으로 가장 큰 계단이 절반을 넘으면 한 번 뛴 것
            int w = Math.Max(3, len / 24); double prev = -1, step = 0;
            for (int i = a; i + w <= n; i += w) { double s = 0; for (int j = 0; j < w; j++) s += v[i + j]; s /= w; if (prev >= 0 && s - prev > step) step = s - prev; prev = s; }
            double total = 0; { double s0 = 0, s1 = 0; for (int j = 0; j < w; j++) { s0 += v[a + j]; s1 += v[n - 1 - j]; } s0 /= w; s1 /= w; first = (float)s0; last = (float)s1; total = s1 - s0; }
            if (total < minGrow || step > 0.5 * total) return false;
            { double r0 = 0, r1 = 0; for (int j = 0; j < 5; j++) { r0 += v[n - 35 + j]; r1 += v[n - 5 + j]; } if ((r1 - r0) / 5 < 0.05 * total) return false; } // 최근 30분 사이에 멈췄으면 아님
            return true;
        }
    }
}
