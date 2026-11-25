using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using static LagWatch.Lang;

namespace LagWatch
{
    /// <summary>램 구성(바이트)</summary>
    internal struct RamMap { public long Total, Free, Cache, Modified, Avail; public bool Known; public long InUse { get { return Math.Max(0, Total - Free - Cache - Modified); } } }

    internal sealed class CleanResult { public long Before, After; public int Programs, Skipped; public bool Cache; public long Gained { get { return After - Before; } } }

    /// <summary>
    /// 램 청소. 놀고 있는 프로그램이 붙잡은 램을 내려놓게 한다.
    /// 게임·안티치트·서비스와 서버·소리·지금 쓰는 프로그램은 건드리지 않는다(다시 읽느라 끊길 수 있어서).
    /// 가상메모리는 줄지 않는다. 그건 프로그램을 껐다 켜야 돌아온다.
    /// </summary>
    internal static unsafe class Cleaner
    {
        [DllImport("ntdll.dll")] static extern uint NtSetSystemInformation(int cls, void* buf, int len);
        [DllImport("advapi32.dll")] static extern bool OpenProcessToken(IntPtr h, int acc, out IntPtr tok);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern bool LookupPrivilegeValueW(string s, string n, out long luid);
        [StructLayout(LayoutKind.Sequential, Pack = 4)] struct TP { public int N; public long Luid; public int Attr; }
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr t, bool off, ref TP n, int len, IntPtr p, IntPtr r);
        [DllImport("advapi32.dll")] static extern bool GetTokenInformation(IntPtr t, int cls, out int v, int len, out int ret);

        static readonly HashSet<string> Keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "audiodg", "dwm", "csrss", "winlogon", "services", "lsass", "wininit", "smss", "System", "Registry", "Memory Compression", "Secure System", "fontdrvhost", "explorer", "ShellExperienceHost", "StartMenuExperienceHost", "ctfmon", "TextInputHost" };

        public static bool Privilege(string name)
        {
            IntPtr t; if (!OpenProcessToken(Native.GetCurrentProcess(), 0x28, out t)) return false;
            try
            {
                var tp = new TP { N = 1, Attr = 2 };
                if (!LookupPrivilegeValueW(null, name, out tp.Luid)) return false;
                return AdjustTokenPrivileges(t, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero) && Marshal.GetLastWin32Error() == 0;
            }
            finally { Native.CloseHandle(t); }
        }

        static int elevated = -1;
        public static bool Elevated
        {
            get
            {
                if (elevated < 0) { IntPtr t; int v = 0, r; if (OpenProcessToken(Native.GetCurrentProcess(), 8, out t)) { GetTokenInformation(t, 20, out v, 4, out r); Native.CloseHandle(t); } elevated = v != 0 ? 1 : 0; }
                return elevated == 1;
            }
        }

        static bool priv;
        static void Privs() { if (priv) return; priv = true; Privilege("SeProfileSingleProcessPrivilege"); Privilege("SeIncreaseQuotaPrivilege"); }

        public static RamMap Map(long total, long avail)
        {
            Privs();
            var r = new RamMap { Total = total, Avail = avail };
            ulong* m = stackalloc ulong[32]; int ret;
            if (Native.NtQuerySystemInformation(80, m, 176, out ret) != 0) return r;
            r.Known = true; r.Free = (long)(m[0] + m[1]) * 4096; r.Modified = (long)m[2] * 4096;
            for (int i = 0; i < 8; i++) r.Cache += (long)m[5 + i] * 4096;
            return r;
        }

        /// <summary>이 프로세스의 램을 내려놓게 해도 되는지</summary>
        public static bool CanTrim(Proc q, int foreground, int self, double gameOn)
        {
            if (q.Pseudo || q.Pid <= 4 || q.Pid == self || q.Pid == foreground || q.IsGame || q.App == null) return false;
            if (q.App.Protected || Names.IsGuarded(q.Name) || Names.IsServer(q.Name) || Keep.Contains(q.Name)) return false;
            if (Names.IsGameClient(q.Name) && gameOn > 0) return false;
            if (q.Session == 0) return false; // 뒤에서 도는 서비스와 서버
            if (q.Ws < 40L << 20) return false;
            return q.Cores < 0.05; // 일하는 중이면 바로 다시 읽어 들이므로 뺀다
        }

        public static CleanResult Trim(Monitor m, bool cache)
        {
            Privs();
            var r = new CleanResult { Before = Avail() };
            var list = new List<Proc>();
            int self = Sys.GetCurrentProcessId();
            lock (m.Sync) foreach (var q in m.Procs.Values) if (CanTrim(q, m.ForegroundPid, self, m.Game != null ? 1 : 0)) list.Add(q); else if (q.Ws >= 40L << 20) r.Skipped++;
            foreach (var q in list)
            {
                IntPtr h = Native.OpenProcess(0x0100 | Native.PROCESS_QUERY_LIMITED_INFORMATION, false, q.Pid);
                if (h == IntPtr.Zero) { r.Skipped++; continue; }
                long c, e, k, u;
                if (Native.GetProcessTimes(h, out c, out e, out k, out u) && c == q.Create && Native.SetProcessWorkingSetSize(h, new IntPtr(-1), new IntPtr(-1))) r.Programs++; else r.Skipped++;
                Native.CloseHandle(h);
            }
            if (cache && m.Game == null && Elevated) { r.Cache = true; Command(3); Command(4); } // 고쳐진 것 저장 → 캐시 비움
            else if (Elevated) Command(5);                                                       // 가장 안 쓰는 캐시만
            int still = 0; for (int i = 0; i < 8 && still < 2; i++) { System.Threading.Thread.Sleep(500); long v = Avail(); if (v > r.After + (64L << 20)) { r.After = v; still = 0; } else { r.After = Math.Max(r.After, v); still++; } } // 내려놓은 것이 디스크에 적힐 때까지. 1초 동안 안 늘면 끝
            return r;
        }

        static uint Command(int c) { return NtSetSystemInformation(80, &c, 4); }

        public static long Avail()
        {
            Native.PERFORMANCE_INFORMATION pi;
            return Native.GetPerformanceInfo(out pi, (uint)Marshal.SizeOf(typeof(Native.PERFORMANCE_INFORMATION))) ? (long)pi.PhysicalAvailable.ToUInt64() * 4096 : 0;
        }
    }

    /// <summary>
    /// 임시 파일. 정해진 폴더 밖으로는 절대 나가지 않고, 정션·링크는 들어가지도 지우지도 않는다.
    /// 시작 폴더가 정션이면 그 종류는 통째로 건너뛴다. 최근에 만들거나 고친 것이 하나라도 든 폴더는 남긴다.
    /// </summary>
    internal sealed class Temp
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
        struct FD { public uint Attr; public long C, A, W; public uint Hi, Lo, R0, R1; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string Alt; }
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr FindFirstFileExW(string p, int lvl, out FD d, int op, IntPtr f, int fl);
        [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool FindNextFileW(IntPtr h, out FD d);
        [DllImport("kernel32")] static extern bool FindClose(IntPtr h);
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool DeleteFileW(string p);
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool RemoveDirectoryW(string p);
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern uint GetFileAttributesW(string p);
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateFileW(string p, uint acc, uint share, IntPtr sa, uint disp, uint fl, IntPtr t);
        [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern uint GetFinalPathNameByHandleW(IntPtr h, StringBuilder s, uint n, uint f);

        internal sealed class Cat
        {
            public string Key, Label; public string[] Roots; public int Days; public bool On, Cache; // Cache = 다시 받을 수 있는 것이라 파일마다 날짜를 본다
            public long Bytes, Deletable; public int Files; public bool Blocked; // 정션이라 건너뜀
        }
        internal sealed class Done { public long Bytes; public int Files, Failed; }

        public readonly List<Cat> Cats = new List<Cat>();
        public bool Scanned;
        public volatile bool Busy;
        public string LogFile;

        public Temp()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), win = Environment.GetFolderPath(Environment.SpecialFolder.Windows), pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            Add("user", Tx("사용자 임시 파일", "User temp files"), 7, true, System.IO.Path.GetTempPath().TrimEnd('\\'));
            Add("win", Tx("윈도우 임시 파일", "Windows temp files"), 7, true, win + @"\Temp");
            Add("update", Tx("윈도우 업데이트 파일", "Windows Update files"), 14, true, win + @"\SoftwareDistribution\Download");
            Add("crash", Tx("오류 보고·덤프", "Error reports, dumps"), 14, true, pd + @"\Microsoft\Windows\WER\ReportArchive", pd + @"\Microsoft\Windows\WER\ReportQueue", local + @"\Microsoft\Windows\WER\ReportArchive", local + @"\CrashDumps", win + @"\Minidump");
            Add("npm", Tx("npm·pip 캐시", "npm, pip cache"), 30, false, local + @"\npm-cache\_cacache", local + @"\pip\cache").Cache = true;
            Add("chrome", Tx("Chrome 캐시", "Chrome cache"), 7, false, local + @"\Google\Chrome\User Data\Default\Cache", local + @"\Google\Chrome\User Data\Default\Code Cache");
        }
        public Temp(string key, string label, int days, params string[] roots) { Add(key, label, days, true, roots); }
        Cat Add(string key, string label, int days, bool on, params string[] roots) { var c = new Cat { Key = key, Label = label, Days = days, On = on, Roots = roots }; Cats.Add(c); return c; }

        static string L(string p) { return @"\\?\" + p; }

        /// <summary>폴더 자신이나 윗 폴더가 정션·링크가 아니고, 진짜 위치가 적힌 그대로인지</summary>
        public static bool Safe(string root)
        {
            if (string.IsNullOrEmpty(root) || root.Length < 8 || root[1] != ':') return false;
            uint at = GetFileAttributesW(L(root));
            if (at == 0xFFFFFFFF || (at & 0x10) == 0 || (at & 0x400) != 0) return false;
            IntPtr h = CreateFileW(root, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
            if (h == (IntPtr)(-1)) return false;
            try
            {
                var sb = new StringBuilder(1024);
                if (GetFinalPathNameByHandleW(h, sb, 1024, 0) == 0) return false;
                string real = sb.ToString(); if (real.StartsWith(@"\\?\")) real = real.Substring(4);
                return string.Equals(real, System.IO.Path.GetFullPath(LongName(root)), StringComparison.OrdinalIgnoreCase);
            }
            finally { Native.CloseHandle(h); }
        }
        [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern uint GetLongPathNameW(string s, StringBuilder d, uint n);
        static string LongName(string p) { var sb = new StringBuilder(1024); return GetLongPathNameW(p, sb, 1024) > 0 ? sb.ToString() : p; }

        struct Info { public long Bytes, Old; public int Files; public bool Fresh, Link, Work; }

        static bool Enter(string dir, out IntPtr h, out FD d) { h = FindFirstFileExW(L(dir) + @"\*", 1, out d, 0, IntPtr.Zero, 2); return h != (IntPtr)(-1); }

        static void Look(string dir, long limit, ref Info t, int depth)
        {
            IntPtr h; FD d;
            if (!Enter(dir, out h, out d)) { t.Fresh = true; return; } // 못 읽는 것은 건드리지 않는다
            do
            {
                if (d.Name == "." || d.Name == "..") continue;
                if ((d.Attr & 0x400) != 0) { t.Link = true; continue; }
                if (d.Name == ".git" || d.Name == "node_modules") t.Work = true; // 작업 폴더로 보이면 통째로 남긴다
                if ((d.Attr & 0x10) != 0) { if (depth < 50) Look(dir + "\\" + d.Name, limit, ref t, depth + 1); else t.Fresh = true; if (d.C >= limit) t.Fresh = true; }
                else { long s = ((long)d.Hi << 32) + d.Lo; t.Bytes += s; t.Files++; if (Math.Max(d.C, d.W) >= limit) t.Fresh = true; else t.Old += s; }
            } while (FindNextFileW(h, out d));
            FindClose(h);
        }

        IEnumerable<KeyValuePair<string, FD>> Top(string root)
        {
            IntPtr h; FD d; var l = new List<KeyValuePair<string, FD>>();
            if (!Enter(root, out h, out d)) return l;
            do { if (d.Name != "." && d.Name != "..") l.Add(new KeyValuePair<string, FD>(root + "\\" + d.Name, d)); } while (FindNextFileW(h, out d));
            FindClose(h);
            return l;
        }

        static bool Skip(string name) { return name.Equals("claude", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LagWatch", StringComparison.OrdinalIgnoreCase); }

        public void Scan()
        {
            if (Busy) return;
            Busy = true;
            try
            {
                foreach (var c in Cats) Scan(c);
                Scanned = true;
            }
            finally { Busy = false; }
        }

        void Scan(Cat c)
        {
            long bytes = 0, del = 0; int files = 0; bool blocked = false;
            long limit = DateTime.UtcNow.AddDays(-c.Days).ToFileTimeUtc();
            foreach (var root in c.Roots)
            {
                if (!Directory.Exists(root)) continue;
                if (!Safe(root)) { blocked = true; continue; }
                foreach (var e in Top(root))
                {
                    if ((e.Value.Attr & 0x400) != 0 || Skip(e.Value.Name)) continue;
                    if ((e.Value.Attr & 0x10) != 0)
                    {
                        var t = new Info(); Look(e.Key, limit, ref t, 0);
                        bytes += t.Bytes; files += t.Files;
                        if (c.Cache) { if (!t.Link) del += t.Old; }
                        else if (!t.Fresh && !t.Link && !t.Work && e.Value.C < limit) del += t.Bytes;
                    }
                    else { long s = ((long)e.Value.Hi << 32) + e.Value.Lo; bytes += s; files++; if (Math.Max(e.Value.C, e.Value.W) < limit) del += s; }
                }
            }
            c.Bytes = bytes; c.Deletable = del; c.Files = files; c.Blocked = blocked && bytes == 0;
        }

        static bool ChromeOn() { return Sys.Running("chrome.exe"); }

        /// <summary>켜 둔 종류만 지운다. 지운 폴더와 크기를 기록 파일에 남긴다.</summary>
        public Done Delete()
        {
            var r = new Done();
            if (Busy) return r;
            Busy = true;
            var log = new StringBuilder();
            try
            {
                foreach (var c in Cats)
                {
                    if (!c.On) continue;
                    if (c.Key == "chrome" && ChromeOn()) continue; // 켜져 있는 Chrome의 캐시는 건드리지 않는다
                    long limit = DateTime.UtcNow.AddDays(-c.Days).ToFileTimeUtc();
                    foreach (var root in c.Roots)
                    {
                        if (!Directory.Exists(root) || !Safe(root)) continue;
                        long b0 = r.Bytes; int f0 = r.Files;
                        foreach (var e in Top(root))
                        {
                            if ((e.Value.Attr & 0x400) != 0 || Skip(e.Value.Name)) continue;
                            if ((e.Value.Attr & 0x10) != 0)
                            {
                                var t = new Info(); Look(e.Key, limit, ref t, 0);
                                if (t.Link || (!c.Cache && (t.Fresh || t.Work || e.Value.C >= limit))) continue;
                                Remove(e.Key, limit, r, 0);
                            }
                            else if (Math.Max(e.Value.C, e.Value.W) < limit) One(e.Key, e.Value, r);
                        }
                        if (r.Files > f0) log.Append(DateTime.Now.ToString("s")).Append('\t').Append(c.Label).Append('\t').Append(root).Append('\t').Append(r.Files - f0).Append(Tx("개\t", " files\t")).Append((r.Bytes - b0) / 1048576).Append("MB\r\n");
                    }
                }
                if (LogFile != null && log.Length > 0) try { File.AppendAllText(LogFile, log.ToString(), new UTF8Encoding(false)); } catch (Exception) { }
                foreach (var c in Cats) Scan(c);
            }
            finally { Busy = false; }
            return r;
        }

        static void One(string path, FD d, Done r)
        {
            if (DeleteFileW(L(path))) { r.Bytes += ((long)d.Hi << 32) + d.Lo; r.Files++; } else r.Failed++;
        }

        static void Remove(string dir, long limit, Done r, int depth)
        {
            // 들어가기 직전에 한 번 더: 그새 링크로 바뀌었으면 손대지 않는다
            uint at = GetFileAttributesW(L(dir));
            if (at == 0xFFFFFFFF || (at & 0x400) != 0 || depth > 50) return;
            foreach (var e in List(dir))
            {
                if ((e.Value.Attr & 0x400) != 0) return;
                if ((e.Value.Attr & 0x10) != 0) Remove(e.Key, limit, r, depth + 1);
                else if (Math.Max(e.Value.C, e.Value.W) < limit) One(e.Key, e.Value, r);
            }
            RemoveDirectoryW(L(dir)); // 비었을 때만 지워진다
        }
        static List<KeyValuePair<string, FD>> List(string dir)
        {
            IntPtr h; FD d; var l = new List<KeyValuePair<string, FD>>();
            if (!Enter(dir, out h, out d)) return l;
            do { if (d.Name != "." && d.Name != "..") l.Add(new KeyValuePair<string, FD>(dir + "\\" + d.Name, d)); } while (FindNextFileW(h, out d));
            FindClose(h);
            return l;
        }

        public long Total(bool onlyOn) { long s = 0; foreach (var c in Cats) if (!onlyOn || c.On) s += c.Deletable; return s; }
    }

    /// <summary>다 쓴 것이 1.5MB 쌓이면 바로 치운다. 쌓아 두었다 치우면 그동안 커밋이 올라가 보인다.</summary>
    internal static class Tidy
    {
        static long floor = long.MaxValue; static int last;
        public static int Count;
        public static void Step()
        {
            long now = GC.GetTotalMemory(false);
            if (floor == long.MaxValue) floor = now;
            if (now < floor) floor = now;
            if (now - floor < 1536 << 10 || Environment.TickCount - last < 4000) return;
            last = Environment.TickCount; Count++;
            GC.Collect(); floor = GC.GetTotalMemory(false);
        }
    }
}
