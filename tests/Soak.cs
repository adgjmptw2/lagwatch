using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace LagWatch
{
    /// <summary>오래 켜 둔 것처럼 돌리며 스스로 새는 것이 없는지 본다.</summary>
    internal static class Soak
    {
        [DllImport("user32.dll")] static extern int GetGuiResources(IntPtr h, int f);
        static Process me = Process.GetCurrentProcess();
        static string Line()
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); me.Refresh();
            return string.Format("커밋 {0,6:F1}MB  관리 {1,5:F1}MB  핸들 {2,4}  스레드 {3,2}  GDI {4,3}  USER {5,3}  D2 {6,3}", me.PrivateMemorySize64 / 1048576.0, GC.GetTotalMemory(false) / 1048576.0, me.HandleCount, me.Threads.Count, GetGuiResources(me.Handle, 0), GetGuiResources(me.Handle, 1), D2.Live);
        }
        [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int c, IntPtr b, int n, out int r);
        [DllImport("ntdll.dll")] static extern int NtQueryObject(IntPtr h, int c, IntPtr b, int n, out int r);
        static Dictionary<string, int> Kinds()
        {
            var d = new Dictionary<string, int>(); int n = 64 << 20, r; IntPtr b = Marshal.AllocHGlobal(n), o = Marshal.AllocHGlobal(4096); var names = new Dictionary<int, string>();
            try
            {
                if (NtQuerySystemInformation(64, b, n, out r) != 0) return d; long cnt = Marshal.ReadInt64(b);
                for (long i = 0; i < cnt; i++)
                {
                    IntPtr e = (IntPtr)((long)b + 16 + i * 40); if (Marshal.ReadInt64(e, 8) != me.Id) continue; int t = Marshal.ReadInt16(e, 30); string nm;
                    if (!names.TryGetValue(t, out nm)) { int q; nm = NtQueryObject((IntPtr)Marshal.ReadInt64(e, 16), 2, o, 4096, out q) == 0 ? Marshal.PtrToStringUni(Marshal.ReadIntPtr(o, 8)) : "#" + t; names[t] = nm; }
                    int c; d.TryGetValue(nm, out c); d[nm] = c + 1;
                }
            }
            finally { Marshal.FreeHGlobal(b); Marshal.FreeHGlobal(o); }
            return d;
        }
        static Dictionary<string, int> kinds0;
        static string KindDiff()
        {
            var k = Kinds(); if (kinds0 == null) { kinds0 = k; return ""; } var sb = new System.Text.StringBuilder();
            foreach (var x in k) { int o; kinds0.TryGetValue(x.Key, out o); if (x.Value != o) sb.Append(x.Key).Append(x.Value > o ? " +" : " ").Append(x.Value - o).Append("  "); }
            return sb.ToString();
        }
        static double Mb() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); me.Refresh(); return me.PrivateMemorySize64 / 1048576.0; }

        static int Main(string[] a)
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            int sec = int.Parse(a[0]); string mode = a.Length > 1 ? a[1] : "all";
            string dir = Path.Combine(Path.GetTempPath(), "LagWatchSoak" + me.Id); Directory.CreateDirectory(dir);
            var m = new Monitor(new Settings(Path.Combine(dir, "s.txt")), new EventLog(Path.Combine(dir, "e.tsv")), new History(Path.Combine(dir, "h")));
            if (a.Length > 2) m.Set.Games.Add(a[2]);
            var d = new Doctor(m, dir);
            bool churn = mode == "all" || mode == "churn", ui = mode == "all" || mode == "ui", kern = mode == "all" || mode == "kern", frames = mode == "all";
            var sw = Stopwatch.StartNew(); bool gameOn = true; int steps = 0, opens = 0, kids = 0, ks = 0; double next = 0, nextChurn = 0, nextUi = 1, nextK = 2, baseMb = 0, peak = 0; var seen = new List<double>();
            Func<double> t = () => sw.Elapsed.TotalSeconds;
            while (t() < sec)
            {
                m.Step(); d.Tick(m.Now); Tidy.Step(); steps++; if (mode == "frames" && steps % 40 == 0) { gameOn = !gameOn; foreach (var x in new List<App>(m.Apps.Values)) if (x.Exe.Equals(a[2], StringComparison.OrdinalIgnoreCase)) { x.IsGame = gameOn; foreach (var q in m.Procs.Values) if (q.App == x) q.IsGame = gameOn; } }
                if (churn && t() > nextChurn) { nextChurn = t() + .25; using (var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { UseShellExecute = false, CreateNoWindow = true })) { } kids++; }
                if (ui && t() > nextUi)
                {
                    nextUi = t() + .8; opens++; Surface v; int w = 1080, h = 720;
                    if (opens % 3 == 0) { v = new MiniView(d); w = 360; h = 250; } else if (opens % 3 == 1) { v = new PanelView(d) { Tab = opens / 3 % 4 }; w = 500; h = 808; } else v = new MainView(d) { Page = opens / 3 % 5 };
                    v.Refresh(); for (int i = 0; i < 4; i++) { v.MouseMove(100 + i * 60, 300 + i * 20); v.Draw(w, h, 1); } if (v is MainView) { foreach (var g in v.S.Groups) if (g.Commit > 300 << 20) { ((MainView)v).Open = g.Key; v.Draw(w, h, 1); } } v.Dispose(); if (opens % 3 == 2) Icons.Clear();
                }
                if (kern && Cleaner.Elevated && t() > nextK) { nextK = t() + 5; d.WantCores = ks++ % 2 == 0; }
                if (t() > next) { next += Math.Max(10, sec / 12.0); double mb = Mb(); seen.Add(mb); if (baseMb == 0 && t() > 25) baseMb = mb; if (mb > peak) peak = mb; Console.WriteLine("{0,4:F0}초 표본 {1,5} 화면 {2,4} 자식 {3,4}  {4}", t(), steps, opens, kids, Line()); if (t() > 60) Console.WriteLine("        " + KindDiff()); }
                Thread.Sleep(90);
            }
            d.Dispose(); m.Stop();
            int n = seen.Count; double first = 0, last = 0; int k = Math.Max(1, n / 4); for (int i = 0; i < k; i++) { first += seen[Math.Min(n - 1, n / 4 + i)]; last += seen[n - 1 - i]; }
            Console.WriteLine("앞쪽 평균 {0:F1}MB → 끝 평균 {1:F1}MB", first / k, last / k);
            Console.WriteLine("끝 {0}", Line());
            try { Directory.Delete(dir, true); } catch (Exception) { }
            return 0;
        }
    }
}
