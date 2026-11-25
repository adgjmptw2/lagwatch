using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace LagWatch
{
    internal static unsafe partial class Tests
    {
        static void LiteTests() { LevelTest(); DrawTest(); ClipTest(); CurveTest(); TextTest(); GlyphCapTest(); SysTest(); ClockTest(); SetTest(); LoopTest(); }

        static uint At(Canvas c, int x, int y) { return ((uint*)c.Bits)[y * c.PxW + x]; }
        static int G(Canvas c, int x, int y) { return (int)(At(c, x, y) >> 8 & 255); }
        static double Sum(Canvas c) { double s = 0; uint* p = (uint*)c.Bits; for (int i = 0; i < c.PxW * c.PxH; i++) s += (p[i] >> 8 & 255) / 255.0; return s; }
        static Canvas Blank(int w, int h, float k = 1) { var c = new Canvas(); c.Resize(w, h, k); c.Begin(); c.Clear(Col.Hex(0)); return c; }
        static readonly Col Wh = Col.Hex(0xffffff);

        static void DrawTest()
        {
            var c = Blank(64, 64);
            c.Fill(10, 10, 20, 20, Wh); Ok(Math.Abs(Sum(c) - 400) < .01 && G(c, 10, 10) == 255 && G(c, 9, 10) == 0 && G(c, 30, 10) == 0, "네모는 자리에 꼭 맞게");
            c.Clear(Col.Hex(0)); c.Fill(10.5f, 10, 10, 10, Wh); Ok(Math.Abs(Sum(c) - 100) < 1 && Math.Abs(G(c, 10, 12) - 128) <= 1 && Math.Abs(G(c, 20, 12) - 128) <= 1, "반 픽셀은 반만");
            c.Clear(Col.Hex(0)); c.Fill(5.2f, 5.2f, .4f, 20, Wh); Ok(Math.Abs(Sum(c) - 8) < .4, "한 픽셀 안의 가는 줄 " + Sum(c).ToString("F2"));
            c.Clear(Col.Hex(0)); c.Fill(-50, -50, 500, 500, Wh); Ok(Math.Abs(Sum(c) - 4096) < .01, "밖으로 나간 것은 잘림");
            c.Clear(Col.Hex(0)); c.Fill(0, 0, 64, 64, Wh.Fade(.25f)); Ok(Math.Abs(G(c, 30, 30) - 64) <= 1, "반투명");
            c.Clear(Col.Hex(0xff0000)); c.Fill(0, 0, 64, 64, Col.Hex(0x0000ff, .5f)); uint v = At(c, 5, 5); Ok(Math.Abs((int)(v >> 16 & 255) - 128) <= 1 && Math.Abs((int)(v & 255) - 128) <= 1 && v >> 24 == 255, "겹치면 섞임");
            c.Clear(Col.Hex(0)); c.Circle(32, 32, 20, Wh); Ok(Math.Abs(Sum(c) - Math.PI * 400) < 6, "원 넓이 " + Sum(c).ToString("F1"));
            c.Clear(Col.Hex(0)); c.Round(4, 4, 50, 40, 12, Wh); Ok(Math.Abs(Sum(c) - (2000 - (4 - Math.PI) * 144)) < 4, "둥근 네모 넓이");
            c.Clear(Col.Hex(0)); c.Round(4, 4, 50, 8, 99, Wh); Ok(Math.Abs(Sum(c) - (400 - (4 - Math.PI) * 16)) < 4, "알약 넓이");
            c.Clear(Col.Hex(0)); c.RoundLine(10, 10, 40, 40, 0, Wh, 2); Ok(Math.Abs(Sum(c) - 320) < 8, "테두리 " + Sum(c).ToString("F1"));
            c.Clear(Col.Hex(0)); c.Line(5, 5, 45, 45, Wh, 2); Ok(Math.Abs(Sum(c) - Math.Sqrt(3200) * 2) < 2, "빗금 " + Sum(c).ToString("F1"));
            c.Clear(Col.Hex(0)); c.Gradient(0, 0, 64, 64, Col.Hex(0), Col.Hex(0x808080), Col.Hex(0xffffff), .5f); Ok(G(c, 3, 0) < 4 && Math.Abs(G(c, 3, 32) - 130) < 5 && G(c, 3, 63) > 250, "번지는 색");
            c.Clear(new Col(0, 0, 0, 0)); Ok(At(c, 1, 1) == 0, "빈 바탕"); c.Fill(0, 0, 10, 10, Wh.Fade(.5f)); Ok(Math.Abs((int)(At(c, 5, 5) >> 24) - 128) <= 1 && Math.Abs(G(c, 5, 5) - 128) <= 1, "빈 바탕 위는 미리 곱한 값");
            c.Dispose();
            var rnd = new Random(3); c = Blank(200, 120, 1.25f); bool ok = true; // 아무 값이나 넣어도 죽지 않음
            for (int i = 0; i < 4000; i++)
            {
                try
                {
                    Func<float> f = () => (float)(rnd.NextDouble() * 600 - 200); var pts = new P2[6]; for (int k = 0; k < 6; k++) pts[k] = new P2(f(), f()); var lo = new P2[6]; for (int k = 0; k < 6; k++) lo[k] = new P2(pts[k].X, f());
                    switch (rnd.Next(9))
                    {
                        case 0: c.Fill(f(), f(), f(), f(), Wh); break; case 1: c.Round(f(), f(), f(), f(), f(), Wh); break; case 2: c.Circle(f(), f(), f(), Wh); break; case 3: c.Line(f(), f(), f(), f(), Wh, 2); break;
                        case 4: c.Area(pts, 6, rnd.Next(2) == 0 ? lo : null, f(), Wh.Fade(.4f)); break; case 5: c.Path(pts, 6, Wh, 3, rnd.Next(2) == 0); break; case 6: c.Poly(pts, 6, Wh); break; case 7: c.RoundLine(f(), f(), f(), f(), f(), Wh, 2); break;
                        default: c.Clip(f(), f(), f(), f()); c.ClipRound(f(), f(), f(), f(), 8); c.Text("가나다 abc…", LagWatch.F.Sans(16, 700), f(), f(), Wh); c.Gradient(f(), f(), f(), f(), Wh, Wh, Wh, .5f); c.PopClip(IntPtr.Zero); c.Unclip(); break;
                    }
                }
                catch (Exception ex) { ok = false; Console.WriteLine(ex.Message + ex.StackTrace); break; }
            }
            Ok(ok, "아무 값 4000번"); c.Dispose();
        }

        static void ClipTest()
        {
            var c = Blank(64, 64); c.Clip(10, 10, 20, 20); c.Fill(0, 0, 64, 64, Wh); c.Unclip(); Ok(Math.Abs(Sum(c) - 400) < .01, "네모 자르기");
            c.Clear(Col.Hex(0)); c.Clip(10, 10, 30, 30); c.Clip(20, 20, 40, 40); c.Fill(0, 0, 64, 64, Wh); c.Unclip(); Ok(Math.Abs(Sum(c) - 400) < .01, "겹쳐 자르기"); c.Fill(0, 0, 64, 64, Wh); c.Unclip(); Ok(Math.Abs(Sum(c) - 900) < .01, "하나 풀면 앞의 것");
            c.Clear(Col.Hex(0)); var g = c.ClipRound(4, 4, 50, 40, 12); c.Fill(0, 0, 64, 64, Wh); c.PopClip(g); double cut = 2000 - (4 - Math.PI) * 144; Ok(Math.Abs(Sum(c) - cut) < 8, "둥글게 자르기 " + Sum(c).ToString("F1") + " / " + cut.ToString("F1"));
            c.Fill(0, 0, 64, 64, Wh); Ok(Math.Abs(Sum(c) - 4096) < .01, "풀면 전체");
            c.Clear(Col.Hex(0)); g = c.ClipRound(4, 4, 50, 40, 12); c.Round(0, 0, 64, 64, 30, Wh); c.PopClip(g); Ok(Sum(c) < cut + 8, "둥근 것도 잘림");
            c.Clear(Col.Hex(0)); c.PushOpacity(.5f); c.Fill(0, 0, 64, 64, Wh); c.PopClip(IntPtr.Zero); Ok(Math.Abs(G(c, 5, 5) - 128) <= 1, "통째로 흐리게"); c.Fill(0, 0, 64, 64, Wh); Ok(G(c, 5, 5) == 255, "풀면 원래대로");
            c.Clear(Col.Hex(0)); g = c.ClipRound(0, 0, 64, 64, 10); c.PushOpacity(.5f); c.Fill(0, 0, 64, 64, Wh); c.PopClip(IntPtr.Zero); c.Fill(0, 0, 64, 64, Wh); c.PopClip(g); Ok(G(c, 32, 32) == 255 && G(c, 0, 0) < 40, "자르기와 흐리기를 겹쳐 써도 짝이 맞음");
            c.Dispose();
        }

        static void CurveTest()
        {
            var c = Blank(220, 120); var p = new P2[12]; var q = new P2[12]; for (int i = 0; i < 12; i++) { p[i] = new P2(10 + i * 18, 30); q[i] = new P2(10 + i * 18, 80); }
            c.Area(p, 12, null, 100, Wh); Ok(Math.Abs(Sum(c) - 198 * 70) < 2, "평평한 면 넓이"); c.Clear(Col.Hex(0)); c.Area(p, 12, q, 0, Wh); Ok(Math.Abs(Sum(c) - 198 * 50) < 2, "두 줄 사이");
            for (int i = 0; i < 12; i++) p[i].Y = 30 + (i % 2) * 20; c.Clear(Col.Hex(0)); c.Area(p, 12, null, 100, Wh); Ok(Math.Abs(Sum(c) - 198 * 60) < 12, "굽은 면 넓이 " + Sum(c).ToString("F0"));
            bool hole = false; for (int x = 12; x < 206; x++) if (G(c, x, 95) < 250) hole = true; Ok(!hole, "굽은 면 안에 빈 줄 없음");
            c.Clear(Col.Hex(0)); c.Path(p, 12, Wh, 2, true); double s = Sum(c); Ok(s > 560 && s < 690, "굽은 줄 " + s.ToString("F0")); bool gap = false; for (int x = 12; x < 206; x++) { int best = 0; for (int y = 20; y < 60; y++) best = Math.Max(best, G(c, x, y)); if (best < 90) gap = true; } Ok(!gap, "굽은 줄이 끊기지 않음");
            // 세로 줄(물결)도 튀지 않음
            var v = new P2[30]; for (int i = 0; i < 30; i++) v[i] = new P2(100 + (float)Math.Sin(i * .4) * 3, 5 + i * 3.6f); c.Clear(Col.Hex(0)); c.Path(v, 30, Wh, 2, false); int lo = 999, hi = 0; for (int y = 0; y < 120; y++) for (int x = 0; x < 220; x++) if (G(c, x, y) > 20) { lo = Math.Min(lo, x); hi = Math.Max(hi, x); } Ok(lo >= 95 && hi <= 105, "세로 물결 " + lo + "~" + hi);
            // 오려 낸 별: 같은 방향으로 겹쳐도 한 번만 칠함
            c.Clear(Col.Hex(0)); c.Path(new[] { new P2(20, 20), new P2(60, 20), new P2(20.5f, 20.5f), new P2(60, 21) }, 4, Wh.Fade(.5f), 4, false); int mx = 0; for (int x = 0; x < 100; x++) for (int y = 0; y < 60; y++) mx = Math.Max(mx, G(c, x, y)); Ok(mx <= 130, "겹친 줄이 두 번 칠해지지 않음 " + mx);
            c.Dispose();
        }

        static void TextTest()
        {
            var c = Blank(400, 80); var st = LagWatch.F.Sans(20, 700); float w = c.Text("가상메모리 56%", st, 20, 40, Wh); int lo = 999, hi = 0; double ink = 0;
            for (int y = 0; y < 80; y++) for (int x = 0; x < 400; x++) if (G(c, x, y) > 30) { lo = Math.Min(lo, x); hi = Math.Max(hi, x); ink++; }
            Ok(w > 90 && w < 160 && lo >= 19 && hi <= 21 + w && ink > 300, "글자 폭과 자리 " + w.ToString("F1")); Ok(Math.Abs(c.Measure("가상메모리 56%", st) - w) < .01, "재는 폭과 그린 폭이 같음");
            Ok(Math.Abs(c.Measure("aaaa", st) - 4 * c.Measure("a", st)) < .1, "글자마다 폭이 더해짐"); Ok(c.Measure("", st) == 0 && c.Measure(null, st) == 0, "빈 글");
            float m = c.Text("아주 긴 프로그램 이름입니다", st, 0, 70, Wh, 0, 80); Ok(m <= 80.5f && m > 50, "넘치면 … " + m.ToString("F1")); Ok(Math.Abs(c.Text("짧음", st, 0, 70, Wh, 0, 80) - c.Measure("짧음", st)) < .01, "안 넘치면 그대로"); Ok(c.Text("가나다", st, 0, 70, Wh, 0, 2) == 0, "자리가 없으면 안 그림");
            c.Clear(Col.Hex(0)); c.Text("AAA", st, 200, 40, Wh, 2); lo = 999; hi = 0; for (int y = 0; y < 80; y++) for (int x = 0; x < 400; x++) if (G(c, x, y) > 30) { lo = Math.Min(lo, x); hi = Math.Max(hi, x); } Ok(hi <= 201 && hi >= 196, "오른쪽 맞춤 " + hi);
            foreach (var t in new[] { "日本語 中文", "→ ◀ ● …", "ß é ñ", "😀", "\t\n", new string('가', 600) }) { c.Text(t, st, 4, 40, Wh); c.Measure(t, st); } Ok(true, "다른 나라 글자와 긴 글");
            Ok(c.Measure("日本", st) > 25, "없는 글자는 다른 글꼴에서 " + c.Measure("日本", st).ToString("F1")); Ok(c.Measure("123", LagWatch.F.Din(34)) > 30, "숫자 글꼴");
            c.Clear(Col.Hex(0)); for (int i = 0; i < 4; i++) c.Text("l", st, 10 + i * 30 + i * .25f, 40, Wh); var ss = new double[4]; for (int i = 0; i < 4; i++) for (int y = 0; y < 80; y++) for (int x = i * 30; x < i * 30 + 30; x++) ss[i] += G(c, x, y); Ok(Math.Abs(ss[0] - ss[2]) < ss[0] * .25, "1/4 픽셀 자리마다 굵기가 비슷");
            c.Dispose(); int live = D2.Live; Ok(live > 0, "글꼴이 살아 있음"); D2.Shutdown(); Ok(D2.Live == 0 && Glyphs.Count == 0, "닫으면 글꼴도 놓음 " + D2.Live); c = Blank(60, 40); Ok(c.Text("다시", LagWatch.F.Sans(20, 700), 2, 30, Wh) > 10 && Sum(c) > 20, "다시 열면 다시 그림"); c.Dispose(); D2.Shutdown();
        }

        static void GlyphCapTest()
        {
            var c = Blank(300, 60); int top = 0; for (int i = 0; i < 9000; i++) { c.Text(((char)(0xAC00 + i)).ToString(), LagWatch.F.Sans(20 + i % 3 * 8, 700), 5 + i % 7 * .3f, 40, Wh); top = Math.Max(top, Glyphs.Bytes); }
            Ok(top <= Glyphs.Cap + 8192, "글자 조각은 정한 만큼만 " + top / 1024 + "KB"); c.Relax(); Ok(Glyphs.Count == 0, "놓기"); c.Dispose(); D2.Shutdown();
        }

        static void SysTest()
        {
            Ok(Sys.Minutes("League of Legends 22분") == 22 && Sys.Minutes("이터널 리턴 1시간 4분") == 64 && Sys.Minutes("이터널 리턴 2시간") == 120 && Sys.Minutes("이터널 리턴") == 0 && Sys.Minutes("분") == 0 && Sys.Minutes("") == 0 && Sys.Minutes("시간") == 0 && Sys.Minutes("A 999999999999분") == 0, "분 읽기");
            var v = Sys.Quoted("\"libraryfolders\"\n{\n \"0\"\n {\n \"path\"\t\t\"C:\\\\Steam\"\n \"label\" \"\"\n }\n \"1\" { \"path\" \"D:\\\\Games\" \"paths\" \"no\" } \"path\" }", "path"); Ok(v.Count == 2 && v[0] == @"C:\\Steam" && v[1] == @"D:\\Games", "따옴표 값 " + v.Count); Ok(Sys.Quoted("\"a\"\"b\"", "a").Count == 0 && Sys.Quoted("\"a\" \"", "a").Count == 0 && Sys.Quoted("", "a").Count == 0, "틀린 꼴");
            var f = Boost.Folders("{\"associated_client\":{\"C:/Riot Games/League of Legends/\": \"x\",\"D:/Games/League of Legends\" : \"y\"},\"name\":\"League of Legends\"}"); Ok(f.Count == 2 && f[0] == @"C:\Riot Games\League of Legends" && f[1] == @"D:\Games\League of Legends", "롤 폴더 " + f.Count);
            string d = Sys.Description(Environment.GetFolderPath(Environment.SpecialFolder.System) + @"\notepad.exe"), d2 = System.Diagnostics.FileVersionInfo.GetVersionInfo(Environment.GetFolderPath(Environment.SpecialFolder.System) + @"\notepad.exe").FileDescription; Ok(d == d2 && d.Length > 0, "파일 설명 " + d); Ok(Sys.Description(@"C:\없는파일.exe") == null, "없는 파일");
            int same = 0, all = 0; foreach (var p in System.Diagnostics.Process.GetProcesses()) { try { string n = p.MainModule.FileName; all++; string a = Sys.Description(n), b = System.Diagnostics.FileVersionInfo.GetVersionInfo(n).FileDescription; if ((a ?? "").Trim() == (b ?? "").Trim() || string.IsNullOrEmpty(b)) same++; else Console.WriteLine("  다름 " + n + " [" + a + "] [" + b + "]"); } catch (Exception) { } p.Dispose(); if (all > 60) break; } Ok(all > 10 && same == all, "켜진 프로그램의 설명이 닷넷과 같음 " + same + "/" + all);
            Ok(Sys.Running("explorer.exe") && Sys.Running("EXPLORER.EXE") && !Sys.Running("explorer") && !Sys.Running("없는것.exe"), "켜져 있는지"); Ok(Sys.GetCurrentProcessId() == System.Diagnostics.Process.GetCurrentProcess().Id, "내 번호");
            bool threw = false; try { Sys.Open(@"C:\없는파일.exe"); } catch (Exception ex) { threw = ex.Message.Length > 3; } Ok(threw, "없는 것을 켜면 까닭을 말함"); Ok(Sys.Open("cmd.exe", "/c exit 7", null, null, 8000, true) == 7, "끝난 값");
            var sw = Stopwatch.StartNew(); System.Threading.Thread.Sleep(60); Ok(sw.ElapsedMilliseconds >= 55 && sw.ElapsedMilliseconds < 400 && Math.Abs(sw.Elapsed.TotalMilliseconds - sw.ElapsedMilliseconds) < 30, "시간 재기"); sw.Restart(); Ok(sw.ElapsedMilliseconds < 30, "다시 재기");
            Ok(Self.Commit > 5 && Self.Ram > 5 && Self.Cpu > 0, "내 사용량");
            string z = Path.Combine(Tmp, "closeup.yaml"); File.WriteAllText(z, "a:\r\n    closeLeagueClientDuringGame: false\r\nb: 1\r\n"); var k = new ClientKnob(null, Tmp, () => false) { FilePath = z }; k.Read(); Ok(k.Now == 0, "롤 설정 읽기 " + k.Now); Ok(k.Set(1) == null && File.ReadAllText(z) == "a:\r\n    closeLeagueClientDuringGame: true\r\nb: 1\r\n", "그 값만 바뀜"); Ok(k.Set(0) == null && File.ReadAllText(z) == "a:\r\n    closeLeagueClientDuringGame: false\r\nb: 1\r\n", "되돌림");
            File.WriteAllText(z, "closeLeagueClientDuringGame: false\ncloseLeagueClientDuringGame: true\n"); k.Read(); Ok(k.Now == -1 && k.Set(1) != null, "두 줄이면 건드리지 않음"); File.WriteAllText(z, "closeLeagueClientDuringGame: maybe\n"); k.Read(); Ok(k.Now == -1, "모르는 값");
        }

        static void ClockTest()
        {
            var c = new Clock(); double lim, r = c.Read(out lim); Ok(r == 0, "첫 값은 비교할 것이 없음"); System.Threading.Thread.Sleep(400); r = c.Read(out lim); Ok(r > .2 && r < 3 && lim > 10 && lim <= 100, "CPU 속도 " + r.ToString("F2") + " 제한 " + lim); c.Close();
            for (int i = 0; i < 60; i++) { var x = new Clock(); x.Read(out lim); x.Close(); } Ok(true, "여닫기");
        }

        static void LevelTest()
        {
            Ok(Theme.Lv(0) == 0 && Theme.Lv(69.9) == 0 && Theme.Lv(70) == 1 && Theme.Lv(89.9) == 1 && Theme.Lv(90) == 2 && Theme.Lv(100) == 2, "사용률 70% 주황, 90% 빨강");
            Ok(Theme.Share(14.9) == 0 && Theme.Share(15) == 1 && Theme.Share(29.9) == 1 && Theme.Share(30) == 2, "프로그램은 전체의 15%, 30%");
            foreach (var t in new[] { Theme.Night, Theme.Day })
            {
                var n = t.W[1]; Ok(t.LvFill(50, n).Equals(n) && t.LvFill(75, n).Equals(t.Warn) && t.LvFill(95, n).Equals(t.Hot) && t.LvInk(75, t.Fg).Equals(t.WarnInk) && t.ALv(95, t.AFill).Equals(t.ARed), "단계 색 " + (t.Dark ? "어두움" : "밝음"));
                Ok(t.WarnInk.Lum > 0 && Math.Abs(t.WarnInk.Lum - t.Bg.Lum) > .15f && Math.Abs(t.HotInk.Lum - t.Bg.Lum) > .1f, "단계 글자가 바탕과 구분됨 " + (t.Dark ? "어두움" : "밝음"));
            }
        }
        static void SetTest()
        {
            var h = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a", "B" }; Ok(h.Count == 2 && h.Contains("A") && !h.Add("b") && h.Add("c") && h.Remove("C") && h.Count == 2, "집합"); h.RemoveWhere(x => x == "a"); Ok(h.Count == 1 && h.Contains("B"), "골라 빼기"); h.Clear(); Ok(h.Count == 0, "비우기");
        }

        static void LoopTest() { int n = 0; Loop.Post(() => n++); Loop.Post(() => { n++; Loop.Post(() => n += 10); }); Loop.Drain(); Ok(n == 2, "시킨 일은 차례로"); Loop.Drain(); Ok(n == 12, "하는 중에 시킨 일은 다음에"); Loop.Drain(); Ok(n == 12, "두 번 하지 않음"); }
    }
}
