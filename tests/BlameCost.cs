using System;
using System.Diagnostics;
using System.Threading;

namespace LagWatch
{
    /// <summary>스레드 교체 기록만 켤 때와 깨어남까지 켤 때의 건수와 CPU. 관리자로. BlameCost exe [초]</summary>
    internal sealed unsafe class Probe : Trace
    {
        public long Sw, Wake, Other; uint want;
        public Probe() : base("LagWatchCost") { }
        public bool Start(uint flags) { Sw = Wake = Other = 0; return Open(flags, 64, 32, 128, new Guid("5f0f6f5a-9c0c-4c0e-8a55-0d7f3a7f1c11")); }
        protected override void On(RECORD* r)
        {
            if (*(uint*)&r->H.Provider != 0x3D6FA8D1) { Other++; return; }
            byte* u = (byte*)r->User; int op = r->H.D.Opcode;
            if (op == 36 && r->Len >= 16) { Sw++; want ^= *(uint*)u; } else if (op == 50 && r->Len >= 4) { Wake++; want ^= *(uint*)u; } else Other++;
        }
    }
    internal static class BlameCost
    {
        static int Main(string[] a)
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false); int sec = a.Length > 0 ? int.Parse(a[0]) : 8; var me = Process.GetCurrentProcess();
            foreach (int round in new[] { 0, 1 })
                foreach (uint fl in new uint[] { 0x10, 0x810 })
                {
                    var p = new Probe(); if (!p.Start(fl)) { Console.WriteLine("실패 " + p.Error); return 1; }
                    Thread.Sleep(1200); p.Sw = p.Wake = p.Other = 0; me.Refresh(); double c0 = me.TotalProcessorTime.TotalMilliseconds; var w = Stopwatch.StartNew(); Thread.Sleep(sec * 1000); me.Refresh();
                    double el = w.Elapsed.TotalSeconds, cpu = (me.TotalProcessorTime.TotalMilliseconds - c0) / el / 10 / Environment.ProcessorCount;
                    Console.WriteLine("0x{0:X}: 교체 {1:F0}/초, 깨어남 {2:F0}/초, 기타 {3:F0}/초, 이 프로세스 CPU {4:F2}% (전체 대비)", fl, p.Sw / el, p.Wake / el, p.Other / el, cpu); p.Close(); Trace.StopByName("LagWatchCost"); Thread.Sleep(400);
                }
            return 0;
        }
    }
}
