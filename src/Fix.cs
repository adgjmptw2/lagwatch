using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using static LagWatch.Lang;

namespace LagWatch
{
    /// <summary>프로그램 하나에 걸어 둔 것</summary>
    internal sealed class Rule
    {
        public string Key = "", Label = "";
        public int Prio;        // 0 그대로, 1 조금 낮게, 2 가장 낮게
        public int Cores;       // 0 전부, n = 뒤쪽 물리 코어 n개
        public bool Always;     // false = 게임할 때만
        public bool Once;       // 이번 게임만(저장하지 않음)
        public bool Any { get { return Prio != 0 || Cores != 0; } }
        public string Text(int phys)
        {
            string s = Cores > 0 ? Tx("코어 {0}개", "{0} {1}", Cores, Pl(Cores, "core", "cores")) : "";
            if (Prio > 0) s += (s.Length > 0 ? ", " : "") + (Prio == 1 ? Tx("우선순위 낮음", "low priority") : Tx("우선순위 매우 낮음", "idle priority"));
            return s;
        }
    }

    /// <summary>게임이 끝나면 할 일</summary>
    internal sealed class After { public string Key, Label; public bool Restart; }

    /// <summary>해결한 일과 그 전후</summary>
    internal sealed class FixLog { public DateTime At; public string Key, Label, Text, Effect; public int HitchBefore; public bool Undo, Fail; public double Mono; }

    /// <summary>우선순위와 코어를 바꾸고, 끝나면 처음 값으로 되돌린다. 갑자기 꺼져도 다음에 켤 때 되돌린다.</summary>
    internal sealed unsafe class Fixer
    {
        [DllImport("kernel32.dll", SetLastError = true)] static extern uint GetPriorityClass(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetPriorityClass(IntPtr h, uint c);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetProcessAffinityMask(IntPtr h, out ulong p, out ulong s);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetProcessAffinityMask(IntPtr h, ulong m);

        const uint Idle = 0x40, Below = 0x4000, Normal = 0x20;
        internal struct Saved { public int Pid; public long Create; public uint Prio; public ulong Mask; public string Key; }

        readonly Monitor m;
        readonly string stateFile;
        public readonly List<Rule> Rules = new List<Rule>();
        public readonly List<After> Queue = new List<After>();
        public readonly List<FixLog> Log = new List<FixLog>();
        readonly Dictionary<int, Saved> applied = new Dictionary<int, Saved>();
        readonly HashSet<int> denied = new HashSet<int>();
        readonly Dictionary<string, string> bg = new Dictionary<string, string>(); // 게임 중 한꺼번에 제한한 것
        bool hadGame;
        public Action Changed;

        public Fixer(Monitor m, string stateFile)
        {
            this.m = m; this.stateFile = stateFile; Watched = HasWatcher;
            foreach (var s in m.Set.Rules) { var r = Parse(s); if (r != null) Rules.Add(r); }
            Recover();
        }

        // ---- 저장 ----

        public static string Format(Rule r) { return r.Key + "|" + r.Label.Replace('|', ' ') + "|" + r.Prio + "|" + r.Cores + "|" + (r.Always ? 1 : 0); }
        public static Rule Parse(string s)
        {
            // 묶음 이름에 | 가 들어갈 수 있어 뒤에서부터 센다
            var f = s.Split('|'); if (f.Length < 5) return null;
            int n = f.Length, p, c, a;
            if (!int.TryParse(f[n - 3], out p) || !int.TryParse(f[n - 2], out c) || !int.TryParse(f[n - 1], out a)) return null;
            return new Rule { Key = string.Join("|", f, 0, n - 4), Label = f[n - 4], Prio = Math.Max(0, Math.Min(2, p)), Cores = Math.Max(0, c), Always = a == 1 };
        }
        void SaveRules()
        {
            m.Set.Rules.Clear();
            foreach (var r in Rules) if (!r.Once && r.Any) m.Set.Rules.Add(Format(r));
            m.Set.Save();
            if (Changed != null) Changed();
        }

        // ---- 할 수 있는 일 ----

        /// <summary>우선순위·코어를 바꿔도 되는지. 게임, 안티치트, 게임과 한 몸인 것, DB, 윈도우는 안 된다.</summary>
        public static bool CanTune(Grp g)
        {
            if (g == null || g.IsGame || g.Key == "win" || g.Key == "lagwatch") return false;
            foreach (var a in g.Apps) if (a.Protected || a.Pseudo || a.Service || Names.IsGameClient(a.Exe) || Names.IsGuarded(a.Exe) || Names.IsDatabase(a.Exe)) return false;
            return true;
        }
        /// <summary>시작 프로그램에 등록돼 있으면 그 명령으로, 아니면 지금 켜진 명령 그대로 다시 켤 수 있을 때</summary>
        public static bool CanRestart(Grp g)
        {
            if (!CanEnd(g)) return false;
            bool all = true; foreach (var a in g.Apps) if (!Actions.CanRestart(a)) all = false;
            return all || AutoRun(g) != null;
        }
        public static bool CanEnd(Grp g) { if (g == null || g.IsGame || g.Key == "win" || g.Key == "lagwatch" || g.Apps.Count == 0) return false; foreach (var a in g.Apps) if (!Actions.CanEnd(a) || Names.IsGameClient(a.Exe)) return false; return true; }

        public Rule Find(string key) { foreach (var r in Rules) if (r.Key == key) return r; return null; }

        public void Set(Rule r)
        {
            lock (m.Sync)
            {
                Rules.RemoveAll(x => x.Key == r.Key);
                if (r.Any) Rules.Add(r);
                foreach (var p in new List<int>(applied.Keys)) if (applied[p].Key == r.Key) Restore(p);
                denied.Clear();
            }
            SaveRules(); SaveState();
            Tick();
        }
        public void Remove(string key)
        {
            lock (m.Sync) { Rules.RemoveAll(x => x.Key == key); foreach (var p in new List<int>(applied.Keys)) if (applied[p].Key == key) Restore(p); }
            SaveRules(); SaveState();
        }

        public void Book(string key, string label, bool restart)
        {
            lock (m.Sync) { Queue.RemoveAll(x => x.Key == key); Queue.Add(new After { Key = key, Label = label, Restart = restart }); }
            if (Changed != null) Changed();
        }
        public void Unbook(string key) { lock (m.Sync) Queue.RemoveAll(x => x.Key == key); if (Changed != null) Changed(); }

        public FixLog Note(string key, string label, string text, string effect, bool undo)
        {
            var f = new FixLog { At = DateTime.Now, Key = key, Label = label, Text = text, Effect = effect, Undo = undo, Mono = m.Now };
            lock (m.Sync) { Log.RemoveAll(x => x.Key == key); Log.Insert(0, f); if (Log.Count > 20) Log.RemoveRange(20, Log.Count - 20); }
            return f;
        }

        /// <summary>뒤쪽 물리 코어 n개의 논리 코어 묶음</summary>
        public static ulong Mask(int[] coreOf, int phys, int n)
        {
            ulong k = 0; n = Math.Max(1, Math.Min(phys, n));
            for (int i = 0; i < coreOf.Length && i < 64; i++) if (coreOf[i] >= phys - n) k |= 1UL << i;
            return k;
        }
        ulong Full { get { ulong k = 0; for (int i = 0; i < m.Lps && i < 64; i++) k |= 1UL << i; return k; } }

        // ---- 게임 중 다른 프로그램 한꺼번에 ----

        /// <summary>제한할 때 남겨 줄 물리 코어 수(8코어면 3)</summary>
        public static int BgCores(int phys) { return Math.Max(1, Math.Min(phys - 1, (int)Math.Round(phys * .375))); }
        public const double BgMin = 2;  // 전체 CPU의 2% 넘게 쓰면
        public static bool CanBg(Grp g) { if (!CanTune(g)) return false; foreach (var a in g.Apps) if (Names.IsLive(a.Exe ?? "")) return false; return true; }
        public List<string> BgLabels() { lock (m.Sync) return new List<string>(bg.Values); }

        void FindBg(bool game)
        {
            if (!game || !m.Set.Bg || m.Phys < 4) { bg.Clear(); return; }
            var map = new Dictionary<string, Grp>(); string front = null; Proc f;
            if (m.Procs.TryGetValue(m.ForegroundPid, out f) && f.App != null) { string fl; Names.Group(f.App, out front, out fl); }
            foreach (var a in m.Apps.Values) { if (a.Count == 0) continue; string k, l; Names.Group(a, out k, out l); Grp g; if (!map.TryGetValue(k, out g)) map[k] = g = new Grp { Key = k, Label = l }; g.Add(a); }
            foreach (var g in map.Values) if (!bg.ContainsKey(g.Key) && g.Key != front && g.Cores / m.Lps * 100 >= BgMin && Find(g.Key) == null && CanBg(g)) bg[g.Key] = g.Label;
        }

        // ---- 걸기와 되돌리기 ----

        /// <summary>몇 초마다. 규칙에 맞는 새 프로세스에 걸고, 필요 없어진 것은 되돌린다.</summary>
        public void Tick()
        {
            bool dirty = false;
            List<After> todo = null;
            lock (m.Sync)
            {
                bool game = m.Game != null;
                if (hadGame && !game)
                {
                    foreach (var r in Rules.FindAll(x => x.Once)) { Rules.Remove(r); foreach (var p in new List<int>(applied.Keys)) if (applied[p].Key == r.Key) { Restore(p); dirty = true; } }
                    if (Queue.Count > 0) { todo = new List<After>(Queue); Queue.Clear(); }
                }
                hadGame = game;
                // 사라진 것 정리
                List<int> gone = null;
                foreach (var kv in applied) { Proc q; if (!m.Procs.TryGetValue(kv.Key, out q) || q.Create != kv.Value.Create) { if (gone == null) gone = new List<int>(); gone.Add(kv.Key); } }
                if (gone != null) foreach (int p in gone) { applied.Remove(p); dirty = true; }
                denied.RemoveWhere(p => !m.Procs.ContainsKey(p));

                FindBg(game);
                foreach (var q in m.Procs.Values)
                {
                    if (q.App == null || q.Pseudo || q.IsGame) continue;
                    if (Rules.Count == 0 && applied.Count == 0 && bg.Count == 0) break;
                    string k, l; Names.Group(q.App, out k, out l);
                    Rule r = Find(k);
                    if (r == null && bg.ContainsKey(k)) r = new Rule { Key = k, Cores = BgCores(m.Phys), Prio = 1 };
                    bool want = r != null && r.Any && (r.Always || game);
                    bool on = applied.ContainsKey(q.Pid);
                    if (want && !on && !denied.Contains(q.Pid)) dirty |= Apply(q, r);
                    else if (!want && on) { Restore(q.Pid); dirty = true; }
                }
            }
            if (dirty) SaveState();
            if (todo != null) foreach (var a in todo) RunAfter(a);
        }

        IntPtr Open(int pid)
        {
            IntPtr h = Native.OpenProcess(0x0200 | Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            return h;
        }

        bool Apply(Proc q, Rule r)
        {
            if (q.App.Protected || Names.IsGuarded(q.Name) || Names.IsGameClient(q.Name) || Names.IsDatabase(q.Name) || Names.IsSystem(q.Name) || q.App.Service) return false;
            IntPtr h = Open(q.Pid);
            if (h == IntPtr.Zero) { denied.Add(q.Pid); return false; }
            try
            {
                long c, e, kt, ut;
                if (!Native.GetProcessTimes(h, out c, out e, out kt, out ut) || c != q.Create) return false;
                ulong pm, sm; uint pr = GetPriorityClass(h);
                if (pr == 0 || !GetProcessAffinityMask(h, out pm, out sm)) { denied.Add(q.Pid); return false; }
                ulong lim = r.Cores > 0 ? Mask(m.CoreOf, m.Phys, r.Cores) & sm : sm;
                uint want = r.Prio == 2 ? Idle : r.Prio == 1 ? Below : pr;
                // 걸려 있는 부모에서 막 태어난 것은 부모 값을 물려받는다 → 되돌릴 값은 원래 값으로
                var s = new Saved { Pid = q.Pid, Create = q.Create, Prio = r.Prio > 0 && pr == want ? Normal : pr, Mask = r.Cores > 0 && pm == lim ? sm : pm, Key = r.Key };
                bool ok = true;
                if (r.Prio > 0 && Rank(want) < Rank(pr)) ok &= SetPriorityClass(h, want);
                if (r.Cores > 0 && lim != 0 && lim != pm) ok &= SetProcessAffinityMask(h, lim & pm) || SetProcessAffinityMask(h, lim);
                if (!ok) { denied.Add(q.Pid); return false; }
                applied[q.Pid] = s;
                return true;
            }
            finally { Native.CloseHandle(h); }
        }
        static int Rank(uint c) { return c == 0x40 ? 0 : c == 0x4000 ? 1 : c == 0x20 ? 2 : c == 0x8000 ? 3 : c == 0x80 ? 4 : 5; }

        void Restore(int pid)
        {
            Saved s; if (!applied.TryGetValue(pid, out s)) return;
            applied.Remove(pid);
            RestoreOne(s);
        }
        static bool RestoreOne(Saved s)
        {
            IntPtr h = Native.OpenProcess(0x0200 | Native.PROCESS_QUERY_LIMITED_INFORMATION, false, s.Pid);
            if (h == IntPtr.Zero) return false;
            try
            {
                long c, e, kt, ut;
                if (!Native.GetProcessTimes(h, out c, out e, out kt, out ut) || c != s.Create) return false;
                if (s.Prio != 0) SetPriorityClass(h, s.Prio);
                if (s.Mask != 0) SetProcessAffinityMask(h, s.Mask);
                return true;
            }
            finally { Native.CloseHandle(h); }
        }

        public void RestoreAll()
        {
            lock (m.Sync) foreach (var p in new List<int>(applied.Keys)) Restore(p);
            SaveState();
        }

        public int AppliedCount(string key) { lock (m.Sync) { int n = 0; foreach (var s in applied.Values) if (s.Key == key) n++; return n; } }

        void SaveState()
        {
            if (stateFile == null) return;
            try
            {
                var sb = new StringBuilder();
                lock (m.Sync) foreach (var s in applied.Values) sb.Append(s.Pid).Append('\t').Append(s.Create).Append('\t').Append(s.Prio).Append('\t').Append(s.Mask).Append('\t').Append(s.Key).Append('\n');
                if (sb.Length == 0) { if (File.Exists(stateFile)) File.Delete(stateFile); }
                else { string tmp = stateFile + ".tmp"; File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false)); if (File.Exists(stateFile)) File.Replace(tmp, stateFile, null); else File.Move(tmp, stateFile); } // 쓰다 꺼져도 원래 값이 남게
            }
            catch (Exception) { }
        }

        static bool StillOn(Saved s)
        {
            IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, s.Pid); if (h == IntPtr.Zero) return false;
            try { long c, e, kt, ut; return Native.GetProcessTimes(h, out c, out e, out kt, out ut) && c == s.Create; } finally { Native.CloseHandle(h); }
        }

        void Recover()
        {
            if (stateFile == null) return;
            try
            {
                if (!File.Exists(stateFile)) return;
                var keep = new StringBuilder();
                foreach (var line in File.ReadAllLines(stateFile))
                {
                    var f = line.Split('\t'); if (f.Length < 4) continue;
                    var s = new Saved { Pid = int.Parse(f[0]), Create = long.Parse(f[1]), Prio = uint.Parse(f[2]), Mask = ulong.Parse(f[3]), Key = f.Length > 4 ? f[4] : "" };
                    // 아직 켜져 있는데 되돌리지 못한 것은 처음 값을 잊지 않게 남긴다
                    if (!RestoreOne(s) && StillOn(s)) { applied[s.Pid] = s; keep.Append(line).Append('\n'); }
                }
                if (keep.Length == 0) File.Delete(stateFile); else File.WriteAllText(stateFile, keep.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        // ---- 껐다 켜기 ----

        public Grp GroupOf(string key)
        {
            Grp g = null;
            foreach (var a in m.Apps.Values)
            {
                if (a.Count == 0) continue;
                string k, l; Names.Group(a, out k, out l);
                if (k != key) continue;
                if (g == null) g = new Grp { Key = k, Label = l };
                g.Add(a);
            }
            return g;
        }

        void RunAfter(After a)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(o =>
            {
                System.Threading.Thread.Sleep(4000); // 게임이 완전히 닫힐 시간
                Grp g; lock (m.Sync) { if (m.Game != null) { Queue.Add(a); return; } g = GroupOf(a.Key); }
                if (g == null) return;
                string err; long before = g.Commit;
                if (a.Restart && CanRestart(g)) { if (Restart(g, out err)) Note(a.Key, a.Label, Tx("{0} 재시작함", "Restarted {0}", a.Label), Tx("재시작 전 가상메모리 ", "Commit before restart: ") + Fmt.Mem(before / 1048576.0), false); else Note(a.Key, a.Label, Tx("{0} 재시작 실패", "Couldn't restart {0}", a.Label), err, false).Fail = true; }
                else if (!a.Restart && CanEnd(g)) { End(g); Note(a.Key, a.Label, Tx("{0} 종료함", "Closed {0}", a.Label), "", false); }
                if (Changed != null) Changed();
            });
        }

        public int End(Grp g) { int n = 0; foreach (var a in g.Apps) if (Actions.CanEnd(a)) n += Actions.End(m, a); return n; }

        public bool Restart(Grp g, out string error)
        {
            error = null;
            string run = AutoRun(g);
            if (run == "*self")
            {
                if (End(g) == 0) { error = Tx("종료 실패", "Couldn't close"); return false; }
                for (int i = 0; i < 15; i++) { System.Threading.Thread.Sleep(1000); Grp now; lock (m.Sync) now = GroupOf(g.Key); if (now != null) return true; }
                error = Tx("종료 후 다시 실행되지 않음", "Closed, but it didn't start again"); return false;
            }
            if (run != null)
            {
                // 로그인 때 켜지는 방식 그대로(최소화 같은 옵션 포함) 다시 켠다
                int n = End(g);
                if (n == 0) { error = Tx("종료 실패", "Couldn't close"); return false; }
                System.Threading.Thread.Sleep(800);
                return Launch(run, out error);
            }
            App main = null; foreach (var a in g.Apps) if (main == null || a.Commit > main.Commit) main = a;
            if (main == null) { error = Tx("실행 중이 아님", "Not running"); return false; }
            bool ok = Actions.Restart(m, main, out error);
            return ok;
        }

        /// <summary>윈도우 시작 프로그램에 등록된 명령 중 이 프로그램의 것</summary>
        public static string AutoRun(Grp g)
        {
            lock (runs)
            {
                KeyValuePair<int, string> c;
                if (runs.TryGetValue(g.Key, out c) && Environment.TickCount - c.Key < 60000 && Environment.TickCount >= c.Key) return c.Value;
                if (runs.Count > 300) runs.Clear();
                string r = null; try { r = FindRun(g); } catch (Exception) { }
                runs[g.Key] = new KeyValuePair<int, string>(Environment.TickCount, r);
                return r;
            }
        }
        static readonly Dictionary<string, KeyValuePair<int, string>> runs = new Dictionary<string, KeyValuePair<int, string>>();
        public static void ForgetRuns() { lock (runs) runs.Clear(); }

        /// <summary>부모 명령이 이 글을 담고 있는지. 부모가 다시 켜 주는 프로그램은 끄기만 하면 된다.</summary>
        static Func<Grp, string, bool> Watched;
        bool HasWatcher(Grp g, string text)
        {
            var ps = new List<int>();
            lock (m.Sync) foreach (var q in m.Procs.Values) if (q.App != null && g.Apps.Contains(q.App)) ps.Add(q.ParentPid);
            foreach (int p in ps) { string c = Names.CommandLine(p); if (c != null && c.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) return true; }
            return false;
        }

        static bool Shared(string dir)
        {
            string w = Environment.GetFolderPath(Environment.SpecialFolder.Windows), p = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), q = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            return dir.Length < 8 || dir.StartsWith(w, StringComparison.OrdinalIgnoreCase) || dir.Equals(p, StringComparison.OrdinalIgnoreCase) || dir.Equals(q, StringComparison.OrdinalIgnoreCase);
        }

        static string FindRun(Grp g)
        {
            if (g.Key.Length >= 4 && Watched != null && Watched(g, g.Key + "-service")) return "*self"; // 지켜보는 서비스가 다시 켠다
            var dirs = new List<string>();
            foreach (var a in g.Apps) if (a.Path != null && !Names.IsHost(a.Exe)) { string d = Path.GetDirectoryName(a.Path); if (!dirs.Contains(d) && !Shared(d)) dirs.Add(d); }
            if (dirs.Count == 0) return null;
            foreach (var root in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
                foreach (var sub in new[] { @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run" })
                    using (var k = root.OpenSubKey(sub))
                    {
                        if (k == null) continue;
                        foreach (var n in k.GetValueNames())
                        {
                            string v = k.GetValue(n) as string; if (string.IsNullOrEmpty(v)) continue;
                            string exe = Exe(v); if (exe == null) continue;
                            string ed = Path.GetDirectoryName(exe);
                            if (ed == null || Shared(ed)) continue;
                            foreach (var d in dirs) if (ed.Equals(d, StringComparison.OrdinalIgnoreCase) || ed.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase) || d.StartsWith(ed + "\\", StringComparison.OrdinalIgnoreCase)) return v;
                        }
                    }
            return null;
        }
        public static string Exe(string cmd)
        {
            cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
            if (cmd.StartsWith("\"")) { int i = cmd.IndexOf('"', 1); return i > 1 ? cmd.Substring(1, i - 1) : null; }
            int e = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return e > 0 ? cmd.Substring(0, e + 4) : null;
        }

        /// <summary>일반 권한으로 켠다. 관리자로 돌고 UAC가 켜져 있으면 탐색기의 권한을 빌린다.</summary>
        public static bool Launch(string cmd, out string error)
        {
            error = null;
            string exe = Exe(cmd); if (exe == null) { error = Tx("실행 파일을 찾을 수 없음", "Executable not found"); return false; }
            string args = cmd.Trim().StartsWith("\"") ? Actions.Rest(cmd.Trim()) : cmd.Trim().Length > exe.Length ? cmd.Trim().Substring(Math.Min(cmd.Trim().Length, exe.Length)).Trim() : "";
            try
            {
                if (Cleaner.Elevated && UacOn) return AsUser(exe, args, out error);
                Sys.Open(exe, args, Path.GetDirectoryName(exe));
                return true;
            }
            catch (Exception ex) { error = Tx("실행 실패: ", "Launch failed: ") + ex.Message; return false; }
        }

        public static bool UacOn
        {
            get { try { return Convert.ToInt32(Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", 1)) != 0; } catch (Exception) { return true; } }
        }

        [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr h, int acc, out IntPtr t);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool DuplicateTokenEx(IntPtr t, uint acc, IntPtr sa, int lvl, int type, out IntPtr n);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct SI { public int cb; public string r, d, t; public int x, y, xs, ys, xc, yc, f, fl; public short sw, c2; public IntPtr r2, i, o, e; }
        [StructLayout(LayoutKind.Sequential)] struct PI { public IntPtr p, t; public int pid, tid; }
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool CreateProcessWithTokenW(IntPtr t, int logon, string app, StringBuilder cmd, uint fl, IntPtr env, string dir, ref SI si, out PI pi);

        static bool AsUser(string exe, string args, out string error)
        {
            error = null; uint pid; Native.GetWindowThreadProcessId(GetShellWindow(), out pid);
            IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, (int)pid), t = IntPtr.Zero, n = IntPtr.Zero;
            try
            {
                if (h == IntPtr.Zero || !OpenProcessToken(h, 0x2, out t) || !DuplicateTokenEx(t, 0x02000000, IntPtr.Zero, 2, 1, out n)) { error = Tx("일반 권한 전환 실패", "Couldn't drop administrator rights"); return false; }
                var si = new SI(); si.cb = Marshal.SizeOf(typeof(SI)); PI pi;
                if (!CreateProcessWithTokenW(n, 0, exe, new StringBuilder("\"" + exe + "\" " + args), 0, IntPtr.Zero, Path.GetDirectoryName(exe), ref si, out pi)) { error = Tx("실행 실패 (", "Launch failed (") + Marshal.GetLastWin32Error() + ")"; return false; }
                Native.CloseHandle(pi.p); Native.CloseHandle(pi.t);
                return true;
            }
            finally { if (n != IntPtr.Zero) Native.CloseHandle(n); if (t != IntPtr.Zero) Native.CloseHandle(t); if (h != IntPtr.Zero) Native.CloseHandle(h); }
        }
    }
}
