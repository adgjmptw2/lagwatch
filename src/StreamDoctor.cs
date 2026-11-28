using System;
using System.Collections.Generic;
using System.Globalization;
using static LagWatch.Lang;

namespace LagWatch
{
    /// <summary>방송 1초. 버린 프레임 수는 측정, 나머지도 측정값이다. 원인 판정만 추정.</summary>
    internal struct Sec
    {
        public DateTime At;
        public int Render, Enc, Net;                 // 그 1초에 버린 프레임: 렌더링 지연, 인코딩 지연, 네트워크
        public float RenderMs, Fps, ObsCpu, Cong, Kbps, ObsEnc, Obs3D, ObsCuda, Game3D, Busy, GpuEnc, Up, Retrans, Cpu, FreeMB, GameFps, TopCores;
        public string Top;                           // 게임·OBS를 빼고 CPU를 가장 많이 쓰는 프로그램(감시 주기라 몇 초 늦을 수 있음)
        public int Frames { get { return Render + Enc + Net; } }
    }

    /// <summary>끊김 하나: 버린 프레임이 3초 안쪽으로 이어지면 한 덩어리</summary>
    internal sealed class Drop
    {
        public DateTime From, To;
        public int Render, Enc, Net;
        public string Cause = "";                    // r1~r7, 빈 값 = 모름
        public string Why = "";                      // 근거 숫자
        public readonly List<string> Also = new List<string>();
        public bool Open = true, Told;
        public int Frames { get { return Render + Enc + Net; } }
        /// <summary>가장 많이 버린 종류: 0 렌더링, 1 인코딩, 2 네트워크</summary>
        public int Kind { get { return Net >= Render && Net >= Enc ? 2 : Enc >= Render ? 1 : 0; } }
    }

    /// <summary>1초 기록을 받아 끊김을 묶고 원인을 고른다. 계획서의 R1~R7, 시작값은 3주차 재현 시험으로 조정.</summary>
    internal sealed class StreamDoctor
    {
        public const int Keep = 900;                 // 화면용 최근 15분
        public const double Merge = 3, After = 2;   // 3초 안쪽 끊김은 한 덩어리, 마지막 끊김 2초 뒤 판정
        public const float GpuFull = 95, GpuRoom = 85, EncFull = 90, CpuFull = 90, CpuBusy = 85, ObsCpuHigh = 30, RamLow = 1024, TopMin = 1, CongHigh = .7f, KbpsShort = .8f, RenderShare = .7f, Starved = 3, ObsCoresHigh = 1.5f;
        /// <summary>논리 코어 수. OBS CPU(전체 대비 %)를 코어 수로 바꿀 때</summary>
        public static int Lps = Environment.ProcessorCount;

        readonly Sec[] ring = new Sec[Keep]; int count, head;
        public readonly List<Drop> Drops = new List<Drop>();
        public readonly object Gate = new object();
        public ObsEncoder Enc = new ObsEncoder();
        public DateTime Start = DateTime.Now, End;
        public long Seconds, RenderTotal, EncTotal, NetTotal, Sent;
        /// <summary>원인을 처음 정했을 때(알림용). 판정 스레드에서 부른다.</summary>
        public Action<Drop> Decided;
        /// <summary>끊김이 닫혀 원인이 확정됐을 때(기록용)</summary>
        public Action<Drop> Closed;

        public void Add(Sec s)
        {
            Drop told = null;
            lock (Gate)
            {
                ring[head] = s; head = (head + 1) % Keep; if (count < Keep) count++;
                Seconds++; RenderTotal += s.Render; EncTotal += s.Enc; NetTotal += s.Net;
                Drop cur = Drops.Count > 0 && Drops[Drops.Count - 1].Open ? Drops[Drops.Count - 1] : null;
                if (s.Frames > 0)
                {
                    if (cur == null || (s.At - cur.To).TotalSeconds > Merge) { if (cur != null) Close(cur, ref told); cur = new Drop { From = s.At, To = s.At }; Drops.Add(cur); }
                    cur.To = s.At; cur.Render += s.Render; cur.Enc += s.Enc; cur.Net += s.Net;
                }
                if (cur != null && cur.Open)
                {
                    double since = (s.At - cur.To).TotalSeconds, len = (s.At - cur.From).TotalSeconds;
                    if (since >= After) Close(cur, ref told);
                    else if (len >= 10 && (int)len % 10 == 0) { Judge(cur); if (!cur.Told) { cur.Told = true; told = cur; } } // 길게 이어지면 10초마다 다시 본다
                }
            }
            if (told != null && Decided != null) Decided(told);
        }

        void Close(Drop d, ref Drop told) { Judge(d); d.Open = false; if (!d.Told) { d.Told = true; told = d; } if (Closed != null) Closed(d); }

        /// <summary>끝: 열린 끊김을 닫는다</summary>
        public void Finish() { Drop told = null; lock (Gate) { End = DateTime.Now; if (Drops.Count > 0 && Drops[Drops.Count - 1].Open) Close(Drops[Drops.Count - 1], ref told); } if (told != null && Decided != null) Decided(told); }

        /// <summary>최근 n초를 오래된 것부터 dst에. 돌려주는 값은 개수.</summary>
        public int Recent(Sec[] dst)
        {
            lock (Gate)
            {
                int n = Math.Min(count, dst.Length), start = (head - n + Keep) % Keep;
                for (int i = 0; i < n; i++) dst[i] = ring[(start + i) % Keep];
                return n;
            }
        }

        public List<Drop> Copy() { lock (Gate) return new List<Drop>(Drops); }

        IEnumerable<Sec> Window(DateTime from, DateTime to)
        {
            for (int i = 0; i < count; i++) { var s = ring[(head - count + i + Keep) % Keep]; if (s.At >= from && s.At <= to) yield return s; }
        }

        /// <summary>원인 고르기. 끊김 종류로 후보를 먼저 좁힌다(다른 종류의 원인으로 잘못 가지 않게).</summary>
        public void Judge(Drop d)
        {
            // 사용률은 끊긴 초들의 중앙값. 최댓값을 쓰면 구간 끝에서 1초 튄 값에 끌려간다(11/28 재현 시험: GPU 99%인데 인코더가 한 번 95%로 튀어 "GPU 인코더"로 오판)
            var lb = new List<float>(); var le = new List<float>(); var lc = new List<float>(); var lo = new List<float>(); var lr = new List<float>(); var lg = new List<float>(); var l3 = new List<float>();
            float cong = 0, free = float.MaxValue, topC = 0, fpsSum = 0; int fpsN = 0; string top = null; bool netClean = true;
            foreach (var s in Window(d.From.AddSeconds(-2), d.To.AddSeconds(2)))
            {
                cong = Math.Max(cong, s.Cong);
                if (s.FreeMB > 0) free = Math.Min(free, s.FreeMB);
                if (s.Fps > 0) { fpsSum += s.Fps; fpsN++; }
                if (s.Top != null && s.TopCores > topC) { topC = s.TopCores; top = s.Top; }
                if (s.Net > 0 && (s.Render > 0 || s.Enc > 0)) netClean = false;
                if (s.Frames == 0) continue; // 사용률은 끊긴 초만
                lb.Add(s.Busy); le.Add(s.GpuEnc); if (!float.IsNaN(s.Cpu)) lc.Add(s.Cpu); lo.Add(s.ObsCpu); lr.Add(s.RenderMs); lg.Add(s.Game3D); l3.Add(s.Obs3D);
            }
            float busy = Med(lb), gpuEnc = Med(le), cpu = Med(lc), obsCpu = Med(lo), rms = Med(lr), game3d = Med(lg), obs3d = Med(l3);
            float kbps = 0; int kn = 0; foreach (var s in Window(d.To.AddSeconds(-9.5), d.To)) { kbps += s.Kbps; kn++; } kbps = kn > 0 ? kbps / kn : 0; // 혼잡하면 송출량이 몇 초씩 몰려서 올라서 10초 평균
            float fps = fpsN > 0 ? fpsSum / fpsN : 0, frameMs = fps > 0 ? 1000 / fps : 0;
            bool rl = d.Render > 0, el = d.Enc > 0, nl = d.Net > 0, hw = Enc.Hardware, sw = Enc.Encoder.Length > 0 && !hw;
            // GPU는 놀고 OBS 렌더 시간도 정상인데 지연 = OBS가 제때 CPU를 못 받음 (11/28 재현 시험 S3·S6)
            bool sched = busy < GpuRoom && gpuEnc < EncFull && (frameMs <= 0 || rms < RenderShare * frameMs);
            float obsCores = obsCpu / 100 * Lps;
            var ok = new Dictionary<string, bool>
            {
                { "r1", (rl || (el && hw)) && busy >= GpuFull },
                { "r2", rl && frameMs > 0 && rms >= RenderShare * frameMs && busy < GpuRoom },
                { "r3", sw && (el || (rl && sched && obsCores >= ObsCoresHigh)) }, // x264는 CPU로만 인코딩한다. 인코딩 지연이거나, OBS가 x264로 CPU를 다 써서 렌더도 밀림
                { "r4", el && gpuEnc >= EncFull },
                { "r5", nl && netClean }, // OBS는 보낼 데이터가 밀릴 때만 네트워크 드롭을 낸다
                { "r6", (rl || el) && topC >= TopMin && (cpu >= CpuBusy || sched || (frameMs > 0 && rms >= Starved * frameMs && busy < GpuRoom)) }, // PC 전체가 바쁘거나, GPU가 노는데 지연 = OBS가 CPU 차례를 못 받음
                { "r7", (rl || el) && free < RamLow },
            };
            string[] order = d.Kind == 0 ? new[] { "r7", "r1", "r3", "r6", "r2" } : d.Kind == 1 ? new[] { "r4", "r3", "r1", "r7", "r6" } : new[] { "r5" };
            d.Cause = ""; d.Also.Clear();
            foreach (var k in order) if (ok[k]) { d.Cause = k; break; }
            foreach (var kv in ok) if (kv.Value && kv.Key != d.Cause && !d.Also.Contains(kv.Key)) d.Also.Add(kv.Key);
            var ic = CultureInfo.InvariantCulture;
            switch (d.Cause)
            {
                case "r1": d.Why = Tx("GPU {0}%", "GPU {0}%", busy.ToString("0", ic)) + (game3d > 0 ? Tx(", 게임 {0}%", ", game {0}%", game3d.ToString("0", ic)) : "") + Tx(", OBS {0}%", ", OBS {0}%", obs3d.ToString("0", ic)); break;
                case "r2": d.Why = Tx("렌더 {0}ms / 프레임 {1}ms, GPU {2}%", "render {0}ms / frame {1}ms, GPU {2}%", rms.ToString("0.0", ic), frameMs.ToString("0.0", ic), busy.ToString("0", ic)); break;
                case "r3": d.Why = Tx("OBS {0}코어, 전체 CPU {1}%, {2} {3}", "OBS {0} cores, total CPU {1}%, {2} {3}", obsCores.ToString("0.0", ic), cpu.ToString("0", ic), Enc.Encoder, Enc.Preset).Trim(); break;
                case "r4": d.Why = Tx("영상 인코더 {0}%", "video encoder {0}%", gpuEnc.ToString("0", ic)); break;
                case "r5": d.Why = (Enc.Kbps > 0 ? Tx("송출 {0} / 설정 {1}kbps", "sent {0} / set {1} kbps", kbps.ToString("#,0", ic), Enc.Kbps.ToString("#,0", ic)) : Tx("송출 {0}kbps", "sent {0} kbps", kbps.ToString("#,0", ic))) + (cong >= CongHigh ? Tx(", 혼잡 {0}", ", congestion {0}", cong.ToString("0.0", ic)) : ""); break;
                case "r6": d.Why = Tx("{0} {1}코어, 전체 CPU {2}%", "{0} {1} cores, total CPU {2}%", top, topC.ToString("0.0", ic), cpu.ToString("0", ic)); break;
                case "r7": d.Why = Tx("남은 램 {0}GB", "free RAM {0} GB", (free / 1024).ToString("0.0", ic)); break;
                default: d.Why = Tx("GPU {0}%, CPU {1}%", "GPU {0}%, CPU {1}%", busy.ToString("0", ic), cpu.ToString("0", ic)); break;
            }
        }

        static float Med(List<float> l) { if (l.Count == 0) return 0; l.Sort(); return l[l.Count / 2]; }

        public static string Name(string k)
        {
            switch (k)
            {
                case "r1": return Tx("GPU가 꽉 참", "GPU maxed out");
                case "r2": return Tx("OBS 장면이 무거움", "Heavy OBS scene");
                case "r3": return Tx("CPU 인코더 과부하", "CPU encoder overloaded");
                case "r4": return Tx("GPU 인코더가 꽉 참", "GPU encoder maxed out");
                case "r5": return Tx("업로드 부족", "Upload too slow");
                case "r6": return Tx("다른 프로그램이 CPU를 가져감", "Another app took the CPU");
                case "r7": return Tx("램 부족", "Low RAM");
                default: return Tx("모름", "Unknown");
            }
        }

        public static string Fix(string k)
        {
            switch (k)
            {
                case "r1": return Tx("게임 fps를 주사율보다 5~10 낮게 제한", "Cap game fps 5–10 below refresh rate");
                case "r2": return Tx("소스·필터 줄이기", "Use fewer sources and filters");
                case "r3": return Tx("프리셋을 한 단계 가볍게, 또는 하드웨어 인코더", "Lighter preset, or a hardware encoder");
                case "r4": return Tx("해상도나 fps 낮추기", "Lower resolution or fps");
                case "r5": return Tx("비트레이트 낮추기, 유선 연결", "Lower bitrate, use wired network");
                case "r6": return Tx("그 프로그램 끄기나 코어 제한", "Close or limit that app");
                case "r7": return Tx("안 쓰는 프로그램 끄기", "Close unused apps");
                default: return "";
            }
        }

        public static string KindName(int k) { return k == 0 ? Tx("렌더링 지연", "Rendering lag") : k == 1 ? Tx("인코딩 지연", "Encoding lag") : Tx("네트워크", "Network"); }

        /// <summary>프레임을 가장 많이 버린 원인 (없으면 null)</summary>
        public string Main()
        {
            lock (Gate)
            {
                var by = new Dictionary<string, int>(); string best = null;
                foreach (var d in Drops) { if (d.Open && d.Cause.Length == 0) continue; int n; by.TryGetValue(d.Cause, out n); by[d.Cause] = n + d.Frames; if (best == null || by[d.Cause] > by[best]) best = d.Cause; }
                return best;
            }
        }

        /// <summary>streams.tsv 한 칸: "r5:3 r1:1"</summary>
        public string Causes()
        {
            lock (Gate)
            {
                var by = new SortedDictionary<string, int>(); foreach (var d in Drops) { string k = d.Cause.Length == 0 ? "?" : d.Cause; int n; by.TryGetValue(k, out n); by[k] = n + 1; }
                var l = new List<string>(); foreach (var kv in by) l.Add(kv.Key + ":" + kv.Value); return string.Join(" ", l.ToArray());
            }
        }
    }
}
