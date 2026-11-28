using System;
using System.Collections.Generic;

namespace LagWatch
{
    internal static partial class Tests
    {
        // ---- 방송 기록 (1.7 1주차) ----
        static void StreamTests()
        {
            // JSON
            var o = Json.Parse("{\"a\":1.5,\"b\":\"x\\\"y\\u0041\",\"c\":[true,false,null],\"d\":{\"e\":-2e3}}");
            Ok(Json.Num(o, "a") == 1.5, "json 숫자");
            Eq(Json.Str(o, "b"), "x\"yA", "json 문자열 이스케이프");
            var c = Json.Arr(o, "c"); Ok(c != null && c.Count == 3 && (bool)c[0] && !(bool)c[1] && c[2] == null, "json 배열");
            Ok(Json.Num(Json.Obj(o, "d"), "e") == -2000, "json 지수");
            Eq(Json.Quote("a\"b\\c\n"), "\"a\\\"b\\\\c\\u000a\"", "json 따옴표");
            bool threw = false; try { Json.Parse("{\"a\":1"); } catch (FormatException) { threw = true; } Ok(threw, "json 덜 닫힘");
            Ok(Json.Num(Json.Parse("{\"big\":1234567890123}"), "big") == 1234567890123, "json 큰 정수");

            // OBS 설정 파일
            var cfg = ObsConfig.Parse("{\"alerts_enabled\":false,\"auth_required\":true,\"first_load\":false,\"server_enabled\":true,\"server_password\":\"p\\\"w\",\"server_port\":4466}");
            Ok(cfg.Found && cfg.Enabled && cfg.Auth && cfg.Port == 4466 && cfg.Password == "p\"w", "obs 설정 읽기");
            var off = ObsConfig.Parse("{\"server_enabled\":false}"); Ok(!off.Enabled && off.Port == 4455, "obs 설정 기본 포트");

            // 인증 문자열: protocol.md 순서대로 계산한 값이 늘 같은지(11/27 실제 OBS 5.7.4 접속 성공한 계산과 같은 식)
            Eq(Obs.Auth("supersecretpassword", "lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=", "+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY="), "1Ct943GAT+6YQUUX47Ia/ncufilbe6+oD6lY+5kaCu4=", "obs 인증");

            // 응답 → 표본
            var st = Json.Parse("{\"activeFps\":60.0,\"averageFrameRenderTime\":0.23,\"cpuUsage\":0.68,\"outputSkippedFrames\":3,\"outputTotalFrames\":100,\"renderSkippedFrames\":1,\"renderTotalFrames\":200}");
            var ss = Json.Parse("{\"outputActive\":true,\"outputBytes\":46103389,\"outputCongestion\":1.0,\"outputReconnecting\":false,\"outputSkippedFrames\":2486,\"outputTotalFrames\":4784}");
            var s = Obs.Stats(st, ss, null);
            Ok(s.Streaming && !s.Recording && s.RenderSkipped == 1 && s.EncSkipped == 3 && s.NetSkipped == 2486 && s.Bytes == 46103389 && s.Congestion == 1.0 && s.Fps == 60, "obs 표본");
            var none = Obs.Stats(null, null, null); Ok(!none.Streaming && none.Bytes == 0, "obs 빈 응답");

            // 인코더 종류
            Ok(new ObsEncoder { Encoder = "nvenc" }.Hardware && new ObsEncoder { Encoder = "jim_nvenc" }.Hardware && !new ObsEncoder { Encoder = "obs_x264" }.Hardware && !new ObsEncoder { Encoder = "x264" }.Hardware, "인코더 하드웨어 판정");

            // GPU 인스턴스 이름
            int pid; string key, type;
            Ok(Gpu.Split("pid_13780_luid_0x00000000_0x0000D6F1_phys_0_eng_3_engtype_VideoEncode", out pid, out key, out type) && pid == 13780 && key == "0x00000000_0x0000D6F1_phys_0_eng_3" && type == "VideoEncode", "gpu 이름 나누기");
            Ok(!Gpu.Split("_Total", out pid, out key, out type), "gpu 이상한 이름");
            Ok(Gpu.IsCompute("3D") && Gpu.IsCompute("Cuda") && Gpu.IsCompute("Compute_1") && Gpu.IsCompute("Graphics_1") && !Gpu.IsCompute("Copy") && !Gpu.IsCompute("VideoEncode"), "gpu 연산 엔진");
            Ok(Gpu.IsEncode("VideoEncode") && Gpu.IsEncode("VideoCodec") && !Gpu.IsEncode("VideoDecode"), "gpu 인코더 엔진");

            // 이 PC에서 카운터가 실제로 열리는지 (관리자 아니어도 됨)
            using (var g = new Gpu()) { g.Start(); var x = g.Sample(0, 0); Ok(g.Ok && x.Instances > 0 && x.Busy >= 0 && x.Busy <= 100, "gpu 카운터 " + x.Instances); }
            using (var n = new Net()) { n.Start(); var y = n.Sample(); Ok(y.UpKbps >= 0 && y.Retrans >= 0, "네트워크 카운터"); }

            Ok(Broadcast.Head.Split('\t').Length == 28, "방송 기록 열 수");
            CauseTests();
        }

        // ---- 원인 판정 R1~R7 ----
        static readonly DateTime T0 = new DateTime(2026, 11, 27, 20, 0, 0);
        static Sec Calm(int i) { return new Sec { At = T0.AddSeconds(i), Busy = 30, GpuEnc = 20, Cpu = 20, ObsCpu = 1, FreeMB = 8000, RenderMs = 2, Fps = 60, Kbps = 6000, Top = "Chrome", TopCores = .2f, Obs3D = 3, Game3D = 25 }; }

        /// <summary>평소 5초 → 끊김 n초(bad로 바꿈) → 평소 4초. 끊김 목록을 돌려준다.</summary>
        static List<Drop> Run(ObsEncoder enc, int n, Func<Sec, Sec> bad, out int told)
        {
            var sd = new StreamDoctor { Enc = enc }; int k = 0; sd.Decided = d => k++;
            int t = 0; for (int i = 0; i < 5; i++) sd.Add(Calm(t++)); for (int i = 0; i < n; i++) sd.Add(bad(Calm(t++))); for (int i = 0; i < 4; i++) sd.Add(Calm(t++));
            sd.Finish(); told = k; return sd.Copy();
        }
        static string Cause(ObsEncoder enc, Func<Sec, Sec> bad) { int k; var l = Run(enc, 4, bad, out k); return l.Count == 1 ? l[0].Cause : "개수 " + l.Count; }

        static void CauseTests()
        {
            var nv = new ObsEncoder { Encoder = "nvenc", Kbps = 6000 }; var x264 = new ObsEncoder { Encoder = "x264", Kbps = 6000, Preset = "veryfast" };
            Eq(Cause(nv, s => { s.Net = 30; s.Kbps = 2500; s.Cong = 1; return s; }), "r5", "R5 업로드 부족");
            Eq(Cause(nv, s => { s.Net = 30; s.Kbps = 6000; return s; }), "r5", "네트워크 드롭만이면 송출량과 상관없이 R5 (11/27 실제 송출 시험에서 첫 끊김이 모름으로 나와서 바꿈)");
            Eq(Cause(nv, s => { s.Net = 30; s.Kbps = 2500; s.Busy = 99; return s; }), "r5", "네트워크 드롭은 GPU가 꽉 차도 R5만");
            Eq(Cause(nv, s => { s.Render = 10; s.Busy = 99; s.Game3D = 80; return s; }), "r1", "R1 렌더링 지연 + GPU 꽉 참");
            Eq(Cause(nv, s => { s.Enc = 10; s.Busy = 99; s.GpuEnc = 30; return s; }), "r1", "R1 하드웨어 인코더의 인코딩 지연 + GPU 꽉 참 (4090 사례)");
            Eq(Cause(x264, s => { s.Enc = 10; s.Busy = 99; s.ObsCpu = 5; return s; }), "r3", "x264 인코딩 지연은 GPU가 꽉 차도 R3 (CPU 인코더)");
            Eq(Cause(nv, s => { s.Render = 10; s.RenderMs = 14; return s; }), "r2", "R2 장면이 무거움");
            Eq(Cause(nv, s => { s.Render = 10; s.RenderMs = 14; s.Busy = 90; return s; }), "", "R2는 GPU 여유가 있을 때만");
            Eq(Cause(x264, s => { s.Enc = 10; s.ObsCpu = 40; return s; }), "r3", "R3 x264 과부하");
            Eq(Cause(x264, s => { s.Enc = 10; s.Cpu = 95; return s; }), "r3", "R3 전체 CPU 90% 이상");
            Eq(Cause(x264, s => { s.Enc = 10; s.Cpu = 25; s.ObsCpu = 20; return s; }), "r3", "R3 코어 일부만 바빠도 (11/27 재현 시험 설계 때 바꿈)");
            Eq(Cause(nv, s => { s.Enc = 10; s.GpuEnc = 95; return s; }), "r4", "R4 GPU 인코더 꽉 참");
            Eq(Cause(nv, s => { s.Render = 10; s.Cpu = 92; s.Top = "Updater"; s.TopCores = 3; return s; }), "r6", "R6 다른 프로그램 CPU");
            Eq(Cause(nv, s => { s.Render = 10; s.FreeMB = 700; s.Busy = 99; return s; }), "r7", "R7 램 부족이 GPU보다 먼저");
            Eq(Cause(nv, s => { s.Net = 5; s.Render = 1; s.Kbps = 2500; return s; }), "", "렌더링 지연이 같이 있으면 R5로 안 감");

            // 11/28 재현 시험에서 나온 모양
            int bi = 0; Eq(Cause(nv, s => { s.Enc = 20; s.Busy = 99; s.GpuEnc = ++bi == 4 ? 95 : 9; return s; }), "r1", "GPU 99%인데 인코더가 한 번만 95%로 튐 → R1 (중앙값)");
            Eq(Cause(nv, s => { s.Render = 30; s.RenderMs = 400; s.Busy = 3; s.Cpu = 12; s.Top = "Python"; s.TopCores = 2; return s; }), "r6", "R6 GPU는 노는데 OBS 렌더가 프레임 간격 3배 넘음 = CPU 못 받음");
            Eq(Cause(nv, s => { s.Render = 30; s.RenderMs = 400; s.Busy = 3; s.Cpu = 12; return s; }), "r2", "범인 프로그램이 없으면 R6 아님");
            StreamDoctor.Lps = 16;
            Eq(Cause(nv, s => { s.Render = 5; s.Enc = 5; s.RenderMs = 1; s.Fps = 56; s.Busy = 3; s.GpuEnc = 25; s.Cpu = 22; s.Top = "Python"; s.TopCores = 1.8f; return s; }), "r6", "S6 모양: GPU는 노는데 렌더·인코딩 지연, 다른 프로그램 1.8코어");
            Eq(Cause(x264, s => { s.Render = 3; s.RenderMs = 4; s.Fps = 57; s.Busy = 3; s.ObsCpu = 12; s.Cpu = 30; s.Top = "mysqld"; s.TopCores = 1.4f; return s; }), "r3", "S3 모양: x264 OBS가 1.9코어, 렌더링 지연");
            Eq(Cause(x264, s => { s.Render = 3; s.RenderMs = 4; s.Busy = 3; s.ObsCpu = 3; s.Cpu = 30; return s; }), "", "x264라도 OBS가 CPU를 별로 안 쓰고 범인도 없으면 모름");
            StreamDoctor.Lps = Environment.ProcessorCount;

            // 근거와 같이 보임
            int k; var l = Run(nv, 4, s => { s.Render = 10; s.FreeMB = 700; s.Busy = 99; return s; }, out k);
            Ok(l.Count == 1 && l[0].Also.Contains("r1") && l[0].Why.Contains("0.7"), "R7 근거와 같이 보임 " + (l.Count > 0 ? l[0].Why + " / " + string.Join(",", l[0].Also.ToArray()) : ""));
            Ok(k == 1, "알림은 끊김 하나에 한 번 " + k);
            l = Run(nv, 4, s => { s.Net = 30; s.Kbps = 2500; return s; }, out k); Ok(l.Count == 1 && l[0].Why.Contains("4,444") && l[0].Why.Contains("6,000") && l[0].Net == 120, "R5 근거 " + (l.Count > 0 ? l[0].Why : ""));

            // 묶기: 3초 안쪽은 한 덩어리, 그보다 멀면 둘
            var sd = new StreamDoctor { Enc = nv }; int t = 0;
            Func<Sec, Sec> net = s => { s.Net = 10; s.Kbps = 2000; return s; };
            sd.Add(net(Calm(t++))); sd.Add(Calm(t++)); sd.Add(Calm(t++)); sd.Add(net(Calm(t++))); // 3초 뒤 → 그 사이 2초가 지나 이미 닫힘
            for (int i = 0; i < 6; i++) sd.Add(Calm(t++)); sd.Add(net(Calm(t++))); for (int i = 0; i < 3; i++) sd.Add(Calm(t++)); sd.Finish();
            Ok(sd.Copy().Count == 3 && sd.NetTotal == 30, "끊김 묶기 " + sd.Copy().Count);
            sd = new StreamDoctor { Enc = nv }; t = 0; sd.Add(net(Calm(t++))); sd.Add(net(Calm(t++))); sd.Add(Calm(t++)); sd.Add(net(Calm(t++))); for (int i = 0; i < 3; i++) sd.Add(Calm(t++)); sd.Finish();
            Ok(sd.Copy().Count == 1 && sd.Copy()[0].Net == 30, "1초 쉬고 다시 끊기면 한 덩어리");
            // 길게 이어지는 끊김은 10초째에 먼저 알린다
            sd = new StreamDoctor { Enc = nv }; int early = 0; sd.Decided = d => early++; t = 0; for (int i = 0; i < 12; i++) sd.Add(net(Calm(t++)));
            Ok(early == 1 && sd.Copy()[0].Open && sd.Copy()[0].Cause == "r5", "긴 끊김 중간 판정");
            Eq(sd.Main(), "r5", "대부분 원인"); sd.Finish(); Eq(sd.Causes(), "r5:1", "원인 요약");
            // 화면용 최근 기록
            var buf = new Sec[600]; int n = sd.Recent(buf); Ok(n == 12 && buf[0].At == T0 && buf[11].Net == 10, "최근 기록 순서");
            Eq(StreamDoctor.Name("r5"), Lang.Tx("업로드 부족", "Upload too slow"), "원인 이름");

            // 기록 파일을 다시 읽으면 같은 판정 (렉 감시를 다시 켜도 지난 방송 리포트)
            var f = System.IO.Path.Combine(Tmp, "20261127_200000.tsv"); var sb = new System.Text.StringBuilder();
            sb.Append("# 렉 감시 방송 기록 · 시작 2026-11-27 20:00:00\n# obs 32.2.2 · websocket 5.7.4 · mode Simple · encoder nvenc · kbps 6000 · preset p5\n# hags - · gamemode 0 · obsElevated 1\n").Append(Broadcast.Head).Append('\n');
            for (int i = 1; i <= 20; i++)
            {
                bool bad = i >= 8 && i < 12;
                sb.Append(i).Append(".0\t20:00:").Append(i.ToString("00")).Append("\t1\t0\t0\t0\t").Append(bad ? 30 : 0).Append("\t60\t60\t60\t0.2\t60\t0.7\t").Append(bad ? 1 : 0).Append('\t').Append(bad ? 2500 : 6000)
                  .Append("\t27\t3\t0\t25\t30\t27\t500\t0\t15\t9000\t\tChrome\t0.3\n");
            }
            sb.Append("# end ...\n"); System.IO.File.WriteAllText(f, sb.ToString());
            var back = Broadcast.Load(f);
            Ok(back != null && back.Enc.Encoder == "nvenc" && back.Enc.Kbps == 6000 && back.Seconds == 20 && back.NetTotal == 120, "기록 다시 읽기");
            Ok(back != null && back.Copy().Count == 1 && back.Copy()[0].Cause == "r5" && back.Start == new DateTime(2026, 11, 27, 20, 0, 0), "다시 읽어도 같은 판정");
            Ok(Broadcast.LoadLatest(Tmp) != null, "가장 최근 기록 찾기");
        }
    }
}
