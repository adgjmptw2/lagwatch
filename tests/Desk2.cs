using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

/// <summary>보이지 않는 데스크톱에서 실제 실행 파일을 켜고 눌러 본다. 켜져 있는 본체와는 이름을 달리해 부딪히지 않는다.</summary>
internal static class Desk2
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SI { public int cb; public string res, desktop, title; public int x, y, w, h, cx, cy, fill, flags; public short show, r2; public IntPtr r3, i, o, e; }
    [StructLayout(LayoutKind.Sequential)] struct PI { public IntPtr p, t; public int pid, tid; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int l, t, r, b; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateDesktopW(string n, string d, IntPtr m, int f, uint a, IntPtr s);
    [DllImport("user32.dll")] static extern bool SetThreadDesktop(IntPtr h);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CreateProcessW(string app, StringBuilder cmd, IntPtr a, IntPtr b, bool inh, uint fl, IntPtr env, string dir, ref SI si, out PI pi);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumDesktopWindows(IntPtr d, EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern int GetGuiResources(IntPtr h, int flag);
    static IntPtr desk; static int pass, fail; static string deskName = "lagwatch-test";
    static void Ok(bool c, string n) { if (c) pass++; else fail++; Console.WriteLine((c ? "  통과 " : "  실패 ") + n); }
    static Process Run(string exe, string args)
    {
        var si = new SI { desktop = deskName }; si.cb = Marshal.SizeOf(typeof(SI)); PI pi;
        if (!CreateProcessW(null, new StringBuilder("\"" + exe + "\" " + args), IntPtr.Zero, IntPtr.Zero, false, 0x4000, IntPtr.Zero, null, ref si, out pi)) throw new Exception("run " + Marshal.GetLastWin32Error());
        return Process.GetProcessById(pi.pid);
    }
    static IntPtr Find(int pid, string title, int minW)
    {
        IntPtr f = IntPtr.Zero;
        EnumDesktopWindows(desk, (h, l) =>
        {
            uint p; GetWindowThreadProcessId(h, out p); if (p != pid || !IsWindowVisible(h)) return true;
            var s = new StringBuilder(64); GetWindowTextW(h, s, 64); RECT r; GetWindowRect(h, out r);
            if (s.ToString() == title && r.r - r.l >= minW) f = h; return true;
        }, IntPtr.Zero);
        return f;
    }
    static void Shot(IntPtr h, string png) { RECT r; GetWindowRect(h, out r); using (var b = new Bitmap(Math.Max(1, r.r - r.l), Math.Max(1, r.b - r.t))) { using (var g = Graphics.FromImage(b)) { IntPtr dc = g.GetHdc(); PrintWindow(h, dc, 2); g.ReleaseHdc(dc); } b.Save(png); } }
    static void Click(IntPtr h, int x, int y) { IntPtr l = (IntPtr)((y << 16) | x); PostMessageW(h, 0x200, IntPtr.Zero, l); Thread.Sleep(120); PostMessageW(h, 0x201, (IntPtr)1, l); PostMessageW(h, 0x202, IntPtr.Zero, l); Thread.Sleep(900); }
    static string Mem(Process p) { p.Refresh(); return string.Format("커밋 {0:F1}MB 램 {1:F1}MB 핸들 {2} 스레드 {3} GDI {4} USER {5}", p.PrivateMemorySize64 / 1048576.0, p.WorkingSet64 / 1048576.0, p.HandleCount, p.Threads.Count, GetGuiResources(p.Handle, 0), GetGuiResources(p.Handle, 1)); }

    static void Cpu(Process p, string what) { p.Refresh(); var c = p.TotalProcessorTime; Thread.Sleep(15000); p.Refresh(); Console.WriteLine("  {0}: 코어 하나의 {1:F1}%", what, (p.TotalProcessorTime - c).TotalSeconds / 15 * 100); }
    static int Main(string[] a)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        string exe = a[0], dir = a[1]; int cycle = a.Length > 2 ? int.Parse(a[2]) : 60;
        if (a.Length > 3) deskName += a[3];
        desk = CreateDesktopW(deskName, null, IntPtr.Zero, 0, 0x10000000, IntPtr.Zero); if (desk == IntPtr.Zero) { Console.WriteLine("데스크톱을 못 만듦"); return 2; }
        SetThreadDesktop(desk);
        if (a.Length > 3)
        {
            var q = Run(exe, "--id " + a[3] + " --tray --test " + a[3]); var w0 = Stopwatch.StartNew(); double lo2 = 1e9; int hl2 = 9999; double sp = cycle / 8.0, at = sp;
            while (w0.Elapsed.TotalSeconds < cycle) { Thread.Sleep(120); if (q.HasExited) { Console.WriteLine("끝나 버림"); break; } q.Refresh(); lo2 = Math.Min(lo2, q.PrivateMemorySize64 / 1048576.0); hl2 = Math.Min(hl2, q.HandleCount); if (w0.Elapsed.TotalSeconds < at) continue; Console.WriteLine("  {0} {1,4:F0}초 최저 커밋 {2:F1}MB 핸들 {3} GDI {4} 스레드 {5}", a[3], w0.Elapsed.TotalSeconds, lo2, hl2, GetGuiResources(q.Handle, 0), q.Threads.Count); at += sp; lo2 = 1e9; hl2 = 9999; }
            if (!q.HasExited) { Run(exe, "--id " + a[3] + " --quit").WaitForExit(4000); if (!q.WaitForExit(6000)) q.Kill(); } CloseDesktop(desk); return 0;
        }
        string data = Path.Combine(Path.GetTempPath(), "LagWatch-dt"); try { Directory.Delete(data, true); } catch (Exception) { }
        Process p = null;
        try
        {
            p = Run(exe, "--id dt"); Thread.Sleep(5000);
            IntPtr w = Find(p.Id, "렉 감시", 600); Ok(w != IntPtr.Zero, "켜면 큰 창이 열림"); Shot(w, dir + "\\dk0.png");
            Console.WriteLine("  창 열림 " + Mem(p));
            Cpu(p, "지금 화면 열어 둠");
            int[] tx = { 55, 115, 190, 250, 325, 383 };
            for (int i = 1; i < 6; i++) { Click(w, tx[i], 30); Thread.Sleep(700); Shot(w, dir + "\\dk" + i + ".png"); }
            Ok(Find(p.Id, "렉 감시", 600) != IntPtr.Zero, "탭 여섯 개");
            Click(w, 55, 30); Click(w, 480, 520); Thread.Sleep(900); Shot(w, dir + "\\dk5.png");
            Click(w, 200, 400);
            Click(w, 172, 193); Thread.Sleep(3500); Cpu(p, "코어 보기 열어 둠"); Shot(w, dir + "\\dk6.png");
            PostMessageW(w, 0x112, (IntPtr)0xF020, IntPtr.Zero); Thread.Sleep(4000); Console.WriteLine("  최소화 " + Mem(p)); PostMessageW(w, 0x112, (IntPtr)0xF120, IntPtr.Zero); Thread.Sleep(1500); Shot(w, dir + "/dk7.png"); Ok(!p.HasExited && Find(p.Id, "렉 감시", 600) != IntPtr.Zero, "최소화했다 다시 열기"); Console.WriteLine("  다시 엶 " + Mem(p));
            PostMessageW(w, 0x10, IntPtr.Zero, IntPtr.Zero); Thread.Sleep(2500);
            p.Refresh(); Ok(!p.HasExited && Find(p.Id, "렉 감시", 600) == IntPtr.Zero, "닫아도 계속 켜져 있음"); Console.WriteLine("  창 닫음 " + Mem(p));
            var p2 = Run(exe, "--id dt"); Ok(p2.WaitForExit(5000), "두 번째 실행은 바로 끝남"); Thread.Sleep(1800); w = Find(p.Id, "렉 감시", 600); Ok(w != IntPtr.Zero, "대신 원래 창이 열림");
            PostMessageW(w, 0x10, IntPtr.Zero, IntPtr.Zero); Thread.Sleep(2000);
            p.Refresh(); var c0 = p.TotalProcessorTime; Thread.Sleep(30000); p.Refresh(); Console.WriteLine("  창 닫고 30초: 코어 하나의 {0:F2}%  {1}", (p.TotalProcessorTime - c0).TotalSeconds / 30 * 100, Mem(p));
            Run(exe, "--id dt --quit").WaitForExit(4000); Ok(p.WaitForExit(8000), "--quit 으로 조용히 끝남");
            foreach (var k in new[] { "panel", "mini" })
            {
                p = Run(exe, "--id dt --tray --test " + k); Thread.Sleep(5000); IntPtr h = IntPtr.Zero; EnumDesktopWindows(desk, (x, l) => { uint q; GetWindowThreadProcessId(x, out q); RECT r; GetWindowRect(x, out r); if (q == p.Id && IsWindowVisible(x) && r.r - r.l > 250) h = x; return true; }, IntPtr.Zero);
                Ok(h != IntPtr.Zero, k + " 이 뜸"); if (h != IntPtr.Zero && k == "panel") { RECT r; GetWindowRect(h, out r); int ph = r.b - r.t; Click(h, 44 + 160, ph - 44 - 230); Click(h, 44 + 100, ph - 44 - 120); Ok(!p.HasExited, "작은 창 누르기"); }
                Cpu(p, k + " 열어 둠");
                Console.WriteLine("  " + k + " " + Mem(p)); Run(exe, "--id dt --quit").WaitForExit(4000); Ok(p.WaitForExit(8000), "끝남");
            }
            // 계속 여닫기
            p = Run(exe, "--id dt --tray --test cycle"); var sw = Stopwatch.StartNew(); double first = 0, last = 0; int g0 = 0, g1 = 0, h0 = 0, h1 = 0;
            double lo = 1e9; int gl = 9999, hl = 9999; double span = Math.Max(9, cycle / 10.0), nextAt = span;
            for (int i = 0; sw.Elapsed.TotalSeconds < cycle;)
            {
                Thread.Sleep(300); if (p.HasExited) break; p.Refresh(); lo = Math.Min(lo, p.PrivateMemorySize64 / 1048576.0); gl = Math.Min(gl, GetGuiResources(p.Handle, 0)); hl = Math.Min(hl, p.HandleCount);
                if (sw.Elapsed.TotalSeconds < nextAt) continue;
                Console.WriteLine("  여닫기 {0,4:F0}초 이 구간 최저: 커밋 {1:F1}MB GDI {2} 핸들 {3} 스레드 {4}", sw.Elapsed.TotalSeconds, lo, gl, hl, p.Threads.Count); last = lo; g1 = gl; h1 = hl; if (i == 2) { first = last; g0 = g1; h0 = h1; } i++; nextAt += span; lo = 1e9; gl = hl = 9999;
            }
            Ok(!p.HasExited, "계속 여닫아도 살아 있음"); Ok(cycle < 40 || last - first < 6 && g1 - g0 < 12 && h1 - h0 < 40, string.Format("여닫기 늘어남: 커밋 {0:F1} → {1:F1}MB, GDI {2} → {3}, 핸들 {4} → {5}", first, last, g0, g1, h0, h1));
            Run(exe, "--id dt --quit").WaitForExit(4000); Ok(p.WaitForExit(8000), "끝남");
            string err = Path.Combine(data, "error.txt"); Ok(!File.Exists(err), "화면 오류 없음"); if (File.Exists(err)) Console.WriteLine(File.ReadAllText(err).Substring(0, Math.Min(1500, File.ReadAllText(err).Length)));
        }
        finally { if (p != null && !p.HasExited) p.Kill(); CloseDesktop(desk); try { Directory.Delete(data, true); } catch (Exception) { } }
        Console.WriteLine("통과 {0}, 실패 {1}", pass, fail);
        return fail;
    }
}
