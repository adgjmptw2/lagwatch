using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using static LagWatch.Lang;

namespace LagWatch
{
    internal static unsafe class Win
    {
        [StructLayout(LayoutKind.Sequential)] public struct PT { public int X, Y; public PT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] public struct SZ { public int W, H; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLEND { public byte Op, Flags, Alpha, Fmt; }
        [StructLayout(LayoutKind.Sequential)] public struct RC { public int L, T, R, B; public int W { get { return R - L; } } public int H { get { return B - T; } } }
        [StructLayout(LayoutKind.Sequential)] public struct MARGINS { public int L, R, T, B; }
        [StructLayout(LayoutKind.Sequential)] public struct MSG { public IntPtr H; public int M; public IntPtr W, L; public uint Time; public PT P; public uint Priv; }
        [StructLayout(LayoutKind.Sequential)] public struct CLS { public int Cb, Style; public IntPtr Proc; public int C, D; public IntPtr Inst, Icon, Cursor, Back; public IntPtr Menu, Name; public IntPtr IconSm; }
        [StructLayout(LayoutKind.Sequential)] public struct PAINT { public IntPtr Dc; public int Erase; public RC R; public int A, B; public fixed byte Z[32]; }
        [StructLayout(LayoutKind.Sequential)] public struct TRACK { public int Cb, Fl; public IntPtr H; public int T; }
        [StructLayout(LayoutKind.Sequential)] public struct MON { public int Cb; public RC Full, Work; public int Fl; }
        [StructLayout(LayoutKind.Sequential)] public struct ICONINFO { public int Icon, X, Y; public IntPtr Mask, Color; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct NID
        {
            public int Cb; public IntPtr H; public int Id, Flags, Msg; public IntPtr Icon; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip; public int State, Mask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info; public int Ver; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title; public int InfoFlags; public Guid G; public IntPtr Balloon;
        }
        [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref PT pos, ref SZ size, IntPtr src, ref PT sp, int key, ref BLEND b, int fl);
        [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint fl);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, int m, IntPtr w, string l);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")] public static extern IntPtr Send(IntPtr h, int m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, int m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern ushort RegisterClassExW(ref CLS c);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
        [DllImport("user32.dll")] public static extern IntPtr DefWindowProcW(IntPtr h, int m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool PeekMessageW(out MSG m, IntPtr h, int a, int b, int rm);
        [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG m);
        [DllImport("user32.dll")] public static extern IntPtr DispatchMessageW(ref MSG m);
        [DllImport("user32.dll")] public static extern void PostQuitMessage(int c);
        [DllImport("user32.dll")] public static extern uint MsgWaitForMultipleObjectsEx(int n, IntPtr[] h, uint ms, uint mask, uint fl);
        [DllImport("user32.dll")] public static extern IntPtr SetTimer(IntPtr h, IntPtr id, int ms, IntPtr f);
        [DllImport("user32.dll")] public static extern bool KillTimer(IntPtr h, IntPtr id);
        [DllImport("user32.dll")] public static extern IntPtr BeginPaint(IntPtr h, out PAINT p);
        [DllImport("user32.dll")] public static extern bool EndPaint(IntPtr h, ref PAINT p);
        [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr h, IntPtr r, bool e);
        [DllImport("user32.dll")] public static extern bool UpdateWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RC r);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RC r);
        [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h, ref PT p);
        [DllImport("user32.dll")] public static extern bool TrackMouseEvent(ref TRACK t);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetFocus();
        [DllImport("user32.dll")] public static extern IntPtr SetCursor(IntPtr c);
        [DllImport("user32.dll")] public static extern IntPtr LoadCursorW(IntPtr i, IntPtr n);
        [DllImport("user32.dll")] public static extern IntPtr LoadImageW(IntPtr i, IntPtr n, int t, int w, int h, int fl);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out PT p);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(PT p, int fl);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfoW(IntPtr m, ref MON i);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLengthW(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetWindowTextW(IntPtr h, string s);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessageW(string s);
        [DllImport("user32.dll")] public static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool AppendMenuW(IntPtr m, int fl, IntPtr id, string s);
        [DllImport("user32.dll")] public static extern int TrackPopupMenuEx(IntPtr m, int fl, int x, int y, IntPtr h, IntPtr p);
        [DllImport("user32.dll")] public static extern bool DestroyMenu(IntPtr m);
        [DllImport("user32.dll")] public static extern IntPtr CreateIconIndirect(ref ICONINFO i);
        [DllImport("user32.dll")] public static extern bool GetIconInfo(IntPtr h, out ICONINFO i);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateBitmap(int w, int h, int planes, int bits, void* p);
        [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr dc, IntPtr b, int from, int n, void* p, ref D2.BITMAPINFO i, int use);
        [DllImport("gdi32.dll")] public static extern int GetDeviceCaps(IntPtr dc, int i);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateSolidBrush(int c);
        [DllImport("gdi32.dll")] public static extern int SetTextColor(IntPtr dc, int c);
        [DllImport("gdi32.dll")] public static extern int SetBkColor(IntPtr dc, int c);
        [DllImport("gdi32.dll")] public static extern int SetBkMode(IntPtr dc, int m);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern bool TextOutW(IntPtr dc, int x, int y, string s, int n);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll")] public static extern IntPtr CallWindowProcW(IntPtr f, IntPtr h, int m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool HideCaret(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowCaret(IntPtr h);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateFontW(int h, int w, int e, int o, int wt, int it, int un, int st, int cs, int op, int cp, int q, int pf, string f);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandleW(string n);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr LoadLibraryW(string n);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern bool Shell_NotifyIconW(int m, ref NID n);
        [DllImport("uxtheme.dll", EntryPoint = "#135")] static extern int PreferDark(int mode);
        [DllImport("uxtheme.dll", EntryPoint = "#136")] static extern void FlushMenus();
        public static void DarkMenus() { try { PreferDark(2); FlushMenus(); } catch (Exception) { } }

        public static float Dpi() { IntPtr dc = GetDC(IntPtr.Zero); int v = GetDeviceCaps(dc, 88); ReleaseDC(IntPtr.Zero, dc); return v > 0 ? v / 96f : 1; }
        public static PT Mouse() { PT p; GetCursorPos(out p); return p; }
        public static RC Work(PT p, out RC full) { var m = new MON { Cb = sizeof(MON) }; GetMonitorInfoW(MonitorFromPoint(p, 2), ref m); full = m.Full; return m.Work; }
        public static readonly IntPtr Arrow = LoadCursorW(IntPtr.Zero, (IntPtr)32512), Hand = LoadCursorW(IntPtr.Zero, (IntPtr)32649);
        public static int X(IntPtr l) { return (short)(l.ToInt64() & 0xffff); }
        public static int Y(IntPtr l) { return (short)(l.ToInt64() >> 16 & 0xffff); }
    }

    /// <summary>메시지 고리. 다른 스레드가 시킨 일도 여기서 한다.</summary>
    internal static class Loop
    {
        static readonly object gate = new object(); static List<Action> q = new List<Action>(); static bool posted; public static IntPtr Host;
        public const int Msg = 0x8002;
        /// <summary>글 상자에서 누른 키를 먼저 본다.</summary>
        public static IntPtr Edit; public static Func<int, bool> EditKey;

        public static void Post(Action a) { lock (gate) { q.Add(a); if (posted || Host == IntPtr.Zero) return; posted = true; } Win.PostMessageW(Host, Msg, IntPtr.Zero, IntPtr.Zero); }
        public static void Drain()
        {
            List<Action> r; lock (gate) { r = q; q = new List<Action>(); posted = false; }
            foreach (var a in r) try { a(); } catch (Exception ex) { Sys.Note(ex); }
        }
        public static void Run(IntPtr[] events, Action<int> on)
        {
            Win.MSG m;
            while (true)
            {
                while (Win.PeekMessageW(out m, IntPtr.Zero, 0, 0, 1))
                {
                    if (m.M == 0x12) return;
                    if (m.M == 0x100 && Edit != IntPtr.Zero && m.H == Edit && EditKey != null && EditKey((int)m.W)) continue;
                    Win.TranslateMessage(ref m); Win.DispatchMessageW(ref m);
                }
                uint r = Win.MsgWaitForMultipleObjectsEx(events.Length, events, 0xFFFFFFFF, 0x4FF, 4);
                if (r < events.Length) try { on((int)r); } catch (Exception ex) { Sys.Note(ex); }
            }
        }
    }

    /// <summary>창 하나</summary>
    internal abstract unsafe class Wnd
    {
        delegate IntPtr Proc(IntPtr h, int m, IntPtr w, IntPtr l);
        static readonly Proc route = Route; static bool known; static Wnd making; static readonly Dictionary<IntPtr, Wnd> all = new Dictionary<IntPtr, Wnd>();
        const string Class = "LagWatchWnd";
        public IntPtr H;
        public bool Visible { get { return H != IntPtr.Zero && Win.IsWindowVisible(H); } }

        protected void Make(string title, uint style, uint ex, int x, int y, int w, int h)
        {
            IntPtr inst = Win.GetModuleHandleW(null);
            if (!known)
            {
                var c = new Win.CLS { Cb = sizeof(Win.CLS), Proc = Marshal.GetFunctionPointerForDelegate(route), Inst = inst, Cursor = Win.Arrow };
                c.Icon = Win.LoadImageW(inst, (IntPtr)32512, 1, Win.GetSystemMetrics(11), Win.GetSystemMetrics(12), 0x8000); c.IconSm = Win.LoadImageW(inst, (IntPtr)32512, 1, Win.GetSystemMetrics(49), Win.GetSystemMetrics(50), 0x8000);
                c.Name = Marshal.StringToHGlobalUni(Class); if (Win.RegisterClassExW(ref c) == 0) throw new InvalidOperationException("class " + Marshal.GetLastWin32Error()); known = true;
            }
            making = this; IntPtr r = Win.CreateWindowExW(ex, Class, title, style, x, y, w, h, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero); making = null;
            if (r == IntPtr.Zero) throw new InvalidOperationException("window " + Marshal.GetLastWin32Error());
        }
        static IntPtr Route(IntPtr h, int m, IntPtr w, IntPtr l)
        {
            Wnd o; if (!all.TryGetValue(h, out o)) { if (making == null) return Win.DefWindowProcW(h, m, w, l); o = making; making = null; o.H = h; all[h] = o; }
            IntPtr r = IntPtr.Zero; bool done = false;
            try { done = o.On(m, w, l, ref r); } catch (Exception ex) { Sys.Note(ex); }
            if (m == 0x82) { all.Remove(h); o.H = IntPtr.Zero; }
            return done ? r : Win.DefWindowProcW(h, m, w, l);
        }
        /// <summary>다 했으면 true</summary>
        protected abstract bool On(int m, IntPtr w, IntPtr l, ref IntPtr r);

        protected void Timer(int id, int ms) { Win.SetTimer(H, (IntPtr)id, ms, IntPtr.Zero); }
        protected void NoTimer(int id) { if (H != IntPtr.Zero) Win.KillTimer(H, (IntPtr)id); }
        bool tracking;
        /// <summary>마우스가 나갈 때 알려 달라고 한다.</summary>
        protected void Track() { if (tracking) return; tracking = true; var t = new Win.TRACK { Cb = sizeof(Win.TRACK), Fl = 2, H = H }; Win.TrackMouseEvent(ref t); }
        protected void Untrack() { tracking = false; }
        public void Destroy() { if (H != IntPtr.Zero) Win.DestroyWindow(H); }
    }

    /// <summary>반투명 모서리와 그림자가 있는 작은 창</summary>
    internal abstract class Popup : Wnd
    {
        protected readonly Surface V;
        protected readonly float Scale_;
        readonly Action onDirty;
        bool queued, drawing; int running;
        protected float Alpha = 1, Shift;
        protected readonly bool NoActivate;
        int touch; // 마지막으로 만진 때. 만지는 동안만 자주 그린다
        protected bool Glance;      // 잠깐 보는 창: 뜰 때만 부드럽게
        double showAt; readonly Stopwatch sw = Stopwatch.StartNew();
        public Func<bool> Busy;     // 게임 중이면 움직임을 줄인다

        protected Popup(Surface v, bool noActivate)
        {
            V = v; NoActivate = noActivate; Scale_ = Win.Dpi();
            Make("", 0x80000000, 0x80000 | 0x80 | 0x8 | (noActivate ? 0x08000020u : 0), 0, 0, 10, 10);
            onDirty = Queue; V.Dirty = onDirty; Icons.Ready += onDirty;
        }

        /// <summary>창 크기(픽셀)</summary>
        protected abstract Win.SZ Pixels { get; }
        protected Win.PT Spot; // 오른쪽 아래 모서리

        void Queue()
        {
            if (queued || H == IntPtr.Zero) return;
            queued = true; Loop.Post(() => { queued = false; if (Visible) Frame(); });
        }
        public void Data() { if (!Visible) return; V.Refresh(); Frame(); }

        protected void Frame()
        {
            if (drawing || H == IntPtr.Zero) return;
            drawing = true;
            try
            {
                double t = sw.Elapsed.TotalSeconds - showAt;
                float k = Math.Min(1, (float)(t / .2)); k = 1 - (1 - k) * (1 - k) * (1 - k);
                if (Busy != null && Busy()) k = 1;
                Alpha = k; Shift = (1 - k) * 18 * Scale_;
                var p = Pixels;
                V.Still = (Busy != null && Busy()) || (Glance && t > .6 && !V.FxOn); V.Calm = k >= 1 && Environment.TickCount - touch >= 700 && !V.FxOn;
                V.Draw(p.W, p.H, Scale_);
                var pos = new Win.PT(Spot.X - p.W, Spot.Y - p.H + (int)Shift); var src = new Win.PT(0, 0); var b = new Win.BLEND { Alpha = (byte)(255 * Alpha), Fmt = 1 };
                Win.UpdateLayeredWindow(H, IntPtr.Zero, ref pos, ref p, V.C.Dc, ref src, 0, ref b, 2);
                bool more = V.Animating || k < 1; int iv = !more ? 0 : k < 1 || V.FxOn || Environment.TickCount - touch < 700 ? 16 : 50;
                if (iv != running) { running = iv; if (iv == 0) NoTimer(1); else Timer(1, iv); }
            }
            finally { drawing = false; }
        }

        public void Pop(Win.PT anchor)
        {
            Spot = anchor; showAt = sw.Elapsed.TotalSeconds;
            V.Refresh();
            var p = Pixels; Win.SetWindowPos(H, IntPtr.Zero, anchor.X - p.W, anchor.Y - p.H, p.W, p.H, 0x14);
            Frame();
            Win.ShowWindow(H, NoActivate ? 4 : 5); Frame();
            if (!NoActivate) { Win.SetForegroundWindow(H); Win.SetFocus(H); }
        }
        public void Hide() { if (H != IntPtr.Zero) Win.ShowWindow(H, 0); }
        public void Work(string id) { V.BusyOn(id); }
        public void Cheer(Outcome o) { V.Cheer(o); }

        protected override bool On(int m, IntPtr w, IntPtr l, ref IntPtr r)
        {
            switch (m)
            {
                case 0x113: if (Visible) Frame(); else { running = 0; NoTimer(1); } return true;
                case 0x200: { Track(); touch = Environment.TickCount; float x = Win.X(l) / Scale_, y = Win.Y(l) / Scale_; if (V.MouseMove(x, y)) Frame(); Win.SetCursor(V.Clickable(x, y) ? Win.Hand : Win.Arrow); return true; }
                case 0x20: if ((l.ToInt64() & 0xffff) == 1) { r = (IntPtr)1; return true; } return false;
                case 0x2A3: Untrack(); V.MouseLeave(); Frame(); return true;
                case 0x201: touch = Environment.TickCount; if (V.Click(Win.X(l) / Scale_, Win.Y(l) / Scale_)) Frame(); return true;
                case 0x14: r = (IntPtr)1; return true;
                case 0x2: Icons.Ready -= onDirty; V.Dirty = null; V.Dispose(); return true;
            }
            return false;
        }
    }

    internal sealed class PanelWin : Popup
    {
        public readonly PanelView P;
        public Action Hidden; public bool Pinned;
        public PanelWin(Doctor d) : base(new PanelView(d), false) { P = (PanelView)V; }
        protected override Win.SZ Pixels { get { float h = Math.Min(760, P.MaxH) + PanelView.Pad * 2; return new Win.SZ { W = (int)((PanelView.Wd + PanelView.Pad * 2) * Scale_), H = (int)(h * Scale_) }; } }
        public void HideNow() { if (!Visible) return; Hide(); if (Hidden != null) Hidden(); }
        protected override bool On(int m, IntPtr w, IntPtr l, ref IntPtr r)
        {
            if (P == null) return base.On(m, w, l, ref r);
            if (m == 0x6 && (w.ToInt64() & 0xffff) == 0 && !Pinned) { HideNow(); return false; }
            if (m == 0x100 && (int)w == 0x1B) { if (P.Open != null || P.Sub != 0) { P.Open = null; P.Sub = 0; Frame(); } else HideNow(); return true; }
            if (m == 0x84) // 그림자 자리는 뒤 창이 받게
            {
                var p = new Win.PT(Win.X(l), Win.Y(l)); Win.ScreenToClient(H, ref p); Win.RC c; Win.GetClientRect(H, out c); float x = p.X / Scale_, y = p.Y / Scale_, h = c.B / Scale_;
                bool inside = x >= PanelView.Pad && x < PanelView.Pad + PanelView.Wd && y >= h - PanelView.Pad - P.Height && y < h - PanelView.Pad;
                r = (IntPtr)(inside ? 1 : -1); return true;
            }
            return base.On(m, w, l, ref r);
        }
    }

    internal sealed class MiniWin : Popup
    {
        public MiniWin(Doctor d) : base(new MiniView(d), true) { Glance = true; }
        protected override Win.SZ Pixels { get { return new Win.SZ { W = (int)((MiniView.Wd + MiniView.Pad * 2) * Scale_), H = (int)((MiniView.Ht + MiniView.Pad * 2) * Scale_) }; } }
    }

    /// <summary>큰 창. 제목 표시줄을 직접 그린다.</summary>
    internal sealed unsafe class MainWin : Wnd
    {
        public readonly MainView V;
        readonly Doctor D; readonly float scale;
        IntPtr find, font, brush; int brushOf = -1, findH; bool findOn;
        readonly Action onDirty;
        bool queued, drawing; int running;
        int touch = Environment.TickCount; // 만지는 동안만 자주 그린다
        public Func<bool> Busy; public Action Minimized, Small, Closed;
        /// <summary>마지막으로 만진 뒤 지난 시간(ms)</summary>
        public int Quiet { get { return Environment.TickCount - touch; } }
        public bool Iconic { get { return Win.IsIconic(H); } }

        public MainWin(Doctor d)
        {
            D = d; V = new MainView(d); scale = Win.Dpi();
            Win.RC full, a = Win.Work(Win.Mouse(), out full);
            int w = Math.Min(d.M.Set.W > 900 ? d.M.Set.W : (int)(1080 * scale), a.W), h = Math.Min(d.M.Set.H > 600 ? d.M.Set.H : (int)(720 * scale), a.H);
            Make(Tx("렉 감시", "LagWatch"), 0x02CF0000, 0, a.L + (a.W - w) / 2, a.T + (a.H - h) / 2, w, h);
            var m = new Win.MARGINS { B = 1 }; Win.DwmExtendFrameIntoClientArea(H, ref m); Win.SetWindowPos(H, IntPtr.Zero, 0, 0, 0, 0, 0x27);
            onDirty = Redraw; V.Dirty = onDirty; Icons.Ready += onDirty;
            V.Min = () => Win.ShowWindow(H, 6); V.Max = () => Win.ShowWindow(H, Win.IsZoomed(H) ? 9 : 3); V.Close = Close;
            V.PageChanged = Place; V.Confirm = V.AskBox; V.Small = () => { if (Small != null) Small(); };
        }
        public void Show() { Win.ShowWindow(H, Iconic ? 9 : 5); Place(); Win.SetForegroundWindow(H); }
        public void Close() { if (H != IntPtr.Zero) Win.Send(H, 0x10, IntPtr.Zero, IntPtr.Zero); }

        bool Shown { get { return Visible && !Iconic; } }
        void Cores() { D.WantCores = Shown && V.Page == MainView.Now && V.Cores; }
        public void Data() { if (!Shown) return; V.Refresh(); Paint_(); Cores(); }

        void Redraw()
        {
            if (queued || H == IntPtr.Zero) return;
            queued = true; Loop.Post(() => { queued = false; if (H == IntPtr.Zero) return; Place(); Paint_(); });
        }

        // ---- 검색 글 상자: 프로그램 탭을 처음 열 때 만든다 ----

        void Place()
        {
            if (H == IntPtr.Zero) return; Cores();
            bool show = V.Page == MainView.Prog && V.Open == null && !V.Asking; Win.RC c; Win.GetClientRect(H, out c);
            if (show)
            {
                if (find == IntPtr.Zero)
                {
                    font = Win.CreateFontW(-(int)(25 * scale), 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 5, 0, Tx("맑은 고딕", "Segoe UI")); findH = (int)Math.Ceiling(25 * scale * 1.34);
                    find = Win.CreateWindowExW(0, "EDIT", "", 0x40000080, 0, 0, 10, findH, H, (IntPtr)1, Win.GetModuleHandleW(null), IntPtr.Zero);
                    Win.Send(find, 0x30, font, (IntPtr)1); editProc = EditRoute; editOld = Win.SetWindowLongPtr(find, -4, Marshal.GetFunctionPointerForDelegate(editProc)); Loop.Edit = find; Loop.EditKey = EditKey;
                }
                Win.SetWindowPos(find, IntPtr.Zero, (int)(46 * scale), (int)(76 * scale), Math.Max(10, c.W - (int)(240 * scale)), findH, 0x14);
            }
            if (find == IntPtr.Zero || findOn == show) return; findOn = show; Win.ShowWindow(find, show ? 5 : 0); if (show) Win.SetFocus(find); else if (Win.GetFocus() == find) Win.SetFocus(H);
        }
        bool EditKey(int vk)
        {
            if (vk != 0x1B) return false;
            if (Win.GetWindowTextLengthW(find) > 0) Win.SetWindowTextW(find, ""); else Close(); return true;
        }
        delegate IntPtr EditProc(IntPtr h, int m, IntPtr w, IntPtr l); EditProc editProc; IntPtr editOld; bool composing, empty = true;
        /// <summary>빈 글 상자에 안내 글자. 윈도우 것은 어두운 바탕에서 안 보인다.</summary>
        IntPtr EditRoute(IntPtr h, int m, IntPtr w, IntPtr l)
        {
            IntPtr r = Win.CallWindowProcW(editOld, h, m, w, l);
            if (m == 0x10D || m == 0x10E) { composing = m == 0x10D; Win.InvalidateRect(h, IntPtr.Zero, true); }
            else if (m == 0xF && !composing && font != IntPtr.Zero && Win.GetWindowTextLengthW(h) == 0)
            {
                var T = Theme.Cur; IntPtr dc = Win.GetDC(h), old = D2.SelectObject(dc, font); Win.HideCaret(h); Win.SetBkMode(dc, 1); Win.SetTextColor(dc, Bgr(T.Dark ? Col.Hex(0xa4adb8) : Col.Hex(0x3d5563))); string hint = Tx("프로그램 검색", "Search apps"); Win.TextOutW(dc, 2, 0, hint, hint.Length); Win.ShowCaret(h); D2.SelectObject(dc, old); Win.ReleaseDC(h, dc);
            }
            return r;
        }
        static int Bgr(Col c) { return (int)(c.R * 255) | (int)(c.G * 255) << 8 | (int)(c.B * 255) << 16; }
        IntPtr EditColor(IntPtr dc)
        {
            var T = Theme.Cur; Win.RC rc; Win.GetClientRect(H, out rc); var c = T.Bg.To(T.Fg, T.Faint.A); int v = Bgr(c);
            if (v != brushOf) { if (brush != IntPtr.Zero) D2.DeleteObject(brush); brush = Win.CreateSolidBrush(v); brushOf = v; }
            Win.SetTextColor(dc, Bgr(T.Dark ? Col.Hex(0xffffff) : Col.Hex(0x08202f))); Win.SetBkColor(dc, v); return brush;
        }

        // ---- 그리기 ----

        void Paint_()
        {
            Win.RC c; if (drawing || H == IntPtr.Zero || !Win.GetClientRect(H, out c) || c.W < 10) return;
            drawing = true;
            try
            {
                V.Still = Busy != null && Busy(); V.Calm = Environment.TickCount - touch >= 700 && !V.FxOn;
                V.Draw(c.W, c.H, scale);
                Win.InvalidateRect(H, IntPtr.Zero, false); Win.UpdateWindow(H);
                int iv = !V.Animating ? 0 : V.FxOn || Environment.TickCount - touch < 700 ? 16 : 50; if (iv != running) { running = iv; if (iv == 0) NoTimer(1); else Timer(1, iv); }
            }
            finally { drawing = false; }
        }
        void OnPaint()
        {
            Win.RC c; Win.GetClientRect(H, out c);
            if ((V.C.Dc == IntPtr.Zero || V.C.PxW != c.W || V.C.PxH != c.H) && !drawing && c.W > 10) { drawing = true; try { V.Draw(c.W, c.H, scale); } finally { drawing = false; } }
            Win.PAINT ps; IntPtr dc = Win.BeginPaint(H, out ps);
            if (V.C.Dc != IntPtr.Zero) D2.BitBlt(dc, 0, 0, V.C.PxW, V.C.PxH, V.C.Dc, 0, 0, 0x00CC0020);
            Win.EndPaint(H, ref ps);
        }

        protected override bool On(int m, IntPtr w, IntPtr l, ref IntPtr r)
        {
            switch (m)
            {
                case 0xF: OnPaint(); return true;
                case 0x14: r = (IntPtr)1; return true;
                case 0x113: if (V.Animating && Shown) Paint_(); else { running = 0; NoTimer(1); } return true;
                case 0x83: // 테두리 없이 전체를 우리가 그린다
                    if (w == IntPtr.Zero) return false;
                    if (Win.IsZoomed(H)) { var q = (Win.RC*)l; int b = Win.GetSystemMetrics(32) + Win.GetSystemMetrics(92); q->L += b; q->R -= b; q->T += b; q->B -= b; }
                    return true;
                case 0x84:
                {
                    var p = new Win.PT(Win.X(l), Win.Y(l)); Win.ScreenToClient(H, ref p); Win.RC c; Win.GetClientRect(H, out c); int e = (int)(6 * scale);
                    if (!Win.IsZoomed(H))
                    {
                        bool L = p.X < e, R = p.X >= c.W - e, T = p.Y < e, B = p.Y >= c.H - e; int hit = T && L ? 13 : T && R ? 14 : B && L ? 16 : B && R ? 17 : L ? 10 : R ? 11 : T ? 12 : B ? 15 : 0;
                        if (hit != 0) { r = (IntPtr)hit; return true; }
                    }
                    r = (IntPtr)(p.Y < 56 * scale && !V.Clickable(p.X / scale, p.Y / scale) && V.Open == null && !V.Asking ? 2 : 1); return true;
                }
                case 0x24: { var q = (Win.PT*)l; q[3] = new Win.PT((int)(960 * scale), (int)(640 * scale)); return true; }
                case 0x5: if (H == IntPtr.Zero || V == null || V.PageChanged == null) return true; Place(); if ((int)w == 1) { if (Minimized != null) Minimized(); } else Win.InvalidateRect(H, IntPtr.Zero, false); return true;
                case 0x7: if (findOn) { Win.SetFocus(find); return true; } return false;
                case 0x200: { Track(); touch = Environment.TickCount; float x = Win.X(l) / scale, y = Win.Y(l) / scale; if (V.MouseMove(x, y)) Paint_(); Win.SetCursor(V.Clickable(x, y) ? Win.Hand : Win.Arrow); return true; }
                case 0x20: if ((l.ToInt64() & 0xffff) == 1 && w == H) { r = (IntPtr)1; return true; } return false;
                case 0x2A3: Untrack(); V.MouseLeave(); Paint_(); return true;
                case 0x201:
                    touch = Environment.TickCount; if (Win.GetFocus() == find && find != IntPtr.Zero && V.Page != MainView.Prog) Win.SetFocus(H);
                    if (V.Click(Win.X(l) / scale, Win.Y(l) / scale) && H != IntPtr.Zero) { Place(); Paint_(); } return true;
                case 0x20B: if ((w.ToInt64() >> 16 & 0xffff) == 1 && V.Open != null) { V.Open = null; Paint_(); } r = (IntPtr)1; return true;
                case 0x20A: touch = Environment.TickCount; V.Wheel((short)(w.ToInt64() >> 16 & 0xffff)); Paint_(); return true;
                case 0x100:
                    touch = Environment.TickCount; int k = (int)w;
                    if (k == 0x1B) { if (V.Asking) V.AskNo(); else if (V.Open != null) V.Open = null; else { Close(); return true; } Place(); Paint_(); }
                    else if (k >= 0x31 && k <= 0x36) V.Go(k - 0x31);
                    return true;
                case 0x111: if (l == find && (w.ToInt64() >> 16 & 0xffff) == 0x300) { var sb = new StringBuilder(Win.GetWindowTextLengthW(find) + 1); Win.GetWindowTextW(find, sb, sb.Capacity); V.Find = sb.ToString(); if (empty != (sb.Length == 0)) { empty = sb.Length == 0; Win.InvalidateRect(find, IntPtr.Zero, true); } Redraw(); } return true;
                case 0x133: r = EditColor(w); return true;
                case 0x10:
                    if (!Iconic && !Win.IsZoomed(H)) { Win.RC c; Win.GetWindowRect(H, out c); D.M.Set.W = c.W; D.M.Set.H = c.H; D.M.Set.Save(); }
                    Win.DestroyWindow(H); return true;
                case 0x2:
                    D.WantCores = false; Icons.Ready -= onDirty; V.Dirty = null; Loop.Edit = IntPtr.Zero; Loop.EditKey = null; V.Dispose();
                    if (font != IntPtr.Zero) D2.DeleteObject(font); if (brush != IntPtr.Zero) D2.DeleteObject(brush); font = brush = find = IntPtr.Zero; findOn = false;
                    if (Closed != null) Closed(); return true;
            }
            return false;
        }
    }
}
