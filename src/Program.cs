using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using static LagWatch.Lang;

[assembly: AssemblyTitle("LagWatch")]
[assembly: AssemblyProduct("LagWatch")]
[assembly: AssemblyVersion("1.6.0.0")]
[assembly: AssemblyFileVersion("1.6.0.0")]

namespace LagWatch
{
    internal static class Program
    {
        public static bool Again; // 끝난 뒤 다시 켜기
        public static string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LagWatch");
        public static string Id = ""; // 시험할 때만: 켜져 있는 것과 부딪히지 않게

        [STAThread]
        static int Main(string[] args)
        {
            args = Split(args);
            string id = Arg(args, "--id"); if (id != null) { Id = "." + id; Dir = Path.Combine(Path.GetTempPath(), "LagWatch-" + id); }
            Lang.Init(Arg(args, "--lang"), Path.Combine(Dir, "settings.txt"));
            string shot = Arg(args, "--shot");
            if (shot != null)
            {
                try { return Shot(args, shot); }
                catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            }
            if (Has(args, "--quit"))
            {
                EventWaitHandle q;
                if (EventWaitHandle.TryOpenExisting("LagWatch.Quit" + Id, out q)) q.Set();
                return 0;
            }
            string task = Arg(args, "--task");
            if (task != null) return Startup.Task(task == "on") ? 0 : 1;
            string bench = Arg(args, "--bench");
            if (bench != null) return Bench(args, int.Parse(bench));

            bool first;
            using (var mutex = new Mutex(true, "LagWatch.Single" + Id, out first))
            {
                if (!first)
                {
                    EventWaitHandle ev;
                    if (EventWaitHandle.TryOpenExisting("LagWatch.Open" + Id, out ev)) ev.Set();
                    return 0;
                }
                Native.SetProcessDPIAware();
                Directory.CreateDirectory(Dir); Sys.ErrorFile = Path.Combine(Dir, "error.txt");
                new Tray(!Has(args, "--tray"), Arg(args, "--test"), Arg(args, "--stats"), int.Parse(Arg(args, "--breath") ?? "600")).Run();
            }
            if (Again) try { Sys.Open(Sys.Exe, "--tray" + (id != null ? " --id " + id : "")); } catch (Exception ex) { Sys.Note(ex); } // 언어를 바꾸면 다시 켠다
            return 0;
        }

        /// <summary>"--id x" 처럼 한 덩어리로 넘어온 것을 나눈다. 안 나누면 --id 를 못 읽고 켜져 있는 본체를 끌 수 있다.</summary>
        public static string[] Split(string[] a)
        {
            var l = new List<string>();
            foreach (var x in a) { if (x.StartsWith("--") && x.Trim().IndexOf(' ') > 0) l.AddRange(x.Split(new[] { ' ', '	' }, StringSplitOptions.RemoveEmptyEntries)); else l.Add(x); }
            return l.ToArray();
        }
        public static bool Has(string[] a, string name) { return Array.IndexOf(a, name) >= 0; }
        public static string Arg(string[] a, string name) { int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }

        static Monitor Headless(string[] args)
        {
            var set = new Settings(null) { Blame = !Has(args, "--noblame") };
            string g = Arg(args, "--game"); if (g != null) set.Games.Add(g);
            return new Monitor(set, new EventLog(null), new History(Arg(args, "--hist"), true));
        }

        /// <summary>창을 띄우지 않고 화면을 그림 파일로 저장</summary>
        static int Shot(string[] args, string path)
        {
            int sec = int.Parse(Arg(args, "--sec") ?? "4");
            float scale = float.Parse(Arg(args, "--scale") ?? "1", CultureInfo.InvariantCulture);
            var sz = (Arg(args, "--size") ?? "1080x720").Split('x');
            var m = Headless(args);
            var d = new Doctor(m, Arg(args, "--data"));
            m.Visible = true;
            m.AfterStep = d.Tick;
            string demo = Arg(args, "--demo");
            m.Start();
            for (int i = 0; i < sec * 4; i++) Thread.Sleep(250);
            if (Has(args, "--temp")) d.Temp.Scan();
            d.Boost.Read();
            m.AfterStep = null; Thread.Sleep(200);
            if (demo != null) Demo.Causes(d, demo);
            string only = Arg(args, "--only"); if (only != null) lock (m.Sync) Demo.Only(m, only.Split(',')); // 설명 그림에 이 PC에서 켠 것을 다 보이지 않게
            if (Arg(args, "--game") != null) lock (m.Sync) if (m.Game != null) m.Game.Create = DateTime.UtcNow.AddMinutes(-22).ToFileTimeUtc();
            Theme.Cur = Has(args, "--light") ? Theme.Day : Theme.Night;
            Surface v; int w = (int)(int.Parse(sz[0]) * scale), h = (int)(int.Parse(sz[1]) * scale);
            string kind = Arg(args, "--view") ?? "main";
            if (kind == "panel") { var p = new PanelView(d) { Tab = int.Parse(Arg(args, "--tab") ?? "0"), Open = Arg(args, "--open"), Sub = int.Parse(Arg(args, "--sub") ?? "0") }; v = p; w = (int)((PanelView.Wd + PanelView.Pad * 2) * scale); h = (int)((720 + PanelView.Pad * 2) * scale); }
            else if (kind == "mini") { v = new MiniView(d); w = (int)((MiniView.Wd + MiniView.Pad * 2) * scale); h = (int)((MiniView.Ht + MiniView.Pad * 2) * scale); }
            else
            {
                var q = new MainView(d) { Page = int.Parse(Arg(args, "--page") ?? "0"), Cores = Has(args, "--cores"), Open = Arg(args, "--open"), Find = Arg(args, "--find") ?? "" };
                if (Arg(args, "--metric") != null) q.Metric = int.Parse(Arg(args, "--metric"));
                if (Arg(args, "--hours") != null) q.Hours = int.Parse(Arg(args, "--hours"));
                if (Arg(args, "--sort") != null) q.sort = int.Parse(Arg(args, "--sort"));
                q.OpenGroup = Arg(args, "--expand"); v = q; if (Arg(args, "--run") != null) q.Run(Arg(args, "--run"));
                if (q.Cores) { d.WantCores = true; m.AfterStep = d.Tick; Thread.Sleep(3500); m.AfterStep = null; Thread.Sleep(300); }
            }
            v.Still = true;
            v.Refresh();
            foreach (var g in v.S.Groups) foreach (var a in g.Apps) { if (a.Commit > 20L << 20 || a.Cores > .02) lock (m.Sync) m.EnsureInfo(a); if (a == g.Main) Icons.Now(a.Path); }
            v.Refresh();
            v.Draw(w, h, scale);
            string mouse = Arg(args, "--mouse"); if (mouse != null) { var q = mouse.Split(','); v.MouseMove(int.Parse(q[0]), int.Parse(q[1])); v.Draw(w, h, scale); }
            string click = Arg(args, "--click"); if (click != null) { var q = click.Split(','); v.Click(int.Parse(q[0]), int.Parse(q[1])); v.Draw(w, h, scale); }
            v.Draw(w, h, scale);
            var sw = Stopwatch.StartNew(); for (int i = 0; i < 20; i++) v.Draw(w, h, scale);
            Console.WriteLine("{0}: {1:F2}ms, 글자 조각 {2}, 살아 있는 것 {3}", Path.GetFileName(path), sw.Elapsed.TotalMilliseconds / 20, v.C.TextCount, D2.Live);
            Png.Save(path, v.C.Bits, w, h, kind == "main" ? -1 : 0x56707a);
            foreach (var c in d.Causes) Console.WriteLine("  [{0}] {1} | {2} | {3}", c.Level, c.Title, c.Why, string.Join(", ", c.Acts.ConvertAll(a => a.Label)));
            v.Dispose(); d.Dispose(); m.Stop();
            return 0;
        }

        /// <summary>창 없이 돌리며 이 프로그램 자신의 사용량을 잰다</summary>
        static int Bench(string[] args, int sec)
        {
            var m = Headless(args); var d = new Doctor(m, null);
            m.Visible = Has(args, "--visible"); d.WantCores = Has(args, "--cores");
            System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.Batch;
            m.AfterStep = t => { d.Tick(t); Tidy.Step(); };
            m.Start();
            Thread.Sleep(6000); long ev0 = d.Blame.Events;
            double c0 = Self.Cpu; int g0 = GC.CollectionCount(0), g2 = GC.CollectionCount(2);
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < sec) Thread.Sleep(1000);
            double used = Self.Cpu - c0, t1 = sw.Elapsed.TotalSeconds;
            if (d.Blame.Running) Console.WriteLine("스레드 교체 초당 {0:F0}번", (d.Blame.Events - ev0) / t1);
            Console.WriteLine("CPU {0:F3}s / {1:F0}s = 코어 하나의 {2:F2}% (전체의 {3:F3}%)", used, t1, used / t1 * 100, used / t1 * 100 / Environment.ProcessorCount);
            Console.WriteLine("커밋 {0:F1}MB, 램 {1:F1}MB, GC0 {2}번, GC2 {3}번, 마지막 표본 {4:F1}ms, 게임 {5}, {6:F0}fps 최대 {7:F1}ms 끊김 {8}", Self.Commit, Self.Ram, GC.CollectionCount(0) - g0, GC.CollectionCount(2) - g2, m.LastStepMs, m.Game != null, d.Fps, d.MaxMs, d.Hitch1m);
            d.Dispose(); m.Stop();
            return 0;
        }
    }

    /// <summary>그림 저장(시험용). 압축 없이 쓴다.</summary>
    internal static unsafe class Png
    {
        static uint[] table;
        static uint Crc(uint c, byte[] b, int n) { if (table == null) { var t = new uint[256]; for (uint i = 0; i < 256; i++) { uint k = i; for (int j = 0; j < 8; j++) k = (k & 1) != 0 ? 0xEDB88320 ^ k >> 1 : k >> 1; t[i] = k; } table = t; } for (int i = 0; i < n; i++) c = table[(c ^ b[i]) & 255] ^ c >> 8; return c; }
        static void Be(Stream s, uint v) { s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
        static void Chunk(Stream s, string name, byte[] d, int n) { Be(s, (uint)n); var h = System.Text.Encoding.ASCII.GetBytes(name); s.Write(h, 0, 4); s.Write(d, 0, n); Be(s, Crc(Crc(0xFFFFFFFF, h, 4), d, n) ^ 0xFFFFFFFF); }
        /// <summary>back 이 0 이상이면 그 색 위에 얹는다.</summary>
        public static void Save(string path, IntPtr bits, int w, int h, int back)
        {
            var raw = new byte[(w * 3 + 1) * h]; int o = 0; uint* p = (uint*)bits;
            for (int y = 0; y < h; y++) { raw[o++] = 0; for (int x = 0; x < w; x++) { uint v = *p++, inv = back < 0 ? 0 : 255 - (v >> 24); for (int k = 16; k >= 0; k -= 8) raw[o++] = (byte)Math.Min(255, (v >> k & 255) + (back < 0 ? 0 : (uint)(back >> k & 255) * inv / 255)); } }
            var z = new MemoryStream(raw.Length + raw.Length / 1000 + 64); z.WriteByte(0x78); z.WriteByte(1); uint a = 1, b = 0;
            for (int i = 0; i < raw.Length; i += 65535) { int n = Math.Min(65535, raw.Length - i); z.WriteByte((byte)(i + n >= raw.Length ? 1 : 0)); z.WriteByte((byte)n); z.WriteByte((byte)(n >> 8)); z.WriteByte((byte)~n); z.WriteByte((byte)(~n >> 8)); z.Write(raw, i, n); }
            foreach (byte q in raw) { a = (a + q) % 65521; b = (b + a) % 65521; } Be(z, b << 16 | a);
            using (var s = File.Create(path))
            {
                s.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 13, 10, 26, 10 }, 0, 8); var hd = new MemoryStream(); Be(hd, (uint)w); Be(hd, (uint)h); hd.Write(new byte[] { 8, 2, 0, 0, 0 }, 0, 5);
                Chunk(s, "IHDR", hd.ToArray(), 13); Chunk(s, "IDAT", z.GetBuffer(), (int)z.Length); Chunk(s, "IEND", new byte[0], 0);
            }
        }
    }

    /// <summary>로그인할 때 켜기. UAC가 켜져 있으면 관리자 권한으로 켜지는 작업을 한 번만 등록한다.</summary>
    internal static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "LagWatch";
        static bool Reg { get { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(Name) != null; } }
        static bool HasTask { get { return File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"Tasks\" + Name)); } }
        public static bool On { get { try { return Reg || HasTask; } catch (Exception) { return false; } } }

        public static void Set(bool on)
        {
            try
            {
                if (on)
                {
                    if (Fixer.UacOn && Elevate("--task on")) return;
                    using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey)) k.SetValue(Name, "\"" + Sys.Exe + "\" --tray");
                }
                else
                {
                    using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey)) k.DeleteValue(Name, false);
                    if (HasTask) { if (Cleaner.Elevated) Task(false); else Elevate("--task off"); }
                }
            }
            catch (Exception) { }
        }
        static bool Elevate(string args)
        {
            try { return Sys.Open(Sys.Exe, args, null, "runas", 20000) == 0; }
            catch (Exception) { return false; } // 허용하지 않음
        }
        public static bool Task(bool on)
        {
            string a = on ? "/create /f /tn " + Name + " /sc onlogon /rl highest /tr \"\\\"" + Sys.Exe + "\\\" --tray\"" : "/delete /f /tn " + Name;
            return Sys.Open("schtasks.exe", a, null, null, 15000, true) == 0;
        }
    }

    internal sealed unsafe class Tray : Wnd
    {
        readonly Monitor m; readonly Doctor d;
        readonly EventWaitHandle open, quit;
        readonly List<string[]> pending = new List<string[]>();
        readonly int taskbar, breathMs; readonly string stats, test;
        MainWin main; PanelWin panel; MiniWin mini;
        IntPtr iconHandle; int iconKey = -1; Win.PT hoverAt; int hoverFrom; bool hovering, menuOn, posted, ending;
        int restAt, breathAt = Environment.TickCount, panelClosed = Environment.TickCount - 10000, cycleN;

        public Tray(bool show, string test, string stats, int breath)
        {
            this.test = test; this.stats = stats; breathMs = breath * 1000;
            Win.DarkMenus(); taskbar = Win.RegisterWindowMessageW("TaskbarCreated");
            Make("LagWatchTray", 0x80000000, 0x80, 0, 0, 0, 0); Loop.Host = H;
            Theme.FromWindows();
            m = new Monitor(new Settings(Path.Combine(Program.Dir, "settings.txt")), new EventLog(Path.Combine(Program.Dir, "events.tsv")), new History(Path.Combine(Program.Dir, "h")));
            d = new Doctor(m, Program.Dir);
            System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.Batch;
            m.AfterStep = t => { try { d.Tick(t); } catch (Exception ex) { Sys.Note(ex); } Tidy.Step(); if (!posted && (main != null || panel != null || mini != null)) { posted = true; Loop.Post(() => { posted = false; Data(); }); } };
            d.Changed = () => Loop.Post(Data);
            d.Fix.Changed = () => Loop.Post(Data);
            SetIcon(); Notify(0);
            m.Rules.Notify = (title, text) => Loop.Post(() => { pending.Add(new[] { title, text }); Deliver(); });
            open = new EventWaitHandle(false, EventResetMode.AutoReset, "LagWatch.Open" + Program.Id);
            quit = new EventWaitHandle(false, EventResetMode.AutoReset, "LagWatch.Quit" + Program.Id);
            Timer(1, 5000); if (stats != null) Timer(3, 20000);
            m.Start();
            if (test == "panel") { TogglePanel(); panel.Pinned = true; }
            else if (test == "mini") { mini = new MiniWin(d); See2(true); mini.Pop(new Win.PT(700, 600)); }
            else if (test != null && test.StartsWith("cycle")) Timer(4, test == "cycle" ? 700 : 250);
            else if (show) OpenMain(-1); else Trim();
        }
        public void Run() { Loop.Run(new[] { open.SafeWaitHandle.DangerousGetHandle(), quit.SafeWaitHandle.DangerousGetHandle() }, i => { if (i == 0) OpenMain(-1); else Quit(); }); GC.KeepAlive(this); }

        protected override bool On(int msg, IntPtr w, IntPtr l, ref IntPtr r)
        {
            if (msg == Loop.Msg) { Loop.Drain(); return true; }
            if (msg == 0x8001) { FromTray((int)(l.ToInt64() & 0xffff)); return true; }
            if (msg == 0x113) { Tick((int)w); return true; }
            if (msg == 0x16 && w != IntPtr.Zero) { Quit(); return true; } // 로그아웃, 종료
            if (msg == taskbar && taskbar != 0 && m != null) { Notify(0); return true; } // 탐색기가 다시 켜짐
            return false;
        }
        void FromTray(int e)
        {
            if (e == 0x200) Hover();
            else if (e == 0x202) { HideMini(); TogglePanel(); }
            else if (e == 0x205) { HideMini(); Menu(); }
            else if (e == 0x405) OpenMain(MainView.Sum);
        }
        void Tick(int id)
        {
            if (id == 1) { Deliver(); SetIcon(); if (restAt != 0 && Environment.TickCount - restAt > 0) Rest(); Breathe(); }
            else if (id == 2) HoverTick();
            else if (id == 3) Stats();
            else if (id == 4) Cycle();
        }

        bool InGame() { return m.Game != null; }
        void Data() { if (main != null) main.Data(); if (panel != null) panel.Data(); if (mini != null) mini.Data(); }
        void See() { m.Visible = (main != null && main.Visible) || (panel != null && panel.Visible) || (mini != null && mini.Visible); if (m.Visible) m.Wake(); }

        /// <summary>전체 화면을 쓰는 중이면 끝날 때까지 알림을 미룬다.</summary>
        void Deliver()
        {
            if (pending.Count == 0 || m.Game != null) return;
            int st;
            if (Native.SHQueryUserNotificationState(out st) == 0 && (st == 2 || st == 3 || st == 4)) return;
            string title = pending[0][0], text = pending[0][1];
            if (pending.Count > 1) text += Tx("\n외 알림 {0}개", "\n+{0} more", pending.Count - 1);
            pending.Clear();
            Balloon(title, text);
        }

        // ---- 트레이 ----

        Win.NID Nid() { var n = new Win.NID { H = H, Id = 1, Msg = 0x8001, Tip = "", Info = "", Title = "" }; n.Cb = Marshal.SizeOf(typeof(Win.NID)); return n; }
        /// <summary>0 넣기, 1 바꾸기, 2 빼기</summary>
        void Notify(int what) { var n = Nid(); n.Flags = 3; n.Icon = iconHandle; Win.Shell_NotifyIconW(what, ref n); }
        void Balloon(string title, string text) { var n = Nid(); n.Flags = 0x10; n.Title = title.Length > 60 ? title.Substring(0, 60) : title; n.Info = text.Length > 250 ? text.Substring(0, 250) : text; Win.Shell_NotifyIconW(1, ref n); }

        void Menu()
        {
            if (menuOn) return; menuOn = true; IntPtr p = Win.CreatePopupMenu();
            try
            {
                Action<int, string, bool> add = (id, s, on) => Win.AppendMenuW(p, s == null ? 0x800 : on ? 8 : 0, (IntPtr)id, s);
                add(1, Tx("열기", "Open"), false); add(2, Tx("램 청소", "Clean RAM"), false); add(3, Tx("임시 파일", "Temp files"), false); add(0, null, false); add(4, Tx("알림", "Notifications"), m.Set.Notify); add(5, Tx("윈도우 시작 시 실행", "Start with Windows"), Startup.On); add(6, Tx("기록 폴더 열기", "Open data folder"), false); add(8, En ? "한국어" : "English", false); add(0, null, false); add(7, Tx("종료", "Exit"), false);
                var at = Win.Mouse(); Win.SetForegroundWindow(H); int k = Win.TrackPopupMenuEx(p, 0x100 | 0x2 | 0x20, at.X, at.Y, H, IntPtr.Zero); Win.PostMessageW(H, 0, IntPtr.Zero, IntPtr.Zero);
                if (k == 1) OpenMain(-1);
                else if (k == 2) ThreadPool.QueueUserWorkItem(o => { string r = null; try { r = d.Do("trim"); } catch (Exception ex) { Sys.Note(ex); } if (r != null) Loop.Post(() => Balloon(Tx("램 청소", "Clean RAM"), r)); });
                else if (k == 3) OpenMain(MainView.Clean);
                else if (k == 4) { m.Set.Notify = !m.Set.Notify; m.Set.Save(); }
                else if (k == 5) Startup.Set(!Startup.On);
                else if (k == 6) try { Sys.Open("explorer.exe", "\"" + Program.Dir + "\""); } catch (Exception) { }
                else if (k == 7) Quit();
                else if (k == 8) { m.Set.Lang = En ? "ko" : "en"; m.Set.Save(); Program.Again = true; Quit(); }
            }
            finally { Win.DestroyMenu(p); menuOn = false; }
        }

        /// <summary>아이콘: CPU, 가상메모리, 램 막대</summary>
        void SetIcon()
        {
            double cpu, ram, vm;
            lock (m.Sync) { cpu = m.TotalCpu; ram = m.RamTotal > 0 ? 1 - (double)m.RamAvail / m.RamTotal : 0; vm = m.CommitLimit > 0 ? (double)m.Commit / m.CommitLimit : 0; }
            int[] lv = { Theme.Lv(cpu * 100), Theme.Lv(vm * 100), Theme.Lv(ram * 100) };
            int a = Q(cpu), b = Q(vm), e = Q(ram), key = a | b << 4 | e << 8 | lv[0] << 12 | lv[1] << 14 | lv[2] << 16;
            if (key == iconKey) return;
            bool first = iconKey < 0; iconKey = key; int n = Math.Max(16, Win.GetSystemMetrics(49));
            uint* px = stackalloc uint[n * n]; for (int i = 0; i < n * n; i++) px[i] = 0;
            uint[] fgs = { 0xFFFFFFFF, 0xFFFF9F0A, 0xFFFF453A }; uint bg = 0x46FFFFFF; int w = Math.Max(3, n * 4 / 16), gap = Math.Max(1, (n - w * 3) / 2); if (gap > n / 8) gap = n / 8; int x0 = (n - (w * 3 + gap * 2)) / 2;
            for (int i = 0; i < 3; i++)
            {
                int x = x0 + i * (w + gap), hh = Math.Max(1, (i == 0 ? a : i == 1 ? b : e) * (n - 2) / 12);
                for (int y = 1; y < n - 1; y++) for (int k = 0; k < w; k++) px[y * n + x + k] = y >= n - 1 - hh ? fgs[lv[i]] : bg;
            }
            byte* mask = stackalloc byte[n * 8 * n / 8 + 64]; for (int i = 0; i < n * n + 64; i++) mask[i] = 0;
            var ii = new Win.ICONINFO { Icon = 1, Mask = Win.CreateBitmap(n, n, 1, 1, mask), Color = Win.CreateBitmap(n, n, 1, 32, px) }; IntPtr ic = Win.CreateIconIndirect(ref ii); D2.DeleteObject(ii.Mask); D2.DeleteObject(ii.Color);
            IntPtr old = iconHandle; iconHandle = ic; if (!first) Notify(1); if (old != IntPtr.Zero) Native.DestroyIcon(old);
        }
        static int Q(double v) { return Math.Max(0, Math.Min(12, (int)Math.Round(v * 12))); }

        // ---- 마우스를 올렸을 때 ----

        void Hover()
        {
            if ((panel != null && panel.Visible) || menuOn || Native.IdleMs() > 1500) return; // 아이콘이 마우스 밑에 생긴 것뿐이면 안 띄운다
            hoverAt = Win.Mouse();
            if (mini != null) return;
            if (!hovering) { hovering = true; hoverFrom = Environment.TickCount; Timer(2, 150); }
        }
        void StopHover() { hovering = false; NoTimer(2); }
        void HoverTick()
        {
            var p = Win.Mouse(); bool near = Math.Abs(p.X - hoverAt.X) < 14 && Math.Abs(p.Y - hoverAt.Y) < 14;
            if (mini == null)
            {
                if (!near) { StopHover(); return; }
                if (Environment.TickCount - hoverFrom < 320) return;
                mini = new MiniWin(d) { Busy = InGame };
                See2(true); mini.Pop(Corner(MiniView.Pad, true));
            }
            else if (!near || Native.IdleMs() > 30000) HideMini();
        }
        void HideMini() { StopHover(); if (mini == null) return; mini.Hide(); mini.Destroy(); mini = null; See(); restAt = Environment.TickCount + 20000; if (restAt == 0) restAt = 1; }

        /// <summary>열린 창이 없으면 그리기에 쓰던 것을 모두 놓는다.</summary>
        void Rest()
        {
            if (main != null || panel != null || mini != null) return;
            restAt = 0; Icons.Clear(); D2.Shutdown(); Trim();
        }
        /// <summary>큰 창을 오래 열어 두면 10분마다 그리기 캐시와 힙의 빈 조각을 놓는다.</summary>
        void Breathe()
        {
            if (main == null || !main.Visible) { breathAt = Environment.TickCount; return; }
            int past = Environment.TickCount - breathAt;
            if (past < breathMs || main.Quiet < 2000 && past < breathMs * 3 / 2) return;
            breathAt = Environment.TickCount;
            main.V.C.Relax(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Native.HeapOptimize(); main.Data();
        }

        void See2(bool on) { m.Visible = on; m.Wake(); }

        /// <summary>작업 표시줄 옆, 트레이 쪽 구석</summary>
        Win.PT Corner(float pad, bool atMouse)
        {
            var c = Win.Mouse(); Win.RC full, a = Win.Work(c, out full); float s = Win.Dpi(); int gap = (int)(12 * s), pd = (int)(pad * s);
            int right = a.R - gap + pd, bottom = a.B - gap + pd;
            if (atMouse) right = Math.Min(right, c.X + (int)(150 * s) + pd);
            if (a.T > full.T) bottom = a.T + gap + (int)((atMouse ? MiniView.Ht : 720) * s) + pd; // 작업 표시줄이 위에 있을 때
            return new Win.PT(right, bottom);
        }

        // ---- 작은 창, 큰 창 ----

        void TogglePanel()
        {
            if (panel != null && panel.Visible) { panel.HideNow(); return; }
            if (Environment.TickCount - panelClosed < 300) return; // 방금 다른 곳을 눌러 닫힘
            if (panel == null)
            {
                panel = new PanelWin(d) { Busy = InGame };
                panel.P.OpenMain = () => { panel.HideNow(); OpenMain(-1); };
                panel.P.OpenIn = p => { panel.HideNow(); OpenMain(p == "clean" ? MainView.Clean : p == "game" || p == "round" ? MainView.Game : MainView.Sum); if (p == "round" && main != null) main.V.Run("page:round"); };
                panel.Hidden = () => { panelClosed = Environment.TickCount; Loop.Post(() => { if (panel != null && !panel.Visible) { panel.Destroy(); panel = null; See(); Rest(); } }); };
            }
            Theme.FromWindows();
            Win.RC full, a = Win.Work(Win.Mouse(), out full); panel.P.MaxH = a.H / Win.Dpi() - 24;
            panel.P.Open = null; panel.P.Sub = 0; panel.P.Tab = 0;
            See2(true);
            panel.Pop(Corner(PanelView.Pad, false));
        }

        void OpenMain(int page)
        {
            Theme.FromWindows();
            if (main == null)
            {
                main = new MainWin(d) { Busy = InGame };
                main.Small = () => { if (main == null) return; main.Close(); Loop.Post(() => { if (panel == null || !panel.Visible) TogglePanel(); }); };
                main.Minimized = () => { if (main == null) return; main.V.C.Sleep(); Icons.Clear(); Trim(); };
                main.Closed = () => { main = null; See(); Loop.Post(Rest); };
            }
            if (page >= 0) main.V.Go(page);
            See2(true);
            main.V.Refresh();
            main.Show();
        }

        /// <summary>시험: 안에서 들고 있는 것의 수를 적는다.</summary>
        void Stats()
        {
            File.AppendAllText(stats, string.Join("\t", DateTime.Now.ToString("HH:mm:ss"), "커밋", Self.Commit.ToString("F1"), "램", Self.Ram.ToString("F1"), "관리", (GC.GetTotalMemory(false) / 1048576.0).ToString("F2"), "DW", D2.Live, "캔버스", Canvas.Count, "글자", Glyphs.Count, Glyphs.Bytes / 1024, "움직임", main == null ? 0 : main.V.AnimCount, "아이콘", Icons.Count, "GC2", GC.CollectionCount(2), "프로세스", m.Procs.Count, "앱", m.Apps.Count) + "\n");
        }

        /// <summary>시험: 창을 계속 여닫는다.</summary>
        void Cycle()
        {
            int n = ++cycleN;
            if (test == "cyclemain") { if (main == null) OpenMain(n / 2 % 6); else main.Close(); }
            else if (test == "cyclepanel") { if (panel == null) { TogglePanel(); if (panel != null) panel.Pinned = true; } else panel.HideNow(); }
            else if (test == "cyclemini") { if (mini == null) { mini = new MiniWin(d); See2(true); mini.Pop(new Win.PT(700, 600)); } else { HideMini(); Rest(); } }
            else if (test == "cycleicon") { iconKey = 1 << 20; SetIcon(); }
            else if (test == "cyclerest") Rest();
            else
            {
                int k = n % 6;
                if (k == 0) OpenMain(n / 6 % 6); else if (k == 2) { if (main != null) main.Close(); } else if (k == 3) { TogglePanel(); if (panel != null) panel.Pinned = true; } else if (k == 4) { if (panel != null) panel.HideNow(); } else if (k == 5) { mini = new MiniWin(d); See2(true); mini.Pop(new Win.PT(700, 600)); } else if (k == 1) { HideMini(); }
            }
        }

        static void Trim()
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Native.HeapOptimize();
            Native.SetProcessWorkingSetSize(Native.GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
        }

        void Quit()
        {
            if (ending) return; ending = true;
            HideMini(); if (panel != null) { panel.Hidden = null; panel.Destroy(); panel = null; } if (main != null) main.Close();
            Notify(2);
            m.AfterStep = null; m.Stop(); d.Dispose(); m.Set.Save();
            if (iconHandle != IntPtr.Zero) Native.DestroyIcon(iconHandle);
            Win.PostQuitMessage(0);
        }
    }
}
