using System;
using System.Collections.Generic;
using static LagWatch.Lang;

namespace LagWatch
{
    /// <summary>시간별 그래프 자료</summary>
    internal sealed class TimeSet
    {
        internal sealed class Layer { public string Key, Label; public Grp G; public float[] V; public int Role, Rank; public double Avg; }
        internal struct Play { public int A, B; public string Label; }
        public int M, Hours, From, Step, N, Minute;         // 분 단위
        public readonly List<Layer> L = new List<Layer>();  // 아래에서 위로
        public float[] Total;                               // 없던 때 -1
        public readonly List<Play> Plays = new List<Play>();
        public readonly List<int> Hits = new List<int>();   // 끊김·CPU 대기가 있던 분
        public double Cap;

        public static TimeSet Build(Monitor m, Snap s, int metric, int hours, string culprit)
        {
            var t = new TimeSet { M = metric, Hours = hours, Step = hours <= 3 ? 1 : hours <= 12 ? 3 : 5, Cap = s.Cap(metric) };
            int now = s.Epoch / 60; t.Minute = now; t.N = hours * 60 / t.Step + 1; t.From = now - (t.N - 1) * t.Step;
            t.Total = new float[t.N]; for (int i = 0; i < t.N; i++) t.Total[i] = -1;
            var pts = new List<MPoint>(); var cnt = new int[t.N];
            var all = new Dictionary<string, Layer>();
            lock (m.Sync)
            {
                m.Hist.Range((t.From - t.Step) * 60.0, s.Epoch, pts);
                int pa = 0; ushort pg = 0; int last = 0;
                foreach (var p in pts)
                {
                    int mn = p.T / 60, i = (mn - t.From + t.Step - 1) / t.Step;
                    if (i >= 0 && i < t.N)
                    {
                        float v = metric == Snap.Cpu ? p.Cpu * 100 : metric == Snap.Ram ? p.Ram * (float)s.RamGb : p.CommitGb;
                        if (cnt[i] == 0) t.Total[i] = 0; t.Total[i] += v; cnt[i]++;
                    }
                    if (p.Game != pg || mn - last > 3) { if (pg != 0 && last >= pa) t.Plays.Add(new Play { A = pa, B = last + 1, Label = m.Hist.Label(pg) }); pa = mn; pg = p.Game; }
                    last = mn;
                }
                if (pg != 0) t.Plays.Add(new Play { A = pa, B = last + 1, Label = m.Hist.Label(pg) });
                for (int i = 0; i < t.N; i++) if (cnt[i] > 0) t.Total[i] /= cnt[i];
                foreach (var a in m.Apps.Values)
                {
                    if (!a.H.Any || a.H.Last < t.From) continue;
                    string k, l; Names.Group(a, out k, out l);
                    Layer y; if (!all.TryGetValue(k, out y)) { all[k] = y = new Layer { Key = k, Label = l, V = new float[t.N], Role = a.IsGame ? 0 : k == "win" ? 2 : 3 }; s.ByKey.TryGetValue(k, out y.G); if (y.G != null) y.Label = y.G.Label; }
                    float c, w, q;
                    for (int i = 0; i < t.N; i++)
                    {
                        float sum = 0; int n = 0;
                        for (int j = 0; j < t.Step; j++) if (a.H.Get(t.From + i * t.Step - j, out c, out w, out q)) { sum += metric == Snap.Cpu ? q / s.Lps * 100 : (metric == Snap.Ram ? w : c) / 1024f; n++; }
                        if (n > 0) y.V[i] += sum / n;
                    }
                }
                foreach (var e in m.Log.Items)
                {
                    if (e.Kind != Kind.Hitch && e.Kind != Kind.GameWait) continue;
                    int mn = History.Epoch(e.Start.ToUniversalTime()) / 60; if (mn >= t.From) t.Hits.Add(mn);
                }
            }
            // 지금 값은 살아 있는 값으로
            foreach (var y in all.Values) if (y.G != null) y.V[t.N - 1] = (float)s.Val(y.G, metric); else y.V[t.N - 1] = 0;
            t.Total[t.N - 1] = (float)s.Used(metric);
            foreach (var y in all.Values) { double sum = 0; int n = 0; for (int i = 0; i < t.N; i++) if (t.Total[i] >= 0) { sum += y.V[i]; n++; } y.Avg = n > 0 ? sum / n : 0; }
            var named = new List<Layer>(); var game = new List<Layer>(); Layer win = null, bad = null;
            foreach (var y in all.Values) { if (y.Role == 0) game.Add(y); else if (y.Key == "win") win = y; else if (y.Key == culprit) bad = y; else if (y.Avg / t.Cap >= .012) named.Add(y); }
            named.Sort((x, y) => y.Avg.CompareTo(x.Avg)); int keep = bad != null ? 4 : 5; if (named.Count > keep) named.RemoveRange(keep, named.Count - keep);
            game.Sort((x, y) => y.Avg.CompareTo(x.Avg));
            foreach (var g in game) if (g.Avg > 0) t.L.Add(g);
            var rest = new Layer { Key = "*rest", Label = Tx("기타", "Other"), V = new float[t.N], Role = 1 }; t.L.Add(rest);
            if (win != null && win.Avg / t.Cap >= .01) t.L.Add(win);
            for (int i = named.Count - 1; i >= 0; i--) { named[i].Rank = i; t.L.Add(named[i]); }
            if (bad != null) { bad.Role = 4; t.L.Add(bad); }
            for (int i = 0; i < t.N; i++)
            {
                if (t.Total[i] < 0) { foreach (var y in t.L) y.V[i] = 0; continue; }
                double sum = 0; foreach (var y in t.L) sum += y.V[i];
                if (sum > t.Total[i] && sum > 0) { float k = (float)(t.Total[i] / sum); foreach (var y in t.L) y.V[i] *= k; }
                else rest.V[i] = (float)(t.Total[i] - sum);
            }
            return t;
        }
    }

    internal sealed partial class MainView
    {
        // ---- 시간별 ----

        public int Metric = Snap.Vm, Hours = 3;
        float scrub = -1; string solo, soloLast; TimeSet ts;
        P2[] hi = new P2[0], lo = new P2[0], ru = new P2[0], rv = new P2[0]; // 그릴 때마다 새로 만들지 않게

        float[] baseV = new float[0];
        static readonly string[] MetricNames = { Txt.Title(Snap.Cpu), Txt.Title(Snap.Vm), Txt.Title(Snap.Ram) }, HourNames = { Tx("3시간", "3h"), Tx("12시간", "12h"), Tx("하루", "24h") };
        public override void MouseLeave() { base.MouseLeave(); scrub = -1; solo = null; }

        void PaintTime(float w, float h)
        {
            var bad = Bad; string cul = bad != null && (bad.Metric == Metric || (bad.Metric == Snap.Vm && Metric == Snap.Ram)) ? bad.Key : null;
            if (ts == null || ts.M != Metric || ts.Hours != Hours || ts.Minute != S.Epoch / 60) ts = TimeSet.Build(M, S, Metric, Hours, cul);
            float xn = (float)Math.Round(w * .752f), yt = 190, yb = h - 98;
            int n = ts.N; if (hi.Length < n + 1) { hi = new P2[n + 1]; lo = new P2[n + 1]; }
            float f = scrub < 0 ? n - 1 : Math.Max(0, Math.Min(n - 1, scrub / xn * (n - 1)));
            int fi = (int)Math.Round(f);
            float so = A("solo", solo != null ? 1 : 0); if (solo != null) soloLast = solo;
            Func<double, float> Y = v => yb - (float)(v / ts.Cap) * (yb - yt);

            // 층
            if (baseV.Length < n) baseV = new float[n]; else Array.Clear(baseV, 0, n);
            if (so < .99f)
            {
                if (so > .01f) C.PushOpacity(1 - so);
                foreach (var y in ts.L)
                {
                    for (int i = 0; i < n; i++) { float x = i / (float)(n - 1) * xn; lo[i] = new P2(x, Y(baseV[i])); baseV[i] += y.V[i]; hi[i] = new P2(x, Y(baseV[i])); }
                    var col = LayerCol(y);
                    Runs(n, (a, len) => { if (len < 2) return; if (ru.Length < len) { ru = new P2[n + 1]; rv = new P2[n + 1]; } var u = ru; var v = rv; Array.Copy(hi, a, u, 0, len); Array.Copy(lo, a, v, 0, len); C.Area(u, len, v, 0, col); C.Path(u, len, T.Dark ? Col.Hex(0x040c18, .5f) : Col.Hex(0xffffff, .5f), 1.5f, true); });
                }
                if (so > .01f) C.PopClip(IntPtr.Zero);
            }
            else foreach (var y in ts.L) for (int i = 0; i < n; i++) baseV[i] += y.V[i];
            string unit = Metric == Snap.Cpu ? "%" : "GB";
            if (so < .99f)
            {
                double[] tick = Metric == Snap.Cpu ? new double[] { 25, 50 } : Metric == Snap.Ram ? Ticks(ts.Cap) : Ticks(ts.Cap);
                foreach (double t in tick) { float y = Y(t); C.Fill(0, y - .5f, xn, 1, T.Fg.Fade(.16f * (1 - so))); Halo(t.ToString("0") + unit, 40, y - 8, 1 - so); }
                C.Fill(0, yt - 1, xn, 1.5f, T.Fg.Fade(.4f * (1 - so))); Halo(Metric == Snap.Cpu ? Tx("최대 100%", "Max 100%") : Tx("한도 ", "Limit ") + ts.Cap.ToString(Metric == Snap.Ram ? "0" : "0.0") + "GB", 40, yt - 8, 1 - so);
            }
            // 하나만 보기: 바닥부터, 세로 눈금을 그 프로그램에 맞춰
            var sl = soloLast == null ? null : ts.L.Find(x => x.Key == soloLast);
            if (so > .01f && sl != null)
            {
                float mx = 0; foreach (float v in sl.V) if (v > mx) mx = v; mx = Math.Max(mx, Metric == Snap.Cpu ? 2 : .2f) * 1.25f;
                for (int i = 0; i < n; i++) hi[i] = new P2(i / (float)(n - 1) * xn, yb - sl.V[i] / mx * (yb - yt)); var col = LayerCol(sl).Fade(so);
                Runs(n, (a, len) => { if (len < 2) return; if (ru.Length < len) { ru = new P2[n + 1]; rv = new P2[n + 1]; } var u = ru; Array.Copy(hi, a, u, 0, len); C.Area(u, len, null, yb, col); });
                double step = 50; foreach (double q in new[] { .1, .2, .5, 1, 2, 5, 10, 20, 50 }) if (mx / q <= 4) { step = q; break; }
                for (double q = step; q < mx; q += step) { float y = yb - (float)(q / mx) * (yb - yt); C.Fill(0, y - .5f, xn, 1, T.Fg.Fade(.16f * so)); Halo(Metric == Snap.Cpu ? q.ToString("0.#") + "%" : q < .95 ? (q * 1000).ToString("0") + "MB" : q.ToString("0.#") + "GB", 40, y - 8, so); }
            }

            // 바닥
            C.Fill(0, yb, xn, 1.5f, T.Fg.Fade(.3f));
            Func<double, float> X = mn => (float)((mn - ts.From) / (double)((n - 1) * ts.Step)) * xn;
            foreach (int mn in ts.Hits) { float x = X(mn); if (x >= 0 && x <= xn) C.Circle(x, yb + 9, 4.5f, T.Red); }
            foreach (var p in ts.Plays)
            {
                float a = Math.Max(0, X(p.A)), b = Math.Min(xn, X(p.B)), bw = Math.Max(6, b - a - 3); if (b <= 0) continue;
                C.Round(a, yb + 17, bw, 30, 8, T.Sand);
                int min = p.B - p.A; string nm = Names.ShortName(p.Label, p.Label), tx = nm + " " + Fmt.Dur(min * 60);
                var st = F.Sans(16, 800); if (C.Measure(tx, st) + 12 > bw) tx = Fmt.Dur(min * 60);
                if (C.Measure(tx, st) + 12 < bw) C.Mid(tx, st, a + bw / 2, yb + 32, Col.Hex(0x241803), 1);
            }
            int every = Hours <= 3 ? 30 : Hours <= 12 ? 120 : 180;
            for (int mn = (ts.From / every + 1) * every; mn <= ts.Minute; mn++)
            {
                var d = History.Local(mn * 60.0); if ((d.Hour * 60 + d.Minute) % every != 0) continue;
                float x = X(mn); if (x > 46 && x < xn - 80) C.Mid(d.ToString("HH:mm"), F.Sans(16, 600), x, yb + 71, T.Fg.Fade(.62f), 1);
                mn += every - 1;
            }
            C.Circle(xn + 34, yb + 32, 5, T.Red); C.Mid(Tx("끊김", "Stutter"), F.Sans(16, 700), xn + 48, yb + 32, T.Fg.Fade(.8f));

            // 시각 손잡이
            float lx = scrub < 0 ? xn : f / (n - 1) * xn;
            C.Fill(lx - 1, yt, 2, yb - yt + 50, T.Fg);
            string when = scrub < 0 ? Tx("지금", "Now") : History.Local((ts.From + fi * ts.Step) * 60.0).ToString("HH:mm"); string pill = "◀ " + when;
            float pw = C.Measure(pill, F.Sans(17, 800)) + 28, px = Math.Max(pw / 2 + 4, Math.Min(xn, lx)); C.Round(px - pw / 2, yb + 54, pw, 34, 17, T.Btn); C.Mid(pill, F.Sans(17, 800), px, yb + 71, T.Bfg, 1);
            Zone(0, yt - 20, xn, h - yt + 20, "graph", null, (x, y) => { scrub = x < xn - 3 ? Math.Max(0, x) : -1; solo = null; Redraw(); });

            // 이름은 자기 층 옆에
            var pos = new List<KeyValuePair<TimeSet.Layer, float>>(); float bs = 0;
            foreach (var y in ts.L) { float a = bs; bs += y.V[n - 1]; pos.Add(new KeyValuePair<TimeSet.Layer, float>(y, (Y(a) + Y(bs)) / 2)); }
            pos.Reverse(); int c = pos.Count; float gap = Math.Min(35, (yb - yt - 10) / Math.Max(1, c)); var py = new float[c]; float yy = yt - gap + 12;
            for (int i = 0; i < c; i++) { py[i] = Math.Max(pos[i].Value, yy + gap); yy = py[i]; }
            yy = yb - 17 + gap; for (int i = c - 1; i >= 0; i--) { py[i] = Math.Min(py[i], yy - gap); yy = py[i]; }
            float surf = Y(baseV[n - 1]); string sh = null;
            for (int i = 0; i < c; i++)
            {
                var y = pos[i].Key; float cy = A("lb" + y.Key, py[i]); string id = "l" + y.Key; string key = y.Key;
                if (Over(id)) sh = key;
                var ink = T.Fg; float al = solo != null && solo != key ? .4f : 1; var lc = LayerCol(y);
                if (so < .5f && Math.Abs(cy - pos[i].Value) > 6) C.Line(xn + 2, pos[i].Value, xn + 14, cy, lc.Fade(.8f), 1.2f);
                C.Round(xn + 14, cy - 10, 5, 20, 2.5f, lc.Fade(al)); Icon(y.G, y.Key, xn + 28, cy - 12, 24, ink.Fade(al)); C.Mid(y.Label, F.Sans(17, 700), xn + 60, cy, ink.Fade(al), 0, w - xn - 160); C.Mid(Txt.Val(Metric, y.V[fi]), F.Din(22), w - 24, cy, ink.Fade(al), 2);
                Zone(xn + 18, cy - gap / 2, w - xn - 20, gap, id, () => { if (key == "*rest" || key == "win") { sort = Metric; openWin = key == "win"; Go(Prog); } else { Open = key; Redraw(); } });
            }
            if (sh != solo) { solo = sh; Animating = true; }

            // 머리글
            var so2 = solo == null ? null : ts.L.Find(x => x.Key == solo); float tot = Math.Max(0, ts.Total[fi]);
            if (so2 != null) Say(w, so2.Label + " " + Txt.Val(Metric, so2.V[fi]), when + ", " + Txt.Title(Metric), 420);
            else
            {
                double pc = Math.Round(tot / Math.Max(1, ts.Cap) * 100);
                float hw = Say(w, Txt.Title(Metric) + " " + pc.ToString("0") + "%", when + (Metric == Snap.Cpu ? "" : ", " + tot.ToString("0.0") + " / " + ts.Cap.ToString(Metric == Snap.Ram ? "0" : "0.0") + "GB"), 420, T.LvInk(pc, T.Fg));
                C.Text((Metric == Snap.Cpu ? (100 - pc).ToString("0") + "%" : Math.Max(0, ts.Cap - tot).ToString("0.0") + "GB") + Tx(" 남음", " free"), F.Sans(19, 600), 40 + hw + 12, 100, T.Fg.Fade(.78f));
            }
            int[] order = { Snap.Cpu, Snap.Vm, Snap.Ram }; int ms = Array.IndexOf(order, Metric);
            Segs("ms", MetricNames, ms, w - 40, 78, 36, F.Sans(17, 700), true, k => { Metric = order[k]; solo = null; soloLast = null; Redraw(); });
            int[] hrs = { 3, 12, 24 }; Segs("hr", HourNames, Array.IndexOf(hrs, Hours), w - 40, 122, 30, F.Sans(14.5f, 700), true, k => { Hours = hrs[k]; Redraw(); });
        }
        static double[] Ticks(double cap) { double s = cap > 60 ? 25 : cap > 24 ? 10 : cap > 12 ? 5 : 2; var l = new List<double>(); for (double v = s; v < cap - s * .4; v += s) l.Add(v); return l.ToArray(); }
        void Halo(string s, float x, float y, float a)
        {
            var sh = T.Dark ? Col.Hex(0x030a16, .55f * a) : Col.Hex(0xffffff, .6f * a); var st = F.Sans(17, 700);
            C.Text(s, st, x - 1, y, sh); C.Text(s, st, x + 1, y, sh); C.Text(s, st, x, y - 1, sh); C.Text(s, st, x, y + 1, sh); C.Text(s, st, x, y, T.Fg.Fade(a));
        }
        void Runs(int n, Action<int, int> f) { int a = 0; while (a < n) { while (a < n && ts.Total[a] < 0) a++; int b = a; while (b < n && ts.Total[b] >= 0) b++; if (b > a) f(a, b - a); a = b + 1; } }
        Col LayerCol(TimeSet.Layer y) { return y.Role == 0 ? T.Sand : y.Role == 1 ? T.Rest : y.Role == 2 ? T.Win : y.Role == 4 ? T.Red : T.W[Math.Min(4, y.Rank)]; }

        // ---- 프로그램 ----

        public int sort = Snap.Vm; // 3 = 3시간 변화
        public bool openWin; public string OpenGroup; public string Find = ""; float scroll, maxScroll;
        sealed class Row { public Grp G; public App A; public string Key, Label; public double[] V = new double[4]; public bool Sub; }

        public override void Wheel(int delta) { if (Page == Prog) { scroll = Math.Max(0, Math.Min(maxScroll, scroll - delta * .8f)); Redraw(); } }

        double Delta(IEnumerable<App> apps)
        {
            int now = S.Epoch / 60 - 1; double a = 0, b = 0;
            lock (M.Sync) foreach (var p in apps) { float f = p.H.Baseline(now - 180); if (f > 0) { a += f; b += p.Commit / 1048576.0; } }
            return a > 0 ? (b - a) / 1024 : 0;
        }

        void PaintProg(float w, float h)
        {
            C.Round(30, 66, w - 60 - 140, 52, 14, T.Faint);
            var rows = new List<Row>(); string q = Find.Trim().ToLowerInvariant();
            foreach (var g in S.Groups)
            {
                if (q.Length > 0) { bool hit = g.Label.ToLowerInvariant().Contains(q) || g.Key.Contains(q); if (!hit) foreach (var a in g.Apps) if (a.Label.ToLowerInvariant().Contains(q) || (a.Exe ?? "").ToLowerInvariant().Contains(q)) { hit = true; break; } if (!hit) continue; }
                var r = new Row { G = g, Key = g.Key, Label = g.Label }; for (int m = 0; m < 3; m++) r.V[m] = S.Val(g, m);
                if (sort == 3 || g.Commit > 100L << 20) r.V[3] = Delta(g.Apps);
                if (q.Length == 0 && r.V[0] < .05 && r.V[1] < .004 && r.V[2] < .008) continue;
                rows.Add(r);
            }
            rows.Sort((x, y) => y.V[sort].CompareTo(x.V[sort]));
            double top = 1e-9; foreach (var r in rows) top = Math.Max(top, r.V[sort]);
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i]; bool ex = (r.Key == "win" && openWin) || (OpenGroup == r.Key);
                if (!ex || r.Sub || r.G.Apps.Count < 2) continue;
                var kids = new List<App>(r.G.Apps); kids.Sort((x, y) => (sort == Snap.Cpu ? y.Cores.CompareTo(x.Cores) : sort == Snap.Ram ? y.Ws.CompareTo(x.Ws) : y.Commit.CompareTo(x.Commit))); int at = i + 1, n = 0;
                foreach (var a in kids)
                {
                    if (n++ >= 14) break; lock (M.Sync) if (!a.InfoDone && a.Commit > 20L << 20) M.EnsureInfo(a);
                    var k = new Row { A = a, G = r.G, Key = r.Key, Sub = true, Label = SubName(a) }; var tmp = new Grp(); tmp.Add(a); for (int m = 0; m < 3; m++) k.V[m] = S.Val(tmp, m); k.V[3] = Delta(new[] { a }); rows.Insert(at++, k);
                }
                i = at - 1;
            }
            const float rh = 54; float y0 = 206, view = h - y0; maxScroll = Math.Max(0, rows.Count * rh - view + 12); scroll = Math.Min(scroll, maxScroll);
            float[] cx = { w - 530, w - 400, w - 250, w - 40 }; string[] cn = { "CPU", Txt.Title(Snap.Ram), Txt.Title(Snap.Vm), Tx("3시간 변화", "3h change") };
            for (int i = 0; i < 4; i++)
            {
                string id = "col" + i; int k = i; string s = cn[i] + (sort == i ? " ↓" : " ↕"); var st = F.Sans(16, 700); float tw = C.Measure(s, st);
                C.Mid(s, st, cx[i], 182, T.Fg.Fade(sort == i || Over(id) ? 1 : .6f), 2); Zone(cx[i] - tw - 8, 166, tw + 16, 34, id, () => { sort = k; scroll = 0; Redraw(); });
            }
            C.Clip(0, y0, w, view);
            for (int i = 0; i < rows.Count; i++)
            {
                float y = y0 + i * rh - scroll; if (y > h || y + rh < y0) continue;
                var r = rows[i]; string id = "r" + i; int sl = sort < 3 ? Theme.Share(Share(r, sort)) : 0; var fill = sl == 2 ? T.Hot : sl == 1 ? T.Warn : r.G.IsGame ? T.Sand : T.W[3];
                if (!r.Sub) { float fw = 40 + (w - 640) * (float)Math.Max(0, r.V[sort] / top); C.Round(-20, y + 2, A("rw" + r.Key, fw) + 20, rh - 4, 10, fill.Fade(Over(id) ? .85f : r.G.IsGame ? .75f : .55f)); }
                else if (Over(id)) C.Fill(0, y + 2, w, rh - 4, T.Fg.Fade(.06f));
                float ix = r.Sub ? 82 : 40, isz = r.Sub ? 26 : 34;
                if (r.Sub) IconApp(r.A, ix, y + rh / 2 - isz / 2, isz); else Icon(r.G, r.Key, ix, y + rh / 2 - isz / 2, isz, T.Fg);
                float tx = ix + isz + 15; bool multi = !r.Sub && r.G.Apps.Count > 1;
                float tw = C.Mid(r.Label, F.Sans(r.Sub ? 17 : 20, r.Sub ? 600 : 700), tx, y + rh / 2, T.Fg, 0, cx[0] - tx - 90);
                if (multi) { bool ex = (r.Key == "win" && openWin) || OpenGroup == r.Key; string mid = "m" + i; string key = r.Key; C.Mid((ex ? "▲ " : "▼ ") + r.G.Apps.Count, F.Sans(13, 700), tx + tw + 10, y + rh / 2, T.Fg.Fade(Over(mid) ? 1 : .6f)); }
                for (int m = 0; m < 4; m++)
                {
                    string s = m == 3 ? (Math.Abs(r.V[3]) < .03 ? "" : (r.V[3] > 0 ? "+" : "−") + Txt.Gb(Math.Abs(r.V[3]))) : m == 0 ? (r.V[0] < .05 ? "0%" : r.V[0] < 9.95 && r.V[0] >= .05 && r.V[0] < 1 ? "1%" : r.V[0].ToString("0") + "%") : r.V[m] < .0005 ? "" : Txt.Gb(r.V[m]);
                    if (m == 0 && r.V[0] < .5) s = "0%";
                    int lv = m < 3 ? Theme.Share(Share(r, m)) : 0; C.Mid(s, F.Din(24), cx[m], y + rh / 2, lv == 2 ? T.HotInk : lv == 1 ? T.WarnInk : T.Fg.Fade(sort == m ? 1 : .72f), 2);
                }
                string k2 = r.Key; bool sub = r.Sub;
                Zone(0, y, w, rh, id, () => { if (k2 == "win" && !sub) openWin = !openWin; else Open = k2; Redraw(); });
                if (multi) { float zx = tx + tw + 2; Zone(zx, y, 56, rh, "m" + i, () => { if (k2 == "win") openWin = !openWin; else OpenGroup = OpenGroup == k2 ? null : k2; Redraw(); }); }
            }
            C.Unclip();
        }
        /// <summary>전체 중 몇 %</summary>
        double Share(Row r, int m) { return m == Snap.Cpu ? r.V[0] : m == Snap.Ram ? r.V[1] / Math.Max(.1, S.RamGb) * 100 : r.V[2] / Math.Max(.1, S.VmGb) * 100; }
        static string SubName(App a) { string s = a.Label; int i = s.IndexOf(" · "); return i > 0 ? s.Substring(i + 3) : Names.ShortName(a.Exe ?? "", s); }
        void IconApp(App a, float x, float y, float s)
        {
            byte[] px = a == null || a.Pseudo || a.Service ? null : Icons.Get(a.Path);
            if (px != null) C.Image(a.Path, px, Icons.Px, Icons.Px, x, y, s, s); else Icon(null, a != null && (a.Pseudo || a.Service || Names.IsSystem(a.Exe)) ? "win" : "", x, y, s, T.Fg);
        }

        // ---- 청소 ----

        RamMap ram; int ramAt = -9999; bool scanning;

        public void ScanTemp()
        {
            if (scanning || D.Temp.Busy) return;
            scanning = true;
            System.Threading.ThreadPool.QueueUserWorkItem(o => { try { D.Temp.Scan(); } catch (Exception) { } scanning = false; Redraw(); });
        }

        void PaintClean(float w, float h)
        {
            if (Environment.TickCount - ramAt > 900) { ramAt = Environment.TickCount; ram = Cleaner.Map((long)(S.RamGb * 1073741824), (long)(S.RamLeft * 1073741824)); }
            if (!D.Temp.Scanned && !scanning) ScanTemp();
            Say(w, Tx("청소", "Clean"), D.LastTrim != null && (DateTime.Now - D.LastTrimAt).TotalMinutes < 30 ? D.LastTrimAt.ToString("HH:mm") + " " + D.TrimText(D.LastTrim) + Tx(", 프로그램 {0}개", ", {0} apps", D.LastTrim.Programs) : "", 0);
            float bot = 66, mid = w / 2, top = 264, H = h - bot - top;
            C.Fill(mid, 160, 1, h - bot - 176, Sep);
            // 왼쪽: 램
            C.Mid(Txt.Title(Snap.Ram), F.Sans(20, 800), 40, 173, T.Fg); C.Mid(Txt.Of(S, Snap.Ram), F.Sans(16, 600), 40 + C.Measure(Txt.Title(Snap.Ram), F.Sans(20, 800)) + 12, 174, T.Fg.Fade(.72f));
            bool all = ram.Known && S.Game == null; var st = F.Sans(mid < 510 ? 16 : 18, 800); float pad = mid < 510 ? 32 : 46, w1 = C.Measure(Tx("램 청소", "Clean RAM"), st), w2 = C.Measure(Tx("캐시 전체 청소", "Clear cache"), st);
            float need = 40 + C.Measure(Txt.Pct(S, Snap.Ram), F.Din(74)) + 10 + C.Measure(Txt.Left(S, Snap.Ram) + Tx(" 남음", " free"), F.Sans(16, 600)) + 10, room = mid - 28 - w1 - (all ? w2 + 10 : 0);
            while (pad > 20 && room - pad * (all ? 2 : 1) < need) pad -= 2;
            float b1 = w1 + pad, b2 = w2 + pad, px = mid - 28 - b1;
            Usage(Snap.Ram, 40, 243, room - pad * (all ? 2 : 1) >= need, (all ? px - b2 - 10 : px) - 8); Pill("trim", Tx("램 청소", "Clean RAM"), px, 194, b1, 50, 25, T.Btn, T.Bfg, st, () => Back("trim"));
            if (all) Pill("trimall", Tx("캐시 전체 청소", "Clear cache"), px - b2 - 10, 194, b2, 50, 25, T.Fg.Fade(.1f), T.Fg, st, () => Ask(Tx("캐시 전체 청소", "Clear cache"), Tx("청소 직후 잠시 느려질 수 있습니다.", "Things may be slow for a moment right after."), () => Back("trimall")));
            double game = S.Game != null ? S.Val(S.Game, Snap.Ram) : 0, use = ram.Known ? ram.InUse / 1073741824.0 - game : S.RamGb - S.RamLeft - game;
            var seg = new List<Seg>();
            if (game > 0) seg.Add(new Seg(S.Game.Label, game, T.Sand, S.Game.Key, S.Game));
            seg.Add(new Seg(Tx("사용 중", "In use"), Math.Max(0, use), T.LvFill(Pct(Snap.Ram), T.W[1]), "*rest", null));
            if (ram.Known) { seg.Add(new Seg(Tx("저장 대기", "Modified"), ram.Modified / 1073741824.0, T.W[3], "win", null)); seg.Add(new Seg(Tx("캐시", "Cache"), ram.Cache / 1073741824.0, T.Fg.Fade(T.Dark ? .3f : .26f), "*cache", null)); }
            // 막대 하나에 차례로, 아래에 같은 차례로 이름과 양
            float lx = 40, lr = mid - 28, bw = lr - lx, y = top + 10, bh = 40, x = lx;
            RamBar = new R4(lx + C.Ox, y + C.Oy, bw, bh); var mask = C.ClipRound(lx, y, bw, bh, 9); C.Fill(lx, y, bw, bh, T.Fg.Fade(T.Dark ? .07f : .09f));
            foreach (var sg in seg) { float sw = Math.Max(0, A("cr" + sg.Label, (float)(sg.V / Math.Max(.1, S.RamGb) * bw))); if (sw < .5f) continue; C.Fill(x, y, Math.Max(.5f, sw - 2), bh, sg.C); x += sw; }
            C.PopClip(mask);
            y += bh + 16; float rh = Math.Min(54, (h - bot - y - 8) / Math.Max(1, seg.Count));
            foreach (var sg in seg)
            {
                float cy = y + rh / 2; C.Round(lx, cy - 7, 14, 14, 4, sg.C);
                if (sg.G != null) Icon(sg.G, sg.Key, lx + 24, cy - 11, 22, T.Fg);
                C.Mid(sg.Label, F.Sans(17, 700), lx + (sg.G != null ? 56 : 26), cy, T.Fg, 0, lr - lx - 150); Value(Snap.Ram, sg.V, lr, cy, 22, T.Fg);
                y += rh; if (y < h - bot - 4) C.Fill(lx, y, lr - lx, 1, Sep);
            }

            // 오른쪽: 임시 파일
            float rx = mid + 28, rr = w - 40; C.Mid(Tx("임시 파일", "Temp files"), F.Sans(20, 800), rx, 173, T.Fg);
            double on = D.Temp.Total(true) / 1073741824.0;
            string big = D.Temp.Scanned ? Txt.Gb(on) : "–"; float tw0 = C.Text(big, F.Din(74), rx, 243, T.Fg); C.Text(D.Temp.Scanned ? Tx("삭제 가능", "can be deleted") : Tx("확인 중", "Checking"), F.Sans(19, 600), rx + tw0 + 10, 243, T.Fg.Fade(.78f));
            if (D.Temp.Scanned) { float b3 = C.Measure(D.Temp.Busy ? Tx("삭제 중", "Deleting") : Tx("삭제", "Delete"), st) + 48; Pill("tdel", D.Temp.Busy ? Tx("삭제 중", "Deleting") : Tx("삭제", "Delete"), rr - b3, 194, b3, 50, 25, on > .0005 ? T.Btn : T.Fg.Fade(.1f), on > .0005 ? T.Bfg : T.Fg, st, () => { if (D.Temp.Busy || D.Temp.Total(true) == 0) return; Ask(Tx("임시 파일 삭제", "Delete temp files"), Tx("{0}를 삭제합니다. 복구할 수 없습니다.", "Deletes {0}. This can't be undone.", Txt.Gb(D.Temp.Total(true) / 1073741824.0)), () => { var from = PressR; BusyOn(PressId); System.Threading.ThreadPool.QueueUserWorkItem(o => { var r = D.Temp.Delete(); D.LastTemp = r; D.LastTempAt = DateTime.Now; LastOk = true; Finish(new Outcome { Op = "tempdel", From = from, Gain = r.Bytes / 1073741824.0 }); toastNext = Tx("{0} 삭제, {1}개", "Deleted {0}, {1} {2}", Txt.Gb(r.Bytes / 1073741824.0), r.Files, Pl(r.Files, "file", "files")) + (r.Failed > 0 ? Tx(" (못 지움 {0}개)", " ({0} skipped)", r.Failed) : ""); Redraw(); }); }); }); }
            var cats = new List<Temp.Cat>(D.Temp.Cats); cats.Sort((p, q) => q.Deletable.CompareTo(p.Deletable)); float ch = Math.Min(64, H / Math.Max(1, cats.Count)); y = top; TempBox = new R4(rx + C.Ox, top + C.Oy, rr - rx, ch * cats.Count);
            for (int i = 0; i < cats.Count; i++)
            {
                var c = cats[i]; string id = "tc" + c.Key; bool live = c.On && c.Deletable > 0; var ink = T.Fg.Fade(c.On ? 1 : .55f);
                if (Over(id)) C.Fill(rx - 12, y + 1, rr - rx + 24, ch - 1, T.Fg.Fade(.05f));
                if (i > 0) C.Fill(rx, y, rr - rx, 1, Sep);
                float cy = y + ch / 2, bx = rx; C.RoundLine(bx, cy - 11, 22, 22, 11, T.Fg.Fade(c.On ? .85f : .4f), 2); if (c.On) C.Circle(bx + 11, cy, 6, T.Fg);
                bool two = ch >= 52; C.Text(c.Label, F.Sans(18, 700), bx + 36, two ? cy - 1 : cy + 6, ink, 0, rr - bx - 160);
                if (two) C.Text(c.Blocked ? Tx("다른 폴더로 연결됨, 제외", "Linked to another folder, skipped") : D.Temp.Scanned ? Tx("{0} 중 {1}일 지난 파일", "Older than {1} days, of {0}", Txt.Gb(c.Bytes / 1073741824.0), c.Days) : Tx("확인 중", "Checking"), F.Sans(15.5f, 500), bx + 36, cy + 20, T.Fg.Fade(c.On ? .66f : .45f), 0, rr - bx - 140);
                if (D.Temp.Scanned) Value(Snap.Ram, c.Deletable / 1073741824.0, rr, cy, 24, live ? T.Fg : T.Fg.Fade(.5f));
                var cc = c; Zone(rx - 12, y, rr - rx + 24, ch, id, () => { cc.On = !cc.On; M.Set.TempOff.Remove(cc.Key); M.Set.TempOn.Remove(cc.Key); (cc.On ? M.Set.TempOn : M.Set.TempOff).Add(cc.Key); M.Set.Save(); Redraw(); }); y += ch;
            }

            // 알아서 하는 것
            C.Fill(40, h - bot, w - 80, 1, Sep); float tx = 40, by = h - bot / 2;
            tx = Toggle("at", Tx("램 {0}% 넘으면 자동 청소", "Clean when RAM is over {0}%", M.Set.AutoTrimPct), M.Set.AutoTrim, tx, by, T.Fg, () => { M.Set.AutoTrim = !M.Set.AutoTrim; M.Set.Save(); }) + 36;
            tx = Toggle("ag", Tx("게임 시작할 때 청소", "Clean at game start"), M.Set.AutoTrimGame, tx, by, T.Fg, () => { M.Set.AutoTrimGame = !M.Set.AutoTrimGame; M.Set.Save(); });
            C.Mid(Tx("제외: 게임, 서버, 오디오, 사용 중인 프로그램", "Skipped: games, servers, audio, apps in use"), F.Sans(16, 600), w - 40, by, T.Fg.Fade(.6f), 2, w - tx - 70);
        }
        struct Seg { public string Label, Key; public double V; public Col C; public Grp G; public Seg(string l, double v, Col c, string k, Grp g) { Label = l; V = v; C = c; Key = k; G = g; } }
        float Toggle(string id, string text, bool on, float x, float cy, Col ink, Action flip)
        {
            float k = A("tg" + id, on ? 1 : 0); C.Round(x, cy - 13, 46, 26, 13, ink.Fade(.22f).To(T.W[2], k)); C.Circle(x + 13 + 20 * k, cy, 9.5f, Col.Hex(0xffffff));
            float tw = C.Mid(text, F.Sans(17, 700), x + 56, cy, ink); Zone(x - 4, cy - 20, tw + 66, 40, id, () => { flip(); Redraw(); });
            return x + 56 + tw;
        }

        // ---- 프로그램 하나 ----

        Rule draft;
        readonly float[] sv = new float[181];

        void PaintSheet(float w, float h)
        {
            float k = A("sheet", Open != null ? 1 : 0); if (k < .004f) return;
            string key = Open ?? lastOpen; if (key == null) return; lastOpen = key;
            Grp g; if (!S.ByKey.TryGetValue(key, out g)) { if (Open != null) { Open = null; Redraw(); } return; }
            float sw = 430, x0 = w - sw * k;
            if (Open != null) Zone(0, 0, w, h, "dim", () => { Open = null; Redraw(); });
            C.Fill(0, 0, w, h, Col.Hex(0, .28f * k)); C.Fill(x0, 0, sw, h, T.Sheet);
            Zone(x0, 0, sw, h, "sheet", null);
            float x = x0 + 32, r = x0 + sw - 32, y = 46;
            C.Mid(Tx("닫기", "Close"), F.Sans(17, 700), r, y + 10, T.Fg.Fade(Over("sx") ? 1 : .7f), 2); Zone(r - 50, y - 8, 58, 38, "sx", () => { Open = null; Redraw(); });
            y += 40; Icon(g, g.Key, x, y, 56, T.Fg); C.Mid(g.Label, F.Sans(34, 800), x + 72, y + 28, T.Fg, 0, sw - 150); y += 84;
            string[] tl = { "CPU", Txt.Title(Snap.Ram), Txt.Title(Snap.Vm) }; float cw = (r - x) / 3;
            for (int m = 0; m < 3; m++) { float cx = x + cw * m; C.Mid(tl[m], F.Sans(16, 600), cx, y + 6, T.Fg.Fade(.7f)); double v = S.Val(g, m); string n = m == 0 ? v.ToString(v < 9.95 ? "0.0" : "0") : Txt.Num(v); float nw = C.Text(n, F.Din(40), cx, y + 54, T.Fg); C.Text(m == 0 ? "%" : Txt.Unit(v), F.Din(20), cx + nw + 2, y + 54, T.Fg); }
            y += 76;
            // 3시간 흐름
            int minute = S.Epoch / 60 - 1; lock (M.Sync) Series.Commit(g, minute - 180, minute, sv); Series.Bridge(sv, 181, 3); float mx = 1, first = -1; foreach (float v in sv) { if (v > mx) mx = v; if (first < 0 && v > 0) first = v; }
            C.Mid(Tx("가상메모리 3시간", "Commit, 3h"), F.Sans(17, 700), x, y + 10, T.Fg); double cur = g.Commit / 1048576.0; C.Mid(first < 0 || Math.Abs(cur - first) < 30 ? Tx("변화 없음", "No change") : Fmt.Mem(first) + " → " + Fmt.Mem(cur), F.Din(22), r, y + 10, T.Fg, 2); y += 28;
            float gh = Math.Max(70, Math.Min(150, h - 620)); int a0 = 0; while (a0 < 181 && sv[a0] < 0) a0++;
            if (181 - a0 > 3) { int n = 181 - a0; if (hi.Length < n) hi = new P2[n]; for (int i = 0; i < n; i++) hi[i] = new P2(x0 + (a0 + i) / 180f * sw, y + gh - Math.Max(0, sv[a0 + i]) / (mx * 1.12f) * gh); var gc = Bad != null && Bad.Key == g.Key && Bad.Metric == Snap.Vm ? T.Red : g.IsGame ? T.Sand : T.W[2]; C.Area(hi, n, null, y + gh, gc); }
            C.Fill(x0, y + gh, sw, 1, T.Fg.Fade(.14f)); y += gh + 18;
            C.Mid(History.Local((minute - 180) * 60.0).ToString("HH:mm"), F.Sans(15.5f, 600), x, y, T.Fg.Fade(.7f)); C.Mid(Tx("지금", "Now"), F.Sans(15.5f, 600), r, y, T.Fg.Fade(.7f), 2); y += 26;

            float by = h - 30;
            bool tune = Fixer.CanTune(g), re = Fixer.CanRestart(g), end = Fixer.CanEnd(g);
            if (end) { by -= 52; Pill("send", Tx("종료", "Close"), x, by, r - x, 52, 26, T.Faint, T.Fg, F.Sans(18, 800), () => Run("end:" + key)); by -= 10; }
            if (re)
            {
                bool game = S.Game != null, bk = D.Booked(key); float bw = game ? (r - x - 10) / 2 : r - x; by -= 52;
                Pill("sre", Tx("재시작", "Restart"), x, by, bw, 52, 26, T.Btn, T.Bfg, F.Sans(18, 800), () => Run("restart:" + key));
                if (game) Pill("saf", bk ? Tx("예약 취소", "Cancel") : Tx("게임 후 재시작", "After game"), x + bw + 10, by, bw, 52, 26, T.Faint, T.Fg, F.Sans(18, 800), () => Run((bk ? "unbook:" : "after:") + key));
                by -= 12;
            }
            if (!end && !re) { C.Mid(WhyNot(g), F.Sans(16, 500), x0 + sw / 2, by - 14, T.Fg.Fade(.7f), 1, sw - 50); by -= 40; }
            if (tune && by - y > 130)
            {
                var rule = D.Fix.Find(key); draft = rule ?? new Rule { Key = key, Label = g.Label };
                float gap = Math.Min(50, (by - y) / 3f);
                Choice(Tx("코어", "Cores"), new[] { Tx("전부", "All"), Tx("4개", "4"), Tx("2개", "2"), Tx("1개", "1") }, draft.Cores == 0 ? 0 : draft.Cores >= 4 ? 1 : draft.Cores >= 2 ? 2 : 3, x, r, y, "sc", i => SetRule(g, new[] { 0, 4, 2, 1 }[i], draft.Prio, draft.Always));
                Choice(Tx("우선순위", "Priority"), new[] { Tx("기본", "Normal"), Tx("낮음", "Low"), Tx("매우 낮음", "Idle") }, draft.Prio, x, r, y + gap, "sp", i => SetRule(g, draft.Cores, i, draft.Always));
                if (draft.Any) Choice(Tx("적용", "When"), new[] { Tx("게임할 때", "In game"), Tx("항상", "Always") }, draft.Always ? 1 : 0, x, r, y + gap * 2, "sw", i => SetRule(g, draft.Cores, draft.Prio, i == 1));
            }
        }
        string lastOpen;
        public static string WhyNot(Grp g)
        {
            if (g.IsGame) return Tx("게임은 종료·제한 불가", "Games can't be closed or limited");
            foreach (var a in g.Apps) { if (a.Protected || Names.IsGuarded(a.Exe)) return Tx("게임 보안 프로그램, 변경 불가", "Anti-cheat, can't change"); if (Names.IsDatabase(a.Exe)) return Tx("데이터베이스, 종료 불가", "Database, can't close"); if (Names.IsGameClient(a.Exe)) return Tx("게임 클라이언트, 종료 불가", "Game client, can't close"); if (Names.IsServer(a.Exe)) return Tx("서버 포함, 종료 불가", "Runs a server, can't close"); }
            return g.Key == "win" ? Tx("윈도우 구성 요소, 변경 불가", "Windows component, can't change") : g.Key == "lagwatch" ? Tx("트레이 메뉴에서 종료", "Exit from the tray menu") : Tx("종료 불가", "Can't close");
        }
        /// <summary>줄 사이 가는 선</summary>
        Col Sep { get { return T.Fg.Fade(T.Dark ? .1f : .14f); } }

        /// <summary>글 폭만큼 나눈 고르기. right 면 x 가 오른쪽 끝. 반대쪽 끝을 돌려준다.</summary>
        float Segs(string id, string[] opt, int sel, float x, float y, float h, Style st, bool right, Action<int> pick)
        {
            float pad = Math.Max(18, h * .7f), tot = 0; for (int i = 0; i < opt.Length; i++) tot += C.Measure(opt[i], st) + pad;
            float x0 = right ? x - tot : x, cx = x0, rad = Math.Min(12, h * .34f);
            C.Round(x0, y, tot, h, rad, T.Faint);
            if (sel >= 0 && sel < opt.Length) { float sx = 0, sw = C.Measure(opt[sel], st) + pad; for (int i = 0; i < sel; i++) sx += C.Measure(opt[i], st) + pad; C.Round(x0 + A(id + "~x", sx), y, A(id + "~w", sw), h, rad, T.Btn); }
            for (int i = 0; i < opt.Length; i++)
            {
                float ow = C.Measure(opt[i], st) + pad; string zid = id + i; int k = i; bool on = i == sel;
                C.Mid(opt[i], st, cx + ow / 2, y + h / 2, on ? T.Bfg : T.Fg.Fade(Over(zid) ? 1 : .7f), 1);
                if (pick != null) Zone(cx, y, ow, h, zid, () => pick(k));
                cx += ow;
            }
            return right ? x0 : x0 + tot;
        }

        void Choice(string label, string[] opt, int sel, float x, float r, float y, string id, Action<int> pick)
        {
            C.Mid(label, F.Sans(16.5f, 700), x, y + 19, T.Fg.Fade(.75f)); float ox = x + 84, ow = (r - ox) / opt.Length;
            C.Round(ox, y, r - ox, 38, 12, T.Faint); if (sel >= 0) C.Round(ox + A(id + "~x", ow * sel), y, ow, 38, 12, T.Btn);
            for (int i = 0; i < opt.Length; i++)
            {
                string zid = id + i; int k = i; bool on = i == sel;
                C.Mid(opt[i], F.Sans(15.5f, 700), ox + ow * i + ow / 2, y + 19, on ? T.Bfg : T.Fg.Fade(Over(zid) ? 1 : .75f), 1); Zone(ox + ow * i, y, ow, 38, zid, () => pick(k));
            }
        }
        void SetRule(Grp g, int cores, int prio, bool always)
        {
            var r = new Rule { Key = g.Key, Label = g.Label, Cores = Math.Min(cores, Math.Max(1, S.Phys - 1)), Prio = prio, Always = always };
            System.Threading.ThreadPool.QueueUserWorkItem(o => { D.Fix.Set(r); if (r.Any && (r.Always || S.Game != null) && D.Fix.AppliedCount(r.Key) == 0) toastNext = Tx("권한이 없어 적용 안 됨", "Not applied (no permission)"); Redraw(); });
        }
    }
}
