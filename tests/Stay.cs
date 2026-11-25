using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

/// <summary>보이지 않는 데스크톱에 큰 창을 오래 열어 두고 탭·프로그램을 계속 눌러 커밋이 느는지 본다.</summary>
internal static class Stay
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct SI { public int cb; public string res, desktop, title; public int x, y, w, h, cx, cy, fill, flags; public short show, r2; public IntPtr r3, i, o, e; }
    [StructLayout(LayoutKind.Sequential)] struct PI { public IntPtr p, t; public int pid, tid; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int l, t, r, b; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateDesktopW(string n, string d, IntPtr m, int f, uint a, IntPtr s);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetThreadDesktop(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool CreateProcessW(string app, StringBuilder cmd, IntPtr a, IntPtr b, bool inh, uint fl, IntPtr env, string dir, ref SI si, out PI pi);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumDesktopWindows(IntPtr d, EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern int GetGuiResources(IntPtr h, int flag);
    static Process Run(string exe, string args) { var si = new SI { desktop = "lagwatch-stay" }; si.cb = Marshal.SizeOf(typeof(SI)); PI pi; CreateProcessW(null, new StringBuilder("\"" + exe + "\" " + args), IntPtr.Zero, IntPtr.Zero, false, 0x4000, IntPtr.Zero, null, ref si, out pi); return Process.GetProcessById(pi.pid); }
    static IntPtr At(int x, int y) { return (IntPtr)((y << 16) | x); }
    static void Main(string[] a)
    {
        Console.OutputEncoding = new UTF8Encoding(false); string exe = a[0]; int minutes = int.Parse(a[1]); string mode = a.Length > 2 ? a[2] : "click"; int gap = a.Length > 3 ? int.Parse(a[3]) : 700;
        IntPtr desk = CreateDesktopW("lagwatch-stay", null, IntPtr.Zero, 0, 0x10000000, IntPtr.Zero); SetThreadDesktop(desk);
        var p = Run(exe, "--id stay" + mode + (a.Length > 4 ? " --stats \"" + a[4] + "\"" : "") + (a.Length > 5 ? " --breath " + a[5] : "")); Thread.Sleep(6000); IntPtr w = IntPtr.Zero;
        EnumDesktopWindows(desk, (h, l) => { uint q; GetWindowThreadProcessId(h, out q); RECT r; GetWindowRect(h, out r); if (q == p.Id && IsWindowVisible(h) && r.r - r.l > 600) w = h; return true; }, IntPtr.Zero);
        Console.WriteLine("창 " + (w != IntPtr.Zero)); var rnd = new Random(7); int[] tx = { 55, 115, 190, 250, 330 };
        var sw = Stopwatch.StartNew(); double lo = 1e9, hi = 0, next = 60; TimeSpan c0 = p.TotalProcessorTime;
        while (sw.Elapsed.TotalMinutes < minutes && !p.HasExited)
        {
            if (mode.StartsWith("click"))
            {
                int k = rnd.Next(10);
                if (k < 3) { int x = tx[rnd.Next(4)]; PostMessageW(w, 0x200, IntPtr.Zero, At(x, 30)); Thread.Sleep(60); PostMessageW(w, 0x201, (IntPtr)1, At(x, 30)); }  // 청소 탭은 빼고
                else if (k < 5) { int y = 300 + rnd.Next(380); PostMessageW(w, 0x200, IntPtr.Zero, At(500, y)); Thread.Sleep(60); }
                else PostMessageW(w, 0x200, IntPtr.Zero, At(rnd.Next(1080), 60 + rnd.Next(640)));
                Thread.Sleep(gap);
            }
            else Thread.Sleep(1000);
            p.Refresh(); double mb = p.PrivateMemorySize64 / 1048576.0; lo = Math.Min(lo, mb); hi = Math.Max(hi, mb);
            if (sw.Elapsed.TotalSeconds >= next) { Console.WriteLine("{0,5:F0}초  커밋 최저 {1:F1} 최고 {2:F1}MB  핸들 {3} GDI {4} USER {5} 스레드 {6}  CPU {7:F1}%", sw.Elapsed.TotalSeconds, lo, hi, p.HandleCount, GetGuiResources(p.Handle, 0), GetGuiResources(p.Handle, 1), p.Threads.Count, (p.TotalProcessorTime - c0).TotalSeconds / 60 * 100); Console.Out.Flush(); c0 = p.TotalProcessorTime; next += 60; lo = 1e9; hi = 0; }
        }
        if (!p.HasExited) { Run(exe, "--id stay" + mode + " --quit").WaitForExit(4000); if (!p.WaitForExit(8000)) p.Kill(); }
        CloseDesktop(desk); Console.WriteLine("끝");
    }
}
