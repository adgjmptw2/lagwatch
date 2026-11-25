using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace LagWatch
{
    internal static partial class Tests
    {
        static void GameTests() { BlameTest(); BlameWakeTest(); LangTest(); BlameReal(); BlameWakeReal(); JudgeTest(); HitsTest(); AvgTest(); RecoverTest(); ReadOnlyTest(); PrioTest(); ClientTest(); BootTest(); RoundTest(); TailTest(); BgTest(); LiteTests(); GameUi(); }

        // ---- 끊긴 까닭 ----

        static void BlameTest()
        {
            long q = Stopwatch.Frequency; Func<double, long> ms = x => (long)(x * q / 1000); var b = new Blame(); b.Watch(10, 20); Blame.Part m, s;
            b.Switch(10, 99, ms(1000), 5, 6);               // 주 스레드 들어옴
            b.Switch(77, 10, ms(1010), 1, 0);               // 77번에게 밀려남
            b.Switch(10, 77, ms(1030), 5, 6);               // 20ms 뒤 다시
            b.Switch(88, 10, ms(1035), 5, 2);               // 메모리를 디스크에서
            b.Switch(10, 88, ms(1075), 5, 6);               // 40ms
            b.Switch(88, 10, ms(1080), 5, 6);               // 그냥 기다림
            b.Switch(10, 88, ms(1090), 5, 6);
            b.Explain(ms(1000), ms(1100), out m, out s);
            Ok(Math.Abs(m.Run - (10 + 5 + 5 + 10)) < .1 && Math.Abs(m.Ready - 20) < .1 && Math.Abs(m.Page - 40) < .1 && Math.Abs(m.Wait - 10) < .1, "나눠 보기 " + m.Run + " " + m.Ready + " " + m.Page + " " + m.Wait); Ok(m.By.Count == 1 && Math.Abs(m.By[77] - 20) < .1, "누가 밀어냈는지"); Ok(s.Known == 0, "둘째 스레드는 기록 없음");
            b.Explain(ms(1020), ms(1040), out m, out s); Ok(Math.Abs(m.Ready - 10) < .1 && Math.Abs(m.Run - 5) < .1 && Math.Abs(m.Page - 5) < .1, "구간을 잘라 보기"); b.Explain(ms(1200), ms(1210), out m, out s); Ok(Math.Abs(m.Run - 10) < .1, "아직 도는 중");
            b.Switch(55, 66, ms(1300), 1, 0); b.Explain(ms(1290), ms(1310), out m, out s); Ok(Math.Abs(m.Run - 20) < .1, "다른 스레드는 안 봄"); b.Watch(20, 10); b.Explain(ms(1000), ms(1010), out m, out s); Ok(s.Run > 9 && m.Known == 0, "순위가 바뀌면 자리를 맞추고 기록은 그대로"); b.Watch(30, 10); b.Explain(ms(1000), ms(1010), out m, out s); Ok(m.Known == 0, "새 스레드는 새로");
            for (int i = 0; i < 40000; i++) { b.Switch(30, 1, ms(2000 + i * 2), 5, 6); b.Switch(1, 30, ms(2001 + i * 2), 5, 6); } b.Explain(ms(2000 + 79000), ms(2000 + 79100), out m, out s); Ok(Math.Abs(m.Run - 50) < 2 && Math.Abs(m.Wait - 50) < 2, "오래 쌓여도 " + m.Run + " " + m.Wait);
        }

        /// <summary>1·2위가 엇갈려 바뀌는 때, 깨어난 뒤 기다린 시간, 메모리 기다림</summary>
        static void BlameWakeTest()
        {
            long q = Stopwatch.Frequency; Func<double, long> ms = x => (long)(x * q / 1000); Blame.Part m, s;
            var b = new Blame(); b.Watch(10, 20); b.Switch(20, 99, ms(1000), 5, 6); b.Switch(99, 20, ms(1040), 5, 6);
            b.Watch(20, 30); b.Explain(ms(1000), ms(1040), out m, out s); Ok(Math.Abs(m.Run - 40) < .1 && s.Known == 0, "2위가 1위로 오고 새 스레드가 들어와도 1위 기록이 남음 " + m.Run);
            b.Watch(30, 20); b.Explain(ms(1000), ms(1040), out m, out s); Ok(Math.Abs(s.Run - 40) < .1 && m.Known == 0, "다시 2위로 가도 남음");
            b.Watch(20, 0); b.Explain(ms(1000), ms(1040), out m, out s); Ok(Math.Abs(m.Run - 40) < .1, "둘째를 모를 때");

            b = new Blame(); b.Watch(10, 20); b.Switch(10, 99, ms(1000), 5, 6); b.Switch(5, 10, ms(1010), 5, 6); b.Wake(10, ms(1020)); b.Switch(10, 77, ms(1060), 1, 0); b.Switch(5, 10, ms(1070), 5, 6);
            b.Explain(ms(1000), ms(1070), out m, out s); double v; m.By.TryGetValue(77, out v);
            Ok(Math.Abs(m.Run - 20) < .1 && Math.Abs(m.Wait - 10) < .1 && Math.Abs(m.Ready - 40) < .1 && Math.Abs(v - 40) < .1, "깨어난 뒤 기다린 40ms 는 CPU 기다림, 그 코어를 쓰던 77번 몫 " + m.Run + " " + m.Wait + " " + m.Ready + " " + v);
            b = new Blame(); b.Watch(10, 20); b.Switch(10, 99, ms(1000), 5, 6); b.Switch(88, 10, ms(1010), 1, 0); b.Wake(10, ms(1015)); b.Switch(10, 88, ms(1030), 5, 6); b.Explain(ms(1000), ms(1030), out m, out s); m.By.TryGetValue(88, out v);
            Ok(Math.Abs(m.Ready - 20) < .1 && Math.Abs(v - 20) < .1, "밀려난 동안의 깨움은 바꾸지 않음");
            b = new Blame(); b.Watch(10, 20); b.Switch(10, 99, ms(1000), 5, 6); b.Switch(5, 10, ms(1010), 5, 1); b.Wake(10, ms(1050)); b.Switch(10, 5, ms(1051), 5, 6); b.Explain(ms(1000), ms(1051), out m, out s);
            Ok(Math.Abs(m.Page - 40) < .1 && m.Ready < 1.1, "빈 메모리를 기다린 것은 메모리 " + m.Page);
            Ok(Blame.MemWait(1) && Blame.MemWait(2) && Blame.MemWait(8) && Blame.MemWait(9) && Blame.MemWait(19) && !Blame.MemWait(17) && !Blame.MemWait(6) && !Blame.MemWait(15), "메모리를 기다리는 까닭 번호");
        }

        static void LangTest()
        {
            Ok(Hit.Say("계산") == "주 스레드" && Hit.Say("그리기 준비") == "보조 스레드" && Hit.Say("main") == "주 스레드" && Hit.Say("자원 정리") == "자원 정리" && Hit.Say("") == "" && Hit.Say("x") == "x", "예전 기록의 까닭");
            Lang.En = true;
            try
            {
                Ok(Lang.Tx("가", "a") == "a" && Lang.Tx("{0}가 {1}", "{1} by {0}", 1, 2) == "2 by 1" && Lang.Pl(1, "match", "matches") == "match" && Lang.Pl(0, "match", "matches") == "matches", "영어");
                Ok(Fmt.Dur(45) == "45s" && Fmt.Dur(22 * 60) == "22m" && Fmt.Dur(64 * 60) == "1h 4m" && Fmt.Dur(7200) == "2h", "영어 시간 " + Fmt.Dur(64 * 60));
                foreach (double sec in new double[] { 60, 1320, 3600, 3840, 7200, 36000 }) Ok(MainView.GameMinutes("LoL " + Fmt.Dur(sec) + " · no issues") == (int)(sec / 60), "영어 게임 시간을 다시 읽음 " + Fmt.Dur(sec));
                Ok(Doctor.Ga("G HUB") == "G HUB" && Txt.Title(Snap.Vm) == "Commit" && Txt.Title(Snap.Ram) == "RAM", "조사를 붙이지 않음");
            }
            finally { Lang.En = false; }
            Ok(Lang.Tx("가", "a") == "가" && Fmt.Dur(64 * 60) == "1시간 4분" && Doctor.Ga("G HUB") == "G HUB가", "한국어로 돌아옴");
            string f = Path.Combine(Path.GetTempPath(), "lw-lang-test.txt");
            try
            {
                var st = new Settings(f); st.Lang = "en"; st.Save(); var st2 = new Settings(f); Ok(st2.Lang == "en", "언어 저장"); Lang.Init(null, f); Ok(Lang.En, "저장한 언어로 켜짐"); Lang.Init("ko", f); Ok(!Lang.En, "--lang 이 먼저");
                File.Delete(f); Lang.Init("en", f); Ok(Lang.En, "설정 파일이 없어도");
            }
            finally { Lang.En = false; try { File.Delete(f); } catch (Exception) { } }
        }

        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        /// <summary>실제 기록으로: 200ms 돌고 200ms 쉬는 스레드를 재 본다.</summary>
        static void BlameReal()
        {
            if (!Cleaner.Elevated) { Console.WriteLine("  관리자가 아니라 건너뜀"); return; }
            uint tid = 0; bool stop = false; long q = Stopwatch.Frequency; var runs = new List<long[]>(); var th = new Thread(() => { tid = GetCurrentThreadId(); while (!stop) { long a = Stopwatch.GetTimestamp(); var w = Stopwatch.StartNew(); while (w.ElapsedMilliseconds < 200) { } lock (runs) runs.Add(new[] { a, Stopwatch.GetTimestamp() }); Thread.Sleep(200); } }) { IsBackground = true, Priority = ThreadPriority.Highest }; th.Start(); while (tid == 0) Thread.Sleep(5);
            var b = new Blame(); var me = Process.GetCurrentProcess(); Ok(b.Start(), "기록 시작 " + b.Error); b.Watch((int)tid, 0); Thread.Sleep(3500); stop = true; th.Join(1000); Thread.Sleep(2500); int good = 0, tried = 0; Blame.Part m, s;
            lock (runs) for (int i = 2; i < runs.Count - 1 && i < 7; i++) { tried++; b.Explain(runs[i][0], runs[i][1], out m, out s); if (m.Run > 150 && m.Wait < 30) good++; b.Explain(runs[i][1] + q / 100, runs[i][1] + q * 15 / 100, out m, out s); if (m.Wait > 100 && m.Run < 20) good++; }
            Ok(tried >= 3 && good >= tried * 2 - 2, "실제 스레드 " + good + " / " + tried * 2); Console.WriteLine("  스레드 교체 초당 {0:F0}번", b.Events / 6.0); b.Close(); Ok(!b.Running, "닫음"); Trace.StopByName(Blame.Session + Trace.Tag);
            var own = Blame.Owners(new List<uint> { GetCurrentThreadId() }); int pid; Ok(own.TryGetValue(GetCurrentThreadId(), out pid) && pid == me.Id, "스레드 주인 찾기");
        }

        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll")] static extern IntPtr SetThreadAffinityMask(IntPtr t, IntPtr mask);
        /// <summary>같은 코어를 더 높은 우선순위로 쓰는 스레드 때문에, 자다 깬 스레드가 기다린 시간이 CPU 기다림으로 잡히는지</summary>
        static void BlameWakeReal()
        {
            if (!Cleaner.Elevated) return;
            uint ta = 0, tb = 0; bool stop = false; var mask = (IntPtr)(1L << (Environment.ProcessorCount - 1)); long q = Stopwatch.Frequency;
            var x = new Thread(() => { SetThreadAffinityMask(GetCurrentThread(), mask); ta = GetCurrentThreadId(); while (!stop) Thread.Sleep(5); }) { IsBackground = true };
            var y = new Thread(() => { SetThreadAffinityMask(GetCurrentThread(), mask); tb = GetCurrentThreadId(); while (!stop) { var w = Stopwatch.StartNew(); while (w.ElapsedMilliseconds < 40) { } Thread.Sleep(40); } }) { IsBackground = true, Priority = ThreadPriority.Highest };
            x.Start(); y.Start(); Thread.Sleep(200);
            var b = new Blame(); Ok(b.Start(), "깨어남 기록 시작 " + b.Error); b.Watch((int)ta, 0); Thread.Sleep(1500); long t0 = Stopwatch.GetTimestamp(); Thread.Sleep(1500); long t1 = Stopwatch.GetTimestamp(); Thread.Sleep(2500); stop = true; x.Join(500); y.Join(500);
            Blame.Part m, s; b.Explain(t0, t1, out m, out s); double v; m.By.TryGetValue(tb, out v); b.Close(); Trace.StopByName(Blame.Session + Trace.Tag);
            Console.WriteLine("  자다 깬 스레드 1.5초: 돎 {0:F0} 차례 기다림 {1:F0} (그중 같은 코어 스레드 {2:F0}) 메모리 {3:F0} 쉼 {4:F0} ms", m.Run, m.Ready, v, m.Page, m.Wait);
            Ok(m.Known > 1300 && m.Ready > 300 && v > 150, "깨어난 뒤 기다린 시간이 CPU 기다림으로 " + m.Ready.ToString("F0") + " / " + v.ToString("F0"));
        }

        static void JudgeTest()
        {
            Func<double, double, double, double, Blame.Part> P = (a, b, c, d) => new Blame.Part { Run = a, Ready = b, Page = c, Wait = d }; var none = new Blame.Part();
            Func<double, Blame.Part, Blame.Part, bool, bool, bool, bool, double, Hit> J = (ms, m, s, ram, pg, cpu, drv, un) => { var h = new Hit { Ms = ms, RamLeft = 8 }; h.Judge(m, s, ram, pg, cpu, drv, un); return h; };
            Ok(J(100, P(20, 5, 70, 5), none, true, true, false, false, 0).Kind == Hit.Ram, "램 부족"); Ok(J(100, P(20, 5, 70, 5), none, false, true, false, false, 0).Kind == Hit.Disk, "램은 넉넉한데 디스크에서 읽음"); var lr = new Hit { Ms = 100, RamLeft = 1.2 }; lr.Judge(P(20, 5, 70, 5), none, false, true, false, false, 0); Ok(lr.Kind == Hit.Ram, "남은 램이 적으면 램");
            Ok(J(100, P(40, 50, 5, 5), none, true, false, false, false, 0).Kind == Hit.Cpu, "밀려났으면 램이 모자라도 CPU"); Ok(J(60, P(55, 1, 0, 4), none, true, true, true, false, 0).Kind == Hit.Game, "계속 돌고 있었으면 게임 자체"); var g = J(80, P(5, 0, 0, 75), P(70, 2, 0, 8), false, false, false, false, 0); Ok(g.Kind == Hit.Game && g.Note == "sub", "둘째 스레드가 바빴으면"); Ok(J(80, P(5, 0, 0, 75), P(8, 2, 0, 70), false, false, false, false, 0).Kind == Hit.Etc, "둘 다 기다림"); Ok(J(80, P(5, 0, 0, 75), P(8, 2, 0, 70), false, false, false, true, 0).Kind == Hit.Drv, "드라이버");
            var u = J(1150, P(900, 0, 100, 150), none, true, true, true, true, 1134); Ok(u.Kind == Hit.Game && u.Note == "gc", "게임이 자원을 치우는 중"); Ok(J(100, P(10, 5, 0, 10), none, true, false, true, false, 0).Kind == Hit.Ram && !J(100, P(10, 5, 0, 10), none, true, false, true, false, 0).Measured, "재지 못했으면 짐작"); Ok(J(100, none, none, false, false, false, false, 0).Kind == Hit.Etc && J(100, none, none, false, false, true, false, 0).Kind == Hit.Cpu && J(100, none, none, false, true, true, false, 0).Kind == Hit.Disk, "짐작 순서"); Ok(J(60, P(12, 9, 9, 30), none, false, false, false, false, 0).Kind != 0, "섞여도 하나로");
        }

        static void HitsTest()
        {
            string dir = Path.Combine(Tmp, "hits"); Directory.CreateDirectory(dir); var h = new Hits(dir); var t = DateTime.Now; t = new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second); for (int i = 0; i < 5; i++) h.Add(new Hit { At = t.AddSeconds(i), Exe = i == 4 ? "x" : Boost.Er, Kind = 1 + i % 3, Ms = 50 + i, Who = "node\t1", Note = "a", Run = 1, Ready = 2, Page = 3, Wait = 4, RamLeft = .3, CpuPct = 55, Measured = i % 2 == 0 });
            var z = new Hits(dir); Ok(z.Items.Count == 5 && z.Items[1].Ms == 51 && z.Items[1].Who == "node 1" && !z.Items[1].Measured && z.Items[2].Measured && z.Items[3].Page == 3, "적고 읽기"); Ok(z.Between(Boost.Er, t.AddSeconds(1), t.AddSeconds(9)).Count == 3 && z.Between(Boost.Er, t, t).Count == 1, "구간"); Ok(Hit.Parse("x") == null && Hit.Parse("2026-11-25T15:00:00.0\tx\t9\t1\t\t\t1\t1\t1\t1\t1\t1\t1") == null, "깨진 줄"); File.AppendAllText(Path.Combine(dir, "hits.tsv"), new Hit { At = t.AddDays(-30), Exe = "x", Kind = 1 }.Line() + "\r\n"); Ok(new Hits(dir).Items.Count == 5, "오래된 것은 안 읽음");
            var r = Round.Parse("2026-11-25T15:29:09\tEternalReturn\t1107\t83.1\t44.2\t1154\t38\t41\t0.011\tp1b1g1\t20\t3\t8\t0\t4\t2026-11-25T15:10:38"); Ok(r.HRam == 20 && r.HGame == 4 && r.Why() == "램 부족 20, CPU 8, 게임 자체 4, 디스크 3, 그 외 3" && r.Start.Minute == 10, "판의 까닭 " + r.Why()); r = Round.Parse("2026-11-25T15:29:09\tEternalReturn\t1107\t83.1\t44.2\t1154\t38\t41\t0.011\tp1b1g1"); Ok(r.Why() == "" && Math.Abs((r.End - r.Start).TotalSeconds - 1107) < 1, "옛 줄");
        }

        static void AvgTest()
        {
            Ok(Frames.IsHitch(33, 10.8) && !Frames.IsHitch(30, 10.8) && !Frames.IsHitch(24, 5) && Frames.IsHitch(50, 40), "끊김 기준 3배");
            long q = Stopwatch.Frequency; Func<double, long> ms = x => (long)(x * q / 1000); var f = new Frames { Active = true }; long t = q; int ep = 1000; double acc = 0; Action<double> push = d => { t += ms(d); acc += d; f.Push(t, ep + (int)(acc / 1000)); };
            for (int i = 0; i < 1500; i++) push(8); f.Active = false; for (int i = 0; i < 400; i++) push(33.3); f.Active = true; for (int i = 0; i < 20; i++) push(8); int h0 = f.HitchTotal; push(40); Ok(f.HitchTotal == h0 + 1, "돌아온 직후의 끊김도 잡음");
        }

        /// <summary>해제하면 적어 둔 파일도 바로 비운다.</summary>
        static void RecoverTest()
        {
            string state = Path.Combine(Tmp, "applied2.tsv"); using (var p = Go("lwchild", 30)) { var m = new Monitor(new Settings(null), new EventLog(null)); Thread.Sleep(300); m.Step(); Thread.Sleep(150); m.Step(); var f = new Fixer(m, state); f.Set(new Rule { Key = "lwchild", Label = "시험", Cores = 2, Prio = 1, Always = true }); Ok(File.Exists(state), "적어 둠"); f.Remove("lwchild"); Ok(!File.Exists(state), "해제하면 파일도 비움"); f.Set(new Rule { Key = "lwchild", Label = "시험", Cores = 2, Prio = 1, Always = true }); f.Set(new Rule { Key = "lwchild", Label = "시험" }); Ok(!File.Exists(state), "없앤 규칙도"); p.Kill(); }
            File.WriteAllText(state, "999999\t1\t32\t255\tx\n"); var m2 = new Monitor(new Settings(null), new EventLog(null)); m2.Step(); new Fixer(m2, state); Ok(!File.Exists(state), "없어진 프로세스는 남기지 않음");
        }

        /// <summary>그림 저장·측정용은 기록 폴더에 아무것도 쓰지 않는다.</summary>
        static void ReadOnlyTest()
        {
            string dir = Path.Combine(Tmp, "ro"); Action<History, int> fill = (h, g) => { int t = 1764000000; ushort id = h.Id("k" + g, "이름" + g); for (int i = 0; i < 400; i++) h.Add(new MPoint { T = t + i, Cpu = .5f, Game = id }, 1); h.AddBlock(new Block { Minute = t / 60, Apps = new[] { new AppAt { Id = id, Commit = 5 } } }); h.Save(); };
            fill(new History(dir), 1); Func<string> all = () => { var sb = new StringBuilder(); var fs = new List<string>(Directory.GetFiles(dir)); fs.Sort(); foreach (var f in fs) sb.Append(Path.GetFileName(f)).Append(Convert.ToBase64String(File.ReadAllBytes(f))); return sb.ToString(); }; string before = all(); Ok(before.Length > 100, "기록 있음");
            var ro = new History(dir, true); Ok(ro.FindId("k1") != 0, "읽기는 됨"); fill(ro, 2); Ok(all() == before, "아무것도 안 바뀜"); string none = Path.Combine(Tmp, "ro2"); fill(new History(none, true), 3); Ok(!Directory.Exists(none), "폴더도 안 만듦");
        }

        // ---- 실행 우선순위(시험용 이름에만 만들고 지운다) ----

        static void PrioTest()
        {
            if (!Alone("게임 우선순위 등록")) return;
            const string exe = "lwprio.exe"; string key = PrioKnob.Ifeo + exe, dir = Path.Combine(Tmp, "prio"); Process p;
            Action clean = () => { try { Registry.LocalMachine.DeleteSubKeyTree(key, false); } catch (Exception) { } };
            if (!Cleaner.Elevated) { Console.WriteLine("  관리자가 아니라 우선순위 시험을 건너뜀"); return; }
            clean();
            try
            {
                var k = new PrioKnob(exe, dir); k.Read(); Ok(k.Now == 0, "처음은 기본");
                Ok(k.Set(1) == null && k.Now == 1, "높음으로"); using (p = Go("lwprio", 20)) { Thread.Sleep(200); Ok(p.PriorityClass == ProcessPriorityClass.High, "켤 때 높음 " + p.PriorityClass); p.Kill(); }
                using (var rk = Registry.LocalMachine.OpenSubKey(key)) Ok(rk.ValueCount == 0 && rk.SubKeyCount == 1, "다른 값은 안 만듦");
                Ok(k.Set(0) == null && k.Now == 0, "되돌리기"); using (var rk = Registry.LocalMachine.OpenSubKey(key)) Ok(rk == null, "만든 키를 지움"); using (p = Go("lwprio", 20)) { Thread.Sleep(200); Ok(p.PriorityClass == ProcessPriorityClass.Normal, "켤 때 보통"); p.Kill(); }
                // 원래 다른 값과 다른 키가 있던 경우
                using (var rk = Registry.LocalMachine.CreateSubKey(key + @"\PerfOptions")) { rk.SetValue("CpuPriorityClass", 6, RegistryValueKind.DWord); rk.SetValue("Other", 1, RegistryValueKind.DWord); }
                k.Read(); Ok(k.Now == -1 && k.Note.Contains("6"), "다른 값이면 모름으로"); Ok(k.Set(1) == null && k.Now == 1, "덮어쓰기"); Ok(k.Set(1) == null, "두 번 눌러도");
                var k2 = new PrioKnob(exe, dir); Ok(k2.Set(0) == null, "다시 켠 뒤 되돌리기"); using (var rk = Registry.LocalMachine.OpenSubKey(key + @"\PerfOptions")) Ok((int)rk.GetValue("CpuPriorityClass") == 6 && (int)rk.GetValue("Other") == 1, "처음 값으로");
                Registry.LocalMachine.DeleteSubKeyTree(key, false); Ok(k.Set(0) == null && k.Now == 0, "키가 없어도 되돌리기");
                using (var rk = Registry.LocalMachine.OpenSubKey(PrioKnob.Ifeo.TrimEnd('\\'))) { var n = new List<string>(rk.GetSubKeyNames()); Ok(!n.Contains(exe), "남은 것 없음"); }
            }
            finally { clean(); }
        }

        // ---- 롤 클라이언트 설정 ----

        static void ClientTest()
        {
            string lol = Path.Combine(Tmp, "lol"), f = Path.Combine(lol, @"Config\LCULocalPreferences.yaml"); Directory.CreateDirectory(Path.GetDirectoryName(f));
            string t = "lol-replays:\n    data:\n        dontShowEogUsageTip: true\n    schemaVersion: 1\nlol-user-experience:\n    data:\n        closeLeagueClientDuringGame: false\n        motionEffectsDisabled: true\n    schemaVersion: 3\n"; File.WriteAllText(f, t);
            bool run = true; var k = new ClientKnob(lol, Path.Combine(Tmp, "cl"), () => run); k.Read(); Ok(k.Now == 0, "켜 둠");
            Ok(k.Set(1) == null && k.Now == 0 && k.Pending == 1 && File.ReadAllText(f) == t, "클라이언트가 켜져 있으면 파일을 안 건드림"); k.Later(); Ok(File.ReadAllText(f) == t, "아직 켜져 있음");
            run = false; k.Later(); Ok(k.Now == 1 && k.Pending == -1 && File.ReadAllText(f) == t.Replace("DuringGame: false", "DuringGame: true"), "꺼지면 그 줄만 바꿈");
            Ok(k.Set(0) == null && File.ReadAllText(f) == t, "되돌리면 처음과 같음"); run = true; k.Set(1); Ok(k.Pending == 1, "다시 예약"); k.Set(0); Ok(k.Pending == -1, "예약 취소");
            Ok(k.Home == 0, "되돌릴 값은 처음 값"); { string d2 = Path.Combine(Tmp, "cl2"); Directory.CreateDirectory(d2); run = false; File.WriteAllText(f, t.Replace("DuringGame: false", "DuringGame: true")); var kh = new ClientKnob(lol, d2, () => run); kh.Read(); Ok(kh.Set(0) == null && kh.Home == 1, "원래 닫기를 쓰던 사람은 닫기로"); File.SetAttributes(f, FileAttributes.ReadOnly); Ok(kh.Set(1) == null && kh.Now == 1 && (File.GetAttributes(f) & FileAttributes.ReadOnly) != 0 && !File.Exists(f + ".lw"), "읽기 전용"); File.SetAttributes(f, FileAttributes.Normal); run = true; }
            File.WriteAllText(f, "a: 1\n"); k.Read(); Ok(k.Now == -1 && k.Set(1) != null && File.ReadAllText(f) == "a: 1\n", "줄이 없으면 안 건드림");
            run = false; File.WriteAllText(f, t.Replace("false", "false\r")); k.Read(); Ok(k.Now == 0 && k.Set(1) == null && k.Now == 1 && File.ReadAllText(f).Contains("DuringGame: true\r"), "줄 끝 그대로");
            File.Delete(f); k.Read(); Ok(k.Now == -1 && k.Set(1) != null, "파일이 없으면"); var n = new ClientKnob(null, null, () => false); n.Read(); Ok(n.Now == -1 && n.Set(1) != null, "설치 안 됨");
        }

        // ---- boot.config ----

        static byte[] V(ulong v) { var l = new List<byte>(); while (v > 127) { l.Add((byte)(v & 127 | 128)); v >>= 7; } l.Add((byte)v); return l.ToArray(); }
        static byte[] F(int field, byte[] d) { var l = new List<byte>(V((ulong)(field << 3 | 2))); l.AddRange(V((ulong)d.Length)); l.AddRange(d); return l.ToArray(); }
        static byte[] Manifest(string name, byte[] sha)
        {
            var m = new List<byte>(); m.AddRange(F(1, Encoding.UTF8.GetBytes(name))); m.Add(16); m.AddRange(V(181)); m.AddRange(F(4, new byte[20])); m.AddRange(F(5, sha)); m.AddRange(F(6, new byte[33]));
            var other = new List<byte>(); other.AddRange(F(1, Encoding.UTF8.GetBytes("a.txt"))); other.AddRange(F(5, new byte[20]));
            var body = new List<byte>(); body.AddRange(F(1, other.ToArray())); body.AddRange(F(1, m.ToArray())); var r = new List<byte>(BitConverter.GetBytes(0x71F617D0u)); r.AddRange(BitConverter.GetBytes(body.Count)); r.AddRange(body); r.AddRange(BitConverter.GetBytes(0x1F4812BEu)); r.AddRange(BitConverter.GetBytes(2)); r.Add(8); r.Add(1); return r.ToArray();
        }
        static void BootTest()
        {
            string root = Path.Combine(Tmp, "er"), er = Path.Combine(root, "game"), steam = Path.Combine(root, "steam"), dir = Path.Combine(root, "keep"), f = Path.Combine(er, @"EternalReturn_Data\boot.config");
            Directory.CreateDirectory(Path.GetDirectoryName(f)); Directory.CreateDirectory(Path.Combine(steam, "depotcache")); Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
            string g1 = "5ebf5821106543c69a34df81bb9c100a", org = "gfx-enable-gfx-jobs=1\ngfx-threading-mode=6\nmax-chunks-per-shader=64\nwait-for-native-debugger=0\nhdr-display-enabled=0\ngc-max-time-slice=3\nbuild-guid=" + g1 + "\n";
            Func<string, byte[]> sha = t => System.Security.Cryptography.SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(t)); string mf = Path.Combine(steam, @"depotcache\1049591_111.manifest"), acf = Path.Combine(steam, @"steamapps\appmanifest_1049590.acf");
            File.WriteAllBytes(mf, Manifest(@"EternalReturn_Data\boot.config", sha(org))); File.WriteAllText(acf, "\"AppState\" { \"InstalledDepots\" { \"1049591\" { \"manifest\" \"111\" } } }"); File.WriteAllText(f, org);
            Ok(BootKnob.FindSha(File.ReadAllBytes(mf), "EternalReturn_Data/boot.config") == BitConverter.ToString(sha(org)).Replace("-", "").ToLowerInvariant(), "매니페스트에서 해시 찾기"); Ok(BootKnob.FindSha(new byte[] { 1, 2, 3 }, "x") == null && BootKnob.FindSha(File.ReadAllBytes(mf), "none") == null, "없는 파일"); var cut = File.ReadAllBytes(mf); Array.Resize(ref cut, cut.Length / 2); Ok(BootKnob.FindSha(cut, @"EternalReturn_Data\boot.config") == null, "잘린 매니페스트");
            bool run = false; int want = 0; var k = new BootKnob(er, steam, dir, () => run) { Wanted = () => want, Want = i => want = i };
            k.Read(); Ok(k.Now == 0 && k.Note == "" && k.Original(g1) == org, "원본이면 남겨 둠");
            Ok(k.Options.Length == 2 && k.Set(1) != null && File.ReadAllText(f) == org, "내 설정이 없으면 안 바꿈");
            // EROptimizer 가 쓴 것
            string mine = "wait-for-native-debugger=0\nmax-chunks-per-shader=8\nmemorysetup-bucket-allocator-granularity=16\njob-worker-count=15\ngfx-threading-mode=6\nbuild-guid=" + g1 + "\n"; File.WriteAllText(f, mine); k.Read(); Ok(k.Now == 1 && k.Options[1] == "EROptimizer", "다른 프로그램이 쓴 것 " + k.Now); run = true; Ok(k.Set(0) != null && File.ReadAllText(f) == mine, "게임이 켜져 있으면 안 바꿈"); run = false;
            Ok(k.Set(0) == null && k.Now == 0 && want == 0 && File.ReadAllText(f) == org && !File.ReadAllText(f).Contains("job-worker-count"), "원본으로"); Ok(Directory.GetFiles(Path.Combine(dir, "boot-backup")).Length >= 1 && !File.Exists(f + ".lw"), "바꾸기 전 것을 남김"); Ok(k.Set(1) == null && k.Now == 1 && want == 1, "다시 그 설정으로"); File.SetAttributes(f, FileAttributes.ReadOnly); Ok(k.Set(0) == null && File.ReadAllText(f) == org && (File.GetAttributes(f) & FileAttributes.ReadOnly) != 0, "읽기 전용이어도 바꾸고 다시 건다"); File.SetAttributes(f, FileAttributes.Normal); Ok(k.Set(1) == null, "다시"); var mp = BootKnob.Parse(File.ReadAllText(f)); Ok(!mp.Exists(x => x.Key == "job-worker-count"), "job-worker-count 는 뺌"); Ok(mp.Exists(x => x.Key == "max-chunks-per-shader" && x.Value == "8") && mp.Exists(x => x.Key == "gc-max-time-slice" && x.Value == "3") && mp[mp.Count - 1].Key == "build-guid", "내 것을 원본에 얹음");
            // 패치
            string g2 = "00000000000000000000000000000002", org2 = org.Replace(g1, g2).Replace("gc-max-time-slice=3", "gc-max-time-slice=5"); File.WriteAllText(f, org2); File.WriteAllBytes(Path.Combine(steam, @"depotcache\1049591_222.manifest"), Manifest(@"EternalReturn_Data\boot.config", sha(org2))); File.WriteAllText(acf, File.ReadAllText(acf).Replace("111", "222"));
            k.Read(); Ok(k.Now == 0 && k.Reset && k.Note == "패치로 초기화됨" && k.Original(g2) == org2, "패치를 알아챔"); Ok(k.Set(want) == null && k.Now == 1 && !k.Reset && BootKnob.Guid(File.ReadAllText(f)) == g2 && File.ReadAllText(f).Contains("gc-max-time-slice=5"), "새 원본에 다시 얹음");
            // 원본을 모를 때
            var k2 = new BootKnob(er, null, Path.Combine(root, "keep2"), () => false); File.WriteAllText(f, mine); k2.Read(); Ok(k2.Now == 1 && k2.Note == "원본 확인 안 됨" && k2.Set(1) == null && File.ReadAllText(f) == mine, "원본을 모르면 안 바꿈");
            File.WriteAllText(f, org.Replace("build-guid", "x")); k.Read(); string nog = File.ReadAllText(f); k.Set(1); Ok(k.Now == 1 && File.ReadAllText(f) == nog, "build-guid 가 없으면 안 바꿈"); File.Delete(f); k.Read(); Ok(k.Now == -1 && k.Set(0) != null, "파일이 없으면"); var k3 = new BootKnob(null, null, null, () => false); k3.Read(); Ok(k3.Now == -1 && k3.Set(1) != null, "설치 안 됨");
            for (int i = 0; i < 30; i++) { File.WriteAllText(f, org2); k.Read(); k.Set(1); System.Threading.Thread.Sleep(1); } Ok(Directory.GetFiles(Path.Combine(dir, "boot-backup")).Length <= 20, "남긴 것은 20개까지");
            // 이 PC 의 실제 파일은 읽기만
            string sd = Boost.SteamDir(); if (sd != null) { var rk = new BootKnob(null, sd, null, () => true); Console.WriteLine("  Steam 원본 해시 " + rk.SteamSha()); }
        }

        // ---- 판 기록 ----

        static void RoundTest()
        {
            var m = new Meter(); for (int i = 0; i < 9900; i++) m.Add(5, false); for (int i = 0; i < 100; i++) m.Add(20, false); Ok(Math.Abs(m.Fps - 10000 / (9900 * 5 + 2000.0) * 1000) < .1, "평균 " + m.Fps); Ok(Math.Abs(m.Slow(.01) - 20) < .3 && Math.Abs(m.Slow(.5) - 5) < .3, "하위 1% " + m.Slow(.01)); m.Add(900, true); Ok(m.Max == 900 && m.Hitch == 1 && m.Slow(.00001) == 900, "긴 프레임");
            var r = new Round { End = new DateTime(2026, 11, 25, 3, 4, 5), Game = Boost.Lol, Sec = 1200, Fps = 171.23, Low = 101.5, Max = 88, Hitch = 7, Cpu = 41, Wait = .052, Sig = "p1c0g1" }; var q = Round.Parse(r.Line()); Ok(q != null && q.End == r.End && q.Game == r.Game && q.Hitch == 7 && Math.Abs(q.Fps - 171.2) < .01 && q.Sig == "p1c0g1" && Math.Abs(q.Per10 - 3.5) < .001, "적고 읽기"); Ok(Round.Parse("x") == null && Round.Parse("a\tb\tc\td\te\tf\tg\th\ti\tj") == null, "깨진 줄");
            string dir = Path.Combine(Tmp, "rounds"); Directory.CreateDirectory(dir); long hz = Stopwatch.Frequency; string sig = "a";
            var f = new Frames { Active = true }; var rs = new Rounds(f, dir, g => sig) { Force = true }; long t = hz; int ep = 5000; double now = 100;
            Action<int, double> play = (sec, ms) => { for (double a = 0; a < sec * 1000; a += ms) { t += (long)(ms * hz / 1000); f.Push(t, ep + (int)(a / 1000)); } ep += sec; };
            // 롤: 켜지고 75초 뒤부터
            rs.Tick(now, Boost.Lol, 40, .02); play(60, 8); rs.Tick(now += 60, Boost.Lol, 40, .02); play(20, 8); rs.Tick(now += 20, Boost.Lol, 40, .02); play(400, 5); rs.Tick(now += 400, Boost.Lol, 60, .04); rs.Tick(now += 2, null, 0, 0);
            Ok(rs.Items.Count == 1 && Math.Abs(rs.Items[0].Fps - 200) < 3 && Math.Abs(rs.Items[0].Sec - 400) < 3 && rs.Items[0].Sig == "a", "롤 한 판 " + (rs.Items.Count > 0 ? rs.Items[0].Line() : "없음"));
            rs.Tick(now += 5, Boost.Lol, 40, 0); now += 80; rs.Tick(now, Boost.Lol, 40, 0); play(100, 5); rs.Tick(now += 100, null, 0, 0); Ok(rs.Items.Count == 1, "3분이 안 되면 안 남김");
            // 이터널 리턴: 로그 줄로
            string old = Rounds.ErLog; Rounds.ErLog = Path.Combine(dir, "Player.log"); File.WriteAllText(Rounds.ErLog, "[INFO][x][1][ClientService:OnUpdateGamePlayPhase:3619] OnUpdateGamePlayPhase : PlayGame, 1 1\n");
            try
            {
                rs.Tick(now += 5, Boost.Er, 40, 0); play(300, 5); rs.Tick(now += 300, Boost.Er, 40, 0); rs.Tick(now += 3, null, 0, 0); Ok(rs.Items.Count == 1, "로비에만 있으면 안 남김(켜기 전 줄도 안 봄)");
                rs.Tick(now += 5, Boost.Er, 40, 0); File.AppendAllText(Rounds.ErLog, "[INFO][x][1][ClientService:OnUpdateGamePlayPhase:3619] OnUpdateGamePlayPhase : Briefing, 1 1\n"); rs.Tick(now += 3, Boost.Er, 40, 0); play(50, 5); File.AppendAllText(Rounds.ErLog, "[INFO][x][1][ClientService:OnUpdateGamePlayPhase:3619] OnUpdateGamePlayPhase : PlayGame, 1 1\n"); rs.Tick(now += 3, Boost.Er, 40, 0); play(500, 10); sig = "b"; t += (long)(200 * hz / 1000); f.Push(t, ep); rs.Tick(now += 1, Boost.Er, 40, 0, 0, true); t += (long)(10 * hz / 1000); f.Push(t, ep); t += (long)(120 * hz / 1000); f.Push(t, ep); rs.Tick(now += 5, Boost.Er, 40, .2); t += (long)(90 * hz / 1000); f.Push(t, ep); rs.Tick(now += 9, Boost.Er, 40, 0); File.AppendAllText(Rounds.ErLog, "[INFO][x][1][LoadingView:Show:135] [PROFILE TIME][LOADING][LOBBY]\n"); rs.Tick(now += 500, Boost.Er, 40, 0);
                Ok(rs.Items.Count == 2 && rs.Items[1].Game == Boost.Er && Math.Abs(rs.Items[1].Sec - 500) < 4 && Math.Abs(rs.Items[1].Fps - 100) < 2 && rs.Items[1].Sig == "a", "이터널 리턴 한 판, 설정은 시작할 때 것"); { var z = rs.Items[1]; Ok(z.Hitch == 3 && z.HRam == 1 && z.HCpu == 1 && z.HDisk == 0 && z.Why() == "램 부족 1, CPU 1, 그 외 1", "끊긴 까닭 " + z.Hitch + " " + z.Why()); var zz = Round.Parse(z.Line()); Ok(zz.HRam == 1 && zz.HCpu == 1 && zz.Why() == z.Why(), "까닭도 적고 읽기"); } t += (long)(900 * hz / 1000); f.Push(t, ep); rs.Mark("[INFO][x][1][LobbyClient:Start:73] [PROFILE TIME][LOBBY]", now); Ok(rs.Items.Count == 2 && rs.Items[1].Max < 300, "로비 로딩은 판에 안 넣음"); play(300, 5); rs.Tick(now += 300, null, 0, 0); Ok(rs.Items.Count == 2, "로비는 안 셈");
            }
            finally { Rounds.ErLog = old; }
            var again = new Rounds(f, dir, null); Ok(again.Items.Count == 2 && again.Of(Boost.Er, 9).Count == 1, "다시 읽기");
            // 앞뒤 비교
            Round b, a2; int nb, na; again.Items.Clear(); Action<string, int, double> add = (s, h, fps) => again.Items.Add(new Round { Game = Boost.Lol, Sec = 600, Fps = fps, Low = fps / 2, Hitch = h, Sig = s });
            Ok(!again.Compare(Boost.Lol, out b, out nb, out a2, out na), "기록 없음"); add("z", 50, 10); foreach (int h in new[] { 6, 4, 5, 5, 30 }) add("p0c0g0", h, 150); foreach (int h in new[] { 1, 2, 1, 2 }) add("p1c1g1", h, 170); Ok(!again.Compare(Boost.Lol, out b, out nb, out a2, out na), "5판이 안 되면 비교 안 함"); add("p1c1g1", 40, 180);
            Ok(again.Compare(Boost.Lol, out b, out nb, out a2, out na) && nb == 5 && na == 5 && Math.Abs(b.Per10 - 5) < .01 && Math.Abs(a2.Per10 - 2) < .01 && Math.Abs(a2.Low - 85) < .01, "앞뒤 비교는 가운데 값 " + nb + " " + na); add("q1", 3, 100); Ok(!again.Compare(Boost.Lol, out b, out nb, out a2, out na), "새 설정은 다시 5판부터");
            Ok(Rounds.Plain("p0b0g0") && Rounds.Plain("p0cxg0") && !Rounds.Plain("p1c0g0") && !Rounds.Plain("p0b1g0") && !Rounds.Plain("p0c0g1"), "기본 설정인지"); Ok(Round.Parse("2026-11-25T13:29:38\tEternalReturn\t436\t92.6\t51.6\t102\t13\t39\t0.008\tp1b2g1").Sig == "p1b1g1", "옛 표시 바꾸기");
        }

        static void TailTest()
        {
            string f = Path.Combine(Tmp, "tail.log"); File.WriteAllText(f, "옛것\n"); var t = new LogTail(f); Ok(t.Read().Count == 0, "처음은 끝에서부터"); File.AppendAllText(f, "하나\n둘"); var l = t.Read(); Ok(l.Count == 1 && l[0] == "하나", "끝난 줄만"); File.AppendAllText(f, " 이어서\r\n"); l = t.Read(); Ok(l.Count == 1 && l[0] == "둘 이어서", "잇기");
            File.WriteAllText(f, "새 파일\n"); l = t.Read(); Ok(l.Count == 1 && l[0] == "새 파일", "새로 시작한 파일"); using (var w = new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) { w.Seek(0, SeekOrigin.End); var b = Encoding.UTF8.GetBytes("쓰는 중\n"); w.Write(b, 0, b.Length); w.Flush(); l = t.Read(); Ok(l.Count == 1, "쓰는 중이어도 읽음"); }
            File.Delete(f); Ok(t.Read().Count == 0, "지워져도 괜찮음"); Ok(new LogTail(Path.Combine(Tmp, "none.log")).Read().Count == 0, "없는 파일");
            var big = new StringBuilder(); for (int i = 0; i < 60000; i++) big.Append("가나다라마바사아자차 ").Append(i).Append('\n'); File.WriteAllText(f, "x\n"); t = new LogTail(f); t.Read(); File.AppendAllText(f, big.ToString()); int n = 0; for (int i = 0; i < 5; i++) n += t.Read().Count; Ok(n > 1000, "아주 긴 것 " + n);
        }

        // ---- 게임 중 다른 프로그램 코어 제한(시험이 켠 것에만) ----

        static void BgTest()
        {
            if (!Alone("다른 프로그램 제한")) return;
            Ok(Fixer.BgCores(8) == 3 && Fixer.BgCores(6) == 2 && Fixer.BgCores(4) == 2 && Fixer.BgCores(16) == 6 && Fixer.BgCores(2) == 1, "남겨 줄 코어 수"); foreach (var n in new[] { "Discord", "obs64", "NVIDIA Overlay", "lghub_agent", "steam" }) Ok(Names.IsLive(n), n + " 은 뺌"); Ok(!Names.IsLive("node") && !Names.IsLive("chrome"), "node, chrome 은 대상");
            foreach (var bad in new[] { "mysqld", "vgc", "LeagueClient", "Discord" }) { var gg = new Grp { Key = bad, Label = bad }; gg.Add(new App { Exe = bad, Key = bad, Protected = Names.IsGuarded(bad) }); Ok(!Fixer.CanBg(gg), bad + " 은 제한 안 함"); }
            using (var busy = Go("lwbusy", 40, "--spin")) using (var idle = Go("lwidle", 40, "--sleep"))
            {
                var set = new Settings(null); set.Games.Add("lwgame"); var m = new Monitor(set, new EventLog(null)); var f = new Fixer(m, Path.Combine(Tmp, "bg.tsv")); Action step = () => { for (int i = 0; i < 3; i++) { m.Step(); Thread.Sleep(350); } m.Step(); };
                try
                {
                    Thread.Sleep(400); step(); ulong full = Aff(busy); uint pr = Pri(busy); ulong want = Fixer.Mask(m.CoreOf, m.Phys, Fixer.BgCores(m.Phys));
                    // 이 시험 동안 실제 프로그램은 걸리지 않게, 시험 프로그램이 아닌 것은 묶음에서 빠지도록 검사만 한다
                    Dry(m); set.Bg = true; f.Tick(); Ok(Aff(busy) == full && f.BgLabels().Count == 0, "게임이 없으면 안 걸림");
                    using (var gm = Go("lwgame", 40, "--sleep"))
                    {
                        Thread.Sleep(400); step(); Dry(m); Ok(m.Game != null, "게임 켜짐"); f.Tick(); Ok(Aff(busy) == want && Pri(busy) == 0x4000, "바쁜 것에 걸림 " + Aff(busy).ToString("x")); Ok(Aff(idle) == full && Pri(idle) == pr, "노는 것은 그대로"); Ok(Aff(gm) == full && Pri(gm) == pr, "게임은 그대로"); Ok(f.BgLabels().Count == 1, "걸린 것 1개 " + f.BgLabels().Count);
                        set.Bg = false; f.Tick(); Ok(Aff(busy) == full && Pri(busy) == pr, "끄면 바로 되돌림"); set.Bg = true; f.Tick(); Ok(Aff(busy) == want, "다시 켬");
                        f.Set(new Rule { Key = "lwbusy", Label = "시험", Cores = 1, Prio = 2 }); f.Tick(); Ok(Aff(busy) == Fixer.Mask(m.CoreOf, m.Phys, 1), "따로 건 것이 먼저"); f.Remove("lwbusy"); f.Tick();
                        gm.Kill(); gm.WaitForExit();
                    }
                    Thread.Sleep(300); step(); Dry(m); f.Tick(); Ok(Aff(busy) == full && Pri(busy) == pr && f.BgLabels().Count == 0, "게임이 끝나면 되돌림");
                }
                finally { set.Bg = false; f.RestoreAll(); try { busy.Kill(); idle.Kill(); } catch (Exception) { } }
            }
            foreach (var n in new[] { "lwbusy", "lwidle", "lwgame" }) foreach (var p in Process.GetProcessesByName(n)) try { p.Kill(); } catch (Exception) { }
        }
        /// <summary>시험이 켠 것 말고는 CPU를 0으로 보이게 해 실제 프로그램에 걸리지 않게 한다.</summary>
        static void Dry(Monitor m) { lock (m.Sync) foreach (var a in m.Apps.Values) if (!(a.Exe ?? "").StartsWith("lwbusy") && !(a.Exe ?? "").StartsWith("lwidle")) a.Cores = 0; }
        static Process Go(string name, int sec, string how) { return Process.Start(new ProcessStartInfo(Child(name), how + " " + sec) { UseShellExecute = false, CreateNoWindow = true }); }

        // ---- 화면 ----

        static void GameUi()
        {
            string dir = Path.Combine(Tmp, "gui"); Directory.CreateDirectory(dir); var m = Live(); var d = new Doctor(m, dir); d.Boost.Read();
            for (int i = 0; i < 20; i++) d.Rounds.Items.Add(new Round { End = DateTime.Now.AddHours(-i), Game = i % 2 == 0 ? Boost.Lol : Boost.Er, Sec = 900, Fps = 150, Low = 80, Hitch = i, Max = 90, Sig = i < 8 ? "x" : d.Boost.Sig(i % 2 == 0 ? Boost.Lol : Boost.Er) });
            foreach (var th in new[] { Theme.Night, Theme.Day }) { Theme.Cur = th; using (var v = new MainView(d) { Still = true }) { v.Refresh(); v.Go(MainView.Game); foreach (var sz in new[] { new[] { 1080, 720 }, new[] { 960, 640 }, new[] { 1920, 1040 }, new[] { 300, 200 } }) v.Draw(sz[0], sz[1], 1); for (int y = 60; y < 720; y += 23) for (int x = 10; x < 1080; x += 41) { v.MouseMove(x, y); v.Draw(1080, 720, 1); } Ok(true, "게임 화면"); var rd = new Round { Start = DateTime.Now.AddMinutes(-20), End = DateTime.Now, Game = Boost.Er, Sec = 1100, Fps = 80, Low = 44, Hitch = 40, Sig = "p0b0g0" }; for (int i = 0; i < 40; i++) d.Hits.Add(new Hit { At = rd.Start.AddSeconds(i * 25), Exe = Boost.Er, Kind = 1 + i % 6, Ms = 30 + i * 37, Who = "node", Note = "계산", RamLeft = .5, Measured = i % 2 == 0 }); lock (d.Rounds.Items) d.Rounds.Items.Add(rd); v.Run("page:round"); foreach (var sz in new[] { new[] { 1080, 720 }, new[] { 960, 640 }, new[] { 300, 200 } }) v.Draw(sz[0], sz[1], 1); for (int x = 20; x < 1060; x += 9) { v.MouseMove(x, 300); v.Draw(1080, 720, 1); } v.Go(0); v.Draw(1080, 720, 1); for (int y = 270; y < 720; y += 19) for (int x = 10; x < 1080; x += 61) { v.MouseMove(x, y); v.Draw(1080, 720, 1); } v.Cores = true; v.Draw(1080, 720, 1); Ok(true, "판 보기와 흐름"); } using (var p = new PanelView(d) { Still = true }) { p.Refresh(); for (int sub = 1; sub <= 2; sub++) { p.Sub = sub; p.Draw(500, 848, 1); p.Draw(500, 848, 1.25f); Ok(p.Height > 300 && p.Height < 760, "작은 창 " + sub + " 높이 " + p.Height); for (int y = 60; y < 840; y += 17) { p.MouseMove(250, y); p.Draw(500, 848, 1); } } } }
            D2.Shutdown(); Theme.Cur = Theme.Night; d.Dispose();
            foreach (var g in d.Boost.Games) { Console.Write("  " + g.Label + " " + (g.Installed ? "있음" : "없음")); foreach (var k in g.Knobs) Console.Write(", " + k.Label + " " + (k.Now < 0 ? "?" : k.Options[k.Now]) + (k.Note.Length > 0 ? "(" + k.Note + ")" : "")); Console.WriteLine(); }
        }
    }
}
