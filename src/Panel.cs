using System;
using System.Collections.Generic;
using static LagWatch.Lang;

namespace LagWatch
{
    /// <summary>트레이를 누르면 나오는 작은 창(검정·흰·주황)</summary>
    internal sealed class PanelView : Surface
    {
        public const float Wd = 412, Pad = 44;      // Pad = 그림자가 들어갈 자리
        public int Tab;                             // 0 요약, 1 CPU, 2 램, 3 가상메모리
        public string Open;
        public int Sub;                             // 1 게임, 2 임시 파일
        int lastSub;
        public float Height = 600, MaxH = 900;
        public Action OpenMain; public Action<string> OpenIn;
        string sure; double sureAt; string toast; double toastAt; volatile string toastNext;
        string lastOpen;

        public PanelView(Doctor d) : base(d) { }

        Cause Bad { get { foreach (var c in D.Causes) if (c.Level == 2) return c; return null; } }
        Cause Done { get { foreach (var c in D.Causes) if (c.Level == -1) return c; return null; } }

        public void Run(string id)
        {
            if (id.StartsWith("open:")) { Open = id.Substring(5); Redraw(); return; }
            if (id == "page:clean") { GoSub(2); return; }
            if (id == "page:round") { if (OpenIn != null) OpenIn("round"); return; }
            if (id.StartsWith("page:")) { if (OpenIn != null) OpenIn(id.Substring(5)); return; }
            if ((id.StartsWith("restart:") || id.StartsWith("end:") || id.StartsWith("after:")) && !(sure == id && Environment.TickCount / 1000.0 - sureAt < 4)) { sure = id; sureAt = Environment.TickCount / 1000.0; Redraw(); return; }
            sure = null;
            System.Threading.ThreadPool.QueueUserWorkItem(o => { string r; try { r = D.Do(id); } catch (Exception ex) { r = ex.Message; } toastNext = r; Redraw(); });
        }
        string Sure(string id, string label) { return sure == id && Environment.TickCount / 1000.0 - sureAt < 4 ? (En ? "Click again" : id == "tempdel" ? "한 번 더 누르면 삭제" : id == "trimall" ? "한 번 더" : id.StartsWith("end:") ? "한 번 더 누르면 종료" : id.StartsWith("restart:") ? "한 번 더 누르면 재시작" : id.StartsWith("after:") ? "한 번 더 누르면 예약" : "한 번 더 누르면 실행") : label; }

        protected override void Paint(float w, float h)
        {
            if (toastNext != null) { if (toastNext.Length > 0) { toast = toastNext; toastAt = Environment.TickCount / 1000.0; } toastNext = null; }
            C.Clear(new Col(0, 0, 0, 0));
            C.Move(0, 0);
            float H = Math.Min(MaxH, A("H", Wanted())); Height = H;
            float top = h - Pad - H;
            for (int i = 0; i < 12; i++) { float g = 2 + i * 3; C.Round(Pad - g, top - g + 12, Wd + g * 2, H + g * 2 - 4, 24 + g, Col.Hex(0, .028f)); }
            C.Move(Pad, top);
            var mask = C.ClipRound(0, 0, Wd, H, 24);
            C.Fill(0, 0, Wd, H, T.ABg);
            Zone(0, 0, Wd, H, "panel", null);
            float k = A("pg", Open != null || Sub != 0 ? 1 : 0); if (Sub != 0) lastSub = Sub; else if (Open != null) lastSub = 0;
            if (k < .996f) { C.Move(Pad - Wd * .26f * k, top); if (k > .01f) C.PushOpacity(1 - k); Front(H); if (k > .01f) C.PopClip(IntPtr.Zero); }
            if (k > .004f) { C.Move(Pad + Wd * (1 - k), top); C.Fill(0, 0, Wd, H, T.ABg); if (lastSub == 1) GameSub(H); else if (lastSub == 2) CleanSub(H); else Detail(H); }
            C.Move(Pad, top);
            if (toast != null && Environment.TickCount / 1000.0 - toastAt < 4) { var st = F.Wide(15.5f, 700); float tw = C.Measure(toast, st) + 34; C.Round(Wd / 2 - tw / 2, H - 58, tw, 42, 21, T.AFill); C.Mid(toast, st, Wd / 2, H - 37, T.AOn, 1); } else toast = null;
            C.PopClip(mask);
            C.Move(0, 0);
        }

        List<Cause> Listed() { var l = new List<Cause>(); foreach (var c in D.Causes) if (c.Level >= 0 && c.Level < 2 && l.Count < 3) l.Add(c); return l; }

        float Wanted()
        {
            if (Sub == 1) return GameH(); if (Sub == 2) return CleanH();
            if (Open != null) { Grp g; return S.ByKey.TryGetValue(Open, out g) && (Fixer.CanTune(g) || Fixer.CanRestart(g)) ? 552 : 440; }
            bool btn = Bad != null || (Done != null && Done.Acts.Count > 0);
            float y = 354 + (btn ? 62 : 0);
            return y + (Tab == 0 ? Math.Max(1, Math.Min(3, Listed().Count)) * 64 + 4 : 4 * 54) + 6 + 66;
        }

        void Front(float H)
        {
            var bad = Bad; var done = bad == null ? Done : null; float y = 20, x = 22, r = Wd - 22;
            string h1 = bad != null ? bad.Title : done != null ? done.Short : Tx("문제 없음", "No issues");
            if (bad != null && C.Measure(h1, F.Wide(27, 800)) > r - x - 50) h1 = bad.Short;
            C.Text(h1, F.Wide(27, 800), x, y + 24, bad != null ? T.AHot : T.AFg, 0, r - x - 50);
            string sub = bad != null ? bad.ShortWhy : done != null && !string.IsNullOrEmpty(done.ShortWhy) ? done.ShortWhy : Line();
            C.Text(sub, F.Wide(16, 500), x, y + 50, T.ADim, 0, r - x - 50);
            bool ex = Over("ex"); C.Round(r - 38, y, 38, 38, 12, ex ? T.ABtn : T.AWell); float cx = r - 19, cy = y + 19; C.Line(cx + 2, cy - 2, cx + 6, cy - 6, T.AFg, 2.2f); C.Line(cx + 1.5f, cy - 6, cx + 6.5f, cy - 6, T.AFg, 2.2f); C.Line(cx + 6, cy - 6.5f, cx + 6, cy - 1.5f, T.AFg, 2.2f); C.Line(cx - 2, cy + 2, cx - 6, cy + 6, T.AFg, 2.2f); C.Line(cx - 1.5f, cy + 6, cx - 6.5f, cy + 6, T.AFg, 2.2f); C.Line(cx - 6, cy + 6.5f, cx - 6, cy + 1.5f, T.AFg, 2.2f);
            Zone(r - 38, y, 38, 38, "ex", () => { if (OpenMain != null) OpenMain(); });
            y += 64;
            Cause act = bad ?? (done != null && done.Acts.Count > 0 ? done : null);
            if (act != null)
            {
                Act a = act.Acts.Find(z => !z.Id.StartsWith("open:")) ?? (act.Acts.Count > 0 ? act.Acts[0] : null);
                if (a != null) { string op = a.Id; Pill("big", Sure(op, Pre(act, a)), x, y - 2, r - x, 50, 15, bad != null ? T.AHot : T.ABtn, bad != null ? Col.Hex(0xffffff) : T.AFg, F.Wide(18, 800), () => Run(op)); }
                y += 60;
            }
            y += 3;
            Caps(x, y, r - x, 88, 7, 14); y += 96;
            float w1 = C.Mid("CPU", F.Wide(15.5f, 600), x, y + 14, T.ADim); C.Mid(S.CpuPct.ToString("0") + "%", F.Wide(20, 800), x + w1 + 7, y + 13, T.ALv(S.CpuPct, T.AFg)); C.Mid(Tx("코어 {0}개", "{0} cores", S.Phys), F.Wide(15.5f, 600), r, y + 14, T.ADim, 2); y += 37;
            float tw = (r - x - 8) / 2; Tank(Snap.Ram, x, y, tw, 60); Tank(Snap.Vm, x + tw + 8, y, tw, 60); y += 73;
            string[] t = { Tx("요약", "Summary"), "CPU", Txt.Title(Snap.Ram), Txt.Title(Snap.Vm) }; float sw = (r - x) / 4; C.Round(x + A("seg", Tab) * sw, y, sw, 36, 11, T.AWell);
            for (int i = 0; i < 4; i++) { int n = i; string id = "seg" + i; C.Mid(t[i], F.Wide(15.5f, 700), x + sw * i + sw / 2, y + 18, Tab == i || Over(id) ? T.AFg : T.ADim, 1); Zone(x + sw * i, y, sw, 36, id, () => { Tab = n; Redraw(); }); }
            y += 43;
            C.Clip(0, y - 2, Wd, Math.Max(1, H - y - 64));
            if (Tab == 0) Rows0(x, y, r); else Rows(Tab - 1, x, y, r, 4);
            C.Unclip();
            // 바로 하기
            string[] an = { busyTrim ? Tx("청소 중", "Cleaning") : Tx("램 청소", "Clean RAM"), Tx("임시 파일", "Temp files"), Tx("게임", "Games") }; float aw = (r - x - 16) / 3, ay = H - 64; C.Fill(x, ay - 9, r - x, 1, T.AWell);
            for (int i = 0; i < 3; i++) { int n = i; Pill("qa" + i, an[i], x + i * (aw + 8), ay, aw, 46, 14, i == 0 ? T.AFill : T.ABtn, i == 0 ? T.AOn : T.AFg, F.Wide(16, 800), () => { if (n == 0) Trim("trim"); else GoSub(n == 1 ? 2 : 1); }); }
        }
        bool busyTrim;
        void Trim(string op) { if (busyTrim) return; busyTrim = true; Redraw(); Job(() => { try { return D.Do(op); } finally { busyTrim = false; } }); }
        void Job(Func<string> f) { System.Threading.ThreadPool.QueueUserWorkItem(o => { string t = null; try { t = f(); } catch (Exception ex) { t = ex.Message; } if (!string.IsNullOrEmpty(t)) toastNext = t; Redraw(); }); }
        void GoSub(int n) { Sub = n; Open = null; if (n == 1) D.Boost.Look(0); if (n == 2 && !D.Temp.Busy) Job(() => { D.Temp.Scan(); return null; }); Redraw(); }

        float Back(string title, Action ex)
        {
            float x = 22, r = Wd - 22, y = 20; C.Round(x, y, 38, 38, 12, Over("bk") ? T.ABtn : T.AWell); C.Line(x + 22, y + 12, x + 15, y + 19, T.AFg, 2.2f); C.Line(x + 15, y + 19, x + 22, y + 26, T.AFg, 2.2f); Zone(x, y, 38, 38, "bk", () => { Open = null; Sub = 0; Redraw(); });
            C.Mid(title, F.Wide(24, 800), x + 52, y + 19, T.AFg); if (ex != null) { C.Mid(Tx("크게 보기", "Full view"), F.Wide(15, 700), r, y + 19, T.ADim.To(T.AFg, Over("big2") ? 1 : 0), 2); Zone(r - 80, y, 80, 38, "big2", ex); }
            return y + 56;
        }

        // ---- 게임 ----

        float GameH() { float y = 76; foreach (var g in D.Boost.Games) if (g.Installed) y += 30 + g.Knobs.Count * 48 + 8; return y + 2 * 46 + 22 + 52 + 20; }
        void GameSub(float H)
        {
            D.Boost.Look(3000); float x = 22, r = Wd - 22, y = Back(Tx("게임", "Games"), () => { if (OpenIn != null) OpenIn("game"); });
            foreach (var g in D.Boost.Games)
            {
                if (!g.Installed) continue; var l = D.Rounds.Of(g.Exe, 5); C.Mid(g.Label, F.Wide(17, 800), x, y + 12, T.AFg);
                if (l.Count > 0) { var m = Rounds.Mean(l); C.Mid(Tx("{0}fps, 끊김 10분당 {1}", "{0}fps, {1} stutters/10 min", m.Fps.ToString("0"), m.Per10.ToString("0.0")), F.Wide(14.5f, 600), r, y + 12, T.ADim, 2); } y += 30;
                for (int i = 0; i < g.Knobs.Count; i++)
                {
                    var k = g.Knobs[i]; float ox = PickA("pk" + g.Exe + i, k, r, y + 4); C.Mid(k.Label, F.Wide(15.5f, 600), x, y + 23, T.ADim, 0, ox - x - 8); y += 48;
                }
                y += 8;
            }
            Switch("sbg", Tx("게임 중 다른 프로그램 코어 {0}개만", "Other apps get {0} cores in game", Fixer.BgCores(S.Phys)), M.Set.Bg, x, r, y, () => { M.Set.Bg = !M.Set.Bg; M.Set.Save(); Job(() => { D.Fix.Tick(); return null; }); }); y += 46;
            Switch("stg", Tx("게임 시작할 때 램 청소", "Clean RAM at game start"), M.Set.AutoTrimGame, x, r, y, () => { M.Set.AutoTrimGame = !M.Set.AutoTrimGame; M.Set.Save(); }); y += 46 + 14;
            float bw = (r - x - 8) / 2; var st = F.Wide(16.5f, 800); Pill("gno", Tx("되돌리기", "Revert"), x, y, bw, 50, 15, T.ABtn, T.AFg, st, () => Job(() => D.ApplyGame(false))); Pill("gyes", Tx("권장 설정 적용", "Apply recommended"), x + bw + 8, y, bw, 50, 15, T.AHot, Col.Hex(0xffffff), st, () => Job(() => D.ApplyGame(true)));
        }
        float PickA(string id, Knob k, float r, float y)
        {
            var st = F.Wide(14.5f, 700); float tot = 0; var ws = new float[k.Options.Length]; for (int i = 0; i < ws.Length; i++) { ws[i] = C.Measure(k.Options[i], st) + 22; tot += ws[i]; }
            float x = r - tot, x0 = x; C.Round(x, y, tot, 38, 12, T.AWell); var ck = k as ClientKnob; int sel = ck != null && ck.Pending >= 0 ? ck.Pending : k.Now;
            for (int i = 0; i < ws.Length; i++)
            {
                string zid = id + "-" + i; int v = i; bool on = i == sel; if (on) C.Round(x, y, ws[i], 38, 12, T.AFill); C.Mid(k.Options[i], st, x + ws[i] / 2, y + 19, on ? T.AOn : Over(zid) ? T.AFg : T.ADim, 1);
                if (k.Now >= 0) Zone(x, y, ws[i], 38, zid, () => Job(() => D.Boost.Set(k, v) ?? (k is PrioKnob ? Tx("다음에 켤 때부터 적용", "Applies next launch") : k.Note.Length > 0 ? k.Note : null))); x += ws[i];
            }
            return x0;
        }
        void Switch(string id, string text, bool on, float x, float r, float y, Action flip)
        {
            float k = A("sw" + id, on ? 1 : 0); if (Over(id)) C.Round(x - 8, y, r - x + 16, 44, 13, T.ARow); C.Mid(text, F.Wide(15.5f, 600), x, y + 22, T.AFg, 0, r - x - 60);
            C.Round(r - 46, y + 9, 46, 26, 13, T.AWell.To(T.AHot, k)); C.Circle(r - 33 + 20 * k, y + 22, 9.5f, Col.Hex(0xffffff)); Zone(x - 8, y, r - x + 16, 44, id, () => { flip(); Redraw(); });
        }

        // ---- 임시 파일 ----

        float CleanH() { return 76 + 104 + 18 + D.Temp.Cats.Count * 46 + 16 + 52 + 20; }
        void CleanSub(float H)
        {
            float x = 22, r = Wd - 22, y = Back(Tx("청소", "Clean"), () => { if (OpenIn != null) OpenIn("clean"); });
            Tank(Snap.Ram, x, y, r - x, 60); y += 68; float bw = (r - x - 8) / 2; var st = F.Wide(15.5f, 800);
            Pill("cr1", busyTrim ? Tx("청소 중", "Cleaning") : Tx("램 청소", "Clean RAM"), x, y, S.Game == null && Cleaner.Elevated ? bw : r - x, 44, 14, T.AFill, T.AOn, st, () => Trim("trim")); if (S.Game == null && Cleaner.Elevated) Pill("cr2", Sure("trimall", Tx("캐시 전체 청소", "Clear cache")), x + bw + 8, y, bw, 44, 14, T.ABtn, T.AFg, st, () => { if (Sure("trimall", "") == "") { sure = "trimall"; sureAt = Environment.TickCount / 1000.0; Redraw(); } else { sure = null; Trim("trimall"); } }); y += 54;
            foreach (var c in D.Temp.Cats) { var cc = c; Switch("ct" + c.Key, c.Label, c.On, x, r, y, () => { cc.On = !cc.On; M.Set.TempOff.Remove(cc.Key); M.Set.TempOn.Remove(cc.Key); (cc.On ? M.Set.TempOn : M.Set.TempOff).Add(cc.Key); M.Set.Save(); }); C.Mid(D.Temp.Scanned ? Txt.Gb(c.Deletable / 1073741824.0) : Tx("확인 중", "Checking"), F.Wide(15, 700), r - 60, y + 22, T.ADim, 2); y += 46; }
            y += 12; long n = D.Temp.Scanned ? D.Temp.Total(true) : 0; string lab = D.Temp.Busy ? Tx("확인 중", "Checking") : n == 0 ? Tx("지울 것 없음", "Nothing to delete") : Sure("tempdel", Tx("임시 파일 {0} 삭제", "Delete {0} of temp files", Txt.Gb(n / 1073741824.0)));
            Pill("cd", lab, x, y, r - x, 50, 15, n > 0 ? T.AHot : T.ABtn, n > 0 ? Col.Hex(0xffffff) : T.ADim, F.Wide(16.5f, 800), () =>
            {
                if (n == 0 || D.Temp.Busy) return; if (Sure("tempdel", "") == "") { sure = "tempdel"; sureAt = Environment.TickCount / 1000.0; Redraw(); return; }
                sure = null; Job(() => { var t = D.Temp.Delete(); D.LastTemp = t; D.LastTempAt = DateTime.Now; D.Temp.Scan(); return Tx("{0} 삭제, {1}개", "Deleted {0}, {1} {2}", Txt.Gb(t.Bytes / 1073741824.0), t.Files, Pl(t.Files, "file", "files")) + (t.Failed > 0 ? Tx(" (못 지움 {0}개)", " ({0} skipped)", t.Failed) : ""); });
            });
        }
        /// <summary>단추에 무엇을 하는지 프로그램 이름까지</summary>
        string Pre(Cause c, Act a)
        {
            Grp g; if (!(a.Id.StartsWith("restart:") || a.Id.StartsWith("once:")) || !S.ByKey.TryGetValue(c.Key, out g) || a.Label.StartsWith(g.Label)) return a.Label;
            return g.Label + " " + a.Label;
        }
        string Line()
        {
            if (S.Game == null) return Tx("실행 중인 게임 없음", "No game running");
            string s = Tx("{0} {1}째", "{0} · {1}", S.Game.Label, Fmt.Dur(S.GameSec));
            if (D.FramesOk && D.Fps > 1) s += ", " + D.Fps.ToString("0") + "fps, " + (D.Hitch1m == 0 ? Tx("끊김 없음", "no stutters") : Tx("끊김 {0}번", "{0} {1}", D.Hitch1m, Pl(D.Hitch1m, "stutter", "stutters")));
            return s;
        }

        void Caps(float x, float y, float w, float h, float gap, float num)
        {
            int n = S.Phys; float cw = (w - gap * (n - 1)) / n, rad = Math.Min(14, cw / 2.6f); bool ov = Hover != null && Hover.StartsWith("cap");
            for (int i = 0; i < n; i++)
            {
                float cx = x + i * (cw + gap); var m = C.ClipRound(cx, y, cw, h, rad); C.Fill(cx, y, cw, h, T.AWell);
                float v = Soft("cap" + i, (float)Math.Max(num > 0 ? 27 : 8, S.Core[i]), 4), bh = h * v / 100; bool hot = Theme.Lv(S.Core[i]) == 2;
                C.Round(cx, y + h - bh, cw, bh + 12, 5, T.ALv(S.Core[i], T.AFill)); C.PopClip(m);
                if (num > 0) C.Mid((i + 1).ToString(), F.Wide(num, 800), cx + cw / 2, y + h - 13, hot ? Col.Hex(0xffffff) : T.AOn, 1);
                if (ov) C.Mid(S.Core[i].ToString("0") + "%", F.Wide(13.5f, 700), cx + cw / 2, y + 15, S.Core[i] > 78 ? T.AOn : T.ADim, 1);
                Zone(cx, y, cw + gap, h, "cap" + i, null);
            }
        }
        void Tank(int m, float x, float y, float w, float h)
        {
            double cap = S.Cap(m), used = S.Used(m), pc = used / Math.Max(1, cap) * 100; bool hot = Theme.Lv(pc) == 2;
            var mask = C.ClipRound(x, y, w, h, 15); C.Fill(x, y, w, h, T.AWell); float fw = A("tk" + m, (float)(used / Math.Max(1, cap)) * w); TankText(m, x, y, w, h, fw, T.ADim, T.AFg);
             C.Clip(x, y, fw, h); C.Fill(x, y, fw, h, T.ALv(pc, T.AFill)); var on = hot ? Col.Hex(0xffffff) : T.AOn; TankText(m, x, y, w, h, fw, on.Fade(.75f), on); C.Unclip(); C.PopClip(mask);
        }
        void TankText(int m, float x, float y, float w, float h, float fw, Col dim, Col fg)
        {
            C.Mid(Txt.Title(m), F.Wide(14.5f, 600), x + 14, y + h * .29f, dim); C.Mid(Txt.Pct(S, m), F.Wide(20, 800), x + 14, y + h * .68f, fg);
            string left = Txt.Left(S, m); var st = F.Wide(13.5f, 700); float r = x + w - 12, lw = Math.Max(C.Measure(left, st), C.Measure(Tx("남음", "free"), F.Wide(12.5f, 600)));
            if (x + fw > r - lw - 5 && x + fw < r + 5) r = x + fw - 9; // 물 높이에 글자가 걸리면 물 안으로
            C.Mid(Tx("남음", "free"), F.Wide(12.5f, 600), r, y + h * .30f, dim, 2); C.Mid(left, st, r, y + h * .70f, dim, 2);
        }

        void Rows0(float x, float y, float r)
        {
            var l = Listed();
            if (l.Count == 0) { C.Mid(Bad != null ? Tx("다른 항목 없음", "Nothing else") : Tx("손볼 것 없음", "Nothing to fix"), F.Wide(17, 700), Wd / 2, y + 34, T.ADim, 1); return; }
            for (int i = 0; i < l.Count; i++)
            {
                var c = l[i]; float ry = y + A("q" + c.Key, i * 64); string id = "q" + i, bid = "qb" + i; Grp g; S.ByKey.TryGetValue(c.Key, out g); string key = c.Key;
                if (Over(id)) C.Round(x - 8, ry, r - x + 16, 62, 14, T.ARow);
                Zone(x - 8, ry, r - x + 16, 62, id, g == null ? (Action)null : () => { Open = key; Redraw(); });
                Icon(g, c.Key, x, ry + 13, 36, T.AFg);
                float rx = r; var a = c.Acts.Find(z => z.Main);
                if (a != null) { string op = a.Id; string lab = Sure(op, a.Short); var st = F.Wide(15, 800); float bw = C.Measure(lab, st) + 26; rx -= bw; Pill(bid, lab, rx, ry + 13, bw, 36, 11, T.ABtn, T.AFg, st, () => Run(op)); rx -= 10; }
                else if (g != null) { C.Mid("›", F.Wide(20, 600), r - 4, ry + 30, T.ADim, 1); rx -= 16; }
                C.Text(c.Short, F.Wide(16.5f, 700), x + 49, ry + 27, T.AFg, 0, rx - x - 52); C.Text(c.ShortWhy, F.Wide(15, 500), x + 49, ry + 49, T.ADim, 0, rx - x - 52);
            }
        }

        void Rows(int m, float x, float y, float r, int n)
        {
            var l = new List<Grp>(); foreach (var g in S.Groups) if (S.Val(g, m) / S.Cap(m) > .004) l.Add(g);
            l.Sort((a, b) => S.Val(b, m).CompareTo(S.Val(a, m))); if (l.Count > n) l.RemoveRange(n, l.Count - n);
            for (int i = 0; i < l.Count; i++)
            {
                var g = l[i]; float ry = y + A("p" + m + g.Key, i * 54); string id = "p" + i; string key = g.Key; double v = S.Val(g, m); bool hot = Bad != null && Bad.Key == g.Key && Bad.Metric == m;
                if (Over(id)) C.Round(x - 8, ry, r - x + 16, 52, 14, T.ARow);
                Zone(x - 8, ry, r - x + 16, 52, id, () => { Open = key; Redraw(); });
                Icon(g, g.Key, x, ry + 8, 36, T.AFg);
                C.Mid("›", F.Wide(20, 600), r - 2, ry + 25, T.ADim, 1);
                string num = m == 0 ? v.ToString("0") : Txt.Num(v), u = m == 0 ? "%" : Txt.Unit(v); float uw = C.Measure(u, F.Wide(15, 700)), rx = r - 16; C.Mid(u, F.Wide(15, 700), rx, ry + 27, T.AFg, 2); float nw = C.Mid(num, F.Wide(20, 800), rx - uw - 1, ry + 26, T.AFg, 2); rx -= uw + nw + 14;
                C.Text(g.Label, F.Wide(17, 700), x + 49, ry + 26, T.AFg, 0, rx - x - 52); float bw = rx - x - 49; C.Round(x + 49, ry + 37, bw, 5, 2.5f, T.AWell); C.Round(x + 49, ry + 37, Math.Max(4, A("pb" + m + g.Key, (float)(v / S.Cap(m)) * bw)), 5, 2.5f, hot ? T.AHot : T.AFill);
            }
        }

        readonly float[] sv = new float[181]; P2[] pts = new P2[181];
        void Detail(float H)
        {
            string key = Open ?? lastOpen; if (key == null) return; lastOpen = key;
            Grp g; if (!S.ByKey.TryGetValue(key, out g)) { if (Open != null) { Open = null; Redraw(); } return; }
            float x = 22, r = Wd - 22, y = 20;
            C.Round(x, y, 38, 38, 12, Over("bk") ? T.ABtn : T.AWell); C.Line(x + 22, y + 12, x + 15, y + 19, T.AFg, 2.2f); C.Line(x + 15, y + 19, x + 22, y + 26, T.AFg, 2.2f); Zone(x, y, 38, 38, "bk", () => { Open = null; Redraw(); });
            y += 52; Icon(g, g.Key, x, y, 52, T.AFg); C.Mid(g.Label, F.Wide(27, 800), x + 66, y + 26, T.AFg, 0, r - x - 70); y += 72;
            int minute = S.Epoch / 60 - 1; lock (M.Sync) Series.Commit(g, minute - 180, minute, sv); Series.Bridge(sv, 181, 3); float mx = 1, first = -1; foreach (float v in sv) { if (v > mx) mx = v; if (first < 0 && v > 0) first = v; }
            double cur = g.Commit / 1048576.0; C.Mid(Txt.Title(Snap.Vm), F.Wide(15, 600), x, y + 6, T.ADim); float nw = C.Text(Fmt.Mem(cur), F.Wide(34, 800), x, y + 46, T.AFg);
            bool hot = Bad != null && Bad.Key == g.Key && Bad.Metric == Snap.Vm; C.Text(first < 0 || Math.Abs(cur - first) < 30 ? Tx("3시간 변화 없음", "No change in 3h") : Tx("3시간 {0}", "{0} in 3h", Fmt.Delta(cur - first)), F.Wide(17, 700), x + nw + 10, y + 46, hot ? T.AHot : T.ADim); y += 60;
            float gh = 92; int a0 = 0; while (a0 < 181 && sv[a0] < 0) a0++; int n = 181 - a0;
            if (n > 3) { for (int i = 0; i < n; i++) pts[i] = new P2((a0 + i) / 180f * Wd, y + gh - Math.Max(0, sv[a0 + i]) / (mx * 1.12f) * gh); var c = hot ? T.AHot : T.AFill; C.Area(pts, n, null, y + gh, c.Fade(.16f)); C.Path(pts, n, c, 2.6f, true); }
            y += gh + 16; C.Mid(History.Local((minute - 180) * 60.0).ToString("HH:mm"), F.Wide(15, 600), x, y, T.ADim); C.Mid(Tx("지금", "Now"), F.Wide(15, 600), r, y, T.ADim, 2); y += 24;
            C.Mid("CPU", F.Wide(15, 600), x, y + 6, T.ADim); C.Text(S.Val(g, 0).ToString("0") + "%", F.Wide(27, 800), x, y + 40, T.AFg); C.Mid(Txt.Title(Snap.Ram), F.Wide(15, 600), x + 184, y + 6, T.ADim); C.Text(Txt.Gb(S.Val(g, 1)), F.Wide(27, 800), x + 184, y + 40, T.AFg); y += 62;
            float by = H - 20; var st = F.Wide(17, 800);
            bool tune = Fixer.CanTune(g), re = Fixer.CanRestart(g), end = Fixer.CanEnd(g); string kk = key;
            if (re || end)
            {
                by -= 52; float bw = re && end ? (r - x - 8) / 2 : r - x, bx = x;
                if (end) { Pill("de", Sure("end:" + kk, Tx("종료", "Close")), bx, by, bw, 52, 15, T.ABtn, T.AFg, st, () => Run("end:" + kk)); bx += bw + 8; }
                if (re) { bool game = S.Game != null, bk = D.Booked(kk); string op = game ? (bk ? "unbook:" : "after:") + kk : "restart:" + kk; Pill("dr", Sure(op, game ? (bk ? Tx("예약 취소", "Cancel restart") : Tx("게임 후 재시작", "Restart after game")) : Tx("재시작", "Restart")), bx, by, bw, 52, 15, T.AHot, Col.Hex(0xffffff), game && !bk && end ? F.Wide(15, 800) : st, () => Run(op)); }
                by -= 8;
            }
            if (tune)
            {
                var rule = D.Fix.Find(kk); bool on = rule != null && rule.Any; by -= 52;
                Pill("dl", on ? Tx("제한 해제 ({0})", "Remove limit ({0})", rule.Text(S.Phys)) : Tx("게임할 때 코어 2개로 제한", "Limit to 2 cores in game"), x, by, r - x, 52, 15, T.ABtn, T.AFg, st, () => Run((on ? "undo:" : "always:") + kk));
            }
            else if (!re && !end) C.Mid(MainView.WhyNot(g), F.Wide(15.5f, 500), Wd / 2, by - 16, T.ADim, 1);
        }

    }

    /// <summary>트레이 아이콘에 마우스를 올리면 뜨는 것. 작은 창에서 결론, 코어, 메모리만.</summary>
    internal sealed class MiniView : Surface
    {
        public const float Wd = 300, Ht = 190, Pad = 30;
        public MiniView(Doctor d) : base(d) { }
        protected override void Paint(float w, float h)
        {
            C.Clear(new Col(0, 0, 0, 0));
            for (int i = 0; i < 9; i++) { float g = 2 + i * 2.5f; C.Round(Pad - g, Pad - g + 7, Wd + g * 2, Ht + g * 2 - 3, 20 + g, Col.Hex(0, .032f)); }
            C.Move(Pad, Pad);
            C.Round(0, 0, Wd, Ht, 20, T.ABg);
            Cause bad = null, done = null; foreach (var c in D.Causes) { if (c.Level == 2 && bad == null) bad = c; if (c.Level == -1 && done == null) done = c; }
            float x = 18, r = Wd - 18;
            C.Text(bad != null ? bad.Short : done != null ? done.Short : Tx("문제 없음", "No issues"), F.Wide(20, 800), x, 36, bad != null ? T.AHot : T.AFg, 0, r - x);
            string sub = bad != null ? bad.ShortWhy : done != null && !string.IsNullOrEmpty(done.ShortWhy) ? done.ShortWhy : S.Game == null ? Tx("실행 중인 게임 없음", "No game running") : Tx("{0} {1}째", "{0} · {1}", S.Game.Label, Fmt.Dur(S.GameSec)) + (D.FramesOk && D.Fps > 1 ? ", " + D.Fps.ToString("0") + "fps" : "");
            C.Text(sub, F.Wide(15, 500), x, 59, T.ADim, 0, r - x);
            int n = S.Phys; float gap = 5, cw = (r - x - 66 - gap * (n - 1)) / n;
            for (int i = 0; i < n; i++)
            {
                float cx = x + i * (cw + gap); var m = C.ClipRound(cx, 74, cw, 42, Math.Min(8, cw / 2.4f)); C.Fill(cx, 74, cw, 42, T.AWell); float bh = 42 * A("c" + i, (float)Math.Max(8, S.Core[i])) / 100; C.Round(cx, 116 - bh, cw, bh + 8, 3, T.ALv(S.Core[i], T.AFill)); C.PopClip(m);
            }
            C.Mid("CPU", F.Wide(13.5f, 600), r, 84, T.ADim, 2); C.Mid(S.CpuPct.ToString("0") + "%", F.Wide(20, 800), r, 105, T.ALv(S.CpuPct, T.AFg), 2);
            float tw = (r - x - 8) / 2; for (int k = 0; k < 2; k++)
            {
                int m = k == 0 ? Snap.Ram : Snap.Vm; float tx = x + k * (tw + 8), ty = 128, th = 44; double cap = S.Cap(m), used = S.Used(m), pc = used / Math.Max(1, cap) * 100; bool hot = Theme.Lv(pc) == 2;
                var mask = C.ClipRound(tx, ty, tw, th, 12); C.Fill(tx, ty, tw, th, T.AWell); Mem(m, tx, ty, used, T.ADim, T.AFg); float fw = A("t" + m, (float)(used / Math.Max(1, cap)) * tw); C.Clip(tx, ty, fw, th); C.Fill(tx, ty, fw, th, T.ALv(pc, T.AFill)); var on = hot ? Col.Hex(0xffffff) : T.AOn; Mem(m, tx, ty, used, on.Fade(.75f), on); C.Unclip(); C.PopClip(mask);
            }
            C.Move(0, 0);
        }
        void Mem(int m, float x, float y, double used, Col dim, Col fg)
        {
            C.Mid(Txt.Title(m), F.Wide(12.5f, 600), x + 11, y + 13, dim); C.Mid((used / Math.Max(1, S.Cap(m)) * 100).ToString("0") + "%", F.Wide(16, 800), x + 11, y + 31, fg);
        }
    }
}
