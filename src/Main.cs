using System;
using System.Collections.Generic;
using static LagWatch.Lang;

namespace LagWatch
{
    /// <summary>큰 창: 지금 · 요약 · 시간별 · 프로그램 · 게임 · 청소</summary>
    internal sealed partial class MainView : Surface
    {
        public const int Now = 0, Sum = 1, Time = 2, Prog = 3, Game = 4, Clean = 5;
        public static readonly string[] Tabs = { Tx("지금", "Now"), Tx("요약", "Summary"), Tx("시간별", "History"), Tx("프로그램", "Apps"), Tx("게임", "Games"), Tx("청소", "Clean") };
        public int Page;
        public bool Cores;
        public string Open;                 // 오른쪽에 연 프로그램
        string toast; double toastAt;
        public Action Min, Max, Close, PageChanged, Small;
        public Action<string, string, Action> Confirm;

        public MainView(Doctor d) : base(d) { }

        Cause Bad { get { foreach (var c in D.Causes) if (c.Level == 2) return c; return null; } }

        public void Go(int page)
        {
            Page = page; Open = null; look = null; scroll = 0; solo = null; scrub = -1;
            if (PageChanged != null) PageChanged();
            Redraw();
        }
        public void Toast(string s) { if (string.IsNullOrEmpty(s)) return; toast = s; toastAt = Environment.TickCount / 1000.0; Redraw(); }

        /// <summary>원인에 붙은 단추</summary>
        public void Run(string id)
        {
            if (id.StartsWith("open:")) { Open = id.Substring(5); Redraw(); return; }
            if (id == "page:round") { Go(Game); lock (D.Rounds.Items) look = D.Rounds.Items.Count > 0 ? D.Rounds.Items[D.Rounds.Items.Count - 1] : null; return; }
            if (id.StartsWith("page:")) { Go(id == "page:clean" ? Clean : id == "page:game" ? Game : Sum); return; }
            if (id.StartsWith("restart:") || id.StartsWith("end:") || id.StartsWith("after:"))
            {
                string key = id.Substring(id.IndexOf(':') + 1); Grp g; S.ByKey.TryGetValue(key, out g);
                if (g == null) return;
                Ask(id[0] == 'r' ? Tx("{0} 재시작", "Restart {0}", g.Label) : id[0] == 'a' ? Tx("{0} 게임 후 재시작", "Restart {0} after game", g.Label) : Tx("{0} 종료", "Close {0}", g.Label), Tx("프로세스 {0}개를 종료합니다. 저장하지 않은 내용은 사라집니다.", "This closes {0} {1}. Unsaved work will be lost.", g.Count, Pl(g.Count, "process", "processes")), () => Back(id));
                return;
            }
            Back(id);
        }
        void Ask(string title, string text, Action ok) { var id = PressId; var r = PressR; if (Confirm != null) Confirm(title, text, () => { PressId = id; PressR = r; ok(); }); else ok(); } // 움직임은 처음 누른 단추에서
        void Back(string id)
        {
            Fire(id, PressId, PressR, r => toastNext = r);
        }
        volatile string toastNext;

        protected override void Paint(float w, float h)
        {
            if (toastNext != null) { Toast(toastNext); toastNext = null; }
            var bad = Bad; RamBar = TempBox = new R4();
            Sky(w, h, 0);
            switch (Page)
            {
                case Now: PaintNow(w, h, bad); break;
                case Sum: PaintSum(w, h, bad); break;
                case Time: PaintTime(w, h); break;
                case Prog: PaintProg(w, h); break;
                case Game: PaintGame(w, h); break;
                default: PaintClean(w, h); break;
            }
            Head(w);
            PaintSheet(w, h);
            PaintAsk(w, h);
            PaintToast(w, h);
            PaintFx(w, h);
            Caption(w);
        }

        void Head(float w)
        {
            float x = 40;
            for (int i = 0; i < Tabs.Length; i++)
            {
                string id = "tab" + i; int n = i;
                var st = F.Sans(18, Page == i ? 800 : 700);
                float tw = C.Measure(Tabs[i], st);
                C.Mid(Tabs[i], st, x, 30, T.Fg.Fade(Page == i ? 1 : Over(id) ? .85f : .5f));
                Zone(x - 10, 12, tw + 20, 38, id, () => Go(n));
                x += tw + 26;
            }
        }

        void Caption(float w)
        {
            { // 작은 창으로
                bool ov = Over("capS"); float sx = w - 184; if (ov) C.Fill(sx, 0, 46, 34, T.Fg.Fade(.14f)); var sc = T.Fg.Fade(.7f);
                C.Line(sx + 17.5f, 11.5f, sx + 29.5f, 11.5f, sc, 1); C.Line(sx + 17.5f, 11.5f, sx + 17.5f, 22.5f, sc, 1); C.Line(sx + 17.5f, 22.5f, sx + 29.5f, 22.5f, sc, 1); C.Line(sx + 29.5f, 11.5f, sx + 29.5f, 22.5f, sc, 1); C.Fill(sx + 23.5f, 17, 5, 4.5f, sc);
                Zone(sx, 0, 46, 34, "capS", () => { if (Small != null) Small(); });
                if (ov) { var st = F.Sans(14, 600); float tw = C.Measure(Tx("작은 창으로", "Compact view"), st) + 20; C.Round(sx + 23 - tw / 2, 40, tw, 28, 8, T.Btn); C.Mid(Tx("작은 창으로", "Compact view"), st, sx + 23, 54, T.Bfg, 1); }
            }
            float[] m = { w - 138, w - 92, w - 46 }; Action[] act = { Min, Max, Close };
            for (int i = 0; i < 3; i++)
            {
                string id = "cap" + i; bool ov = Over(id);
                if (ov) C.Fill(m[i], 0, 46, 34, i == 2 ? Col.Hex(0xe81123) : T.Fg.Fade(.14f));
                var c = ov && i == 2 ? Col.Hex(0xffffff) : T.Fg.Fade(.7f); float cx = m[i] + 23, cy = 17;
                if (i == 0) C.Line(cx - 5, cy + .5f, cx + 5, cy + .5f, c, 1);
                else if (i == 1) { C.Line(cx - 5, cy - 5, cx + 5, cy - 5, c, 1); C.Line(cx + 5, cy - 5, cx + 5, cy + 5, c, 1); C.Line(cx + 5, cy + 5, cx - 5, cy + 5, c, 1); C.Line(cx - 5, cy + 5, cx - 5, cy - 5, c, 1); }
                else { C.Line(cx - 5, cy - 5, cx + 5, cy + 5, c, 1); C.Line(cx + 5, cy - 5, cx - 5, cy + 5, c, 1); }
                int k = i; Zone(m[i], 0, 46, 34, id, () => { if (act[k] != null) act[k](); });
            }
        }

        void PaintToast(float w, float h)
        {
            if (toast == null) return;
            if (!ToastBox(toast, Environment.TickCount / 1000.0 - toastAt, 5, w / 2, h - 50, 48, F.Sans(17, 700), T.Btn, T.Bfg, LastOk)) toast = null;
        }

        /// <summary>맨 위 큰 글. 오른쪽에 단추가 있으면 그 폭만큼 비운다.</summary>
        float Say(float w, string h1, string sub, float right) { return Say(w, h1, sub, right, T.Fg); }
        float Say(float w, string h1, string sub, float right, Col ink)
        {
            float tw = C.Text(h1, F.Sans(46, 800), 40, 100, ink, 0, w - 80 - right);
            if (!string.IsNullOrEmpty(sub)) C.Text(sub, F.Sans(19, 500), 40, 134, T.Fg.Fade(.88f), 0, w - 80 - right);
            return tw;
        }

        string GameLine()
        {
            if (S.Game == null) return Tx("실행 중인 게임 없음", "No game running");
            string s = Tx("{0} {1}째", "{0} · {1}", S.Game.Label, Fmt.Dur(S.GameSec));
            if (D.FramesOk && D.Fps > 1) s += ", " + D.Fps.ToString("0") + "fps, " + (D.Hitch1m == 0 ? Tx("끊김 없음", "no stutters") : Tx("최근 1분 끊김 {0}번", "{0} {1} in last min", D.Hitch1m, Pl(D.Hitch1m, "stutter", "stutters")));
            else if (S.Wait > 0.02) s += Tx(", CPU 대기 ", ", CPU wait ") + (S.Wait * 100).ToString("0") + "%";
            return s;
        }

        Cause Done { get { foreach (var c in D.Causes) if (c.Level == -1) return c; return null; } }

        float HeadActs(float w, Cause c)
        {
            float x = w - 40, total = 0; var list = new List<Act>(c.Acts); if (list.Count > 2) list.RemoveRange(2, list.Count - 2);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var a = list[i]; var st = F.Sans(18, 800); float bw = C.Measure(a.Label, st) + 48; x -= bw;
                bool main = i == 0 && c.Level == 2; string id = "ha" + i; string op = a.Id;
                Pill(id, a.Label, x, 76, bw, 52, 26, main ? Col.Hex(0xffffff) : T.Glass, main ? Col.Hex(0xb51d14) : T.Fg, st, () => Run(op));
                x -= 10; total += bw + 10;
            }
            return total;
        }

        // ---- 지금 ----

        void PaintNow(float w, float h, Cause bad)
        {
            var done = bad == null ? Done : null;
            float rs = 0;
            if (bad != null) rs = HeadActs(w, bad);
            else if (done != null && done.Acts.Count > 0) rs = HeadActs(w, done);
            else rs = PaintUps(w);
            if (bad != null) Say(w, HeadTitle(bad), bad.Why, rs + 20);
            else if (done != null) Say(w, done.Title, string.IsNullOrEmpty(done.Why) ? GameLine() : done.Why, rs + 20);
            else Say(w, Tx("문제 없음", "No issues"), GameLine(), rs + 20);
            PaintFlow(w, h, bad);
        }
        static string HeadTitle(Cause c) { return c.Title; }

        float PaintUps(float w)
        {
            var g = D.Growing; if (g.Count == 0) return 0;
            float x = w - 40; int n = Math.Min(2, g.Count);
            for (int i = n - 1; i >= 0; i--)
            {
                var q = g[i]; string t = "+" + Fmt.Mem(q.Last - q.First); float tw = C.Measure(t, F.Din(20));
                Grp gr; S.ByKey.TryGetValue(q.Key, out gr); string id = "up" + i; string key = q.Key;
                float nw = C.Measure(q.Label, F.Sans(16.5f, 700)), bw = 22 + 8 + nw + 8 + tw;
                x -= bw; Icon(gr, q.Key, x, 105, 22, T.Fg);
                C.Mid(q.Label, F.Sans(16.5f, Over(id) ? 800 : 700), x + 30, 116, T.Fg); C.Mid(t, F.Din(20), x + 38 + nw, 116, T.Fg);
                Zone(x - 6, 98, bw + 12, 36, id, () => { Open = key; Redraw(); });
                x -= 22;
            }
            C.Mid(Tx("3시간 가상메모리 증가", "Commit growth, 3h"), F.Sans(16, 600), w - 40, 84, T.Fg.Fade(.75f), 2);
            return w - 40 - x;
        }

        string Culprit(int m, Cause bad) { return bad != null && (bad.Metric == m || (bad.Metric == Snap.Vm && m == Snap.Ram)) ? bad.Key : null; }
        bool Low(int m) { double l = S.Left(m) / Math.Max(1, S.Cap(m)); return l < (m == Snap.Cpu ? .12 : m == Snap.Ram ? .12 : .15); }

        /// <summary>사용률을 크게, 남은 양은 그 오른쪽에 작게. 큰 글의 폭을 돌려준다.</summary>
        float Usage(int m, float x, float y, bool left, float max = 9999)
        {
            var c = T.LvInk(Pct(m), T.Fg);
            float bw = C.Text(Txt.Pct(S, m), F.Din(74), x, y, c), lx = x + bw + 10; string t = Txt.Left(S, m) + Tx(" 남음", " free");
            var st = lx + C.Measure(t, F.Sans(19, 600)) <= max ? F.Sans(19, 600) : F.Sans(16, 600);
            if (left) C.Text(t, st, lx, y, c.Fade(.78f));
            return bw;
        }

        // ---- 코어 기둥 ----

        sealed class CoreMix { public string Key, Label; public Grp G; public double[] V; public double Sum; }
        List<CoreMix> mix; Kern.Report mixOf;

        List<CoreMix> Mix()
        {
            var r = D.Live; if (r == null) return null;
            if (r == mixOf) return mix;
            var map = new Dictionary<string, CoreMix>();
            lock (M.Sync)
                foreach (var kv in r.Core)
                {
                    if (kv.Key == 0) continue;
                    Proc q; string k = "win", l = Tx("윈도우", "Windows");
                    if (M.Procs.TryGetValue(kv.Key, out q) && q.App != null) Names.Group(q.App, out k, out l);
                    CoreMix c; if (!map.TryGetValue(k, out c)) { map[k] = c = new CoreMix { Key = k, Label = l, V = new double[S.Phys] }; S.ByKey.TryGetValue(k, out c.G); }
                    for (int i = 0; i < S.Lps && i < 64; i++) { double p = kv.Value[i] / r.Ms * 100 / Math.Max(1, S.Lps / S.Phys); c.V[M.CoreOf[i]] += p; c.Sum += p; }
                }
            var l2 = new List<CoreMix>(map.Values); l2.Sort((x, y) => y.Sum.CompareTo(x.Sum));
            mix = l2; mixOf = r;
            return l2;
        }

        /// <summary>코어마다 기둥 하나. 색은 그 코어의 사용률로. 올리면 그 코어를 쓴 프로그램.</summary>
        void CoreTubes(float x, float w, float top, float H, float legendY)
        {
            var all = Mix(); int n = S.Phys, hc = -1; float gap = 4, cw = (w - gap * (n - 1)) / n;
            for (int i = 0; i < n; i++)
            {
                float cx = x + i * (cw + gap); double v = Math.Min(100, S.Core[i]); var f = T.LvFill(v, T.W[1]);
                var mask = C.ClipRound(cx, top, cw, H, 8); C.Fill(cx, top, cw, H, T.Fg.Fade(T.Dark ? .07f : .09f));
                float bh = Soft("cb" + i, (float)(v / 100 * H), 4); C.Fill(cx, top + H - bh, cw, bh, Hover == "cc" + i ? f.To(T.Fg, .12f) : f); C.PopClip(mask);
                var ink = top + H - bh <= top + 26 ? T.Ink(f) : T.Fg;
                C.Mid((i + 1).ToString(), F.Sans(14, 700), cx + 9, top + 15, ink.Fade(.7f)); C.Mid(v.ToString("0") + "%", F.Din(cw > 60 ? 19 : 16), cx + cw - 8, top + 15, ink, 2);
                Zone(cx, top, cw + gap, H, "cc" + i, null); if (Hover == "cc" + i) hc = i;
            }
            if (all == null) { C.Mid(D.CoresOk ? Tx("측정 중", "Measuring") : Tx("프로그램별 표시는 관리자 권한 필요", "Per-app view needs administrator"), F.Sans(15.5f, 600), x, legendY, T.Fg.Fade(.7f)); return; }
            float lx = x; var ls = F.Sans(16.5f, 700); var vs = F.Din(19);
            foreach (var c in all)
            {
                double pc = c.Sum / Math.Max(1, n); if (pc < .5) break; string val = pc.ToString("0") + "%"; float iw = 20 + 7 + C.Measure(c.Label, ls) + 7 + C.Measure(val, vs); if (lx + iw > x + w) break;
                Icon(c.G, c.Key, lx, legendY - 10, 20, T.Fg); float tx = lx + 27; tx += C.Mid(c.Label, ls, tx, legendY, T.Fg) + 7; C.Mid(val, vs, tx, legendY + 1, T.Fg.Fade(.8f)); lx += iw + 28;
            }
            if (hc < 0) return;
            var sb = new System.Text.StringBuilder(Tx("{0}번 코어", "Core {0}", hc + 1)); int k = 0;
            foreach (var c in all) { if (c.V[hc] < 1 || k >= 3) continue; sb.Append(k++ == 0 ? "  " : ", ").Append(c.Label).Append(' ').Append(c.V[hc].ToString("0")).Append('%'); }
            var st = F.Sans(16, 700); string t = sb.ToString(); float tw = C.Measure(t, st) + 26, px = Math.Max(x, Math.Min(x + w - tw, x + hc * (cw + gap) + cw / 2 - tw / 2));
            C.Round(px, top - 40, tw, 32, 16, T.Btn); C.Mid(t, st, px + tw / 2, top - 24, T.Bfg, 1);
        }

        // ---- 요약 ----

        void PaintSum(float w, float h, Cause bad)
        {
            var cs = D.Causes; int todo = 0, now = 0; foreach (var c in cs) { if (c.Level == 2) now++; else if (c.Fail || c.Level == 1 && c.Acts.Count > 0) todo++; }
            string h1 = now > 0 ? Tx("문제 {0}개", "{0} {1}", now, Pl(now, "issue", "issues")) : todo > 0 ? Tx("손볼 것 {0}개", "{0} to check", todo) : Tx("문제 없음", "No issues");
            float rs = 0;
            if (S.Game != null && D.FramesOk && D.Fps > 1)
            {
                float x = w - 40; string[] n = { D.Hitch1m.ToString(), D.MaxMs.ToString("0"), D.Fps.ToString("0") }, u = { Tx("번", ""), "ms", "" }, l = { Tx("최근 1분 끊김", "Stutters, 1 min"), Tx("최대 멈춤", "Max freeze"), "fps" };
                for (int i = 0; i < 3; i++)
                {
                    float uw = u[i].Length > 0 ? C.Measure(u[i], i == 0 ? F.Sans(22, 700) : F.Din(26)) + 2 : 0, nw = C.Measure(n[i], F.Din(58)), lw = C.Measure(l[i], F.Sans(16, 600)), bw = Math.Max(lw, nw + uw);
                    if (uw > 0) C.Text(u[i], i == 0 ? F.Sans(22, 700) : F.Din(26), x, 112, T.Fg, 2);
                    C.Text(n[i], F.Din(58), x - uw, 112, T.Fg, 2); C.Text(l[i], F.Sans(16, 600), x, 138, T.Fg.Fade(.8f), 2);
                    x -= bw + 38; rs += bw + 38;
                }
            }
            Say(w, h1, TodayLine(), rs + 20);

            var list = new List<Cause>(cs); if (list.Count > 6) list.RemoveRange(6, list.Count - 6);
            var rules = new List<KeyValuePair<string, string>>(); // 해 둔 것
            lock (M.Sync)
            {
                foreach (var r0 in D.Fix.Rules) rules.Add(new KeyValuePair<string, string>("undo:" + r0.Key, r0.Label + ", " + (r0.Always ? Tx("항상 ", "always: ") : r0.Once ? Tx("이번 게임만 ", "this game: ") : Tx("게임할 때 ", "in game: ")) + r0.Text(S.Phys)));
                foreach (var q in D.Fix.Queue) rules.Add(new KeyValuePair<string, string>("unbook:" + q.Key, q.Label + Tx(", 게임 후 재시작", ", restart after game")));
            }
            float y = 176, sx = 40, r = w - 40; var line = T.Fg.Fade(T.Dark ? .1f : .14f);
            float room = h - y - 8 - (rules.Count > 0 ? 70 + Math.Min(3, rules.Count) * 44 : 0), rh = list.Count > 0 ? Math.Max(62, Math.Min(84, room / list.Count)) : 0;
            if (list.Count == 0) C.Mid(Tx("손볼 것 없음", "Nothing to fix"), F.Sans(19, 600), sx, y + 26, T.Fg.Fade(.6f));
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i]; float cy = y + rh / 2; if (i > 0) C.Fill(sx, y, r - sx, 1, line);
                C.Round(sx, cy - 18, 5, 36, 2.5f, c.Level == 2 ? T.Hot : c.Level == 1 && !c.Fail ? T.Warn : c.Level == -1 ? T.W[1] : T.Fg.Fade(.3f));
                Grp g; S.ByKey.TryGetValue(c.Key, out g); string key = c.Key; const float isz = 32;
                Icon(g, c.Key, sx + 20, cy - isz / 2, isz, T.Fg); if (g != null) Zone(sx + 16, y, isz + 8, rh, "si" + i, () => { Open = key; Redraw(); });
                float bx = r;
                for (int k = Math.Min(1, c.Acts.Count - 1); k >= 0; k--)
                {
                    var a = c.Acts[k]; if (a.Id.StartsWith("open:")) continue; var st = F.Sans(16, 700); float bw = C.Measure(a.Label, st) + 36; bx -= bw;
                    bool main = a.Main && k == 0; string op = a.Id;
                    Pill("sa" + i + "-" + k, a.Label, bx, cy - 20, bw, 40, 20, main ? T.Btn : T.Fg.Fade(.1f), main ? T.Bfg : T.Fg, st, () => Run(op)); bx -= 10;
                }
                float tx = sx + 20 + isz + 16, tw = bx - tx - 16;
                C.Text(c.Title, F.Sans(19, 700), tx, cy - 3, c.Level == 2 ? T.HotInk : T.Fg, 0, tw);
                C.Text(c.Why, F.Sans(16, 500), tx, cy + 21, T.Fg.Fade(.7f), 0, tw);
                y += rh;
            }
            if (rules.Count == 0) return;
            y += 12; C.Fill(sx, y, r - sx, 1, line); y += 30; C.Mid(Tx("해 둔 것", "Changes"), F.Sans(16, 700), sx, y, T.Fg.Fade(.6f)); y += 34;
            for (int i = 0; i < rules.Count && y < h - 16; i++)
            {
                string key = rules[i].Key.Substring(rules[i].Key.IndexOf(':') + 1); Grp g; S.ByKey.TryGetValue(key, out g);
                Icon(g, key, sx, y - 11, 22, T.Fg); C.Mid(rules[i].Value, F.Sans(16.5f, 600), sx + 32, y, T.Fg, 0, r - sx - 140);
                string op = rules[i].Key; float cw = Math.Max(64, C.Measure(Tx("취소", "Remove"), F.Sans(15, 700)) + 28); Pill("rule" + i, Tx("취소", "Remove"), r - cw, y - 16, cw, 32, 16, T.Fg.Fade(.1f), T.Fg, F.Sans(15, 700), () => Back(op));
                y += 44;
            }
        }

        string TodayLine()
        {
            int n = 0; double min = 0; var t0 = DateTime.Now.Date; if (DateTime.Now.Hour < 6) t0 = t0.AddHours(-12);
            string game = null;
            lock (M.Sync) foreach (var e in M.Log.Items) if (e.Kind == Kind.Game && e.Start >= t0) { n++; min += GameMinutes(e.Text); if (game == null) { int sp = e.Text.LastIndexOf(" ·"); game = e.Text; } }
            if (S.Game != null) { n++; min += S.GameSec / 60; }
            return n == 0 ? Tx("오늘 게임 없음", "No games today") : Tx("오늘 게임 {0}판, {1}", "Today: {0} {2}, {1}", n, Fmt.Dur(min * 60), Pl(n, "match", "matches"));
        }
        /// <summary>"… 22분 · …", "… 1시간 4분 · …" 에서 분</summary>
        public static int GameMinutes(string text)
        {
            int dot = text.IndexOf(" ·"); return Sys.Minutes(dot > 0 ? text.Substring(0, dot) : text);
        }
    }
}
