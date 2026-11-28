using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace LagWatch
{
    internal static partial class Tests
    {
        static string tmp;
        static string Tmp { get { if (tmp == null) { tmp = Path.Combine(Path.GetTempPath(), "LagWatchTest" + Process.GetCurrentProcess().Id); Directory.CreateDirectory(tmp); } return tmp; } }

        static void More()
        {
            FrameTest(); KernTest(); RuleText(); SeriesTest(); GroupTest(); TempTest(); TrimTest(); FixReal(); PartsTest(); UiTest(); TimeTest(); UsageTest(); FailTest(); GameTests(); StreamTests();
            try { Directory.Delete(Tmp, true); } catch (Exception) { }
        }

        // ---- 프레임 ----

        static void FrameTest()
        {
            long q = Stopwatch.Frequency; Func<double, long> ms = x => (long)(x * q / 1000);
            var f = new Frames { Active = true }; long t = q; int ep = 1000; double acc = 0;
            Action<double> push = d => { t += ms(d); acc += d; f.Push(t, ep + (int)(acc / 1000)); };
            for (int i = 0; i < 2000; i++) push(5);
            double fps, mx; int h; f.Last(ep + (int)(acc / 1000), out fps, out mx, out h);
            Ok(Math.Abs(fps - 200) < 12 && h == 0 && mx < 6, "고른 프레임 " + fps + " " + mx + " " + h);
            push(60); f.Last(ep + (int)(acc / 1000), out fps, out mx, out h); Ok(h == 1 && mx > 59, "60ms 한 번 = 끊김");
            for (int i = 0; i < 200; i++) push(5); push(30); f.Last(ep + (int)(acc / 1000), out fps, out mx, out h); Ok(h == 2, "5ms 하다가 30ms = 끊김");
            for (int i = 0; i < 400; i++) push(16.6); push(30); f.Last(ep + (int)(acc / 1000), out fps, out mx, out h); Ok(h == 2, "60fps 게임에서 30ms 는 끊김 아님 " + h);
            push(2500); f.Last(ep + (int)(acc / 1000), out fps, out mx, out h); Ok(h == 2, "2초 넘게 멈춘 것은 로딩");
            f.Active = false; push(300); f.Last(ep + (int)(acc / 1000), out fps, out mx, out h); Ok(h == 2, "창이 뒤에 있으면 안 셈");
            f.Active = true; for (int i = 0; i < 500; i++) push(80); Ok(f.Since(0).Count <= 128, "끊김 기록은 정해진 수까지 " + f.Since(0).Count);
            f.Last(ep + (int)(acc / 1000) + 100, out fps, out mx, out h); Ok(h == 0 && fps == 0, "1분 지난 것은 빠짐");
            Ok(Frames.IsHitch(50, 5) && !Frames.IsHitch(24, 3) && Frames.IsHitch(26, 6) && !Frames.IsHitch(40, 16), "끊김 기준");
        }

        static void KernTest()
        {
            long q = Stopwatch.Frequency; Func<double, long> ms = x => (long)(x * q / 1000) + q;
            var k = new Kern { GamePid = 100 }; k.Map(10, 100); k.Map(11, 100); k.Map(20, 200); k.Map(30, 300);
            k.Switch(0, 10, 0, ms(0), 0); k.Switch(0, 20, 10, ms(5), 1); k.Switch(0, 10, 20, ms(9), 5); k.Switch(0, 0, 10, ms(10), 5); k.Switch(0, 10, 0, ms(20), 0);
            k.Switch(1, 30, 0, ms(0), 0); k.Switch(1, 0, 30, ms(7), 5);
            var r = k.Take();
            Ok(Math.Abs(r.Core[100][0] - 6) < .01 && Math.Abs(r.Core[200][0] - 4) < .01 && Math.Abs(r.Core[300][1] - 7) < .01, "코어별 시간");
            Ok(Math.Abs(r.Robbed - 4) < .01 && Math.Abs(r.Thief[200] - 4) < .01 && Math.Abs(r.RobbedMax - 4) < .01, "밀려난 시간 " + r.Robbed);
            Ok(k.Take().Robbed == 0, "꺼내면 새로 시작");
            k.Map(10, 100); k.Map(11, 100); k.Switch(2, 11, 10, ms(30), 1); k.Switch(2, 10, 11, ms(40), 5); r = k.Take(); Ok(r.Robbed == 0, "게임 자기 스레드끼리는 뺏긴 게 아님");
            k.Dpc(1, 0.5); k.Dpc(1, 2); r = k.Take(); Ok(r.Drivers.Count == 1 && Math.Abs(r.Drivers[0].Max - 2) < .01 && r.Drivers[0].N == 2, "드라이버");
            // 실제 추적을 짧게(관리자일 때만)
            if (Cleaner.Elevated)
            {
                Ok(k.Start(0) && k.Running, "커널 추적 시작 " + k.Error); Thread.Sleep(1200); r = k.Take(); double tot = 0; foreach (var v in r.Core.Values) foreach (double x in v) tot += x;
                Ok(tot > r.Ms * Environment.ProcessorCount * .7, "코어 시간 합 " + (tot / r.Ms).ToString("0.0")); Ok(r.Drivers.Count > 2, "드라이버 수 " + r.Drivers.Count); k.Close(); Ok(!k.Running, "추적 닫힘");
                for (int i = 0; i < 6; i++) { Ok(k.Start(0), "다시 켜기 " + i); k.Close(); }
            }
        }

        // ---- 규칙 글 ----

        static void RuleText()
        {
            var r = Fixer.Parse(Fixer.Format(new Rule { Key = "node|x.js", Label = "no|de", Cores = 2, Prio = 1, Always = true }));
            Ok(r != null && r.Key == "node|x.js" && r.Label == "no de" && r.Cores == 2 && r.Prio == 1 && r.Always, "규칙 저장");
            Ok(Fixer.Parse("a|b") == null && Fixer.Parse("") == null && Fixer.Parse("k|l|x|1|0") == null, "망가진 규칙");
            Ok(Fixer.Parse("k|l|9|-3|0").Prio == 2 && Fixer.Parse("k|l|9|-3|0").Cores == 0, "범위 밖");
            int[] co = { 0, 0, 1, 1, 2, 2, 3, 3 };
            Ok(Fixer.Mask(co, 4, 1) == 0xC0 && Fixer.Mask(co, 4, 2) == 0xF0 && Fixer.Mask(co, 4, 9) == 0xFF && Fixer.Mask(co, 4, 0) == 0xC0, "뒤쪽 코어");
            Eq(Fixer.Exe("\"C:\\a b\\c.exe\" --min"), "C:\\a b\\c.exe", "따옴표"); Eq(Fixer.Exe("C:\\a b\\c.exe --x"), "C:\\a b\\c.exe", "따옴표 없음"); Ok(Fixer.Exe("notepad") == null, "exe 없음");
            Eq(Doctor.Ga("node"), "node가", "node"); Eq(Doctor.Ga("G HUB"), "G HUB가", "G HUB"); Eq(Doctor.Ga("롤"), "롤이", "롤"); Eq(Doctor.Ga("롤 클라이언트"), "롤 클라이언트가", "클라이언트"); Eq(Doctor.Ga("탐색기"), "탐색기가", "탐색기"); Eq(Doctor.Ga("Steam"), "Steam이", "Steam"); Eq(Doctor.Ga("Discord"), "Discord가", "Discord"); Eq(Doctor.Ga("Chrome"), "Chrome가", "Chrome"); Eq(Doctor.Ga(""), "", "빈 글");
            Ok(MainView.GameMinutes("League of Legends 22분 · 다른 프로그램 7번") == 22 && MainView.GameMinutes("이터널 리턴 1시간 4분 · 문제 없음") == 64 && MainView.GameMinutes("x 2시간 · y") == 120 && MainView.GameMinutes("abc") == 0, "게임 시간 읽기");
            Ok(MainView.GameMinutes("Eternal Return 1h 4m · no issues") == 64 && MainView.GameMinutes("LoL 22m · x") == 22 && MainView.GameMinutes("x 2h · y") == 120, "게임 시간 읽기(영어)");
        }

        // ---- 늘어나는지 ----

        static float[] S(Func<int, double> f, int n = 181) { var v = new float[n]; for (int i = 0; i < n; i++) v[i] = (float)f(i); return v; }
        static void SeriesTest()
        {
            float a, b; var rnd = new Random(5);
            Ok(Series.Rising(S(i => 762 + i * .5 + rnd.NextDouble() * 4), 181, 60, out a, out b) && b > a + 60, "G HUB처럼 꾸준히");
            Ok(!Series.Rising(S(i => i < 50 ? -1 : i < 56 ? 488 : 680 + rnd.NextDouble() * 20), 181, 60, out a, out b), "켜지고 멈춘 것");
            Ok(!Series.Rising(S(i => 1000 + rnd.NextDouble() * 400), 181, 60, out a, out b), "출렁이는 것");
            Ok(!Series.Rising(S(i => i < 90 ? 1000 : 1900), 181, 60, out a, out b), "한 번 뛴 것");
            Ok(!Series.Rising(S(i => i < 60 ? 1000 + i * 8 : 1480), 181, 60, out a, out b), "앞에서만 오른 것");
            Ok(Series.Rising(S(i => 2465 + i * 4.5 + (i % 30 == 0 ? 100 : 0)), 181, 60, out a, out b), "롤 클라이언트처럼 튀면서 오름");
            Ok(!Series.Rising(S(i => 3000 - i * 2), 181, 60, out a, out b), "줄어드는 것");
            Ok(!Series.Rising(S(i => i < 150 ? -1 : 1000 + i), 181, 60, out a, out b), "막 켠 것");
            Ok(!Series.Rising(S(i => 1000 + i * .2), 181, 60, out a, out b), "너무 조금");
            Ok(!Series.Rising(S(i => i < 140 ? 1000 + i * 3 : 1420 + rnd.NextDouble() * 4), 181, 60, out a, out b), "오르다가 40분째 멈춘 것");
            Ok(Series.Rising(S(i => 900 + i * 3 + rnd.NextDouble() * 6, 61), 61, 60, out a, out b), "1시간 창에서 꾸준히");
        }

        // ---- 묶음 ----

        static void G(string exe, string key, string want, string label = null, bool game = false, bool pseudo = false)
        {
            string k, l; Names.Group(new App { Exe = exe, Key = key ?? exe.ToLowerInvariant(), Label = exe, IsGame = game, Pseudo = pseudo }, out k, out l);
            Eq(k, want, "묶음 " + exe); if (label != null) Eq(l, label, "이름 " + exe);
        }
        static void GroupTest()
        {
            G("LeagueClient", null, "lolclient", "롤 클라이언트"); G("LeagueClientUxRender", null, "lolclient"); G("League of Legends", null, "game|league of legends", "롤", true); G("EternalReturn", null, "game|eternalreturn", "이터널 리턴", true);
            G("node", "node|dist/server.js", "node"); G("query-engine-windows", null, "node"); G("bun", "bun|@scope/tool", "tool", "tool"); G("node", "node|@scope/tool", "node"); G("bun", "bun|x.ts", "bun|x.ts");
            G("svchost", "svchost|Audio", "win"); G("dwm", null, "win"); G("", "*paged", "win", "윈도우", false, true); G("lghub_agent", "lghub", "lghub", "G HUB"); G("vgc", null, "vanguard"); G("chrome", null, "chrome", "Chrome"); G("Riot Client", null, "riot"); G("steamwebhelper", null, "steam"); G("LagWatch", null, "lagwatch");
            Ok(Names.IsGameClient("LeagueClientUx") && Names.IsGameClient("RiotClientServices") && !Names.IsGameClient("lghub"), "게임과 한 몸");
            Ok(Names.IsDatabase("mysqld") && !Names.IsDatabase("node") && Names.IsServer("mysqld"), "DB");
        }

        // ---- 임시 파일: 밖으로 절대 안 나간다 ----

        static string Put(string path, int days, int bytes = 1000)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, new byte[bytes]);
            var t = DateTime.Now.AddDays(-days); File.SetCreationTime(path, t); File.SetLastWriteTime(path, t); return path;
        }
        static void Age(string dir, int days) { var t = DateTime.Now.AddDays(-days); foreach (var d in Directory.GetDirectories(dir)) { if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0) Age(d, days); } try { Directory.SetCreationTime(dir, t); Directory.SetLastWriteTime(dir, t); } catch (Exception) { } }
        static void Link(string link, string target) { using (var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })) { p.StandardOutput.ReadToEnd(); p.WaitForExit(); } }

        static void TempTest()
        {
            string b = Path.Combine(Tmp, "t"), root = b + @"\root", outside = b + @"\outside";
            string keep1 = Put(outside + @"\precious.txt", 400), keep2 = Put(outside + @"\deep\data.bin", 400);
            string old1 = Put(root + @"\old.tmp", 30), new1 = Put(root + @"\new.tmp", 0), old2 = Put(root + @"\olddir\a\b.txt", 30), mix1 = Put(root + @"\mixed\old.txt", 30), mix2 = Put(root + @"\mixed\new.txt", 0);
            string git = Put(root + @"\repo\.git\HEAD", 90), git2 = Put(root + @"\repo\src\x.cs", 90), nm = Put(root + @"\work\node_modules\p\i.js", 90), cl = Put(root + @"\claude\s.txt", 90), kr = Put(root + @"\한글 폴더\파일.txt", 30), longd = Put(root + "\\long\\" + new string('a', 60) + "\\" + new string('b', 60) + "\\c.txt", 30);
            Directory.CreateDirectory(root + @"\haslink"); Put(root + @"\haslink\z.txt", 90); Link(root + @"\haslink\j", outside); Link(root + @"\direct", outside); Link(b + @"\rootlink", outside);
            string open = Put(root + @"\open.tmp", 30), ro = Put(root + @"\ro.tmp", 30);
            foreach (var d in Directory.GetDirectories(root)) if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0) Age(d, 60);
            Ok(Directory.Exists(root + @"\haslink\j\deep") && Directory.Exists(root + @"\direct\deep"), "시험용 정션 만들기");

            Ok(Temp.Safe(root) && !Temp.Safe(root + @"\direct") && !Temp.Safe(b + @"\rootlink") && !Temp.Safe(b + @"\rootlink\deep") && !Temp.Safe(root + @"\nope") && !Temp.Safe("C:\\") && !Temp.Safe("") && !Temp.Safe(null) && !Temp.Safe(old1), "시작 폴더 검사");

            var t = new Temp("x", "시험", 7, root);
            t.Scan(); long will = t.Cats[0].Deletable;
            Ok(will == 1000 * 6, "지울 크기 " + will + " (old, olddir, 한글, long, open, ro)");
            Temp.Done r;
            using (var hold = new FileStream(open, FileMode.Open, FileAccess.Read, FileShare.None)) r = t.Delete();
            Ok(File.Exists(keep1) && File.Exists(keep2), "정션 너머는 그대로"); Ok(Directory.Exists(root + @"\direct") && Directory.Exists(root + @"\haslink\j") && File.Exists(root + @"\haslink\z.txt"), "정션과 정션이 든 폴더는 그대로");
            Ok(!File.Exists(old1) && !File.Exists(old2) && !Directory.Exists(root + @"\olddir") && !File.Exists(kr) && !File.Exists(longd), "오래된 것은 지움");
            Ok(File.Exists(new1) && File.Exists(mix1) && File.Exists(mix2), "새것과 새것이 섞인 폴더는 남김"); Ok(File.Exists(git) && File.Exists(git2) && File.Exists(nm), "작업 폴더는 남김"); Ok(File.Exists(cl), "claude 폴더는 남김"); Ok(File.Exists(open) && r.Failed == 1, "쓰는 중인 것은 남김 " + r.Failed);
            Ok(r.Files == 5 && r.Bytes == 5000, "지운 수 " + r.Files + " " + r.Bytes);

            t = new Temp("x", "시험", 7, b + @"\rootlink"); t.Scan(); Ok(t.Cats[0].Blocked && t.Cats[0].Deletable == 0, "시작 폴더가 정션이면 통째로 건너뜀"); r = t.Delete(); Ok(r.Files == 0 && File.Exists(keep1), "지우지도 않음");
            t = new Temp("x", "시험", 7, root + @"\haslink"); t.Scan(); r = t.Delete(); Ok(File.Exists(keep1) && File.Exists(keep2) && !File.Exists(root + @"\haslink\z.txt"), "안쪽 정션은 그대로, 파일만");
            // 캐시: 파일마다
            string cr = b + @"\cache"; Put(cr + @"\v2\a", 60); string cn = Put(cr + @"\v2\b", 0); Link(cr + @"\lk", outside);
            t = new Temp("x", "시험", 30, cr); t.Cats[0].Cache = true; t.Scan(); Ok(t.Cats[0].Deletable == 1000, "캐시 크기 " + t.Cats[0].Deletable); r = t.Delete(); Ok(r.Files == 1 && File.Exists(cn) && File.Exists(keep1) && Directory.Exists(cr + @"\v2"), "캐시는 오래된 파일만");
            // 시험 폴더의 정션은 먼저 끊고 지운다
            foreach (var j in new[] { root + @"\haslink\j", root + @"\direct", b + @"\rootlink", cr + @"\lk" }) try { Directory.Delete(j); } catch (Exception) { }
            Ok(File.Exists(keep1), "정리 뒤에도 그대로");
            var real = new Temp(); real.Scan(); Ok(real.Scanned && real.Cats.Count >= 5, "실제 폴더는 재기만");
            foreach (var c in real.Cats) foreach (var p in c.Roots) Ok(p.Length > 10 && p.IndexOf("Temp", StringComparison.OrdinalIgnoreCase) + p.IndexOf("Cache", StringComparison.OrdinalIgnoreCase) + p.IndexOf("WER", StringComparison.OrdinalIgnoreCase) + p.IndexOf("Dump", StringComparison.OrdinalIgnoreCase) + p.IndexOf("SoftwareDistribution", StringComparison.OrdinalIgnoreCase) > -5, "정해진 폴더만 " + p);
        }

        // ---- 램 청소 대상 ----

        static void TrimTest()
        {
            Func<string, long, double, Proc> mk = (n, mb, cores) => new Proc { Pid = 5000, Session = 1, Name = n, Ws = mb << 20, Cores = cores, App = new App { Exe = n, Key = n, Protected = Names.IsGuarded(n) } };
            Ok(Cleaner.CanTrim(mk("notepad", 200, 0), 1, 2, 0), "놀고 있는 것");
            Ok(!Cleaner.CanTrim(mk("notepad", 200, .3), 1, 2, 0), "일하는 중"); Ok(!Cleaner.CanTrim(mk("notepad", 10, 0), 1, 2, 0), "작은 것"); Ok(!Cleaner.CanTrim(mk("notepad", 200, 0), 5000, 2, 0), "지금 쓰는 것"); Ok(!Cleaner.CanTrim(mk("notepad", 200, 0), 1, 5000, 0), "자기 자신");
            foreach (var n in new[] { "mysqld", "audiodg", "dwm", "csrss", "lsass", "vgc", "EasyAntiCheat_EOS", "explorer", "System", "Memory Compression", "query-engine-windows" }) Ok(!Cleaner.CanTrim(mk(n, 900, 0), 1, 2, 0), n + " 은 안 건드림");
            var sv = mk("node", 900, 0); sv.Session = 0; Ok(!Cleaner.CanTrim(sv, 1, 2, 0), "서비스로 도는 서버");
            var g = mk("League of Legends", 900, 0); g.IsGame = true; Ok(!Cleaner.CanTrim(g, 1, 2, 0), "게임"); Ok(!Cleaner.CanTrim(mk("LeagueClient", 900, 0), 1, 2, 1) && Cleaner.CanTrim(mk("LeagueClient", 900, 0), 1, 2, 0), "게임 중엔 롤 클라이언트도");
            var m = Cleaner.Map(32L << 30, 8L << 30); if (Cleaner.Elevated) Ok(m.Known && m.Cache >= 0 && m.Free >= 0 && m.Free + m.Cache + m.Modified < 64L << 30, "램 구성");
        }

        // ---- 실제 프로세스에 걸고 되돌리기(시험이 직접 켠 것에만) ----

        [DllImport("kernel32.dll")] static extern uint GetPriorityClass(IntPtr h);
        [DllImport("kernel32.dll")] static extern bool GetProcessAffinityMask(IntPtr h, out ulong p, out ulong s);
        static string Child(string name) { string f = Path.Combine(Tmp, name + ".exe"); if (!File.Exists(f)) File.Copy(Process.GetCurrentProcess().MainModule.FileName, f); return f; }
        static Process Go(string name, int sec) { return Process.Start(new ProcessStartInfo(Child(name), "--sleep " + sec) { UseShellExecute = false, CreateNoWindow = true }); }
        static uint Pri(Process p) { return GetPriorityClass(p.Handle); }
        static ulong Aff(Process p) { ulong a, s; GetProcessAffinityMask(p.Handle, out a, out s); return a; }

        /// <summary>게임 중이거나, 켜져 있는 렉 감시가 이 시험을 제한했으면 우선순위·코어 시험은 뜻이 없다.</summary>
        static bool Alone(string what)
        {
            var me = Process.GetCurrentProcess(); ulong a, s; GetProcessAffinityMask(me.Handle, out a, out s); bool ok = !Sys.Running("EternalReturn.exe") && !Sys.Running("League of Legends.exe") && GetPriorityClass(me.Handle) == 0x20 && a == s;
            if (!ok) Console.WriteLine("  건너뜀(게임 중이거나 이 시험이 제한됨): " + what); return ok;
        }
        static void FixReal()
        {
            if (!Alone("코어·우선순위")) return;
            string state = Path.Combine(Tmp, "applied.tsv"); ulong full;
            using (var p = Go("lwchild", 60))
            {
                var set = new Settings(null); set.Games.Add("lwgame"); var m = new Monitor(set, new EventLog(null)); Action step = () => { m.Step(); Thread.Sleep(150); m.Step(); };
                Thread.Sleep(300); step(); full = Aff(p); uint pr0 = Pri(p);
                var f = new Fixer(m, state); var g = f.GroupOf("lwchild"); Ok(g != null && Fixer.CanTune(g) && Fixer.CanEnd(g), "시험 프로그램 찾기");
                f.Set(new Rule { Key = "lwchild", Label = "시험", Cores = 2, Prio = 2, Always = true });
                ulong want = Fixer.Mask(m.CoreOf, m.Phys, 2); Ok(Pri(p) == 0x40 && Aff(p) == want && f.AppliedCount("lwchild") == 1, "걸림 " + Pri(p).ToString("x") + " " + Aff(p).ToString("x")); Ok(File.Exists(state), "갑자기 꺼질 때를 대비해 적어 둠");
                f.Remove("lwchild"); Ok(Pri(p) == pr0 && Aff(p) == full, "되돌림 " + Pri(p).ToString("x")); f.Tick();
                f.Set(new Rule { Key = "lwchild", Label = "시험", Cores = 1, Prio = 1, Always = true }); Ok(Pri(p) == 0x4000, "조금 낮게");
                f.Set(new Rule { Key = "lwchild", Label = "시험", Cores = 4, Prio = 0, Always = true }); Ok(Pri(p) == pr0 && Aff(p) == Fixer.Mask(m.CoreOf, m.Phys, 4), "바꿔 걸기(우선순위는 원래대로)");
                // 갑자기 꺼짐 → 다음에 켤 때 되돌림
                f.Set(new Rule { Key = "lwchild", Label = "시험", Cores = 2, Prio = 2, Always = true }); Ok(Pri(p) == 0x40, "다시 걸림");
                var m2 = new Monitor(new Settings(null), new EventLog(null)); m2.Step(); var f2 = new Fixer(m2, state); Ok(Pri(p) == pr0 && Aff(p) == full && !File.Exists(state), "다음에 켤 때 되돌림");
                // 게임할 때만
                f.Set(new Rule { Key = "lwchild", Label = "시험", Cores = 2, Prio = 1 }); Ok(Pri(p) == pr0, "게임이 없으면 안 걸림");
                using (var gm = Go("lwgame", 60))
                {
                    Thread.Sleep(300); step(); Ok(m.Game != null, "게임 켜짐"); f.Tick(); Ok(Pri(p) == 0x4000 && Aff(p) == want, "게임 중에 걸림"); Ok(Pri(gm) == 0x20, "게임은 안 건드림");
                    f.Set(new Rule { Key = "lwgame", Label = "게임", Cores = 1, Prio = 2, Always = true }); f.Tick(); Ok(Pri(gm) == 0x20 && Aff(gm) == full, "게임에 규칙을 걸어도 안 먹음"); f.Remove("lwgame");
                    var q2 = Go("lwchild", 60); Thread.Sleep(300); step(); f.Tick(); Ok(Pri(q2) == 0x4000, "나중에 켜진 것에도 걸림"); q2.Kill(); q2.Dispose();
                    gm.Kill(); gm.WaitForExit();
                }
                Thread.Sleep(200); step(); Ok(m.Game == null, "게임 꺼짐"); f.Tick(); Ok(Pri(p) == pr0 && Aff(p) == full, "게임이 끝나면 되돌림");
                f.Set(new Rule { Key = "lwchild", Label = "시험", Cores = 2, Prio = 1, Once = true }); using (var gm = Go("lwgame", 60)) { Thread.Sleep(300); step(); f.Tick(); Ok(Pri(p) == 0x4000, "이번 게임만: 걸림"); gm.Kill(); gm.WaitForExit(); }
                Thread.Sleep(200); step(); f.Tick(); Ok(Pri(p) == pr0 && f.Find("lwchild") == null && set.Rules.Count == 0, "이번 게임만: 끝나면 사라짐");
                // 시작 프로그램에 등록된 명령으로 껐다 켜기(시험용 등록을 만들고 지운다)
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    try
                    {
                        k.SetValue("LagWatchTest", "\"" + Child("lwchild") + "\" --sleep 41"); Fixer.ForgetRuns(); step(); lock (m.Sync) foreach (var ap in m.Apps.Values) if (ap.Exe == "lwchild") m.EnsureInfo(ap);
                        var gr = f.GroupOf("lwchild"); Ok(Fixer.AutoRun(gr) != null && Fixer.CanRestart(gr), "시작 프로그램 찾기"); string err; int pid0 = p.Id; Ok(f.Restart(gr, out err), "껐다 켜기 " + err); Thread.Sleep(900);
                        var now = Process.GetProcessesByName("lwchild"); Ok(now.Length == 1 && now[0].Id != pid0 && Names.CommandLine(now[0].Id).Contains("--sleep 41"), "등록된 명령으로 다시 켜짐 " + now.Length); foreach (var z in now) { z.Kill(); z.WaitForExit(2000); }
                    }
                    finally { k.DeleteValue("LagWatchTest", false); Fixer.ForgetRuns(); }
                }
                {
                    // 실제 프로그램은 찾기만(건드리지 않음)
                    var real = new Monitor(new Settings(null), new EventLog(null)); real.Step(); var rf = new Fixer(real, null);
                    foreach (var key in new[] { "lghub", "discord", "steam", "kakaotalk" }) { Grp rg; lock (real.Sync) { rg = rf.GroupOf(key); if (rg != null) foreach (var ap in rg.Apps) real.EnsureInfo(ap); } if (rg != null) Console.WriteLine("  {0}: 껐다 켜기 {1}, 시작 명령 {2}", rg.Label, Fixer.CanRestart(rg), Fixer.AutoRun(rg)); }
                }
            }
            using (var p = Go("lwchild", 60))
            {
                var m = new Monitor(new Settings(null), new EventLog(null)); Thread.Sleep(300); m.Step(); Thread.Sleep(150); m.Step(); var f = new Fixer(m, null); Grp g;
                // 묶음으로 끄기
                g = f.GroupOf("lwchild"); Ok(f.End(g) == 1 && p.WaitForExit(3000), "끄기");
                foreach (var bad in new[] { "mysqld", "vgc", "LeagueClient", "dwm" }) { var gg = new Grp { Key = bad, Label = bad }; gg.Add(new App { Exe = bad, Key = bad, Protected = Names.IsGuarded(bad), Path = "x" }); Ok(!Fixer.CanEnd(gg) && !Fixer.CanRestart(gg), bad + " 은 못 끔"); Ok(bad == "dwm" ? true : !Fixer.CanTune(gg), bad + " 은 못 바꿈"); }
                f.RestoreAll();
            }
            foreach (var p in Process.GetProcessesByName("lwchild")) { try { p.Kill(); } catch (Exception) { } }
        }

        // ---- 화면 자료 ----

        static Monitor Live()
        {
            var m = new Monitor(new Settings(null), new EventLog(null)); m.Step(); Thread.Sleep(400); m.Step(); Thread.Sleep(400); m.Step(); return m;
        }
        static void PartsTest()
        {
            var m = Live(); var s = Snap.Take(m);
            for (int k = 0; k < 3; k++)
            {
                foreach (int n in new[] { 1, 3, 4, 9 }) foreach (string c in new[] { null, "node", "chrome", "없는것" })
                    {
                        var p = s.Parts(k, n, c); double sum = 0; foreach (var x in p) { sum += x.V; Ok(x.V >= 0 && !double.IsNaN(x.V), "음수 없음"); }
                        Ok(Math.Abs(sum - s.Used(k)) < 0.02 * s.Cap(k) + .01, "합 = 쓴 양 " + k + " " + sum + " " + s.Used(k)); Ok(p.Count >= 1 && p.Count <= n + 4, "칸 수 " + p.Count);
                    }
                Ok(s.Left(k) >= 0 && s.Left(k) <= s.Cap(k), "남은 양");
            }
            Ok(s.Programs > 30 && s.Core.Length == m.Phys, "프로그램 수 " + s.Programs);
            var l = new List<string>(); foreach (var g in s.Groups) { Ok(!l.Contains(g.Key), "묶음 겹침 없음"); l.Add(g.Key); }
        }

        static void UiTest()
        {
            var m = Live(); var d = new Doctor(m, null); for (int i = 0; i < 3; i++) { m.Step(); d.Tick(m.Now); Thread.Sleep(120); }
            foreach (var th in new[] { Theme.Night, Theme.Day })
            {
                Theme.Cur = th;
                using (var v = new MainView(d) { Still = true })
                {
                    v.Refresh();
                    foreach (var sz in new[] { new[] { 1080, 720 }, new[] { 960, 640 }, new[] { 1920, 1040 }, new[] { 300, 200 } })
                        for (int p = 0; p < 5; p++)
                        {
                            v.Go(p); v.Draw(sz[0], sz[1], 1); v.Draw(sz[0], sz[1], 1.25f); Ok(v.Page == p, "화면 " + p + " " + sz[0]);
                        }
                    // 자리마다 눌러 보기(되돌릴 수 없는 것은 확인 창에서 멈춘다)
                    for (int p = 0; p < 5; p++)
                    {
                        for (int y = 170; y < 720; y += 37) for (int x = 10; x < 1080; x += 53)
                            {
                                v.Go(p); v.Draw(1080, 720, 1); v.MouseMove(x, y);
                                if (p == 4) continue; // 청소 화면의 단추는 실제로 동작하므로 올려만 본다
                                if (p == 1) continue; // 요약의 단추도
                                v.Click(x, y); v.Draw(1080, 720, 1); if (v.Asking) v.AskNo(); v.Open = null;
                            }
                        Ok(true, "눌러 보기 " + p);
                    }
                    foreach (var g in v.S.Groups) { v.Page = 0; v.Open = g.Key; v.Draw(1080, 720, 1); }
                    v.Open = null; v.Page = 3; v.Find = "chrome"; v.Draw(1080, 720, 1); v.Find = "zzzz"; v.Draw(1080, 720, 1); v.Wheel(-2000); v.Draw(1080, 720, 1);
                    v.Page = 0; v.Cores = true; v.Draw(1080, 720, 1);
                    bool asked = false; v.AskBox("시험", "글", () => asked = true); v.Draw(1080, 720, 1); Ok(v.Asking, "확인 창"); v.AskNo(); Ok(!v.Asking && !asked, "그만두기");
                }
                using (var p = new PanelView(d) { Still = true }) { p.Refresh(); for (int t = 0; t < 4; t++) { p.Tab = t; p.Draw(500, 808, 1); Ok(p.Height > 200 && p.Height < 760, "작은 창 높이 " + p.Height); } foreach (var g in p.S.Groups) { p.Open = g.Key; p.Draw(500, 808, 1); } }
                using (var q = new MiniView(d) { Still = true }) { q.Refresh(); q.Draw(360, 250, 1); Ok(true, "올렸을 때"); }
            }
            foreach (var what in new[] { "cpu", "leak", "ok", "todo" })
            {
                d.Fix.Rules.Clear(); d.Fix.Queue.Clear(); Demo.Causes(d, what);
                using (var v = new MainView(d) { Still = true }) { v.Refresh(); for (int p = 0; p < 5; p++) { v.Go(p); v.Draw(1080, 720, 1); } }
                using (var p = new PanelView(d) { Still = true }) { p.Refresh(); p.Draw(500, 808, 1); } using (var q = new MiniView(d) { Still = true }) { q.Refresh(); q.Draw(360, 250, 1); }
            }
            D2.Shutdown(); Ok(D2.Live == 0 && Canvas.Count == 0, "화면을 닫으면 남는 것 없음 " + D2.Live + " " + Canvas.Count);
            using (var v = new MiniView(d) { Still = true }) { v.Refresh(); v.Draw(360, 250, 1); Ok(D2.Live > 0, "다시 열기"); } D2.Shutdown(); Ok(D2.Live == 0, "다시 닫기");
            d.Fix.Rules.Clear(); d.Dispose(); Theme.Cur = Theme.Night;
        }

        // ---- 사용률·남은 양 ----

        static void UsageTest()
        {
            var m = Live(); var s = Snap.Take(m);
            for (int k = 0; k < 3; k++)
            {
                double pct = double.Parse(Txt.Pct(s, k).TrimEnd('%')); Ok(pct >= 0 && pct <= 100, "사용률 " + pct);
                Ok(Math.Abs(pct - s.Used(k) / s.Cap(k) * 100) <= .5001, "사용률 = 쓴 양 / 전체 " + k);
                string left = Txt.Left(s, k); if (k == Snap.Cpu) Ok(double.Parse(left.TrimEnd('%')) + pct == 100, "CPU 사용 + 남음 = 100 " + left); else Ok(Math.Abs(double.Parse(left.Replace("GB", "")) - (s.Cap(k) - s.Used(k))) < .06, "남은 양 " + left);
                if (k != Snap.Cpu) { var of = Txt.Of(s, k).Replace("GB", "").Split('/'); Ok(Math.Abs(double.Parse(of[0]) - s.Used(k)) < .06 && Math.Abs(double.Parse(of[1]) - s.Cap(k)) < .51, "쓴 양 / 전체 " + Txt.Of(s, k)); }
            }
            foreach (double u in new[] { 0, .4, 12.5, 49.5, 50.5, 99.5, 99.6, 100 }) { s.CpuPct = u; Ok(double.Parse(Txt.Pct(s, 0).TrimEnd('%')) + double.Parse(Txt.Left(s, 0).TrimEnd('%')) == 100, "합 100: " + u + " " + Txt.Pct(s, 0) + " " + Txt.Left(s, 0)); }
            var r = new Doctor(m, null); long t = m.RamTotal; Ok(r.TrimText(new CleanResult { Before = t * 4 / 10, After = t / 2 }).StartsWith("램 60% → 50%, "), "청소 글 " + r.TrimText(new CleanResult { Before = t * 4 / 10, After = t / 2 })); Ok(!r.TrimText(new CleanResult { Before = t / 2, After = t / 2 - 1000 }).Contains("확보"), "안 늘었으면 확보라고 안 함"); r.Dispose();
            var v = new float[] { -1, -1, 5, 6, -1, 7, -1, -1, -1, 8, -1, -1, -1, -1, 9, -1 }; Series.Bridge(v, v.Length, 3);
            Ok(v[0] == -1 && v[1] == -1 && v[4] == 6 && v[6] == 7 && v[8] == 7 && v[10] == -1 && v[13] == -1 && v[15] == 9, "짧게 빈 곳만 잇기");
        }

        /// <summary>실패한 것은 원인을 가리지 않는다.</summary>
        static void FailTest()
        {
            var m = Live(); var d = new Doctor(m, null); string key = null; Grp g0 = null;
            foreach (var g in Snap.Take(m).Groups) if (!g.IsGame && g.Key != "win" && g.Key != "lagwatch" && g.Commit > 200L << 20) { key = g.Key; g0 = g; break; }
            if (key == null) { d.Dispose(); return; }
            d.Growing = new List<Growth> { new Growth { Key = key, Label = g0.Label, First = 100, Last = 9000, Hours = 3 } };
            Ok(d.Find(m.Now).Exists(c => c.Key == key && c.Level >= 1), "누수 원인");
            d.Fix.Note(key, g0.Label, g0.Label + " 재시작 실패", "종료 실패", false).Fail = true;
            var l = d.Find(m.Now); Ok(l.Exists(c => c.Key == key && c.Level >= 1) && l.Exists(c => c.Key == key && c.Level == -1 && c.Fail), "실패하면 원인이 남는다");
            using (var v = new MainView(d) { Still = true }) { d.Causes = l; v.Refresh(); v.Go(0); v.Draw(1080, 720, 1); v.Go(1); v.Draw(1080, 720, 1); }
            d.Fix.Note(key, g0.Label, g0.Label + " 재시작함", "", false);
            l = d.Find(m.Now); Ok(!l.Exists(c => c.Key == key && c.Level >= 1) && l.Exists(c => c.Key == key && c.Level == -1 && !c.Fail), "성공하면 사라진다");
            d.Dispose(); D2.Shutdown();
        }

        static void TimeTest()
        {
            var m = Live(); var s = Snap.Take(m);
            foreach (int h in new[] { 3, 12, 24 }) for (int k = 0; k < 3; k++)
                {
                    var t = TimeSet.Build(m, s, k, h, null); Ok(t.N > 100 && t.N < 320, "칸 수 " + t.N); double sum = 0; foreach (var l in t.L) { sum += l.V[t.N - 1]; Ok(l.V.Length == t.N, "길이"); }
                    Ok(Math.Abs(sum - t.Total[t.N - 1]) < s.Cap(k) * .02, "지금 합 " + sum + " " + t.Total[t.N - 1]); foreach (float v in t.Total) Ok(v >= -1 && v <= s.Cap(k) * 1.05, "범위");
                }
        }
    }
}
