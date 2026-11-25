using System;
using System.Collections.Generic;
using System.IO;

namespace LagWatch
{
    internal static partial class Tests
    {
        static int pass, fail;
        static void Ok(bool c, string name) { if (c) pass++; else { fail++; Console.WriteLine("실패: " + name); } }
        static void Eq(string a, string b, string name) { Ok(a == b, name + "  받은 값 [" + a + "] 기대 [" + b + "]"); }

        static FloatRing Ring(int n, Func<int, double> f) { var r = new FloatRing(288); for (int i = 0; i < n; i++) r.Add((float)f(i)); return r; }

        static int Main(string[] args)
        {
            if (args.Length > 1 && args[0] == "--sleep") { System.Threading.Thread.Sleep(int.Parse(args[1]) * 1000); return 0; }
            if (args.Length > 1 && args[0] == "--spin") { var w = System.Diagnostics.Stopwatch.StartNew(); for (int i = 0; i < 3; i++) new System.Threading.Thread(() => { while (w.Elapsed.TotalSeconds < int.Parse(args[1])) { } }) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal }.Start(); System.Threading.Thread.Sleep(int.Parse(args[1]) * 1000); return 0; }
            Console.OutputEncoding = new System.Text.UTF8Encoding(false); Trace.Tag = ".test";
            // 켜져 있는 게임과 렉 감시에 끼어들지 않게
            foreach (string g in new[] { "EternalReturn.exe" }) if (Sys.Running(g) && Array.IndexOf(args, "--force") < 0) { Console.WriteLine("게임 중에는 시험하지 않음: " + g); return 2; }
            RingTest(); LeakTest(); NameTest(); LogTest(); RuleTest(); HistTest(); AppHistTest(); ForecastTest(); OldBits(); MiscTest(); More();
            Console.WriteLine("통과 {0}, 실패 {1}", pass, fail);
            return fail == 0 ? 0 : 1;
        }

        static void RingTest()
        {
            var r = new FloatRing(50);
            for (int i = 0; i < 175; i++)
            {
                r.Add(i);
                Ok(r.Back(0) == i, "ring newest " + i);
                Ok(r.Count == Math.Min(50, i + 1), "ring count " + i);
                Ok(r.Front(0) == Math.Max(0, i - 49), "ring oldest " + i);
            }
        }

        static void LeakTest()
        {
            var rnd = new Random(7);
            float g; int m;
            Ok(Leak.Find(Ring(13, i => 300 + i * 40), out g, out m) && m == 60, "1시간 동안 5분마다 40MB");
            Ok(Math.Abs(g - 440) < 30, "증가량 " + g);
            Ok(Leak.Find(Ring(40, i => 500 + i * 15 + rnd.Next(-8, 8)), out g, out m) && m == 180, "3시간 느린 누수 + 잡음");
            Ok(Leak.Find(Ring(150, i => 400 + i * 5 + rnd.Next(-10, 10)), out g, out m) && m == 720, "12시간 아주 느린 누수");
            Ok(!Leak.Find(Ring(60, i => 900 + rnd.Next(-60, 60)), out g, out m), "오르내리기만 함");
            Ok(!Leak.Find(Ring(60, i => i < 30 ? 500 : 2500), out g, out m), "한 번에 튄 작업");
            Ok(!Leak.Find(Ring(60, i => i < 20 ? 300 + i * 60 : 1500), out g, out m), "올랐다가 멈춤");
            Ok(!Leak.Find(Ring(60, i => 800 + (i % 10) * 120), out g, out m), "톱니(GC)");
            Ok(Leak.Find(Ring(60, i => 800 + (i % 4) * 4 + i * 30), out g, out m), "톱니가 섞인 누수");
            Ok(!Leak.Find(Ring(60, i => 6000 - i * 20), out g, out m), "줄어듦");
            Ok(!Leak.Find(Ring(8, i => 100 + i * 200), out g, out m), "1시간이 안 됨");
            Ok(!Leak.Find(Ring(14, i => 10000 + i * 20), out g, out m), "큰 프로그램의 작은 변화(25% 미만)");
            Ok(!Leak.Find(null, out g, out m), "기록 없음");
        }

        static void NameTest()
        {
            Eq(Names.ScriptLabel("node", "\"C:\\Program Files\\nodejs\\node.exe\" C:\\a\\node_modules\\vite\\bin\\vite.js preview --config D:\\x"), "vite", "node_modules");
            Eq(Names.ScriptLabel("node", "node dist/server.js"), "dist/server.js", "흔한 이름");
            Eq(Names.ScriptLabel("node", "node --expose-gc --max-old-space-size=3072 C:\\a\\dist\\w\\workerProcess.js"), "workerProcess.js", "옵션 건너뜀");
            Eq(Names.ScriptLabel("node", "node -r dotenv/config C:/x/job.js"), "job.js", "-r 값 건너뜀");
            Eq(Names.ScriptLabel("node", "node -e \"console.log(process.env.TOKEN)\""), null, "-e 코드는 이름으로 안 씀");
            Eq(Names.ScriptLabel("node", "node C:/u/node_modules/@scope/pkg/dist/index.js --token=abc"), "@scope/pkg", "스코프 패키지");
            Eq(Names.ScriptLabel("python", "python -m http.server 80"), "http.server", "python -m");
            Eq(Names.ScriptLabel("python", "python -u C:\\t\\main.py"), "t/main.py", "python 파일");
            Eq(Names.ScriptLabel("python", "python -c \"import os\""), null, "python -c");
            Eq(Names.ScriptLabel("java", "java -Xmx4G -jar C:\\mc\\server.jar nogui"), "mc/server.jar", "java -jar");
            Eq(Names.ScriptLabel("javaw", "javaw -cp a;b net.minecraft.client.main.Main"), "Main", "java 클래스");
            Eq(Names.ScriptLabel("java", "java --add-opens java.base/java.util=ALL-UNNAMED --add-exports=a/b=ALL-UNNAMED -Xmx2g -cp x org.gradle.launcher.daemon.bootstrap.GradleDaemon 9"), "GradleDaemon", "java --add-opens 값 건너뜀");
            Eq(Names.ScriptLabel("pwsh", "pwsh -NoProfile -File C:\\a\\watch.ps1"), "watch.ps1", "pwsh -File");
            Eq(Names.ScriptLabel("pwsh", "pwsh -Command \"$env:KEY='x'\""), null, "pwsh -Command");
            Eq(Names.ScriptLabel("bun", "bun run src/index.ts"), "src/index.ts", "bun run");
            Eq(Names.ScriptLabel("node", "node D:/x/node_modules/.bin/vitest run"), "vitest", ".bin 아래 이름");
            Eq(Names.ScriptLabel("node", "node D:/x/node_modules/.pnpm/esbuild@0.21.5/bin/esbuild"), "esbuild", ".pnpm");
            Eq(Names.ScriptLabel("node", "\"node\" \"D:\\x\\node_modules\\.bin\\\\..\\vitest\\vitest.mjs\" run"), "vitest", ".bin/.. 경로");
            Eq(Names.ScriptLabel("node", "node --conditions development --conditions node D:\\x\\node_modules\\tinypool\\dist\\entry\\process.js"), "tinypool", "--conditions 값 건너뜀");
            Eq(Names.ScriptLabel("node", "node ."), null, "점 하나");
            Eq(Names.ScriptLabel("node", null), null, "명령줄 없음");
            Eq(Names.ShortPath("server.js"), "server.js", "폴더 없음");
            Eq(Names.ShortPath("C:/index.js"), "index.js", "드라이브 바로 아래");
            Ok(Names.SplitArgs("\"a b\" c  \"d\"").Count == 3, "인수 나누기");
            Eq(Actions.Rest("\"C:\\a b\\x.exe\" --bg 1"), "--bg 1", "실행 파일 빼기");
            Eq(Actions.Rest("x.exe"), "", "인수 없음");
            Ok(!Actions.CanEnd(new App { Key = "mysqld", Exe = "mysqld", Path = "x" }) && !Actions.CanRestart(new App { Key = "mysqld", Exe = "mysqld", Path = "x" }) && !Actions.CanRestart(new App { Key = "node|a", Exe = "node", Script = true, Path = "x" }) && Actions.CanRestart(new App { Key = "lghub", Exe = "lghub", Path = "x" }), "DB는 못 끄고, 스크립트는 다시 켜기 없음");
            Ok(Names.IsGuarded("EasyAntiCheat_EOS") && !Names.IsGuarded("chrome"), "안티치트");
        }

        static void LogTest()
        {
            var t = new DateTime(2026, 11, 24, 21, 0, 0);
            var log = new EventLog(null); log.Now = () => t;
            Ok(log.Report(Kind.CpuFull, "", 0.93, 0, 3, false, () => "a"), "새 기록");
            t = t.AddSeconds(5);
            Ok(!log.Report(Kind.CpuFull, "", 0.99, 5, 8, false, () => "b"), "이어짐");
            Eq(log.Items[0].Text, "b", "더 심하면 글 갱신");
            log.Report(Kind.CpuFull, "", 0.94, 5, 9, false, () => "c");
            Eq(log.Items[0].Text, "b", "덜 심하면 유지");
            log.Tick(40);
            Ok(log.Items[0].Open, "40초엔 열림");
            log.Tick(100);
            Ok(!log.Items[0].Open, "60초 넘으면 닫힘");
            Ok(log.Report(Kind.CpuFull, "", 0.95, 98, 101, false, () => "d") && log.Items.Count == 2, "다시 생기면 새 줄");
            Ok(log.Active(105) != null && log.Active(200) == null, "지금 문제");
            for (int i = 0; i < 600; i++) log.Add(Kind.Game, "", "x" + i, 0, false);
            Ok(log.Items.Count == 400, "400개까지만");

            string f = Path.Combine(Path.GetTempPath(), "lagwatch-test-" + Guid.NewGuid().ToString("N") + ".tsv");
            try
            {
                var a = new EventLog(f);
                a.Add(Kind.Leak, "lghub", "G HUB\t탭\n줄", 0, true);
                a.Save(0); a.Add(Kind.Commit, "", "둘", 0, false); a.Save(1);
                var b = new EventLog(f);
                Ok(b.Items.Count == 2 && b.Items[0].Text == "G HUB 탭 줄" && b.Items[0].InGame && b.Items[1].Kind == Kind.Commit, "저장하고 다시 읽기");
                File.WriteAllText(f, "깨진 줄\n\n" + File.ReadAllText(f));
                Ok(new EventLog(f).Items.Count == 2, "깨진 줄 건너뜀");
            }
            finally { File.Delete(f); }
        }

        static Monitor Fake(out App game, out App other, out Proc gp, out Proc op)
        {
            var m = new Monitor(new Settings(null), new EventLog(null));
            m.RamTotal = 32L << 30; m.RamAvail = 16L << 30; m.CommitLimit = 75L << 30; m.Commit = 40L << 30; m.BaseGhz = 3.8; m.Ghz = 4.6;
            game = new App { Key = "eternalreturn", Label = "이터널 리턴", IsGame = true, InfoDone = true, Count = 1 };
            other = new App { Key = "node", Label = "node · a.js", InfoDone = true, Count = 1, Cores = 1 };
            gp = new Proc { Pid = 10, Name = "EternalReturn", App = game, IsGame = true };
            op = new Proc { Pid = 11, Name = "node", App = other };
            m.Apps["eternalreturn"] = game; m.Apps["node"] = other; m.Procs[10] = gp; m.Procs[11] = op;
            return m;
        }

        static int CountKind(Monitor m, Kind k) { int n = 0; foreach (var e in m.Log.Items) if (e.Kind == k) n++; return n; }

        static void RuleTest()
        {
            App ga, oa; Proc gp, op;
            var m = Fake(out ga, out oa, out gp, out op);
            string note = null; m.Rules.Notify = (a, b) => note = a + "|" + b;
            double t = 100;
            for (int i = 0; i < 60; i++) m.Rules.Eval(t += 1, 1);
            Ok(m.Log.Items.Count == 0, "평소엔 기록 없음");

            m.TotalCpu = 0.97; m.Rules.Eval(t += 1, 1); m.Rules.Eval(t += 1, 1);
            Ok(CountKind(m, Kind.CpuFull) == 0, "CPU 2초는 무시");
            m.Rules.Eval(t += 1, 1); m.Rules.Eval(t += 1, 1);
            Ok(CountKind(m, Kind.CpuFull) == 1, "CPU 3초 넘으면 기록");
            m.TotalCpu = 0.2;

            // 게임 밖에서는 다른 프로그램 사용량을 문제로 보지 않음
            oa.Cores = 8; for (int i = 0; i < 20; i++) m.Rules.Eval(t += 1, 1);
            Ok(CountKind(m, Kind.Others) == 0, "게임 밖");
            m.Game = gp; m.Rules.GameStart(t);
            for (int i = 0; i < 30; i++) m.Rules.Eval(t += 0.5, 0.5);
            Ok(CountKind(m, Kind.Others) == 0, "CPU에 여유가 있으면 문제 아님");
            m.TotalCpu = 0.7;
            for (int i = 0; i < 8; i++) m.Rules.Eval(t += 0.5, 0.5);
            Ok(CountKind(m, Kind.Others) == 0, "게임 중 4초");
            for (int i = 0; i < 8; i++) m.Rules.Eval(t += 0.5, 0.5);
            Ok(CountKind(m, Kind.Others) == 1, "게임 중 5초 넘게 8코어");
            oa.Cores = 1; m.TotalCpu = 0.2;

            // 게임 스레드 대기
            for (int i = 0; i < 20; i++) m.G.Push(2, 0, 5, 6);
            m.Rules.Eval(t += 0.5, 0.5);
            Ok(CountKind(m, Kind.GameWait) == 0, "대기 없음");
            for (int i = 0; i < 20; i++) m.G.Push((uint)(i % 4 == 0 ? 1 : 2), 0, 5, 6);
            Ok(Math.Abs(m.G.ReadyPct - 0.25) < 0.01, "대기 비율 " + m.G.ReadyPct);
            m.Rules.Eval(t += 0.5, 0.5);
            Ok(CountKind(m, Kind.GameWait) == 1, "대기 25%");
            for (int i = 0; i < 20; i++) m.G.Push(2, 0, 5, 9);
            Ok(m.G.DiskPct > 0.9, "디스크 대기");

            // 드라이버
            m.Drv[4] = 0.3f; for (int i = 0; i < 10; i++) m.Rules.Eval(t += 0.5, 0.5);
            Ok(CountKind(m, Kind.Driver) == 1, "드라이버 30%"); m.Drv[4] = 0;

            // 속도 저하: 놀고 있을 때는 문제 아님
            m.Ghz = 2.5; m.TotalCpu = 0.1; for (int i = 0; i < 20; i++) m.Rules.Eval(t += 0.5, 0.5);
            Ok(CountKind(m, Kind.Slow) == 0, "놀 때 느린 건 정상");
            m.TotalCpu = 0.5; for (int i = 0; i < 20; i++) m.Rules.Eval(t += 0.5, 0.5);
            Ok(CountKind(m, Kind.Slow) == 1, "일하는데 느림"); m.Ghz = 4.6;

            // 코어 하나 꽉
            op.HotSince = t; for (int i = 0; i < 30; i++) m.Rules.Eval(t += 0.5, 0.5);
            Ok(CountKind(m, Kind.OneCore) == 0, "15초는 무시");
            for (int i = 0; i < 20; i++) m.Rules.Eval(t += 0.5, 0.5);
            Ok(CountKind(m, Kind.OneCore) == 1, "20초 넘으면 기록"); op.HotSince = -1;

            // 가상메모리: 게임 중 알림은 모았다가 끝난 뒤
            note = null; m.Commit = 70L << 30; for (int i = 0; i < 40; i++) m.Rules.Eval(t += 0.5, 0.5);
            Ok(CountKind(m, Kind.Commit) == 1, "가상메모리 93%");
            Ok(note == null, "게임 중엔 알림 안 띄움");
            m.Commit = 30L << 30;
            t += 200; m.Rules.GameEnd(t);
            Ok(note == null, "게임이 끝나도 알림 없음: " + note);
            Ok(CountKind(m, Kind.Game) == 1, "게임 요약 한 줄");
            Console.WriteLine("  요약 예: " + m.Log.Items[m.Log.Items.Count - 1].Text.Replace("\n", " / "));
            m.Game = null;

            // 누수 알림, 제외, 다시 알림 간격
            note = null; op.Commit = 900L << 20; op.Lows = Ring(14, i => 300 + i * 45);
            m.Rules.Leaks(t); Ok(op.Leaking && note != null && CountKind(m, Kind.Leak) == 1, "누수 알림: " + note);
            note = null; m.Rules.Leaks(t); Ok(note == null && CountKind(m, Kind.Leak) == 1, "같은 크기면 다시 안 알림");
            op.Commit = 1500L << 20; m.Rules.Leaks(t); Ok(CountKind(m, Kind.Leak) == 2, "500MB 더 늘면 다시");
            oa.Ignored = true; m.Rules.Leaks(t); Ok(!op.Leaking, "알림에서 빼기");
            gp.Lows = Ring(14, i => 300 + i * 100); m.Rules.Leaks(t); Ok(!gp.Leaking, "게임은 누수로 안 봄");
            m.Set.Notify = false; oa.Ignored = false; op.AlertedAt = 0; note = null; m.Rules.Leaks(t); Ok(note == null && op.Leaking, "알림 끔");
        }

        static string TempDir() { string d = Path.Combine(Path.GetTempPath(), "lagwatch-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }

        static void HistTest()
        {
            string dir = TempDir();
            try
            {
                int now = History.Epoch(DateTime.UtcNow), from = now / 60 * 60 - 30 * 3600; // 자정을 한 번 이상 넘김
                var h = new History(dir);
                ushort er = h.Id("eternalreturn", "이터널 리턴");
                int minutes = 0;
                for (int t = from; t <= now - 600; t += 20)
                    if (h.Add(new MPoint { T = t, Cpu = 0.5f, Core = t % 60 == 0 ? 0.9f : 0.2f, Ram = 0.6f, Commit = 0.55f, CommitGb = 41.5f, Game = (ushort)(t > now - 7200 ? er : 0), GReady = t % 60 == 20 ? 0.3f : 0, Ghz = 4.66f }, 20)) minutes++;
                for (int q = from / 60 + 4; q < (now - 600) / 60; q += 5) h.AddBlock(new Block { Minute = q, Apps = new[] { new AppAt { Id = er, Commit = 4000, Ws = 3000, Cores = 2.5f }, new AppAt { Id = h.Id("lghub", "Logitech G HUB"), Commit = 700, Ws = 90, Cores = 0.02f } } });
                Ok(minutes > 1780, "1분 칸 " + minutes);
                var l = new List<MPoint>();
                h.Range(now - 86400, now, l);
                Ok(l.Count > 1400, "메모리에서 하루 " + l.Count);
                h.Save();
                Ok(Directory.GetFiles(dir, "m-*.bin").Length >= 2, "날짜별 파일");
                long size = 0; foreach (var f in Directory.GetFiles(dir)) size += new FileInfo(f).Length;

                var r = new History(dir);
                r.Range(now - 30 * 3600, now, l);
                Ok(l.Count > 1770, "다시 켠 뒤 파일에서 " + l.Count);
                bool sorted = true; for (int i = 1; i < l.Count; i++) if (l[i].T <= l[i - 1].T) sorted = false;
                Ok(sorted, "시각 순서, 겹침 없음");
                var p = l[l.Count / 2];
                Ok(Math.Abs(p.Cpu - 0.5) < 0.001 && Math.Abs(p.Core - 0.9) < 0.001 && Math.Abs(p.GReady - 0.3) < 0.001 && Math.Abs(p.CommitGb - 41.5) < 0.02 && Math.Abs(p.Ghz - 4.66) < 0.002, "값 보존(평균과 최고)");
                Ok(l[l.Count - 1].Game == r.FindId("eternalreturn") && r.Label(l[l.Count - 1].Game) == "이터널 리턴", "이름 번호 보존");
                var b = r.BlockAt((now - 3600) / 60);
                Ok(b != null && b.Length == 2 && b[0].Commit == 4000 && Math.Abs(b[0].Cores - 2.5) < 0.001 && r.Label(b[1].Id) == "Logitech G HUB", "프로그램 기록");
                Ok(r.BlockAt((now + 86400) / 60) == null, "없는 시각은 없음");
                Ok(Math.Abs(r.Oldest() - from) < 120, "가장 오래된 시각");
                Ok(r.Id("new", "새 것") == 3 && r.Id("lghub", "Logitech G HUB") == 2, "새 이름 번호 이어짐");

                // 이어서 쌓으면 파일과 메모리가 겹치지 않음
                for (int t = now - 590; t <= now; t += 5) r.Add(new MPoint { T = t, Cpu = 0.1f }, 5);
                r.Range(now - 3600, now, l);
                sorted = true; for (int i = 1; i < l.Count; i++) if (l[i].T <= l[i - 1].T) sorted = false;
                Ok(sorted && l[l.Count - 1].Cpu < 0.2 && l[0].Cpu > 0.4, "파일 + 메모리 이어 붙이기 " + l.Count);
                r.Save();

                // 끝이 잘린 파일
                foreach (var f in Directory.GetFiles(dir, "*.bin")) { var fs = new FileStream(f, FileMode.Open); fs.SetLength(fs.Length - 7); fs.Dispose(); }
                var c = new History(dir); c.Range(now - 30 * 3600, now, l);
                Ok(l.Count > 1700 && c.BlockAt((now - 3600) / 60) != null, "끝이 잘려도 읽음");

                File.WriteAllBytes(Path.Combine(dir, "m-20200101.bin"), new byte[26]); File.WriteAllBytes(Path.Combine(dir, "other-20200101.bin"), new byte[1]);
                new History(dir);
                Ok(!File.Exists(Path.Combine(dir, "m-20200101.bin")) && File.Exists(Path.Combine(dir, "other-20200101.bin")), "35일 지난 것만 지움");
                Console.WriteLine("  30시간 기록 파일 {0}KB", size / 1024);
                var last = new History(dir).LastDay(now);
                Ok(last.ContainsKey("lghub") && last["lghub"].Count > 200 && last["lghub"][5].Value.Commit == 700, "다시 켰을 때 이어 보이기 " + (last.ContainsKey("lghub") ? last["lghub"].Count : 0));
            }
            finally { Directory.Delete(dir, true); }
        }

        static void AppHistTest()
        {
            float c, w, k;
            var h = new AppHist();
            for (int i = 0; i < 4000; i++) h.Set(1000 + i, i, i / 2.0, 0.5);
            Ok(h.Get(4999, out c, out w, out k) && c == 3999 && Math.Abs(k - 0.5) < 0.001, "가장 최근");
            Ok(!h.Get(1000, out c, out w, out k) && h.Last == 4999 && h.Last - h.First < 1440, "24시간만");
            Ok(h.Get(h.First, out c, out w, out k) && c == h.First - 1000, "가장 오래된 값");
            var g = new AppHist();
            g.Set(100, 50, 50, 1); g.Set(110, 80, 50, 1);
            Ok(!g.Get(105, out c, out w, out k) && g.Baseline(101) == 80 && g.Baseline(0) == 50, "꺼져 있던 구간");
            g.Set(90, 1, 1, 1); Ok(g.First == 100, "과거로는 안 씀");
            g.Set(90000, 7, 7, 7); Ok(g.Get(90000, out c, out w, out k) && c == 7 && g.Baseline(0) == 7, "오래 꺼졌다 켜짐");
            var z = new AppHist(); z.Set(5, 200000, 1, 200); Ok(z.Get(5, out c, out w, out k) && c == 65535, "아주 큰 값은 한도에서 멈춤");
            for (int i = 0; i < 3; i++) { var q = new AppHist(); q.Set(0, 1, 1, 1); q.Set(1439 + i * 700, 2, 2, 2); Ok(q.Get(1439 + i * 700, out c, out w, out k) && c == 2, "빈 구간 " + i); }
        }

        static void ForecastTest()
        {
            var rnd = new Random(5); double s, lv;
            Func<Func<int, double>, List<MPoint>> mk = f => { var l = new List<MPoint>(); for (int i = 0; i < 120; i++) l.Add(new MPoint { T = 1000000 + i * 60, Commit = (float)f(i) }); return l; };
            double now = 1000000 + 119 * 60;
            Ok(Forecast.Commit(mk(i => 0.5 + i * 0.001 + rnd.NextDouble() * 0.01), now, 7200, out s, out lv) && Math.Abs(s * 60 - 0.001) < 0.0002, "꾸준히 오름 " + (s * 60));
            Ok(!Forecast.Commit(mk(i => 0.6 + rnd.NextDouble() * 0.02), now, 7200, out s, out lv), "그대로면 예상 안 함");
            Ok(!Forecast.Commit(mk(i => 0.8 - i * 0.001), now, 7200, out s, out lv), "줄면 예상 안 함");
            Ok(!Forecast.Commit(mk(i => i < 60 ? 0.4 : 0.6), now, 7200, out s, out lv) || s * 3600 < 0.1, "한 번 튄 것에 안 휘둘림 " + s * 3600);
            Ok(!Forecast.Commit(mk(i => 0.5 + i * 0.001).GetRange(90, 30), now, 7200, out s, out lv), "30분치로는 예상 안 함");
            Ok(!Forecast.Commit(new List<MPoint>(), now, 7200, out s, out lv), "기록 없음");
        }

        static void OldBits()
        {
            var hh = new AppHist(); hh.Set(10, 5, 9, 2); hh.Set(12, 6, 8, 1);
            Ok(Math.Abs(hh.SumCores(0, 100) - 3) < 0.01 && Math.Abs(hh.SumCores(11, 100) - 1) < 0.01 && hh.BaselineWs(0) == 9, "코어 합, 램 기준");
        }

        static void MiscTest()
        {
            Eq(Fmt.Mem(640), "640MB", "MB"); Eq(Fmt.Mem(2150), "2.1GB", "GB"); Eq(Fmt.Delta(2150), "+2.1GB", "+GB"); Eq(Fmt.Delta(-300), "-300MB", "-MB"); Eq(Fmt.Delta(0.2), "0", "0");
            Eq(Fmt.Dur(45), "45초", "초"); Eq(Fmt.Dur(3600), "1시간", "시간"); Eq(Fmt.Dur(3900), "1시간 5분", "시간 분"); Eq(Fmt.Dur(130), "2분", "분");
            Eq(Fmt.Cores(2.34), "2.3코어", "코어"); Eq(Fmt.Cores(12.4), "12코어", "코어 큰 값");

            // 실제 이 PC에서 한 번 읽어 값이 말이 되는지
            var m = new Monitor(new Settings(null), new EventLog(null));
            m.Step(); System.Threading.Thread.Sleep(1200); m.Step(); System.Threading.Thread.Sleep(1000); m.Step();
            double cores = 0; long commit = 0; int procs = 0;
            foreach (var q in m.Procs.Values) { if (q.Pseudo) continue; cores += q.Cores; commit += q.Commit; procs++; }
            Ok(procs > 50, "프로세스 수 " + procs);
            Ok(m.RamTotal > 1L << 30 && m.RamAvail > 0 && m.RamAvail < m.RamTotal, "램");
            Ok(m.Commit > 0 && m.CommitLimit > m.Commit / 2 && commit <= m.Commit * 1.05, "가상메모리 합계 " + (commit >> 20) + " <= " + (m.Commit >> 20));
            Ok(m.Phys > 0 && m.Lps >= m.Phys, "코어 " + m.Phys + "/" + m.Lps);
            Ok(Math.Abs(cores - m.TotalCpu * m.Lps) < Math.Max(1.5, m.Lps * 0.15), "프로그램 합 " + cores.ToString("0.0") + " ≈ 전체 " + (m.TotalCpu * m.Lps).ToString("0.0"));
            Console.WriteLine("  프로세스 {0}, 프로그램 합 {1:F2}코어, 전체 {2:F2}코어, 가상메모리 프로그램 합 {3:F1}GB / 전체 {4:F1}GB", procs, cores, m.TotalCpu * m.Lps, commit / 1073741824.0, m.Commit / 1073741824.0);
        }
    }
}
