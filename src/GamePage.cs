using System;
using System.Collections.Generic;
using static LagWatch.Lang;

namespace LagWatch
{
    internal sealed partial class MainView
    {
        // ---- 게임 ----

        void PaintGame(float w, float h)
        {
            D.Boost.Look(3000); if (look != null) { PaintRound(w, h); return; }
            float bot = 66, mid = w / 2, top = 264;
            float bx = w - 40; var st = F.Sans(18, 800);
            bool any = false, all = true; foreach (var g in D.Boost.Games) if (g.Installed) foreach (var k in g.Knobs) { if (k.Rec < 0 || k.Now < 0) continue; if (k.Now != k.Rec && !Waiting(k)) all = false; else any = true; }
            if (!M.Set.Bg) all = false; else any = true;
            if (any) { float b2 = C.Measure(Tx("되돌리기", "Revert"), st) + 44; bx -= b2; Pill("gno", Tx("되돌리기", "Revert"), bx, 76, b2, 52, 26, T.Fg.Fade(.1f), T.Fg, st, () => Ask(Tx("되돌리기", "Revert"), Tx("우선순위, 클라이언트, 코어 제한을 원래 값으로 되돌립니다.", "Priority, client and core limits go back to their original values."), () => Work(() => ApplyAll(false), true))); bx -= 10; }
            if (!all) { float b1 = C.Measure(Tx("권장 설정 적용", "Apply recommended"), st) + 48; bx -= b1; Pill("gall", Tx("권장 설정 적용", "Apply recommended"), bx, 76, b1, 52, 26, T.Btn, T.Bfg, st, () => Ask(Tx("권장 설정 적용", "Apply recommended"), RecText(), () => Work(() => ApplyAll(true), true))); bx -= 10; }
            Say(w, Tx("게임", "Games"), GameLine(), w - bx);
            C.Fill(mid, 160, 1, h - bot - 176, Sep);
            for (int i = 0; i < D.Boost.Games.Count && i < 2; i++) Half(D.Boost.Games[i], i == 0 ? 0 : mid, i == 0 ? mid : w - mid, top, h - bot, i == 0 ? 40 : 28, i == 0 ? 28 : 40, i);

            C.Fill(40, h - bot, w - 80, 1, Sep); float by = h - bot / 2;
            float x = Toggle("bg", Tx("게임 중 다른 프로그램은 코어 {0}개만", "In game, other apps get {0} cores", Fixer.BgCores(S.Phys)), M.Set.Bg, 40, by, T.Fg, () => { M.Set.Bg = !M.Set.Bg; M.Set.Save(); System.Threading.ThreadPool.QueueUserWorkItem(o => { try { D.Fix.Tick(); } catch (Exception) { } Redraw(); }); });
            var l = D.Fix.BgLabels();
            C.Mid(l.Count > 0 ? Tx("적용 중: ", "Active: ") + string.Join(", ", l.ToArray()) : Tx("제외: DB, 음성, 녹화, 입력 장치, 클라이언트", "Skipped: DB, voice, recording, input, clients"), F.Sans(16, 600), w - 40, by, T.Fg.Fade(.6f), 2, w - x - 70);
        }

        /// <summary>권장 설정이 하는 일. 설치된 게임 것만</summary>
        string RecText()
        {
            bool lol = false; foreach (var g in D.Boost.Games) if (g.Installed) foreach (var k in g.Knobs) if (k is ClientKnob) lol = true;
            return Tx("게임 우선순위 높음, ", "High game priority, ") + (lol ? Tx("롤 클라이언트 닫기, ", "close the LoL client, ") : "") + Tx("다른 프로그램 코어 {0}개로 제한", "limit other apps to {0} cores", Fixer.BgCores(S.Phys));
        }

        static bool Waiting(Knob k) { var c = k as ClientKnob; return c != null && c.Pending == c.Rec; }
        void Work(Func<string> f, bool big = false) { var from = PressR; System.Threading.ThreadPool.QueueUserWorkItem(o => { string r = null; bool ok = true; try { r = f(); } catch (Exception ex) { r = ex.Message; ok = false; } if (!string.IsNullOrEmpty(r)) toastNext = r; LastOk = ok; if (big || !ok) Finish(new Outcome { Op = "apply", Ok = ok, From = from }); Redraw(); }); }

        string ApplyAll(bool on) { return D.ApplyGame(on); }

        void Half(GameSet g, float x0, float w, float top, float bot, float pl, float pr, int side)
        {
            float tx = x0 + pl, r = x0 + w - pr; var list = D.Rounds.Of(g.Exe, 16);
            float nw = C.Mid(g.Label, F.Sans(20, 800), tx, 173, T.Fg);
            bool run = S.Game != null && S.Game.Main != null && S.Game.Main.Exe == g.Exe;
            C.Mid(!g.Installed ? Tx("설치 안 됨", "Not installed") : run ? Tx("실행 중", "Running") : list.Count > 0 ? Tx("최근 {0}판", "Last {0} {1}", Math.Min(5, list.Count), Pl(Math.Min(5, list.Count), "match", "matches")) : "", F.Sans(16, 600), tx + nw + 12, 174, T.Fg.Fade(.72f));
            if (list.Count == 0) C.Text(Tx("기록 없음", "No records"), F.Sans(34, 800), tx, 238, T.Fg.Fade(.5f));
            else
            {
                var m = Rounds.Mean(list.GetRange(Math.Max(0, list.Count - 5), Math.Min(5, list.Count)));
                float bw = C.Text(m.Fps.ToString("0"), F.Din(74), tx, 243, T.Fg), u = C.Text("fps", F.Din(30), tx + bw + 4, 243, T.Fg);
                C.Text(Tx("하위 1% {0}fps, 끊김 10분당 {1}번", "1% low {0}fps, {1} stutters/10 min", m.Low.ToString("0"), m.Per10.ToString("0.0")), F.Sans(17, 600), tx + bw + u + 16, 243, T.Fg.Fade(.78f), 0, r - tx - bw - u - 16);
            }
            float y = top, rh = 62;
            if (g.Installed)
                for (int i = 0; i < g.Knobs.Count; i++, y += rh)
                {
                    var k = g.Knobs[i]; C.Fill(tx, y, r - tx, 1, Sep);
                    float ox = Pick("gk" + side + i, k, r, y + (rh - 38) / 2);
                    bool two = !string.IsNullOrEmpty(k.Note); var ink = T.Fg.Fade(k.Now < 0 ? .6f : 1);
                    var bk = k as BootKnob;
                    C.Text(k.Label, F.Sans(18, 700), tx, y + (two ? 28 : rh / 2 + 6), ink, 0, ox - tx - 12); if (two) C.Text(k.Note, F.Sans(15, 500), tx, y + 49, bk != null && bk.Reset ? T.WarnInk : T.Fg.Fade(.66f), 0, ox - tx - 12);
                }
            Bars(g, list, x0, w, y, bot, tx, r, side);
        }

        /// <summary>오른쪽에 붙는 고르기. 왼쪽 끝을 돌려준다.</summary>
        float Pick(string id, Knob k, float r, float y)
        {
            var ck = k as ClientKnob; int sel = ck != null && ck.Pending >= 0 ? ck.Pending : k.Now;
            var st = F.Sans(C.W < 1040 ? 14.5f : 15.5f, 700);
            return Segs(id + "-", k.Options, sel, r, y, 38, st, true, k.Now < 0 ? (Action<int>)null : v =>
            {
                Action set = () => Work(() => D.Boost.Set(k, v) ?? (k is PrioKnob ? Tx("다음에 켤 때부터 적용", "Applies next launch") : null));
                var bk = k as BootKnob; if (bk != null && bk.NoOrg && v == 0 && k.Now != 0) Ask(Tx("Steam 파일 무결성 확인", "Verify files in Steam"), Tx("원본을 몰라 Steam에서 이터널 리턴 파일을 확인합니다.", "The original file is unknown. Steam will verify the Eternal Return files."), set); else set();
            });
        }

        /// <summary>판마다 10분당 끊김. 지금 설정으로 한 판은 다른 색.</summary>
        void Bars(GameSet g, List<Round> l, float x0, float w, float top, float bot, float tx, float r, int side)
        {
            C.Fill(tx, top, r - tx, 1, Sep);
            Round b, a; int nb, na; string head = Tx("판마다 끊김 (10분당)", "Stutters per match (per 10 min)");
            if (D.Rounds.Compare(g.Exe, out b, out nb, out a, out na)) head = Tx("10분당 끊김, 바꾸기 전 {0}판 {1}번 → 후 {2}판 {3}번", "Per 10 min: {1} ({0} {4}) → {3} ({2})", nb, b.Per10.ToString("0.0"), na, a.Per10.ToString("0.0"), Pl(nb, "match", "matches"));
            int hov = -1; for (int i = 0; i < l.Count; i++) if (Over("gb" + side + "-" + i)) hov = i;
            if (hov >= 0) { var q = l[hov]; head = Tx("{0}, {1}, {2}fps, 하위 1% {3}, 끊김 {4}번{5}, 최대 {6}ms", "{0}, {1}, {2}fps, 1% low {3}, {4} {7}{5}, max {6}ms", q.End.ToString("M'/'d HH:mm"), Fmt.Dur(q.Sec), q.Fps.ToString("0"), q.Low.ToString("0"), q.Hitch, q.Why().Length > 0 ? " (" + q.Why() + ")" : "", q.Max.ToString("0"), Pl(q.Hitch, "stutter", "stutters")); }
            if (l.Count == 0) return;
            Col c0 = T.Fg.Fade(T.Dark ? .32f : .28f), c1 = T.W[1];
            float lw = 0; { string[] nm = { Tx("기본", "Default"), Tx("설정 적용", "Tuned") }; Col[] cc = { c0, c1 }; var ls = F.Sans(14.5f, 600); float lx = r; for (int i = 1; i >= 0; i--) { float tw = C.Mid(nm[i], ls, lx, top + 26, T.Fg.Fade(.7f), 2); lx -= tw + 10; C.Circle(lx, top + 26, 4.5f, cc[i]); lx -= 18; } lw = r - lx; }
            C.Mid(head, F.Sans(15.5f, 600), tx, top + 26, T.Fg.Fade(.8f), 0, r - tx - lw);
            double mx = 2; foreach (var q in l) mx = Math.Max(mx, q.Per10);
            float gap = 5, bw = Math.Min(40, (r - tx - gap * 15) / 16), x = tx, base_ = bot - 14, H = base_ - top - 66;
            if (H < 30) return;
            for (int i = 0; i < l.Count; i++)
            {
                var q = l[i]; float bh = Math.Max(4, (float)(q.Per10 / mx) * H); string id = "gb" + side + "-" + i;
                var c = Rounds.Plain(q.Sig) ? c0 : c1; C.Round(x, base_ - bh, bw, bh, Math.Min(5, bw / 3), Over(id) ? c.To(T.Fg, .3f) : c);
                C.Mid(q.Per10.ToString(q.Per10 < 9.95 ? "0.#" : "0"), F.Din(16), x + bw / 2, base_ - bh - 11, T.Fg.Fade(.85f), 1);
                var qq = q; Zone(x - gap / 2, top + 40, bw + gap, base_ - top - 40, id, () => { look = qq; Redraw(); }); x += bw + gap;
            }
        }

        // ---- 판 하나: 언제, 왜, 어떻게 ----

        Round look;
        static readonly Col[] HitCol = { Col.Hex(0xffffff), Col.Hex(0xff5b2e), Col.Hex(0x5cbcc6), Col.Hex(0xe2a83a), Col.Hex(0xe2578b), Col.Hex(0x7f9fd0), Col.Hex(0x98a4b0) };

        void PaintRound(float w, float h)
        {
            var q = look; var hits = D.Hits.Between(q.Game, q.Start.AddSeconds(-2), q.End.AddSeconds(2)); var gs = D.Boost.Of(q.Game);
            float b1 = C.Measure(Tx("목록", "Back"), F.Sans(18, 800)) + 48; Pill("rback", Tx("목록", "Back"), w - 40 - b1, 76, b1, 52, 26, T.Fg.Fade(.1f), T.Fg, F.Sans(18, 800), () => { look = null; Redraw(); });
            Say(w, Tx("{0} 끊김 {1}번", "{0}: {1} {2}", gs == null ? q.Game : gs.Label, q.Hitch, Pl(q.Hitch, "stutter", "stutters")), q.End.ToString("M'/'d HH:mm") + ", " + Fmt.Dur(q.Sec) + Tx(", 평균 {0}fps, 하위 1% {1}fps", ", avg {0}fps, 1% low {1}fps", q.Fps.ToString("0"), q.Low.ToString("0")), b1 + 20);
            // 프레임 시간: 바닥 선에서 끊긴 만큼 솟는다
            float y0 = 168, ch = Math.Min(230, h * .33f), yb = y0 + ch, gl = 40, gr = w - 96, floor = yb - 34;
            Func<double, float> Y = ms => floor - 6 - (float)Math.Max(0, Math.Min(6.3, Math.Log(Math.Max(1, ms) / 16, 2))) * (ch - 62) / 6.3f;
            foreach (double ms in new double[] { 25, 50, 100, 250, 500, 1000 }) { float y = Y(ms); C.Fill(gl, y, gr - gl, 1, T.Fg.Fade(.08f)); C.Mid(ms >= 1000 ? Tx("1초", "1s") : ms.ToString("0") + "ms", F.Sans(13.5f, 600), w - 40, y, T.Fg.Fade(.5f), 2); }
            C.Fill(gl, floor, gr - gl, 1.5f, T.Fg.Fade(.3f));
            double span = Math.Max(1, (q.End - q.Start).TotalSeconds); Hit hov = null; float hx = 0;
            for (int i = 0; i < hits.Count; i++)
            {
                var t = hits[i]; float x = gl + (gr - gl) * (float)Math.Max(0, Math.Min(1, (t.At - q.Start).TotalSeconds / span)), y = Y(t.Ms); var c = HitCol[t.Kind]; string id = "ht" + i; bool ov = Over(id);
                C.Fill(x - 1, y, 2, floor - y, c); C.Circle(x, y, ov ? 6 : 3.8f, c); if (ov) { hov = t; hx = x; }
                Zone(x - 5, y - 8, 10, floor - y + 8, id, () => { });
            }
            C.Mid(q.Start.ToString("HH:mm"), F.Sans(14.5f, 600), gl, floor + 18, T.Fg.Fade(.65f)); C.Mid(q.End.ToString("HH:mm"), F.Sans(14.5f, 600), gr, floor + 18, T.Fg.Fade(.65f), 2);
            if (hov != null)
            {
                string t = hov.At.ToString("HH:mm:ss") + ", " + hov.Ms.ToString("0") + "ms, " + Hit.Name[hov.Kind] + (hov.Who.Length > 0 && hov.Kind == Hit.Cpu ? " (" + hov.Who + ")" : hov.Note.Length > 0 ? " (" + Hit.Say(hov.Note) + ")" : "") + (hov.Kind == Hit.Ram ? Tx(", 남은 램 ", ", free RAM ") + hov.RamLeft.ToString("0.0") + "GB" : "") + (hov.Measured ? "" : Tx(", 추정", ", estimated"));
                var st = F.Sans(15.5f, 700); float tw = C.Measure(t, st) + 28, px = Math.Max(8, Math.Min(w - tw - 8, hx - tw / 2)); C.Round(px, y0 - 4, tw, 34, 17, T.Btn); C.Mid(t, st, px + tw / 2, y0 + 13, T.Bfg, 1);
            }
            // 까닭
            var kinds = new List<int>(); var cnt = new int[7]; foreach (var t in hits) cnt[t.Kind]++; for (int k = 1; k <= 6; k++) if (cnt[k] > 0) kinds.Add(k); kinds.Sort((x, y) => cnt[y].CompareTo(cnt[x]));
            float ry = yb + 20, sx = 40, r = w - 40;
            if (kinds.Count == 0) { C.Mid(q.Hitch == 0 ? Tx("끊김 없음", "No stutters") : Tx("이 판은 까닭을 재지 않았음", "Causes weren't measured in this match"), F.Sans(19, 600), sx, ry + 30, T.Fg.Fade(.6f)); return; }
            float rh = Math.Max(62, Math.Min(84, (h - ry - 8) / kinds.Count));
            for (int i = 0; i < kinds.Count; i++)
            {
                int k = kinds[i]; float y = ry + i * rh, cy = y + rh / 2; C.Fill(sx, y, r - sx, 1, Sep);
                var mine = hits.FindAll(t => t.Kind == k); double mx = 0; int guess = 0; foreach (var t in mine) { mx = Math.Max(mx, t.Ms); if (!t.Measured) guess++; }
                C.Round(sx, cy - 18, 5, 36, 2.5f, HitCol[k]);
                float bx = r; var acts = Fixes(k, mine);
                for (int j = acts.Count - 1; j >= 0; j--) { var a = acts[j]; var st = F.Sans(16, 700); float bw = C.Measure(a.Label, st) + 36; bx -= bw; string op = a.Id; Pill("rf" + i + "-" + j, a.Label, bx, cy - 20, bw, 40, 20, j == 0 ? T.Btn : T.Fg.Fade(.1f), j == 0 ? T.Bfg : T.Fg, st, () => Do2(op)); bx -= 10; }
                float tx = sx + 22, tw = bx - tx - 16;
                C.Text(Tx("{0} {1}번", "{0}: {1}", Hit.Name[k], cnt[k]), F.Sans(19, 700), tx, cy - 3, T.Fg, 0, tw);
                C.Text(Evidence(k, mine, mx) + (guess * 2 > mine.Count ? Tx(", 추정", ", estimated") : ""), F.Sans(16, 500), tx, cy + 21, T.Fg.Fade(.7f), 0, tw);
            }
        }

        static string Evidence(int k, List<Hit> l, double mx)
        {
            string s = Tx("최대 ", "max ") + mx.ToString("0") + "ms";
            if (k == Hit.Ram) { double least = 99; string who = ""; foreach (var h in l) if (h.RamLeft < least) { least = h.RamLeft; who = h.Who; } return s + Tx(", 남은 램 ", ", free RAM ") + least.ToString("0.0") + "GB" + (who.Length > 0 ? ", " + who : ""); }
            var by = new Dictionary<string, int>(); foreach (var h in l) { string t = k == Hit.Cpu ? h.Who : Hit.Say(h.Note); if (string.IsNullOrEmpty(t)) continue; int c; by.TryGetValue(t, out c); by[t] = c + 1; }
            var o = new List<KeyValuePair<string, int>>(by); o.Sort((a, b) => b.Value.CompareTo(a.Value)); for (int i = 0; i < o.Count && i < 3; i++) s += ", " + Tx("{0} {1}번", "{0} ×{1}", o[i].Key, o[i].Value);
            return s;
        }

        /// <summary>그 까닭에 지금 할 수 있는 것</summary>
        List<Act> Fixes(int k, List<Hit> l)
        {
            var r = new List<Act>();
            if (k == Hit.Ram)
            {
                Grp top = null; foreach (var g in S.Groups) if (g != S.Game && Fixer.CanEnd(g) && g.Ws > 400L << 20 && !g.Apps.Exists(a => Names.IsWork(a.Exe)) && (top == null || g.Ws > top.Ws)) top = g;
                if (top != null) r.Add(new Act(Tx("{0} 종료 ({1})", "Close {0} ({1})", top.Label, Fmt.Mem(top.Ws / 1048576.0)), "end:" + top.Key));
                if (!M.Set.AutoTrimGame) r.Add(new Act(Tx("시작할 때 램 청소", "Clean RAM at start"), "trimgame"));
            }
            else if (k == Hit.Cpu)
            {
                if (!M.Set.Bg) r.Add(new Act(Tx("다른 프로그램 코어 제한", "Limit other apps"), "bgon"));
                var by = new Dictionary<string, int>(); foreach (var h in l) { int c; by.TryGetValue(h.Who, out c); by[h.Who] = c + 1; } string who = null; foreach (var kv in by) if (who == null || kv.Value > by[who]) who = kv.Key;
                foreach (var g in S.Groups) if (g.Label == who && Fixer.CanTune(g) && D.Fix.Find(g.Key) == null) { r.Add(new Act(Tx("{0} 코어 2개로", "{0} to 2 cores", g.Label), "always:" + g.Key)); break; }
            }
            else if (k == Hit.Drv) r.Add(new Act(D.DeepBusy ? Tx("검사 중", "Scanning") : Tx("드라이버 검사", "Scan drivers"), "deep"));
            if (r.Count > 2) r.RemoveRange(2, r.Count - 2);
            return r;
        }
        void Do2(string id)
        {
            if (id == "trimgame") { M.Set.AutoTrimGame = true; M.Set.Save(); LastOk = true; Toast(Tx("게임 시작할 때 청소함", "Will clean at game start")); return; }
            if (id == "bgon") { M.Set.Bg = true; M.Set.Save(); System.Threading.ThreadPool.QueueUserWorkItem(o => { try { D.Fix.Tick(); } catch (Exception) { } }); LastOk = true; Toast(Tx("게임 중 다른 프로그램 코어 {0}개만", "In game, other apps get {0} cores", Fixer.BgCores(S.Phys))); return; }
            Run(id);
        }
    }
}
