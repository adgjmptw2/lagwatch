using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

/// <summary>보이지 않는 데스크톱에서 검색창에 한글을 넣고, 트레이 메뉴를 열어 본다. Desk3 exe 그림폴더</summary>
internal static class Desk3
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
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, StringBuilder l);
    [DllImport("user32.dll")] static extern int GetMenuItemCount(IntPtr m);
    [DllImport("user32.dll")] static extern uint GetMenuState(IntPtr m, uint id, uint fl);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetMenuStringW(IntPtr m, uint id, StringBuilder s, int n, uint fl);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] static extern IntPtr Send(IntPtr h, uint m, IntPtr w, IntPtr l);
    static IntPtr desk; static int pass, fail; const string Name = "lagwatch-d3";
    static void Ok(bool c, string n) { if (c) pass++; else fail++; Console.WriteLine((c ? "  통과 " : "  실패 ") + n); }
    static Process Run(string exe, string args) { var si = new SI { desktop = Name }; si.cb = Marshal.SizeOf(typeof(SI)); PI pi; CreateProcessW(null, new StringBuilder("\"" + exe + "\" " + args), IntPtr.Zero, IntPtr.Zero, false, 0x4000, IntPtr.Zero, null, ref si, out pi); return Process.GetProcessById(pi.pid); }
    static string Text(IntPtr h) { var s = new StringBuilder(256); GetWindowTextW(h, s, 256); return s.ToString(); }
    static string Cls(IntPtr h) { var s = new StringBuilder(64); GetClassNameW(h, s, 64); return s.ToString(); }
    static IntPtr Top(int pid, Func<IntPtr, bool> f) { IntPtr r = IntPtr.Zero; EnumDesktopWindows(desk, (h, l) => { uint q; GetWindowThreadProcessId(h, out q); if (q == pid && f(h)) r = h; return true; }, IntPtr.Zero); return r; }
    static void Shot(IntPtr h, string png)
    {
        RECT r; GetWindowRect(h, out r);
        using (var b = new Bitmap(Math.Max(1, r.r - r.l), Math.Max(1, r.b - r.t))) { using (var g = Graphics.FromImage(b)) { IntPtr dc = g.GetHdc(); PrintWindow(h, dc, 2); g.ReleaseHdc(dc); } b.Save(png); }
    }
    static void Key(IntPtr h, int vk) { PostMessageW(h, 0x100, (IntPtr)vk, IntPtr.Zero); Thread.Sleep(60); PostMessageW(h, 0x101, (IntPtr)vk, (IntPtr)unchecked((int)0xC0000001)); Thread.Sleep(120); }
    static IntPtr Menu(int pid) { return Top(pid, h => IsWindowVisible(h) && Cls(h) == "#32768"); }

    static int Main(string[] a)
    {
        Console.OutputEncoding = new UTF8Encoding(false); string exe = a[0], dir = a[1];
        string data = Path.Combine(Path.GetTempPath(), "LagWatch-d3"); try { Directory.Delete(data, true); } catch (Exception) { }
        desk = CreateDesktopW(Name, null, IntPtr.Zero, 0, 0x10000000, IntPtr.Zero); SetThreadDesktop(desk); Process p = null;
        try
        {
            p = Run(exe, "--id d3"); Thread.Sleep(5000);
            IntPtr w = Top(p.Id, h => { RECT r; GetWindowRect(h, out r); return IsWindowVisible(h) && Text(h) == "렉 감시" && r.r - r.l > 600; }); Ok(w != IntPtr.Zero, "큰 창");
            PostMessageW(w, 0x200, IntPtr.Zero, (IntPtr)((30 << 16) | 250)); PostMessageW(w, 0x201, (IntPtr)1, (IntPtr)((30 << 16) | 250)); PostMessageW(w, 0x202, IntPtr.Zero, (IntPtr)((30 << 16) | 250)); Thread.Sleep(900);
            IntPtr ed = IntPtr.Zero; EnumChildWindows(w, (h, l) => { if (Cls(h).Equals("Edit", StringComparison.OrdinalIgnoreCase)) ed = h; return true; }, IntPtr.Zero); Ok(ed != IntPtr.Zero && IsWindowVisible(ed), "프로그램 탭 검색창");
            Shot(w, dir + "\\d3_s0.png");
            foreach (char c in "롤 클") { PostMessageW(ed, 0x102, (IntPtr)c, IntPtr.Zero); Thread.Sleep(80); }
            Thread.Sleep(900); var sb = new StringBuilder(64); SendMessageW(ed, 0xD, (IntPtr)64, sb); Ok(sb.ToString() == "롤 클", "한글이 들어감 [" + sb + "]"); Shot(w, dir + "\\d3_s1.png");
            Key(ed, 0x1B); Thread.Sleep(600); sb.Clear(); SendMessageW(ed, 0xD, (IntPtr)64, sb); Ok(sb.Length == 0 && Top(p.Id, h => h == w) != IntPtr.Zero, "Esc 한 번은 지우기만"); Shot(w, dir + "\\d3_s2.png");
            PostMessageW(w, 0x10, IntPtr.Zero, IntPtr.Zero); Thread.Sleep(2000);

            IntPtr tray = Top(p.Id, h => Text(h) == "LagWatchTray"); Ok(tray != IntPtr.Zero, "트레이 창");
            for (int round = 0; round < 2; round++)
            {
                PostMessageW(tray, 0x8001, (IntPtr)1, (IntPtr)0x205); Thread.Sleep(900); IntPtr m = Menu(p.Id); Ok(m != IntPtr.Zero, "오른쪽 클릭 메뉴");
                if (m == IntPtr.Zero) break;
                IntPtr hm = Send(m, 0x1E1, IntPtr.Zero, IntPtr.Zero); int n = GetMenuItemCount(hm); var names = new StringBuilder();
                for (int i = 0; i < n; i++) { var s = new StringBuilder(64); GetMenuStringW(hm, (uint)i, s, 64, 0x400); names.Append(s.Length == 0 ? "|" : s + " "); }
                if (round == 0) { Ok(n == 10 && names.ToString().StartsWith("열기 램 청소 임시 파일 |알림 ") && names.ToString().Contains("English"), "메뉴 항목 " + names); Shot(m, dir + "\\d3_m0.png"); }
                for (int i = 0; i < 4; i++) Key(m, 0x28);
                bool at = (GetMenuState(hm, 4, 0x400) & 0x80) != 0; var s4 = new StringBuilder(64); GetMenuStringW(hm, 4, s4, 64, 0x400);
                Ok(at && s4.ToString() == "알림", "아래 키 네 번이면 알림에 섬");
                if (!at) { Key(m, 0x1B); Key(m, 0x1B); break; } // 다른 항목이면 누르지 않는다
                Key(m, 0x0D); Thread.Sleep(900);
                string set = File.Exists(Path.Combine(data, "settings.txt")) ? File.ReadAllText(Path.Combine(data, "settings.txt")) : "";
                Ok(set.Contains(round == 0 ? "notify=0" : "notify=1"), "알림 " + (round == 0 ? "끔" : "켬") + "이 저장됨");
            }
            PostMessageW(tray, 0x8001, (IntPtr)1, (IntPtr)0x205); Thread.Sleep(900); IntPtr mm = Menu(p.Id); if (mm != IntPtr.Zero) Key(mm, 0x1B); Thread.Sleep(600);
            p.Refresh(); Ok(Menu(p.Id) == IntPtr.Zero && !p.HasExited, "Esc 로 메뉴만 닫힘");
            PostMessageW(tray, 0x8001, (IntPtr)1, (IntPtr)0x202); Thread.Sleep(1200); IntPtr panel = Top(p.Id, h => IsWindowVisible(h) && Text(h) == "" && Cls(h) == "LagWatchWnd"); Ok(panel != IntPtr.Zero, "왼쪽 클릭 작은 창");
            // 언어: 메뉴에서 English 를 고르면 끝났다가 영어로 다시 켜진다
            PostMessageW(panel, 0x100, (IntPtr)0x1B, IntPtr.Zero); Thread.Sleep(700);
            PostMessageW(tray, 0x8001, (IntPtr)1, (IntPtr)0x205); Thread.Sleep(900); IntPtr ml = Menu(p.Id), hl = ml == IntPtr.Zero ? IntPtr.Zero : Send(ml, 0x1E1, IntPtr.Zero, IntPtr.Zero); if (ml != IntPtr.Zero) for (int i = 0; i < 7; i++) Key(ml, 0x28);
            var s7 = new StringBuilder(64); GetMenuStringW(hl, 7, s7, 64, 0x400); bool on7 = (GetMenuState(hl, 7, 0x400) & 0x80) != 0; Ok(on7 && s7.ToString() == "English", "메뉴의 English [" + s7 + "]");
            if (on7 && s7.ToString() == "English")
            {
                Key(ml, 0x0D); Ok(p.WaitForExit(9000), "언어를 고르면 끝남"); Process n2 = null;
                for (int t = 0; t < 40 && n2 == null; t++) { Thread.Sleep(250); foreach (var q in Process.GetProcessesByName("LagWatch")) try { if (q.Id != p.Id && string.Equals(q.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase)) n2 = q; } catch (Exception) { } }
                Ok(n2 != null, "다시 켜짐");
                if (n2 != null)
                {
                    p = n2; Thread.Sleep(3000); Run(exe, "--id d3").WaitForExit(4000); Thread.Sleep(2500);
                    IntPtr we = Top(p.Id, h => { RECT r; GetWindowRect(h, out r); return IsWindowVisible(h) && Text(h) == "LagWatch" && r.r - r.l > 600; }); Ok(we != IntPtr.Zero, "영어 창"); if (we != IntPtr.Zero) Shot(we, dir + "\\d3_en.png");
                    Ok(File.ReadAllText(Path.Combine(data, "settings.txt")).Contains("lang=en"), "고른 언어 저장");
                }
            }
            Run(exe, "--id d3 --quit").WaitForExit(4000); Ok(p.WaitForExit(8000), "끝남");
            string err = Path.Combine(data, "error.txt"); Ok(!File.Exists(err), "오류 없음"); if (File.Exists(err)) Console.WriteLine(File.ReadAllText(err));
        }
        finally { if (p != null && !p.HasExited) p.Kill(); CloseDesktop(desk); try { Directory.Delete(data, true); } catch (Exception) { } }
        Console.WriteLine("통과 {0}, 실패 {1}", pass, fail); return fail;
    }
}
