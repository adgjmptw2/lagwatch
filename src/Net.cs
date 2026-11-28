using System;
using System.Runtime.InteropServices;

namespace LagWatch
{
    internal struct NetSample { public float UpKbps, Retrans; }

    /// <summary>업로드(\Network Interface(*)\Bytes Sent/sec 합)와 TCP 재전송. 127.0.0.1 통신은 여기 잡히지 않는다.
    /// 재전송은 PC 전체 값이라 OBS 몫을 따로 알 수 없다.</summary>
    internal sealed unsafe class Net : IDisposable
    {
        IntPtr q, up, r4, r6, buf; int cap;
        double sum; readonly Pdh.RawEach add;
        public Net() { add = (n, v) => sum += v; }
        public void Start()
        {
            if (q != IntPtr.Zero) return;
            q = Pdh.Open(); up = Pdh.Add(q, @"\Network Interface(*)\Bytes Sent/sec");
            r4 = Pdh.Add(q, @"\TCPv4\Segments Retransmitted/sec"); r6 = Pdh.Add(q, @"\TCPv6\Segments Retransmitted/sec");
            if (q != IntPtr.Zero) Pdh.PdhCollectQueryData(q);
        }
        public void Dispose() { if (q != IntPtr.Zero) Pdh.PdhCloseQuery(q); q = up = r4 = r6 = IntPtr.Zero; if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); buf = IntPtr.Zero; cap = 0; }

        public NetSample Sample()
        {
            var s = new NetSample();
            if (q == IntPtr.Zero || Pdh.PdhCollectQueryData(q) != 0) return s;
            sum = 0; Pdh.Many(up, ref buf, ref cap, add);
            s.UpKbps = (float)(sum * 8 / 1000);
            double a = Pdh.One(r4), b = Pdh.One(r6); s.Retrans = (float)((double.IsNaN(a) ? 0 : a) + (double.IsNaN(b) ? 0 : b));
            return s;
        }
    }
}
