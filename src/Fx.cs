using System;
using System.Runtime.InteropServices;
using static LagWatch.Lang;

namespace LagWatch
{
    /// <summary>누른 것의 결과. 작업 스레드가 만들고 화면이 움직임으로 보여 준다.</summary>
    internal sealed class Outcome
    {
        public string Op = ""; public bool Ok = true;
        public R4 From;          // 누른 단추(창 좌표)
        public double Gain;      // 비운 양(GB)
        public double Was, Now;  // 램 사용 비율 전후
        public Grp G; public string Key;
    }

    /// <summary>누르는 순간, 하는 동안, 끝난 순간을 잠깐 보여 준다. 끝나면 아무것도 남지 않고 장수도 쓰지 않는다.</summary>
    internal abstract partial class Surface
    {
        protected string PressId; protected R4 PressR; float pressX, pressY; double pressAt = -9;
        string busyId; double busyAt; volatile Outcome outcome; Outcome play; double playAt;
        protected R4 RamBar, TempBox;   // 지금 화면에서 램 막대와 임시 파일 목록이 있는 자리(창 좌표)
        protected bool LastOk = true;
        /// <summary>0 이상이면 움직임을 그 시각에 멈춘다(그림 저장·시험용)</summary>
        public double FxTime = -1;

        [DllImport("user32.dll")] static extern bool SystemParametersInfoW(int a, int b, out int v, int f);
        static int motion = -1;
        /// <summary>윈도우에서 애니메이션을 껐으면 움직이지 않고 결과만 보여 준다.</summary>
        static bool Motion { get { if (motion < 0) { int v; motion = SystemParametersInfoW(0x1042, 0, out v, 0) && v == 0 ? 0 : 1; } return motion == 1; } }

        protected double Clock { get { return FxTime >= 0 ? FxTime : clock.Elapsed.TotalSeconds; } }
        bool Quiet { get { return (Still && FxTime < 0) || !Motion; } }
        const double Long = 3.2;
        static double Length(Outcome o) { return o.Op == "trim" || o.Op == "trimall" || o.Op == "tempdel" ? o.Gain >= .05 ? Long : 1.1 : 1.4; }
        /// <summary>움직이는 중이면 자주 그린다. 숫자만 떠 있는 동안은 아니다.</summary>
        public bool FxOn { get { return busyId != null || outcome != null || Clock - pressAt < .45 || play != null && (Clock - playAt < 1.4 || Clock - playAt > Length(play) - .45); } }

        void Pressed(Zone z, float x, float y) { PressId = z.Id; PressR = z.R; pressX = x; pressY = y; pressAt = Clock; }
        public void BusyOn(string id) { busyId = id; busyAt = Clock; Redraw(); }
        protected bool IsBusy(string id) { return busyId == id; }
        protected double BusyFor { get { return Clock - busyAt; } }
        protected void Finish(Outcome o) { outcome = o; Redraw(); }
        /// <summary>누른 자리가 없을 때(트레이 메뉴)</summary>
        public void Cheer(Outcome o) { outcome = o; Redraw(); }
        public void FxTest(Outcome o, string zone, double t) { foreach (var z in zones) if (z.Id == zone) o.From = z.R; play = o; playAt = 0; FxTime = t; pressAt = -9; }
        public void FxBusy(string zone, double t) { busyId = zone; busyAt = 0; FxTime = t; pressAt = -9; }

        /// <summary>조치를 작업 스레드에서 하고 결과를 넘긴다.</summary>
        protected void Fire(string id, string src, R4 from, Action<string> text)
        {
            string op = id.IndexOf(':') < 0 ? id : id.Substring(0, id.IndexOf(':')), key = id.IndexOf(':') < 0 ? "" : id.Substring(id.IndexOf(':') + 1); Grp g = null; if (key.Length > 0 && S != null) S.ByKey.TryGetValue(key, out g);
            if (op == "trim" || op == "trimall") BusyOn(src);
            System.Threading.ThreadPool.QueueUserWorkItem(x =>
            {
                string r; bool ok; try { r = D.Do(id); ok = !Doctor.Failed; } catch (Exception ex) { r = ex.Message; ok = false; }
                LastOk = ok; outcome = Made(op, key, g, from, ok); text(r); Redraw();
            });
        }
        Outcome Made(string op, string key, Grp g, R4 from, bool ok)
        {
            var o = new Outcome { Op = op, Key = key, G = g, From = from, Ok = ok };
            if (op == "trim" || op == "trimall") Fill(o, D.LastTrim, M.RamTotal);
            return o;
        }
        public static void Fill(Outcome o, CleanResult r, long total) { if (r == null || total <= 0) return; o.Gain = Math.Max(0, r.Gained / 1073741824.0); o.Was = 1 - (double)r.Before / total; o.Now = 1 - (double)r.After / total; }

        // ---- 그리기 ----

        static float Clamp(double v) { return v < 0 ? 0 : v > 1 ? 1 : (float)v; }
        static float Out(double v) { float k = 1 - Clamp(v); return 1 - k * k * k; }
        static float Back1(double v) { float k = Clamp(v) - 1; return 1 + 2.3f * k * k * k + 1.3f * k * k; }
        static float Rnd(int i, int s) { unchecked { uint x = (uint)(i * 374761393 + s * 668265263); x = (x ^ x >> 13) * 1274126177; return ((x ^ x >> 16) & 0xffff) / 65535f; } }
        protected virtual Col FxInk { get { return T.Fg; } }
        protected virtual Col FxChip { get { return T.Btn; } }
        protected virtual Col FxChipInk { get { return T.Bfg; } }

        /// <summary>단추 위의 물결. 단추를 그린 뒤에 부른다.</summary>
        void Ripple(string id, float x, float y, float w, float h, float rad, Col fg)
        {
            double t = Clock - pressAt; if (PressId != id || t < 0 || t > .42 || Quiet) return;
            float k = Out(t / .42), r = k * ((float)Math.Sqrt(w * w + h * h) + 4); var m = C.ClipRound(x, y, w, h, rad); C.Circle(pressX - C.Ox, pressY - C.Oy, r, fg.Fade(.24f * (1 - k))); C.PopClip(m); Animating = true;
        }
        /// <summary>누르는 동안 살짝 들어간다</summary>
        float Squash(string id) { double t = Clock - pressAt; if (PressId != id || t < 0 || t > .2 || Quiet) return 0; Animating = true; return (float)Math.Sin(Math.PI * t / .2) * .045f; }

        /// <summary>하는 중: 로켓에 불이 붙은 채 떨고 있다</summary>
        bool Fuel(string id, string text, float x, float y, float w, float h, Col fg, Style st)
        {
            if (busyId != id || Quiet) return false;
            double t = Clock - busyAt; if (t > 120) { busyId = null; return false; } float tw = C.Measure(text, st), rs = Math.Min(26, h * .52f), need = rs * .8f + 9 + tw; bool both = need + 18 <= w; float gx = x + w / 2 - (both ? need : rs * .8f) / 2;
            Rocket(gx + rs * .4f + (float)Math.Sin(t * 57) * .9f, y + h / 2 + rs * .34f + (float)Math.Sin(t * 43) * .5f, rs, .34f + .1f * (float)Math.Sin(t * 31), t, fg, T.AHot, fg.Lum > .5f ? Col.Hex(0x247592) : Col.Hex(0x8fdcdb));
            if (both) C.Mid(text, st, gx + rs * .8f + 9, y + h / 2, fg); Animating = true; return true;
        }

        readonly P2[] rk = new P2[64];
        /// <summary>위를 보는 로켓. (cx, by) 가 밑동 가운데, ht 가 키</summary>
        protected void Rocket(float cx, float by, float ht, float flame, double t, Col body, Col hot, Col glass)
        {
            float top = by - ht; int n = 0;
            for (int s = 0; s < 2; s++) // 날개
            {
                float d = s == 0 ? 1 : -1; rk[0] = new P2(cx + d * .15f * ht, top + .60f * ht); rk[1] = new P2(cx + d * .41f * ht, top + .88f * ht); rk[2] = new P2(cx + d * .40f * ht, top + 1.06f * ht); rk[3] = new P2(cx + d * .15f * ht, top + .95f * ht); C.Poly(rk, 4, hot);
            }
            if (flame > 0) for (int s = 0; s < 2; s++) // 불꽃
            {
                float L = flame * ht * (s == 0 ? 1 : .58f) * (1 + .16f * (float)Math.Sin(t * 61 + s)), wd = ht * (s == 0 ? .15f : .08f), y0 = by - .01f * ht; n = 0;
                for (int i = 0; i <= 9; i++) { float u = i / 9f; rk[n++] = new P2(cx + wd * (1 - u) * (1 + 1.1f * u), y0 + u * L); }
                for (int i = 9; i >= 0; i--) { float u = i / 9f; rk[n++] = new P2(cx - wd * (1 - u) * (1 + 1.1f * u), y0 + u * L); }
                C.Poly(rk, n, s == 0 ? Col.Hex(0xff8f2e) : Col.Hex(0xffe08a));
            }
            n = 0; for (int i = 0; i <= 16; i++) { float v = i / 16f; rk[n++] = new P2(cx + Wide(v) * ht, top + v * ht); }
            for (int i = 16; i >= 0; i--) { float v = i / 16f; rk[n++] = new P2(cx - Wide(v) * ht, top + v * ht); }
            C.Poly(rk, n, body);
            n = 0; for (int i = 0; i <= 6; i++) { float v = i / 6f * .25f; rk[n++] = new P2(cx + Wide(v) * ht, top + v * ht); }
            for (int i = 6; i >= 0; i--) { float v = i / 6f * .25f; rk[n++] = new P2(cx - Wide(v) * ht, top + v * ht); }
            C.Poly(rk, n, hot);
            C.Circle(cx, top + .46f * ht, .105f * ht, glass);
        }
        static float Wide(float v) { return .235f * (float)Math.Pow(Math.Sin(Math.PI * (.045 + .66 * v)), .72); }

        /// <summary>맨 마지막에 부른다. w, h 는 지금 좌표계에서 보이는 크기</summary>
        protected void PaintFx(float w, float h)
        {
            var o = outcome; if (o != null) { outcome = null; busyId = null; play = o; playAt = Clock; LastOk = o.Ok; }
            if (play == null) return;
            o = play; double t = Clock - playAt; if (t >= Length(o) || t < 0) { play = null; return; }
            if (!Quiet) Animating = true;
            float fx = (o.From.L + o.From.R) / 2 - C.Ox, fy = o.From.T - C.Oy, fb = o.From.B - C.Oy; if (o.From.W < 1) { fx = w / 2; fy = fb = h; }
            if (o.Op == "trim" || o.Op == "trimall" || o.Op == "tempdel")
            {
                if (!o.Ok) { Mark(fx, (fy + fb) / 2, t, false); return; }
                bool temp = o.Op == "tempdel"; var box = temp ? TempBox : RamBar; float bl = box.L - C.Ox, bt = box.T - C.Oy, bw = box.W, bh = box.B - box.T;
                if (o.Gain >= .05)
                {
                    if (!Quiet) { if (bw > 4) Blocks(o, t, temp, bl, bt, bw, bh, fx); Launch(fx, fy, t, h); }
                    Chip(o, t, bw > 4, bl, bt, bw, bh, fx, fy, w);
                }
                else if (!Quiet) Hop(fx, fy, t);
                return;
            }
            if (Quiet) return;
            if (!o.Ok) { Mark(fx, (fy + fb) / 2, t, false); return; }
            if (o.Op == "end" || o.Op == "restart") { Whoosh(o, fx, (fy + fb) / 2, t); return; }
            if (o.Op == "power" || o.Op == "deep" || o.Op == "") return;
            Mark(fx, (fy + fb) / 2, t, true);
        }

        const float Lift = .14f, Fly = .78f;
        static float Height(double t, float dist) { float p = Clamp((t - Lift) / Fly); return dist * (float)Math.Pow(p, 2.1); }
        void Launch(float x, float y, double t, float h)
        {
            float size = h < 260 ? 30 : 44, dist = y + size + 60; var smoke = FxInk;
            for (int i = 0; i < 18; i++) // 바닥에 퍼지는 연기
            {
                double a = t - .03 - Rnd(i, 1) * .1; if (a < 0 || a > .75) continue; float k = Out(a / .75), d = (i % 2 == 0 ? 1 : -1) * (10 + 74 * Rnd(i, 2) * k); C.Circle(x + d, y - 2 - 18 * Rnd(i, 3) * k, 4 + 15 * k * (.5f + .5f * Rnd(i, 10)), smoke.Fade(.1f * (1 - (float)(a / .75))));
            }
            for (int i = 0; i < 64; i++) // 지나간 자리
            {
                double te = Lift + Fly * Math.Pow(i / 64.0, 1 / 2.1), a = t - te; if (a < 0) break; if (a > .8 || te > Lift + Fly) continue; float ry = y - Height(te, dist), k = (float)(a / .8);
                C.Circle(x + (Rnd(i, 4) - .5f) * (5 + 26 * k), ry + size * .2f + 26 * k, 3.5f + 19 * (float)Math.Sqrt(k) * (.6f + .4f * Rnd(i, 11)), smoke.Fade(.1f * (1 - k) * (float)Math.Sqrt(Math.Min(1, i / 8f + .3f))));
            }
            if (t < Lift + Fly + .1) { float hop = t < Lift ? 3 * (float)Math.Sin(Math.PI * t / Lift) : 0, sway = (float)Math.Sin(t * 11) * 2.2f * Clamp(1 - (t - Lift) / Fly); Rocket(x + sway, y - 4 + hop - Height(t, dist), size, .5f + .75f * Clamp((t - Lift) / .4), t, FxInk, T.AHot, T.W[1]); }
        }
        /// <summary>비울 것이 없었을 때는 뛰었다가 내려앉는다</summary>
        void Hop(float x, float y, double t)
        {
            float k = Clamp(t / .5); for (int i = 0; i < 5; i++) { float a = Clamp((t - .05) / .6); C.Circle(x + (i - 2) * (6 + 12 * a), y - 1, 3 + 6 * a, FxInk.Fade(.2f * (1 - a))); }
            if (t < .9) { C.PushOpacity(1 - Clamp((t - .6) / .3)); Rocket(x, y - 4 - 16 * (float)Math.Sin(Math.PI * k), 30, k < .5f ? .4f : 0, t, FxInk, T.AHot, T.W[1]); C.PopClip(IntPtr.Zero); }
        }
        /// <summary>비운 만큼 막대에서 떨어져 나간다</summary>
        void Blocks(Outcome o, double t, bool temp, float l, float tp, float w, float h, float to)
        {
            float a = temp ? l : l + w * Clamp(o.Now), b = temp ? l + w : l + w * Clamp(o.Was); if (b - a < 8) { float m = (a + b) / 2; a = m - 10; b = m + 10; }
            var c = temp ? T.W[1] : T.LvFill(o.Was * 100, T.W[1]);
            if (!temp && t < .8) { float k = Out(t / .8); C.Fill(a, tp, (b - a) * (1 - k), h, c.To(Col.Hex(0xffffff), .35f).Fade(1 - k * .6f)); }
            for (int i = 0; i < 26; i++)
            {
                double at = t - .06 - Rnd(i, 5) * .34; if (at < 0 || at > 1) continue; float u = (float)at, up = u * u; float bx = a + (b - a) * Rnd(i, 6), by = tp + h * (.1f + .8f * Rnd(i, 7));
                float x = bx + (Rnd(i, 8) - .5f) * 46 * u + (to - bx) * .18f * up, y = by - 34 * u - 250 * up, s = (6 + 6 * Rnd(i, 9)) * (1 - .45f * u); C.Round(x - s / 2, y - s / 2, s, s, s * .28f, c.Fade(1 - up));
            }
        }
        /// <summary>비운 양</summary>
        void Chip(Outcome o, double t, bool bar, float l, float tp, float w, float h, float fx, float fy, float vw)
        {
            double a = t - (Quiet ? 0 : .2); if (a < 0) return; float k = Quiet ? 1 : Back1(a / .34), show = Quiet ? 1 : Clamp(a / .12) * (1 - Clamp((t - (Long - .4)) / .4));
            double v = o.Gain * (Quiet ? 1 : Out(a / .8)); string num = v.ToString(o.Gain >= .95 ? "0.0" : "0.00") + "GB", what = o.Op == "tempdel" ? Tx("삭제", "deleted") : Tx("확보", "freed"); bool small = vw < 460; var ns = F.Din(small ? 25 : 31); var ts = small ? F.Wide(14.5f, 700) : F.Sans(16, 700);
            float nw = C.Measure(num, ns), cw = nw + 7 + C.Measure(what, ts) + 34, ch = small ? 40 : 46, cx = bar ? (w < 150 ? l + w / 2 : l + w - 6 - cw / 2) : fx, cy = bar ? (o.Op == "tempdel" ? tp + 40 : w < 150 ? tp - 16 : tp + h / 2) : fy - 46;
            cx = Math.Max(cw / 2 + 6, Math.Min(vw - cw / 2 - 6, cx)); cy += (1 - k) * 22; if (show <= 0) return;
            if (show < 1) C.PushOpacity(show);
            for (int i = 0; i < 4; i++) C.Round(cx - cw / 2 - i, cy - ch / 2 + 2 - i, cw + i * 2, ch + i * 2 + 2, ch / 2 + i, Col.Hex(0, .07f));
            C.Round(cx - cw / 2, cy - ch / 2, cw, ch, ch / 2, FxChip); C.Mid(num, ns, cx - cw / 2 + 17, cy + 1, FxChipInk); C.Mid(what, ts, cx - cw / 2 + 17 + nw + 7, cy, FxChipInk.Fade(.8f));
            if (show < 1) C.PopClip(IntPtr.Zero);
        }
        /// <summary>됐으면 체크, 안 됐으면 흔들리는 가위표</summary>
        void Mark(float x, float y, double t, bool ok)
        {
            var c = ok ? T.AOk : T.ARed; float k = Back1(t / .26), gone = 1 - Clamp((t - .95) / .35), r = 19 * k; if (!ok) x += (float)Math.Sin(t * 46) * 7 * Clamp(1 - t / .42); if (gone <= 0) return;
            C.PushOpacity(gone);
            if (ok) { float e = Out(t / .5); for (int i = 0; i < 10; i++) { double g = i * Math.PI / 5 + .3; float d = 18 + 30 * e, s = 3.4f * (1 - e) + .4f; C.Circle(x + (float)Math.Cos(g) * d, y + (float)Math.Sin(g) * d, s, i % 2 == 0 ? c : T.Sand); } }
            C.Circle(x, y, Math.Max(1, r), c); var w = Col.Hex(0xffffff); float p = Clamp((t - .12) / .22);
            if (ok) { float a = Clamp(p / .4f), b = Clamp((p - .4f) / .6f); if (a > 0) C.Line(x - 8, y + .5f, x - 8 + 6 * a, y + .5f + 6 * a, w, 3.2f); if (b > 0) C.Line(x - 2, y + 6.5f, x - 2 + 10.5f * b, y + 6.5f - 12.5f * b, w, 3.2f); }
            else if (p > 0) { C.Line(x - 6 * p, y - 6 * p, x + 6 * p, y + 6 * p, w, 3.2f); C.Line(x + 6 * p, y - 6 * p, x - 6 * p, y + 6 * p, w, 3.2f); }
            C.PopClip(IntPtr.Zero);
        }
        /// <summary>끄면 아이콘이 날아가고, 재시작이면 새것이 올라온다</summary>
        void Whoosh(Outcome o, float x, float y, double t)
        {
            float u = Clamp(t / .55), up = u * u * u; const float s = 38;
            if (u < 1) { for (int i = 0; i < 4; i++) { float lag = Clamp(u - .05f * (i + 1)); C.Fill(x - 8 + i * 5, y - 130 * lag * lag * lag + s * .6f, 2.4f, 26 * u, FxInk.Fade(.2f * (1 - u))); } C.PushOpacity(1 - up * .9f); Icon(o.G, o.Key, x - s / 2, y - s / 2 - 130 * up, s, FxInk); C.PopClip(IntPtr.Zero); }
            if (o.Op == "restart" && t > .5) { float k = Back1((t - .5) / .4), gone = 1 - Clamp((t - 1.1) / .3); C.PushOpacity(Clamp((t - .5) / .15) * gone); Icon(o.G, o.Key, x - s / 2, y - s / 2 + (1 - k) * 30, s, FxInk); C.PopClip(IntPtr.Zero); }
        }

        /// <summary>아래에서 올라왔다 사라지는 알림. 됐는지도 그린다.</summary>
        protected bool ToastBox(string text, double age, double life, float cx, float cy, float h, Style st, Col bg, Col fg, bool ok)
        {
            if (age > life) return false; bool still = Quiet; float k = still ? 1 : Back1(age / .3), show = still ? 1 : Clamp(age / .12) * (1 - Clamp((age - life + .3) / .3)); if (show <= 0) return true;
            float r = h * .25f, tw = C.Measure(text, st) + h * .95f + r, x = cx - tw / 2; cy += (1 - k) * 26; if (!still && (age < .5 || age > life - .5)) Animating = true;
            if (show < 1) C.PushOpacity(show);
            C.Round(x, cy - h / 2, tw, h, h / 2, bg); var c = ok ? T.AOk : T.ARed; float ix = x + h * .24f + r, w = r * .24f; C.Circle(ix, cy, r, c); var wh = Col.Hex(0xffffff);
            if (ok) { C.Line(ix - r * .42f, cy + r * .02f, ix - r * .1f, cy + r * .34f, wh, w); C.Line(ix - r * .1f, cy + r * .34f, ix + r * .45f, cy - r * .32f, wh, w); } else { C.Line(ix - r * .33f, cy - r * .33f, ix + r * .33f, cy + r * .33f, wh, w); C.Line(ix + r * .33f, cy - r * .33f, ix - r * .33f, cy + r * .33f, wh, w); }
            C.Mid(text, st, ix + r + h * .2f, cy, fg);
            if (show < 1) C.PopClip(IntPtr.Zero);
            return true;
        }
    }
}
