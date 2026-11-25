using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using static LagWatch.Lang;

namespace LagWatch
{
    /// <summary>사용자가 직접 누를 때만 쓰는 동작</summary>
    internal static class Actions
    {
        public static bool CanEnd(App a)
        {
            return !a.Pseudo && !a.IsGame && !a.Protected && !a.Service && !Names.IsSystem(a.Exe) && !Names.IsServer(a.Exe);
        }

        /// <summary>스크립트는 작업 폴더와 환경을 알 수 없어 다시 켜지 않는다.</summary>
        public static bool CanRestart(App a) { return CanEnd(a) && !a.Script && !Names.IsHost(a.Exe) && a.Path != null; }

        struct Target { public int Pid, Parent; public long Create; public string Path; }

        static List<Target> Collect(Monitor m, App a)
        {
            var list = new List<Target>();
            lock (m.Sync)
                foreach (var q in m.Procs.Values)
                    if (q.App == a && q.Session != 0) list.Add(new Target { Pid = q.Pid, Parent = q.ParentPid, Create = q.Create });
            return list;
        }

        public static int Count(Monitor m, App a) { return Collect(m, a).Count; }

        /// <summary>끈 개수</summary>
        public static int End(Monitor m, App a) { return End(Collect(m, a)); }

        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint c);
        [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);

        /// <summary>창이 있으면 먼저 닫기를 보낸다(세션을 저장할 틈). 보낸 창의 수</summary>
        static int CloseWindows(HashSet<int> pids)
        {
            int n = 0;
            EnumWindows((h, l) => { uint p; Native.GetWindowThreadProcessId(h, out p); if (pids.Contains((int)p) && IsWindowVisible(h) && GetWindow(h, 4) == IntPtr.Zero) { PostMessageW(h, 0x10, IntPtr.Zero, IntPtr.Zero); n++; } return true; }, IntPtr.Zero);
            return n;
        }

        static int End(List<Target> list)
        {
            var handles = new List<IntPtr>(); var pids = new HashSet<int>();
            foreach (var t in list)
            {
                IntPtr h = Native.OpenProcess(Native.PROCESS_TERMINATE | Native.PROCESS_QUERY_LIMITED_INFORMATION | Native.SYNCHRONIZE, false, t.Pid);
                if (h == IntPtr.Zero) continue;
                long c, e, k, u;
                // PID 가 그새 다른 프로그램으로 바뀌었으면 건드리지 않는다
                if (Native.GetProcessTimes(h, out c, out e, out k, out u) && c == t.Create) { handles.Add(h); pids.Add(t.Pid); }
                else Native.CloseHandle(h);
            }
            if (handles.Count > 0 && CloseWindows(pids) > 0)
            {
                var sw = Stopwatch.StartNew(); bool all = false;
                while (!all && sw.ElapsedMilliseconds < 2000) { all = true; foreach (var h in handles) if (Native.WaitForSingleObject(h, 0) != 0) { all = false; System.Threading.Thread.Sleep(50); break; } }
            }
            int done = 0;
            foreach (var h in handles)
            {
                if (Native.WaitForSingleObject(h, 0) == 0 || Native.TerminateProcess(h, 1)) { done++; Native.WaitForSingleObject(h, 3000); }
                Native.CloseHandle(h);
            }
            return done;
        }

        public static bool Restart(Monitor m, App a, out string error)
        {
            error = null;
            var list = Collect(m, a);
            var pids = new HashSet<int>();
            foreach (var t in list) pids.Add(t.Pid);
            int root = -1; string path = null, args = "";
            foreach (var t in list)
            {
                if (pids.Contains(t.Parent)) continue;
                string p = Names.ImagePath(t.Pid);
                if (p == null) continue;
                // 이름이 가장 짧은 것이 본체 (lghub.exe, lghub_agent.exe …)
                if (root < 0 || Path.GetFileName(p).Length < Path.GetFileName(path).Length)
                {
                    root = t.Pid; path = p;
                    string cmd = Names.CommandLine(t.Pid) ?? "";
                    args = Rest(cmd);
                }
            }
            if (path == null) { error = Tx("실행 파일을 찾을 수 없음", "Executable not found"); return false; }
            if (End(list) == 0) { error = Tx("종료 실패 (관리자 권한 프로그램일 수 있음)", "Couldn't close (it may be running as administrator)"); return false; }
            return Fixer.Launch("\"" + path + "\" " + args, out error); // UAC가 켜져 있으면 일반 권한으로
        }

        /// <summary>명령줄에서 실행 파일을 뺀 나머지</summary>
        public static string Rest(string cmd)
        {
            cmd = cmd.TrimStart();
            if (cmd.Length == 0) return "";
            int i;
            if (cmd[0] == '"') { i = cmd.IndexOf('"', 1); return i < 0 ? "" : cmd.Substring(i + 1).Trim(); }
            i = cmd.IndexOf(' ');
            return i < 0 ? "" : cmd.Substring(i + 1).Trim();
        }
    }
}
