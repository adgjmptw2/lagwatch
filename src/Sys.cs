using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using static LagWatch.Lang;

namespace LagWatch
{
    /// <summary>시간 재기. System.dll 을 불러오지 않게 직접 둔다.</summary>
    internal sealed class Stopwatch
    {
        [DllImport("kernel32.dll")] static extern bool QueryPerformanceCounter(out long v);
        [DllImport("kernel32.dll")] static extern bool QueryPerformanceFrequency(out long v);
        public static readonly long Frequency = Freq();
        static long Freq() { long v; QueryPerformanceFrequency(out v); return v; }
        public static long GetTimestamp() { long v; QueryPerformanceCounter(out v); return v; }
        long at = GetTimestamp();
        public static Stopwatch StartNew() { return new Stopwatch(); }
        public TimeSpan Elapsed { get { return TimeSpan.FromTicks((long)((GetTimestamp() - at) * 1e7 / Frequency)); } }
        public long ElapsedMilliseconds { get { return (GetTimestamp() - at) * 1000 / Frequency; } }
        public void Restart() { at = GetTimestamp(); }
    }

    /// <summary>프로그램 실행, 파일 설명 같은 작은 일들</summary>
    internal static unsafe class Sys
    {
        /// <summary>화면 오류 하나로 감시가 꺼지지 않게 적어 두고 계속한다.</summary>
        public static string ErrorFile;
        public static void Note(Exception ex)
        {
            try { var f = ErrorFile; if (f != null && (!System.IO.File.Exists(f) || new System.IO.FileInfo(f).Length < 200000)) System.IO.File.AppendAllText(f, DateTime.Now.ToString("s") + " " + ex + "\r\n"); }
            catch (Exception) { }
        }
        public static string Exe { get { return System.Reflection.Assembly.GetEntryAssembly().Location; } }

        // ---- 실행 ----

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct EXEC { public int Cb; public uint Mask; public IntPtr Hwnd; public string Verb, File, Args, Dir; public int Show; public IntPtr Inst, IdList; public string Class; public IntPtr Key; public uint Hot; public IntPtr Icon, Process; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool ShellExecuteExW(ref EXEC e);
        [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr h, out int code);

        /// <summary>실패하면 예외. wait 를 주면 끝날 때까지 기다려 끝난 값을 준다(못 기다리면 -1).</summary>
        public static int Open(string file, string args = null, string dir = null, string verb = null, int wait = 0, bool hide = false)
        {
            var e = new EXEC { Mask = 0x400 | (wait > 0 ? 0x40u | 0x100u : 0), File = file, Args = args, Dir = dir, Verb = verb, Show = hide ? 0 : 1 }; e.Cb = Marshal.SizeOf(typeof(EXEC));
            if (!ShellExecuteExW(ref e)) throw new InvalidOperationException(Error(Marshal.GetLastWin32Error()));
            if (wait == 0 || e.Process == IntPtr.Zero) return 0;
            try { int c; return Native.WaitForSingleObject(e.Process, (uint)wait) == 0 && GetExitCodeProcess(e.Process, out c) ? c : -1; }
            finally { Native.CloseHandle(e.Process); }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int FormatMessageW(int fl, IntPtr src, int id, int lang, StringBuilder sb, int n, IntPtr a);
        public static string Error(int code) { var sb = new StringBuilder(300); return FormatMessageW(0x1200, IntPtr.Zero, code, 0, sb, 300, IntPtr.Zero) > 0 ? sb.ToString().Trim() : Tx("오류 ", "Error ") + code; }

        [DllImport("kernel32.dll")] public static extern int GetCurrentProcessId();

        /// <summary>그 이름의 프로그램이 켜져 있는지</summary>
        public static bool Running(string exe)
        {
            int n = 768 * 1024, ret; byte* m = (byte*)Marshal.AllocHGlobal(n);
            try
            {
                uint st; while ((st = Native.NtQuerySystemInformation(5, m, n, out ret)) == Native.STATUS_INFO_LENGTH_MISMATCH) { n = Math.Max(n, ret) + 128 * 1024; m = (byte*)Marshal.ReAllocHGlobal((IntPtr)m, (IntPtr)n); }
                if (st != 0) return false; byte* p = m;
                while (true)
                {
                    int next = *(int*)p, nl = *(ushort*)(p + 0x38); char* np = (char*)*(IntPtr*)(p + 0x40);
                    if (nl / 2 == exe.Length && np != null && string.Equals(new string(np, 0, nl / 2), exe, StringComparison.OrdinalIgnoreCase)) return true;
                    if (next == 0) return false; p += next;
                }
            }
            finally { Marshal.FreeHGlobal((IntPtr)m); }
        }

        // ---- 파일 설명 ----

        [DllImport("version.dll", CharSet = CharSet.Unicode)] static extern int GetFileVersionInfoSizeW(string f, out int h);
        [DllImport("version.dll", CharSet = CharSet.Unicode)] static extern bool GetFileVersionInfoW(string f, int h, int len, byte* data);
        [DllImport("version.dll", CharSet = CharSet.Unicode)] static extern bool VerQueryValueW(byte* block, string sub, out IntPtr p, out uint len);
        public static string Description(string path)
        {
            int z, n = GetFileVersionInfoSizeW(path, out z); if (n <= 0) return null; byte* b = (byte*)Marshal.AllocHGlobal(n);
            try
            {
                if (!GetFileVersionInfoW(path, 0, n, b)) return null; IntPtr p; uint len;
                if (VerQueryValueW(b, @"\VarFileInfo\Translation", out p, out len) && len >= 4) { string s = One(b, ((ushort*)p)[0].ToString("x4") + ((ushort*)p)[1].ToString("x4")); if (!string.IsNullOrEmpty(s)) return s; }
                foreach (var k in new[] { "040904B0", "040904E4", "04090000" }) { string s = One(b, k); if (!string.IsNullOrEmpty(s)) return s; }
                return null;
            }
            finally { Marshal.FreeHGlobal((IntPtr)b); }
        }
        static string One(byte* b, string lang) { IntPtr p; uint len; return VerQueryValueW(b, @"\StringFileInfo\" + lang + @"\FileDescription", out p, out len) && len > 0 ? Marshal.PtrToStringUni(p) : null; }

        // ---- 글 ----

        /// <summary>"key"  "값" 꼴의 값들</summary>
        public static List<string> Quoted(string t, string key)
        {
            var l = new List<string>(); string k = "\"" + key + "\""; int i = 0;
            while ((i = t.IndexOf(k, i, StringComparison.Ordinal)) >= 0)
            {
                i += k.Length; int j = i; while (j < t.Length && char.IsWhiteSpace(t[j])) j++; if (j == i || j >= t.Length || t[j] != '"') continue;
                int e = t.IndexOf('"', j + 1); if (e <= j + 1) continue; l.Add(t.Substring(j + 1, e - j - 1)); i = e + 1;
            }
            return l;
        }
        /// <summary>"… 1시간 4분", "… 22분", "… 1h 4m", "… 22m" 끝의 분</summary>
        public static int Minutes(string s)
        {
            int h = 0, m = 0, e = s.Length;
            if (s.EndsWith("분", StringComparison.Ordinal) || e >= 2 && s[e - 1] == 'm' && Digit(s[e - 2])) { int v, at = Digits(s, e - 1, out v); if (at == e - 1) return 0; m = v; e = at; while (e > 0 && s[e - 1] == ' ') e--; }
            if (e >= 2 && string.CompareOrdinal(s, e - 2, "시간", 0, 2) == 0) { int v, at = Digits(s, e - 2, out v); if (at < e - 2) h = v; }
            else if (e >= 2 && s[e - 1] == 'h' && Digit(s[e - 2])) { int v; Digits(s, e - 1, out v); h = v; }
            return h * 60 + m;
        }
        static bool Digit(char c) { return c >= '0' && c <= '9'; }
        static int Digits(string s, int end, out int v) { int i = end; while (i > 0 && s[i - 1] >= '0' && s[i - 1] <= '9') i--; v = i < end && end - i < 8 ? int.Parse(s.Substring(i, end - i)) : 0; return i; }
    }

    /// <summary>이 프로그램 자신의 사용량</summary>
    internal static class Self
    {
        [StructLayout(LayoutKind.Sequential)] struct MEM { public int Cb, Faults; public IntPtr PeakWs, Ws, A, B, C, D, Page, PeakPage, Private; }
        [DllImport("kernel32.dll")] static extern bool K32GetProcessMemoryInfo(IntPtr h, ref MEM m, int cb);
        static MEM Get() { var m = new MEM(); m.Cb = Marshal.SizeOf(typeof(MEM)); K32GetProcessMemoryInfo(Native.GetCurrentProcess(), ref m, m.Cb); return m; }
        public static double Commit { get { return Get().Private.ToInt64() / 1048576.0; } }
        public static double Ram { get { return Get().Ws.ToInt64() / 1048576.0; } }
        public static double Cpu { get { long c, e, k, u; Native.GetProcessTimes(Native.GetCurrentProcess(), out c, out e, out k, out u); return (k + u) / 1e7; } }
    }
}
