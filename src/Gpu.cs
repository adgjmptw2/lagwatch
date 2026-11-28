using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LagWatch
{
    /// <summary>성능 카운터(PDH). 영문 이름으로 넣어서 한국어 윈도우에서도 같은 경로를 쓴다.</summary>
    internal static class Pdh
    {
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern uint PdhOpenQueryW(string src, IntPtr user, out IntPtr q);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern uint PdhAddEnglishCounterW(IntPtr q, string path, IntPtr user, out IntPtr c);
        [DllImport("pdh.dll")] public static extern uint PdhCollectQueryData(IntPtr q);
        [DllImport("pdh.dll")] public static extern uint PdhCloseQuery(IntPtr q);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhGetFormattedCounterArrayW(IntPtr c, uint fmt, ref uint size, out uint count, IntPtr buf);
        [DllImport("pdh.dll")] static extern uint PdhGetFormattedCounterValue(IntPtr c, uint fmt, IntPtr type, out Val v);
        [StructLayout(LayoutKind.Explicit)] struct Val { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double D; }
        const uint Double = 0x200, NoCap = 0x8000, MoreData = 0x800007D2;

        public static IntPtr Open() { IntPtr q; return PdhOpenQueryW(null, IntPtr.Zero, out q) == 0 ? q : IntPtr.Zero; }
        public static IntPtr Add(IntPtr q, string path) { IntPtr c; return q != IntPtr.Zero && PdhAddEnglishCounterW(q, path, IntPtr.Zero, out c) == 0 ? c : IntPtr.Zero; }

        public static double One(IntPtr c) { Val v; return c != IntPtr.Zero && PdhGetFormattedCounterValue(c, Double | NoCap, IntPtr.Zero, out v) == 0 && v.Status <= 1 ? v.D : double.NaN; }

        public unsafe delegate void RawEach(char* name, double v);

        /// <summary>와일드카드 카운터의 인스턴스별 값. 이름은 PDH 버퍼 안의 글자 그대로 넘긴다(문자열을 만들지 않음). buf는 다시 쓴다.</summary>
        public static unsafe int Many(IntPtr c, ref IntPtr buf, ref int cap, RawEach each)
        {
            if (c == IntPtr.Zero) return 0;
            uint size = (uint)cap, n;
            uint st = PdhGetFormattedCounterArrayW(c, Double | NoCap, ref size, out n, buf);
            if (st == MoreData) { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); cap = (int)size + 4096; buf = Marshal.AllocHGlobal(cap); size = (uint)cap; st = PdhGetFormattedCounterArrayW(c, Double | NoCap, ref size, out n, buf); }
            if (st != 0) return 0;
            byte* p = (byte*)buf;
            for (int i = 0; i < n; i++, p += 24) // PDH_FMT_COUNTERVALUE_ITEM_W: 이름 포인터 8 + 값 16
            {
                if (*(uint*)(p + 8) > 1) continue;
                each(*(char**)p, *(double*)(p + 16));
            }
            return (int)n;
        }
    }

    internal struct GpuSample
    {
        public float ObsEnc, Obs3D, ObsCuda, Game3D, Busy, Enc;
        public int Instances;
    }

    /// <summary>GPU 엔진 사용률(\GPU Engine(*)\Utilization Percentage). 프로세스를 열지 않는다.
    /// 엔진은 (어댑터, 엔진 번호)마다 모든 프로세스 몫을 더하고, "바쁨"은 그중 가장 높은 엔진이다(작업 관리자와 같은 방식).</summary>
    internal sealed unsafe class Gpu : IDisposable
    {
        IntPtr q, c, buf; int cap;
        // 인스턴스 이름은 매초 같으니 해시로 한 번만 풀어 둔다(초당 수백 개 문자열을 만들지 않으려고)
        struct Inst { public int Pid, Eng; public byte Kind; }
        const byte KCompute = 1, KEncode = 2, K3D = 4, KCuda = 8;
        readonly Dictionary<ulong, Inst> cache = new Dictionary<ulong, Inst>(1024);
        readonly Dictionary<string, int> engIndex = new Dictionary<string, int>(64);
        float[] engSum = new float[64]; byte[] engKind = new byte[64];
        readonly Pdh.RawEach each;
        int obsPid, gamePid; GpuSample cur;

        public Gpu() { each = Each; }
        public bool Ok { get { return c != IntPtr.Zero; } }
        public void Start() { if (q != IntPtr.Zero) return; q = Pdh.Open(); c = Pdh.Add(q, @"\GPU Engine(*)\Utilization Percentage"); if (q != IntPtr.Zero) Pdh.PdhCollectQueryData(q); }
        public void Dispose() { if (q != IntPtr.Zero) Pdh.PdhCloseQuery(q); q = c = IntPtr.Zero; if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); buf = IntPtr.Zero; cap = 0; }

        public static bool IsCompute(string type) { return type.StartsWith("3D") || type.StartsWith("Compute") || type.StartsWith("Cuda") || type.StartsWith("Graphics"); }
        public static bool IsEncode(string type) { return type.StartsWith("VideoEncode") || type.StartsWith("VideoCodec"); }

        /// <summary>"pid_1234_luid_0x0_0xD6F1_phys_0_eng_3_engtype_VideoEncode" → pid, 엔진 키, 종류</summary>
        public static bool Split(string name, out int pid, out string key, out string type)
        {
            pid = 0; key = type = null;
            int a = name.IndexOf("pid_"), b = name.IndexOf("_luid_"), e = name.IndexOf("_engtype_");
            if (a != 0 || b < 0 || e < 0 || !int.TryParse(name.Substring(4, b - 4), out pid)) return false;
            key = name.Substring(b + 6, e - b - 6); type = name.Substring(e + 9);
            return true;
        }

        public GpuSample Sample(int obsPid, int gamePid)
        {
            cur = new GpuSample(); this.obsPid = obsPid; this.gamePid = gamePid;
            if (q == IntPtr.Zero || Pdh.PdhCollectQueryData(q) != 0) return cur;
            Array.Clear(engSum, 0, engIndex.Count);
            cur.Instances = Pdh.Many(c, ref buf, ref cap, each);
            for (int i = 0; i < engIndex.Count; i++) { if ((engKind[i] & KCompute) != 0) cur.Busy = Math.Max(cur.Busy, engSum[i]); if ((engKind[i] & KEncode) != 0) cur.Enc = Math.Max(cur.Enc, engSum[i]); }
            cur.Busy = Math.Min(100, cur.Busy); cur.Enc = Math.Min(100, cur.Enc);
            return cur;
        }

        void Each(char* n, double v)
        {
            ulong h = 14695981039346656037UL; int len = 0;
            for (char* p = n; *p != 0; p++, len++) { h ^= *p; h *= 1099511628211UL; }
            Inst it;
            if (!cache.TryGetValue(h, out it))
            {
                int pid; string key, type;
                if (!Split(new string(n, 0, len), out pid, out key, out type)) it.Pid = -1;
                else
                {
                    it.Pid = pid; int e;
                    if (!engIndex.TryGetValue(key, out e)) { e = engIndex.Count; engIndex[key] = e; if (e >= engSum.Length) { Array.Resize(ref engSum, e * 2); Array.Resize(ref engKind, e * 2); } }
                    it.Eng = e;
                    it.Kind = (byte)((IsCompute(type) ? KCompute : 0) | (IsEncode(type) ? KEncode : 0) | (type.StartsWith("3D") ? K3D : 0) | (type.StartsWith("Cuda") || type.StartsWith("Compute") ? KCuda : 0));
                    engKind[e] |= it.Kind;
                }
                if (cache.Count > 8192) cache.Clear(); // 프로세스가 오래 바뀌어도 끝없이 늘지 않게
                cache[h] = it;
            }
            if (it.Pid < 0) return;
            float f = (float)v; engSum[it.Eng] += f;
            if (it.Pid == obsPid && obsPid != 0)
            {
                if ((it.Kind & KEncode) != 0) cur.ObsEnc = Math.Max(cur.ObsEnc, f);
                else if ((it.Kind & K3D) != 0) cur.Obs3D = Math.Max(cur.Obs3D, f);
                else if ((it.Kind & KCuda) != 0) cur.ObsCuda = Math.Max(cur.ObsCuda, f);
            }
            if (it.Pid == gamePid && gamePid != 0 && (it.Kind & K3D) != 0) cur.Game3D = Math.Max(cur.Game3D, f);
        }
    }
}
