using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using static LagWatch.Lang;

namespace LagWatch
{
    [StructLayout(LayoutKind.Sequential)] internal struct P2 { public float X, Y; public P2(float x, float y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)]
    internal struct R4
    {
        public float L, T, R, B;
        public R4(float x, float y, float w, float h) { L = x; T = y; R = x + w; B = y + h; }
        public float W { get { return R - L; } }
        public float H { get { return B - T; } }
        public bool Has(float x, float y) { return x >= L && x < R && y >= T && y < B; }
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Col
    {
        public float R, G, B, A;
        public Col(float r, float g, float b, float a) { R = r; G = g; B = b; A = a; }
        public static Col Hex(int rgb, float a = 1) { return new Col((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f, a); }
        public Col Fade(float a) { return new Col(R, G, B, A * a); }
        public Col To(Col o, float t) { return new Col(R + (o.R - R) * t, G + (o.G - G) * t, B + (o.B - B) * t, A + (o.A - A) * t); }
        static float L(float v) { return v <= 0.04f ? v / 12.92f : (float)Math.Pow((v + 0.055f) / 1.055f, 2.4); }
        public float Lum { get { return 0.2126f * L(R) + 0.7152f * L(G) + 0.0722f * L(B); } }
    }

    /// <summary>
    /// 글자 모양만 DirectWrite 에서 얻는다(그래픽 카드와 Direct2D 는 쓰지 않는다).
    /// 만든 것의 수를 Live 로 세서 시험에서 누수를 잡는다.
    /// </summary>
    internal static unsafe class D2
    {
        [DllImport("dwrite.dll")] static extern int DWriteCreateFactory(int type, ref Guid iid, out IntPtr f);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr h);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr h);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr h, IntPtr o);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr h, ref BITMAPINFO bi, uint use, out IntPtr bits, IntPtr sec, uint off);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, int rop);
        [StructLayout(LayoutKind.Sequential)] public struct BITMAPINFO { public int Size, W, H; public short Planes, Bits; public int Comp, Img, Xp, Yp, Used, Imp, Pad; }

        public static int Live;
        public static IntPtr Write, Fonts;
        static IntPtr loader, system;

        public static void** V(IntPtr p) { return *(void***)p; }
        public static void Release(ref IntPtr p)
        {
            if (p == IntPtr.Zero) return;
            ((delegate* unmanaged[Stdcall]<IntPtr, uint>)V(p)[2])(p);
            p = IntPtr.Zero; Live--;
        }
        public static IntPtr Keep(int hr, IntPtr p)
        {
            if (hr < 0) throw new COMException("DW 0x" + hr.ToString("x8"), hr);
            Live++; return p;
        }

        static readonly object gate = new object();

        public static void Init()
        {
            lock (gate)
            {
                if (Write != IntPtr.Zero) return;
                var g = new Guid("958DB99A-BE2A-4F09-AF7D-65189803D1D3"); IntPtr p; // IDWriteFactory5
                int hr = DWriteCreateFactory(1, ref g, out p); Write = Keep(hr, p);
                try { LoadFonts(); } catch (Exception) { Fonts = IntPtr.Zero; }
            }
        }

        /// <summary>창이 모두 닫혔을 때. 그리기에 쓰던 것을 돌려준다(다시 열면 새로 만든다).</summary>
        public static void Shutdown()
        {
            lock (gate)
            {
                if (Write == IntPtr.Zero || Canvas.Count > 0) return;
                Glyphs.Clear();
                Release(ref Fonts); Release(ref system);
                if (loader != IntPtr.Zero) ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)V(Write)[14])(Write, loader);
                Release(ref loader); Release(ref Write);
            }
        }

        static void LoadFonts()
        {
            IntPtr b, l, set, col, f;
            Keep(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)V(Write)[44])(Write, &l), l); loader = l;
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)V(Write)[13])(Write, l);
            Keep(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)V(Write)[43])(Write, &b), b);
            int n = 0;
            foreach (var name in Assembly.GetExecutingAssembly().GetManifestResourceNames())
            {
                if (!name.EndsWith(".ttf")) continue;
                using (var s = (UnmanagedMemoryStream)Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                {
                    // 소유자를 안 주면 DirectWrite 가 복사해 간다. 자원은 exe 안의 주소를 그대로 넘긴다
                    int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, byte*, uint, IntPtr, IntPtr*, int>)V(l)[4])(l, Write, s.PositionPointer, (uint)s.Length, IntPtr.Zero, &f);
                    if (hr < 0) continue;
                }
                Live++;
                if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)V(b)[7])(b, f) >= 0) n++;
                Release(ref f);
            }
            if (n > 0)
            {
                Keep(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)V(b)[6])(b, &set), set);
                int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)V(Write)[37])(Write, set, &col);
                if (hr >= 0) { Live++; Fonts = col; }
                Release(ref set);
            }
            Release(ref b);
        }

        /// <summary>글꼴 하나. 없으면 0</summary>
        public static IntPtr Face(string family, int weight, int stretch, bool own)
        {
            if (system == IntPtr.Zero && (!own || Fonts == IntPtr.Zero)) { IntPtr c; if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int, int>)V(Write)[3])(Write, &c, 0) >= 0) system = Keep(0, c); }
            IntPtr col = own && Fonts != IntPtr.Zero ? Fonts : system; if (col == IntPtr.Zero) return IntPtr.Zero;
            uint idx; int ok; fixed (char* n = family) if (((delegate* unmanaged[Stdcall]<IntPtr, char*, uint*, int*, int>)V(col)[5])(col, n, &idx, &ok) < 0 || ok == 0) return IntPtr.Zero;
            IntPtr fam, font, face = IntPtr.Zero; if (((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)V(col)[4])(col, idx, &fam) < 0) return IntPtr.Zero; Live++;
            if (((delegate* unmanaged[Stdcall]<IntPtr, int, int, int, IntPtr*, int>)V(fam)[7])(fam, weight, stretch, 0, &font) >= 0) { Live++; if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)V(font)[13])(font, &face) >= 0) Live++; else face = IntPtr.Zero; Release(ref font); }
            Release(ref fam); return face;
        }
    }

    /// <summary>글자 모양 하나</summary>
    internal sealed class Style
    {
        public readonly string Family; public readonly int Weight, Stretch; public readonly float Size, Track; public readonly bool Own;
        internal Face Face;
        public Style(string family, int weight, float size, float track = 0, int stretch = 5, bool own = true) { Family = family; Weight = weight; Size = size; Track = track; Stretch = stretch; Own = own; }
    }

    internal sealed class Face { public IntPtr Ptr; public int Id; public float Upm; public readonly Dictionary<int, int> Map = new Dictionary<int, int>(); public Face[] Spare; public int Weight; }
    internal sealed class Glyph { public short L, T, W, H; public byte[] A; }

    /// <summary>글꼴과 그린 글자 조각. 모든 창이 같이 쓴다.</summary>
    internal static unsafe class Glyphs
    {
        static readonly Dictionary<string, Face> faces = new Dictionary<string, Face>();
        static readonly Dictionary<ulong, Glyph> bits = new Dictionary<ulong, Glyph>();
        static readonly List<Style> used = new List<Style>();
        static readonly Glyph Empty = new Glyph(); static int bytes, ids;
        public const int Cap = 1600 * 1024;
        public static int Count { get { return bits.Count; } }
        public static int Bytes { get { return bytes; } }

        public static void Clear()
        {
            bits.Clear(); bytes = 0; foreach (var s in used) s.Face = null; used.Clear();
            foreach (var f in faces.Values) if (f != null) D2.Release(ref f.Ptr); faces.Clear(); ids = 0;
        }
        public static void Drop() { bits.Clear(); bytes = 0; }

        static Face Load(string family, int weight, int stretch, bool own)
        {
            string key = family + "|" + weight + "|" + stretch + "|" + own; Face f; if (faces.TryGetValue(key, out f)) return f;
            IntPtr p = D2.Face(family, weight, stretch, own); if (p != IntPtr.Zero) { ushort* m = stackalloc ushort[16]; ((delegate* unmanaged[Stdcall]<IntPtr, ushort*, void>)D2.V(p)[8])(p, m); f = new Face { Ptr = p, Id = ++ids, Upm = Math.Max((ushort)1, m[0]), Weight = weight }; }
            faces[key] = f; return f;
        }
        public static Face Of(Style s)
        {
            if (s.Face != null) return s.Face; D2.Init();
            s.Face = Load(s.Family, s.Weight, s.Stretch, s.Own) ?? Load("Malgun Gothic", s.Weight, 5, false) ?? Load("Segoe UI", s.Weight, 5, false) ?? Load("Arial", 400, 5, false); if (s.Face == null) throw new InvalidOperationException(Tx("글꼴 없음", "No font")); used.Add(s); return s.Face;
        }
        static readonly string[] Others = { "Malgun Gothic", "Segoe UI Symbol", "Microsoft YaHei", "Yu Gothic", "Segoe UI Emoji" };

        /// <summary>글자 번호(위 16비트)와 폭(글꼴 단위). 이 글꼴에 없으면 다른 글꼴에서 찾아 face 를 바꾼다.</summary>
        public static int Find(ref Face face, int cp)
        {
            int v = Raw(face, cp); if (v >> 16 != 0) return v;
            if (face.Spare == null) { face.Spare = new Face[Others.Length]; for (int i = 0; i < Others.Length; i++) face.Spare[i] = Load(Others[i], face.Weight >= 600 ? 700 : 400, 5, false); }
            foreach (var f in face.Spare) { if (f == null) continue; int q = Raw(f, cp); if (q >> 16 != 0) { face = f; return q; } }
            return v;
        }
        static int Raw(Face f, int cp)
        {
            int v; if (f.Map.TryGetValue(cp, out v)) return v;
            ushort g = 0; uint c = (uint)cp; ((delegate* unmanaged[Stdcall]<IntPtr, uint*, uint, ushort*, int>)D2.V(f.Ptr)[11])(f.Ptr, &c, 1, &g);
            int* m = stackalloc int[7]; m[1] = 0; ((delegate* unmanaged[Stdcall]<IntPtr, ushort*, uint, int*, int, int>)D2.V(f.Ptr)[10])(f.Ptr, &g, 1, m, 0);
            v = g << 16 | Math.Min(65535, m[1]); if (f.Map.Count > 4000) f.Map.Clear(); f.Map[cp] = v; return v;
        }

        [StructLayout(LayoutKind.Sequential)] struct Run { public IntPtr Face; public float Size; public uint Count; public ushort* Index; public float* Adv; public void* Off; public int Side; public uint Bidi; }

        /// <summary>글자 조각. sub 은 가로 1/4 픽셀 자리</summary>
        public static Glyph Get(Face f, int glyph, float px, int sub)
        {
            ulong key = (ulong)(uint)(f.Id & 255) << 40 | (ulong)(uint)((int)(px * 8) & 0x3FFF) << 20 | (ulong)(uint)glyph << 4 | (uint)sub; // id 8 · 크기 14 · 글자 16 · 자리 2
            Glyph g; if (bits.TryGetValue(key, out g)) return g;
            if (bytes > Cap) Drop();
            ushort gi = (ushort)glyph; float adv = 0; var r = new Run { Face = f.Ptr, Size = px, Count = 1, Index = &gi, Adv = &adv }; IntPtr an; g = Empty;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, Run*, void*, int, int, int, int, float, float, IntPtr*, int>)D2.V(D2.Write)[30])(D2.Write, &r, null, 5, 0, 1, 1, sub * .25f, 0, &an);
            if (hr >= 0)
            {
                D2.Live++; int* rc = stackalloc int[4];
                if (((delegate* unmanaged[Stdcall]<IntPtr, int, int*, int>)D2.V(an)[3])(an, 0, rc) >= 0 && rc[2] > rc[0] && rc[3] > rc[1])
                {
                    int w = rc[2] - rc[0], h = rc[3] - rc[1]; var a = new byte[w * h];
                    fixed (byte* p = a) if (((delegate* unmanaged[Stdcall]<IntPtr, int, int*, byte*, uint, int>)D2.V(an)[4])(an, 0, rc, p, (uint)a.Length) >= 0) { g = new Glyph { L = (short)rc[0], T = (short)rc[1], W = (short)w, H = (short)h, A = a }; bytes += a.Length + 48; }
                }
                D2.Release(ref an);
            }
            bits[key] = g; return g;
        }
    }

    /// <summary>그림판 하나. 크기가 같으면 계속 다시 쓴다. 모든 그리기를 CPU 한 가닥으로 직접 한다.</summary>
    internal sealed unsafe class Canvas : IDisposable
    {
        IntPtr dc, bmp, oldBmp;
        public IntPtr Bits { get; private set; }
        public IntPtr Dc { get { return dc; } }
        public int PxW, PxH;
        public float Scale = 1;
        public static int Count;
        bool counted;
        uint* px; int W_, H_;
        float ox, oy, alpha = 1;
        // 자르기: 네모(픽셀)와 둥근 네모
        int cl, ct, cr, cb;
        struct RectClip { public int L, T, R, B; }
        struct RoundClip { public float L, T, R, B, Rad; }
        readonly List<RectClip> rects = new List<RectClip>(); readonly List<RoundClip> rounds = new List<RoundClip>(); readonly List<float> alphas = new List<float>();
        float[] row = new float[0]; // 한 줄의 덮인 정도

        public float W { get { return PxW / Scale; } }
        public float H { get { return PxH / Scale; } }
        public float Ox { get { return ox; } }
        public float Oy { get { return oy; } }
        public int TextCount { get { return Glyphs.Count; } }

        public void Resize(int w, int h, float scale)
        {
            if (!counted) { counted = true; System.Threading.Interlocked.Increment(ref Count); }
            D2.Init();
            w = Math.Max(1, w); h = Math.Max(1, h);
            if (w == PxW && h == PxH && scale == Scale && bmp != IntPtr.Zero) return;
            FreeBitmap();
            PxW = w; PxH = h; Scale = scale;
            dc = D2.CreateCompatibleDC(IntPtr.Zero);
            var bi = new D2.BITMAPINFO { Size = 40, W = w, H = -h, Planes = 1, Bits = 32 };
            IntPtr bits; bmp = D2.CreateDIBSection(dc, ref bi, 0, out bits, IntPtr.Zero, 0); Bits = bits; px = (uint*)bits; W_ = w; H_ = h;
            oldBmp = D2.SelectObject(dc, bmp); row = new float[w + 4];
        }
        void FreeBitmap()
        {
            if (dc == IntPtr.Zero) return;
            D2.SelectObject(dc, oldBmp); D2.DeleteObject(bmp); D2.DeleteDC(dc);
            dc = bmp = IntPtr.Zero; Bits = IntPtr.Zero; px = null; row = new float[0];
        }
        public void Begin() { ox = oy = 0; alpha = 1; rects.Clear(); rounds.Clear(); alphas.Clear(); kinds.Clear(); cl = ct = 0; cr = W_; cb = H_; }
        public bool End() { return true; }
        /// <summary>그리기에 쓰던 것을 놓는다. 다음 그릴 때 다시 만든다.</summary>
        public void Relax() { icons.Clear(); iconBytes = 0; Glyphs.Drop(); edges = new Edge[0]; flat = new P2[0]; }
        /// <summary>최소화했을 때. 그림판까지 놓는다.</summary>
        public void Sleep() { Relax(); FreeBitmap(); PxW = PxH = 0; }
        public void Dispose() { icons.Clear(); FreeBitmap(); if (counted) { counted = false; System.Threading.Interlocked.Decrement(ref Count); } }

        // ---- 자리와 자르기 ----

        public void Move(float x, float y) { ox = x; oy = y; }
        float X(float v) { return (v + ox) * Scale; }
        float Y(float v) { return (v + oy) * Scale; }
        static int Rnd(float v) { return (int)Math.Floor(v + .5f); }

        public void Clip(float x, float y, float w, float h)
        {
            rects.Add(new RectClip { L = cl, T = ct, R = cr, B = cb });
            cl = Math.Max(cl, Rnd(X(x))); ct = Math.Max(ct, Rnd(Y(y))); cr = Math.Min(cr, Rnd(X(x + w))); cb = Math.Min(cb, Rnd(Y(y + h)));
        }
        public void Unclip() { var r = rects[rects.Count - 1]; rects.RemoveAt(rects.Count - 1); cl = r.L; ct = r.T; cr = r.R; cb = r.B; }
        readonly List<byte> kinds = new List<byte>(); // 둥근 자르기 1, 흐리기 2
        public IntPtr ClipRound(float x, float y, float w, float h, float rad)
        {
            var c = new RoundClip { L = X(x), T = Y(y), R = X(x + w), B = Y(y + h), Rad = Math.Min(rad, Math.Min(w, h) / 2) * Scale };
            rects.Add(new RectClip { L = cl, T = ct, R = cr, B = cb }); rounds.Add(c); kinds.Add(1);
            cl = Math.Max(cl, (int)Math.Floor(c.L)); ct = Math.Max(ct, (int)Math.Floor(c.T)); cr = Math.Min(cr, (int)Math.Ceiling(c.R)); cb = Math.Min(cb, (int)Math.Ceiling(c.B));
            return IntPtr.Zero;
        }
        public void PushOpacity(float a) { alphas.Add(alpha); alpha *= Math.Max(0, Math.Min(1, a)); kinds.Add(2); }
        public void PopClip(IntPtr g)
        {
            byte k = kinds[kinds.Count - 1]; kinds.RemoveAt(kinds.Count - 1);
            if (k == 1) { rounds.RemoveAt(rounds.Count - 1); Unclip(); } else { alpha = alphas[alphas.Count - 1]; alphas.RemoveAt(alphas.Count - 1); }
        }

        // ---- 한 줄 칠하기 ----

        static uint Pack(Col c) { return (uint)(byte)(Math.Max(0, Math.Min(1, c.R)) * 255 + .5f) << 16 | (uint)(byte)(Math.Max(0, Math.Min(1, c.G)) * 255 + .5f) << 8 | (byte)(Math.Max(0, Math.Min(1, c.B)) * 255 + .5f); }
        /// <summary>rgb 를 a(0~255)만큼 덮는다. 바탕은 미리 곱한 값</summary>
        static uint Over(uint d, uint rgb, uint a)
        {
            uint inv = 255 - a, rb = (d & 0xFF00FF) * inv + (rgb & 0xFF00FF) * a + 0x800080, ag = (d >> 8 & 0xFF00FF) * inv + ((rgb >> 8 & 0xFF) | 0xFF0000) * a + 0x800080;
            rb = (rb + (rb >> 8 & 0xFF00FF)) >> 8 & 0xFF00FF; ag = (ag + (ag >> 8 & 0xFF00FF)) & 0xFF00FF00; return rb | ag;
        }
        static void Solid(uint* p, int n, uint v)
        {
            ulong w = (ulong)v << 32 | v; while (n >= 8) { ulong* q = (ulong*)p; q[0] = w; q[1] = w; q[2] = w; q[3] = w; p += 8; n -= 8; }
            while (n-- > 0) *p++ = v;
        }
        /// <summary>y 줄의 [x0, x1) 을 고르게</summary>
        void Span(int y, int x0, int x1, uint rgb, float a)
        {
            if (y < ct || y >= cb) return; if (x0 < cl) x0 = cl; if (x1 > cr) x1 = cr; if (x0 >= x1) return;
            if (rounds.Count > 0)
            {
                // 둥근 자르기의 모서리에 걸린 곳만 하나씩
                int a0, a1; if (!RoundSpan(y, ref x0, ref x1, out a0, out a1)) { for (int x = x0; x < x1; x++) { if (x == a0 && a1 > a0) { Span0(y, a0, a1, rgb, a); x = a1 - 1; continue; } float c = RoundAt(x, y); if (c > 0) Dot(y, x, rgb, a * c); } return; }
            }
            Span0(y, x0, x1, rgb, a);
        }
        void Span0(int y, int x0, int x1, uint rgb, float a)
        {
            uint A = (uint)(a * 255 + .5f); if (A == 0) return; uint* p = px + (long)y * W_ + x0; int n = x1 - x0;
            if (A >= 255) { Solid(p, n, 0xFF000000 | rgb); return; }
            uint last = 0, res = 0; bool has = false; for (int i = 0; i < n; i++) { uint d = p[i]; if (!has || d != last) { res = Over(d, rgb, A); last = d; has = true; } p[i] = res; }
        }
        void Dot(int y, int x, uint rgb, float a) { uint A = (uint)(a * 255 + .5f); if (A == 0) return; uint* p = px + (long)y * W_ + x; *p = A >= 255 ? 0xFF000000 | rgb : Over(*p, rgb, A); }
        /// <summary>y 줄의 덮인 정도(row[i], x0 부터 n 개)를 칠한다.</summary>
        void Cover(int y, int x0, int n, uint rgb, float a)
        {
            if (y < ct || y >= cb) return; int i0 = Math.Max(0, cl - x0), i1 = Math.Min(n, cr - x0); bool rc = rounds.Count > 0;
            for (int i = i0; i < i1; i++)
            {
                float c = row[i]; if (c <= .002f) continue;
                if (c >= .998f && !rc) { int j = i + 1; while (j < i1 && row[j] >= .998f) j++; Span0(y, x0 + i, x0 + j, rgb, a); i = j - 1; continue; }
                if (rc) c *= RoundAt(x0 + i, y); if (c > 0) Dot(y, x0 + i, rgb, a * Math.Min(1, c));
            }
        }
        /// <summary>이 줄이 둥근 모서리에 걸리지 않으면 true. 걸리면 온전히 안쪽인 구간을 준다.</summary>
        bool RoundSpan(int y, ref int x0, ref int x1, out int in0, out int in1)
        {
            in0 = x0; in1 = x1; bool clean = true;
            for (int k = 0; k < rounds.Count; k++)
            {
                var r = rounds[k]; float d = y + .5f < r.T + r.Rad + 1 ? r.T + r.Rad - y : y + .5f > r.B - r.Rad - 1 ? y + 1 - (r.B - r.Rad) : 0; float inset = 0; if (d > 0) { float q = Math.Min(r.Rad, d); inset = r.Rad - (float)Math.Sqrt(Math.Max(0, r.Rad * r.Rad - q * q)); if (y < r.T || y + 1 > r.B) inset = 1e6f; }
                int a = (int)Math.Ceiling(r.L + inset) + (inset > 0 ? 1 : 0), b = (int)Math.Floor(r.R - inset) - (inset > 0 ? 1 : 0); if (a > in0) in0 = a; if (b < in1) in1 = b; if (a > x0 || b < x1) clean = false;
            }
            return clean;
        }
        float RoundAt(int x, int y) { float c = 1; for (int k = 0; k < rounds.Count && c > 0; k++) { var r = rounds[k]; c *= RRect(x + .5f, y + .5f, r.L, r.T, r.R, r.B, r.Rad); } return c; }
        /// <summary>둥근 네모 안에 픽셀이 얼마나 들어가는지(거리로 어림)</summary>
        static float RRect(float x, float y, float l, float t, float r, float b, float rad)
        {
            float dx = x < l + rad ? l + rad - x : x > r - rad ? x - (r - rad) : 0, dy = y < t + rad ? t + rad - y : y > b - rad ? y - (b - rad) : 0;
            if (dx > 0 && dy > 0) { float d = rad - (float)Math.Sqrt(dx * dx + dy * dy) + .5f; return d <= 0 ? 0 : d >= 1 ? 1 : d; }
            float ex = Math.Min(x - l, r - x) + .5f, ey = Math.Min(y - t, b - y) + .5f; ex = ex <= 0 ? 0 : ex >= 1 ? 1 : ex; ey = ey <= 0 ? 0 : ey >= 1 ? 1 : ey; return ex * ey;
        }

        // ---- 도형 ----

        public void Clear(Col c) { uint A = (uint)(c.A * 255 + .5f), v = A == 0 ? 0 : Over(0, Pack(c), A); for (int y = 0; y < H_; y++) Solid(px + (long)y * W_, W_, v); }

        public void Fill(float x, float y, float w, float h, Col c)
        {
            if (w <= 0 || h <= 0 || px == null) return; float a = c.A * alpha; if (a <= .002f) return;
            float l = X(x), t = Y(y), r = X(x + w), b = Y(y + h); uint rgb = Pack(c); int y0 = Math.Max(ct, (int)Math.Floor(t)), y1 = Math.Min(cb, (int)Math.Ceiling(b)), xi = (int)Math.Ceiling(l - .002f), xj = (int)Math.Floor(r + .002f);
            float fl = xi - l, fr = r - xj; if (fl < .01f) fl = 0; if (fr < .01f) fr = 0; if (xi > xj) { fl = r - l; fr = 0; xj = xi; }
            for (int yy = y0; yy < y1; yy++)
            {
                float cv = Math.Min(yy + 1, b) - Math.Max(yy, t); if (cv <= .004f) continue; if (cv > .996f) cv = 1;
                if (fl > 0) Span(yy, xi - 1, xi, rgb, a * cv * Math.Min(1, fl)); if (xj > xi) Span(yy, xi, xj, rgb, a * cv); if (fr > 0) Span(yy, xj, xj + 1, rgb, a * cv * fr);
            }
        }
        public void Round(float x, float y, float w, float h, float rad, Col c)
        {
            if (w <= 0 || h <= 0 || px == null) return; float a = c.A * alpha; if (a <= .002f) return;
            rad = Math.Min(rad, Math.Min(w, h) / 2) * Scale; float l = X(x), t = Y(y), r = X(x + w), b = Y(y + h); uint rgb = Pack(c); int y0 = Math.Max(ct, (int)Math.Floor(t - .5f)), y1 = Math.Min(cb, (int)Math.Ceiling(b + .5f)), xa = Math.Max(cl, (int)Math.Floor(l - .5f)), xb = Math.Min(cr, (int)Math.Ceiling(r + .5f));
            for (int yy = y0; yy < y1; yy++)
            {
                float cy = yy + .5f, d = cy < t + rad ? t + rad - cy : cy > b - rad ? cy - (b - rad) : 0, inset = d > 0 ? rad - (float)Math.Sqrt(Math.Max(0, rad * rad - d * d)) : 0; bool edge = cy - t < .5f || b - cy < .5f;
                int i0 = Math.Max(xa, Math.Min(xb, (int)Math.Ceiling(l + inset) + 2)), i1 = Math.Max(i0, Math.Min(xb, (int)Math.Floor(r - inset) - 2)); if (edge || d >= rad - .5f) { i0 = i1 = xb; }
                for (int xx = xa; xx < i0; xx++) Soft1(yy, xx, rgb, a * RRect(xx + .5f, cy, l, t, r, b, rad));
                if (i1 > i0) Span(yy, i0, i1, rgb, a);
                for (int xx = Math.Max(i1, i0); xx < xb; xx++) Soft1(yy, xx, rgb, a * RRect(xx + .5f, cy, l, t, r, b, rad));
            }
        }
        void Soft1(int y, int x, uint rgb, float a) { if (a <= .002f) return; if (rounds.Count > 0) { a *= RoundAt(x, y); if (a <= .002f) return; } Dot(y, x, rgb, a); }
        public void RoundLine(float x, float y, float w, float h, float rad, Col c, float lw)
        {
            if (px == null) return; float a = c.A * alpha, k = lw / 2 * Scale; rad = Math.Min(rad, Math.Min(w, h) / 2) * Scale; float l = X(x), t = Y(y), r = X(x + w), b = Y(y + h); uint rgb = Pack(c);
            int y0 = Math.Max(ct, (int)Math.Floor(t - k - 1)), y1 = Math.Min(cb, (int)Math.Ceiling(b + k + 1)), xa = Math.Max(cl, (int)Math.Floor(l - k - 1)), xb = Math.Min(cr, (int)Math.Ceiling(r + k + 1));
            for (int yy = y0; yy < y1; yy++) for (int xx = xa; xx < xb; xx++) { float o = RRect(xx + .5f, yy + .5f, l - k, t - k, r + k, b + k, rad + k); if (o > 0) Soft1(yy, xx, rgb, a * Math.Max(0, o - RRect(xx + .5f, yy + .5f, l + k, t + k, r - k, b - k, Math.Max(0, rad - k)))); }
        }
        public void Circle(float cx, float cy, float r, Col c) { Round(cx - r, cy - r, r * 2, r * 2, r, c); }
        readonly P2[] quad = new P2[4];
        public void Line(float x1, float y1, float x2, float y2, Col c, float w)
        {
            if (Math.Abs(x1 - x2) < .01f) { Fill(x1 - w / 2, Math.Min(y1, y2), w, Math.Abs(y2 - y1), c); return; } if (Math.Abs(y1 - y2) < .01f) { Fill(Math.Min(x1, x2), y1 - w / 2, Math.Abs(x2 - x1), w, c); return; }
            float dx = x2 - x1, dy = y2 - y1, n = (float)Math.Sqrt(dx * dx + dy * dy), ux = -dy / n * w / 2, uy = dx / n * w / 2; quad[0] = new P2(x1 + ux, y1 + uy); quad[1] = new P2(x2 + ux, y2 + uy); quad[2] = new P2(x2 - ux, y2 - uy); quad[3] = new P2(x1 - ux, y1 - uy); Poly(quad, 4, c);
        }
        /// <summary>위에서 아래로 번지는 색(가로 전체)</summary>
        public void Gradient(float x, float y, float w, float h, Col a, Col b, Col c, float mid)
        {
            if (px == null) return; float t = Y(y), hh = h * Scale; int l = Math.Max(cl, Rnd(X(x))), r = Math.Min(cr, Rnd(X(x + w)));
            for (int yy = Math.Max(ct, Rnd(t)); yy < Math.Min(cb, Rnd(t + hh)); yy++) { float k = (yy + .5f - t) / hh; var q = k < mid ? a.To(b, k / mid) : b.To(c, (k - mid) / (1 - mid)); Solid(px + (long)yy * W_ + l, r - l, 0xFF000000 | Pack(q)); }
        }

        // ---- 다각형: 줄마다 덮인 넓이를 쌓아 칠한다 ----

        struct Edge { public float X0, Y0, X1, Y1; public int Dir; }
        Edge[] edges = new Edge[0]; int en;
        static readonly Comparison<Edge> ByTop = (a, b) => a.Y0.CompareTo(b.Y0);
        sealed class EdgeCmp : IComparer<Edge> { public int Compare(Edge a, Edge b) { return a.Y0.CompareTo(b.Y0); } }
        static readonly EdgeCmp Cmp = new EdgeCmp();
        void Add(float x0, float y0, float x1, float y1)
        {
            if (y0 == y1) return; if (en >= edges.Length) Array.Resize(ref edges, Math.Max(256, en * 2));
            edges[en++] = y0 < y1 ? new Edge { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Dir = 1 } : new Edge { X0 = x1, Y0 = y1, X1 = x0, Y1 = y0, Dir = -1 };
        }
        void AddCurve(P2 u, P2 v, ref float px0, ref float py0, bool straight)
        {
            float x0 = px0, y0 = py0, x3 = X(v.X), y3 = Y(v.Y);
            if (straight || Math.Abs(y3 - y0) < .35f) { Add(x0, y0, x3, y3); px0 = x3; py0 = y3; return; }
            float mx = (x0 + x3) / 2; int n = Math.Max(2, Math.Min(14, (int)(Math.Sqrt((x3 - x0) * (x3 - x0) + (y3 - y0) * (y3 - y0)) / 4) + 1)); float lx = x0, ly = y0;
            for (int i = 1; i <= n; i++) { float t = i / (float)n, s = 1 - t, x = s * s * s * x0 + 3 * s * s * t * mx + 3 * s * t * t * mx + t * t * t * x3, y = s * s * s * y0 + 3 * s * s * t * y0 + 3 * s * t * t * y3 + t * t * t * y3; Add(lx, ly, x, y); lx = x; ly = y; }
            px0 = x3; py0 = y3;
        }
        /// <summary>점들을 가로로 부드럽게 잇는다. low 가 있으면 그 사이를 채우고, 없으면 base 까지 채운다.</summary>
        public void Area(P2[] hi, int n, P2[] low, float baseY, Col c, bool straight = false)
        {
            if (n < 2 || px == null) return; en = 0; float x = X(hi[0].X), y = Y(hi[0].Y), fx = x, fy = y;
            for (int i = 1; i < n; i++) AddCurve(hi[i - 1], hi[i], ref x, ref y, straight);
            if (low != null) { float tx = X(low[n - 1].X), ty = Y(low[n - 1].Y); Add(x, y, tx, ty); x = tx; y = ty; for (int i = n - 1; i > 0; i--) AddCurve(low[i], low[i - 1], ref x, ref y, straight); }
            else { float tx = X(hi[n - 1].X), ty = Y(baseY); Add(x, y, tx, ty); Add(tx, ty, fx, ty); x = fx; y = ty; }
            Add(x, y, fx, fy); FillEdges(c);
        }
        public void Poly(P2[] p, int n, Col c)
        {
            if (n < 3 || px == null) return; en = 0; for (int i = 0; i < n; i++) { var u = p[i]; var v = p[(i + 1) % n]; Add(X(u.X), Y(u.Y), X(v.X), Y(v.Y)); } FillEdges(c);
        }
        P2[] flat = new P2[0]; int fn;
        void Flat(float x, float y)
        {
            if (fn > 0) { float dx = x - flat[fn - 1].X, dy = y - flat[fn - 1].Y; if (dx * dx + dy * dy < .01f) return; }
            if (fn >= flat.Length) Array.Resize(ref flat, Math.Max(128, fn * 2)); flat[fn++] = new P2(x, y);
        }
        public void Path(P2[] p, int n, Col c, float w, bool curve)
        {
            if (n < 2 || px == null) return; en = fn = 0; float k = Math.Max(.5f, w * Scale / 2), x0 = X(p[0].X), y0 = Y(p[0].Y); Flat(x0, y0);
            for (int i = 1; i < n; i++)
            {
                float x3 = X(p[i].X), y3 = Y(p[i].Y); if (!curve || Math.Abs(y3 - y0) < .35f) { Flat(x3, y3); x0 = x3; y0 = y3; continue; }
                float mx = (x0 + x3) / 2; int m = Math.Max(2, Math.Min(12, (int)(Math.Sqrt((x3 - x0) * (x3 - x0) + (y3 - y0) * (y3 - y0)) / 4) + 1));
                for (int j = 1; j <= m; j++) { float t = j / (float)m, s = 1 - t; Flat(s * s * s * x0 + 3 * s * s * t * mx + 3 * s * t * t * mx + t * t * t * x3, s * s * s * y0 + 3 * s * s * t * y0 + 3 * s * t * t * y3 + t * t * t * y3); }
                x0 = x3; y0 = y3;
            }
            // 완만한 곳은 한 띠로, 꺾이는 곳에서만 나눈다
            int a = 0;
            while (a < fn - 1) { int b = a + 1; while (b < fn - 1 && Turn(b) > .72f) b++; Strip(a, b, k); a = b; }
            FillEdges(c);
        }
        static float Dir(P2 a, P2 b, out float dx, out float dy) { dx = b.X - a.X; dy = b.Y - a.Y; float n = (float)Math.Sqrt(dx * dx + dy * dy); dx /= n; dy /= n; return n; }
        float Turn(int i) { float ax, ay, bx, by; Dir(flat[i - 1], flat[i], out ax, out ay); Dir(flat[i], flat[i + 1], out bx, out by); return ax * bx + ay * by; }
        /// <summary>a 에서 b 까지를 한 다각형으로. 끝은 이음새가 벌어지지 않게 조금 늘린다.</summary>
        void Strip(int a, int b, float k)
        {
            float e = Math.Min(k * .35f, .6f), lx = 0, ly = 0, rx = 0, ry = 0, fx = 0, fy = 0, gx = 0, gy = 0;
            for (int i = a; i <= b; i++)
            {
                float dx, dy, ux, uy, m = 1; P2 p = flat[i];
                if (i == a) { Dir(p, flat[i + 1], out dx, out dy); p.X -= dx * e; p.Y -= dy * e; }
                else if (i == b) { Dir(flat[i - 1], p, out dx, out dy); p.X += dx * e; p.Y += dy * e; }
                else { float ax, ay, bx, by; Dir(flat[i - 1], p, out ax, out ay); Dir(p, flat[i + 1], out bx, out by); dx = ax + bx; dy = ay + by; float n = (float)Math.Sqrt(dx * dx + dy * dy); dx /= n; dy /= n; m = 1 / Math.Max(.5f, dx * bx + dy * by); }
                ux = -dy * k * m; uy = dx * k * m; float px1 = p.X + ux, py1 = p.Y + uy, qx1 = p.X - ux, qy1 = p.Y - uy;
                if (i == a) { fx = px1; fy = py1; gx = qx1; gy = qy1; } else { Add(lx, ly, px1, py1); Add(qx1, qy1, rx, ry); }
                lx = px1; ly = py1; rx = qx1; ry = qy1;
            }
            Add(lx, ly, rx, ry); Add(gx, gy, fx, fy);
        }
        int[] active = new int[64];
        void FillEdges(Col c)
        {
            float a = c.A * alpha; if (en == 0 || a <= .002f) return; uint rgb = Pack(c);
            float minX = 1e9f, maxX = -1e9f, minY = 1e9f, maxY = -1e9f; for (int i = 0; i < en; i++) { var e = edges[i]; if (e.Y0 < minY) minY = e.Y0; if (e.Y1 > maxY) maxY = e.Y1; float l = Math.Min(e.X0, e.X1), r = Math.Max(e.X0, e.X1); if (l < minX) minX = l; if (r > maxX) maxX = r; }
            int y0 = Math.Max(ct, (int)Math.Floor(minY)), y1 = Math.Min(cb, (int)Math.Ceiling(maxY)), bx = Math.Max(cl, (int)Math.Floor(minX)), ex = Math.Min(cr, (int)Math.Ceiling(maxX)); if (y0 >= y1 || bx >= ex) return;
            Array.Sort(edges, 0, en, Cmp); int next = 0, an = 0, bw = ex - bx; if (active.Length < en) active = new int[en];
            fixed (float* rw = row)
            {
                for (int y = y0; y < y1; y++)
                {
                    while (next < en && edges[next].Y0 < y + 1) active[an++] = next++;
                    int lo = bw, hi = -1; int w = 0;
                    for (int i = 0; i < an; i++)
                    {
                        var e = edges[active[i]]; if (e.Y1 <= y) continue; active[w++] = active[i];
                        float ya = Math.Max(e.Y0, y), yb = Math.Min(e.Y1, y + 1); if (yb <= ya) continue; float k = (e.X1 - e.X0) / (e.Y1 - e.Y0), xa = e.X0 + (ya - e.Y0) * k - bx, xb = e.X0 + (yb - e.Y0) * k - bx, d = (yb - ya) * e.Dir; Acc(rw, bw, xa, xb, d, ref lo, ref hi);
                    }
                    an = w; if (hi < 0) continue;
                    // 왼쪽부터 더해 가면 그 자리의 덮인 정도가 된다
                    float sum = 0; int x = lo; hi = Math.Min(hi, bw);
                    while (x < bw)
                    {
                        if (x <= hi) { sum += rw[x]; rw[x] = 0; float v = sum < 0 ? -sum : sum; if (v > 1) v = 1; if (v > .998f) { int j = x + 1; while (j <= hi && j < bw && rw[j] == 0) j++; if (j > hi) j = v > .5f && j < bw ? j : j; Span(y, bx + x, bx + j, rgb, a); x = j; continue; } if (v > .003f) Soft1(y, bx + x, rgb, a * v); x++; }
                        else { float v = Math.Min(1, Math.Abs(sum)); if (v > .01f) Span(y, bx + x, ex, rgb, a * v); break; }
                    }
                    for (int q = lo; q <= hi + 1 && q < row.Length; q++) rw[q] = 0;
                }
            }
        }
        /// <summary>한 줄 안에서 모서리 토막 하나가 덮는 넓이를 쌓는다.</summary>
        static void Acc(float* r, int n, float xa, float xb, float d, ref int lo, ref int hi)
        {
            if (xa > xb) { float t = xa; xa = xb; xb = t; }
            if (xb <= 0) { r[0] += d; if (0 < lo) lo = 0; if (hi < 0) hi = 0; return; } if (xa >= n) return;
            if (xa < 0) { float k = xb > xa ? -xa / (xb - xa) : 0; r[0] += d * k; d *= 1 - k; xa = 0; if (0 < lo) lo = 0; if (hi < 0) hi = 0; }
            if (xb > n) { float k = (xb - n) / (xb - xa); d *= 1 - k; xb = n; if (xb <= xa) return; }
            int i0 = (int)xa, i1 = (int)xb; if (i0 >= n) return; if (i0 < lo) lo = i0;
            if (i0 == i1 || xb - xa < 1e-5f) { float m = (xa + xb) / 2 - i0; r[i0] += d * (1 - m); r[i0 + 1] += d * m; if (i0 + 1 > hi) hi = i0 + 1; return; }
            float s = 1 / (xb - xa), f = 1 - (xa - i0), a0 = .5f * s * f * f; r[i0] += d * a0; float prev = a0;
            if (i1 == i0 + 1) { float g = xb - i1, am = .5f * s * g * g; r[i0 + 1] += d * (1 - a0 - am); r[i0 + 2] += d * am; if (i0 + 2 > hi) hi = i0 + 2; return; }
            float a1 = s * (1.5f - (xa - i0)); r[i0 + 1] += d * (a1 - a0); for (int x = i0 + 2; x < i1; x++) r[x] += d * s; float a2 = a1 + (i1 - i0 - 2) * s, g2 = xb - i1, an = .5f * s * g2 * g2; r[i1] += d * (1 - a2 - an); r[i1 + 1] += d * an; if (i1 + 1 > hi) hi = i1 + 1;
        }

        // ---- 그림 ----

        readonly Dictionary<string, uint[]> icons = new Dictionary<string, uint[]>(); int iconBytes;
        /// <summary>미리 곱한 BGRA 픽셀을 줄여 그린다. 같은 크기는 다시 만들지 않는다.</summary>
        public void Image(object key, byte[] src, int w, int h, float x, float y, float dw, float dh, float opacity = 1)
        {
            if (px == null) return; int tw = Math.Max(1, Rnd(dw * Scale)), th = Math.Max(1, Rnd(dh * Scale)); string k = key + "|" + tw; uint[] d;
            if (!icons.TryGetValue(k, out d)) { if (iconBytes > 900 * 1024) { icons.Clear(); iconBytes = 0; } d = Shrink(src, w, h, tw, th); icons[k] = d; iconBytes += d.Length * 4; }
            int l = Rnd(X(x)), t = Rnd(Y(y)); float op = opacity * alpha; uint oa = (uint)(op * 256 + .5f);
            for (int j = Math.Max(0, ct - t); j < th && t + j < cb; j++)
            {
                uint* p = px + (long)(t + j) * W_;
                for (int i = Math.Max(0, cl - l); i < tw && l + i < cr; i++)
                {
                    uint s = d[j * tw + i]; if (s >> 24 == 0) continue; uint m = oa; if (rounds.Count > 0) { m = (uint)(m * RoundAt(l + i, t + j)); if (m == 0) continue; }
                    if (m < 256) s = ((s & 0xFF00FF) * m >> 8 & 0xFF00FF) | ((s >> 8 & 0xFF00FF) * m & 0xFF00FF00);
                    uint inv = 255 - (s >> 24), q = p[l + i], rb = (q & 0xFF00FF) * inv + 0x800080, ag = (q >> 8 & 0xFF00FF) * inv + 0x800080; rb = (rb + (rb >> 8 & 0xFF00FF)) >> 8 & 0xFF00FF; ag = (ag + (ag >> 8 & 0xFF00FF)) & 0xFF00FF00; p[l + i] = s + (rb | ag);
                }
            }
        }
        static uint[] Shrink(byte[] s, int w, int h, int tw, int th)
        {
            var d = new uint[tw * th]; float sx = w / (float)tw, sy = h / (float)th;
            for (int j = 0; j < th; j++) for (int i = 0; i < tw; i++)
                {
                    float x0 = i * sx, x1 = x0 + sx, y0 = j * sy, y1 = y0 + sy, b = 0, g = 0, r = 0, a = 0, sum = 0;
                    for (int yy = (int)y0; yy < h && yy < y1; yy++) { float wy = Math.Min(yy + 1, y1) - Math.Max(yy, y0); for (int xx = (int)x0; xx < w && xx < x1; xx++) { float k = wy * (Math.Min(xx + 1, x1) - Math.Max(xx, x0)); int o = (yy * w + xx) * 4; b += s[o] * k; g += s[o + 1] * k; r += s[o + 2] * k; a += s[o + 3] * k; sum += k; } }
                    if (sum > 0) d[j * tw + i] = (uint)(a / sum + .5f) << 24 | (uint)(r / sum + .5f) << 16 | (uint)(g / sum + .5f) << 8 | (uint)(b / sum + .5f);
                }
            return d;
        }

        // ---- 글자 ----

        const int MaxRun = 256;
        readonly Face[] rf = new Face[MaxRun]; readonly int[] rg = new int[MaxRun]; readonly float[] rx = new float[MaxRun + 1];
        /// <summary>글자를 늘어놓는다. 폭(배율 1 기준)을 돌려준다. 끝의 빈칸은 폭에 넣지 않는다.</summary>
        float Lay(string t, Style s, float maxW, out int n)
        {
            var f0 = Glyphs.Of(s); float x = 0, end = 0; n = 0; float tr = s.Track * s.Size;
            for (int i = 0; i < t.Length && n < MaxRun - 1; i++)
            {
                int cp = t[i]; if (char.IsHighSurrogate(t[i]) && i + 1 < t.Length) { cp = char.ConvertToUtf32(t[i], t[i + 1]); i++; } else if (cp < 32) cp = 32;
                var f = f0; int v = Glyphs.Find(ref f, cp); rf[n] = f; rg[n] = v >> 16 & 0xFFFF; rx[n] = x; x += (v & 0xFFFF) * s.Size / f.Upm + tr; n++; if (cp != 32) end = x;
            }
            rx[n] = x;
            if (maxW <= 0 || end <= maxW + .01f) return end;
            var ff = f0; int dv = Glyphs.Find(ref ff, 0x2026); float dw = (dv & 0xFFFF) * s.Size / ff.Upm + tr; int k = n; while (k > 0 && rx[k] + dw > maxW) k--; while (k > 0 && rx[k] - rx[k - 1] > 0 && IsSpace(f0, k - 1)) k--;
            if (dw > maxW) { n = 0; return 0; } rf[k] = ff; rg[k] = dv >> 16 & 0xFFFF; n = k + 1; rx[n] = rx[k] + dw; return rx[n];
        }
        bool IsSpace(Face f, int i) { Face q = f; return rf[i] == f && rg[i] == (Glyphs.Find(ref q, 32) >> 16 & 0xFFFF); }

        public float Measure(string t, Style s) { int n; return string.IsNullOrEmpty(t) ? 0 : Lay(t, s, 0, out n); }

        /// <summary>y 는 글자 밑줄(기준선). align 0 왼쪽, 1 가운데, 2 오른쪽. 그린 폭을 돌려준다.</summary>
        public float Text(string t, Style s, float x, float y, Col c, int align = 0, float maxW = 0)
        {
            if (string.IsNullOrEmpty(t)) return 0; int n; float w = Lay(t, s, maxW, out n); if (px == null) return w;
            float a = c.A * alpha; if (a <= .004f) return w; float sx = X(align == 1 ? x - w / 2 : align == 2 ? x - w : x), size = s.Size * Scale; int by = Rnd(Y(y)); uint rgb = Pack(c);
            // 밝은 글자는 조금 또렷하게(어두운 바탕에서 가늘어 보이지 않게)
            bool light = c.R * .3f + c.G * .59f + c.B * .11f > .5f; uint am = (uint)(a * 256 + .5f);
            for (int i = 0; i < n; i++)
            {
                float gx = sx + rx[i] * Scale; int ix = (int)Math.Floor(gx), sub = (int)((gx - ix) * 4); var g = Glyphs.Get(rf[i], rg[i], size, sub); if (g.A == null) continue;
                int l = ix + g.L, tp = by + g.T; int j0 = Math.Max(0, ct - tp), j1 = Math.Min(g.H, cb - tp), i0 = Math.Max(0, cl - l), i1 = Math.Min(g.W, cr - l); if (i0 >= i1) continue; bool rc = rounds.Count > 0;
                fixed (byte* ga = g.A) for (int j = j0; j < j1; j++)
                    {
                        uint* p = px + (long)(tp + j) * W_ + l; byte* q = ga + j * g.W;
                        for (int k = i0; k < i1; k++) { uint v = q[k]; if (v == 0) continue; if (light) v += v * (255 - v) >> 10; v = v * am >> 8; if (rc) v = (uint)(v * RoundAt(l + k, tp + j)); if (v == 0) continue; p[k] = v >= 255 ? 0xFF000000 | rgb : Over(p[k], rgb, v); }
                    }
            }
            return w;
        }
        /// <summary>cy 를 글자 가운데로</summary>
        public float Mid(string t, Style s, float x, float cy, Col c, int align = 0, float maxW = 0) { return Text(t, s, x, cy + s.Size * 0.355f, c, align, maxW); }
    }
}
