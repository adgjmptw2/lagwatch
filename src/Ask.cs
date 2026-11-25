using System;
using static LagWatch.Lang;

namespace LagWatch
{
    /// <summary>되돌릴 수 없는 일을 하기 전에 창 안에서 한 번 묻는다.</summary>
    internal sealed partial class MainView
    {
        string askT, askS; Action askOk;
        public bool Asking { get { return askOk != null; } }
        public void AskBox(string title, string text, Action ok) { askT = title; askS = text; askOk = ok; if (PageChanged != null) PageChanged(); Redraw(); }
        public void AskNo() { askOk = null; if (PageChanged != null) PageChanged(); Redraw(); }

        void PaintAsk(float w, float h)
        {
            float k = A("ask", askOk != null ? 1 : 0); if (k < .01f || askT == null) return;
            if (askOk != null) Zone(0, 0, w, h, "askdim", AskNo);
            C.Fill(0, 0, w, h, Col.Hex(0, .5f * k));
            float bw = Math.Min(560, Math.Max(440, C.Measure(askS, F.Sans(17, 500)) + 72)), bh = 190, x = w / 2 - bw / 2, y = h / 2 - bh / 2 + (1 - k) * 14;
            C.PushOpacity(k);
            C.Round(x, y, bw, bh, 26, T.Sheet); if (askOk != null) Zone(x, y, bw, bh, "askbox", null);
            C.Text(askT, F.Sans(27, 800), x + 34, y + 54, T.Fg, 0, bw - 68); C.Text(askS, F.Sans(17, 500), x + 34, y + 86, T.Fg.Fade(.85f), 0, bw - 68);
            float pw = (bw - 78) / 2; var st = F.Sans(18, 800);
            Pill("askno", Tx("취소", "Cancel"), x + 34, y + 112, pw, 52, 26, T.Faint, T.Fg, st, AskNo);
            Pill("askok", Tx("확인", "OK"), x + 44 + pw, y + 112, pw, 52, 26, T.Btn, T.Bfg, st, () => { var f = askOk; AskNo(); if (f != null) f(); });
            C.PopClip(IntPtr.Zero);
        }
    }
}
