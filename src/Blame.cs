using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using static LagWatch.Lang;

namespace LagWatch
{
    /// <summary>
    /// 게임의 바쁜 두 스레드가 언제 돌고, 밀려나고, 메모리를 디스크에서 기다렸는지를 윈도우의 스레드 교체 기록에서 읽는다.
    /// 게임 프로세스는 열지 않는다. 게임 중에만 켠다.
    /// </summary>
    internal sealed unsafe class Blame : Trace
    {
        public const string Session = "LagWatchBlame";
        static readonly double Q = Stopwatch.Frequency / 1000.0;
        public const byte Run = 1, Ready = 2, Page = 3, Wait = 4;

        struct Seg { public long A, B; public byte Kind; public uint By; }
        sealed class Line
        {
            public uint Tid; public Seg[] S; public int Pos, Count; public long At; public byte Kind; public uint By;
            public void Clear(uint tid) { if (tid != 0 && S == null) S = new Seg[16384]; Tid = tid; Pos = Count = 0; At = 0; Kind = 0; By = 0; }
            public void Turn(long t, byte next, uint by)
            {
                if (S != null && Kind != 0 && t > At) { S[Pos] = new Seg { A = At, B = t, Kind = Kind, By = By }; Pos = (Pos + 1) % S.Length; if (Count < S.Length) Count++; }
                At = t; Kind = next; By = by;
            }
        }
        internal sealed class Part
        {
            public double Run, Ready, Page, Wait; public readonly Dictionary<uint, double> By = new Dictionary<uint, double>();
            public double Known { get { return Run + Ready + Page + Wait; } }
        }

        readonly object gate = new object();
        Line a = new Line(), b = new Line();
        public long Events;

        public Blame() : base(Session) { }
        public bool Start() { lock (gate) { a.Clear(0); b.Clear(0); Events = 0; } return Open(0x10 | 0x800, 64, 32, 128, new Guid("91b0b6de-1d3f-4d77-9c0d-7f7f0a3a1e55")); }

        /// <summary>게임이 끝나면 기록 자리도 놓는다.</summary>
        public override void Close() { base.Close(); lock (gate) { a.Clear(0); b.Clear(0); a.S = b.S = null; } }

        /// <summary>지켜볼 스레드 둘. 이미 보던 스레드는 기록을 그대로 두고 자리만 맞춘다.</summary>
        public void Watch(int main, int second)
        {
            lock (gate)
            {
                uint m = (uint)main, s = (uint)second; if (m == 0) return;
                Line x = a.Tid == m ? a : b.Tid == m ? b : null, y = s == 0 ? null : a.Tid == s ? a : b.Tid == s ? b : null;
                if (x == null) { x = y == a ? b : a; x.Clear(m); }
                if (y == null) { y = x == a ? b : a; if (s != 0) y.Clear(s); }
                a = x; b = y;
            }
        }

        protected override void On(RECORD* r)
        {
            if (*(uint*)&r->H.Provider != 0x3D6FA8D1) return;
            byte* u = (byte*)r->User; int op = r->H.D.Opcode;
            if (op == 36 && r->Len >= 16) Switch(*(uint*)u, *(uint*)(u + 4), r->H.Time, u[14], u[12]);
            else if (op == 50 && r->Len >= 4) Wake(*(uint*)u, r->H.Time);
        }

        /// <summary>기다리던 스레드가 깨어남. 이때부터 다시 돌 때까지는 CPU 차례를 기다린 것</summary>
        public void Wake(uint tid, long time)
        {
            if (tid != a.Tid && tid != b.Tid) return;
            lock (gate) { if (a.Tid == tid && (a.Kind == Wait || a.Kind == Page)) a.Turn(time, Ready, 0); else if (b.Tid == tid && (b.Kind == Wait || b.Kind == Page)) b.Turn(time, Ready, 0); }
        }

        /// <summary>next 가 들어오고 old 가 나감. state 1 = 준비됨(밀려남), 5 = 기다림. reason 1·2·8·9·19 = 메모리를 기다림</summary>
        public void Switch(uint next, uint old, long time, byte state, byte reason)
        {
            Events++;
            if (next != a.Tid && old != a.Tid && next != b.Tid && old != b.Tid) return;
            lock (gate) { One(a, next, old, time, state, reason); One(b, next, old, time, state, reason); }
        }
        static void One(Line l, uint next, uint old, long t, byte state, byte reason)
        {
            if (l.Tid == 0) return;
            if (l.Tid == old) l.Turn(t, state == 1 || state == 3 || state == 7 ? Ready : state == 6 || state == 5 && MemWait(reason) ? Page : Wait, next);
            else if (l.Tid == next) { if (l.Kind == Ready && l.By == 0) l.By = old; l.Turn(t, Run, 0); } // 깨어난 뒤 기다린 시간은 그 코어를 쓰던 스레드 몫
        }
        /// <summary>KWAIT_REASON 1 FreePage, 2 PageIn, 8 WrFreePage, 9 WrPageIn, 19 WrPageOut</summary>
        public static bool MemWait(uint reason) { return reason == 1 || reason == 2 || reason == 8 || reason == 9 || reason == 19; }

        /// <summary>그 사이에 두 스레드가 무엇을 했는지(ms)</summary>
        public void Explain(long t0, long t1, out Part main, out Part second) { lock (gate) { main = Sum(a, t0, t1); second = Sum(b, t0, t1); } }
        static Part Sum(Line l, long t0, long t1)
        {
            var p = new Part(); if (l.Tid == 0 || l.S == null) return p;
            for (int i = 0; i < l.Count; i++) { var s = l.S[(l.Pos - 1 - i + l.S.Length * 2) % l.S.Length]; if (s.B <= t0) break; Add(p, s, t0, t1); }
            if (l.Kind != 0) Add(p, new Seg { A = l.At, B = t1, Kind = l.Kind, By = l.By }, t0, t1);
            return p;
        }
        static void Add(Part p, Seg s, long t0, long t1)
        {
            double ms = (Math.Min(s.B, t1) - Math.Max(s.A, t0)) / Q; if (ms <= 0) return;
            if (s.Kind == Run) p.Run += ms; else if (s.Kind == Ready) { p.Ready += ms; double v; p.By.TryGetValue(s.By, out v); p.By[s.By] = v + ms; } else if (s.Kind == Page) p.Page += ms; else p.Wait += ms;
        }

        /// <summary>스레드 번호 → 프로세스 번호. 끊겼을 때만 부른다.</summary>
        public static Dictionary<uint, int> Owners(ICollection<uint> want)
        {
            var d = new Dictionary<uint, int>(); if (want.Count == 0) return d;
            int n = 704 * 1024, ret; byte* m = (byte*)Marshal.AllocHGlobal(n);
            try
            {
                uint st; while ((st = Native.NtQuerySystemInformation(5, m, n, out ret)) == Native.STATUS_INFO_LENGTH_MISMATCH) { n = Math.Max(n, ret) + 96 * 1024; m = (byte*)Marshal.ReAllocHGlobal((IntPtr)m, (IntPtr)n); }
                if (st != 0) return d; byte* p = m;
                while (true)
                {
                    int next = *(int*)p, c = *(int*)(p + 4), pid = (int)*(long*)(p + 0x50); byte* t = p + 0x100;
                    for (int i = 0; i < c; i++, t += 80) { uint id = (uint)*(long*)(t + 0x30); if (want.Contains(id)) d[id] = pid; }
                    if (next == 0) break; p += next;
                }
            }
            finally { Marshal.FreeHGlobal((IntPtr)m); }
            return d;
        }
    }

    /// <summary>끊김 한 번과 그 까닭</summary>
    internal sealed class Hit
    {
        public const int Ram = 1, Disk = 2, Cpu = 3, Drv = 4, Game = 5, Etc = 6;
        public static readonly string[] Name = { "", Tx("램 부족", "Low RAM"), Tx("디스크", "Disk"), "CPU", Tx("드라이버", "Driver"), Tx("게임 자체", "Game itself"), Tx("그 외", "Other") };
        public DateTime At; public string Exe = "", Who = "", Note = ""; public int Kind; public double Ms, Run, Ready, Page, Wait, RamLeft, CpuPct; public bool Measured;
        static readonly CultureInfo C = CultureInfo.InvariantCulture;
        public string Line() { return string.Join("\t", At.ToString("yyyy-MM-ddTHH:mm:ss.f"), Exe, Kind.ToString(C), Ms.ToString("0.0", C), One(Who), One(Note), Run.ToString("0.0", C), Ready.ToString("0.0", C), Page.ToString("0.0", C), Wait.ToString("0.0", C), RamLeft.ToString("0.00", C), CpuPct.ToString("0", C), Measured ? "1" : "0"); }
        static string One(string s) { return (s ?? "").Replace('\t', ' ').Replace('\n', ' '); }
        public static Hit Parse(string s)
        {
            var f = s.Split('\t'); if (f.Length < 13) return null; DateTime t; int k; var v = new double[7];
            if (!DateTime.TryParseExact(f[0], "yyyy-MM-ddTHH:mm:ss.f", C, DateTimeStyles.None, out t) || !int.TryParse(f[2], out k) || k < 1 || k > 6 || !double.TryParse(f[3], NumberStyles.Float, C, out v[0])) return null;
            for (int i = 0; i < 6; i++) if (!double.TryParse(f[6 + i], NumberStyles.Float, C, out v[1 + i])) return null;
            return new Hit { At = t, Exe = f[1], Kind = k, Ms = v[0], Who = f[4], Note = f[5], Run = v[1], Ready = v[2], Page = v[3], Wait = v[4], RamLeft = v[5], CpuPct = v[6], Measured = f[12] == "1" };
        }

        /// <summary>기록에는 짧은 표시만 남기고 보여 줄 때 그 언어로 바꾼다. 1.5까지의 기록은 한국어로 적혀 있다.</summary>
        public static string Say(string note)
        {
            switch (note)
            {
                case "gc": case "자원 정리": return Tx("자원 정리", "asset cleanup");
                case "main": case "계산": return Tx("주 스레드", "main thread");
                case "sub": case "그리기 준비": return Tx("보조 스레드", "second thread");
                case "wait": case "그래픽이나 파일을 기다림": return Tx("그래픽 등을 기다림", "waiting on GPU or other work");
                default: return note;
            }
        }

        /// <summary>잰 것으로 까닭을 정한다. 재지 못했으면 그때 있었던 일로 짐작한다.</summary>
        public void Judge(Blame.Part m, Blame.Part s, bool ramLow, bool paging, bool cpuFull, bool driver, double unloadMs)
        {
            double L = Math.Max(1, Ms); Measured = m.Known >= L * .5;
            if (unloadMs >= L * .4) { Kind = Game; Note = "gc"; return; }
            if (Measured)
            {
                var w = m; // 주 스레드가 그저 기다리기만 했으면 두 번째 스레드를 본다
                if (m.Wait > L * .5 && s.Known >= L * .5 && s.Wait < m.Wait) w = s;
                Run = w.Run; Ready = w.Ready; Page = w.Page; Wait = w.Wait;
                if (w.Page >= Math.Max(8, L * .2) && w.Page >= w.Ready) { Kind = ramLow || RamLeft < 2.6 ? Ram : Disk; return; }
                if (w.Ready >= Math.Max(8, L * .2)) { Kind = Cpu; return; }
                if (w.Run >= L * .55) { Kind = Game; Note = w == s ? "sub" : "main"; return; }
                if (w.Page >= 6) { Kind = ramLow ? Ram : Disk; return; }
                if (driver) { Kind = Drv; return; }
                Kind = Etc; Note = "wait"; return;
            }
            Kind = ramLow ? Ram : paging ? Disk : cpuFull ? Cpu : driver ? Drv : Etc;
        }
    }

    /// <summary>끊김 기록. 최근 것만 들고 있고 파일에도 남긴다.</summary>
    internal sealed class Hits
    {
        readonly string file; public readonly List<Hit> Items = new List<Hit>();
        public Hits(string dir)
        {
            file = dir == null ? null : Path.Combine(dir, "hits.tsv");
            try
            {
                if (file == null || !File.Exists(file)) return; var all = File.ReadAllLines(file, Encoding.UTF8); var old = DateTime.Now.AddDays(-14);
                foreach (var l in all) { var h = Hit.Parse(l); if (h != null && h.At >= old) Items.Add(h); }
                if (Items.Count > 3000) Items.RemoveRange(0, Items.Count - 3000);
                if (all.Length > Items.Count + 500) { var sb = new StringBuilder(); foreach (var h in Items) sb.Append(h.Line()).Append("\r\n"); File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false)); }
            }
            catch (Exception) { }
        }
        public void Add(Hit h) { lock (Items) { Items.Add(h); if (Items.Count > 3000) Items.RemoveAt(0); } try { if (file != null) File.AppendAllText(file, h.Line() + "\r\n", new UTF8Encoding(false)); } catch (Exception) { } }
        public List<Hit> Between(string exe, DateTime a, DateTime b) { var l = new List<Hit>(); lock (Items) for (int i = Items.Count - 1; i >= 0; i--) { var h = Items[i]; if (h.At < a) break; if (h.At <= b && h.Exe == exe) l.Insert(0, h); } return l; }
    }
}
