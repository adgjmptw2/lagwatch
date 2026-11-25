using System;
using System.Collections.Generic;
using System.Globalization;
using static LagWatch.Lang;

namespace LagWatch
{
    /// <summary>작은 집합. 이것 하나 때문에 System.Core 를 불러오지 않게 직접 둔다.</summary>
    internal sealed class HashSet<T> : IEnumerable<T>
    {
        readonly Dictionary<T, byte> d;
        public HashSet() { d = new Dictionary<T, byte>(); }
        public HashSet(IEqualityComparer<T> c) { d = new Dictionary<T, byte>(c); }
        public int Count { get { return d.Count; } }
        public bool Add(T v) { if (d.ContainsKey(v)) return false; d[v] = 0; return true; }
        public bool Contains(T v) { return d.ContainsKey(v); }
        public bool Remove(T v) { return d.Remove(v); }
        public void RemoveWhere(Predicate<T> f) { List<T> x = null; foreach (var v in d.Keys) if (f(v)) { if (x == null) x = new List<T>(); x.Add(v); } if (x != null) foreach (var v in x) d.Remove(v); }
        public void Clear() { d.Clear(); }
        public IEnumerator<T> GetEnumerator() { return d.Keys.GetEnumerator(); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return d.Keys.GetEnumerator(); }
    }

    /// <summary>고정 크기 기록. 필요한 만큼만 늘리다가 한도에서 돌려 쓴다.</summary>
    internal sealed class FloatRing
    {
        float[] a;
        readonly int cap;
        int head, count;

        public FloatRing(int capacity) { cap = capacity; a = new float[Math.Min(16, capacity)]; }
        public int Count { get { return count; } }

        public void Add(float v)
        {
            if (count == a.Length && a.Length < cap)
            {
                var b = new float[Math.Min(cap, a.Length * 2)];
                for (int i = 0; i < count; i++) b[i] = Front(i);
                a = b; head = count;
            }
            if (head == a.Length) head = 0;
            a[head++] = v;
            if (count < a.Length) count++;
        }

        /// <summary>0 = 가장 최근</summary>
        public float Back(int i)
        {
            int k = head - 1 - i;
            if (k < 0) k += a.Length;
            return a[k];
        }

        /// <summary>0 = 가장 오래됨</summary>
        public float Front(int i) { return Back(count - 1 - i); }
    }

    internal sealed class ThreadTrack
    {
        public long Time;
        public double Load;
        public int Stamp;
    }

    internal sealed class Proc
    {
        public int Pid, ParentPid, Session;
        public long Create;
        public string Name;
        public App App;
        public bool IsGame, Pseudo;

        public long Cycles;
        public uint HardFaults;
        public long Commit, Ws;
        public int Handles, ThreadCount;

        public double Cores;        // 지금 쓰는 코어 수
        public double Hot;          // 가장 바쁜 스레드가 코어 하나를 쓴 비율
        public double FaultsPerSec; // 디스크에서 다시 읽은 횟수
        public double HotSince = -1;

        public double FirstSeen;
        public int Stamp;

        public Dictionary<int, ThreadTrack> Threads;
        public double ThreadsAt = -1;

        public FloatRing Lows;      // 5분마다 최저 커밋(MB)
        public FloatRing HandleLows;
        public long BucketMin = long.MaxValue;
        public int HandleMin = int.MaxValue;
        public float AlertedAt;     // 마지막으로 알린 커밋(MB)
        public int HandleAlertedAt;
        public bool Leaking;
        public float LeakGrow;
        public int LeakMinutes;
    }

    internal sealed class App
    {
        public string Key, Label, Exe, Path;
        public bool Script, Service, Pseudo, IsGame, Protected;
        public bool InfoDone, IconDone, Ignored;

        public int Count;
        public double Cores, Hot;
        public long Ws, Commit;
        public bool Leaking;
        public float LeakGrow;
        public int LeakMinutes;

        public readonly AppHist H = new AppHist();            // 1분 기록
        public double Acc, AccT, FiveCores;                   // 이번 1분·5분 동안 쓴 코어
        public int FiveN;
        public FloatRing Counts;                              // 5분마다 프로세스 수
        public int CountAlertedAt;
        public double LastSeen;
    }

    internal static class Leak
    {
        /// <summary>
        /// 5분 최저값이 창 전체에서 꾸준히 올라갔는지 본다.
        /// 한 번에 튄 것, 앞에서만 오르고 멈춘 것, 오르내리는 것은 누수로 보지 않는다.
        /// </summary>
        public static bool Check(FloatRing r, int n, float minGrow, float minRatio, out float grow)
        {
            grow = 0;
            if (r == null || r.Count < n || n < 6) return false;
            float first = (r.Back(n - 1) + r.Back(n - 2)) / 2;
            float last = (r.Back(0) + r.Back(1)) / 2;
            grow = last - first;
            if (grow < Math.Max(minGrow, first * minRatio)) return false;

            float runMax = r.Back(n - 1), maxStep = 0, prev = r.Back(n - 1);
            int ups = 0;
            for (int i = n - 2; i >= 0; i--)
            {
                float v = r.Back(i);
                if (v >= runMax - grow * 0.03f) ups++;
                if (v > runMax) runMax = v;
                if (v - prev > maxStep) maxStep = v - prev;
                prev = v;
            }
            if (ups < 0.85 * (n - 1)) return false;
            if (maxStep > 0.6f * grow) return false;
            float mid = (r.Back(n / 2) + r.Back(n / 2 - 1)) / 2;
            if (last - mid < 0.25f * grow) return false;
            grow = Math.Max(grow, r.Back(0) - r.Back(n - 1)); // 보여 줄 때는 양 끝 차이
            return true;
        }

        // 5분 칸 수, 최소 증가(MB), 최소 비율
        static readonly int[] Win = { 144, 36, 12 };
        static readonly float[] Min = { 500, 300, 200 };
        static readonly float[] Ratio = { 0.30f, 0.25f, 0.25f };

        /// <summary>12시간 → 3시간 → 1시간 순으로 보고 가장 긴 것을 돌려준다.</summary>
        public static bool Find(FloatRing r, out float grow, out int minutes)
        {
            for (int k = 0; k < Win.Length; k++)
                if (Check(r, Win[k], Min[k], Ratio[k], out grow)) { minutes = Win[k] * 5; return true; }
            grow = 0; minutes = 0;
            return false;
        }
    }

    /// <summary>조건이 정해진 시간 동안 이어졌는지</summary>
    internal sealed class Hold
    {
        double since = -1;
        public bool On(bool cond, double now, double need)
        {
            if (!cond) { since = -1; return false; }
            if (since < 0) since = now;
            return now - since >= need;
        }
        public void Reset() { since = -1; }
    }

    internal static class Fmt
    {
        static readonly CultureInfo C = CultureInfo.InvariantCulture;

        public static string Mem(double mb)
        {
            double v = Math.Abs(mb);
            if (v >= 1024 * 100) return (mb / 1024).ToString("0", C) + "GB";
            if (v >= 1000) return (mb / 1024).ToString("0.0", C) + "GB";
            return mb.ToString("0", C) + "MB";
        }
        public static string Delta(double mb)
        {
            if (Math.Abs(mb) < 0.5) return "0";
            return (mb > 0 ? "+" : "") + Mem(mb);
        }
        public static string Gb(double bytes) { return (bytes / (1L << 30)).ToString("0.0", C); }
        public static string Cores(double c) { return c.ToString(c >= 9.95 ? "0" : "0.0", C) + Tx("코어", " cores"); }
        public static string Pct(double f) { return (f * 100).ToString("0", C) + "%"; }
        public static string Ghz(double g) { return g.ToString("0.00", C) + "GHz"; }
        public static string Dur(double sec)
        {
            if (sec < 60) return Math.Max(1, (int)Math.Round(sec)) + Tx("초", "s");
            int m = (int)Math.Round(sec / 60);
            if (m < 60) return m + Tx("분", "m");
            return (m % 60 == 0) ? (m / 60) + Tx("시간", "h") : (m / 60) + Tx("시간 ", "h ") + (m % 60) + Tx("분", "m");
        }
    }
}
