using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using static LagWatch.Lang;

namespace LagWatch
{
    internal sealed class Theme
    {
        public bool Dark;
        public Col Bg, Warn, Hot, WarnInk, HotInk, AWarn, ARed, S1, S2, S3, R1, R2, R3, Fg, Dim, Hair, Faint, Win, Rest, Sand, Red, Btn, Bfg, Glass, Sheet, Edge, Deep;
        public Col[] W;
        public Col ABg, AFg, ADim, AWell, AFill, AOn, ARow, ABtn, AHot, AOk;

        static Col H(int v, float a = 1) { return Col.Hex(v, a); }
        public static readonly Theme Night = new Theme
        {
            Dark = true, Bg = H(0x0b1222), Warn = H(0xff8f2e), Hot = H(0xe8372c), WarnInk = H(0xffa04a), HotInk = H(0xff5a4f), AWarn = H(0xff9f0a), ARed = H(0xff453a), S1 = H(0x050913), S2 = H(0x0e1932), S3 = H(0x254069), R1 = H(0x1c0408), R2 = H(0x560c11), R3 = H(0x8f1c18),
            Fg = H(0xffffff), Dim = H(0xffffff, .72f), Hair = H(0xffffff, .3f), Faint = H(0xffffff, .16f),
            W = new[] { H(0x8fdcdb), H(0x5cbcc6), H(0x3798ae), H(0x247592), H(0x1b5a78) }, Win = H(0x174a67), Rest = H(0x113650), Sand = H(0xe2a83a), Red = H(0xe8372c),
            Btn = H(0xffffff), Bfg = H(0x0b1424), Glass = H(0xffffff, .15f), Sheet = H(0x101c33), Edge = H(0x040c18, .55f), Deep = H(0x040b16),
            ABg = H(0x17191c), AFg = H(0xf4f5f6), ADim = H(0xa3a9b0), AWell = H(0x26292d), AFill = H(0xf4f5f6), AOn = H(0x14161a), ARow = H(0x212428), ABtn = H(0x3a3e44), AHot = H(0xff5b2e), AOk = H(0x39b57a)
        };
        public static readonly Theme Day = new Theme
        {
            Dark = false, Bg = H(0xf1f5f8), Warn = H(0xf08a24), Hot = H(0xdf3227), WarnInk = H(0xb8520b), HotInk = H(0xb42318), AWarn = H(0xe8820c), ARed = H(0xe5281c), S1 = H(0x86c5e3), S2 = H(0xc3e4ee), S3 = H(0xf4ecd8), R1 = H(0xff8f7a), R2 = H(0xffc0a6), R3 = H(0xffe9d2),
            Fg = H(0x08202f), Dim = H(0x08202f, .74f), Hair = H(0x08202f, .4f), Faint = H(0x08202f, .14f),
            W = new[] { H(0x7fcfd2), H(0x52b3bf), H(0x3296ab), H(0x217a91), H(0x186177) }, Win = H(0x1d5f7c), Rest = H(0x164c66), Sand = H(0xe6ad3c), Red = H(0xdf3227),
            Btn = H(0x08202f), Bfg = H(0xffffff), Glass = H(0xffffff, .55f), Sheet = H(0xf6fafb), Edge = H(0xffffff, .6f), Deep = H(0x0c3a50),
            ABg = H(0xffffff), AFg = H(0x14161a), ADim = H(0x666c73), AWell = H(0xeceef0), AFill = H(0x14161a), AOn = H(0xffffff), ARow = H(0xf3f4f6), ABtn = H(0xdfe2e5), AHot = H(0xf04c1c), AOk = H(0x1f9a5e)
        };
        public static Theme Cur = Night;
        public static void FromWindows()
        {
            try { object v = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1); Cur = v is int && (int)v == 0 ? Night : Day; }
            catch (Exception) { Cur = Night; }
        }
        /// <summary>사용률 단계: 70% 넘으면 1(주황), 90% 넘으면 2(빨강)</summary>
        public static int Lv(double pct) { return pct >= 90 ? 2 : pct >= 70 ? 1 : 0; }
        public Col LvFill(double pct, Col normal) { int l = Lv(pct); return l == 2 ? Hot : l == 1 ? Warn : normal; }
        public Col LvInk(double pct, Col normal) { int l = Lv(pct); return l == 2 ? HotInk : l == 1 ? WarnInk : normal; }
        public Col ALv(double pct, Col normal) { int l = Lv(pct); return l == 2 ? ARed : l == 1 ? AWarn : normal; }
        /// <summary>프로그램 하나가 전체의 15% 넘게 쓰면 1, 30% 넘으면 2</summary>
        public static int Share(double pct) { return pct >= 30 ? 2 : pct >= 15 ? 1 : 0; }
        public Col Ink(Col bg) { return bg.Lum > .27f ? H(0x04202b) : H(0xffffff); }
        public Col Of(Part p) { return p.Role == 0 ? Sand : p.Role == 1 ? Rest : p.Role == 2 ? Win : p.Role == 4 ? Red : W[Math.Min(4, p.Rank)]; }
    }

    internal static class F
    {
        static readonly Dictionary<int, Style> sans = new Dictionary<int, Style>(), wide = new Dictionary<int, Style>(), din = new Dictionary<int, Style>();
        public static Style Sans(float size, int weight) { int k = (int)(size * 10) * 1000 + weight; Style s; if (!sans.TryGetValue(k, out s)) sans[k] = s = new Style("LW Sans", weight, size, size >= 36 ? -.045f : size >= 22 ? -.035f : -.022f); return s; }
        public static Style Wide(float size, int weight) { int k = (int)(size * 10) * 1000 + weight; Style s; if (!wide.TryGetValue(k, out s)) wide[k] = s = new Style("LW Wide", weight, size, size >= 24 ? -.035f : -.02f); return s; }
        public static Style Din(float size) { int k = (int)(size * 10); Style s; if (!din.TryGetValue(k, out s)) din[k] = s = new Style("Bahnschrift", 600, size, 0, 4, false); return s; }
    }

    internal static class Txt
    {
        static readonly System.Globalization.CultureInfo C = System.Globalization.CultureInfo.InvariantCulture;
        public static string Num(double gb) { return gb >= .95 ? gb.ToString("0.0", C) : (gb * 1024).ToString("0", C); }
        public static string Unit(double gb) { return gb >= .95 ? "GB" : "MB"; }
        public static string Gb(double gb) { return Num(gb) + Unit(gb); }
        public static string Val(int m, double v) { return m == Snap.Cpu ? v.ToString("0", C) + "%" : Gb(v); }
        /// <summary>사용률</summary>
        public static string Pct(Snap s, int m) { double cap = s.Cap(m); return (cap > 0 ? s.Used(m) / cap * 100 : 0).ToString("0", C) + "%"; }
        /// <summary>남은 양</summary>
        public static string Left(Snap s, int m) { return m == Snap.Cpu ? (100 - Math.Round(s.Used(m), MidpointRounding.AwayFromZero)).ToString("0", C) + "%" : s.Left(m).ToString("0.0", C) + "GB"; }
        /// <summary>쓴 양 / 전체</summary>
        public static string Of(Snap s, int m) { return s.Used(m).ToString("0.0", C) + " / " + s.Cap(m).ToString(m == Snap.Ram ? "0" : "0.0", C) + "GB"; }
        public static string Title(int m) { return m == Snap.Cpu ? "CPU" : m == Snap.Ram ? Tx("램", "RAM") : Tx("가상메모리", "Commit"); }
        public static string Clock(int epoch) { return History.Local(epoch).ToString("HH:mm"); }
    }

    /// <summary>프로그램 아이콘. 파일에서 64px로 뽑아 일정 개수만 들고 있는다.</summary>
    internal static class Icons
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint PrivateExtractIconsW(string f, int i, int cx, int cy, IntPtr[] h, uint[] id, uint n, uint fl);
        public const int Px = 64, Cap = 64;
        static readonly Dictionary<string, byte[]> map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> wait = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly object gate = new object();
        static readonly byte[] None = new byte[0];
        public static Action Ready;

        /// <summary>없으면 뒤에서 읽어 오고 일단 null</summary>
        public static byte[] Get(string path)
        {
            if (path == null) return null;
            lock (gate)
            {
                byte[] b; if (map.TryGetValue(path, out b)) return b == None ? null : b;
                if (!wait.Add(path)) return null;
            }
            if (System.Threading.Interlocked.Exchange(ref busy, 1) == 0) ThreadPool.QueueUserWorkItem(o => Work());
            return null;
        }
        /// <summary>스레드 하나가 차례로 읽는다.</summary>
        static void Work()
        {
            while (true)
            {
                string path = null; lock (gate) { foreach (var p in wait) { path = p; break; } if (path == null) { busy = 0; return; } }
                byte[] b = null; try { b = Load(path); } catch (Exception) { }
                lock (gate) { if (map.Count >= Cap) map.Clear(); map[path] = b ?? None; wait.Remove(path); }
                var r = Ready; if (r != null) r();
            }
        }
        static int busy;
        public static void Clear() { lock (gate) map.Clear(); }
        public static int Count { get { lock (gate) return map.Count; } }

        /// <summary>기다리지 않고 바로(그림 저장용)</summary>
        public static void Now(string path) { if (path == null) return; byte[] b = null; try { b = Load(path); } catch (Exception) { } lock (gate) map[path] = b ?? None; }

        static byte[] Load(string path)
        {
            var h = new IntPtr[1]; var id = new uint[1];
            if (PrivateExtractIconsW(path, 0, Px, Px, h, id, 1, 0) < 1 || h[0] == IntPtr.Zero) return null;
            try { return Pixels(h[0], Px); }
            finally { Native.DestroyIcon(h[0]); }
        }
        /// <summary>아이콘을 미리 곱한 BGRA 로. 투명도가 없는 옛 아이콘은 가림판을 쓴다.</summary>
        static unsafe byte[] Pixels(IntPtr icon, int n)
        {
            Win.ICONINFO ii; if (!Win.GetIconInfo(icon, out ii)) return null; IntPtr dc = Win.GetDC(IntPtr.Zero);
            try
            {
                if (ii.Color == IntPtr.Zero) return null; var bi = new D2.BITMAPINFO { Size = 40, W = n, H = -n, Planes = 1, Bits = 32 }; var px = new byte[n * n * 4]; bool any = false;
                fixed (byte* p = px)
                {
                    if (Win.GetDIBits(dc, ii.Color, 0, n, p, ref bi, 0) != n) return null;
                    for (int i = 3; i < px.Length && !any; i += 4) any = p[i] != 0;
                    if (!any) { var mk = new byte[px.Length]; fixed (byte* q = mk) { bi = new D2.BITMAPINFO { Size = 40, W = n, H = -n, Planes = 1, Bits = 32 }; Win.GetDIBits(dc, ii.Mask, 0, n, q, ref bi, 0); for (int i = 0; i < px.Length; i += 4) p[i + 3] = (byte)(q[i] == 0 ? 255 : 0); } }
                    for (int i = 0; i < px.Length; i += 4) { int a = p[i + 3]; p[i] = (byte)(p[i] * a / 255); p[i + 1] = (byte)(p[i + 1] * a / 255); p[i + 2] = (byte)(p[i + 2] * a / 255); }
                }
                return px;
            }
            finally { Win.ReleaseDC(IntPtr.Zero, dc); if (ii.Color != IntPtr.Zero) D2.DeleteObject(ii.Color); if (ii.Mask != IntPtr.Zero) D2.DeleteObject(ii.Mask); }
        }
    }

    internal struct Zone { public R4 R; public string Id; public Action Click; public Action<float, float> Move; }

    /// <summary>그리기·누르기·움직임의 바탕. 화면마다 하나씩.</summary>
    internal abstract class Surface : IDisposable
    {
        public readonly Canvas C = new Canvas();
        public readonly Doctor D;
        public readonly Monitor M;
        public Snap S;
        protected Theme T { get { return Theme.Cur; } }
        protected readonly List<Zone> zones = new List<Zone>(64);
        public float Mx = -1, My = -1;
        public string Hover;
        public bool Animating;
        /// <summary>게임 중이거나 그림 저장이면 바로 끝값으로</summary>
        public bool Still;
        /// <summary>만지지 않을 때. 적은 장수로 끝낸다</summary>
        public bool Calm;
        public Action Dirty;
        readonly Dictionary<string, float> anim = new Dictionary<string, float>();
        readonly Dictionary<string, int> seen = new Dictionary<string, int>();
        int frame; double lastAt; readonly Stopwatch clock = Stopwatch.StartNew(); float step = 1;

        protected Surface(Doctor d) { D = d; M = d.M; }

        /// <summary>그 자원의 사용률(%)</summary>
        protected double Pct(int m) { double cap = S.Cap(m); return cap > 0 ? S.Used(m) / cap * 100 : 0; }
        public void Refresh() { S = Snap.Take(M); foreach (var g in S.Groups) { var a = g.Main; if (a != null && !a.InfoDone && (g.Commit > 120L << 20 || g.Cores > 0.05)) lock (M.Sync) M.EnsureInfo(a); } }

        public void Draw(int pxW, int pxH, float scale)
        {
            C.Resize(pxW, pxH, scale);
            if (S == null) Refresh();
            double now = clock.Elapsed.TotalSeconds, dt = Math.Min(0.1, now - lastAt); lastAt = now;
            step = Still ? 1 : (float)(1 - Math.Exp(-dt / (Calm ? 0.045 : 0.085)));
            Animating = false; frame++;
            zones.Clear();
            for (int i = 0; i < 2; i++) { C.Begin(); Paint(C.W, C.H); if (C.End()) break; zones.Clear(); }
            if (frame % 240 == 0) { var old = new List<string>(); foreach (var kv in seen) if (frame - kv.Value > 480) old.Add(kv.Key); foreach (var k in old) { seen.Remove(k); anim.Remove(k); } }
            FindHover();
        }
        protected abstract void Paint(float w, float h);

        /// <summary>목표값으로 부드럽게</summary>
        protected float A(string key, float target)
        {
            float v; seen[key] = frame;
            if (!anim.TryGetValue(key, out v) || Still || float.IsNaN(v)) { anim[key] = target; return target; }
            float d = target - v;
            float ad = Math.Abs(d);
            if (ad < 0.3f && ad < Math.Abs(target) * 0.004f + 0.003f || ad < 1 && Math.Abs(target) > 24) { anim[key] = target; return target; }
            v += d * step; anim[key] = v; Animating = true;
            return v;
        }
        /// <summary>만지지 않을 때는 작은 변화(snap 미만)를 바로 끝값으로. 매초 조금씩 바뀌는 값에 장수를 쓰지 않는다.</summary>
        protected float Soft(string key, float target, float snap)
        {
            float v; if (Calm && anim.TryGetValue(key, out v) && Math.Abs(target - v) < snap) { anim[key] = target; seen[key] = frame; return target; }
            return A(key, target);
        }
        public int AnimCount { get { return anim.Count; } }

        protected void Zone(float x, float y, float w, float h, string id, Action click, Action<float, float> move = null)
        {
            zones.Add(new Zone { R = new R4(x + C.Ox, y + C.Oy, w, h), Id = id, Click = click, Move = move });
        }
        protected bool Over(string id) { return Hover == id; }

        void FindHover()
        {
            string h = null;
            for (int i = zones.Count - 1; i >= 0; i--) if (zones[i].R.Has(Mx, My)) { h = zones[i].Id; break; }
            if (h != Hover) { Hover = h; Redraw(); }
        }
        protected void Redraw() { if (Dirty != null) Dirty(); }

        public bool MouseMove(float x, float y)
        {
            Mx = x; My = y;
            string h = null; Zone z = new Zone();
            for (int i = zones.Count - 1; i >= 0; i--) if (zones[i].R.Has(x, y)) { z = zones[i]; h = z.Id; break; }
            if (z.Move != null) { z.Move(x, y); Hover = h; return true; }
            if (h == Hover) return false;
            Hover = h;
            return true;
        }
        public bool Clickable(float x, float y) { for (int i = zones.Count - 1; i >= 0; i--) if (zones[i].R.Has(x, y)) return zones[i].Click != null; return false; }
        public bool Click(float x, float y)
        {
            for (int i = zones.Count - 1; i >= 0; i--)
            {
                if (!zones[i].R.Has(x, y)) continue;
                if (zones[i].Click == null) return false;
                zones[i].Click(); return true;
            }
            return false;
        }
        public virtual void MouseLeave() { Mx = My = -1; Hover = null; }
        public virtual void Wheel(int delta) { }

        // ---- 자주 쓰는 그림 ----

        static readonly Dictionary<string, string> Letters = new Dictionary<string, string> { { "mysqld", "my" }, { "claude", "C" }, { "python", "py" } };

        public void Icon(Grp g, string key, float x, float y, float size, Col ink)
        {
            if (key == "win") { WinMark(x, y, size, ink); return; }
            if (key == "*rest") { Dots(x, y, size, ink); return; }
            if (key != null && key.StartsWith("*")) { Mark(key, x, y, size, ink); return; }
            App a = g == null ? null : g.Main;
            byte[] px = a == null ? null : Icons.Get(a.Path);
            if (px != null) { C.Image(a.Path, px, Icons.Px, Icons.Px, x, y, size, size); return; }
            string l; if (!Letters.TryGetValue(key ?? "", out l)) l = g != null && g.Label.Length > 0 ? g.Label.Substring(0, 1) : "";
            C.Round(x, y, size, size, size * .26f, ink.Fade(.18f));
            C.Mid(l, F.Sans(size * .46f, 800), x + size / 2, y + size / 2, ink, 1);
        }
        void WinMark(float x, float y, float s, Col c) { float p = s * .14f, q = (s - p * 2 - s * .08f) / 2; C.Fill(x + p, y + p, q, q, c); C.Fill(x + s - p - q, y + p, q, q, c); C.Fill(x + p, y + s - p - q, q, q, c); C.Fill(x + s - p - q, y + s - p - q, q, q, c); }
        void Dots(float x, float y, float s, Col c) { for (int i = 0; i < 3; i++) C.Circle(x + s * (.24f + .26f * i), y + s / 2, s * .075f, c); }
        void Mark(string key, float x, float y, float s, Col c)
        {
            C.Round(x, y, s, s, s * .26f, c.Fade(.16f));
            string t = key == "*ram" ? Tx("램", "RAM") : key == "*temp" ? Tx("임시", "TMP") : key == "*vm" ? "VM" : key == "*drv" ? "DR" : key == "*boot" ? "ER" : key == "*round" ? Tx("판", "FPS") : key == "*power" ? Tx("전원", "PWR") : "CPU";
            C.Mid(t, F.Sans(s * (t.Length > 2 ? .3f : .36f), 800), x + s / 2, y + s / 2, c, 1);
        }

        /// <summary>값과 단위를 붙여 오른쪽 맞춤</summary>
        protected float Value(int m, double v, float right, float cy, float size, Col c)
        {
            string n = m == Snap.Cpu ? v.ToString("0") : Txt.Num(v), u = m == Snap.Cpu ? "%" : Txt.Unit(v);
            float bl = cy + size * .355f, uw = C.Measure(u, F.Din(size * .68f));
            C.Text(u, F.Din(size * .68f), right, bl, c, 2);
            float nw = C.Text(n, F.Din(size), right - uw - 1, bl, c, 2);
            return uw + nw + 1;
        }

        protected void Pill(string id, string text, float x, float y, float w, float h, float rad, Col bg, Col fg, Style st, Action click)
        {
            C.Round(x, y, w, h, rad, Over(id) ? bg.To(fg, .08f) : bg);
            C.Mid(text, st, x + w / 2, y + h / 2, fg, 1);
            Zone(x, y, w, h, id, click);
        }

        static readonly P2[] star = MakeStars();
        static P2[] MakeStars() { var p = new P2[90]; for (int i = 0; i < 90; i++) p[i] = new P2((i * 397.3f) % 1080 / 1080, (i * 91.7f) % 420 / 420); return p; }
        protected void Stars(float w, float h)
        {
            if (!T.Dark) return;
            for (int i = 0; i < 90; i++) { float r = i % 11 == 0 ? 1.9f : 1.2f; C.Fill(star[i].X * w, star[i].Y * h, r, r, Col.Hex(0xffffff, .12f + ((i * 7) % 10) / 22f)); }
        }
        protected void Sky(float w, float h, float red)
        {
            C.Clear(T.Bg);
        }
        readonly P2[] wave = new P2[64];
        protected void Wave(float x, float y, float w, Col c)
        {
            int n = Math.Min(63, (int)(w / 30) + 2);
            for (int i = 0; i < n; i++) wave[i] = new P2(x + w * i / (n - 1), y - ((i * 7 + 3) % 5) * 1.3f - (i % 2) * 2);
            wave[0].Y = y - 3; wave[n - 1].Y = y - 3;
            C.Clip(x, y - 12, w, 14); C.Area(wave, n, null, y + 2, c); C.Unclip();
        }

        public virtual void Dispose() { C.Dispose(); }
    }
}
