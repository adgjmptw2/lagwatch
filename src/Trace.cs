using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace LagWatch
{
    /// <summary>윈도우 추적(ETW)을 실시간으로 받는다. 게임 프로세스는 열지 않는다.</summary>
    internal abstract unsafe class Trace : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct WNODE { public uint BufferSize, ProviderId; public ulong Hist; public long TimeStamp; public Guid Guid; public uint ClientContext, Flags; }
        [StructLayout(LayoutKind.Sequential)] struct PROPS { public WNODE W; public uint BufferSize, MinBuffers, MaxBuffers, MaxFile, LogFileMode, FlushTimer, EnableFlags; public int AgeLimit; public uint NumBuffers, FreeBuffers, EventsLost, BuffersWritten, LogBuffersLost, RtBuffersLost; public IntPtr LoggerThread; public uint LogFileNameOffset, LoggerNameOffset; }
        [StructLayout(LayoutKind.Sequential)] protected struct DESC { public ushort Id; public byte Version, Channel, Level, Opcode; public ushort Task; public ulong Keyword; }
        [StructLayout(LayoutKind.Sequential)] protected struct HEADER { public ushort Size, Type, Flags, Prop; public uint Tid, Pid; public long Time; public Guid Provider; public DESC D; public ulong Cpu; public Guid Act; }
        [StructLayout(LayoutKind.Sequential)] protected struct RECORD { public HEADER H; public byte Proc, Align; public ushort Logger, ExtCount, Len; public IntPtr Ext, User, Ctx; }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] protected delegate void Callback(RECORD* r);
        [StructLayout(LayoutKind.Sequential)] struct LOGFILE { public IntPtr FileName, LoggerName; public long Time; public uint Read, Mode; public fixed byte Cur[88]; public fixed byte Head[280]; public IntPtr BufCb; public uint BufSize, Filled, Lost; public IntPtr EventCb; public uint IsKernel; public IntPtr Ctx; }
        [DllImport("advapi32", CharSet = CharSet.Unicode)] static extern uint StartTraceW(out ulong h, string name, IntPtr p);
        [DllImport("advapi32", CharSet = CharSet.Unicode)] static extern uint ControlTraceW(ulong h, string name, IntPtr p, uint code);
        [DllImport("advapi32")] static extern uint EnableTraceEx2(ulong h, ref Guid g, uint code, byte level, ulong any, ulong all, uint timeout, IntPtr prm);
        [DllImport("advapi32", SetLastError = true)] static extern ulong OpenTraceW(ref LOGFILE f);
        [DllImport("advapi32")] static extern uint ProcessTrace(ulong[] h, uint n, IntPtr s, IntPtr e);
        [DllImport("advapi32")] static extern uint CloseTrace(ulong h);

        readonly string name;
        readonly Callback cb;
        ulong session, trace;
        IntPtr namePtr;
        Thread thread;
        public uint Error;
        public bool Running { get { return session != 0; } }

        /// <summary>시험이나 측정으로 따로 켠 것은 이름을 달리해, 켜져 있는 렉 감시의 기록을 끊지 않는다.</summary>
        public static string Tag = "";
        protected Trace(string name) { this.name = name + Tag; cb = Guarded; }
        static Guid Tagged(Guid id)
        {
            if (Tag.Length == 0) return id;
            uint h = 2166136261; foreach (char c in Tag) h = (h ^ c) * 16777619; var b = id.ToByteArray(); for (int i = 0; i < 4; i++) b[i] ^= (byte)(h >> i * 8);
            return new Guid(b);
        }

        static IntPtr Props(uint flags, uint mode, uint bufKb, uint min, uint max, Guid id)
        {
            int sz = sizeof(PROPS) + 512; IntPtr p = Marshal.AllocHGlobal(sz);
            for (int i = 0; i < sz; i++) ((byte*)p)[i] = 0;
            var q = (PROPS*)p;
            q->W.BufferSize = (uint)sz; q->W.Flags = 0x20000; q->W.ClientContext = 1; q->W.Guid = id;
            q->BufferSize = bufKb; q->MinBuffers = min; q->MaxBuffers = max; q->LogFileMode = mode; q->FlushTimer = 1; q->EnableFlags = flags; q->LoggerNameOffset = (uint)sizeof(PROPS);
            return p;
        }

        public static void StopByName(string n)
        {
            IntPtr p = Props(0, 0, 0, 0, 0, Guid.Empty);
            ControlTraceW(0, n, p, 1);
            Marshal.FreeHGlobal(p);
        }

        protected bool Open(uint kernelFlags, uint bufKb, uint min, uint max, Guid id)
        {
            if (Running) return true;
            StopByName(name); // 지난번에 갑자기 꺼졌으면 남아 있을 수 있다
            IntPtr p = Props(kernelFlags, 0x100u | (kernelFlags != 0 ? 0x02000000u : 0), bufKb, min, max, Tagged(id));
            ulong h; Error = StartTraceW(out h, name, p);
            Marshal.FreeHGlobal(p);
            if (Error != 0) return false;
            session = h;
            var f = new LOGFILE { Mode = 0x100 | 0x10000000 | 0x1000 };
            namePtr = Marshal.StringToHGlobalUni(name);
            f.LoggerName = namePtr; f.EventCb = Marshal.GetFunctionPointerForDelegate(cb);
            trace = OpenTraceW(ref f);
            if (trace == ulong.MaxValue) { Error = (uint)Marshal.GetLastWin32Error(); Close(); return false; }
            ulong t = trace;
            thread = new Thread(() => ProcessTrace(new[] { t }, 1, IntPtr.Zero, IntPtr.Zero)) { IsBackground = true, Name = name, Priority = ThreadPriority.BelowNormal };
            thread.Start();
            return true;
        }

        protected void Enable(Guid provider, byte level, ulong keyword) { EnableTraceEx2(session, ref provider, 1, level, keyword, 0, 0, IntPtr.Zero); }

        public virtual void Close()
        {
            if (session == 0) return;
            IntPtr p = Props(0, 0, 0, 0, 0, Guid.Empty);
            ControlTraceW(session, name, p, 1);
            Marshal.FreeHGlobal(p);
            session = 0;
            if (trace != 0 && trace != ulong.MaxValue) CloseTrace(trace);
            trace = 0;
            if (thread != null && thread != Thread.CurrentThread) thread.Join(3000);
            thread = null;
            if (namePtr != IntPtr.Zero) { Marshal.FreeHGlobal(namePtr); namePtr = IntPtr.Zero; }
        }
        public void Dispose() { Close(); }

        void Guarded(RECORD* r) { try { if (session != 0) On(r); } catch (Exception) { } }
        protected abstract void On(RECORD* r);
    }

    /// <summary>게임이 화면을 내보낸 시각으로 프레임 간격과 끊김을 잰다.</summary>
    internal sealed unsafe class Frames : Trace
    {
        public const string Session = "LagWatchFrames";
        static readonly Guid Dxgi = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9"), D3d9 = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9");
        static readonly double Q = Stopwatch.Frequency / 1000.0;

        internal struct Sec { public int T, N, Hitch; public float Max; }
        internal struct Hitch { public int T; public float Ms; public long A, B; }

        readonly object gate = new object();
        readonly Sec[] secs = new Sec[60];
        readonly Hitch[] hits = new Hitch[128];
        int hitPos;
        public int HitchTotal;
        double avg;
        long last;
        uint pid;
        /// <summary>게임 창이 앞에 있을 때만 끊김으로 센다.</summary>
        public volatile bool Active;

        public Frames() : base(Session) { }

        readonly List<Hitch> fresh = new List<Hitch>();
        /// <summary>아직 까닭을 안 본 끊김을 꺼낸다.</summary>
        public List<Hitch> Drain() { lock (gate) { if (fresh.Count == 0) return null; var l = new List<Hitch>(fresh); fresh.Clear(); return l; } }
        Meter rec;
        /// <summary>한 판의 프레임을 모으기 시작하거나(새 것) 끝낸다(null). 모으던 것을 돌려준다.</summary>
        public Meter Record(Meter m) { lock (gate) { var o = rec; rec = m; return o; } }
        public Meter Rec { get { lock (gate) return rec; } }

        public bool Start(int gamePid)
        {
            lock (gate) { fresh.Clear(); pid = (uint)gamePid; last = 0; avg = 0; hitPos = 0; HitchTotal = 0; Array.Clear(secs, 0, secs.Length); Array.Clear(hits, 0, hits.Length); }
            if (!Open(0, 16, 4, 16, new Guid("2f6e0c7a-31c4-4a55-9d0e-52ab0d0f6a01"))) return false;
            Enable(Dxgi, 4, 0x2); Enable(D3d9, 4, 0x2);
            return true;
        }

        protected override void On(RECORD* r)
        {
            if (r->H.Pid != pid) return;
            if (!(r->H.D.Id == 42 && r->H.Provider == Dxgi) && !(r->H.D.Id == 1 && r->H.Provider == D3d9)) return;
            Push(r->H.Time, History.Epoch(DateTime.UtcNow));
        }

        /// <summary>시험에서도 부른다.</summary>
        public void Push(long qpc, int epoch)
        {
            lock (gate)
            {
                double ms = last == 0 ? 0 : (qpc - last) / Q;
                last = qpc;
                if (ms <= 0) return;
                int i = epoch % 60; if (i < 0) i += 60;
                if (secs[i].T != epoch) secs[i] = new Sec { T = epoch };
                secs[i].N++;
                // 2초 넘게 멈춘 것은 로딩이나 화면 전환으로 본다
                if (!Active || ms > 2000) return; // 내려 둔 동안의 느린 프레임이 평소 값을 흐리지 않게
                if (ms > secs[i].Max) secs[i].Max = (float)ms;
                bool hit = IsHitch(ms, avg); if (rec != null) rec.Add(ms, hit);
                if (hit) { secs[i].Hitch++; HitchTotal++; var hh = new Hitch { T = epoch, Ms = (float)ms, A = qpc - (long)(ms * Q), B = qpc }; if (fresh.Count < 300) fresh.Add(hh); hits[hitPos] = hh; hitPos = (hitPos + 1) % hits.Length; }
                avg = avg == 0 ? ms : avg + (Math.Min(ms, avg * 3 + 5) - avg) * 0.02;
            }
        }

        /// <summary>50ms를 넘거나, 25ms를 넘으면서 평소의 3배가 넘으면 끊김</summary>
        public static bool IsHitch(double ms, double usual) { return ms >= 50 || (ms >= 25 && usual > 0 && ms >= usual * 3); }

        /// <summary>최근 1분</summary>
        public void Last(int epoch, out double fps, out double max, out int hitch)
        {
            lock (gate)
            {
                double n = 0; int k = 0; max = 0; hitch = 0;
                for (int i = 0; i < 60; i++)
                {
                    if (epoch - secs[i].T > 60 || secs[i].T == 0) continue;
                    if (secs[i].Max > max) max = secs[i].Max;
                    hitch += secs[i].Hitch;
                    int age = epoch - secs[i].T;
                    if (age >= 1 && age <= 4) { n += secs[i].N; k++; }
                }
                fps = k > 0 ? n / k : 0;
            }
        }

        public List<Hitch> Since(int epoch)
        {
            var l = new List<Hitch>();
            lock (gate) for (int i = 0; i < hits.Length; i++) { var h = hits[(hitPos + i) % hits.Length]; if (h.T >= epoch && h.T != 0) l.Add(h); }
            return l;
        }
    }

    /// <summary>어느 프로그램이 어느 코어를 썼는지와 드라이버 처리 시간. 화면에서 볼 때와 원인을 찾을 때만 켠다.</summary>
    internal sealed unsafe class Kern : Trace
    {
        public const string Session = "LagWatchKernel";
        static readonly Guid Perf = new Guid("CE1DBFB4-137E-4DA6-87B0-3F59AA102CBC"), Thr = new Guid("3D6FA8D1-FE05-11D0-9DDA-00C04FD7BA7C");
        static readonly double Q = Stopwatch.Frequency / 1000.0;

        internal sealed class Drv { public string Name; public double Ms, Max; public int N; }
        internal sealed class Report
        {
            public double Ms;                                                              // 잰 시간
            public Dictionary<int, double[]> Core = new Dictionary<int, double[]>();       // 프로세스 → 논리 코어별 ms
            public List<Drv> Drivers = new List<Drv>();
            public Dictionary<int, double> Thief = new Dictionary<int, double>();          // 게임을 밀어낸 프로세스 → ms
            public double Robbed, RobbedMax;                                               // 게임이 밀려나 있던 시간
        }

        readonly object gate = new object();
        Dictionary<uint, int> tids = new Dictionary<uint, int>();
        readonly long[] lastT = new long[64];
        readonly uint[] lastTid = new uint[64];
        ulong[] modBase; string[] modName;
        Dictionary<string, Drv> drv = new Dictionary<string, Drv>();
        Report rep = new Report();
        readonly Dictionary<uint, long> outAt = new Dictionary<uint, long>();
        readonly Dictionary<uint, int> outBy = new Dictionary<uint, int>();
        long startAt;
        public int GamePid;

        public Kern() : base(Session) { }

        public bool Start(int game)
        {
            GamePid = game;
            Modules();
            lock (gate) { Reset(); Array.Clear(lastT, 0, 64); Threads(); }
            // CSWITCH | DPC | INTERRUPT
            return Open(0x10 | 0x20 | 0x40, 64, 32, 128, new Guid("5f2c8f5e-6f0b-4d6c-9a27-1d6c1b0a77aa"));
        }

        void Reset() { rep = new Report(); drv = new Dictionary<string, Drv>(); outAt.Clear(); outBy.Clear(); startAt = Stopwatch.GetTimestamp(); }

        /// <summary>지금까지 모은 것을 꺼내고 새로 시작한다.</summary>
        public Report Take()
        {
            lock (gate)
            {
                var r = rep; r.Ms = (Stopwatch.GetTimestamp() - startAt) / Q;
                r.Drivers.AddRange(drv.Values); r.Drivers.Sort((x, y) => y.Max.CompareTo(x.Max));
                Reset(); if (Environment.TickCount - scanAt > 5000) Threads();
                return r;
            }
        }

        protected override void On(RECORD* r)
        {
            if (r->H.Provider == Thr)
            {
                if (r->H.D.Opcode != 36 || r->Len < 12) return;
                byte* u = (byte*)r->User;
                Switch(r->Proc, *(uint*)u, *(uint*)(u + 4), r->H.Time, r->Len > 14 ? u[14] : (byte)0);
            }
            else if (r->H.Provider == Perf && r->H.D.Opcode >= 66 && r->H.D.Opcode <= 69 && r->Len >= 16)
                Dpc(*(ulong*)((byte*)r->User + 8), (r->H.Time - *(long*)r->User) / Q);
        }

        /// <summary>코어 cpu 에서 old 가 나가고 next 가 들어옴. state 1 = 준비됨(밀려남)</summary>
        public void Switch(int cpu, uint next, uint old, long time, byte state)
        {
            if (cpu >= 64) return;
            lock (gate)
            {
                int pOld, pNew; tids.TryGetValue(old, out pOld); tids.TryGetValue(next, out pNew);
                if (lastT[cpu] != 0)
                {
                    double ms = (time - lastT[cpu]) / Q;
                    if (ms > 0 && ms < 10000) { double[] a; if (!rep.Core.TryGetValue(pOld, out a)) rep.Core[pOld] = a = new double[64]; a[cpu] += ms; }
                }
                lastT[cpu] = time; lastTid[cpu] = next;
                if (GamePid == 0) return;
                if (pOld == GamePid && state == 1 && old != 0) { outAt[old] = time; outBy[old] = pNew; }
                if (pNew == GamePid)
                {
                    long at;
                    if (outAt.TryGetValue(next, out at))
                    {
                        double w = (time - at) / Q; int by = outBy[next];
                        outAt.Remove(next); outBy.Remove(next);
                        if (w > 0.05 && by != GamePid) { rep.Robbed += w; if (w > rep.RobbedMax) rep.RobbedMax = w; double v; rep.Thief.TryGetValue(by, out v); rep.Thief[by] = v + w; }
                    }
                }
            }
        }

        public void Dpc(ulong routine, double ms)
        {
            if (ms < 0 || ms > 5000) return;
            lock (gate)
            {
                string n = Module(routine);
                Drv d; if (!drv.TryGetValue(n, out d)) drv[n] = d = new Drv { Name = n };
                d.Ms += ms; d.N++; if (ms > d.Max) d.Max = ms;
            }
        }

        string Module(ulong a)
        {
            if (modBase == null || modBase.Length == 0) return "?";
            int i = Array.BinarySearch(modBase, a); if (i < 0) i = ~i - 1;
            return i >= 0 ? modName[i] : "?";
        }

        void Modules()
        {
            int n = 1 << 20, ret; byte* b = (byte*)Marshal.AllocHGlobal(n);
            try
            {
                if (Native.NtQuerySystemInformation(11, b, n, out ret) != 0) return;
                int c = *(int*)b; byte* p = b + 8;
                var k = new ulong[c]; var v = new string[c];
                for (int i = 0; i < c; i++, p += 296)
                {
                    k[i] = *(ulong*)(p + 16); byte* s = p + 40 + *(ushort*)(p + 38); int l = 0; while (l < 200 && s[l] != 0) l++;
                    var x = new byte[l]; Marshal.Copy((IntPtr)s, x, 0, l); v[i] = Encoding.ASCII.GetString(x);
                }
                Array.Sort(k, v); modBase = k; modName = v;
            }
            finally { Marshal.FreeHGlobal((IntPtr)b); }
        }

        void Threads()
        {
            int ret; if (tbuf == null) tbuf = (byte*)Marshal.AllocHGlobal(tlen); scanAt = Environment.TickCount;
            {
                uint st; while ((st = Native.NtQuerySystemInformation(5, tbuf, tlen, out ret)) == Native.STATUS_INFO_LENGTH_MISMATCH) { tlen = Math.Max(tlen, ret) + 96 * 1024; tbuf = (byte*)Marshal.ReAllocHGlobal((IntPtr)tbuf, (IntPtr)tlen); }
                if (st != 0) return; tids.Clear(); byte* p = tbuf;
                while (true)
                {
                    int next = *(int*)p, c = *(int*)(p + 4), pid = (int)*(long*)(p + 0x50);
                    byte* t = p + 0x100;
                    for (int i = 0; i < c; i++, t += 80) { uint id = (uint)*(long*)(t + 0x30); if (id != 0) tids[id] = pid; }
                    if (next == 0) break;
                    p += next;
                }
            }
        }
        byte* tbuf; int tlen = 640 * 1024, scanAt;
        public override void Close() { base.Close(); lock (gate) { if (tbuf != null) { Marshal.FreeHGlobal((IntPtr)tbuf); tbuf = null; } tids = new Dictionary<uint, int>(); modBase = null; modName = null; } }

        /// <summary>시험용</summary>
        public void Map(uint tid, int pid) { lock (gate) tids[tid] = pid; }
    }
}
