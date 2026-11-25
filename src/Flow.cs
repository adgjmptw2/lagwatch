using System;
using System.Collections.Generic;
using static LagWatch.Lang;

namespace LagWatch
{
    /// <summary>지금 탭. 행 하나가 자원 하나: 왼쪽에 사용률, 오른쪽에 막대 하나와 많이 쓰는 순서.
    /// 막대 색은 사용률로 정한다(70% 주황, 90% 빨강).</summary>
    internal sealed partial class MainView
    {
        sealed class Item { public string Key, Label; public Grp G; public double V; public bool Game, Bad; }
        static readonly int[] Zones = { Snap.Cpu, Snap.Vm, Snap.Ram };
        static readonly string[] CoreNames = { Tx("프로그램", "Apps"), Tx("코어", "Cores") };

        /// <summary>그 자원을 쓰는 순서: 게임, 많이 쓰는 것, 윈도우, 기타.</summary>
        List<Item> Items(int m, Cause bad)
        {
            string cul = Culprit(m, bad); double cap = S.Cap(m), used = S.Used(m), sum = 0; Grp win = null; var l = new List<Grp>();
            foreach (var g in S.Groups) { if (g.Key == "win") { win = g; continue; } if (g == S.Game) continue; if (S.Val(g, m) / cap >= .015 || g.Key == cul) l.Add(g); }
            l.Sort((a, b) => S.Val(b, m).CompareTo(S.Val(a, m)));
            var r = new List<Item>(); if (S.Game != null) r.Add(new Item { Key = S.Game.Key, Label = S.Game.Label, G = S.Game, V = S.Val(S.Game, m), Game = true });
            for (int i = 0; i < l.Count; i++) if (i < 6 || l[i].Key == cul) r.Add(new Item { Key = l[i].Key, Label = l[i].Label, G = l[i], V = S.Val(l[i], m), Bad = l[i].Key == cul });
            if (win != null && S.Val(win, m) / cap >= .015) r.Add(new Item { Key = "win", Label = Tx("윈도우", "Windows"), G = win, V = S.Val(win, m) });
            foreach (var x in r) sum += x.V;
            if (sum > used && sum > 0) { foreach (var x in r) x.V *= used / sum; sum = used; } // 공유 메모리를 겹쳐 센 만큼
            r.Add(new Item { Key = "*rest", Label = Tx("기타", "Other"), V = Math.Max(0, used - sum) });
            return r;
        }

        void PaintFlow(float w, float h, Cause bad)
        {
            float x0 = 272, bw = w - 40 - x0, top = 176, pitch = (h - top - 8) / 3;
            for (int z = 0; z < 3; z++)
            {
                int m = Zones[z]; float y = top + z * pitch; double pct = Pct(m);
                if (z > 0) C.Fill(40, y - 14, w - 80, 1, T.Fg.Fade(T.Dark ? .1f : .14f));
                float nw = C.Mid(Txt.Title(m), F.Sans(20, 800), 40, y + 12, T.Fg);
                if (m == Snap.Cpu) Segs("core", CoreNames, Cores ? 1 : 0, 40 + nw + 14, y - 3, 30, F.Sans(14.5f, 700), false, k => { Cores = k == 1; if (PageChanged != null) PageChanged(); Redraw(); });
                else C.Mid(Txt.Of(S, m), F.Sans(16, 600), 40 + nw + 12, y + 13, T.Fg.Fade(.72f), 0, x0 - 60 - nw);
                Usage(m, 40, y + 86, true, x0 - 8);
                if (m == Snap.Cpu && Cores) { CoreTubes(x0, bw, y + 4, 80, y + 112); continue; }
                if (m == Snap.Ram) RamBar = new R4(x0 + C.Ox, y + 36 + C.Oy, bw, 48);
                Bar(z, m, x0, y + 36, bw, 48, pct, bad);
            }
        }

        /// <summary>막대 하나. 프로그램마다 틈을 두고 잇고, 아래에 이름과 양을 그 순서대로.</summary>
        void Bar(int z, int m, float x0, float y, float bw, float bh, double pct, Cause bad)
        {
            var list = Items(m, bad); double cap = S.Cap(m); string pre = "fl" + z, hov = Hover != null && (Hover.StartsWith(pre) || Hover.StartsWith("fg" + z)) ? Hover.Substring(3) : null;
            var fill = T.LvFill(pct, T.W[1]);
            var mask = C.ClipRound(x0, y, bw, bh, 9); C.Fill(x0, y, bw, bh, T.Fg.Fade(T.Dark ? .07f : .09f));
            float x = x0;
            foreach (var it in list)
            {
                float sw = Math.Max(0, Soft(pre + it.Key, (float)(it.V / cap * bw), 4)); if (sw < .5f) continue;
                var c = it.Key == "*rest" || it.Key == "win" ? fill.To(T.Bg, .4f) : fill; if (hov != null && hov != it.Key) c = c.To(T.Bg, .45f);
                C.Fill(x, y, Math.Max(.5f, sw - 2), bh, c);
                if (it.Game) C.Fill(x, y + bh - 6, Math.Max(.5f, sw - 2), 6, T.Sand);
                string k = it.Key; Zone(x, y, Math.Max(3, sw), bh, pre + k, () => Pick(m, k));
                x += sw;
            }
            C.PopClip(mask);
            // 이름과 양
            float lx = x0, ly = y + bh + 26, right = x0 + bw; var ls = F.Sans(16.5f, 700); var vs = F.Din(19);
            for (int i = 0; i < list.Count; i++)
            {
                var it = list[i]; if (it.V / cap < .004 && it.Key == "*rest") continue;
                string val = Txt.Val(m, it.V); float iw = 20 + 7 + C.Measure(it.Label, ls) + 7 + C.Measure(val, vs);
                bool last = i == list.Count - 1; string more = last ? "" : Tx("외 {0}개", "+{0} more", list.Count - i); if (lx + iw > right - (last ? 0 : 28 + C.Measure(more, F.Sans(15.5f, 700)))) { if (last) more = Tx("외 {0}개", "+{0} more", 1); C.Mid(more, F.Sans(15.5f, 700), lx, ly, T.Fg.Fade(.6f)); break; }
                bool on = hov == null || hov == it.Key; var ink = (it.Bad ? T.HotInk : T.Fg).Fade(on ? 1 : .45f);
                Icon(it.G, it.Key, lx, ly - 10, 20, ink); float tx = lx + 27; tx += C.Mid(it.Label, ls, tx, ly, ink) + 7; C.Mid(val, vs, tx, ly + 1, ink.Fade(.8f));
                string k = it.Key; Zone(lx - 6, ly - 17, iw + 12, 34, "fg" + z + k, () => Pick(m, k));
                lx += iw + 28;
            }
        }
        void Pick(int m, string key) { if (key == "*rest" || key == "win") { sort = m; openWin = key == "win"; Go(Prog); } else { Open = key; Redraw(); } }
    }
}
