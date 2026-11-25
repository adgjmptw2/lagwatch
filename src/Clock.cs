using System;
using System.Runtime.InteropServices;

namespace LagWatch
{
    /// <summary>
    /// 실제 CPU 속도. 성능 카운터 이름 목록(3MB)을 통째로 읽는 PDH 대신 필요한 값 셋만 직접 묻는다.
    /// 처음 물을 때 열고, 실패하면 조용히 안 쓴다.
    /// </summary>
    internal sealed unsafe class Clock
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern uint PerfOpenQueryHandle(string machine, out IntPtr h);
        [DllImport("advapi32.dll")] static extern uint PerfCloseQueryHandle(IntPtr h);
        [DllImport("advapi32.dll")] static extern uint PerfAddCounters(IntPtr h, byte* ids, int n);
        [DllImport("advapi32.dll")] static extern uint PerfQueryCounterData(IntPtr h, byte* buf, int n, out int got);
        static readonly Guid Processor = new Guid("b4fc721a-0378-476f-89ba-a5a79f810b36");
        static readonly int[] Ids = { 24, 25, 30 }; // % Processor Performance 와 그 밑수, % Performance Limit

        IntPtr q; bool tried, has; long n0, b0;

        void Open()
        {
            tried = true; const int One = 56; byte* b = stackalloc byte[One * 3]; string all = "_Total";
            for (int i = 0; i < One * 3; i++) b[i] = 0;
            for (int i = 0; i < 3; i++)
            {
                byte* p = b + One * i; *(Guid*)p = Processor; uint* u = (uint*)(p + 16); u[0] = 0; u[1] = One; u[2] = (uint)Ids[i]; u[3] = 0xFFFFFFFF; u[4] = (uint)i;
                for (int k = 0; k < all.Length; k++) ((char*)(p + 40))[k] = all[k];
            }
            try { IntPtr h; if (PerfOpenQueryHandle(null, out h) != 0) return; if (PerfAddCounters(h, b, One * 3) != 0) { PerfCloseQueryHandle(h); return; } q = h; }
            catch (Exception) { }
        }
        public void Close() { if (q != IntPtr.Zero) PerfCloseQueryHandle(q); q = IntPtr.Zero; tried = has = false; }

        /// <summary>기본 속도 대비 비율(1.23 = 터보). 못 읽으면 0.</summary>
        public double Read(out double limitPct)
        {
            limitPct = 100; if (!tried) Open(); if (q == IntPtr.Zero) return 0;
            byte* b = stackalloc byte[1024]; int got; if (PerfQueryCounterData(q, b, 1024, out got) != 0 || got < 48) return 0;
            int n = *(int*)(b + 4), ok = 0; byte* p = b + 48; long* v = stackalloc long[3];
            for (int i = 0; i < n && i < 3 && p + 32 <= b + got; i++)
            {
                int type = *(int*)(p + 4), size = *(int*)(p + 8); if (type == 1) { v[i] = *(int*)(p + 16) == 8 ? *(long*)(p + 24) : *(uint*)(p + 24); ok |= 1 << i; }
                if (size <= 0) break; p += size;
            }
            if ((ok & 4) != 0 && v[2] > 0) limitPct = v[2];
            if ((ok & 3) != 3) return 0;
            uint db = unchecked((uint)(v[1] - b0)); double r = has && db > 0 ? (double)(v[0] - n0) / db / 100 : 0; n0 = v[0]; b0 = v[1]; has = true;
            return r > 0 && r < 20 ? r : 0;
        }
    }
}
