using System;
using System.Collections.Generic;
using System.Globalization;
using static LagWatch.Lang;

namespace LagWatch
{
    internal sealed partial class MainView
    {
        // ---- 방송 ----
        static readonly Col[] KindCol = { Col.Hex(0xe2a83a), Col.Hex(0xe2578b), Col.Hex(0x5cbcc6) }; // 렌더링, 인코딩, 네트워크
        readonly Sec[] secBuf = new Sec[600];

        /// <summary>OBS를 쓰는 PC에서만 탭을 보인다</summary>
        bool StreamTab { get { var c = D.Cast; return c != null && (c.State != "off" || c.LastSession != null); } }

        void PaintStream(float w, float h)
        {
            var cast = D.Cast; var sd = cast.Live ?? cast.LastSession; bool live = cast.Live != null;
            if (sd == null) { PaintStreamIdle(w, cast); return; }

            int n = sd.Recent(secBuf); var drops = sd.Copy(); var ic = CultureInfo.InvariantCulture;
            float kb = 0; int kn = 0; for (int i = Math.Max(0, n - 10); i < n; i++) { kb += secBuf[i].Kbps; kn++; }
            string main = sd.Main(), enc = sd.Enc.Encoder.Length > 0 ? sd.Enc.Encoder + (sd.Enc.Kbps > 0 ? " " + sd.Enc.Kbps.ToString("#,0", ic) + "kbps" : "") : "";
            double dur = ((live ? DateTime.Now : sd.End) - sd.Start).TotalSeconds;
            string title = live ? (drops.Count == 0 ? Tx("방송 중 · 끊김 없음", "Streaming · no drops") : main != null && main.Length > 0 ? Tx("끊김 {0}번 · 대부분 {1}", "{0} {2} · mostly {1}", drops.Count, StreamDoctor.Name(main), Pl(drops.Count, "drop", "drops")) : Tx("끊김 {0}번", "{0} {1}", drops.Count, Pl(drops.Count, "drop", "drops")))
                                : (drops.Count == 0 ? Tx("지난 방송 · 끊김 없음", "Last stream · no drops") : Tx("지난 방송 · 끊김 {0}번", "Last stream · {0} {1}", drops.Count, Pl(drops.Count, "drop", "drops")));
            string sub = live ? Fmt.Dur(dur) + (kn > 0 ? ", " + (kb / kn).ToString("#,0", ic) + "kbps" : "") + (enc.Length > 0 ? Tx(", 설정 ", ", set ") + enc : "")
                              : sd.Start.ToString("M'/'d HH:mm", ic) + ", " + Fmt.Dur(dur) + (enc.Length > 0 ? ", " + enc : "");
            Say(w, title, sub, 0);

            // 1초마다 버린 프레임: 바닥 선에서 솟는다. 색은 끊김 종류.
            float y0 = 168, ch = Math.Min(220, h * .3f), gl = 40, gr = w - 40, floor = y0 + ch - 30;
            C.Fill(gl, floor, gr - gl, 1.5f, T.Fg.Fade(.3f));
            if (n > 0)
            {
                float bw = (gr - gl) / Math.Max(60, n), H = floor - y0 - 34; DateTime t0 = secBuf[0].At; int hov = -1;
                for (int i = 0; i < n; i++)
                {
                    var s = secBuf[i]; if (s.Frames == 0) continue;
                    float x = gl + i * bw, y = floor, scale = (float)Math.Sqrt(Math.Min(60, s.Frames) / 60.0) * H / Math.Max(1, s.Frames);
                    int[] parts = { s.Render, s.Enc, s.Net };
                    for (int k = 0; k < 3; k++) { if (parts[k] == 0) continue; float bh = Math.Max(2, parts[k] * scale); y -= bh; C.Fill(x, y, Math.Max(1.5f, bw - .5f), bh, KindCol[k]); }
                    string id = "sb" + i; Zone(x - 2, y0, bw + 4, floor - y0, id, () => { }); if (Over(id)) hov = i;
                }
                C.Mid(t0.ToString("HH:mm", ic), F.Sans(14.5f, 600), gl, floor + 18, T.Fg.Fade(.65f));
                C.Mid(secBuf[n - 1].At.ToString("HH:mm", ic), F.Sans(14.5f, 600), gr, floor + 18, T.Fg.Fade(.65f), 2);
                if (hov >= 0)
                {
                    var s = secBuf[hov];
                    string t = s.At.ToString("HH:mm:ss", ic) + Tx(" · 렌더링 {0} · 인코딩 {1} · 네트워크 {2}프레임 · 송출 {3}kbps · GPU {4}% · CPU {5}%", " · render {0} · encode {1} · network {2} frames · sent {3} kbps · GPU {4}% · CPU {5}%", s.Render, s.Enc, s.Net, s.Kbps.ToString("#,0", ic), s.Busy.ToString("0", ic), float.IsNaN(s.Cpu) ? "-" : s.Cpu.ToString("0", ic));
                    var st = F.Sans(15.5f, 700); float tw = C.Measure(t, st) + 28, px = Math.Max(8, Math.Min(w - tw - 8, gl + hov * bw - tw / 2)); C.Round(px, y0 - 4, tw, 34, 17, T.Btn); C.Mid(t, st, px + tw / 2, y0 + 13, T.Bfg, 1);
                }
            }

            // 원인별: 측정한 프레임 수 + 추정한 원인
            float ry = floor + 44, sx = 40, r = w - 40, bot = 66;
            var by = new Dictionary<string, List<Drop>>(); var keys = new List<string>();
            foreach (var d in drops) { List<Drop> l; if (!by.TryGetValue(d.Cause, out l)) { by[d.Cause] = l = new List<Drop>(); keys.Add(d.Cause); } l.Add(d); }
            keys.Sort((a, b) => Frames(by[b]).CompareTo(Frames(by[a])));
            if (keys.Count == 0) C.Mid(Tx("끊김 없음", "No drops"), F.Sans(19, 600), sx, ry + 30, T.Fg.Fade(.55f));
            float rh = keys.Count == 0 ? 0 : Math.Max(62, Math.Min(84, (h - bot - ry - 8) / keys.Count));
            for (int i = 0; i < keys.Count && ry + (i + 1) * rh <= h - bot + 4; i++)
            {
                var l = by[keys[i]]; var last = l[l.Count - 1]; float y = ry + i * rh, cy = y + rh / 2; C.Fill(sx, y, r - sx, 1, Sep);
                C.Round(sx, cy - 18, 5, 36, 2.5f, KindCol[last.Kind]);
                string fr = Tx("{0}프레임", "{0} frames", Frames(l).ToString("#,0", ic)); var fs = F.Sans(19, 700); float fw = C.Measure(fr, fs);
                C.Mid(fr, fs, r, cy - 2, T.Fg.Fade(.85f), 2);
                float tx = sx + 22, tw = r - fw - tx - 24;
                C.Text(Tx("{0} {1}번", "{0}: {1}", StreamDoctor.Name(keys[i]), l.Count) + (keys[i].Length > 0 ? Tx(" · 추정", " · estimated") : ""), F.Sans(19, 700), tx, cy - 3, T.Fg, 0, tw);
                string fix = StreamDoctor.Fix(keys[i]);
                C.Text(StreamDoctor.KindName(last.Kind) + ", " + last.Why + (fix.Length > 0 ? " · " + fix : ""), F.Sans(16, 500), tx, cy + 21, T.Fg.Fade(.7f), 0, tw);
            }

            // 바닥: 잰 숫자
            C.Fill(40, h - bot, w - 80, 1, Sep); float bx = 40, by2 = h - bot / 2; long[] tot = { sd.RenderTotal, sd.EncTotal, sd.NetTotal };
            for (int k = 0; k < 3; k++) { C.Circle(bx + 5, by2, 5, KindCol[k]); bx += 16; bx += C.Mid(StreamDoctor.KindName(k) + " " + tot[k].ToString("#,0", ic), F.Sans(16, 600), bx, by2, T.Fg.Fade(.75f)) + 26; }
        }

        static int Frames(List<Drop> l) { int n = 0; foreach (var d in l) n += d.Frames; return n; }

        void PaintStreamIdle(float w, Broadcast c)
        {
            string t, s = "";
            switch (c.State)
            {
                case "disabled": t = Tx("OBS 웹소켓 꺼짐", "OBS WebSocket is off"); s = Tx("OBS의 도구 → WebSocket 서버 설정에서 켜기", "Turn it on in OBS: Tools → WebSocket Server Settings"); break;
                case "auth": t = Tx("OBS 연결 거절됨", "OBS refused the connection"); s = Tx("OBS의 WebSocket 서버 설정에서 비밀번호 확인", "Check the password in OBS WebSocket Server Settings"); break;
                case "wait": t = Tx("OBS 연결 중", "Connecting to OBS"); break;
                case "on": t = Tx("방송 대기", "Waiting for a stream"); s = "OBS " + c.ObsVersion; break;
                default: t = Tx("OBS 꺼짐", "OBS is closed"); break;
            }
            Say(w, t, s, 0);
        }
    }
}
